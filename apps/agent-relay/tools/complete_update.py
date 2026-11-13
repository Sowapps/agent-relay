"""Complete portable update adapter. C# owns file inventory, intent, activation and recovery policy.

@see InstallationUpdate.cs for its single state owner and deployment.py for owned host/snapshot adapters.
"""

import argparse
import json
import os
from pathlib import Path
import signal
import subprocess
import time
import uuid
import traceback

from deployment import confirm_quiescence, demand_idle, exclusive, launch, read, restore_snapshot, snapshot, write
from host_identity import identity, resolve_host
from relay_mcp import RelayMcp
from release_artifact import MANIFEST_FILE, VERIFICATION_FILE, extract_archive, ordinary_directory, ordinary_file, qualified_archive, sha256, validate_version, validate_windows_name, verified_archive
from runtime_layout import app_var, runtime_command


EXE = 'AgentRelay.Windows.exe'


class UpdateFailure(RuntimeError):
    """A surfaced, identifiable error; retained details are separate from the user message."""

    def __init__(self, error):
        self.error = error
        super().__init__(error['message'] + ' [' + error['code'] + '] ' + error['diagnosticId']
                         + (' Details: ' + error['detailsPath'] if error.get('detailsPath') else '')
                         + (' Diagnostic persistence: ' + error['diagnosticStorageError'] if error.get('diagnosticStorageError') else ''))


def invoke(updater, installation, data, action, candidate=None, failure=None):
    request = dict(action=action, installation=str(installation), data=str(data), candidate=str(candidate) if candidate else None)
    if failure is not None:
        request['failure'] = failure
    result = subprocess.run(runtime_command(updater) + ['installation-update', '--data-dir', str(data)],
                            input=json.dumps(request), capture_output=True, text=True, encoding='utf-8',
                            timeout=180, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        try:
            error = json.loads(result.stderr)
        except ValueError:
            error = None
        if isinstance(error, dict) and error.get('diagnosticId') and error.get('code'):
            raise UpdateFailure(error)
        raise RuntimeError(result.stderr.strip() or 'The C# update command did not complete')
    return json.loads(result.stdout)


def recovery_updater(data):
    """Validate the independently retained runtime before executing recovery, without an archive."""
    installation = data.parents[3]
    reports = app_var(installation) / 'update'
    descriptor = read(ordinary_file(reports / 'recovery.json'))
    identifier = descriptor.get('transactionId', '')
    if (descriptor.get('schema') != 1 or len(identifier) != 12 or any(c not in '0123456789abcdef' for c in identifier)
            or Path(descriptor['installation']) != installation or Path(descriptor['data']) != data):
        raise ValueError('The retained recovery runtime belongs to another installation or transaction')
    runner = ordinary_directory(reports / identifier / 'runner')
    if Path(descriptor['executable']) != runner / EXE:
        raise ValueError('The retained recovery executable escaped its owned directory')
    actual_names = {path.relative_to(runner).as_posix() for path in runner.rglob('*') if path.is_file()}
    if actual_names != set(descriptor['files']):
        raise ValueError('The retained recovery runtime contains unmanifested files')
    for name, expected in descriptor['files'].items():
        validate_windows_name(name)
        if sha256(ordinary_file(runner / name)) != expected:
            raise ValueError('The retained recovery runtime is missing or changed: ' + name)
    if EXE not in descriptor['files']:
        raise ValueError('The retained recovery manifest omits its executable')
    return {'executable': str(runner / EXE)}


def report_error(data, error, updater=None):
    if isinstance(error, UpdateFailure):
        return error.error
    related = list(getattr(error, 'related_diagnostics', []))
    cause = error.__cause__ or error.__context__
    seen = set()
    while cause is not None and id(cause) not in seen:
        seen.add(id(cause))
        if isinstance(cause, UpdateFailure):
            related.append(cause.error['diagnosticId'])
        cause = cause.__cause__ or cause.__context__
    failure = dict(type=type(error).__name__, message=str(error) or type(error).__name__,
                   detail=''.join(traceback.format_exception(error))[-24000:],
                   code='update_cancelled' if isinstance(error, KeyboardInterrupt) else 'update_adapter_failed', relatedDiagnostics=related)
    try:
        updater = updater or recovery_updater(data)
        return invoke(updater, data.parents[3], data, 'report-error', failure=failure)
    except (Exception, KeyboardInterrupt) as diagnostic_error:
        # This is a last-resort transport/storage adapter, not a second failure-policy owner.
        identifier = 'update-' + uuid.uuid4().hex[:16]
        result = dict(code=failure['code'], message=failure['message'][:1600], diagnosticId=identifier,
                      recovery='Read the retained diagnostic and restore the verified recovery tool. No error was suppressed.',
                      detailsPath=None, diagnosticStorageError=str(diagnostic_error), inlineDetail=failure['detail'])
        try:
            ordinary_directory(data)
            retained = data == app_var(data.parents[3]) / 'default' and (app_var(data.parents[3]) / 'update/recovery.json').is_file()
            if not (data / 'mcp-runtime.json').is_file() and not (data / 'relay.sqlite').is_file() and not retained:
                raise ValueError('No initialized data scope for diagnostic persistence')
            path = ordinary_directory(data / 'logs/update') / (identifier + '.json')
            path.parent.mkdir(parents=True, exist_ok=True)
            result['detailsPath'] = str(path)
            write(path, dict(error=result, type=failure['type'], detail=failure['detail'], relatedDiagnostics=related))
        except Exception as storage_error:
            result.update(detailsPath=None, diagnosticStorageError=str(diagnostic_error) + '; ' + str(storage_error))
        return result


def update(data, archive=None, digest=None, **options):
    """Public boundary: every failure reaches the caller with an ID and retained/fallback details."""
    context = dict(updater=options.get('updater'))
    try:
        return _update(data, archive, digest, context=context, **options)
    except (Exception, KeyboardInterrupt) as error:
        raise UpdateFailure(report_error(data.absolute(), error, context['updater'])) from error


def startup(executable, data):
    result = subprocess.run([str(executable), 'startup-status', '--data-dir', str(data)],
                            capture_output=True, timeout=15, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise RuntimeError('The retained startup preference cannot be verified')
    state = json.loads(result.stdout)
    if type(state.get('enabled')) is not bool or state.get('error'):
        raise RuntimeError('The retained startup registration requires repair')
    return state['enabled']


def candidate_from_archive(archive, digest):
    # Acquisition origin is irrelevant: local packaging and downloaded releases use these same bytes.
    destination = ordinary_directory(archive.parent / ('c-' + digest[:12]))
    if not destination.exists():
        pending = destination.with_name('p-' + uuid.uuid4().hex[:12])
        with verified_archive(archive, digest) as package:
            extract_archive(package, pending)
        pending.replace(destination)
    # Validate before executing the candidate's C# engine, including on resume. This is the
    # acquisition trust boundary; the engine independently validates its installed-file transaction.
    with verified_archive(archive, digest) as package:
        manifest_bytes = package.read(MANIFEST_FILE)
    if ordinary_file(destination / MANIFEST_FILE).read_bytes() != manifest_bytes:
        raise RuntimeError('The extracted candidate manifest no longer matches the selected archive')
    manifest = json.loads(manifest_bytes)
    actual_names = {path.relative_to(destination).as_posix() for path in destination.rglob('*') if path.is_file()}
    if actual_names != set(manifest['files']) | {MANIFEST_FILE}:
        raise RuntimeError('The extracted candidate contains unmanifested files')
    for name, expected in manifest['files'].items():
        validate_windows_name(name)
        if sha256(ordinary_file(destination / name)) != expected:
            raise RuntimeError('The extracted candidate changed before execution: ' + name)
    return destination


def stop_runtime(runtime, data):
    owner = resolve_host(runtime, data)
    if owner is None:
        with exclusive(data):
            return
    confirm_quiescence(runtime, data)
    if not identity(owner, data):
        raise RuntimeError('Host ownership changed before update; no process was stopped')
    os.kill(owner['pid'], signal.SIGTERM)
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        try:
            with exclusive(data):
                return
        except RuntimeError:
            time.sleep(.1)
    raise RuntimeError('The owned host did not release the selected data')


def saved_snapshot(data, destination):
    if not destination.exists():
        pending = destination.with_name(destination.name + '.pending-' + uuid.uuid4().hex)
        snapshot(data, pending)
        hashes = {file.relative_to(pending).as_posix(): sha256(file) for file in pending.rglob('*') if file.is_file()}
        write(pending / 'snapshot.json', hashes)
        pending.replace(destination)
    expected = read(destination / 'snapshot.json')
    if any(sha256(ordinary_file(destination / name)) != digest for name, digest in expected.items()):
        raise RuntimeError('The retained coherent rollback snapshot was changed; recovery requires review')


def fresh_probe(runtime, data, reports, version):
    pointer = read(data / 'mcp-runtime.json')
    broker = dict(executable=pointer['mcpExecutable'], connectionId=pointer['connectionId'])
    with (reports / 'mcp.stderr.log').open('a', encoding='utf-8') as errors:
        with RelayMcp(broker, data, reports, errors) as client:
            status = client.tool('get_connection_status')
            if status['version'] != version or status['brokerVersion'] != version or status['clientRefreshRequired']:
                raise RuntimeError('The complete broker/worker contract was not confirmed')
            projects = client.tool('list_projects')
    return dict(status=status, projects=projects)


def _update(data, archive, digest, *, updater=None, verify=None, guard=None, refresh=None, action='apply', context):
    """Resume the same transaction. Never install, recreate data or replay an agent request."""
    data = ordinary_directory(data.absolute())
    installation = data.parents[3]
    if data != app_var(installation) / 'default' or (not (data / 'mcp-runtime.json').is_file()
                                                   and not (app_var(installation) / 'update/recovery.json').is_file()):
        raise ValueError('Select one existing portable installation and its retained default data')
    candidate = None
    # Recovery uses retained checked bytes. It must work after release downloads are removed.
    retained = (app_var(installation) / 'update/recovery.json').exists()
    if retained:
        updater = updater or recovery_updater(data)
    elif archive is not None and digest is not None:
        candidate = candidate_from_archive(archive, digest)
        updater = updater or {'executable': str(candidate / 'app' / EXE)}
    else:
        raise RuntimeError('No retained recovery runtime. Select a verified candidate for the first update')
    context['updater'] = updater
    reports = app_var(installation) / 'update'
    reports.mkdir(exist_ok=True)
    started = time.monotonic()
    with exclusive(data, 'update-driver.lock'):
        existing = None
        if (reports / 'current.json').exists():
            existing = invoke(updater, installation, data, 'status')
        barrier = data / 'promotion.lock'
        if existing and existing.get('schema') != 2 and existing['phase'] == 'rolled_back' and action != 'status':
            prior_receipt = reports / existing['id'] / 'ports.json'
            if barrier.exists() or not prior_receipt.exists() or not read(prior_receipt).get('rollbackVerified'):
                return finalize_rollback(updater, installation, data, existing, prior_receipt, guard)
        recovering = existing and existing['phase'] in ('rolling_back', 'files_restored', 'recovery_required')
        if action in ('status', 'rollback') or recovering or archive is None:
            state = existing
        else:
            candidate = candidate or candidate_from_archive(archive, digest)
            if not (candidate / 'transport/AgentRelay.Transport.exe').is_file():
                raise ValueError('Forward updates require the stable transport. Legacy transactions retain status/rollback recovery only; client-exit deployment is retired')
            updater = {'executable': str(candidate / 'app' / EXE)}
            context['updater'] = updater
            state = invoke(updater, installation, data, 'prepare', candidate)
            updater = recovery_updater(data)
            context['updater'] = updater
        if state is None:
            raise RuntimeError('No existing update transaction to inspect or roll back')
        transaction = reports / state['id']
        receipt = transaction / 'ports.json'
        if action == 'status':
            return dict(state=state, receipt=str(receipt))
        if state['phase'] == 'rolled_back' and receipt.exists() and read(receipt).get('rollbackVerified'):
            return dict(state=state, receipt=str(receipt), rolledBack=True, alreadyCurrent=True)
        if state.get('schema') == 2:
            # C# owns drain, snapshot, immutable selection, transport readiness and rollback.
            # Legacy typed-tool probes/reload callbacks cannot validate the fixed generic surface.
            if guard:
                guard()
            before = state['phase']
            state = invoke(updater, installation, data, 'rollback' if action == 'rollback' else 'resume')
            ports = dict(startupEnabled=state['activation']['startupEnabled'],
                         admissionReleased=state['activation']['admissionReleased'],
                         rollbackVerified=state['phase'] == 'rolled_back',
                         elapsedSeconds=round(time.monotonic() - started, 3),
                         transactionOwner='InstallationUpdate', phase=state['phase'])
            write(receipt, ports)
            return dict(state=state, receipt=str(receipt), verification=dict(transactionPhase=state['phase']),
                        alreadyCurrent=before == 'complete', rolledBack=state['phase'] == 'rolled_back')
        if action != 'rollback' and not recovering and state['phase'] in ('preparing', 'prepared', 'applying', 'files_applied'):
            raise ValueError('Forward legacy publication is retired. Roll back this retained transaction, then qualify the initial stable-transport migration')
        runtime = {'executable': str(installation / 'app' / EXE)}
        if state['phase'] in ('complete', 'awaiting_clients'):
            if action == 'rollback':
                invoke(updater, installation, data, 'rollback')  # C# refuses restoration after admission reopens.
            if guard:
                guard()
            if resolve_host(runtime, data) is None:
                with exclusive(data):
                    demand_idle(updater, data)
                restarted = launch(runtime, data, transaction)
                ports = read(receipt)
                ports['hostPid'] = restarted.pid
                write(receipt, ports)
            checked = verify() if verify else fresh_probe(runtime, data, transaction, state['version'])
            state = invoke(updater, installation, data, 'verify')
            if state['phase'] == 'awaiting_clients' and refresh:
                refreshed = refresh(transaction)
                ports = read(receipt)
                ports['clientRefresh'] = refreshed
                write(receipt, ports)
                if refreshed.get('sameClientVerified'):
                    state = invoke(updater, installation, data, 'verify')
            if barrier.exists() and barrier.read_text() == state['id']:
                barrier.unlink()  # Recover a crash after verification but before release.
            return dict(state=state, receipt=str(receipt), verification=checked, alreadyCurrent=True)
        if state['phase'] == 'preparing':
            raise RuntimeError('Candidate preparation is incomplete; repeat this command with the same archive')
        if not receipt.exists():
            write(receipt, dict(previous=read(data / 'mcp-runtime.json'), startupEnabled=startup(Path(read(data / 'mcp-runtime.json')['executable']), data),
                                wasRunning=resolve_host(read(data / 'mcp-runtime.json'), data) is not None, admissionReleased=False))
        ports = read(receipt)
        if guard:
            guard()
        if barrier.exists():
            if barrier.read_text(encoding='ascii') != state['id']:
                raise RuntimeError('Another maintenance operation owns admission; it was not changed')
        else:
            with barrier.open('x', encoding='ascii') as marker:
                marker.write(state['id'])
        backup = transaction / 'data-before'
        if state['phase'] in ('applying', 'files_applied', 'rolling_back', 'files_restored', 'recovery_required') and not backup.exists():
            raise RuntimeError('The original coherent snapshot is missing. Admission remains suspended; no replacement snapshot or rollback was attempted')
        selected = read(data / 'mcp-runtime.json') if (data / 'mcp-runtime.json').exists() else ports['previous']
        activated = dict(ports['previous'], executable=runtime['executable'], mcpExecutable=str(data / 'mcp-launcher' / EXE))
        if selected != ports['previous'] and (state['phase'] not in ('files_applied', 'files_restored', 'rolling_back', 'recovery_required') or selected != activated):
            raise RuntimeError('The selected runtime changed outside this transaction; no process was stopped and no snapshot was restored')
        process = None
        try:
            stop_runtime(selected, data)
            demand_idle(updater, data)
            with exclusive(data):
                saved_snapshot(data, backup)
            if action == 'rollback' or state['phase'] in ('rolling_back', 'files_restored', 'recovery_required', 'rolled_back'):
                raise RuntimeError('Resume rollback of the retained transaction')
            invoke(updater, installation, data, 'apply')
            invoke(updater, installation, data, 'activate')
            process = launch(runtime, data, transaction)
            if guard:
                guard()
            if startup(Path(runtime['executable']), data) != ports['startupEnabled']:
                raise RuntimeError('The stable startup preference changed during activation')
            checked = verify() if verify else fresh_probe(runtime, data, transaction, state['version'])
            state = invoke(updater, installation, data, 'verify')
            if state['phase'] == 'awaiting_clients' and refresh:
                ports['clientRefresh'] = refresh(transaction)
                if ports['clientRefresh'].get('sameClientVerified'):
                    state = invoke(updater, installation, data, 'verify')
            ports.update(admissionReleased=True, hostPid=process.pid, verification=checked,
                         elapsedSeconds=round(time.monotonic() - started, 3))
            write(receipt, ports)
            barrier.unlink()
            return dict(state=state, receipt=str(receipt), verification=checked, alreadyCurrent=False, hostPid=process.pid)
        except BaseException as error:
            current = invoke(updater, installation, data, 'status')
            if current['phase'] in ('complete', 'awaiting_clients') or ports['admissionReleased']:
                # The candidate is verified or has accepted new work. Never restore the old database.
                raise
            captured = error if isinstance(error, UpdateFailure) else UpdateFailure(report_error(data, error, updater))
            ports.update(error=str(captured), failure=captured.error)
            write(receipt, ports)  # Retain the primary diagnostic before recovery can itself be interrupted.
            try:
                selected = read(data / 'mcp-runtime.json') if (data / 'mcp-runtime.json').exists() else ports['previous']
                stop_runtime(selected, data)
                invoke(updater, installation, data, 'rollback')
                if backup.exists():
                    saved_snapshot(data, backup)
                    with exclusive(data):
                        restore_snapshot(data, backup, transaction / ('failed-state-' + uuid.uuid4().hex))
                if ports['wasRunning']:
                    restored = launch(ports['previous'], data, transaction)
                    ports['restoredHostPid'] = restored.pid
                if startup(Path(ports['previous']['executable']), data) != ports['startupEnabled']:
                    raise RuntimeError('The restored startup preference did not match')
                invoke(updater, installation, data, 'rollback-finish')
                ports.update(rollbackVerified=True, error=str(captured), failure=captured.error,
                             elapsedSeconds=round(time.monotonic() - started, 3))
                write(receipt, ports)
                invoke(updater, installation, data, 'rollback-release')
            except BaseException as rollback_error:
                recovery_error = RuntimeError('Recovery is incomplete. Primary diagnostic ' + captured.error['diagnosticId']
                                              + '. Admission remains suspended; inspect ' + str(receipt))
                recovery_error.related_diagnostics = [captured.error['diagnosticId']]
                ports.update(recoveryRequired=True, error=str(captured), rollbackError=str(rollback_error))
                try:
                    write(receipt, ports)
                except Exception as retention_error:
                    recovery_error.args = (str(recovery_error) + ' The recovery report also could not be written: ' + str(retention_error),)
                    raise recovery_error from retention_error
                raise recovery_error from rollback_error
            if action == 'rollback' or recovering:
                return dict(state=invoke(updater, installation, data, 'status'), receipt=str(receipt), rolledBack=True)
            if captured is error:
                raise
            raise captured from error


def finalize_rollback(updater, installation, data, state, receipt, guard):
    """Finish the prior transaction before preparing anything new, including after a hard exit."""
    ports = read(ordinary_file(receipt))
    barrier = data / 'promotion.lock'
    if barrier.exists() and barrier.read_text(encoding='ascii') != state['id']:
        raise RuntimeError('Another operation owns the barrier; no recovery effect was attempted')
    if not barrier.exists() and not ports.get('rollbackVerified'):
        raise RuntimeError('Rollback acknowledgement is missing after admission reopened; recovery requires review')
    if guard:
        guard()
    if read(data / 'mcp-runtime.json') != ports['previous']:
        raise RuntimeError('The restored runtime pointer changed; the recovery barrier was retained')
    backup = receipt.parent / 'data-before'
    if not backup.exists():
        raise RuntimeError('The original rollback snapshot is missing; the barrier was retained')
    saved_snapshot(data, backup)
    if startup(Path(ports['previous']['executable']), data) != ports['startupEnabled']:
        raise RuntimeError('The restored startup preference did not match; the barrier was retained')
    if ports['wasRunning'] and resolve_host(ports['previous'], data) is None:
        restored = launch(ports['previous'], data, receipt.parent)
        ports['restoredHostPid'] = restored.pid
    ports['rollbackVerified'] = True
    write(receipt, ports)
    state = invoke(updater, installation, data, 'rollback-release')
    return dict(state=state, receipt=str(receipt), rolledBack=True, alreadyCurrent=True)


def main():
    parser = UpdateParser(description=__doc__)
    parser.add_argument('--data', required=True, type=Path)
    parser.add_argument('--release-directory', type=Path)
    parser.add_argument('--version')
    parser.add_argument('--action', choices=('apply', 'resume', 'status', 'rollback', 'diagnostic'), default='apply')
    parser.add_argument('--diagnostic-id')
    args = parser.parse_args()
    data = args.data.absolute()
    try:
        if args.action == 'diagnostic':
            if not args.diagnostic_id:
                raise ValueError('Diagnostic retrieval requires --diagnostic-id')
            updater = recovery_updater(data)
            result = invoke(updater, data.parents[3], data, 'read-error', args.diagnostic_id)
        elif args.action in ('resume', 'status', 'rollback'):
            result = update(data, action=args.action)
        else:
            if not args.version or not args.release_directory:
                raise ValueError('Apply requires --version and --release-directory; recovery actions require only --data')
            validate_version(args.version)
            if '-rc.' in args.version:
                raise ValueError('Stable updates accept stable releases only; qualify RCs on isolated development fixtures')
            if not (args.release_directory / VERIFICATION_FILE).exists():
                subprocess.run([os.sys.executable, '-B', str(Path(__file__).with_name('verify-release.py')),
                                '--directory', str(args.release_directory.absolute()), '--version', args.version],
                               check=True, timeout=180, stdout=subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)
            archive, digest = qualified_archive(args.release_directory.absolute(), args.version)
            result = update(data, archive, digest, action=args.action)
        print(json.dumps(result))
    except (Exception, KeyboardInterrupt) as error:
        print(json.dumps({'error': report_error(data, error)}), file=os.sys.stderr)
        return 1
    return 0


class UpdateParser(argparse.ArgumentParser):
    def error(self, message):
        raise ValueError(message)


if __name__ == '__main__':
    try:
        os.sys.exit(main())
    except (Exception, KeyboardInterrupt) as error:
        # Invalid arguments may not identify a writable installed scope; inline details remain visible.
        print(json.dumps({'error': report_error(Path.cwd(), error)}), file=os.sys.stderr)
        os.sys.exit(1)

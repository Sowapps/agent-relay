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

from deployment import confirm_quiescence, demand_idle, exclusive, launch, read, restore_snapshot, snapshot, write
from host_identity import identity, resolve_host
from relay_mcp import RelayMcp
from release_artifact import MANIFEST_FILE, VERIFICATION_FILE, extract_archive, ordinary_directory, ordinary_file, qualified_archive, sha256, validate_version, validate_windows_name, verified_archive
from runtime_layout import app_var, runtime_command


EXE = 'AgentRelay.Windows.exe'


def invoke(updater, installation, data, action, candidate=None):
    request = dict(action=action, installation=str(installation), data=str(data), candidate=str(candidate) if candidate else None)
    result = subprocess.run(runtime_command(updater) + ['installation-update', '--data-dir', str(data)],
                            input=json.dumps(request), capture_output=True, text=True, encoding='utf-8',
                            timeout=180, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise RuntimeError(result.stderr.strip() or 'The C# update command did not complete')
    return json.loads(result.stdout)


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


def update(data, archive, digest, *, updater=None, verify=None, guard=None, refresh=None, action='apply'):
    """Resume the same transaction. Never install, recreate data or replay an agent request."""
    data = ordinary_directory(data.absolute())
    installation = data.parents[3]
    if data != app_var(installation) / 'default' or not (data / 'mcp-runtime.json').is_file():
        raise ValueError('Select one existing portable installation and its retained default data')
    candidate = candidate_from_archive(archive, digest)
    updater = updater or {'executable': str(candidate / 'app' / EXE)}
    reports = app_var(installation) / 'update'
    reports.mkdir(exist_ok=True)
    started = time.monotonic()
    with exclusive(data, 'update-driver.lock'):
        existing = None
        if (reports / 'current.json').exists():
            existing = invoke(updater, installation, data, 'status')
        recovering = existing and existing['phase'] in ('rolling_back', 'files_restored', 'recovery_required')
        state = existing if recovering or action in ('status', 'rollback') else invoke(updater, installation, data, 'prepare', candidate)
        if state is None:
            raise RuntimeError('No existing update transaction to inspect or roll back')
        transaction = reports / state['id']
        receipt = transaction / 'ports.json'
        if action == 'status':
            return dict(state=state, receipt=str(receipt))
        if state['phase'] == 'rolled_back' and receipt.exists() and read(receipt).get('rollbackVerified'):
            return dict(state=state, receipt=str(receipt), rolledBack=True, alreadyCurrent=True)
        runtime = {'executable': str(installation / 'app' / EXE)}
        barrier = data / 'promotion.lock'
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
        process = None
        try:
            selected = read(data / 'mcp-runtime.json')
            activated = dict(ports['previous'], executable=runtime['executable'], mcpExecutable=str(data / 'mcp-launcher' / EXE))
            if selected != ports['previous'] and (state['phase'] not in ('files_applied', 'files_restored', 'rolling_back', 'recovery_required') or selected != activated):
                raise RuntimeError('The selected runtime changed outside this transaction; review before retrying')
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
            try:
                stop_runtime(read(data / 'mcp-runtime.json'), data)
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
                ports.update(rollbackVerified=True, error=str(error), elapsedSeconds=round(time.monotonic() - started, 3))
                write(receipt, ports)
                barrier.unlink()
            except BaseException as rollback_error:
                ports.update(recoveryRequired=True, error=str(error), rollbackError=str(rollback_error))
                write(receipt, ports)
                raise RuntimeError('Recovery is incomplete. Admission remains suspended; inspect ' + str(receipt)) from rollback_error
            if action == 'rollback':
                return dict(state=invoke(updater, installation, data, 'status'), receipt=str(receipt), rolledBack=True)
            raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data', required=True, type=Path)
    parser.add_argument('--release-directory', required=True, type=Path)
    parser.add_argument('--version', required=True)
    parser.add_argument('--action', choices=('apply', 'status', 'rollback'), default='apply')
    args = parser.parse_args()
    validate_version(args.version)
    if '-rc.' in args.version:
        parser.error('Stable updates accept stable releases only; qualify RCs on isolated development fixtures')
    if not (args.release_directory / VERIFICATION_FILE).exists():
        subprocess.run([os.sys.executable, '-B', str(Path(__file__).with_name('verify-release.py')),
                        '--directory', str(args.release_directory.absolute()), '--version', args.version],
                       check=True, timeout=180, stdout=subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)
    archive, digest = qualified_archive(args.release_directory.absolute(), args.version)
    print(json.dumps(update(args.data, archive, digest, action=args.action)))


if __name__ == '__main__':
    main()

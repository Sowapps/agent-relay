"""Qualify complete update/repeat/rollback on copied portable data, never the installed stable."""

import argparse
from contextlib import closing
import json
from pathlib import Path
import signal
import os
import sqlite3
import subprocess
import uuid

import complete_update
import pilotage_stable
from deployment import launch, publish, snapshot
from host_identity import identity, resolve_host
from release_artifact import EXTRACTED_DIRECTORY, VERIFICATION_FILE, archive_path, declared_checksum, extract_archive, read_json, verified_archive
from runtime_layout import app_var, reject_pilotage_paths


ROOT = Path(__file__).resolve().parents[3]


def tables(data):
    with closing(sqlite3.connect((data / 'relay.sqlite').as_uri() + '?mode=ro', uri=True)) as database:
        names = [row[0] for row in database.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")]
        return {name: sorted(database.execute('SELECT * FROM "' + name.replace('"', '""') + '"').fetchall()) for name in names}


def fixture(root, baseline):
    installation = root / 'installation'
    info = read_json(baseline / VERIFICATION_FILE)
    archive = archive_path(baseline, info['version'])
    with verified_archive(archive, declared_checksum(archive)) as package:
        extract_archive(package, installation)
    source = baseline / EXTRACTED_DIRECTORY
    context = read_json(source / 'requests-0/example-context.json')
    data = app_var(installation) / 'default'
    data.parent.mkdir(parents=True)
    snapshot(app_var(source) / 'default', data)
    (data / 'mcp-runtime.json').unlink(missing_ok=True)
    runtime = {'executable': str(installation / 'app' / complete_update.EXE)}
    subprocess.run([runtime['executable'], 'initialize', '--data-dir', str(data), '--connection-id', context['connectionId']],
                   capture_output=True, check=True, timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
    launch(runtime, data, root)
    return installation, data, runtime


def stop(data):
    pointer = read_json(data / 'mcp-runtime.json')
    owner = resolve_host(pointer, data)
    if owner and identity(owner, data):
        os.kill(owner['pid'], signal.SIGTERM)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline-directory', required=True, type=Path)
    parser.add_argument('--candidate-directory', required=True, type=Path)
    args = parser.parse_args()
    baseline, candidate = args.baseline_directory.resolve(), args.candidate_directory.resolve()
    version = read_json(candidate / 'payload/release-manifest.json')['version']
    archive = archive_path(candidate, version)
    digest = declared_checksum(archive)
    root = ROOT / 'local/apps/agent-relay/tests' / ('cu-' + uuid.uuid4().hex[:8])
    reject_pilotage_paths(ROOT, root)
    root.mkdir(parents=True)
    results = dict(modelCalls=0, stableTouched=False)
    nominal = root / 'nominal'
    nominal.mkdir()
    installation, data, runtime = fixture(nominal, baseline)
    original = tables(data)
    grants = (data / 'relay-projects.json').read_bytes()
    try:
        try:
            publish(data, nominal / 'unused-preparation', nominal / 'unused-report', False)
        except ValueError as error:
            assert 'complete update procedure' in str(error), str(error)
        else:
            raise AssertionError('Runtime-only publication accepted a portable installation')
        first = complete_update.update(data, archive, digest)
        assert first['state']['phase'] == 'complete', first['state']
        assert tables(data) == original and (data / 'relay-projects.json').read_bytes() == grants
        mtime = (installation / 'app' / complete_update.EXE).stat().st_mtime_ns
        repeated = complete_update.update(data, archive, digest)
        assert repeated['alreadyCurrent'] is True and repeated['state']['id'] == first['state']['id']
        assert (installation / 'app' / complete_update.EXE).stat().st_mtime_ns == mtime
        owner = resolve_host(runtime, data)
        assert owner['pid'] == first['hostPid']
        assert complete_update.startup(Path(runtime['executable']), data) is False
        results['nominal'] = first
        results['idempotent'] = dict(hostPid=owner['pid'], executableTimestampUnchanged=True, transactionUnchanged=True)
        stop(data)
        restarted = complete_update.update(data, archive, digest)
        assert restarted['state']['phase'] == 'complete' and restarted['state']['id'] == first['state']['id']
        results['stoppedHostRecovery'] = dict(receipt=restarted['receipt'], sameTransaction=True)
        context = read_json(baseline / EXTRACTED_DIRECTORY / 'requests-0/example-context.json')
        config = nominal / 'codex.toml'
        config.write_text('[mcp_servers.' + pilotage_stable.SERVER + ']\ncommand = '
                          + json.dumps(str(data / 'mcp-launcher' / complete_update.EXE)) + '\nargs = '
                          + json.dumps(['mcp', '--data-dir', str(data), '--connection-id', context['connectionId']]) + '\n', encoding='utf-8')
        before_config = config.read_bytes()
        witness = pilotage_stable.upgrade(config, candidate, version, context['projectId'], context['mainId'],
                                         str(uuid.uuid4()), nominal, rehearsal=True)
        witness_report = read_json(witness)
        assert witness_report['completeUpdate']['phase'] == 'complete'
        assert witness_report['freshMcp']['projectId'] == context['projectId']
        assert config.read_bytes() == before_config
        results['scopedWitness'] = dict(receipt=str(witness), unchangedRegistration=True)
        current_pid = resolve_host(runtime, data)['pid']
        try:
            complete_update.update(data, archive, digest, action='rollback')
        except RuntimeError as error:
            assert 'Admission was reopened' in str(error), str(error)
        else:
            raise AssertionError('A completed update restored an old database')
        assert resolve_host(runtime, data)['pid'] == current_pid and tables(data) == original
        results['lateRollbackRefused'] = True
    finally:
        stop(data)
    failing = root / 'rollback'
    failing.mkdir()
    installation, data, runtime = fixture(failing, baseline)
    original = tables(data)
    old_executable = (installation / 'app' / complete_update.EXE).read_bytes()

    def failed_verification():
        with closing(sqlite3.connect(data / 'relay.sqlite')) as database:
            database.execute('CREATE TABLE injected_migration_failure(value TEXT)')
            database.commit()
        raise RuntimeError('injected health failure after candidate startup')

    try:
        try:
            complete_update.update(data, archive, digest, verify=failed_verification)
        except RuntimeError as error:
            assert 'injected health failure' in str(error), str(error)
        else:
            raise AssertionError('Failed health did not roll back')
        assert tables(data) == original
        assert (installation / 'app' / complete_update.EXE).read_bytes() == old_executable
        assert not (data / 'promotion.lock').exists()
        state = read_json(app_var(installation) / 'update/current.json')
        assert state['phase'] == 'rolled_back'
        ports = read_json(app_var(installation) / 'update' / state['id'] / 'ports.json')
        assert ports['rollbackVerified'] is True
        results['rollback'] = ports
    finally:
        stop(data)
    interrupted = root / 'interrupted'
    interrupted.mkdir()
    installation, data, runtime = fixture(interrupted, baseline)
    original = tables(data)
    try:
        crashed = subprocess.run([os.sys.executable, '-B', str(Path(__file__).resolve()), '--crash-after-files',
                                  str(data), str(archive), digest], capture_output=True, timeout=180,
                                 creationflags=subprocess.CREATE_NO_WINDOW)
        assert crashed.returncode == 73, crashed.stderr.decode(errors='replace')
        partial = read_json(app_var(installation) / 'update/current.json')
        assert partial['phase'] == 'files_applied' and (data / 'promotion.lock').exists()
        resumed = complete_update.update(data, archive, digest)
        assert resumed['state']['id'] == partial['id'] and resumed['state']['phase'] == 'complete'
        assert tables(data) == original and not (data / 'promotion.lock').exists()
        results['interruption'] = dict(childExitCode=73, resumedSameTransaction=True, receipt=resumed['receipt'])
    finally:
        stop(data)
    results.update(passed=True, root=str(root))
    (root / 'result.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    print(json.dumps(dict(passed=True, report=str(root / 'result.json'), modelCalls=0, stableTouched=False)))


if __name__ == '__main__':
    if len(os.sys.argv) == 5 and os.sys.argv[1] == '--crash-after-files':
        original_invoke = complete_update.invoke

        def crash_after_files(*arguments, **keywords):
            result = original_invoke(*arguments, **keywords)
            if arguments[3] == 'apply':
                os._exit(73)  # Deliberately skip every cleanup/finally in the isolated updater process.
            return result

        complete_update.invoke = crash_after_files
        complete_update.update(Path(os.sys.argv[2]), Path(os.sys.argv[3]), os.sys.argv[4])
    else:
        main()

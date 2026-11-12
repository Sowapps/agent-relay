"""Targeted crash/recovery/diagnostic qualification using copied portable installations only."""

import argparse
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import uuid

import complete_update as updater
from deployment import read
from host_identity import resolve_host
from relay_mcp import RelayMcp
from release_artifact import archive_path, declared_checksum
from runtime_layout import app_var, reject_pilotage_paths

spec = importlib.util.spec_from_file_location('complete_fixture', Path(__file__).with_name('test-complete-update.py'))
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)


def cli(data, action, *arguments):
    result = subprocess.run([os.sys.executable, '-B', str(Path(updater.__file__)), '--data', str(data), '--action', action, *arguments],
                            capture_output=True, text=True, encoding='utf-8', timeout=180,
                            creationflags=subprocess.CREATE_NO_WINDOW)
    assert result.returncode == 0, result.stderr
    return json.loads(result.stdout)


def child(data, archive, digest, boundary):
    result = subprocess.run([os.sys.executable, '-B', str(Path(__file__).resolve()), '--crash', boundary,
                             str(data), str(archive), digest], capture_output=True, timeout=180,
                            creationflags=subprocess.CREATE_NO_WINDOW)
    assert result.returncode in (73, 74, 75), result.stderr.decode(errors='replace')


def remove_source(case, archive):
    source = archive.parent
    destination = case / 'source-offline'
    assert source.resolve().is_relative_to(case.resolve()) and destination.resolve().is_relative_to(case.resolve())
    source.rename(destination)
    assert not archive.exists()


def local_archive(case, archive):
    source = case / 'source'
    source.mkdir()
    local = source / archive.name
    os.link(archive, local)  # Removing the fixture path never removes the original qualified artifact.
    return local


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline-directory', required=True, type=Path)
    parser.add_argument('--candidate-directory', required=True, type=Path)
    args = parser.parse_args()
    baseline, candidate = args.baseline_directory.resolve(), args.candidate_directory.resolve()
    version = read(candidate / 'payload/release-manifest.json')['version']
    archive = archive_path(candidate, version)
    digest = declared_checksum(archive)
    root = fixture.ROOT / 'local/apps/agent-relay/tests' / ('ur-' + uuid.uuid4().hex[:8])
    reject_pilotage_paths(fixture.ROOT, root)
    root.mkdir(parents=True)
    results = dict(modelCalls=0, wslCalls=0, stableTouched=False)
    for boundary in ('rollback-finish', 'rollback-release', 'files'):
        case = root / boundary
        case.mkdir()
        installation, data, runtime = fixture.fixture(case, baseline)
        original = fixture.tables(data)
        local = local_archive(case, archive)
        try:
            child(data, local, digest, boundary)
            state = read(app_var(installation) / 'update/current.json')
            before = resolve_host(runtime, data)
            remove_source(case, local)
            status = cli(data, 'status')
            assert status['state']['id'] == state['id'] and (data / 'promotion.lock').exists()
            resumed = cli(data, 'rollback' if boundary == 'files' else 'resume')
            assert resumed['state']['phase'] == 'rolled_back' and resumed['state']['id'] == state['id']
            assert not (data / 'promotion.lock').exists() and fixture.tables(data) == original
            if before:
                assert resolve_host(runtime, data)['pid'] == before['pid'], 'Finalizing rollback unnecessarily restarted the host'
            ports = read(Path(resumed['receipt']))
            if ports.get('failure'):
                identifier = ports['failure']['diagnosticId']
                detail = cli(data, 'diagnostic', '--diagnostic-id', identifier)
                assert detail['error']['diagnosticId'] == identifier and detail['detail']
            repeat = cli(data, 'resume')
            assert repeat['rolledBack'] is True
            results[boundary] = dict(transaction=state['id'], archiveRemoved=True, sameTransaction=True, restoredTables=True)
        finally:
            fixture.stop(data)
    case = root / 'connected'
    case.mkdir()
    installation, data, runtime = fixture.fixture(case, baseline)
    original = fixture.tables(data)
    pointer = read(data / 'mcp-runtime.json')
    binding = dict(executable=pointer['mcpExecutable'], connectionId=pointer['connectionId'])
    try:
        with (case / 'retained-client.stderr.log').open('w', encoding='utf-8') as errors:
            with RelayMcp(binding, data, case, errors) as client:
                client.tool('list_projects')
                try:
                    first = updater.update(data, archive, digest)
                except updater.UpdateFailure as error:
                    assert Path(error.error['detailsPath']).is_file()
                    state = read(app_var(installation) / 'update/current.json')
                    assert state['phase'] == 'rolled_back' and not (data / 'promotion.lock').exists()
                    assert client.tool('get_connection_status')['version'] != version
                    results['connectedOutcome'] = dict(state='safe_rollback', diagnosticId=error.error['diagnosticId'])
                else:
                    assert first['state']['phase'] == 'awaiting_clients', first['state']
                    results['connectedOutcome'] = dict(state='awaiting_clients', transaction=first['state']['id'])
        with (case / 'reloaded-client.stderr.log').open('w', encoding='utf-8') as errors:
            # After a rollback the update first runs with the old mapped client gone.
            current = read(app_var(installation) / 'update/current.json')
            if current['phase'] == 'rolled_back':
                updater.update(data, archive, digest)
            with RelayMcp(binding, data, case, errors) as client:
                done = updater.update(data, action='resume')
                status = client.tool('get_connection_status')
                assert done['state']['phase'] == 'complete' and status['version'] == status['brokerVersion'] == version
                assert status['clientRefreshRequired'] is False and fixture.tables(data) == original
                if results['connectedOutcome'].get('diagnosticId'):
                    assert status['lastUpdateError']['diagnosticId'] == results['connectedOutcome']['diagnosticId']
        results['connectedClientReloaded'] = True
    finally:
        fixture.stop(data)
    case = root / 'damaged'
    case.mkdir()
    installation, data, runtime = fixture.fixture(case, baseline)
    try:
        child(data, archive, digest, 'files')
        state = read(app_var(installation) / 'update/current.json')
        broken = next(item for item in state['files'] if item['before'] is not None and item['before'] != item['after'])
        backup = app_var(installation) / 'update' / state['id'] / 'before' / broken['name']
        assert backup.resolve().is_relative_to(case.resolve())
        backup.unlink()
        try:
            updater.update(data, action='rollback')
        except updater.UpdateFailure as error:
            detail = json.loads(Path(error.error['detailsPath']).read_text(encoding='utf-8'))
            assert detail['relatedDiagnostics'], detail
            assert (data / 'promotion.lock').exists()
            assert cli(data, 'status')['state']['phase'] == 'recovery_required'
            results['failedRecovery'] = dict(diagnosticId=error.error['diagnosticId'], related=detail['relatedDiagnostics'], barrierRetained=True)
        else:
            raise AssertionError('A damaged backup was silently accepted')
    finally:
        fixture.stop(data)
    results.update(passed=True, root=str(root))
    (root / 'result.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    print(json.dumps(dict(passed=True, report=str(root / 'result.json'), modelCalls=0, stableTouched=False)))


if __name__ == '__main__':
    if len(os.sys.argv) == 6 and os.sys.argv[1] == '--crash':
        boundary, data, archive, digest = os.sys.argv[2], Path(os.sys.argv[3]), Path(os.sys.argv[4]), os.sys.argv[5]
        original_invoke = updater.invoke

        def crash(*arguments, **keywords):
            if boundary == 'rollback-release' and arguments[3] == boundary:
                os._exit(75)
            result = original_invoke(*arguments, **keywords)
            if boundary == 'rollback-finish' and arguments[3] == boundary:
                os._exit(74)
            if boundary == 'files' and arguments[3] == 'apply':
                os._exit(73)
            return result

        def failure():
            raise RuntimeError('primary fixture failure before rollback interruption')

        updater.invoke = crash
        updater.update(data, archive, digest, verify=None if boundary == 'files' else failure)
    else:
        main()

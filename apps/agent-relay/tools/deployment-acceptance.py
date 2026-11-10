"""Exercise an update and rollback on new synthetic data without calling WSL or models.

@see isolated-delivery.py for the normal caller and verified candidate evidence.
"""

import argparse
from contextlib import closing
import json
from pathlib import Path
import sqlite3
import subprocess
import time
from unittest.mock import patch
import uuid

import deployment
from relay_mcp import RelayMcp
from release_artifact import ordinary_directory, ordinary_file
from runtime_layout import reject_pilotage_paths, runtime_command


class InjectedFailure(RuntimeError):
    """The test host was started and a durable change was made before failure."""


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def verify_durable_guard(runtime, case):
    missing = case / 'uninitialized'
    deployment.demand_idle(runtime, missing, allow_uninitialized=True)
    require(not missing.exists(), 'A read-only check initialized a data directory')
    try:
        deployment.demand_idle(runtime, missing)
    except RuntimeError as error:
        require('storage_unavailable' in str(error), str(error))
    else:
        raise RuntimeError('A missing required database was accepted')

    damaged = case / 'damaged'
    damaged.mkdir()
    database = damaged / 'relay.sqlite'
    contents = b'damaged fixture'
    database.write_bytes(contents)
    try:
        deployment.demand_idle(runtime, damaged, allow_uninitialized=True)
    except RuntimeError:
        require(database.read_bytes() == contents, 'The read-only check changed the damaged database')
    else:
        raise RuntimeError('A damaged database was accepted as uninitialized')


def prepare_data(data, fixture):
    if fixture:
        deployment.snapshot(ordinary_directory(fixture), data)
        for name in ('mcp-runtime.json', 'mcp-contract.json', 'mcp-contract-history.json'):
            (data / name).unlink(missing_ok=True)
    else:
        data.mkdir()
    settings = data / 'relay-config.json'
    if not settings.exists():
        deployment.write(settings, {'version': 1, 'profiles': []})
    protected = [data / 'relay-projects.json', *(data / 'connections').glob('*')]
    return {path: ordinary_file(path).read_bytes() for path in protected if path.is_file()}


def verify_binding(case, data, binding, root):
    if binding is None:
        return
    pointer = deployment.read(data / 'mcp-runtime.json')
    with (case / 'mcp.stderr.log').open('a', encoding='utf-8') as errors:
        with RelayMcp(dict(pointer, connectionId=binding['connectionId']), data, root, errors) as client:
            projects = client.tool('list_projects')['configuration']['projects']
            require([project['id'] for project in projects] == [binding['projectId']],
                    'Fresh MCP connection did not retain the expected project scope')


def verify_preserved(expected):
    for path, content in expected.items():
        require(path.read_bytes() == content, f'Rehearsal changed protected fixture file: {path.name}')


def verify_bad_preflight(candidate, data, reports, restored):
    settings = data / 'relay-config.json'
    original = settings.read_bytes()
    try:
        deployment.write(settings, {'version': 999, 'profiles': []})
        try:
            deployment.publish(data, candidate, reports, False)
        except RuntimeError as error:
            if 'Candidate host exited' not in str(error):
                raise
        else:
            raise RuntimeError('Invalid preparation was accepted')
    finally:
        settings.write_bytes(original)
    require(deployment.identity(restored, data), 'Failed preflight displaced the restored host')


def exercise_update(args, root, case, result):
    data = case / 'data'
    reports = case / 'reports'
    reject_pilotage_paths(root, args.prepared, args.previous_prepared, *(path for path in (args.fixture, args.binding) if path))
    runtime = deployment.read(ordinary_file(args.prepared / 'mcp-runtime.json'))
    previous = deployment.read(ordinary_file(args.previous_prepared / 'mcp-runtime.json'))
    for pointer in (runtime, previous):
        deployment.validate(pointer, root, development=True)
    verify_durable_guard(runtime, case)
    result['durableGuardMissingAndDamagedChecks'] = True
    require(runtime_command(previous) != runtime_command(runtime),
            'Rehearsal requires distinct candidate and baseline runtime paths')
    expected = prepare_data(data, args.fixture)
    binding = deployment.read(ordinary_file(args.binding)) if args.binding else None
    if binding is not None:
        require(any(path.parent == data / 'connections' for path in expected),
                'Scoped rehearsal fixture has no credential files to verify')
    launch = deployment.launch
    confirm = deployment.confirm_quiescence
    children = []
    inject = False
    def promote(candidate, *, expected=data):
        deployment.publish(data, candidate, reports, False, expected_data=expected, guard_runtime=runtime)

    def confirm_and_probe(pointer, target):
        confirm(pointer, target)
        response = subprocess.run(runtime_command(pointer) + ['send', '--data-dir', str(target), '--message', 'late fixture request'],
                                  capture_output=True, timeout=10, creationflags=subprocess.CREATE_NO_WINDOW)
        error = json.loads(response.stderr)
        require(response.returncode != 0 and error['code'] == 'maintenance' and error['effectsPossible'] is False,
                'A request after quiescence confirmation was not safely refused')
        result['lateRequestRefused'] = True

    def owned_launch(pointer, target, logs, show=False):
        nonlocal inject
        process = launch(pointer, target, logs, show)
        children.append(process)
        if target == data and inject:
            inject = False
            try:
                with closing(sqlite3.connect(data / 'relay.sqlite')) as connection:
                    connection.execute('CREATE TABLE injected_failure(marker TEXT)')
                    connection.commit()
                deployment.write(data / 'relay-config.json', {'version': 999, 'profiles': []})
            finally:
                process.terminate()
                process.wait(timeout=10)
            raise InjectedFailure('Controlled candidate failure after migration, before publication')
        return process

    try:
        with patch.object(deployment, 'launch', owned_launch), patch.object(deployment, 'confirm_quiescence', confirm_and_probe):
            promote(args.previous_prepared)
            verify_binding(case, data, binding, root)
            old = deployment.read(reports / 'host.json')
            pointer = deployment.read(data / 'mcp-runtime.json')
            if args.expect_legacy_refusal:
                try:
                    promote(args.prepared)
                except RuntimeError as error:
                    require('did not confirm the admission barrier' in str(error), str(error))
                else:
                    raise RuntimeError('A legacy host was accepted for live promotion')
                require(deployment.identity(old, data), 'The refusal displaced the legacy host')
                require(not (data / 'promotion.lock').exists(), 'The refused legacy update left admissions blocked')
                old_process = next(process for process in children if process.pid == old['pid'])
                old_process.terminate()
                old_process.wait(timeout=10)
                promote(args.prepared)
                verify_binding(case, data, binding, root)
                verify_preserved(expected)
                require(not (data / 'promotion.lock').exists(), 'Healthy offline promotion left admissions blocked')
                result.update(legacyOnlinePromotionRefused=True, stoppedLegacyPromotionPassed=True)
                return
            settings_before = (data / 'relay-config.json').read_bytes()
            try:
                promote(args.prepared, expected=case / 'wrong-scope')
            except ValueError as error:
                require('approved promotion target' in str(error), str(error))
            else:
                raise RuntimeError('Wrong promotion target was accepted')
            require(deployment.identity(old, data), 'Wrong-target refusal displaced the current host')
            with closing(sqlite3.connect(data / 'relay.sqlite')) as connection:
                active_id = str(uuid.uuid4())
                connection.execute('INSERT INTO work(id,payload) VALUES (?,?)', (active_id, '{"state":"running"}'))
                connection.commit()
            try:
                try:
                    promote(args.prepared)
                except RuntimeError as error:
                    require('Active durable work' in str(error), str(error))
                else:
                    raise RuntimeError('An active target accepted promotion')
            finally:
                with closing(sqlite3.connect(data / 'relay.sqlite')) as connection:
                    connection.execute('DELETE FROM work WHERE id=?', (active_id,))
                    connection.commit()
            require(deployment.identity(old, data), 'Busy-target refusal displaced the current host')
            require(not (data / 'promotion.lock').exists(), 'Busy-target refusal left admissions blocked')
            result.update(wrongTargetRefused=True, activeWorkRefused=True)
            started = time.monotonic()
            inject = True
            try:
                promote(args.prepared)
            except InjectedFailure:
                pass
            else:
                raise RuntimeError('Injected failure was not observed')
            restored = deployment.read(reports / 'host.json')
            require(restored['pid'] != old['pid'] and deployment.identity(restored, data),
                    'Rollback did not restore a running baseline host')
            require(deployment.read(data / 'mcp-runtime.json') == pointer,
                    'Rollback did not restore the previous runtime pointer')
            require(not (data / 'promotion.lock').exists(), 'Healthy rollback left admissions blocked')
            require((data / 'relay-config.json').read_bytes() == settings_before,
                    'Rollback did not restore the previous relay settings')
            verify_preserved(expected)
            verify_binding(case, data, binding, root)
            with closing(sqlite3.connect(data / 'relay.sqlite')) as connection:
                marker = connection.execute(
                    "SELECT 1 FROM sqlite_master WHERE name='injected_failure'").fetchone()
            require(marker is None, 'Rollback retained the injected database change')
            result.update(rollbackAfterMigration=True, failedUpdateAndRecoverySeconds=round(time.monotonic() - started, 3))
            verify_bad_preflight(args.prepared, data, reports, restored)

            started = time.monotonic()
            promote(args.prepared)
            verify_binding(case, data, binding, root)
            verify_preserved(expected)
            require(runtime_command(deployment.read(data / 'mcp-runtime.json')) == runtime_command(runtime),
                    'Successful rehearsal did not select the candidate runtime')
            require(not (data / 'promotion.lock').exists(), 'Healthy promotion left admissions blocked')
            result.update(failedPreparationPreservesHost=True, projectAndCredentialFilesPreserved=len(expected),
                          healthyUpdateSeconds=round(time.monotonic() - started, 3),
                          scopedMcpAfterUpdateAndRollback=binding is not None,
                          existingChatVerification='unconfirmed')
    finally:
        # Only handles returned by our own launch calls are eligible for cleanup.
        for process in reversed(children):
            if process.poll() is None:
                process.terminate()
            process.wait(timeout=10)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prepared', type=Path, required=True)
    parser.add_argument('--previous-prepared', type=Path, required=True)
    parser.add_argument('--fixture', type=Path)
    parser.add_argument('--binding', type=Path)
    parser.add_argument('--expect-legacy-refusal', action='store_true')
    args = parser.parse_args()
    if args.binding and not args.fixture:
        parser.error('--binding requires --fixture')
    root = Path(__file__).resolve().parents[3]
    cases = ordinary_directory(root / 'local/apps/agent-relay/tests')
    case = cases / ('deployment-' + uuid.uuid4().hex)
    case.mkdir(parents=True)
    receipt = case / 'result.json'
    result = {'modelPrompts': 0, 'passed': False}
    try:
        exercise_update(args, root, case, result)
        result['passed'] = True
    except (Exception, KeyboardInterrupt) as error:
        result['error'] = str(error) or type(error).__name__
        raise
    finally:
        deployment.write(receipt, result)
        print(json.dumps({'passed': result['passed'], 'report': str(receipt)}), flush=True)


if __name__ == '__main__':
    main()

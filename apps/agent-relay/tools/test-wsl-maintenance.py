"""Qualify C# WSL maintenance diagnostics without models, shared restarts or project access."""
import argparse
import json
from pathlib import Path
import time
import uuid

from deployment import read, write
from runtime_layout import reject_pilotage_paths, validate_runtime
from wsl_maintenance import run_script, WslMaintenanceError


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime-pointer', required=True, type=Path)
    parser.add_argument('--distribution', required=True)
    parser.add_argument('--user', required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    runtime = read(args.runtime_pointer.resolve())
    validate_runtime(runtime, root, development=True)
    case = root / 'local/apps/agent-relay/tests' / ('wsl-diagnostics-' + uuid.uuid4().hex)
    reject_pilotage_paths(root, case)
    case.mkdir(parents=True)
    report = {'passed': False, 'modelCalls': 0, 'attempts': []}
    started = time.monotonic()

    def invoke(script, timeout=5, distribution=None):
        try:
            result = run_script(runtime, case, distribution or args.distribution, args.user,
                                script, {'text': 'Échange — 中文 😀'}, timeout)
        except WslMaintenanceError as error:
            report['attempts'].append(error.diagnostic)
            raise
        report['attempts'].append(result)
        return result

    try:
        normal = invoke("import json,sys;print(json.load(sys.stdin)['text'])")
        assert normal['output'].strip() == 'Échange — 中文 😀'
        assert normal['phase'] == 'native_operation' and normal['error'] == ''
        assert normal['runtimeWaitSeconds'] >= normal['seconds'] - 0.01
        try:
            invoke("import sys;print('native-failure-É',file=sys.stderr,flush=True);sys.exit(7)")
        except WslMaintenanceError as error:
            assert error.diagnostic['exitCode'] == 7 and not error.diagnostic['timedOut']
            assert error.diagnostic['phase'] == 'native_operation'
            assert error.diagnostic['error'].strip() == 'native-failure-É'
        else:
            raise AssertionError('Native failure was accepted')
        try:
            invoke("import sys,time;print('before-wait',flush=True);print('native-stderr',file=sys.stderr,flush=True);time.sleep(3)", 1)
        except WslMaintenanceError as error:
            value = error.diagnostic
            assert value['timedOut'] and value['phase'] == 'native_operation'
            assert 'before-wait' in value['output'] and 'native-stderr' in value['error']
            assert value['effectsPossible'] is True and value['seconds'] < 6
        else:
            raise AssertionError('Native timeout was accepted')
        try:
            invoke('raise AssertionError("must not run")', distribution='relay-missing-' + uuid.uuid4().hex)
        except WslMaintenanceError as error:
            value = error.diagnostic
            assert value['phase'] == 'native_start_unconfirmed' and not value['timedOut']
            assert value['error'] or value['output']
            assert '\x00' not in value['error'] + value['output']
        else:
            raise AssertionError('Absent distribution was accepted')
        report['passed'] = True
    except BaseException as error:
        report['error'] = str(error) or type(error).__name__
        raise
    finally:
        report['seconds'] = round(time.monotonic() - started, 3)
        write(case / 'result.json', report)
        print(json.dumps({'report': str(case / 'result.json'), 'passed': report['passed'],
                          'seconds': report['seconds']}), flush=True)


if __name__ == '__main__':
    main()

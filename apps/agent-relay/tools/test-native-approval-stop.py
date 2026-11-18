"""Exercise guarded approval recovery with isolated Linux tmux, without a model."""

import json
import os
from pathlib import Path
import pwd
import shutil
import signal
import sys
import tempfile
import time
import uuid


def wait_for(get, seconds=5):
    deadline = time.monotonic() + seconds
    while not (value := get()):
        if time.monotonic() >= deadline:
            raise AssertionError('Timed out waiting for fixture state')
        time.sleep(.05)
    return value


def refuses(recover, root, request, stop, effects_possible=False):
    from native_errors import NativeFailure
    try:
        recover(root, 'codex', request, stop)
    except NativeFailure as error:
        assert error.code == 'cancellation_unconfirmed' and error.effects_possible is effects_possible
    else:
        raise AssertionError('Expected unconfirmed stop')


def exercise(base, adapter):
    session = uuid.uuid4()
    root = base / 'sessions' / session.hex
    data = root / 'codex' / 'data'
    data.mkdir(parents=True)
    tools = base / 'install'
    shutil.copytree(adapter, tools)
    layout = tools / 'native-layout.json'
    layout.write_text(json.dumps(json.loads(layout.read_text()) | {'base': str(base)}))
    sys.path.insert(0, str(tools))
    from approval_stop import pane, recover
    from native_clients import process_stamp, tmux
    from relay_files import commit

    (root / 'tmux.conf').touch()
    commit(root / 'provision-owner.json', {'sessionId': str(session), 'user': pwd.getpwuid(os.getuid()).pw_name})
    scope = {key: str(uuid.uuid4()) for key in ('projectId', 'senderId', 'recipientId', 'workspaceId')}
    commit(root / 'codex' / 'context.json', scope | {'sessionId': str(session)})

    pid_file = root / 'child.pid'
    launcher = root / 'fixture.py'
    launcher.write_text('import subprocess, time\nfrom pathlib import Path\n'
                        'child = subprocess.Popen(["sleep", "30"], start_new_session=True)\n'
                        f'Path({str(pid_file)!r}).write_text(str(child.pid))\n'
                        'time.sleep(30)\n')
    child = None
    child_stamp = None
    try:
        tmux(root, 'new-session', '-d', '-s', 'independent', 'sleep', '30')
        tmux(root, 'new-session', '-d', '-s', 'codex', sys.executable, str(launcher))
        child = int(wait_for(lambda: pid_file.exists() and pid_file.read_text()))
        child_stamp = process_stamp(child)
        assert child_stamp is not None

        identifier = str(uuid.uuid4())
        request = {'requestId': identifier, 'scope': scope}
        dispatch = {'id': identifier, 'scope': scope, 'expectedNativeGeneration': 'wrong-generation',
                    'expectedDistributionGeneration': process_stamp(1)['generation']}
        path = data / f'dispatch-{identifier}.json'
        commit(path, {'request': dispatch})
        refuses(recover, root, request, False)
        generation = pane(root, 'codex')[1]
        dispatch['expectedNativeGeneration'] = generation
        commit(path, {'request': dispatch})
        assert recover(root, 'codex', request, False) == {'requestId': identifier, 'prepared': True}
        assert pane(root, 'codex')[1] == generation

        refuses(recover, root, request, True, effects_possible=True)
        assert pane(root, 'codex') is None
        assert process_stamp(child) == child_stamp
        os.kill(child, signal.SIGTERM)
        assert recover(root, 'codex', request, True) == {'requestId': identifier, 'stopped': True}
        assert recover(root, 'codex', request, True) == {'requestId': identifier, 'stopped': True}
        assert pane(root, 'independent') is not None
        print('NATIVE_APPROVAL_STOP=PASS (generation guard, detached writer, verified retry, independent terminal)')
    finally:
        if child_stamp is not None and process_stamp(child) == child_stamp:
            os.kill(child, signal.SIGTERM)
        tmux(root, 'kill-server', check=False)


if __name__ == '__main__':
    source = Path(__file__).resolve().parents[1] / 'wsl'
    with tempfile.TemporaryDirectory(prefix='ar-approval-stop-') as directory:
        exercise(Path(directory), source)

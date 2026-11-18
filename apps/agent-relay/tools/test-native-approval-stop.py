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


def dispatch_for(data, scope, native, distribution, allow_work=True):
    from relay_files import commit
    identifier = str(uuid.uuid4())
    request = {'requestId': identifier, 'scope': scope}
    commit(data / f'dispatch-{identifier}.json', {'request': {
        'id': identifier, 'scope': scope, 'expectedNativeGeneration': native,
        'expectedDistributionGeneration': distribution, 'allowLocalWork': allow_work}})
    return request


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
                    'expectedDistributionGeneration': process_stamp(1)['generation'], 'allowLocalWork': False}
        path = data / f'dispatch-{identifier}.json'
        commit(path, {'request': dispatch})
        refuses(recover, root, request, False)
        generation = pane(root, 'codex')[1]
        dispatch['expectedNativeGeneration'] = generation
        commit(path, {'request': dispatch})
        assert recover(root, 'codex', request, False) == {'requestId': identifier, 'prepared': True}
        assert pane(root, 'codex')[1] == generation

        assert recover(root, 'codex', request, True) == {'requestId': identifier, 'terminalClosed': True, 'stopped': False}
        assert pane(root, 'codex') is None
        assert process_stamp(child) == child_stamp
        os.kill(child, signal.SIGTERM)
        assert recover(root, 'codex', request, True) == {'requestId': identifier, 'stopped': True}
        assert recover(root, 'codex', request, True) == {'requestId': identifier, 'stopped': True}
        assert pane(root, 'independent') is not None

        pid_file.unlink()
        tmux(root, 'new-session', '-d', '-s', 'codex', sys.executable, str(launcher))
        child = int(wait_for(lambda: pid_file.exists() and pid_file.read_text()))
        child_stamp = process_stamp(child)
        assert child_stamp is not None
        original = pane(root, 'codex')[1]
        distribution = process_stamp(1)['generation']
        missing = dispatch_for(data, scope, original, distribution)
        tmux(root, 'kill-session', '-t', 'codex')
        assert pane(root, 'codex') is None and process_stamp(child) == child_stamp
        assert recover(root, 'codex', missing, False)['prepared']
        assert recover(root, 'codex', missing, True)['terminalClosed']
        assert not recover(root, 'codex', missing, True)['stopped']
        os.kill(child, signal.SIGTERM)

        # An already detached child is invisible to a later ancestry/SID census.
        orphan = root / 'orphan.py'
        orphan.write_text('import os, time\nfrom pathlib import Path\n'
                          'if os.fork(): os._exit(0)\n'
                          'os.setsid()\n'
                          f'Path({str(pid_file)!r}).write_text(str(os.getpid()))\n'
                          'time.sleep(30)\n')
        pid_file.unlink()
        tmux(root, 'new-session', '-d', '-s', 'codex', 'sh', '-c', f'{sys.executable} {orphan}; exec sleep 30')
        child = int(wait_for(lambda: pid_file.exists() and pid_file.read_text()))
        child_stamp = process_stamp(child)
        assert child_stamp is not None
        from approval_stop import owned_processes, tracked
        leader, original, _ = pane(root, 'codex')
        assert str(child) not in tracked(owned_processes(), leader)
        writable = dispatch_for(data, scope, original, distribution)
        assert recover(root, 'codex', writable, False)['prepared']
        assert recover(root, 'codex', writable, True) == {
            'requestId': writable['requestId'], 'terminalClosed': True, 'stopped': False}
        assert pane(root, 'codex') is None and process_stamp(child) == child_stamp
        os.kill(child, signal.SIGTERM)

        # Encode a prior boot in an isolated copy. The local WSL kernel never restarts.
        previous_boot = str(uuid.uuid4())
        previous_native = previous_boot + ':' + original.split(':', 1)[1]
        previous_distro = previous_boot + ':' + distribution.split(':', 1)[1]
        inconsistent = dispatch_for(data, scope, previous_native, distribution)
        refuses(recover, root, inconsistent, False)
        previous = dispatch_for(data, scope, previous_native, previous_distro)
        tmux(root, 'new-session', '-d', '-s', 'codex', 'sleep', '30')
        refuses(recover, root, previous, False)
        assert pane(root, 'codex') is not None
        tmux(root, 'kill-session', '-t', 'codex')
        assert recover(root, 'codex', previous, False)['prepared']
        assert recover(root, 'codex', previous, True)['stopped']
        assert recover(root, 'codex', previous, True)['stopped']
        assert pane(root, 'independent') is not None
        print('NATIVE_APPROVAL_STOP=PASS (detached writer keeps stop unconfirmed; prior boot recovered; independent terminal preserved)')
    finally:
        if child_stamp is not None and process_stamp(child) == child_stamp:
            os.kill(child, signal.SIGTERM)
        tmux(root, 'kill-server', check=False)


if __name__ == '__main__':
    source = Path(__file__).resolve().parents[1] / 'wsl'
    with tempfile.TemporaryDirectory(prefix='ar-approval-stop-') as directory:
        exercise(Path(directory), source)

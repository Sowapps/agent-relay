"""Exercise exact terminal targeting and fixed keys with private tmux fixtures."""

import json
import os
from pathlib import Path
import pwd
import shutil
import subprocess
import sys
import tempfile
import time
import uuid


def wait_for(read):
    deadline = time.monotonic() + 3
    while not (value := read()):
        if time.monotonic() >= deadline:
            raise AssertionError('Timed out waiting for fixture')
        time.sleep(.05)
    return value


def exercise(base):
    source = Path(__file__).resolve().parents[1] / 'wsl'
    install = shutil.copytree(source, base / 'install')
    layout = install / 'native-layout.json'
    layout.write_text(json.dumps(json.loads(layout.read_text()) | {'base': str(base)}))
    sys.path.insert(0, str(install))
    from native_clients import process_stamp, tmux
    from approval_stop import pane, verify_dormant
    from native_errors import NativeFailure
    from terminal_control import read, signal

    session = str(uuid.uuid4())
    root = base / 'sessions' / session.replace('-', '')
    folder = root / 'codex'
    (folder / 'data').mkdir(parents=True)
    scope = {key: str(uuid.uuid4()) for key in ('projectId', 'senderId', 'recipientId', 'workspaceId')}
    (folder / 'context.json').write_text(json.dumps({'sessionId': session, **scope}))
    (root / 'provision-owner.json').write_text(json.dumps({
        'sessionId': session, 'user': pwd.getpwuid(os.getuid()).pw_name}))
    (root / 'tmux.conf').touch()
    agent = base / 'fake-client.py'
    agent.write_text(
        'import os,sys,tty\n'
        'from pathlib import Path\n'
        'tty.setraw(0)\n'
        'screen,seen=map(Path,sys.argv[1:3])\n'
        'def draw(): print("\\033[2J\\033[H"+screen.read_text().replace("\\n","\\r\\n"),end="",flush=True)\n'
        'draw()\n'
        'while key:=os.read(0,1):\n'
        ' if key==b"r": draw()\n'
        ' else:\n'
        '  with seen.open("ab") as stream: stream.write(key)\n'
    )
    screen, seen, other_keys = (base / name for name in ('screen.txt', 'seen', 'other'))

    def approve_screen(command):
        return ('Would you like to run the following command?\n\n'
                f'Reason: isolated fixture\n$ {command}\n\n'
                '› 1. Yes, proceed (y)\n'
                '  2. Yes, and do not ask again (p)\n'
                '  3. No, and tell Codex what to do differently (esc)\n\n'
                'Press enter to confirm or esc to cancel\n')

    def rejects(code, action):
        try:
            action()
        except NativeFailure as error:
            assert error.code == code, error
        else:
            raise AssertionError('Expected ' + code)

    def bridge(operation, **arguments):
        result = subprocess.run(
            [sys.executable, str(install / 'relay_bridge.py'), operation, str(root), 'codex'],
            input=json.dumps({'sessionId': session, **arguments}),
            capture_output=True, text=True, timeout=4, check=True)
        return json.loads(result.stdout)

    screen.write_text(approve_screen('fixture original'))
    try:
        tmux(root, 'new-session', '-d', '-s', 'codex', 'python3', str(agent), str(screen), str(seen))
        tmux(root, 'new-session', '-d', '-s', 'independent', 'python3', str(agent), str(screen), str(other_keys))
        revision = wait_for(lambda: read(root, 'codex', session)['approval'])['revision']
        rejects('capability_denied', lambda: read(root, 'codex', str(uuid.uuid4())))
        screen.write_text(approve_screen('fixture replacement'))
        tmux(root, 'send-keys', '-t', 'codex', 'r')
        wait_for(lambda: 'fixture replacement' in read(root, 'codex', session)['text'])
        rejects('terminal_approval_changed', lambda: signal(root, 'codex', session, 'approve', revision))
        assert not seen.exists()
        revision = bridge('read_terminal')['approval']['revision']
        assert bridge('signal_terminal', action='approve', approvalRevision=revision)['keySent']
        rejects('terminal_decision_recorded', lambda: signal(root, 'codex', session, 'approve', revision))
        assert wait_for(lambda: seen.exists() and seen.read_bytes()) == b'y'
        screen.write_text(approve_screen('fixture refuse'))
        tmux(root, 'send-keys', '-t', 'codex', 'r')
        wait_for(lambda: 'fixture refuse' in read(root, 'codex', session)['text'])
        revision = read(root, 'codex', session)['approval']['revision']
        assert bridge('signal_terminal', action='refuse', approvalRevision=revision)['keySent']
        wait_for(lambda: seen.read_bytes() == b'y\x1b')
        screen.write_text(approve_screen('fixture MCP').replace('Would you like to run the following command?',
            'Allow relay_project.get_connection_status?'))
        tmux(root, 'send-keys', '-t', 'codex', 'r')
        wait_for(lambda: 'fixture MCP' in read(root, 'codex', session)['text'])
        mcp = bridge('read_terminal')['approval']
        assert 'Allow relay_project.get_connection_status?' in mcp['text']
        assert bridge('signal_terminal', action='refuse', approvalRevision=mcp['revision'])['keySent']
        wait_for(lambda: seen.read_bytes() == b'y\x1b\x1b')
        def discovery_screen(detail):
            return ('relay_project needs your approval.\n'
                    f'Confirm relay_project.discover_operations ({detail})\n'
                    '› 1. Yes, provide the requested info (y)\n'
                    '  2. No, but continue without it (n)\n'
                    '  3. Cancel this request (esc)\n'
                    'Press enter to confirm or esc to cancel\n')
        for decision, expected in (('approve', b'y\x1b\x1by'), ('refuse', b'y\x1b\x1byn')):
            screen.write_text(discovery_screen(decision))
            tmux(root, 'send-keys', '-t', 'codex', 'r')
            wait_for(lambda: f'({decision})' in read(root, 'codex', session)['text'])
            snapshot = bridge('read_terminal')['approval']
            assert 'relay_project.discover_operations' in snapshot['text']
            assert bridge('signal_terminal', action=decision, approvalRevision=snapshot['revision'])['keySent']
            wait_for(lambda: seen.read_bytes() == expected)
        screen.write_text('Unknown modal.\nWould you like to run the following command?\nHistorical heading only.\n')
        tmux(root, 'send-keys', '-t', 'codex', 'r')
        wait_for(lambda: 'Unknown modal.' in read(root, 'codex', session)['text'])
        assert read(root, 'codex', session)['approval'] is None
        assert bridge('signal_terminal', action='escape')['keySent']
        wait_for(lambda: seen.read_bytes() == b'y\x1b\x1byn\x1b')
        assert not other_keys.exists()
        original = str(uuid.uuid4())
        history = folder / 'config' / 'sessions'
        history.mkdir(parents=True)
        (history / (original + '.jsonl')).touch()
        (folder / 'native-conversation.json').write_text(json.dumps({'id': original}))
        expired = str(uuid.uuid4())
        (folder / 'data' / f'dispatch-{expired}.json').write_text(json.dumps({'request': {
            'id': expired, 'scope': scope, 'expectedNativeGeneration': pane(root, 'codex')[1],
            'expectedDistributionGeneration': process_stamp(1)['generation']}}))
        proof_request = {'requestId': expired, 'scope': scope}
        assert verify_dormant(root, 'codex', proof_request)['reason'] == 'terminal_live'
        tmux(root, 'kill-session', '-t', 'codex')
        proof = verify_dormant(root, 'codex', proof_request)
        assert proof['dormant'], proof
        assert tmux(root, 'has-session', '-t', 'independent', check=False).returncode == 0
        print('NATIVE_TERMINAL_CONTROL=PASS (fresh one-time decision, stale/replay refusal, unknown prompt Escape, other pane untouched)')
        print('NATIVE_DORMANT_APPROVAL=PASS (original terminal absent, pinned conversation retained)')
    finally:
        tmux(root, 'kill-server', check=False)


if __name__ == '__main__':
    with tempfile.TemporaryDirectory(prefix='ar-terminal-control-') as directory:
        exercise(Path(directory))

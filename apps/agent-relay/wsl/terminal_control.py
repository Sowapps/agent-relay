"""Read the owned live viewport and send only the reviewed native choice or Escape."""

from hashlib import sha256
import re
import subprocess

from native_clients import owned_session_root, process_stamp, tmux
from native_errors import NativeFailure
from relay_files import context


MAX_VIEWPORT = 8000
CODEX_HEADING = re.compile(
    r'Would you like to run the following command\?'
    r'|Do you want to approve network access to ".+"\?'
)
CODEX_FOOTER = re.compile(r'Press enter to confirm or esc to cancel(?: or .+ to open thread)?', re.IGNORECASE)
CODEX_APPROVE = re.compile(r'\d+\. Yes, (?:proceed|just this once) \(y\)')
CODEX_REFUSE = re.compile(r'\d+\. No, (?:and tell Codex what to do differently|continue without running it) \((esc|n)\)')


def _owned(root, client, session_id):
    root, identifier = owned_session_root(root)
    binding = context(root, client)
    if identifier != session_id or not binding or binding.get('sessionId') != identifier:
        raise NativeFailure('capability_denied', 'The terminal is not bound to the selected relay session.', False)
    return root


def _target(root, client):
    result = tmux(root, 'list-panes', '-a', '-F', '#{session_name}\t#{pane_id} #{pane_pid} #{pane_dead}', check=False)
    matches = [line.partition('\t')[2] for line in result.stdout.splitlines() if line.partition('\t')[0] == client]
    if result.returncode or len(matches) != 1:
        raise NativeFailure('terminal_unavailable', 'The owned session does not have a single live terminal.', False)
    fields = matches[0].split()
    if len(fields) != 3 or not re.fullmatch(r'%\d+', fields[0]) or not fields[1].isdigit() or fields[2] != '0':
        raise NativeFailure('terminal_unavailable', 'The owned terminal is unavailable.', False)
    pane, pid, _ = fields
    stamp = process_stamp(int(pid))
    if stamp is None:
        raise NativeFailure('terminal_unavailable', 'The terminal process identity could not be verified.', False)
    return pane, pid, stamp['generation']


def _codex_approval(text):
    # Inspect the live footer and choices together. A heading in output or scrollback cannot select a key.
    lines = [line.strip().lstrip('›❯> ').rstrip() for line in text.splitlines() if line.strip()]
    if not lines or not CODEX_FOOTER.fullmatch(lines[-1]):
        return None
    heading = next((index for index in range(len(lines) - 2, -1, -1) if CODEX_HEADING.fullmatch(lines[index])), None)
    if heading is None:
        return None
    body = lines[heading:]
    yes = [line for line in body if CODEX_APPROVE.fullmatch(line)]
    no = [match for line in body if (match := CODEX_REFUSE.fullmatch(line))]
    if len(yes) != 1 or len(no) != 1 or body.index(yes[0]) >= body.index(no[0][0]):
        return None
    return '\n'.join(body), 'Escape' if no[0][1] == 'esc' else 'n'


def _viewport(root, client):
    target = _target(root, client)
    screen = tmux(root, 'capture-pane', '-p', '-t', target[0], check=False)
    if screen.returncode or _target(root, client) != target:
        raise NativeFailure('terminal_changed', 'The terminal changed during capture. Read it again.', False)
    text = ''.join(char for char in screen.stdout if char in '\n\t' or ord(char) >= 32 and not 127 <= ord(char) <= 159).rstrip()
    approval = _codex_approval(text) if client == 'codex' and len(text) <= MAX_VIEWPORT else None
    return target, text[-MAX_VIEWPORT:], approval


def _revision(target, text):
    return sha256(('\0'.join(target) + '\0' + text).encode('utf-8')).hexdigest()


def read(root, client, session_id):
    root = _owned(root, client, session_id)
    target, text, approval = _viewport(root, client)
    return {'client': client, 'text': text,
            'approval': {'revision': _revision(target, text), 'text': approval[0]} if approval else None}


def signal(root, client, session_id, action, revision):
    root = _owned(root, client, session_id)
    if action == 'escape' and revision is None:
        target, key = _target(root, client), 'Escape'
    elif action in ('approve', 'refuse') and isinstance(revision, str) and re.fullmatch('[0-9a-f]{64}', revision):
        target, text, approval = _viewport(root, client)
        if not approval or revision != _revision(target, text):
            raise NativeFailure('terminal_approval_changed', 'The reviewed approval is no longer visible. Read the terminal again.', False)
        key = 'y' if action == 'approve' else approval[1]
        # Reserve before sending so a lost acknowledgement cannot apply another key to the same frame.
        marker = root / client / 'data' / ('terminal-decision-' + revision)
        try:
            with marker.open('x', encoding='ascii') as stream:
                stream.write(action)
        except FileExistsError as error:
            raise NativeFailure('terminal_decision_recorded', 'A decision was already attempted. Inspect the terminal and request.', False) from error
    else:
        raise NativeFailure('invalid_request', 'Only Escape or a decision for a reviewed approval is accepted.', False)

    pane, pid, _ = target
    if _target(root, client) != target:
        raise NativeFailure('terminal_changed', 'The terminal process changed. No key was sent.', False)
    condition = '#{&&:#{==:#{pane_pid},' + pid + '},#{==:#{pane_dead},0}}'
    try:
        receipt = tmux(root, 'if-shell', '-F', '-t', pane, condition,
                       f'send-keys -t {pane} {key} ; display-message -p sent',
                       'display-message -p changed', check=False)
    except subprocess.TimeoutExpired as error:
        raise NativeFailure('terminal_signal_unconfirmed', 'Delivery could not be confirmed. Read the terminal before sending another key.') from error
    if receipt.stdout.strip() == 'changed':
        raise NativeFailure('terminal_changed', 'The terminal process changed. No key was sent.', False)
    if receipt.returncode or receipt.stdout.strip() != 'sent':
        raise NativeFailure('terminal_signal_unconfirmed', 'Delivery could not be confirmed. Read the terminal before sending another key.')
    return {'action': action, 'keySent': True}

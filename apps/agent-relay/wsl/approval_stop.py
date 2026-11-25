"""Retire only the proven native runtime of an abandoned retained approval."""

import hashlib
import json
import os
from pathlib import Path
import time
import uuid

from native_clients import _process_fields, generation_identity, owned_session_root, process_stamp, tmux
from native_errors import NativeFailure
from native_dispatch import NativeDispatch
from native_layout import owned_socket_absent
from relay_files import commit, context, find_reply


def refuse(message, effects_possible=False):
    return NativeFailure('cancellation_unconfirmed', message, effects_possible)


def stop_record(data, identifier):
    return data / f'approval-stop-{identifier}.json'


def stop_has_not_started(data, identifier):
    path = stop_record(data, identifier)
    if path.is_symlink():
        return False
    try:
        proof = json.loads(path.read_text(encoding='utf-8'))
    except FileNotFoundError:
        return True
    except (OSError, ValueError):
        return False
    return isinstance(proof, dict) and proof.get('requestId') == identifier and proof.get('stopStarted', False) is False


def owned_processes():
    found = {}
    for entry in Path('/proc').iterdir():
        if not entry.name.isdecimal():
            continue
        try:
            if entry.stat().st_uid != os.getuid():
                continue
            fields = _process_fields(entry.name)
        except (FileNotFoundError, ProcessLookupError):
            continue
        if fields[0] not in ('Z', 'X', 'x'):
            found[int(entry.name)] = (int(fields[1]), int(fields[3]), int(fields[19]))
    return found


def tracked(processes, leader, prior=None):
    live = {pid for pid, (_, sid, _) in processes.items() if sid == leader}
    for pid, ticks in (prior or {}).items():
        if int(pid) in processes and processes[int(pid)][2] == ticks:
            live.add(int(pid))
    while True:
        descendants = {pid for pid, (parent, _, _) in processes.items() if parent in live}
        if descendants <= live:
            return {str(pid): processes[pid][2] for pid in sorted(live)}
        live |= descendants


def pane(root, client):
    result = tmux(root, 'list-panes', '-a', '-F', '#{session_name}\t#{pane_pid} #{pane_dead} #{session_id}', check=False)
    if result.returncode:
        try:
            if owned_socket_absent(root / 'tmux.sock'):
                return None
        except OSError as error:
            raise refuse('The owned terminal socket could not be verified.') from error
        raise refuse('The owned terminal could not be inspected.')
    matches = [line.partition('\t')[2] for line in result.stdout.splitlines() if line.partition('\t')[0] == client]
    if not matches:
        return None
    if len(matches) != 1:
        raise refuse('The native session does not have a single owned terminal.')
    fields = matches[0].split()
    if len(fields) != 3 or not fields[0].isdecimal() or fields[1] not in ('0', '1') or not fields[2].startswith('$') or not fields[2][1:].isdecimal():
        raise refuse('The owned terminal identity could not be read.')
    leader = int(fields[0])
    if fields[1] == '1':
        return None
    stamp = process_stamp(leader)
    if stamp is None:
        raise refuse('The owned terminal process generation could not be verified.')
    return leader, stamp['generation'], fields[2]


def owned_dispatch(root, client, request, *, latest=False):
    root, session = owned_session_root(root)
    identifier = str(uuid.UUID(request['requestId']))
    binding = context(root, client)
    scope = request['scope']
    if not binding or binding.get('sessionId') != session or any(binding.get(key) != scope.get(key) for key in ('projectId', 'senderId', 'recipientId')):
        raise refuse('The retained approval does not belong to this native session.')
    data = root / client / 'data'
    dispatch = NativeDispatch(data, identifier).latest() if latest else NativeDispatch(data, identifier).marker(1)
    if dispatch.is_symlink() or not dispatch.is_file():
        raise refuse('No matching native dispatch intent is available for this approval.')
    size = dispatch.stat()
    if size.st_size > 1024 * 1024:
        raise refuse('The retained native dispatch exceeds its diagnostic limit.')
    raw = dispatch.read_bytes()
    submitted = json.loads(raw)['request']
    if submitted.get('id') != identifier or submitted.get('scope') != scope:
        raise refuse('The native dispatch belongs to a different request or scope.')
    return root, identifier, data, dispatch, size, raw, submitted


def verify_dormant(root, client, request):
    if client != 'codex':
        return {'dormant': False, 'reason': 'adapter_unsupported'}
    root, identifier, data, _, _, _, submitted = owned_dispatch(root, client, request, latest=True)
    if not stop_has_not_started(data, identifier):
        return {'dormant': False, 'reason': 'explicit_stop_started'}
    if pane(root, client) is not None:
        return {'dormant': False, 'reason': 'terminal_live'}
    from native_clients import demand_pinned_conversation
    demand_pinned_conversation(root / client, client, context(root, client))
    original = generation_identity(submitted.get('expectedNativeGeneration'))
    initial = generation_identity(submitted.get('expectedDistributionGeneration'))
    current = process_stamp(1)
    observed = generation_identity(current['generation']) if current else None
    if not original or not initial or not observed or original[0] != initial[0] or initial[1] != 1 or original[1] <= 1:
        return {'dormant': False, 'reason': 'generation_unverified'}
    if initial[0] == observed[0]:
        if initial != observed:
            return {'dormant': False, 'reason': 'distribution_changed'}
        if tracked(owned_processes(), original[1]):
            return {'dormant': False, 'reason': 'original_process_active'}
    return {'dormant': False, 'reason': 'terminal_reappeared'} if pane(root, client) is not None else {'dormant': True}


def recover(root, client, request, stop):
    root, identifier, data, dispatch, size, raw, submitted = owned_dispatch(root, client, request)
    dispatch_hash = hashlib.sha256(raw).hexdigest()
    if any(path != dispatch and path.stat().st_mtime_ns >= size.st_mtime_ns for path in data.glob('dispatch-*.json')):
        raise refuse('A later native dispatch may own the terminal. No stop was attempted.')
    saved = stop_record(data, identifier)
    if saved.is_symlink():
        raise refuse('The native stop proof is not an owned record.')
    current = pane(root, client)
    distro = process_stamp(1)
    if distro is None:
        raise refuse('The WSL distribution generation could not be verified.')
    distro = distro['generation']
    generation = submitted.get('expectedNativeGeneration')
    dispatched_distro = submitted.get('expectedDistributionGeneration')
    original = generation_identity(generation)
    initial = generation_identity(dispatched_distro)
    observed = generation_identity(distro)
    if not original or not initial or not observed or initial[1] != 1 or original[0] != initial[0] or original[1] <= 1:
        raise refuse('The retained dispatch has no verified native generation.')
    kernel_changed = initial[0] != observed[0]

    if saved.exists():
        proof = json.loads(saved.read_text(encoding='utf-8'))
        if (proof.get('requestId'), proof.get('dispatchHash'), proof.get('generation'), proof.get('distribution'), proof.get('leader')) != (
                identifier, dispatch_hash, generation, dispatched_distro, original[1]):
            raise refuse('The retained native stop proof belongs to a different dispatch.')
    else:
        if find_reply(root, client, identifier):
            raise NativeFailure('approval_already_resolved', 'A final native reply exists. Read the inbox before closing.', False)
        if stop:
            raise refuse('The native stop has no prepared proof.')
        leader = original[1]
        if current is None and (kernel_changed or dispatched_distro == distro):
            # The dispatch still identifies the session. An absent terminal supplies no prior process inventory.
            terminal, processes = None, {}
        elif current is not None and current[1] == generation and dispatched_distro == distro:
            active = owned_processes()
            if leader not in active or active[leader][1] != leader:
                raise refuse('The original process does not own an isolated terminal session.')
            terminal, processes = current[2], tracked(active, leader)
        else:
            raise refuse('The original runtime cannot be verified.')
        proof = {'requestId': identifier, 'dispatchHash': dispatch_hash, 'generation': generation,
                 'distribution': dispatched_distro, 'leader': leader, 'terminal': terminal, 'processes': processes}
        commit(saved, proof)

    if current is not None and current != (proof['leader'], generation, proof['terminal']):
        raise refuse('The terminal is now owned by another runtime. No stop was attempted.')
    if not proof.get('stopStarted') and find_reply(root, client, identifier):
        raise NativeFailure('approval_already_resolved', 'A final native reply exists. Read the inbox before closing.', False)
    if not stop:
        return {'requestId': identifier, 'prepared': True}
    if not kernel_changed and distro != dispatched_distro:
        raise refuse('The original WSL process namespace cannot be verified on the same kernel.', bool(proof.get('stopStarted')))
    leader = proof['leader']
    if current is not None:
        proof['processes'] = tracked(owned_processes(), leader, proof['processes'])
        proof['stopStarted'] = True
        commit(saved, proof)
        if pane(root, client) != current:
            raise refuse('The native generation changed at the stop boundary.', True)
        # Check the target again inside tmux, where the predicate and command share the same server queue.
        terminal = proof['terminal']
        tmux(root, 'if-shell', '-F', '-t', terminal, '#{==:#{pane_pid},' + str(leader) + '}', 'kill-session -t ' + terminal)

    deadline = time.monotonic() + 4
    while True:
        remaining = {} if kernel_changed else tracked(owned_processes(), leader, proof['processes'])
        terminal_closed = pane(root, client) is None
        if terminal_closed and not remaining:
            if not kernel_changed and (proof['terminal'] is None or submitted.get('allowLocalWork') is not False):
                # A detached writer can disappear from both the SID and ancestry before the first census.
                return {'requestId': identifier, 'terminalClosed': True, 'stopped': False}
            return {'requestId': identifier, 'stopped': True}
        if time.monotonic() >= deadline:
            if terminal_closed:
                return {'requestId': identifier, 'terminalClosed': True, 'stopped': False}
            raise refuse('Processes of the owned native session remain. The session stays unavailable.',
                         bool(proof.get('stopStarted')))
        time.sleep(.05)

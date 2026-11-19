"""Filesystem side of named-session adapter maintenance.

The caller owns admission and keeps the captured before-image before submitting
a replace request. A replace accepts a mix of before/after images after interruption.
"""

import base64
from contextlib import contextmanager
import fcntl
import json
import os
from pathlib import Path
import re
import socket as unix_socket
import stat
import subprocess
import sys
import tempfile
import uuid


def owned_install(request):
    session = request['session']
    root = Path(session['root'])
    namespace = {}
    # The caller supplies this packaged layout facade, never session-controlled contents.
    exec(base64.b64decode(request['layoutSource'], validate=True), namespace)
    layout = namespace['layout_from_payload']({'native-layout.json': request['layoutDefinition']})
    if not layout.is_session(root, session['id']) or root.resolve(strict=True) != root or root.stat().st_uid != os.getuid():
        raise PermissionError('Native root identity or ownership differs from the selected session')
    owner = root / 'provision-owner.json'
    owner_stat = owner.lstat()
    if not stat.S_ISREG(owner_stat.st_mode) or owner_stat.st_uid != os.getuid() or owner_stat.st_nlink != 1:
        raise PermissionError('Native session ownership marker is not a private regular file')
    if json.loads(owner.read_text(encoding='utf-8')) != {'sessionId': session['id'], 'user': session['template']['user']}:
        raise PermissionError('Native root marker does not match the selected session')
    install = root / 'install'
    if install.resolve(strict=True) != install or not install.is_dir() or install.stat().st_uid != os.getuid():
        raise PermissionError('Native helper directory is not owned by the selected session')
    return install


@contextmanager
def exclusive(install):
    lock = install.parent / '.adapter-maintenance.lock'
    descriptor = os.open(lock, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_NONBLOCK, 0o600)
    try:
        attributes = os.fstat(descriptor)
        if not stat.S_ISREG(attributes.st_mode) or attributes.st_uid != os.getuid() or attributes.st_nlink != 1:
            raise PermissionError('Native helper maintenance lock is not private')
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield
    finally:
        os.close(descriptor)


class NativeSessionActive(RuntimeError):
    pass


def demand_stopped(install):
    socket = install.parent / 'tmux.sock'
    if not socket.exists() and not socket.is_symlink():
        return
    attributes = socket.lstat()
    if not stat.S_ISSOCK(attributes.st_mode) or attributes.st_uid != os.getuid():
        raise PermissionError('Owned native runtime socket cannot be verified')
    result = subprocess.run(['tmux', '-S', str(socket), 'list-panes', '-a', '-F', '#{pane_dead}'],
                            capture_output=True, text=True, encoding='utf-8', timeout=5)
    if result.returncode:
        with unix_socket.socket(unix_socket.AF_UNIX, unix_socket.SOCK_STREAM) as probe:
            probe.settimeout(2)
            try:
                probe.connect(str(socket))
            except (ConnectionRefusedError, FileNotFoundError):
                return  # The socket survived its server, or was cleaned up while interrogated.
        raise RuntimeError('Owned native runtime is reachable but could not be inspected')
    if any(line != '1' for line in result.stdout.splitlines()):
        raise NativeSessionActive('The owned native session is still running')


def observe(install, names):
    observed = {}
    for name in names:
        if not re.fullmatch(r'[a-zA-Z0-9_]+\.py|native-layout\.json', name):
            raise ValueError('Unrecognized native helper name')
        file = install / name
        try:
            attributes = file.lstat()
        except FileNotFoundError:
            observed[name] = None
            continue
        if not stat.S_ISREG(attributes.st_mode) or attributes.st_uid != os.getuid() or attributes.st_nlink != 1 or attributes.st_size > 1024 * 1024:
            raise PermissionError('Native helper is not a bounded owned regular file: ' + name)
        observed[name] = base64.b64encode(file.read_bytes()).decode('ascii')
    return observed


def replace(install, observed, desired):
    # Decode the entire desired set before the first write. File replacement is atomic per helper.
    payload = {name: base64.b64decode(value, validate=True) if value is not None else None for name, value in desired.items()}
    directory = os.open(install, os.O_DIRECTORY)
    try:
        for name, value in payload.items():
            if observed[name] == desired[name]:
                continue
            target = install / name
            if value is None:
                target.unlink()
            else:
                mode = stat.S_IMODE(target.stat().st_mode) if observed[name] is not None else 0o644
                descriptor, temporary = tempfile.mkstemp(prefix='.adapter-', dir=install)
                try:
                    with os.fdopen(descriptor, 'wb') as stream:
                        os.fchmod(stream.fileno(), mode & 0o777)
                        stream.write(value)
                        stream.flush()
                        os.fsync(stream.fileno())
                    os.replace(temporary, target)
                finally:
                    if os.path.exists(temporary):
                        os.unlink(temporary)
            os.fsync(directory)
    finally:
        os.close(directory)


def historical_approval(install, request):
    demand_stopped(install)
    identifier = str(uuid.UUID(request['requestId']))
    client = request['client']
    if client not in ('codex', 'claude'):
        raise ValueError('Unsupported approval client')
    folder = install.parent / client
    scope = request['scope']
    for path in (folder, folder / 'data', folder / 'context.json', folder / 'data' / f'dispatch-{identifier}.json'):
        if path.resolve(strict=True) != path or path.stat().st_uid != os.getuid():
            raise PermissionError('Retained approval path is not owned')
    binding = json.loads((folder / 'context.json').read_text(encoding='utf-8'))
    marker = folder / 'data' / f'dispatch-{identifier}.json'
    if not marker.is_file() or marker.stat().st_size > 1024 * 1024:
        raise ValueError('Retained dispatch marker is invalid')
    dispatch = json.loads(marker.read_text(encoding='utf-8'))['request']
    keys = ('projectId', 'senderId', 'recipientId')
    scoped = binding.get('sessionId') == request['session']['id'] and all(binding.get(k) == scope[k] for k in keys)
    retained_scope = dispatch.get('scope')
    if retained_scope is not None:
        scoped = scoped and all(retained_scope.get(k) == scope[k] for k in (*keys, 'workspaceId'))
    boot = next(int(line.split()[1]) for line in Path('/proc/stat').read_text().splitlines() if line.startswith('btime '))
    current_boot = Path('/proc/sys/kernel/random/boot_id').read_text().strip()
    generation = dispatch.get('expectedNativeGeneration')
    ended = max(marker.stat().st_mtime, request['updated']) < boot and (
        generation is None or str(uuid.UUID(generation.split(':', 1)[0])) != current_boot)
    return {'requestId': identifier, 'historical': scoped and str(uuid.UUID(dispatch['id'])) == identifier and ended}


def maintain(request):
    install = owned_install(request)
    action = request['action']
    if action == 'check-idle':
        try:
            demand_stopped(install)
        except NativeSessionActive:
            return {'sessionId': request['session']['id'], 'idle': False}
        return {'sessionId': request['session']['id'], 'idle': True}
    if action == 'verify-dormant':
        client = request['client']
        if client not in ('codex', 'claude'):
            raise ValueError('Unsupported session client')
        binding_path = install.parent / client / 'context.json'
        if binding_path.resolve(strict=True) != binding_path or binding_path.stat().st_uid != os.getuid():
            raise PermissionError('The native binding is not owned by the selected account')
        binding = json.loads(binding_path.read_text(encoding='utf-8'))
        scope = request['scope']
        if binding.get('sessionId') != request['session']['id'] or any(
                binding.get(key) != scope.get(key) for key in ('projectId', 'senderId', 'recipientId')):
            raise PermissionError('The retained native session binding differs')
        demand_stopped(install)
        return {'sessionId': request['session']['id'], 'dormant': True}
    if action == 'approval-history':
        return historical_approval(install, request)
    names = request['names']
    if not names or len(names) > 64 or len(set(names)) != len(names):
        raise ValueError('Expected a unique, bounded native helper set')
    with exclusive(install):
        if action != 'refresh':
            demand_stopped(install)
        before = observe(install, names)
        if action == 'capture':
            return {'sessionId': request['session']['id'], 'files': before}
        after = request['files']
        if action not in ('replace', 'refresh') or set(names) != set(after):
            raise ValueError('Native helper action or file set is invalid')
        if action == 'replace':
            expected = request['expected']
            if set(names) != set(expected) or any(before[name] not in (expected[name], after[name]) for name in names):
                raise RuntimeError('Native helpers changed outside the retained maintenance intent')
        replace(install, before, after)
        if observe(install, names) != after:
            raise RuntimeError('Native helper publication could not be verified')
        return {'sessionId': request['session']['id'], 'files': len(after), 'modelPrompts': 0}


if __name__ == '__main__':
    print(json.dumps(maintain(json.load(sys.stdin))))

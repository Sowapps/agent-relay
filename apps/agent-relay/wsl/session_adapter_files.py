"""Filesystem side of named-session adapter maintenance.

The caller owns admission and keeps the captured before-image before submitting
a replace request. A replace accepts a mix of before/after images after interruption.
"""

import base64
from contextlib import contextmanager
import ctypes
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
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
    return install, namespace["owned_socket_absent"]


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


def demand_stopped(install, socket_absent):
    socket = install.parent / 'tmux.sock'
    if not socket.exists() and not socket.is_symlink():
        return
    attributes = socket.lstat()
    if not stat.S_ISSOCK(attributes.st_mode) or attributes.st_uid != os.getuid():
        raise PermissionError('Owned native runtime socket cannot be verified')
    result = subprocess.run(['tmux', '-S', str(socket), 'list-panes', '-a', '-F', '#{pane_dead}'],
                            capture_output=True, text=True, encoding='utf-8', timeout=5)
    if result.returncode:
        if socket_absent(socket):
            return
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


def sync_directory(path):
    descriptor = os.open(path, os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def publish_live(install, observed, desired):
    if observed == desired:
        return
    # Keep the public directory present throughout. The retained before-image is
    # outside WSL, so a resumed transaction can verify either complete generation.
    with tempfile.TemporaryDirectory(prefix='.adapter-stage-', dir=install.parent) as staging:
        candidate = Path(staging) / 'install'
        shutil.copytree(install, candidate, symlinks=True, ignore=shutil.ignore_patterns('__pycache__'))
        replace(candidate, observed, desired)
        for directory, _, files in os.walk(candidate, topdown=False):
            for name in files:
                file = Path(directory) / name
                if stat.S_ISREG(file.lstat().st_mode):
                    with file.open('rb') as stream:
                        os.fsync(stream.fileno())
            sync_directory(directory)
        sync_directory(staging)
        exchange = ctypes.CDLL(None, use_errno=True).renameat2
        exchange.argtypes = (ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint)
        if exchange(-100, os.fsencode(install), -100, os.fsencode(candidate), 2) != 0:  # AT_FDCWD, RENAME_EXCHANGE
            error = ctypes.get_errno()
            raise OSError(error, os.strerror(error))
        sync_directory(staging)
        sync_directory(install.parent)


def historical_approval(install, request, socket_absent):
    demand_stopped(install, socket_absent)
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
    raw = marker.read_bytes()
    dispatch = json.loads(raw)['request']
    keys = ('projectId', 'senderId', 'recipientId')
    scoped = binding.get('sessionId') == request['session']['id'] and all(binding.get(k) == scope[k] for k in keys)
    retained_scope = dispatch.get('scope')
    if retained_scope is not None:
        scoped = scoped and all(retained_scope.get(k) == scope[k] for k in (*keys, 'workspaceId'))
    boot = next(int(line.split()[1]) for line in Path('/proc/stat').read_text().splitlines() if line.startswith('btime '))
    current_boot = Path('/proc/sys/kernel/random/boot_id').read_text().strip()
    generation = dispatch.get('expectedNativeGeneration')
    matched = scoped and str(uuid.UUID(dispatch['id'])) == identifier
    if request.get('retiredStop'):
        return {'requestId': identifier, 'historical': matched and retained_scope is not None and
                parked_approval_stop(folder / 'data', marker, identifier, raw, dispatch, current_boot)}
    ended = max(marker.stat().st_mtime, request['updated']) < boot and (
        generation is None or str(uuid.UUID(generation.split(':', 1)[0])) != current_boot)
    return {'requestId': identifier, 'historical': matched and ended}


def parked_approval_stop(data, marker, identifier, raw, dispatch, current_boot):
    """A retired terminal cannot take more input. Keep uncertainty about earlier effects on the work record."""
    proof_path = data / f'approval-stop-{identifier}.json'
    attributes = proof_path.lstat()
    if not stat.S_ISREG(attributes.st_mode) or attributes.st_uid != os.getuid() or attributes.st_nlink != 1 or attributes.st_size > 1024 * 1024:
        return False
    if any(path != marker and path.stat().st_mtime_ns >= marker.stat().st_mtime_ns for path in data.glob('dispatch-*.json')):
        return False
    proof = json.loads(proof_path.read_bytes())
    generation = dispatch['expectedNativeGeneration']
    distribution = dispatch['expectedDistributionGeneration']
    boot_id, leader, started = generation.split(':')
    distro_boot, distro_pid, distro_started = distribution.split(':')
    if (proof.get('requestId'), proof.get('dispatchHash'), proof.get('generation'), proof.get('distribution')) != (
            identifier, hashlib.sha256(raw).hexdigest(), generation, distribution):
        return False
    if (proof.get('stopStarted') is not True or str(uuid.UUID(boot_id)) != distro_boot or int(distro_pid) != 1 or
            int(started) <= 0 or int(distro_started) <= 0 or int(leader) <= 1 or proof.get('leader') != int(leader)):
        return False
    processes = proof.get('processes')
    if not isinstance(processes, dict) or any(not pid.isdecimal() or int(pid) <= 1 or type(tick) is not int or tick <= 0
                                               for pid, tick in processes.items()):
        return False
    if boot_id != current_boot:
        return True
    for entry in Path('/proc').iterdir():
        if not entry.name.isdecimal():
            continue
        try:
            if entry.stat().st_uid != os.getuid():
                continue
            fields = (entry / 'stat').read_text().rsplit(')', 1)[1].split()
        except (FileNotFoundError, ProcessLookupError):
            continue
        if fields[0] not in ('Z', 'X', 'x') and (fields[3] == leader or
                processes.get(entry.name) == int(fields[19]) or (entry.name == leader and fields[19] == started)):
            return False
    return True


def maintain(request):
    install, socket_absent = owned_install(request)
    action = request['action']
    if action == 'check-idle':
        try:
            demand_stopped(install, socket_absent)
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
        demand_stopped(install, socket_absent)
        return {'sessionId': request['session']['id'], 'dormant': True}
    if action == 'approval-history':
        return historical_approval(install, request, socket_absent)
    names = request['names']
    if not names or len(names) > 64 or len(set(names)) != len(names):
        raise ValueError('Expected a unique, bounded native helper set')
    with exclusive(install):
        live = request.get('preserveLiveSessions') is True and action in ('capture', 'replace')
        if action != 'refresh' and not live:
            demand_stopped(install, socket_absent)
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
        (publish_live if live else replace)(install, before, after)
        if observe(install, names) != after:
            raise RuntimeError('Native helper publication could not be verified')
        return {'sessionId': request['session']['id'], 'files': len(after), 'modelPrompts': 0}


if __name__ == '__main__':
    print(json.dumps(maintain(json.load(sys.stdin))))

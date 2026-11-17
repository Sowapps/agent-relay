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
        raise RuntimeError('Stop the owned native session or verify its socket before replacing adapters')


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


def maintain(request):
    install = owned_install(request)
    action = request['action']
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

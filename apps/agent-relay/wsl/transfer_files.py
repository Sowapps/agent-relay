"""Single-file transfers inside a relay-created session workspace; never exposed as agent MCP tools."""
import hashlib
import fcntl
import os
from pathlib import Path, PurePosixPath
import uuid
from relay_files import commit, workspace as assigned_workspace

MAXIMUM_BYTES = 256 * 1024 * 1024


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()


def paths(root, client, request, create=False):
    if not root.name.startswith('agent-relay-session-'):
        raise PermissionError('File transfers require a dedicated named session')
    workspace = assigned_workspace(root, client)
    if workspace.is_symlink() or workspace.resolve() != workspace:
        raise PermissionError('Workspace cannot be a symbolic link')
    relative = PurePosixPath(request['linuxPath'])
    if relative.is_absolute() or not relative.parts or any(part in ['.', '..'] for part in relative.parts) or '\\' in str(relative):
        raise ValueError('Expected a relative Linux workspace path')
    current = workspace
    for part in relative.parts:
        current = current / part
        if current.is_symlink():
            raise PermissionError('Transfer paths cannot contain symbolic links')
    target = workspace / relative
    if not target.resolve().is_relative_to(workspace):
        raise PermissionError('Path escaped the workspace')
    if create:
        target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_name('.relay-transfer-' + str(uuid.UUID(request['id'])) + '.part')
    if temporary.is_symlink():
        raise PermissionError('Temporary file cannot be a symbolic link')
    return workspace, target, temporary


def transfer(root, client, action, request):
    lock = root / client / 'data' / ('transfer-' + str(uuid.UUID(request['id'])) + '.lock')
    with lock.open('a') as stream:
        try: fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError: raise RuntimeError('busy: transfer helper is still active; do not clean its partial')
        return transfer_locked(root, client, action, request)


def transfer_locked(root, client, action, request):
    maximum=request.get('maximumBytes',MAXIMUM_BYTES)
    if not isinstance(maximum,int) or not 1<=maximum<=4*1024*1024*1024:raise ValueError('Invalid configured transfer limit')
    upload = request['direction'] == 'upload'
    if request['direction'] not in ['upload', 'download']:
        raise ValueError('Unsupported transfer direction')
    workspace, target, temporary = paths(root, client, request, create=upload and action == 'transfer_inspect')
    if action == 'transfer_partial':
        exists = temporary.is_file(); size = temporary.stat().st_size if exists else 0
        if request.get('remove') and exists: temporary.unlink()
        return {'bytes': size, 'exists': exists, 'removed': bool(request.get('remove') and exists)}
    if action == 'transfer_inspect':
        if upload:
            if target.exists() and not request.get('overwrite', False):
                raise FileExistsError('destination_exists: Destination exists; overwrite must be explicit')
            if temporary.exists():
                raise FileExistsError('Uncertain partial transfer exists; it is not automatically replayed')
            if target.exists() and not target.is_file():
                raise ValueError('Destination must be a file')
            return {'workspace': str(workspace), 'path': str(target), 'temporary': str(temporary)}
        if not target.is_file() or target.stat().st_size > maximum:
            raise ValueError('Expected a file within the configured transfer limit')
        return {'workspace': str(workspace), 'path': str(target), 'bytes': target.stat().st_size, 'sha256': digest(target)}
    if action != 'transfer_commit' or not upload:
        raise ValueError('Unsupported transfer action')
    if not temporary.is_file() or temporary.stat().st_size != request['bytes'] or request['bytes'] > maximum or digest(temporary) != request['sha256']:
        raise ValueError('Transferred file size or SHA-256 does not match')
    with temporary.open('rb') as stream:
        os.fsync(stream.fileno())
    if request.get('overwrite', False):
        os.replace(temporary, target)
    else:
        os.link(temporary, target)  # Atomic publication that refuses an existing target.
        temporary.unlink()
    descriptor = os.open(target.parent, os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)
    receipt = {'id': request['id'], 'path': str(target), 'workspace': str(workspace), 'bytes': request['bytes'], 'sha256': request['sha256']}
    commit(root / client / 'data' / ('transfer-' + request['id'] + '.json'), receipt)
    return receipt

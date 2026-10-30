"""JSON process bridge for the configured dedicated WSL relay root."""
import json
from pathlib import Path
import subprocess
import sys
import uuid
from relay_files import commit, messages, root_path, context, message_page, workspace
import native_clients
from transfer_files import transfer


def main():
    action, value, profile = sys.argv[1:4]
    root = root_path(value)
    if profile not in ['command', 'codex', 'claude']:
        raise ValueError('Unknown dedicated profile')
    request = json.load(sys.stdin)
    if action in ['transfer_inspect', 'transfer_commit', 'transfer_partial']:
        return transfer(root, profile, action, request)
    if action == 'status':
        return {'available': True} if profile == 'command' else native_clients.status(root, profile)
    if action == 'start' and profile != 'command':
        return native_clients.start(root, profile, request.get('allowLocalWork', False), request.get('resume', False))
    if action in ['stop', 'interrupt'] and profile != 'command':
        return native_clients.control(root, profile, action == 'stop')
    if action == 'inbox':
        if profile=='command':return {'messages':[]}
        return message_page(root,profile,request.get('after',0),request.get('limit',50)) if context(root,profile) else {'messages':messages(root,profile)}
    if action == 'recover':
        identifier = str(uuid.UUID(request['id']))
        if profile == 'command':
            result = root / 'command/data' / (identifier + '.json')
            return {'receipt': json.loads(result.read_text()) if result.exists() else None, 'recoveryNonce': request['nonce']}
        received = messages(root, profile)
        return {'messages': received, 'reply': next((item for item in received if item.get('inReplyTo') == identifier), None)}
    if action == 'message' and profile != 'command':
        request['id'] = str(uuid.UUID(request['id']))
        return native_clients.send(root, profile, request)
    if action == 'command':
        if profile != 'command':
            raise PermissionError('Command execution is not available on agent-message profiles')
        identifier = str(uuid.UUID(request['id']))
        rule = request['command']
        if not isinstance(rule.get('program'), str) or not rule['program'].startswith('/') or not isinstance(rule.get('arguments'), list):
            raise PermissionError('command_denied: expected configured executable and argument array')
        result = root / 'command/data' / (identifier + '.json')
        marker = result.with_suffix('.started')
        if result.exists():
            return json.loads(result.read_text())
        with marker.open('x') as stream:
            stream.write(identifier)
            stream.flush()
        working = Path(request['workspace']) if request.get('workspace') else workspace(root, 'command')
        if not working.is_absolute() or working.resolve()!=working or not working.is_dir():raise PermissionError('Configured command workspace is invalid')
        process = subprocess.run([rule['program'], *rule['arguments']], cwd=working, capture_output=True, timeout=10)
        response = {'requestId': identifier, 'nonce': request['nonce'], 'workspace': str(working), 'user': subprocess.check_output(['id', '-un'], text=True).strip(),
                    'stdout': process.stdout[:65536].decode('utf-8', 'replace'), 'stderr': process.stderr[:16384].decode('utf-8', 'replace'), 'exitCode': process.returncode,
                    'commandId': request['commandId'], 'program': rule['program'], 'arguments': rule['arguments']}
        commit(result, response)
        return response
    raise PermissionError('Unsupported bridge operation')


if __name__ == '__main__':
    try:
        print(json.dumps(main(), ensure_ascii=False))
    except Exception as error:
        print(json.dumps({'error': str(error), 'type': type(error).__name__}))
        raise SystemExit(1)

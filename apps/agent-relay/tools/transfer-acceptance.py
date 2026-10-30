"""Real Windows/WSL single-file round trip through MCP in a dedicated model-free session."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import time
import uuid
from relay_mcp import RelayMcp

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--runtime', type=Path, required=True)
parser.add_argument('--data', type=Path, required=True)
parser.add_argument('--templates', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
data = args.data.resolve()
assert data.is_relative_to(root / 'local/apps/agent-relay')
data.mkdir(parents=True, exist_ok=True)
configuration = json.loads(args.templates.read_text(encoding='utf-8-sig'))
template = next(profile for profile in configuration['profiles'] if profile['client'] == 'codex')
template['allowLocalWork'] = True  # Only this newly owned qualification data, never operator profile permissions.
configuration = {'version': 1, 'profiles': [template]}
target = data / 'relay-config.json'
if target.exists():
    assert json.loads(target.read_text()) == configuration
else:
    target.write_text(json.dumps(configuration), encoding='utf-8')
runtime = json.loads(args.runtime.read_text(encoding='utf-8-sig'))
manifest = data / 'transfer-acceptance.json'
assert not manifest.exists(), 'Qualification is once-only; inspect previous receipt rather than silently rerun'
result = {'sessionId': str(uuid.uuid4()), 'modelPrompts': 0, 'passed': False}
manifest.write_text(json.dumps(result), encoding='utf-8')


def finish(mcp, request):
    deadline = time.monotonic() + 40
    while time.monotonic() < deadline:
        record = mcp.tool('get_result', {'request_id': request})['work']
        if record['state'] in ['completed', 'failed', 'unknown']:
            return record
        time.sleep(.1)
    raise TimeoutError(record)


with (data / 'transfer.stderr.log').open('w', encoding='utf-8') as errors:
    host = subprocess.Popen([runtime['dotnet'], runtime['assembly'], 'tray', '--data-dir', str(data)],
                            stdout=subprocess.DEVNULL, stderr=errors, creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        time.sleep(.7)
        assert host.poll() is None
        with RelayMcp(runtime, data, root, errors) as mcp:
            opened = mcp.tool('open_session', {'session_id': result['sessionId'], 'profile_id': template['id'], 'name': 'File round trip qualification'})
            session = opened['session']['session']
            assert session['state'] == 'created', opened
            workspace = Path(session['windowsWorkspace'])
            payload = ('Windows ↔ WSL — été\n'.encode('utf-8') + bytes(range(256))) * 4096
            source = workspace / 'source.bin'; source.write_bytes(payload)
            upload = {'session_id': session['id'], 'direction': 'upload', 'windows_path': source.name, 'linux_path': 'nested/round-trip.bin', 'request_id': str(uuid.uuid4())}
            mcp.tool('transfer_file', upload)
            record = finish(mcp, upload['request_id']); result['upload'] = record
            assert record['state'] == 'completed', record
            assert record['result']['details']['sha256'] == hashlib.sha256(payload).hexdigest()
            assert mcp.tool('transfer_file', upload)['work'] == record, 'Duplicate transfer changed its retained outcome'
            download = dict(upload, direction='download', windows_path='downloaded.bin', request_id=str(uuid.uuid4()))
            mcp.tool('transfer_file', download)
            record = finish(mcp, download['request_id']); result['download'] = record
            assert record['state'] == 'completed' and (workspace / 'downloaded.bin').read_bytes() == payload, record
            collision = dict(upload, request_id=str(uuid.uuid4()))
            mcp.tool('transfer_file', collision)
            record = finish(mcp, collision['request_id']); result['collision'] = record
            assert record['state'] == 'failed' and record['errorCode'] == 'destination_exists', record
            escape = dict(upload, windows_path='../escape.bin', request_id=str(uuid.uuid4()))
            mcp.tool('transfer_file', escape)
            record = finish(mcp, escape['request_id']); result['escape'] = record
            assert record['state'] == 'failed' and record['errorCode'] == 'invalid_path', record
            result['closed'] = mcp.tool('close_session', {'session_id': session['id']})
            result['passed'] = True
    except BaseException as error:
        result['error'] = str(error)
        raise
    finally:
        manifest.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
        if host.poll() is None:
            host.terminate()
        host.wait(timeout=10)
print(json.dumps({'passed': result['passed'], 'report': str(manifest), 'modelPrompts': 0}))

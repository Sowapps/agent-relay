"""Model-free I/O acceptance against an already running owned V1 qualification host."""
import argparse
from contextlib import ExitStack
import hashlib
import json
from pathlib import Path
import shutil
import time
import uuid
from relay_mcp import RelayMcp

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--data', type=Path, required=True)
parser.add_argument('--large-mib', type=int, default=256)
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]; data = args.data.resolve()
assert data.is_relative_to(root / 'local/apps/agent-relay') and 1 <= args.large_mib <= 256
state = json.loads((data / 'v1-acceptance.json').read_text(encoding='utf-8-sig')); ids = state['ids']
runtime = json.loads((data / 'mcp-runtime.json').read_text(encoding='utf-8-sig'))
runtime['connectionId'] = ids['connection-a']
local = Path(next(item['location'] for item in state['configuration']['workspaces'] if item['id'] == ids['work-main-a']))
assert local.is_relative_to(root / 'local/apps/agent-relay/v1-workspaces')
assert shutil.disk_usage(local).free > (args.large_mib * 3 + 1024) * 1024 * 1024
case = 'io-' + uuid.uuid4().hex; (local / case).mkdir()
result = {'case': case, 'modelPrompts': 0, 'operations': [], 'passed': False}
report = data / (case + '.json')


def save(): report.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')


with ExitStack() as stack:
    errors = stack.enter_context((data / (case + '.stderr.log')).open('w', encoding='utf-8'))
    client = stack.enter_context(RelayMcp(runtime, data, root, errors))

    def copy(source, destination, expected='completed'):
        identifier = str(uuid.uuid4()); record = {'requestId': identifier}; result['operations'].append(record); save()
        started = time.monotonic()
        client.tool('transfer_file', {'request_id': identifier, 'session_id': state['sessions']['codex-a'], 'source': source, 'destination': destination})
        deadline = time.monotonic() + 190
        while time.monotonic() < deadline:
            operation = client.tool('get_result', {'request_id': identifier})['operation']
            if operation['state'] not in ['pending', 'running']: break
            time.sleep(.2)
        record.update(seconds=round(time.monotonic() - started, 3), operation=operation); save()
        assert operation['state'] == expected, operation
        return operation

    def location(remote, name):
        return {'endpoint_id': ids['codex-a' if remote else 'main-a'], 'workspace_id': ids['work-codex-a' if remote else 'work-main-a'], 'path': case + '/' + name}

    try:
        result['host'] = client.tool('inspect_host', {'project_id': ids['project-a'], 'endpoint_id': ids['command-a']})['host']
        assert result['host']['facts']['effectiveUser'] == 'sowapps'
        size = args.large_mib * 1024 * 1024; block = bytes(range(256)) * 4096
        path = local / case / 'large.bin'; digest = hashlib.sha256()
        with path.open('xb') as stream:
            for _ in range(args.large_mib): stream.write(block); digest.update(block)
        upload = copy(location(False, 'large.bin'), location(True, 'large.bin'))
        assert upload['result']['details']['sha256'] == digest.hexdigest()
        download = copy(location(True, 'large.bin'), location(False, 'returned.bin'))
        with (local / case / 'returned.bin').open('rb') as stream: assert hashlib.file_digest(stream, 'sha256').hexdigest() == digest.hexdigest()
        result['largeBytes'] = size
        copy(location(False, 'large.bin'), location(True, 'large.bin'), 'failed')
        with (local / case / 'oversized.bin').open('xb') as stream: stream.truncate(256 * 1024 * 1024 + 1)
        refused = copy(location(False, 'oversized.bin'), location(True, 'oversized.bin'), 'failed')
        assert refused['errorCode'] == 'file_too_large'
        started = time.monotonic()
        for index in range(8):
            name = 'small-' + str(index) + '.txt'; (local / case / name).write_text('été\n' * 4096, encoding='utf-8')
            copy(location(False, name), location(True, name))
        result['smallFiles'] = {'count': 8, 'seconds': round(time.monotonic() - started, 3)}
        result['passed'] = True
    finally: save()
print(json.dumps({'report': str(report), 'passed': result['passed'], 'modelPrompts': 0}))

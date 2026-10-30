"""Inspect and clean controlled owned partials through real scoped MCP, without models."""
import argparse
import json
from pathlib import Path
from relay_mcp import RelayMcp

parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--data', type=Path, required=True); args = parser.parse_args()
root = Path(__file__).resolve().parents[3]; data = args.data.resolve(); assert data.is_relative_to(root / 'local/apps/agent-relay')
state = json.loads((data / 'v1-acceptance.json').read_text(encoding='utf-8-sig')); ids = state['ids']
io = json.loads(sorted(data.glob('io-*.json'), key=lambda path: path.stat().st_mtime)[-1].read_text(encoding='utf-8'))
assert io['passed']; runtime = json.loads((data / 'mcp-runtime.json').read_text(encoding='utf-8-sig')); runtime['connectionId'] = ids['connection-a']
workspaces = {item['id']: item for item in state['configuration']['workspaces']}
result = {'modelPrompts': 0, 'passed': False, 'checks': []}
with (data / 'partial-maintenance.stderr.log').open('w', encoding='utf-8') as errors, RelayMcp(runtime, data, root, errors) as client:
    try:
        for index in [1, 3]:
            operation = io['operations'][index]['operation']; request = operation['request']; destination = request['artifact']['destination']
            workspace = workspaces[destination['workspaceId']]
            base = Path(workspace['location']) if index == 1 else Path('\\\\wsl.localhost\\Ubuntu-24.04' + workspace['location'].replace('/', '\\'))
            target = base / destination['relativePath']; partial = target.with_name('.relay-transfer-' + request['id'] + '.part')
            assert not partial.exists()
            before = target.stat().st_size if target.exists() else None
            with partial.open('xb') as stream: stream.write(b'partial')
            arguments = {'request_id': request['id']}
            inspected = client.tool('inspect_transfer', arguments)['partial']; assert inspected['exists'] and inspected['bytes'] == 7 and not inspected['removed']
            cleaned = client.tool('cleanup_transfer', arguments)['partial']; assert cleaned['removed'] and not partial.exists()
            assert (target.stat().st_size if target.exists() else None) == before
            assert not client.tool('cleanup_transfer', arguments)['partial']['removed']
            result['checks'].append({'requestId': request['id'], 'direction': 'download' if index == 1 else 'upload', 'finalPreserved': True})
        result['passed'] = True
    finally: (data / 'partial-maintenance.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps({'passed': result['passed'], 'modelPrompts': 0}))

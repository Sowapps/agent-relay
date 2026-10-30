"""Read only the dedicated clients' native records; do not invoke a model."""
import json
import sys
from relay_files import root_path

root = root_path(sys.argv[1])
result = {}
for client in ['codex', 'claude']:
    folder = root / client
    counts = {}
    codex_usage = None
    for path in (folder / 'config').rglob('*.jsonl'):
        for line in path.read_text(errors='replace').splitlines():
            try:
                record = json.loads(line)
            except ValueError:
                continue
            if client == 'codex' and record.get('type') == 'event_msg' and record.get('payload', {}).get('type') == 'token_count':
                codex_usage = record['payload'].get('info', {}).get('total_token_usage')
            if client == 'claude' and record.get('type') == 'assistant':
                message = record.get('message', {})
                if message.get('usage'):
                    counts[message.get('id', record.get('uuid'))] = message['usage']
    result[client] = {'exclusiveDispatchMarkers': len(list((folder / 'data').glob('dispatch-*.json'))),
                      'nativeUsage': codex_usage if client == 'codex' else {key: sum(row.get(key, 0) for row in counts.values())
                          for key in ['input_tokens', 'cache_creation_input_tokens', 'cache_read_input_tokens', 'output_tokens']},
                      'assistantApiMessages': len(counts) if client == 'claude' else None}
print(json.dumps(result))

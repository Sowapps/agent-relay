"""Real MVP acceptance through its C# executable, with explicit paid-agent markers."""
import argparse
import json
from pathlib import Path
import subprocess
import time
import uuid

parser = argparse.ArgumentParser()
parser.add_argument('phase', choices=['local', 'agents', 'recovery'])
parser.add_argument('--runtime', required=True)
args = parser.parse_args()
configuration = json.loads(Path(args.runtime).read_text(encoding='utf-8-sig'))
data = Path(configuration['data'])
base = [configuration['dotnet'], configuration['assembly']]


def invoke(mode, *arguments):
    process = subprocess.run(base + [mode, '--data-dir', str(data), *arguments], capture_output=True, text=True,
                             encoding='utf-8', timeout=50, creationflags=subprocess.CREATE_NO_WINDOW)
    if process.returncode:
        raise RuntimeError(process.stderr)
    return json.loads(process.stdout)


def completed(identifier):
    deadline = time.monotonic() + 145
    while time.monotonic() < deadline:
        record = invoke('result', '--id', identifier)['work']
        if record['state'] in ['completed', 'failed', 'unknown']:
            return record
        time.sleep(.5)
    raise TimeoutError('Operation has not reached a confirmed outcome')


host = subprocess.Popen(base + ['tray', '--data-dir', str(data), '--lifetime-ms', '300000'],
                        stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)
results = {}
try:
    invoke('profiles')
    if args.phase == 'local':
        identifier = str(uuid.uuid4())
        invoke('submit', '--id', identifier, '--profile', 'command', '--kind', 'command', '--command', 'list')
        record = completed(identifier)
        assert record['state'] == 'completed' and record['result']['output'] == 'alpha.txt\nbeta.txt\n', record
        results['directCommand'] = record
        # Exercise the actual stdio MCP facade against the same running host.
        requests = [{'jsonrpc': '2.0', 'id': 1, 'method': 'initialize', 'params': {'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'mvp-acceptance', 'version': '1'}}},
                    {'jsonrpc': '2.0', 'id': 2, 'method': 'tools/list'},
                    {'jsonrpc': '2.0', 'id': 3, 'method': 'tools/call', 'params': {'name': 'get_result', 'arguments': {'request_id': identifier}}}]
        mcp = subprocess.run(base + ['mcp', '--data-dir', str(data)], input=''.join(json.dumps(value)+'\n' for value in requests),
                             text=True, encoding='utf-8', capture_output=True, timeout=15, creationflags=subprocess.CREATE_NO_WINDOW)
        replies = [json.loads(line) for line in mcp.stdout.splitlines()]
        assert mcp.returncode == 0 and len(replies[1]['result']['tools']) == 6, mcp.stderr
        result = json.loads(replies[2]['result']['content'][0]['text'])
        assert result['work'] == record
        results['mcp'] = replies
        for profile in ['codex', 'claude']:
            invoke('start-profile', '--profile', profile)
        results['nativeStartedWithoutPrompt'] = True
    elif args.phase == 'agents':
        for profile in ['codex', 'claude']:
            identifier = str(uuid.uuid4())
            marker = data / ('real-agent-' + profile + '.started')
            with marker.open('x', encoding='utf-8') as stream:
                json.dump({'id': identifier, 'profile': profile, 'budget': 'first authorized short exchange'}, stream)
            text = ('Answer exactly ' + profile.upper() + '_MVP_REPLY ' + identifier +
                    '. Also call relay.send_message once with text "' + profile.upper() + '_MVP_INIT ' + identifier +
                    '" and no in_reply_to, independently of your correlated reply. No other tools or agents.')
            invoke('submit', '--id', identifier, '--profile', profile, '--kind', 'message', '--message', text)
            record = completed(identifier)
            results[profile] = record
            (data / ('acceptance-' + profile + '.json')).write_text(json.dumps(record, indent=2), encoding='utf-8')
            if record['state'] != 'completed':
                raise RuntimeError(json.dumps(record))
            assert profile.upper() + '_MVP_REPLY ' + identifier in record['result']['output']
        results['inbox'] = invoke('inbox')['inbox']
        for profile in ['codex', 'claude']:
            assert any(message['sender'] == profile + '-wsl' and message['inReplyTo'] is None for message in results['inbox'])
    else:
        original = json.loads((data / 'acceptance-agents.json').read_text())
        for profile in ['codex', 'claude']:
            previous = original[profile]
            if profile == 'codex' and (data / 'fault-recovery.json').exists():
                previous = json.loads((data / 'fault-recovery.json').read_text())['Record']
            current = invoke('result', '--id', previous['request']['id'])['work']
            assert current['request'] == previous['request'] and current['state'] == previous['state'] == 'completed'
            assert current['result']['origin'] == previous['result']['origin'] and current['result']['output'] == previous['result']['output']
            assert current['updatedAt'] == previous['updatedAt'], 'Completed reply was rewritten after host restart'
            results[profile] = current
        results['inbox'] = invoke('inbox')['inbox']
        assert len(results['inbox']) >= 4
        results['nativePromptsRepeated'] = 0
finally:
    # Only this owned Windows test host is stopped. Linux agents remain independent.
    if host.poll() is None:
        host.terminate()
    host.wait(timeout=10)
    (data / ('acceptance-' + args.phase + '.json')).write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding='utf-8')
print(json.dumps(results, ensure_ascii=False))

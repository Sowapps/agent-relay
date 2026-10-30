"""Dedicated named-session qualification through the real MCP protocol, separate from desktop-tool discovery."""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import time
import uuid
from relay_mcp import RelayMcp

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('phase', choices=['probe', 'agents', 'resume', 'cleanup'])
parser.add_argument('--runtime', type=Path, required=True)
parser.add_argument('--data', type=Path, required=True)
parser.add_argument('--templates', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
data = args.data.resolve()
assert data.is_relative_to(root / 'local/apps/agent-relay'), 'Qualification must remain application-local'
data.mkdir(parents=True, exist_ok=True)
runtime = json.loads(args.runtime.read_text(encoding='utf-8-sig'))
configuration = json.loads(args.templates.read_text(encoding='utf-8-sig'))
target = data / 'relay-config.json'
if not target.exists():
    target.write_text(json.dumps(configuration), encoding='utf-8')
else:
    assert json.loads(target.read_text()) == configuration, 'Do not silently replace fixture permissions'
manifest = data / 'session-acceptance.json'
if manifest.exists():
    state = json.loads(manifest.read_text())
else:
    state = {'sessions': {key: str(uuid.uuid4()) for key in ['codex-a', 'codex-b', 'claude']}, 'phases': {}}
    manifest.write_text(json.dumps(state, indent=2), encoding='utf-8')


def save():
    manifest.write_text(json.dumps(state, indent=2, ensure_ascii=False), encoding='utf-8')


def wait_ready(mcp, identifier):
    deadline = time.monotonic() + 70
    while time.monotonic() < deadline:
        result = mcp.tool('get_session', {'session_id': identifier})
        agent = result['session'].get('agent') or {}
        if agent.get('state') == 'ready':
            return result
        if agent.get('state') in ['attention_required', 'unavailable']:
            raise RuntimeError(result)
        time.sleep(.5)
    raise TimeoutError(result)


def submit_once(mcp, key, text):
    marker = data / (key + '.intent.json')
    request = {'session_id': state['sessions']['claude' if key.startswith('claude') else 'codex-b' if key.startswith('codex-b') else 'codex-a'],
               'request_id': str(uuid.uuid4()), 'text': text}
    # Persist paid intent first. An uncertain prior intent is never resubmitted automatically.
    with marker.open('x', encoding='utf-8') as stream:
        json.dump(request, stream, indent=2)
    return request, mcp.tool('send_session_message', request)


def wait_result(mcp, request):
    deadline = time.monotonic() + 145
    while time.monotonic() < deadline:
        record = mcp.tool('get_result', {'request_id': request['request_id']})['work']
        if record['state'] in ['completed', 'failed', 'unknown']:
            if record['state'] != 'completed':
                raise RuntimeError(record)
            return record
        time.sleep(.5)
    raise TimeoutError(request)


with (data / ('host-' + args.phase + '.stderr.log')).open('w', encoding='utf-8') as host_errors, (data / ('mcp-' + args.phase + '.stderr.log')).open('w', encoding='utf-8') as errors:
    host = subprocess.Popen([runtime['dotnet'], runtime['assembly'], 'tray', '--data-dir', str(data)],
                            stdout=subprocess.DEVNULL, stderr=host_errors, creationflags=subprocess.CREATE_NO_WINDOW)
    result = {'at': datetime.now(timezone.utc).isoformat(), 'desktopCurrentChatToolsProven': False}
    try:
        time.sleep(.7)
        if host.poll() is not None:
            raise RuntimeError('Owned qualification host failed to start')
        with RelayMcp(runtime, data, root, errors) as mcp:
            if args.phase == 'probe':
                result['tools'] = [tool['name'] for tool in mcp.call('tools/list')['tools']]
                result['sessions'] = {}
                for key, identifier in state['sessions'].items():
                    profile = next(p for p in configuration['profiles'] if p['client'] == ('claude' if key == 'claude' else 'codex'))
                    mcp.tool('open_session', {'session_id': identifier, 'profile_id': profile['id'], 'name': 'Qualification ' + key})
                    result['sessions'][key] = wait_ready(mcp, identifier)
                result['modelPrompts'] = 0
            elif args.phase == 'agents':
                assert state['phases'].get('probe', {}).get('passed'), 'Probe readiness before paid prompts'
                pending = []
                for key, secret in [('codex-a', 'AMBER_731'), ('codex-b', 'COBALT_842')]:
                    request, acknowledgement = submit_once(mcp, key, 'Keep this session-only token in context: ' + secret + '. Send an independent progress message saying "Context retained", then reply "Ready". No other tools.')
                    pending.append(request)
                result['parallelAcknowledgements'] = len(pending)
                result['records'] = [wait_result(mcp, request) for request in pending]
                request, acknowledgement = submit_once(mcp, 'claude', 'Send an independent progress message saying "Claude relay ready", then reply "Claude confirmed". No other tools.')
                result['records'].append(wait_result(mcp, request))
                result['inboxes'] = {}
                for key, identifier in state['sessions'].items():
                    inbox = mcp.tool('read_inbox', {'session_id': identifier})['inboxPage']
                    assert any(item['inReplyTo'] is None and item['sessionId'] == identifier for item in inbox['items']), inbox
                    result['inboxes'][key] = inbox
                result['modelPrompts'] = 3
            elif args.phase == 'resume':
                previous = state['phases'].get('agents', {})
                assert len(previous.get('records', [])) >= 2 and all(record['state'] == 'completed' for record in previous['records'][:2]), 'Both original Codex messages must be confirmed'
                result['records'] = []
                for key, secret in [('codex-a', 'AMBER_731'), ('codex-b', 'COBALT_842')]:
                    identifier = state['sessions'][key]
                    mcp.tool('rename_session', {'session_id': identifier, 'name': 'Resumed ' + key})
                    mcp.tool('close_session', {'session_id': identifier})
                    mcp.tool('reopen_session', {'session_id': identifier})
                    wait_ready(mcp, identifier)
                    request, acknowledgement = submit_once(mcp, key + '-resume', 'What session-only token did I ask you to keep? Return only that token through the reply tool. No other tools.')
                    record = wait_result(mcp, request)
                    assert secret in record['result']['output'], record
                    result['records'].append(record)
                result['modelPrompts'] = 2
            else:
                result['statusBeforeCleanup'] = {key: mcp.tool('get_session', {'session_id': identifier}) for key, identifier in state['sessions'].items()}
                result['inboxes'] = {key: mcp.tool('read_inbox', {'session_id': identifier})['inboxPage'] for key, identifier in state['sessions'].items()}
                for key in ['codex-a', 'codex-b']:
                    assert any(item['inReplyTo'] is None and item['sessionId'] == state['sessions'][key] for item in result['inboxes'][key]['items']), result['inboxes'][key]
                result['sessions'] = {key: mcp.tool('close_session', {'session_id': identifier}) for key, identifier in state['sessions'].items()}
                result['modelPrompts'] = 0
            result['passed'] = True
    except BaseException as error:
        result.update(passed=False, error=str(error))
        raise
    finally:
        state['phases'][args.phase] = result
        save()
        if host.poll() is None:
            host.terminate()
        host.wait(timeout=10)
print(json.dumps({'phase': args.phase, 'passed': result['passed'], 'report': str(manifest)}))

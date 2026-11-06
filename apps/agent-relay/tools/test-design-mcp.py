"""Real Unicode MCP request/response through broker, worker and pipe; retained closed fixture only.

Usage: --runtime <prepared pointer> --data <fresh seeded design-fixture directory>.
Does not start or contact a native agent, WSL, model or desktop window.
"""
import argparse
import json
from pathlib import Path
import subprocess
from deployment import launch
from relay_mcp import RelayMcp
from runtime_layout import runtime_command


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', required=True, type=Path)
    parser.add_argument('--data', required=True, type=Path)
    parser.add_argument('--broker', type=Path, help='Optionally qualify a previously retained broker executable in a new owned child')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    data = args.data.resolve()
    if not data.is_relative_to(root / 'local/apps/agent-relay'):
        raise ValueError('Use an explicit developer fixture')
    fixture = json.loads((data / 'design-fixture.json').read_text(encoding='utf-8-sig'))
    runtime = json.loads(args.runtime.read_text(encoding='utf-8-sig'))
    runtime['connectionId'] = fixture['connection']
    initialize = subprocess.run(runtime_command(runtime) + ['initialize', '--data-dir', str(data), '--connection-id', fixture['connection']],
                                capture_output=True, text=True, encoding='utf-8', timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
    if initialize.returncode:
        raise RuntimeError(initialize.stderr)
    pointer = json.loads((data / 'mcp-runtime.json').read_text(encoding='utf-8-sig'))
    host = launch(runtime, data, data)
    try:
        with (data / 'unicode.stderr.log').open('w', encoding='utf-8') as errors:
            with RelayMcp(dict(runtime, executable=str(args.broker.resolve()) if args.broker else pointer['mcpExecutable']), data, root, errors) as client:
                projects = client.tool('list_projects')['configuration']['projects']
                assert projects[0]['name'] == 'Étude — 中文 😀', projects
                text = 'Révision — façade 中文 😀'
                renamed = client.tool('rename_session', {'session_id': fixture['session'], 'name': text})['session']
                assert renamed['name'] == text and renamed['state'] == 'closed', renamed
                retained = client.tool('get_session', {'session_id': fixture['session']})['session']
                assert retained['name'] == text and retained['state'] == 'closed', retained
                result = {'passed': True, 'unicodeRequestAndResponse': True, 'closedSessionPreserved': True,
                          'nativeAgentCalls': 0, 'modelCalls': 0, 'desktopWindows': 0}
                if fixture.get('errors'):
                    denied = client.call('tools/call', {'name': 'send_session_message', 'arguments': {
                        'session_id': fixture['session'], 'text': ' '}})
                    failure = denied['structuredContent']['error']
                    assert denied['isError'] and failure['code'] == 'invalid_work' and failure['execution'] == 'not_started', failure
                    for code, state in [('approval_required', 'awaiting_approval'), ('reply_unconfirmed', 'unknown')]:
                        request_id = fixture['errors'][code]
                        operation = client.tool('get_result', {'request_id': request_id})['operation']
                        assert operation['state'] == state and operation['errorCode'] == code, operation
                        if state == 'unknown':
                            retained = client.tool('send_session_message', {'session_id': fixture['session'], 'request_id': request_id,
                                                                          'text': 'fixture'})['operation']
                            assert retained == operation, retained
                    result.update(preExecutionRefusal=True, approvalDistinct=True, uncertainWithoutReplay=True)
                (data / 'design-mcp-result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
                print(json.dumps(result))
    finally:
        host.terminate()
        host.wait(timeout=10)


if __name__ == '__main__':
    main()

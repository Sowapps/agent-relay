"""Controls only qualification-owned interactive tmux sessions and transcripts."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time


def tmux(root, *args, check=True):
    return subprocess.run(['tmux', '-S', str(root / 'tmux.sock'), '-f', str(root / 'tmux.conf'), *args], capture_output=True,
                          text=True, check=check)


def start(root, client):
    folder = root / client
    data = folder / 'data'
    workspace = folder / 'workspace'
    env = os.environ.copy()
    env['DISABLE_AUTOUPDATER'] = '1'
    env['CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC'] = '1'
    if client == 'codex':
        env['CODEX_HOME'] = str(folder / 'config')
        argv = ['/usr/local/bin/codex', '--no-alt-screen', '-C', str(workspace)]
    else:
        env['CLAUDE_CONFIG_DIR'] = str(folder / 'config')
        settings = {'autoMemoryEnabled': False, 'claudeMdExcludes': ['**/CLAUDE.md', '**/CLAUDE.local.md', '**/AGENTS.md'],
                    'enabledPlugins': {}, 'hooks': {}}
        argv = ['/home/sowapps/.local/bin/claude', '--setting-sources', '', '--settings', json.dumps(settings),
                '--strict-mcp-config', '--mcp-config', str(folder / 'mcp.json'), '--disable-slash-commands',
                '--tools', '', '--allowedTools', 'mcp__relay__send_message', '--no-chrome', '--effort', 'low',
                '--session-id', json.loads((root / 'config.json').read_text())['claude_session'],
                '--system-prompt', 'You are an isolated relay test endpoint. Use only the relay message tool as requested. Be brief.']
    (data / 'launch.json').write_text(json.dumps({'argv': argv, 'config_directory': env.get('CODEX_HOME') or env['CLAUDE_CONFIG_DIR']}))
    # Preserve terminal stdio so the client remains connected to a real PTY.
    wrapper = folder / 'launch.py'
    wrapper.write_text('import os\nos.environ.update(' + repr({k: v for k, v in env.items() if k in
                       ['CODEX_HOME', 'CLAUDE_CONFIG_DIR', 'DISABLE_AUTOUPDATER', 'CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC']})
                       + ')\nos.execv(' + repr(argv[0]) + ',' + repr(argv) + ')\n')
    tmux(root, 'new-session', '-d', '-s', client, '-c', str(workspace), '-x', '170', '-y', '45',
         'python3', str(wrapper))
    tmux(root, 'set-option', '-t', client, 'remain-on-exit', 'on')
    print(json.dumps({'started': client, 'model_prompt_sent': False}))


def inspect(root, client):
    folder = root / client
    data = folder / 'data'
    captured = tmux(root, 'capture-pane', '-p', '-t', client, '-S', '-55', check=False)
    (data / 'pane.txt').write_text(captured.stdout + captured.stderr)
    result = {'client': client, 'mcp_initialized': (data / 'mcp-initialized.json').exists(),
              'outbound': json.loads((data / 'outbox.json').read_text()) if (data / 'outbox.json').exists() else None,
              'assistant_text': [], 'usage': None, 'pane': captured.stdout[-2800:]}
    claude_usage = {}
    user_messages = set()
    sessions = set()
    calls = {}
    # Only isolated native config directories are read, never other conversations.
    for path in (folder / 'config').rglob('*.jsonl'):
        for line in path.read_text(errors='replace').splitlines():
            try:
                row = json.loads(line)
            except ValueError:
                continue
            if client == 'codex':
                item = row.get('payload', {})
                if row.get('type') == 'session_meta':
                    sessions.add(item.get('id', 'unknown'))
                if row.get('type') == 'response_item' and item.get('type') == 'function_call':
                    calls[item.get('call_id', str(len(calls)))] = item.get('name')
                if row.get('type') == 'event_msg' and item.get('type') == 'user_message':
                    user_messages.add(item.get('message', ''))
                if row.get('type') == 'response_item' and item.get('role') == 'assistant':
                    result['assistant_text'].extend(x.get('text', '') for x in item.get('content', []) if x.get('type') == 'output_text')
                if row.get('type') == 'event_msg' and item.get('type') == 'token_count':
                    result['usage'] = item.get('info', {}).get('total_token_usage')
            elif row.get('type') == 'assistant':
                message = row.get('message', {})
                result['assistant_text'].extend(x.get('text', '') for x in message.get('content', []) if x.get('type') == 'text')
                for block in message.get('content', []):
                    if block.get('type') == 'tool_use':
                        calls[block.get('id', str(len(calls)))] = block.get('name')
                if message.get('usage'):
                    claude_usage[message.get('id', str(row.get('uuid')))] = message['usage']
            elif row.get('type') == 'user' and isinstance(row.get('message', {}).get('content'), str):
                user_messages.add(row['message']['content'])
    if client == 'claude':
        result['usage'] = {key: sum(u.get(key, 0) for u in claude_usage.values()) for key in
                           ['input_tokens', 'cache_creation_input_tokens', 'cache_read_input_tokens', 'output_tokens']}
        result['assistant_api_messages'] = len(claude_usage)
    # This counter covers recognized native record shapes, not controller turns.
    result['recognized_user_records'] = len(user_messages)
    result['session_ids'] = sorted(sessions)
    result['tool_calls'] = list(calls.values())
    (data / 'inspection.json').write_text(json.dumps(result, indent=2))
    print(json.dumps(result))


def send(root, client):
    folder = root / client
    data = folder / 'data'
    if not (data / 'mcp-initialized.json').exists():
        raise RuntimeError('Native MCP initialization is not confirmed; no model prompt sent')
    screen = tmux(root, 'capture-pane', '-p', '-t', client, '-S', '-35').stdout
    if any(phrase in screen for phrase in ['Do you trust', 'Yes, I trust', 'Sign in', 'Log in', 'Select the login']):
        raise RuntimeError('Startup requires attention; no model prompt sent')
    ident = json.loads((root / 'config.json').read_text())['run_id']
    prompt = ('Call relay send_message exactly once with text "WSL_INIT_' + client.upper() + ' ' + ident
              + '". Then reply with exactly "' + client.upper() + '_REPLY ' + ident
              + '". Use no other tools or agents. This is the single authorized short exchange.')
    with (data / 'prompt.started').open('x') as stream:
        stream.write(prompt)
    tmux(root, 'send-keys', '-l', '-t', client, prompt)
    time.sleep(0.1 + len(prompt) / 1000)
    tmux(root, 'send-keys', '-t', client, 'Enter')
    # Never retype the prompt or create a second user turn automatically.
    print(json.dumps({'submitted': client, 'characters': len(prompt), 'single_prompt': True}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('operation', choices=['start', 'inspect', 'send'])
    parser.add_argument('root')
    parser.add_argument('client', choices=['codex', 'claude'])
    args = parser.parse_args()
    root = Path(args.root).resolve()
    assert root.parent == Path('/srv') and root.name.startswith('agent-relay-qualification-')
    {'start': start, 'inspect': inspect, 'send': send}[args.operation](root, args.client)

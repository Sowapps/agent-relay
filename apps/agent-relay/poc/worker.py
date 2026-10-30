"""One bounded Claude response; invoked only inside the disposable WSL tmux pane."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time


def atomic_json(path, value):
    temp = path.with_suffix('.tmp')
    with temp.open('w', encoding='utf-8') as stream:
        json.dump(value, stream, ensure_ascii=False)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temp, path)
    fd = os.open(path.parent, os.O_DIRECTORY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def main():
    workspace = Path(sys.argv[1]).resolve()
    container = workspace.parent
    if container.parent != Path('/srv') or not container.name.startswith('agent-relay-poc-') or workspace.name != 'workspace':
        raise ValueError('Workspace outside the isolated POC prefix')
    root = container / 'data'
    os.chdir(workspace)
    # Marker is committed BEFORE the model call. Never retry a consumed attempt.
    with (root / 'invocation.started').open('x') as stream:
        stream.write(str(time.time()))
        stream.flush()
        os.fsync(stream.fileno())
    print('WORKER_RECEIVED', flush=True)
    request = json.loads((root / 'request.json').read_text())
    key = request['request_id']
    argv = [
        '/home/sowapps/.local/bin/claude', '-p', '--safe-mode',
        '--tools', '', '--strict-mcp-config', '--disable-slash-commands',
        '--no-chrome', '--no-session-persistence', '--output-format', 'json',
        '--max-turns', '1', '--max-budget-usd', '0.05', '--effort', 'low',
        '--system-prompt', 'You are a literal echo endpoint. Reply exactly as requested. No tools.',
        'Reply with exactly this single line and nothing else: RELAY_OK ' + key,
    ]
    started = time.monotonic()
    try:
        env = os.environ.copy()
        env['DISABLE_AUTOUPDATER'] = '1'
        env['CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC'] = '1'
        completed = subprocess.run(argv, stdin=subprocess.DEVNULL, capture_output=True,
                                   text=True, timeout=75, check=False, env=env)
        atomic_json(root / 'client-output.json', {'stdout': completed.stdout, 'stderr': completed.stderr})
        value = json.loads(completed.stdout)
        ok = (completed.returncode == 0 and not value.get('is_error')
              and value.get('result', '').strip() == 'RELAY_OK ' + key)
        record = {'request_id': key, 'status': 'completed' if ok else 'failed',
                  'worker_invocations': 1, 'returncode': completed.returncode,
                  'duration_seconds': round(time.monotonic() - started, 3),
                  'response': value.get('result'), 'usage': value.get('usage'),
                  'model_usage': value.get('modelUsage'), 'cost_usd_estimate': value.get('total_cost_usd'),
                  'num_turns': value.get('num_turns'), 'session_id': value.get('session_id'),
                  'stderr_tail': completed.stderr[-1000:]}
    except Exception as error:
        record = {'request_id': key, 'status': 'failed', 'worker_invocations': 1,
                  'error': type(error).__name__ + ': ' + str(error)}
    atomic_json(root / 'result.json', record)
    print('WORKER_RESULT_DURABLE ' + hashlib.sha256((root / 'result.json').read_bytes()).hexdigest(), flush=True)
    return 0 if record['status'] == 'completed' else 1


if __name__ == '__main__':
    sys.exit(main())

"""Explicit steps for the authorized one-response POC; no automatic rerun."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid
from workspace_paths import repository_root

ROOT = Path(__file__).resolve().parent.parent
BASE = repository_root(__file__) / 'local' / 'apps' / 'agent-relay' / 'poc'


def wsl(argv, data=None, user='sowapps'):
    result = subprocess.run(['wsl.exe', '-d', 'Ubuntu-24.04', '-u', user, '--exec', *argv],
                            input=data, capture_output=True, timeout=50)
    if result.returncode:
        raise RuntimeError(result.stderr.decode('utf-8', 'replace')[-500:])
    return result.stdout.decode('utf-8').strip()


def save(root, name, value):
    (root / name).write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')


def mcp(root, name, arguments):
    messages = [
        {'jsonrpc': '2.0', 'id': 1, 'method': 'initialize',
         'params': {'protocolVersion': '2024-11-05', 'capabilities': {}, 'clientInfo': {'name': 'recovery-check', 'version': '1'}}},
        {'jsonrpc': '2.0', 'method': 'notifications/initialized'},
        {'jsonrpc': '2.0', 'id': 2, 'method': 'tools/call', 'params': {'name': name, 'arguments': arguments}},
    ]
    process = subprocess.run([sys.executable, str(ROOT / 'poc' / 'relay.py'), '--data', str(root)],
                             input=''.join(json.dumps(m) + '\n' for m in messages), capture_output=True,
                             text=True, timeout=100, check=True)
    result = json.loads(process.stdout.splitlines()[-1])['result']
    if result.get('isError'):
        raise RuntimeError(result)
    return json.loads(result['content'][0]['text'])


def prepare():
    key = str(uuid.uuid4())
    root = BASE / key
    root.mkdir(parents=True)
    linux_root = '/srv/agent-relay-poc-' + key
    config = {'request_id': key, 'linux_root': linux_root, 'socket': linux_root + '/tmux.sock',
              'linux_workspace': linux_root + '/workspace', 'linux_data': linux_root + '/data'}
    save(root, 'run.json', config)
    payload = {**config, 'worker': base64.b64encode((ROOT / 'poc' / 'worker.py').read_bytes()).decode()}
    setup = '''import sys,json,pathlib,base64,os,pwd
c=json.load(sys.stdin); root=pathlib.Path(c['linux_root'])
assert root.parent==pathlib.Path('/srv') and root.name=='agent-relay-poc-'+c['request_id']
assert not root.exists() and not root.is_symlink()
for parent in [root.parent,*root.parent.parents]:
 for name in ['CLAUDE.md','CLAUDE.local.md','AGENTS.md','.claude/CLAUDE.md','.claude/AGENTS.md']:
  assert not (parent/name).exists(), 'Ancestor instruction: '+str(parent/name)
account=pwd.getpwnam('sowapps');root.mkdir(mode=0o700);os.chown(root,account.pw_uid,account.pw_gid)
p=pathlib.Path(c['linux_workspace']);data=pathlib.Path(c['linux_data'])
for d in [p,data]:d.mkdir(mode=0o700);os.chown(d,account.pw_uid,account.pw_gid)
(p/'worker.py').write_bytes(base64.b64decode(c['worker']))
request=data/'request.json';request.write_text(json.dumps({'request_id':c['request_id']}));os.chown(request,account.pw_uid,account.pw_gid)
print(json.dumps({'workspace':str(p),'data_directory':str(data),'ancestor_instructions':False,'owner':'sowapps'}))
'''
    prepared = json.loads(wsl(['python3', '-c', setup], json.dumps(payload).encode(), user='root'))
    save(root, 'isolation.json', prepared)
    print(json.dumps(prepared))
    print('RUN_ID=' + key)


def pilot(root, config, approval_fix=False):
    if (root / 'pilot.started').exists():
        if not approval_fix or (root / 'pilot-approval-refusal.jsonl').exists():
            raise RuntimeError('Pilot already attempted; inspect records instead of repeating it')
        import sqlite3
        with sqlite3.connect(root / 'relay.sqlite') as db:
            assert db.execute('SELECT count(*) FROM task').fetchone()[0] == 0
        assert 'MCP tool call requires approval' in (root / 'pilot.jsonl').read_text()
        for old, new in [('pilot.jsonl', 'pilot-approval-refusal.jsonl'),
                         ('pilot.stderr', 'pilot-approval-refusal.stderr'),
                         ('pilot-summary.json', 'pilot-approval-refusal-summary.json')]:
            (root / old).rename(root / new)
    (root / 'pilot.started').touch()
    # Per-process TOML only: no edits to user or project client configuration.
    q = json.dumps
    server = ('mcp_servers={agent_relay_poc={command=' + q(sys.executable)
              + ',args=[' + q(str(ROOT / 'poc' / 'relay.py')) + ',"--data",' + q(str(root))
              + '],startup_timeout_sec=20,tool_timeout_sec=100,required=true,'
              + 'enabled_tools=["submit_task","get_result"],'
              + 'tools={submit_task={approval_mode="approve"},get_result={approval_mode="approve"}}}}')
    prompt = ('Florent explicitly said GO for this limited Agent Relay POC. The parent agent tracks its objective. '
              'Execute ONLY these two MCP tool calls, no shell, files, planning or extra agents: '
              'agent_relay_poc.submit_task with request_id=' + config['request_id'] + ', then '
              'agent_relay_poc.get_result with the same request_id. Never submit any other request. '
              'If either fails, report that and stop without retries. Finish with the exact worker response and nothing else.')
    args = [shutil.which('codex.exe'), 'exec', '--ignore-user-config', '--skip-git-repo-check',
            '--ephemeral', '--json', '--sandbox', 'read-only', '-C', str(root), '-c', server,
            '-c', 'model_reasoning_effort="low"', '-c', 'features.shell_tool=false',
            '-c', 'features.multi_agent=false', prompt]
    env = os.environ.copy()
    env.pop('CODEX_THREAD_ID', None)
    env['PYTHONIOENCODING'] = 'utf-8'
    with (root / 'pilot.jsonl').open('w', encoding='utf-8') as out, (root / 'pilot.stderr').open('w', encoding='utf-8') as err:
        process = subprocess.run(args, stdin=subprocess.DEVNULL, stdout=out, stderr=err, env=env, timeout=180)
    items = []
    usage = None
    for line in (root / 'pilot.jsonl').read_text(encoding='utf-8').splitlines():
        try:
            event = json.loads(line)
        except ValueError:
            continue
        if event.get('type') == 'turn.completed':
            usage = event.get('usage')
        if event.get('type') == 'item.completed':
            items.append(event.get('item'))
    save(root, 'pilot-summary.json', {'returncode': process.returncode, 'usage': usage, 'items': items})
    print(json.dumps({'returncode': process.returncode, 'usage': usage, 'items': items}, ensure_ascii=False))


def recover(root, config):
    key = config['request_id']
    first = mcp(root, 'get_result', {'request_id': key})
    if first.get('status') != 'completed' or first.get('source') != 'windows-durable-store':
        raise RuntimeError('No previously committed result to test')
    digest = first['result_sha256']
    wsl(['python3', '-c', 'import pathlib,sys; print(pathlib.Path(sys.argv[1]).read_text())',
         config['linux_data'] + '/result.json'])
    stopped = subprocess.run(['wsl.exe', '--terminate', 'Ubuntu-24.04'], capture_output=True, timeout=45)
    listing = subprocess.run(['wsl.exe', '--list', '--running', '--quiet'], capture_output=True, timeout=15)
    running = listing.stdout.decode('utf-16-le').replace('\x00', '')
    if 'Ubuntu-24.04' in running:
        raise RuntimeError('Distribution stop not confirmed')
    # A fresh relay process can read the result while WSL is stopped.
    offline = mcp(root, 'get_result', {'request_id': key})
    assert offline == first
    check = '''import pathlib,json,sys,hashlib
p=pathlib.Path(sys.argv[1]);raw=(p/'result.json').read_bytes();r=json.loads(raw)
print(json.dumps({'user':__import__('getpass').getuser(),'marker_count':len(list(p.glob('invocation.started'))),
                 'sha256':hashlib.sha256(raw).hexdigest(),'result':r}))
'''
    linux = json.loads(wsl(['python3', '-c', check, config['linux_data']]))
    assert linux['user'] == 'sowapps' and linux['marker_count'] == 1
    assert linux['result']['response'] == first['response']
    assert linux['sha256'] == digest
    assert first['num_turns'] == 1
    after = mcp(root, 'get_result', {'request_id': key})
    assert after['result_sha256'] == digest and after == first
    duplicate = mcp(root, 'submit_task', {'request_id': key})
    assert duplicate['duplicate'] and not duplicate['dispatched_again']
    report = {'wsl_stop_confirmed': True, 'fresh_relay_while_wsl_stopped': True,
              'result_unchanged_after_wsl_restart': True, 'linux_result_preserved': True,
              'worker_invocation_markers': 1, 'duplicate_submission_not_dispatched': True,
              'result_sha256': digest, 'response': first['response'], 'worker_usage': first.get('usage'),
              'worker_model_usage': first.get('model_usage'), 'worker_cost_usd_estimate': first.get('cost_usd_estimate')}
    save(root, 'recovery.json', report)
    print(json.dumps(report, ensure_ascii=False))


def cleanup(root, config):
    # Copy the small test result first; never read another session's state.
    code = '''import pathlib,sys,json
p=pathlib.Path(sys.argv[1]);print((p/'result.json').read_text() if (p/'result.json').exists() else 'null')
'''
    value = json.loads(wsl(['python3', '-c', code, config['linux_data']]))
    save(root, 'worker-result.json', value)
    # Unique socket belongs exclusively to this run.
    check = wsl(['sh', '-c', 'tmux -S "$1" kill-server 2>/dev/null || true; if tmux -S "$1" has-session 2>/dev/null; then exit 1; fi; printf "SOCKET_ABSENT"',
                 'cleanup', config['socket']])
    assert check == 'SOCKET_ABSENT'
    remove = '''import pathlib,sys,shutil,pwd
p=pathlib.Path(sys.argv[1]);expected='agent-relay-poc-'+sys.argv[2]
assert p.parent==pathlib.Path('/srv') and p.name==expected and not p.is_symlink()
if p.exists():
 assert p.resolve().parent==pathlib.Path('/srv') and p.stat().st_uid==pwd.getpwnam('sowapps').pw_uid
 shutil.rmtree(p)
assert not p.exists();print('ROOT_ABSENT')
'''
    assert wsl(['python3', '-c', remove, config['linux_root'], config['request_id']], user='root') == 'ROOT_ABSENT'
    save(root, 'cleanup.json', {'linux_workspace_removed': True, 'test_socket_cleanup_requested': True,
                              'test_socket_absent': True, 'client_configuration_files_created': False})
    print('CLEANUP=CONFIRMED')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('step', choices=['prepare', 'pilot', 'recover', 'cleanup'])
    parser.add_argument('--run')
    parser.add_argument('--approval-diagnostic-retry', action='store_true')
    arguments = parser.parse_args()
    if arguments.step == 'prepare':
        prepare()
    else:
        root = (BASE / str(uuid.UUID(arguments.run))).resolve()
        assert root.parent == BASE.resolve()
        config = json.loads((root / 'run.json').read_text())
        if arguments.step == 'pilot':
            pilot(root, config, arguments.approval_diagnostic_retry)
        else:
            {'recover': recover, 'cleanup': cleanup}[arguments.step](root, config)

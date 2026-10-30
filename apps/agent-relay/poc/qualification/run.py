"""Explicit qualification steps. No model is invoked until an explicit send step."""
import base64
import json
from pathlib import Path
import subprocess
import sys
import uuid

SOURCE = Path(__file__).resolve().parent
sys.path.insert(0, str(SOURCE.parent))
from workspace_paths import repository_root

BASE = repository_root(__file__) / 'local' / 'apps' / 'agent-relay' / 'qualification'


def wsl(argv, payload=None, user='sowapps'):
    result = subprocess.run(['wsl.exe', '-d', 'Ubuntu-24.04', '-u', user, '--exec', *argv],
                            input=payload, capture_output=True, timeout=35)
    if result.returncode:
        raise RuntimeError(result.stderr.decode('utf-8', 'replace')[-800:])
    return result.stdout.decode('utf-8').strip()


def call(root, name, arguments):
    requests = [
        {'jsonrpc': '2.0', 'id': 1, 'method': 'initialize', 'params': {'protocolVersion': '2025-11-25',
         'capabilities': {}, 'clientInfo': {'name': 'windows-qualification-controller', 'version': '1'}}},
        {'jsonrpc': '2.0', 'method': 'notifications/initialized'},
        {'jsonrpc': '2.0', 'id': 2, 'method': 'tools/call', 'params': {'name': name, 'arguments': arguments}},
    ]
    result = subprocess.run([sys.executable, str(SOURCE / 'relay_windows.py'), '--data', str(root)],
                            input=''.join(json.dumps(r) + '\n' for r in requests), capture_output=True, text=True,
                            timeout=80, check=True)
    reply = json.loads(result.stdout.splitlines()[-1])['result']
    if reply.get('isError'):
        raise RuntimeError(reply)
    value = json.loads(reply['content'][0]['text'])
    return value


def prepare():
    ident = str(uuid.uuid4())
    root = BASE / ident
    root.mkdir(parents=True)
    config = {'run_id': ident, 'linux_root': '/srv/agent-relay-qualification-' + ident,
              'claude_session': str(uuid.uuid4())}
    (root / 'config.json').write_text(json.dumps(config))
    files = {name: base64.b64encode((SOURCE / name).read_bytes()).decode()
             for name in ['message_endpoint.py', 'linux_control.py']}
    setup = r'''import sys,json,pathlib,base64,pwd,os
c=json.load(sys.stdin);p=pathlib.Path(c['linux_root']);assert p.parent==pathlib.Path('/srv') and p.name=='agent-relay-qualification-'+c['run_id']
assert not p.exists()
for parent in [p.parent,*p.parent.parents]:
 for name in ['CLAUDE.md','CLAUDE.local.md','AGENTS.md','.claude/CLAUDE.md','.claude/AGENTS.md']:
  assert not (parent/name).exists(), 'Instruction ancestor'
p.mkdir(mode=0o700);(p/'config.json').write_text(json.dumps({k:v for k,v in c.items() if k!='files'}))
for name,value in c['files'].items():(p/name).write_bytes(base64.b64decode(value))
(p/'tmux.conf').write_text('set -g remain-on-exit on\nset -g history-limit 8000\n')
(p/'fixture').mkdir();(p/'fixture/alpha.txt').write_text('alpha');(p/'fixture/beta.txt').write_text('beta')
(p/'instructions.md').write_text('You are an isolated relay qualification endpoint. Use only relay.send_message as requested and reply briefly. Do not use shell, files, skills or other agents.')
for client in ['codex','claude']:
 f=p/client
 for name in ['workspace','data','config']:(f/name).mkdir(parents=True)
 endpoint={'command':'/usr/bin/python3','args':[str(p/'message_endpoint.py'),'--data',str(f/'data'),'--sender',client+'-wsl']}
 (f/'mcp.json').write_text(json.dumps({'mcpServers':{'relay':endpoint}}))
 if client=='codex':
  (f/'config/auth.json').symlink_to('/home/sowapps/.codex/auth.json')
  q=json.dumps
  text='model_reasoning_effort="low"\napproval_policy="never"\nsandbox_mode="read-only"\nmodel_instructions_file='+q(str(p/'instructions.md'))+'\n'
  text+='[features]\nshell_tool=false\nmulti_agent=false\n'
  text+='[projects.'+q(str(f/'workspace'))+']\ntrust_level="trusted"\n'
  text+='[mcp_servers.relay]\ncommand="/usr/bin/python3"\nargs='+q(endpoint['args'])+'\nrequired=true\n'
  text+='[mcp_servers.relay.tools.send_message]\napproval_mode="approve"\n'
  (f/'config/config.toml').write_text(text)
 else:
  (f/'config/.credentials.json').symlink_to('/home/sowapps/.claude/.credentials.json')
  (f/'config/.claude.json').write_text(json.dumps({'hasCompletedOnboarding':True,'theme':'dark','projects':{str(f/'workspace'):{'hasTrustDialogAccepted':True}}}))
account=pwd.getpwnam('sowapps')
for item in [p,*p.rglob('*')]:os.chown(item,account.pw_uid,account.pw_gid,follow_symlinks=False)
print(json.dumps({'prepared':str(p),'ancestor_instructions':False,'credential_references_only':True,'models_called':0}))
'''
    result = wsl(['python3', '-c', setup], json.dumps({**config, 'files': files}).encode(), user='root')
    print('RUN_ID=' + ident)
    print(result)


def cleanup(root, config):
    linux = config['linux_root']
    # Kill only our unique tmux server, and preserve the small per-run diagnostics.
    code = '''import sys,pathlib,subprocess,json,time,os,signal
p=pathlib.Path(sys.argv[1]);t=subprocess.run(['tmux','-S',str(p/'tmux.sock'),'display-message','-p','#{pid}'],capture_output=True,text=True)
seen={}
def capture(pid):
 try:
  stat=pathlib.Path('/proc/'+str(pid)+'/stat').read_text().rsplit(')',1)[1].split();seen[pid]=stat[19]
  children=pathlib.Path('/proc/'+str(pid)+'/task/'+str(pid)+'/children').read_text().split()
  for child in children:capture(int(child))
 except (FileNotFoundError,ProcessLookupError):pass
if t.returncode==0:capture(int(t.stdout.strip()))
subprocess.run(['tmux','-S',str(p/'tmux.sock'),'kill-server'],capture_output=True)
def active(pid,start):
 try:
  stat=pathlib.Path('/proc/'+str(pid)+'/stat').read_text().rsplit(')',1)[1].split()
  return stat[19]==start and stat[0]!='Z'
 except FileNotFoundError:return False
time.sleep(.4)
for sig in [signal.SIGTERM,signal.SIGKILL]:
 for pid,start in seen.items():
  if active(pid,start):os.kill(pid,sig)
 time.sleep(.3)
assert not any(active(pid,start) for pid,start in seen.items()), 'A qualification process remains active'
out={}
for client in ['codex','claude']:
 d=p/client/'data';out[client]={f.name:f.read_text(errors='replace') for f in d.glob('*') if f.is_file() and f.stat().st_size<200000}
print(json.dumps({'records':out,'tracked_processes':len(seen),'process_cleanup_confirmed':True,
                  'denial':json.loads((p/'denial.json').read_text())}))
'''
    records = json.loads(wsl(['python3', '-c', code, linux]))
    (root / 'linux-records.json').write_text(json.dumps(records, indent=2))
    remove = '''import sys,pathlib,shutil,subprocess,pwd,json
p=pathlib.Path(sys.argv[1]);assert p.parent==pathlib.Path('/srv') and p.name=='agent-relay-qualification-'+sys.argv[2] and not p.is_symlink()
assert p.resolve().parent==pathlib.Path('/srv')
if p.exists():
 assert p.stat().st_uid==pwd.getpwnam('sowapps').pw_uid
 shutil.rmtree(p)
print(json.dumps({'linux_root_absent':not p.exists()}))
'''
    result = json.loads(wsl(['python3', '-c', remove, linux, config['run_id']], user='root'))
    (root / 'cleanup.json').write_text(json.dumps(result))
    print(json.dumps(result))


if __name__ == '__main__':
    step = sys.argv[1]
    if step == 'prepare':
        prepare()
    else:
        root = BASE / str(uuid.UUID(sys.argv[2]))
        config = json.loads((root / 'config.json').read_text())
        linux = config['linux_root']
        if step in ['start', 'inspect']:
            print(wsl(['python3', linux + '/linux_control.py', step, linux, sys.argv[3]]))
        elif step == 'send':
            print(json.dumps(call(root, 'send_agent_message', {'client': sys.argv[3]})))
        elif step == 'ls':
            value = call(root, 'execute_command', {'program': 'ls', 'arguments': ['-1'], 'workspace': 'fixture'})
            assert value['stdout']=='alpha.txt\nbeta.txt\n' and value['returncode']==0
            (root/'direct-command.json').write_text(json.dumps(value,indent=2));print(json.dumps(value))
        elif step == 'receive':
            value = call(root,'receive_messages',{})
            (root/'messages.json').write_text(json.dumps(value,indent=2));print(json.dumps(value))
        elif step == 'cleanup':
            cleanup(root,config)
        else:
            raise ValueError(step)

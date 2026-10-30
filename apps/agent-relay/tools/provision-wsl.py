"""Provision only a new dedicated relay root; no model prompt and no global edits."""
import argparse
import base64
import json
from pathlib import Path
import subprocess
import uuid

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--distribution', default='Ubuntu-24.04')
parser.add_argument('--user', default='sowapps')
parser.add_argument('--session-id', type=uuid.UUID)
parser.add_argument('--workspace')
parser.add_argument('--client', choices=['command','codex', 'claude'])
parser.add_argument('--binding')
parser.add_argument('--executable')
args = parser.parse_args()
application = Path(__file__).resolve().parents[1]
root = '/srv/agent-relay-session-' + args.session_id.hex if args.session_id else '/srv/agent-relay-mvp-' + uuid.uuid4().hex
files = {path.name: base64.b64encode(path.read_bytes()).decode() for path in (application / 'wsl').glob('*.py')}
setup = r'''
import base64,json,os,pathlib,pwd,sys
c=json.load(sys.stdin);p=pathlib.Path(c['root']);assert p.parent==pathlib.Path('/srv') and not p.is_symlink()
assert p.name==('agent-relay-session-'+c['sessionId'].replace('-','')) if c.get('sessionId') else p.name.startswith('agent-relay-mvp-')
account=pwd.getpwnam(c['user'])
if p.exists():
 marker=json.loads((p/'provision-owner.json').read_text());assert c.get('sessionId') and marker=={'sessionId':c['sessionId'],'user':c['user']} and p.stat().st_uid==account.pw_uid
 if c.get('workspace'):
  expected={'workspace':c['workspace'],**(c.get('binding') or {})}
  if c.get('executable'):expected['nativeExecutable']=c['executable']
  assert json.loads((p/c['client']/'context.json').read_text())==expected, 'Existing root belongs to another workspace or project binding'
 print(json.dumps({'root':str(p),'modelsCalled':0,'alreadyPrepared':True}));sys.exit(0)
for parent in [p.parent,*p.parent.parents]:
 for name in ['CLAUDE.md','CLAUDE.local.md','AGENTS.md','.claude/CLAUDE.md','.claude/AGENTS.md']:
  assert not (parent/name).exists(), 'Inherited instruction file'
p.mkdir(mode=0o700);(p/'install').mkdir();(p/'tmux.conf').write_text('set -g remain-on-exit on\nset -g history-limit 8000\n')
for name,value in c['files'].items():(p/'install'/name).write_bytes(base64.b64decode(value))
(p/'instructions.md').write_text('You are an isolated relay endpoint. Use only relay.send_message. Do not use shell, files, skills or other agents. Be brief.')
for client in ['command','codex','claude']:
 f=p/client
 for name in ['workspace','data/outbox','config']:(f/name).mkdir(parents=True)
 (f/'workspace/alpha.txt').write_text('alpha');(f/'workspace/beta.txt').write_text('beta')
 workspace=pathlib.Path(c['workspace']) if c.get('workspace') and c.get('client')==client else f/'workspace'
 if c.get('workspace') and c.get('client')==client:
  assert workspace.is_absolute() and workspace.is_dir() and workspace.resolve()==workspace, 'Configured workspace must already exist without symbolic links'
  assert not workspace.is_relative_to(p), 'Project workspace must be separate from the private session runtime'
  context={'workspace':str(workspace),**(c.get('binding') or {})}
  if c.get('executable'):context['nativeExecutable']=c['executable']
  (f/'context.json').write_text(json.dumps(context))
 if client=='command':continue
 endpoint={'command':'/usr/bin/python3','args':[str(p/'install/message_server.py'),'--root',str(p),'--profile',client]}
 (f/'mcp.json').write_text(json.dumps({'mcpServers':{'relay':endpoint}}))
 if client=='codex':
  (f/'config/auth.json').symlink_to(pathlib.Path(account.pw_dir)/'.codex/auth.json')
  q=json.dumps
  text='model_reasoning_effort="low"\napproval_policy="never"\nsandbox_mode="read-only"\nmodel_instructions_file='+q(str(p/'instructions.md'))+'\n'
  text+='[features]\nshell_tool=false\nmulti_agent=false\n[projects.'+q(str(workspace))+']\ntrust_level="trusted"\n'
  text+='[mcp_servers.relay]\ncommand="/usr/bin/python3"\nargs='+q(endpoint['args'])+'\nrequired=true\n[mcp_servers.relay.tools.send_message]\napproval_mode="approve"\n'
  (f/'config/config.toml').write_text(text)
 else:
  if not c.get('binding'):(f/'config/.credentials.json').symlink_to(pathlib.Path(account.pw_dir)/'.claude/.credentials.json')
  (f/'config/.claude.json').write_text(json.dumps({'hasCompletedOnboarding':True,'theme':'dark','projects':{str(workspace):{'hasTrustDialogAccepted':True}}}))
for item in [p,*p.rglob('*')]:os.chown(item,account.pw_uid,account.pw_gid,follow_symlinks=False)
(p/'provision-owner.json').write_text(json.dumps({'sessionId':c.get('sessionId'),'user':c['user']}));os.chown(p/'provision-owner.json',account.pw_uid,account.pw_gid)
print(json.dumps({'root':str(p),'modelsCalled':0,'credentialReferencesOnly':True}))
'''
result = subprocess.run(['wsl.exe', '-d', args.distribution, '-u', 'root', '--exec', 'python3', '-c', setup],
                        input=json.dumps({'root': root, 'files': files, 'user': args.user, 'sessionId': str(args.session_id) if args.session_id else None,
                            'workspace': args.workspace, 'client': args.client, 'binding': json.loads(args.binding) if args.binding else None, 'executable': args.executable}), text=True, capture_output=True, timeout=60)
if result.returncode:
    raise RuntimeError(result.stderr[-2000:])
profiles = []
for client, label in [('command', 'Commandes WSL'), ('codex', 'Codex WSL'), ('claude', 'Claude WSL')]:
    profiles.append({'id': client, 'name': label, 'distribution': args.distribution, 'user': args.user,
                     'root': root, 'client': client, 'commands': [{'id': 'list', 'name': 'Lister les fichiers',
                     'program': '/bin/ls', 'arguments': ['-1']}] if client == 'command' else []})
output = Path(args.output).resolve()
output.mkdir(parents=True, exist_ok=True)
(output / 'relay-config.json').write_text(json.dumps({'version': 1, 'profiles': profiles}, indent=2, ensure_ascii=False), encoding='utf-8')
(output / 'provision.json').write_text(result.stdout, encoding='utf-8')
print(result.stdout)
print(str(output / 'relay-config.json'))

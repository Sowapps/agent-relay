"""Refresh packaged adapters only in explicitly selected, relay-owned named-session roots."""
import argparse
import base64
import json
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'wsl'))
from native_layout import load_layout, payload_files
import sqlite3
import subprocess
import uuid

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--data', type=Path, required=True)
parser.add_argument('--session-id', type=uuid.UUID, action='append', help='Refresh only these retained session identities; repeat for an explicit set')
args = parser.parse_args()
application = Path(__file__).resolve().parents[1]
root = application.parents[1]
data = args.data.resolve()
assert any(data.is_relative_to(root / folder) for folder in ['var/apps/agent-relay','local/apps/agent-relay']), 'Select explicit declared relay data'
with sqlite3.connect(data / 'relay.sqlite') as database:
    active = database.execute("SELECT count(*) FROM work WHERE json_extract(payload,'$.state') IN ('pending','running')").fetchone()[0]
    assert active == 0, 'Wait for active relay work before updating adapters'
    sessions = [json.loads(row[0]) for row in database.execute('SELECT payload FROM sessions')]
if args.session_id:
    selected = {str(value) for value in args.session_id}
    sessions = [session for session in sessions if session['id'] in selected]
    if {session['id'] for session in sessions} != selected:
        raise ValueError('A selected session is absent; no adapter was updated')
files = payload_files(application / 'wsl')
script = '''
import base64,json,os,pathlib,sys,uuid
c=json.load(sys.stdin);s=c['session'];p=pathlib.Path(s['root'])
namespace={};exec(base64.b64decode(c['files']['native_layout.py']),namespace)
layout=namespace['layout_from_payload'](c['files'])
assert layout.is_session(p,s['id']) and p.resolve()==p and p.stat().st_uid==os.getuid()
assert json.loads((p/'provision-owner.json').read_text())=={'sessionId':s['id'],'user':s['template']['user']}
install=p/'install';assert install.resolve()==install
for name,value in c['files'].items():
 target=install/name;assert target.parent==install and not target.is_symlink()
 temporary=target.with_suffix('.pending-'+uuid.uuid4().hex);temporary.write_bytes(base64.b64decode(value));os.replace(temporary,target)
print(json.dumps({'sessionId':s['id'],'files':len(c['files']),'modelPrompts':0}))
'''
for session in sessions:
    template = session['template']
    result = subprocess.run(['wsl.exe', '-d', template['distribution'], '-u', template['user'], '--exec', 'python3', '-c', script],
                            input=json.dumps({'session': session, 'files': files}), text=True, encoding='utf-8', capture_output=True, timeout=20)
    if result.returncode:
        raise RuntimeError(result.stderr)
    print(result.stdout.strip())

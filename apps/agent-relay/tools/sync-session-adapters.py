"""Refresh packaged adapters only in explicitly selected, relay-owned named-session roots."""
import argparse
import base64
import json
from pathlib import Path
import sqlite3
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--data', type=Path, required=True)
args = parser.parse_args()
application = Path(__file__).resolve().parents[1]
root = application.parents[1]
data = args.data.resolve()
assert data.is_relative_to(root / 'local/apps/agent-relay'), 'Select explicit application-local data'
with sqlite3.connect(data / 'relay.sqlite') as database:
    active = database.execute("SELECT count(*) FROM work WHERE json_extract(payload,'$.state') IN ('pending','running')").fetchone()[0]
    assert active == 0, 'Wait for active relay work before updating adapters'
    sessions = [json.loads(row[0]) for row in database.execute('SELECT payload FROM sessions')]
files = {path.name: base64.b64encode(path.read_bytes()).decode() for path in (application / 'wsl').glob('*.py')}
script = '''
import base64,json,os,pathlib,sys,uuid
c=json.load(sys.stdin);s=c['session'];p=pathlib.Path(s['root'])
assert p.parent==pathlib.Path('/srv') and p.name=='agent-relay-session-'+uuid.UUID(s['id']).hex and p.resolve()==p and p.stat().st_uid==os.getuid()
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

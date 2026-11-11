"""Refresh packaged adapters only in explicitly selected, relay-owned named-session roots.

@see deployment.demand_idle and deployment.exclusive for shared offline maintenance guards.
@see provision-wsl.py and native_layout.py for the packaged helpers and session identity.
"""
import argparse
from contextlib import closing
import json
from pathlib import Path
import sqlite3
import sys
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'wsl'))
from native_layout import payload_files
from deployment import demand_idle, exclusive, read
from runtime_layout import app_var, read_runtime, validate_runtime
from wsl_maintenance import run_script

application = Path(__file__).resolve().parents[1]
root = application.parents[1]
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


def synchronize(data, session_ids=None, runtime_pointer=None):
    data = data.resolve()
    if not any(data.is_relative_to(folder) and data != folder
               for folder in [app_var(root), root / 'local/apps/agent-relay']):
        raise ValueError('Select explicit declared relay data')
    registered = read_runtime(data)
    runtime = read(runtime_pointer.resolve()) if runtime_pointer else registered
    validate_runtime(runtime, root, development=True)
    with exclusive(data):
        demand_idle(runtime, data)
        with closing(sqlite3.connect((data / 'relay.sqlite').as_uri() + '?mode=ro', uri=True)) as database:
            sessions = [json.loads(row[0]) for row in database.execute('SELECT payload FROM sessions')]
        if session_ids:
            selected = {str(value) for value in session_ids}
            sessions = [session for session in sessions if session['id'] in selected]
            if {session['id'] for session in sessions} != selected:
                raise ValueError('A selected session is absent; no adapter was updated')
        files = payload_files(application / 'wsl')
        for session in sessions:
            template = session['template']
            result = run_script(runtime, data, template['distribution'], template['user'], script,
                                {'session': session, 'files': files}, 20)
            print(result['output'].strip())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data', type=Path, required=True)
    parser.add_argument('--session-id', type=uuid.UUID, action='append', help='Refresh only these retained session identities; repeat for an explicit set')
    parser.add_argument('--runtime-pointer', type=Path, help='Explicit prepared guard runtime for installations predating maintenance-idle')
    args = parser.parse_args()
    synchronize(args.data, args.session_id, args.runtime_pointer)


if __name__ == '__main__':
    main()

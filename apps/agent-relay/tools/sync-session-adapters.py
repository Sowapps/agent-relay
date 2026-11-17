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
from deployment import demand_idle, exclusive
from runtime_layout import maintenance_runtime
from wsl_maintenance import run_script

application = Path(__file__).resolve().parents[1]
root = application.parents[1]
script = (application / 'wsl/session_adapter_files.py').read_text(encoding='utf-8')


def synchronize(data, session_ids=None, runtime_pointer=None):
    data = data.resolve()
    runtime = maintenance_runtime(data, root, runtime_pointer)
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
                                {'action': 'refresh', 'session': session, 'names': list(files), 'files': files,
                                 'layoutSource': files['native_layout.py'], 'layoutDefinition': files['native-layout.json']}, 20)
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

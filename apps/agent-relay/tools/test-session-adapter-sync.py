"""Qualify offline adapter synchronization on unique Windows and WSL copies, without models.

Usage: --runtime-pointer <current pointer> --distribution <distro> --user <user>.
An optional --legacy-runtime-pointer exercises an earlier runtime's refusal and explicit override.
"""
import argparse
import base64
from contextlib import closing
import hashlib
import importlib.util
import json
from pathlib import Path
import sqlite3
import time
from unittest.mock import patch
import uuid

from deployment import exclusive, read, write
from runtime_layout import reject_pilotage_paths, validate_runtime
from wsl_maintenance import run_script, WslMaintenanceError

spec = importlib.util.spec_from_file_location('session_adapter_sync', Path(__file__).with_name('sync-session-adapters.py'))
sync = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sync)
from native_layout import load_layout, payload_files


NATIVE = r'''
import base64,hashlib,json,os,pathlib,pwd,shutil,sys
c=json.load(sys.stdin);account=pwd.getpwnam(c['user'])
namespace={};exec(base64.b64decode(c['files']['native_layout.py']),namespace)
layout=namespace['layout_from_payload'](c['files'])
result={}
for session in c['sessions']:
 p=pathlib.Path(session['root']);identifier=session['id']
 assert layout.is_session(p,identifier) and p.is_absolute() and p.resolve()==p
 if c['action']=='prepare':
  assert not p.exists()
  p.parent.mkdir(parents=True,exist_ok=True);p.mkdir(mode=0o700)
  (p/'.qualification-owner').write_text(identifier)
  (p/'provision-owner.json').write_text(json.dumps({'sessionId':identifier,'user':c['user']}))
  (p/'install').mkdir()
  for name in c['files']:(p/'install'/name).write_bytes(b'previous packaged helper\n')
  (p/'install/retained.txt').write_text('retained')
  for item in [p,*p.rglob('*')]:os.chown(item,account.pw_uid,account.pw_gid)
 else:
  if not p.exists():continue
  assert (p/'.qualification-owner').read_text()==identifier and p.stat().st_uid==account.pw_uid
  assert (p/'install/retained.txt').read_text()=='retained'
  result[identifier]={name:hashlib.sha256((p/'install'/name).read_bytes()).hexdigest() for name in c['files']}
  if c['action']=='cleanup':shutil.rmtree(p)
print(json.dumps(result))
'''


def refused(action, expected):
    try:
        action()
    except (RuntimeError, ValueError) as error:
        assert expected in str(error), str(error)
    else:
        raise AssertionError('Expected refusal: ' + expected)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime-pointer', required=True, type=Path)
    parser.add_argument('--legacy-runtime-pointer', type=Path)
    parser.add_argument('--distribution', required=True)
    parser.add_argument('--user', required=True)
    args = parser.parse_args()
    started = time.monotonic()
    runtime = read(args.runtime_pointer.resolve())
    validate_runtime(runtime, sync.root, development=True)
    case = sync.root / 'local/apps/agent-relay/tests' / ('adapter-sync-' + uuid.uuid4().hex)
    reject_pilotage_paths(sync.root, case)
    data = case / 'data'
    data.mkdir(parents=True)
    report = {'passed': False, 'modelCalls': 0}
    sessions = []
    for legacy in [False, True]:
        identifier = uuid.uuid4()
        sessions.append({'id': str(identifier), 'root': load_layout().session(identifier, legacy=legacy),
                         'template': {'distribution': args.distribution, 'user': args.user}})
    files = payload_files(sync.application / 'wsl')

    def native(action):
        payload = {'action': action, 'user': args.user, 'sessions': sessions, 'files': files}
        result = run_script(runtime, data, args.distribution, 'root', NATIVE, payload, 30)
        return json.loads(result['output'])

    original_run = sync.run_script
    wsl_calls = []
    native_prepared = False

    def guarded_run(*arguments):
        refused(lambda: enter_competing_owner(data), 'Another host owns')
        wsl_calls.append(arguments[2:4])
        return original_run(*arguments)

    try:
        write(data / 'mcp-runtime.json', runtime)
        with closing(sqlite3.connect(data / 'relay.sqlite')) as database:
            database.executescript('CREATE TABLE work(id TEXT PRIMARY KEY, payload TEXT NOT NULL);'
                                   'CREATE TABLE sessions(id TEXT PRIMARY KEY, payload TEXT NOT NULL);')
            for session in sessions:
                database.execute('INSERT INTO sessions(id,payload) VALUES (?,?)',
                                 (session['id'], json.dumps(session)))
            database.commit()
        with patch.object(sync, 'run_script', side_effect=guarded_run):
            with exclusive(data):
                refused(lambda: sync.synchronize(data), 'Another host owns')
            for state in ['pending', 'running']:
                with closing(sqlite3.connect(data / 'relay.sqlite')) as database:
                    database.execute('INSERT INTO work VALUES (?,?)', ('busy', json.dumps({'state': state})))
                    database.commit()
                refused(lambda: sync.synchronize(data), 'Active durable work')
                with closing(sqlite3.connect(data / 'relay.sqlite')) as database:
                    assert json.loads(database.execute('SELECT payload FROM work WHERE id=?', ('busy',)).fetchone()[0])['state'] == state
                    database.execute('DELETE FROM work WHERE id=?', ('busy',))
                    database.commit()
            refused(lambda: sync.synchronize(data, [uuid.uuid4()]), 'selected session is absent')
            assert not wsl_calls, 'Refused maintenance invoked WSL'
        native_prepared = True
        native('prepare')
        before = native('inspect')
        pointer_before = (data / 'mcp-runtime.json').read_bytes()
        database_before = (data / 'relay.sqlite').read_bytes()
        with patch.object(sync, 'run_script', side_effect=guarded_run):
            sync.synchronize(data, [sessions[0]['id']])
        after = native('inspect')
        expected = {name: hashlib.sha256(base64.b64decode(value)).hexdigest() for name, value in files.items()}
        assert after[sessions[0]['id']] == expected
        assert after[sessions[1]['id']] == before[sessions[1]['id']]
        assert (data / 'mcp-runtime.json').read_bytes() == pointer_before
        override = None
        if args.legacy_runtime_pointer:
            legacy_runtime = read(args.legacy_runtime_pointer.resolve())
            validate_runtime(legacy_runtime, sync.root, development=True)
            write(data / 'mcp-runtime.json', legacy_runtime)
            pointer_before = (data / 'mcp-runtime.json').read_bytes()
            with patch.object(sync, 'run_script', side_effect=guarded_run):
                refused(lambda: sync.synchronize(data), 'Unsupported application mode')
            assert len(wsl_calls) == 1, 'Unsupported runtime invoked WSL'
            override = args.runtime_pointer
        with patch.object(sync, 'run_script', side_effect=guarded_run):
            sync.synchronize(data, [sessions[1]['id']], override)
        assert all(hashes == expected for hashes in native('inspect').values())
        assert (data / 'mcp-runtime.json').read_bytes() == pointer_before
        assert (data / 'relay.sqlite').read_bytes() == database_before
        enter_competing_owner(data)
        report.update(passed=True, activeStatesRefused=['pending', 'running'], hostOwnershipRefused=True,
                      guardHeldThroughCopy=True, unknownSelectionRefused=True,
                      defaultRuntimeChecked=True, legacyOverrideChecked=bool(args.legacy_runtime_pointer),
                      selectedCurrentAndLegacyRootsUpdated=True, otherSessionPreserved=True,
                      databaseAndRuntimeUnchanged=True, synchronizedSessions=len(wsl_calls))
    except BaseException as error:
        report['error'] = str(error) or type(error).__name__
        if isinstance(error, WslMaintenanceError):
            report['diagnostic'] = error.diagnostic
        raise
    finally:
        try:
            if native_prepared:
                native('cleanup')
                report['ownedNativeCopiesRemoved'] = True
        except BaseException as error:
            report['passed'] = False
            report['cleanupError'] = str(error) or type(error).__name__
            if isinstance(error, WslMaintenanceError):
                report['cleanupDiagnostic'] = error.diagnostic
            raise
        finally:
            report['seconds'] = round(time.monotonic() - started, 3)
            write(case / 'result.json', report)
            print(json.dumps({'report': str(case / 'result.json'), **report}), flush=True)


def enter_competing_owner(data):
    with exclusive(data):
        pass


if __name__ == '__main__':
    main()

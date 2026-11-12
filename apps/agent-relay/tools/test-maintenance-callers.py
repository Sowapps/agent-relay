"""Exercise profile synchronization and native migration transport on isolated copies."""
import argparse
from contextlib import closing
import importlib.util
import json
from pathlib import Path
import sqlite3
import time
from unittest.mock import patch
import uuid

from deployment import read, write
from runtime_layout import reject_pilotage_paths, validate_runtime
from wsl_maintenance import run_script, WslMaintenanceError


def load(name):
    spec = importlib.util.spec_from_file_location(name.replace('-', '_'), Path(__file__).with_name(name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


NATIVE = r'''
import base64,hashlib,json,os,pathlib,pwd,shutil,sys
c=json.load(sys.stdin);namespace={};exec(base64.b64decode(c['files']['native_layout.py']),namespace)
p=pathlib.Path(namespace['layout_from_payload'](c['files']).profile(c['id']))
assert p.resolve()==p
if c['action']=='prepare':
 assert not p.exists();p.mkdir(parents=True);(p/'install').mkdir()
 (p/'.qualification-owner').write_text(c['id']);(p/'retained.txt').write_text('preserve')
 for name in c['files']:(p/'install'/name).write_text('old')
 account=pwd.getpwnam(c['user'])
 for item in [p,*p.rglob('*')]:os.chown(item,account.pw_uid,account.pw_gid)
else:
 assert (p/'.qualification-owner').read_text()==c['id']
 assert (p/'retained.txt').read_text()=='preserve'
 if c['action']=='verify':
  assert all((p/'install'/name).read_bytes()==base64.b64decode(value) for name,value in c['files'].items())
 if c['action']=='link':
  target=p/'install'/next(iter(c['files']));target.unlink();target.symlink_to(p/'retained.txt')
 if c['action']=='cleanup':shutil.rmtree(p)
print(json.dumps({'root':str(p)}))
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime-pointer', required=True, type=Path)
    parser.add_argument('--distribution', required=True)
    parser.add_argument('--user', required=True)
    args = parser.parse_args()
    sync, migration = load('sync-wsl'), load('migrate-storage')
    runtime = read(args.runtime_pointer.resolve())
    validate_runtime(runtime, sync.root, development=True)
    data = sync.root / 'local/apps/agent-relay/tests' / ('maintenance-callers-' + uuid.uuid4().hex)
    reject_pilotage_paths(sync.root, data)
    data.mkdir(parents=True)
    started = time.monotonic()
    report = {'passed': False, 'modelCalls': 0}
    identifier = 'qualification-' + uuid.uuid4().hex
    files = sync.payload_files(sync.application / 'wsl')

    def native(action):
        payload = {'id': identifier, 'files': files, 'user': args.user, 'action': action}
        return json.loads(run_script(runtime, data, args.distribution, 'root', NATIVE, payload, 20)['output'])

    prepared = False
    try:
        write(data / 'mcp-runtime.json', runtime)
        with closing(sqlite3.connect(data / 'relay.sqlite')) as database:
            database.execute('CREATE TABLE work(id TEXT PRIMARY KEY,payload TEXT NOT NULL)')
        prepared = True
        location = native('prepare')['root']
        profile = {'distribution': args.distribution, 'user': args.user, 'root': location}
        settings = data / 'relay-config.json'
        write(settings, {'profiles': [profile, profile]})
        with patch.object(sync, 'run_script', wraps=sync.run_script) as invoke:
            sync.synchronize(settings)
            assert invoke.call_count == 1, 'Shared profile root was copied twice'
        native('verify')
        with closing(sqlite3.connect(data / 'relay.sqlite')) as database:
            database.execute('INSERT INTO work VALUES (?,?)', ('busy', '{"state":"running"}'))
            database.commit()
        with patch.object(sync, 'run_script') as invoke:
            try: sync.synchronize(settings)
            except RuntimeError as error: assert 'Active durable work' in str(error)
            else: raise AssertionError('Active profile synchronization was accepted')
            invoke.assert_not_called()
        with closing(sqlite3.connect(data / 'relay.sqlite')) as database:
            database.execute('DELETE FROM work');database.commit()
        native('link')
        try: sync.synchronize(settings)
        except WslMaintenanceError as error: assert error.diagnostic['phase'] == 'native_operation'
        else: raise AssertionError('Profile helper symlink was followed')
        group = {'distribution': args.distribution, 'user': args.user, 'roots': [], 'partials': []}
        assert migration.native_call(runtime, data, group, 'probe', uuid.uuid4().hex)['roots'] == []
        report.update(passed=True, deduplicatedProfiles=True, busyRefused=True, symlinkRefused=True,
                      nativeMigrationTransport=True)
    except BaseException as error:
        report['error'] = str(error)
        if isinstance(error, WslMaintenanceError): report['diagnostic'] = error.diagnostic
        raise
    finally:
        try:
            if prepared: native('cleanup')
            report['nativeCopyRemoved'] = True
        except BaseException as error:
            report.update(passed=False, cleanupError=str(error))
            raise
        finally:
            report['seconds'] = round(time.monotonic() - started, 3)
            write(data / 'result.json', report)
            print(json.dumps({'report': str(data / 'result.json'), **report}), flush=True)


if __name__ == '__main__':
    main()

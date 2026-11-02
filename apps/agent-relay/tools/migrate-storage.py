"""Review/apply one owned runtime-storage migration. Default is a non-mutating dry run.

Usage: --source <legacy-scope> --destination <var-scope> --runtime-pointer <prepared-native-pointer>
       --config <Codex-config> [--config ...] [--apply]. No model or global-agent-auth change.
"""
import argparse
import base64
from contextlib import closing
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import signal
import stat
import sqlite3
import subprocess
import time
import tomllib
import uuid
import winreg
from deployment import exclusive, launch
import importlib.util
_spec=importlib.util.spec_from_file_location('relay_register',Path(__file__).with_name('register-codex.py'))
_registration=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(_registration)
from runtime_layout import app_var, validate_runtime, runtime_command
from relay_mcp import RelayMcp

REGISTRY = r'Software\Microsoft\Windows\CurrentVersion\Run'
CORE = ['relay-config.json', 'relay-projects.json', 'connections', 'attention-reviews.json']
RETIRE = [*CORE, 'relay.sqlite', 'relay.sqlite-wal', 'relay.sqlite-shm', 'prepared', 'presentation-cache',
          'startup-launcher', 'startup.log', 'ui-host.json', 'mcp-runtime.json', 'mcp.codex.toml', 'mcp.windows.json',
          'launcher.stderr.log', 'ui.stderr.log']


def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    temporary = path.with_name(path.name + '.pending')
    temporary.write_text(json.dumps(value, indent=2), encoding='utf-8'); temporary.replace(path)


def reparse(path): return bool(path.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT)


def startup_name(data): return 'AgentRelay-' + hashlib.sha256(str(data).upper().encode()).hexdigest()[:12].upper()


def startup_read(data):
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, REGISTRY) as key: return winreg.QueryValueEx(key, startup_name(data))
    except FileNotFoundError: return None


def startup_restore(data, value):
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, REGISTRY) as key:
        if value is not None: winreg.SetValueEx(key, startup_name(data), 0, value[1], value[0])
        else:
            try: winreg.DeleteValue(key, startup_name(data))
            except FileNotFoundError: pass


def documents(data, table):
    with closing(sqlite3.connect((data / 'relay.sqlite').as_uri() + '?mode=ro', uri=True)) as database:
        return [(key, json.loads(payload)) for key, payload in database.execute('SELECT id,payload FROM ' + table)]


def fingerprint(data):
    with closing(sqlite3.connect((data / 'relay.sqlite').as_uri() + '?mode=ro', uri=True)) as database:
        # Byte-level historical payload retention, not just record counts.
        return {table: hashlib.sha256(json.dumps(database.execute('SELECT * FROM ' + table + ' ORDER BY 1').fetchall()).encode()).hexdigest()
                for table in ['work', 'inbox', 'transfers', 'connections', 'cursors', 'work_events', 'exchanges']}


def idle(data):
    with closing(sqlite3.connect((data / 'relay.sqlite').as_uri() + '?mode=ro', uri=True)) as database:
        count = database.execute("SELECT count(*) FROM work WHERE json_extract(payload,'$.state') IN ('pending','running')").fetchone()[0]
        if count: raise RuntimeError('Active work exists; finish or explicitly interrupt it before migration')


def plans(source, destination):
    settings = read(source / 'relay-config.json') if (source / 'relay-config.json').exists() else {'version': 1, 'profiles': []}
    sessions = documents(source, 'sessions')
    dispatches = documents(source, 'dispatches'); native = {}; windows = []
    def profile(value, session=None):
        old = value['root']
        if old.startswith('/srv/agent-relay/var/'): return old
        if not re.fullmatch('/srv/agent-relay-[a-zA-Z0-9-]+', old): raise ValueError('Unqualified native root')
        suffix = old.removeprefix('/srv/agent-relay-session-')
        relative = 'sessions/' + suffix if re.fullmatch('[0-9a-f]{32}', suffix) else 'profiles/' + old.removeprefix('/srv/')
        new = '/srv/agent-relay/var/apps/agent-relay/' + relative
        key = (value['distribution'], value['user']); mappings = native.setdefault(key, {})
        item = {'source': old, 'destination': new}
        if session: item['sessionId'] = session
        if old in mappings and mappings[old] != item: raise ValueError('Conflicting native root identity')
        mappings[old] = item
        return new
    for value in settings['profiles']: profile(value)
    for identifier, session in sessions:
        profile(session['template'])
        profile(dict(session['template'], root=session['root']), identifier)
        old = Path(session['windowsWorkspace']).resolve()
        expected = Path(str(source) + '-workspaces') / uuid.UUID(identifier).hex
        if old != expected: raise ValueError('Internal Windows session path is not the owned legacy path')
        windows.append({'source': str(old), 'destination': str(destination / 'workspaces' / uuid.UUID(identifier).hex)})
    for _, dispatch in dispatches: profile(dispatch['profile'])
    projects = read(source / 'relay-projects.json')
    locations = [workspace['location'] for workspace in projects['workspaces']]
    for mappings in native.values():
        for old in mappings:
            if any(location == old or location.startswith(old + '/') or old.startswith(location.rstrip('/') + '/') for location in locations if location.startswith('/')):
                raise ValueError('An explicit user workspace overlaps private storage; review required')
    groups = [{'distribution': key[0], 'user': key[1], 'roots': list(items.values()), 'partials': []} for key, items in native.items()]
    by_session = dict(sessions); workspaces = {item['id']: item for item in projects['workspaces']}
    for identifier, transfer in documents(source, 'transfers'):
        artifact = transfer.get('artifact'); session = by_session.get(transfer.get('sessionId'))
        if not artifact or not session: continue
        target = artifact['destination']; location = workspaces[target['workspaceId']]['location']
        if location.startswith('/'):
            relative = PurePosixPath(target['relativePath']); assert not relative.is_absolute() and '..' not in relative.parts
            old = str((PurePosixPath(location) / relative).parent / ('.relay-transfer-' + identifier + '.part'))
            template = session['template']; group = next(item for item in groups if item['distribution'] == template['distribution'] and item['user'] == template['user'])
            native_root = next(item['destination'] for item in group['roots'] if item['source'] == session['root'])
            group['partials'].append({'source': old, 'destination': native_root + '/' + template['client'] + '/data/staging/transfers/' + identifier + '.part', 'client': template['client']})
        else:
            base = Path(location).resolve(); final = (base / target['relativePath']).resolve(); assert final.is_relative_to(base)
            old = final.with_name('.relay-transfer-' + identifier + '.part')
            if old.exists(): windows.append({'source': str(old), 'destination': str(destination / 'staging/transfers' / (identifier + '.part'))})
    return {'native': groups, 'windows': windows, 'projectIds': [project['id'] for project in projects['projects']]}


def native_call(group, action, transaction):
    adapters = {path.name: base64.b64encode(path.read_bytes()).decode() for path in Path(__file__).parents[1].joinpath('wsl').glob('*.py')}
    payload = dict(group, action=action, transaction=transaction, adapters=adapters)
    result = subprocess.run(['wsl.exe', '-d', group['distribution'], '-u', 'root', '--exec', '/usr/bin/python3', '-c',
                             Path(__file__).with_name('native-storage.py').read_text()],
                            input=json.dumps(payload).encode(), capture_output=True, timeout=60,
                            creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        output = result.stderr or result.stdout
        message = output.decode('utf-16-le' if b'\x00' in output else 'utf-8', errors='replace')
        raise RuntimeError('Owned native storage ' + action + ' failed: ' + message[-1800:])
    return json.loads(result.stdout)


def client_changes(paths, source, destination, runtime, default_connection):
    changes = []
    for path in paths:
        if not path.exists(): continue
        raw = path.read_bytes(); value = tomllib.loads(raw.decode('utf-8-sig')).get('mcp_servers', {}).get('agent_relay')
        if not value: continue
        arguments = value.get('args', [])
        if '--data-dir' not in arguments or Path(arguments[arguments.index('--data-dir') + 1]).resolve() != source: continue
        connection = arguments[arguments.index('--connection-id') + 1] if '--connection-id' in arguments else default_connection
        if not connection: raise ValueError('The scoped connection identity is missing')
        arguments = ['mcp', '--data-dir', str(destination), '--connection-id', connection]
        fragment = '[mcp_servers.agent_relay]\ncommand = ' + json.dumps(runtime.get('mcpExecutable') or runtime_command(runtime)[0]) + '\nargs = ' + json.dumps(arguments) + '\n'
        updated = _registration.update(raw.decode('utf-8-sig'), fragment).encode()
        changes.append({'path': path, 'before': raw, 'after': updated, 'connection': connection})
    return changes


def replace_config(change, reverse=False):
    expected, desired = (change['after'], change['before']) if reverse else (change['before'], change['after'])
    _registration.replace_if_unchanged(change['path'], expected, desired)


def current_host(source, runtime):
    # Inventory only Agent Relay host candidates. Never identify a process from PID metadata alone.
    command = "Get-CimInstance Win32_Process -Filter \"Name='AgentRelay.Windows.exe' OR Name='dotnet.exe'\" | Select-Object ProcessId,ExecutablePath,CommandLine | ConvertTo-Json -Compress"
    response = subprocess.run(['pwsh', '-NoProfile', '-Command', command], capture_output=True, text=True, check=True, timeout=20)
    values = json.loads(response.stdout) if response.stdout.strip() else []
    if isinstance(values, dict): values = [values]
    matches = [value for value in values if value.get('CommandLine') and str(source) in value['CommandLine'] and ' tray ' in value['CommandLine']
               and os.path.normcase(value.get('ExecutablePath') or '') == os.path.normcase(runtime_command(runtime)[0])
               and all(part in value['CommandLine'] for part in runtime_command(runtime))]
    if len(matches) > 1: raise RuntimeError('Multiple source owners; migration refused')
    return matches[0] if matches else None


def copy_owned(source, target):
    if reparse(source): raise ValueError('Owned storage contains a reparse root')
    if source.is_dir():
        if any(reparse(path) for path in source.rglob('*')): raise ValueError('Owned Windows storage contains a reparse entry')
        shutil.copytree(source, target)
    else: shutil.copy2(source, target)


def prepare(source, destination, runtime, plan):
    destination.mkdir()
    for name in CORE:
        if (source / name).exists(): copy_owned(source / name, destination / name)
    with closing(sqlite3.connect(source / 'relay.sqlite')) as old, closing(sqlite3.connect(destination / 'relay.sqlite')) as new: old.backup(new)
    for item in plan['windows']:
        old, new = Path(item['source']), Path(item['destination'])
        if old.exists(): new.parent.mkdir(parents=True, exist_ok=True); copy_owned(old, new)
    mappings = {item['source']: item['destination'] for group in plan['native'] for item in group['roots']}
    settings = read(destination / 'relay-config.json') if (destination / 'relay-config.json').exists() else {'version': 1, 'profiles': []}
    for profile in settings['profiles']: profile['root'] = mappings.get(profile['root'], profile['root'])
    write(destination / 'relay-config.json', settings)
    with closing(sqlite3.connect(destination / 'relay.sqlite')) as database:
        for key, session in documents(destination, 'sessions'):
            session['root'] = mappings.get(session['root'], session['root'])
            session['template']['root'] = mappings.get(session['template']['root'], session['template']['root'])
            session['windowsWorkspace'] = str(destination / 'workspaces' / uuid.UUID(key).hex)
            database.execute('UPDATE sessions SET payload=? WHERE id=?', (json.dumps(session), key))
        for key, dispatch in documents(destination, 'dispatches'):
            dispatch['profile']['root'] = mappings.get(dispatch['profile']['root'], dispatch['profile']['root'])
            database.execute('UPDATE dispatches SET payload=? WHERE id=?', (json.dumps(dispatch), key))
        database.commit()
    write(destination / 'mcp-runtime.json', runtime)
    write(destination / 'maintenance.json', {'operation': 'storage-migration'})
    exported = subprocess.run(runtime_command(runtime) + ['initialize', '--data-dir', str(destination), '--connection-id', runtime['connectionId']],
                              capture_output=True, text=True, timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
    if exported.returncode: raise RuntimeError('Native connection export failed: ' + exported.stderr)


def qualify(runtime, data, root, reports, connection, expected):
    with (reports / 'mcp.stderr.log').open('a') as errors, RelayMcp(dict(runtime, connectionId=connection), data, root, errors) as client:
        assert len(client.call('tools/list')['tools']) == 26
        guides = client.call('resources/list')['resources']; assert len(guides) == 5
        for guide in guides: assert client.call('resources/read', {'uri': guide['uri']})['contents'][0]['text']
        configuration = client.tool('list_projects')['configuration']
        assert {project['id'] for project in configuration['projects']} == set(expected), 'Scoped project membership changed'
        for project in expected: client.tool('list_results', {'project_id': project, 'limit': 1})
        denied = client.call('tools/call', {'name': 'list_results', 'arguments': {'project_id': str(uuid.uuid4())}})
        assert denied.get('isError'), 'Foreign project was visible'


def migrate(args):
    root = Path(__file__).resolve().parents[3]; source = args.source.resolve(); destination = args.destination.resolve()
    assert source.is_relative_to(root / 'local/apps/agent-relay') and source != root / 'local/apps/agent-relay'
    assert destination.parent == app_var(root) and destination.name not in ['run', 'preparing', 'migrations', 'deployment']
    assert not reparse(source)
    if (source / 'migrated-to.json').exists():
        assert read(source / 'migrated-to.json')['destination'] == str(destination)
        assert (destination / 'mcp-runtime.json').is_file()
        return {'applied': False, 'alreadyMigrated': True, 'destination': str(destination)}
    assert not destination.exists(), 'Destination exists; no merge or overwrite is allowed'
    idle(source); before = fingerprint(source); plan = plans(source, destination)
    required = (source / 'relay.sqlite').stat().st_size
    for name in CORE:
        path = source / name
        if path.is_file(): required += path.stat().st_size
        elif path.is_dir(): required += sum(item.stat().st_size for item in path.rglob('*') if item.is_file())
    for item in plan['windows']:
        path = Path(item['source'])
        if path.is_file(): required += path.stat().st_size
        elif path.is_dir(): required += sum(file.stat().st_size for file in path.rglob('*') if file.is_file())
    if shutil.disk_usage(root).free < required + 32 * 1024 * 1024: raise RuntimeError('Insufficient space for the Windows migration copy')
    runtime = read(args.runtime_pointer); validate_runtime(runtime, root)
    old_runtime = read(source / 'mcp-runtime.json')
    if old_runtime.get('connectionId'): runtime['connectionId'] = old_runtime['connectionId']
    changes = client_changes(args.config, source, destination, runtime, runtime.get('connectionId'))
    transaction_id = uuid.uuid4().hex
    native_probe = [native_call(group, 'probe', transaction_id) for group in plan['native']] if args.check_native or args.apply else []
    report = {'applied': False, 'source': str(source), 'destination': str(destination), 'projects': plan['projectIds'], 'native': plan['native'],
              'windows': plan['windows'], 'clientConfigurations': [str(change['path']) for change in changes], 'nativeProbe': native_probe,
              'modelCalls': 0, 'userWorkspacesMoved': False, 'requiresClientReload': True}
    if not args.apply: return report
    transaction = app_var(root) / 'migrations' / transaction_id; transaction.mkdir(parents=True)
    write(transaction / 'plan.json', report)
    for index, change in enumerate(changes): (transaction / ('client-' + str(index) + '.before.toml')).write_bytes(change['before'])
    old_startup = startup_read(source); new_startup = startup_read(destination)
    if new_startup is not None: raise RuntimeError('A destination startup registration already exists')
    write(transaction / 'startup-before.json', {'source': old_startup, 'destination': new_startup})
    previous = current_host(source, old_runtime); started = None; applied_configs = []; native_attempted = []
    retired = transaction / 'retired'; retired.mkdir(); moved = []; workspace_moved = []
    opened = False
    try:
        # Disable only this scope's startup registration while its path is being changed.
        startup_restore(source, None)
        if previous:
            confirmed = current_host(source, old_runtime)
            assert confirmed and confirmed['ProcessId'] == previous['ProcessId']
            os.kill(previous['ProcessId'], signal.SIGTERM)
        for _ in range(60):
            try:
                with exclusive(source): break
            except RuntimeError: time.sleep(.1)
        else: raise RuntimeError('Source host did not release ownership')
        with exclusive(source):
            idle(source)
            if fingerprint(source) != before: raise RuntimeError('History changed since preflight; migration refused')
            prepare(source, destination, runtime, plan)
            for group in plan['native']:
                native_attempted.append(group); native_call(group, 'apply', transaction_id)
            assert fingerprint(destination) == before
            assert (destination / 'relay-projects.json').read_bytes() == (source / 'relay-projects.json').read_bytes()
            if args.fail_after_prepare: raise RuntimeError('Qualification-only injected failure')
            started = launch(runtime, destination, transaction)
            bindings = {value['id']: value for _, value in documents(source, 'connections')}
            for connection in set(change['connection'] for change in changes) | {runtime['connectionId']}:
                binding = bindings[connection]; assert not binding['administrator'] and not binding['revoked']
                qualify(runtime, destination, root, transaction, connection, binding['projectIds'])
            for change in changes: replace_config(change); applied_configs.append(change)
            if old_startup:
                result = subprocess.run(runtime_command(runtime) + ['startup-enable', '--data-dir', str(destination)], capture_output=True, text=True, timeout=35,
                                        creationflags=subprocess.CREATE_NO_WINDOW)
                if result.returncode or not json.loads(result.stdout)['enabled']: raise RuntimeError('Destination startup registration not confirmed: ' + result.stderr)
            for group in plan['native']: native_call(group, 'commit', transaction_id)
            for name in RETIRE:
                path = source / name
                if path.exists(): path.replace(retired / name); moved.append(name)
            for item in plan['windows']:
                path = Path(item['source'])
                if path.exists():
                    target = retired / ('workspace-' + path.name); path.replace(target); workspace_moved.append((path, target))
            # Older SQLite binaries cannot recreate a second writable store at this location.
            (source / 'relay.sqlite').mkdir()
            write(source / 'migrated-to.json', {'destination': str(destination), 'transaction': str(transaction)})
            write(destination / 'ui-host.json', dict(runtime, pid=started.pid, data=str(destination), transaction=str(transaction)))
            assert fingerprint(destination) == before
            write(transaction / 'result.json', dict(report, applied=True, healthy=True, startupPreserved=bool(old_startup), rollbackSnapshot=str(retired)))
            (destination / 'maintenance.json').unlink(); opened = True
        report.update(applied=True, healthy=True, transaction=str(transaction), pid=started.pid, startupPreserved=bool(old_startup))
        return report
    except BaseException as error:
        if opened: raise RuntimeError('Migration opened for work; automatic rollback is no longer safe') from error
        rollback_errors = []
        if started and started.poll() is None: started.terminate(); started.wait(timeout=15)
        for change in reversed(applied_configs):
            try: replace_config(change, reverse=True)
            except Exception as problem: rollback_errors.append(str(problem))
        try: startup_restore(destination, new_startup); startup_restore(source, old_startup)
        except Exception as problem: rollback_errors.append(str(problem))
        try:
            if (source / 'migrated-to.json').exists(): (source / 'migrated-to.json').unlink()
            if (source / 'relay.sqlite').is_dir(): (source / 'relay.sqlite').rmdir()
            for name in reversed(moved): (retired / name).replace(source / name)
            for path, target in reversed(workspace_moved): target.replace(path)
        except Exception as problem: rollback_errors.append(str(problem))
        for group in reversed(native_attempted):
            try: native_call(group, 'rollback', transaction_id)
            except Exception as problem: rollback_errors.append(str(problem))
        if destination.exists():
            # Keep the failed candidate in var for diagnosis. Do not remove possibly valuable data.
            destination.replace(transaction / 'failed-candidate')
        if previous and not rollback_errors:
            restored = launch(old_runtime, source, transaction)
            write(source / 'ui-host.json', dict(old_runtime, pid=restored.pid, data=str(source)))
        write(transaction / 'result.json', {'applied': False, 'error': str(error), 'rollbackPerformed': not rollback_errors, 'rollbackErrors': rollback_errors})
        raise


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['source', 'destination', 'runtime-pointer']: parser.add_argument('--' + name, required=True, type=Path)
    parser.add_argument('--config', action='append', type=Path, default=[])
    parser.add_argument('--check-native', action='store_true'); parser.add_argument('--apply', action='store_true')
    parser.add_argument('--report', type=Path, help='Explicit operator report destination')
    parser.add_argument('--fail-after-prepare', action='store_true', help=argparse.SUPPRESS)
    arguments = parser.parse_args(); result = migrate(arguments)
    if arguments.report:
        arguments.report.parent.mkdir(parents=True, exist_ok=True); write(arguments.report, result)
        print(json.dumps({'report': str(arguments.report), 'applied': result['applied'], 'projects': len(result.get('projects', []))}))
    else: print(json.dumps(result, indent=2))

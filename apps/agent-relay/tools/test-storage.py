"""Qualify real native storage defaults and a reversible, isolated Windows migration; no WSL/model.

Usage: python -B test-storage.py --runtime-pointer <prepared pointer>. Creates explicit developer fixtures only.
"""
import argparse
from contextlib import closing
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess
import time
from types import SimpleNamespace
from unittest.mock import patch
import uuid
from deployment import launch
from runtime_layout import runtime_command, app_var
from relay_mcp import RelayMcp

spec = importlib.util.spec_from_file_location('storage_migration', Path(__file__).with_name('migrate-storage.py'))
migration = importlib.util.module_from_spec(spec); spec.loader.exec_module(migration)


def rehearse_enabled_rollback(runtime, arguments):
    source, destination = arguments.source, arguments.destination
    entries = {scope: migration.startup_entry(runtime, scope) for scope in (source, destination)}
    original = dict(entries[source]['enabledEntry'], kind=2)
    entries[source]['entry'] = original
    events = []

    def startup(_runtime, scope, **change):
        state = entries[scope]
        if change:
            action = change['action']
            assert state['entry'] == change['expected'], (scope, change, state)
            value = state['enabledEntry'] if action == 'enable' else change.get('entry')
            state['entry'] = value
            events.append((scope, action))
        return dict(state)

    original_write = migration.write

    def fail_after_retiring_source(path, value):
        if path == destination / 'ui-host.json':
            assert entries[destination]['entry'] == entries[destination]['enabledEntry']
            assert not (source / 'mcp-runtime.json').exists()
            raise RuntimeError('Injected failure after enabling destination startup')
        original_write(path, value)

    with patch.object(migration, 'startup_entry', startup), patch.object(migration, 'write', fail_after_retiring_source):
        try:
            migration.migrate(arguments)
        except RuntimeError as error:
            assert 'Injected failure after enabling destination startup' in str(error), error
        else:
            raise AssertionError('Expected a late migration failure')
    assert events == [(source, 'disable'), (destination, 'enable'), (source, 'disable'),
                      (destination, 'disable'), (source, 'restore')]
    assert entries[source]['entry'] == original and entries[destination]['entry'] is None


def main(pointer):
    root = Path(__file__).resolve().parents[3]; runtime = migration.read(pointer)
    identifier = uuid.uuid4().hex
    fixture = root / 'local/apps/agent-relay/qualification' / ('storage-' + identifier); fixture.mkdir(parents=True)
    source = fixture / 'old-data'; destination = app_var(root) / ('qualification-' + identifier)
    env = {key: value for key, value in os.environ.items() if not key.startswith(('DOTNET_', 'NUGET_'))}
    env.update(DOTNET_ROOT=str(fixture / 'missing-sdk'), PATH=str(Path(os.environ['SystemRoot']) / 'System32'))
    def run(command, expected=0):
        result = subprocess.run([str(item) for item in command], capture_output=True, text=True, encoding='utf-8', errors='replace',
                                env=env, cwd=fixture, timeout=45, creationflags=subprocess.CREATE_NO_WINDOW)
        assert result.returncode == expected, result.stdout + result.stderr
        return result.stdout
    exe = runtime_command(runtime)[0]
    run([exe, 'initialize', '--data-dir', source])
    for directory in ['main', 'peer']: (fixture / directory).mkdir()
    run([shutil.which('pwsh.exe'), '-NoProfile', '-File', root / 'apps/agent-relay/doc/using/examples/new-project-request.ps1',
         '-DataDirectory', source, '-Name', 'Storage qualification', '-MainWorkspace', fixture / 'main', '-PeerWorkspace', fixture / 'peer',
         '-OutputDirectory', fixture / 'request', '-LocalOnly'])
    for name in ['configure-project', 'connection']: run([exe, 'admin', '--data-dir', source, '--input', fixture / 'request' / (name + '.request.json')])
    context = migration.read(fixture / 'request/example-context.json')
    run([exe, 'initialize', '--data-dir', source, '--connection-id', context['connectionId']])
    config = fixture / 'config.toml'; config.write_text('model="preserve-me"\n\n' + (source / 'mcp.codex.toml').read_text())
    before = migration.fingerprint(source); configuration = (source / 'relay-projects.json').read_bytes(); raw = config.read_bytes()
    reports = fixture / 'reports'; reports.mkdir()
    host = launch(runtime, source, reports)
    arguments = SimpleNamespace(source=source, destination=destination, runtime_pointer=pointer, config=[config], check_native=False, apply=False, fail_after_prepare=False)
    original_startup = migration.startup_entry(runtime, source)['entry']
    assert original_startup is None, 'The fictional scope must start without a Windows startup entry'
    try:
        assert not migration.migrate(arguments)['applied']
        assert host.poll() is None and config.read_bytes() == raw and not destination.exists()
        # Refuse active work before stopping the owner or writing a destination.
        with closing(sqlite3.connect(source / 'relay.sqlite')) as database:
            database.execute('INSERT INTO work VALUES (?,?)', (identifier, '{"state":"pending"}')); database.commit()
        arguments.apply = True
        try: migration.migrate(arguments)
        except RuntimeError as error: assert 'Active durable work' in str(error)
        else: raise AssertionError('Active work migrated')
        assert host.poll() is None and not destination.exists()
        with closing(sqlite3.connect(source / 'relay.sqlite')) as database:
            database.execute('DELETE FROM work WHERE id=?', (identifier,)); database.commit()
        arguments.fail_after_prepare = True
        try: migration.migrate(arguments)
        except RuntimeError as error: assert 'injected failure' in str(error)
        else: raise AssertionError('Failure injection did not run')
        assert migration.fingerprint(source) == before and config.read_bytes() == raw and not destination.exists()
        assert migration.startup_entry(runtime, source)['entry'] == original_startup
        # The rollback restarted its exact source host; the next invocation rediscovers it.
        arguments.fail_after_prepare = False
        rehearse_enabled_rollback(runtime, arguments)
        assert migration.fingerprint(source) == before and config.read_bytes() == raw and not destination.exists()
        result = migration.migrate(arguments)
        assert result['healthy'] and migration.fingerprint(destination) == before
        assert migration.startup_entry(runtime, destination)['entry'] is None
        assert (destination / 'relay-projects.json').read_bytes() == configuration
        assert 'model="preserve-me"' in config.read_text()
        assert (source / 'relay.sqlite').is_dir() and not (source / 'connections').exists()
        assert migration.migrate(arguments)['alreadyMigrated']
        # Retired old binary invocation cannot create a second writable database.
        run([exe, 'initialize', '--data-dir', source], expected=1)
        owner = migration.current_host(destination, runtime); assert owner
        os.kill(owner['ProcessId'], 15)
        # Published-layout default works without local/ and independently of cwd or a system .NET SDK.
        packaged = fixture / 'standalone'; binary = packaged / 'app'; shutil.copytree(Path(exe).parent, binary)
        migration.write(binary / 'installation.json', {'version': 1, 'root': '..'})
        run([binary / 'AgentRelay.Windows.exe', 'initialize'])
        default = packaged / 'var/apps/agent-relay/default'
        assert (default / 'mcp-runtime.json').is_file() and not (packaged / 'local').exists()
        standalone = subprocess.Popen([str(binary / 'AgentRelay.Windows.exe'), 'tray', '--lifetime-ms', '15000'], cwd=fixture, env=env,
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            for _ in range(30):
                assert standalone.poll() is None
                if (default / 'relay.sqlite').is_file(): break
                time.sleep(.1)
            assert (default / 'relay.sqlite').is_file() and not (packaged / 'local').exists()
            run([binary / 'AgentRelay.Windows.exe', 'profiles'])
            run([binary / 'AgentRelay.Windows.exe', 'tray'])
            assert standalone.poll() is None
        finally:
            if standalone.poll() is None: standalone.terminate()
            standalone.wait(timeout=10)
        report = {'passed': True, 'dryRunNonMutating': True, 'activeWorkRefused': True, 'injectedRollback': True,
                  'disabledStartupStayedAbsent': True, 'enabledStartupLateRollbackSimulated': True,
                  'historyAndPermissionsPreserved': True, 'clientTableOnly': True, 'idempotent': True, 'retiredStoreBlocked': True,
                  'noLocalDefault': True, 'systemDotnetAbsent': True, 'modelCalls': 0, 'wslCalls': 0, 'fixture': str(fixture)}
        migration.write(fixture / 'result.json', report)
        print(json.dumps(report))
    finally:
        for data in [source, destination]:
            owner = migration.current_host(data, runtime)
            if owner: os.kill(owner['ProcessId'], 15)
        # Keep all reports/data for review; remove only the bulky freshly copied standalone runtime.
        binary = fixture / 'standalone/app'
        if binary.is_dir(): shutil.rmtree(binary)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--runtime-pointer', type=Path, required=True)
    main(parser.parse_args().runtime_pointer.resolve())

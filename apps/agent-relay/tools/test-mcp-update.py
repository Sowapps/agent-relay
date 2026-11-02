"""Qualify retained MCP broker, migration/errors, catalog switching and update idempotence; no WSL/model."""
import argparse
from contextlib import closing
import importlib.util
import json
from pathlib import Path
import queue
import shutil
import sqlite3
import subprocess
import time
import tomllib
import uuid
from deployment import launch, publish
from relay_mcp import RelayMcp
from runtime_layout import runtime_command


def main(first_pointer, next_pointer):
    root = Path(__file__).resolve().parents[3]
    identifier = uuid.uuid4().hex
    fixture = root / 'local/apps/agent-relay/qualification' / ('mcp-update-' + identifier); fixture.mkdir(parents=True)
    data = root / 'var/apps/agent-relay/qualification' / ('mcp-update-' + identifier)
    reports = fixture / 'reports'; reports.mkdir(); (fixture / 'workspace').mkdir()
    first = json.loads(first_pointer.read_text(encoding='utf-8-sig')); second = json.loads(next_pointer.read_text(encoding='utf-8-sig'))
    exe = runtime_command(first)[0]
    def write(path, value): path.write_text(json.dumps(value, indent=2), encoding='utf-8')
    def run(arguments):
        result = subprocess.run([str(x) for x in arguments], capture_output=True, encoding='utf-8', errors='replace', timeout=45, creationflags=subprocess.CREATE_NO_WINDOW)
        if result.returncode: raise RuntimeError(result.stdout + result.stderr)
        return result.stdout
    run([exe, 'initialize', '--data-dir', data])
    request = fixture / 'request'
    run([shutil.which('pwsh.exe'), '-NoProfile', '-File', root / 'apps/agent-relay/doc/using/examples/new-project-request.ps1',
         '-DataDirectory', data, '-Name', 'MCP update fixture', '-MainWorkspace', fixture / 'workspace', '-PeerWorkspace', fixture / 'workspace', '-OutputDirectory', request, '-LocalOnly'])
    context = json.loads((request / 'example-context.json').read_text(encoding='utf-8-sig'))
    for name in ['configure-project.request.json', 'connection.request.json']: run([exe, 'admin', '--data-dir', data, '--input', request / name])
    run([exe, 'initialize', '--data-dir', data, '--connection-id', context['connectionId']])
    exported = json.loads((data / 'mcp-runtime.json').read_text(encoding='utf-8'))
    broker = exported['mcpExecutable']; first['connectionId'] = context['connectionId']; second['connectionId'] = context['connectionId']
    host = launch(first, data, reports)
    result = {'modelCalls': 0, 'nativeAgentCalls': 0, 'fixture': str(fixture)}
    client = None
    try:
        with (reports / 'mcp.stderr.log').open('w', encoding='utf-8') as errors:
            client = RelayMcp(dict(first, executable=broker), data, root, errors)
            assert len(client.inventory) == 26
            before = client.tool('get_connection_status'); result['initialVersion'] = before['version']
            assert not before['clientRefreshRequired']
            assert client.tool('list_projects')['configuration']['projects'][0]['id'] == context['projectId']
            # A future/optional host field must survive the facade's raw JSON path.
            assert 'archiveRequested' in client.tool('list_projects')['configuration']['projects'][0]
            unknown = fixture / 'unconfigured'; unknown.mkdir()
            with RelayMcp(dict(first, connectionId=str(uuid.uuid4())), unknown, root, errors) as missing:
                failed = missing.call('tools/call', {'name': 'list_projects', 'arguments': {}})
                assert failed['isError'] and failed['structuredContent']['error']['code'] == 'authentication_required'
                assert not (unknown / 'relay.sqlite').exists()
                assert 'SQLite' not in failed['content'][0]['text']
            wrong_id = uuid.uuid4()
            wrong_credential = data / 'connections' / (wrong_id.hex + '.bin')
            shutil.copy2(data / 'connections' / (uuid.UUID(context['connectionId']).hex + '.bin'), wrong_credential)
            try:
                with RelayMcp(dict(first, connectionId=str(wrong_id)), data, root, errors) as wrong:
                    denied = wrong.call('tools/call', {'name':'list_projects','arguments':{}})
                    assert denied['isError'] and denied['structuredContent']['error']['code']=='authentication_required'
            finally: wrong_credential.unlink()
            retired = fixture / 'retired'; retired.mkdir(); (retired / 'relay.sqlite').mkdir()
            write(retired / 'migrated-to.json', {'destination': str(data)})
            with RelayMcp(dict(first, executable=broker), retired, root, errors) as moved:
                assert moved.tool('list_projects')['configuration']['projects'][0]['id'] == context['projectId']
                assert (retired / 'relay.sqlite').is_dir() and not (retired / 'connections').exists()
            # Initialization/registration update must preserve an independent scoped ID and all other TOML settings.
            spec = importlib.util.spec_from_file_location('update_connections', Path(__file__).with_name('update-connections.py'))
            update = importlib.util.module_from_spec(spec); spec.loader.exec_module(update)
            write(data / 'mcp-contract.json', before['host']['identity'])
            config = fixture / 'client.toml'
            config.write_text('model = "preserved"\n[mcp_servers.agent_relay]\ncommand = ' + json.dumps(exe) + '\nargs = ' + json.dumps(['mcp', '--data-dir', str(data), '--connection-id', context['connectionId']]) + '\nrequired = true\n', encoding='utf-8')
            one = update.update(data, reports / 'registration-first', [config], fixture / 'absent-socket')
            raw = config.read_bytes(); two = update.update(data, reports / 'registration-second', [config], fixture / 'absent-socket')
            assert one['updated'] == 1 and two['updated'] == 0 and config.read_bytes() == raw
            assert tomllib.loads(raw.decode())['model'] == 'preserved' and context['connectionId'] in raw.decode()
            assert one['clientRefresh']['state'] == 'unsupported' and one['clientRefresh']['sameClientVerified'] is False
            result['idempotentRegistrationAndUnsupportedClient'] = True
            # Keep the original outer stdio process alive while host/runtime/contract change.
            original_pid = client.process.pid
            host.terminate(); host.wait(timeout=10)
            write(data / 'mcp-runtime.json', dict(second, mcpExecutable=broker))
            host = launch(second, data, reports)
            time.sleep(1.2)
            notices = []
            while True:
                try: notices.append(client.incoming.get_nowait())
                except queue.Empty: break
            assert any(item and item.get('method') == 'notifications/tools/list_changed' for item in notices)
            changed = client.tool('get_connection_status'); result['updatedVersion'] = changed['version']
            assert changed['version'] != before['version'] and changed['contractHash'] != before['contractHash'] and changed['clientRefreshRequired']
            blocked = client.call('tools/call', {'name': 'execute_command', 'arguments': {'project_id': context['projectId'], 'endpoint_id': context['peerId'], 'workspace_id': context['peerWorkspaceId'], 'command_id': 'not-executed'}})
            assert blocked['isError'] and blocked['structuredContent']['error']['code'] == 'connection_outdated'
            assert blocked['structuredContent']['error']['execution'] == 'not_started'
            assert client.process.pid == original_pid
            client.call('tools/list')
            assert not client.tool('get_connection_status')['clientRefreshRequired']
            guide = client.call('resources/read', {'uri': 'agent-relay://docs/bootstrap'})['contents'][0]['text']
            assert guide.replace('\r\n', '\n') == (Path(second['executable']).parent / 'documentation/bootstrap.md').read_text(encoding='utf-8-sig').replace('\r\n', '\n')
            # A contract version cannot be reused, even when a host is currently running.
            prepared = fixture / 'prepared'; prepared.mkdir(); write(prepared / 'mcp-runtime.json', second)
            contract = json.loads((Path(second['executable']).parent / 'mcp-contract.json').read_text(encoding='utf-8-sig'))
            write(data / 'mcp-contract-history.json', {contract['version']: 'different-contract'})
            try: publish(data, prepared, reports, False)
            except ValueError as error: assert 'different MCP contract' in str(error)
            else: raise AssertionError('A deployed version was reused for a changed contract')
            assert host.poll() is None
            write(data / 'mcp-contract-history.json', {contract['version']: contract['contractHash']})
            # Explicit close drains the outer broker, preserves credentials and does not auto-reconnect.
            instance = client.tool('get_connection_status')['instanceId']; assert instance
            (data / 'mcp-connections' / (uuid.UUID(instance).hex + '.close')).write_text('qualification close', encoding='utf-8')
            client.process.wait(timeout=6)
            assert host.poll() is None and (data / 'connections' / (uuid.UUID(context['connectionId']).hex + '.bin')).is_file()
            client.close(); client = None
            with closing(sqlite3.connect(data / 'relay.sqlite')) as database:
                assert database.execute('SELECT count(*) FROM work').fetchone()[0] == 0
            result.update(passed=True, retainedStdioGenerationSwitch=True, explicitCatalogGate=True, notificationSent=True,
                          currentResources=True, migrationPreservesIdentity=True, missingCredentialCreatesNoDatabase=True,
                          versionReuseRefusedBeforeStop=True, explicitClosureRetainsPermissions=True, noWorkReplayed=True)
    finally:
        if client is not None: client.close()
        if host.poll() is None: host.terminate(); host.wait(timeout=10)
    write(fixture / 'result.json', result); print(json.dumps(result))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime-pointer', required=True, type=Path); parser.add_argument('--next-runtime-pointer', required=True, type=Path)
    args = parser.parse_args(); main(args.runtime_pointer.resolve(), args.next_runtime_pointer.resolve())

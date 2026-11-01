"""Qualify project lifecycle through actual stdio MCP on isolated local-only projects; no WSL/model.

Usage: --runtime-pointer <explicit prepared native pointer>. Stops only the host it creates.
"""
import argparse
from contextlib import closing
import json
from pathlib import Path
import shutil
import sqlite3
import subprocess
import uuid
import time
from datetime import datetime, timezone
from deployment import launch
from relay_mcp import RelayMcp
from runtime_layout import runtime_command


def main(pointer):
    root = Path(__file__).resolve().parents[3]; runtime = json.loads(pointer.read_text(encoding='utf-8-sig'))
    fixture = root / 'local/apps/agent-relay/qualification' / ('project-lifecycle-' + uuid.uuid4().hex)
    fixture.mkdir(parents=True); data = fixture / 'data'; reports = fixture / 'reports'; reports.mkdir()
    for name in ['main', 'peer']: (fixture / name).mkdir()
    def run(arguments):
        result = subprocess.run([str(item) for item in arguments], capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=40, creationflags=subprocess.CREATE_NO_WINDOW)
        assert result.returncode == 0, result.stdout + result.stderr
    def write(path, value): path.write_text(json.dumps(value, indent=2), encoding='utf-8')
    exe = runtime_command(runtime)[0]; run([exe, 'initialize', '--data-dir', data]); contexts = []
    for index in range(2):
        request = fixture / ('request-' + str(index))
        run([shutil.which('pwsh.exe'), '-NoProfile', '-File', root / 'apps/agent-relay/doc/using/examples/new-project-request.ps1',
             '-DataDirectory', data, '-Name', 'Lifecycle ' + str(index), '-MainWorkspace', fixture / 'main', '-PeerWorkspace', fixture / 'peer', '-OutputDirectory', request, '-LocalOnly'])
        run([exe, 'admin', '--data-dir', data, '--input', request / 'configure-project.request.json'])
        contexts.append(json.loads((request / 'example-context.json').read_text(encoding='utf-8-sig')))
        if index == 0: run([exe, 'admin', '--data-dir', data, '--input', request / 'connection.request.json'])
    runtime['connectionId'] = contexts[0]['connectionId']; project = contexts[0]['projectId']
    host = launch(runtime, data, reports)
    try:
        with (reports / 'mcp.stderr.log').open('w') as errors, RelayMcp(runtime, data, root, errors) as mcp:
            assert 'set_project_state' in {tool['name'] for tool in mcp.call('tools/list')['tools']}
            request = {'project_id': project, 'state': 'archived', 'expected_state': 'active'}
            denied = mcp.call('tools/call', {'name': 'set_project_state', 'arguments': request})
            assert denied.get('isError') and 'capability_denied' in denied['content'][0]['text']
            configuration = json.loads((data / 'relay-projects.json').read_text(encoding='utf-8-sig'))
            member = next(item for item in configuration['memberships'] if item['projectId'] == project and item['endpointId'] == contexts[0]['mainId'])
            member['capabilities'] += ['project.manage', 'command.execute']; member['commandIds'] = ['fixture-list']
            admin = fixture / 'grant.request.json'; write(admin, {'operation': 'configure_projects', 'projectsConfiguration': configuration})
            run([exe, 'admin', '--data-dir', data, '--input', admin])
            denied = mcp.call('tools/call', {'name': 'set_project_state', 'arguments': dict(request, project_id=contexts[1]['projectId'])})
            assert denied.get('isError') and 'capability_denied' in denied['content'][0]['text']
            # Insert a fictional uncertain record so the real host must retain intent across restart.
            identifier = str(uuid.uuid4()); stamp = datetime.now(timezone.utc).isoformat()
            record = {'request': {'id': identifier, 'profileId': contexts[0]['peerId'], 'kind': 'command',
                       'scope': {'projectId': project, 'senderId': contexts[0]['mainId'], 'recipientId': contexts[0]['peerId']}},
                      'state': 'unknown', 'createdAt': stamp, 'updatedAt': stamp}
            with sqlite3.connect(data / 'relay.sqlite') as database:
                database.execute('INSERT INTO work VALUES(?,?)', (identifier, json.dumps(record)))
            result = mcp.tool('set_project_state', request)['project']
            assert result['state'] == 'active' and result['archiveRequested']
            assert mcp.tool('set_project_state', request)['project']['archiveRequested']  # Lost-response retry.
            blocked = mcp.call('tools/call', {'name': 'execute_command', 'arguments': {'project_id': project, 'endpoint_id': contexts[0]['peerId'], 'workspace_id': contexts[0]['peerWorkspaceId'], 'command_id': 'fixture-list'}})
            assert blocked.get('isError') and 'project_draining' in blocked['content'][0]['text']
        host.terminate(); host.wait(timeout=10); host = launch(runtime, data, reports)
        with (reports / 'mcp.stderr.log').open('a') as errors, RelayMcp(runtime, data, root, errors) as mcp:
            current = lambda: mcp.tool('list_projects')['configuration']['projects'][0]
            assert current()['archiveRequested'] and current()['state'] == 'active'
            deadline = time.monotonic() + 12
            while not current().get('archiveDiagnostic') and time.monotonic() < deadline: time.sleep(.15)
            assert 'unknown' in current()['archiveDiagnostic']
            # Only this explicit fictional record is resolved, without executing any command.
            with sqlite3.connect(data / 'relay.sqlite') as database:
                database.execute("UPDATE work SET payload=json_set(payload,'$.state','failed') WHERE id=?", (identifier,))
            deadline = time.monotonic() + 12
            while current()['state'] != 'archived' and time.monotonic() < deadline: time.sleep(.15)
            assert current()['state'] == 'archived' and not current()['archiveRequested']
            assert len(mcp.tool('list_results', {'project_id': project})['operations']['items']) == 1
            assert mcp.tool('set_project_state', {'project_id': project, 'state': 'active', 'expected_state': 'archived'})['project']['state'] == 'active'
        with closing(sqlite3.connect(data / 'relay.sqlite')) as database:
            assert database.execute('SELECT count(*) FROM work').fetchone()[0] == 1
            assert database.execute('SELECT count(*) FROM sessions').fetchone()[0] == 0
        result = {'passed': True, 'scopedPermissionAndForeignProjectRefusal': True, 'archiveIdempotence': True, 'archivedDiscoveryAndHistory': True,
                  'newCommandRefusedBeforeExecution': True, 'restartPersistence': True, 'durableDeferredArchival': True, 'unknownDiagnostic': True, 'automaticFinalization': True, 'reactivationWithoutWorkOrSession': True, 'modelCalls': 0, 'wslCalls': 0, 'fixture': str(fixture)}
        write(fixture / 'result.json', result); print(json.dumps(result))
    finally:
        if host.poll() is None: host.terminate(); host.wait(timeout=10)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--runtime-pointer', required=True, type=Path)
    main(parser.parse_args().runtime_pointer.resolve())

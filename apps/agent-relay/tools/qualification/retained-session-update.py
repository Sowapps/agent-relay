"""Qualify complete update without losing work or a retained WSL conversation."""

import argparse
from functools import partial
import hashlib
import json
import os
from pathlib import Path
import signal
import sqlite3
import subprocess
import sys
import time
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from complete_update import invoke, recovery_updater
from deployment import launch
from host_identity import identity, resolve_host
from portable_fixture import extract_verified, run
from relay_mcp import McpRpc


AGENT = r'''#!/usr/bin/python3
import json,os,pathlib,re,select,subprocess,sys,time,tty,uuid
from datetime import datetime,timezone
if sys.argv[1:3]==['login','status']: sys.exit(0)
config=pathlib.Path(os.environ['CODEX_HOME'])
folder=config.parent
root=folder.parent
sys.path.insert(0,str(root/'install'))
import native_clients
from relay_files import commit,context,store_message
scope=context(root,'codex')
work=pathlib.Path(scope['workspace'])
history=config/'sessions'
history.mkdir(exist_ok=True)
known=list(history.glob('*.jsonl'))
if known:
    native_id=known[0].stem
    assert sys.argv[-2:]==['resume',native_id]
else:
    native_id=str(uuid.uuid4())
    (history/(native_id+'.jsonl')).write_text(json.dumps({'type':'session_meta','payload':{'id':native_id}})+'\n')
commit(work/'native-launch.json', {'nativeId':native_id, 'resumed':bool(known)})
tty.setraw(0)
commit(folder/'data/mcp-initialized.json',{'initialized':True,'nativeAncestors':[native_clients.process_stamp(os.getpid())['generation']]})

def check_participant():
    launch=json.loads((folder/'launch-state.json').read_text())['participantMcp']
    entry=native_clients.project_mcp_entry(launch)
    child=subprocess.Popen([entry['command'],*entry['args']],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8')
    def call(identifier,method,args):
        child.stdin.write(json.dumps(dict(jsonrpc='2.0',id=identifier,method=method,params=args))+'\n')
        child.stdin.flush()
        if not select.select([child.stdout],[],[],45)[0]: raise TimeoutError(method)
        value=json.loads(child.stdout.readline())
        assert value.get('id')==identifier and 'error' not in value, value
        return value['result']
    try:
        initialization=call(1,'initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='owned fixture',version='1')))
        result=call(2,'tools/call',dict(name='invoke_operation',arguments=dict(call_id=str(uuid.uuid4()),
            operation_id='list_projects',revision=1,arguments={})))
        commit(work/'participant-proof.json',{'version':initialization['serverInfo']['version'],
            'memberships':result['structuredContent']['configuration']['memberships']})
    except Exception as error:
        child.stdin.close()
        try: child.wait(timeout=8)
        except subprocess.TimeoutExpired: child.kill(); child.wait()
        commit(work/'participant-proof.json',{'error':str(error),'exitCode':child.returncode,'stderr':child.stderr.read()[-2500:]})
    finally:
        child.stdin.close()
        try: child.wait(timeout=8)
        except subprocess.TimeoutExpired: child.kill(); child.wait()

while True:
    content=b''
    while not content.endswith(b'\r'):
        value=os.read(0,1)
        if not value: sys.exit(0)
        content+=value
    match=re.search(rb'Relay request ([0-9a-f-]{36})',content)
    if not match: continue
    identifier=match[1].decode()
    if b'hold for update' in content:
        commit(work/'entered.json', {'requestId':identifier})
        end=time.monotonic()+100
        while not (work/'release').exists() and time.monotonic()<end: time.sleep(.1)
        assert (work/'release').exists()
    elif b'check participant MCP' in content: check_participant()
    with (history/(native_id+'.jsonl')).open('a') as stream:
        stream.write(json.dumps({'type':'qualification_turn','requestId':identifier})+'\n')
    store_message(root,'codex',{'id':str(uuid.uuid4()),'profileId':'codex','sender':'codex-wsl','recipient':'windows-pilot',
        'text':'Owned fixture reply','inReplyTo':identifier,'receivedAt':datetime.now(timezone.utc).isoformat(),
        'sessionId':scope['sessionId'],'projectId':scope['projectId'],'senderEndpointId':scope['recipientId'],'recipientEndpointId':scope['senderId']})
'''


def write(path, value):
    path.write_text(json.dumps(value, indent=2), encoding='utf-8')


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def wait_for(check, description, seconds=35):
    until = time.monotonic() + seconds
    while time.monotonic() < until:
        value = check()
        if value:
            return value
        time.sleep(.25)
    raise TimeoutError(description)


def qualify(baseline, candidate, output, distribution, linux_user):
    output.mkdir()
    installation = output / 'installation'
    data = installation / 'var/apps/agent-relay/default'
    old_version, digest = extract_verified(baseline, installation)
    runtime = {'executable': str(installation / 'app/AgentRelay.Windows.exe')}
    before = partial(run, cwd=installation, environment=dict(os.environ))
    update = partial(invoke, installation=installation, data=data)
    candidate_runtime = {'executable': str(candidate / 'app/AgentRelay.Windows.exe')}
    project, owner, agent, workspace, binding, participant, session, first, second = (str(uuid.uuid4()) for _ in range(9))
    native_root = '/srv/agent-relay/var/apps/agent-relay/sessions/' + session.replace('-', '')
    native_workspace = '/tmp/agent-relay-update-' + session.replace('-', '')
    result = {'baseVersion': old_version, 'baseSha256': digest, 'sessionId': session, 'initialRequest': first,
              'secondRequest': second, 'modelCalls': 0, 'stableChanged': False}

    def wsl(script, payload):
        operation = subprocess.run(['wsl.exe', '-d', distribution, '-u', linux_user, '--exec', 'python3', '-B', '-c', script],
                                   input=json.dumps(payload), text=True, encoding='utf-8', capture_output=True, timeout=22,
                                   creationflags=subprocess.CREATE_NO_WINDOW)
        if operation.returncode:
            raise RuntimeError(operation.stderr)
        return operation.stdout.strip()

    def native_read(path):
        return wsl("import json,pathlib,sys; p=pathlib.Path(json.load(sys.stdin)); print(p.read_text() if p.is_file() else '')", path)

    def completed(identifier):
        with sqlite3.connect((data / 'relay.sqlite').as_uri() + '?mode=ro', uri=True) as database:
            row = database.execute('SELECT payload FROM work WHERE id=?', (identifier,)).fetchone()
        record = json.loads(row[0]) if row else {}
        return record if record.get('state') == 'completed' else None

    def configure(configuration, executable):
        path = output / 'configure.request.json'
        write(path, {'version': 1, 'operation': 'configure_projects', 'projectsConfiguration': configuration})
        before(executable, 'admin', '--data-dir', data, '--input', path)

    wsl("""import json,pathlib,sys
p=json.load(sys.stdin); root=pathlib.Path(p['path']); root.mkdir(mode=0o700)
(root/'owner').write_text(p['session'])
(root/'agent').write_text(p['agent']); (root/'agent').chmod(0o700)
""", {'path': native_workspace, 'session': session, 'agent': AGENT})
    host = None
    try:
        before(runtime['executable'], 'initialize', '--data-dir', data)
        settings = {'version': 1, 'projects': [{'id': project, 'name': 'Owned update fixture'}],
                    'endpoints': [{'id': owner, 'name': 'Fixture requester', 'adapterId': 'local-files', 'bindingId': 'fixture'},
                                  {'id': agent, 'name': 'Fixture peer', 'adapterId': 'wsl-agent', 'bindingId': 'fixture-agent'}],
                    'workspaces': [{'id': workspace, 'endpointId': agent, 'name': 'Fixture workspace', 'location': native_workspace}],
                    'memberships': [{'projectId': project, 'endpointId': owner, 'role': 'main',
                                     'capabilities': ['history.read', 'session.manage', 'message.send', 'message.receive', 'file.read'],
                                     'workspaces': [{'workspaceId': workspace, 'read': True}]},
                                    {'projectId': project, 'endpointId': agent, 'role': 'secondary',
                                     'capabilities': ['history.read', 'message.receive'], 'workspaces': []}]}
        configure(settings, runtime['executable'])
        for reference, endpoint in ((binding, owner), (participant, agent)):
            path = output / ('binding-' + reference + '.json')
            write(path, {'version': 1, 'operation': 'connection',
                         'connection': {'id': reference, 'endpointId': endpoint, 'projectIds': [project]}})
            before(runtime['executable'], 'admin', '--data-dir', data, '--input', path)
        before(runtime['executable'], 'initialize', '--data-dir', data, '--connection-id', binding)
        write(data / 'relay-config.json', {'version': 1, 'profiles': [{'id': 'fixture-agent', 'name': 'Fixture agent',
              'distribution': distribution, 'user': linux_user, 'root': native_root, 'client': 'codex',
              'nativeExecutable': native_workspace + '/agent', 'commands': []}]})
        host = launch(runtime, data, output)
        fragment = read(data / 'mcp.windows.json')['mcpServers']['agent_relay']
        result['hostBefore'] = host.pid
        with (output / 'mcp.stderr.log').open('w', encoding='utf-8') as errors:
            with McpRpc([fragment['command'], *fragment['args']], installation, errors) as client:
                def call(name, args=None, revision=1):
                    return client.tool('invoke_operation', {'call_id': str(uuid.uuid4()), 'operation_id': name,
                                       'revision': revision, 'arguments': args or {}}, timeout=60)

                def ready():
                    observed = call('get_session', {'session_id': session})['session']
                    if observed['state'] not in ('starting', 'ready'):
                        raise RuntimeError(json.dumps(observed))
                    return observed if observed['state'] == 'ready' else None

                call('open_session', {'project_id': project, 'endpoint_id': agent, 'workspace_id': workspace,
                                      'session_id': session, 'name': 'Retained update fixture'})
                wait_for(ready, 'Baseline native session did not start')
                initial = json.loads(native_read(native_workspace + '/native-launch.json'))
                history_file = native_root + '/codex/config/sessions/' + initial['nativeId'] + '.jsonl'
                assert initial['resumed'] is False
                prepared = update(candidate_runtime, action='prepare', candidate=candidate)
                assert prepared['activation']['nativeAdapters'] is True
                call('send_session_message', {'session_id': session, 'request_id': first, 'text': 'hold for update'})
                wait_for(lambda: native_read(native_workspace + '/entered.json'), 'Native fixture never began its request')
                updater = recovery_updater(data)
                drained = update(updater, action='resume')
                assert drained['phase'] == 'awaiting_work' and drained['errorCode'] == 'busy', drained
                assert not completed(first) and host.poll() is None
                result['activeDrain'] = drained['errorCode']
                wsl("import json,pathlib,sys; pathlib.Path(json.load(sys.stdin)).touch()", native_workspace + '/release')
                original = wait_for(lambda: completed(first), 'Original request did not finish during drain')
                deferred = update(updater, action='resume')
                assert deferred['errorCode'] == 'update_native_session_open' and deferred['phase'] == 'awaiting_work', deferred
                assert not deferred['activation']['resumeAuthorized']
                assert host.poll() is None and not (data / 'promotion.lock').exists()
                result['idleTerminalDeferral'] = {'code': deferred['errorCode'], 'hostUnchanged': True, 'admissionReleased': True}
                old_history = native_read(history_file)
                assert first in old_history
                closed = call('close_session', {'session_id': session}, revision=2)['session']
                assert closed['id'] == session and closed['state'] == 'closed', closed

                def finish():
                    step = update(updater, action='resume')
                    if step['phase'] in ('recovery_required', 'rolled_back'):
                        raise RuntimeError(json.dumps(step))
                    return step if step['phase'] == 'complete' else None

                state = wait_for(finish, 'Update did not complete on the retained session', 95)
                result['transaction'] = state['id']
                result['candidateVersion'] = state['version']
                assert completed(first) == original and native_read(history_file) == old_history
                status = client.tool('get_connection_status', timeout=30)
                assert status['version'] == state['version'] and status['host']['connectionId'] == binding, status
                result['hostAfter'] = status['host']['processId']
                settings['memberships'][1]['nativeConnectionId'] = participant
                configure(settings, read(data / 'mcp-runtime.json')['executable'])
                reopened = call('reopen_session', {'session_id': session})['session']
                assert reopened['id'] == session
                current = wait_for(ready, 'Retained native session did not reopen')
                assert current['nativeConversationId'] == initial['nativeId'], current
                call('send_session_message', {'session_id': session, 'request_id': second, 'text': 'check participant MCP'})
                result.update(nativeConversationId=initial['nativeId'], firstRequestPreserved=True,
                              originalHistorySha256=hashlib.sha256(old_history.encode()).hexdigest())
                evidence = wait_for(lambda: native_read(native_workspace + '/participant-proof.json'), 'Session did not connect to relay_project', 80)
                proof = json.loads(evidence)
                result['participantMcp'] = proof
                assert proof.get('version') == state['version'] and proof['memberships'][0]['endpointId'] == agent, proof
                assert proof['memberships'][0]['projectId'] == project, proof
                wait_for(lambda: completed(second), 'Reopened session did not reply')
                history = native_read(history_file)
                assert history.startswith(old_history) and second in history
                result.update(nativeConversationId=initial['nativeId'], firstRequestPreserved=True,
                              originalHistorySha256=hashlib.sha256(old_history.encode()).hexdigest(),
                              preservedHistoryAndNewTurn=True, participantMcp=proof, passed=True)
    finally:
        write(output / 'qualification.json', result)
        if (data / 'mcp-runtime.json').is_file():
            pointer = read(data / 'mcp-runtime.json')
            process = resolve_host(pointer, data)
            if process and identity(process, data):
                os.kill(process['pid'], signal.SIGTERM)
        if host is not None:
            try: host.wait(timeout=8)
            except subprocess.TimeoutExpired: host.terminate(); host.wait(timeout=8)
        wsl("""import json,pathlib,shutil,subprocess,sys
p=json.load(sys.stdin); root=pathlib.Path(p['root']); workspace=pathlib.Path(p['workspace'])
assert root.parent==pathlib.Path('/srv/agent-relay/var/apps/agent-relay/sessions') and root.name==p['session'].replace('-','') and not root.is_symlink()
assert workspace.parent==pathlib.Path('/tmp') and workspace.name=='agent-relay-update-'+root.name and (workspace/'owner').read_text()==p['session']
if root.exists():
    assert json.loads((root/'provision-owner.json').read_text())['sessionId']==p['session']
    subprocess.run(['tmux','-S',str(root/'tmux.sock'),'kill-server'],capture_output=True,check=False)
    shutil.rmtree(root)
shutil.rmtree(workspace)
""", {'root': native_root, 'workspace': native_workspace, 'session': session})
    print('RETAINED_SESSION_UPDATE=PASS ' + str(output / 'qualification.json'))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ('baseline', 'candidate', 'output'):
        parser.add_argument('--' + option, type=Path, required=True)
    parser.add_argument('--distribution', required=True)
    parser.add_argument('--user', required=True)
    args = parser.parse_args()
    target = args.output.resolve()
    if not target.is_relative_to(Path(__file__).resolve().parents[4] / 'local/apps/agent-relay/qualification') or target.exists():
        parser.error('Select a new owned local qualification directory.')
    qualify(args.baseline.resolve(), args.candidate.resolve(), target, args.distribution, args.user)

"""Qualify complete update without losing work or a retained WSL conversation."""

import argparse
from datetime import datetime
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
from relay_mcp import McpRemoteError, McpRpc


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
        child.stdin.write(json.dumps(dict(jsonrpc='2.0',method='notifications/initialized'))+'\n')
        child.stdin.flush()
        result=call(2,'tools/call',dict(name='invoke_operation',arguments=dict(call_id=str(uuid.uuid4()),
            operation_id='list_projects',revision=1,arguments={})))
        configuration=result['structuredContent']['configuration']
        commit(work/'participant-proof.json',{'version':initialization['serverInfo']['version'],
            'projects':[item['id'] for item in configuration['projects']],
            'memberships':configuration['memberships']})
    except Exception as error:
        child.stdin.close()
        try: child.wait(timeout=8)
        except subprocess.TimeoutExpired: child.kill(); child.wait()
        commit(work/'participant-proof.json',{'error':str(error),'exitCode':child.returncode,'stderr':child.stderr.read()[-2500:]})
    finally:
        child.stdin.close()
        try: child.wait(timeout=8)
        except subprocess.TimeoutExpired: child.kill(); child.wait()

def temporary_failure(identifier, mode):
    from native_dispatch import NativeDispatch
    marker=NativeDispatch(folder/'data',identifier).marker(1)
    prompt=json.loads(marker.read_text())['prompt']
    turn=str(uuid.uuid4())
    records=[('event_msg',{'type':'task_started','turn_id':turn}),
             ('event_msg',{'type':'item_completed','turn_id':turn,'item':{'type':'UserMessage',
               'content':[{'type':'text','text':prompt,'text_elements':[]}]}})]
    error={'codex_error_info':'server_overloaded'}
    if mode==b'qualify rate':
        error={'codex_error_info':{'response_too_many_failed_attempts':{'http_status_code':429,'retry_after_seconds':11}}}
    if mode==b'qualify already acted':
        records.append(('response_item',{'type':'function_call','name':'fixture_action','call_id':'owned-action'}))
    records.append(('event_msg',{'type':'task_complete','turn_id':turn,'error':error}))
    with (history/(native_id+'.jsonl')).open('a') as stream:
        for kind,payload in records: stream.write(json.dumps({'type':kind,'payload':payload})+'\n')

while True:
    content=b''
    while not content.endswith(b'\r'):
        value=os.read(0,1)
        if not value: sys.exit(0)
        content+=value
    match=re.search(rb'Relay request ([0-9a-f-]{36})',content)
    if not match: continue
    identifier=match[1].decode()
    if b'qualify approval escape' in content:
        from native_dispatch import NativeDispatch
        prompt=json.loads(NativeDispatch(folder/'data',identifier).latest().read_text())['prompt']
        turn=str(uuid.uuid4())
        def append_event(kind, **values):
            with (history/(native_id+'.jsonl')).open('a') as stream:
                stream.write(json.dumps({'type':'event_msg','payload':dict(type=kind,turn_id=turn,**values)})+'\n')
        append_event('item_completed',item={'type':'UserMessage','content':[{'type':'text','text':prompt}]})
        print("Allow relay_project.get_connection_status?\n1. Yes, proceed (y)\n2. No, continue without running it (esc)\nPress enter to confirm or esc to cancel",flush=True)
        while os.read(0,1)!=b'\x1b': pass
        print('\033[2J\033[3J\033[H',end='',flush=True)
        append_event('task_complete')
        continue
    if b'hold for update' in content:
        commit(work/'entered.json', {'requestId':identifier})
        end=time.monotonic()+100
        while not (work/'release').exists() and time.monotonic()<end: time.sleep(.1)
        assert (work/'release').exists()
        print('Would you like to run the following command?',flush=True)
        end=time.monotonic()+180
        while not (work/'decide').exists() and time.monotonic()<end: time.sleep(.1)
        assert (work/'decide').exists()
        print('\033[2J\033[3J\033[H',end='',flush=True)
    elif b'check participant MCP' in content: check_participant()
    elif b'await reply across host restart' in content:
        commit(work/'detached-entered.json', {'id':identifier})
        until=time.monotonic()+75
        while not (work/'detached-release').exists() and time.monotonic()<until: time.sleep(.1)
        assert (work/'detached-release').exists()
    mode=next((value for value in (b'qualify rate',b'qualify late',b'qualify already acted') if value in content),None)
    if mode and b'Relay attempt ' not in content:
        temporary_failure(identifier,mode)
        if mode != b'qualify late':continue
        time.sleep(2)  # Let the adapter observe the error, then deliver the original answer during backoff.
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


def qualify(baseline, candidate, output, distribution, linux_user, verify_native_retry=False, verify_approval_resume=False,
            candidate_release=None, verify_detached_reply=False):
    output.mkdir()
    installation = output / 'installation'
    data = installation / 'var/apps/agent-relay/default'
    old_version, digest = extract_verified(baseline, installation)
    runtime = {'executable': str(installation / 'app/AgentRelay.Windows.exe')}
    before = partial(run, cwd=installation, environment=dict(os.environ))
    update = partial(invoke, installation=installation, data=data)
    project, owner, agent, workspace, binding, participant, session, first, second, warm = (str(uuid.uuid4()) for _ in range(10))
    native_root = '/srv/agent-relay/var/apps/agent-relay/sessions/' + session.replace('-', '')
    native_workspace = '/tmp/agent-relay-update-' + session.replace('-', '')
    result = {'baseVersion': old_version, 'baseSha256': digest, 'sessionId': session, 'initialRequest': first,
              'secondRequest': second, 'modelCalls': 0, 'stableChanged': False, 'observationRetries': []}
    if candidate_release is not None:
        candidate = output / 'candidate'
        candidate_version, candidate_digest = extract_verified(candidate_release, candidate)
        result['candidateSha256'] = candidate_digest
    candidate_runtime = {'executable': str(candidate / 'app/AgentRelay.Windows.exe')}

    def wsl(script, payload):
        operation = subprocess.run(['wsl.exe', '-d', distribution, '-u', linux_user, '--exec', 'python3', '-B', '-c', script],
                                   input=json.dumps(payload), text=True, encoding='utf-8', capture_output=True, timeout=22,
                                   creationflags=subprocess.CREATE_NO_WINDOW)
        if operation.returncode:
            raise RuntimeError(operation.stderr)
        return operation.stdout.strip()

    def native_read(path):
        return wsl("import json,pathlib,sys; p=pathlib.Path(json.load(sys.stdin)); print(p.read_text() if p.is_file() else '')", path)

    def retained(identifier):
        with sqlite3.connect((data / 'relay.sqlite').as_uri() + '?mode=ro', uri=True) as database:
            row = database.execute('SELECT payload FROM work WHERE id=?', (identifier,)).fetchone()
        return json.loads(row[0]) if row else {}

    def completed(identifier):
        record = retained(identifier)
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
                    invocation = {'call_id': str(uuid.uuid4()), 'operation_id': name,
                                  'revision': revision, 'arguments': args or {}}
                    for attempt in range(3):
                        try:
                            return client.tool('invoke_operation', invocation, timeout=60)
                        except McpRemoteError as failure:
                            if (name not in ('get_session', 'get_result') or attempt == 2 or
                                    failure.error.get('code') != 'transport_unavailable' or
                                    failure.error.get('execution') != 'not_started'):
                                raise
                            result['observationRetries'].append({'operation': name,
                                'diagnosticId': failure.error.get('diagnosticId'), 'delaySeconds': attempt + 1})
                            time.sleep(attempt + 1)

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
                call('send_session_message', {'session_id': session, 'request_id': warm, 'text': 'retain this completed work'})
                original = wait_for(lambda: completed(warm), 'Baseline request did not complete')
                before_update = call('get_session', {'session_id': session})['session']
                prepared = update(candidate_runtime, action='prepare', candidate=candidate)
                assert prepared['activation']['nativeAdapters'] and prepared['activation']['preserveLiveSessions']
                call('send_session_message', {'session_id': session, 'request_id': first, 'text': 'hold for update'})
                wait_for(lambda: native_read(native_workspace + '/entered.json'), 'Native fixture never began its request')
                updater = recovery_updater(data)
                drained = update(updater, action='resume')
                assert drained['phase'] == 'awaiting_work' and drained['errorCode'] == 'busy', drained
                assert not completed(first) and host.poll() is None
                result['activeDrain'] = drained['errorCode']
                wsl("import json,pathlib,sys; pathlib.Path(json.load(sys.stdin)).touch()", native_workspace + '/release')
                wait_for(lambda: retained(first).get('state') == 'awaiting_approval', 'The retained native invite did not open')
                approval = retained(first)
                old_history = native_read(history_file)
                assert warm in old_history

                def finish():
                    step = update(updater, action='resume')
                    if step['phase'] in ('recovery_required', 'rolled_back'):
                        raise RuntimeError(json.dumps(step))
                    return step if step['phase'] == 'complete' else None

                state = wait_for(finish, 'Update did not complete on the retained session', 95)
                result['transaction'] = state['id']
                result['candidateVersion'] = state['version']
                assert completed(warm) == original and retained(first) == approval and native_read(history_file) == old_history
                live = call('get_session', {'session_id': session})['session']
                assert live['state'] == 'awaiting_approval' and live['nativeConversationId'] == initial['nativeId'], live
                assert live['runtime']['native']['generation'] == before_update['runtime']['native']['generation']
                assert live['runtime']['distribution']['generation'] == before_update['runtime']['distribution']['generation']
                terminal = call('read_session_terminal', {'session_id': session})
                assert 'Would you like to run the following command?' in json.dumps(terminal), terminal
                result['openApprovalPreserved'] = {'requestId': first, 'nativeGeneration': live['runtime']['native']['generation']}
                status = client.tool('get_connection_status', timeout=30)
                assert status['version'] == state['version'] and status['host']['connectionId'] == binding, status
                result['hostAfter'] = status['host']['processId']
                wsl("import json,pathlib,sys; pathlib.Path(json.load(sys.stdin)).touch()", native_workspace + '/decide')
                def approval_completed():
                    call('read_inbox', {'project_id': project, 'session_id': session})
                    return completed(first)

                wait_for(approval_completed, 'Original invite did not produce its retained reply')
                settings['memberships'][1]['nativeConnectionId'] = participant
                configure(settings, read(data / 'mcp-runtime.json')['executable'])
                call('close_session', {'session_id': session}, revision=2)
                reopened = call('reopen_session', {'session_id': session})['session']
                assert reopened['id'] == session
                current = wait_for(ready, 'Retained native session did not reopen')
                assert current['nativeConversationId'] == initial['nativeId'], current
                call('send_session_message', {'session_id': session, 'request_id': second, 'text': 'check participant MCP'})
                result.update(nativeConversationId=initial['nativeId'], firstRequestPreserved=True,
                              originalHistorySha256=hashlib.sha256(old_history.encode()).hexdigest())
                evidence = wait_for(lambda: native_read(native_workspace + '/participant-proof.json'), 'No relay_project result from the reopened session', 105)
                proof = json.loads(evidence)
                result['participantMcp'] = proof
                write(output / 'participant-mcp.json', proof)
                assert proof.get('version') == state['version'] and proof.get('projects') == [project], proof
                assert {(item['endpointId'], item['projectId']) for item in proof['memberships']} == {(agent, project)}, proof
                if candidate_release is not None: assert state['version'] == candidate_version, state
                wait_for(lambda: completed(second), 'Reopened session did not reply')
                history = native_read(history_file)
                assert history.startswith(old_history) and second in history
                if verify_native_retry:
                    cases = {}
                    for mode, expected, code, attempts in (
                            ('qualify rate', 'completed', 'native_provider_rate_limited', 2),
                            ('qualify late', 'completed', 'server_overloaded', 1),
                            ('qualify already acted', 'unknown', 'server_overloaded', 1)):
                        request = str(uuid.uuid4())
                        call('send_session_message', {'session_id': session, 'request_id': request, 'text': mode})
                        def settled():
                            row = retained(request)
                            return row if row.get('state') in ('completed','failed','unknown') else None
                        row = wait_for(settled, 'Retry scenario never settled: ' + mode, 55)
                        timeline = row['nativeAttempts']
                        assert row['state'] == expected and len(timeline) == attempts and timeline[0]['errorCode'] == code, row
                        if attempts == 2:
                            retry_at = datetime.fromisoformat(timeline[0]['retryAt'])
                            assert (retry_at - datetime.fromisoformat(timeline[0]['finishedAt'])).total_seconds() >= 10.9
                            assert datetime.fromisoformat(timeline[1]['startedAt']) >= retry_at
                        if mode == 'qualify late': assert timeline[0].get('retryAt')
                        if mode == 'qualify already acted': assert not timeline[0].get('retryAt')
                        cases[mode] = {'id': request, 'state': expected, 'attempts': timeline}
                    result['nativeRetry'] = cases
                if verify_approval_resume:
                    request = str(uuid.uuid4())
                    call('send_session_message', {'session_id': session, 'request_id': request, 'text': 'qualify approval escape'})
                    wait_for(lambda: retained(request).get('state') == 'awaiting_approval', 'Native MCP approval was not detected')
                    approval = call('read_session_terminal', {'session_id': session})['terminal']['approval']
                    assert 'Allow relay_project.get_connection_status?' in approval['text']
                    # Set the clock outcome only in the isolated fixture, without waiting two hours.
                    with sqlite3.connect(data / 'relay.sqlite') as db:
                        payload = retained(request)
                        payload.update(state='approval_expired', approvalExpiredAt=payload['updatedAt'], errorCode='approval_expired')
                        db.execute('UPDATE work SET payload=? WHERE id=?', (json.dumps(payload), request))
                    before = call('get_result', {'request_id': request})
                    assert before['cancellation']['sessionRequiresResolution']
                    escape_receipt = call('send_session_escape', {'session_id': session})
                    assert escape_receipt['terminalSignal']['keySent']
                    def observed_resume():
                        status = call('get_result', {'request_id': request})
                        return status if status['approval']['resumeVerifiedAt'] else None
                    resolved = wait_for(observed_resume, 'The exact expired approval did not release the session')
                    assert resolved['operation']['state'] == 'approval_expired' and resolved['approval']['turnEnded']
                    assert resolved['approval']['stopConfirmedAt'] is None and not resolved['cancellation']['sessionRequiresResolution']
                    after = call('get_session', {'session_id': session})['session']
                    assert after['nativeConversationId'] == initial['nativeId'] and after['runtime']['native']['generation'] == current['runtime']['native']['generation']
                    resumed = str(uuid.uuid4())
                    call('send_session_message', {'session_id': session, 'request_id': resumed, 'text': 'same session after expired approval'})
                    wait_for(lambda: completed(resumed), 'The same verified session did not accept a new message')
                    result['expiredApprovalResume'] = {'requestId': request, 'nextRequestId': resumed,
                        'sessionId': session, 'sameNativeGeneration': True, 'proof': resolved['approval']}
                if verify_detached_reply:
                    waiting = str(uuid.uuid4())
                    call('send_session_message', {'session_id': session, 'request_id': waiting, 'text': 'await reply across host restart'})
                    wait_for(lambda: retained(waiting).get('state') == 'awaiting_reply', 'Handoff did not free the worker')
                    native_entry = wait_for(lambda: native_read(native_workspace + '/detached-entered.json'), 'No native fixture turn')
                    assert json.loads(native_entry)['id'] == waiting
                    pointer = read(data / 'mcp-runtime.json')
                    previous = resolve_host(pointer, data)
                    assert previous and identity(previous, data), 'No exact isolated host to restart'
                    os.kill(previous['pid'], signal.SIGTERM)
                    wait_for(lambda: resolve_host(pointer, data) is None, 'Isolated relay did not stop')
                    assert retained(waiting)['state'] == 'awaiting_reply'
                    host = launch(pointer, data, output)
                    assert host.pid != previous['pid']
                    wsl("import json,pathlib,sys; pathlib.Path(json.load(sys.stdin)).touch()", native_workspace + '/detached-release')
                    reply = wait_for(lambda: completed(waiting), 'Host did not import the response without a client poll')
                    assert reply['request']['id'] == waiting and len(reply['nativeAttempts']) == 1
                    assert reply['result']['output'] == 'Owned fixture reply'
                    result['detachedReply'] = {'requestId': waiting, 'sessionId': session, 'previousHost': previous['pid'],
                        'newHost': host.pid, 'nativeAttempts': 1}
                result.update(nativeConversationId=initial['nativeId'], firstRequestPreserved=True,
                              originalHistorySha256=hashlib.sha256(old_history.encode()).hexdigest(),
                              preservedHistoryAndNewTurn=True, passed=True)
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
    for option in ('baseline', 'output'):
        parser.add_argument('--' + option, type=Path, required=True)
    candidate = parser.add_mutually_exclusive_group(required=True)
    candidate.add_argument('--candidate', type=Path, help='Use an extracted development payload')
    candidate.add_argument('--candidate-release', type=Path, help='Extract the verified ZIP and retain its SHA-256')
    parser.add_argument('--distribution', required=True)
    parser.add_argument('--user', required=True)
    parser.add_argument('--verify-native-retry', action='store_true')
    parser.add_argument('--verify-approval-resume', action='store_true')
    parser.add_argument('--verify-detached-reply', action='store_true')
    args = parser.parse_args()
    target = args.output.resolve()
    if not target.is_relative_to(Path(__file__).resolve().parents[4] / 'local/apps/agent-relay/qualification') or target.exists():
        parser.error('Select a new owned local qualification directory.')
    qualify(args.baseline.resolve(), args.candidate.resolve() if args.candidate else None, target, args.distribution, args.user,
            args.verify_native_retry, args.verify_approval_resume, args.candidate_release.resolve() if args.candidate_release else None,
            args.verify_detached_reply)

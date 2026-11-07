"""Qualify open/ready/close and optionally one read-only model reply in a private WSL workspace.

Use only with native-test authorization, a prepared runtime and a fresh design-fixture data directory.
Retains uniquely marked private runtime/workspace and result; never enters operator projects.
"""
import argparse
import json
from pathlib import Path
import subprocess
import sys
import time
import uuid
from deployment import launch
from relay_mcp import RelayMcp
from runtime_layout import runtime_command

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'wsl'))
from native_layout import load_layout


def round_trip(client, data, project, session, workspace, result):
    request = {'session_id': session, 'request_id': str(uuid.uuid4()), 'task_id': str(uuid.uuid4()),
               'text': f'Read-only fixture. Run only pwd, then ls -1 if pwd is exactly {workspace}. '
                       'Use the relay reply tool to return both outputs. No file changes or other work.'}
    with (data / 'design-native-model-intent.json').open('x', encoding='utf-8') as marker:
        json.dump(request, marker, indent=2)
    result.update(requestId=request['request_id'], taskId=request['task_id'], modelSubmissions=1)
    client.tool('send_session_message', request, timeout=20)
    deadline = time.monotonic() + 145
    while time.monotonic() < deadline:
        operation = client.tool('get_result', {'request_id': request['request_id']}, timeout=20)['operation']
        if operation['state'] not in ('pending', 'running'):
            result['operation'] = operation
            break
        time.sleep(1)
    if result.get('operation', {}).get('state') != 'completed':
        raise RuntimeError('The fixture reply was not confirmed; inspect the retained result before any new test')
    inbox = client.tool('read_inbox', {'project_id': project, 'session_id': session}, timeout=20)['messages']
    matching = [item for item in inbox['items']
                if item['inReplyTo'] == request['request_id'] and item['sessionId'] == session]
    if len(matching) != 1 or workspace not in matching[0]['text'] or 'relay-fixture.txt' not in matching[0]['text']:
        raise RuntimeError('The session history did not contain the expected correlated pwd/ls reply')
    result['receipt'] = matching[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', required=True, type=Path)
    parser.add_argument('--data', required=True, type=Path)
    parser.add_argument('--distribution', required=True)
    parser.add_argument('--user', required=True)
    parser.add_argument('--round-trip', action='store_true', help='Authorize one model submission in the fresh fixture')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    data = args.data.resolve()
    if not data.is_relative_to(root / 'local/apps/agent-relay'):
        raise ValueError('Use explicit developer fixture data')
    fixture = json.loads((data / 'design-fixture.json').read_text(encoding='utf-8-sig'))
    if (data / 'design-native-result.json').exists() or (data / 'design-native-model-intent.json').exists():
        raise ValueError('Select a fresh fixture; prior native work is never replayed')
    identifier = str(uuid.uuid4())
    workspace = load_layout().base + '/qualification/' + uuid.UUID(identifier).hex + '/workspace'
    setup = r'''
import json,os,pathlib,pwd,sys,uuid
c=json.load(sys.stdin);p=pathlib.Path(c['workspace']);account=pwd.getpwnam(c['user'])
assert str(p)==c['base']+'/qualification/'+uuid.UUID(c['id']).hex+'/workspace'
assert not p.parent.exists()
p.mkdir(parents=True,mode=0o700)
(p.parent/'qualification-owner.json').write_text(json.dumps({'id':c['id'],'user':c['user']}))
(p/'relay-fixture.txt').write_text('Agent Relay qualification fixture\n')
for target in [p.parent,p,p.parent/'qualification-owner.json',p/'relay-fixture.txt']:os.chown(target,account.pw_uid,account.pw_gid)
print(json.dumps({'workspace':str(p),'modelCalls':0}))
'''
    prepared = subprocess.run(['wsl.exe', '-d', args.distribution, '-u', 'root', '--exec', '/usr/bin/python3', '-c', setup],
                              input=json.dumps({'workspace': workspace, 'user': args.user, 'base': load_layout().base, 'id': identifier}),
                              capture_output=True, text=True, encoding='utf-8', timeout=20, creationflags=subprocess.CREATE_NO_WINDOW)
    if prepared.returncode:
        raise RuntimeError(prepared.stderr)
    configuration = json.loads((data / 'relay-projects.json').read_text(encoding='utf-8-sig'))
    configuration['workspaces'][0]['location'] = workspace
    if args.round_trip:
        # LocalWork is explicit on both participants. The request itself remains read-only.
        for membership in configuration['memberships']:
            membership['capabilities'].append('agent.local-work')
            membership['workspaces'][0]['write'] = True
        configuration['memberships'][0]['capabilities'].append('message.receive')
    (data / 'relay-projects.json').write_text(json.dumps(configuration), encoding='utf-8')
    settings = json.loads((data / 'relay-config.json').read_text(encoding='utf-8-sig'))
    settings['profiles'][0].update(distribution=args.distribution, user=args.user)
    (data / 'relay-config.json').write_text(json.dumps(settings), encoding='utf-8')
    runtime = json.loads(args.runtime.read_text(encoding='utf-8-sig'))
    runtime['connectionId'] = fixture['connection']
    initialized = subprocess.run(runtime_command(runtime) + ['initialize', '--data-dir', str(data), '--connection-id', fixture['connection']],
                                 capture_output=True, text=True, encoding='utf-8', timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
    if initialized.returncode:
        raise RuntimeError(initialized.stderr)
    pointer = json.loads((data / 'mcp-runtime.json').read_text(encoding='utf-8-sig'))
    host = launch(runtime, data, data)
    result = {'sessionId': identifier, 'workspace': workspace, 'modelSubmissions': 0, 'states': []}
    try:
        with (data / 'native.stderr.log').open('w', encoding='utf-8') as errors:
            with RelayMcp(dict(runtime, executable=pointer['mcpExecutable']), data, root, errors) as client:
                opened = client.tool('open_session', {'project_id': fixture['project'], 'endpoint_id': configuration['endpoints'][1]['id'],
                    'workspace_id': configuration['workspaces'][0]['id'], 'session_id': identifier, 'name': 'Model-free lifecycle qualification'})['session']
                result['states'].append(opened['state'])
                deadline = time.monotonic() + 45
                current = opened
                while current['state'] != 'ready' and time.monotonic() < deadline:
                    time.sleep(1)
                    current = client.tool('get_session', {'session_id': identifier})['session']
                    if current['state'] in ['attention', 'setup_failed', 'authentication_required']:
                        break
                result['states'].append(current['state'])
                if current['state'] != 'ready' or current['workActive']:
                    raise RuntimeError('Native readiness not confirmed; inspect retained fixture, do not submit a prompt: ' + json.dumps(current))
                result['version'] = client.initialization['serverInfo']['version']
                try:
                    if args.round_trip:
                        round_trip(client, data, fixture['project'], identifier, workspace, result)
                finally:
                    # Do not interrupt a paid request whose status is still uncertain.
                    if not result['modelSubmissions'] or 'operation' in result:
                        closed = client.tool('close_session', {'session_id': identifier})['session']
                        result['states'].append(closed['state'])
                        assert closed['state'] == 'closed' and not closed['workActive'], closed
                result['passed'] = True
    finally:
        (data / 'design-native-result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
        host.terminate()
        host.wait(timeout=10)
    print(json.dumps(result))


if __name__ == '__main__':
    main()

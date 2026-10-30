"""Scoped V1 acceptance in explicit persistent owned resources; paid phases are once-only."""
import argparse
from contextlib import ExitStack
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import time
import uuid
from relay_mcp import RelayMcp

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('phase', choices=['prepare', 'local', 'agents', 'claude', 'resume', 'cleanup'])
parser.add_argument('--data', type=Path, required=True)
parser.add_argument('--templates', type=Path, required=True)
parser.add_argument('--attach', action='store_true', help='Use an already running explicitly owned qualification host; never stop it')
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
data = args.data.resolve()
assert data.is_relative_to(root / 'local/apps/agent-relay')
data.mkdir(parents=True, exist_ok=True)
runtime = json.loads((data / 'mcp-runtime.json').read_text(encoding='utf-8-sig'))
base = [runtime['dotnet'], runtime['assembly']]
manifest = data / 'v1-acceptance.json'


def save():
    temporary = manifest.with_suffix('.pending')
    temporary.write_text(json.dumps(state, indent=2, ensure_ascii=False), encoding='utf-8')
    temporary.replace(manifest)


def admin(request):
    source = data / ('admin-' + uuid.uuid4().hex + '.json')
    source.write_text(json.dumps(request), encoding='utf-8')
    result = subprocess.run(base + ['admin', '--data-dir', str(data), '--input', str(source)], text=True, encoding='utf-8', capture_output=True, timeout=20, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise RuntimeError(result.stderr)
    return json.loads(result.stdout)


def completed(mcp, identifier, expected='completed'):
    deadline = time.monotonic() + 165
    while time.monotonic() < deadline:
        record = mcp.tool('get_result', {'request_id': identifier})['operation']
        if record['state'] not in ['pending', 'running']:
            assert record['state'] == expected, record
            return record
        time.sleep(.15)
    raise TimeoutError(record)


def ready(mcp, key):
    identifier = state['sessions'][key]
    deadline = time.monotonic() + 65
    while time.monotonic() < deadline:
        session = mcp.tool('get_session', {'session_id': identifier})['session']
        if session['state'] == 'ready':
            return session
        if session['state'] in ['attention_required', 'setup_failed', 'unavailable']:
            raise RuntimeError(session)
        time.sleep(.4)
    raise TimeoutError(session)


def paid(mcp, key, text, marker):
    request = {'session_id': state['sessions'][key], 'request_id': str(uuid.uuid4()), 'text': text}
    with (data / (marker + '.paid-intent.json')).open('x', encoding='utf-8') as stream:
        json.dump(request, stream, indent=2)
    mcp.tool('send_session_message', request)
    return completed(mcp, request['request_id'])


if args.phase == 'prepare':
    assert not manifest.exists(), 'Preparation is once-only; reuse the retained manifest'
    ids = {key: str(uuid.uuid4()) for key in ['project-a', 'project-b', 'main-a', 'main-b', 'codex-a', 'codex-b', 'claude-a', 'command-a',
        'work-main-a', 'work-main-b', 'work-codex-a', 'work-codex-b', 'work-claude-a', 'work-command-a', 'connection-a', 'connection-b']}
    state = {'ids': ids, 'sessions': {key: str(uuid.uuid4()) for key in ['codex-a', 'codex-b', 'claude-a']}, 'phases': {}}
    state['linuxRoot'] = '/srv/agent-relay-v1-' + uuid.uuid4().hex
    save()
    templates = json.loads(args.templates.read_text(encoding='utf-8-sig'))
    (data / 'relay-config.json').write_text(json.dumps(templates), encoding='utf-8')
    template = templates['profiles'][0]
    script = '''import json,pathlib,pwd,os,sys
c=json.load(sys.stdin);p=pathlib.Path(c['root']);assert p.parent==pathlib.Path('/srv') and p.name.startswith('agent-relay-v1-') and not p.exists()
a=pwd.getpwnam(c['user']);p.mkdir(mode=0o700)
for name in ['codex-a','codex-b','claude-a','command-a']:
 f=p/name;f.mkdir();(f/'alpha.txt').write_text('project fixture')
for f in [p,*p.rglob('*')]:os.chown(f,a.pw_uid,a.pw_gid)
print(json.dumps({'created':str(p),'modelPrompts':0}))'''
    result = subprocess.run(['wsl.exe', '-d', template['distribution'], '-u', 'root', '--exec', 'python3', '-c', script],
                            input=json.dumps({'root': state['linuxRoot'], 'user': template['user']}), text=True, encoding='utf-8', capture_output=True, timeout=20)
    assert result.returncode == 0, result.stderr
    endpoints = []; workspaces = []; memberships = []
    for key, client in [('main-a', None), ('main-b', None), ('codex-a', 'codex'), ('codex-b', 'codex'), ('claude-a', 'claude'), ('command-a', 'command')]:
        binding = 'local' if client is None else next(profile['id'] for profile in templates['profiles'] if profile['client'] == client)
        endpoints.append({'id': ids[key], 'name': key, 'adapterId': 'local-files' if client is None else 'wsl-command' if client == 'command' else 'wsl-agent', 'bindingId': binding})
        if client is None:
            location = str(data.parent / 'v1-workspaces' / ids['project-' + key[-1]] / key)
            Path(location).mkdir(parents=True)
        else:
            location = state['linuxRoot'] + '/' + key
        workspaces.append({'id': ids['work-' + key], 'endpointId': ids[key], 'name': key + ' workspace', 'location': location})
    capabilities = ['message.send', 'message.receive', 'history.read', 'session.manage', 'command.execute', 'host.inspect', 'file.read', 'file.write', 'file.replace', 'agent.local-work']
    for suffix in ['a', 'b']:
        selected = [workspace for workspace in workspaces if workspace['name'].split()[0].endswith('-' + suffix)]
        memberships.append({'projectId': ids['project-' + suffix], 'endpointId': ids['main-' + suffix], 'role': 'main', 'capabilities': capabilities,
            'workspaces': [{'workspaceId': workspace['id'], 'read': True, 'write': True, 'replace': True} for workspace in selected], 'commandIds': ['list']})
    for key in ['codex-a', 'codex-b', 'claude-a', 'command-a']:
        caps = ['message.receive', 'message.send'] + (['agent.local-work'] if key == 'codex-a' else [])
        memberships.append({'projectId': ids['project-' + key[-1]], 'endpointId': ids[key], 'role': 'secondary', 'capabilities': caps,
            'workspaces': [{'workspaceId': ids['work-' + key], 'read': True, 'write': True}] if key == 'codex-a' else []})
    configuration = {'version': 1, 'projects': [{'id': ids['project-a'], 'name': 'Qualification A'}, {'id': ids['project-b'], 'name': 'Qualification B'}],
        'endpoints': endpoints, 'workspaces': workspaces, 'memberships': memberships}
    admin({'operation': 'configure_projects', 'projectsConfiguration': configuration})
    for suffix in ['a', 'b']:
        admin({'operation': 'connection', 'connection': {'id': ids['connection-' + suffix], 'endpointId': ids['main-' + suffix], 'projectIds': [ids['project-' + suffix]]}})
    state['configuration'] = configuration
    state['phases']['prepare'] = {'passed': True, 'modelPrompts': 0}
    save()
    print(json.dumps({'phase': 'prepare', 'passed': True, 'manifest': str(manifest)}))
    raise SystemExit(0)

state = json.loads(manifest.read_text(encoding='utf-8')); ids = state['ids']
result = {'at': datetime.now(timezone.utc).isoformat(), 'passed': False, 'desktopCurrentChatToolsProven': False}
with ExitStack() as stack:
    errors = stack.enter_context((data / (args.phase + '.stderr.log')).open('w', encoding='utf-8'))
    host = None if args.attach else subprocess.Popen(base + ['tray', '--data-dir', str(data)], stdout=subprocess.DEVNULL, stderr=errors, creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        time.sleep(.8)
        assert host is None or host.poll() is None, 'Owned test host failed'
        clients = {suffix: stack.enter_context(RelayMcp(dict(runtime, connectionId=ids['connection-' + suffix]), data, root, errors)) for suffix in ['a', 'b']}
        a, b = clients['a'], clients['b']
        if args.phase == 'local':
            result['discovery'] = a.tool('list_projects')
            assert [project['id'] for project in result['discovery']['configuration']['projects']] == [ids['project-a']]
            result['sessionProbe'] = {}
            for key in ['codex-a', 'codex-b', 'claude-a']:
                mcp = clients[key[-1]]
                opened = mcp.tool('open_session', {'project_id': ids['project-' + key[-1]], 'endpoint_id': ids[key], 'workspace_id': ids['work-' + key], 'session_id': state['sessions'][key], 'name': 'V1 ' + key})
                result['sessionProbe'][key] = ready(mcp, key)
            try:
                a.tool('get_session', {'session_id': state['sessions']['codex-b']})
                raise AssertionError('Cross-project session read succeeded')
            except RuntimeError as error:
                assert 'capability_denied' in str(error), error
            request = str(uuid.uuid4())
            a.tool('execute_command', {'project_id': ids['project-a'], 'endpoint_id': ids['command-a'], 'workspace_id': ids['work-command-a'], 'command_id': 'list', 'request_id': request})
            result['command'] = completed(a, request)
            assert result['command']['result']['details']['workspace'] == state['linuxRoot'] + '/command-a'
            local = next(item['location'] for item in state['configuration']['workspaces'] if item['id'] == ids['work-main-a'])
            payload = ('V1 scoped transfer — été\n'.encode('utf-8') + bytes(range(256))) * 4096
            (Path(local) / 'source.bin').write_bytes(payload)
            upload = {'session_id': state['sessions']['codex-a'], 'request_id': str(uuid.uuid4()),
                'source': {'endpoint_id': ids['main-a'], 'workspace_id': ids['work-main-a'], 'path': 'source.bin'},
                'destination': {'endpoint_id': ids['codex-a'], 'workspace_id': ids['work-codex-a'], 'path': 'received.bin'}}
            a.tool('transfer_file', upload); result['upload'] = completed(a, upload['request_id'])
            assert result['upload']['result']['details']['sha256'] == hashlib.sha256(payload).hexdigest()
            download = dict(upload, request_id=str(uuid.uuid4()), source=upload['destination'], destination=dict(upload['source'], path='returned.bin'))
            a.tool('transfer_file', download); result['download'] = completed(a, download['request_id'])
            assert (Path(local) / 'returned.bin').read_bytes() == payload
            result['modelPrompts'] = 0
        elif args.phase == 'agents':
            assert state['phases'].get('local', {}).get('passed')
            result['codex'] = paid(a, 'codex-a', 'Garde le repère AMBRE_731 pour cette conversation. Crée le fichier validation-v1.txt contenant exactement "été\\nV1" dans ton espace de travail. Il manque le fichier exigences-client.md nécessaire à la suite : signale ce blocage au pilote sans inventer son contenu, puis termine par un bref bilan.', 'codex-first')
            result['inbox'] = a.tool('read_inbox', {'project_id': ids['project-a'], 'session_id': state['sessions']['codex-a']})
            assert any(item['inReplyTo'] is None for item in result['inbox']['messages']['items']), 'No ordinary independent blocker message'
            result['modelPrompts'] = 1
        elif args.phase == 'claude':
            assert state['phases'].get('local', {}).get('passed')
            a.tool('close_session', {'session_id': state['sessions']['claude-a']})
            a.tool('reopen_session', {'session_id': state['sessions']['claude-a']})
            result['readiness'] = ready(a, 'claude-a')
            result['record'] = paid(a, 'claude-a', 'Pour préparer la suite, il te manque le fichier consignes-client.md. Signale brièvement ce blocage au pilote sans inventer le fichier, puis termine par un bilan de quelques mots. Aucun autre travail.', 'claude-first')
            result['inbox'] = a.tool('read_inbox', {'project_id': ids['project-a'], 'session_id': state['sessions']['claude-a']})
            assert any(item['inReplyTo'] is None for item in result['inbox']['messages']['items']), 'No ordinary independent Claude blocker message'
            result['modelPrompts'] = 1
        elif args.phase == 'resume':
            assert state['phases'].get('agents', {}).get('passed')
            before = ready(a, 'codex-a')
            assert before.get('nativeConversationId'), before
            a.tool('close_session', {'session_id': state['sessions']['codex-a']})
            a.tool('reopen_session', {'session_id': state['sessions']['codex-a']})
            after = ready(a, 'codex-a')
            assert after['nativeConversationId'] == before['nativeConversationId'], (before, after)
            result['record'] = paid(a, 'codex-a', 'Quel repère t’ai-je demandé de garder dans cette conversation ? Réponds seulement avec ce repère.', 'codex-resume')
            assert 'AMBRE_731' in result['record']['result']['output'], result['record']
            result['nativeConversationId'] = after['nativeConversationId']; result['modelPrompts'] = 1
        else:
            result['closed'] = {key: clients[key[-1]].tool('close_session', {'session_id': identifier}) for key, identifier in state['sessions'].items()}
            result['modelPrompts'] = 0
        result['passed'] = True
    except BaseException as error:
        result['error'] = str(error)
        raise
    finally:
        state['phases'][args.phase] = result; save()
        if host is not None:
            if host.poll() is None:host.terminate()
            host.wait(timeout=10)
print(json.dumps({'phase': args.phase, 'passed': result['passed'], 'manifest': str(manifest)}))

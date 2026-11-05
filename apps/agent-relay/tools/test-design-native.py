"""Explicit model-free open/ready/close qualification in a private WSL workspace.

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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', required=True, type=Path)
    parser.add_argument('--data', required=True, type=Path)
    parser.add_argument('--distribution', required=True)
    parser.add_argument('--user', required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    data = args.data.resolve()
    if not data.is_relative_to(root / 'local/apps/agent-relay'):
        raise ValueError('Use explicit developer fixture data')
    fixture = json.loads((data / 'design-fixture.json').read_text(encoding='utf-8-sig'))
    identifier = str(uuid.uuid4())
    workspace = load_layout().base + '/qualification/' + uuid.UUID(identifier).hex + '/workspace'
    setup = '''
import json,os,pathlib,pwd,sys,uuid
c=json.load(sys.stdin);p=pathlib.Path(c['workspace']);account=pwd.getpwnam(c['user'])
assert str(p)==c['base']+'/qualification/'+uuid.UUID(c['id']).hex+'/workspace'
assert not p.parent.exists()
p.mkdir(parents=True,mode=0o700)
(p.parent/'qualification-owner.json').write_text(json.dumps({'id':c['id'],'user':c['user']}))
for target in [p.parent,p,p.parent/'qualification-owner.json']:os.chown(target,account.pw_uid,account.pw_gid)
print(json.dumps({'workspace':str(p),'modelCalls':0}))
'''
    prepared = subprocess.run(['wsl.exe', '-d', args.distribution, '-u', 'root', '--exec', '/usr/bin/python3', '-c', setup],
                              input=json.dumps({'workspace': workspace, 'user': args.user, 'base': load_layout().base, 'id': identifier}),
                              capture_output=True, text=True, encoding='utf-8', timeout=20, creationflags=subprocess.CREATE_NO_WINDOW)
    if prepared.returncode:
        raise RuntimeError(prepared.stderr)
    configuration = json.loads((data / 'relay-projects.json').read_text(encoding='utf-8-sig'))
    configuration['workspaces'][0]['location'] = workspace
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
    result = {'sessionId': identifier, 'workspace': workspace, 'modelCalls': 0, 'states': []}
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

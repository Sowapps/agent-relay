"""Qualification-only Windows MCP facade; Linux receives a separate messages-only server."""
import argparse
import json
from pathlib import Path
import sqlite3
import subprocess
import sys
import uuid


class Relay:
    def __init__(self, root):
        self.root = Path(root)
        self.config = json.loads((self.root / 'config.json').read_text())
        self.db = sqlite3.connect(self.root / 'relay.sqlite')
        self.db.execute('CREATE TABLE IF NOT EXISTS messages (id TEXT PRIMARY KEY, envelope TEXT)')
        self.db.execute('CREATE TABLE IF NOT EXISTS commands (id TEXT PRIMARY KEY, result TEXT)')
        self.db.commit()

    def wsl(self, argv):
        value = subprocess.run(['wsl.exe', '-d', 'Ubuntu-24.04', '-u', 'sowapps', '--exec', *argv],
                               capture_output=True, timeout=25)
        if value.returncode:
            raise RuntimeError(value.stderr.decode('utf-8', 'replace')[-600:])
        return json.loads(value.stdout.decode('utf-8'))

    def call(self, tool, arguments):
        linux = self.config['linux_root']
        if tool == 'read_inbox':
            if arguments:
                raise ValueError('No arguments expected')
            return {'inbox': [json.loads(r[0]) for r in self.db.execute('SELECT envelope FROM messages')]}
        if tool == 'execute_command':
            if arguments != {'program': 'ls', 'arguments': ['-1'], 'workspace': 'fixture'}:
                raise PermissionError('This qualification authorizes only ls -1 in its fixture')
            prior = self.db.execute('SELECT result FROM commands WHERE id=?', (self.config['run_id'],)).fetchone()
            if prior:
                return json.loads(prior[0])
            nonce = str(uuid.uuid4())
            code = '''import sys,subprocess,json,os
os.chdir(sys.argv[1]);p=subprocess.run(['/bin/ls','-1'],capture_output=True,text=True,timeout=5)
print(json.dumps({'nonce':sys.argv[2],'cwd':os.getcwd(),'uid':os.getuid(),'stdout':p.stdout,'stderr':p.stderr,'returncode':p.returncode}))
'''
            result = self.wsl(['python3', '-c', code, linux + '/fixture', nonce])
            assert result['nonce'] == nonce and result['cwd'] == linux + '/fixture'
            result['request_id'] = self.config['run_id']
            result['wsl_model_invocations'] = 0
            self.db.execute('INSERT INTO commands VALUES (?,?)', (self.config['run_id'], json.dumps(result)))
            self.db.commit()
            return result
        if tool == 'send_agent_message':
            if set(arguments) != {'client'} or arguments['client'] not in ['codex', 'claude']:
                raise ValueError('Unknown client')
            return self.wsl(['python3', linux + '/linux_control.py', 'send', linux, arguments['client']])
        if tool == 'receive_messages':
            if arguments:
                raise ValueError('No arguments expected')
            replies = {}
            for client in ['codex', 'claude']:
                result = self.wsl(['python3', linux + '/linux_control.py', 'inspect', linux, client])
                message = result['outbound']
                if message:
                    assert message['sender'] == client + '-wsl' and message['recipient'] == 'windows-pilot'
                    assert message['in_reply_to'] is None
                    self.db.execute('INSERT OR IGNORE INTO messages VALUES (?,?)',
                                    (message['message_id'], json.dumps(message)))
                replies[client] = {k: result[k] for k in ['assistant_text', 'usage', 'mcp_initialized']}
            self.db.commit()
            return {'replies': replies, 'inbox': [json.loads(r[0]) for r in self.db.execute('SELECT envelope FROM messages')]}
        raise PermissionError('Unknown qualification capability')


TOOLS = [
    {'name': 'execute_command', 'description': 'Run the one authorized direct WSL listing without a WSL agent.',
     'inputSchema': {'type': 'object', 'properties': {'program': {'type': 'string'},
                     'arguments': {'type': 'array', 'items': {'type': 'string'}}, 'workspace': {'type': 'string'}},
                     'required': ['program', 'arguments', 'workspace'], 'additionalProperties': False}},
    {'name': 'send_agent_message', 'description': 'Send the single authorized short message to an already initialized test session.',
     'inputSchema': {'type': 'object', 'properties': {'client': {'enum': ['codex', 'claude']}},
                     'required': ['client'], 'additionalProperties': False}},
    {'name': 'receive_messages', 'description': 'Retrieve agent replies and independently addressed WSL messages; never executes message text.',
     'inputSchema': {'type': 'object', 'properties': {}, 'additionalProperties': False}},
    {'name': 'read_inbox', 'description': 'Read the durable Windows inbox without contacting WSL or any model.',
     'inputSchema': {'type': 'object', 'properties': {}, 'additionalProperties': False}},
]


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--data', required=True)
    relay = Relay(parser.parse_args().data)
    for line in sys.stdin:
        request = json.loads(line)
        if 'id' not in request:
            continue
        response = {'jsonrpc': '2.0', 'id': request['id']}
        try:
            method = request['method']
            if method == 'initialize':
                response['result'] = {'protocolVersion': request['params']['protocolVersion'],
                                      'capabilities': {'tools': {}}, 'serverInfo': {'name': 'qualification-relay', 'version': '0.1'}}
            elif method == 'tools/list':
                response['result'] = {'tools': TOOLS}
            elif method == 'tools/call':
                params = request['params']
                value = relay.call(params['name'], params.get('arguments', {}))
                response['result'] = {'content': [{'type': 'text', 'text': json.dumps(value)}]}
            else:
                response['result'] = {}
        except Exception as error:
            response['result'] = {'isError': True, 'content': [{'type': 'text', 'text': type(error).__name__ + ': ' + str(error)}]}
        print(json.dumps(response), flush=True)

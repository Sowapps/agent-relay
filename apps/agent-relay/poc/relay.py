"""Disposable stdio MCP server. One fixed route and request, not a production relay."""
import argparse
import hashlib
import json
from pathlib import Path
import sqlite3
import subprocess
import sys
import time


class Relay:
    def __init__(self, directory):
        self.root = Path(directory).resolve()
        self.config = json.loads((self.root / 'run.json').read_text())
        self.db = sqlite3.connect(self.root / 'relay.sqlite')
        self.db.execute('PRAGMA synchronous=FULL')
        self.db.execute('CREATE TABLE IF NOT EXISTS task (id TEXT PRIMARY KEY, state TEXT, result TEXT)')
        self.db.commit()

    def linux(self, args, timeout=15):
        result = subprocess.run(['wsl.exe', '--distribution', 'Ubuntu-24.04', '--user', 'sowapps',
                                 '--exec', *args], capture_output=True, timeout=timeout)
        if result.returncode:
            raise RuntimeError('WSL command did not confirm success: ' + result.stderr.decode('utf-8', 'replace')[-300:])
        return result.stdout.decode('utf-8')

    def submit(self, request_id):
        if request_id != self.config['request_id']:
            raise ValueError('Request outside the authorized single-task scope')
        row = self.db.execute('SELECT state FROM task WHERE id=?', (request_id,)).fetchone()
        if row:
            return {'request_id': request_id, 'state': row[0], 'duplicate': True, 'dispatched_again': False}
        self.db.execute('INSERT INTO task VALUES (?, ?, NULL)', (request_id, 'dispatch-intent'))
        self.db.commit()
        # An uncertain dispatch is never automatically retried.
        launch = '''import pathlib,subprocess,sys
p=pathlib.Path(sys.argv[1]); assert (p/'worker.py').is_file() and (p.parent/'data/request.json').is_file(), 'POC workspace is missing'
subprocess.run(['tmux','-S',sys.argv[2],'-f','/dev/null','new-session','-d','-s','worker','-c',str(p),'-x','120','-y','30',
                'python3',str(p/'worker.py'),str(p)],check=True)
print('DISPATCH_CONFIRMED '+sys.argv[3])
'''
        output = self.linux(['python3', '-c', launch, self.config['linux_workspace'], self.config['socket'], request_id])
        if 'DISPATCH_CONFIRMED ' + request_id not in output:
            raise RuntimeError('Worker launch did not provide the correlation marker')
        self.db.execute('UPDATE task SET state=? WHERE id=?', ('dispatched', request_id))
        self.db.commit()
        return {'request_id': request_id, 'state': 'dispatched', 'result_confirmed': False}

    def result(self, request_id):
        if request_id != self.config['request_id']:
            raise ValueError('Request outside the authorized scope')
        row = self.db.execute('SELECT state,result FROM task WHERE id=?', (request_id,)).fetchone()
        if not row:
            raise ValueError('Unknown task')
        if row[1]:
            return {'request_id': request_id, 'source': 'windows-durable-store', **json.loads(row[1])}
        if row[0] == 'outcome-unknown':
            return {'request_id': request_id, 'status': 'outcome-unknown', 'retry_allowed': False}
        # Bounded wait avoids repeated model polling turns. It never starts a worker.
        deadline = time.monotonic() + 85
        while time.monotonic() < deadline:
            raw = self.linux(['python3', '-c',
                             'from pathlib import Path; p=Path(__import__("sys").argv[1]); print(p.read_text() if p.exists() else "null")',
                             self.config['linux_data'] + '/result.json'])
            data = json.loads(raw)
            if data:
                if data.get('request_id') != request_id or data.get('worker_invocations') != 1:
                    raise ValueError('Result correlation failed')
                digest = hashlib.sha256(raw.strip().encode()).hexdigest()
                data['result_sha256'] = digest
                self.db.execute('UPDATE task SET state=?,result=? WHERE id=?',
                                (data['status'], json.dumps(data), request_id))
                self.db.commit()
                return {'request_id': request_id, 'source': 'linux-result-import', **data}
            time.sleep(1)
        self.db.execute('UPDATE task SET state=? WHERE id=?', ('outcome-unknown', request_id))
        self.db.commit()
        return {'request_id': request_id, 'status': 'outcome-unknown', 'retry_allowed': False}


TOOLS = [
    {'name': 'submit_task', 'description': 'Submit the one authorized POC request exactly once. Does not confirm completion.',
     'inputSchema': {'type': 'object', 'properties': {'request_id': {'type': 'string'}},
                     'required': ['request_id'], 'additionalProperties': False}},
    {'name': 'get_result', 'description': 'Read the correlated result, waiting up to 85 seconds. Never invokes Claude or retries a task.',
     'inputSchema': {'type': 'object', 'properties': {'request_id': {'type': 'string'}},
                     'required': ['request_id'], 'additionalProperties': False}},
]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--data', required=True)
    relay = Relay(parser.parse_args().data)
    for line in sys.stdin:
        message = json.loads(line)
        if 'id' not in message:
            continue
        response = {'jsonrpc': '2.0', 'id': message['id']}
        try:
            method = message['method']
            if method == 'initialize':
                version = message.get('params', {}).get('protocolVersion', '2024-11-05')
                response['result'] = {'protocolVersion': version, 'capabilities': {'tools': {}},
                                      'serverInfo': {'name': 'agent-relay-poc', 'version': '0.0.1'}}
            elif method == 'tools/list':
                response['result'] = {'tools': TOOLS}
            elif method == 'ping':
                response['result'] = {}
            elif method == 'tools/call':
                params = message['params']
                key = params['arguments']['request_id']
                if params['name'] == 'submit_task':
                    result = relay.submit(key)
                elif params['name'] == 'get_result':
                    result = relay.result(key)
                else:
                    raise ValueError('Unknown tool')
                response['result'] = {'content': [{'type': 'text', 'text': json.dumps(result)}], 'isError': False}
            else:
                response['error'] = {'code': -32601, 'message': 'Method not implemented by this limited POC'}
        except Exception as error:
            response['result'] = {'content': [{'type': 'text', 'text': type(error).__name__ + ': ' + str(error)}], 'isError': True}
        print(json.dumps(response), flush=True)
    relay.db.close()


if __name__ == '__main__':
    main()

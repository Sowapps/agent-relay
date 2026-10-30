"""WSL-side MCP endpoint: messages only, no command execution implementation."""
import argparse
import json
import os
from pathlib import Path
import sys
import time
import uuid


def commit(path, value):
    temp = path.with_suffix('.tmp')
    with temp.open('w') as stream:
        json.dump(value, stream)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temp, path)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--data', required=True)
    parser.add_argument('--sender', choices=['codex-wsl', 'claude-wsl'], required=True)
    args = parser.parse_args()
    data = Path(args.data)
    tools = [{'name': 'send_message', 'description': 'Send a text message to windows-pilot. Cannot execute commands.',
              'inputSchema': {'type': 'object', 'properties': {'text': {'type': 'string', 'maxLength': 500}},
                              'required': ['text'], 'additionalProperties': False}}]
    for line in sys.stdin:
        request = json.loads(line)
        if 'id' not in request:
            continue
        response = {'jsonrpc': '2.0', 'id': request['id']}
        try:
            method = request['method']
            if method == 'initialize':
                response['result'] = {'protocolVersion': request['params']['protocolVersion'],
                                      'capabilities': {'tools': {}}, 'serverInfo': {'name': 'relay', 'version': '0.1'}}
                commit(data / 'mcp-initialized.json', {'sender': args.sender, 'pid': os.getpid(), 'at': time.time()})
            elif method == 'tools/list':
                response['result'] = {'tools': tools}
            elif method == 'tools/call':
                params = request['params']
                if params['name'] != 'send_message':
                    raise PermissionError('WSL endpoint permits messages only; commands are forbidden')
                arguments = params.get('arguments', {})
                if set(arguments) != {'text'} or not isinstance(arguments['text'], str) or len(arguments['text']) > 500:
                    raise ValueError('Only a text message is accepted')
                message = {'message_id': str(uuid.uuid4()), 'sender': args.sender, 'recipient': 'windows-pilot',
                           'text': arguments['text'], 'in_reply_to': None}
                # One outbound message per client in this bounded qualification.
                with (data / 'message.started').open('x') as stream:
                    stream.write(message['message_id'])
                commit(data / 'outbox.json', message)
                response['result'] = {'content': [{'type': 'text', 'text': json.dumps({'sent': True, **message})}]}
            elif method == 'ping':
                response['result'] = {}
            else:
                response['error'] = {'code': -32601, 'message': 'Unsupported method'}
        except Exception as error:
            response['result'] = {'isError': True, 'content': [{'type': 'text', 'text': str(error)}]}
        print(json.dumps(response), flush=True)


if __name__ == '__main__':
    main()

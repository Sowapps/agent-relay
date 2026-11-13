"""Qualify cooperative stdio succession with isolated Codex; no model or live connection.

This OS/protocol fixture reuses CodexRpc. It implements no Agent Relay application rule.
"""

import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from codex_rpc import CodexRpc


def send(identifier, result):
    print(json.dumps({'jsonrpc': '2.0', 'id': identifier, 'result': result}), flush=True)


def fixture(generation, output):
    (output / f'generation-{generation}.pid').write_text(str(os.getpid()), encoding='utf-8')
    tool = {'name': 'transport_probe', 'description': 'Synthetic transport qualification only',
            'inputSchema': {'type': 'object', 'properties': {
                'action': {'type': 'string', 'enum': ['status', 'handover', 'stop']}},
                'required': ['action'], 'additionalProperties': False}}
    for line in sys.stdin:
        request = json.loads(line)
        if 'id' not in request:
            continue
        identifier, method = request['id'], request['method']
        if method == 'initialize':
            result = {'protocolVersion': request['params']['protocolVersion'],
                      'serverInfo': {'name': 'isolated-handover-fixture', 'version': '1'},
                      'capabilities': {'tools': {}}}
        elif method == 'tools/list':
            result = {'tools': [tool]}
        elif method in ('resources/list', 'resources/templates/list'):
            result = {'resources' if method == 'resources/list' else 'resourceTemplates': []}
        elif method == 'ping':
            result = {}
        elif method == 'tools/call':
            action = request['params']['arguments']['action']
            if action == 'handover' and generation == 1:
                successor = subprocess.Popen(
                    [sys.executable, str(Path(__file__).resolve()), '--fixture', '2', '--output', str(output)],
                    stdin=sys.stdin, stdout=sys.stdout, stderr=sys.stderr,
                    creationflags=subprocess.CREATE_NO_WINDOW)
                value = {'generation': generation, 'pid': os.getpid(), 'successorPid': successor.pid}
                send(identifier, {'structuredContent': value, 'content': [{'type': 'text', 'text': json.dumps(value)}]})
                return
            value = {'generation': generation, 'pid': os.getpid()}
            result = {'structuredContent': value, 'content': [{'type': 'text', 'text': json.dumps(value)}]}
            send(identifier, result)
            if action == 'stop':
                return
            continue
        else:
            print(json.dumps({'jsonrpc': '2.0', 'id': identifier,
                              'error': {'code': -32601, 'message': 'Fixture method unsupported'}}), flush=True)
            continue
        send(identifier, result)


def alive(pid):
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = [ctypes.c_uint32, ctypes.c_int, ctypes.c_uint32]
    kernel.OpenProcess.restype = ctypes.c_void_p
    kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    handle = kernel.OpenProcess(0x00100000, False, pid)
    if not handle:
        error = ctypes.get_last_error()
        if error == 87:
            return False
        raise ctypes.WinError(error)
    try:
        return kernel.WaitForSingleObject(handle, 0) == 258
    finally:
        kernel.CloseHandle(handle)


def cleanup(output):
    """Stop only fixture descendants still matching the unique experiment path."""
    for marker in output.glob('generation-*.pid'):
        pid = int(marker.read_text(encoding='utf-8'))
        guard = str(output).replace("'", "''")
        command = (f'$fixtureProcess = Get-CimInstance Win32_Process -Filter "ProcessId={pid}"; '
                   f"if ($fixtureProcess -and $fixtureProcess.CommandLine.Contains('{guard}') "
                   "-and $fixtureProcess.CommandLine.Contains('codex-stdio-handover.py')) "
                   '{ Stop-Process -Id $fixtureProcess.ProcessId -Force -ErrorAction Stop }')
        subprocess.run(['pwsh', '-NoProfile', '-Command', command], check=True, capture_output=True,
                       creationflags=subprocess.CREATE_NO_WINDOW)


def qualify(root):
    output = root / 'local/apps/agent-relay/qualification' / ('u0-handover-' + uuid.uuid4().hex[:12])
    output.mkdir(parents=True)
    home, work = output / 'codex-home', output / 'workspace'
    home.mkdir()
    work.mkdir()
    executable = shutil.which('codex')
    if not executable:
        raise RuntimeError('Installed Codex executable unavailable')
    command = [str(Path(__file__).resolve()), '--fixture', '1', '--output', str(output)]
    (home / 'config.toml').write_text('[mcp_servers.handover_fixture]\ncommand = '
                                     + json.dumps(sys.executable) + '\nargs = ' + json.dumps(command) + '\n', encoding='utf-8')
    environment = dict(os.environ, CODEX_HOME=str(home))
    started = time.monotonic()
    result = {'scope': 'isolated installed Codex app-server; synthetic MCP; ephemeral protocol thread',
              'modelTurns': 0, 'reloadCalls': 0, 'fixtureSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
              'codexVersion': subprocess.check_output([executable, '--version'], text=True).strip()}
    try:
        with (output / 'codex.stderr.log').open('w', encoding='utf-8') as errors:
            with CodexRpc(executable, work, errors, environment=environment) as rpc:
                thread = rpc.call('thread/start', {'cwd': str(work), 'ephemeral': True,
                                                  'approvalPolicy': 'never', 'sandbox': 'read-only'})['thread']['id']
                result['thread'] = thread
                def status():
                    return rpc.call('mcpServerStatus/list', {'threadId': thread,
                                    'serverName': 'handover_fixture', 'detail': 'full'}, timeout=12)
                def call(action):
                    return rpc.call('mcpServer/tool/call', {'threadId': thread, 'server': 'handover_fixture',
                                    'tool': 'transport_probe', 'arguments': {'action': action}}, timeout=12)
                result['catalogBefore'] = status()
                result['before'] = call('status')
                result['handover'] = call('handover')
                original = result['handover']['structuredContent']['pid']
                successor = result['handover']['structuredContent']['successorPid']
                deadline = time.monotonic() + 3
                while alive(original) and time.monotonic() < deadline:
                    time.sleep(0.05)
                result['originalExited'] = not alive(original)
                result['successorAlive'] = alive(successor)
                try:
                    result['after'] = call('status')
                    result['catalogAfter'] = status()
                    result['sameConnectionSuccessorResponded'] = result['after']['structuredContent']['pid'] == successor
                    result['stop'] = call('stop')
                except Exception as error:
                    result['afterError'] = str(error)
                    result['sameConnectionSuccessorResponded'] = False
    except Exception as error:
        result['experimentError'] = str(error)
    finally:
        try:
            cleanup(output)
            result['ownedFixtureCleanup'] = 'completed'
        except Exception as error:
            result['cleanupError'] = str(error)
        result['seconds'] = round(time.monotonic() - started, 3)
        (output / 'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps({'report': str(output / 'result.json'), 'result': {
        key: value for key, value in result.items() if key not in ('catalogBefore', 'catalogAfter', 'before', 'after', 'handover', 'stop', 'thread')}}))
    return 1 if 'experimentError' in result or 'cleanupError' in result else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--fixture', type=int, choices=(1, 2))
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    if args.fixture:
        if args.output is None:
            parser.error('--fixture requires --output')
        fixture(args.fixture, args.output)
        return 0
    return qualify(Path(__file__).resolve().parents[4])


if __name__ == '__main__':
    sys.exit(main())

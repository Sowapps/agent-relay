"""Actual stdio MCP client for repeatable relay diagnostics; no model prompt on connection."""
import json
import uuid
import argparse
from pathlib import Path
import sys
import tomllib
import os
import time
import codex_registration

from runtime_layout import runtime_command
from stdio_rpc import StdioRpc

DEFAULT_TIMEOUT_SECONDS = 190


class McpRemoteError(RuntimeError):
    def __init__(self, result):
        self.result = result
        self.error = result.get('structuredContent', {}).get('error', {})
        super().__init__(self.error.get('message') or str(result.get('content')))


class McpRpc(StdioRpc):
    """Initialize MCP over an explicit stdio command, including an exported launcher."""

    def _write(self, value):
        super()._write({'jsonrpc': '2.0', **value})

    def __init__(self, command, root, stderr, environment=None, *, client_name='agent-relay-qualification'):
        super().__init__(command, root, stderr, environment)
        try:
            self.initialization = self.call('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                'clientInfo': {'name': client_name, 'version': '1'}})
            self._write({'method': 'notifications/initialized'})
            self.inventory = self.call('tools/list')['tools']
        except BaseException:
            self.close()
            raise

    def tool(self, name, arguments=None, timeout=DEFAULT_TIMEOUT_SECONDS):
        result = self.call('tools/call', {'name': name, 'arguments': arguments or {}}, timeout)
        if result.get('isError'):
            raise McpRemoteError(result)
        return result.get('structuredContent') or json.loads(result['content'][0]['text'])

    def operation(self, name, arguments=None, *, revision=None, timeout=DEFAULT_TIMEOUT_SECONDS, call_id=None):
        """Discover an explicit operation revision, then invoke once; never replay a failed invocation."""
        if not any(item['name'] == 'invoke_operation' for item in self.inventory):
            if revision is not None:
                raise ValueError('Legacy typed surface cannot select an operation revision')
            return self.tool(name, arguments, timeout)
        operations = []
        query = {'limit': 100}
        while True:
            page = self.tool('discover_operations', query)
            operations.extend(page['operations'])
            if page.get('nextCursor') is None:
                break
            query = dict(limit=100, cursor=page['nextCursor'], registry_hash=page['registryHash'])
        supported = [item for item in operations if item['id'] == name and (revision is None or item['revision'] == revision)]
        if not supported:
            raise ValueError('Operation/revision is not advertised: ' + name)
        selected = max(supported, key=lambda item: item['revision'])
        return self.tool('invoke_operation', dict(call_id=str(call_id or uuid.uuid4()), operation_id=name,
                         revision=selected['revision'], arguments=arguments or {}), timeout)


class RelayMcp(McpRpc):
    """Connect to an explicitly scoped relay runtime."""

    def __init__(self, runtime, data, root, stderr):
        if runtime.get('mcpLaunch'):
            launch = runtime['mcpLaunch']
            command = [launch['command'], *launch['args']]
        else:
            command = runtime_command(runtime) + ['mcp', '--data-dir', str(data)]
            if runtime.get('connectionId'):
                command += ['--connection-id', runtime['connectionId']]
        super().__init__(command, root, stderr)


def result_fields(result, fields):
    selected = {}
    for field in fields.split(','):
        value = result
        for part in field.split('.'):
            if isinstance(value, dict):
                value = value.get(part)
            elif isinstance(value, list) and part.isdigit() and int(part) < len(value):
                value = value[int(part)]
            else:
                value = None
        selected[field] = value
    return selected


def main(arguments=None):
    parser = argparse.ArgumentParser(description='Call the configured stable MCP directly without restarting or replacing the Codex backend.')
    parser.add_argument('--config', type=Path, default=Path(os.environ.get('CODEX_HOME', Path.home() / '.codex')) / 'config.toml')
    parser.add_argument('--server', default=codex_registration.SHARED_SERVER)
    parser.add_argument('--operation', required=True)
    parser.add_argument('--arguments-file', type=Path, help='UTF-8 JSON object; retain the operation-specific request ID for work')
    parser.add_argument('--revision', type=int)
    parser.add_argument('--call-id', type=uuid.UUID, help='MCP trace ID, separate from the operation request ID')
    parser.add_argument('--timeout', type=int, default=DEFAULT_TIMEOUT_SECONDS)
    parser.add_argument('--fields', help='Optional comma-separated result paths; omitted paths are not printed')
    args = parser.parse_args(arguments)
    started = time.monotonic()
    call_id = str(args.call_id or uuid.uuid4())
    dispatched = False
    try:
        if args.timeout < 1:
            raise ValueError('Timeout must be positive')
        configuration = tomllib.loads(args.config.read_text(encoding='utf-8-sig'))
        server = configuration.get('mcp_servers', {}).get(args.server)
        codex_registration.connection_identity(server)
        supplied = json.loads(args.arguments_file.read_text(encoding='utf-8-sig')) if args.arguments_file else {}
        if not isinstance(supplied, dict):
            raise ValueError('Operation arguments must be a JSON object')
        with McpRpc([server['command'], *server['args']], Path.cwd(), sys.stderr, client_name='agent-relay-stable-client') as rpc:
            names = {item['name'] for item in rpc.inventory}
            if 'invoke_operation' not in names:
                raise ValueError('The selected connection does not expose the stable generic MCP surface')
            if args.operation in names:
                if args.revision is not None:
                    raise ValueError('Revision selects an operation, not a fixed MCP tool')
                dispatched = True
                result = rpc.tool(args.operation, supplied, timeout=args.timeout)
            else:
                dispatched = True
                result = rpc.operation(args.operation, supplied, revision=args.revision, timeout=args.timeout, call_id=call_id)
        if args.fields:
            result = result_fields(result, args.fields)
        print(json.dumps(dict(callId=call_id, server=args.server, operation=args.operation, selectedFields=args.fields,
                              roundTripSeconds=round(time.monotonic()-started, 3), result=result)))
        return 0
    except (Exception, KeyboardInterrupt) as error:
        details = error.error if isinstance(error, McpRemoteError) and error.error else dict(code='mcp_client_failed', message=str(error))
        print(json.dumps(dict(callId=call_id, operation=args.operation, error=details,
                              roundTripSeconds=round(time.monotonic()-started, 3),
                              execution=details.get('execution', 'unconfirmed' if dispatched else 'not_started'))))
        return 1


if __name__ == '__main__':
    sys.exit(main())

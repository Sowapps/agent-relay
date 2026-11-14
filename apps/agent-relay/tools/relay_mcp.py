"""Actual stdio MCP client for repeatable relay diagnostics; no model prompt on connection."""
import json
import uuid

from runtime_layout import runtime_command
from stdio_rpc import StdioRpc


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

    def tool(self, name, arguments=None, timeout=190):
        result = self.call('tools/call', {'name': name, 'arguments': arguments or {}}, timeout)
        if result.get('isError'):
            raise McpRemoteError(result)
        return result.get('structuredContent') or json.loads(result['content'][0]['text'])

    def operation(self, name, arguments=None, *, revision=None, timeout=190):
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
        return self.tool('invoke_operation', dict(call_id=str(uuid.uuid4()), operation_id=name,
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

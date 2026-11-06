"""Actual stdio MCP client for repeatable relay diagnostics; no model prompt on connection."""
import json

from runtime_layout import runtime_command
from stdio_rpc import StdioRpc


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
            raise RuntimeError(result.get('content'))
        return result.get('structuredContent') or json.loads(result['content'][0]['text'])


class RelayMcp(McpRpc):
    """Connect to an explicitly scoped relay runtime."""

    def __init__(self, runtime, data, root, stderr):
        command = runtime_command(runtime) + ['mcp', '--data-dir', str(data)]
        if runtime.get('connectionId'):
            command += ['--connection-id', runtime['connectionId']]
        super().__init__(command, root, stderr)

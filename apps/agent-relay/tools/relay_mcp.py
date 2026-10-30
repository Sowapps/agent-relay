"""Actual stdio MCP client for repeatable relay diagnostics; no model prompt on connection."""
import json
from stdio_rpc import StdioRpc


class RelayMcp(StdioRpc):
    def _write(self, value):
        super()._write({'jsonrpc': '2.0', **value})

    def __init__(self, runtime, data, root, stderr):
        command = [runtime['dotnet'], runtime['assembly'], 'mcp', '--data-dir', str(data)]
        if runtime.get('connectionId'):command += ['--connection-id',runtime['connectionId']]
        super().__init__(command, root, stderr)
        try:
            self.initialization = self.call('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                'clientInfo': {'name': 'agent-relay-qualification', 'version': '1'}})
            self._write({'jsonrpc': '2.0', 'method': 'notifications/initialized'})
        except BaseException:
            self.close()
            raise

    def tool(self, name, arguments=None, timeout=190):
        result = self.call('tools/call', {'name': name, 'arguments': arguments or {}}, timeout)
        if result.get('isError'):
            raise RuntimeError(result.get('content'))
        return result.get('structuredContent') or json.loads(result['content'][0]['text'])

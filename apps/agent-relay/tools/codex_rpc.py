"""Native Codex diagnostics; never starts a model turn or approves a request."""
from stdio_rpc import StdioRpc


class CodexRpc(StdioRpc):
    def __init__(self, executable, root, stderr, overrides=(), environment=None):
        super().__init__([str(executable), *overrides, 'app-server', '--stdio'], root, stderr, environment)
        try:
            self.initialization = self.call('initialize', {'clientInfo': {'name': 'agent_relay_diagnostics', 'version': '1.0'},
                                                           'capabilities': {'experimentalApi': True}})
            self._write({'method': 'initialized'})
        except BaseException:
            self.close()
            raise

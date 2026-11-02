"""Refresh the owning client through an existing supported control channel; never start a replacement backend."""
from pathlib import Path
from typing import Protocol
import shutil
from stdio_rpc import StdioRpc


class ClientRefreshConnector(Protocol):
    def refresh(self, server: str, thread: str | None = None) -> dict: ...


class CodexControlConnector:
    def __init__(self, root, reports, socket=None, executable=None, expected=None, transport_factory=StdioRpc):
        self.root, self.reports = Path(root), Path(reports)
        self.expected = expected or {}
        self.transport_factory = transport_factory
        self.socket = Path(socket) if socket else Path.home() / '.codex/app-server-control/app-server-control.sock'
        self.executable = executable or shutil.which('codex')

    def refresh(self, server='agent_relay', thread=None):
        result = {'client': 'codex', 'modelTurns': 0, 'sameClientVerified': False, 'requiresClientReload': None}
        if not self.executable or not self.socket.exists():
            return dict(result, state='unsupported', message='The running Codex client exposes no reachable control socket. Check get_connection_status from that client; reload its connection if it retains an old contract. No daemon or replacement backend was started.')
        self.reports.mkdir(parents=True, exist_ok=True)
        reload_requested = False
        try:
            with (self.reports / 'codex-control.stderr.log').open('w', encoding='utf-8') as errors:
                with self.transport_factory([str(self.executable), 'app-server', 'proxy', '--sock', str(self.socket)], self.root, errors) as rpc:
                    rpc.call('initialize', {'clientInfo': {'name': 'agent-relay-update', 'version': '1'}, 'capabilities': {'experimentalApi': True}}, timeout=10)
                    rpc._write({'method': 'initialized'})
                    rpc.call('config/mcpServer/reload', None, timeout=15)
                    reload_requested = True
                    if not thread:
                        return dict(result, state='reload_requested', message='Reload queued in the owning backend. No thread identity supplied; its loaded catalog has not been certified.')
                    status = rpc.call('mcpServerStatus/list', {'threadId': thread, 'serverName': server, 'detail': 'toolsAndAuthOnly'}, timeout=20)
                    selected = next((item for item in status['data'] if item['name'] == server), None)
                    if not selected or selected.get('toolsError') or 'get_connection_status' not in selected.get('tools', {}):
                        return dict(result, state='reload_pending', message='Reload was requested, but this thread has not confirmed the new tool catalog.')
                    probe = rpc.call('mcpServer/tool/call', {'threadId': thread, 'server': server, 'tool': 'get_connection_status', 'arguments': {}}, timeout=20)
                    # Preserve raw verification in a private report; do not call a mutation to qualify a connection.
                    import json
                    (self.reports / 'same-client-status.json').write_text(json.dumps(probe, indent=2), encoding='utf-8')
                    status = probe.get('structuredContent')
                    if not isinstance(status, dict):
                        try: status = json.loads(probe['content'][0]['text'])
                        except (KeyError, ValueError, IndexError): status = {}
                    verified = (not probe.get('isError') and status.get('clientRefreshRequired') is False
                                and bool(self.expected) and status.get('version') == self.expected.get('version')
                                and status.get('contractHash') == self.expected.get('contractHash')
                                and status.get('host', {}).get('connectionId') in self.expected.get('connectionIds', []))
                    if verified:
                        guide = rpc.call('mcpServer/resource/read', {'threadId': thread, 'server': server, 'uri': 'agent-relay://docs/quickstart'}, timeout=15)
                        import hashlib
                        verified = hashlib.sha256(guide['contents'][0]['text'].replace('\r\n', '\n').encode()).hexdigest() == self.expected.get('quickstartHash')
                    return dict(result, state='verified' if verified else 'reload_pending', sameClientVerified=verified, requiresClientReload=not verified,
                                message='The same owning thread was checked after reload.' if verified else 'The same thread still requires verification.')
        except Exception:
            return dict(result, state='reload_pending' if reload_requested else 'unreachable', message='The owning backend accepted reload, but the supplied thread could not be verified.' if reload_requested else 'The existing client control channel could not be used. Reload the client connection explicitly; no restart, configuration or daemon change was performed.')

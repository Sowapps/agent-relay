"""Qualify owning-client adapter decisions with a bounded protocol fixture; never starts Codex/model."""
import hashlib,json,tempfile
from pathlib import Path
from client_refresh import CodexControlConnector

class FixtureRpc:
    stale=False
    server='agent_relay'
    calls=[]
    fail_method=None
    def __init__(self,command,*args):
        assert 'proxy' in command and '--sock' in command and command[1:3]==['app-server','proxy'];self.calls.clear()
    def __enter__(self):return self
    def __exit__(self,*args):pass
    def _write(self,value):self.calls.append(value['method'])
    def call(self,method,params=None,timeout=None):
        self.calls.append(method)
        if method==self.fail_method:
            raise TimeoutError('Synthetic control request timeout')
        if method=='initialize':return {}
        if method=='config/mcpServer/reload':assert params is None;return {}
        if method=='mcpServerStatus/list':
            assert params['threadId']=='owned-thread' and params['serverName']==self.server
            return {'data':[{'name':self.server,'tools':{'get_connection_status':{}}}]}
        if method=='mcpServer/tool/call':
            assert params['threadId']=='owned-thread' and params['server']==self.server and params['tool']=='get_connection_status'
            return {'structuredContent':{'version':'1.1.0-rc.1','contractHash':'stale' if self.stale else 'contract','clientRefreshRequired':False,'host':{'connectionId':'fixture-identity'}},'content':[]}
        if method=='mcpServer/resource/read':
            assert params['server']==self.server
            return {'contents':[{'text':'Current guide'}]}
        raise AssertionError(method)

root=Path(__file__).resolve().parents[3]
folder=root/'local/apps/agent-relay/qualification/client-refresh';folder.mkdir(parents=True,exist_ok=True)
expected={'version':'1.1.0-rc.1','contractHash':'contract','connectionIds':['fixture-identity'],'quickstartHash':hashlib.sha256(b'Current guide').hexdigest()}
socket=folder/'fixture.sock';socket.write_text('Test marker, no listener',encoding='utf-8')
client=CodexControlConnector(root,folder,socket,executable='codex.exe',expected=expected,transport_factory=FixtureRpc)
assert client.refresh(thread='owned-thread')['sameClientVerified'] is True
FixtureRpc.server='agent_relay_personal_assistant'
assert client.refresh(server=FixtureRpc.server, thread='owned-thread')['sameClientVerified'] is True
FixtureRpc.server='agent_relay'
FixtureRpc.stale=True
assert client.refresh(thread='owned-thread')['sameClientVerified'] is False
assert client.refresh()['state']=='reload_requested'
missing=CodexControlConnector(root,folder,folder/'absent.sock',executable='codex.exe',expected=expected,transport_factory=FixtureRpc)
assert missing.refresh(thread='owned-thread')['state']=='unsupported'
assert missing.refresh(thread='owned-thread')['reason']=='control_socket_absent'
retained=[]
def capture(error):
    retained.append(str(error))
    return {'diagnosticId':'fixture-refresh-error','code':'update_adapter_failed'}
client.diagnostic=capture
FixtureRpc.fail_method='config/mcpServer/reload'
failed=client.refresh(thread='owned-thread')
assert failed['state']=='unreachable' and failed['phase']=='request_reload'
assert failed['diagnostic']['diagnosticId']=='fixture-refresh-error'
FixtureRpc.fail_method='mcpServerStatus/list'
failed=client.refresh(thread='owned-thread')
assert failed['state']=='reload_pending' and failed['phase']=='verify_catalog'
assert retained==['Synthetic control request timeout']*2
def fail_capture(error):
    raise OSError('Synthetic diagnostic storage failure')
client.diagnostic=fail_capture
failed=client.refresh(thread='owned-thread')
assert failed['errorType']=='TimeoutError' and failed['diagnosticStorageError']=='OSError'
FixtureRpc.fail_method=None
assert not any(name in FixtureRpc.calls for name in ['thread/start','turn/start'])
print(json.dumps({'passed':True,'sameOwnerVerified':True,'wrongContractRefused':True,'noThreadDoesNotClaimSuccess':True,'missingControlUnsupported':True,'failurePhaseRetained':True,'diagnosticFailureVisible':True,'modelCalls':0}))

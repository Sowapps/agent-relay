"""Qualify owning-client adapter decisions with a bounded protocol fixture; never starts Codex/model."""
import hashlib,json,tempfile
from pathlib import Path
from client_refresh import CodexControlConnector

class FixtureRpc:
    stale=False
    calls=[]
    def __init__(self,command,*args):
        assert 'proxy' in command and '--sock' in command and command[1:3]==['app-server','proxy'];self.calls.clear()
    def __enter__(self):return self
    def __exit__(self,*args):pass
    def _write(self,value):self.calls.append(value['method'])
    def call(self,method,params=None,timeout=None):
        self.calls.append(method)
        if method=='initialize':return {}
        if method=='config/mcpServer/reload':assert params is None;return {}
        if method=='mcpServerStatus/list':assert params['threadId']=='owned-thread';return {'data':[{'name':'agent_relay','tools':{'get_connection_status':{}}}]}
        if method=='mcpServer/tool/call':
            assert params['threadId']=='owned-thread' and params['tool']=='get_connection_status'
            return {'structuredContent':{'version':'1.1.0-rc.1','contractHash':'stale' if self.stale else 'contract','clientRefreshRequired':False,'host':{'connectionId':'fixture-identity'}},'content':[]}
        if method=='mcpServer/resource/read':return {'contents':[{'text':'Current guide'}]}
        raise AssertionError(method)

root=Path(__file__).resolve().parents[3]
folder=root/'local/apps/agent-relay/qualification/client-refresh';folder.mkdir(parents=True,exist_ok=True)
expected={'version':'1.1.0-rc.1','contractHash':'contract','connectionIds':['fixture-identity'],'quickstartHash':hashlib.sha256(b'Current guide').hexdigest()}
socket=folder/'fixture.sock';socket.write_text('Test marker, no listener',encoding='utf-8')
client=CodexControlConnector(root,folder,socket,executable='codex.exe',expected=expected,transport_factory=FixtureRpc)
assert client.refresh(thread='owned-thread')['sameClientVerified'] is True
FixtureRpc.stale=True
assert client.refresh(thread='owned-thread')['sameClientVerified'] is False
assert client.refresh()['state']=='reload_requested'
missing=CodexControlConnector(root,folder,folder/'absent.sock',executable='codex.exe',expected=expected,transport_factory=FixtureRpc)
assert missing.refresh(thread='owned-thread')['state']=='unsupported'
assert not any(name in FixtureRpc.calls for name in ['thread/start','turn/start'])
print(json.dumps({'passed':True,'sameOwnerVerified':True,'wrongContractRefused':True,'noThreadDoesNotClaimSuccess':True,'missingControlUnsupported':True,'modelCalls':0}))

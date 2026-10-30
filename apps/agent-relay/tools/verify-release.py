"""Utility: qualify the exact extracted Windows archive without a checkout/runtime launcher.
Usage: release.ps1 Verify. Owns only its new child host and persistent extraction/data directories; no WSL/model.
"""
import argparse,hashlib,json,os,shutil,subprocess,time,zipfile
from pathlib import Path
from stdio_rpc import StdioRpc
class Mcp(StdioRpc):
 def _write(self,value):super()._write({'jsonrpc':'2.0',**value})
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--directory',required=True,type=Path);p.add_argument('--version',required=True);a=p.parse_args()
directory=a.directory.resolve();archive=directory/('agent-relay-'+a.version+'-win-x64.zip');digest=hashlib.sha256(archive.read_bytes()).hexdigest()
assert (directory/'SHA256SUMS.txt').read_text().split()[0]==digest
extracted=directory/'extracted verification';extracted.mkdir() # Never overwrite another verification.
with zipfile.ZipFile(archive) as z:
 for item in z.infolist():
  assert (extracted/item.filename).resolve().is_relative_to(extracted),'Archive path traversal'
 z.extractall(extracted)
manifest=json.loads((extracted/'release-manifest.json').read_text());assert manifest['version']==a.version
for name,expected in manifest['files'].items():assert hashlib.sha256((extracted/name).read_bytes()).hexdigest()==expected,name
assert set(manifest['files'])|{'release-manifest.json'}=={f.relative_to(extracted).as_posix() for f in extracted.rglob('*') if f.is_file()}
exe=extracted/'app/AgentRelay.Windows.exe';data=extracted/'data';main=extracted/'workspaces/main';peer=extracted/'workspaces/peer';main.mkdir(parents=True);peer.mkdir(parents=True)
env={key:value for key,value in os.environ.items() if not key.startswith(('DOTNET_','NUGET_'))}
env.update({'DOTNET_ROOT':str(extracted/'no-sdk-installed'),'DOTNET_MULTILEVEL_LOOKUP':'0','PATH':str(Path(os.environ['SystemRoot'])/'System32')})
pwsh=shutil.which('pwsh.exe')
def run(command):
 r=subprocess.run([str(x) for x in command],cwd=extracted,env=env,capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=45,creationflags=subprocess.CREATE_NO_WINDOW)
 if r.returncode:raise RuntimeError(str(command[0])+': '+r.stderr+r.stdout)
 return r.stdout
run([exe,'initialize','--data-dir',data])
contexts=[]
for index in range(2):
 request=extracted/('requests-'+str(index))
 run([pwsh,'-NoProfile','-File',extracted/'tools/new-project-request.ps1','-DataDirectory',data,'-Name','Archive project '+str(index),'-MainWorkspace',main,'-PeerWorkspace',peer,'-OutputDirectory',request,'-LocalOnly'])
 run([exe,'admin','--data-dir',data,'--input',request/'configure-project.request.json'])
 contexts.append(json.loads((request/'example-context.json').read_text(encoding='utf-8-sig')))
 if index==0:run([exe,'admin','--data-dir',data,'--input',request/'connection.request.json'])
config=json.loads((data/'relay-projects.json').read_text(encoding='utf-8-sig'));assert len(config['projects'])==2
run([exe,'initialize','--data-dir',data,'--connection-id',contexts[0]['connectionId']])
pointer_before=(data/'mcp-runtime.json').read_bytes()
for forbidden in ['11111111-1111-4111-8111-111111111111','28c10e25-b802-42da-92f5-98723e6b54ae']:
 try:run([exe,'initialize','--data-dir',data,'--connection-id',forbidden])
 except RuntimeError:pass
 else:raise AssertionError('Missing/administrator connection was exportable')
 assert (data/'mcp-runtime.json').read_bytes()==pointer_before
fragment=json.loads((data/'mcp.windows.json').read_text(encoding='utf-8-sig'))['mcpServers']['agent_relay']
assert fragment['command']==str(exe) and fragment['args'][0]=='mcp'
resources=[]
def probe():
 with (directory/'mcp.stderr.log').open('a',encoding='utf-8') as errors:
  with Mcp([fragment['command'],*fragment['args']],extracted,errors,env) as client:
   init=client.call('initialize',{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'portable-archive-check','version':'1'}})
   assert init['serverInfo']['version']==a.version
   client._write({'method':'notifications/initialized'})
   listed=client.call('resources/list')['resources'];assert len(listed)==5
   for item in listed:
    contents=client.call('resources/read',{'uri':item['uri']})['contents'][0]
    assert contents['text'].replace('\r\n','\n')==(extracted/'app/documentation'/item['name']).read_text(encoding='utf-8-sig'),item['uri']
   found=client.call('tools/call',{'name':'list_projects','arguments':{}});assert not found.get('isError')
   assert [p['id'] for p in found['structuredContent']['configuration']['projects']]==[contexts[0]['projectId']]
   empty=client.call('tools/call',{'name':'list_results','arguments':{'project_id':contexts[0]['projectId']}});assert not empty.get('isError') and empty['structuredContent']['operations']['items']==[]
   denied=client.call('tools/call',{'name':'list_results','arguments':{'project_id':contexts[1]['projectId']}});assert denied.get('isError')
   assert len(client.call('tools/list')['tools'])==24
   try:client.call('resources/read',{'uri':'agent-relay://docs/../../connections'})
   except RuntimeError:pass
   else:raise AssertionError('Resource traversal accepted')
   return [item['uri'] for item in listed]
for phase in ['initial','restart']:
 with (directory/('host-'+phase+'.stderr.log')).open('wb') as errors:
  host=subprocess.Popen([str(exe),'tray','--data-dir',str(data),'--lifetime-ms','120000'],cwd=extracted,env=env,stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=errors,creationflags=subprocess.CREATE_NO_WINDOW)
  try:
   for attempt in range(30):
    if host.poll() is not None:raise RuntimeError('Extracted host exited early')
    try:json.loads(run([exe,'profiles','--data-dir',data]));break
    except RuntimeError:time.sleep(.2)
   else:raise RuntimeError('Extracted host unavailable')
   resources=probe()
   run([exe,'tray','--data-dir',data]) # Reuses the existing owner instead of a duplicate icon.
   assert host.poll() is None
  finally:
   if host.poll() is None:host.terminate()
   host.wait(timeout=10)
result={'passed':True,'archiveSha256':digest,'version':a.version,'extracted':str(extracted),'resources':resources,'tools':24,'selfContained':True,'runtimeIsolation':'No dotnet/NuGet variables, nonexistent DOTNET_ROOT, PATH restricted to Windows System32','projectPreservation':True,'scopedConnectionAndForeignProjectRefusal':True,'restartAndDuplicateOwner':True,'modelCalls':0,'wslCalls':0,'guiVerification':'Separate exact-archive native capture required before Publish'}
(directory/'verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result))

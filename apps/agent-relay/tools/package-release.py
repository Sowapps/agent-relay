"""Utility: package an explicit self-contained payload with notices, file hashes and stable ZIP ordering.
Usage: called by release.ps1 Package; never includes local application data or uploads anything.
"""
import argparse, json, shutil, zipfile, subprocess
from pathlib import Path
from release_artifact import MANIFEST_FILE, archive_path, sha256
p=argparse.ArgumentParser(description=__doc__)
for name in ['payload','output','cache','sdk']:p.add_argument('--'+name,type=Path,required=True)
p.add_argument('--version',required=True);a=p.parse_args()
payload=a.payload.resolve();notices=payload/'third-party';notices.mkdir()
app=Path(__file__).resolve().parents[1]
root=app.parents[1]
update_tools = ['complete_update.py', 'deployment.py', 'host_identity.py', 'release_artifact.py', 'portable_fixture.py',
                'runtime_layout.py', 'relay_mcp.py', 'stdio_rpc.py', 'verify-release.py']
updater = payload / 'tools/updater'
updater.mkdir(parents=True)
for name in update_tools:
 shutil.copy2(app / 'tools' / name, updater / name)
(payload/'app/installation.json').write_text(json.dumps({'version':1,'root':'..'}),encoding='utf-8')
# Keep repository prose as the only source; adapt links to the extracted layout.
def distribution_links(text):
 text=text.replace('](apps/agent-relay/doc/using/','](app/documentation/')
 text=text.replace('](apps/agent-relay/doc/developing/','](https://github.com/Sowapps/agent-relay/blob/main/apps/agent-relay/doc/developing/')
 return text.replace('](../developing/','](https://github.com/Sowapps/agent-relay/blob/main/apps/agent-relay/doc/developing/')
for name in ['README.md','README.fr.md']:
 readme=distribution_links((root/name).read_text(encoding='utf-8-sig'))
 (payload/name).write_text(readme,encoding='utf-8',newline='\n')
for guide in (payload/'app/documentation').rglob('*.md'):
 guide.write_text(distribution_links(guide.read_text(encoding='utf-8-sig')),encoding='utf-8',newline='\n')
for name in ['LICENSE.txt','ThirdPartyNotices.txt']:
 source=a.sdk/name
 if source.exists():shutil.copy2(source,notices/('dotnet-'+name))
dependencies=[]
# License materials from exactly the versions in the dependency locks/runtime assets.
versions={}
for lock in (app/'src').glob('*/packages.lock.json'):
 for packages in json.loads(lock.read_text())['dependencies'].values():
  for name,item in packages.items():
   if 'resolved' in item:versions[name.lower()]=item['resolved']
for name,version in sorted(versions.items()):
 directory=a.cache/name/version
 entry={'package':name,'version':version,'licenseFiles':[]}
 if directory.exists():
  for item in directory.iterdir():
   if item.is_file() and (item.suffix=='.nuspec' or any(word in item.name.lower() for word in ['license','notice','copying'])):
    target=notices/name/version/item.name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(item,target);entry['licenseFiles'].append(str(target.relative_to(payload)).replace('\\','/'))
 dependencies.append(entry)
(notices/'dependencies.json').write_text(json.dumps(dependencies,indent=2),encoding='utf-8')
(notices/'README.txt').write_text('Third-party components retain their respective licenses. Included .NET runtime notices and NuGet package metadata/license files accompany this distribution. This file grants no separate license to Agent Relay source. WebView2 Runtime is an external prerequisite and is not redistributed here.\n',encoding='utf-8')
assert (payload/'app/coreclr.dll').is_file(),'Self-contained runtime missing'
assert (payload/'app/AgentRelay.Windows.exe').is_file()
# Fingerprint the final distributed guides after link adaptation, before hashing/zipping the payload.
contract=subprocess.run([str(payload/'app/AgentRelay.Windows.exe'),'mcp-contract','--data-dir',str(payload/'contract-preparation-unused')],capture_output=True,text=True,encoding='utf-8',check=True,timeout=20,creationflags=subprocess.CREATE_NO_WINDOW)
identity=json.loads(contract.stdout);assert identity['version']==a.version
(payload/'app/mcp-contract.json').write_text(json.dumps(identity),encoding='utf-8')
for mode, destination in [('mcp-operations', payload/'app/mcp-operations.json'), ('portable-layout', payload/'portable-layout.json')]:
 metadata=subprocess.run([str(payload/'app/AgentRelay.Windows.exe'),mode,'--data-dir',str(payload/'contract-preparation-unused')],capture_output=True,text=True,encoding='utf-8',check=True,timeout=20,creationflags=subprocess.CREATE_NO_WINDOW)
 destination.write_text(json.dumps(json.loads(metadata.stdout)),encoding='utf-8')
assert {path.name for path in (payload/'transport').iterdir()} == {'AgentRelay.Transport.exe'}, 'Expected a single-file independent transport'
files={file.relative_to(payload).as_posix():sha256(file) for file in sorted(payload.rglob('*')) if file.is_file()}
assert not any(any(part in {'var','local','data','connections','presentation-cache','.codex','.git','bin','obj'} for part in Path(name).parts) for name in files)
inputs=[file for folder in ['src','packaging','wsl','doc/using'] for file in (app/folder).rglob('*') if file.is_file() and not {'bin','obj','__pycache__'}.intersection(file.relative_to(app).parts)]
release_tools = ['release.ps1', 'package-release.py', 'verify-release.py', 'release_artifact.py',
                 'relay_mcp.py', 'stdio_rpc.py', 'runtime_layout.py', 'provision-wsl.py', 'test-startup.ps1']
inputs += list(app.glob('*.props')) + [root/'README.md',root/'README.fr.md',app/'NuGet.Config']
inputs += [app/'tools'/name for name in release_tools]
inputs += [app/'tools'/name for name in update_tools if name not in release_tools]
source_inputs={file.relative_to(root).as_posix():sha256(file) for file in sorted(inputs)}
manifest={'version':a.version,'platform':'win-x64','selfContained':True,'sourceInputs':source_inputs,'files':files}
(payload/MANIFEST_FILE).write_text(json.dumps(manifest,indent=2),encoding='utf-8')
archive=archive_path(a.output,a.version)
with zipfile.ZipFile(archive,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as target:
 for file in sorted(payload.rglob('*')):
  if file.is_file():
   info=zipfile.ZipInfo(file.relative_to(payload).as_posix(),date_time=(2020,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED
   target.writestr(info,file.read_bytes())
digest=sha256(archive)
(a.output/'SHA256SUMS.txt').write_text(digest+'  '+archive.name+'\n',encoding='utf-8')
print(json.dumps({'archive':str(archive),'bytes':archive.stat().st_size,'expandedBytes':sum(f.stat().st_size for f in payload.rglob('*') if f.is_file()),'sha256':digest,'files':len(files)}))

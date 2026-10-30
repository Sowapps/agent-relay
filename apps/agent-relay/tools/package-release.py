"""Utility: package an explicit self-contained payload with notices, file hashes and stable ZIP ordering.
Usage: called by release.ps1 Package; never includes local application data or uploads anything.
"""
import argparse, hashlib, json, shutil, zipfile
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
for name in ['payload','output','cache','sdk']:p.add_argument('--'+name,type=Path,required=True)
p.add_argument('--version',required=True);a=p.parse_args()
payload=a.payload.resolve();notices=payload/'third-party';notices.mkdir()
for name in ['LICENSE.txt','ThirdPartyNotices.txt']:
 source=a.sdk/name
 if source.exists():shutil.copy2(source,notices/('dotnet-'+name))
dependencies=[]
# License materials from exactly the versions in the dependency locks/runtime assets.
app=Path(__file__).resolve().parents[1]
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
files={file.relative_to(payload).as_posix():hashlib.sha256(file.read_bytes()).hexdigest() for file in sorted(payload.rglob('*')) if file.is_file()}
assert not any(any(part in {'data','connections','presentation-cache','.codex','.git','bin','obj'} for part in Path(name).parts) for name in files)
root=app.parents[1]
inputs=[file for folder in ['src','packaging','wsl','doc/using'] for file in (app/folder).rglob('*') if file.is_file() and not {'bin','obj','__pycache__'}.intersection(file.relative_to(app).parts)]
inputs += list(app.glob('*.props')) + [app/'NuGet.Config'] + [app/'tools'/name for name in ['release.ps1','package-release.py','verify-release.py','provision-wsl.py']]
source_inputs={file.relative_to(root).as_posix():hashlib.sha256(file.read_bytes()).hexdigest() for file in sorted(inputs)}
manifest={'version':a.version,'platform':'win-x64','selfContained':True,'sourceInputs':source_inputs,'files':files}
(payload/'release-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
archive=a.output/('agent-relay-'+a.version+'-win-x64.zip')
with zipfile.ZipFile(archive,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as target:
 for file in sorted(payload.rglob('*')):
  if file.is_file():
   info=zipfile.ZipInfo(file.relative_to(payload).as_posix(),date_time=(2020,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED
   target.writestr(info,file.read_bytes())
digest=hashlib.sha256(archive.read_bytes()).hexdigest()
(a.output/'SHA256SUMS.txt').write_text(digest+'  '+archive.name+'\n',encoding='utf-8')
print(json.dumps({'archive':str(archive),'bytes':archive.stat().st_size,'expandedBytes':sum(f.stat().st_size for f in payload.rglob('*') if f.is_file()),'sha256':digest,'files':len(files)}))

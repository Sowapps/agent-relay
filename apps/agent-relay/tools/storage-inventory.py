"""Read-only storage inventory for cleanup design. Never follows links, deletes or opens file contents."""
import argparse
from datetime import datetime,timezone
import json
import os
from pathlib import Path
import stat

root=Path(__file__).resolve().parents[3]
parser=argparse.ArgumentParser()
parser.add_argument('--directory',type=Path,default=root/'var/apps/agent-relay')
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args();directory=args.directory.resolve()
if not directory.is_relative_to(root/'var/apps/agent-relay'):raise ValueError('Select an application-owned var directory explicitly')
groups={};skipped=[]
def scan(path):
 try:
  with os.scandir(path) as entries:
   for entry in entries:
    try:
     info=entry.stat(follow_symlinks=False)
     if entry.is_symlink() or getattr(info,'st_file_attributes',0)&0x400:skipped.append(str(Path(entry.path).relative_to(directory)));continue
     if stat.S_ISDIR(info.st_mode):scan(entry.path)
     elif stat.S_ISREG(info.st_mode):
      rel=Path(entry.path).relative_to(directory);parts=rel.parts
      key='/'.join(parts[:2]) if parts[0] not in {'run','preparing','deployment','migrations'} else parts[0]
      group=groups.setdefault(key,{'files':0,'apparentBytes':0,'oldestModificationUtc':None,'newestModificationUtc':None})
      group['files']+=1;group['apparentBytes']+=info.st_size
      stamp=datetime.fromtimestamp(info.st_mtime,timezone.utc).isoformat()
      group['oldestModificationUtc']=min(group['oldestModificationUtc'] or stamp,stamp);group['newestModificationUtc']=max(group['newestModificationUtc'] or stamp,stamp)
    except OSError:skipped.append(str(Path(entry.path).relative_to(directory)))
 except OSError:skipped.append(str(Path(path).relative_to(directory)))
scan(directory)
result={'directory':str(directory),'observedAtUtc':datetime.now(timezone.utc).isoformat(),'readOnly':True,'followLinks':False,'sizeMeaning':'Logical apparent sizes; shared hardlinks can be counted more than once, not guaranteed reclaimable bytes.','groups':dict(sorted(groups.items())),'skipped':skipped}
args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps({'groups':len(groups),'files':sum(g['files'] for g in groups.values()),'apparentBytes':sum(g['apparentBytes'] for g in groups.values()),'skipped':len(skipped),'report':str(args.output)}))

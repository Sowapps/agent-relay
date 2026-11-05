"""Qualify owned WSL relocation, private transfer staging and cross-filesystem refusal without any agent/model.

Usage: --distribution <explicit distro> --user <explicit user> --report <developer output>.
Creates and removes only uniquely marked qualification roots, preserving a JSON result.
"""
import argparse
import base64
import json
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'wsl'))
from native_layout import load_layout, payload_files
import subprocess
import uuid

SCRIPT = r'''
import base64,hashlib,json,os,pathlib,pwd,shutil,subprocess,sys,uuid
c=json.load(sys.stdin); account=pwd.getpwnam(c['user']);identifier=c['id'];assert uuid.UUID(identifier).hex==identifier
old=pathlib.Path('/srv/agent-relay-session-'+identifier)
new=pathlib.Path('/srv/agent-relay/var/apps/agent-relay/sessions/'+identifier)
fixture=pathlib.Path('/srv/agent-relay-storage-test-'+identifier)
shared=pathlib.Path('/dev/shm/agent-relay-storage-test-'+identifier)
assert not any(path.exists() for path in [old,new,fixture,shared])
for path in [old,fixture,shared]:path.mkdir(mode=0o700);(path/'.qualification-owner').write_text(identifier)
workspace=fixture/'workspace';workspace.mkdir();(old/'command/data').mkdir(parents=True)
(old/'command/context.json').write_text(json.dumps({'workspace':str(workspace)}))
(old/'install').mkdir();(old/'tmux.conf').write_text('set -g remain-on-exit on\n')
(old/'provision-owner.json').write_text(json.dumps({'user':c['user'],'sessionId':str(uuid.UUID(identifier))}))
(old/'command/auth-reference').symlink_to(fixture/'dummy-credential')
partial_id=str(uuid.uuid4());partial=workspace/('.relay-transfer-'+partial_id+'.part');partial.write_bytes(b'retained partial')
for root in [old,fixture,shared]:
 for path in [root,*root.rglob('*')]:os.chown(path,account.pw_uid,account.pw_gid,follow_symlinks=False)
request={'user':c['user'],'transaction':identifier,'roots':[{'source':str(old),'destination':str(new),'sessionId':str(uuid.UUID(identifier))}],
 'partials':[{'source':str(partial),'destination':str(new/'command/data/staging/transfers'/(partial_id+'.part')),'client':'command'}],'adapters':c['adapters']}
def invoke(action):
 value=subprocess.run(['/usr/bin/python3','-c',c['migration']],input=json.dumps(dict(request,action=action)),text=True,capture_output=True,timeout=20)
 assert value.returncode==0,value.stderr
 return json.loads(value.stdout)
try:
 invoke('probe');assert not new.exists()
 invoke('apply');assert old.exists() and new.exists() and (new/'command/auth-reference').is_symlink()
 assert os.readlink(new/'command/auth-reference')==str(fixture/'dummy-credential')
 sys.path.insert(0,str(new/'install'));import transfer_files,relay_files
 os.setresuid(account.pw_uid,account.pw_uid,0)
 try:
  assert relay_files.root_path(str(new))==new
  data=b'private staging qualification';transfer={'id':str(uuid.uuid4()),'linuxPath':'nested/final.txt','direction':'upload','bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()}
  info=transfer_files.transfer(new,'command','transfer_inspect',transfer);temporary=pathlib.Path(info['temporary'])
  assert temporary.is_relative_to(new/'command/data/staging/transfers')
  temporary.write_bytes(data);receipt=transfer_files.transfer(new,'command','transfer_commit',transfer)
  assert (workspace/'nested/final.txt').read_bytes()==data and not temporary.exists()
  assert not list((workspace/'nested').glob('.relay-transfer-*'))
  (new/'command/context.json').write_text(json.dumps({'workspace':str(shared)}))
  assert shared.stat().st_dev != new.stat().st_dev,'Cross-filesystem fixture unavailable'
  try:transfer_files.transfer(new,'command','transfer_inspect',dict(transfer,id=str(uuid.uuid4()),linuxPath='no-create/final.txt'))
  except PermissionError as error:assert 'cross_filesystem_publication' in str(error)
  else:raise AssertionError('Cross-filesystem publication accepted')
  assert not (shared/'no-create').exists()
  (new/'command/context.json').write_text(json.dumps({'workspace':str(workspace)}))
 finally:os.setresuid(0,0,0)
 invoke('commit');assert not old.exists() and not partial.exists()
 invoke('rollback');assert old.exists() and not new.exists() and partial.read_bytes()==b'retained partial'
 result={'passed':True,'nativeOwnership':True,'copyCommitRollback':True,'credentialSymlinkPreserved':True,'partialRecovered':True,
         'privateStagingAndAtomicPublication':True,'crossFilesystemRefusedBeforeTargetCreation':True,'modelCalls':0}
finally:
 os.setresuid(0,0,0)
 if new.exists():invoke('rollback')
 for path in [old,fixture,shared]:
  if path.exists():assert (path/'.qualification-owner').read_text()==identifier;shutil.rmtree(path)
 backup=pathlib.Path('/srv/agent-relay/var/apps/agent-relay/legacy-backups')/identifier
 if backup.exists():shutil.rmtree(backup)
print(json.dumps(result))
'''


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--distribution', required=True); parser.add_argument('--user', required=True); parser.add_argument('--report', required=True, type=Path)
    args = parser.parse_args(); application = Path(__file__).resolve().parents[1]
    payload = {'id': uuid.uuid4().hex, 'user': args.user, 'migration': Path(__file__).with_name('native-storage.py').read_text(),
               'adapters': payload_files(application / 'wsl')}
    result = subprocess.run(['wsl.exe', '-d', args.distribution, '-u', 'root', '--exec', '/usr/bin/python3', '-c', SCRIPT],
                            input=json.dumps(payload).encode(), capture_output=True, timeout=60, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        output = result.stderr or result.stdout
        raise RuntimeError(output.decode('utf-16-le' if b'\x00' in output and not b'Traceback' in output else 'utf-8', errors='replace'))
    report = json.loads(result.stdout); args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2)); print(json.dumps(report))

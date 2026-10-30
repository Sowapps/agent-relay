"""Stop only the private tmux server from a retained qualification provision record."""
import argparse
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--provision', required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args()
root = json.loads(Path(args.provision).read_text(encoding='utf-8-sig'))['root']
code = r'''
import json,os,pathlib,signal,subprocess,sys,time
p=pathlib.Path(sys.argv[1]);assert p.parent==pathlib.Path('/srv') and p.name.startswith('agent-relay-mvp-') and p.resolve()==p and p.stat().st_uid==os.getuid()
r=subprocess.run(['tmux','-S',str(p/'tmux.sock'),'display-message','-p','#{pid}'],capture_output=True,text=True)
seen={}
def capture(pid):
 try:
  stat=pathlib.Path('/proc/'+str(pid)+'/stat').read_text().rsplit(')',1)[1].split();seen[pid]=stat[19]
  for child in pathlib.Path('/proc/'+str(pid)+'/task/'+str(pid)+'/children').read_text().split():capture(int(child))
 except (FileNotFoundError,ProcessLookupError):pass
def active(pid,start):
 try:
  stat=pathlib.Path('/proc/'+str(pid)+'/stat').read_text().rsplit(')',1)[1].split();return stat[19]==start and stat[0]!='Z'
 except FileNotFoundError:return False
if r.returncode==0:
 capture(int(r.stdout.strip()));subprocess.run(['tmux','-S',str(p/'tmux.sock'),'kill-server'],capture_output=True)
 time.sleep(.3)
 for sig in [signal.SIGTERM,signal.SIGKILL]:
  for pid,start in seen.items():
   if active(pid,start):os.kill(pid,sig)
  time.sleep(.3)
assert not any(active(pid,start) for pid,start in seen.items())
print(json.dumps({'root':str(p),'trackedProcesses':len(seen),'processCleanupConfirmed':True,'dataRetained':True,'modelsPrompted':0}))
'''
result = subprocess.run(['wsl.exe', '-d', 'Ubuntu-24.04', '-u', 'sowapps', '--exec', 'python3', '-c', code, root],
                        capture_output=True, text=True, timeout=20)
if result.returncode:
    raise RuntimeError(result.stderr)
Path(args.output).write_text(result.stdout, encoding='utf-8')
print(result.stdout)

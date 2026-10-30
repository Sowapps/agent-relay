"""Update helpers only inside already provisioned dedicated roots; never sends a prompt."""
import argparse
import base64
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--settings', required=True)
args = parser.parse_args()
settings = json.loads(Path(args.settings).read_text(encoding='utf-8-sig'))
files = {path.name: base64.b64encode(path.read_bytes()).decode() for path in (Path(__file__).resolve().parents[1] / 'wsl').glob('*.py')}
seen = set()
for profile in settings['profiles']:
    key = (profile['distribution'], profile['user'], profile['root'])
    if key in seen:
        continue
    seen.add(key)
    code = """import base64,json,pathlib,sys,os
c=json.load(sys.stdin);p=pathlib.Path(c['root']);assert p.parent==pathlib.Path('/srv') and p.name.startswith('agent-relay-') and p.resolve()==p and p.stat().st_uid==os.getuid()
for name,value in c['files'].items():
 assert '/' not in name and name.endswith('.py')
 (p/'install'/name).write_bytes(base64.b64decode(value))
print(json.dumps({'updated':str(p),'modelPrompts':0}))
"""
    process = subprocess.run(['wsl.exe', '-d', key[0], '-u', key[1], '--exec', 'python3', '-c', code],
                             input=json.dumps({'root': key[2], 'files': files}), capture_output=True, text=True, timeout=30)
    if process.returncode:
        raise RuntimeError(process.stderr)
    print(process.stdout.strip())

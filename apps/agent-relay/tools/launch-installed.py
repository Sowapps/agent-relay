"""Stable manually/explicitly opted-in tray launcher; never creates startup registration."""
import argparse
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--data-dir', type=Path, required=True)
parser.add_argument('--show', action='store_true')
args = parser.parse_args()
data = args.data_dir.resolve()
value = json.loads((data / 'mcp-runtime.json').read_text(encoding='utf-8-sig'))
root = Path(__file__).resolve().parents[3]
assembly = Path(value['assembly']).resolve()
assert assembly.is_relative_to(root / 'local/apps/agent-relay/run') and assembly.name == 'AgentRelay.Windows.dll' and assembly.is_file()
command = [value['dotnet'], str(assembly), 'tray', '--data-dir', str(data)]
if args.show:
    command.append('--show')
with (data / 'launcher.stderr.log').open('a', encoding='utf-8') as errors:
    result = subprocess.run(command, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=errors, creationflags=subprocess.CREATE_NO_WINDOW)
raise SystemExit(result.returncode)

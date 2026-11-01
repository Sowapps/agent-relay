"""Stable manually/explicitly opted-in tray launcher; never creates startup registration."""
from runtime_layout import read_runtime, validate_runtime, runtime_command
import argparse
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--data-dir', type=Path, required=True)
parser.add_argument('--show', action='store_true')
args = parser.parse_args()
data = args.data_dir.resolve()
value = read_runtime(data)
root = Path(__file__).resolve().parents[3]
command = validate_runtime(value, root, development=data.is_relative_to(root / 'local/apps/agent-relay')) + ['tray', '--data-dir', str(data)]
if args.show:
    command.append('--show')
with (data / 'launcher.stderr.log').open('a', encoding='utf-8') as errors:
    result = subprocess.run(command, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=errors, creationflags=subprocess.CREATE_NO_WINDOW)
raise SystemExit(result.returncode)

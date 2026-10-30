"""Remove only the flat, named binary test fixture after successful I/O and maintenance acceptance."""
import argparse
import json
from pathlib import Path
import re

parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--data', type=Path, required=True); parser.add_argument('--apply', action='store_true'); args = parser.parse_args()
root = Path(__file__).resolve().parents[3]; data = args.data.resolve(); assert data.is_relative_to(root / 'local/apps/agent-relay')
state = json.loads((data / 'v1-acceptance.json').read_text(encoding='utf-8-sig'))
report = sorted(data.glob('io-*.json'), key=lambda item: item.stat().st_mtime)[-1]
io = json.loads(report.read_text(encoding='utf-8')); assert io['passed'] and re.fullmatch('io-[0-9a-f]{32}', io['case'])
assert json.loads((data / 'partial-maintenance.json').read_text(encoding='utf-8'))['passed']
ids = state['ids']; workspaces = {item['id']: item for item in state['configuration']['workspaces']}
local = Path(workspaces[ids['work-main-a']]['location']).resolve(); assert local.is_relative_to(root / 'local/apps/agent-relay/v1-workspaces')
remote = workspaces[ids['work-codex-a']]['location']; assert remote == state['linuxRoot'] + '/codex-a' and re.fullmatch('/srv/agent-relay-v1-[0-9a-f]{32}/codex-a', remote)
native = Path('\\\\wsl.localhost\\Ubuntu-24.04' + remote.replace('/', '\\'))
allowed = {'large.bin', 'returned.bin', 'oversized.bin'} | {'small-' + str(index) + '.txt' for index in range(8)}
targets = []
for parent in [local, native]:
    folder = parent / io['case']; assert folder.resolve().is_relative_to(parent.resolve()) and not folder.is_symlink()
    if folder.exists():
        files = list(folder.iterdir()); assert all(item.is_file() and not item.is_symlink() and item.name in allowed for item in files)
        targets.append((folder, files))
bytes_removed = sum(item.stat().st_size for _, files in targets for item in files)
if args.apply:
    for folder, files in targets:
        for item in files: item.unlink()
        folder.rmdir()
    io['fixtureCleanup'] = {'applied': True, 'logicalBytesRemoved': bytes_removed, 'receiptsRetained': True}
    report.write_text(json.dumps(io, indent=2, ensure_ascii=False), encoding='utf-8')
print(json.dumps({'applied': args.apply, 'logicalBytes': bytes_removed, 'directories': len(targets), 'receiptsRetained': True}))

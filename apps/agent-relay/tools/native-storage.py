"""Owned WSL storage relocation helper; executed without a shell, client prompt or model."""
import base64
import json
import os
from pathlib import Path
import pwd
import re
import shutil
import stat
import subprocess
import sys


def main():
    request = json.load(sys.stdin)
    account = pwd.getpwnam(request['user'])
    base = Path('/srv/agent-relay/var/apps/agent-relay')
    items = request['roots']
    action = request['action']
    assert action in ['probe', 'apply', 'commit', 'rollback']
    assert re.fullmatch('[0-9a-f]{32}', request['transaction'])
    backup_root = base / 'legacy-backups' / request['transaction']
    result = []
    for item in items:
        old, new = Path(item['source']), Path(item['destination'])
        assert re.fullmatch('/srv/agent-relay-[a-zA-Z0-9-]+', str(old)), 'Source root must be an explicitly owned legacy relay root'
        assert re.fullmatch('/srv/agent-relay/var/apps/agent-relay/(sessions/[0-9a-f]{32}|profiles/[a-zA-Z0-9-]+)', str(new))
        if action in ['probe', 'apply']:
            for parent in new.parents:
                for name in ['CLAUDE.md', 'CLAUDE.local.md', 'AGENTS.md', '.claude/CLAUDE.md', '.claude/AGENTS.md']:
                    assert not (parent / name).exists(), 'New private root would inherit foreign agent instructions'
        if action == 'rollback':
            backup = backup_root / old.name
            if backup.exists() and not old.exists(): os.rename(backup, old)
            if new.exists():
                assert new.resolve() == new and new.stat().st_uid == account.pw_uid
                assert (new / '.migration-owner').read_text() == request['transaction'], 'Rollback refuses an unowned destination'
                shutil.rmtree(new)
            continue
        assert old.is_dir() and old.resolve() == old and old.stat().st_uid == account.pw_uid, 'Native source ownership missing'
        marker_file = old / 'provision-owner.json'
        if marker_file.exists():
            marker = json.loads(marker_file.read_text())
            assert marker.get('user') == request['user']
            if item.get('sessionId'): assert marker.get('sessionId') == item['sessionId']
        else:
            # Early technical-profile roots predate provision-owner.json. Never extend this exception to sessions.
            assert not item.get('sessionId') and old.name.startswith('agent-relay-mvp-')
            assert (old / 'tmux.conf').is_file() and (old / 'instructions.md').is_file()
            assert all((old / name).is_dir() and not (old / name).is_symlink() and (old / name).stat().st_uid == account.pw_uid
                       for name in ['install', 'command', 'codex', 'claude'])
            assert (old / 'install/message_server.py').is_file()
        if (old / 'tmux.sock').exists():
            active = subprocess.run(['tmux', '-S', str(old / 'tmux.sock'), 'has-session'], capture_output=True, timeout=5)
            assert active.returncode != 0, 'Native session is still active; migration refused'
        if action == 'commit':
            assert (new / '.migration-owner').read_text() == request['transaction']
            backup = backup_root / old.name
            backup.parent.mkdir(parents=True, exist_ok=True)
            assert not backup.exists()
            os.rename(old, backup)
            continue
        assert not new.exists(), 'Native destination already exists'
        total = sum(p.lstat().st_size for p in old.rglob('*') if p.is_file() and not p.is_symlink())
        existing = new.parent
        while not existing.exists(): existing = existing.parent
        assert existing.stat().st_dev == old.stat().st_dev, 'Native retirement backup must share the original filesystem'
        assert shutil.disk_usage(existing).free > total + 16 * 1024 * 1024, 'Insufficient space for a retained native copy'
        result.append({'source': str(old), 'destination': str(new), 'bytes': total, 'ownerVerified': True, 'active': False})
        if action != 'apply': continue
        for parent in reversed([new.parent, *new.parent.parents]):
            if str(parent).startswith('/srv/agent-relay'):
                if parent.exists(): assert parent.resolve() == parent and parent.stat().st_uid in [0, account.pw_uid]
                else: parent.mkdir(mode=0o700); os.chown(parent, account.pw_uid, account.pw_gid)
        def ignore(folder, names):
            return [name for name in names if stat.S_ISSOCK((Path(folder) / name).lstat().st_mode)]
        new.mkdir(mode=0o700); os.chown(new, account.pw_uid, account.pw_gid)
        (new / '.migration-owner').write_text(request['transaction'])
        shutil.copytree(old, new, symlinks=True, ignore=ignore, dirs_exist_ok=True)
        if not (new / 'provision-owner.json').exists():
            (new / 'provision-owner.json').write_text(json.dumps({'user': request['user'], 'sessionId': None}))
        # Only active generated configuration is rewritten. Journals, receipts and model transcripts stay historical.
        selected = {'context.json', 'mcp.json', 'config.toml', '.claude.json', 'launch.py', 'instructions.md', 'instructions-work.md', 'tmux.conf'}
        for file in new.rglob('*'):
            if file.is_symlink():
                target = os.readlink(file)
                for mapping in items:
                    if target.startswith(mapping['source'] + '/'):
                        file.unlink(); file.symlink_to(mapping['destination'] + target[len(mapping['source']):]); break
            elif file.is_file() and file.name in selected:
                text = file.read_text()
                for mapping in items: text = text.replace(mapping['source'] + '/', mapping['destination'] + '/').replace('"' + mapping['source'] + '"', '"' + mapping['destination'] + '"')
                file.write_text(text)
        install = new / 'install'; install.mkdir(exist_ok=True)
        for name, value in request['adapters'].items():
            assert Path(name).name == name and name.endswith('.py')
            (install / name).write_bytes(base64.b64decode(value))
        for path in [new, *new.rglob('*')]: os.chown(path, account.pw_uid, account.pw_gid, follow_symlinks=False)
        os.chmod(new, 0o700)
    if action in ['probe', 'apply', 'commit', 'rollback']:
        for part in request.get('partials', []):
            old = Path(part['source']); new = Path(part['destination'])
            assert re.fullmatch(r'\.relay-transfer-[0-9a-f-]{36}\.part', old.name)
            assert any(new.parent == Path(item['destination']) / part['client'] / 'data/staging/transfers' for item in items)
            assert part['client'] in ['codex', 'claude', 'command']
            assert re.fullmatch('[0-9a-f-]{36}\\.part', new.name)
            backup = backup_root / 'partials' / new.name
            if action == 'rollback':
                if backup.exists() and not old.exists(): os.rename(backup, old)
                if new.exists():
                    assert not new.is_symlink() and new.stat().st_uid == account.pw_uid
                    new.unlink()
                continue
            if old.exists(): assert not old.is_symlink() and old.resolve() == old and old.stat().st_uid == account.pw_uid
            if action == 'apply' and old.exists():
                assert not old.is_symlink() and old.resolve() == old and old.stat().st_uid == account.pw_uid
                assert not new.exists()
                new.parent.mkdir(parents=True, exist_ok=True)
                owned = next(Path(item['destination']) for item in items if new.is_relative_to(Path(item['destination'])))
                for folder in [new.parent, *new.parent.parents]:
                    if folder == owned or folder.is_relative_to(owned): os.chown(folder,account.pw_uid,account.pw_gid); os.chmod(folder,0o700)
                shutil.copy2(old, new); os.chown(new,account.pw_uid,account.pw_gid)
            if action == 'commit' and old.exists():
                backup.parent.mkdir(parents=True, exist_ok=True); assert not backup.exists(); os.rename(old, backup)
    return {'action': action, 'roots': result, 'modelCalls': 0, 'userWorkspacesMoved': False}


if __name__ == '__main__':
    print(json.dumps(main()))

"""Refresh explicitly selected legacy profile helpers under the common offline maintenance guard.

@see sync-session-adapters.py for named sessions and wsl_maintenance for the C# transport.
"""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'wsl'))
from native_layout import payload_files
from deployment import demand_idle, exclusive, read
from runtime_layout import maintenance_runtime
from wsl_maintenance import run_script

application = Path(__file__).resolve().parents[1]
root = application.parents[1]
code = """import base64,json,pathlib,sys,os,uuid
c=json.load(sys.stdin);p=pathlib.Path(c['root'])
namespace={};exec(base64.b64decode(c['files']['native_layout.py']),namespace)
layout=namespace['layout_from_payload'](c['files'])
assert layout.valid(p) and not layout.is_session(p) and p.resolve()==p and p.stat().st_uid==os.getuid()
install=p/'install';assert install.resolve()==install
for name,value in c['files'].items():
 assert '/' not in name and (name.endswith('.py') or name=='native-layout.json')
 target=install/name;assert target.parent==install and not target.is_symlink()
 temporary=target.with_suffix('.pending-'+uuid.uuid4().hex)
 temporary.write_bytes(base64.b64decode(value));os.replace(temporary,target)
print(json.dumps({'updated':str(p),'modelPrompts':0}))
"""


def synchronize(settings_path, runtime_pointer=None):
    settings_path = settings_path.resolve()
    data = settings_path.parent
    runtime = maintenance_runtime(data, root, runtime_pointer)
    with exclusive(data):
        demand_idle(runtime, data)
        settings = read(settings_path)
        files = payload_files(application / 'wsl')
        selected = {(profile['distribution'], profile['user'], profile['root']) for profile in settings['profiles']}
        for distribution, user, native_root in sorted(selected):
            result = run_script(runtime, data, distribution, user, code, {'root': native_root, 'files': files}, 30)
            print(result['output'].strip())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--settings', required=True, type=Path)
    parser.add_argument('--runtime-pointer', type=Path, help='Prepared current runtime for an older installation')
    args = parser.parse_args()
    synchronize(args.settings, args.runtime_pointer)


if __name__ == '__main__':
    main()

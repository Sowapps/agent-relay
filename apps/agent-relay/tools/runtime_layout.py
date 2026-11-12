"""Declared relay runtime locations; development outputs are a separate, explicit choice."""
import json
from pathlib import Path


def app_var(root):
    return Path(root).resolve() / 'var/apps/agent-relay'


def pilotage_legacy_scope(root):
    return app_var(root) / 'v1-qualification'


def pilotage_stable_root(root):
    return app_var(root) / 'ap-stable'


def reject_pilotage_paths(root, *paths):
    protected = (pilotage_legacy_scope(root).resolve(), pilotage_stable_root(root).resolve())
    for path in paths:
        target = Path(path).resolve()
        if any(target.is_relative_to(scope) or scope.is_relative_to(target) for scope in protected):
            raise ValueError('Pilotage AP requires a separately authorized stable deployment')


def validate_runtime(runtime, root, development=False):
    binary = Path(runtime.get('assembly') or runtime['executable']).resolve()
    allowed = [app_var(root) / 'run']
    if development:
        allowed.append(Path(root).resolve() / 'local/apps/agent-relay/run')
    if not any(binary.is_relative_to(folder) for folder in allowed) or not binary.is_file():
        raise ValueError('Runtime is outside its declared immutable installation area')
    command = runtime_command(runtime)
    if not Path(command[0]).is_file():
        raise ValueError('The declared executable is missing')
    return command


def runtime_command(runtime):
    executable = runtime.get('executable')
    if executable and Path(executable).name.lower() == 'agentrelay.windows.exe':
        return [str(Path(executable).resolve())]
    return [runtime['dotnet'], runtime['assembly']]


def read_runtime(data):
    data = Path(data).resolve()
    if (data / 'migrated-to.json').exists():
        target = json.loads((data / 'migrated-to.json').read_text(encoding='utf-8-sig'))['destination']
        raise ValueError('This scope was migrated. Reconnect with the reviewed configuration for ' + target)
    return json.loads((data / 'mcp-runtime.json').read_text(encoding='utf-8-sig'))


def maintenance_runtime(data, root, pointer=None):
    """Select a reviewed maintenance runtime without rebinding the selected scope."""
    data, root = Path(data).resolve(), Path(root).resolve()
    if not any(data.is_relative_to(folder) and data != folder
               for folder in [app_var(root), root / 'local/apps/agent-relay']):
        raise ValueError('Select explicit declared relay data')
    registered = read_runtime(data)
    runtime = json.loads(Path(pointer).read_text(encoding='utf-8-sig')) if pointer else registered
    validate_runtime(runtime, root, development=True)
    return runtime

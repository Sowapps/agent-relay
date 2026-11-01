"""Declared relay runtime locations; development outputs are a separate, explicit choice."""
import json
from pathlib import Path


def app_var(root):
    return Path(root).resolve() / 'var/apps/agent-relay'


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

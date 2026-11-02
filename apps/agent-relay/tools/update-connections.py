"""Apply only matching Agent Relay registrations to its retained broker; report real client-refresh capability."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import tomllib
import uuid
from client_refresh import CodexControlConnector
from relay_mcp import RelayMcp

root = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('relay_registration', Path(__file__).with_name('register-codex.py'))
registration = importlib.util.module_from_spec(spec); spec.loader.exec_module(registration)


def update(data, reports, paths, control_socket=None, thread=None):
    data, reports = data.resolve(), reports.resolve(); reports.mkdir(parents=True, exist_ok=True)
    runtime = json.loads((data / 'mcp-runtime.json').read_text(encoding='utf-8-sig'))
    broker = Path(runtime['mcpExecutable']).resolve()
    if not broker.is_file() or broker != data / 'mcp-launcher/AgentRelay.Windows.exe': raise ValueError('The retained native broker is not prepared')
    expected = json.loads((data / 'mcp-contract.json').read_text(encoding='utf-8-sig'))
    guide = Path(runtime['executable']).parent / 'documentation/agent-quickstart.md'
    expected['quickstartHash'] = hashlib.sha256(guide.read_text(encoding='utf-8-sig').replace('\r\n', '\n').encode()).hexdigest()
    changes = []
    for path in dict.fromkeys(path.resolve() for path in paths):
        if not path.is_file(): continue
        raw = path.read_bytes(); table = tomllib.loads(raw.decode('utf-8-sig')).get('mcp_servers', {}).get('agent_relay')
        if not table: continue
        arguments = table.get('args', [])
        if '--data-dir' not in arguments or '--connection-id' not in arguments: continue
        existing_data = Path(arguments[arguments.index('--data-dir') + 1]).resolve()
        if existing_data != data:
            marker = existing_data / 'migrated-to.json'
            if not marker.is_file() or Path(json.loads(marker.read_text(encoding='utf-8-sig'))['destination']).resolve() != data: continue
        identity = arguments[arguments.index('--connection-id') + 1]; uuid.UUID(identity)
        # Fail instead of falling back to another principal. Actual host authentication follows below.
        if not (data / 'connections' / (uuid.UUID(identity).hex + '.bin')).is_file(): raise ValueError('The exact registered connection credential is missing; no settings changed')
        desired_args = ['mcp', '--data-dir', str(data), '--connection-id', identity]
        fragment = '[mcp_servers.agent_relay]\ncommand = ' + json.dumps(str(broker)) + '\nargs = ' + json.dumps(desired_args) + '\n'
        updated = registration.update(raw.decode('utf-8-sig'), fragment).encode('utf-8')
        changes.append((path, raw, updated, identity))
    # Verify each preserved principal before writing any registration. No rights/default connection substitution.
    with (reports / 'fresh-connections.stderr.log').open('w', encoding='utf-8') as errors:
        for identity in sorted({str(uuid.UUID(item[3])) for item in changes}):
            with RelayMcp(dict(runtime, executable=str(broker), connectionId=identity), data, root, errors) as client:
                client.call('tools/list'); client.tool('list_projects'); status = client.tool('get_connection_status')
                if uuid.UUID(status['host']['connectionId']) != uuid.UUID(identity): raise ValueError('The authenticated principal does not match the registered identity')
    backup = reports / ('registrations-' + uuid.uuid4().hex); backup.mkdir(); applied = []
    try:
        for index, (path, before, after, identity) in enumerate(changes):
            (backup / (str(index) + '.before.toml')).write_bytes(before)
            if before == after: continue
            registration.replace_if_unchanged(path, before, after)
            applied.append((path, before, after))
    except BaseException:
        for path, before, after in reversed(applied):
            if path.read_bytes() == after: registration.replace_if_unchanged(path, after, before)
        raise
    expected['connectionIds'] = sorted({str(uuid.UUID(item[3])) for item in changes})
    result = {'updated': len(applied), 'matchingConnections': len(changes), 'identitiesAndOtherSettingsPreserved': True,
              'freshScopedConnectionsVerified': bool(changes), 'clientRefresh': CodexControlConnector(root, reports, control_socket, expected=expected).refresh(thread=thread), 'modelCalls': 0}
    (reports / 'update-result.json').write_text(json.dumps(result, indent=2), encoding='utf-8'); return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data', required=True, type=Path); parser.add_argument('--reports', required=True, type=Path)
    parser.add_argument('--config', action='append', type=Path); parser.add_argument('--codex-control-socket', type=Path); parser.add_argument('--thread')
    args = parser.parse_args()
    paths = args.config if args.config is not None else [root / '.codex/config.toml', Path.home() / '.codex/config.toml']
    print(json.dumps(update(args.data, args.reports, paths, args.codex_control_socket, args.thread)))

"""Inspect a native Windows Codex MCP connection without starting any model turn."""
import argparse
import json
import os
from pathlib import Path
from codex_rpc import CodexRpc


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--codex', required=True)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--qualification-home', type=Path)
    parser.add_argument('--runtime-fragment', type=Path, help='Process-only server overrides; does not prove desktop registration')
    args = parser.parse_args()
    command = [args.codex]
    if args.runtime_fragment:
        fragment = json.loads(args.runtime_fragment.read_text(encoding='utf-8-sig'))['mcpServers']['agent_relay']
        command += ['-c', 'mcp_servers.agent_relay.command=' + json.dumps(fragment['command']),
                    '-c', 'mcp_servers.agent_relay.args=' + json.dumps(fragment['args'])]
    environment = os.environ.copy()
    if args.qualification_home:
        home = args.qualification_home.resolve()
        if not home.is_relative_to(args.root.resolve() / 'local' / 'apps' / 'agent-relay'):
            raise ValueError('Qualification home must stay inside this application local directory')
        home.mkdir(parents=True, exist_ok=True)
        configuration = '[projects.' + json.dumps(str(args.root.resolve()).lower()) + ']\ntrust_level = "trusted"\n'
        if (home / 'config.toml').exists() and (home / 'config.toml').read_text(encoding='utf-8') != configuration:
            raise ValueError('Refusing to replace unrelated qualification configuration')
        (home / 'config.toml').write_text(configuration, encoding='utf-8')
        environment['CODEX_HOME'] = str(home)
    errors = args.report.with_suffix('.stderr.log')
    args.report.parent.mkdir(parents=True, exist_ok=True)
    with errors.open('w', encoding='utf-8') as stderr, CodexRpc(args.codex, args.root, stderr, command[1:], environment) as rpc:
        config = rpc.call('config/read', {'cwd': str(args.root.resolve()), 'includeLayers': True})
        layers = [{'name': layer.get('name'), 'disabledReason': layer.get('disabledReason'),
                   'serverNames': list(layer.get('config', {}).get('mcp_servers', {}))}
                  for layer in config.get('layers', [])]
        servers = rpc.call('mcpServerStatus/list', {'serverName': 'agent_relay', 'detail': 'toolsAndAuthOnly'})
        report = {'modelTurns': 0, 'qualificationHome': str(args.qualification_home) if args.qualification_home else None,
                  'processServerOverride': bool(args.runtime_fragment), 'desktopCurrentChatToolsProven': False,
                  'client': rpc.initialization, 'layers': layers, 'servers': servers,
                  'effectiveServerNames': list(config.get('config', {}).get('mcp_servers', {}))}
        args.report.write_text(json.dumps(report, indent=2), encoding='utf-8')
        relay = next((server for server in servers.get('data', []) if server['name'] == 'agent_relay'), None)
        if not relay or relay.get('toolsError') or not relay.get('tools'):
            raise RuntimeError('Native relay discovery failed; inspect ' + str(args.report))
        print(json.dumps({'passed': True, 'tools': len(relay['tools']), 'desktopCurrentChatToolsProven': False, 'report': str(args.report)}))


if __name__ == '__main__':
    main()

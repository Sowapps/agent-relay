"""Single owner of Codex registration names, TOML edits and guarded replacement.

register-codex.py remains the compatible CLI entry point; application workflows
import this adapter directly rather than depending on another workflow.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import tomllib
import uuid


SHARED_SERVER = 'agent_relay'
DEVELOPMENT_SERVER = 'agent_relay_dev'
LEGACY_STABLE_SERVER = 'agent_relay_personal_assistant'


def connection_identity(table):
    """Parse an explicit configured scope; this does not authenticate or grant rights."""
    if not isinstance(table, dict) or not isinstance(table.get('command'), str) or not table['command']:
        raise ValueError('The named connection needs an explicit executable')
    args = table.get('args')
    if not isinstance(args, list) or not all(isinstance(value, str) for value in args) or 'mcp' not in args or any(
            args.count(flag) != 1 or args.index(flag) == len(args) - 1
            for flag in ('--data-dir', '--connection-id')):
        raise ValueError('The named connection needs explicit relay data and a scoped credential')
    data = Path(args[args.index('--data-dir') + 1])
    if not data.is_absolute() or '..' in data.parts:
        raise ValueError('The named connection needs an absolute data path')
    return data, str(uuid.UUID(args[args.index('--connection-id') + 1]))


def toml_value(value):
    if isinstance(value, bool):
        return str(value).lower()
    if isinstance(value, (str, int, float)):
        return json.dumps(value, ensure_ascii=False, allow_nan=False)
    if isinstance(value, list):
        return '[' + ', '.join(toml_value(item) for item in value) + ']'
    if isinstance(value, dict):
        return '{ ' + ', '.join(json.dumps(key) + ' = ' + toml_value(item) for key, item in value.items()) + ' }'
    raise ValueError('Unsupported existing MCP value; configuration was not changed')


def replace_servers(original, replacements):
    """Exact named-table replacement/removal. @see update for merge semantics."""
    expected = tomllib.loads(original)
    servers = expected.setdefault('mcp_servers', {})
    if all(servers.get(name) == value for name, value in replacements.items()):
        return original
    headers = list(re.finditer(r'(?m)^[ \t]*\[\[?[^\r\n]+\]\]?[ \t]*(?:#[^\r\n]*)?\r?$', original))
    result = original
    spans = []
    for index, header in enumerate(headers):
        parsed = tomllib.loads(header.group() + '\n__relay_probe__ = 1\n').get('mcp_servers', {})
        if any(name in parsed for name in replacements):
            spans.append((header.start(), headers[index + 1].start() if index + 1 < len(headers) else len(original)))
    for start, end in reversed(spans):
        result = result[:start] + result[end:]
    for name, value in replacements.items():
        if value is None:
            servers.pop(name, None)
        else:
            servers[name] = value
            result = result.rstrip() + '\n\n[mcp_servers.' + json.dumps(name) + ']\n' + ''.join(
                json.dumps(key) + ' = ' + toml_value(item) + '\n' for key, item in value.items())
    actual = tomllib.loads(result)
    if not servers and 'mcp_servers' not in actual:
        expected.pop('mcp_servers', None)
    if actual != expected:
        raise ValueError('Unexpected configuration change; refusing table replacement')
    return result


def update(original, fragment, server_name=SHARED_SERVER):
    existing = tomllib.loads(original).get('mcp_servers', {}).get(server_name, {})
    desired = tomllib.loads(fragment)['mcp_servers'][server_name]
    return replace_servers(original, {server_name: dict(existing, **desired)})


def replace_if_unchanged(path, expected, desired):
    """Shared compare-before-replace owner; unrelated writers still need coordination.

    The final os.replace is atomic, not an OS conditional compare-and-swap. Do not
    claim it excludes an uncooperative writer between the check and replacement.
    """
    path = Path(path); temporary = path.with_name('config.agent-relay-' + uuid.uuid4().hex + '.tmp')
    try:
        with temporary.open('xb') as stream:
            stream.write(desired); stream.flush(); os.fsync(stream.fileno())
        if path.read_bytes() != expected: raise RuntimeError('Configuration changed concurrently; preserve it and re-inspect')
        os.replace(temporary, path)
    finally:
        if temporary.exists(): temporary.unlink()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', type=Path, required=True)
    parser.add_argument('--fragment', type=Path, required=True)
    parser.add_argument('--backup-dir', type=Path, required=True)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    raw = args.config.read_bytes()
    new = update(raw.decode('utf-8-sig'), args.fragment.read_text(encoding='utf-8-sig')).encode('utf-8')
    args.backup_dir.mkdir(parents=True, exist_ok=True)
    identifier = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:8]
    backup = args.backup_dir / (identifier + '.before.toml')
    backup.write_bytes(raw)
    proposal = args.backup_dir / (identifier + '.proposed.toml')
    proposal.write_bytes(new)
    if args.apply:
        replace_if_unchanged(args.config, raw, new)
    print(json.dumps({'applied': args.apply, 'backup': str(backup), 'beforeSha256': hashlib.sha256(raw).hexdigest(),
                      'afterSha256': hashlib.sha256(new).hexdigest(), 'onlyAgentRelayChanged': True}))


if __name__ == '__main__':
    main()

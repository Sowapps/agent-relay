"""Safely maintain only the authorized agent_relay MCP table, preserving all other effective settings."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import tomllib
import uuid


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


def update(original, fragment):
    before = tomllib.loads(original)
    desired = tomllib.loads(fragment)['mcp_servers']['agent_relay']
    existing = before.get('mcp_servers', {}).get('agent_relay', {})
    if existing and all(existing.get(key) == value for key, value in desired.items()):
        return original
    replacement = dict(existing, **desired)
    table = '[mcp_servers.agent_relay]\n' + ''.join(key + ' = ' + toml_value(value) + '\n' for key, value in replacement.items())
    headers = list(re.finditer(r'(?m)^[ \t]*\[(?!\[)[^\r\n]+\][ \t]*(?:#[^\r\n]*)?\r?$', original))
    spans = []
    for index, header in enumerate(headers):
        parsed = tomllib.loads(header.group() + '\n__relay_probe__ = 1\n')
        if 'agent_relay' in parsed.get('mcp_servers', {}):
            spans.append((header.start(), headers[index+1].start() if index+1 < len(headers) else len(original)))
    result = original
    if spans:
        for index in range(len(spans)-1, -1, -1):
            start, end = spans[index]
            result = result[:start] + (table + '\n' if index == 0 else '') + result[end:]
    else:
        result = original.rstrip() + '\n\n' + table
    after = tomllib.loads(result)
    actual = after.get('mcp_servers', {}).pop('agent_relay')
    baseline = tomllib.loads(original)
    baseline.setdefault('mcp_servers', {}).pop('agent_relay', None)
    assert after == baseline and actual == replacement, 'Unexpected configuration change; refusing update'
    return result


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
        temporary = args.config.with_name('config.agent-relay-' + uuid.uuid4().hex + '.tmp')
        try:
            with temporary.open('xb') as stream:
                stream.write(new);stream.flush();os.fsync(stream.fileno())
            if args.config.read_bytes() != raw:
                raise RuntimeError('Configuration changed concurrently; preserve it and re-inspect')
            os.replace(temporary, args.config)
        finally:
            if temporary.exists():temporary.unlink()
    print(json.dumps({'applied': args.apply, 'backup': str(backup), 'beforeSha256': hashlib.sha256(raw).hexdigest(),
                      'afterSha256': hashlib.sha256(new).hexdigest(), 'onlyAgentRelayChanged': True}))


if __name__ == '__main__':
    main()

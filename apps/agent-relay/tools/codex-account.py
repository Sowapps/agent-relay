"""Inspect quota, or redeem only the explicitly authorized expiring reset near exhaustion."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import uuid
from codex_rpc import CodexRpc

AUTHORIZED_CREDIT = 'RateLimitResetCredit_940623572ff481919e155756f96a6d3b'


def durable(path, value):
    temporary = path.with_suffix('.pending')
    with temporary.open('w', encoding='utf-8') as stream:
        json.dump(value, stream, indent=2)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['inspect', 'redeem'])
    parser.add_argument('--codex', required=True)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--reports', type=Path, required=True)
    args = parser.parse_args()
    reports = args.reports.resolve()
    if not reports.is_relative_to(args.root.resolve() / 'local' / 'apps' / 'agent-relay'):
        raise ValueError('Reports must remain inside this application local directory')
    reports.mkdir(parents=True, exist_ok=True)
    with (reports / 'account.stderr.log').open('a', encoding='utf-8') as stderr:
        with CodexRpc(args.codex, args.root, stderr) as rpc:
            before = rpc.call('account/rateLimits/read')
            limits = before.get('rateLimitsByLimitId', {}).get('codex') or before.get('rateLimits')
            if not limits or not limits.get('primary'):
                raise RuntimeError('Quota unavailable; no redemption is permitted')
            report = {'at': datetime.now(timezone.utc).isoformat(), 'limits': limits,
                      'resetCredits': before.get('rateLimitResetCredits'), 'modelTurns': 0}
            if args.action == 'redeem':
                state_path = reports / 'redemption.json'
                if state_path.exists():
                    state = json.loads(state_path.read_text(encoding='utf-8'))
                    if state.get('outcome') in ['reset', 'alreadyRedeemed']:
                        raise RuntimeError('Authorized reset already redeemed; preserve the other credit')
                else:
                    state = {'creditId': AUTHORIZED_CREDIT, 'idempotencyKey': str(uuid.uuid4()), 'outcome': 'pending'}
                if state['creditId'] != AUTHORIZED_CREDIT:
                    raise RuntimeError('Unexpected credit; no redemption')
                if limits['primary']['usedPercent'] < 95:
                    raise RuntimeError('Preserve current quota: redemption requires at least95% used')
                credits = (before.get('rateLimitResetCredits') or {}).get('credits') or []
                if not any(c['id'] == AUTHORIZED_CREDIT and c['status'] == 'available' for c in credits):
                    raise RuntimeError('Authorized expiring credit unavailable; preserve other credits')
                durable(state_path, state)
                result = rpc.call('account/rateLimitResetCredit/consume',
                                  {'creditId': state['creditId'], 'idempotencyKey': state['idempotencyKey']})
                state['outcome'] = result['outcome']
                durable(state_path, state)
                report['redemption'] = result
                report['after'] = rpc.call('account/rateLimits/read')
            durable(reports / ('account-' + args.action + '.json'), report)
            print(json.dumps(report))


if __name__ == '__main__':
    main()

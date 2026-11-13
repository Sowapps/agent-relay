"""Prepare, reconcile and restore a reviewed pair of Codex registration files.

Configuration/OS adapter only. TOML edits and guarded replacement belong to codex_registration;
application permissions, credentials and relay data are never edited here.
"""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import sys
import time
import tomllib

from deployment import exclusive
import codex_registration as registration
from release_artifact import ordinary_file


def digest(value):
    return hashlib.sha256(value).hexdigest()


def write_new(path, value):
    with path.open('xb') as stream:
        stream.write(value)
        stream.flush()
        os.fsync(stream.fileno())


def save(path, report):
    registration.replace_if_unchanged(path, path.read_bytes(), (json.dumps(report, indent=2) + '\n').encode('utf-8'))


def encoded_edit(raw, changes):
    original = raw.decode('utf-8-sig')
    changed = registration.replace_servers(original, changes)
    if changed == original:
        return raw
    return (b'\xef\xbb\xbf' if raw.startswith(b'\xef\xbb\xbf') else b'') + changed.encode('utf-8')


def prepare(shared, workspace, folder, *, stable_source,
            shared_name=registration.SHARED_SERVER, dev_name=registration.DEVELOPMENT_SERVER):
    """Retain reviewed before/after intent. @see reconcile for publication/rollback."""
    shared, workspace = ordinary_file(shared).resolve(), ordinary_file(workspace).resolve()
    if shared == workspace or shared_name == dev_name or stable_source == dev_name:
        raise ValueError('Select two different configuration files and unambiguous server names')
    raw = [shared.read_bytes(), workspace.read_bytes()]
    tables = [tomllib.loads(value.decode('utf-8-sig')).get('mcp_servers', {}) for value in raw]
    stable = tables[0].get(stable_source)
    dev = tables[1].get(shared_name) or tables[1].get(dev_name)
    if not stable or not dev or not all(isinstance(value, dict) and value.get('command') and value.get('args') for value in (stable, dev)):
        raise ValueError('Explicit stable source and workspace development registrations are required')
    if stable == dev:
        raise ValueError('Stable and development registrations must be distinct')
    if tables[0].get(shared_name) not in (None, stable, dev):
        raise ValueError('The shared destination differs from both reviewed registrations')
    if tables[1].get(dev_name) not in (None, dev) or tables[0].get(dev_name) not in (None, dev):
        raise ValueError('The development alias selects an unrelated registration')
    proposed = [encoded_edit(raw[0], {stable_source: None, dev_name: None, shared_name: stable}),
                encoded_edit(raw[1], {shared_name: None, dev_name: dev})]
    folder = Path(folder).resolve()
    folder.mkdir(parents=True, exist_ok=False)
    files = []
    for index, (target, before, after) in enumerate(zip((shared, workspace), raw, proposed)):
        for suffix, value in (('before', before), ('after', after)):
            write_new(folder / f'{index}.{suffix}.toml', value)
        files.append({'path': str(target), 'before': digest(before), 'after': digest(after),
                      'beforeBytes': len(before), 'afterBytes': len(after)})
    report = {'schema': 1, 'phase': 'prepared', 'createdUtc': datetime.now(timezone.utc).isoformat(),
              'files': files, 'names': {'stableSource': stable_source, 'shared': shared_name, 'development': dev_name},
              'selectionSha256': digest(json.dumps(files, sort_keys=True).encode('utf-8')),
              'attempts': [], 'dataOrGrantsEdited': False}
    plan = folder / 'plan.json'
    write_new(plan, (json.dumps(report, indent=2) + '\n').encode('utf-8'))
    return plan


def load(plan):
    report = json.loads(ordinary_file(plan).read_text(encoding='utf-8'))
    if report.get('schema') != 1 or len(report.get('files', [])) != 2:
        raise ValueError('Unsupported registration plan')
    if report.get('selectionSha256') != digest(json.dumps(report['files'], sort_keys=True).encode('utf-8')):
        raise ValueError('Registration target selection changed; prepare a new reviewed plan')
    paths = []
    for index, item in enumerate(report['files']):
        target = Path(item['path'])
        if not target.is_absolute() or target.resolve() != target or target in paths:
            raise ValueError('Registration plan targets must be distinct ordinary absolute paths')
        ordinary_file(target)
        paths.append(target)
        for suffix in ('before', 'after'):
            if digest(ordinary_file(plan.parent / f'{index}.{suffix}.toml').read_bytes()) != item[suffix]:
                raise ValueError('Registration plan backup/proposal changed')
    return report


def current_states(report):
    return [digest(ordinary_file(Path(item['path'])).read_bytes()) for item in report['files']]


def inspect(plan):
    plan = Path(plan).resolve()
    with exclusive(plan.parent, name='registration.lock'):
        report = load(plan)
        return dict(report, observedFiles=[{'path': item['path'], 'sha256': actual,
                    'matchesBefore': actual == item['before'], 'matchesAfter': actual == item['after']}
                    for item, actual in zip(report['files'], current_states(report))])


def reconcile(plan, *, rollback=False):
    """Resume from actual bytes. @see prepare for immutable intent and inspect for status."""
    plan = Path(plan).resolve()
    with exclusive(plan.parent, name='registration.lock'):
        report = load(plan)
        started = time.monotonic()
        target_side = 'before' if rollback else 'after'
        source_side = 'after' if rollback else 'before'
        for prior in report['attempts']:
            if prior.get('outcome') == 'running':
                prior['outcome'] = 'unacknowledged'
        attempt = {'id': len(report['attempts']) + 1, 'outcome': 'running',
                   'action': 'rollback' if rollback else 'apply', 'writtenFiles': 0, 'writtenBytes': 0,
                   'startedUtc': datetime.now(timezone.utc).isoformat()}
        report['attempts'].append(attempt)
        try:
            states = current_states(report)
            if any(actual not in (item['before'], item['after']) for item, actual in zip(report['files'], states)):
                raise RuntimeError('Configuration changed concurrently; preserve external edits and inspect this plan')
            if all(actual == item[target_side] for item, actual in zip(report['files'], states)):
                report['phase'] = 'rolled_back' if rollback else 'complete'
                attempt['noOp'] = True
            else:
                report['phase'] = 'rolling_back' if rollback else 'applying'
                save(plan, report)
                for index, item in enumerate(report['files']):
                    target = Path(item['path'])
                    current = target.read_bytes()
                    if digest(current) == item[target_side]:
                        continue
                    if digest(current) != item[source_side]:
                        raise RuntimeError('Configuration changed concurrently; no external edit was overwritten')
                    desired = (plan.parent / f'{index}.{target_side}.toml').read_bytes()
                    registration.replace_if_unchanged(target, current, desired)
                    attempt['writtenFiles'] += 1
                    attempt['writtenBytes'] += len(desired)
                    save(plan, report)
                report['phase'] = 'rolled_back' if rollback else 'complete'
            report.pop('error', None)
            attempt['outcome'] = 'no_op' if attempt.get('noOp') else 'complete'
        except (Exception, KeyboardInterrupt) as error:
            report['phase'] = 'recovery_required'
            report['error'] = {'code': 'registration_recovery_required', 'type': type(error).__name__,
                               'message': str(error) or type(error).__name__,
                               'recovery': 'Inspect current bytes; resume this plan or roll back. Do not overwrite unrelated edits.'}
            attempt.update(outcome='failed', error=report['error'])
            raise
        finally:
            attempt['seconds'] = round(time.monotonic() - started, 6)
            try:
                save(plan, report)
            except Exception as retention:
                raise RuntimeError('Registration journal persistence failed; inspect actual files. Primary error: '
                                   + json.dumps(report.get('error')) + '; retention: ' + str(retention)) from retention
        return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='action', required=True)
    prepare_cli = commands.add_parser('prepare')
    for name in ('shared', 'workspace', 'output'):
        prepare_cli.add_argument('--' + name, type=Path, required=True)
    prepare_cli.add_argument('--stable-source', required=True)
    for name in ('apply', 'resume', 'rollback', 'status'):
        commands.add_parser(name).add_argument('--plan', type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.action == 'prepare':
            result = {'plan': str(prepare(args.shared, args.workspace, args.output, stable_source=args.stable_source)), 'applied': False}
        elif args.action == 'status':
            result = inspect(args.plan)
        else:
            result = reconcile(args.plan, rollback=args.action == 'rollback')
        print(json.dumps(result))
        return 0
    except (Exception, KeyboardInterrupt) as error:
        print(json.dumps({'error': {'code': 'registration_migration_failed', 'type': type(error).__name__,
                                   'message': str(error) or type(error).__name__, 'action': args.action,
                                   'plan': str(getattr(args, 'plan', '') or '')}}), file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())

"""Compatibility receipt entry for the unified durable C# update, without a client-exit countdown.

No client is killed or restarted. @see pilotage_stable.upgrade and complete_update.update.
"""

import argparse
import json
from pathlib import Path
import time
import uuid

import pilotage_stable as workflow
from complete_update import UpdateFailure, candidate_from_archive, invoke, report_error, update
from deployment import read, write
from runtime_layout import app_var, pilotage_stable_root


def arm(config, release, version, project, endpoint, thread, *, stable_root=None, server=workflow.SERVER):
    stable_root = stable_root or pilotage_stable_root(workflow.ROOT)
    raw, table, data, binding = workflow.registration_state(config, server)
    if not data.is_relative_to(stable_root):
        raise ValueError('The named connection is outside the reviewed stable scope')
    workflow.expected_membership(data, project, endpoint)
    archive, digest = workflow.verified_inputs(release, version)
    candidate = candidate_from_archive(archive, digest)
    if not (candidate / 'transport/AgentRelay.Transport.exe').is_file():
        raise ValueError('Client-exit standby is retired. Select a stable-transport release and its qualified initial migration')
    installation = data.parents[3]
    prepared = invoke({'executable': str(candidate / 'app' / workflow.EXE)}, installation, data, 'prepare', candidate)
    folder = stable_root / ('armed-' + uuid.uuid4().hex[:12])
    folder.mkdir()
    receipt = folder / 'result.json'
    write(receipt, dict(stage='armed', config=str(config), configSha256=workflow.sha256(config),
                        release=str(release), version=version, archiveSha256=digest, project=project, endpoint=endpoint,
                        thread=thread, server=server, data=str(data), binding=binding, transaction=prepared['id'], modelCalls=0))
    return receipt


def run(receipt, timeout=None):
    """Resume the transaction; the legacy timeout argument never expires or restarts clients."""
    receipt = receipt.absolute()
    root = pilotage_stable_root(workflow.ROOT)
    if not receipt.is_relative_to(root) or receipt.name != 'result.json':
        raise ValueError('Select a managed armed stable receipt')
    report = read(receipt)
    config, data = Path(report['config']), Path(report['data'])
    started = time.monotonic()
    failure = None
    try:
        server = report.get('server', workflow.SERVER)
        if workflow.sha256(config) != report['configSha256'] or workflow.registration_state(config, server)[2] != data:
            raise RuntimeError('The named registration changed since arming; no update was started')
        archive, digest = workflow.verified_inputs(Path(report['release']), report['version'])
        if digest != report['archiveSha256']:
            raise RuntimeError('The selected archive changed since arming')
        report['stage'] = 'resuming_transaction'
        write(receipt, report)
        def guard():
            if workflow.sha256(config) != report['configSha256']:
                raise RuntimeError('The named registration changed; no new update step was authorized')
        result = update(data, archive, digest, guard=guard)
        report.update(stage=result['state']['phase'], deploymentReceipt=str(result['receipt']),
                      applied=result['state']['phase'] == 'complete', transaction=result['state']['id'])
    except (Exception, KeyboardInterrupt) as error:
        report.update(stage='failed', error=report_error(data, error))
        failure = UpdateFailure(report['error'])
    finally:
        report['seconds'] = round(time.monotonic() - started, 3)
        try:
            write(receipt, report)
        except Exception as retention:
            combined = RuntimeError('The standby report could not be written: ' + str(retention))
            combined.related_diagnostics = [failure.error['diagnosticId']] if failure else []
            raise UpdateFailure(report_error(data, combined)) from retention
    if failure:
        raise failure
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='action', required=True)
    prepare = commands.add_parser('arm')
    prepare.add_argument('--server', default=workflow.SERVER)
    for name in ('config', 'release-directory'):
        prepare.add_argument('--' + name, required=True, type=Path)
    for name in ('version', 'project', 'endpoint', 'thread'):
        prepare.add_argument('--' + name, required=True)
    apply = commands.add_parser('run')
    apply.add_argument('--receipt', required=True, type=Path)
    args = parser.parse_args()
    data = Path.cwd()
    try:
        if args.action == 'arm':
            data = workflow.registration_state(args.config.absolute(), args.server)[2]
            result = dict(receipt=str(arm(args.config.absolute(), args.release_directory.absolute(), args.version,
                                          args.project, args.endpoint, args.thread, server=args.server)))
        else:
            data = Path(read(args.receipt)['data'])
            result = run(args.receipt)
    except (Exception, KeyboardInterrupt) as error:
        import sys
        print(json.dumps({'error': report_error(data, error)}), file=sys.stderr)
        return 1
    print(json.dumps(result))
    return 0


if __name__ == '__main__':
    import sys
    sys.exit(main())

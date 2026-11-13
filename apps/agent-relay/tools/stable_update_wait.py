"""Arm a qualified stable update and run the existing upgrade after its MCP clients exit.

No client is killed or restarted. @see pilotage_stable.upgrade and complete_update.update.
"""

import argparse
import json
from pathlib import Path
import time
import uuid

import pilotage_stable as workflow
from complete_update import UpdateFailure, candidate_from_archive, invoke, report_error
from deployment import read, write
from host_identity import command_parts, processes, same_absolute_path
from runtime_layout import app_var, pilotage_stable_root


def clients(installation, data, inventory):
    selected = []
    for item in inventory:
        executable = item.get('ExecutablePath')
        if not executable or not Path(executable).is_absolute() or not Path(executable).is_relative_to(installation):
            continue
        parts = command_parts(item.get('CommandLine') or '')
        if (len(parts) < 4 or not same_absolute_path(parts[0], executable)
                or parts[1] not in ('mcp', 'mcp-worker') or parts.count('--data-dir') != 1):
            continue
        index = parts.index('--data-dir')
        if index + 1 < len(parts) and same_absolute_path(parts[index + 1], data):
            selected.append(int(item['ProcessId']))
    return sorted(selected)


def arm(config, release, version, project, endpoint, thread, *, stable_root=None, server=workflow.SERVER):
    stable_root = stable_root or pilotage_stable_root(workflow.ROOT)
    raw, table, data, binding = workflow.registration_state(config, server)
    if not data.is_relative_to(stable_root):
        raise ValueError('The named connection is outside the reviewed stable scope')
    workflow.expected_membership(data, project, endpoint)
    archive, digest = workflow.verified_inputs(release, version)
    candidate = candidate_from_archive(archive, digest)
    installation = data.parents[3]
    prepared = invoke({'executable': str(candidate / 'app' / workflow.EXE)}, installation, data, 'prepare', candidate)
    folder = stable_root / ('armed-' + uuid.uuid4().hex[:12])
    folder.mkdir()
    receipt = folder / 'result.json'
    write(receipt, dict(stage='armed', config=str(config), configSha256=workflow.sha256(config),
                        release=str(release), version=version, archiveSha256=digest, project=project, endpoint=endpoint,
                        thread=thread, server=server, data=str(data), binding=binding, transaction=prepared['id'], modelCalls=0))
    return receipt


def run(receipt, timeout=900):
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
        deadline = time.monotonic() + timeout
        report['stage'] = 'waiting_clients'
        write(receipt, report)
        while True:
            active = clients(data.parents[3], data, processes({'executable': str(data / 'mcp-launcher' / workflow.EXE)}))
            report.update(remainingClientProcesses=active, waitSeconds=round(time.monotonic() - started, 3))
            write(receipt, report)
            if not active:
                break
            if time.monotonic() >= deadline:
                raise TimeoutError('MCP clients still hold the stable installation. It was not replaced; close/reload the owning client before retrying')
            time.sleep(1)
        result = workflow.upgrade(config, Path(report['release']), report['version'], report['project'],
                                  report['endpoint'], report['thread'], root, server=server)
        report.update(stage='awaiting_chat', deploymentReceipt=str(result), applied=True)
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

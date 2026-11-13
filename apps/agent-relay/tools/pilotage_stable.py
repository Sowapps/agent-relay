"""Prepare and switch the named Pilotage AP connection to a verified stable archive."""

import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import time
import tomllib
import uuid

from deployment import PromotionFreeze, demand_idle, exclusive, launch, snapshot, write
from relay_mcp import RelayMcp
from release_artifact import (
    MANIFEST_FILE, VERIFICATION_FILE, archive_path, declared_checksum, extract_archive,
    ordinary_directory, ordinary_file, read_json, sha256, verified_archive,
    qualified_archive,
)
from runtime_layout import app_var, pilotage_legacy_scope, pilotage_stable_root
from complete_update import startup as startup_state
from complete_update import report_error
from complete_update import update as complete_update
from client_refresh import CodexControlConnector
import codex_registration as registration


ROOT = Path(__file__).resolve().parents[3]
EXE = 'AgentRelay.Windows.exe'
DOC_URI = 'agent-relay://docs/tools'
STATE_FILES = ('relay.sqlite', 'relay-projects.json', 'relay-config.json', 'attention-reviews.json')
SERVER = registration.SHARED_SERVER
LEGACY_SERVER = registration.LEGACY_STABLE_SERVER


def now():
    return datetime.now(timezone.utc).isoformat()


def registration_state(config, server=SERVER):
    raw = ordinary_file(config).read_bytes()
    servers = tomllib.loads(raw.decode('utf-8-sig')).get('mcp_servers', {})
    if server == SERVER and LEGACY_SERVER in servers:
        raise ValueError('Legacy stable alias is present; prepare the shared registration migration or explicitly select --server ' + LEGACY_SERVER + ' for reviewed legacy maintenance')
    table = servers.get(server)
    if table is None:
        raise ValueError(f'No named connection {server} in the selected configuration')
    args = table['args']
    if not isinstance(args, list) or 'mcp' not in args or any(
        args.count(flag) != 1 or args.index(flag) == len(args) - 1
        for flag in ('--data-dir', '--connection-id')
    ):
        raise ValueError('The named connection needs explicit relay data and a scoped credential')
    data = Path(args[args.index('--data-dir') + 1])
    if not data.is_absolute() or '..' in data.parts:
        raise ValueError('The named connection needs an absolute data path')
    return raw, table, ordinary_directory(data), str(uuid.UUID(args[args.index('--connection-id') + 1]))


def route(table):
    return {key: table.get(key) for key in ('command', 'args', 'enabled')}


def locations(receipt, report=None):
    if report and report.get('runtimeExecutable') and report.get('data'):
        return Path(report['runtimeExecutable']), Path(report['data'])
    installation = receipt.parent / 'installation'
    return installation / 'app' / EXE, app_var(installation) / 'default'


def fingerprint(folder, files):
    return {path.relative_to(folder).as_posix(): sha256(ordinary_file(path)) for path in sorted(files)}


def source_fingerprint(source):
    credentials = ordinary_directory(source / 'connections')
    files = [source / name for name in STATE_FILES if (source / name).exists()]
    wal = source / 'relay.sqlite-wal'
    if wal.exists() and ordinary_file(wal).stat().st_size:
        files.append(wal)
    return fingerprint(source, [*files, *credentials.glob('*.bin')])


def prepared_fingerprint(data):
    runtime_files = ('mcp-runtime.json', 'mcp-contract.json', 'mcp-contract-history.json')
    return source_fingerprint(data) | fingerprint(data, (data / name for name in runtime_files if (data / name).exists()))


def installation_fingerprint(executable):
    return fingerprint(executable.parent, (p for p in executable.parent.rglob('*') if p.is_file()))


@contextmanager
def stopped_source(source, guard_runtime):
    ordinary_file(source / 'relay.sqlite')
    ordinary_file(source / 'host.lock')
    with exclusive(source):
        if (source / 'migrated-to.json').exists() or (source / 'promotion.lock').exists():
            raise ValueError('Source was migrated or has a pending promotion')
        demand_idle(guard_runtime, source)
        yield


def doc_digest(content):
    return hashlib.sha256(content.replace('\r\n', '\n').encode('utf-8')).hexdigest()


def expected_membership(data, project, endpoint):
    configured = read_json(data / 'relay-projects.json')['memberships']
    memberships = [item for item in configured if item['projectId'] == project and item['endpointId'] == endpoint]
    if len(memberships) != 1:
        raise ValueError('The expected project and participant do not have exactly one membership')
    return memberships


def scoped_launcher(executable, data, retained=None):
    runtime = read_json(data / 'mcp-runtime.json')
    declared = runtime.get('mcpExecutable')
    if declared is None:
        if retained is not None:
            raise ValueError('The retained MCP launcher disappeared')
        return executable
    broker = data / 'mcp-launcher' / EXE
    digest = retained or sha256(ordinary_file(executable))
    if (Path(declared) != broker or Path(runtime['executable']) != executable
            or sha256(ordinary_file(broker)) != digest):
        raise ValueError('The scoped MCP launcher differs from the staged archive')
    return broker


def probe(receipt, report):
    executable, data = locations(receipt, report)
    launcher = scoped_launcher(executable, data, report.get('retainedBroker'))
    runtime = {'executable': str(launcher), 'connectionId': report['bindingId']}
    with (receipt.parent / 'mcp.stderr.log').open('a', encoding='utf-8') as errors:
        with RelayMcp(runtime, data, ROOT, errors) as mcp:
            version = mcp.initialization['serverInfo']['version']
            guide = mcp.call('resources/read', {'uri': DOC_URI})['contents'][0]['text']
            configuration = mcp.tool('list_projects', timeout=15)['configuration']
            has_status = any(tool['name'] == 'get_connection_status' for tool in mcp.inventory)
            if has_status:
                connection = mcp.tool('get_connection_status', timeout=15)
                if connection['version'] != report['version'] or connection['clientRefreshRequired']:
                    raise ValueError('The current relay and client catalog did not agree')
    if version != report['version'] or doc_digest(guide) != report['toolsDocSha256']:
        raise ValueError('MCP version or tools documentation differs from the verified archive')
    if [p['id'] for p in configuration['projects']] != [report['projectId']]:
        raise ValueError('The named credential did not return exactly the expected project')
    # Newer serializers may add metadata. Compare the persisted fields that grant authority.
    actual = configuration['memberships']
    expected = expected_membership(data, report['projectId'], report['endpointId'])
    if len(actual) != 1 or any(actual[0].get(key) != value for key, value in expected[0].items()):
        raise ValueError('The authenticated participant or its grants differ from the preserved project')
    return {'version': version, 'toolsDocSha256': report['toolsDocSha256'], 'projectId': report['projectId'],
            'endpointId': report['endpointId'], 'membership': actual[0], 'tools': len(mcp.inventory),
            'reportsVersionToClient': has_status}


def stop_owned(process):
    if process.poll() is None:
        process.terminate()
    process.wait(timeout=10)

def verified_inputs(release, version, *, rehearsal=False):
    return qualified_archive(release, version, allow_rc=rehearsal)


def release_manifest(package, version):
    with package.open(MANIFEST_FILE) as manifest_file:
        manifest = json.load(manifest_file)
    if manifest.get('version') != version or manifest.get('selfContained') is not True:
        raise ValueError('Archive manifest does not match the selected self-contained release')
    return manifest


def stage_archive(archive, digest, version, executable):
    with verified_archive(archive, digest) as package:
        release_manifest(package, version)
        executable.parent.parent.mkdir(parents=True)
        extract_archive(package, executable.parent, prefix='app/')


def prepare(config, release, version, project, endpoint, thread, stable_root, legacy_scope=None, *, guard_runtime=None, server=SERVER):
    archive, digest = verified_inputs(release, version)
    _, table, source, binding = registration_state(config, server)
    if not source.is_relative_to(stable_root) and source != legacy_scope:
        raise ValueError('The named connection points outside the declared Pilotage AP scopes')
    if table.get('enabled', True) is not True:
        raise ValueError('The named connection is disabled; review it before preparing a switch')
    project, endpoint, thread = (str(uuid.UUID(value)) for value in (project, endpoint, thread))
    folder = ordinary_directory(stable_root / f'v{version}-{uuid.uuid4().hex}')
    folder.mkdir(parents=True)
    receipt = folder / 'result.json'
    executable, data = locations(receipt)
    report = {'stage': 'preparing', 'startedUtc': now(), 'version': version, 'archiveSha256': digest,
              'source': str(source), 'config': str(config), 'bindingId': binding, 'projectId': project,
              'endpointId': endpoint, 'threadId': thread, 'server': server, 'modelCalls': 0,
              'challenge': uuid.uuid4().hex, 'previousRoute': route(table)}
    started = time.monotonic()
    write(receipt, report)
    try:
        stage_archive(archive, digest, version, executable)
        report['installationFiles'] = installation_fingerprint(executable)
        guide = ordinary_file(executable.parent / 'documentation/mcp-tools.md')
        report['toolsDocSha256'] = doc_digest(guide.read_text(encoding='utf-8-sig'))
        data.parent.mkdir(parents=True)
        with stopped_source(source, guard_runtime or {'executable': str(executable)}):
            report['sourceFiles'] = source_fingerprint(source)
            expected_membership(source, project, endpoint)
            snapshot(source, data, files=STATE_FILES)
        # Initialize version-specific runtime files in the copy. Preserve the named credential.
        initialized = subprocess.run([str(executable), 'initialize', '--data-dir', str(data), '--connection-id', binding],
                                     capture_output=True, timeout=25, creationflags=subprocess.CREATE_NO_WINDOW)
        if initialized.returncode:
            (folder / 'initialize.stderr.log').write_bytes(initialized.stderr)
            raise RuntimeError('The stable archive could not initialize the copied data; see initialize.stderr.log')
        process = launch({'executable': str(executable)}, data, folder)
        try:
            report['freshMcp'] = probe(receipt, report)
        finally:
            stop_owned(process)
        report['preparedFiles'] = prepared_fingerprint(data)
        report['stage'] = 'prepared'
    except (Exception, KeyboardInterrupt) as error:
        report.update(stage='prepare-failed', error=str(error) or type(error).__name__)
        raise
    finally:
        report.update(prepareSeconds=round(time.monotonic() - started, 3), updatedUtc=now())
        write(receipt, report)
    return receipt


def candidate_table(receipt, report):
    executable, data = locations(receipt, report)
    return {'command': str(scoped_launcher(executable, data, report.get('retainedBroker'))),
            'args': ['mcp', '--data-dir', str(data), '--connection-id', report['bindingId']]}


def matches_target(table, receipt, report):
    return table.get('enabled', True) is True and all(
        table.get(key) == value for key, value in candidate_table(receipt, report).items()
    )


def switch(receipt, report, *, guard_runtime=None):
    """Commit only the named registration, after all fallible host and scope checks."""
    if report['stage'] != 'prepared':
        raise ValueError('A prepared receipt is required; inspect status before retrying an interrupted switch')
    config = Path(report['config'])
    executable, data = locations(receipt)
    source = Path(report['source'])
    server = report['server']
    raw, table, selected, binding = registration_state(config, server)
    if selected != source or binding != report['bindingId'] or route(table) != report['previousRoute']:
        raise ValueError('The named connection changed after preparation')
    fragment = '[mcp_servers.' + json.dumps(server) + ']\n' + ''.join(
        f'{key} = {registration.toml_value(value)}\n' for key, value in candidate_table(receipt, report).items()
    )
    desired = registration.update(raw.decode('utf-8-sig'), fragment, server).encode('utf-8')
    started = time.monotonic()
    guard_runtime = guard_runtime or {'executable': str(executable)}
    with stopped_source(source, guard_runtime):
        if source_fingerprint(source) != report['sourceFiles']:
            raise ValueError('The source changed after preparation; prepare a new copy')
        with stopped_source(data, guard_runtime):
            if (installation_fingerprint(executable) != report['installationFiles']
                    or prepared_fingerprint(data) != report['preparedFiles']):
                raise ValueError('The prepared archive or data changed; prepare a new copy')
        report.update(stage='switching', updatedUtc=now())
        write(receipt, report)
        process = None
        try:
            process = launch({'executable': str(executable)}, data, receipt.parent)
            report['freshMcp'] = probe(receipt, report)
            if process.poll() is not None:
                raise RuntimeError('The stable host stopped before registration')
            registration.replace_if_unchanged(config, raw, desired)
        except (Exception, KeyboardInterrupt) as error:
            # A signal may arrive after the atomic replacement. Never stop a possibly selected host.
            _, current, _, _ = registration_state(config, server)
            if not matches_target(current, receipt, report):
                if process is not None:
                    stop_owned(process)
                report.update(stage='switch-failed', error=str(error) or type(error).__name__, updatedUtc=now(),
                              switchSeconds=round(time.monotonic() - started, 3))
                write(receipt, report)
            raise
        report.update(stage='awaiting-chat', hostPid=process.pid, updatedUtc=now(),
                      switchSeconds=round(time.monotonic() - started, 3))
        write(receipt, report)
    return report


def startup_transfer_plan(executable, data, previous):
    result = subprocess.run([str(executable), 'startup-transfer', '--data-dir', str(data),
                             '--previous-data-dir', str(previous)],
                            capture_output=True, timeout=12, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        return {'status': 'review_required', 'previousData': str(previous)}
    plan = json.loads(result.stdout)
    return dict(plan, status='transfer_available' if plan['previousEnabled'] else 'no_legacy_entry',
                previousData=str(previous))


def upgrade(config, release, version, project, endpoint, thread, stable_root, *, rehearsal=False, legacy_startup=None, server=SERVER):
    """Run the shared complete update while retaining the reviewed scoped witness."""
    raw, table, data, binding = registration_state(config, server)
    if rehearsal and not stable_root.is_relative_to(ROOT / 'local/apps/agent-relay'):
        raise ValueError('RC rehearsal requires a private developer fixture')
    if (not data.is_relative_to(stable_root) or table.get('enabled', True) is not True
            or (data / 'migrated-to.json').exists() or (data / 'maintenance.json').exists()):
        raise ValueError('The named connection is not on an active managed stable scope')
    installation = data.parents[3]
    if data != app_var(installation) / 'default' or installation.name != 'installation':
        raise ValueError('The stable connection does not use the retained portable layout')
    broker = data / 'mcp-launcher' / EXE
    pointer = ordinary_file(data / 'mcp-runtime.json')
    previous = read_json(pointer)
    if (Path(table['command']) != broker or Path(previous.get('mcpExecutable', '')) != broker
            or previous.get('distribution') != 'portable-win-x64'):
        raise ValueError('The scope first needs the guarded one-time retained launcher cutover')
    marker = read_json(broker.parent / 'installation.json')
    if marker.get('version') != 1 or (broker.parent / marker['root']).resolve() != installation:
        raise ValueError('The retained broker belongs to another installation')

    project, endpoint, thread = (str(uuid.UUID(value)) for value in (project, endpoint, thread))
    expected_membership(data, project, endpoint)
    archive, digest = verified_inputs(release, version, rehearsal=rehearsal)
    identifier = uuid.uuid4().hex[:12]
    folder = ordinary_directory(stable_root / ('upgrade-' + identifier))
    folder.mkdir()
    receipt = folder / 'result.json'
    executable = installation / 'app' / EXE
    report = {'stage': 'preparing-upgrade', 'startedUtc': now(), 'version': version, 'archiveSha256': digest,
              'config': str(config), 'data': str(data), 'runtimeExecutable': str(executable),
              'bindingId': binding, 'projectId': project,
              'endpointId': endpoint, 'threadId': thread, 'server': server, 'modelCalls': 0,
              'challenge': uuid.uuid4().hex}
    started = time.monotonic()
    write(receipt, report)
    try:
        with verified_archive(archive, digest) as package:
            report['toolsDocSha256'] = doc_digest(package.read('app/documentation/mcp-tools.md').decode('utf-8-sig'))
            quickstart_hash = doc_digest(package.read('app/documentation/agent-quickstart.md').decode('utf-8-sig'))
            candidate_contract = json.loads(package.read('app/mcp-contract.json'))
        if legacy_startup is not None and legacy_startup != data:
            report['legacyStartup'] = startup_transfer_plan(executable, data, legacy_startup)
        def guard():
            if config.read_bytes() != raw:
                raise RuntimeError('Codex registration changed during the stable update')
        def refresh(reports):
            expected = dict(version=version, contractHash=candidate_contract['contractHash'],
                            connectionIds=[binding], quickstartHash=quickstart_hash)
            return CodexControlConnector(ROOT, reports, expected=expected,
                                         diagnostic=lambda error: report_error(data, error)).refresh(server, thread)

        outcome = complete_update(data, archive, digest, guard=guard, verify=lambda: probe(receipt, report), refresh=refresh)
        ports = read_json(Path(outcome['receipt']))
        report.update(stage='awaiting-chat', hostPid=ports.get('hostPid'), freshMcp=outcome['verification'],
                      startupEnabled=ports['startupEnabled'], completeUpdate=outcome['state'],
                      deploymentTransaction=None if outcome['alreadyCurrent'] else outcome['receipt'])
    except (Exception, KeyboardInterrupt) as error:
        report.update(stage='upgrade-failed', error=str(error) or type(error).__name__)
        raise
    finally:
        report.update(upgradeSeconds=round(time.monotonic() - started, 3), updatedUtc=now())
        write(receipt, report)
    return receipt


def chat_check(report):
    result = {key: report[key] for key in ('threadId', 'server', 'challenge', 'projectId', 'toolsDocSha256')}
    if report['freshMcp']['reportsVersionToClient']:
        result['version'] = report['version']
    return result


def status(config, receipt=None, report=None, server=SERVER):
    if report is not None and report['server'] != server:
        raise ValueError('The receipt selects another server; explicitly select its recorded --server')
    _, table, data, binding = registration_state(config, server)
    command = Path(table['command'])
    stable_root = pilotage_stable_root(ROOT)
    location = 'managed-stable' if (data.is_relative_to(stable_root) and command.is_relative_to(stable_root)) else 'outside-managed-stable'
    result = {'server': server, 'data': str(data), 'bindingId': binding, 'command': str(command), 'location': location,
              'enabled': table.get('enabled', True), 'chatConfirmed': False}
    if receipt is None or str(config) != report['config'] or data != locations(receipt, report)[1]:
        return dict(result, stage='registration-only')
    installed = read_json(data / 'mcp-runtime.json')
    if Path(installed['executable']) != locations(receipt, report)[0]:
        return dict(result, stage='superseded')
    contract_path = data / 'mcp-contract.json'
    if contract_path.exists() and read_json(contract_path).get('version') != report['version']:
        return dict(result, stage='superseded')
    if not matches_target(table, receipt, report):
        return dict(result, stage='registration-only')
    result['freshMcp'] = probe(receipt, report)
    confirmed = report['stage'] == 'confirmed'
    return dict(result, stage='confirmed' if confirmed else 'awaiting-chat', chatConfirmed=confirmed,
                chatCheck=chat_check(report))


def confirm(receipt, report, proof):
    """Record evidence read from the owning chat. This command never sends a chat message."""
    started = time.monotonic()
    if proof != dict(chat_check(report), listProjectsSucceeded=True):
        raise ValueError('Same-chat proof did not match the challenge, server, project or catalog')
    state = status(Path(report['config']), receipt, report, server=report['server'])
    if state['stage'] not in ('awaiting-chat', 'confirmed'):
        raise ValueError('The named connection is no longer pointing at the prepared stable archive')
    report.update(stage='confirmed', chatProof=proof, updatedUtc=now(),
                  confirmSeconds=round(time.monotonic() - started, 3))
    write(receipt, report)
    return {'stage': 'confirmed', 'threadId': report['threadId'], 'version': report['version'],
            'message': f"Agent Relay stable {report['version']} est confirmé dans ce chat. "
                       "Tu peux reprendre avec une nouvelle requête, sans rejouer l'ancienne."}


def main():
    started = time.monotonic()
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='action', required=True)
    prepare_cli = commands.add_parser('prepare')
    prepare_cli.add_argument('--config', required=True, type=Path)
    prepare_cli.add_argument('--release-directory', required=True, type=Path)
    for key in ('version', 'project', 'endpoint', 'thread'):
        prepare_cli.add_argument('--' + key, required=True)
    upgrade_cli = commands.add_parser('upgrade')
    upgrade_cli.add_argument('--config', required=True, type=Path)
    upgrade_cli.add_argument('--release-directory', required=True, type=Path)
    upgrade_cli.add_argument('--apply', required=True, action='store_true')
    for key in ('version', 'project', 'endpoint', 'thread'):
        upgrade_cli.add_argument('--' + key, required=True)
    status_cli = commands.add_parser('status')
    status_cli.add_argument('--config', required=True, type=Path)
    status_cli.add_argument('--receipt', type=Path)
    for command in (prepare_cli, upgrade_cli, status_cli):
        command.add_argument('--server', default=SERVER, help='Explicit MCP registration; no automatic legacy fallback')
    switch_cli = commands.add_parser('switch')
    switch_cli.add_argument('--receipt', required=True, type=Path)
    switch_cli.add_argument('--apply', required=True, action='store_true')
    confirm_cli = commands.add_parser('confirm')
    confirm_cli.add_argument('--receipt', required=True, type=Path)
    confirm_cli.add_argument('--proof', required=True, type=Path)
    args = parser.parse_args()
    stable_root = pilotage_stable_root(ROOT)
    try:
        if args.action == 'prepare':
            receipt = prepare(args.config.absolute(), args.release_directory.absolute(), args.version, args.project,
                              args.endpoint, args.thread, stable_root, pilotage_legacy_scope(ROOT), server=args.server)
            result = {'stage': 'prepared', 'receipt': str(receipt)}
        elif args.action == 'upgrade':
            receipt = upgrade(args.config.absolute(), args.release_directory.absolute(), args.version, args.project,
                              args.endpoint, args.thread, stable_root, legacy_startup=pilotage_legacy_scope(ROOT), server=args.server)
            report = read_json(receipt)
            result = {'stage': report['stage'], 'receipt': str(receipt), 'version': report['version'],
                      'chatCheck': chat_check(report), 'startupEnabled': report['startupEnabled'],
                      'legacyStartup': report.get('legacyStartup')}
        else:
            receipt = args.receipt.absolute() if args.receipt else None
            if receipt and (receipt.parent.parent != stable_root or receipt.name != 'result.json'):
                raise ValueError('Select the result.json of a managed stable installation')
            report = read_json(receipt) if receipt else None
            if args.action == 'status':
                result = status(args.config.absolute(), receipt, report, server=args.server)
            elif args.action == 'switch':
                report = switch(receipt, report)
                result = {'stage': report['stage'], 'version': report['version'], 'chatCheck': chat_check(report)}
            else:
                result = confirm(receipt, report, read_json(args.proof.absolute()))
        print(json.dumps(dict(result, seconds=round(time.monotonic() - started, 3), modelCalls=0),
                         ensure_ascii=True))
    except (OSError, ValueError, RuntimeError, KeyError, subprocess.SubprocessError) as error:
        parser.exit(1, f'{error}\n')


if __name__ == '__main__':
    main()

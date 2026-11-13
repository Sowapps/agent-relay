"""Rehearse the stable switch with the portable verifier's fictional project and Codex config."""

import argparse
import json
import os
from pathlib import Path
import signal
import tomllib
from unittest.mock import patch
import uuid

import pilotage_stable as workflow
from deployment import snapshot
from host_identity import identity, resolve_host
from relay_mcp import RelayMcp
from release_artifact import EXTRACTED_DIRECTORY, VERIFICATION_FILE, read_json
from runtime_layout import app_var, pilotage_stable_root, reject_pilotage_paths, validate_runtime


def rejected(action, message):
    try:
        action()
    except (ValueError, RuntimeError) as error:
        assert message in str(error), str(error)
    else:
        raise AssertionError(f'Expected refusal: {message}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release-directory', required=True, type=Path)
    parser.add_argument('--server', default=workflow.SERVER)
    parser.add_argument('--baseline-directory', type=Path, help='Use an older portable verifier fixture as the source')
    parser.add_argument('--upgrade-release-directory', type=Path, help='Rehearse an in-place candidate update after cutover')
    parser.add_argument('--guard-runtime-pointer', type=Path, help='Prepared current C# runtime for rehearsing an older archive')
    args = parser.parse_args()
    release = args.release_directory.resolve()
    verification = read_json(release / VERIFICATION_FILE)
    baseline = args.baseline_directory.resolve() if args.baseline_directory else release
    extracted = baseline / EXTRACTED_DIRECTORY
    assert Path(read_json(baseline / VERIFICATION_FILE)['extracted']) == extracted
    fixture = read_json(extracted / 'requests-0/example-context.json')
    assert fixture['localOnly'] is True
    original = app_var(extracted) / 'default'
    if not original.is_dir():
        original = extracted / 'data'  # Storage of the stable 1.0.1 verifier.
    stable = pilotage_stable_root(workflow.ROOT)
    guard = None
    if args.guard_runtime_pointer:
        pointer = args.guard_runtime_pointer.resolve()
        reject_pilotage_paths(workflow.ROOT, pointer)
        guard = read_json(pointer)
        validate_runtime(guard, workflow.ROOT, development=True)
    rejected(lambda: reject_pilotage_paths(workflow.ROOT, stable / 'installation'), 'separately authorized')
    test = workflow.ROOT / 'local/apps/agent-relay/tests' / ('ap-' + uuid.uuid4().hex[:8])
    test.mkdir(parents=True)
    sandbox = test / 's'
    sandbox.mkdir()
    source = sandbox / 'source'
    snapshot(original, source, files=workflow.STATE_FILES)
    (source / 'host.lock').touch()
    config = test / 'config.toml'
    command = str(extracted / 'app' / workflow.EXE)
    arguments = ['mcp', '--data-dir', str(source), '--connection-id', fixture['connectionId']]
    config.write_text('[mcp_servers.' + json.dumps(args.server) + ']\ncommand = ' + json.dumps(command)
                      + '\nargs = ' + json.dumps(arguments) + '\nrequired = true\n'
                      + '[mcp_servers.unrelated]\ncommand = "preserved"\n', encoding='utf-8')
    initial_config = config.read_bytes()
    initial_source = workflow.source_fingerprint(source)
    project, endpoint, thread = fixture['projectId'], fixture['mainId'], str(uuid.uuid4())
    version = verification['version']
    upgrade_result = None
    rejected(lambda: workflow.prepare(config, release, version + '-rc.1', project, endpoint, thread, sandbox, server=args.server),
             'stable releases only')
    receipt = workflow.prepare(config, release, version, project, endpoint, thread, sandbox, guard_runtime=guard, server=args.server)
    report = read_json(receipt)
    assert report['freshMcp']['projectId'] == project and report['freshMcp']['endpointId'] == endpoint
    assert config.read_bytes() == initial_config
    assert workflow.source_fingerprint(source) == initial_source
    changed = source / 'attention-reviews.json'
    changed.write_text('{}', encoding='utf-8')
    rejected(lambda: workflow.switch(receipt, report, guard_runtime=guard), 'source changed')
    assert config.read_bytes() == initial_config
    changed.unlink()
    # Simulate a failing atomic registration after the real candidate is healthy.
    hosts = []
    launch = workflow.launch

    def track_launch(*arguments):
        host = launch(*arguments)
        hosts.append(host)
        return host

    with patch.object(workflow, 'launch', track_launch):
        with patch.object(workflow.registration, 'replace_if_unchanged', side_effect=RuntimeError('fixture conflict')):
            rejected(lambda: workflow.switch(receipt, report, guard_runtime=guard), 'fixture conflict')
        assert hosts[-1].poll() is not None and config.read_bytes() == initial_config
        receipt = workflow.prepare(config, release, version, project, endpoint, thread, sandbox, guard_runtime=guard, server=args.server)
        try:
            with config.open('a', encoding='utf-8') as stream:
                stream.write('required = false\n')
            report = workflow.switch(receipt, read_json(receipt), guard_runtime=guard)
            raw, table, data, binding = workflow.registration_state(config, args.server)
            assert table['required'] is True and binding == fixture['connectionId']
            assert tomllib.loads(raw.decode())['mcp_servers']['unrelated'] == {'command': 'preserved', 'required': False}
            assert (data / 'relay-projects.json').read_bytes() == (source / 'relay-projects.json').read_bytes()
            assert workflow.source_fingerprint(source) == initial_source
            assert workflow.status(config, receipt, report, server=args.server)['chatConfirmed'] is False
            proof = dict(workflow.chat_check(report), listProjectsSucceeded=True)
            rejected(lambda: workflow.confirm(receipt, report, dict(proof, toolsDocSha256='old catalog')),
                     'did not match')
            assert workflow.confirm(receipt, report, proof)['stage'] == 'confirmed'
            assert workflow.status(config, receipt, report, server=args.server)['chatConfirmed'] is True
            if args.upgrade_release_directory:
                upgrade_result = rehearse_upgrade(config, args.upgrade_release_directory.resolve(), receipt, report,
                                                  data, project, endpoint, thread, sandbox)
        finally:
            for host in hosts:
                workflow.stop_owned(host)
    print(json.dumps({'passed': True, 'fixtureReceipt': str(receipt), 'inPlaceUpgradeReceipt': upgrade_result,
                      'chatEvidenceSimulated': True, 'modelCalls': 0}))


def rehearse_upgrade(config, release, receipt, report, data, project, endpoint, thread, sandbox):
    version = read_json(release / VERIFICATION_FILE)['version']
    registered = config.read_bytes()
    initial_startup = workflow.startup_state(workflow.locations(receipt)[0], data)
    assert initial_startup is False, 'A fictional installation should not have a sign-in entry'
    previous = workflow.candidate_table(receipt, report)
    before = workflow.source_fingerprint(data)
    updated = None
    try:
        # Updating the broker binary is different from swapping only its worker. Do not
        # keep an old mapped executable open and pretend that its bytes were upgraded.
        updated = workflow.upgrade(config, release, version, project, endpoint, thread, sandbox, rehearsal=True, server=report['server'])
        upgraded = read_json(updated)
        assert upgraded['startupEnabled'] is initial_startup
        assert workflow.registration_state(config, report['server'])[2] == data and config.read_bytes() == registered
        assert workflow.candidate_table(updated, upgraded) == previous
        assert workflow.source_fingerprint(data)['relay-projects.json'] == before['relay-projects.json']
        runtime = {'executable': previous['command'], 'connectionId': report['bindingId']}
        with (sandbox / 'retained-client.stderr.log').open('w', encoding='utf-8') as errors:
            with RelayMcp(runtime, data, workflow.ROOT, errors) as client:
                status = client.tool('get_connection_status')
                assert status['version'] == status['brokerVersion'] == version and status['clientRefreshRequired'] is False
                assert client.tool('list_projects')['configuration']['projects'][0]['id'] == project
                repeated = workflow.upgrade(config, release, version, project, endpoint, thread, sandbox, rehearsal=True, server=report['server'])
                repeated_report = read_json(repeated)
                assert repeated_report['hostPid'] == upgraded['hostPid']
                assert repeated_report['runtimeExecutable'] == upgraded['runtimeExecutable']
                assert repeated_report['deploymentTransaction'] is None
        assert workflow.status(config, receipt, report, server=report['server'])['stage'] == 'superseded'
        return str(updated)
    finally:
        if updated:
            executable, _ = workflow.locations(updated, read_json(updated))
            owner = resolve_host({'executable': str(executable)}, data)
            if owner and identity(owner, data):
                os.kill(owner['pid'], signal.SIGTERM)


if __name__ == '__main__':
    main()

"""Rehearse the stable switch with the portable verifier's fictional project and Codex config."""

import argparse
import json
from pathlib import Path
import tomllib
from unittest.mock import patch
import uuid

import pilotage_stable as workflow
from deployment import snapshot
from release_artifact import EXTRACTED_DIRECTORY, VERIFICATION_FILE, read_json
from runtime_layout import app_var, pilotage_stable_root, reject_pilotage_paths


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
    args = parser.parse_args()
    release = args.release_directory.resolve()
    verification = read_json(release / VERIFICATION_FILE)
    extracted = release / EXTRACTED_DIRECTORY
    assert Path(verification['extracted']) == extracted
    fixture = read_json(extracted / 'requests-0/example-context.json')
    assert fixture['localOnly'] is True
    original = app_var(extracted) / 'default'
    if not original.is_dir():
        original = extracted / 'data'  # Storage of the stable 1.0.1 verifier.
    stable = pilotage_stable_root(workflow.ROOT)
    rejected(lambda: reject_pilotage_paths(workflow.ROOT, stable / 'installation'), 'separately authorized')
    test = workflow.ROOT / 'local/apps/agent-relay/tests' / ('ap-' + uuid.uuid4().hex[:8])
    test.mkdir(parents=True)
    sandbox = test / 's'
    sandbox.mkdir()
    source = sandbox / 'source'
    snapshot(original, source, files=workflow.STATE_FILES)
    (source / 'host.lock').touch()
    config = test / 'config.toml'
    command = workflow.candidate_table(test / 'result.json', fixture['connectionId'])
    command['args'][2] = str(source)
    config.write_text(f'[mcp_servers.{workflow.SERVER}]\ncommand = ' + json.dumps(command['command'])
                      + '\nargs = ' + json.dumps(command['args']) + '\nrequired = true\n'
                      + '[mcp_servers.unrelated]\ncommand = "preserved"\n', encoding='utf-8')
    initial_config = config.read_bytes()
    initial_source = workflow.source_fingerprint(source)
    project, endpoint, thread = fixture['projectId'], fixture['mainId'], str(uuid.uuid4())
    version = verification['version']
    rejected(lambda: workflow.prepare(config, release, version + '-rc.1', project, endpoint, thread, sandbox),
             'stable releases only')
    receipt = workflow.prepare(config, release, version, project, endpoint, thread, sandbox)
    report = read_json(receipt)
    assert report['freshMcp']['projectId'] == project and report['freshMcp']['endpointId'] == endpoint
    assert config.read_bytes() == initial_config
    assert workflow.source_fingerprint(source) == initial_source
    changed = source / 'attention-reviews.json'
    changed.write_text('{}', encoding='utf-8')
    rejected(lambda: workflow.switch(receipt, report), 'source changed')
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
            rejected(lambda: workflow.switch(receipt, report), 'fixture conflict')
        assert hosts[-1].poll() is not None and config.read_bytes() == initial_config
        receipt = workflow.prepare(config, release, version, project, endpoint, thread, sandbox)
        try:
            with config.open('a', encoding='utf-8') as stream:
                stream.write('required = false\n')
            report = workflow.switch(receipt, read_json(receipt))
            raw, table, data, binding = workflow.registration_state(config)
            assert table['required'] is True and binding == fixture['connectionId']
            assert tomllib.loads(raw.decode())['mcp_servers']['unrelated'] == {'command': 'preserved', 'required': False}
            assert (data / 'relay-projects.json').read_bytes() == (source / 'relay-projects.json').read_bytes()
            assert workflow.source_fingerprint(source) == initial_source
            assert workflow.status(config, receipt, report)['chatConfirmed'] is False
            proof = dict(workflow.chat_check(report), listProjectsSucceeded=True)
            rejected(lambda: workflow.confirm(receipt, report, dict(proof, toolsDocSha256='old catalog')),
                     'did not match')
            assert workflow.confirm(receipt, report, proof)['stage'] == 'confirmed'
            assert workflow.status(config, receipt, report)['chatConfirmed'] is True
        finally:
            for host in hosts:
                workflow.stop_owned(host)
    print(json.dumps({'passed': True, 'fixtureReceipt': str(receipt), 'chatEvidenceSimulated': True, 'modelCalls': 0}))


if __name__ == '__main__':
    main()

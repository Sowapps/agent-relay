"""Qualify on-demand MCP startup against a verified portable archive on fictional data."""

import argparse
from concurrent.futures import ThreadPoolExecutor
from contextlib import closing
import json
import os
from pathlib import Path
import signal
import sqlite3
import subprocess
import threading
import time
import uuid
from functools import partial

from deployment import PromotionFreeze, exclusive, snapshot, write
from host_identity import identity, resolve_host
from pilotage_stable import EXE, ROOT, STATE_FILES, startup_state
from relay_mcp import RelayMcp, McpRemoteError
from portable_fixture import extract_verified, prepare_fixture, run
from release_artifact import EXTRACTED_DIRECTORY, VERIFICATION_FILE, read_json
from runtime_layout import app_var


def assert_refused(client, code):
    try:
        client.operation('list_projects', timeout=22)
    except McpRemoteError as error:
        result = error.result
    else:
        raise AssertionError('Expected scoped startup refusal: ' + code)
    failure = result['structuredContent']['error']
    assert failure['code'] == code and failure['execution'] == 'not_started', failure
    return failure


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release-directory', type=Path, required=True)
    args = parser.parse_args()
    release = args.release_directory.resolve()
    if not release.is_relative_to(ROOT / 'local/apps/agent-relay/releases'):
        raise ValueError('Select a private verified archive')
    verification = read_json(release / VERIFICATION_FILE)
    started = time.monotonic()
    reference = release / EXTRACTED_DIRECTORY
    generic = read_json(reference / 'app/mcp-contract.json').get('compatibility', {}).get('transport')
    if generic:
        extracted = ROOT / 'local/apps/agent-relay/qualification' / ('cold-' + uuid.uuid4().hex[:12])
        extract_verified(release, extracted)
        executable = extracted / 'app' / EXE
        data = extracted / read_json(extracted / 'portable-layout.json')['data']
        prepare_fixture(extracted, executable, data, partial(run, cwd=extracted, environment=dict(os.environ)))
        fixture = read_json(extracted / 'requests-0/example-context.json')
    else:
        # Retain the already-qualified typed baseline's separate copied-scope probe.
        extracted = reference
        fixture = read_json(extracted / 'requests-0/example-context.json')
        executable = extracted / 'app' / EXE
        data = app_var(extracted) / ('wake-' + uuid.uuid4().hex[:8])
        snapshot(app_var(extracted) / 'default', data, files=STATE_FILES)
        subprocess.run([str(executable), 'initialize', '--data-dir', str(data), '--connection-id', fixture['connectionId']],
                       capture_output=True, check=True, timeout=35, creationflags=subprocess.CREATE_NO_WINDOW)
    pointer = read_json(data / 'mcp-runtime.json')
    host_runtime = {'executable': str(executable)}
    client_runtime = pointer if generic else {'executable': pointer['mcpExecutable'], 'connectionId': fixture['connectionId']}
    assert startup_state(executable, data) is False

    def host():
        return resolve_host(host_runtime, data)

    try:
        with (data / 'mcp.stderr.log').open('w', encoding='utf-8') as errors:
            with RelayMcp(client_runtime, data, ROOT, errors) as client:
                client.call('resources/list')
                assert host() is None, 'Public discovery started the tray'
                with PromotionFreeze(data):
                    assert_refused(client, 'maintenance')
                    assert host() is None, 'Maintenance admitted an on-demand start'
                with closing(sqlite3.connect(data / 'relay.sqlite')) as db:
                    row = db.execute('SELECT payload FROM connections WHERE id=?', (fixture['connectionId'],)).fetchone()[0]
                    db.execute('UPDATE connections SET payload=? WHERE id=?',
                               (json.dumps(dict(json.loads(row), revoked=True)), fixture['connectionId']))
                    db.commit()
                    try:
                        assert_refused(client, 'authentication_required')
                        assert host() is None, 'A revoked binding started the tray'
                    finally:
                        db.execute('UPDATE connections SET payload=? WHERE id=?', (row, fixture['connectionId']))
                        db.commit()
                with exclusive(data):
                    failure = assert_refused(client, 'relay_start_failed')
                    assert failure['recovery'] == 'start_relay_or_contact_maintainer'
                    assert 'maintainer' in failure['message'] and host() is None

            gate = threading.Barrier(2)

            def call_together():
                with RelayMcp(client_runtime, data, ROOT, errors) as client:
                    gate.wait(timeout=10)
                    projects = client.operation('list_projects', timeout=20)['configuration']['projects']
                    assert [project['id'] for project in projects] == [fixture['projectId']]
                    status = client.tool('get_connection_status', timeout=12)
                    assert status['version'] == verification['version'] and status['clientRefreshRequired'] is False

            with ThreadPoolExecutor(max_workers=2) as pool:
                outcomes = [pool.submit(call_together) for _ in range(2)]
                for outcome in outcomes:
                    outcome.result()
            owner = host()
            assert owner is not None and startup_state(executable, data) is False
            report = {'passed': True, 'version': verification['version'], 'owner': owner['pid'], 'coldCallers': 2,
                      'discoveryPassive': True, 'maintenanceRefused': True, 'revokedRefused': True,
                      'startupFailure': failure, 'signInPreferencePreservedDisabled': True, 'modelCalls': 0,
                      'seconds': round(time.monotonic() - started, 3)}
            write(data / 'startup-test.json', report)
            print(json.dumps(dict(report, receipt=str(data / 'startup-test.json'))))
    finally:
        owner = host()
        if owner and identity(owner, data):
            os.kill(owner['pid'], signal.SIGTERM)


if __name__ == '__main__':
    main()

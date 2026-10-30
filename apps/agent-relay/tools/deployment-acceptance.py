"""Fault-inject local publication in a new owned data directory; no WSL or model."""
import argparse
from contextlib import closing
import json
import os
from pathlib import Path
import signal
import sqlite3
import uuid
import deployment

parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--prepared', type=Path, required=True); args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
case = root / 'local/apps/agent-relay/tests' / ('deployment-' + uuid.uuid4().hex)
data = case / 'data'; reports = case / 'reports'; data.mkdir(parents=True)
runtime = deployment.read(args.prepared.resolve() / 'mcp-runtime.json'); deployment.validate(runtime, root)
data.joinpath('relay-config.json').write_text(json.dumps({'version': 1, 'profiles': []}), encoding='utf-8')
result = {'modelPrompts': 0, 'passed': False}
try:
    deployment.publish(data, args.prepared.resolve(), reports, False)
    old = deployment.read(reports / 'host.json'); pointer = deployment.read(data / 'mcp-runtime.json')
    original = deployment.launch
    injected = False

    def failing_candidate(value, target, logs, show=False):
        global injected
        process = original(value, target, logs, show)
        if target == data and not injected:
            injected = True
            with closing(sqlite3.connect(data / 'relay.sqlite')) as connection: connection.execute('CREATE TABLE injected_failure(marker TEXT)'); connection.commit()
            process.terminate(); process.wait(timeout=10)
            raise RuntimeError('Controlled candidate failure after migration, before publication')
        return process

    deployment.launch = failing_candidate
    try:
        deployment.publish(data, args.prepared.resolve(), reports, False)
        raise AssertionError('Injected failure was not observed')
    except RuntimeError as error: assert str(error).startswith('Controlled candidate failure'), error
    finally: deployment.launch = original
    restored = deployment.read(reports / 'host.json')
    assert restored['pid'] != old['pid'] and deployment.identity(restored, data)
    assert deployment.read(data / 'mcp-runtime.json') == pointer
    with closing(sqlite3.connect(data / 'relay.sqlite')) as connection:
        assert connection.execute("SELECT count(*) FROM sqlite_master WHERE name='injected_failure'").fetchone()[0] == 0
    result['rollbackAfterMigration'] = True
    # A malformed copied configuration must fail before the restored host is touched.
    original_configuration = (data / 'relay-config.json').read_bytes()
    (data / 'relay-config.json').write_text('{"version":999,"profiles":[]}', encoding='utf-8')
    try:
        deployment.publish(data, args.prepared.resolve(), reports, False)
        raise AssertionError('Invalid preparation was accepted')
    except RuntimeError as error: assert 'Candidate host exited' in str(error), error
    finally: (data / 'relay-config.json').write_bytes(original_configuration)
    assert deployment.identity(restored, data)
    result.update(failedPreparationPreservesHost=True, passed=True)
finally:
    metadata = reports / 'host.json'
    if metadata.exists():
        owned = deployment.read(metadata)
        if deployment.identity(owned, data): os.kill(int(owned['pid']), signal.SIGTERM)
    (case / 'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps({'passed': result['passed'], 'report': str(case / 'result.json')}))

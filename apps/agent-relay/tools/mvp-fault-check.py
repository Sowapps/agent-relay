"""Verify late receipt reconciliation without replaying any real model request."""
import argparse
import json
from pathlib import Path
import sqlite3
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--runtime', required=True)
args = parser.parse_args()
runtime = json.loads(Path(args.runtime).read_text(encoding='utf-8-sig'))
data = Path(runtime['data'])
base = [runtime['dotnet'], runtime['assembly']]
original = json.loads((data / 'acceptance-codex.json').read_text())
identifier = original['request']['id']
# This is a deliberately injected unconfirmed state in qualification data only.
with sqlite3.connect(data / 'relay.sqlite') as database:
    payload = json.loads(database.execute('SELECT payload FROM work WHERE id=?', (identifier,)).fetchone()[0])
    payload['state'] = 'unknown'
    payload['error'] = 'qualification: simulated missing final confirmation'
    database.execute('UPDATE work SET payload=? WHERE id=?', (json.dumps(payload), identifier))
host = subprocess.Popen(base + ['tray', '--data-dir', str(data), '--lifetime-ms', '60000'],
                        stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)
try:
    refreshed = subprocess.run(base + ['inbox', '--data-dir', str(data)], capture_output=True, text=True, encoding='utf-8', timeout=50)
    assert refreshed.returncode == 0, refreshed.stderr
    current = subprocess.run(base + ['result', '--data-dir', str(data), '--id', identifier], capture_output=True, text=True, encoding='utf-8', timeout=10)
    record = json.loads(current.stdout)['work']
    assert record['state'] == 'completed' and record['result']['output'] == original['result']['output']
    result = {'Passed': True, 'SimulatedUnknownResolvedFromReceipt': True, 'RequestId': identifier, 'NewModelPrompts': 0, 'Record': record}
    (data / 'fault-recovery.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result))
finally:
    if host.poll() is None:
        host.terminate()
    host.wait(timeout=10)

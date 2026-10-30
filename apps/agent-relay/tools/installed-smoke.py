"""Exercise the prepared normal-use profiles and visible window without an AI turn."""
import argparse
import json
from pathlib import Path
import subprocess
import time
import uuid

parser = argparse.ArgumentParser()
parser.add_argument('--data', required=True)
args = parser.parse_args()
data = Path(args.data).resolve()
fragment = json.loads((data / 'mcp.windows.json').read_text(encoding='utf-8-sig'))['mcpServers']['agent_relay']
runtime = json.loads((data / 'mcp-runtime.json').read_text(encoding='utf-8-sig'))
base = [runtime['dotnet'], runtime['assembly']]
configuration = json.loads((data / 'relay-config.json').read_text(encoding='utf-8-sig'))
profile = next(item for item in configuration['profiles'] if item['client'] == 'command')
host = subprocess.Popen(base + ['tray', '--data-dir', str(data), '--show', '--lifetime-ms', '8000'],
                        stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)


def call(mode, *options):
    result = subprocess.run(base + [mode, '--data-dir', str(data), *options], capture_output=True, text=True, encoding='utf-8', timeout=10)
    if result.returncode:
        raise RuntimeError(result.stderr)
    return json.loads(result.stdout)


try:
    identifier = str(uuid.uuid4())
    call('submit', '--id', identifier, '--profile', profile['id'], '--kind', 'command', '--command', 'list')
    deadline = time.monotonic() + 6
    while time.monotonic() < deadline:
        record = call('result', '--id', identifier)['work']
        if record['state'] in ['completed', 'failed', 'unknown']:
            break
        time.sleep(.1)
    assert record['state'] == 'completed' and record['result']['output'] == 'alpha.txt\nbeta.txt\n', record
    assert record['result']['details']['linuxUser'] == profile['user']
    stdout, stderr = host.communicate(timeout=12)
    assert host.returncode == 0, stderr
    proof = {'Passed': True, 'VisibleWindowLifecyclePassed': True, 'DefaultConfigurationPassed': True, 'ModelPrompts': 0, 'Command': record}
    report = data / 'installed-smoke.json'
    report.write_text(json.dumps(proof, indent=2), encoding='utf-8')
    print(json.dumps(proof))
finally:
    if host.poll() is None:
        host.terminate()
    host.wait(timeout=5)

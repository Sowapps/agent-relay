"""Verify standby timeout/identity without stopping a process or touching the installed stable."""

import json
from pathlib import Path
from unittest.mock import patch
import uuid

import stable_update_wait as driver
from complete_update import UpdateFailure
from deployment import read, write
from runtime_layout import app_var, pilotage_stable_root


root = driver.workflow.ROOT / 'local/apps/agent-relay/tests' / ('wait-' + uuid.uuid4().hex[:8])
stable = pilotage_stable_root(root)
data = app_var(stable / 'installation') / 'default'
data.mkdir(parents=True)
(data / 'mcp-runtime.json').write_text('{}', encoding='utf-8')
broker = data / 'mcp-launcher/AgentRelay.Windows.exe'
config = root / 'config.toml'
config.write_text('[mcp_servers.' + driver.workflow.SERVER + ']\ncommand = ' + json.dumps(str(broker))
                  + '\nargs = ' + json.dumps(['mcp', '--data-dir', str(data), '--connection-id', str(uuid.uuid4())]), encoding='utf-8')
receipt = stable / 'armed-fixture/result.json'
receipt.parent.mkdir()
write(receipt, dict(config=str(config), configSha256=driver.workflow.sha256(config), data=str(data),
                    release=str(root / 'candidate'), version='9.0.0', archiveSha256='fixture'))
inventory = [dict(ProcessId=123, ExecutablePath=str(broker), CommandLine='"' + str(broker) + '" mcp --data-dir "' + str(data) + '"')]
with patch.object(driver.workflow, 'ROOT', root), patch.object(driver.workflow, 'verified_inputs', return_value=(root / 'archive.zip', 'fixture')):
    with patch.object(driver, 'processes', return_value=inventory), patch.object(driver.workflow, 'upgrade') as apply:
        try:
            driver.run(receipt, timeout=0)
        except UpdateFailure as error:
            assert error.error['diagnosticId'] and 'MCP clients still hold' in error.error['message']
        else:
            raise AssertionError('Standby ignored a mapped client')
        apply.assert_not_called()
result = read(receipt)
assert result['stage'] == 'failed' and result['remainingClientProcesses'] == [123]
assert (data / 'mcp-runtime.json').read_text() == '{}'
assert driver.clients(data.parents[3], data, []) == []
(root / 'result.json').write_text(json.dumps(dict(passed=True, noProcessesStopped=True, noUpgradeBeforeClientExit=True), indent=2), encoding='utf-8')
print(json.dumps(dict(passed=True, report=str(root / 'result.json'))))

"""Verify the receipt entry delegates once without a client-exit timer or stable effects."""

import json
from unittest.mock import patch
import uuid

import stable_update_wait as driver
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
with patch.object(driver.workflow, 'ROOT', root), patch.object(driver.workflow, 'verified_inputs', return_value=(root / 'archive.zip', 'fixture')):
    outcome = dict(state=dict(schema=2, phase='awaiting_work', id='fixture'), receipt='retained C# transaction')
    with patch.object(driver.workflow, 'upgrade', side_effect=AssertionError('Must use the unified transaction')), \
            patch.object(driver, 'update', return_value=outcome) as apply:
        driver.run(receipt, timeout=0)
        apply.assert_called_once()
result = read(receipt)
assert result['stage'] == 'awaiting_work' and result['applied'] is False
assert (data / 'mcp-runtime.json').read_text() == '{}'
(root / 'result.json').write_text(json.dumps(dict(passed=True, noProcessesStopped=True, noClientExitWait=True, durableWaitingWithoutExpiry=True), indent=2), encoding='utf-8')
print(json.dumps(dict(passed=True, report=str(root / 'result.json'))))

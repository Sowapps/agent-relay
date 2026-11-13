"""Focused registration migration qualification; copied files, no relay/client/model."""

import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import tomllib
import uuid

import pilotage_stable as workflow
import registration_migration as migration


def rejected(action, phrase):
    try:
        action()
    except (ValueError, RuntimeError) as error:
        assert phrase in str(error), str(error)
    else:
        raise AssertionError('Expected refusal: ' + phrase)


def fixture(root):
    root.mkdir()
    stable, dev = root / 'stable-data', root / 'dev-data'
    stable.mkdir()
    dev.mkdir()
    def table(name, data, binding):
        args = ['mcp', '--data-dir', str(data), '--connection-id', binding]
        return ('[mcp_servers.' + name + ']\ncommand = "fixture.exe"\nargs = '
                + json.dumps(args) + '\nrequired = true\n')
    stable_binding, dev_binding = str(uuid.uuid4()), str(uuid.uuid4())
    dev_table = table('agent_relay', dev, dev_binding) + '[mcp_servers.agent_relay.env]\nDEV_ONLY = "preserved-in-dev"\n'
    shared, workspace = root / 'shared.toml', root / 'workspace.toml'
    shared.write_text('model = "preserved"\n' + dev_table
                      + table(workflow.LEGACY_SERVER, stable, stable_binding)
                      + '[mcp_servers.unrelated]\ncommand = "untouched"\n'
                      + '[[unrelated_jobs]]\nname = "retained"\n', encoding='utf-8')
    workspace.write_text('# Workspace comment\n' + dev_table
                         + '[workspace_rules]\nkeep = true\n', encoding='utf-8')
    plan = migration.prepare(shared, workspace, root / 'plan', stable_source=workflow.LEGACY_SERVER)
    return shared, workspace, plan, stable_binding


def crash(plan):
    original = migration.save
    def interrupt_after_file(path, report):
        first = report['files'][0]
        if migration.digest(Path(first['path']).read_bytes()) == first['after']:
            os._exit(73)
        original(path, report)
    migration.save = interrupt_after_file
    migration.reconcile(Path(plan))


def main():
    root = workflow.ROOT / 'local/apps/agent-relay/tests' / ('registration-' + uuid.uuid4().hex[:10])
    root.mkdir(parents=True)
    started = time.monotonic()
    results = {}
    shared, workspace, plan, stable_binding = fixture(root / 'normal')
    before = [shared.read_bytes(), workspace.read_bytes()]
    rejected(lambda: workflow.registration_state(shared), 'Legacy stable alias')
    assert workflow.registration_state(shared, workflow.LEGACY_SERVER)[3] == stable_binding
    migration.reconcile(plan)
    after = [shared.read_bytes(), workspace.read_bytes()]
    a, b = (tomllib.loads(value.decode()) for value in after)
    original_a, original_b = (tomllib.loads(value.decode()) for value in before)
    assert a['mcp_servers']['agent_relay'] == original_a['mcp_servers'][workflow.LEGACY_SERVER]
    assert 'env' not in a['mcp_servers']['agent_relay']
    assert workflow.LEGACY_SERVER not in a['mcp_servers'] and 'agent_relay_dev' not in a['mcp_servers']
    assert b['mcp_servers']['agent_relay_dev'] == original_b['mcp_servers']['agent_relay']
    assert 'agent_relay' not in b['mcp_servers']
    assert a['mcp_servers']['unrelated'] == original_a['mcp_servers']['unrelated']
    assert a['unrelated_jobs'] == original_a['unrelated_jobs'] and b['workspace_rules'] == original_b['workspace_rules']
    assert workflow.registration_state(shared)[3] == stable_binding
    stamps = [shared.stat().st_mtime_ns, workspace.stat().st_mtime_ns]
    repeated = migration.reconcile(plan)
    assert repeated['attempts'][-1]['noOp'] and repeated['attempts'][-1]['writtenFiles'] == 0
    assert stamps == [shared.stat().st_mtime_ns, workspace.stat().st_mtime_ns]
    migration.reconcile(plan, rollback=True)
    assert before == [shared.read_bytes(), workspace.read_bytes()]
    results['normal_exact_identity_no_dev_option_leak_and_idempotent_rollback'] = True
    owner = workflow.registration
    original = '[mcp_servers.only]\ncommand="before"\nargs=["keep"]\n'
    merged = owner.update(original, '[mcp_servers.only]\ncommand="after"\n', 'only')
    assert tomllib.loads(merged)['mcp_servers']['only'] == {'command': 'after', 'args': ['keep']}
    assert tomllib.loads(owner.replace_servers(original, {'only': None})) == {}
    results['existing_merge_interface_and_sole_removal'] = True
    shared, workspace, plan, _ = fixture(root / 'interrupted')
    original = [shared.read_bytes(), workspace.read_bytes()]
    process = subprocess.run([sys.executable, str(Path(__file__).resolve()), '--crash', str(plan)],
                             capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)
    assert process.returncode == 73, process.stderr.decode(errors='replace')
    assert json.loads(plan.read_text())['phase'] == 'applying'
    assert shared.read_bytes() != original[0] and workspace.read_bytes() == original[1]
    observed = migration.inspect(plan)['observedFiles']
    assert observed[0]['matchesAfter'] and observed[1]['matchesBefore']
    recovered = migration.reconcile(plan)
    assert recovered['phase'] == 'complete' and recovered['attempts'][-1]['writtenFiles'] == 1
    migration.reconcile(plan, rollback=True)
    assert original == [shared.read_bytes(), workspace.read_bytes()]
    results['real_process_exit_resume_and_paired_rollback'] = True
    shared, workspace, plan, _ = fixture(root / 'conflict')
    original = [shared.read_bytes(), workspace.read_bytes()]
    first = migration.load(plan)['files'][0]
    owner.replace_if_unchanged(shared, original[0], (plan.parent / '0.after.toml').read_bytes())
    workspace.write_bytes(original[1] + b'\n# external user edit\n')
    conflicted = [shared.read_bytes(), workspace.read_bytes()]
    rejected(lambda: migration.reconcile(plan), 'concurrently')
    rejected(lambda: migration.reconcile(plan, rollback=True), 'concurrently')
    assert conflicted == [shared.read_bytes(), workspace.read_bytes()]
    results['external_edit_preserved_during_resume_and_rollback'] = True
    workspace.write_bytes(original[1])  # Fixture repair only; no production recovery bypass.
    migration.reconcile(plan, rollback=True)
    assert original == [shared.read_bytes(), workspace.read_bytes()]
    (plan.parent / '0.after.toml').write_bytes(b'changed')
    rejected(lambda: migration.reconcile(plan), 'backup/proposal changed')
    results['tampered_proposal_refused_before_effects'] = True
    shared, workspace, plan, _ = fixture(root / 'retarget')
    altered = json.loads(plan.read_text())
    altered['files'][0]['path'] = str(workspace)
    plan.write_text(json.dumps(altered), encoding='utf-8')
    rejected(lambda: migration.reconcile(plan), 'target selection changed')
    results['changed_target_selection_refused'] = True
    shared, workspace, plan, _ = fixture(root / 'ambiguous')
    text = workspace.read_text(encoding='utf-8')
    workspace.write_text(text + '[mcp_servers.agent_relay_dev]\ncommand="foreign"\nargs=["foreign"]\n', encoding='utf-8')
    rejected(lambda: migration.prepare(shared, workspace, root / 'refused', stable_source=workflow.LEGACY_SERVER), 'unrelated')
    assert not (root / 'refused').exists()
    rejected(lambda: migration.prepare(shared, shared, root / 'same-file', stable_source=workflow.LEGACY_SERVER), 'different')
    results['ambiguous_alias_and_same_file_refused'] = True
    report = {'passed': True, 'cases': results, 'seconds': round(time.monotonic() - started, 3),
              'modelCalls': 0, 'liveConfigurationsWritten': False, 'stableTouched': False,
              'sourceHashes': {name: hashlib.sha256((Path(__file__).parent / name).read_bytes()).hexdigest()
                               for name in ('codex_registration.py', 'register-codex.py', 'registration_migration.py', 'pilotage_stable.py')}}
    (root / 'result.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'passed': True, 'report': str(root / 'result.json'), 'cases': len(results), 'seconds': report['seconds']}))


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '--crash':
        crash(sys.argv[2])
    else:
        main()

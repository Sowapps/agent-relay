"""Qualify real portable update through one still-open isolated Codex client.

Only protocol/process orchestration lives here. InstallationUpdate owns policy and recovery.
No model turn, client reload, actual client setting or stable installation is used.
"""
import argparse
from functools import partial
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from codex_rpc import CodexRpc
from complete_update import invoke, recovery_updater
from host_identity import resolve_host, identity
from release_artifact import archive_path, declared_checksum, extract_archive, verified_archive


def load_verifier():
    path = Path(__file__).resolve().parents[1] / 'verify-release.py'
    spec = importlib.util.spec_from_file_location('archive_verifier', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def extract(directory, destination):
    version = read(directory / 'payload/release-manifest.json')['version']
    archive = archive_path(directory, version)
    digest = declared_checksum(archive)
    with verified_archive(archive, digest) as source:
        extract_archive(source, destination)
    return version, digest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', required=True, type=Path)
    parser.add_argument('--candidate', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[4]
    output = args.output.resolve()
    if not output.is_relative_to(root / 'local/apps/agent-relay/qualification') or output.exists():
        raise ValueError('Choose a fresh owned application qualification directory')
    output.mkdir(parents=True)
    installed, candidate = output / 'installation', output / 'candidate'
    result = dict(modelTurns=0, reloadCalls=0, actualSettingsChanged=False, stableChanged=False)
    started = time.monotonic()
    data = installed / 'var/apps/agent-relay/default'
    try:
        result['baseVersion'], result['baseSha256'] = extract(args.base, installed)
        result['candidateVersion'], result['candidateSha256'] = extract(args.candidate, candidate)
        verifier = load_verifier()
        environment = dict(os.environ)
        run = partial(verifier.run, cwd=installed, environment=environment)
        executable = installed / 'app/AgentRelay.Windows.exe'
        command, projects = verifier.prepare_fixture(installed, executable, data, run)
        binding = read(data / 'mcp-runtime.json')['connectionId']
        run(shutil.which('pwsh.exe'), '-NoProfile', '-File', installed / 'tools/launch.ps1',
            '-Root', installed, '-Mode', 'Initialize', '--connection-id', binding)
        result['rootInitializeForwardedBinding'] = read(data / 'mcp-runtime.json')['connectionId'] == binding
        home, work = output / 'codex-home', output / 'workspace'
        home.mkdir()
        work.mkdir()
        fragment = read(data / 'mcp.windows.json')['mcpServers']['agent_relay']
        config = ('[mcp_servers.relay_qualification]\ncommand = ' + json.dumps(fragment['command'])
                  + '\nargs = ' + json.dumps(fragment['args']) + '\n')
        (home / 'config.toml').write_text(config, encoding='utf-8')
        environment['CODEX_HOME'] = str(home)
        codex = shutil.which('codex')
        if not codex:
            raise RuntimeError('Installed Codex executable unavailable')
        # Process-only override suppresses the checkout's unrelated development registration.
        overrides = ['-c', 'mcp_servers.agent_relay.command=' + json.dumps(fragment['command']),
                     '-c', 'mcp_servers.agent_relay.args=' + json.dumps(fragment['args']),
                     '-c', 'mcp_servers.agent_relay.enabled=false']
        with (output / 'codex.stderr.log').open('w', encoding='utf-8') as errors:
            with CodexRpc(codex, work, errors, overrides, environment) as rpc:
                thread = rpc.call('thread/start', dict(cwd=str(work), ephemeral=True,
                                  approvalPolicy='never', sandbox='read-only'))['thread']['id']
                result['thread'] = thread
                result['clientPid'] = rpc.process.pid
                def tool(name, arguments=None):
                    response = rpc.call('mcpServer/tool/call', dict(threadId=thread, server='relay_qualification',
                                        tool=name, arguments=arguments or {}), timeout=60)
                    result.setdefault('protocol', []).append(dict(tool=name, result=response))
                    (output / 'protocol.json').write_text(json.dumps(result['protocol'], indent=2), encoding='utf-8')
                    if response.get('isError'):
                        raise RuntimeError('MCP refused: ' + json.dumps(response.get('structuredContent')))
                    return response['structuredContent']
                catalog = lambda: rpc.call('mcpServerStatus/list', dict(threadId=thread,
                                           serverName='relay_qualification', detail='full'), timeout=30)
                result['catalogBefore'] = catalog()
                before = tool('get_connection_status')
                discovery = tool('discover_operations')
                operation = next(item for item in discovery['operations'] if item['id'] == 'list_projects')
                def project_list():
                    return tool('invoke_operation', dict(call_id=str(uuid.uuid4()), operation_id=operation['id'],
                                revision=operation['revision'], arguments={}))
                prior_projects = project_list()
                leases = read(data / 'transport/instances' / (before['instanceId'].replace('-', '') + '.json'))
                result['transportBefore'] = leases
                protected = {str(path.relative_to(data)): path.read_bytes() for path in [data / 'relay-projects.json',
                             *list((data / 'connections').rglob('*'))] if path.is_file()}
                updater = dict(executable=str(candidate / 'app/AgentRelay.Windows.exe'))
                prepared = invoke(updater, installed, data, 'prepare', candidate)
                if prepared['phase'] != 'prepared' or prepared['activation']['resumeAuthorized']:
                    raise RuntimeError('Preparation unexpectedly authorized execution')
                result['prepared'] = prepared['id']
                retained = recovery_updater(data)
                # The original extracted artifact is removed from its selected path before execution.
                candidate.rename(output / 'retired-original-download')
                invoke(retained, installed, data, 'start-resume')
                deadline = time.monotonic() + 90
                phases = []
                while True:
                    state = invoke(retained, installed, data, 'status')
                    if not phases or phases[-1] != state['phase']:
                        phases.append(state['phase'])
                    if state['phase'] == 'complete':
                        break
                    if state['phase'] in ('rolled_back', 'recovery_required'):
                        raise RuntimeError('Update stopped: ' + str((state['phase'], state.get('errorCode'), state.get('error'))))
                    if time.monotonic() >= deadline:
                        raise RuntimeError('Qualification bound reached, retained phase: ' + json.dumps(state))
                    time.sleep(.3)
                result['phases'] = phases
                after = tool('get_connection_status')
                after_projects = project_list()
                run(shutil.which('pwsh.exe'), '-NoProfile', '-File', installed / 'tools/launch.ps1',
                    '-Root', installed, '-Mode', 'Open')
                result['updatedRootLauncher'] = True
                result['catalogAfter'] = catalog()
                if after['version'] != result['candidateVersion'] or after['instanceId'] != before['instanceId']:
                    raise RuntimeError('Same connection did not reach the candidate identity')
                lease_after = read(data / 'transport/instances' / (after['instanceId'].replace('-', '') + '.json'))
                if leases['processId'] == lease_after['processId']:
                    raise RuntimeError('Gateway process did not succeed to the candidate')
                if prior_projects != after_projects or any((data / name).read_bytes() != content for name, content in protected.items()):
                    raise RuntimeError('Project context, permissions or credentials changed')
                if (data / 'promotion.lock').exists():
                    raise RuntimeError('Admission remained suspended after completion')
                repeat = invoke(retained, installed, data, 'resume')
                if repeat['id'] != prepared['id'] or repeat['phase'] != 'complete':
                    raise RuntimeError('Resume was not idempotent')
                result.update(passed=True, sameOpenClient=True, sameThread=True, sameConnection=True,
                              transportAfter=lease_after, originalDownloadIndependent=True,
                              preservedProjectsAndCredentials=True, idempotentResume=True)
    except BaseException as error:
        result['error'] = repr(error)
        raise
    finally:
        try:
            if (data / 'mcp-runtime.json').exists():
                runtime = read(data / 'mcp-runtime.json')
                host = resolve_host(runtime, data)
                if host and identity(host, data):
                    os.kill(host['pid'], signal.SIGTERM)
            result['ownedHostCleanup'] = True
        except Exception as error:
            result['cleanupError'] = repr(error)
        result.pop('protocol', None)
        result['seconds'] = round(time.monotonic() - started, 3)
        (output / 'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(dict(passed=result.get('passed'), seconds=result['seconds'], report=str(output / 'result.json'))))


if __name__ == '__main__':
    main()

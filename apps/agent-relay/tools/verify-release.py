"""Utility: qualify the exact extracted Windows archive without the checkout launcher.
Usage: release.ps1 Verify. Runs only its own fixture host, with no WSL or model calls.
"""

import argparse
from functools import partial
import json
import os
from pathlib import Path
import re
import shutil
import uuid
import subprocess
import time

from relay_mcp import McpRpc
from release_artifact import (
    EXTRACTED_DIRECTORY, MANIFEST_FILE, VERIFICATION_FILE, archive_path, declared_checksum,
    extract_archive, read_json, sha256, verified_archive,
)


EXPECTED_TOOLS = 4
EXPECTED_RESOURCES = 5


def require(condition, message):
    if not condition:
        raise ValueError(message)


def extract_package(directory, version):
    archive = archive_path(directory, version)
    digest = declared_checksum(archive)
    extracted = directory / EXTRACTED_DIRECTORY
    with verified_archive(archive, digest) as source:
        extract_archive(source, extracted)
    manifest = read_json(extracted / MANIFEST_FILE)
    require(manifest['version'] == version, 'Extracted manifest version differs from the requested version')
    files = {file.relative_to(extracted).as_posix(): file for file in extracted.rglob('*') if file.is_file()}
    require(set(manifest['files']) | {MANIFEST_FILE} == files.keys(), 'Extracted files differ from the manifest')
    for name, expected in manifest['files'].items():
        require(sha256(files[name]) == expected, f'Extracted file checksum differs: {name}')
    return extracted, digest


def verify_documentation(extracted):
    french = extracted / 'README.fr.md'
    require(french.is_file(), 'French starting README missing')
    guides = [extracted / 'README.md', french, *(extracted / 'app/documentation').rglob('*.md')]
    links = 0
    for guide in guides:
        for target in re.findall(r'\]\(([^)]+)\)', guide.read_text(encoding='utf-8-sig')):
            target = target.strip('<>')
            if re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*:', target):
                continue
            relative = target.split('#', 1)[0]
            if not relative:
                continue
            resolved = (guide.parent / relative).resolve()
            require(resolved.is_relative_to(extracted) and resolved.is_file(),
                    f'Broken distribution link: {guide.name}: {target}')
            links += 1
    return links


def run(*command, cwd, environment):
    result = subprocess.run(command, cwd=cwd, env=environment, capture_output=True, text=True,
                            encoding='utf-8', errors='replace', timeout=45, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise RuntimeError(f'{command[0]}: {result.stderr}{result.stdout}')
    return result.stdout


def prepare_fixture(extracted, executable, data, invoke):
    main_workspace = extracted / 'workspaces/main'
    peer_workspace = extracted / 'workspaces/peer'
    main_workspace.mkdir(parents=True)
    peer_workspace.mkdir(parents=True)
    powershell = shutil.which('pwsh.exe')
    require(powershell is not None, 'PowerShell 7 is required to generate the portable verification fixture')
    invoke(executable, 'initialize')
    pointer = data / 'mcp-runtime.json'
    require(pointer.is_file() and not (extracted / 'local').exists(), 'Default storage escaped installation var')
    contexts = []
    for index in range(2):
        request = extracted / f'requests-{index}'
        invoke(powershell, '-NoProfile', '-File', extracted / 'tools/new-project-request.ps1',
               '-DataDirectory', data, '-Name', f'Archive project {index}', '-MainWorkspace', main_workspace,
               '-PeerWorkspace', peer_workspace, '-OutputDirectory', request, '-LocalOnly')
        invoke(executable, 'admin', '--data-dir', data, '--input', request / 'configure-project.request.json')
        contexts.append(read_json(request / 'example-context.json'))
        if index == 0:
            invoke(executable, 'admin', '--data-dir', data, '--input', request / 'connection.request.json')
    require(len(read_json(data / 'relay-projects.json')['projects']) == 2, 'Fixture did not retain both projects')
    invoke(executable, 'initialize', '--data-dir', data, '--connection-id', contexts[0]['connectionId'])
    pointer_before = pointer.read_bytes()
    for forbidden in ('11111111-1111-4111-8111-111111111111', '28c10e25-b802-42da-92f5-98723e6b54ae'):
        try:
            invoke(executable, 'initialize', '--data-dir', data, '--connection-id', forbidden)
        except RuntimeError:
            require(pointer.read_bytes() == pointer_before, 'A refused connection changed the exported binding')
        else:
            raise ValueError('Missing or administrator connection was exportable')
    fragment = read_json(data / 'mcp.windows.json')['mcpServers']['agent_relay']
    require(Path(fragment['command']).name.lower() == 'pwsh.exe'
            and str(extracted / read_json(extracted / 'portable-layout.json')['entryScript']) in fragment['args'], 'Portable fragment did not select its fixed common entry')
    return [fragment['command'], *fragment['args']], [item['projectId'] for item in contexts]


def verify_mcp(extracted, command, project_ids, version, environment, errors):
    with McpRpc(command, extracted, errors, environment, client_name='portable-archive-check') as client:
        require(client.initialization['serverInfo']['version'] == version, 'Unexpected MCP server version')
        require(len(client.inventory) == EXPECTED_TOOLS, 'Unexpected MCP tool count')
        listed = client.call('resources/list')['resources']
        require(len(listed) == EXPECTED_RESOURCES, 'Unexpected MCP resource count')
        for item in listed:
            contents = client.call('resources/read', {'uri': item['uri']})['contents'][0]
            expected = (extracted / 'app/documentation' / item['name']).read_text(encoding='utf-8-sig')
            require(contents['text'].replace('\r\n', '\n') == expected, f"MCP resource differs: {item['uri']}")
        found = client.operation('list_projects')
        require([p['id'] for p in found['configuration']['projects']] == project_ids[:1],
                'Scoped connection did not discover exactly its own project')
        empty = client.operation('list_results', {'project_id': project_ids[0]})
        require(empty['operations']['items'] == [], 'Fresh project unexpectedly contains operations')
        denied = client.call('tools/call', {'name': 'invoke_operation', 'arguments': dict(call_id=str(uuid.uuid4()),
            operation_id='list_results', revision=1, arguments={'project_id': project_ids[1]})})
        require(denied.get('isError'), 'Scoped connection read a foreign project')
        require(len(client.call('tools/list')['tools']) == EXPECTED_TOOLS, 'Connection failed after scope refusal')
        try:
            client.call('resources/read', {'uri': 'agent-relay://docs/../../connections'})
        except RuntimeError:
            pass
        else:
            raise ValueError('Resource traversal was accepted')
        return [item['uri'] for item in listed]


def wait_for_host(host, executable, data, invoke):
    for _ in range(30):
        if host.poll() is not None:
            raise RuntimeError('Extracted host exited early')
        try:
            json.loads(invoke(executable, 'profiles', '--data-dir', data))
            return
        except RuntimeError:
            time.sleep(.2)
    raise RuntimeError('Extracted host unavailable')


def verify_runtime(directory, extracted, version):
    executable = extracted / 'app/AgentRelay.Windows.exe'
    data = extracted / 'var/apps/agent-relay/default'
    environment = {key: value for key, value in os.environ.items() if not key.startswith(('DOTNET_', 'NUGET_'))}
    environment.update(DOTNET_ROOT=str(extracted / 'no-sdk-installed'), DOTNET_MULTILEVEL_LOOKUP='0',
                       PATH=str(Path(os.environ['SystemRoot']) / 'System32'),
                       AGENT_RELAY_PWSH=shutil.which('pwsh.exe') or shutil.which('pwsh') or '')
    invoke = partial(run, cwd=extracted, environment=environment)
    command, project_ids = prepare_fixture(extracted, executable, data, invoke)
    resources = []
    for phase in ('initial', 'restart'):
        with (directory / f'host-{phase}.stderr.log').open('wb') as errors:
            # Start with the packaged default location. The duplicate startup explicitly selects that same scope.
            host = subprocess.Popen([executable, 'tray', '--lifetime-ms', '120000'], cwd=extracted.parent,
                                    env=environment, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                    stderr=errors, creationflags=subprocess.CREATE_NO_WINDOW)
            try:
                wait_for_host(host, executable, data, invoke)
                with (directory / 'mcp.stderr.log').open('a', encoding='utf-8') as mcp_errors:
                    resources = verify_mcp(extracted, command, project_ids, version, environment, mcp_errors)
                invoke(executable, 'tray', '--data-dir', data)
                require(host.poll() is None, 'Duplicate startup displaced the existing host')
                require(not (extracted / 'local').exists(), 'Runtime created a developer local directory')
            finally:
                if host.poll() is None:
                    host.terminate()
                host.wait(timeout=10)
    return resources


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', required=True, type=Path)
    parser.add_argument('--version', required=True)
    args = parser.parse_args()
    directory = args.directory.absolute()
    extracted, digest = extract_package(directory, args.version)
    documentation_links = verify_documentation(extracted)
    resources = verify_runtime(directory, extracted, args.version)
    result = {'passed': True, 'archiveSha256': digest, 'version': args.version, 'extracted': str(extracted),
              'resources': resources, 'tools': EXPECTED_TOOLS, 'selfContained': True, 'frenchReadme': True,
              'documentationLinks': documentation_links,
              'runtimeIsolation': 'No dotnet/NuGet variables, nonexistent DOTNET_ROOT, PATH restricted to Windows System32',
              'projectPreservation': True, 'scopedConnectionAndForeignProjectRefusal': True,
              'restartAndDuplicateOwner': True, 'modelCalls': 0, 'wslCalls': 0,
              'guiVerification': 'Separate exact-archive native capture required before Publish'}
    with (directory / VERIFICATION_FILE).open('x', encoding='utf-8') as output:
        json.dump(result, output, indent=2)
    print(json.dumps(result))


if __name__ == '__main__':
    main()

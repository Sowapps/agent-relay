"""Utility: shared fictional portable setup for archive, cold-start and live-client checks.
Usage: test adapters only; production update policy remains in C# InstallationUpdate.
"""
import os
from pathlib import Path
import shutil
import subprocess
from release_artifact import VERIFICATION_FILE, qualified_archive, verified_archive, extract_archive, read_json


def require(condition, message):
    if not condition:
        raise ValueError(message)


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


def extract_verified(directory, destination):
    verification = read_json(directory / VERIFICATION_FILE)
    version = verification['version']
    archive, digest = qualified_archive(directory, version, allow_rc=True)
    with verified_archive(archive, digest) as package:
        extract_archive(package, destination)
    return version, digest

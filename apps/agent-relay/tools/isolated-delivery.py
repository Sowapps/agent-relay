"""Utility: build, test and qualify Agent Relay away from Pilotage AP.
Usage: run with build, test, run, qualify or rehearse; see the development guide.
"""

import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET

from deployment import read, write
from host_identity import resolve_host
from runtime_layout import app_var, pilotage_scope, reject_pilotage_paths, runtime_command


TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[2]
VAR = app_var(ROOT)
STABLE = pilotage_scope(ROOT)
RELEASES = ROOT / 'local/apps/agent-relay/releases'


def ordinary_directory(path):
    """Reject junctions as well as symlinks, including intermediate directories."""
    for directory in (*reversed(path.parents), path):
        try:
            status = directory.lstat()
        except FileNotFoundError:
            continue
        if not directory.is_dir() or status.st_file_attributes & 0x400:
            raise ValueError(f'Expected ordinary directory: {directory}')
    return path


def sha256(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def stable_snapshot():
    ordinary_directory(STABLE)
    names = ['mcp-runtime.json', 'mcp-contract.json', 'relay-projects.json',
             'relay-config.json', 'mcp.codex.toml', 'mcp.windows.json']
    files = {name: sha256(STABLE / name) for name in names}
    for credential in ordinary_directory(STABLE / 'connections').glob('*.json'):
        if credential.is_file():
            files[credential.relative_to(STABLE).as_posix()] = sha256(credential)
    runtime = read(STABLE / 'mcp-runtime.json')
    host = resolve_host(runtime, STABLE)
    if host:
        probe = subprocess.run(runtime_command(runtime) + ['profiles', '--data-dir', str(STABLE)],
                               capture_output=True, check=True, timeout=12, creationflags=subprocess.CREATE_NO_WINDOW)
        json.loads(probe.stdout)
    return {'version': read(STABLE / 'mcp-contract.json')['version'], 'files': files, 'responded': bool(host),
            'pid': host['pid'] if host else None, 'executableSha256': sha256(Path(runtime_command(runtime)[0]))}


def git(*arguments):
    return subprocess.check_output(['git', *arguments], cwd=ROOT, text=True).strip()


def powershell(script, *arguments):
    return ['pwsh', '-NoProfile', '-File', script, *arguments]


def python_script(script, *arguments):
    return [sys.executable, '-X', 'utf8', '-B', TOOLS / script, *arguments]


def phase(report, folder, name, command):
    started = time.monotonic()
    step = {'name': name, 'attempts': 1, 'exitCode': None}
    report['phases'].append(step)
    log = folder / (name + '.log')
    step['log'] = log.name
    try:
        with log.open('wb') as output:
            print(f'{name}: {log}', flush=True)
            result = subprocess.run(command, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT)
            step['exitCode'] = result.returncode
            result.check_returncode()
        return log.read_text(encoding='utf-8-sig', errors='replace')
    finally:
        step['seconds'] = round(time.monotonic() - started, 3)
        write(folder / 'result.json', report)


def qualify(report, folder):
    if report['dirty']:
        raise ValueError('Qualification requires a clean, identified Git commit')
    version = ET.parse(TOOLS.parent / 'Version.props').findtext('.//Version')
    if '-rc.' not in version:
        raise ValueError('Qualification entry point requires an RC version')
    ordinary_directory(RELEASES)
    script = TOOLS / 'release.ps1'
    output = phase(report, folder, 'package', powershell(script, '-Action', 'Package', '-Offline'))
    directory = Path(next(line.split('=', 1)[1].strip() for line in output.splitlines()
                          if line.startswith('RELEASE_DIRECTORY=')))
    ordinary_directory(directory)
    if not directory.is_relative_to(RELEASES):
        raise ValueError('Package output is outside the release fixture root')
    phase(report, folder, 'verify', powershell(script, '-Action', 'Verify', '-Directory', directory))
    if git('rev-parse', 'HEAD') != report['commit'] or git('status', '--porcelain'):
        raise ValueError('Source changed during qualification')
    verification = read(directory / 'verification.json')
    archive = directory / f'agent-relay-{version}-win-x64.zip'
    digest = sha256(archive)
    if not verification['passed'] or digest != verification['archiveSha256']:
        raise ValueError('Portable verifier did not qualify the exact archive')
    report['candidate'] = {'version': version, 'archive': str(archive), 'sha256': digest,
                           'bytes': archive.stat().st_size, 'checks': str(directory / 'verification.json')}


def rehearse(report, folder, receipt):
    if not receipt or not receipt.is_absolute() or receipt.name != 'result.json':
        raise ValueError('Supply the absolute qualification result.json with --receipt')
    ordinary_directory(receipt.parent)
    if receipt.parent.parent != VAR / 'isolated-delivery' / 'qualify':
        raise ValueError('Expected a receipt from the local qualification command')
    qualified = read(receipt)
    if qualified['status'] != 'passed' or qualified['commit'] != report['commit'] or report['dirty']:
        raise ValueError('Rehearsal requires a passed receipt for the current clean commit')
    candidate = qualified['candidate']
    archive = Path(candidate['archive'])
    ordinary_directory(archive.parent)
    if not archive.is_relative_to(RELEASES) or sha256(archive) != candidate['sha256']:
        raise ValueError('Candidate archive changed')
    # Reuse the deployment transaction and its rollback fault injection on its own fixture.
    import zipfile
    stage = ordinary_directory(VAR / 'run') / ('rehearsal-' + uuid.uuid4().hex)
    stage.mkdir()
    with zipfile.ZipFile(archive) as source:
        for item in source.infolist():
            if not item.filename.startswith('app/') or item.is_dir():
                continue
            target = (stage / item.filename.removeprefix('app/')).resolve()
            if not target.is_relative_to(stage):
                raise ValueError('Archive member escaped the rehearsal runtime')
            target.parent.mkdir(parents=True, exist_ok=True)
            with source.open(item) as contents, target.open('xb') as output:
                shutil.copyfileobj(contents, output)
    write(folder / 'mcp-runtime.json', {'executable': str(stage / 'AgentRelay.Windows.exe')})
    previous = folder / 'previous'
    previous.mkdir()
    if not STABLE.exists():
        raise ValueError('Rehearsal requires an existing installed baseline runtime')
    runtime = read(STABLE / 'mcp-runtime.json')
    # Only the installed executable is reused. The transaction has its own data and credential files.
    write(previous / 'mcp-runtime.json', {'executable': runtime_command(runtime)[0]})
    verification = read(Path(candidate['checks']))
    fixture = ordinary_directory(Path(verification['extracted']) / 'var/apps/agent-relay/default')
    if not fixture.is_relative_to(archive.parent):
        raise ValueError('Fixture is outside the verified extraction')
    reject_pilotage_paths(ROOT, fixture)
    binding = Path(verification['extracted']) / 'requests-0/example-context.json'
    phase(report, folder, 'rollback', python_script('deployment-acceptance.py', '--prepared', folder,
                                                    '--previous-prepared', previous, '--fixture', fixture,
                                                    '--binding', binding))
    report['candidate'] = candidate


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['build', 'test', 'run', 'qualify', 'rehearse'])
    parser.add_argument('--receipt', type=Path)
    args = parser.parse_args()
    output = ordinary_directory(VAR / 'isolated-delivery' / args.action) / uuid.uuid4().hex
    reject_pilotage_paths(ROOT, output)
    output.mkdir(parents=True)
    report = {'action': args.action, 'startedUtc': datetime.now(timezone.utc).isoformat(),
              'status': 'running', 'phases': [], 'modelCalls': None if args.action == 'run' else 0,
              'modelTokens': None, 'diskFreeBeforeBytes': shutil.disk_usage(output).free}
    started = time.monotonic()
    before = None
    write(output / 'result.json', report)
    print(f'Operation report: {output / "result.json"}', flush=True)
    try:
        report.update(commit=git('rev-parse', 'HEAD'), dirty=bool(git('status', '--porcelain')))
        before = stable_snapshot() if STABLE.exists() else None
        report['stablePresent'] = before is not None
        report['stableRespondedBefore'] = before['responded'] if before else None
        if args.action == 'qualify':
            qualify(report, output)
        elif args.action == 'rehearse':
            rehearse(report, output, args.receipt)
        else:
            phase(report, output, args.action, powershell(ROOT / 'dev.ps1', args.action, '-App', 'agent-relay', '-Offline'))
        report['status'] = 'passed'
    except BaseException:
        report['status'] = 'failed'
        raise
    finally:
        try:
            report['stablePreserved'] = stable_snapshot() == before if before is not None else None
            if report['stablePreserved'] is False:
                raise RuntimeError('Protected stable identity or configuration changed during operation')
        except BaseException:
            report['status'] = 'failed'
            raise
        finally:
            report['seconds'] = round(time.monotonic() - started, 3)
            report['finishedUtc'] = datetime.now(timezone.utc).isoformat()
            report['diskFreeAfterBytes'] = shutil.disk_usage(output).free
            write(output / 'result.json', report)


if __name__ == '__main__':
    main()

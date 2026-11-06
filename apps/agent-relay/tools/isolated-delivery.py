"""Utility: build, test and qualify Agent Relay away from Pilotage AP.
Usage: run with build, test, run, qualify, rehearse or status; see the development guide.
"""

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET

from delivery_reports import REPORT_FILE, latest_reports, operation_directory
from deployment import read, write
from host_identity import resolve_host
from release_artifact import (
    EXTRACTED_DIRECTORY, VERIFICATION_FILE, archive_path, extract_archive,
    ordinary_directory, ordinary_file, read_json, sha256, verified_archive,
)
from runtime_layout import app_var, pilotage_scope, reject_pilotage_paths, runtime_command


TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[2]
VAR = app_var(ROOT)
STABLE = pilotage_scope(ROOT)
RELEASES = ROOT / 'local/apps/agent-relay/releases'
DELIVERY = VAR / 'isolated-delivery'
OPERATIONS = ('build', 'test', 'run', 'qualify', 'rehearse')


def show_status(actions, as_json):
    status = latest_reports(DELIVERY, actions)
    if as_json:
        print(json.dumps(status, indent=2))
        return
    for report in status['latest']:
        elapsed = f"{report['seconds']:.3f}s" if report['seconds'] is not None else 'duration unavailable'
        commit = (report['commit'] or 'unknown')[:12]
        candidate = report['candidate'] if isinstance(report['candidate'], dict) else {}
        version = f"  {candidate['version']}" if candidate.get('version') else ''
        print(f"{report['action']}  {report['status']}  {elapsed}  {commit}{version}\n  {report['report']}")
        if report['error']:
            print(f"  {report['error']}")
    for item in status['unreadable']:
        print(f"Unreadable report: {item['report']} ({item['reason']})", file=sys.stderr)
    if not status['latest']:
        print('No retained operation reports.')


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
    step = {'name': name, 'startedUtc': datetime.now(timezone.utc).isoformat(), 'attempts': 1, 'exitCode': None}
    report['phases'].append(step)
    log = folder / (name + '.log')
    step['log'] = log.name
    write(folder / REPORT_FILE, report)
    try:
        with log.open('wb') as output:
            print(f'{name}: {log}', flush=True)
            result = subprocess.run(command, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT)
            step['exitCode'] = result.returncode
            result.check_returncode()
        return log.read_text(encoding='utf-8-sig', errors='replace')
    finally:
        step['seconds'] = round(time.monotonic() - started, 3)
        step['finishedUtc'] = datetime.now(timezone.utc).isoformat()
        write(folder / REPORT_FILE, report)


def package_directory(directory):
    if not directory.is_absolute() or not directory.is_relative_to(RELEASES) or directory == RELEASES:
        raise ValueError('Package output is outside the release fixture root')
    if '..' in directory.relative_to(RELEASES).parts:
        raise ValueError('Package output contains parent segments')
    return ordinary_directory(directory)


def candidate_evidence(directory, version):
    directory = package_directory(directory)
    checks = directory / VERIFICATION_FILE
    verification = read_json(checks)
    if verification.get('passed') is not True or verification.get('version') != version:
        raise ValueError('Portable verifier did not qualify the selected version')
    extracted = directory / EXTRACTED_DIRECTORY
    if verification.get('extracted') != str(extracted):
        raise ValueError('Verification does not identify the expected extraction directory')
    archive = ordinary_file(archive_path(directory, version))
    return {'version': version, 'archive': str(archive), 'sha256': verification['archiveSha256'],
            'bytes': archive.stat().st_size, 'checks': str(checks)}


def qualify(report, folder):
    if report['dirty']:
        raise ValueError('Qualification requires a clean, identified Git commit')
    version = ET.parse(TOOLS.parent / 'Version.props').findtext('.//Version')
    if not version or '-rc.' not in version:
        raise ValueError('Qualification entry point requires an RC version')
    ordinary_directory(RELEASES)
    script = TOOLS / 'release.ps1'
    output = phase(report, folder, 'package', powershell(script, '-Action', 'Package', '-Offline'))
    locations = [line.split('=', 1)[1].strip() for line in output.splitlines() if line.startswith('RELEASE_DIRECTORY=')]
    if len(locations) != 1:
        raise ValueError('Packager did not report exactly one output directory')
    directory = package_directory(Path(locations[0]))
    phase(report, folder, 'verify', powershell(script, '-Action', 'Verify', '-Directory', directory))
    if git('rev-parse', 'HEAD') != report['commit'] or git('status', '--porcelain'):
        raise ValueError('Source changed during qualification')
    candidate = candidate_evidence(directory, version)
    if sha256(Path(candidate['archive'])) != candidate['sha256']:
        raise ValueError('Portable verifier did not qualify the exact archive')
    report['candidate'] = candidate


def stage_runtime(candidate):
    stage = ordinary_directory(VAR / 'run') / ('rehearsal-' + uuid.uuid4().hex)
    with verified_archive(Path(candidate['archive']), candidate['sha256']) as source:
        extract_archive(source, stage, prefix='app/')
    return {'executable': str(stage / 'AgentRelay.Windows.exe')}


def rehearse(report, folder, receipt, baseline_directory):
    if not receipt or not receipt.is_absolute() or receipt.name != REPORT_FILE:
        raise ValueError('Supply the absolute qualification result.json with --receipt')
    if receipt.parent.parent != operation_directory(DELIVERY, 'qualify') or receipt.parent.name == '..':
        raise ValueError('Expected a receipt from the local qualification command')
    qualified = read_json(receipt)
    if (qualified.get('action') != 'qualify' or qualified.get('status') != 'passed'
            or qualified.get('commit') != report['commit'] or report['dirty']):
        raise ValueError('Rehearsal requires a passed receipt for the current clean commit')
    candidate = qualified['candidate']
    archive = Path(candidate['archive'])
    if candidate != candidate_evidence(archive.parent, candidate['version']):
        raise ValueError('Qualification receipt differs from the archive evidence')
    extracted = archive.parent / EXTRACTED_DIRECTORY
    fixture = ordinary_directory(extracted / 'var/apps/agent-relay/default')
    reject_pilotage_paths(ROOT, fixture)
    binding = ordinary_file(extracted / 'requests-0/example-context.json')
    if baseline_directory:
        baseline_directory = package_directory(baseline_directory)
        baseline_version = read_json(baseline_directory / VERIFICATION_FILE)['version']
        baseline = candidate_evidence(baseline_directory, baseline_version)
        if baseline['sha256'] == candidate['sha256']:
            raise ValueError('Select a different verified baseline archive')
        baseline_runtime = stage_runtime(baseline)
        report['baseline'] = baseline
    else:
        if not STABLE.exists():
            raise ValueError('Supply --baseline-directory when there is no installed baseline runtime')
        # The compatibility route uses only the installed binary on synthetic data.
        runtime = read_json(STABLE / 'mcp-runtime.json')
        baseline_runtime = {'executable': runtime_command(runtime)[0]}
    previous = folder / 'previous'
    previous.mkdir()
    write(previous / 'mcp-runtime.json', baseline_runtime)
    write(folder / 'mcp-runtime.json', stage_runtime(candidate))
    report['candidate'] = candidate
    phase(report, folder, 'rollback', python_script('deployment-acceptance.py', '--prepared', folder,
                                                    '--previous-prepared', previous, '--fixture', fixture,
                                                    '--binding', binding))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=(*OPERATIONS, 'status'))
    parser.add_argument('--receipt', type=Path)
    parser.add_argument('--baseline-directory', type=Path, help='Use a verified baseline archive for rehearsal')
    parser.add_argument('--observe-stable', action='store_true', help='Explicitly compare stable identity before and after')
    parser.add_argument('--operation', choices=OPERATIONS, help='Filter status to one operation')
    parser.add_argument('--json', action='store_true', help='Emit machine-readable status')
    args = parser.parse_args()
    if args.action == 'status':
        if args.receipt or args.baseline_directory or args.observe_stable:
            parser.error('status reads reports only')
        show_status((args.operation,) if args.operation else OPERATIONS, args.json)
        return
    if args.operation or args.json:
        parser.error('--operation and --json are for status')
    if args.action == 'rehearse' and not args.receipt:
        parser.error('rehearse requires --receipt')
    if (args.receipt or args.baseline_directory) and args.action != 'rehearse':
        parser.error('--receipt and --baseline-directory are for rehearse')
    output = operation_directory(DELIVERY, args.action) / uuid.uuid4().hex
    reject_pilotage_paths(ROOT, output)
    output.mkdir(parents=True)
    report = {'action': args.action, 'startedUtc': datetime.now(timezone.utc).isoformat(),
              'status': 'running', 'phases': [], 'modelCalls': None if args.action == 'run' else 0,
              'modelTokens': None, 'diskFreeBeforeBytes': shutil.disk_usage(output).free}
    started = time.monotonic()
    before = None
    write(output / REPORT_FILE, report)
    print(f'Operation report: {output / REPORT_FILE}', flush=True)
    try:
        report.update(commit=git('rev-parse', 'HEAD'), dirty=bool(git('status', '--porcelain')))
        observe_stable = args.observe_stable or (args.action == 'rehearse' and not args.baseline_directory)
        before = stable_snapshot() if observe_stable and STABLE.exists() else None
        report['stableObserved'] = observe_stable
        report['stablePresent'] = before is not None if observe_stable else None
        report['stableRespondedBefore'] = before['responded'] if before else None
        if args.action == 'qualify':
            qualify(report, output)
        elif args.action == 'rehearse':
            rehearse(report, output, args.receipt, args.baseline_directory)
        else:
            phase(report, output, args.action, powershell(ROOT / 'dev.ps1', args.action, '-App', 'agent-relay', '-Offline'))
        report['status'] = 'passed'
    except (Exception, KeyboardInterrupt) as error:
        report['status'] = 'interrupted' if isinstance(error, KeyboardInterrupt) else 'failed'
        report['error'] = str(error) or type(error).__name__
        raise
    finally:
        original_error = sys.exc_info()[1]
        try:
            report['stablePreserved'] = stable_snapshot() == before if before is not None else None
            if report['stablePreserved'] is False:
                raise RuntimeError('Protected stable identity or configuration changed during operation')
        except Exception as error:
            report['stableCheckError'] = str(error)
            if original_error is None:
                report.update(status='failed', error=str(error))
                raise
        finally:
            report['seconds'] = round(time.monotonic() - started, 3)
            report['finishedUtc'] = datetime.now(timezone.utc).isoformat()
            report['diskFreeAfterBytes'] = shutil.disk_usage(output).free
            write(output / REPORT_FILE, report)


if __name__ == '__main__':
    main()

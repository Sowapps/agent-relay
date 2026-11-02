"""Guarded local publication; prepare on a copy before replacing an owned host."""
from runtime_layout import runtime_command, validate_runtime, app_var
import argparse
from contextlib import contextmanager, closing
import ctypes
import json
import os
from pathlib import Path
import shutil
import signal
import sqlite3
import subprocess
import time
import uuid
import re


def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    temporary = path.with_suffix(path.suffix + '.pending')
    temporary.write_text(json.dumps(value, indent=2), encoding='utf-8'); temporary.replace(path)


@contextmanager
def exclusive(data):
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
    kernel.CreateFileW.restype = ctypes.c_void_p; kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    handle = kernel.CreateFileW(str(data / 'host.lock'), 0xC0000000, 0, None, 4, 0x80, None)
    if handle == ctypes.c_void_p(-1).value: raise RuntimeError('Another host owns the directory; no migration or rollback performed')
    try: yield
    finally: kernel.CloseHandle(handle)


def snapshot(data, destination):
    destination.mkdir()
    for name in ['mcp-runtime.json', 'relay-config.json', 'relay-projects.json', 'mcp-contract.json', 'mcp-contract-history.json']:
        if (data / name).exists(): shutil.copy2(data / name, destination / name)
    if (data / 'connections').exists(): shutil.copytree(data / 'connections', destination / 'connections')
    if (data / 'relay.sqlite').exists():
        with closing(sqlite3.connect(data / 'relay.sqlite')) as source, closing(sqlite3.connect(destination / 'relay.sqlite')) as target: source.backup(target)


def validate(runtime, root, development=False):
    validate_runtime(runtime, root, development)


def identity(previous, data):
    command = f'Get-CimInstance Win32_Process -Filter "ProcessId={int(previous["pid"])}" | Select-Object ExecutablePath,CommandLine | ConvertTo-Json -Compress'
    result = subprocess.run(['pwsh', '-NoProfile', '-Command', command], capture_output=True, text=True, check=True, timeout=20)
    if not result.stdout.strip(): return False
    process = json.loads(result.stdout)
    if os.path.normcase(process['ExecutablePath']) != os.path.normcase(runtime_command(previous)[0]) or not all(part in process['CommandLine'] for part in runtime_command(previous)) or ' tray ' not in process['CommandLine'] or str(data) not in process['CommandLine']:
        raise RuntimeError('Recorded host identity changed; no process was stopped')
    return True


def launch(runtime, data, reports, show=False):
    identifier = uuid.uuid4().hex
    with (reports / (identifier + '.stdout.log')).open('wb') as output, (reports / (identifier + '.stderr.log')).open('wb') as errors:
        process = subprocess.Popen(runtime_command(runtime) + ['tray', '--data-dir', str(data)] + (['--show'] if show else []), stdout=output, stderr=errors, creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        deadline = time.monotonic() + 25
        while time.monotonic() < deadline:
            if process.poll() is not None: raise RuntimeError('Candidate host exited before health confirmation')
            probe = subprocess.run(runtime_command(runtime) + ['profiles', '--data-dir', str(data)], capture_output=True, timeout=8, creationflags=subprocess.CREATE_NO_WINDOW)
            if probe.returncode == 0: json.loads(probe.stdout); return process
            time.sleep(.2)
        raise RuntimeError('Candidate host health was not confirmed')
    except BaseException:
        if process.poll() is None: process.terminate()
        process.wait(timeout=10); raise


def publish(data, prepared, reports, show):
    root = Path(__file__).resolve().parents[3]; data, prepared, reports = data.resolve(), prepared.resolve(), reports.resolve()
    for path in [data, prepared, reports]:
        if not any(path.is_relative_to(base) for base in [app_var(root), root / 'local/apps/agent-relay']): raise ValueError('Deployment paths escaped the declared application roots')
    runtime = read(prepared / 'mcp-runtime.json'); development=data.is_relative_to(root / 'local/apps/agent-relay'); validate(runtime, root, development)
    contract_path = Path(runtime_command(runtime)[0]).parent / 'mcp-contract.json'
    contract = read(contract_path)
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-rc\.[1-9]\d*)?', contract['version']): raise ValueError('Only stable and RC versions may be deployed')
    history_path = data / 'mcp-contract-history.json'
    history = read(history_path) if history_path.exists() else {}
    known = history.get(contract['version'])
    if known is not None and known != contract['contractHash']:
        raise ValueError('This deployed version already identifies a different MCP contract. Increment the RC or stable version before deployment.')
    existing_pointer = read(data / 'mcp-runtime.json') if (data / 'mcp-runtime.json').is_file() else None
    existing_host = read(data / 'ui-host.json') if (data / 'ui-host.json').is_file() else None
    if existing_pointer and existing_host and runtime_command(existing_pointer) == runtime_command(runtime) and identity(existing_host, data):
        probe = subprocess.run(runtime_command(runtime) + ['profiles', '--data-dir', str(data)], capture_output=True, timeout=8, creationflags=subprocess.CREATE_NO_WINDOW)
        if probe.returncode == 0:
            json.loads(probe.stdout)
            write(data / 'mcp-contract.json', contract)
            write(history_path, dict(history, **{contract['version']: contract['contractHash']}))
            print(json.dumps({'published': False, 'alreadyCurrent': True, 'pid': existing_host['pid'], 'currentChatMcpQualified': False}))
            return
    reports.mkdir(parents=True, exist_ok=True); data.mkdir(parents=True, exist_ok=True)
    transaction = reports / ('deployment-' + uuid.uuid4().hex); transaction.mkdir()
    check = transaction / 'preflight'; check.mkdir(); snapshot(data, check / 'before')
    for name in ['relay.sqlite', 'relay-config.json', 'relay-projects.json']:
        if (check / 'before' / name).exists(): shutil.copy2(check / 'before' / name, check / name)
    if (check / 'before/connections').exists(): shutil.copytree(check / 'before/connections', check / 'connections')
    probe = launch(runtime, check, transaction); probe.terminate(); probe.wait(timeout=10)
    metadata = data / 'ui-host.json'; report_metadata = reports / 'host.json'
    previous = read(metadata) if metadata.exists() else read(report_metadata) if report_metadata.exists() else None
    if previous is not None:
        validate(previous, root, development)
    was_running = previous is not None and identity(previous, data)
    if (data / 'relay.sqlite').exists():
        with closing(sqlite3.connect((data / 'relay.sqlite').as_uri() + '?mode=ro', uri=True)) as database:
            if database.execute("SELECT count(*) FROM work WHERE json_extract(payload,'$.state') IN ('pending','running')").fetchone()[0]:
                raise RuntimeError('Active durable work exists; publication refused')
    if was_running:
        os.kill(int(previous['pid']), signal.SIGTERM)
        for _ in range(50):
            try:
                with exclusive(data): break
            except RuntimeError: time.sleep(.1)
        else: raise RuntimeError('Previous host did not release ownership')
    backup = transaction / 'before'; backup_ready = False
    started = None
    try:
        with exclusive(data): snapshot(data, backup)
        backup_ready = True
        started = launch(runtime, data, transaction, show)
        old = read(backup / 'mcp-runtime.json') if (backup / 'mcp-runtime.json').exists() else {}
        if old.get('connectionId'): runtime['connectionId'] = old['connectionId']
        write(data / 'mcp-runtime.json', runtime)
        write(data / 'mcp-contract.json', contract)
        write(history_path, dict(history, **{contract['version']: contract['contractHash']}))
        write(metadata, dict(runtime, pid=started.pid, executable=runtime_command(runtime)[0], data=str(data), transaction=str(transaction)))
        write(report_metadata, read(metadata))
        write(transaction / 'result.json', {'healthy': True, 'rollbackSnapshot': str(backup), 'pid': started.pid})
    except BaseException:
        if started is not None: started.terminate(); started.wait(timeout=10)
        if backup_ready:
            with exclusive(data):
                for name in ['relay.sqlite', 'relay.sqlite-wal', 'relay.sqlite-shm', 'mcp-runtime.json', 'mcp-contract.json', 'mcp-contract-history.json', 'connections']:
                    target = data / name
                    if target.exists(): target.replace(transaction / ('failed-' + name))
                for name in ['relay.sqlite', 'mcp-runtime.json', 'mcp-contract.json', 'mcp-contract-history.json']:
                    if (backup / name).exists(): shutil.copy2(backup / name, data / name)
                if (backup / 'connections').exists(): shutil.copytree(backup / 'connections', data / 'connections')
        if was_running:
            restored = launch(previous, data, transaction, show); write(metadata, dict(previous, pid=restored.pid)); write(report_metadata, read(metadata))
        write(transaction / 'result.json', {'healthy': False, 'rollbackPerformed': True}); raise
    print(json.dumps({'published': True, 'pid': started.pid, 'transaction': str(transaction), 'currentChatMcpQualified': False}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ['data', 'prepared', 'reports']: parser.add_argument('--' + option, type=Path, required=True)
    parser.add_argument('--show', action='store_true'); args = parser.parse_args()
    publish(args.data, args.prepared, args.reports, args.show)

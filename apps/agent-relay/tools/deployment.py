"""Guarded local publication; prepare on a copy before replacing an owned host."""
from runtime_layout import runtime_command, validate_runtime, app_var, reject_pilotage_paths
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
from host_identity import identity, resolve_host
from release_artifact import validate_version

STATE_FILES = ('relay.sqlite', 'mcp-runtime.json', 'relay-config.json', 'relay-projects.json',
               'mcp-contract.json', 'mcp-contract-history.json')

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
    if handle == ctypes.c_void_p(-1).value: raise RuntimeError('Another host owns the directory; offline maintenance refused')
    try: yield
    finally: kernel.CloseHandle(handle)


def snapshot(data, destination, *, files=STATE_FILES):
    destination.mkdir()
    for name in files:
        if name == 'relay.sqlite':
            continue  # SQLite backup includes committed WAL state.
        if (data / name).exists(): shutil.copy2(data / name, destination / name)
    if (data / 'connections').exists():
        shutil.copytree(data / 'connections', destination / 'connections')
    if (data / 'relay.sqlite').exists():
        with closing(sqlite3.connect(data / 'relay.sqlite')) as source, closing(sqlite3.connect(destination / 'relay.sqlite')) as target: source.backup(target)


def validate(runtime, root, development=False):
    validate_runtime(runtime, root, development)


def demand_idle(runtime, data, *, allow_uninitialized=False):
    """Call the C# durable-work guard. The online admission barrier is separate."""
    command = runtime_command(runtime) + ['maintenance-idle', '--data-dir', str(data)]
    if allow_uninitialized:
        command.append('--allow-uninitialized')
    result = subprocess.run(command, capture_output=True, encoding='utf-8', errors='replace',
                            timeout=12, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise RuntimeError('Maintenance guard refused: ' + result.stderr.strip())
    if json.loads(result.stdout) != {'idle': True}:
        raise RuntimeError('The maintenance guard did not confirm idle durable work')


class PromotionFreeze:
    """Block new admission until an owned replacement or rollback is healthy."""

    def __init__(self, data):
        self.marker = data / 'promotion.lock'
        self.owner = uuid.uuid4().hex
        self.retain = False

    def __enter__(self):
        try:
            with self.marker.open('x', encoding='ascii') as lock:
                lock.write(self.owner)
        except FileExistsError as error:
            raise RuntimeError('A promotion barrier already exists for this scope') from error
        return self

    def __exit__(self, *_):
        if not self.retain and self.marker.is_file() and self.marker.read_text(encoding='ascii') == self.owner:
            self.marker.unlink()


def confirm_quiescence(runtime, data):
    result = subprocess.run(runtime_command(runtime) + ['deployment-quiesce', '--data-dir', str(data)],
                            capture_output=True, timeout=10, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        try:
            failure = json.loads(result.stderr)
        except (ValueError, UnicodeError):
            failure = {}
        if failure.get('code') == 'busy':
            raise RuntimeError('Admitted requests or operations are still active; publication refused')
        raise RuntimeError('The running relay did not confirm the admission barrier; publication refused')
    if json.loads(result.stdout).get('warnings') != ['deployment-quiescent-v1']:
        raise RuntimeError('The running relay did not acknowledge the promotion protocol; publication refused')


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


def publish(data, prepared, reports, show, *, expected_data=None, guard_runtime=None):
    root = Path(__file__).resolve().parents[3]; data, prepared, reports = data.resolve(), prepared.resolve(), reports.resolve()
    if expected_data is not None and data != expected_data.resolve():
        raise ValueError('Selected data scope differs from the approved promotion target')
    reject_pilotage_paths(root, data, prepared, reports)
    for path in [data, prepared, reports]:
        if not any(path.is_relative_to(base) for base in [app_var(root), root / 'local/apps/agent-relay']): raise ValueError('Deployment paths escaped the declared application roots')
    runtime = read(prepared / 'mcp-runtime.json'); development=data.is_relative_to(root / 'local/apps/agent-relay'); validate(runtime, root, development)
    if guard_runtime is not None:
        validate(guard_runtime, root, development)
    contract_path = Path(runtime_command(runtime)[0]).parent / 'mcp-contract.json'
    contract = read(contract_path)
    validate_version(contract['version'])
    data.mkdir(parents=True, exist_ok=True)
    with PromotionFreeze(data) as freeze:
        result = publish_prepared(data, reports, runtime, contract, root, development, show, freeze,
                                  guard_runtime=guard_runtime)
    print(json.dumps(result))


def publish_prepared(data, reports, runtime, contract, root, development, show, freeze, *,
                     validate_previous=None, verify=None, guard_runtime=None):
    history_path = data / 'mcp-contract-history.json'
    history = read(history_path) if history_path.exists() else {}
    known = history.get(contract['version'])
    if known is not None and known != contract['contractHash']:
        raise ValueError('This deployed version already identifies a different MCP contract. Increment the RC or stable version before deployment.')
    existing_pointer = read(data / 'mcp-runtime.json') if (data / 'mcp-runtime.json').is_file() else None
    existing_host = read(data / 'ui-host.json') if (data / 'ui-host.json').is_file() else None
    if validate_previous is not None and existing_pointer:
        validate_previous(existing_pointer)
        existing_host = existing_pointer
    current_host = resolve_host(existing_host, data) if existing_pointer and existing_host and runtime_command(existing_pointer) == runtime_command(runtime) else None
    if current_host is not None:
        probe = subprocess.run(runtime_command(runtime) + ['profiles', '--data-dir', str(data)], capture_output=True, timeout=8, creationflags=subprocess.CREATE_NO_WINDOW)
        if probe.returncode == 0:
            json.loads(probe.stdout)
            write(data / 'mcp-contract.json', contract)
            write(history_path, dict(history, **{contract['version']: contract['contractHash']}))
            write(data / 'ui-host.json', current_host)
            proof = verify() if verify else None
            return {'published': False, 'alreadyCurrent': True, 'pid': current_host['pid'],
                    'currentChatMcpQualified': False, 'verification': proof}
    guard_runtime = runtime if guard_runtime is None else guard_runtime
    first_install = existing_pointer is None and existing_host is None and not (reports / 'host.json').exists()
    demand_idle(guard_runtime, data, allow_uninitialized=first_install)
    reports.mkdir(parents=True, exist_ok=True)
    transaction = reports / ('deployment-' + uuid.uuid4().hex); transaction.mkdir()
    check = transaction / 'preflight'; check.mkdir(); snapshot(data, check / 'before')
    for name in ['relay.sqlite', 'relay-config.json', 'relay-projects.json']:
        if (check / 'before' / name).exists(): shutil.copy2(check / 'before' / name, check / name)
    if (check / 'before/connections').exists(): shutil.copytree(check / 'before/connections', check / 'connections')
    probe = launch(runtime, check, transaction); probe.terminate(); probe.wait(timeout=10)
    return replace_runtime(data, reports, runtime, contract, history, transaction, root, development, show, freeze,
                           validate_previous=validate_previous, verify=verify, guard_runtime=guard_runtime,
                           allow_uninitialized=first_install)


def replace_runtime(data, reports, runtime, contract, history, transaction, root, development, show, freeze, *,
                    validate_previous=None, verify=None, guard_runtime, allow_uninitialized):
    history_path = data / 'mcp-contract-history.json'
    metadata = data / 'ui-host.json'; report_metadata = reports / 'host.json'
    previous = read(metadata) if metadata.exists() else read(report_metadata) if report_metadata.exists() else None
    pointer = data / 'mcp-runtime.json'
    if validate_previous is not None and pointer.is_file():
        previous = read(pointer)
    if previous is not None:
        if validate_previous is None:
            validate(previous, root, development)
        else:
            validate_previous(previous)
    previous = resolve_host(previous, data)
    was_running = previous is not None
    if was_running:
        confirm_quiescence(runtime, data)
    demand_idle(guard_runtime, data, allow_uninitialized=allow_uninitialized)
    if was_running:
        if not identity(previous, data):
            raise RuntimeError('Relay owner changed before shutdown; no process was stopped')
        os.kill(int(previous['pid']), signal.SIGTERM)
        freeze.retain = True
        for _ in range(50):
            try:
                with exclusive(data): break
            except RuntimeError: time.sleep(.1)
        else: raise RuntimeError('Previous host did not release ownership')
    backup = transaction / 'before'; backup_ready = False
    started = None
    proof = None
    try:
        with exclusive(data): snapshot(data, backup)
        backup_ready = True
        freeze.retain = True
        started = launch(runtime, data, transaction, show)
        old = read(backup / 'mcp-runtime.json') if (backup / 'mcp-runtime.json').exists() else {}
        retained = {key: old[key] for key in ('connectionId', 'distribution', 'mcpExecutable') if old.get(key)}
        write(data / 'mcp-runtime.json', retained | runtime)
        write(data / 'mcp-contract.json', contract)
        write(history_path, dict(history, **{contract['version']: contract['contractHash']}))
        proof = verify() if verify else None
        write(metadata, dict(runtime, pid=started.pid, executable=runtime_command(runtime)[0], data=str(data), transaction=str(transaction)))
        write(report_metadata, read(metadata))
        write(transaction / 'result.json', {'healthy': True, 'rollbackSnapshot': str(backup), 'pid': started.pid})
        freeze.retain = False
    except BaseException:
        if started is not None: started.terminate(); started.wait(timeout=10)
        if backup_ready:
            with exclusive(data):
                for name in (*STATE_FILES, 'relay.sqlite-wal', 'relay.sqlite-shm', 'connections'):
                    target = data / name
                    if target.exists(): target.replace(transaction / ('failed-' + name))
                for name in STATE_FILES:
                    if (backup / name).exists(): shutil.copy2(backup / name, data / name)
                if (backup / 'connections').exists(): shutil.copytree(backup / 'connections', data / 'connections')
        if was_running:
            restored = launch(previous, data, transaction, show); write(metadata, dict(previous, pid=restored.pid)); write(report_metadata, read(metadata))
        write(transaction / 'result.json', {'healthy': False, 'rollbackPerformed': True})
        freeze.retain = False
        raise
    return {'published': True, 'pid': started.pid, 'transaction': str(transaction), 'currentChatMcpQualified': False,
            'verification': proof}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ['data', 'prepared', 'reports']: parser.add_argument('--' + option, type=Path, required=True)
    parser.add_argument('--show', action='store_true'); args = parser.parse_args()
    publish(args.data, args.prepared, args.reports, args.show)

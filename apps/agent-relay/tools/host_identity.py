"""Discover only the unique exact runtime/data tray owner; the data lock remains authoritative.

@see deployment.publish: revalidate immediately before stopping a discovered owner.
"""
import ctypes
import json
import os
from pathlib import Path
import subprocess
from runtime_layout import runtime_command


def command_parts(command):
    count = ctypes.c_int()
    shell = ctypes.WinDLL('shell32', use_last_error=True)
    shell.CommandLineToArgvW.argtypes = [ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_int)]
    shell.CommandLineToArgvW.restype = ctypes.POINTER(ctypes.c_wchar_p)
    pointer = shell.CommandLineToArgvW(command, ctypes.byref(count))
    if not pointer:
        raise OSError('Host command line could not be parsed')
    try:
        return [pointer[index] for index in range(count.value)]
    finally:
        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        kernel.LocalFree.argtypes = [ctypes.c_void_p]
        kernel.LocalFree.restype = ctypes.c_void_p
        kernel.LocalFree(ctypes.cast(pointer, ctypes.c_void_p))


def same_absolute_path(actual, expected):
    # The observed process may have a different working directory or drive context.
    return (bool(actual) and Path(actual).is_absolute() and Path(expected).is_absolute()
            and os.path.normcase(os.path.normpath(actual)) == os.path.normcase(os.path.normpath(expected)))


def matches(process, runtime, data):
    expected = runtime_command(runtime)
    if not same_absolute_path(process.get('ExecutablePath'), expected[0]):
        return False
    command = process.get('CommandLine')
    if not command:
        return False
    parts = command_parts(command)
    if len(parts) <= len(expected) or any(not same_absolute_path(actual, wanted) for actual, wanted in zip(parts, expected)):
        return False
    tail = parts[len(expected):]
    if tail[0] != 'tray' or tail.count('--data-dir') != 1:
        return False
    index = tail.index('--data-dir')
    return index + 1 < len(tail) and same_absolute_path(tail[index + 1], data)


def processes(runtime):
    executable = runtime_command(runtime)[0]
    name = Path(executable).name.replace("'", "''")
    script = ("[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); "
              "Get-CimInstance Win32_Process -ErrorAction Stop -Filter \"Name='" + name + "'\" | "
              "Select-Object ProcessId,ExecutablePath,CommandLine | ConvertTo-Json -Compress")
    result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], capture_output=True,
                            text=True, encoding='utf-8', errors='replace', timeout=20,
                            creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise RuntimeError(f'Cannot verify host identity through Windows process inventory (exit {result.returncode})')
    value = json.loads(result.stdout) if result.stdout.strip() else []
    return value if isinstance(value, list) else [value]


def resolve_host(previous, data, inventory=None):
    if previous is None:
        return None
    candidates = [item for item in (processes(previous) if inventory is None else inventory) if matches(item, previous, data)]
    if len(candidates) > 1:
        raise RuntimeError('Multiple exact-scope relay hosts were found; no process was stopped')
    return dict(previous, pid=int(candidates[0]['ProcessId'])) if candidates else None


def identity(previous, data):
    current = resolve_host(previous, data)
    return current is not None and current['pid'] == previous['pid']

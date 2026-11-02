"""Stable stdio launcher: follow the exported runtime pointer without changing client settings on every build."""
from runtime_layout import read_runtime, validate_runtime, runtime_command
import argparse
import json
from pathlib import Path
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data-dir', type=Path, required=True)
    parser.add_argument('--connection-id')
    args = parser.parse_args()
    data = args.data_dir.resolve()
    runtime = read_runtime(data)
    root = Path(__file__).resolve().parents[3]
    command = validate_runtime(runtime, root, development=data.is_relative_to(root / 'local/apps/agent-relay')) + ['mcp', '--data-dir', str(data)]
    connection = args.connection_id
    if not connection: raise ValueError('An explicit connection-id is required; no principal is inferred from the runtime pointer')
    if connection:command += ['--connection-id',connection]
    with subprocess.Popen(command,
                          stdin=sys.stdin, stdout=sys.stdout, stderr=sys.stderr,
                          creationflags=subprocess.CREATE_NO_WINDOW) as process:
        try:
            return process.wait()
        except BaseException:
            if process.poll() is None:
                process.terminate()
            process.wait()
            raise


if __name__ == '__main__':
    raise SystemExit(main())

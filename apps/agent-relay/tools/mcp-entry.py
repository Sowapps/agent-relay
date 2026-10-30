"""Stable stdio launcher: follow the exported runtime pointer without changing client settings on every build."""
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
    runtime = json.loads((data / 'mcp-runtime.json').read_text(encoding='utf-8-sig'))
    root = Path(__file__).resolve().parents[3]
    assembly = Path(runtime['assembly']).resolve()
    dotnet = Path(runtime['dotnet']).resolve()
    if not assembly.is_relative_to(root / 'local/apps/agent-relay/run') or assembly.name != 'AgentRelay.Windows.dll' or not assembly.is_file():
        raise ValueError('Runtime pointer must identify a staged Agent Relay assembly')
    if not dotnet.is_file():
        raise FileNotFoundError('Qualified private .NET runtime is unavailable')
    # Native child inherits the same stdio pipes. Never print launcher progress to stdout.
    command = [str(dotnet), str(assembly), 'mcp', '--data-dir', str(data)]
    connection = args.connection_id or runtime.get('connectionId')
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

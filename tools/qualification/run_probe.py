"""Bound execution time for isolated fixture processes; stdout stays machine-readable."""
import argparse
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument("--timeout", type=float, default=20)
parser.add_argument("command", nargs=argparse.REMAINDER)
args = parser.parse_args()
try:
    process = subprocess.run(args.command, capture_output=True, text=True, timeout=args.timeout)
    sys.stdout.write(process.stdout)
    sys.stderr.write(process.stderr)
    raise SystemExit(process.returncode)
except subprocess.TimeoutExpired as error:
    print(f"Fixture timeout after {args.timeout}s: {error.cmd}", file=sys.stderr)
    raise SystemExit(124)

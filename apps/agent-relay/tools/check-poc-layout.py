"""Inspect migrated Python paths without executing POC steps or native commands."""
import ast
from pathlib import Path
import runpy
import subprocess
import sys


application = Path(__file__).resolve().parents[1]
repository = application.parents[1]
sources = list((application / "poc").rglob("*.py"))
for source in sources:
    ast.parse(source.read_text(encoding="utf-8-sig"), filename=str(source))


def forbidden_process(*args, **kwargs):
    raise AssertionError("Layout inspection must not launch any native process")


original_run = subprocess.run
original_path = list(sys.path)
try:
    subprocess.run = forbidden_process
    sys.path.insert(0, str(application / "poc"))
    limited = runpy.run_path(str(application / "poc/run.py"), run_name="layout_inspection")
    qualification = runpy.run_path(str(application / "poc/qualification/run.py"), run_name="layout_inspection")
    assert limited["ROOT"] == application
    assert limited["BASE"] == repository / "local/apps/agent-relay/poc"
    assert qualification["BASE"] == repository / "local/apps/agent-relay/qualification"
    for name in ("relay.py", "worker.py"):
        assert (limited["ROOT"] / "poc" / name).is_file(), name
    for name in ("relay_windows.py", "message_endpoint.py", "linux_control.py"):
        assert (qualification["SOURCE"] / name).is_file(), name
    assert limited["BASE"].is_dir()
    assert qualification["BASE"].is_dir()
finally:
    subprocess.run = original_run
    sys.path[:] = original_path

print(f"POC_LAYOUT_SYNTAX=PASS ({len(sources)} Python sources; two runner paths; no native processes)")

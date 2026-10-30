"""Exercise the actual Windows tray executable and client modes in separate processes."""
import argparse
import json
from pathlib import Path
import subprocess
import time
import uuid


parser = argparse.ArgumentParser()
parser.add_argument("--dotnet", required=True)
parser.add_argument("--assembly", required=True)
parser.add_argument("--data", required=True)
args = parser.parse_args()
data = Path(args.data).resolve()
data.mkdir(parents=True, exist_ok=True)
base = [args.dotnet, args.assembly]
identifier = str(uuid.uuid4())


def call(mode, *options, timeout=8):
    return subprocess.run(base + [mode, "--data-dir", str(data), *options], capture_output=True, text=True,
                          encoding="utf-8", timeout=timeout, creationflags=subprocess.CREATE_NO_WINDOW)


def host():
    process = subprocess.Popen(base + ["tray", "--data-dir", str(data), "--lifetime-ms", "3000"],
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)
    deadline = time.monotonic() + 2
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise AssertionError(process.communicate())
        # The exclusive ownership file exists before the server; allow its short startup path.
        if (data / "host.lock").exists():
            return process
        time.sleep(0.025)
    process.kill()
    process.wait()
    raise AssertionError("Tray host did not create its data directory")


def finish(process):
    try:
        stdout, stderr = process.communicate(timeout=8)
        if process.returncode != 0:
            raise AssertionError(f"Tray exited {process.returncode}: {stderr!r}")
    except Exception:
        if process.poll() is None:
            process.kill()
            process.wait()
        raise


first = host()
try:
    sent = call("send", "--id", identifier, "--message", "Échange Windows réel")
    if sent.returncode != 0:
        raise AssertionError(sent.stderr)
    exchange = json.loads(sent.stdout)
    if exchange["reply"]["inReplyTo"] != identifier or exchange["reply"]["body"] != "Échange Windows réel":
        raise AssertionError(exchange)
        duplicate = call("tray", "--show")
        if duplicate.returncode != 0 or first.poll() is not None:
            raise AssertionError(f"Relaunch did not activate the existing host: {duplicate.stderr!r}")
finally:
    finish(first)
second = host()
try:
    read = call("read", "--id", identifier)
    if read.returncode != 0 or json.loads(read.stdout) != exchange:
        raise AssertionError(f"Restart changed recorded exchange: {read.stderr}")
finally:
    finish(second)
unavailable = call("read", "--id", identifier)
if unavailable.returncode == 0 or json.loads(unavailable.stderr).get("code") != "relay_timeout":
    raise AssertionError("A stopped host produced false success")
result = {"Passed": True, "RequestId": identifier, "Exchange": exchange, "HostRestartPassed": True,
          "UnavailableHostRejected": True, "DataDirectory": str(data)}
(data / "process-smoke.json").write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
print("WINDOWS_PROCESS_SMOKE=PASS (tray host, client message, persisted reply after restart, unavailable host)")

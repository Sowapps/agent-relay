"""Real Debug Adapter Protocol qualification using an isolated netcoredbg process."""
import argparse
import json
from pathlib import Path
import queue
import subprocess
import threading
import time


class Adapter:
    def __init__(self, executable, transcript):
        self.log = transcript.open("w", encoding="utf-8")
        self.process = subprocess.Popen([executable, "--interpreter=vscode"], stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        self.incoming = queue.Queue()
        self.pending = []
        self.events = []
        self.sequence = 0
        threading.Thread(target=self.read, daemon=True).start()
        threading.Thread(target=self.read_errors, daemon=True).start()

    def read(self):
        try:
            while True:
                headers = {}
                while True:
                    line = self.process.stdout.readline()
                    if not line:
                        raise EOFError("Debugger stdout closed")
                    if line in (b"\r\n", b"\n"):
                        break
                    key, value = line.decode("ascii").split(":", 1)
                    headers[key.lower()] = value.strip()
                length = int(headers["content-length"])
                payload = bytearray()
                while len(payload) < length:
                    block = self.process.stdout.read(length - len(payload))
                    if not block:
                        raise EOFError("Incomplete debugger frame")
                    payload.extend(block)
                self.incoming.put(json.loads(payload))
        except Exception as error:
            self.incoming.put(error)

    def read_errors(self):
        for line in iter(self.process.stderr.readline, b""):
            self.incoming.put({"type": "stderr", "text": line.decode("utf-8", "replace").rstrip()})

    def send(self, command, arguments=None):
        self.sequence += 1
        request = {"seq": self.sequence, "type": "request", "command": command, "arguments": arguments or {}}
        payload = json.dumps(request).encode("utf-8")
        self.log.write(json.dumps({"sent": request}) + "\n")
        self.log.flush()
        self.process.stdin.write(f"Content-Length: {len(payload)}\r\n\r\n".encode("ascii") + payload)
        self.process.stdin.flush()
        return self.sequence

    def wait(self, predicate, timeout=40):
        deadline = time.monotonic() + timeout
        while True:
            for index, message in enumerate(self.pending):
                if predicate(message):
                    return self.pending.pop(index)
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError("Timed out waiting for debugger response/event")
            message = self.incoming.get(timeout=remaining)
            if isinstance(message, Exception):
                raise message
            self.log.write(json.dumps({"received": message}) + "\n")
            self.log.flush()
            if message.get("type") == "event":
                self.events.append(message)
            self.pending.append(message)

    def response(self, sequence):
        message = self.wait(lambda item: item.get("type") == "response" and item.get("request_seq") == sequence)
        if not message.get("success"):
            raise RuntimeError(json.dumps(message))
        return message.get("body", {})

    def request(self, command, arguments=None):
        return self.response(self.send(command, arguments))

    def event(self, name):
        return self.wait(lambda item: item.get("type") == "event" and item.get("event") == name).get("body", {})

    def close(self):
        try:
            if self.process.poll() is None:
                try:
                    self.send("disconnect", {"terminateDebuggee": True})
                except OSError:
                    pass  # The debugger can exit between poll and the pipe write.
                try:
                    self.process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    self.process.kill()
                    self.process.wait(timeout=5)
        finally:
            self.log.close()


def qualify(args):
    source = Path(args.source).resolve()
    line = next(number for number, text in enumerate(source.read_text(encoding="utf-8-sig").splitlines(), 1)
                if "var reply = new ProbeMessage" in text)
    adapter = Adapter(args.debugger, Path(args.transcript))
    result = {"Passed": False, "BreakpointLine": line}
    try:
        adapter.request("initialize", {"adapterID": "coreclr", "linesStartAt1": True, "columnsStartAt1": True,
                                       "pathFormat": "path", "supportsVariableType": True})
        launch = adapter.send("launch", {"name": "qualification", "type": "coreclr", "request": "launch",
                                        "program": args.dotnet, "args": [str(Path(args.dll).resolve())],
                                        "cwd": str(source.parent), "stopAtEntry": False, "justMyCode": False})
        adapter.event("initialized")
        binding = adapter.request("setBreakpoints", {"source": {"path": str(source)}, "breakpoints": [{"line": line}]})
        adapter.request("configurationDone")
        adapter.response(launch)
        stopped = adapter.event("stopped")
        if stopped.get("reason") != "breakpoint":
            raise AssertionError(f"Expected breakpoint, got {stopped}")
        thread = stopped["threadId"]
        stack = adapter.request("stackTrace", {"threadId": thread})["stackFrames"]
        frame = stack[0]
        if Path(frame["source"]["path"]).resolve() != source or frame["line"] != line:
            raise AssertionError(f"Unexpected breakpoint location: {frame}")
        values = {}
        for expression, expected in (("input.Id", "probe-001"), ("input.Body", "ping")):
            value = adapter.request("evaluate", {"expression": expression, "frameId": frame["id"], "context": "watch"})["result"]
            if json.loads(value) != expected:
                raise AssertionError(f"Unexpected inspected value: {expression}={value}")
            values[expression] = value
        adapter.request("next", {"threadId": thread})
        stepped = adapter.event("stopped")
        if stepped.get("reason") != "step":
            raise AssertionError(f"Expected step, got {stepped}")
        adapter.request("continue", {"threadId": stepped["threadId"]})
        exited = adapter.event("exited")
        adapter.event("terminated")
        if exited["exitCode"] != 0:
            raise AssertionError(f"Debuggee failed: {exited}")
        output = "".join(event.get("body", {}).get("output", "") for event in adapter.events
                         if event.get("event") == "output" and event.get("body", {}).get("category") == "stdout")
        reply = json.loads(output.strip())
        if reply != {"Id": "probe-001", "From": "fixture", "To": "windows", "Body": "ack:ping"}:
            raise AssertionError(f"Unexpected debuggee reply: {reply}")
        modules = [event.get("body", {}).get("module", {}) for event in adapter.events if event.get("event") == "module"]
        runtime = next(Path(module["path"]).resolve() for module in modules if module.get("name") == "System.Private.CoreLib.dll")
        if not runtime.is_relative_to(Path(args.dotnet).resolve().parent):
            raise AssertionError(f"Target loaded a runtime outside its selected SDK: {runtime}")
        result.update(Passed=True, Values=values, Stepped=True, Reply=reply, InitialBinding=binding, RuntimeModule=str(runtime))
    except Exception as error:
        result["Error"] = f"{type(error).__name__}: {error}"
    finally:
        adapter.close()
        result["DebuggerExitCode"] = adapter.process.returncode
    Path(args.result).write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["Passed"] else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    for option in ("debugger", "dotnet", "dll", "source", "transcript", "result"):
        parser.add_argument("--" + option, required=True)
    raise SystemExit(qualify(parser.parse_args()))

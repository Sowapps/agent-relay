"""Bounded JSON-RPC stdio transport; owns only the child it creates."""
import json
import queue
import subprocess
import threading
import time


class StdioRpc:
    def __init__(self, command, root, stderr, environment=None):
        self.process = subprocess.Popen(command, cwd=root, env=environment, stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=stderr, text=True, encoding='utf-8',
                                        creationflags=subprocess.CREATE_NO_WINDOW)
        self.incoming = queue.Queue()
        self.identifier = 0
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        try:
            for line in self.process.stdout:
                self.incoming.put(json.loads(line))
        except Exception as error:
            self.incoming.put(error)
        finally:
            self.incoming.put(None)

    def _write(self, value):
        self.process.stdin.write(json.dumps(value) + '\n')
        self.process.stdin.flush()

    def call(self, method, parameters=None, timeout=45):
        self.identifier += 1
        self._write({'id': self.identifier, 'method': method, 'params': parameters})
        deadline = time.monotonic() + timeout
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError('Native Codex request timed out: ' + method)
            response = self.incoming.get(timeout=remaining)
            if response is None or isinstance(response, Exception):
                raise RuntimeError('Native Codex exited or returned malformed output')
            if 'method' in response and 'id' in response:
                self._write({'id': response['id'], 'error': {'code': -32601, 'message': 'Diagnostics never approve server requests'}})
            elif response.get('id') == self.identifier:
                if 'error' in response:
                    raise RuntimeError(response['error'])
                return response['result']

    def close(self):
        if self.process.stdin and not self.process.stdin.closed:
            self.process.stdin.close()
        try:
            self.process.wait(timeout=8)
        except subprocess.TimeoutExpired:
            self.process.terminate()
            self.process.wait(timeout=5)

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()

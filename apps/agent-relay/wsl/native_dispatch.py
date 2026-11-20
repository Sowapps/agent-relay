"""Retain each terminal submission and the proof that can authorize the next one."""

import json
import os

from native_errors import NativeFailure


class NativeDispatch:
    def __init__(self, data, identifier, attempt=1):
        self.data = data
        self.identifier = identifier
        self.attempt = attempt
        self.history = data / 'dispatch-attempts' / identifier

    def marker(self, attempt):
        return (self.data / f'dispatch-{self.identifier}.json' if attempt == 1
                else self.history / f'{attempt}.json')

    def latest(self):
        return next((self.marker(number) for number in (3, 2, 1) if self.marker(number).is_file()), self.marker(1))

    def proof(self, attempt):
        return self.history / f'{attempt}.retry.json'

    def demand_unused(self):
        if self.marker(self.attempt).exists():
            raise NativeFailure('reply_unconfirmed', 'Prior dispatch intent exists. Inspect the original result; no replay was attempted.')

    def demand_previous_proof(self, request, observation):
        if self.attempt == 1:
            return
        try:
            previous = json.loads(self.marker(self.attempt - 1).read_text(encoding='utf-8'))
            proof = json.loads(self.proof(self.attempt - 1).read_text(encoding='utf-8'))
            current = (observation.path.name, observation.identity.st_dev, observation.identity.st_ino, observation.offset)
            retained = (proof['log'], proof['device'], proof['inode'], proof['endOffset'])
            if previous['request'] == request and current == retained:
                return
        except (AttributeError, KeyError, OSError, ValueError):
            pass
        raise NativeFailure('native_retry_unverified', 'The previous turn could not be verified for another submission. Inspect the original request.')

    def record_intent(self, request, prompt):
        self._write(self.marker(self.attempt), {'request': request, 'prompt': prompt})

    def record_retry_proof(self, proof):
        self._write(self.proof(self.attempt), proof)

    @staticmethod
    def _write(path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open('x', encoding='utf-8') as stream:
            json.dump(value, stream)
            stream.flush()
            os.fsync(stream.fileno())

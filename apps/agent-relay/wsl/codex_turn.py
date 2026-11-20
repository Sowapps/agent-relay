"""Observe a freshly submitted Codex turn through its owned conversation log."""

import json
import os
from pathlib import Path
import re
import stat
import uuid


def conversation_logs(folder):
    return (folder / 'config/sessions').rglob('*.jsonl')


class TurnObservation:
    MAX_RECORD = 256 * 1024
    MAX_POLL_BYTES = 1024 * 1024

    @classmethod
    def before_submit(cls, folder, conversation_id, prompt):
        if not conversation_id:
            return None
        try:
            suffix = str(uuid.UUID(conversation_id)) + '.jsonl'
            (path,) = (log for log in conversation_logs(folder) if log.name.endswith(suffix))
            identity = path.lstat()
            if stat.S_ISREG(identity.st_mode) and path.resolve().is_relative_to(folder.resolve()):
                return cls(path, identity, prompt)
        except (OSError, ValueError):
            pass  # Older or unavailable transcripts do not prevent normal reply handling.
        return None

    @classmethod
    def retained(cls, folder, conversation_id, prompt):
        observation = cls.before_submit(folder, conversation_id, prompt)
        if observation is not None:
            observation.offset = max(0, observation.offset - cls.MAX_POLL_BYTES)
        return observation

    def __init__(self, path: Path, identity, prompt: str):
        self.path = path
        self.identity = identity
        self.offset = identity.st_size
        self.prompt = prompt
        self.turn = None
        self.observing = True
        self.finished = False

    def failure(self):
        if not self.observing:
            return None
        try:
            with self.path.open('rb') as stream:
                current = os.fstat(stream.fileno())
                if (current.st_dev, current.st_ino) != (self.identity.st_dev, self.identity.st_ino) or current.st_size < self.offset:
                    self.observing = False
                    return None
                start = stream.seek(self.offset)
                while self.offset - start < self.MAX_POLL_BYTES:
                    line = stream.readline(self.MAX_RECORD + 1)
                    if len(line) > self.MAX_RECORD:
                        self.observing = False
                        return None
                    if not line.endswith(b'\n'):
                        break  # Revisit a partial record once Codex finishes writing it.
                    self.offset = stream.tell()
                    failure = self._read_record(line)
                    if failure or self.finished:
                        return failure
        except OSError:
            pass
        return None

    def _read_record(self, line):
        try:
            record = json.loads(line)
        except ValueError:
            return None
        event = record.get('payload') if isinstance(record, dict) and record.get('type') == 'event_msg' else None
        if not isinstance(event, dict):
            return None
        turn_id = event.get('turn_id')
        if not isinstance(turn_id, str) or not turn_id:
            return None
        if event.get('type') == 'item_completed':
            item = event.get('item')
            if isinstance(item, dict) and item.get('type') == 'UserMessage':
                content = item.get('content')
                if (isinstance(content, list) and len(content) == 1 and isinstance(content[0], dict) and
                        content[0].get('type') == 'text' and content[0].get('text') == self.prompt):
                    self.turn = turn_id
        if event.get('type') == 'task_complete' and turn_id == self.turn:
            self.observing = False
            self.finished = True
            return describe_failure(event.get('error'))
        return None


def describe_failure(error):
    if not isinstance(error, dict) or not error:
        return None
    info = error.get('codex_error_info')
    if info == 'server_overloaded':
        return 'server_overloaded', 'Codex reported that the selected model is at capacity for this request.'
    if isinstance(info, dict):
        attempts = info.get('response_too_many_failed_attempts')
        if isinstance(attempts, dict) and attempts.get('http_status_code') == 429:
            return 'native_provider_rate_limited', 'The service returned HTTP 429 after Codex exhausted its attempts.'
    category = info if isinstance(info, str) else next(iter(info), '') if isinstance(info, dict) and len(info) == 1 else ''
    detail = f' ({category})' if re.fullmatch(r'[a-z][a-z0-9_]{0,79}', category) else ''
    return 'native_turn_failed', f'Codex reported an error for this turn{detail}.'

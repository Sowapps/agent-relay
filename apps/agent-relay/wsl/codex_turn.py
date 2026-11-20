"""Observe a freshly submitted Codex turn through its owned conversation log."""

import json
import os
from pathlib import Path
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

    def __init__(self, path: Path, identity, prompt: str):
        self.path = path
        self.identity = identity
        self.offset = identity.st_size
        self.prompt = prompt
        self.turn = None
        self.observing = True

    def overloaded(self):
        if not self.observing:
            return False
        try:
            with self.path.open('rb') as stream:
                current = os.fstat(stream.fileno())
                if (current.st_dev, current.st_ino) != (self.identity.st_dev, self.identity.st_ino) or current.st_size < self.offset:
                    self.observing = False
                    return False
                start = stream.seek(self.offset)
                while self.offset - start < self.MAX_POLL_BYTES:
                    line = stream.readline(self.MAX_RECORD + 1)
                    if len(line) > self.MAX_RECORD:
                        self.observing = False
                        return False
                    if not line.endswith(b'\n'):
                        break  # Revisit a partial record once Codex finishes writing it.
                    self.offset = stream.tell()
                    if self._overloaded_record(line):
                        return True
        except OSError:
            pass
        return False

    def _overloaded_record(self, line):
        try:
            record = json.loads(line)
        except ValueError:
            return False
        event = record.get('payload') if isinstance(record, dict) and record.get('type') == 'event_msg' else None
        if not isinstance(event, dict):
            return False
        turn_id = event.get('turn_id')
        if not isinstance(turn_id, str) or not turn_id:
            return False
        if event.get('type') == 'item_completed':
            item = event.get('item')
            if isinstance(item, dict) and item.get('type') == 'UserMessage':
                content = item.get('content')
                if (isinstance(content, list) and len(content) == 1 and isinstance(content[0], dict) and
                        content[0].get('type') == 'text' and content[0].get('text') == self.prompt):
                    self.turn = turn_id
        if event.get('type') == 'task_complete' and turn_id == self.turn:
            self.observing = False
            error = event.get('error')
            return isinstance(error, dict) and error.get('codex_error_info') == 'server_overloaded'
        return False

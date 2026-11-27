"""Observe a freshly submitted Codex turn through its owned conversation log."""

from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
import json
import math
import os
from pathlib import Path
import re
import stat
import uuid

TEMPORARY_ERRORS = ('server_overloaded', 'native_provider_rate_limited')


def conversation_logs(folder):
    return (folder / 'config/sessions').rglob('*.jsonl')


def observed_activity(folder, conversation_id):
    """Read the latest complete turn boundary of the pinned conversation."""
    if not conversation_id:
        return 'unknown'
    try:
        suffix = str(uuid.UUID(conversation_id)) + '.jsonl'
        (log,) = (path for path in conversation_logs(folder) if path.name.endswith(suffix))
        if not stat.S_ISREG(log.lstat().st_mode) or not log.resolve().is_relative_to(folder.resolve()):
            return 'unknown'
        with log.open('rb') as stream:
            end = stream.seek(0, os.SEEK_END)
            stream.seek(max(0, end - 65536))
            if end > 65536:
                stream.readline()
            tail = stream.read(65536)
        if not tail.endswith(b'\n'):
            return 'unknown'
        for line in reversed(tail.splitlines()):
            entry = json.loads(line)
            event = entry.get('payload')
            if entry.get('type') != 'event_msg' or not isinstance(event, dict):
                continue
            if event.get('type') == 'task_started':
                return 'working'
            if event.get('type') == 'task_complete':
                return 'waiting_for_input'
    except (OSError, ValueError, TypeError, AttributeError):
        pass
    return 'unknown'


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
        self.started_turn = None
        self.actions_excluded = False
        self.observing = True
        self.finished = False
        self.retry_after_seconds = None
        self.retry_proof = None

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

    def at_live_end(self):
        try:
            current = self.path.stat()
        except OSError:
            return False
        return self.finished and (current.st_dev, current.st_ino, current.st_size) == (
            self.identity.st_dev, self.identity.st_ino, self.offset)

    def _read_record(self, line):
        try:
            record = json.loads(line)
        except ValueError:
            self.actions_excluded = False
            return None
        if not isinstance(record, dict) or not isinstance(payload := record.get('payload'), dict):
            self.actions_excluded = False
            return None
        kind = record.get('type')
        if kind != 'event_msg':
            if kind != 'turn_context' and not (kind == 'response_item' and payload.get('type') == 'message' and
                                               payload.get('role') in ('developer', 'system', 'user')):
                self.actions_excluded = False
            return None
        event = payload
        turn_id = event.get('turn_id')
        action = event.get('type')
        if action == 'task_started':
            first_start = self.started_turn != turn_id
            self.started_turn = turn_id
            self.actions_excluded = first_start and isinstance(turn_id, str) and bool(turn_id)
        elif (turn_id is not None and turn_id != self.started_turn or
              action not in ('item_completed', 'task_complete', 'token_count', 'world_state')):
            self.actions_excluded = False
        if action == 'item_completed':
            item = event.get('item')
            content = item.get('content') if isinstance(item, dict) and item.get('type') == 'UserMessage' else None
            if not (isinstance(content, list) and len(content) == 1 and isinstance(content[0], dict) and
                    content[0].get('type') == 'text' and content[0].get('text') == self.prompt):
                self.actions_excluded = False
            elif isinstance(turn_id, str) and turn_id:
                self.turn = turn_id
        if not isinstance(turn_id, str) or not turn_id:
            return None
        if action == 'task_complete' and turn_id == self.turn:
            self.observing = False
            self.finished = True
            error = event.get('error')
            failure = describe_failure(error)
            if (failure and failure[0] in TEMPORARY_ERRORS and self.started_turn == turn_id and
                    self.actions_excluded and not event.get('last_agent_message')):
                self.retry_after_seconds = retry_delay(error)
                self.retry_proof = {'turnId': turn_id, 'log': self.path.name, 'device': self.identity.st_dev,
                                    'inode': self.identity.st_ino, 'endOffset': self.offset}
            return failure
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


def retry_delay(error):
    info = error.get('codex_error_info')
    attempts = info.get('response_too_many_failed_attempts') if isinstance(info, dict) else None
    seconds = []
    for source in (error, attempts):
        if not isinstance(source, dict):
            continue
        values = [source.get('retry_after_seconds')]
        if isinstance(headers := source.get('headers'), dict):
            values.extend(value for name, value in headers.items() if name.lower() == 'retry-after')
        seconds.extend(delay for value in values if (delay := parse_delay(value)) is not None)
    return max(seconds, default=None)


def parse_delay(value):
    if type(value) not in (str, int):
        return None
    if re.fullmatch(r'[0-9]{1,10}', str(value)):
        return int(value)
    if isinstance(value, str):
        try:
            deadline = parsedate_to_datetime(value)
            if deadline.tzinfo is not None:
                return max(0, math.ceil((deadline - datetime.now(timezone.utc)).total_seconds()))
        except (TypeError, ValueError, OverflowError):
            pass
    return None

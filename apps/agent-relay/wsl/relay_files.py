"""Durable, run-owned relay files. Never resolves credential symlink contents."""
import json
import os
from pathlib import Path
import uuid
import sqlite3


def root_path(value):
    root = Path(value)
    if root.parent != Path('/srv') or not root.name.startswith('agent-relay-') or root.is_symlink():
        raise ValueError('Expected a dedicated /srv/agent-relay-* root')
    if root.resolve() != root or root.stat().st_uid != os.getuid():
        raise PermissionError('Relay root must belong to the current Linux user')
    return root


def commit(path, value):
    path = Path(path)
    temporary = path.with_name(path.name + '.tmp-' + uuid.uuid4().hex)
    with temporary.open('x', encoding='utf-8') as stream:
        json.dump(value, stream, ensure_ascii=False)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)
    descriptor = os.open(path.parent, os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def context(root, profile):
    path = root / profile / 'context.json'
    return json.loads(path.read_text(encoding='utf-8')) if path.exists() else {}


def workspace(root, profile):
    path = Path(context(root, profile).get('workspace', str(root / profile / 'workspace')))
    if not path.is_absolute() or path.resolve() != path or not path.is_dir():
        raise PermissionError('Configured workspace is missing or contains a symbolic link')
    return path


def message_database(root, profile):
    connection = sqlite3.connect(root / profile / 'data/messages.sqlite', timeout=5)
    connection.execute('PRAGMA journal_mode=WAL')
    connection.execute('PRAGMA synchronous=FULL')
    connection.execute('CREATE TABLE IF NOT EXISTS messages(sequence INTEGER PRIMARY KEY AUTOINCREMENT,id TEXT UNIQUE NOT NULL,reply_to TEXT,payload TEXT NOT NULL)')
    connection.execute('CREATE INDEX IF NOT EXISTS messages_reply ON messages(reply_to)')
    return connection


def store_message(root, profile, message):
    with message_database(root, profile) as connection:
        connection.execute('INSERT OR IGNORE INTO messages(id,reply_to,payload) VALUES(?,?,?)', (message['id'], message.get('inReplyTo'), json.dumps(message, ensure_ascii=False)))
    if not context(root, profile):
        commit(root / profile / 'data/outbox' / (message['id'] + '.json'), message)


def message_page(root, profile, after=0, limit=50):
    if not isinstance(after, int) or after < 0 or not isinstance(limit, int) or not 1 <= limit <= 100:
        raise ValueError('Expected nonnegative cursor and limit1..100')
    with message_database(root, profile) as connection:
        rows = connection.execute('SELECT sequence,payload FROM messages WHERE sequence>? ORDER BY sequence LIMIT ?', (after, limit+1)).fetchall()
    return {'messages': [json.loads(row[1]) for row in rows[:limit]], 'nextCursor': rows[min(limit,len(rows))-1][0] if rows else after, 'hasMore': len(rows)>limit}


def messages(root, profile):
    if context(root, profile):
        return message_page(root, profile)['messages']
    return [json.loads(path.read_text(encoding='utf-8')) for path in sorted((root / profile / 'data/outbox').glob('*.json'))[-200:]]


def find_reply(root, profile, identifier):
    if context(root, profile):
        with message_database(root, profile) as connection:
            row = connection.execute('SELECT payload FROM messages WHERE reply_to=? ORDER BY sequence LIMIT 1', (identifier,)).fetchone()
        return json.loads(row[0]) if row else None
    return next((message for message in messages(root, profile) if message.get('inReplyTo')==identifier), None)

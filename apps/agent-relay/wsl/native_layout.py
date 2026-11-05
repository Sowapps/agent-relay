"""Native layout facade; native-layout.json is the sole layout definition.

@see provision-wsl.py and native_clients.control: construct and close the same identity.
@see sync-session-adapters.py and native-storage.py: retain this definition with helpers.
"""
import base64
import json
from pathlib import Path, PurePosixPath
import re
import uuid


class NativeLayout:
    def __init__(self, definition):
        if definition['version'] != 1:
            raise ValueError('Unsupported native layout version')
        self.definition = definition
        self.base = definition['base']
        self.protected_root = definition['protectedRoot']
        self.sessions = self.base + '/' + definition['sessions'] + '/'
        self.profiles = self.base + '/' + definition['profiles'] + '/'
        self.legacy_prefix = definition['legacyPrefix']
        self.legacy_session_prefix = definition['legacySessionPrefix']

    def session(self, identifier, legacy=False):
        return (self.legacy_session_prefix if legacy else self.sessions) + uuid.UUID(str(identifier)).hex

    def profile(self, identifier):
        if not re.fullmatch('[a-zA-Z0-9-]+', identifier):
            raise ValueError('Invalid profile root identity')
        return self.profiles + identifier

    def valid(self, value):
        value = str(value)
        return bool(re.fullmatch(re.escape(self.sessions) + '[0-9a-f]{32}', value)
                    or re.fullmatch(re.escape(self.profiles) + '[a-zA-Z0-9-]+', value)
                    or re.fullmatch(re.escape(self.legacy_prefix) + '[a-zA-Z0-9-]+', value))

    def is_session(self, value, identifier=None):
        value = str(value)
        if identifier is not None:
            return value in (self.session(identifier), self.session(identifier, legacy=True))
        return any(re.fullmatch(re.escape(prefix) + '[0-9a-f]{32}', value)
                   for prefix in (self.sessions, self.legacy_session_prefix))

    def current(self, value):
        value = str(value)
        if not self.valid(value):
            raise ValueError('Unqualified native root')
        if value.startswith(self.base + '/'):
            return value
        if self.is_session(value):
            return self.session(value.removeprefix(self.legacy_session_prefix))
        return self.profile(PurePosixPath(value).name)


def load_layout():
    return NativeLayout(json.loads(Path(__file__).with_name('native-layout.json').read_text(encoding='utf-8')))


def payload_files(folder):
    """Package definition and adapters together for provisioning and maintenance."""
    return {path.name: base64.b64encode(path.read_bytes()).decode('ascii')
            for path in sorted(Path(folder).iterdir()) if path.suffix == '.py' or path.name == 'native-layout.json'}


def layout_from_payload(files):
    return NativeLayout(json.loads(base64.b64decode(files['native-layout.json'])))

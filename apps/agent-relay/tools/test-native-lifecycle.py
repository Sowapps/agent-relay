"""Model-free lifecycle guard regression; mocks tmux and ownership OS calls, never touches WSL."""
import json
from pathlib import Path, PurePosixPath
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'wsl'))
import native_clients


class LifecycleGuardTests(unittest.TestCase):
    def setUp(self):
        self.identifier = uuid.uuid4()
        self.owner = {'sessionId': str(self.identifier), 'user': 'relay-test'}

    def invoke(self, location, owner=None, symlink=False, uid=123, stop=True):
        root = PurePosixPath(location)
        marker = Mock()
        marker.is_symlink.return_value = symlink
        marker.stat.return_value.st_uid = uid
        marker.read_text.return_value = json.dumps(self.owner if owner is None else owner)
        validated = Mock()
        validated.__truediv__ = Mock(return_value=marker)
        validated.__str__ = Mock(return_value=str(root))
        with patch.object(native_clients, 'root_path', return_value=validated) as validate, \
             patch.object(native_clients.os, 'getuid', return_value=123, create=True), \
             patch.dict(sys.modules, {'pwd': SimpleNamespace(getpwuid=lambda _: SimpleNamespace(pw_name='relay-test'))}), \
             patch.object(native_clients, 'tmux') as tmux:
            tmux.return_value.returncode = 0
            try:
                result = native_clients.control(root, 'codex', stop)
                validate.assert_called_once_with(root)
                self.assertEqual(result['modelPromptSent'], False)
                self.assertEqual(tmux.call_args_list[-1].args[1], 'kill-session' if stop else 'send-keys')
            except (PermissionError, ValueError):
                tmux.assert_not_called()
                raise

    def test_owned_current_and_legacy_roots_stop_and_interrupt(self):
        for location in ['/srv/agent-relay/var/apps/agent-relay/sessions/' + self.identifier.hex,
                         '/srv/agent-relay-session-' + self.identifier.hex]:
            for stop in [True, False]:
                with self.subTest(location=location, stop=stop):
                    self.invoke(location, stop=stop)

    def test_profiles_workspace_and_other_identity_refused_before_tmux(self):
        for location in ['/srv/agent-relay/var/apps/agent-relay/profiles/' + self.identifier.hex,
                         '/home/relay-test/project',
                         '/srv/agent-relay/var/apps/agent-relay/sessions/' + uuid.uuid4().hex]:
            with self.subTest(location=location), self.assertRaises(PermissionError):
                self.invoke(location)

    def test_wrong_user_symlink_and_wrong_marker_owner_refused(self):
        location = '/srv/agent-relay/var/apps/agent-relay/sessions/' + self.identifier.hex
        for options in [dict(owner=dict(self.owner, user='other')), dict(symlink=True), dict(uid=999)]:
            with self.subTest(options=options), self.assertRaises(PermissionError):
                self.invoke(location, **options)

    def test_missing_session_identity_refused(self):
        with self.assertRaises(ValueError):
            self.invoke('/srv/agent-relay-session-' + self.identifier.hex, owner={'sessionId': None, 'user': 'relay-test'})


if __name__ == '__main__':
    unittest.main()

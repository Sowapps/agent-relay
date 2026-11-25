"""Model-free lifecycle guard regression; mocks tmux and ownership OS calls, never touches WSL."""
import json
from pathlib import Path, PurePosixPath
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'wsl'))
import native_clients
from codex_turn import describe_failure


class LifecycleGuardTests(unittest.TestCase):
    def test_native_failure_variants_are_typed_and_bounded(self):
        wrapped = {'codex_error_info': {'response_too_many_failed_attempts': {'http_status_code': 429}}}
        self.assertEqual(describe_failure(wrapped)[0], 'native_provider_rate_limited')
        wrapped['codex_error_info']['response_too_many_failed_attempts']['http_status_code'] = 500
        self.assertEqual(describe_failure(wrapped)[0], 'native_turn_failed')
        self.assertEqual(describe_failure({'codex_error_info': 'unfamiliar'})[1],
                         'Codex reported an error for this turn (unfamiliar).')
        self.assertIsNone(describe_failure(None))

    def test_correlated_overload_surfaces_before_timeout_without_redispatch(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            log = root / 'codex/config/sessions' / f'rollout-{self.identifier}.jsonl'
            log.parent.mkdir(parents=True)
            (root / 'codex/data').mkdir()
            overload = {'type': 'task_complete', 'error': {'codex_error_info': 'server_overloaded'}}

            def event(turn, **payload):
                return json.dumps({'type': 'event_msg', 'payload': {'turn_id': turn, **payload}}) + '\n'

            log.write_text(event('old', **overload), encoding='utf8')

            def publish(_, __, ___, prompt, ____):
                accepted = {'type': 'UserMessage', 'content': [{'type': 'text', 'text': prompt, 'text_elements': []}]}
                with log.open('a', encoding='utf8') as stream:
                    stream.write(event('unrelated', type='item_completed', item={'type': 'UserMessage',
                        'content': [{'type': 'text', 'text': 'other prompt'}]}))
                    stream.write(event('unrelated', **overload))
                    stream.write(event('current', type='item_completed', item=accepted))
                    stream.write(event('different-turn', **overload))
                    stream.write(event('current', **overload).rstrip('\n'))

            def finish_record(_):
                with log.open('a', encoding='utf8') as stream:
                    stream.write('\n')

            observed = {'available': True, 'mcpInitialized': True, 'pane': '', 'paneId': '%0',
                        'nativeConversationId': str(self.identifier)}
            request = {'id': str(self.identifier), 'text': 'Fixture'}
            with patch.object(native_clients, 'status', return_value=observed), \
                 patch.object(native_clients, 'find_reply', return_value=None), \
                 patch.object(native_clients, 'submit_prompt', side_effect=publish) as submit, \
                 patch.object(native_clients.time, 'sleep', side_effect=finish_record) as sleep:
                with self.assertRaises(native_clients.NativeFailure) as caught:
                    native_clients.send(root, 'codex', request)
                self.assertEqual(caught.exception.code, 'server_overloaded')
                self.assertTrue(caught.exception.effects_possible)
                self.assertTrue(caught.exception.turn_ended)
                sleep.assert_called_once_with(.5)
                with self.assertRaises(native_clients.NativeFailure) as repeated:
                    native_clients.send(root, 'codex', request)
                self.assertEqual(repeated.exception.code, 'reply_unconfirmed')
                submit.assert_called_once()
            (root / 'codex/native-conversation.json').write_text(json.dumps({'id': str(self.identifier)}), encoding='utf8')
            with patch.object(native_clients, 'status', return_value=observed):
                self.assertEqual(native_clients.inspect_turn(root, 'codex', str(self.identifier)),
                                 {'turnEnded': True, 'sessionReady': False})
            self.assertEqual(native_clients.inspect_turn(root, 'codex', str(uuid.uuid4())), {'turnEnded': False})

    def test_finished_approval_requires_same_ready_generation_and_no_later_turn(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            folder = root / 'codex'
            log = folder / 'config/sessions' / f'rollout-{self.identifier}.jsonl'
            log.parent.mkdir(parents=True)
            (folder / 'data').mkdir()
            (folder / 'native-conversation.json').write_text(json.dumps({'id': str(self.identifier)}))
            request = {'id': str(self.identifier), 'expectedNativeGeneration': 'native', 'expectedDistributionGeneration': 'wsl'}
            (folder / 'data' / f'dispatch-{self.identifier}.json').write_text(json.dumps({'request': request, 'prompt': 'reviewed'}))
            def event(kind, **payload):
                return json.dumps({'type': 'event_msg', 'payload': {'type': kind, 'turn_id': 'original', **payload}}) + '\n'
            log.write_text(event('item_completed', item={'type': 'UserMessage', 'content': [{'type': 'text', 'text': 'reviewed'}]}) +
                           event('task_complete'))
            observed = {'nativeConversationId': str(self.identifier), 'available': True, 'mcpInitialized': True,
                        'authenticated': True, 'awaitingApproval': False, 'nativeRuntime': {'generation': 'native'},
                        'distributionRuntime': {'generation': 'wsl'}}
            with patch.object(native_clients, 'status', return_value=observed):
                inspect = lambda: native_clients.inspect_turn(root, 'codex', str(self.identifier))
                self.assertEqual(inspect(), {'turnEnded': True, 'sessionReady': True})
                observed['awaitingApproval'] = True
                self.assertFalse(inspect()['sessionReady'])
                observed['awaitingApproval'] = False
                marker = folder / 'data' / f'approval-stop-{self.identifier}.json'
                marker.write_text(json.dumps({'requestId': str(self.identifier), 'stopStarted': True}))
                self.assertFalse(inspect()['sessionReady'])
                marker.unlink()
                with log.open('a') as stream:
                    stream.write(event('task_started'))
                self.assertEqual(inspect(), {'turnEnded': True, 'sessionReady': False})

    def test_long_codex_prompt_is_pasted_then_submitted_once(self):
        root = PurePosixPath('/owned/fixture')
        prompt = 'Échange de qualification. ' * 90
        buffer = 'relay-' + str(self.identifier)
        with patch.object(native_clients, 'tmux') as tmux:
            native_clients.submit_prompt(root, 'codex', str(self.identifier), prompt)
        self.assertEqual([entry.args[1] for entry in tmux.call_args_list], ['load-buffer', 'paste-buffer', 'send-keys'])
        self.assertEqual(tmux.call_args_list[0].kwargs['input_text'], prompt)
        self.assertIn(buffer, tmux.call_args_list[1].args)
        self.assertEqual(tmux.call_args_list[-1].args[-1], 'Enter')

    def setUp(self):
        self.identifier = uuid.uuid4()
        self.owner = {'sessionId': str(self.identifier), 'user': 'relay-test'}

    def test_offline_and_changed_generation_never_write_dispatch_intent(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'codex/data').mkdir(parents=True)
            request = {'id': str(self.identifier), 'text': 'Fixture', 'expectedNativeGeneration': 'old'}
            for available, expected in [(False, 'session_not_ready'), (True, 'session_generation_changed')]:
                observed = {'available': available, 'mcpInitialized': available, 'pane': '', 'nativeRuntime': {'generation': 'different'}}
                with self.subTest(available=available), patch.object(native_clients, 'find_reply', return_value=None), \
                     patch.object(native_clients, 'status', return_value=observed), patch.object(native_clients, 'tmux') as tmux:
                    with self.assertRaises(native_clients.NativeFailure) as caught:
                        native_clients.send(root, 'codex', request)
                    self.assertEqual(caught.exception.code, expected)
                    self.assertFalse(caught.exception.effects_possible)
                    self.assertFalse(list((root / 'codex/data').iterdir()))
                    tmux.assert_not_called()

    def test_automatic_restore_refuses_missing_pinned_history_before_start(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'codex').mkdir()
            (root / 'codex/native-conversation.json').write_text(json.dumps({'id': str(self.identifier)}), encoding='utf-8')
            with patch.object(native_clients, 'workspace', return_value=root), \
                 patch.object(native_clients, 'context', return_value=self.owner), patch.object(native_clients, 'tmux') as tmux:
                with self.assertRaises(native_clients.NativeFailure) as caught:
                    native_clients.start(root, 'codex', resume=True, require_pinned=True)
                self.assertEqual(caught.exception.code, 'context_lost')
                self.assertFalse(caught.exception.effects_possible)
                tmux.assert_not_called()

    def test_prior_intent_remains_uncertain_even_if_native_is_offline(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            data = root / 'codex/data'
            data.mkdir(parents=True)
            (data / f'dispatch-{self.identifier}.json').write_text('{}', encoding='utf-8')
            with patch.object(native_clients, 'find_reply', return_value=None), patch.object(native_clients, 'status') as status:
                with self.assertRaises(native_clients.NativeFailure) as caught:
                    native_clients.send(root, 'codex', {'id': str(self.identifier), 'text': 'Fixture'})
                self.assertEqual(caught.exception.code, 'reply_unconfirmed')
                self.assertTrue(caught.exception.effects_possible)
                status.assert_not_called()

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

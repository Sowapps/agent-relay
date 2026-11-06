"""Portable layout/error checks plus exact host-identity tests; no agents, model or WSL."""
import ast
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'wsl'))
from native_layout import load_layout, payload_files, layout_from_payload
from native_errors import NativeFailure, envelope
import host_identity


class DesignContractTests(unittest.TestCase):
    def test_changed_native_scripts_and_embedded_programs_parse(self):
        application = Path(__file__).resolve().parents[1]
        for path in (application / 'wsl').glob('*.py'):
            ast.parse(path.read_text(encoding='utf-8-sig'), filename=str(path))
        for name in ['provision-wsl.py', 'sync-session-adapters.py', 'sync-wsl.py', 'native-storage.py', 'migrate-storage.py']:
            tree = ast.parse((application / 'tools' / name).read_text(encoding='utf-8-sig'))
            for item in ast.walk(tree):
                if isinstance(item, ast.Assign) and isinstance(item.value, ast.Constant) and isinstance(item.value.value, str):
                    if any(isinstance(target, ast.Name) and target.id in ['setup', 'script', 'code'] for target in item.targets):
                        ast.parse(item.value.value, filename=name + ':embedded')

    def test_shared_layout_corpus_and_package_roundtrip(self):
        application = Path(__file__).resolve().parents[1]
        corpus = json.loads((application / 'tests/AgentRelay.Tests/Fixtures/native-layout.json').read_text(encoding='utf-8'))
        for layout in [load_layout(), layout_from_payload(payload_files(application / 'wsl'))]:
            self.assertEqual(layout.session(corpus['id']), corpus['session'])
            self.assertEqual(layout.session(corpus['id'], True), corpus['legacySession'])
            for key in ['session', 'legacySession']:
                self.assertTrue(layout.is_session(corpus[key], corpus['id']))
            for value in corpus['valid']:
                self.assertTrue(layout.valid(value))
                self.assertFalse(layout.is_session(value))
            for value in corpus['invalid']:
                self.assertFalse(layout.valid(value))
            self.assertEqual(layout.current(corpus['legacySession']), corpus['session'])

    def test_typed_errors_do_not_infer_no_effects_from_exception_class(self):
        self.assertEqual(envelope(NativeFailure('capability_denied', 'Guard', False))['effectsPossible'], False)
        self.assertTrue(envelope(PermissionError('Guard'))['effectsPossible'])
        self.assertEqual(envelope(RuntimeError('approval_required: native approval'))['code'], 'approval_required')
        self.assertIsNone(envelope(RuntimeError('approval_required: native approval'))['effectsPossible'])
        self.assertEqual(envelope(RuntimeError('prose mentions unavailable: elsewhere'))['code'], 'native_error')

    def test_stale_pid_unique_owner_wrong_scope_prefix_and_ambiguity(self):
        executable = str(Path('local/design-fixture/AgentRelay.Windows.exe').resolve())
        data = str(Path('local/design-fixture/data with spaces').resolve())
        runtime = {'executable': executable, 'pid': 1}
        process = {'ProcessId': 7, 'ExecutablePath': executable, 'CommandLine': 'exact'}
        with patch.object(host_identity, 'command_parts', return_value=[executable, 'tray', '--data-dir', data]):
            self.assertEqual(host_identity.resolve_host(runtime, data, [process])['pid'], 7)
            self.assertIsNone(host_identity.resolve_host(runtime, data + '-other', [process]))
            self.assertIsNone(host_identity.resolve_host(runtime, data, [dict(process, ExecutablePath=executable + '.other')]))
            with self.assertRaises(RuntimeError):
                host_identity.resolve_host(runtime, data, [process, dict(process, ProcessId=8)])
        with patch.object(host_identity, 'command_parts', return_value=[executable, 'mcp-worker', '--data-dir', data]):
            self.assertIsNone(host_identity.resolve_host(runtime, data, [process]))

    @unittest.skipUnless(os.name == 'nt', 'Windows process argument parsing')
    def test_quoted_absolute_owner_and_relative_scope(self):
        relative_data = Path('local/design fixture/data')
        data = relative_data.resolve()
        executable = str(data.parent / 'AgentRelay.Windows.exe')
        runtime = {'executable': executable, 'pid': 1}
        process = {'ProcessId': 7, 'ExecutablePath': executable}
        process['CommandLine'] = subprocess.list2cmdline([executable, 'tray', '--data-dir', str(data)])
        self.assertEqual(host_identity.resolve_host(runtime, data, [process])['pid'], 7)
        process['CommandLine'] = subprocess.list2cmdline([executable, 'tray', '--data-dir', str(relative_data)])
        self.assertIsNone(host_identity.resolve_host(runtime, data, [process]))


if __name__ == '__main__':
    unittest.main()

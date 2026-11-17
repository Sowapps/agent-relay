"""Check release evidence and report recovery with small synthetic local fixtures."""

import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import complete_update
import host_identity
from types import SimpleNamespace

from delivery_reports import REPORT_FILE, latest_reports
from release_artifact import archive_path, extract_archive, sha256, verified_archive, validate_version, verification_directory, EXTRACTED_DIRECTORY, VERIFICATION_FILE


class DeliveryEvidenceTests(unittest.TestCase):
    def test_initial_host_proof_rechecks_exact_scope_after_reading_start_time(self):
        observed = SimpleNamespace(returncode=0, stdout='2026-01-01T00:00:00.0000000Z')
        runtime = {'executable': 'fixture.exe'}
        with patch.object(host_identity, 'resolve_host', return_value={'pid': 123}), \
             patch.object(host_identity, 'identity', return_value=True) as recheck, \
             patch.object(host_identity.subprocess, 'run', return_value=observed), \
             patch.object(host_identity, 'runtime_command', return_value=['fixture.exe']):
            proof = host_identity.legacy_host(runtime, self.root)
            self.assertEqual(proof['processId'], 123)
            self.assertEqual(proof['startedAt'], observed.stdout)
            recheck.assert_called_once_with({'pid': 123}, self.root)
        with patch.object(host_identity, 'resolve_host', return_value={'pid': 123}), \
             patch.object(host_identity, 'identity', return_value=False), \
             patch.object(host_identity.subprocess, 'run', return_value=observed):
            with self.assertRaisesRegex(RuntimeError, 'no process was stopped'):
                host_identity.legacy_host(runtime, self.root)

    def test_source_promotion_reuses_matching_qualification_without_running_it_again(self):
        receipt = {'status': 'passed', 'commit': 'fixture-commit',
                   'candidate': {'version': '1.2.0', 'archive': str(self.root / 'candidate.zip')}}
        with patch.object(complete_update.subprocess, 'check_output', side_effect=['fixture-commit', '', 'fixture-commit', '']), \
             patch.object(complete_update.subprocess, 'run') as qualify, \
             patch.object(complete_update, 'latest_reports', return_value={'latest': [receipt]}), \
             patch.object(complete_update.ET, 'parse') as version, \
             patch.object(complete_update, 'qualified_archive', return_value=(self.root / 'candidate.zip', 'fixture-hash')), \
             patch.object(complete_update, 'verified_archive'):
            version.return_value.findtext.return_value = '1.2.0'
            self.assertEqual(complete_update.source_candidate(), (self.root, '1.2.0'))
            qualify.assert_not_called()

    def test_update_command_waits_on_retained_transaction_without_reapplying(self):
        pending = {'state': {'schema': 2, 'phase': 'awaiting_work'}}
        complete = {'state': {'schema': 2, 'phase': 'complete'}}
        with patch.object(complete_update, 'update', return_value=complete) as resume, patch.object(complete_update.time, 'sleep'):
            self.assertEqual(complete_update.finish_command(self.root, pending), complete)
            resume.assert_called_once_with(self.root, action='resume')
        with patch.object(complete_update, 'update') as resume:
            with self.assertRaisesRegex(RuntimeError, 'update_uncertain_work'):
                complete_update.finish_command(self.root, {'state': {'schema': 2, 'phase': 'awaiting_work', 'errorCode': 'update_uncertain_work'}})
            resume.assert_not_called()

    def test_default_update_summary_omits_full_transaction_payload(self):
        result = complete_update.summary({'state': {'schema': 2, 'phase': 'complete', 'files': ['fixture'] * 10000,
                                                  'activation': {'previousPointer': 'private'}, 'clients': [1, 2]}, 'receipt': 'retained'})
        self.assertNotIn('files', result['state'])
        self.assertNotIn('activation', result['state'])
        self.assertEqual(result['state']['clientCount'], 2)
        self.assertLess(len(json.dumps(result)), 500)

    def test_interrupted_verification_preserves_failed_output_and_retries(self):
        extracted = self.root / EXTRACTED_DIRECTORY
        extracted.mkdir()
        (extracted / 'failure.txt').write_text('retained failure', encoding='utf-8')
        selected = verification_directory(self.root)
        self.assertEqual(selected, extracted)
        self.assertFalse(selected.exists())
        retained = list(self.root.glob(EXTRACTED_DIRECTORY + '.failed-*'))
        self.assertEqual(len(retained), 1)
        self.assertEqual((retained[0] / 'failure.txt').read_text(encoding='utf-8'), 'retained failure')
        selected.mkdir()
        (self.root / VERIFICATION_FILE).write_text('{}', encoding='utf-8')
        with self.assertRaises(ValueError):
            verification_directory(self.root)
        self.assertTrue(selected.exists())

    def test_packaging_and_deployment_share_stable_rc_grammar(self):
        for version in ['1.0.0', '1.2.3-rc.1']:
            self.assertEqual(validate_version(version), version)
            self.assertIn(version, archive_path(Path('.'), version).name)
        for version in ['1.0.0-beta.1', '1.0.0-rc.0', '../1.0.0', '１.０.０', None]:
            with self.subTest(version=version), self.assertRaises(ValueError):
                validate_version(version)

    def setUp(self):
        fixture_root = Path(__file__).resolve().parents[3] / 'local/apps/agent-relay/tests'
        fixture_root.mkdir(parents=True, exist_ok=True)
        fixture = tempfile.TemporaryDirectory(prefix='delivery-evidence-', dir=fixture_root)
        self.addCleanup(fixture.cleanup)
        self.root = Path(fixture.name)

    def archive(self, names):
        archive = archive_path(self.root, '1.2.3-rc.1')
        with zipfile.ZipFile(archive, 'w') as output:
            for name in names:
                output.writestr(name, b'fixture')
        return archive

    def report(self, folder, action, started, **fields):
        receipt = self.root / action / folder / REPORT_FILE
        receipt.parent.mkdir(parents=True)
        receipt.write_text(json.dumps({'action': action, 'status': 'passed', 'startedUtc': started, **fields}),
                           encoding='utf-8')
        return receipt

    def test_exact_checksum_and_owned_extraction(self):
        archive = self.archive(('app/tool.txt', 'README.md'))
        digest = sha256(archive)
        extracted = self.root / 'extracted'
        with verified_archive(archive, digest) as source:
            extract_archive(source, extracted, prefix='app/')
        self.assertEqual([file.name for file in extracted.iterdir()], ['tool.txt'])
        self.assertEqual((extracted / 'tool.txt').read_bytes(), b'fixture')
        with archive.open('ab') as file:
            file.write(b'changed')
        with self.assertRaisesRegex(ValueError, 'checksum'):
            with verified_archive(archive, digest):
                self.fail('Changed archive was opened')

    def test_windows_alias_or_traversal_is_refused_before_extraction(self):
        cases = (
            ('app/valid.txt', 'app/../escape.txt'),
            ('app/valid.txt', 'app/..\\escape.txt'),
            ('app/tool.txt', 'app/TOOL.txt'),
            ('app/valid.txt', 'app/tool.txt:stream'),
        )
        for names in cases:
            with self.subTest(names=names):
                archive = self.archive(names)
                extracted = self.root / 'extracted'
                with verified_archive(archive, sha256(archive)) as source:
                    with self.assertRaisesRegex(ValueError, 'archive path'):
                        extract_archive(source, extracted, prefix='app/')
                self.assertFalse(extracted.exists())

    def test_latest_report_uses_start_time_and_surfaces_unreadable_report(self):
        recent = self.report('a-recent', 'qualify', '2026-11-06T10:00:00+01:00', seconds=12.5, commit='candidate')
        self.report('z-older', 'qualify', '2026-11-06T08:55:00+00:00', seconds=500)
        damaged = self.report('invalid', 'qualify', '2026-11-06T09:10:00+00:00')
        damaged.write_text('{', encoding='utf-8')
        status = latest_reports(self.root, ('qualify', 'build'))
        self.assertEqual([item['report'] for item in status['latest']], [str(recent)])
        self.assertEqual(status['latest'][0]['commit'], 'candidate')
        self.assertEqual([item['report'] for item in status['unreadable']], [str(damaged)])


if __name__ == '__main__':
    unittest.main()

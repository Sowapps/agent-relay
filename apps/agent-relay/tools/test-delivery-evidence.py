"""Check release evidence and report recovery with small synthetic local fixtures."""

import json
from pathlib import Path
import tempfile
import unittest
import zipfile

from delivery_reports import REPORT_FILE, latest_reports
from release_artifact import archive_path, extract_archive, sha256, verified_archive, validate_version


class DeliveryEvidenceTests(unittest.TestCase):
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

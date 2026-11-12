"""Identity and safe extraction of the portable Windows release.

@see package-release.py, verify-release.py and isolated-delivery.py for its producers and consumers.
"""

import argparse
from contextlib import contextmanager
import hashlib
import json
import re
from pathlib import Path, PureWindowsPath
import shutil
import stat
import zipfile


VERIFICATION_FILE = 'verification.json'
EXTRACTED_DIRECTORY = 'extracted verification'
WINDOWS_REPARSE_POINT = getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0x400)
MANIFEST_FILE = 'release-manifest.json'


def ordinary_directory(path):
    """Check existing ancestors before writing a new child. Junctions are links too."""
    for directory in (*reversed(path.parents), path):
        try:
            status = directory.lstat()
        except FileNotFoundError:
            continue
        if not stat.S_ISDIR(status.st_mode) or getattr(status, 'st_file_attributes', 0) & WINDOWS_REPARSE_POINT:
            raise ValueError(f'Expected ordinary directory: {directory}')
    return path


def ordinary_file(path):
    ordinary_directory(path.parent)
    status = path.lstat()
    if not stat.S_ISREG(status.st_mode) or getattr(status, 'st_file_attributes', 0) & WINDOWS_REPARSE_POINT:
        raise ValueError(f'Expected ordinary file: {path}')
    return path


def validate_version(version):
    """Own the stable/RC version grammar used by packaging and local deployment."""
    if not isinstance(version, str) or not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+(?:-rc\.[1-9][0-9]*)?', version):
        raise ValueError('Expected a stable or RC release version')
    return version


def archive_path(directory, version):
    validate_version(version)
    return directory / f'agent-relay-{version}-win-x64.zip'


def sha256(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def read_json(path):
    with ordinary_file(path).open(encoding='utf-8-sig') as source:
        return json.load(source)


def declared_checksum(archive):
    checksums = ordinary_file(archive.parent / 'SHA256SUMS.txt').read_text(encoding='utf-8').split()
    if len(checksums) != 2 or checksums[1] != archive.name or not re.fullmatch('[0-9a-f]{64}', checksums[0]):
        raise ValueError('Checksum file must identify the selected archive')
    return checksums[0]


@contextmanager
def verified_archive(path, expected_digest):
    """Hash and consume the same open archive so a replaced path cannot select another ZIP."""
    with ordinary_file(path).open('rb') as source:
        digest = hashlib.file_digest(source, 'sha256').hexdigest()
        if digest != expected_digest:
            raise ValueError('Candidate archive checksum does not match the qualification evidence')
        source.seek(0)
        with zipfile.ZipFile(source) as archive:
            yield archive


def validate_windows_name(name):
    parts = name.removesuffix('/').split('/')
    if any(not part or part in {'.', '..'} or part.endswith(('.', ' '))
           or re.search(r'[<>:"\\|?*\x00-\x1f]', part) or PureWindowsPath(part).is_reserved()
           for part in parts):
        raise ValueError(f'Invalid Windows archive path: {name!r}')


def extract_archive(archive, destination, *, prefix=''):
    """Validate all names before writing. A prefix selects members and is stripped."""
    ordinary_directory(destination)
    members = {}
    for item in archive.infolist():
        name = item.orig_filename
        validate_windows_name(name)
        key = name.removesuffix('/').casefold()
        if key in members:
            raise ValueError(f'Duplicate Windows archive path: {name!r}')
        members[key] = item

    destination.mkdir()
    for item in members.values():
        if item.is_dir() or not item.filename.startswith(prefix):
            continue
        target = destination / item.filename.removeprefix(prefix)
        target.parent.mkdir(parents=True, exist_ok=True)
        with archive.open(item) as source, target.open('xb') as output:
            shutil.copyfileobj(source, output)


def publication_evidence(directory, version, root):
    archive = archive_path(directory, version)
    verification = read_json(directory / VERIFICATION_FILE)
    if verification.get('passed') is not True or verification.get('version') != version:
        raise ValueError('Exact archive is not verified for this version')
    digest = verification['archiveSha256']
    if declared_checksum(archive) != digest:
        raise ValueError('Checksum file does not match archive verification')
    visual_path = directory / 'visual-verification.json'
    if not visual_path.exists():
        raise ValueError('Record actual extracted application visual verification before publication')
    visual = read_json(visual_path)
    if visual.get('passed') is not True or visual.get('archiveSha256') != digest:
        raise ValueError('Visual verification does not match the archive')
    with verified_archive(archive, digest) as source:
        # Read provenance from the bytes selected for release, never the separate build output.
        with source.open(MANIFEST_FILE) as manifest_file:
            manifest = json.load(manifest_file)
    if manifest.get('version') != version:
        raise ValueError('Package version differs from the source version')
    inputs = manifest.get('sourceInputs')
    if not isinstance(inputs, dict) or not inputs:
        raise ValueError('Archive manifest does not identify any source inputs')
    for name, expected in inputs.items():
        validate_windows_name(name)
        if sha256(ordinary_file(root / name)) != expected:
            raise ValueError(f'Source changed after packaging: {name}')
    return {'archive': str(archive), 'sha256': digest}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publication-evidence', required=True, type=Path)
    parser.add_argument('--version', required=True)
    parser.add_argument('--source', required=True, type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(publication_evidence(args.publication_evidence, args.version, args.source)))
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as error:
        parser.exit(1, f'{error}\n')


if __name__ == '__main__':
    main()

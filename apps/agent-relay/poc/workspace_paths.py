"""Locate the development repository independently of this application's depth."""
from pathlib import Path


def repository_root(source):
    for parent in Path(source).resolve().parents:
        if (parent / 'workspace.psd1').is_file():
            return parent
    raise RuntimeError('Repository marker workspace.psd1 was not found')

#!/usr/bin/env python3
"""Protect the legacy payload during deployment."""
from pathlib import Path
import sys

repo = Path(__file__).resolve().parents[1]
target = Path(sys.argv[1])

def files(root):
    if not root.is_dir():
        return {}
    return {
        p.relative_to(root): p.read_bytes()
        for p in root.rglob('*')
        if p.is_file() and p.name != '.DS_Store' and not p.name.startswith('._')
        and 'BridgeTools' not in p.relative_to(root).parts
    }

for name in ('1.1', '1.2', '1.3', '1.4', '1.5', '1.6', 'Features', 'Languages', 'Patches', 'Textures'):
    installed = target / name
    if installed.is_symlink() or (installed.exists() and files(repo / name) != files(installed)):
        sys.exit(f'Installed frozen payload differs: {installed}')

#!/usr/bin/env python3
"""Reproduce upstream add-on copies using pinned URLs and verified SHA-256 hashes."""
import hashlib
import json
from pathlib import Path
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parent.parent


def main():
    sources = json.loads((ROOT / 'tools/addon-sources.json').read_text())
    for name, source in sources.items():
        with urlopen(source['url']) as response:
            data = response.read()
        if hashlib.sha256(data).hexdigest() != source['sha256']:
            raise SystemExit(f'{name}: upstream SHA-256 mismatch')
        (ROOT / source['path']).write_bytes(data)
        print(f'{name} {source["version"]}: SHA-256 verified')


if __name__ == '__main__':
    main()

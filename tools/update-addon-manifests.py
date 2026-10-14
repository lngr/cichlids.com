#!/usr/bin/env python3
"""Reproduce upstream add-on copies using pinned URLs and verified SHA-256 hashes."""
import base64
import yaml
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
        if hashlib.sha256(data).hexdigest() != source.get('source_sha256', source['sha256']):
            raise SystemExit(f'{name}: upstream SHA-256 mismatch')
        if name == 'barman-cloud':
            docs = list(yaml.safe_load_all(data))
            for doc in docs:
                if doc['kind'] == 'Secret':
                    doc['kind'] = 'ConfigMap'
                    doc.pop('type')
                    doc['data'] = {k: base64.b64decode(v).decode() for k, v in doc['data'].items()}
                if doc['kind'] == 'Deployment':
                    for container in doc['spec']['template']['spec']['containers']:
                        for entry in container.get('env', []):
                            if entry['name'] == 'SIDECAR_IMAGE':
                                entry['valueFrom']['configMapKeyRef'] = entry['valueFrom'].pop('secretKeyRef')
            data = yaml.safe_dump_all(docs, sort_keys=False).encode()
        if hashlib.sha256(data).hexdigest() != source['sha256']:
            raise SystemExit(f'{name}: vendored SHA-256 mismatch')
        (ROOT / source['path']).write_bytes(data)
        print(f'{name} {source["version"]}: SHA-256 verified')


if __name__ == '__main__':
    main()

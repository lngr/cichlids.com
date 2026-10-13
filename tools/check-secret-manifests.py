#!/usr/bin/env python3
"""Reject plaintext Kubernetes Secret payloads in tracked YAML and JSON files."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import sys

import yaml

CIPHER = re.compile(r'ENC\[AES256_GCM,data:[A-Za-z0-9+/=]*,iv:[A-Za-z0-9+/=]+,'
                    r'tag:[A-Za-z0-9+/=]+,type:str\]')


class UniqueLoader(yaml.SafeLoader):
    pass


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate mapping key')
        result[key] = value
    return result


def unique_mapping(loader, node, deep=False):
    loader.flatten_mapping(node)
    result = {}
    for key_node, value_node in node.value:
        key = loader.construct_object(key_node, deep=deep)
        if key in result:
            raise ValueError('Duplicate mapping key')
        result[key] = loader.construct_object(value_node, deep=deep)
    return result


UniqueLoader.add_constructor(yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, unique_mapping)


def encrypted(value):
    return isinstance(value, str) and CIPHER.fullmatch(value) is not None


def secrets(document):
    if not isinstance(document, dict):
        return
    if document.get('kind') == 'Secret':
        yield document
    if document.get('kind') in ('List', 'SecretList'):
        for item in document.get('items', []):
            yield from secrets(item)


def check(document):
    for secret in secrets(document):
        for field in ('data', 'stringData'):
            payload = secret.get(field)
            if payload is None or payload == {}:
                continue
            metadata = secret.get('sops')
            if (not isinstance(payload, dict) or not isinstance(metadata, dict)
                    or not encrypted(metadata.get('mac'))
                    or not all(encrypted(value) for value in payload.values())):
                return False
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parent.parent)
    args = parser.parse_args()
    paths = subprocess.check_output(['git', '-C', str(args.root), 'ls-files', '-z']).decode().split('\0')
    failures = []
    count = 0
    for name in paths:
        if not name or Path(name).suffix.lower() not in ('.yaml', '.yml', '.json'):
            continue
        count += 1
        try:
            source = (args.root / name).read_text(encoding='utf-8-sig')
            documents = ([json.loads(source, object_pairs_hook=unique_pairs)]
                         if Path(name).suffix.lower() == '.json'
                         else list(yaml.load_all(source, Loader=UniqueLoader)))
            if not all(check(document) for document in documents):
                failures.append(f'{name}: plaintext Kubernetes Secret payload')
        except (OSError, ValueError, TypeError, yaml.YAMLError):
            # Parser errors can include secret values; report only the path.
            failures.append(f'{name}: invalid manifest (cannot verify Secret payloads)')
    if failures:
        print('Secret manifest guard FAILED:', file=sys.stderr)
        for failure in failures:
            print(f'  {failure}', file=sys.stderr)
        return 1
    print(f'Secret manifest guard OK ({count} tracked YAML/JSON files)')
    return 0


if __name__ == '__main__':
    sys.exit(main())

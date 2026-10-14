#!/usr/bin/env python3
"""Validate immutable image delivery and the TASK-2.11 required gate."""
import argparse
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parent.parent
IMAGES = ('api', 'migration', 'web')
REQUIRED = ('adr-index', 'story-test-binding', 'toolchain', 'infra-validate', 'infra-policy',
            'secrets-scan', 'manifests', 'backend', 'client-core', 'e2e')


def validate(digests):
    if set(digests) != set(IMAGES):
        raise ValueError('Require API, migration and web digests')
    for name, digest in digests.items():
        if not isinstance(digest, str) or not re.fullmatch(r'sha256:[0-9a-f]{64}', digest) or digest == 'sha256:' + '0' * 64:
            raise ValueError(f'{name}: require a published SHA256 digest')


def bump(root, environment, digests):
    validate(digests)
    if environment not in ('staging', 'prod'):
        raise ValueError('Delivery environment must be staging or prod')
    for service, names in [('api', ('api', 'migration')), ('web', ('web',))]:
        overlay = root / f'apps/{service}/overlays/{environment}'
        subprocess.run(['kustomize', 'edit', 'set', 'image',
                        *(f'ghcr.io/lngr/cichlids-{name}=ghcr.io/lngr/cichlids-{name}@{digests[name]}' for name in names)],
                       cwd=overlay, check=True)


def preflight(root, environment):
    spec = importlib.util.spec_from_file_location('preflight', ROOT / 'tools/check-app-images.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.check(root, environment)


def resolve(sha):
    if not re.fullmatch(r'[0-9a-f]{40}', sha):
        raise ValueError('Require a full commit SHA')
    digests = {}
    for name in IMAGES:
        value = subprocess.check_output(['docker', 'buildx', 'imagetools', 'inspect',
                                         f'ghcr.io/lngr/cichlids-{name}:{sha}',
                                         '--format', '{{json .Manifest.Digest}}'], text=True)
        digests[name] = json.loads(value)
    validate(digests)
    return digests


def promote(root, tag):
    if not re.fullmatch(r'v[0-9A-Za-z][0-9A-Za-z._-]*', tag):
        raise ValueError('Release tag must start with v')
    try:
        sha = subprocess.check_output(['git', 'rev-parse', '--verify', f'refs/tags/{tag}^{{commit}}'],
                                      cwd=root, text=True, stderr=subprocess.PIPE).strip()
        subprocess.run(['git', 'merge-base', '--is-ancestor', sha, 'main'], cwd=root, check=True)
    except subprocess.CalledProcessError as error:
        raise ValueError('Release commit must exist on main') from error
    digests = resolve(sha)
    bump(root, 'prod', digests)
    preflight(root, 'prod')
    return sha


def required(results):
    missing = set(REQUIRED) - set(results)
    failed = [name for name, job in results.items() if job.get('result') != 'success']
    if missing or failed:
        raise ValueError(f'Required jobs missing={sorted(missing)} unsuccessful={sorted(failed)}')
    print(f'required: {len(results)} jobs successful')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    update = sub.add_parser('bump')
    update.add_argument('environment', choices=['staging', 'prod'])
    update.add_argument('digests', type=Path)
    release = sub.add_parser('promote')
    release.add_argument('tag')
    gate = sub.add_parser('required')
    gate.add_argument('results')
    args = parser.parse_args()
    if args.command == 'required':
        required(json.loads(args.results))
    elif args.command == 'promote':
        print(f'Promote {promote(ROOT, args.tag)}')
    else:
        bump(ROOT, args.environment, json.loads(args.digests.read_text()))
        preflight(ROOT, args.environment)


if __name__ == '__main__':
    main()

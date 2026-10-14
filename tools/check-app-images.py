#!/usr/bin/env python3
"""Reject unconfigured application image digests before deployment."""
import argparse
from pathlib import Path
import re
import subprocess
import yaml

ROOT = Path(__file__).resolve().parent.parent


def check(root, environment):
    for service in ['api', 'web']:
        output = subprocess.check_output(['kustomize', 'build', f'apps/{service}/overlays/{environment}'],
                                         cwd=root, text=True)
        for doc in yaml.safe_load_all(output):
            if doc['kind'] not in ['Deployment', 'Job']:
                continue
            for container in doc['spec']['template']['spec']['containers']:
                image = container['image']
                if not re.search(r'@sha256:[0-9a-f]{64}$', image) or image.endswith('sha256:' + '0' * 64):
                    raise SystemExit(f'FAIL: {service}: set a published image digest before deployment')
    config = yaml.safe_load((root / f'apps/api/overlays/{environment}/settings.yaml').read_text())
    if any('REPLACE_WITH_' in v for v in config['data'].values()):
        raise SystemExit('FAIL: configure the environment media bucket before deployment')
    print(f'{environment}: application images and media configuration ready for operator review')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('environment', choices=['staging', 'prod'])
    args = parser.parse_args()
    check(ROOT, args.environment)


if __name__ == '__main__':
    main()

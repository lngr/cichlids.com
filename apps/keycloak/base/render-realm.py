#!/usr/bin/env python3
"""Combine a reviewed realm with mounted secrets, rejecting unresolved values."""
import argparse
import copy
import json
from pathlib import Path
import re
import sys

UNRESOLVED = re.compile(r'not-configured|\$\{|\$\(')


def value(settings, key):
    result = settings.get(key)
    if not isinstance(result, str) or not result.strip() or UNRESOLVED.search(result):
        raise ValueError('Missing or unresolved secret setting')
    return result


def render(source, smtp, providers, bff=None):
    realm = copy.deepcopy(source)
    def guard(node):
        if isinstance(node, dict):
            if {'registrationAllowed', 'users', 'credentials', 'federatedIdentities'} & node.keys():
                raise ValueError('Unsafe realm source')
            for child in node.values():
                guard(child)
        elif isinstance(node, list):
            for child in node:
                guard(child)
    guard(realm)
    realm['smtpServer'] = {key: value(smtp, key) for key in ['host', 'port', 'user', 'password', 'from']}
    if not realm['smtpServer']['port'].isdigit():
        raise ValueError('Invalid SMTP port')
    realm['smtpServer'].update(auth='true', starttls='true', ssl='false')
    for provider in realm.get('identityProviders', []):
        alias = provider['alias']
        keys = [f'{alias}-client-id', f'{alias}-client-secret']
        if any(key in providers for key in keys):
            provider['config'].update(clientId=value(providers, keys[0]), clientSecret=value(providers, keys[1]))
        else:
            if provider.get('enabled'):
                raise ValueError('Enabled provider requires credentials')
            provider['config'].pop('clientId', None)
            provider['config'].pop('clientSecret', None)
    if bff:
        client = next(c for c in realm['clients'] if c['clientId'] == 'cichlids-web-bff')
        client['secret'] = value(bff, 'client-secret')
    for provider in realm.get('identityProviders', []):
        if UNRESOLVED.search(json.dumps(provider)):
            raise ValueError('Unresolved provider setting')
    return realm


def settings(path):
    return {p.name: p.read_text() for p in path.iterdir() if p.is_file() and not p.name.startswith('.')}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path('/realm/cichlids-realm.json'))
    parser.add_argument('--smtp', type=Path, default=Path('/smtp'))
    parser.add_argument('--providers', type=Path, default=Path('/providers'))
    parser.add_argument('--bff', type=Path, default=Path('/bff'))
    parser.add_argument('--output', type=Path, default=Path('/rendered/cichlids-realm.json'))
    args = parser.parse_args()
    try:
        realm = render(json.loads(args.source.read_text()), settings(args.smtp), settings(args.providers), settings(args.bff))
        args.output.write_text(json.dumps(realm))
        args.output.chmod(0o600)
    except (ValueError, OSError, KeyError, StopIteration):
        print('Realm rendering failed: check source and mounted secret keys', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())

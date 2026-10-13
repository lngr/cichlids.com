#!/usr/bin/env python3
"""Round-trip the dedicated test-only age fixture for TASK-2.6."""
import base64
import os
import re
from pathlib import Path
import subprocess
import tempfile

import yaml

ROOT = Path(__file__).resolve().parent.parent
FIXTURE = ROOT / 'tools/fixtures/secrets/secret.sops.yaml'
KEY = ROOT / 'tools/fixtures/secrets/age-test-key.txt'


def run(*args, env=None):
    return subprocess.check_output(args, cwd=ROOT, env=env, stderr=subprocess.PIPE, text=True)


def validate(plaintext):
    secret = yaml.safe_load(plaintext)
    assert secret['apiVersion'] == 'v1' and secret['kind'] == 'Secret'
    assert secret['metadata'] == {'name': 'ci-test-only', 'namespace': 'default'}
    assert secret['type'] == 'Opaque'
    assert base64.b64decode(secret['data']['username'], validate=True) == b'ci-test-only'
    assert secret['stringData'] == {'password': 'public-test-value'}
    assert 'sops' not in secret
    return secret


def main():
    env = {k: v for k, v in os.environ.items() if not k.startswith('SOPS_')}
    with tempfile.TemporaryDirectory() as directory:
        env.update(SOPS_AGE_KEY_FILE=str(KEY), XDG_CONFIG_HOME=directory, GNUPGHOME=directory)
        plaintext = run('sops', '--decrypt', str(FIXTURE), env=env)
        expected = validate(plaintext)
        assert validate(run('kustomize', 'build', '--enable-alpha-plugins', '--enable-exec',
                            str(FIXTURE.parent), env=env)) == expected
        plain = Path(directory) / 'plain.yaml'
        plain.write_text(plaintext)
        encrypted = run('sops', '--encrypt', '--filename-override', str(FIXTURE),
                        str(plain), env=env)
        cipher = Path(directory) / 'secret.sops.yaml'
        cipher.write_text(encrypted)
        assert validate(run('sops', '--decrypt', str(cipher), env=env)) == expected
        public = run('age-keygen', '-y', str(KEY)).strip()
        rules = yaml.safe_load((ROOT / '.sops.yaml').read_text())['creation_rules']
        for path in ('clusters/cichlids/production.sops.yaml',
                     'apps/example/overlays/staging/credentials.sops.yaml',
                     'infra/credentials.sops.yaml'):
            for rule in rules:
                if re.search(rule.get('path_regex', ''), path):
                    assert public not in rule.get('age', ''), 'Test recipient matches a production path'
        if subprocess.run(['sops', '--decrypt', str(FIXTURE)], env={**env, 'SOPS_AGE_KEY_FILE':
                          str(Path(directory) / 'missing-key.txt')}, capture_output=True).returncode == 0:
            raise AssertionError('Decryption succeeds without the test key')
        missing_env = {**env, 'SOPS_AGE_KEY_FILE': str(Path(directory) / 'missing-key.txt')}
        missing = subprocess.run(['kustomize', 'build', '--enable-alpha-plugins', '--enable-exec',
                                  str(FIXTURE.parent)], env=missing_env, capture_output=True)
        assert missing.returncode != 0, 'KSOPS renders successfully without the test key'
    print('SOPS age round-trip and KSOPS render OK (test-only fixture; missing key rejected)')


if __name__ == '__main__':
    main()

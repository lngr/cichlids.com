#!/usr/bin/env python3
"""Round-trip the dedicated test-only age fixture for TASK-2.6."""
import base64
import os
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
        prod = subprocess.run(['sops', '--encrypt', '--filename-override',
                               'clusters/cichlids/production.sops.yaml', str(plain)],
                              env=env, capture_output=True)
        assert prod.returncode != 0, 'Test recipient matches a production path'
        if subprocess.run(['sops', '--decrypt', str(FIXTURE)], env={**env, 'SOPS_AGE_KEY_FILE':
                          str(Path(directory) / 'missing-key.txt')}, capture_output=True).returncode == 0:
            raise AssertionError('Decryption succeeds without the test key')
    print('SOPS age round-trip and KSOPS render OK (test-only fixture; missing key rejected)')


if __name__ == '__main__':
    main()

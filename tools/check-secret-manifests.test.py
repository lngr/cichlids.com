#!/usr/bin/env python3
"""Exercise the tracked Kubernetes Secret guard for TASK-2.6."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

GUARD = Path(__file__).with_name('check-secret-manifests.py').resolve()
CIPHER = 'ENC[AES256_GCM,data:dGVzdA==,iv:dGVzdA==,tag:dGVzdA==,type:str]'


class SecretGuardTest(unittest.TestCase):
    def scan(self, content, filename='nested/plaintext-secret.yaml', tracked=True):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            subprocess.run(['git', 'init', '-q', directory], check=True)
            path = root / filename
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content)
            if tracked:
                subprocess.run(['git', '-C', directory, 'add', filename], check=True)
            return subprocess.run(['python3', str(GUARD), '--root', directory],
                                  text=True, capture_output=True)

    def assert_rejected(self, content, filename='nested/plaintext-secret.yaml'):
        result = self.scan(content, filename)
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertIn(filename, result.stderr)
        self.assertNotIn('fixture-value', result.stderr)

    def test_plaintext_secret_is_named(self):
        self.assert_rejected('kind: Secret\nstringData:\n  password: fixture-value\n')

    def test_base64_is_plaintext(self):
        self.assert_rejected('kind: Secret\ndata:\n  password: Zml4dHVyZQ==\n')

    def test_multi_document_and_list(self):
        self.assert_rejected('kind: ConfigMap\n---\nkind: List\nitems:\n'
                             '  - kind: Secret\n    stringData: {password: fixture-value}\n')

    def test_json_secret(self):
        self.assert_rejected(json.dumps({'kind': 'Secret', 'data': {'password': 'abc'}}),
                             'elsewhere/secret.json')

    def test_sops_metadata_does_not_allow_plaintext(self):
        self.assert_rejected('kind: Secret\nsops: {mac: fake}\n'
                             'stringData: {password: fixture-value}\n')

    def test_mixed_ciphertext_and_plaintext(self):
        self.assert_rejected(f'kind: Secret\nsops: {{mac: "{CIPHER}"}}\n'
                             f'data: {{encrypted: "{CIPHER}", password: fixture-value}}\n')

    def test_ciphertext_requires_metadata(self):
        self.assert_rejected(f'kind: Secret\ndata: {{password: "{CIPHER}"}}\n')

    def test_encrypted_and_empty_secrets(self):
        encrypted = f'kind: Secret\nsops: {{mac: "{CIPHER}"}}\ndata: {{password: "{CIPHER}"}}\n'
        for content in (encrypted, 'kind: Secret\n', 'kind: Secret\ndata: {}\n'):
            result = self.scan(content)
            self.assertEqual(result.returncode, 0, result.stderr)

    def test_untracked_files_are_outside_guard(self):
        result = self.scan('kind: Secret\nstringData: {password: fixture-value}\n', tracked=False)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_duplicate_keys_fail_closed(self):
        self.assert_rejected('kind: Secret\nkind: ConfigMap\n')
        self.assert_rejected('{"kind": "Secret", "kind": "ConfigMap"}', 'secret.json')

    def test_invalid_yaml_fails_closed(self):
        self.assert_rejected('kind: Secret\nstringData: [broken\n')


if __name__ == '__main__':
    unittest.main()

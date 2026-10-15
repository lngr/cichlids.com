#!/usr/bin/env python3
"""Exercise optional enrollment, route reconciliation and safe key handling offline."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('install-tailscale.sh')


class Enrollment(unittest.TestCase):
    def test_empty_key_enrollment_and_idempotence(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            binary = root / 'bin'
            binary.mkdir()
            key = root / 'auth'
            key.write_text('')
            log = root / 'calls'
            status = root / 'status'
            status.write_text('NeedsLogin')
            for name, body in {
                'tailscale': f'''case "$1" in
version) echo 1.104.1;;
status) echo '{{"BackendState":"'$(cat {status})'"}}';;
debug) echo '{{"ControlURL":"https://headscale.cichlids.com"}}';;
*) echo "$*" >> {log}; echo Running > {status};;
esac''',
                'systemctl': 'exit 0',
                'sysctl': 'exit 0',
            }.items():
                p = binary / name
                p.write_text('#!/bin/sh\n' + body)
                p.chmod(0o755)
            script = root / 'install.sh'
            script.write_text(SCRIPT.read_text().replace('/etc/systemd/system/tailscaled.service', str(root / 'tailscaled.service')).replace('/etc/sysctl.d/99-cichlids-tailnet.conf', str(root / 'sysctl.conf')).replace('/var/lib/tailscale', str(root / 'state')))
            env = {**os.environ, 'PATH': str(binary) + ':' + os.environ['PATH']}

            def run(ok=True):
                result = subprocess.run(['bash', str(script), '203.0.113.7', str(key)], env=env, capture_output=True, text=True)
                self.assertEqual(result.returncode == 0, ok, result.stderr)
                self.assertNotIn('synthetic-test-key', result.stdout + result.stderr)

            run()
            self.assertFalse(log.exists())
            self.assertFalse(key.exists(), 'temporary credential is removed even when empty')
            key.write_text('synthetic-test-key')
            run()
            calls = log.read_text()
            self.assertIn('up --login-server=https://headscale.cichlids.com', calls)
            self.assertIn('--advertise-routes=203.0.113.7/32', calls)
            self.assertIn('--snat-subnet-routes=false', calls)
            self.assertIn('--ssh=false', calls)
            self.assertNotIn('synthetic-test-key', calls)
            self.assertFalse(key.exists())
            log.write_text('')
            key.write_text('synthetic-test-key')
            run()
            self.assertTrue(log.read_text().startswith('set '))
            self.assertNotIn('auth-key', log.read_text())
            self.assertFalse(key.exists())
            key.write_text('')
            log.write_text('')
            run()
            self.assertTrue(log.read_text().startswith('set '))
            key.write_text('synthetic-test-key')
            result = subprocess.run(['bash', str(script), 'bad-ip', str(key)], env=env, capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse(key.exists())

    def test_checksum_failure_prevents_install_and_removes_credential(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            binary = root / 'bin'
            binary.mkdir()
            key = root / 'auth'
            key.write_text('synthetic-test-key')
            for name, body in {
                'tailscale': 'echo wrong-version',
                'curl': 'while [ "$1" != -o ]; do shift; done; shift; echo corrupt > "$1"',
                'install': 'echo unexpected-install >&2; exit 99',
            }.items():
                path = binary / name
                path.write_text('#!/bin/sh\n' + body)
                path.chmod(0o755)
            env = {**os.environ, 'PATH': str(binary) + ':' + os.environ['PATH']}
            result = subprocess.run(['bash', str(SCRIPT), '203.0.113.7', str(key)], env=env, capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertNotIn('unexpected-install', result.stderr)
            self.assertNotIn('synthetic-test-key', result.stdout + result.stderr)
            self.assertFalse(key.exists())

    def test_iac_sensitive_input_and_bootstrap_triggers(self):
        app = SCRIPT.parent.parent
        variables = (app / 'variables.tf').read_text().split('variable "tailscale_auth_key"')[1]
        self.assertIn('sensitive   = true', variables)
        self.assertIn('default     = ""', variables)
        main = (app / 'main.tf').read_text()
        self.assertIn('filesha256("${path.module}/main.tf")', main)
        self.assertIn('install -d -m 700 -o 65532 -g 65532 /var/lib/cichlids-headscale', main)
        self.assertIn('cichlids.com/headscale-state=true', main)
        tailnet = (app / 'tailnet.tf').read_text()
        self.assertIn('filesha256("${path.module}/scripts/install-tailscale.sh")', tailnet)
        self.assertIn('sha256(var.tailscale_auth_key)', tailnet)
        self.assertIn('content     = var.tailscale_auth_key', tailnet)
        self.assertNotIn("'${var.tailscale_auth_key}'", tailnet)


if __name__ == '__main__':
    unittest.main()

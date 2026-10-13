#!/usr/bin/env python3
"""Exercise k3s bootstrap configuration and service reconciliation offline."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('install-k3s.sh')


class Bootstrap(unittest.TestCase):
    def test_configuration_version_and_idempotence(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            binary = root / 'bin'
            binary.mkdir()
            log = root / 'calls'
            config = root / 'etc/config.yaml'
            version = root / 'version'
            service = root / 'k3s.service'
            service.write_text('ExecStart=/usr/local/bin/k3s server --disable traefik\n')
            version.write_text('v1.36.5+k3s1')
            mocks = {
                'k3s': f'cat {version}',
                'kubectl': 'echo node Ready',
                'systemctl': f'echo systemctl "$*" >> {log}',
                'curl': f'''cat <<'INSTALL'
echo installer "$INSTALL_K3S_EXEC" "$INSTALL_K3S_SKIP_START" >> {log}
echo "$INSTALL_K3S_VERSION" > {version}
echo 'ExecStart=/usr/local/bin/k3s server' > {service}
INSTALL''',
            }
            for name, body in mocks.items():
                path = binary / name
                path.write_text('#!/bin/sh\n' + body + '\n')
                path.chmod(0o755)
            script = root / 'install.sh'
            script.write_text(SCRIPT.read_text().replace('/etc/rancher/k3s/config.yaml', str(config))
                              .replace('/etc/systemd/system/k3s.service', str(service))
                              .replace('/usr/local/bin/kubectl', str(binary / 'kubectl'))
                              .replace('/etc/rancher/k3s/k3s.yaml', str(root / 'kubeconfig')))
            env = {**os.environ, 'PATH': str(binary) + ':' + os.environ['PATH']}

            def run():
                result = subprocess.run(['bash', str(script), 'v1.36.5+k3s1'], env=env,
                                        capture_output=True, text=True)
                self.assertEqual(result.returncode, 0, result.stderr)

            run()
            self.assertTrue(config.exists(), 'desired k3s config exists on a pinned node')
            self.assertEqual(config.read_text(), 'write-kubeconfig-mode: "0600"\n')
            self.assertEqual(config.stat().st_mode & 0o777, 0o600)
            self.assertIn('installer server true', log.read_text())
            self.assertIn('systemctl restart k3s', log.read_text())
            self.assertNotIn('--disable', service.read_text())
            log.write_text('')
            run()
            self.assertEqual(log.read_text(), '', 'identical config and version require no restart')
            config.write_text('write-kubeconfig-mode: "0644"\n')
            run()
            self.assertIn('systemctl restart k3s', log.read_text())
            log.write_text('')
            version.write_text('v1.35.0+k3s1')
            run()
            self.assertIn('installer server true', log.read_text())
            self.assertIn('systemctl restart k3s', log.read_text())
            log.write_text('')
            service.write_text('ExecStart=/usr/local/bin/k3s server --disable traefik\n')
            run()
            self.assertIn('installer server true', log.read_text())
            self.assertNotIn('--disable', service.read_text())


if __name__ == '__main__':
    unittest.main()

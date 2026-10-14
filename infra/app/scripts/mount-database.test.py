#!/usr/bin/env python3
"""Given an attached volume, verify safe mounting and repeated bootstrap offline."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('mount-database.sh')


class MountDatabase(unittest.TestCase):
    def test_mount_safety_and_idempotence(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            binary = root / 'bin'
            binary.mkdir()
            device = root / 'scsi-0HC_Volume_123'
            device.touch()
            target = root / 'database'
            fstab = root / 'fstab'
            fstab.write_text('# operator mounts\n')
            mounted = root / 'mounted'
            log = root / 'calls'
            mocks = {
                'blkid': 'echo "${FS_TYPE-ext4}"',
                'findmnt': f'''[ -f {mounted} ] || exit 1
case "$*" in
  *SOURCE*) echo "${{SOURCE-{device}}}" ;;
  *FSTYPE*) echo "${{MOUNT_TYPE-ext4}}" ;;
esac''',
                'mount': f'echo mount >> {log}\n[ "${{FAIL_MOUNT-false}}" != true ] || exit 1\ntouch {mounted}',
                'systemctl': f'echo "$*" >> {log}',
                'mkfs': 'exit 99',
            }
            for name, body in mocks.items():
                path = binary / name
                path.write_text('#!/bin/sh\n' + body + '\n')
                path.chmod(0o755)
            source = SCRIPT.read_text()
            self.assertNotIn('mkfs', source)
            source = source.replace('/dev/disk/by-id/scsi-0HC_Volume_', str(root / 'scsi-0HC_Volume_'))
            source = source.replace('/var/lib/cichlids-db', str(target)).replace('/etc/fstab', str(fstab))
            source = source.replace('/etc/systemd/system/k3s.service.d', str(root / 'dropin'))
            source = source.replace('/etc/rancher/k3s/config.yaml.restart-required', str(root / 'pending'))
            source = source.replace('[[ -b "$DEVICE" ]]', '[[ -e "$DEVICE" ]]')
            script = root / 'mount.sh'
            script.write_text(source)
            env = {**os.environ, 'PATH': str(binary) + ':' + os.environ['PATH']}

            def run(extra=None, success=True, volume='123'):
                result = subprocess.run(['bash', str(script), volume], env={**env, **(extra or {})},
                                        capture_output=True, text=True)
                self.assertEqual(result.returncode == 0, success, result.stderr)

            for filesystem in ['', 'xfs']:
                run({'FS_TYPE': filesystem}, success=False)
                self.assertFalse(target.exists())
                self.assertEqual(fstab.read_text(), '# operator mounts\n')
            run(volume='123;echo unsafe', success=False)
            device.unlink()
            run(success=False)
            device.touch()
            run({'FAIL_MOUNT': 'true'}, success=False)
            self.assertFalse((target / 'postgres').exists())
            run()
            self.assertTrue((target / 'postgres').is_dir())
            self.assertIn('ext4 defaults,nofail 0 2', fstab.read_text())
            self.assertIn('RequiresMountsFor=', (root / 'dropin/database.conf').read_text())
            self.assertIn('ExecStartPre=/bin/bash /opt/cichlids-bootstrap/mount-database.sh 123',
                          (root / 'dropin/database.conf').read_text())
            self.assertIn(r'BindsTo=var-lib-cichlids\x2ddb.mount',
                          (root / 'dropin/database.conf').read_text())
            log.write_text('')
            run()
            self.assertEqual(log.read_text(), '')
            self.assertEqual(fstab.read_text().count(str(device)), 1)
            for extra in [{'SOURCE': '/dev/other'}, {'MOUNT_TYPE': 'xfs'}]:
                run(extra, success=False)
            fstab.write_text(f'/dev/other {target} ext4 defaults 0 2\n')
            run(success=False)
            self.assertEqual(fstab.read_text(), f'/dev/other {target} ext4 defaults 0 2\n')


if __name__ == '__main__':
    unittest.main()

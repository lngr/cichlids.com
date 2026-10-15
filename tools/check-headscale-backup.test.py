#!/usr/bin/env python3
"""Check consistent SQLite snapshots and private state archive retention."""
import importlib.util
from pathlib import Path
import sqlite3
import tarfile
import tempfile
import unittest
import sys
sys.dont_write_bytecode = True

PATH = Path('apps/headscale/overlays/cluster/backup.py')


class Backup(unittest.TestCase):
    def test_wal_snapshot_keys_permissions_and_retention(self):
        spec = importlib.util.spec_from_file_location('backup', PATH)
        backup = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(backup)
        with tempfile.TemporaryDirectory() as directory:
            state = Path(directory)
            for name in ['config.yaml', 'noise_private.key', 'derp_server_private.key']:
                (state / name).write_text(name)
            with sqlite3.connect(state / 'db.sqlite') as db:
                db.execute('pragma journal_mode=wal')
                db.execute('create table nodes (name text)')
                db.execute("insert into nodes values ('operator')")
                db.commit()
                for n in range(9):
                    archive = backup.snapshot(state, f'20261015T{n:06d}')
                self.assertEqual(len(list((state / 'backups').glob('*.tar.gz'))), 7)
                self.assertEqual(archive.stat().st_mode & 0o777, 0o600)
                with tarfile.open(archive) as tar:
                    self.assertEqual(set(tar.getnames()), {'config.yaml', 'noise_private.key', 'derp_server_private.key', 'db.sqlite'})
                    target = state / 'restored'
                    tar.extractall(target, filter='data')
                with sqlite3.connect(target / 'db.sqlite') as restored:
                    self.assertEqual(restored.execute('select * from nodes').fetchall(), [('operator',)])
                self.assertFalse(list((state / 'backups').glob('*.tmp')))


if __name__ == '__main__':
    unittest.main()

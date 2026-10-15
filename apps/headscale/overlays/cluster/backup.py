"""Archive an online SQLite snapshot and coordination keys on the state volume."""
from datetime import datetime, timezone
import os
from pathlib import Path
import sqlite3
import tarfile
import tempfile
import time


def snapshot(state, stamp):
    os.umask(0o077)
    backups = state / 'backups'
    backups.mkdir(mode=0o700, exist_ok=True)
    archive = backups / f'headscale-{stamp}.tar.gz'
    pending = archive.with_suffix('.tmp')
    with tempfile.TemporaryDirectory(dir=backups) as directory:
        database = Path(directory) / 'db.sqlite'
        with sqlite3.connect(f'file:{state}/db.sqlite?mode=ro', uri=True) as source:
            with sqlite3.connect(database) as target:
                source.backup(target)
                if target.execute('pragma integrity_check').fetchone() != ('ok',):
                    raise RuntimeError('SQLite snapshot integrity check failed')
        with tarfile.open(pending, 'w:gz') as bundle:
            bundle.add(database, arcname='db.sqlite')
            for name in ['config.yaml', 'noise_private.key', 'derp_server_private.key']:
                bundle.add(state / name, arcname=name)
        os.replace(pending, archive)
    for old in sorted(backups.glob('headscale-*.tar.gz'))[:-7]:
        old.unlink()
    return archive


if __name__ == '__main__':
    while True:
        try:
            archive = snapshot(Path('/var/lib/headscale'), datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S'))
            print(f'Snapshot ready: {archive.name}', flush=True)
            time.sleep(21600)
        except Exception as error:
            print(f'Snapshot failed: {type(error).__name__}', flush=True)
            time.sleep(60)

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import time

import psycopg
from psycopg import sql

from snapshot import counts, pg_env, sha256, validate_manifest


def check_environment(environment):
    if environment not in ('staging', 'cichlids-staging') and not re.fullmatch(r'pr-[1-9][0-9]*', environment):
        raise ValueError('staging or preview namespace required')


def check_version(version):
    if not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}', version):
        raise ValueError('invalid version')


def require_complete_media(manifest):
    if not manifest.get('media_complete') or not manifest.get('media'):
        raise ValueError('complete published media required')
    if sorted(m['key'] for m in manifest['media']) != manifest['media_keys']:
        raise ValueError('media manifest mismatch')


def restore_database(dsn, dump, manifest, environment, refresh=False):
    check_environment(environment)
    if refresh and environment not in ('staging', 'cichlids-staging'):
        raise ValueError('refresh is staging-only')
    validate_manifest(manifest, manifest['version'])
    require_complete_media(manifest)
    if sha256(dump) != manifest['dump_sha256']:
        raise ValueError('dump checksum mismatch')
    marker = f'golden:{manifest["version"]}:{manifest["dump_sha256"]}'
    with psycopg.connect(dsn, autocommit=True) as conn:
        conn.execute('select pg_advisory_lock(217, 1)')
        old = conn.execute("select shobj_description(oid, 'pg_database') from pg_database where datname=current_database()").fetchone()[0]
        if old == marker and not refresh:
            print('RESTORE: already_initialized=1')
            return
        populated = conn.execute("select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname not in ('pg_catalog','information_schema') and n.nspname not like 'pg_toast%' and c.relkind in ('r','p')").fetchone()[0]
        if populated and not refresh:
            raise ValueError('target populated; staging refresh requires explicit operator action')
        command = ['pg_restore', '--dbname', conn.info.dbname, '--no-owner', '--no-acl', '--no-comments', '--exit-on-error', '--single-transaction']
        if refresh:
            command += ['--clean', '--if-exists']
        with Path(dump).open('rb') as stream:
            result = subprocess.run(command, stdin=stream, env=pg_env(conn), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if result.returncode:
            raise RuntimeError('restore failed')
        migration = conn.execute('select migration_id from "__EFMigrationsHistory" order by migration_id desc limit 1').fetchone()[0]
        if counts(conn) != manifest['row_counts'] or migration != manifest['migration_id']:
            raise ValueError('restored data mismatch')
        conn.execute(sql.SQL('comment on database {} is {}').format(sql.Identifier(conn.info.dbname), sql.Literal(marker)))
        print(f'RESTORE: tables={len(manifest["row_counts"])} verified=1')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--refresh', action='store_true')
    args = parser.parse_args()
    os.umask(0o077)
    try:
        import boto3
        environment = os.environ['GOLDEN_ENVIRONMENT']
        version = os.environ['GOLDEN_VERSION']
        check_environment(environment)
        check_version(version)
        bucket = os.environ['GOLDEN_BUCKET']
        client = boto3.client('s3', endpoint_url=os.environ['GOLDEN_S3_ENDPOINT'])
        with tempfile.TemporaryDirectory(prefix='golden-restore-') as directory:
            directory = Path(directory)
            manifest_file = directory / 'manifest.json'
            client.download_file(bucket, f'golden/{version}/manifest.json', str(manifest_file))
            manifest = json.loads(manifest_file.read_text())
            validate_manifest(manifest, version)
            require_complete_media(manifest)
            dump = directory / 'snapshot.dump'
            client.download_file(bucket, f'golden/{version}/snapshot.dump', str(dump))
            # Head requests avoid downloading the shared media into each environment.
            for media in manifest['media']:
                head = client.head_object(Bucket=bucket, Key=media['key'])
                if head.get('Metadata', {}).get('sha256') != media['sha256']:
                    raise ValueError('published media checksum metadata mismatch')
            dsn = os.environ['GOLDEN_TARGET_DSN']
            for attempt in range(90):
                try:
                    with psycopg.connect(dsn, connect_timeout=3):
                        break
                except psycopg.OperationalError:
                    if attempt == 89:
                        raise
                    time.sleep(2)
            restore_database(dsn, dump, manifest, environment, args.refresh)
        return 0
    except Exception:
        print('RESTORE FAILED: failures=1', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())

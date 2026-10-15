import argparse
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import uuid

import psycopg
from psycopg import sql

HERE = Path(__file__).parent
SCHEMA = json.loads((HERE / 'schema.json').read_text())
EMPTY_TABLES = ('public.profile_identity', 'public.web_session', 'archive.legacy_comment',
                'public.outbox_event', 'public.webhook_subscription')
FREE_TEXT = {
    'public.comment': ('body', 'delete_reason'),
    'public.discussion_post': ('body',),
    'public.discussion_thread': ('title',),
    'public.post': ('title', 'description', 'delete_reason'),
    'public.collection': ('title',),
    'public.tank': ('title', 'description', 'gravel', 'plants', 'decoration', 'light',
                    'light_duration', 'filtration', 'technic', 'water_ph', 'water_kh',
                    'water_gh', 'water_no2', 'water_no3', 'water_po4', 'water_notes',
                    'food', 'notes', 'delete_reason'),
}
LABELS = ('thumb', 'small', 'medium', 'large')
MEDIA_KEY = re.compile(r'variants/(thumb|small|medium|large)/media-[0-9]+\.jpg\Z')
EMAIL = re.compile(r'[\w.+-]+@[\w.-]+\.[a-z]{2,}', re.I)
URL = re.compile(r'https?://', re.I)
URL_EXEMPT_TABLES = {'public.species', 'public.species_common_name', 'public.species_link'}


def ident(table):
    return sql.Identifier(*table.split('.'))


def pseudonyms(key, ids, prefix):
    names = {row_id: f'{prefix}-{int.from_bytes(hmac.digest(key, f"{prefix}:{row_id}".encode(), "sha256")[:12], "big")}' for row_id in set(ids)}
    if len(set(names.values())) != len(names):
        raise ValueError('pseudonym collision')
    return names


def generated_text(key, context, value):
    if value is None or value == '':
        return value
    words = ('aquatic', 'water', 'fish', 'sand', 'stone', 'plant', 'lake', 'river', 'habitat', 'reef', 'blue')
    digest = hmac.digest(key, context.encode(), 'sha256')
    sentence = ' '.join(words[b % len(words)] for b in digest) + ' '
    return (sentence * (len(value) // len(sentence) + 1))[:len(value)]


def check_schema(conn):
    unsupported = conn.execute("""
        select
          (select count(*) from pg_namespace where nspname !~ '^pg_'
             and nspname not in ('public','archive','information_schema'))
        + (select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace
             where n.nspname !~ '^pg_' and n.nspname <> 'information_schema'
               and c.relkind not in ('r','p','i','I','S'))
        + (select count(*) from pg_proc p join pg_namespace n on n.oid=p.pronamespace
             where n.nspname !~ '^pg_' and n.nspname <> 'information_schema')
        + (select count(*) from pg_type t join pg_namespace n on n.oid=t.typnamespace
             where n.nspname !~ '^pg_' and n.nspname <> 'information_schema'
               and t.typtype in ('e','d','r','m'))
        + (select count(*) from pg_largeobject_metadata)
        + (select count(*) from pg_foreign_server)
        + (select count(*) from pg_foreign_data_wrapper)
        + (select count(*) from pg_policy)
        + (select count(*) from pg_rewrite r join pg_class c on c.oid=r.ev_class
             join pg_namespace n on n.oid=c.relnamespace where n.nspname !~ '^pg_'
               and n.nspname <> 'information_schema')
        + (select count(*) from pg_trigger where not tgisinternal)
        + (select count(*) from pg_event_trigger)
        + (select count(*) from pg_publication)
        + (select count(*) from pg_subscription)
        + (select count(*) from pg_extension where extname <> 'plpgsql')
    """).fetchone()[0]
    if unsupported:
        raise ValueError('unsupported database objects; masking review required')
    actual = {}
    for schema, table, column, typ in conn.execute("""
        select n.nspname, c.relname, a.attname, format_type(a.atttypid,a.atttypmod)
        from pg_class c join pg_namespace n on n.oid=c.relnamespace
        join pg_attribute a on a.attrelid=c.oid
        where c.relkind in ('r','p') and n.nspname not in ('pg_catalog','information_schema')
          and n.nspname not like 'pg_toast%' and a.attnum>0 and not a.attisdropped
        order by 1,2,3
    """):
        actual.setdefault(f'{schema}.{table}', {})[column] = typ
    if actual != SCHEMA:
        raise ValueError('schema mismatch; masking review required')


def denylist(conn):
    values = []
    for query in ('select username, display_name from profile',
                  'select poster_name from comment', 'select poster_name from discussion_post',
                  'select original_filename from media_item'):
        with conn.cursor(name='deny_' + uuid.uuid4().hex) as cursor:
            cursor.execute(query)
            values.extend(value for row in cursor for value in row if value and value.strip())
    return set(values)


def counts(conn):
    return {table: conn.execute(sql.SQL('select count(*) from {}').format(ident(table))).fetchone()[0]
            for table in SCHEMA}


def fingerprint(dsn):
    with psycopg.connect(dsn) as conn:
        return {table: conn.execute(sql.SQL("select md5(coalesce(string_agg(row_to_json(t)::text, '' order by row_to_json(t)::text), '')) from {} t").format(ident(table))).fetchone()[0] for table in SCHEMA}


def verify(conn, originals):
    check_schema(conn)
    leaks = 0
    original_pattern = re.compile('|'.join(re.escape(v) for v in sorted(originals, key=len, reverse=True)), re.I) if originals else None
    definitions = conn.execute("""
        select c.relname from pg_class c join pg_namespace n on n.oid=c.relnamespace
          where n.nspname in ('public','archive')
        union all
        select pg_get_expr(d.adbin,d.adrelid) from pg_attrdef d
          join pg_class c on c.oid=d.adrelid join pg_namespace n on n.oid=c.relnamespace
          where n.nspname in ('public','archive')
        union all
        select conname || ' ' || pg_get_constraintdef(c.oid) from pg_constraint c
          join pg_namespace n on n.oid=c.connamespace where n.nspname in ('public','archive')
        union all
        select pg_get_indexdef(i.indexrelid) from pg_index i join pg_class c on c.oid=i.indrelid
          join pg_namespace n on n.oid=c.relnamespace where n.nspname in ('public','archive')
    """)
    for (value,) in definitions:
        if EMAIL.search(value) or URL.search(value) or (original_pattern and original_pattern.search(value)):
            leaks += 1
    for table in EMPTY_TABLES:
        leaks += conn.execute(sql.SQL('select count(*) from {}').format(ident(table))).fetchone()[0]
    for table, columns in SCHEMA.items():
        for column, typ in columns.items():
            if not any(t in typ for t in ('text', 'character varying', 'json')):
                continue
            with conn.cursor(name='verify_' + uuid.uuid4().hex) as cursor:
                cursor.execute(sql.SQL('select {}::text from {} where {} is not null').format(sql.Identifier(column), ident(table), sql.Identifier(column)))
                for (value,) in cursor:
                    if EMAIL.search(value) or (table not in URL_EXEMPT_TABLES and URL.search(value)) or (original_pattern and original_pattern.search(value)):
                        leaks += 1
    for column in ('city', 'external_avatar_url', 'last_login_at', 'suspended_until', 'avatar_media_id', 'profile_image_media_id'):
        leaks += conn.execute(sql.SQL('select count(*) from profile where {} is not null').format(sql.Identifier(column))).fetchone()[0]
    leaks += conn.execute("select count(*) from profile where username !~ '^member-[0-9]+$' or display_name is distinct from username").fetchone()[0]
    leaks += conn.execute("select count(*) from media_item where checksum_sha256 is not null or original_filename <> 'media-' || id || '.jpg' or storage_key <> 'variants/thumb/media-' || id || '.jpg'").fetchone()[0]
    for (key,) in conn.execute('select storage_key from media_variant'):
        if not MEDIA_KEY.fullmatch(key):
            leaks += 1
    print(f'VERIFICATION: leaks={leaks}')
    return leaks


def pg_env(conn, readonly=False):
    env = os.environ.copy()
    for key, value in conn.info.get_parameters().items():
        if key in ('host', 'hostaddr', 'port', 'dbname', 'user', 'password', 'sslmode', 'sslrootcert', 'sslcert', 'sslkey', 'service'):
            env['PG' + ('DATABASE' if key == 'dbname' else key.upper())] = value
    if conn.info.password:
        env['PGPASSWORD'] = conn.info.password
    if readonly:
        env['PGOPTIONS'] = '-c default_transaction_read_only=on'
    return env


def copy_database(source, target):
    snapshot_id = source.execute('select pg_export_snapshot()').fetchone()[0]
    with subprocess.Popen(['pg_dump', '--format=custom', '--no-owner', '--no-acl', '--no-comments', '--snapshot', snapshot_id],
                          env=pg_env(source, True), stdout=subprocess.PIPE, stderr=subprocess.DEVNULL) as dump:
        result = subprocess.run(['pg_restore', '--dbname', target.info.dbname, '--no-owner', '--no-acl', '--no-comments', '--exit-on-error', '--single-transaction'],
                                env=pg_env(target), stdin=dump.stdout, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        dump.stdout.close()
        if dump.wait() or result.returncode:
            raise RuntimeError('database copy failed')


def select_media(conn, key, limit, post_ids):
    rows = conn.execute("""
        select p.id, m.id, v.id, v.label, v.storage_key
        from post p join post_media pm on pm.post_id=p.id
        join media_item m on m.id=pm.media_item_id
        join media_variant v on v.media_item_id=m.id
        where p.state='published' and p.deleted_at is null and m.kind='photo'
          and v.label=any(%s) and v.storage_key like 'variants/%%'
        order by p.id,m.id,v.id
    """, (list(LABELS),)).fetchall()
    candidates = {r[0] for r in rows if r[3] == 'thumb'}
    if post_ids is not None:
        if not set(post_ids) <= candidates:
            raise ValueError('requested posts have no eligible public pictures')
        chosen = set(post_ids)
    else:
        chosen = set(sorted(candidates, key=lambda i: hmac.digest(key, f'picture:{i}'.encode(), 'sha256'))[:limit])
    if not chosen:
        raise ValueError('no eligible public pictures')
    # One photo per post bounds the picture subset even for multi-photo stories.
    selected = {}
    for post, media, variant, label, old_key in rows:
        if post in chosen and label == 'thumb':
            selected.setdefault(post, media)
    media_ids = set(selected.values())
    mapping = [{'post_id': post, 'media_id': media, 'variant_id': variant, 'source_key': old_key,
                'key': f'variants/{label}/media-{media}.jpg', 'label': label}
               for post, media, variant, label, old_key in rows
               if post in chosen and media == selected[post] and media in media_ids
               and old_key.startswith(f'variants/{label}/') and old_key.endswith('.jpg')]
    if {r['media_id'] for r in mapping if r['label'] == 'thumb'} != media_ids:
        raise ValueError('invalid public variant layout')
    return sorted(chosen), mapping


def mask(conn, key, mapping):
    conn.execute((HERE / 'mask.sql').read_text())
    ids = [r[0] for r in conn.execute('select id from profile')]
    names = pseudonyms(key, ids, 'member')
    temporary = 'snapshot-' + uuid.uuid4().hex + '-'
    conn.execute('update profile set username=%s || id::text', (temporary,))
    with conn.cursor() as cur:
        cur.executemany('update profile set username=%s,display_name=%s where id=%s', [(name, name, row_id) for row_id, name in names.items()])
    for table, columns in FREE_TEXT.items():
        for column in columns:
            with conn.cursor(name='text_' + uuid.uuid4().hex) as read, conn.cursor() as write:
                read.execute(sql.SQL('select id,{} from {}').format(sql.Identifier(column), ident(table)))
                while batch := read.fetchmany(1000):
                    write.executemany(sql.SQL('update {} set {}=%s where id=%s').format(ident(table), sql.Identifier(column)),
                                      [(generated_text(key, f'{table}.{column}:{row_id}', text), row_id) for row_id, text in batch])
    for table in ('comment', 'discussion_post'):
        guest_ids = [r[0] for r in conn.execute(sql.SQL('select id from {} where poster_name is not null').format(sql.Identifier(table)))]
        with conn.cursor() as cur:
            cur.executemany(sql.SQL('update {} set poster_name=%s where id=%s').format(sql.Identifier(table)),
                            [(name, row_id) for row_id, name in pseudonyms(hmac.digest(key, table.encode(), 'sha256'), guest_ids, 'guest').items()])
    conn.execute("update slug_alias set value='snapshot-' || %s || '-' || id::text", (uuid.uuid4().hex,))
    conn.execute("update slug_alias set value='post-' || post_id || '-alias-' || id")
    media_ids = list({m['media_id'] for m in mapping})
    variant_ids = list({m['variant_id'] for m in mapping})
    conn.execute('delete from media_variant where not (id=any(%s))', (variant_ids,))
    for table in ('post_media', 'tank_media', 'discussion_post_media'):
        conn.execute(sql.SQL('delete from {} where not (media_item_id=any(%s))').format(sql.Identifier(table)), (media_ids,))
    conn.execute('update tank set main_media_id=null where not (main_media_id=any(%s))', (media_ids,))
    conn.execute('delete from media_item where not (id=any(%s))', (media_ids,))
    for media_id in media_ids:
        conn.execute("update media_item set storage_key='variants/thumb/media-' || id || '.jpg', original_filename='media-' || id || '.jpg', content_type='image/jpeg' where id=%s", (media_id,))
    for media in {m['variant_id']: m for m in mapping}.values():
        conn.execute('update media_variant set storage_key=%s where id=%s', (media['key'], media['variant_id']))


def sha256(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def validate_manifest(manifest, version):
    if not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}', version) or manifest['version'] != version or manifest['format'] != 1:
        raise ValueError('invalid snapshot version')
    if not re.fullmatch(r'[0-9a-f]{64}', manifest['dump_sha256']) or not manifest['migration_id']:
        raise ValueError('invalid snapshot manifest')
    for media in manifest['media']:
        if not MEDIA_KEY.fullmatch(media['key']) or not re.fullmatch(r'[0-9a-f]{64}', media['sha256']):
            raise ValueError('invalid media manifest')


def build(source_dsn, target_dsn, key, output, version, limit=2000, post_ids=None, media_store=None):
    if len(key) < 32 or limit < 1 or not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}', version):
        raise ValueError('key, sample size or version invalid')
    output = Path(output)
    if output.exists():
        raise ValueError('output must be new')
    with psycopg.connect(source_dsn) as source, psycopg.connect(target_dsn) as target:
        source.execute('set transaction isolation level repeatable read, read only')
        src_id = source.execute('select system_identifier from pg_control_system()').fetchone()[0]
        dst_id = target.execute('select system_identifier from pg_control_system()').fetchone()[0]
        if (src_id, source.info.dbname) == (dst_id, target.info.dbname):
            raise ValueError('source and target must differ')
        existing = target.execute("select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname not in ('pg_catalog','information_schema') and n.nspname not like 'pg_toast%' and c.relkind in ('r','p','v','m','S','f')").fetchone()[0]
        if existing:
            raise ValueError('target must be empty')
        check_schema(source)
        originals = denylist(source)
        if any(v.casefold() in version.casefold() for v in originals):
            raise ValueError('version contains an original identifier')
        post_list, mapping = select_media(source, key, limit, post_ids)
        target.commit()
        copy_database(source, target)
        check_schema(target)
        mask(target, key, mapping)
        if verify(target, originals):
            raise ValueError('privacy verification failed')
        row_counts = counts(target)
        migration = target.execute('select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId" desc limit 1').fetchone()[0]
        target.commit()
        output.mkdir(mode=0o700, parents=True)
        try:
            media_manifest = []
            if media_store:
                for media in {m['key']: m for m in mapping}.values():
                    digest = media_store(media['source_key'], f'golden/{version}/media/{media["key"]}')
                    media_manifest.append({'key': media['key'], 'sha256': digest})
            dump_path = output / 'snapshot.dump'
            with dump_path.open('xb') as stream:
                os.chmod(dump_path, 0o600)
                result = subprocess.run(['pg_dump', '--format=custom', '--no-owner', '--no-acl', '--no-comments'], env=pg_env(target, True), stdout=stream, stderr=subprocess.DEVNULL)
            if result.returncode:
                raise RuntimeError('anonymized dump failed')
            manifest = {'format': 1, 'version': version, 'migration_id': migration, 'row_counts': row_counts,
                        'post_ids': post_list, 'media': media_manifest,
                        'media_keys': sorted({m['key'] for m in mapping}),
                        'media_complete': bool(media_store), 'dump_sha256': sha256(dump_path)}
            (output / 'manifest.json').write_text(json.dumps(manifest, indent=2, sort_keys=True) + '\n')
            print(f'SNAPSHOT: tables={len(row_counts)} pictures={len(post_list)} variants={len(manifest["media_keys"])}')
            return manifest
        except BaseException:
            for file in output.iterdir():
                file.unlink()
            output.rmdir()
            raise


def transfer_variant(client, source_bucket, target_bucket, source_key, target_key):
    if not re.fullmatch(r'variants/(thumb|small|medium|large)/.+\.jpg', source_key):
        raise ValueError('generated public JPEG required')
    if not re.fullmatch(r'golden/[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}/media/variants/(thumb|small|medium|large)/media-[0-9]+\.jpg', target_key):
        raise ValueError('generated golden key required')
    with tempfile.TemporaryFile() as stream:
        client.download_fileobj(source_bucket, source_key, stream)
        stream.seek(0)
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        stream.seek(0)
        client.upload_fileobj(stream, target_bucket, target_key,
                              ExtraArgs={'ContentType': 'image/jpeg', 'Metadata': {'sha256': digest}})
        return digest


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path)
    parser.add_argument('--verify-only', action='store_true')
    parser.add_argument('--version')
    parser.add_argument('--limit', type=int, default=2000)
    parser.add_argument('--post-ids', type=Path)
    parser.add_argument('--publish-bucket')
    parser.add_argument('--media-source-bucket')
    args = parser.parse_args()
    os.umask(0o077)
    try:
        if args.verify_only:
            with psycopg.connect(os.environ['GOLDEN_SOURCE_DSN']) as source, psycopg.connect(os.environ['GOLDEN_TARGET_DSN']) as target:
                source.execute('set transaction read only')
                target.execute('set transaction read only')
                return 1 if verify(target, denylist(source)) else 0
        if not args.output or not args.version:
            raise ValueError('output and version required')
        store = None
        if args.publish_bucket:
            if not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}', args.version):
                raise ValueError('invalid version')
            import boto3
            client = boto3.client('s3', endpoint_url=os.environ['GOLDEN_S3_ENDPOINT'])
            if client.list_objects_v2(Bucket=args.publish_bucket, Prefix=f'golden/{args.version}/', MaxKeys=1).get('KeyCount', 0):
                raise ValueError('published prefix must be new')
            if not args.media_source_bucket:
                raise ValueError('media source bucket required')

            def store(source_key, target_key):
                return transfer_variant(client, args.media_source_bucket, args.publish_bucket, source_key, target_key)

        manifest = build(os.environ['GOLDEN_SOURCE_DSN'], os.environ['GOLDEN_TARGET_DSN'],
                         bytes.fromhex(os.environ['GOLDEN_HMAC_KEY']), args.output, args.version,
                         args.limit, json.loads(args.post_ids.read_text()) if args.post_ids else None, store)
        if args.publish_bucket:
            validate_manifest(manifest, args.version)
            # Publishing the manifest last makes it the completion marker.
            for name in ('snapshot.dump', 'manifest.json'):
                client.upload_file(str(args.output / name), args.publish_bucket, f'golden/{args.version}/{name}')
            print('PUBLISHED: snapshots=1')
        return 0
    except Exception:
        print('SNAPSHOT FAILED: failures=1', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())

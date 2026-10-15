import contextlib
import copy
import io
import json
import os
import subprocess
import sys

import restore
from pathlib import Path
import tempfile
import unittest
import uuid

import psycopg
from psycopg.types.json import Jsonb
from psycopg import sql
from psycopg.conninfo import make_conninfo

import snapshot

HERE = Path(__file__).parent
KEY = bytes(range(32))


class GenerationTests(unittest.TestCase):
    def test_pseudonyms_are_keyed_stable_and_unique(self):
        first = snapshot.pseudonyms(KEY, [9, 2, 7], 'member')
        self.assertEqual(first, snapshot.pseudonyms(KEY, [7, 9, 2], 'member'))
        self.assertNotEqual(first, snapshot.pseudonyms(b'x' * 32, [9, 2, 7], 'member'))
        self.assertEqual(len(set(first.values())), 3)
        self.assertEqual(first[9], snapshot.pseudonyms(KEY, [9], 'member')[9])

    def test_generated_text_preserves_null_empty_and_length(self):
        for value in (None, '', 'x', 'Contact SyntheticAlice: alice@example.invalid ' * 31):
            result = snapshot.generated_text(KEY, 'comment.body:1', value)
            self.assertEqual(result, snapshot.generated_text(KEY, 'comment.body:1', value))
            if value is None:
                self.assertIsNone(result)
            else:
                self.assertEqual(len(value), len(result))
                self.assertNotIn('@', result)
                self.assertNotIn('SyntheticAlice', result)

    def test_manifest_rejects_tampering_and_unsafe_media_keys(self):
        manifest = {'format': 1, 'version': 'v1', 'migration_id': 'test',
                    'dump_sha256': '0' * 64, 'row_counts': {}, 'post_ids': [1],
                    'media': [{'key': 'golden/v1/media/variants/thumb/media-1.jpg', 'sha256': '0' * 64}]}
        snapshot.validate_manifest(manifest, 'v1')
        for key in ('originals/a.jpg', 'variants/../secret', 'forum_attachments/a.jpg', 'golden/v2/media/variants/thumb/media-1.jpg', 'variants/thumb/media-1.jpg'):
            bad = copy.deepcopy(manifest)
            bad['media'][0]['key'] = key
            with self.assertRaises(ValueError):
                snapshot.validate_manifest(bad, 'v1')
        with self.assertRaises(ValueError):
            snapshot.validate_manifest(manifest, 'v2')


@unittest.skipUnless(os.environ.get('GOLDEN_TEST_DSN'), 'synthetic Postgres required')
class DatabaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.dsn = os.environ['GOLDEN_TEST_DSN']
        with psycopg.connect(cls.dsn) as conn:
            if not conn.execute('select current_database()').fetchone()[0].startswith('synthetic_'):
                raise RuntimeError('synthetic database name required')
        cls.suffix = uuid.uuid4().hex[:10]
        cls.source = make_conninfo(cls.dsn, dbname='synthetic_source_' + cls.suffix)
        cls.target = make_conninfo(cls.dsn, dbname='synthetic_target_' + cls.suffix)
        with psycopg.connect(cls.dsn, autocommit=True) as conn:
            for name in ('synthetic_source_', 'synthetic_target_', 'synthetic_clone_', 'synthetic_leak_'):
                conn.execute(sql.SQL('create database {}').format(sql.Identifier(name + cls.suffix)))
        with psycopg.connect(cls.source) as conn:
            conn.execute((HERE / 'fixture-schema.sql').read_text())
            conn.execute((HERE / 'fixture.sql').read_text())
        cls.output = tempfile.TemporaryDirectory()

    @classmethod
    def tearDownClass(cls):
        cls.output.cleanup()
        with psycopg.connect(cls.dsn, autocommit=True) as conn:
            for name in ('synthetic_source_', 'synthetic_target_', 'synthetic_clone_', 'synthetic_leak_'):
                conn.execute(sql.SQL('drop database {} with (force)').format(sql.Identifier(name + cls.suffix)))

    def test_end_to_end_masks_every_rule_and_verifies_dump(self):
        output = Path(self.output.name) / 'snapshot'
        before = snapshot.fingerprint(self.source)
        transfers = []

        def publish(source, target):
            transfers.append((source, target))
            return '0' * 64

        manifest = snapshot.build(self.source, self.target, KEY, output, 'synthetic-v1', limit=2, media_store=publish)
        self.assertEqual(before, snapshot.fingerprint(self.source))
        self.assertEqual(manifest, json.loads((output / 'manifest.json').read_text()))
        self.assertEqual(manifest['migration_id'], '20261013120319_EncryptWebSessionTokens')
        self.assertEqual(len(manifest['post_ids']), 2)
        self.assertLessEqual(set(manifest['post_ids']), {1, 2, 3})
        self.assertTrue((output / 'snapshot.dump').read_bytes().startswith(b'PGDMP'))
        with psycopg.connect(self.target) as conn, psycopg.connect(self.source) as source:
            denylist = snapshot.denylist(source)
            self.assertEqual(snapshot.verify(conn, denylist, 'synthetic-v1'), 0)
            for table in snapshot.EMPTY_TABLES:
                self.assertEqual(conn.execute(sql.SQL('select count(*) from {}').format(snapshot.ident(table))).fetchone()[0], 0)
            profiles = conn.execute('select username, display_name, city, external_avatar_url, last_login_at, suspended_until, country_code, avatar_media_id, profile_image_media_id from profile order by id').fetchall()
            self.assertEqual({r[0] for r in profiles}, set(snapshot.pseudonyms(KEY, [1, 2, 3], 'member').values()))
            for row in profiles:
                self.assertEqual(row[0], row[1])
                self.assertEqual(row[2:6], (None,) * 4)
                self.assertEqual(row[6], 'DE')
                self.assertEqual(row[7:], (None, None))
            for table, columns in snapshot.FREE_TEXT.items():
                for column in columns:
                    query = sql.SQL('select id, {} from {} order by id').format(sql.Identifier(column), snapshot.ident(table))
                    old, new = source.execute(query).fetchall(), conn.execute(query).fetchall()
                    self.assertEqual([r[0] for r in old], [r[0] for r in new])
                    for (row_id, a), (_, b) in zip(old, new):
                        self.assertEqual(b, snapshot.generated_text(KEY, f'{table}.{column}:{row_id}', a))
            for table in ('species', 'species_link', 'species_common_name', 'rating', 'comment_vote', 'follow', 'inhabitant', 'post_species'):
                query = sql.SQL('select * from {} order by id').format(sql.Identifier(table))
                self.assertEqual(source.execute(query).fetchall(), conn.execute(query).fetchall())
            changed = {table: set(columns) for table, columns in snapshot.FREE_TEXT.items()}
            changed['public.profile'] = {'username', 'display_name', 'city', 'external_avatar_url', 'last_login_at', 'suspended_until', 'avatar_media_id', 'profile_image_media_id'}
            changed['public.slug_alias'] = {'value'}
            changed['public.media_item'] = {'storage_key', 'original_filename', 'content_type', 'checksum_sha256'}
            changed['public.media_variant'] = {'storage_key'}
            changed['public.tank'].add('main_media_id')
            changed['public.comment'].add('poster_name')
            changed['public.discussion_post'].add('poster_name')
            subset_tables = {'public.media_item', 'public.media_variant', 'public.post_media', 'public.tank_media', 'public.discussion_post_media'}
            for table, columns in snapshot.SCHEMA.items():
                if table in snapshot.EMPTY_TABLES or table == 'public.__EFMigrationsHistory':
                    continue
                stable = sorted(set(columns) - changed.get(table, set()))
                query = sql.SQL('select {} from {} order by id').format(sql.SQL(',').join(map(sql.Identifier, stable)), snapshot.ident(table))
                old, new = source.execute(query).fetchall(), conn.execute(query).fetchall()
                if table in subset_tables:
                    self.assertTrue(set(new) <= set(old))
                else:
                    self.assertEqual(old, new)
            for table in ('comment', 'discussion_post'):
                self.assertTrue(conn.execute(sql.SQL("select poster_name from {} where poster_name is not null").format(sql.Identifier(table))).fetchone()[0].startswith('guest-'))
            exported = set(manifest['media_keys'])
            self.assertEqual(exported, {target for _, target in transfers})
            self.assertEqual(exported, {r[0] for r in conn.execute('select storage_key from media_variant')})
            self.assertTrue(all(k.startswith('golden/synthetic-v1/media/variants/') for k in exported))
            self.assertEqual({r[0] for r in conn.execute('select id from media_variant')},
                             {m['variant_id'] for m in snapshot.select_media(source, KEY, 2, None, 'synthetic-v1')[1]})
            self.assertEqual(conn.execute('select count(*) from media_item').fetchone()[0], 2)
            for key, filename, checksum in conn.execute('select storage_key, original_filename, checksum_sha256 from media_item'):
                self.assertIn(key, exported)
                self.assertRegex(key, r'^golden/synthetic-v1/media/variants/thumb/media-[0-9]+\.jpg$')
                self.assertRegex(filename, r'^media-[0-9]+\.jpg$')
                self.assertIsNone(checksum)
            self.assertEqual(conn.execute("select count(*) from slug_alias where value not like 'post-%'").fetchone()[0], 0)
            conn.execute('savepoint taxonomy_urls')
            conn.execute("update species_link set url='https://taxonomy.example.invalid/species/1' where id=1")
            conn.execute("update species set description='https://taxonomy.example.invalid' where id=1")
            conn.execute("update species_common_name set name='https://taxonomy.example.invalid/name' where id=1")
            self.assertEqual(snapshot.verify(conn, denylist, 'synthetic-v1'), 0)
            conn.execute('rollback to savepoint taxonomy_urls')
            for statement in (
                "update comment set body='planted@example.invalid' where id=1",
                "update tank set notes='https://personal.invalid' where id=1",
                "update post set title='SyntheticAlice' where id=1",
                "alter table profile alter column city set default 'planted@example.invalid'",
                "update comment set body='https://personal.invalid' where id=1",
                "update species_link set url='https://reference.invalid/SyntheticAlice' where id=1",
                "update species_common_name set name='planted@example.invalid' where id=1",
                "update post set title='sYnThEtIc DiSpLaY' where id=1",
                "insert into profile_identity (profile_id,provider,subject,created_at) values (1,'test','test',now())",
            ):
                with self.subTest(statement=statement.split()[1]):
                    conn.execute('savepoint leak')
                    conn.execute(statement)
                    capture = io.StringIO()
                    with contextlib.redirect_stdout(capture):
                        self.assertGreater(snapshot.verify(conn, denylist, 'synthetic-v1'), 0)
                    self.assertNotIn('SyntheticAlice', capture.getvalue())
                    self.assertNotIn('@', capture.getvalue())
                    conn.execute('rollback to savepoint leak')
            for table in snapshot.EMPTY_TABLES:
                data = source.execute(sql.SQL('select * from {}').format(snapshot.ident(table))).fetchone()
                cols = [d.name for d in source.execute(sql.SQL('select * from {} limit 0').format(snapshot.ident(table))).description]
                conn.execute('savepoint leak_table')
                conn.execute(sql.SQL('insert into {} ({}) overriding system value values ({})').format(snapshot.ident(table), sql.SQL(',').join(map(sql.Identifier, cols)), sql.SQL(',').join(sql.Placeholder() for _ in cols)), [Jsonb(v) if isinstance(v, dict) else v for v in data])
                self.assertGreater(snapshot.verify(conn, denylist, 'synthetic-v1'), 0)
                conn.execute('rollback to savepoint leak_table')
        clone = make_conninfo(self.dsn, dbname='synthetic_clone_' + self.suffix)
        with self.assertRaisesRegex(ValueError, 'checksum mismatch'):
            restore.restore_database(clone, output / 'snapshot.dump', {**manifest, 'dump_sha256': '1' * 64}, 'pr-17')
        restore.restore_database(clone, output / 'snapshot.dump', manifest, 'pr-17')
        with psycopg.connect(clone) as conn:
            self.assertEqual(set(manifest['media_keys']), {r[0] for r in conn.execute('select storage_key from media_variant')})
            self.assertTrue({r[0] for r in conn.execute('select storage_key from media_item')} <= set(manifest['media_keys']))
            conn.execute("update post set title='environment upload' where id=1")
        restore.restore_database(clone, output / 'snapshot.dump', manifest, 'pr-17')
        with psycopg.connect(clone) as conn:
            self.assertEqual(conn.execute('select title from post where id=1').fetchone()[0], 'environment upload')
        restore.restore_database(clone, output / 'snapshot.dump', manifest, 'staging', refresh=True)
        with psycopg.connect(clone) as conn:
            self.assertEqual(snapshot.counts(conn), manifest['row_counts'])
            conn.execute("update comment set body='planted@example.invalid' where id=1")
        result = subprocess.run([sys.executable, str(HERE / 'snapshot.py'), '--verify-only', '--version', 'synthetic-v1'],
                                env={**os.environ, 'GOLDEN_SOURCE_DSN': self.source, 'GOLDEN_TARGET_DSN': clone}, capture_output=True, text=True)
        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.stdout.strip(), 'VERIFICATION: leaks=1')
        self.assertNotIn('@', result.stdout + result.stderr)
        print('PLANTED LEAK: exit=1; VERIFICATION: leaks=1')
        with self.assertRaises(ValueError):
            snapshot.build(self.source, self.source, KEY, output / 'same', 'v1')
        with self.assertRaises(ValueError):
            snapshot.build(self.source, self.target, KEY, output / 'occupied', 'v1')
        print('SYNTHETIC E2E: source unchanged; custom dump verified; all masking rules passed')

    def test_curated_selection_and_deterministic_sampling(self):
        with psycopg.connect(self.source) as source:
            first = snapshot.select_media(source, KEY, 2, None, 'synthetic-v1')
            self.assertEqual(first, snapshot.select_media(source, KEY, 2, None, 'synthetic-v1'))
            selected, media = snapshot.select_media(source, KEY, 2000, [1], 'v1')
            self.assertEqual(selected, [1])
            self.assertEqual(len(media), 2)
            self.assertTrue(media[0]['source_key'].startswith('variants/thumb/'))
            self.assertEqual({m['key'] for m in media}, {'golden/v1/media/variants/thumb/media-1.jpg', 'golden/v1/media/variants/medium/media-1.jpg'})
            for invalid in ([4], [5], [999]):
                with self.assertRaises(ValueError):
                    snapshot.select_media(source, KEY, 2000, invalid, 'v1')

    def test_failure_rolls_back_masking_and_emits_no_artifact(self):
        target = make_conninfo(self.dsn, dbname='synthetic_leak_' + self.suffix)
        output = Path(self.output.name) / 'leaked'
        with psycopg.connect(self.source) as conn:
            conn.execute("update species set description='leak@example.invalid'")
        try:
            with self.assertRaisesRegex(ValueError, 'privacy verification'):
                snapshot.build(self.source, target, KEY, output, 'leaked-v1')
            self.assertFalse(output.exists())
            with psycopg.connect(target) as conn:
                self.assertEqual(conn.execute('select username from profile where id=1').fetchone()[0], 'SyntheticAlice')
        finally:
            with psycopg.connect(self.source) as conn:
                conn.execute("update species set description='Taxonomy reference'")

    def test_uninventoried_database_objects_fail_closed(self):
        statements = (
            "create materialized view public.personal_view as select 'leak@example.invalid' as body",
            "create view public.personal_view as select 'leak@example.invalid' as body",
            "create function public.personal_function() returns text language sql as $$select 'leak@example.invalid'::text$$",
            "select lo_from_bytea(0, convert_to('leak@example.invalid','UTF8'))",
            "create schema personal_schema",
            "create schema pgleak; create view pgleak.personal_view as select 'leak@example.invalid' as body",
        )
        for statement in statements:
            with self.subTest(object=statement.split()[1]), psycopg.connect(self.source) as conn:
                conn.execute(statement)
                with self.assertRaises(ValueError):
                    snapshot.check_schema(conn)
                conn.rollback()

    def test_unknown_schema_fails_closed(self):
        with psycopg.connect(self.source) as conn:
            conn.execute('alter table profile add column personal_note text')
            with self.assertRaises(ValueError):
                snapshot.check_schema(conn)
            conn.rollback()


if __name__ == '__main__':
    unittest.main(verbosity=2)

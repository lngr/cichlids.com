import contextlib
import copy
import io
import json
import os
import re
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

    def test_every_column_has_a_handling_rule(self):
        self.assertEqual({table: set(columns) for table, columns in snapshot.SCHEMA.items()},
                         {table: set(rules) for table, rules in snapshot.COLUMN_HANDLING.items()})
        for table, rules in snapshot.COLUMN_HANDLING.items():
            self.assertLessEqual(set(rules.values()), {'generated', 'pseudonym', 'null', 'alias', 'media', 'empty', 'kept'})
            self.assertEqual({c for c, rule in rules.items() if rule == 'generated'}, set(snapshot.FREE_TEXT.get(table, ())))
            if table in snapshot.EMPTY_TABLES:
                self.assertEqual(set(rules.values()), {'empty'})
            else:
                self.assertNotIn('empty', rules.values())
        for table in snapshot.EMPTY_TABLES:
            self.assertIn(table, snapshot.COLUMN_HANDLING)

    def test_manifest_rejects_tampering_and_unsafe_media_keys(self):
        manifest = {'format': 1, 'version': 'v1', 'migration_id': 'test',
                    'dump_sha256': '0' * 64, 'row_counts': {}, 'story_ids': [1],
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
        self.assertEqual(manifest['migration_id'], re.search(r"VALUES \('([0-9]{14}_[A-Za-z0-9]+)'", (HERE / 'fixture-schema.sql').read_text()).group(1))
        self.assertEqual(len(manifest['story_ids']), 2)
        self.assertLessEqual(set(manifest['story_ids']), {1, 2, 3})
        self.assertTrue((output / 'snapshot.dump').read_bytes().startswith(b'PGDMP'))
        with psycopg.connect(self.target) as conn, psycopg.connect(self.source) as source:
            denylist = snapshot.denylist(source)
            self.assertEqual(snapshot.verify(conn, denylist, 'synthetic-v1'), 0)
            for table in snapshot.EMPTY_TABLES:
                self.assertEqual(conn.execute(sql.SQL('select count(*) from {}').format(snapshot.ident(table))).fetchone()[0], 0)
            profiles = conn.execute('select username, display_name, city, external_avatar_url, last_login_at, suspended_until, country_code, handle_changed_at, last_seen_at, away_since, archive_retrospective_seen_at from profile order by id').fetchall()
            self.assertEqual({r[0] for r in profiles}, set(snapshot.pseudonyms(KEY, [1, 2, 3], 'member').values()))
            for row in profiles:
                self.assertEqual(row[0], row[1])
                self.assertEqual(row[2:6], (None,) * 4)
                self.assertEqual(row[7:], (None,) * 4)
                self.assertEqual(row[6], 'DE')
            for table, columns in snapshot.FREE_TEXT.items():
                for column in columns:
                    query = sql.SQL('select id, {} from {} order by id').format(sql.Identifier(column), snapshot.ident(table))
                    old, new = source.execute(query).fetchall(), conn.execute(query).fetchall()
                    if table in ('public.media_link', 'public.care_entry', 'public.comment', 'public.story', 'public.collection', 'public.tank', 'public.discussion_post'):
                        self.assertTrue({r[0] for r in new} <= {r[0] for r in old})
                        old = [r for r in old if r[0] in {n[0] for n in new}]
                    else:
                        self.assertEqual([r[0] for r in old], [r[0] for r in new])
                    self.assertEqual([r[0] for r in old], [r[0] for r in new])
                    for (row_id, a), (_, b) in zip(old, new):
                        self.assertEqual(b, snapshot.generated_text(KEY, f'{table}.{column}:{row_id}', a))
            for table in ('species', 'species_link', 'species_common_name'):
                query = sql.SQL('select * from {} order by id').format(sql.Identifier(table))
                self.assertEqual(source.execute(query).fetchall(), conn.execute(query).fetchall())
            changed = {table: set(columns) for table, columns in snapshot.FREE_TEXT.items()}
            changed['public.profile'] = {'username', 'display_name', 'city', 'external_avatar_url', 'last_login_at', 'suspended_until', 'handle_changed_at', 'last_seen_at', 'away_since', 'archive_retrospective_seen_at'}
            changed['public.slug_alias'] = {'value'}
            changed['public.media_item'] = {'storage_key', 'original_filename', 'content_type', 'checksum_sha256', 'taken_at_local', 'taken_at_offset_minutes', 'capture_checked_at', 'received_at', 'captured_in_app', 'placed_public', 'placement_attempts', 'placement_pending', 'placement_next_attempt_at', 'visibility_generation'}
            changed['public.media_variant'] = {'storage_key'}
            changed['public.story'].add('cover_media_link_id')
            changed['public.comment'].add('poster_name')
            changed['public.discussion_post'].add('poster_name')
            subset_tables = {'public.media_item', 'public.media_variant', 'public.media_link', 'public.tank_decommissioning', 'public.care_entry', 'public.care_measurement_value', 'public.care_plan_rule', 'public.care_plan_rule_change', 'public.water_change_session', 'public.water_change_session_step', 'public.streak_freeze', 'public.tank_visibility_consent', 'public.inhabitant', 'public.collection_entry', 'public.comment', 'public.rating', 'public.follow', 'public.story_subject', 'public.story', 'public.collection', 'public.tank', 'public.profile', 'public.discussion_post'}
            for table, columns in snapshot.SCHEMA.items():
                if table in snapshot.EMPTY_TABLES or table == 'public.__EFMigrationsHistory':
                    continue
                stable = sorted(set(columns) - changed.get(table, set()))
                query = sql.SQL('select {} from {} order by id').format(sql.SQL(',').join(map(sql.Identifier, stable)), snapshot.ident(table))
                old, new = source.execute(query).fetchall(), conn.execute(query).fetchall()
                if table in subset_tables:
                    hashable = lambda rows: {tuple(repr(v) if isinstance(v, (list, dict)) else v for v in r) for r in rows}
                    self.assertTrue(hashable(new) <= hashable(old))
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
            retained = {r[0] for r in conn.execute('select id from media_item')}
            self.assertEqual({r[0] for r in conn.execute('select distinct media_item_id from media_link')} - retained, set())
            self.assertEqual(conn.execute("select count(*) from media_link where context_type='profile'").fetchone()[0], 0)
            self.assertGreater(source.execute("select count(*) from media_link where context_type='profile'").fetchone()[0], 0)
            self.assertEqual(conn.execute("select count(*) from media_link where context_type='story' and role='item' and media_item_id=any(%s)", (list(retained),)).fetchone()[0],
                             source.execute("select count(*) from media_link where context_type='story' and role='item' and media_item_id=any(%s)", (list(retained),)).fetchone()[0])
            kept = [r[0] for r in source.execute('select d.id from tank_decommissioning d join tank t on t.id=d.tank_id where t.stream_visibility=\'public\' and d.start_media_item_id=any(%s) and coalesce(d.end_media_item_id, d.start_media_item_id)=any(%s) order by d.id', (list(retained), list(retained)))]
            self.assertEqual(kept, [r[0] for r in conn.execute('select id from tank_decommissioning order by id')])
            self.assertLess(len(kept), source.execute('select count(*) from tank_decommissioning').fetchone()[0])
            for table in ('streak_freeze', 'tank_visibility_consent', 'care_plan_rule', 'care_plan_rule_change', 'care_measurement_value', 'water_change_session', 'water_change_session_step', 'care_entry'):
                self.assertGreater(conn.execute(sql.SQL('select count(*) from {}').format(sql.Identifier(table))).fetchone()[0], 0, table)
            count = lambda db, query: db.execute(query).fetchone()[0]
            # private scopes are gone, public scopes remain
            self.assertEqual(count(conn, "select count(*) from care_entry where only_me or tank_id in (4, 5, 6, 7, 8)"), 0)
            self.assertEqual(sorted(r[0] for r in conn.execute('select id from care_entry')), [1, 3])
            self.assertEqual(sorted(r[0] for r in conn.execute('select id from water_change_session')), [1])
            self.assertEqual(sorted(r[0] for r in conn.execute('select id from care_measurement_value order by id')), [r[0] for r in source.execute('select id from care_measurement_value where care_entry_id=3')])
            for table in ('care_plan_rule', 'care_plan_rule_change', 'streak_freeze', 'tank_visibility_consent', 'tank_decommissioning'):
                self.assertEqual(count(conn, f'select count(*) from {table} where tank_id in (4, 5, 6, 7, 8)'), 0, table)
                self.assertGreater(count(source, f'select count(*) from {table} where tank_id in (4, 5, 6, 7, 8)'), 0, table)
            for table in ('care_plan_rule', 'care_plan_rule_change', 'streak_freeze', 'tank_visibility_consent'):
                self.assertGreater(count(conn, f'select count(*) from {table} where tank_id in (1)'), 0, table)
            self.assertEqual(sorted(r[0] for r in conn.execute('select tank_id from inhabitant order by tank_id')), [1, 4, 8])
            self.assertEqual(count(conn, 'select count(*) from media_link where tank_id in (5, 6, 7)'), 0)
            self.assertEqual(sorted(r[0] for r in conn.execute('select id from collection_entry')), [r[0] for r in source.execute('select id from collection_entry where collection_id=1 and story_id<>9')])
            self.assertEqual(count(conn, 'select count(*) from collection'), 2)
            self.assertEqual(sorted(r[0] for r in conn.execute('select id from comment where tank_id is not null')), [2, 4, 7])
            self.assertEqual(sorted(r[0] for r in conn.execute('select tank_id from rating where tank_id is not null')), [1, 4, 8])
            self.assertEqual(sorted(conn.execute('select tank_id, comment_id from rating where comment_id is not null')), [(1, 2)])
            self.assertEqual(count(source, 'select count(*) from rating where comment_id is not null'), 3)
            self.assertEqual(count(conn, 'select count(*) from rating where story_id is not null'), count(source, 'select count(*) from rating where story_id is not null and story_id <> 9 and profile_id <> 9'))
            self.assertEqual(count(conn, 'select count(*) from comment where story_id is not null'), count(source, 'select count(*) from comment where story_id is not null and story_id <> 9 and author_profile_id is distinct from 9'))
            self.assertEqual(sorted(r[0] for r in conn.execute('select target_tank_id from follow where target_tank_id is not null')), [1, 8])
            self.assertEqual(count(conn, "select count(*) from follow where status <> 'approved'"), 0)
            self.assertGreater(count(source, "select count(*) from follow where status = 'pending'"), 0)
            self.assertEqual(count(conn, 'select count(*) from follow where target_tank_id is null'), count(source, 'select count(*) from follow where target_tank_id is null and follower_profile_id <> 9 and target_profile_id <> 9'))
            self.assertEqual(sorted((r[0], r[1]) for r in conn.execute('select story_id, subject_type from story_subject')), [(1, 'inhabitant'), (1, 'species'), (1, 'tank'), (2, 'species'), (3, 'inhabitant'), (3, 'tank')])
            self.assertEqual(count(conn, "select count(*) from story where cover_media_link_id is not null and cover_media_link_id not in (select id from media_link)"), 0)
            self.assertEqual(count(source, 'select count(*) from story where cover_media_link_id is not null'), 3)
            self.assertEqual(count(source, "select count(*) from profile where kind = 'provisional'"), 1)
            self.assertEqual(count(conn, "select count(*) from profile where kind = 'provisional' or id = 9"), 0)
            for query in ('tank where profile_id = 9', 'story where author_profile_id = 9', 'media_item where owner_profile_id = 9', 'care_entry where tank_id = 9 or author_profile_id = 9',
                          'comment where author_profile_id = 9 or tank_id = 9 or story_id = 9', 'rating where profile_id = 9 or tank_id = 9 or story_id = 9',
                          'follow where follower_profile_id = 9 or target_profile_id = 9', 'collection where profile_id = 9', 'discussion_post where author_profile_id = 9',
                          'media_link where media_item_id = 9', 'media_variant where media_item_id = 9', 'tank_decommissioning where tank_id = 9'):
                self.assertEqual(count(conn, f'select count(*) from {query}'), 0, query)
                self.assertGreater(count(source, f'select count(*) from {query}'), 0, query)
            for table in ('device_credential', 'device_challenge'):
                self.assertEqual(count(conn, f'select count(*) from {table}'), 0)
                self.assertGreater(count(source, f'select count(*) from {table}'), 0)
            self.assertEqual(count(conn, 'select count(*) from tank_selected_member'), 0)
            self.assertGreater(count(source, 'select count(*) from tank_selected_member'), 0)
            for table in ('vacation', 'care_reminder_snooze', 'care_todo', 'comment_vote', 'used_checksum', 'released_handle', 'milestone_dismissal'):
                self.assertEqual(count(conn, f'select count(*) from {table}'), 0, table)
                self.assertGreater(count(source, f'select count(*) from {table}'), 0, table)
            self.assertEqual(count(conn, "select count(*) from media_item where taken_at_local is not null or taken_at_offset_minutes is not null or capture_checked_at is not null or received_at <> created_at or captured_in_app"), 0)
            self.assertEqual(sorted(conn.execute('select id, quick_share_story_id from media_item where quick_share_story_id is not null').fetchall()),
                             sorted(source.execute('select id, quick_share_story_id from media_item where quick_share_story_id is not null and id=any(%s)', (list(retained),)).fetchall()))
            self.assertGreater(count(conn, 'select count(*) from media_item where quick_share_story_id is not null'), 0)
            placement = "select count(*) from media_item where not placed_public or placement_pending or placement_attempts <> 0 or placement_next_attempt_at is not null or visibility_generation <> 0"
            self.assertEqual(count(conn, placement), 0)
            self.assertEqual(count(source, placement), count(source, 'select count(*) from media_item'))
            self.assertGreater(count(source, 'select count(*) from media_item where captured_in_app and taken_at_local is not null and received_at <> created_at'), 0)
            self.assertGreater(count(source, 'select count(*) from profile where handle_changed_at is not null'), 0)
            self.assertGreater(count(source, 'select count(*) from profile where last_seen_at is not null and away_since is not null'), 0)
            self.assertGreater(count(source, 'select count(*) from profile where archive_retrospective_seen_at is not null'), 0)
            self.assertEqual(source.execute('select bool_and(streaks_enabled) from profile').fetchone(), conn.execute('select bool_and(streaks_enabled) from profile').fetchone())
            for key, filename, checksum in conn.execute('select storage_key, original_filename, checksum_sha256 from media_item'):
                self.assertIn(key, exported)
                self.assertRegex(key, r'^golden/synthetic-v1/media/variants/thumb/media-[0-9]+\.jpg$')
                self.assertRegex(filename, r'^media-[0-9]+\.jpg$')
                self.assertIsNone(checksum)
            self.assertEqual(conn.execute("select count(*) from slug_alias where value not like 'story-%'").fetchone()[0], 0)
            conn.execute('savepoint taxonomy_urls')
            conn.execute("update species_link set url='https://taxonomy.example.invalid/species/1' where id=1")
            conn.execute("update species set description='https://taxonomy.example.invalid' where id=1")
            conn.execute("update species_common_name set name='https://taxonomy.example.invalid/name' where id=1")
            self.assertEqual(snapshot.verify(conn, denylist, 'synthetic-v1'), 0)
            conn.execute('rollback to savepoint taxonomy_urls')
            for statement in (
                "update comment set body='planted@example.invalid' where id=1",
                "update tank set notes='https://personal.invalid' where id=1",
                "update story set title='SyntheticAlice' where id=1",
                "alter table profile alter column city set default 'planted@example.invalid'",
                "update comment set body='https://personal.invalid' where id=1",
                "update species_link set url='https://reference.invalid/SyntheticAlice' where id=1",
                "update species_common_name set name='planted@example.invalid' where id=1",
                "update story set title='sYnThEtIc DiSpLaY' where id=1",
                "insert into profile_identity (profile_id,provider,subject,created_at) values (1,'test','test',now())",
                "update care_entry set only_me=true where id=1",
                "update tank set stream_visibility='private' where id=1",
                "update tank set stream_visibility='private', profile_visibility='private' where id=1",
                "update collection set is_public=false where id=1",
                "insert into story_subject (story_id,subject_type,tank_id,position) values (1,'tank',5,5)",
                "update tank set profile_visibility='followers', stream_visibility='followers' where id=1",
                "update tank set profile_visibility='followers', stream_visibility='followers' where id=4",
                "update tank set profile_visibility='selected', stream_visibility='selected' where id=1",
                "update comment set tank_id=6, story_id=null where id=2",
                "insert into follow (follower_profile_id,target_tank_id,status,created_at) values (3,2,'pending',now())",
                "update follow set status='pending', decided_at=null where follower_profile_id=2 and target_tank_id=1",
                "insert into follow (follower_profile_id,target_tank_id,status,created_at) values (1,5,'approved',now())",
                "update tank set profile_visibility='private', stream_visibility='private' where id=4",
                "update comment set tank_id=5, story_id=null where id=2",
                "update profile set kind='provisional' where id=1",
                "insert into rating (tank_id,profile_id,stars,created_at) values (5,3,3,now())",
                "update media_item set captured_in_app=true",
                "update media_item set placement_pending=true where id=(select min(id) from media_item)",
                "update media_item set visibility_generation=1 where id=(select min(id) from media_item)",
                "update media_item set placed_public=false where id=(select min(id) from media_item)",
                "update media_item set placement_attempts=1 where id=(select min(id) from media_item)",
                "update media_item set placement_next_attempt_at=now() where id=(select min(id) from media_item)",
                "update media_item set taken_at_local='2020-01-01 12:00'",
                "update media_item set received_at=created_at + interval '1 day'",
            ):
                with self.subTest(statement=statement):
                    conn.execute('savepoint leak')
                    conn.execute(statement)
                    capture = io.StringIO()
                    with contextlib.redirect_stdout(capture):
                        self.assertGreater(snapshot.verify(conn, denylist, 'synthetic-v1'), 0)
                    self.assertNotIn('SyntheticAlice', capture.getvalue())
                    self.assertNotIn('@', capture.getvalue())
                    conn.execute('rollback to savepoint leak')
            parents = {'public.device_credential': ('public.profile', 'profile_id'), 'public.web_push_reminder_sent': ('public.web_push_subscription', 'subscription_id'), 'public.video_job': ('public.media_item', 'media_item_id')}

            def copy_row(table, row_id=None):
                query = sql.SQL('select * from {} order by 1').format(snapshot.ident(table)) if row_id is None else sql.SQL('select * from {} where id=%s').format(snapshot.ident(table))
                data = source.execute(query, None if row_id is None else (row_id,)).fetchone()
                cols = [d.name for d in source.execute(sql.SQL('select * from {} limit 0').format(snapshot.ident(table))).description]
                conn.execute(sql.SQL('insert into {} ({}) overriding system value values ({})').format(snapshot.ident(table), sql.SQL(',').join(map(sql.Identifier, cols)), sql.SQL(',').join(sql.Placeholder() for _ in cols)), [Jsonb(v) if isinstance(v, dict) else v for v in data])

            for table in snapshot.EMPTY_TABLES:
                with self.subTest(table=table):
                    conn.execute('savepoint leak_table')
                    baseline = 0
                    if table in parents:
                        parent, column = parents[table]
                        copy_row(parent, source.execute(sql.SQL('select {} from {} order by id').format(sql.Identifier(column), snapshot.ident(table))).fetchone()[0])
                        with contextlib.redirect_stdout(io.StringIO()):
                            baseline = snapshot.verify(conn, denylist, 'synthetic-v1')
                    copy_row(table)
                    with contextlib.redirect_stdout(io.StringIO()):
                        self.assertGreater(snapshot.verify(conn, denylist, 'synthetic-v1'), baseline)
                    conn.execute('rollback to savepoint leak_table')
        clone = make_conninfo(self.dsn, dbname='synthetic_clone_' + self.suffix)
        with self.assertRaisesRegex(ValueError, 'checksum mismatch'):
            restore.restore_database(clone, output / 'snapshot.dump', {**manifest, 'dump_sha256': '1' * 64}, 'pr-17')
        restore.restore_database(clone, output / 'snapshot.dump', manifest, 'pr-17')
        with psycopg.connect(clone) as conn:
            self.assertEqual(set(manifest['media_keys']), {r[0] for r in conn.execute('select storage_key from media_variant')})
            self.assertTrue({r[0] for r in conn.execute('select storage_key from media_item')} <= set(manifest['media_keys']))
            conn.execute("update story set title='environment upload' where id=1")
        restore.restore_database(clone, output / 'snapshot.dump', manifest, 'pr-17')
        with psycopg.connect(clone) as conn:
            self.assertEqual(conn.execute('select title from story where id=1').fetchone()[0], 'environment upload')
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
            for invalid in ([4], [5], [9], [999]):
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
            "create trigger personal_trigger after update on public.tank for each row execute function public.tank_changes_media_visibility()",
            "create or replace function public.story_changes_media_visibility() returns trigger language plpgsql as $$ begin return null; end $$",
            "drop trigger story_changes_media_visibility on public.story",
            "drop function public.media_item_ids_of_tank(bigint) cascade",
            "create function public.media_item_ids_of_tank(p_tank_id text) returns text language sql as $$select 'x'::text$$",
            "create schema pgleak; create view pgleak.personal_view as select 'leak@example.invalid' as body",
        )
        for statement in statements:
            with self.subTest(object=statement), psycopg.connect(self.source) as conn:
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

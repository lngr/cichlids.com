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
ROUTINES = json.loads((HERE / 'routines.json').read_text())
EMPTY_TABLES = ('public.profile_identity', 'public.web_session', 'archive.legacy_comment',
                'public.outbox_event', 'public.webhook_subscription',
                'public.web_push_subscription', 'public.web_push_reminder_sent',
                'public.video_upload', 'public.video_job', 'public.vacation',
                'public.care_reminder_snooze', 'public.care_todo', 'public.comment_vote',
                'public.tank_selected_member', 'public.used_checksum', 'public.device_credential',
                'public.device_challenge', 'public.released_handle', 'public.milestone_dismissal')
FREE_TEXT = {
    'public.comment': ('body', 'delete_reason'),
    'public.discussion_post': ('body',),
    'public.discussion_thread': ('title',),
    'public.story': ('title', 'description', 'delete_reason'),
    'public.collection': ('title',),
    'public.care_entry': ('note',),
    'public.media_link': ('caption',),
    'public.tank': ('title', 'description', 'gravel', 'plants', 'decoration', 'light',
                    'light_duration', 'filtration', 'technic', 'water_ph', 'water_kh',
                    'water_gh', 'water_no2', 'water_no3', 'water_po4', 'water_notes',
                    'food', 'notes', 'delete_reason'),
}
PRIVATE_TANK = "tank_id in (select id from tank where stream_visibility <> 'public')"
PRIVATE_SCOPE_CHECKS = (
    ('public.care_entry', 'only_me or ' + PRIVATE_TANK),
    ('public.care_plan_rule', PRIVATE_TANK),
    ('public.care_plan_rule_change', PRIVATE_TANK),
    ('public.water_change_session', PRIVATE_TANK),
    ('public.streak_freeze', PRIVATE_TANK),
    ('public.tank_decommissioning', PRIVATE_TANK),
    ('public.tank_visibility_consent', PRIVATE_TANK),
    ('public.inhabitant', "tank_id in (select id from tank where profile_visibility <> 'public')"),
    ('public.media_link', "tank_id in (select id from tank where profile_visibility <> 'public')"),
    ('public.story_subject', "tank_id in (select id from tank where profile_visibility <> 'public') or inhabitant_id in (select id from inhabitant where tank_id in (select id from tank where profile_visibility <> 'public'))"),
    ('public.story', "cover_media_link_id is not null and cover_media_link_id not in (select id from media_link)"),
    ('public.comment', "tank_id in (select id from tank where profile_visibility <> 'public')"),
    ('public.rating', "tank_id in (select id from tank where profile_visibility <> 'public')"),
    ('public.follow', "status <> 'approved' or target_tank_id in (select id from tank where profile_visibility <> 'public')"),
    ('public.collection_entry', "collection_id in (select id from collection where not is_public)"),
)
LABELS = ('thumb', 'small', 'medium', 'large')

# Handling of every column. generated: replaced by generated text of the same length (FREE_TEXT).
# pseudonym: replaced by an HMAC pseudonym. null: set to NULL. alias: rewritten from the story and alias
# IDs. media: rewritten to the published media keys or to fixed values. empty: the table is emptied.
# kept: copied unchanged. Kept text columns hold fixed vocabularies (kinds, states, units, roles, finding
# codes), system values (reason codes and parameters, migration IDs) or the public species catalog; the
# verification scans them for emails, URLs and original identifiers. Rows of private scopes are removed
# by mask.sql, which leaves the handling of the remaining columns unchanged.
COLUMN_HANDLING = {
    'archive.legacy_comment': {
        'empty': (
            'author_legacy_user_id body created_at_legacy id imported_at legacy_id legacy_target_id '
            'legacy_target_type payload poster_name stars vault_reason'),
    },
    'public.__EFMigrationsHistory': {
        'kept': 'migration_id product_version',
    },
    'public.care_entry': {
        'generated': 'note',
        'kept': (
            'amount_input_unit amount_input_value amount_liters author_profile_id duration_seconds evidence '
            'evidence_missing id kind occurred_at only_me recorded_at tank_id water_change_session_id'),
    },
    'public.care_measurement_value': {
        'kept': 'care_entry_id id input_unit input_value normalized_value origin parameter',
    },
    'public.care_plan_rule': {
        'kept': 'enabled id kind reason_code reason_parameters_json rhythm_days tank_id updated_at',
    },
    'public.care_plan_rule_change': {
        'kept': 'at enabled id kind rhythm_days tank_id',
    },
    'public.care_reminder_snooze': {
        'empty': 'created_at due_date id kind profile_id tank_id until_date',
    },
    'public.care_todo': {
        'empty': 'care_entry_id created_at done_at id profile_id tank_id text',
    },
    'public.collection': {
        'generated': 'title',
        'kept': 'created_at id is_public legacy_id profile_id',
    },
    'public.collection_entry': {
        'kept': 'collection_id id story_id sort',
    },
    'public.comment': {
        'generated': 'body delete_reason',
        'kept': 'author_profile_id created_at deleted_at deleted_by_profile_id id legacy_id story_id score tank_id',
        'pseudonym': 'poster_name',
    },
    'public.comment_vote': {
        'empty': 'comment_id created_at id profile_id value',
    },
    'public.discussion_post': {
        'generated': 'body',
        'kept': 'author_profile_id created_at id legacy_id sort thread_id',
        'pseudonym': 'poster_name',
    },
    'public.discussion_thread': {
        'generated': 'title',
        'kept': 'category created_at id last_post_at legacy_id post_count state',
    },
    'public.follow': {
        'kept': 'created_at decided_at follower_profile_id id status target_profile_id target_tank_id',
    },
    'public.inhabitant': {
        'kept': 'count id sort species_id tank_id',
    },
    'public.media_item': {
        'kept': 'byte_size created_at duration_ms height id kind legacy_id owner_profile_id quick_share_story_id width',
        'media': (
            'captured_in_app checksum_sha256 content_type original_filename placed_public placement_attempts '
            'placement_pending received_at storage_key visibility_generation'),
        'null': 'capture_checked_at placement_next_attempt_at taken_at_local taken_at_offset_minutes',
    },
    'public.media_link': {
        'generated': 'caption',
        'kept': (
            'care_entry_id context_type created_at discussion_post_id id inhabitant_id media_item_id position '
            'story_id profile_id role tank_id water_change_session_id'),
    },
    'public.media_variant': {
        'kept': 'byte_size id label media_item_id width',
        'media': 'storage_key',
    },
    'public.outbox_event': {
        'empty': (
            'acknowledged_subscription_ids aggregate_id aggregate_type attempt_count dispatched_at event_type '
            'given_up_at id last_attempt_at last_error next_attempt_at occurred_at payload redelivered_at'),
    },
    'public.profile': {
        'kept': 'badges country_code created_at id interests kind legacy_id onboarding_completed_at streaks_enabled',
        'null': 'away_since city external_avatar_url handle_changed_at last_login_at last_seen_at suspended_until',
        'pseudonym': 'display_name username',
    },
    'public.profile_identity': {
        'empty': 'created_at id profile_id provider subject',
    },
    'public.rating': {
        'kept': 'comment_id created_at id legacy_comment_id story_id profile_id stars tank_id',
    },
    'public.released_handle': {
        'empty': 'handle id profile_id released_at reserved_until',
    },
    'public.slug_alias': {
        'alias': 'value',
        'kept': 'created_at id is_canonical story_id',
    },
    'public.species': {
        'kept': (
            'aggression breeding category created_at description diet display_name genus gh_range habitat id '
            'intra_aggression kh_range legacy_id max_size morphs name origin ph_range slug temperature_range'),
    },
    'public.species_common_name': {
        'kept': 'id name species_id',
    },
    'public.species_link': {
        'kept': 'id label sort species_id url',
    },
    'public.streak_freeze': {
        'kept': 'care_entry_id created_at id period_end period_start tank_id tank_year',
    },
    'public.story': {
        'generated': 'delete_reason description title',
        'kept': (
            'author_profile_id cover_media_link_id created_at deleted_at deleted_by_profile_id id legacy_id '
            'published_at rating_average rating_count state topic updated_at view_count'),
    },
    'public.story_subject': {
        'kept': 'id inhabitant_id position species_id story_id subject_type tank_id',
    },
    'public.tank': {
        'generated': (
            'decoration delete_reason description filtration food gravel light light_duration notes plants '
            'technic title water_gh water_kh water_no2 water_no3 water_notes water_ph water_po4'),
        'kept': (
            'adopted_at care_reminders_enabled category created_at deleted_at deleted_by_profile_id '
            'depth_input_value depth_value dimension_unit height_input_value height_value id legacy_id '
            'profile_id '
            'profile_visibility published_at region state stream_visibility volume_input_unit volume_input_value '
            'volume_liters width_input_value width_value'),
    },
    'public.tank_decommissioning': {
        'kept': 'end_date end_media_item_id ended_at id scope start_date start_media_item_id started_at tank_id',
    },
    'public.tank_selected_member': {
        'empty': 'added_at id member_profile_id tank_id',
    },
    'public.tank_visibility_consent': {
        'kept': 'answer answered_at context id tank_id',
    },
    'public.used_checksum': {
        'empty': 'checksum_sha256 first_used_at kind profile_id',
    },
    'public.device_challenge': {
        'empty': 'challenge_hash created_at expires_at id',
    },
    'public.device_credential': {
        'empty': 'algorithm created_at id last_signed_request_at profile_id public_key retired_at state',
    },
    'public.milestone_dismissal': {
        'empty': 'dismissed_at id key profile_id',
    },
    'public.vacation': {
        'empty': 'created_at end_date id profile_id start_date',
    },
    'public.video_job': {
        'empty': 'attempts created_at error_code finished_at id locked_until media_item_id run_after status',
    },
    'public.video_upload': {
        'empty': (
            'byte_size care_entry_id chunk_size container created_at expires_at id multipart_upload_id offset '
            'original_filename owner_profile_id part_e_tags storage_key tank_id target'),
    },
    'public.water_change_session': {
        'kept': 'duration_seconds finished_at id profile_id started_at tank_id',
    },
    'public.water_change_session_step': {
        'kept': 'at id session_id state step',
    },
    'public.web_push_reminder_sent': {
        'empty': 'due_date fire_date id kind sent_at subscription_id tank_id',
    },
    'public.web_push_subscription': {
        'empty': (
            'auth created_at endpoint failure_count id last_success_at next_attempt_at p256dh profile_id '
            'time_zone'),
    },
    'public.web_session': {
        'empty': (
            'access_token_expires_at created_at csrf_token encrypted_access_token encrypted_id_token '
            'encrypted_refresh_token id last_seen_at session_id_hash subject'),
    },
    'public.webhook_subscription': {
        'empty': 'active created_at event_types id secret url',
    },
}
COLUMN_HANDLING = {table: {column: label for label, names in spec.items() for column in names.split()}
                   for table, spec in COLUMN_HANDLING.items()}
MEDIA_KEY = re.compile(r'golden/[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}/media/variants/(thumb|small|medium|large)/media-[0-9]+\.jpg\Z')
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


def routine_inventory(conn):
    functions = {f'{schema}.{name}({arguments})': hashlib.sha256(definition.encode()).hexdigest()
                 for schema, name, arguments, definition in conn.execute("""
        select n.nspname, p.proname, pg_get_function_identity_arguments(p.oid), pg_get_functiondef(p.oid)
        from pg_proc p join pg_namespace n on n.oid=p.pronamespace
        where n.nspname !~ '^pg_' and n.nspname <> 'information_schema' and p.prokind = 'f'""")}
    triggers = {f'{schema}.{table}.{name}': hashlib.sha256(definition.encode()).hexdigest()
                for schema, table, name, definition in conn.execute("""
        select n.nspname, c.relname, t.tgname, pg_get_triggerdef(t.oid, true)
        from pg_trigger t join pg_class c on c.oid=t.tgrelid join pg_namespace n on n.oid=c.relnamespace
        where not t.tgisinternal""")}
    return {'functions': dict(sorted(functions.items())), 'triggers': dict(sorted(triggers.items()))}


def check_schema(conn):
    unsupported = conn.execute("""
        select
          (select count(*) from pg_namespace where nspname !~ '^pg_'
             and nspname not in ('public','archive','information_schema'))
        + (select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace
             where n.nspname !~ '^pg_' and n.nspname <> 'information_schema'
               and c.relkind not in ('r','p','i','I','S'))
        + (select count(*) from pg_proc p join pg_namespace n on n.oid=p.pronamespace
             where n.nspname !~ '^pg_' and n.nspname <> 'information_schema' and p.prokind <> 'f')
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
        + (select count(*) from pg_event_trigger)
        + (select count(*) from pg_publication)
        + (select count(*) from pg_subscription)
        + (select count(*) from pg_extension where extname <> 'plpgsql')
    """).fetchone()[0]
    if unsupported:
        raise ValueError('unsupported database objects; masking review required')
    if routine_inventory(conn) != ROUTINES:
        raise ValueError('unexpected functions or triggers; masking review required')
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


def verify(conn, originals, version):
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
    for table, scope in PRIVATE_SCOPE_CHECKS:
        leaks += conn.execute(sql.SQL('select count(*) from {} where {}').format(ident(table), sql.SQL(scope))).fetchone()[0]
    leaks += conn.execute("select count(*) from media_item where not placed_public or placement_pending or placement_attempts <> 0 or placement_next_attempt_at is not null or visibility_generation <> 0").fetchone()[0]
    leaks += conn.execute("select count(*) from media_item where taken_at_local is not null or taken_at_offset_minutes is not null or capture_checked_at is not null or received_at <> created_at or captured_in_app").fetchone()[0]
    leaks += conn.execute("select count(*) from profile where kind = 'provisional'").fetchone()[0]
    for column in ('away_since', 'city', 'external_avatar_url', 'handle_changed_at', 'last_login_at', 'last_seen_at', 'suspended_until'):
        leaks += conn.execute(sql.SQL('select count(*) from profile where {} is not null').format(sql.Identifier(column))).fetchone()[0]
    leaks += conn.execute("select count(*) from media_link where context_type='profile'").fetchone()[0]
    leaks += conn.execute("select count(*) from profile where username !~ '^member-[0-9]+$' or display_name is distinct from username").fetchone()[0]
    leaks += conn.execute("select count(*) from media_item where checksum_sha256 is not null or original_filename <> 'media-' || id || '.jpg' or storage_key <> %s || 'variants/thumb/media-' || id || '.jpg'", (f'golden/{version}/media/',)).fetchone()[0]
    for (key,) in conn.execute('select storage_key from media_variant'):
        if not MEDIA_KEY.fullmatch(key) or not key.startswith(f'golden/{version}/media/'):
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


def select_media(conn, key, limit, story_ids, version):
    rows = conn.execute("""
        select p.id, m.id, v.id, v.label, v.storage_key
        from story p join media_link ml on ml.story_id=p.id and ml.context_type='story' and ml.role='item'
        join media_item m on m.id=ml.media_item_id
        join media_variant v on v.media_item_id=m.id
        join profile author on author.id=p.author_profile_id and author.kind <> 'provisional'
        left join profile owner on owner.id=m.owner_profile_id
        where p.state='published' and p.deleted_at is null and m.kind='photo'
          and v.label=any(%s) and v.storage_key like 'variants/%%'
          and owner.kind is distinct from 'provisional'
        order by p.id,ml.position,m.id,v.id
    """, (list(LABELS),)).fetchall()
    candidates = {r[0] for r in rows if r[3] == 'thumb'}
    if story_ids is not None:
        if not set(story_ids) <= candidates:
            raise ValueError('requested stories have no eligible public pictures')
        chosen = set(story_ids)
    else:
        chosen = set(sorted(candidates, key=lambda i: hmac.digest(key, f'picture:{i}'.encode(), 'sha256'))[:limit])
    if not chosen:
        raise ValueError('no eligible public pictures')
    # One photo per story bounds the picture subset.
    selected = {}
    for story, media, variant, label, old_key in rows:
        if story in chosen and label == 'thumb':
            selected.setdefault(story, media)
    media_ids = set(selected.values())
    mapping = [{'story_id': story, 'media_id': media, 'variant_id': variant, 'source_key': old_key,
                'key': f'golden/{version}/media/variants/{label}/media-{media}.jpg', 'label': label}
               for story, media, variant, label, old_key in rows
               if story in chosen and media == selected[story] and media in media_ids
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
    conn.execute("update slug_alias set value='story-' || story_id || '-alias-' || id")
    media_ids = list({m['media_id'] for m in mapping})
    variant_ids = list({m['variant_id'] for m in mapping})
    conn.execute('delete from media_variant where not (id=any(%s))', (variant_ids,))
    conn.execute('delete from tank_decommissioning where not (start_media_item_id=any(%s)) or not (coalesce(end_media_item_id, start_media_item_id)=any(%s))', (media_ids, media_ids))
    conn.execute("delete from media_link where context_type='profile'")
    conn.execute('delete from media_item where not (id=any(%s))', (media_ids,))
    for media in {m['media_id']: m for m in mapping if m['label'] == 'thumb'}.values():
        conn.execute("update media_item set storage_key=%s, original_filename='media-' || id || '.jpg', content_type='image/jpeg' where id=%s", (media['key'], media['media_id']))
    for media in {m['variant_id']: m for m in mapping}.values():
        conn.execute('update media_variant set storage_key=%s where id=%s', (media['key'], media['variant_id']))
    # The retained variants are public objects of the shared golden prefix and nothing is due for moving.
    # The deletes above fired the visibility triggers, so the placement state is set last.
    conn.execute('update media_item set placed_public=true, placement_pending=false, placement_attempts=0, placement_next_attempt_at=null, visibility_generation=0')


def sha256(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def validate_manifest(manifest, version):
    if not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}', version) or manifest['version'] != version or manifest['format'] != 1:
        raise ValueError('invalid snapshot version')
    if not re.fullmatch(r'[0-9a-f]{64}', manifest['dump_sha256']) or not manifest['migration_id']:
        raise ValueError('invalid snapshot manifest')
    for media in manifest['media']:
        if not MEDIA_KEY.fullmatch(media['key']) or not media['key'].startswith(f'golden/{version}/media/') or not re.fullmatch(r'[0-9a-f]{64}', media['sha256']):
            raise ValueError('invalid media manifest')


def build(source_dsn, target_dsn, key, output, version, limit=2000, story_ids=None, media_store=None):
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
        story_list, mapping = select_media(source, key, limit, story_ids, version)
        target.commit()
        copy_database(source, target)
        check_schema(target)
        mask(target, key, mapping)
        if verify(target, originals, version):
            raise ValueError('privacy verification failed')
        row_counts = counts(target)
        migration = target.execute('select migration_id from "__EFMigrationsHistory" order by migration_id desc limit 1').fetchone()[0]
        target.commit()
        output.mkdir(mode=0o700, parents=True)
        try:
            media_manifest = []
            if media_store:
                for media in {m['key']: m for m in mapping}.values():
                    digest = media_store(media['source_key'], media['key'])
                    media_manifest.append({'key': media['key'], 'sha256': digest})
            dump_path = output / 'snapshot.dump'
            with dump_path.open('xb') as stream:
                os.chmod(dump_path, 0o600)
                result = subprocess.run(['pg_dump', '--format=custom', '--no-owner', '--no-acl', '--no-comments'], env=pg_env(target, True), stdout=stream, stderr=subprocess.DEVNULL)
            if result.returncode:
                raise RuntimeError('anonymized dump failed')
            manifest = {'format': 1, 'version': version, 'migration_id': migration, 'row_counts': row_counts,
                        'story_ids': story_list, 'media': media_manifest,
                        'media_keys': sorted({m['key'] for m in mapping}),
                        'media_complete': bool(media_store), 'dump_sha256': sha256(dump_path)}
            (output / 'manifest.json').write_text(json.dumps(manifest, indent=2, sort_keys=True) + '\n')
            print(f'SNAPSHOT: tables={len(row_counts)} pictures={len(story_list)} variants={len(manifest["media_keys"])}')
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
    parser.add_argument('--story-ids', type=Path)
    parser.add_argument('--publish-bucket')
    parser.add_argument('--media-source-bucket')
    args = parser.parse_args()
    os.umask(0o077)
    try:
        if args.verify_only:
            if not args.version or not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}', args.version):
                raise ValueError('valid version required')
            with psycopg.connect(os.environ['GOLDEN_SOURCE_DSN']) as source, psycopg.connect(os.environ['GOLDEN_TARGET_DSN']) as target:
                source.execute('set transaction read only')
                target.execute('set transaction read only')
                return 1 if verify(target, denylist(source), args.version) else 0
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
                         args.limit, json.loads(args.story_ids.read_text()) if args.story_ids else None, store)
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

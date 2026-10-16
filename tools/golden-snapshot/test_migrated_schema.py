import os
from pathlib import Path
import re
import unittest

import psycopg

import snapshot

HERE = Path(__file__).parent


@unittest.skipUnless(os.environ.get('GOLDEN_MIGRATED_DSN'), 'database migrated by the EF migrations required')
class MigratedSchemaTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.conn = psycopg.connect(os.environ['GOLDEN_MIGRATED_DSN'])
        if not cls.conn.execute('select current_database()').fetchone()[0].startswith('synthetic_'):
            cls.conn.close()
            raise RuntimeError('synthetic database name required')

    @classmethod
    def tearDownClass(cls):
        cls.conn.close()

    def test_schema_json_matches_the_migrated_schema(self):
        snapshot.check_schema(self.conn)

    def test_fixture_pins_the_latest_migration(self):
        pinned = re.search(r"VALUES \('([0-9]{14}_[A-Za-z0-9]+)'", (HERE / 'fixture-schema.sql').read_text()).group(1)
        latest = self.conn.execute('select max(migration_id) from "__EFMigrationsHistory"').fetchone()[0]
        self.assertEqual(pinned, latest)

    def test_fixture_tables_equal_the_migrated_tables(self):
        text = (HERE / 'fixture-schema.sql').read_text()
        fixture = {name.replace('"', '') for name in re.findall(r'^CREATE TABLE ((?:public|archive)\.[a-z_]+|public\."__EFMigrationsHistory")', text, re.M)}
        self.assertEqual(fixture, set(snapshot.SCHEMA))


if __name__ == '__main__':
    unittest.main(verbosity=2)

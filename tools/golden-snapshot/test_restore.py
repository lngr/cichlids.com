import json
import os
from pathlib import Path
import unittest
from unittest.mock import Mock, patch

import boto3

import restore


class RestoreContracts(unittest.TestCase):
    def test_restore_checks_the_exact_published_media_keys(self):
        key = 'golden/v1/media/variants/thumb/media-1.jpg'
        manifest = {'format': 1, 'version': 'v1', 'migration_id': 'synthetic',
                    'dump_sha256': '0' * 64, 'media_complete': True,
                    'media_keys': [key], 'media': [{'key': key, 'sha256': '1' * 64}]}
        client = Mock()

        def download(bucket, object_key, destination):
            Path(destination).write_bytes(json.dumps(manifest).encode() if object_key.endswith('manifest.json') else b'synthetic')

        client.download_file.side_effect = download
        client.head_object.return_value = {'Metadata': {'sha256': '1' * 64}}
        environment = {'GOLDEN_ENVIRONMENT': 'pr-17', 'GOLDEN_VERSION': 'v1',
                       'GOLDEN_BUCKET': 'synthetic', 'GOLDEN_S3_ENDPOINT': 'http://unused',
                       'GOLDEN_TARGET_DSN': 'unused'}
        with patch.dict(os.environ, environment), patch('sys.argv', ['restore.py']), \
                patch.object(boto3, 'client', return_value=client), \
                patch.object(restore.psycopg, 'connect'), patch.object(restore, 'restore_database') as database:
            self.assertEqual(restore.main(), 0)
        client.head_object.assert_called_once_with(Bucket='synthetic', Key=key)
        self.assertEqual(database.call_count, 1)

    def test_only_staging_or_numeric_preview_is_allowed(self):
        for env in ('prod', 'production', 'pr-../prod', 'pr-0', ''):
            with self.subTest(env=env), self.assertRaises(ValueError):
                restore.check_environment(env)
        for env in ('staging', 'pr-12'):
            restore.check_environment(env)

    def test_incomplete_media_cannot_be_deployed(self):
        with self.assertRaises(ValueError):
            restore.require_complete_media({'media_complete': False})

    def test_version_does_not_allow_paths(self):
        for version in ('../prod', 'a/b', '', 'https://example.invalid'):
            with self.assertRaises(ValueError):
                restore.check_version(version)


if __name__ == '__main__':
    unittest.main(verbosity=2)

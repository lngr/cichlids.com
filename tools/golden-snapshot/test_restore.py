import unittest

import restore


class RestoreContracts(unittest.TestCase):
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

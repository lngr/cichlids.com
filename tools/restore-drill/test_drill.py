"""TASK-2.10: recovery, discrepancy detection and failure cleanup contracts."""

import tempfile
import unittest
from unittest.mock import Mock, patch

import drill


FINGERPRINT = 'measurements|9231|abc\nobservations|7000|def\ntanks|2500|ghi'


class RecoveryContract(unittest.TestCase):
    def test_restore_bootstraps_fresh_cluster_with_plugin_and_post_backup_target(self):
        with tempfile.TemporaryDirectory() as work:
            instance = drill.Drill(work)
            instance.backup_id = '20261014T120000'
            instance.expected = FINGERPRINT
            instance.apply = Mock()
            instance.sql = Mock(return_value=FINGERPRINT)
            with patch.object(drill, 'wait_for'):
                instance.restore()
            target = instance.apply.call_args.args[0][0]
            self.assertEqual(target['metadata']['name'], 'target')
            self.assertNotIn('finalizers', target['metadata'])
            spec = target['spec']
            self.assertNotIn('initdb', spec['bootstrap'])
            self.assertNotIn('plugins', spec)
            self.assertEqual(spec['bootstrap']['recovery'], {
                'source': 'source', 'recoveryTarget': {
                    'backupID': instance.backup_id, 'targetName': 'drill_complete'}})
            self.assertEqual(spec['externalClusters'][0]['plugin'], {
                'name': drill.PLUGIN, 'parameters': {
                    'barmanObjectName': 'database-backup', 'serverName': 'source'}})
            self.assertTrue(instance.target_created)

    def test_fingerprints_match_independent_of_table_order(self):
        drill.compare(FINGERPRINT, '\n'.join(reversed(FINGERPRINT.splitlines())))

    def test_discrepancies_and_incomplete_results_fail(self):
        for target in [FINGERPRINT.replace('9231', '9230'),
                       FINGERPRINT.replace('abc', 'changed'),
                       '\n'.join(FINGERPRINT.splitlines()[1:]), '',
                       FINGERPRINT + '\ntanks|2500|ghi']:
            with self.subTest(target=target), self.assertRaises(RuntimeError):
                drill.compare(FINGERPRINT, target)

    def test_source_uses_pinned_base_and_local_storage(self):
        with tempfile.TemporaryDirectory() as work:
            instance = drill.Drill(work)
            source = instance.source_manifest()
            self.assertEqual(source['spec']['imageName'],
                             'ghcr.io/cloudnative-pg/postgresql:18.6-system-trixie')
            self.assertEqual(source['spec']['bootstrap']['initdb']['database'], 'drill')
            self.assertEqual(source['spec']['storage'], {'storageClass': 'standard', 'size': '1Gi'})
            self.assertNotIn('finalizers', source['metadata'])
            self.assertNotIn('inheritedMetadata', source['spec'])

    def test_cleanup_attempts_all_resources_even_when_target_deletion_fails(self):
        with tempfile.TemporaryDirectory() as work:
            instance = drill.Drill(work)
            instance.target_created = instance.cluster_attempted = True
            instance.store_attempted = instance.network_created = True
            instance.kubectl = Mock(side_effect=RuntimeError('target API unavailable'))
            with patch.object(drill, 'run', return_value=Mock(stdout='')) as command:
                with self.assertRaisesRegex(RuntimeError, 'CLEANUP FAILED'):
                    instance.cleanup()
            calls = [call.args[0] for call in command.call_args_list]
            self.assertIn(['kind', 'delete', 'cluster', '--name', instance.name,
                           '--kubeconfig', str(instance.kubeconfig)], calls)
            self.assertIn(['docker', 'rm', '-f', '-v', instance.store], calls)
            self.assertIn(['docker', 'network', 'rm', instance.name], calls)

    def test_negative_mode_deletes_only_base_archives_and_checks_absence(self):
        with tempfile.TemporaryDirectory() as work:
            instance = drill.Drill(work, broken=True)
            archive = 'source/base/20261014T120000/data.tar.gz'
            info = 'source/base/20261014T120000/backup.info'
            instance.s3 = Mock()
            instance.objects = Mock(return_value=[info])
            instance.break_backup([archive, info])
            instance.s3.delete_object.assert_called_once_with(Bucket='restore-drill', Key=archive)
            instance.objects.return_value = [archive, info]
            with self.assertRaisesRegex(RuntimeError, 'deletion failed'):
                instance.break_backup([archive, info])
            with self.assertRaisesRegex(RuntimeError, 'requires a base archive'):
                instance.break_backup([info])

    def test_failed_recovery_job_fails_before_readiness_timeout(self):
        with tempfile.TemporaryDirectory() as work:
            instance = drill.Drill(work)
            instance.cluster_ready = Mock(return_value=False)
            instance.kubectl = Mock(return_value=Mock(stdout='{"items": ['
                '{"metadata": {"name": "target-1-full-recovery"}, "status": {"failed": 1}}]}'))
            with self.assertRaisesRegex(RuntimeError, 'Recovery Job target-1-full-recovery'):
                instance.restore_ready()


if __name__ == '__main__':
    unittest.main()

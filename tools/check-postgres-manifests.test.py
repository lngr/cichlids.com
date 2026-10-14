#!/usr/bin/env python3
"""Verify TASK-2.9 backup and environment isolation through rendered manifests."""
import copy
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
import yaml

ROOT = Path(__file__).resolve().parent.parent
FINALIZER = 'resources-finalizer.argocd.argoproj.io'
PLUGIN = 'barman-cloud.cloudnative-pg.io'


def build(environment):
    return list(yaml.safe_load_all(subprocess.check_output(
        ['kustomize', 'build', f'apps/postgres/overlays/{environment}'], cwd=ROOT, text=True)))


def policy(docs, environment):
    cluster = next(d for d in docs if d['kind'] == 'Cluster')
    spec = cluster['spec']
    assert spec['instances'] == 1
    assert spec['storage']['storageClass'] == 'local-path'
    assert spec['bootstrap'] == {'initdb': {'database': 'cichlids', 'owner': 'cichlids'}}
    assert 'externalClusters' not in spec
    assert spec['resources']['requests'] == {'cpu': '250m', 'memory': '512Mi'}
    if environment == 'preview':
        assert not cluster['metadata'].get('finalizers')
        assert 'argocd.argoproj.io/sync-options' not in cluster['metadata'].get('annotations', {})
    else:
        assert FINALIZER in cluster['metadata']['finalizers']
        assert cluster['metadata']['annotations']['argocd.argoproj.io/sync-options'] == 'Prune=false,Delete=false'
        assert spec['inheritedMetadata']['annotations'].get('argocd.argoproj.io/sync-options') == 'Prune=false,Delete=false'
        assert spec['storage']['pvcTemplate']['accessModes'] == ['ReadWriteOnce']
    if environment != 'prod':
        assert not any(d['kind'] in {'ObjectStore', 'ScheduledBackup', 'Secret'} for d in docs)
        assert 'plugins' not in spec and 'backup' not in spec
        return
    assert spec['postgresql']['parameters']['archive_timeout'] == '300s'
    assert spec['plugins'] == [{'name': PLUGIN, 'isWALArchiver': True,
                               'parameters': {'barmanObjectName': 'database-backup'}}]
    assert 'backup' not in spec
    store = next(d for d in docs if d['kind'] == 'ObjectStore')['spec']
    assert 'retentionPolicy' not in store
    config = store['configuration']
    assert config['destinationPath'] == 's3://cichlids-db-backup/'
    assert config['endpointURL'] == 'https://nbg1.your-objectstorage.com'
    assert config['s3Credentials'] == {
        'accessKeyId': {'name': 'postgres-backup-writer', 'key': 'ACCESS_KEY_ID'},
        'secretAccessKey': {'name': 'postgres-backup-writer', 'key': 'ACCESS_SECRET_KEY'}}
    assert not config['wal'].get('encryption') and not config['data'].get('encryption')
    schedules = [d['spec'] for d in docs if d['kind'] == 'ScheduledBackup']
    assert {s['schedule'] for s in schedules} == {'0 0 2 * * *', '0 0 3 1 * *'}
    assert len(schedules) == 2
    for schedule in schedules:
        assert schedule['method'] == 'plugin'
        assert schedule['pluginConfiguration'] == {'name': PLUGIN}
        assert schedule['cluster']['name'] == cluster['metadata']['name']
        assert schedule['backupOwnerReference'] == 'none'


class Postgres(unittest.TestCase):
    def test_rendered_backup_and_isolation(self):
        for environment in ['prod', 'staging', 'preview']:
            with self.subTest(environment=environment):
                policy(build(environment), environment)

    def test_policy_rejects_backup_and_protection_regressions(self):
        docs = build('prod')
        cluster = next(d for d in docs if d['kind'] == 'Cluster')
        store = next(d for d in docs if d['kind'] == 'ObjectStore')
        mutations = [lambda: cluster['spec']['plugins'].clear(),
                     lambda: cluster['spec']['postgresql']['parameters'].update(archive_timeout='600s'),
                     lambda: cluster['metadata']['finalizers'].clear(),
                     lambda: cluster['spec']['inheritedMetadata']['annotations'].clear(),
                     lambda: store['spec'].update(retentionPolicy='14d'),
                     lambda: docs.pop(next(i for i,d in enumerate(docs) if d['kind']=='ScheduledBackup'))]
        for mutate in mutations:
            saved = copy.deepcopy(docs)
            mutate()
            with self.assertRaises(AssertionError):
                policy(docs, 'prod')
            docs = saved
            cluster = next(d for d in docs if d['kind'] == 'Cluster')
            store = next(d for d in docs if d['kind'] == 'ObjectStore')

    def test_nonproduction_rejects_production_access(self):
        for environment in ['staging', 'preview']:
            for key, value in [('plugins', [{'name': PLUGIN}]),
                               ('externalClusters', [{'name': 'production'}])]:
                docs = build(environment)
                next(d for d in docs if d['kind'] == 'Cluster')['spec'][key] = value
                with self.assertRaises(AssertionError):
                    policy(docs, environment)

    def test_schemas_reject_unknown_fields(self):
        docs = build('prod')
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'rendered.yaml'
            command = ['kubeconform', '-strict', '-summary', '-schema-location',
                       str(ROOT / 'tools/manifest-schemas/{{.ResourceKind}}{{.KindSuffix}}.json'), str(path)]
            path.write_text(yaml.safe_dump_all(docs))
            result = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout)
            for doc in docs:
                doc['spec']['unknownField'] = True
            path.write_text(yaml.safe_dump_all(docs))
            result = subprocess.run(command, capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('Invalid: 4', result.stdout)


if __name__ == '__main__':
    unittest.main()

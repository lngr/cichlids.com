#!/usr/bin/env python3
"""TASK-2.10: synthetic base-backup and WAL recovery in an isolated kind cluster."""

import argparse
import copy
import json
import os
from pathlib import Path
import secrets
import signal
import subprocess
import sys
import tempfile
import time

import boto3
from botocore.config import Config
import yaml

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
STORE_IMAGE = 'rustfs/rustfs@sha256:fa19210ac4697c79d7ccca1ec9b0eb91aebacc6691991ffb14014bb3c67e6cc3'
PLUGIN = 'barman-cloud.cloudnative-pg.io'
TABLES = {'tanks', 'observations', 'measurements'}


def run(args, *, stdin=None, check=True, timeout=120):
    result = subprocess.run(args, input=stdin, text=True, capture_output=True, timeout=timeout)
    if check and result.returncode:
        raise RuntimeError(f"{' '.join(args[:4])}: {result.stderr.strip()} {result.stdout.strip()}")
    return result


def wait_for(label, predicate, timeout=600):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            print(f'READY: {label}', flush=True)
            return
        time.sleep(3)
    raise RuntimeError(f'Timeout waiting for {label} ({timeout}s)')


def compare(source, target):
    def parse(value):
        rows = {}
        for line in value.strip().splitlines():
            table, count, checksum = line.split('|')
            if table in rows or not checksum or int(count) < 1000:
                raise RuntimeError(f'Invalid fingerprint: {line}')
            rows[table] = (int(count), checksum)
        if set(rows) != TABLES:
            raise RuntimeError(f'Missing tables in fingerprint: {rows}')
        return rows

    expected, actual = parse(source), parse(target)
    for table in sorted(TABLES):
        print(f'COMPARE {table}: source={expected[table]} target={actual[table]}', flush=True)
    different = [table for table in sorted(TABLES) if expected[table] != actual[table]]
    if different:
        raise RuntimeError(f'Row count/checksum discrepancy: {", ".join(different)}')


class Drill:
    def __init__(self, work, broken=False):
        self.name = 'restore-drill-' + secrets.token_hex(5)
        self.store = self.name + '-s3'
        self.work = Path(work)
        self.kubeconfig = self.work / 'kubeconfig'
        self.broken = broken
        self.target_created = False
        self.cluster_attempted = False
        self.store_attempted = False
        self.network_created = False
        self.env = dict(os.environ, KIND_EXPERIMENTAL_DOCKER_NETWORK=self.name)
        self.key = secrets.token_hex(12)
        self.password = secrets.token_hex(24)
        self.s3 = None

    def kubectl(self, *args, stdin=None, check=True, timeout=120):
        return run(['kubectl', '--kubeconfig', str(self.kubeconfig), '--context',
                    'kind-' + self.name, '--request-timeout=15s', *args],
                   stdin=stdin, check=check, timeout=timeout)

    def apply(self, documents):
        self.kubectl('apply', '--server-side', '-f', '-', stdin=yaml.safe_dump_all(documents))

    def get(self, kind, name):
        result = self.kubectl('-n', 'drill', 'get', kind, name, '-o', 'json', check=False)
        return json.loads(result.stdout) if result.returncode == 0 else {}

    def cluster_ready(self, name):
        return any(c['type'] == 'Ready' and c['status'] == 'True'
                   for c in self.get('cluster', name).get('status', {}).get('conditions', []))

    def setup(self):
        print(f'RESOURCES: kind={self.name} store={self.store} network={self.name}', flush=True)
        self.network_created = True
        run(['docker', 'network', 'create', self.name])
        self.store_attempted = True
        run(['docker', 'run', '-d', '--name', self.store, '--network', self.name,
             '-p', '127.0.0.1::9000', '-e', f'RUSTFS_ACCESS_KEY={self.key}',
             '-e', f'RUSTFS_SECRET_KEY={self.password}', STORE_IMAGE])
        info = json.loads(run(['docker', 'inspect', self.store]).stdout)[0]
        port = info['NetworkSettings']['Ports']['9000/tcp'][0]['HostPort']
        address = info['NetworkSettings']['Networks'][self.name]['IPAddress']
        self.endpoint = f'http://{address}:9000'
        self.s3 = boto3.client('s3', endpoint_url=f'http://127.0.0.1:{port}',
                               aws_access_key_id=self.key, aws_secret_access_key=self.password,
                               region_name='us-east-1', config=Config(
                                   connect_timeout=3, read_timeout=5, retries={'max_attempts': 0},
                                   s3={'addressing_style': 'path'}))

        def store_ready():
            try:
                self.s3.list_buckets()
                return True
            except Exception:
                return False

        wait_for('local S3', store_ready, 120)
        self.s3.create_bucket(Bucket='restore-drill')
        self.cluster_attempted = True
        result = subprocess.run(['kind', 'create', 'cluster', '--name', self.name,
                                 '--kubeconfig', str(self.kubeconfig), '--wait', '180s'],
                                env=self.env, text=True, timeout=300)
        if result.returncode:
            raise RuntimeError('kind creation failed')
        for addon, namespace in [('cert-manager', 'cert-manager'),
                                  ('cloudnative-pg', 'cnpg-system'), ('barman-cloud', 'cnpg-system')]:
            documents = list(yaml.safe_load_all(run(
                ['kustomize', 'build', str(ROOT / 'apps' / addon / 'base')]).stdout))
            self.apply(documents)
            deployments = self.kubectl('-n', namespace, 'get', 'deployment', '-o', 'name').stdout.splitlines()
            for deployment in deployments:
                self.kubectl('-n', namespace, 'rollout', 'status', deployment,
                             '--timeout=300s', timeout=320)
            print(f'ADDON READY: {addon}', flush=True)
            if addon == 'barman-cloud':
                self.kubectl('-n', namespace, 'wait', 'certificate', '--all',
                             '--for=condition=Ready', '--timeout=120s', timeout=140)
        self.kubectl('create', 'namespace', 'drill')
        self.kubectl('-n', 'drill', 'create', 'secret', 'generic', 'postgres-backup-writer',
                     '--from-literal=ACCESS_KEY_ID=' + self.key,
                     '--from-literal=ACCESS_SECRET_KEY=' + self.password)

    def source_manifest(self):
        cluster = yaml.safe_load(run(['kustomize', 'build', str(ROOT / 'apps/postgres/base')]).stdout)
        cluster['metadata'] = {'name': 'source', 'namespace': 'drill'}
        spec = cluster['spec']
        spec.pop('inheritedMetadata', None)
        spec['bootstrap']['initdb'] = {'database': 'drill', 'owner': 'drill'}
        spec['storage'] = {'storageClass': 'standard', 'size': '1Gi'}
        patch = yaml.safe_load((ROOT / 'apps/postgres/overlays/prod/backup-patch.yaml').read_text())
        spec.update(copy.deepcopy(patch['spec']))
        return cluster

    def object_store(self):
        store = yaml.safe_load((ROOT / 'apps/postgres/overlays/prod/object-store.yaml').read_text())
        store['metadata'] = {'name': 'database-backup', 'namespace': 'drill'}
        store['spec']['configuration'].update(destinationPath='s3://restore-drill/', endpointURL=self.endpoint)
        return store

    def sql(self, cluster, sql):
        pod = self.get('cluster', cluster)['status']['currentPrimary']
        return self.kubectl('-n', 'drill', 'exec', '-i', pod, '-c', 'postgres', '--',
                            'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-U', 'postgres',
                            '-d', 'drill', '-At', stdin=sql).stdout.strip()

    def objects(self, prefix):
        pages = self.s3.get_paginator('list_objects_v2').paginate(Bucket='restore-drill', Prefix=prefix)
        return [obj['Key'] for page in pages for obj in page.get('Contents', [])]

    def backup(self):
        self.apply([self.object_store(), self.source_manifest()])
        wait_for('source database', lambda: self.cluster_ready('source'))
        self.sql('source', (HERE / 'seed.sql').read_text())
        print('SEED: tanks=2000 observations=6000 measurements=8000', flush=True)
        schedule = next(yaml.safe_load_all((ROOT / 'apps/postgres/overlays/prod/scheduled-backups.yaml').read_text()))
        self.apply([{'apiVersion': 'postgresql.cnpg.io/v1', 'kind': 'Backup',
                     'metadata': {'name': 'base', 'namespace': 'drill'},
                     'spec': {'cluster': {'name': 'source'}, 'method': schedule['spec']['method'],
                              'pluginConfiguration': schedule['spec']['pluginConfiguration']}}])

        def completed():
            status = self.get('backup', 'base').get('status', {})
            if status.get('phase') == 'failed':
                raise RuntimeError(f'Base backup failed: {status.get("error")}')
            return status.get('phase') == 'completed'

        wait_for('base backup', completed)
        metadata = self.objects('source/base/')
        ids = sorted({key.split('/')[2] for key in metadata if key.endswith('/backup.info')})
        if len(ids) != 1 or not any(key.endswith('.tar.gz') for key in metadata):
            raise RuntimeError(f'Expected one base backup with archive and metadata, found {metadata}')
        self.backup_id = ids[0]
        print(f'BASE BACKUP: {self.backup_id}, objects={len(metadata)}', flush=True)
        self.sql('source', (HERE / 'writes.sql').read_text())
        self.expected = self.sql('source', (HERE / 'fingerprint.sql').read_text())
        print('SOURCE AFTER BACKUP:\n' + self.expected, flush=True)
        self.sql('source', "SELECT pg_create_restore_point('drill_complete');")
        segment = self.sql('source', 'SELECT pg_walfile_name(pg_current_wal_lsn());')
        self.sql('source', 'SELECT pg_switch_wal();')
        wait_for('post-backup WAL ' + segment,
                 lambda: any(Path(key).name == segment + '.gz' for key in self.objects('source/wals/')), 180)
        if self.broken:
            self.break_backup(metadata)

    def break_backup(self, metadata):
        archives = [key for key in metadata if key.endswith('.tar.gz')]
        if not archives:
            raise RuntimeError('Negative mode requires a base archive to delete')
        for key in archives:
            self.s3.delete_object(Bucket='restore-drill', Key=key)
        if any(key in self.objects('source/base/') for key in archives):
            raise RuntimeError('Negative-mode base archive deletion failed')
        print(f'BROKEN BACKUP: deleted {len(archives)} base archive(s); metadata and WAL retained', flush=True)

    def restore(self):
        target = self.source_manifest()
        target['metadata']['name'] = 'target'
        spec = target['spec']
        spec.pop('plugins')
        spec['bootstrap'] = {'recovery': {
            'source': 'source', 'recoveryTarget': {
                'backupID': self.backup_id, 'targetName': 'drill_complete'}}}
        spec['externalClusters'] = [{'name': 'source', 'plugin': {
            'name': PLUGIN, 'parameters': {
                'barmanObjectName': 'database-backup', 'serverName': 'source'}}}]
        started = time.monotonic()
        self.target_created = True
        try:
            self.apply([target])
            wait_for('restored target database', self.restore_ready, 600)
            compare(self.expected, self.sql('target', (HERE / 'fingerprint.sql').read_text()))
        except Exception as error:
            print(f'RESTORE DURATION: {time.monotonic() - started:.2f}s (failed)', flush=True)
            raise RuntimeError(f'RESTORE FAILED: {error}') from error
        print(f'RESTORE DURATION: {time.monotonic() - started:.2f}s (RTO indication)', flush=True)

    def restore_ready(self):
        if self.cluster_ready('target'):
            return True
        jobs = json.loads(self.kubectl('-n', 'drill', 'get', 'jobs',
                                      '-l', 'cnpg.io/cluster=target', '-o', 'json').stdout)
        for job in jobs.get('items', []):
            if job.get('status', {}).get('failed', 0):
                raise RuntimeError(f'Recovery Job {job["metadata"]["name"]} has a failed pod')
        return False

    def diagnostics(self):
        if not self.kubeconfig.exists():
            return
        for args in [('-n', 'drill', 'get', 'clusters,backups,pods,jobs', '-o', 'wide'),
                     ('-n', 'drill', 'get', 'events', '--field-selector=type=Warning')]:
            try:
                print(self.kubectl(*args, check=False).stdout, flush=True)
            except Exception as error:
                print(f'Diagnostics unavailable: {error}', flush=True)
        try:
            pods = json.loads(self.kubectl('-n', 'drill', 'get', 'pods', '-o', 'json').stdout)
            for pod in pods.get('items', []):
                if pod['metadata']['name'].startswith('target'):
                    print(self.kubectl('-n', 'drill', 'logs', pod['metadata']['name'],
                                       '--all-containers', '--tail=30', check=False).stdout, flush=True)
        except Exception as error:
            print(f'Restore logs unavailable: {error}', flush=True)

    def cleanup(self):
        errors = []

        def attempt(action):
            try:
                action()
            except Exception as error:
                errors.append(str(error))

        def delete_target():
            self.kubectl('-n', 'drill', 'delete', 'cluster', 'target', '--ignore-not-found',
                         '--wait=true', '--timeout=90s', timeout=110)
            target = self.kubectl('-n', 'drill', 'get', 'cluster', 'target',
                                  '--ignore-not-found', '-o', 'name')
            if target.stdout.strip():
                raise RuntimeError('target Cluster still exists')
            print('CLEANUP: target Cluster absent', flush=True)

        if self.target_created:
            attempt(delete_target)
        if self.cluster_attempted:
            attempt(lambda: run(['kind', 'delete', 'cluster', '--name', self.name,
                                 '--kubeconfig', str(self.kubeconfig)], timeout=180))
        if self.store_attempted:
            attempt(lambda: run(['docker', 'rm', '-f', '-v', self.store]))
        if self.network_created:
            attempt(lambda: run(['docker', 'network', 'rm', self.name]))

        def assert_absent():
            if self.name in run(['kind', 'get', 'clusters']).stdout.splitlines():
                raise RuntimeError(f'kind cluster survives: {self.name}')
            containers = run(['docker', 'ps', '-a', '--format', '{{.Names}}']).stdout.splitlines()
            if any(name == self.store or name.startswith(self.name + '-') for name in containers):
                raise RuntimeError(f'Drill containers survive: {containers}')
            if self.name in run(['docker', 'network', 'ls', '--format', '{{.Name}}']).stdout.splitlines():
                raise RuntimeError(f'Drill network survives: {self.name}')
            print(f'CLEANUP VERIFIED: kind={self.name} target=absent containers=absent network=absent', flush=True)

        attempt(assert_absent)
        if errors:
            raise RuntimeError('CLEANUP FAILED: ' + '; '.join(errors))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--break-backup', action='store_true', help='delete base archives before recovery')
    args = parser.parse_args()

    def interrupted(signum, frame):
        raise RuntimeError(f'Drill interrupted by signal {signum}')

    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    status = 0
    with tempfile.TemporaryDirectory(prefix='restore-drill-') as work:
        os.chmod(work, 0o700)
        drill = Drill(work, args.break_backup)
        try:
            drill.setup()
            drill.backup()
            drill.restore()
            print('RESTORE PASSED: all table counts and checksums match', flush=True)
        except Exception as error:
            print(f'DRILL FAILED: {error}', file=sys.stderr, flush=True)
            drill.diagnostics()
            status = 1
        finally:
            signal.signal(signal.SIGTERM, signal.SIG_IGN)
            signal.signal(signal.SIGINT, signal.SIG_IGN)
            try:
                drill.cleanup()
            except Exception as error:
                print(error, file=sys.stderr, flush=True)
                status = 1
    return status


if __name__ == '__main__':
    sys.exit(main())

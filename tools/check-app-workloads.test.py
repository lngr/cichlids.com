#!/usr/bin/env python3
"""Verify TASK-2.31 application delivery with offline rendered manifests."""
from pathlib import Path
import subprocess
import unittest
import yaml

ROOT = Path(__file__).resolve().parent.parent


def build(service, environment):
    return list(yaml.safe_load_all(subprocess.check_output(
        ['kustomize', 'build', f'apps/{service}/overlays/{environment}'], cwd=ROOT, text=True)))


def resource(docs, kind):
    return next(d for d in docs if d['kind'] == kind)


class ApplicationWorkloads(unittest.TestCase):
    def test_given_staging_when_rendered_then_workloads_are_hardened(self):
        for environment in ['staging', 'prod']:
            for service in ['api', 'web']:
                with self.subTest(environment=environment, service=service):
                    docs = build(service, environment)
                    deployment = resource(docs, 'Deployment')
                    self.assertEqual(deployment['spec']['replicas'], 1 if environment == 'staging' else 0)
                    pod = deployment['spec']['template']['spec']
                    self.assertFalse(pod['automountServiceAccountToken'])
                    self.assertTrue(pod['securityContext']['runAsNonRoot'])
                    self.assertEqual(pod['securityContext']['seccompProfile']['type'], 'RuntimeDefault')
                    container = pod['containers'][0]
                    self.assertRegex(container['image'], r'@sha256:[0-9a-f]{64}$')
                    self.assertTrue(container['securityContext']['readOnlyRootFilesystem'])
                    self.assertFalse(container['securityContext']['allowPrivilegeEscalation'])
                    self.assertEqual(container['securityContext']['capabilities']['drop'], ['ALL'])
                    for field in ['requests', 'limits']:
                        self.assertEqual(set(container['resources'][field]), {'cpu', 'memory'})
                    for probe in ['readinessProbe', 'livenessProbe']:
                        self.assertEqual(container[probe]['httpGet']['path'], '/healthz' if service == 'api' else '/')
                    svc = resource(docs, 'Service')['spec']
                    self.assertEqual(svc['selector'], deployment['spec']['selector']['matchLabels'])
                    self.assertEqual(svc['ports'][0]['targetPort'], 'http')
                    if service == 'api':
                        job = resource(docs, 'Job')
                        migration = job['spec']['template']['spec']['containers'][0]
                        self.assertEqual(migration['args'], ['migrate'])
                        self.assertTrue(migration['securityContext']['readOnlyRootFilesystem'])
                        env = {e['name']: e for e in migration['env']}
                        self.assertIn('Passfile=/db/pgpass', env['CICHLIDS_ETL_TARGET_CONNECTION']['value'])
                        db = next(v for v in job['spec']['template']['spec']['volumes'] if v['name'] == 'database')
                        self.assertEqual(db['secret'], {'secretName': 'postgres-app', 'items': [{'key': 'pgpass', 'path': 'pgpass'}]})
                        self.assertEqual(job['spec']['activeDeadlineSeconds'], 300)
                        if environment == 'staging':
                            self.assertEqual(job['metadata']['annotations']['argocd.argoproj.io/hook'], 'PreSync')
                        else:
                            self.assertTrue(job['spec']['suspend'])
                        env = {e['name']: e for e in container['env']}
                        self.assertIn('Host=postgres-rw;', env['ConnectionStrings__Cichlids']['value'])
                        self.assertIn('Passfile=/db/pgpass', env['ConnectionStrings__Cichlids']['value'])
                        self.assertEqual(env['CICHLIDS_SLUG_SECRET']['valueFrom']['secretKeyRef'],
                                         {'name': 'api-slugs', 'key': 'secret'})
                        settings = resource(docs, 'ConfigMap')['data']
                        for name, key in [('WebPush__VapidPublicKey', 'public-key'), ('WebPush__VapidPrivateKey', 'private-key')]:
                            self.assertEqual(env[name]['valueFrom']['secretKeyRef'],
                                             {'name': 'api-web-push', 'key': key, 'optional': True})
                        self.assertEqual(settings['WebPush__Enabled'], 'false')
                        self.assertEqual(settings['ASPNETCORE_FORWARDEDHEADERS_ENABLED'], 'true')
                        self.assertEqual(settings['Authentication__Audience'], 'cichlids-api')
                        self.assertEqual(settings['OutboxDispatcher__Enabled'], 'false')
                        if environment == 'staging':
                            self.assertEqual(env['WebSession__ClientSecret']['valueFrom']['secretKeyRef'],
                                             {'name': 'keycloak-web-bff', 'key': 'client-secret'})
                            self.assertEqual(settings['WebSession__CallbackUrl'], 'https://staging.cichlids.com/api/auth/callback')
                        else:
                            self.assertNotIn('WebSession__ClientSecret', env)
                            self.assertNotIn('WebSession__CallbackUrl', settings)
                        keyring = next(v for v in pod['volumes'] if v['name'] == 'key-ring')
                        self.assertEqual(keyring['secret']['secretName'], 'api-data-protection')
                        self.assertTrue(next(v for v in container['volumeMounts'] if v['name'] == 'key-ring')['readOnly'])

    def test_given_staging_when_routed_then_api_prefix_and_tls_are_preserved(self):
        docs = build('web', 'staging')
        ingress = resource(docs, 'Ingress')
        self.assertEqual(ingress['metadata']['annotations']['cert-manager.io/cluster-issuer'], 'letsencrypt-production')
        self.assertEqual(ingress['spec']['ingressClassName'], 'traefik')
        self.assertEqual(ingress['spec']['tls'], [{'hosts': ['staging.cichlids.com'], 'secretName': 'web-tls'}])
        rule = ingress['spec']['rules'][0]
        self.assertEqual(rule['host'], 'staging.cichlids.com')
        self.assertEqual({p['path']: p['backend']['service']['name'] for p in rule['http']['paths']},
                         {'/api': 'api', '/': 'web'})
        self.assertEqual(ingress['metadata']['annotations']['traefik.ingress.kubernetes.io/router.middlewares'], 'tailnet-only@file')
        self.assertFalse((ROOT / 'apps/placeholder/overlays/staging/kustomization.yaml').exists())
        self.assertFalse(any(d['kind'] == 'Ingress' for d in build('web', 'prod')))


if __name__ == '__main__':
    unittest.main()

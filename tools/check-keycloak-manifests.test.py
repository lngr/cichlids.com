#!/usr/bin/env python3
"""Verify TASK-2.30 deployment, secret boundaries and realm rendering offline."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import yaml

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parent.parent


def build(path):
    return list(yaml.safe_load_all(subprocess.check_output(
        ['kustomize', 'build', str(ROOT / path)], text=True)))


def resource(docs, kind, name):
    return next(d for d in docs if d['kind'] == kind and d['metadata']['name'] == name)


class Keycloak(unittest.TestCase):
    def test_deployment_and_public_path_boundary(self):
        for env, host in [('staging', 'auth.staging.cichlids.com'), ('prod', 'auth.cichlids.com')]:
            with self.subTest(env=env):
                docs = build(f'apps/keycloak/overlays/{env}')
                self.assertTrue(all(d['metadata']['namespace'] == f'cichlids-{env}' for d in docs))
                deployment = resource(docs, 'Deployment', 'keycloak')['spec']
                self.assertEqual(deployment['replicas'], 1)
                self.assertEqual(deployment['strategy']['type'], 'Recreate')
                pod = deployment['template']['spec']
                self.assertFalse(pod['automountServiceAccountToken'])
                container = pod['containers'][0]
                self.assertRegex(container['image'], r':26\.6\.4@sha256:[0-9a-f]{64}$')
                self.assertEqual(container['args'], ['start'])
                settings = {e['name']: e.get('value', e.get('valueFrom')) for e in container['env']}
                self.assertEqual(settings['KC_HOSTNAME'], f'https://{host}')
                self.assertEqual(settings['KC_PROXY_HEADERS'], 'xforwarded')
                self.assertEqual(settings['KC_HOSTNAME_ADMIN'], 'http://localhost:18080')
                self.assertEqual(settings['KC_HTTP_ENABLED'], 'true')
                self.assertEqual(settings['KC_DB'], 'postgres')
                self.assertEqual(settings['KC_HEALTH_ENABLED'], 'true')
                self.assertEqual(settings['KC_METRICS_ENABLED'], 'true')
                self.assertEqual(settings['KC_DB_URL'], 'jdbc:postgresql://postgres-rw:5432/keycloak')
                for field, secret, key in [('KC_DB_USERNAME', 'keycloak-db', 'username'),
                                           ('KC_DB_PASSWORD', 'keycloak-db', 'password'),
                                           ('KC_BOOTSTRAP_ADMIN_USERNAME', 'keycloak-admin', 'username'),
                                           ('KC_BOOTSTRAP_ADMIN_PASSWORD', 'keycloak-admin', 'password')]:
                    self.assertEqual(settings[field], {'secretKeyRef': {'name': secret, 'key': key}})
                self.assertEqual(container['resources']['requests'], {'cpu': '250m', 'memory': '768Mi'})
                for probe, path in [('readinessProbe', '/health/ready'), ('livenessProbe', '/health/live'),
                                    ('startupProbe', '/health/started')]:
                    self.assertEqual(container[probe]['httpGet'], {'path': path, 'port': 'management'})
                service = resource(docs, 'Service', 'keycloak')['spec']
                self.assertEqual(service['type'], 'ClusterIP')
                self.assertEqual(service['selector'], deployment['selector']['matchLabels'])
                self.assertEqual(service['ports'], [{'name': 'http', 'port': 8080, 'targetPort': 'http'}])
                ingress = resource(docs, 'Ingress', 'keycloak')
                self.assertEqual(ingress['spec']['ingressClassName'], 'traefik')
                self.assertEqual(ingress['metadata']['annotations']['traefik.ingress.kubernetes.io/router.entrypoints'], 'websecure')
                self.assertEqual(ingress['metadata']['annotations']['cert-manager.io/cluster-issuer'], 'letsencrypt-production')
                self.assertEqual(ingress['spec']['tls'], [{'hosts': [host], 'secretName': 'keycloak-tls'}])
                rule = ingress['spec']['rules'][0]
                self.assertEqual(rule['host'], host)
                self.assertEqual({p['path'] for p in rule['http']['paths']}, {'/realms/cichlids', '/resources/', '/js/'})
                for path in rule['http']['paths']:
                    self.assertEqual(path['pathType'], 'Prefix')
                    self.assertEqual(path['backend']['service'], {'name': 'keycloak', 'port': {'number': 8080}})
                self.assertFalse(any(d['kind'] in {'Secret', 'Cluster', 'PersistentVolumeClaim'} for d in docs))

    def test_postsync_hook_uses_source_realm_and_safe_apply_settings(self):
        for env in ['staging', 'prod']:
            docs = build(f'apps/keycloak/overlays/{env}')
            job = resource(docs, 'Job', 'keycloak-realm-apply')
            self.assertEqual(job['metadata']['annotations']['argocd.argoproj.io/hook'], 'PostSync')
            self.assertEqual(job['metadata']['annotations']['argocd.argoproj.io/hook-delete-policy'], 'BeforeHookCreation,HookSucceeded')
            self.assertEqual(job['spec']['backoffLimit'], 0)
            self.assertLessEqual(job['spec']['activeDeadlineSeconds'], 600)
            pod = job['spec']['template']['spec']
            self.assertFalse(pod['automountServiceAccountToken'])
            cli = pod['containers'][0]
            self.assertRegex(cli['image'], r':6\.5\.1-26@sha256:[0-9a-f]{64}$')
            settings = {e['name']: e.get('value', e.get('valueFrom')) for e in cli['env']}
            self.assertEqual(settings['KEYCLOAK_URL'], 'http://keycloak:8080')
            self.assertEqual(settings['IMPORT_FILES_LOCATIONS'], '/rendered/cichlids-realm.json')
            self.assertEqual(settings['IMPORT_CACHE_ENABLED'], 'false')
            self.assertEqual(settings['IMPORT_REMOTESTATE_ENABLED'], 'false')
            self.assertEqual(settings['IMPORT_VARSUBSTITUTION_ENABLED'], 'false')
            for key in ['CLIENT', 'ROLE', 'IDENTITYPROVIDER', 'CLIENTSCOPE', 'COMPONENT', 'REQUIREDACTION']:
                self.assertEqual(settings[f'IMPORT_MANAGED_{key}'], 'no-delete')
            init = pod['initContainers'][0]
            self.assertRegex(init['image'], r'@sha256:[0-9a-f]{64}$')
            self.assertEqual(init['command'], ['python3', '/scripts/render-realm.py'])
            volumes = {v['name']: v for v in pod['volumes']}
            realm = resource(docs, 'ConfigMap', volumes['realm']['configMap']['name'])['data']['cichlids-realm.json']
            self.assertEqual(json.loads(realm), json.loads((ROOT / f'apps/keycloak/overlays/{env}/cichlids-realm.json').read_text()))
            self.assertEqual(volumes['rendered']['emptyDir']['medium'], 'Memory')
            self.assertEqual(volumes['smtp']['secret']['secretName'], 'keycloak-smtp')
            self.assertTrue(volumes['providers']['secret']['optional'])
            self.assertEqual(volumes['providers']['secret']['secretName'], 'keycloak-providers')

    def test_database_owner_backup_and_preview_isolation(self):
        for env in ['staging', 'prod']:
            docs = build(f'apps/postgres/overlays/{env}')
            cluster = resource(docs, 'Cluster', 'postgres')
            self.assertIn({'name': 'keycloak', 'ensure': 'present', 'login': True,
                           'passwordSecret': {'name': 'keycloak-db'}}, cluster['spec']['managed']['roles'])
            database = resource(docs, 'Database', 'keycloak')
            self.assertEqual(database['spec'], {'name': 'keycloak', 'owner': 'keycloak', 'cluster': {'name': 'postgres'}, 'ensure': 'present', 'databaseReclaimPolicy': 'retain'})
            self.assertEqual(database['metadata']['annotations']['argocd.argoproj.io/sync-options'], 'Prune=false,Delete=false')
            self.assertIn('resources-finalizer.argocd.argoproj.io', database['metadata']['finalizers'])
            if env == 'prod':
                self.assertEqual(len([d for d in docs if d['kind'] == 'ObjectStore']), 1)
                self.assertTrue(cluster['spec']['plugins'][0]['isWALArchiver'])
            else:
                self.assertNotIn('plugins', cluster['spec'])
        preview = build('apps/postgres/overlays/preview')
        self.assertFalse(any(d['kind'] == 'Database' for d in preview))
        self.assertNotIn('managed', resource(preview, 'Cluster', 'postgres')['spec'])
        self.assertFalse((ROOT / 'apps/keycloak/overlays/preview').exists())

    def renderer(self):
        spec = importlib.util.spec_from_file_location('render_realm', ROOT / 'apps/keycloak/base/render-realm.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module.render

    def test_render_secret_escaping_and_omitted_provider_placeholders(self):
        render = self.renderer()
        source = json.loads((ROOT / 'apps/keycloak/overlays/staging/cichlids-realm.json').read_text())
        smtp = {'host': 'smtp.example.org', 'port': '587', 'user': 'mailer', 'password': 'quote"\\newline\n', 'from': 'auth@example.org'}
        realm = render(source, smtp, {})
        self.assertNotIn('registrationAllowed', realm)
        self.assertNotIn('users', realm)
        self.assertEqual(realm['smtpServer']['password'], smtp['password'])
        self.assertEqual(realm['smtpServer']['starttls'], 'true')
        self.assertTrue(all(p['enabled'] is False and p['config'] == {} for p in realm['identityProviders']))
        self.assertNotIn('not-configured', json.dumps(realm))
        self.assertEqual(source['identityProviders'][0]['config']['clientSecret'], 'not-configured')
        providers = {'google-client-id': 'google-id', 'google-client-secret': 'google-secret'}
        realm = render(source, smtp, providers)
        self.assertEqual(realm['identityProviders'][0]['config'], {'clientId': 'google-id', 'clientSecret': 'google-secret'})
        self.assertFalse(realm['identityProviders'][0]['enabled'])
        self.assertNotIn('secret', next(c for c in realm['clients'] if c['clientId'] == 'cichlids-web-bff'))

    def test_render_fails_closed_on_missing_or_unresolved_secrets_and_unsafe_source(self):
        render = self.renderer()
        source = json.loads((ROOT / 'apps/keycloak/overlays/staging/cichlids-realm.json').read_text())
        smtp = {'host': 'smtp.example.org', 'port': '587', 'user': 'mailer', 'password': 'test-only', 'from': 'auth@example.org'}
        for key in smtp:
            for value in ['', 'not-configured', '${SMTP_PASSWORD}', '$(env:SMTP_PASSWORD)']:
                with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                    render(source, {**smtp, key: value}, {})
            with self.assertRaises(ValueError):
                render(source, {k: v for k, v in smtp.items() if k != key}, {})
        for key in ['registrationAllowed', 'users', 'credentials']:
            with self.subTest(key=key), self.assertRaises(ValueError):
                render({**source, key: False}, smtp, {})
        with self.assertRaises(ValueError):
            render(source, smtp, {'google-client-id': 'id'})
        with self.assertRaises(ValueError):
            render(source, smtp, {'google-client-id': 'id', 'google-client-secret': '${MISSING}'})
        enabled = copy.deepcopy(source)
        enabled['identityProviders'][0]['enabled'] = True
        with self.assertRaises(ValueError):
            render(enabled, smtp, {})


if __name__ == '__main__':
    unittest.main()

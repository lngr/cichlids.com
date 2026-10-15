#!/usr/bin/env python3
"""Verify TASK-2.32 isolation, public ACME and recoverable coordination state."""
import importlib.util
from pathlib import Path
import unittest
import sys
sys.dont_write_bytecode = True
import yaml

spec = importlib.util.spec_from_file_location('edge', Path(__file__).with_name('check-edge-manifests.test.py'))
edge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(edge)
build, resource = edge.build, edge.resource


class Tailnet(unittest.TestCase):
    def test_private_routes_and_public_production(self):
        ingress = resource(build('apps/web/overlays/staging'), 'Ingress', 'web')
        self.assertEqual(ingress['metadata']['annotations']['traefik.ingress.kubernetes.io/router.middlewares'], 'tailnet-only@file')
        ingress = resource(build('apps/placeholder/overlays/preview'), 'Ingress', 'placeholder')
        self.assertEqual(ingress['metadata']['annotations']['traefik.ingress.kubernetes.io/router.middlewares'], 'tailnet-only@file')
        for doc in build('apps/placeholder/overlays/prod'):
            if doc['kind'] == 'Ingress':
                self.assertNotIn('traefik.ingress.kubernetes.io/router.middlewares', doc['metadata']['annotations'])
        for path in ['apps/api/overlays/staging', 'apps/web/overlays/staging', 'apps/placeholder/overlays/preview']:
            for doc in build(path):
                if doc['kind'] == 'Ingress':
                    self.assertEqual(doc['metadata']['annotations']['traefik.ingress.kubernetes.io/router.middlewares'], 'tailnet-only@file')

    def test_source_ip_and_acme_exception(self):
        values = yaml.safe_load(resource(build('apps/traefik/overlays/cluster'), 'HelmChartConfig', 'traefik')['spec']['valuesContent'])
        self.assertEqual(values['service']['spec']['externalTrafficPolicy'], 'Local')
        http = yaml.safe_load(values['providers']['file']['content'])['http']
        self.assertEqual(http['middlewares']['tailnet-only'], {'ipAllowList': {'sourceRange': ['100.64.0.0/10', 'fd7a:115c:a1e0::/48']}})
        self.assertEqual(http['routers']['https-redirect']['rule'], '!PathPrefix(`/.well-known/acme-challenge/`)')
        self.assertEqual(http['routers']['https-redirect']['middlewares'], ['https-redirect'])
        solver = resource(build('apps/cert-manager/overlays/cluster'), 'ClusterIssuer', 'letsencrypt-production')['spec']['acme']['solvers'][0]['http01']['ingress']
        self.assertNotIn('ingressTemplate', solver)
        self.assertNotIn('node-external-ip', Path('infra/app/scripts/install-k3s.sh').read_text())

    def test_headscale_state_relay_and_ownership(self):
        docs = build('apps/headscale/overlays/cluster')
        topology = build('clusters/cichlids/apps')
        app = resource(topology, 'Application', 'headscale')
        self.assertEqual(app['spec']['project'], 'cichlids-addons')
        self.assertFalse(app['spec']['syncPolicy']['automated']['prune'])
        self.assertIn(edge.check.FINALIZER, app['metadata']['finalizers'])
        for kind, name in [('Namespace', 'headscale'), ('PersistentVolume', 'headscale'), ('PersistentVolumeClaim', 'headscale'), ('StatefulSet', 'headscale')]:
            self.assertEqual(resource(docs, kind, name)['metadata']['annotations']['argocd.argoproj.io/sync-options'], 'Prune=false,Delete=false')
        self.assertEqual(resource(docs, 'PersistentVolume', 'headscale')['spec']['persistentVolumeReclaimPolicy'], 'Retain')
        self.assertEqual(resource(docs, 'Service', 'headscale-stun')['spec']['ports'][0]['protocol'], 'UDP')
        self.assertEqual(resource(docs, 'Service', 'headscale-stun')['spec']['ports'][0]['port'], 3478)
        self.assertEqual(resource(docs, 'Service', 'headscale-stun')['spec']['type'], 'LoadBalancer')
        ingress = resource(docs, 'Ingress', 'headscale')
        self.assertEqual(ingress['spec']['rules'][0]['host'], 'headscale.cichlids.com')
        self.assertEqual(ingress['metadata']['annotations']['cert-manager.io/cluster-issuer'], 'letsencrypt-production')
        config = yaml.safe_load(next(d for d in docs if d['kind'] == 'ConfigMap')['data']['config.yaml'])
        self.assertEqual(config['database']['type'], 'sqlite')
        self.assertTrue(config['derp']['server']['enabled'])
        self.assertTrue(config['derp']['server']['verify_clients'])
        self.assertEqual(config['derp']['urls'], [])
        self.assertEqual(config['derp']['paths'], [])
        for key in [config['noise']['private_key_path'], config['derp']['server']['private_key_path'], config['database']['sqlite']['path']]:
            self.assertTrue(key.startswith('/var/lib/headscale/'))
        pod = resource(docs, 'StatefulSet', 'headscale')['spec']['template']['spec']
        self.assertFalse(pod['automountServiceAccountToken'])
        self.assertIn('backup', [c['name'] for c in pod['containers']])
        for c in pod['containers'] + pod['initContainers']:
            self.assertRegex(c['image'], r'@sha256:[a-f0-9]{64}$')
        self.assertIn('"headscale"', Path('infra/platform/dns.tf').read_text())


if __name__ == '__main__':
    unittest.main()

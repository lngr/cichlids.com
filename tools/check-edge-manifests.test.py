#!/usr/bin/env python3
"""Verify TASK-2.8 edge delivery through rendered GitOps manifests."""
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import tempfile
import unittest

import yaml

spec = importlib.util.spec_from_file_location('manifests', Path(__file__).with_name('check-cluster-manifests.py'))
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)
ROOT = check.ROOT


def build(path):
    return check.load(check.run('kustomize', 'build', str(path)))


def resource(docs, kind, name):
    return next(d for d in docs if d['kind'] == kind and d['metadata']['name'] == name)


class EdgeDelivery(unittest.TestCase):
    def test_addons_have_one_owner_and_constrained_permissions(self):
        topology = build('clusters/cichlids/apps')
        project = resource(topology, 'AppProject', 'cichlids-addons')['spec']
        self.assertEqual(project['sourceRepos'], [check.REPO])
        self.assertEqual({d['namespace'] for d in project['destinations']},
                         {'ingress-nginx', 'cert-manager', 'kube-system'})
        self.assertTrue(all(d['server'] == 'https://kubernetes.default.svc'
                            for d in project['destinations']))
        allowed = {(d['group'], d['kind']) for d in project['clusterResourceWhitelist']}
        self.assertNotIn(('*', '*'), allowed)
        for service in ['ingress-nginx', 'cert-manager']:
            owners = [d for d in topology if d['kind'] == 'Application' and
                      d['spec']['source']['path'] == f'apps/{service}/overlays/cluster']
            self.assertEqual(len(owners), 1)
            self.assertEqual(owners[0]['spec']['project'], 'cichlids-addons')
            self.assertIn('ServerSideApply=true', owners[0]['spec']['syncPolicy']['syncOptions'])
            self.assertFalse(owners[0]['spec']['syncPolicy']['automated']['prune'])
            for env in ['staging', 'prod', 'preview']:
                self.assertFalse((ROOT / f'apps/{service}/overlays/{env}').exists())
            for doc in build(f'apps/{service}/overlays/cluster'):
                group = doc['apiVersion'].split('/')[0] if '/' in doc['apiVersion'] else ''
                if 'namespace' not in doc['metadata']:
                    self.assertIn((group, doc['kind']), allowed)
                else:
                    self.assertIn(doc['metadata']['namespace'],
                                  {d['namespace'] for d in project['destinations']})

    def test_upstream_copies_match_recorded_checksums(self):
        sources = json.loads((ROOT / 'tools/addon-sources.json').read_text())
        self.assertEqual(set(sources), {'ingress-nginx', 'cert-manager'})
        for entry in sources.values():
            self.assertEqual(hashlib.sha256((ROOT / entry['path']).read_bytes()).hexdigest(), entry['sha256'])
            self.assertRegex(entry['version'], r'v\d+\.\d+\.\d+$')

    def test_node_edge_binds_standard_ports_and_rolls_on_one_node(self):
        docs = build('apps/ingress-nginx/overlays/cluster')
        deployment = resource(docs, 'Deployment', 'ingress-nginx-controller')['spec']
        self.assertEqual(deployment['replicas'], 1)
        self.assertEqual(deployment['strategy']['type'], 'Recreate')
        pod = deployment['template']['spec']
        ports = pod['containers'][0]['ports']
        self.assertEqual({p['containerPort']: p['hostPort'] for p in ports if 'hostPort' in p},
                         {80: 80, 443: 443})
        self.assertEqual(resource(docs, 'Service', 'ingress-nginx-controller')['spec']['type'], 'ClusterIP')
        self.assertIn('--report-node-internal-ip-address', pod['containers'][0]['args'])
        for name, wave in [('ingress-nginx-admission-create', '-1'), ('ingress-nginx-admission-patch', '1')]:
            job = resource(docs, 'Job', name)
            self.assertEqual(job['metadata']['annotations']['argocd.argoproj.io/hook'], 'Sync')
            self.assertEqual(job['metadata']['annotations']['argocd.argoproj.io/sync-wave'], wave)
            self.assertIn('BeforeHookCreation', job['metadata']['annotations']['argocd.argoproj.io/hook-delete-policy'])

    def test_issuers_use_http01_and_operator_email(self):
        docs = build('apps/cert-manager/overlays/cluster')
        for env, endpoint in [('staging', 'https://acme-staging-v02.api.letsencrypt.org/directory'),
                              ('production', 'https://acme-v02.api.letsencrypt.org/directory')]:
            issuer = resource(docs, 'ClusterIssuer', f'letsencrypt-{env}')['spec']['acme']
            self.assertEqual(issuer['server'], endpoint)
            self.assertEqual(issuer['email'], resource(docs, 'ConfigMap', 'acme-settings')['data']['email'])
            self.assertIn('@', issuer['email'])
            self.assertEqual(issuer['solvers'], [{'http01': {'ingress': {'ingressClassName': 'nginx'}}}])
            self.assertEqual(issuer['privateKeySecretRef']['name'], f'letsencrypt-{env}-account')
        self.assertNotIn('dns01', yaml.safe_dump_all(docs))
        for doc in docs:
            if doc['kind'] in {'Namespace', 'CustomResourceDefinition', 'ClusterIssuer'}:
                self.assertEqual(doc['metadata']['annotations']['argocd.argoproj.io/sync-options'],
                                 'Prune=false,Delete=false')

    def assert_placeholder(self, docs, host, namespace):
        ingress = resource(docs, 'Ingress', 'placeholder')
        self.assertEqual(ingress['spec']['ingressClassName'], 'nginx')
        self.assertEqual(ingress['spec']['rules'][0]['host'], host)
        self.assertEqual(ingress['spec']['tls'], [{'hosts': [host], 'secretName': 'placeholder-tls'}])
        self.assertEqual(ingress['metadata']['annotations']['cert-manager.io/cluster-issuer'],
                         'letsencrypt-production')
        self.assertTrue(all(d['metadata'].get('namespace') == namespace for d in docs))
        deployment = resource(docs, 'Deployment', 'placeholder')['spec']
        self.assertEqual(deployment['replicas'], 1)
        container = deployment['template']['spec']['containers'][0]
        self.assertRegex(container['image'], r'@sha256:[0-9a-f]{64}$')
        for field in ['requests', 'limits']:
            self.assertIn('cpu', container['resources'][field])
            self.assertIn('memory', container['resources'][field])
        self.assertEqual(container['ports'][0]['containerPort'], 8080)
        self.assertFalse(deployment['template']['spec']['automountServiceAccountToken'])
        service = resource(docs, 'Service', 'placeholder')['spec']
        self.assertEqual(service['type'], 'ClusterIP')
        self.assertEqual(service['selector'], deployment['selector']['matchLabels'])
        self.assertEqual(service['ports'][0]['targetPort'], 'http')
        self.assertEqual(ingress['spec']['rules'][0]['http']['paths'][0]['backend']['service'],
                         {'name': 'placeholder', 'port': {'number': 80}})
        self.assertIn('cichlids.com', resource(docs, 'ConfigMap', 'placeholder-page')['data']['index.html'])

    def test_persistent_placeholder_hosts_and_tls(self):
        for env, host in [('staging', 'staging.cichlids.com'), ('prod', 'cichlids.com')]:
            with self.subTest(env=env):
                self.assert_placeholder(build(f'apps/placeholder/overlays/{env}'), host, f'cichlids-{env}')

    def test_preview_number_reaches_rendered_ingress_and_tls_and_closure_removes_apps(self):
        appset = resource(build('clusters/cichlids/apps'), 'ApplicationSet', 'preview')
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            chart = temp / 'chart'
            (chart / 'templates').mkdir(parents=True)
            (chart / 'Chart.yaml').write_text('apiVersion: v2\nname: edge-test\nversion: 0.1.0\n')
            (chart / 'templates/render.yaml').write_text(
                'apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: rendered\ndata:\n'
                '  rendered: {{ tpl (toYaml .Values.input) .Values.params | toJson }}\n')
            for number in [17, 402]:
                pr = {'number': number, 'sha': 'a' * 40, 'state': 'open', 'labels': ['preview-active']}
                apps = check.generate(appset, ROOT, [pr], chart)
                app = next(a for a in apps if a['metadata']['name'] == f'placeholder-pr-{number}')
                self.assertEqual(app['spec']['source']['targetRevision'], pr['sha'])
                self.assertIn(check.FINALIZER, app['metadata']['finalizers'])
                tree = temp / str(number) / 'placeholder'
                shutil.copytree(ROOT / 'apps/placeholder', tree)
                config = tree / 'overlays/preview/kustomization.yaml'
                settings = yaml.safe_load(config.read_text())
                settings.update(app['spec']['source']['kustomize'])
                config.write_text(yaml.safe_dump(settings))
                self.assert_placeholder(build(config.parent), f'pr-{number}.dev.cichlids.com', f'pr-{number}')
                self.assertFalse(check.generate(appset, ROOT, [{**pr, 'state': 'closed'}], chart))
                self.assertFalse(check.generate(appset, ROOT, [{**pr, 'labels': []}], chart))

    def test_dns_hosts_reference_platform_node_with_destroy_protection(self):
        text = (ROOT / 'infra/platform/dns.tf').read_text()
        self.assertIn('resource "hcloud_zone_rrset" "ingress"', text)
        self.assertRegex(text, r'for_each\s*=\s*toset\(\["@", "staging", "\*\.dev"\]\)')
        self.assertIn('name    = each.value', text)
        self.assertIn('value = hcloud_server.node.ipv4_address', text)
        self.assertIn('prevent_destroy = true', text.split('resource "hcloud_zone_rrset" "ingress"')[1])
        self.assertNotIn('AAAA', text)


if __name__ == '__main__':
    unittest.main()

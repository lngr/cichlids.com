#!/usr/bin/env python3
"""Verify TASK-2.8 edge delivery through rendered GitOps manifests."""
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

import yaml

sys.dont_write_bytecode = True

spec = importlib.util.spec_from_file_location('manifests', Path(__file__).with_name('check-cluster-manifests.py'))
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)
ROOT = check.ROOT


def build(path):
    return check.load(check.run('kustomize', 'build', str(path)))


def resource(docs, kind, name):
    return next(d for d in docs if d['kind'] == kind and d['metadata']['name'] == name)


def quantity(value):
    match = re.fullmatch(r'(\d+)(m|Mi|Gi)?', value)
    return int(match[1]) * {None: 1, 'm': 0.001, 'Mi': 2**20, 'Gi': 2**30}[match[2]]


class EdgeDelivery(unittest.TestCase):
    def test_addons_have_one_owner_and_constrained_permissions(self):
        topology = build('clusters/cichlids/apps')
        project = resource(topology, 'AppProject', 'cichlids-addons')['spec']
        self.assertEqual(project['sourceRepos'], [check.REPO])
        self.assertEqual({d['namespace'] for d in project['destinations']},
                         {'cert-manager', 'kube-system'})
        self.assertTrue(all(d['server'] == 'https://kubernetes.default.svc'
                            for d in project['destinations']))
        allowed = {(d['group'], d['kind']) for d in project['clusterResourceWhitelist']}
        self.assertNotIn(('*', '*'), allowed)
        namespaced = {(d['group'], d['kind']) for d in project['namespaceResourceWhitelist']}
        for service in ['traefik', 'cert-manager']:
            owners = [d for d in topology if d['kind'] == 'Application' and
                      d['spec']['source']['path'] == f'apps/{service}/overlays/cluster']
            self.assertEqual(len(owners), 1)
            self.assertEqual(owners[0]['spec']['project'], 'cichlids-addons')
            self.assertIn('ServerSideApply=true', owners[0]['spec']['syncPolicy']['syncOptions'])
            self.assertFalse(owners[0]['spec']['syncPolicy']['automated']['prune'])
            self.assertNotIn(check.FINALIZER, owners[0]['metadata'].get('finalizers', []))
            self.assertIn('RespectIgnoreDifferences=true', owners[0]['spec']['syncPolicy']['syncOptions'])
            if service == 'cert-manager':
                self.assertTrue(all(rule['jqPathExpressions'] == ['.webhooks[]?.clientConfig.caBundle']
                                    for rule in owners[0]['spec']['ignoreDifferences']))
            else:
                self.assertNotIn('ignoreDifferences', owners[0]['spec'])
            for env in ['staging', 'prod', 'preview']:
                self.assertFalse((ROOT / f'apps/{service}/overlays/{env}').exists())
            for doc in build(f'apps/{service}/overlays/cluster'):
                group = doc['apiVersion'].split('/')[0] if '/' in doc['apiVersion'] else ''
                if 'namespace' not in doc['metadata']:
                    self.assertIn((group, doc['kind']), allowed)
                else:
                    self.assertIn((group, doc['kind']), namespaced)
                    self.assertIn(doc['metadata']['namespace'],
                                  {d['namespace'] for d in project['destinations']})

    def test_upstream_copies_match_recorded_checksums(self):
        sources = json.loads((ROOT / 'tools/addon-sources.json').read_text())
        self.assertEqual(set(sources), {'cert-manager'})
        for entry in sources.values():
            self.assertEqual(hashlib.sha256((ROOT / entry['path']).read_bytes()).hexdigest(), entry['sha256'])
            self.assertRegex(entry['version'], r'v\d+\.\d+\.\d+$')

    def test_node_edge_ports_and_acme_redirect_exception(self):
        docs = build('apps/traefik/overlays/cluster')
        config = resource(docs, 'HelmChartConfig', 'traefik')
        self.assertEqual(config['metadata']['namespace'], 'kube-system')
        values = yaml.safe_load(config['spec']['valuesContent'])
        self.assertEqual(values['deployment']['replicas'], 1)
        self.assertEqual(values['service']['type'], 'LoadBalancer')
        self.assertEqual(values['ports']['web']['exposedPort'], 80)
        self.assertEqual(values['ports']['websecure']['exposedPort'], 443)
        self.assertTrue(values['providers']['file']['enabled'])
        http = yaml.safe_load(values['providers']['file']['content'])['http']
        router = http['routers']['https-redirect']
        self.assertEqual(router['entryPoints'], ['web'])
        self.assertEqual(router['rule'], '!PathPrefix(`/.well-known/acme-challenge/`)')
        self.assertEqual(router['priority'], 2147483646)
        self.assertEqual(router['service'], 'noop@internal')
        self.assertEqual(router['middlewares'], ['https-redirect'])
        self.assertEqual(http['middlewares']['https-redirect']['redirectScheme'],
                         {'scheme': 'https', 'permanent': True})
        self.assertFalse(values['ports']['web'].get('redirections'))

    def test_issuers_use_http01_and_operator_email(self):
        docs = build('apps/cert-manager/overlays/cluster')
        for env, endpoint in [('staging', 'https://acme-staging-v02.api.letsencrypt.org/directory'),
                              ('production', 'https://acme-v02.api.letsencrypt.org/directory')]:
            issuer = resource(docs, 'ClusterIssuer', f'letsencrypt-{env}')['spec']['acme']
            self.assertEqual(issuer['server'], endpoint)
            self.assertEqual(issuer['email'], resource(docs, 'ConfigMap', 'acme-settings')['data']['email'])
            self.assertIn('@', issuer['email'])
            solver = issuer['solvers'][0]['http01']['ingress']
            self.assertEqual(solver['ingressClassName'], 'traefik')
            self.assertEqual(solver['serviceType'], 'ClusterIP')
            self.assertEqual(solver['podTemplate']['spec']['resources']['limits'],
                             {'cpu': '100m', 'memory': '128Mi'})
            self.assertEqual(issuer['privateKeySecretRef']['name'], f'letsencrypt-{env}-account')
        self.assertFalse(any('dns01' in solver for d in docs if d['kind'] == 'ClusterIssuer'
                             for solver in d['spec']['acme']['solvers']))
        for doc in docs:
            if doc['kind'] in {'Namespace', 'CustomResourceDefinition', 'ClusterIssuer'}:
                self.assertEqual(doc['metadata']['annotations']['argocd.argoproj.io/sync-options'],
                                 'Prune=false,Delete=false')

        with tempfile.TemporaryDirectory() as directory:
            tree = Path(directory) / 'cert-manager'
            shutil.copytree(ROOT / 'apps/cert-manager', tree)
            config = tree / 'overlays/cluster/kustomization.yaml'
            settings = yaml.safe_load(config.read_text())
            settings['configMapGenerator'][0]['literals'] = ['email=acme@example.invalid']
            config.write_text(yaml.safe_dump(settings))
            issuers = [d for d in build(config.parent) if d['kind'] == 'ClusterIssuer']
            self.assertEqual(len(issuers), 2)
            self.assertTrue(all(d['spec']['acme']['email'] == 'acme@example.invalid' for d in issuers))

    def assert_placeholder(self, docs, host, namespace):
        ingress = resource(docs, 'Ingress', 'placeholder')
        self.assertEqual(ingress['spec']['ingressClassName'], 'traefik')
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
        quota = resource(build('clusters/cichlids/preview'), 'ResourceQuota', 'preview')['spec']['hard']
        solver = resource(build('apps/cert-manager/overlays/cluster'), 'ClusterIssuer',
                          'letsencrypt-production')['spec']['acme']['solvers'][0]['http01']['ingress']['podTemplate']['spec']['resources']
        for field in ['requests', 'limits']:
            for name in ['cpu', 'memory']:
                total = quantity(container['resources'][field][name]) + quantity(solver[field][name])
                self.assertLessEqual(total, quantity(quota[f'{field}.{name}']))
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

    def test_certificate_and_issuer_schemas_accept_valid_and_reject_unknown_fields(self):
        docs = build('tools/fixtures/cluster-manifests/certificates')
        docs.append(resource(build('apps/cert-manager/overlays/cluster'), 'ClusterIssuer', 'letsencrypt-production'))
        docs.append(resource(build('apps/traefik/overlays/cluster'), 'HelmChartConfig', 'traefik'))
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'resources.yaml'
            path.write_text(yaml.safe_dump_all(docs))
            command = ['kubeconform', '-strict', '-summary', '-schema-location',
                       str(ROOT / 'tools/manifest-schemas/{{.ResourceKind}}{{.KindSuffix}}.json'), str(path)]
            result = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout)
            for doc in docs:
                doc['spec']['unknownField'] = True
            path.write_text(yaml.safe_dump_all(docs))
            result = subprocess.run(command, capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('Invalid: 3', result.stdout)

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

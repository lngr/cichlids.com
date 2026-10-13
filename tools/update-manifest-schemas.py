#!/usr/bin/env python3
"""Vendor Kubernetes and controller schemas from pinned upstream sources."""
import hashlib
import json
from pathlib import Path
from urllib.request import urlopen

import yaml

DEST = Path(__file__).resolve().parent / 'manifest-schemas'
ARGO = 'a5038b0e2bbac1715754380ae0084df8e3d5b48c'  # Argo CD v3.5.5
KUBERNETES = '817113f21a2643311cbc3e992bb44ffe1c416c54'
KUBERNETES_SPEC = 'ecf6decece6a6de25a57aad9ba90b6ce580f6f78'
KINDS = ['namespace-v1', 'resourcequota-v1', 'limitrange-v1', 'configmap-v1',
         'persistentvolumeclaim-v1', 'deployment-apps-v1', 'statefulset-apps-v1',
         'service-v1', 'serviceaccount-v1', 'role-rbac-v1', 'rolebinding-rbac-v1',
         'clusterrole-rbac-v1', 'clusterrolebinding-rbac-v1', 'ingress-networking-v1',
         'ingressclass-networking-v1', 'job-batch-v1',
         'validatingwebhookconfiguration-admissionregistration-v1',
         'mutatingwebhookconfiguration-admissionregistration-v1']


def download(url):
    with urlopen(url) as response:
        return response.read()


def strict(schema):
    if isinstance(schema, dict):
        if schema.get('type') == 'object' and 'properties' in schema:
            schema.setdefault('additionalProperties', False)
        for value in schema.values():
            strict(value)
    elif isinstance(schema, list):
        for value in schema:
            strict(value)


def main():
    DEST.mkdir(exist_ok=True)
    provenance = {}
    for kind in KINDS:
        url = (f'https://raw.githubusercontent.com/yannh/kubernetes-json-schema/{KUBERNETES}'
               f'/v1.36.0-standalone-strict/{kind}.json')
        data = download(url)
        (DEST / f'{kind}.json').write_bytes(data)
        provenance[kind] = {'url': url, 'sha256': hashlib.sha256(data).hexdigest()}
    metadata = json.loads((DEST / 'namespace-v1.json').read_text())['properties']['metadata']
    for kind in ['application', 'applicationset', 'appproject']:
        url = (f'https://raw.githubusercontent.com/argoproj/argo-cd/{ARGO}'
               f'/manifests/crds/{kind}-crd.yaml')
        data = download(url)
        crd = yaml.safe_load(data)
        schema = next(v['schema']['openAPIV3Schema'] for v in crd['spec']['versions']
                      if v['name'] == 'v1alpha1')
        schema['$schema'] = 'http://json-schema.org/draft-04/schema#'
        schema['properties']['metadata'] = metadata
        schema['properties']['apiVersion'] = {'enum': ['argoproj.io/v1alpha1']}
        schema['properties']['kind'] = {'enum': [crd['spec']['names']['kind']]}
        strict(schema)
        (DEST / f'{kind}-argoproj-v1alpha1.json').write_text(
            json.dumps(schema, indent=2, sort_keys=True) + '\n')
        provenance[kind] = {'url': url, 'sha256': hashlib.sha256(data).hexdigest()}
    url = (f'https://raw.githubusercontent.com/kubernetes/kubernetes/{KUBERNETES_SPEC}'
           '/api/openapi-spec/swagger.json')
    data = download(url)
    definitions = json.loads(data)['definitions']
    name = 'io.k8s.apiextensions-apiserver.pkg.apis.apiextensions.v1.CustomResourceDefinition'
    schema = definitions[name].copy()
    pending = [schema]
    needed = {}
    while pending:
        value = pending.pop()
        if isinstance(value, dict):
            if isinstance(value.get('$ref'), str):
                ref = value['$ref'].removeprefix('#/definitions/')
                if ref not in needed:
                    needed[ref] = definitions[ref]
                    pending.append(needed[ref])
            pending.extend(v for k, v in value.items() if k != '$ref')
        elif isinstance(value, list):
            pending.extend(value)
    schema['definitions'] = needed
    schema['$schema'] = 'http://json-schema.org/draft-04/schema#'
    strict(schema)
    (DEST / 'customresourcedefinition-apiextensions-v1.json').write_text(
        json.dumps(schema, indent=2, sort_keys=True) + '\n')
    provenance['customresourcedefinition'] = {'url': url, 'sha256': hashlib.sha256(data).hexdigest()}
    source = json.loads((DEST.parent / 'addon-sources.json').read_text())['cert-manager']
    data = download(source['url'])
    if hashlib.sha256(data).hexdigest() != source['sha256']:
        raise SystemExit('cert-manager upstream SHA-256 mismatch')
    crds = {d['spec']['names']['kind']: d for d in yaml.safe_load_all(data)
            if d and d['kind'] == 'CustomResourceDefinition'}
    for kind in ['Certificate', 'ClusterIssuer']:
        crd = crds[kind]
        schema = next(v['schema']['openAPIV3Schema'] for v in crd['spec']['versions']
                      if v['name'] == 'v1')
        schema['$schema'] = 'http://json-schema.org/draft-04/schema#'
        schema['properties']['metadata'] = metadata
        schema['properties']['apiVersion'] = {'enum': ['cert-manager.io/v1']}
        schema['properties']['kind'] = {'enum': [kind]}
        strict(schema)
        (DEST / f'{kind.lower()}-cert-manager-v1.json').write_text(
            json.dumps(schema, indent=2, sort_keys=True) + '\n')
        provenance[kind.lower()] = {'url': source['url'], 'sha256': source['sha256']}
    url = ('https://raw.githubusercontent.com/k3s-io/helm-controller/'
           '71be4991f2652515a2bb7f7464399f6733297964/'
           'pkg/crds/yaml/generated/helm.cattle.io_helmchartconfigs.yaml')
    data = download(url)
    crd = yaml.safe_load(data)
    schema = next(v['schema']['openAPIV3Schema'] for v in crd['spec']['versions']
                  if v['name'] == 'v1')
    schema['$schema'] = 'http://json-schema.org/draft-04/schema#'
    schema['properties']['metadata'] = metadata
    schema['properties']['apiVersion'] = {'enum': ['helm.cattle.io/v1']}
    schema['properties']['kind'] = {'enum': ['HelmChartConfig']}
    strict(schema)
    (DEST / 'helmchartconfig-helm-v1.json').write_text(
        json.dumps(schema, indent=2, sort_keys=True) + '\n')
    provenance['helmchartconfig'] = {'url': url, 'sha256': hashlib.sha256(data).hexdigest()}
    (DEST / 'sources.json').write_text(json.dumps(provenance, indent=2, sort_keys=True) + '\n')


if __name__ == '__main__':
    main()

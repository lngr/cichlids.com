#!/usr/bin/env python3
"""Vendor Kubernetes and Argo CD schemas from immutable upstream commits."""
import hashlib
import json
from pathlib import Path
from urllib.request import urlopen

import yaml

DEST = Path(__file__).resolve().parent / 'manifest-schemas'
ARGO = 'a5038b0e2bbac1715754380ae0084df8e3d5b48c'  # Argo CD v3.5.5
KUBERNETES = '817113f21a2643311cbc3e992bb44ffe1c416c54'
KINDS = ['namespace-v1', 'resourcequota-v1', 'limitrange-v1', 'configmap-v1',
         'persistentvolumeclaim-v1', 'deployment-apps-v1', 'statefulset-apps-v1']


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
    (DEST / 'sources.json').write_text(json.dumps(provenance, indent=2, sort_keys=True) + '\n')


if __name__ == '__main__':
    main()

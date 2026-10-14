#!/usr/bin/env python3
"""Check TASK-2.7 topology and render ApplicationSet templates with offline parameters."""
import copy
import shutil
from pathlib import Path
import subprocess
import tempfile

import yaml

ROOT = Path(__file__).resolve().parent.parent
REPO = 'https://github.com/lngr/cichlids.com.git'
FINALIZER = 'resources-finalizer.argocd.argoproj.io'
EXPECTED = {'staging', 'prod', 'preview', 'preview-environments'}
CRITICAL = {'PersistentVolumeClaim', 'Namespace', 'Cluster', 'StatefulSet', 'Database'}


def run(*args):
    return subprocess.check_output(args, text=True, cwd=ROOT)


def load(text):
    return [doc for doc in yaml.safe_load_all(text) if doc]


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def render(value, params, temp):
    # Helm tpl uses Go text/template and Sprig for the simple ApplicationSet expressions.
    # Generator discovery is simulated offline; controller reconciliation needs a live cluster.
    (temp / 'values.yaml').write_text(yaml.safe_dump({'input': value, 'params': params}))
    envelope = yaml.safe_load(run('helm', 'template', 'fixture', str(temp)))
    return yaml.safe_load(envelope['data']['rendered'])


def merge(target, patch):
    for key, value in patch.items():
        if isinstance(value, dict) and isinstance(target.get(key), dict):
            merge(target[key], value)
        else:
            target[key] = value


def git_parameters(generator, tree):
    require(generator['repoURL'] == REPO, 'Git generator repository')
    included = set()
    for entry in generator['files']:
        matches = set(tree.glob(entry['path']))
        if entry.get('exclude'):
            included -= matches
        else:
            included |= matches
    result = []
    for path in sorted(included):
        require(path.name == 'kustomization.yaml', 'Only deployable overlays join topology')
        relative = path.parent.relative_to(tree).as_posix()
        params = yaml.safe_load(path.read_text())
        params['path'] = {'path': relative, 'segments': relative.split('/'),
                          'basename': path.parent.name, 'filename': path.name}
        result.append(params)
    return result


def pr_parameters(generator, prs):
    require(generator['requeueAfterSeconds'] == 300, 'Tokenless polling interval is 300s')
    require(generator['github'] == {'owner': 'lngr', 'repo': 'cichlids.com',
                                   'labels': ['preview-active']}, 'Preview admission label')
    return [{'number': str(pr['number']), 'head_sha': pr['sha']} for pr in prs
            if pr['state'] == 'open' and 'preview-active' in pr['labels']]


def generate(appset, tree, prs, temp):
    spec = appset['spec']
    require(spec.get('goTemplate') is True, 'Go templates are enabled')
    require(spec.get('goTemplateOptions') == ['missingkey=error'], 'Missing parameters fail closed')
    generator = spec['generators'][0]
    if 'matrix' in generator:
        first, second = generator['matrix']['generators']
        result = []
        for pr in pr_parameters(first['pullRequest'], prs):
            git = render(second['git'], pr, temp)
            require(git['revision'] == pr['head_sha'], 'Preview discovery uses PR head SHA')
            for service in git_parameters(git, tree):
                result.append({**service, **pr})
    elif 'git' in generator:
        require(generator['git']['revision'] == 'main', 'Persistent discovery uses main')
        result = git_parameters(generator['git'], tree)
    else:
        result = pr_parameters(generator['pullRequest'], prs)
    applications = []
    for params in result:
        application = {'apiVersion': 'argoproj.io/v1alpha1', 'kind': 'Application',
                       **render(spec['template'], params, temp)}
        if spec.get('templatePatch'):
            patch_text = render(spec['templatePatch'], params, temp)
            merge(application, yaml.safe_load(patch_text))
        applications.append(application)
    return applications


def check_application(app):
    spec = app['spec']
    require(spec['source']['repoURL'] == REPO, 'Application repository')
    require(spec['destination']['server'] == 'https://kubernetes.default.svc', 'Local cluster only')
    if spec['project'] == 'cichlids-addons':
        require(spec['source']['path'] in {'apps/traefik/overlays/cluster',
                                          'apps/cert-manager/overlays/cluster', 'apps/cloudnative-pg/overlays/cluster',
                                          'apps/barman-cloud/overlays/cluster'}, 'Add-on source restriction')
        require(spec['destination']['namespace'] in {'kube-system', 'cert-manager', 'cnpg-system'},
                'Add-on namespace restriction')
        require(spec['syncPolicy']['automated']['prune'] is False, 'Add-ons require deliberate pruning')
        require('Prune=false' in spec['syncPolicy']['syncOptions'], 'Add-ons have Prune=false')
        require('ServerSideApply=true' in spec['syncPolicy']['syncOptions'], 'CRDs use server-side apply')
        require(FINALIZER not in app['metadata'].get('finalizers', []), 'Add-on deletion preserves resources')
        return
    require(spec['project'] == 'cichlids', 'Generated apps use constrained project')
    if app['metadata'].get('labels', {}).get('cichlids.com/stateful') == 'true':
        require('Prune=false' in spec['syncPolicy']['syncOptions'], 'Stateful app has Prune=false')
        require(spec['syncPolicy']['automated']['prune'] is False, 'Stateful automated pruning disabled')
        require(FINALIZER in app['metadata']['finalizers'], 'Stateful app has resources finalizer')
    else:
        require(spec['syncPolicy']['automated']['prune'] is True, 'Stateless apps prune')


def check_resources(docs, path):
    stateful = any(d['kind'] in {'PersistentVolumeClaim', 'StatefulSet', 'Cluster', 'Database'} for d in docs)
    for doc in docs:
        if doc['kind'] in CRITICAL:
            options = doc['metadata'].get('annotations', {}).get('argocd.argoproj.io/sync-options', '')
            if '/preview' not in str(path):
                require('Prune=false' in options and 'Delete=false' in options,
                        f'{path}: persistent {doc["kind"]} needs Prune=false,Delete=false')
    if path.name == 'preview' and 'overlays' in path.parts:
        forbidden = CRITICAL | {'ResourceQuota', 'LimitRange', 'StatefulSet'}
        if path.parts[-3:] == ('postgres', 'overlays', 'preview'):
            forbidden -= {'Cluster'}
            cluster = next(d for d in docs if d['kind'] == 'Cluster')
            require('initdb' in cluster['spec']['bootstrap'] and
                    'plugins' not in cluster['spec'] and 'externalClusters' not in cluster['spec'],
                    'Preview database starts empty and has no production access')
        require(not any(d['kind'] in forbidden for d in docs),
                'Preview service overlay cannot own namespace bounds or persistent data')
    if stateful and 'overlays' in path.parts and path.parts[-3:] != ('postgres', 'overlays', 'preview'):
        config = yaml.safe_load((path / 'kustomization.yaml').read_text())
        marker = config.get('metadata', {}).get('annotations', {}).get('cichlids.com/stateful')
        require(marker == 'true', f'{path}: stateful overlay needs explicit marker')
        require(path.name != 'preview' or path.parts[-3:] == ('postgres', 'overlays', 'preview'),
                'Persistent data cannot join disposable previews')


def main():
    with tempfile.TemporaryDirectory(prefix='cichlids-manifests-') as directory:
        temp = Path(directory)
        chart = temp / 'chart'
        (chart / 'templates').mkdir(parents=True)
        (chart / 'Chart.yaml').write_text('apiVersion: v2\nname: appset-check\nversion: 0.1.0\n')
        (chart / 'templates/application.yaml').write_text(
            'apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: rendered\ndata:\n'
            '  rendered: {{ tpl (toYaml .Values.input) .Values.params | toJson }}\n')
        rendered = []
        topology = load(run('kustomize', 'build', 'clusters/cichlids/apps'))
        sets = {d['metadata']['name']: d for d in topology if d['kind'] == 'ApplicationSet'}
        require(set(sets) == EXPECTED, f'Expected ApplicationSets {sorted(EXPECTED)}, found {sorted(sets)}')
        project = next(d for d in topology if d['kind'] == 'AppProject' and
                       d['metadata']['name'] == 'cichlids')
        require(project['spec']['sourceRepos'] == [REPO], 'Project source restriction')
        require({d['namespace'] for d in project['spec']['destinations']} ==
                {'cichlids-staging', 'cichlids-prod', 'pr-*'}, 'Project namespace restriction')
        require(project['spec']['clusterResourceWhitelist'] == [{'group': '', 'kind': 'Namespace'}],
                'Only namespace cluster resources are allowed')
        prs = [{'number': 17, 'sha': 'a' * 40, 'state': 'open', 'labels': ['preview-active']},
               {'number': 18, 'sha': 'b' * 40, 'state': 'closed', 'labels': ['preview-active']},
               {'number': 19, 'sha': 'c' * 40, 'state': 'open', 'labels': ['preview']}]
        for name, appset in sets.items():
            current = generate(appset, ROOT, [], chart)
            services = {'api', 'web', 'postgres', 'keycloak'} if name == 'staging' else (
                {'api', 'web', 'placeholder', 'postgres', 'keycloak'} if name == 'prod' else set())
            require(len(current) == len(services), f'{name}: repository discovers environment services')
            for app in current:
                check_application(app)
                require(app['metadata']['name'] in {f'{service}-{name}' for service in services},
                        'Deployable services join environment discovery')
                rendered.append(app)
            fixture = generate(appset, ROOT / 'tools/fixtures/cluster-manifests', prs, chart)
            require(len(fixture) == (2 if name in {'staging', 'prod'} else 1), f'{name}: fixture count')
            for app in fixture:
                check_application(app)
                if name in {'staging', 'prod'}:
                    require(app['spec']['destination']['namespace'] == f'cichlids-{name}', 'Persistent namespace')
                    require(app['spec']['source']['targetRevision'] == 'main', 'Persistent revision')
                    require(appset['spec']['syncPolicy']['applicationsSync'] == 'create-update',
                            'Persistent app deletion is disabled')
                    require(appset['spec']['syncPolicy']['preserveResourcesOnDeletion'] is True,
                            'Persistent deletion preservation')
                else:
                    require(app['spec']['destination']['namespace'] == 'pr-17', 'One namespace per PR')
                    require(FINALIZER in app['metadata']['finalizers'], 'Preview teardown finalizer')
                    require(not generate(appset, ROOT / 'tools/fixtures/cluster-manifests',
                                         [{**prs[0], 'state': 'closed'}], chart), 'Closed PR generates no apps')
                    if name == 'preview':
                        require(app['spec']['source']['kustomize']['namespace'] == 'pr-17',
                                'Preview manifests override source namespaces')
                        require(app['spec']['source']['targetRevision'] == 'a' * 40, 'Pinned preview head')
                        require(app['spec']['source']['path'] == 'apps/demo/overlays/preview', 'Shared preview overlay')
                if name == 'preview-environments':
                    source = app['spec']['source']
                    preview_path = temp / 'preview'
                    shutil.copytree(ROOT / source['path'], preview_path)
                    config_path = preview_path / 'kustomization.yaml'
                    config = yaml.safe_load(config_path.read_text())
                    config.update(source['kustomize'])
                    config_path.write_text(yaml.safe_dump(config))
                    bounds = load(run('kustomize', 'build', str(preview_path)))
                    require(next(d for d in bounds if d['kind'] == 'Namespace')['metadata']['name'] ==
                            'pr-17', 'Preview namespace name patch renders')
                    require(all(d['metadata']['namespace'] == 'pr-17' for d in bounds
                                if d['kind'] != 'Namespace'), 'Preview bounds use PR namespace')
                    rendered.extend(bounds)
                rendered.append(app)
        for tree in ['clusters', 'apps', 'tools/fixtures/cluster-manifests']:
            for config in sorted((ROOT / tree).rglob('kustomization.yaml')):
                path = config.parent
                docs = load(run('kustomize', 'build', str(path)))
                check_resources(docs, path)
                rendered.extend(docs)
                print(f'kustomize build {path.relative_to(ROOT)}: OK')
        root = yaml.safe_load((ROOT / 'clusters/cichlids/root.yaml').read_text())
        require(root['spec']['source']['path'] == 'clusters/cichlids/apps', 'Bootstrap path')
        rendered.append(root)
        preview_docs = load(run('kustomize', 'build', 'clusters/cichlids/preview'))
        quota = next(d for d in preview_docs if d['kind'] == 'ResourceQuota')
        require(quota['spec']['hard'] == {'requests.cpu': '1', 'requests.memory': '1Gi',
                                        'limits.cpu': '2', 'limits.memory': '2Gi', 'pods': '10',
                                        'persistentvolumeclaims': '1', 'requests.storage': '1Gi', 'services.loadbalancers': '0',
                                        'services.nodeports': '0'}, 'Preview footprint bounds')
        require(any(d['kind'] == 'LimitRange' for d in preview_docs), 'Preview default requests and limits')
        for app in rendered:
            if app['kind'] == 'Application' and app['metadata'].get('name') != 'root':
                check_application(app)
        output = temp / 'rendered.yaml'
        output.write_text(yaml.safe_dump_all(rendered))
        print(run('kubeconform', '-strict', '-summary', '-kubernetes-version', '1.36.0',
                  '-schema-location', str(ROOT / 'tools/manifest-schemas') +
                  '/{{.ResourceKind}}{{.KindSuffix}}.json', str(output)), end='')
        print('TASK-2.7: topology, fixture generation, lifecycle and stateful safeguards OK')


if __name__ == '__main__':
    try:
        main()
    except (AssertionError, subprocess.CalledProcessError) as error:
        if isinstance(error, subprocess.CalledProcessError) and error.output:
            print(error.output, end='')
        raise SystemExit(f'FAIL: {error}')

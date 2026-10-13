#!/usr/bin/env python3
"""Render the Argo CD KSOPS bootstrap and check its static contract for TASK-2.6."""
import argparse
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import urllib.request

import yaml

HERE = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--upstream', type=Path)
    args = parser.parse_args()
    with tempfile.TemporaryDirectory() as directory:
        work = Path(directory)
        shutil.copytree(HERE / 'argocd', work / 'argocd')
        config = work / 'argocd/kustomization.yaml'
        spec = yaml.safe_load(config.read_text())
        url = spec['resources'][0]
        assert url == 'https://raw.githubusercontent.com/argoproj/argo-cd/v3.5.5/manifests/install.yaml'
        source = args.upstream.read_bytes() if args.upstream else urllib.request.urlopen(url).read()
        (work / 'argocd/upstream.yaml').write_bytes(source)
        spec['resources'] = ['upstream.yaml']
        config.write_text(yaml.safe_dump(spec))
        rendered = subprocess.check_output(['kustomize', 'build', str(config.parent)], text=True)
        objects = {(d['kind'], d['metadata']['name']): d for d in yaml.safe_load_all(rendered)}
        assert objects[('ConfigMap', 'argocd-cm')]['data']['kustomize.buildOptions'] == \
            '--enable-alpha-plugins --enable-exec'
        pod = objects[('Deployment', 'argocd-repo-server')]['spec']['template']['spec']
        init = next(c for c in pod['initContainers'] if c['name'] == 'install-ksops')
        assert re.fullmatch(r'viaductoss/ksops:v4\.5\.1@sha256:[a-f0-9]{64}', init['image'])
        assert init['command'] == ['/usr/local/bin/ksops', 'install', '/custom-tools']
        container = next(c for c in pod['containers'] if c['name'] == 'argocd-repo-server')
        env = {e['name']: e.get('value') for e in container['env']}
        assert env['SOPS_AGE_KEY_FILE'] == '/var/run/sops-age/keys.txt'
        mount = next(m for m in container['volumeMounts'] if m['name'] == 'sops-age')
        assert mount == {'name': 'sops-age', 'mountPath': '/var/run/sops-age', 'readOnly': True}
        volume = next(v for v in pod['volumes'] if v['name'] == 'sops-age')
        assert pod['securityContext']['fsGroup'] == 999
        assert init['securityContext']['runAsUser'] == 999
        assert volume['secret'] == {'secretName': 'sops-age', 'optional': True, 'defaultMode': 0o440,
                                    'items': [{'key': 'keys.txt', 'path': 'keys.txt'}]}
        assert any(m['mountPath'] == '/usr/local/bin/ksops' and m['subPath'] == 'ksops'
                   for m in container['volumeMounts'])
        assert any(c['name'] == 'copyutil' for c in pod['initContainers'])
        for doc in objects.values():
            if doc['kind'] == 'Secret':
                assert not doc.get('data') and not doc.get('stringData')
        main_tf = (HERE / 'main.tf').read_text()
        assert 'fileset("${path.module}/argocd", "*.yaml")' in main_tf
        assert 'filesha256("${path.module}/argocd/${file}")' in main_tf
        assert 'destination = "${local.remote_dir}/argocd"' in main_tf
        script = (HERE / 'scripts/install-argocd.sh').read_text()
        # A local executable records the command contract and never accesses Kubernetes.
        log = work / 'kubectl.log'
        mock = work / 'kubectl'
        mock.write_text('#!/bin/bash\nprintf "%s\\n" "$*" >> "'+str(log)+'"\n'
                        'if [[ "$*" == *"get deployments"* ]]; then echo deployment/argocd-repo-server; fi\n')
        mock.chmod(0o755)
        test_script = work / 'install-argocd.sh'
        test_script.write_text(script.replace('KUBECTL=/usr/local/bin/kubectl', f'KUBECTL={mock}'))
        root = work / 'root.yaml'
        root.write_text('')
        for _ in range(2):
            subprocess.run(['bash', str(test_script), 'v3.5.5', str(root),
                            str(HERE / 'argocd')], check=True)
        commands = log.read_text().splitlines()
        applies = [c for c in commands if '--server-side' in c]
        assert len(applies) == 2 and all(' -k ' in c for c in applies)
        assert commands.count(f'apply -f {root}') == 2
    print('Argo CD KSOPS bootstrap OK (rendered upstream; optional age mount; idempotent command contract)')


if __name__ == '__main__':
    main()

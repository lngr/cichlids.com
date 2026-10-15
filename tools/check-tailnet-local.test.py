#!/usr/bin/env python3
"""Exercise self-address routing and ingress isolation in disposable Docker containers."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import unittest
import uuid

import yaml

HEADSCALE = 'headscale/headscale:0.29.5@sha256:780ed2f9e85719a2211277433f2c8afb45105d0d25031949a6848a485f9e216d'
TAILSCALE = 'tailscale/tailscale:v1.104.1@sha256:87c55d6be56ad85b3d8bceca5f87f73de288c609131460dd1be1874c250cc41d'
TRAEFIK = 'traefik:v3.6.1@sha256:fd5932c796f7e2db9fd6bff485ef693d53797f0ee8ad03dc68aa424ea6f21958'
PYTHON = 'python:3.14.8-alpine@sha256:f6a589d43c42b9e7f7dc67a12d37132491f362859a5d750607710cc56da3bc72'
ROOT = Path(__file__).resolve().parent.parent


def run(*args, check=True):
    return subprocess.run(args, capture_output=True, text=True, check=check).stdout.strip()


class LocalTailnet(unittest.TestCase):
    def test_public_denied_tailnet_allowed_acme_and_production_public(self):
        prefix = 'cichlids-proof-' + uuid.uuid4().hex[:8]
        names = []
        network = prefix + '-net'
        with tempfile.TemporaryDirectory(prefix='cichlids-tailnet-') as directory:
            root = Path(directory)
            root.chmod(0o755)
            state = root / 'state'
            state.mkdir(mode=0o777)
            state.chmod(0o777)
            shutil.copy(ROOT / 'apps/headscale/overlays/cluster/config.yaml', state / 'config.yaml')
            values = yaml.safe_load(yaml.safe_load((ROOT / 'apps/traefik/overlays/cluster/config.yaml').read_text())['spec']['valuesContent'])
            dynamic = yaml.safe_load(values['providers']['file']['content'])
            dynamic['tls'] = {'certificates': [{'certFile': '/proof/cert.pem', 'keyFile': '/proof/key.pem'}]}
            routers = dynamic['http']['routers']
            for host in ['staging.cichlids.com', 'pr-17.dev.cichlids.com', 'pr-402.dev.cichlids.com', 'cichlids.com', 'headscale.cichlids.com']:
                router = {'entryPoints': ['websecure'], 'rule': f'Host(`{host}`)', 'tls': {}, 'service': 'headscale' if host.startswith('headscale.') else 'backend'}
                if host.startswith(('staging.', 'pr-')):
                    router['middlewares'] = ['tailnet-only']
                routers[host] = router
            routers['acme'] = {'entryPoints': ['web'], 'rule': 'PathPrefix(`/.well-known/acme-challenge/`)', 'service': 'backend'}
            dynamic['http']['services'] = {name: {'loadBalancer': {'servers': [{'url': url}]}} for name, url in [('headscale', 'http://127.0.0.1:8080'), ('backend', 'http://127.0.0.1:8000')]}
            challenge = root / 'www/.well-known/acme-challenge'
            challenge.mkdir(parents=True)
            (challenge / 'proof').write_text('local-key-authorization')
            (root / 'www/index.html').write_text('local backend')
            (root / 'dynamic.yaml').write_text(yaml.safe_dump(dynamic))
            run('openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-keyout', str(root / 'key.pem'), '-out', str(root / 'cert.pem'), '-days', '1', '-subj', '/CN=headscale.cichlids.com', '-addext', 'subjectAltName=DNS:headscale.cichlids.com,DNS:staging.cichlids.com,DNS:*.dev.cichlids.com,DNS:cichlids.com')
            (root / 'key.pem').chmod(0o644)
            run('docker', 'network', 'create', '--subnet=198.18.32.0/24', network)

            def container(role, image, args, options=()):
                name = prefix + '-' + role
                names.append(name)
                run('docker', 'run', '-d', '--name', name, *options, image, *args)
                return name

            def exec_in(name, *args):
                return run('docker', 'exec', name, *args)

            def wait_for(call):
                for _ in range(20):
                    try:
                        return call()
                    except (subprocess.CalledProcessError, AssertionError):
                        time.sleep(0.5)
                return call()

            try:
                router = container('router', TAILSCALE, ['tailscaled', '--state=/tmp/state', '--socket=/tmp/tailscaled.sock', '--port=41641'], ['--privileged', '--network', network, '--ip', '198.18.32.10', '--add-host', 'headscale.cichlids.com:198.18.32.10', '-v', f'{root}:/proof', '-e', 'SSL_CERT_FILE=/proof/cert.pem', '-e', 'TS_NO_LOGS_NO_SUPPORT=true'])
                hs = container('headscale', HEADSCALE, ['serve', '--config', '/var/lib/headscale/config.yaml'], ['--network', f'container:{router}', '-v', f'{state}:/var/lib/headscale'])
                container('backend', PYTHON, ['python3', '-m', 'http.server', '8000', '--directory', '/proof/www'], ['--network', f'container:{router}', '-v', f'{root}:/proof:ro'])
                container('traefik', TRAEFIK, ['--entrypoints.web.address=:80', '--entrypoints.websecure.address=:443', '--providers.file.filename=/proof/dynamic.yaml', '--accesslog=true', '--accesslog.format=json'], ['--network', f'container:{router}', '-v', f'{root}:/proof:ro'])
                client = container('client', TAILSCALE, ['tailscaled', '--state=/tmp/state', '--socket=/tmp/tailscaled.sock', '--port=41641'], ['--privileged', '--network', network, '--ip', '198.18.32.11', '--add-host', 'headscale.cichlids.com:198.18.32.10', '-v', f'{root}:/proof', '-e', 'SSL_CERT_FILE=/proof/cert.pem', '-e', 'TS_NO_LOGS_NO_SUPPORT=true'])

                probe = container('probe', PYTHON, ['sleep', 'infinity'], ['--network', f'container:{client}', '-v', f'{root}:/proof:ro'])

                def headscale(*args):
                    return exec_in(hs, '/ko-app/headscale', '--config', '/var/lib/headscale/config.yaml', *args)

                wait_for(lambda: headscale('users', 'list'))
                headscale('users', 'create', 'operator')
                for device in [router, client]:
                    result = json.loads(headscale('preauthkeys', 'create', '--user', '1', '--expiration', '1h', '--output', 'json'))
                    key = result['key']
                    key_path = root / ('auth-' + device)
                    key_path.write_text(key)
                    key_path.chmod(0o600)
                    wait_for(lambda: exec_in(device, 'tailscale', '--socket=/tmp/tailscaled.sock', 'status', '--json'))
                    args = ['--advertise-routes=198.18.32.10/32', '--snat-subnet-routes=false', '--accept-routes=false'] if device == router else ['--accept-routes=true']
                    exec_in(device, 'tailscale', '--socket=/tmp/tailscaled.sock', 'up', '--login-server=https://headscale.cichlids.com', '--auth-key=file:/proof/' + key_path.name, '--accept-dns=false', '--ssh=false', '--timeout=30s', *args)
                    key_path.unlink()
                nodes = json.loads(headscale('nodes', 'list', '--output', 'json'))
                node = next(n for n in nodes if '198.18.32.10/32' in (n.get('available_routes') or [])) if any('available_routes' in n for n in nodes) else nodes[0]
                headscale('nodes', 'approve-routes', '--identifier', str(node['id']), '--routes', '198.18.32.10/32')

                def request(device, host, path='/', secure=True):
                    port = 443 if secure else 80
                    scheme = 'https' if secure else 'http'
                    code = """
import socket, ssl, urllib.request, urllib.error, sys
original = socket.getaddrinfo
socket.getaddrinfo = lambda host, port, *a, **kw: original('198.18.32.10', port, *a, **kw)
class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args):
        return None
opener = urllib.request.build_opener(NoRedirect(), urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile='/proof/cert.pem')))
try:
    request = urllib.request.Request(sys.argv[1], headers={'X-Forwarded-For': '100.64.0.1'})
    with opener.open(request, timeout=3) as response:
        print(response.status)
except urllib.error.HTTPError as error:
    print(error.code)
"""
                    return exec_in(probe, 'python3', '-c', code, f'{scheme}://{host}{path}')

                def private_ok():
                    self.assertEqual(request(client, 'staging.cichlids.com'), '200')
                wait_for(private_ok)
                for host in ['pr-17.dev.cichlids.com', 'pr-402.dev.cichlids.com']:
                    self.assertEqual(request(client, host), '200')
                for device in [router, client]:
                    exec_in(device, 'iptables', '-I', 'OUTPUT', '-p', 'udp', '--dport', '41641', '-j', 'REJECT')
                wait_for(private_ok)
                relay = exec_in(client, 'tailscale', '--socket=/tmp/tailscaled.sock', 'status')
                self.assertIn('relay "cichlids"', relay)
                exec_in(client, 'tailscale', '--socket=/tmp/tailscaled.sock', 'set', '--accept-routes=false')

                def public_denied():
                    self.assertEqual(request(client, 'staging.cichlids.com'), '403')
                wait_for(public_denied)
                for host in ['pr-17.dev.cichlids.com', 'pr-402.dev.cichlids.com']:
                    self.assertEqual(request(client, host), '403')
                self.assertEqual(request(client, 'cichlids.com'), '200')
                self.assertEqual(request(client, 'staging.cichlids.com', '/.well-known/acme-challenge/proof', False), '200')
                self.assertEqual(request(client, 'staging.cichlids.com', '/', False), '301')
                logs = run('docker', 'logs', prefix + '-traefik')
                entries = [json.loads(line) for line in logs.splitlines() if line.startswith('{')]
                self.assertTrue(any(e.get('ClientHost', '').startswith('100.') and e.get('DownstreamStatus') == 200 for e in entries))
                print('Local /32 proof: tailnet HTTPS 200; public staging/previews 403; production 200; public ACME challenge 200; redirect 301; Traefik saw 100.x source; forced relay via DERP(cichlids)')
            except Exception:
                for device in [router, client]:
                    print(device, run('docker', 'exec', device, 'ip', 'route', 'get', '198.18.32.10', check=False))
                    print(run('docker', 'exec', device, 'tailscale', '--socket=/tmp/tailscaled.sock', 'status', check=False))
                print(headscale('nodes', 'list-routes'))
                entries = run('docker', 'logs', prefix + '-traefik', check=False)
                for line in entries.splitlines()[-8:]:
                    if line.startswith('{'):
                        e = json.loads(line)
                        print({k: e.get(k) for k in ['ClientHost', 'RequestHost', 'DownstreamStatus']})
                raise
            finally:
                for name in reversed(names):
                    run('docker', 'rm', '-f', name, check=False)
                run('docker', 'network', 'rm', network, check=False)


if __name__ == '__main__':
    unittest.main()

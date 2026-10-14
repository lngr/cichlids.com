#!/usr/bin/env python3
"""Exercise the pinned realm apply against a disposable local Keycloak container."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import uuid
from urllib.error import HTTPError
from urllib.request import Request, build_opener, HTTPRedirectHandler

import yaml

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parent.parent


def run(*args):
    return subprocess.check_output(args, text=True, stderr=subprocess.STDOUT).strip()


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def main():
    docs = list(yaml.safe_load_all(run('kustomize', 'build', str(ROOT / 'apps/keycloak/overlays/staging'))))
    server = next(d for d in docs if d['kind'] == 'Deployment')['spec']['template']['spec']['containers'][0]
    pod = next(d for d in docs if d['kind'] == 'Job')['spec']['template']['spec']
    cli = pod['containers'][0]
    token = uuid.uuid4().hex[:12]
    network, name = f'kc-test-{token}', f'kc-test-server-{token}'
    admin, password = 'local-proof-operator', uuid.uuid4().hex
    client_name = f'kc-test-apply-{token}'
    module_spec = importlib.util.spec_from_file_location('renderer', ROOT / 'apps/keycloak/base/render-realm.py')
    renderer = importlib.util.module_from_spec(module_spec)
    module_spec.loader.exec_module(renderer)
    source = json.loads((ROOT / 'apps/keycloak/overlays/staging/cichlids-realm.json').read_text())
    smtp = {'host': 'smtp.example.org', 'port': '587', 'user': 'local-proof', 'password': 'local-proof-only', 'from': 'auth@example.org'}
    opener = build_opener(NoRedirect())
    try:
        run('docker', 'network', 'create', network)
        run('docker', 'run', '-d', '--name', name, '--network', network, '--network-alias', 'keycloak',
            '-p', '127.0.0.1::8080', '-e', f'KC_BOOTSTRAP_ADMIN_USERNAME={admin}',
            '-e', f'KC_BOOTSTRAP_ADMIN_PASSWORD={password}', server['image'], 'start-dev',
            '--hostname=https://auth.staging.cichlids.com', '--hostname-admin=http://localhost:18080', '--proxy-headers=xforwarded')
        port = run('docker', 'port', name, '8080/tcp').split(':')[-1]
        local = f'http://127.0.0.1:{port}'
        deadline = time.monotonic() + 180
        while True:
            try:
                opener.open(local + '/realms/master/.well-known/openid-configuration', timeout=3).close()
                break
            except (OSError, HTTPError):
                if time.monotonic() > deadline:
                    raise RuntimeError('Local Keycloak readiness timed out')
                time.sleep(2)
        def admin_call(*args):
            return run('docker', 'exec', name, '/opt/keycloak/bin/kcadm.sh', *args)
        admin_call('config', 'credentials', '--server', 'http://localhost:8080', '--realm', 'master', '--user', admin, '--password', password)
        admin_call('update', 'realms/master', '-s', 'attributes.frontendUrl=http://localhost:18080')
        admin_call('config', 'credentials', '--server', 'http://localhost:8080', '--realm', 'master', '--user', admin, '--password', password)
        with tempfile.TemporaryDirectory(prefix='kc-apply-proof-') as directory:
            path = Path(directory)
            path.chmod(0o755)
            realm_file = path / 'cichlids-realm.json'
            realm_file.write_text(json.dumps(renderer.render(source, smtp, {})))
            def apply():
                command = ['docker', 'run', '--rm', '--name', client_name, '--network', network,
                           '--user', '1000:1000', '-v', f'{directory}:/rendered:ro']
                for setting in cli['env']:
                    if 'value' in setting:
                        command += ['-e', f"{setting['name']}={setting['value']}"]
                command += ['-e', f'KEYCLOAK_USER={admin}', '-e', f'KEYCLOAK_PASSWORD={password}', cli['image']]
                run(*command)
            apply()
            realm = json.loads(admin_call('get', 'realms/cichlids'))
            assert realm['registrationAllowed'] is False
            assert realm['sslRequired'] == 'all'
            assert realm['smtpServer']['host'] == smtp['host']
            assert realm['smtpServer']['starttls'] == 'true'
            assert realm['smtpServer']['password'] and 'not-configured' not in realm['smtpServer']['password']
            providers = json.loads(admin_call('get', 'identity-provider/instances', '-r', 'cichlids'))
            assert all(p['enabled'] is False and p['config'].get('clientId') in (None, '') for p in providers)
            assert 'not-configured' not in json.dumps(providers)
            clients = json.loads(admin_call('get', 'clients', '-r', 'cichlids', '-q', 'clientId=cichlids-web-bff'))
            client_id = clients[0]['id']
            secret = json.loads(admin_call('get', f'clients/{client_id}/client-secret', '-r', 'cichlids'))['value']
            admin_call('create', 'users', '-r', 'cichlids', '-s', 'username=local-proof-user', '-s', 'enabled=true')
            users = json.loads(admin_call('get', 'users', '-r', 'cichlids'))
            user_id = users[0]['id']
            admin_call('update', 'realms/cichlids', '-s', 'registrationAllowed=true')
            apply()
            assert json.loads(admin_call('get', 'realms/cichlids'))['registrationAllowed'] is True
            assert json.loads(admin_call('get', 'users', '-r', 'cichlids'))[0]['id'] == user_id
            assert json.loads(admin_call('get', f'clients/{client_id}/client-secret', '-r', 'cichlids'))['value'] == secret
            def request(path, forwarded=True):
                headers = {'X-Forwarded-Proto': 'https', 'X-Forwarded-Host': 'auth.staging.cichlids.com', 'X-Forwarded-Port': '443'} if forwarded else {}
                try:
                    with opener.open(Request(local + path, headers=headers), timeout=10) as response:
                        return response.status, response.read().decode()
                except HTTPError as error:
                    return error.code, error.read().decode()
            status, console = request('/admin/master/console/')
            assert status == 200 and 'http://localhost:18080' in console
            status, discovery = request('/realms/cichlids/.well-known/openid-configuration')
            assert status == 200
            assert json.loads(discovery)['issuer'] == 'https://auth.staging.cichlids.com/realms/cichlids'
            login_path = '/realms/cichlids/protocol/openid-connect/auth?client_id=cichlids-app&redirect_uri=cichlids%3A%2F%2Fauth&response_type=code&scope=openid&code_challenge=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&code_challenge_method=S256'
            status, body = request(login_path)
            assert status == 200 and '/resources/' in body
            assert request('/realms/cichlids/account/')[0] == 200
            print('Keycloak 26.6.4 local apply OK: registration false on create, true after re-apply; user and BFF secret preserved')
            print('Keycloak 26.6.4 paths OK: discovery issuer, PKCE login resources, account console and private admin frontend URL')
    finally:
        for container in [client_name, name]:
            subprocess.run(['docker', 'rm', '-f', container], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        subprocess.run(['docker', 'network', 'rm', network], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


if __name__ == '__main__':
    main()

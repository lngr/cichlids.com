#!/usr/bin/env python3
"""Smoke TASK-2.31 images against a disposable PostgreSQL container."""
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time
import urllib.error
import urllib.request


def run(*args, **kwargs):
    return subprocess.check_output(args, text=True, **kwargs).strip()


def main():
    prefix = 'task-2-31-' + secrets.token_hex(4)
    containers = []
    run('docker', 'network', 'create', prefix)
    try:
        with tempfile.TemporaryDirectory(prefix=prefix) as directory:
            temp = Path(directory)
            temp.chmod(0o755)
            keys = temp / 'keys'
            keys.mkdir(mode=0o755)
            (temp / 'keys.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup></Project>')
            (temp / 'Program.cs').write_text('using System; using System.IO; using Microsoft.AspNetCore.DataProtection; using Microsoft.AspNetCore.DataProtection.KeyManagement; using Microsoft.Extensions.DependencyInjection; var services = new ServiceCollection(); services.AddDataProtection().SetApplicationName("cichlids-api").PersistKeysToFileSystem(new DirectoryInfo(args[0])); using var provider = services.BuildServiceProvider(); provider.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));')
            run('dotnet', 'run', '--project', str(temp / 'keys.csproj'), '--', str(keys))
            for key in keys.iterdir():
                key.chmod(0o644)
            password = secrets.token_hex(24)
            db = prefix + '-db'
            containers.append(db)
            run('docker', 'run', '-d', '--name', db, '--network', prefix,
                '--network-alias', 'postgres-rw', '--tmpfs', '/var/lib/postgresql/data',
                '-e', 'POSTGRES_DB=workload_test', '-e', 'POSTGRES_USER=workload_test',
                '-e', 'POSTGRES_PASSWORD=' + password, 'postgres:17')
            for _ in range(60):
                ready = subprocess.run(['docker', 'exec', db, 'pg_isready', '-U', 'workload_test', '-d', 'workload_test'], capture_output=True)
                if ready.returncode == 0:
                    break
                time.sleep(1)
            else:
                raise AssertionError('Disposable PostgreSQL did not start')
            pgpass = temp / 'db'
            pgpass.mkdir(mode=0o755)
            (pgpass / 'pgpass').write_text(f'postgres-rw:5432:workload_test:workload_test:{password}\n')
            (pgpass / 'pgpass').chmod(0o644)
            connection = 'Host=postgres-rw;Database=workload_test;Username=workload_test;Passfile=/db/pgpass;GSS Encryption Mode=Disable'
            common = ['--network', prefix, '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges',
                      '--tmpfs', '/tmp:rw,noexec,nosuid,size=64m', '-v', f'{pgpass}:/db:ro']
            migration = prefix + '-migration'
            containers.append(migration)
            output = run('docker', 'run', '--name', migration, *common,
                         '-e', 'DOTNET_ENVIRONMENT=Staging', '-e', 'CICHLIDS_ETL_TARGET_CONNECTION=' + connection,
                         '-e', 'CICHLIDS_ETL_LEGACY_CONNECTION=unused-by-migrate', 'cichlids-migration:task-2.31')
            assert 'Migration applied.' in output
            print('migration: Migration applied. (disposable database workload_test)')
            settings = {
                'ASPNETCORE_ENVIRONMENT': 'Staging', 'ASPNETCORE_FORWARDEDHEADERS_ENABLED': 'true', 'ConnectionStrings__Cichlids': connection,
                'Authentication__Authority': 'https://auth.staging.cichlids.com/realms/cichlids',
                'Authentication__Audience': 'cichlids-api', 'DataProtection__KeyRingPath': '/keys',
                'WebSession__CallbackUrl': 'https://staging.cichlids.com/api/auth/callback',
                'WebSession__PostLogoutRedirectUrl': 'https://staging.cichlids.com/me',
                'WebSession__AllowedOrigins__0': 'https://staging.cichlids.com',
                'WebSession__ClientSecret': secrets.token_hex(24),
                'ObjectStorage__ServiceUrl': 'https://nbg1.your-objectstorage.com', 'ObjectStorage__Region': 'nbg1',
                'ObjectStorage__Bucket': 'synthetic-workload-test',
                'ObjectStorage__PublicBaseUrl': 'https://synthetic-workload-test.nbg1.your-objectstorage.com',
                'ObjectStorage__AccessKey': secrets.token_hex(16), 'ObjectStorage__SecretKey': secrets.token_hex(24),
                'OutboxDispatcher__Enabled': 'false',
            }
            environment = []
            for name, value in settings.items():
                environment.extend(['-e', f'{name}={value}'])
            api = prefix + '-api'
            containers.append(api)
            run('docker', 'run', '-d', '--name', api, *common, '-v', f'{keys}:/keys:ro',
                '-p', '127.0.0.1::8080', *environment, '-e', 'CICHLIDS_SLUG_SECRET=' + secrets.token_hex(32),
                'cichlids-api:task-2.31')
            port = json.loads(run('docker', 'inspect', api))[0]['NetworkSettings']['Ports']['8080/tcp'][0]['HostPort']
            base = f'http://127.0.0.1:{port}'
            for _ in range(60):
                try:
                    with urllib.request.urlopen(base + '/healthz', timeout=2) as response:
                        assert json.load(response) == {'status': 'ok'}
                    break
                except (OSError, urllib.error.URLError):
                    time.sleep(1)
            else:
                raise AssertionError('Staging API did not start')
            with urllib.request.urlopen(base + '/api/stories') as response:
                assert response.status == 200
            assert 'Hosting environment: Staging' in run('docker', 'logs', api)
            print('api: Hosting environment: Staging; /healthz 200 {"status":"ok"}; /api/stories 200')
            failed = prefix + '-without-slug'
            containers.append(failed)
            result = subprocess.run(['docker', 'run', '--name', failed, *common, '-v', f'{keys}:/keys:ro',
                                     *environment, 'cichlids-api:task-2.31'], capture_output=True, text=True)
            assert result.returncode != 0
            message = next(line for line in (result.stdout + result.stderr).splitlines() if 'slug secret' in line.lower())
            assert 'Slugs:Secret' in message or 'CICHLIDS_SLUG_SECRET' in message
            print(f'api without slug: exit {result.returncode}; {message}')
            web = prefix + '-web'
            containers.append(web)
            run('docker', 'run', '-d', '--name', web, '--read-only', '--cap-drop', 'ALL',
                '--security-opt', 'no-new-privileges', '-p', '127.0.0.1::8080', 'cichlids-web:task-2.31')
            port = json.loads(run('docker', 'inspect', web))[0]['NetworkSettings']['Ports']['8080/tcp'][0]['HostPort']
            url = f'http://127.0.0.1:{port}'
            with urllib.request.urlopen(url) as response:
                index = response.read()
                assert response.status == 200 and b'<html' in index
            try:
                response = urllib.request.urlopen(url + '/profiles/synthetic-route')
            except urllib.error.HTTPError as error:
                response = error
            assert response.read() == index
            files = run('docker', 'exec', web, 'find', '/www', '-name', '*.js').splitlines()
            bundles = '\n'.join(run('docker', 'exec', web, 'cat', file) for file in files)
            assert 'https://staging.cichlids.com' in bundles and 'https://auth.staging.cichlids.com' in bundles
            print('web: / 200; SPA deep link serves index.html; staging API and Keycloak addresses baked in')
            browser = Path(__file__).with_name('test-app-web.mjs')
            output = run('node', str(browser), env={**os.environ, 'WORKLOAD_API_URL': base, 'WORKLOAD_WEB_URL': url})
            print(output)
    finally:
        for container in reversed(containers):
            subprocess.run(['docker', 'rm', '-f', '-v', container], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        subprocess.run(['docker', 'network', 'rm', prefix], stdout=subprocess.DEVNULL)


if __name__ == '__main__':
    main()

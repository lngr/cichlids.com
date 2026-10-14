#!/usr/bin/env python3
"""Assert that no restore-drill kind clusters, containers or networks exist."""

import subprocess


checks = {
    'kind clusters': ['kind', 'get', 'clusters'],
    'containers': ['docker', 'ps', '-a', '--format', '{{.Names}}'],
    'networks': ['docker', 'network', 'ls', '--format', '{{.Name}}'],
}
for label, command in checks.items():
    output = subprocess.check_output(command, text=True, timeout=30)
    survivors = [name for name in output.splitlines() if name.startswith('restore-drill-')]
    if survivors:
        raise SystemExit(f'CLEANUP FAILED: {label}: {survivors}')
    print(f'CLEANUP CHECK: {label}: no restore-drill resources')

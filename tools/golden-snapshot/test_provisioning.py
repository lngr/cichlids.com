from pathlib import Path
import subprocess
import json
import re
import unittest
import yaml

ROOT = Path(__file__).resolve().parents[2]


class Provisioning(unittest.TestCase):
    def test_schema_inventory_matches_ef_model(self):
        model = (ROOT / 'app/api/src/Cichlids.Infrastructure/Persistence/Migrations/CichlidsDbContextModelSnapshot.cs').read_text(encoding='utf-8-sig')
        inventory = {}
        for block in model.split('modelBuilder.Entity(')[1:]:
            table = re.search(r'b.ToTable\("([^"]+)"(?:, "([^"]+)")?', block)
            if not table:
                continue
            columns = {}
            for tail in re.findall(r'^\s*b\.(?:Property|PrimitiveCollection)<.+?>\("[^"]+"\)(.*?);', block, re.S | re.M):
                name = re.search(r'HasColumnName\("([^"]+)"', tail)
                typ = re.search(r'HasColumnType\("([^"]+)"', tail)
                if name and typ:
                    columns[name[1]] = typ[1]
            inventory[f'{table[2] or "public"}.{table[1]}'] = columns
        expected = json.loads((ROOT / 'tools/golden-snapshot/schema.json').read_text())
        del expected['public.__EFMigrationsHistory']
        self.assertEqual(inventory, expected)

    def test_each_nonproduction_database_has_a_golden_restore_hook(self):
        for environment in ('staging', 'preview'):
            documents = list(yaml.safe_load_all(subprocess.check_output(
                ['kustomize', 'build', str(ROOT / 'apps/postgres/overlays' / environment)], text=True)))
            jobs = [d for d in documents if d['kind'] == 'Job']
            self.assertEqual(len(jobs), 1)
            job = jobs[0]
            self.assertEqual(job['metadata']['annotations']['argocd.argoproj.io/hook'], 'PostSync')
            container = job['spec']['template']['spec']['containers'][0]
            env = {e['name']: e for e in container['env']}
            self.assertEqual(env['GOLDEN_TARGET_DSN']['valueFrom']['secretKeyRef']['name'], 'postgres-app')
            self.assertEqual(env['GOLDEN_ENVIRONMENT']['valueFrom']['fieldRef']['fieldPath'], 'metadata.namespace')
            self.assertEqual(job['spec']['template']['spec']['automountServiceAccountToken'], False)
            self.assertNotIn('volumes', job['spec']['template']['spec'])
            config = next(d for d in documents if d['kind'] == 'ConfigMap' and d['metadata']['name'] == 'golden-snapshot')
            self.assertIn('GOLDEN_VERSION', config['data'])
            self.assertNotIn('backup', config['data']['GOLDEN_BUCKET'])
        production = subprocess.check_output(['kustomize', 'build', str(ROOT / 'apps/postgres/overlays/prod')], text=True)
        self.assertNotIn('golden-snapshot', production)


if __name__ == '__main__':
    unittest.main(verbosity=2)

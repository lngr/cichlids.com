#!/usr/bin/env python3
"""Exercise TASK-2.11 delivery against disposable Git and Kustomize fixtures."""
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import yaml

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parent.parent
DIGESTS = json.loads((ROOT / 'tools/fixtures/delivery/digests.json').read_text())
spec = importlib.util.spec_from_file_location('delivery', ROOT / 'tools/delivery.py')
delivery = importlib.util.module_from_spec(spec)
spec.loader.exec_module(delivery)


class Delivery(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for service in ['api', 'web']:
            shutil.copytree(ROOT / f'apps/{service}', self.root / f'apps/{service}')
            for environment in ['staging', 'prod']:
                settings = self.root / f'apps/api/overlays/{environment}/settings.yaml'
                settings.write_text(settings.read_text().replace('REPLACE_WITH_STAGING_MEDIA_BUCKET', 'staging-media')
                                    .replace('REPLACE_WITH_PROD_MEDIA_BUCKET', 'prod-media'))
        self.git('init', '-b', 'main')
        self.git('config', 'user.name', 'Delivery Test')
        self.git('config', 'user.email', 'delivery@example.invalid')
        self.git('add', 'apps/api', 'apps/web')
        self.git('commit', '-m', 'test: delivery fixture')
        self.sha = self.git('rev-parse', 'HEAD').strip()
        self.git('tag', 'v1.0.0')

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.root, text=True, stderr=subprocess.STDOUT)

    def snapshot(self):
        return {str(p.relative_to(self.root)): p.read_bytes() for p in (self.root / 'apps').rglob('*') if p.is_file()}

    def test_given_digests_when_staging_bumps_then_only_image_overrides_change(self):
        before = self.snapshot()
        delivery.bump(self.root, 'staging', DIGESTS)
        after = self.snapshot()
        self.assertEqual(sorted(p for p in before if before[p] != after[p]),
                         ['apps/api/overlays/staging/kustomization.yaml', 'apps/web/overlays/staging/kustomization.yaml'])
        for service in ['api', 'web']:
            path = f'apps/{service}/overlays/staging/kustomization.yaml'
            old, new = yaml.safe_load(before[path]), yaml.safe_load(after[path])
            old.pop('images')
            overrides = new.pop('images')
            self.assertEqual(old, new)
            for image in overrides:
                name = image['name'].removeprefix('ghcr.io/lngr/cichlids-')
                self.assertEqual(image['digest'], DIGESTS[name])
            rendered = subprocess.check_output(['kustomize', 'build', str(self.root / Path(path).parent)], text=True)
            self.assertNotIn('sha256:' + '0' * 64, rendered)
        delivery.preflight(self.root, 'staging')

    def test_given_invalid_or_missing_digest_when_bumped_then_no_files_change(self):
        for bad in [{}, {**DIGESTS, 'migration': None}, {**DIGESTS, 'api': 'sha256:' + '0' * 64},
                    {**DIGESTS, 'web': 'latest'}, {**DIGESTS, 'api': 'sha256:' + 'a' * 63}]:
            before = self.snapshot()
            with self.assertRaises(ValueError):
                delivery.bump(self.root, 'staging', bad)
            self.assertEqual(before, self.snapshot())

    def test_given_release_on_main_when_promoted_then_only_prod_changes(self):
        before = self.snapshot()
        with patch.object(delivery, 'resolve', return_value=DIGESTS) as resolve:
            delivery.promote(self.root, 'v1.0.0')
        resolve.assert_called_once_with(self.sha)
        after = self.snapshot()
        self.assertEqual(set(p for p in before if before[p] != after[p]),
                         {'apps/api/overlays/prod/kustomization.yaml', 'apps/web/overlays/prod/kustomization.yaml'})
        delivery.preflight(self.root, 'prod')

    def test_given_staging_bump_release_when_sha_tag_is_missing_then_use_that_commit_overlay(self):
        delivery.bump(self.root, 'staging', DIGESTS)
        self.git('add', 'apps/api/overlays/staging/kustomization.yaml', 'apps/web/overlays/staging/kustomization.yaml')
        self.git('commit', '-m', 'chore(deploy): stage fixture')
        self.git('tag', 'v1.1.0')
        released = self.git('rev-parse', 'HEAD').strip()
        other = {name: 'sha256:' + 'd' * 64 for name in DIGESTS}
        delivery.bump(self.root, 'staging', other)
        with patch.object(delivery, 'resolve', side_effect=subprocess.CalledProcessError(1, 'inspect')):
            self.assertEqual(delivery.promote(self.root, 'v1.1.0'), released)
        prod = yaml.safe_load((self.root / 'apps/web/overlays/prod/kustomization.yaml').read_text())
        self.assertEqual(prod['images'][0]['digest'], DIGESTS['web'])

    def test_given_unpublished_release_and_placeholder_snapshot_then_refuse(self):
        before = self.snapshot()
        with patch.object(delivery, 'resolve', side_effect=subprocess.CalledProcessError(1, 'inspect')):
            with self.assertRaises(ValueError):
                delivery.promote(self.root, 'v1.0.0')
        self.assertEqual(before, self.snapshot())

    def test_given_release_outside_main_or_invalid_tag_then_refuse_before_registry(self):
        self.git('switch', '-c', 'other')
        (self.root / 'other').write_text('fixture\n')
        self.git('add', 'other')
        self.git('commit', '-m', 'test: other branch')
        self.git('tag', 'v2.0.0')
        self.git('switch', 'main')
        for tag in ['v2.0.0', 'other', '--help', 'v-missing']:
            with patch.object(delivery, 'resolve') as resolve, self.assertRaises(ValueError):
                delivery.promote(self.root, tag)
            resolve.assert_not_called()

    def test_given_missing_registry_digest_then_promotion_has_no_changes(self):
        before = self.snapshot()
        with patch.object(delivery, 'resolve', return_value={}), self.assertRaises(ValueError):
            delivery.promote(self.root, 'v1.0.0')
        self.assertEqual(before, self.snapshot())

    def test_registry_resolves_each_sha_tag_and_rejects_missing_manifest(self):
        calls = []
        def inspect(command, **kwargs):
            calls.append(command)
            return json.dumps(DIGESTS[command[4].split('cichlids-')[1].split(':')[0]])
        with patch.object(delivery.subprocess, 'check_output', side_effect=inspect):
            self.assertEqual(delivery.resolve(self.sha), DIGESTS)
        self.assertEqual(len(calls), 3)
        self.assertTrue(all(f':{self.sha}' in call[4] for call in calls))
        with patch.object(delivery.subprocess, 'check_output', return_value='null'), self.assertRaises(ValueError):
            delivery.resolve(self.sha)

    def test_given_required_results_then_any_skip_failure_cancel_or_missing_fails(self):
        results = {name: {'result': 'success'} for name in delivery.REQUIRED}
        delivery.required(results)
        for value in ['skipped', 'failure', 'cancelled']:
            with self.assertRaises(ValueError):
                delivery.required({**results, 'backend': {'result': value}})
        with self.assertRaises(ValueError):
            delivery.required({name: value for name, value in results.items() if name != 'e2e'})

    def test_given_preview_sha_then_appset_overrides_published_images(self):
        topology = list(yaml.safe_load_all(subprocess.check_output(
            ['kustomize', 'build', 'clusters/cichlids/apps'], cwd=ROOT, text=True)))
        appset = next(doc for doc in topology if doc.get('kind') == 'ApplicationSet' and doc['metadata']['name'] == 'preview')
        overrides = appset['spec']['template']['spec']['source']['kustomize']['images']
        for name in delivery.IMAGES:
            self.assertIn(f'ghcr.io/lngr/cichlids-{name}:{{{{ .head_sha }}}}', overrides)
        docs = list(yaml.safe_load_all(subprocess.check_output(
            ['kustomize', 'build', 'apps/placeholder/overlays/preview'], cwd=ROOT, text=True)))
        container = next(doc for doc in docs if doc['kind'] == 'Deployment')['spec']['template']['spec']['containers'][0]
        self.assertEqual(container['image'], 'ghcr.io/lngr/cichlids-web:unpublished')
        self.assertNotIn('volumeMounts', container)
        self.assertNotIn('command', container)
        self.assertNotIn('args', container)

    def test_given_fork_repository_then_image_publication_job_is_ineligible(self):
        workflow = yaml.safe_load((ROOT / '.github/workflows/images.yml').read_text())
        self.assertEqual(workflow['jobs']['select']['if'], "github.repository == 'lngr/cichlids.com'")

    def test_given_results_when_real_summary_step_runs_then_only_all_success_passes(self):
        workflow = yaml.safe_load((ROOT / '.github/workflows/ci.yml').read_text())
        job = workflow['jobs']['required']
        import os
        results = {name: {'result': 'success'} for name in job['needs']}
        for outcome, expected in [('success', 0), ('failure', 1), ('skipped', 1), ('cancelled', 1)]:
            results['backend']['result'] = outcome
            result = subprocess.run(['bash', '-e', '-c', job['steps'][0]['run']],
                                    env={**os.environ, 'RESULTS': json.dumps(results)}, capture_output=True, text=True)
            self.assertEqual(result.returncode, expected, result.stdout + result.stderr)

    def test_workflow_gate_includes_every_ci_job_and_all_actions_are_pinned(self):
        ci = yaml.safe_load((ROOT / '.github/workflows/ci.yml').read_text())
        self.assertEqual(set(ci['jobs']['required']['needs']), set(ci['jobs']) - {'required'})
        self.assertTrue(set(delivery.REQUIRED) <= set(ci['jobs']['required']['needs']))
        self.assertEqual(ci['jobs']['required']['if'], '${{ always() }}')
        for path in (ROOT / '.github/workflows').glob('*.yml'):
            workflow = yaml.safe_load(path.read_text())
            for job in workflow['jobs'].values():
                self.assertIn('permissions', job, str(path))
                for step in job.get('steps', []):
                    if 'uses' in step:
                        self.assertRegex(step['uses'], r'@[0-9a-f]{40}$', str(path))


if __name__ == '__main__':
    unittest.main()

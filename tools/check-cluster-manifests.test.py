#!/usr/bin/env python3
"""Exercise destructive-change failures for TASK-2.7."""
import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest

import yaml

spec = importlib.util.spec_from_file_location('manifests', Path(__file__).with_name('check-cluster-manifests.py'))
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)


class Safeguards(unittest.TestCase):
    def setUp(self):
        self.app = yaml.safe_load((check.ROOT / 'clusters/cichlids/apps/environments.yaml').read_text())

    def test_stateful_application_rejects_each_missing_guard(self):
        for field in ['option', 'finalizer', 'automated']:
            with self.subTest(field=field), self.assertRaises(AssertionError):
                app = copy.deepcopy(self.app)
                if field == 'option':
                    app['spec']['syncPolicy']['syncOptions'].remove('Prune=false')
                elif field == 'finalizer':
                    app['metadata']['finalizers'] = []
                else:
                    app['spec']['syncPolicy']['automated']['prune'] = True
                check.check_application(app)

    def test_persistent_resource_rejects_missing_prune_or_delete_protection(self):
        pvc = {'kind': 'PersistentVolumeClaim', 'metadata': {'annotations': {}}}
        for options in ['', 'Prune=false', 'Delete=false']:
            with self.subTest(options=options), self.assertRaises(AssertionError):
                pvc['metadata']['annotations']['argocd.argoproj.io/sync-options'] = options
                check.check_resources([pvc], Path('apps/database/base'))

    def test_stateful_overlay_requires_marker(self):
        with tempfile.TemporaryDirectory() as directory:
            overlay = Path(directory) / 'overlays/staging'
            overlay.mkdir(parents=True)
            (overlay / 'kustomization.yaml').write_text('kind: Kustomization\n')
            pvc = {'kind': 'PersistentVolumeClaim', 'metadata': {'annotations': {
                'argocd.argoproj.io/sync-options': 'Prune=false,Delete=false'}}}
            with self.assertRaisesRegex(AssertionError, 'explicit marker'):
                check.check_resources([pvc], overlay)

    def test_preview_service_cannot_own_namespace_or_data(self):
        for kind in ['Namespace', 'ResourceQuota', 'LimitRange', 'PersistentVolumeClaim', 'Cluster']:
            with self.subTest(kind=kind), self.assertRaisesRegex(AssertionError, 'cannot own'):
                check.check_resources([{'kind': kind, 'metadata': {}}], Path('apps/service/overlays/preview'))


if __name__ == '__main__':
    unittest.main()

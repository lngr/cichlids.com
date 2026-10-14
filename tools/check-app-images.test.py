#!/usr/bin/env python3
"""Verify deployment preflight rejects unconfigured image and media settings."""
import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import yaml

sys.dont_write_bytecode = True

spec = importlib.util.spec_from_file_location('images', Path(__file__).with_name('check-app-images.py'))
images = importlib.util.module_from_spec(spec)
spec.loader.exec_module(images)


class DeploymentPreflight(unittest.TestCase):
    def check(self, image, bucket):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for service in ['api', 'web']:
                overlay = root / f'apps/{service}/overlays/staging'
                overlay.mkdir(parents=True)
                (overlay / 'kustomization.yaml').write_text(yaml.safe_dump({'apiVersion': 'kustomize.config.k8s.io/v1beta1', 'kind': 'Kustomization', 'resources': ['deployment.yaml']}))
                (overlay / 'deployment.yaml').write_text(yaml.safe_dump({'apiVersion': 'apps/v1', 'kind': 'Deployment', 'metadata': {'name': service}, 'spec': {'template': {'spec': {'containers': [{'name': service, 'image': image}]}}}}))
            (root / 'apps/api/overlays/staging/settings.yaml').write_text(yaml.safe_dump({'data': {'ObjectStorage__Bucket': bucket}}))
            with patch.object(images, 'ROOT', root), patch.object(sys, 'argv', ['check-app-images.py', 'staging']):
                images.main()

    def test_placeholder_and_mutable_image_fail(self):
        for image in ['ghcr.io/example/api:latest', 'ghcr.io/example/api@sha256:' + '0' * 64]:
            with self.subTest(image=image), self.assertRaisesRegex(SystemExit, 'published image digest'):
                self.check(image, 'staging-media')

    def test_media_placeholder_fails_with_published_images(self):
        with self.assertRaisesRegex(SystemExit, 'media bucket'):
            self.check('ghcr.io/example/api@sha256:' + 'a' * 64, 'REPLACE_WITH_STAGING_MEDIA_BUCKET')

    def test_configured_deployment_passes(self):
        self.check('ghcr.io/example/api@sha256:' + 'a' * 64, 'staging-media')


if __name__ == '__main__':
    unittest.main()

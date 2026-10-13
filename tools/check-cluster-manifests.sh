#!/usr/bin/env bash
# TASK-2.7: Render topology, fixture templates and every clusters/apps kustomization.
# Offline schemas come from Argo CD v3.5.5 CRDs and Kubernetes v1.36.0 at immutable
# commits recorded in manifest-schemas/sources.json. update-manifest-schemas.py
# extracts openAPIV3Schema, injects Kubernetes metadata and closes typed objects.
set -euo pipefail
cd "$(dirname "$0")/.."
python3 tools/check-cluster-manifests.test.py
python3 tools/check-cluster-manifests.py
node --test tools/preview-admission.test.mjs

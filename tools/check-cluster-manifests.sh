#!/usr/bin/env bash
# Render topology, edge workloads and fixture templates through offline validation.
# Local schemas derive from pinned Kubernetes and controller sources in
# manifest-schemas/sources.json. update-manifest-schemas.py extracts controller
# openAPIV3Schema, injects Kubernetes metadata and closes typed objects.
set -euo pipefail
cd "$(dirname "$0")/.."
python3 tools/check-postgres-manifests.test.py
python3 tools/check-edge-manifests.test.py
python3 tools/check-cluster-manifests.test.py
python3 tools/check-cluster-manifests.py
node --test tools/preview-admission.test.mjs

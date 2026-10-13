# Cluster topology

The cluster configuration lives in
`clusters/cichlids/`. Its root Application reconciles `clusters/cichlids/apps`
from `https://github.com/lngr/cichlids.com.git` at revision `main`. The repository
is public and Git access requires no credentials.

## Persistent environments

The `cichlids` AppProject permits this repository and the local Kubernetes API,
with destination namespaces `cichlids-staging`, `cichlids-prod` and `pr-*`.
Its cluster resource allowlist contains Namespace. Argo CD control resources
are excluded from workload namespaces. The root Application uses the bootstrap
project `default` to create the AppProject and ApplicationSets in `argocd`.

The `environments` Application owns the persistent namespaces. The `staging`
and `prod` ApplicationSets discover service overlays through Git **file**
generators:

```text
apps/<service>/base/kustomization.yaml
apps/<service>/overlays/staging/kustomization.yaml
apps/<service>/overlays/prod/kustomization.yaml
apps/<service>/overlays/preview/kustomization.yaml
```

The generator matches `apps/*/overlays/<environment>/kustomization.yaml`.
Adding that file makes a service eligible. Realm JSON files in
`apps/keycloak/overlays/` are configuration assets; these directories contain
no deployable Kustomization. The service generators therefore produce zero
Applications for the checked-in service tree.

Application names are `<service>-staging` and `<service>-prod`. Both use `main`
and their respective overlays. Image promotion updates the overlay content;
this topology defines no image build or promotion automation.

## Stateful protection

A stateful overlay declares its classification on the Kustomization:

```yaml
apiVersion: kustomize.config.k8s.io/v1beta1
kind: Kustomization
metadata:
  annotations:
    cichlids.com/stateful: "true"
resources:
  - ../../base
```

The generated Application has the `cichlids.com/stateful: "true"` label,
`Prune=false`, automated pruning disabled and the
`resources-finalizer.argocd.argoproj.io` finalizer. Persistent ApplicationSets
use `applicationsSync: create-update` and `preserveResourcesOnDeletion: true`.
Removing an overlay leaves its Application under deliberate operator control.
Stateless Applications prune removed workload resources during synchronization.

Persistent namespaces, PVCs, StatefulSets and CloudNativePG Cluster resources
require this annotation on the rendered resource:

```yaml
metadata:
  annotations:
    argocd.argoproj.io/sync-options: Prune=false,Delete=false
```

`Prune=false` protects reconciliation. `Delete=false` protects Application
cascade deletion. The Argo CD resources finalizer performs cascade deletion;
its presence alone does not protect data. The check rejects unclassified
stateful overlays and missing resource protection. Intentional data deletion
requires an explicit reviewed change to these safeguards, as specified in
[ADR-0014](../docs/adr/0014-safeguards-against-destructive-infrastructure-changes.md).
Native PVC protection finalizers complement these Argo CD options.

## Previews

An open, ready PR from `lngr/cichlids.com` requests a preview with the `preview`
label. The Preview admission workflow selects the two lowest eligible PR
numbers and sets `preview-active`. Eligibility expires 48 hours after PR
creation. Drafts and forks are excluded. Label reconciliation removes excess
admissions before granting available slots. The workflow serializes runs and
uses the Actions token only for reading PRs and managing labels. It executes
code from the repository default branch.

Events and a 15-minute scheduled run reconcile admission. GitHub scheduling
and controller polling make expiration and teardown eventual. Manual edits to
`preview-active` are corrected by reconciliation; the maximum of two is an
admission limit, not a Kubernetes admission policy or a guarantee that a stuck
terminating namespace disappears. Monitor workflow failures and terminating
preview namespaces before admitting replacement workloads.

The tokenless GitHub pull request generators select `preview-active` and poll
at `requeueAfterSeconds: 300`. Unauthenticated requests share GitHub's rate
limit for the controller's public IP. There are two PR generators, for service
Applications and environment bounds. Rate limiting delays reconciliation.

The preview service generator combines each admitted PR with the deployable
`apps/*/overlays/preview/kustomization.yaml` files at its immutable `head_sha`.
Every service uses its shared preview overlay and targets `pr-<number>`.
The environment Application uses the bounds in `clusters/cichlids/preview`
at `main`, patches the Namespace name and applies the namespace to the bounds.
It defines one namespace per admitted PR, including PRs with no deployable
service overlay.

Each preview namespace has a ResourceQuota:

| Resource | Maximum |
| --- | --- |
| CPU requests / limits | 1 / 2 cores |
| Memory requests / limits | 1 GiB / 2 GiB |
| Pods | 10 |
| PVCs | 0 |
| LoadBalancer / NodePort services | 0 |

A LimitRange supplies per-container requests of 100m CPU and 128Mi memory,
default limits of 500m CPU and 256Mi memory, and maxima of 1 CPU and 1Gi memory.
The namespace sync wave precedes the quota and limits. Service Applications
reconcile independently and can retry while their namespace is created.
Preview service overlays must contain disposable workloads and cannot own
Namespaces, ResourceQuotas, LimitRanges or persistent data.

Closing or merging the PR, removing admission or reaching expiry removes it
from the generator result. Preview ApplicationSets permit deletion and their
Applications have resource finalizers, so Argo CD deletes workloads and the
namespace. Actual teardown depends on successful controller reconciliation
and Kubernetes finalizer completion.

## Verification

```sh
python3 -m pip install PyYAML==6.0.3
./tools/check-cluster-manifests.sh
```

The `manifests` CI job installs the pinned toolchain with
`tools/install-toolchain.sh`, then runs this check. It builds every
`kustomization.yaml` under `clusters/`, `apps/` and the test-only fixtures.
Fixture services live in `tools/fixtures/cluster-manifests`, outside Git
application discovery. They prove persistent stateful and stateless templates
and an admitted, closed and unrequested PR using offline generator parameters.
Helm `tpl` renders the Go text/template and Sprig expressions and template
patches. The check also builds the rendered preview namespace patch, exercises
negative protection cases, and tests preview admission and label API failures.
A live ApplicationSet controller is required to verify discovery, sync and
cascade deletion on the cluster.

Kubeconform runs in strict mode with local schemas and rejects missing schemas.
`tools/manifest-schemas/sources.json` records immutable upstream commits and
source hashes. Kubernetes schemas use v1.36.0, matching the cluster's minor
version. Argo CD schemas derive from the v3.5.5 CRDs. The conversion extracts
`openAPIV3Schema`, inserts Kubernetes metadata and disallows unknown properties
on typed objects. The checked-in output makes CI schema validation offline.
To reproduce the schemas with network access:

```sh
python3 tools/update-manifest-schemas.py
git diff --exit-code -- tools/manifest-schemas
```

The schema updater lists supported Kubernetes resource kinds. A service that
introduces another kind extends this list and commits its schema. Upstream
references: [Git files generator](https://argo-cd.readthedocs.io/en/stable/operator-manual/applicationset/Generators-Git/),
[PR generator](https://argo-cd.readthedocs.io/en/stable/operator-manual/applicationset/Generators-Pull-Request/)
and [sync options](https://argo-cd.readthedocs.io/en/stable/user-guide/sync-options/).

## Encrypted Secrets

Follow [the SOPS operator instructions](../infra/README.md#sops-age-key) to
configure the recipient and provision `argocd/sops-age`. Add encrypted
`*.sops.yaml` or `*.sops.yml` Secret manifests through a KSOPS generator in
the relevant Kustomize overlay. See
[`ksops-generator.yaml`](../tools/fixtures/secrets/ksops-generator.yaml) and
its adjacent `kustomization.yaml` for the generator syntax.

Run `python3 tools/check-secret-manifests.py` to check tracked YAML and JSON
for plaintext Secret payloads. The `secrets-scan` job runs this guard and a
SOPS age round-trip through KSOPS alongside gitleaks. Run
`python3 tools/test-sops-roundtrip.py` with the pinned plugin installed to
verify the dedicated public test fixture.

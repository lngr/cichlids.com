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
Adding that file makes a service eligible. The `placeholder` service supplies all three environment overlays. Realm JSON
files in `apps/keycloak/overlays/` are configuration assets; these directories
contain no deployable Kustomization.

Application names are `<service>-staging` and `<service>-prod`. Both use `main`
and their respective overlays. Image promotion updates the overlay content;
this topology defines no image build or promotion automation.

## Cluster add-ons and TLS

Deploy cluster-wide services through the fixed `ingress-nginx` and
`cert-manager` Applications in `clusters/cichlids/apps/addons.yaml`. Each uses
its service's `overlays/cluster` at `main`. The environment generators discover
only `staging`, `prod` and `preview` overlays, so each add-on has one owner.

Use the `cichlids-addons` AppProject for these Applications. Its destinations
are `ingress-nginx`, `cert-manager` and `kube-system` (cert-manager leader
election RBAC). Its explicit resource allowlists permit the upstream RBAC,
webhooks, IngressClass, cert-manager CRDs and ClusterIssuers. Workload
Applications use the separate `cichlids` project.

The add-on Applications use server-side apply for the large CRDs, self-healing
and deliberate pruning. They have no cascade finalizer. Namespaces, CRDs and
ClusterIssuers have `Prune=false,Delete=false`. Review resource deletion as an
explicit operator action. Webhook CA bundles are owned by certificate
controllers; Argo CD ignores those fields and respects the ignore rules during
sync.

The ingress controller uses one replica with `hostPort` 80 and 443. k3s's CNI
maps these node ports to the pod; its Service is ClusterIP. The controller
publishes the node's internal address in Ingress status. Public DNS uses the
platform node's IPv4 address. The `Recreate` strategy releases host ports before
starting a replacement; controller updates interrupt ingress on the single
node. Keep TCP 80 and 443 reachable from the public internet for HTTP-01 and
HTTPS. The admission certificate creation job runs as a Sync hook before the
controller, and the webhook CA patch job runs after it. Hook deletion policies
allow repeat synchronization and upgrades.

Upstream pins are ingress-nginx **v1.15.1** and cert-manager **v1.21.2**.
The upstream copies live in each `base/upstream.yaml`; their release URLs and
SHA-256 hashes are in `tools/addon-sources.json`. Reproduce them with:

```sh
python3 tools/update-addon-manifests.py
python3 tools/update-manifest-schemas.py
./tools/check-cluster-manifests.sh
```

For upgrades, select an exact upstream release and record its URL and verified
SHA-256 hash in `tools/addon-sources.json`. Run the commands above and review
the manifest and schema changes. CI validates the local copies offline.

[ingress-nginx is archived](https://github.com/kubernetes/ingress-nginx/releases/tag/controller-v1.15.1)
and unsupported. Review this maintenance risk before
public deployment. Its compatibility with the configured k3s 1.36 cluster
requires a runtime check by the operator.

The `letsencrypt-staging` and `letsencrypt-production` ClusterIssuers use
HTTP-01 with `ingressClassName: nginx`. The solver Service is explicitly
ClusterIP so it fits the preview quota. Solver pods request 10m CPU and 64Mi
memory, with limits of 100m and 128Mi. ACME account keys are generated in
`cert-manager` and use separate Secret names for each issuer.

Set the non-secret `email` value in
`apps/cert-manager/overlays/cluster/kustomization.yaml` to the operator's
monitored ACME contact address before deployment. `acme@cichlids.com` is a
placeholder that requires operator confirmation or replacement. Kustomize
copies this value from `acme-settings` to both ClusterIssuers.

The placeholder uses BusyBox **1.37.0-musl** pinned by the image digest in
`apps/placeholder/base/deployment.yaml`. It serves the ConfigMap page on port
8080 as UID 65534 with a read-only root filesystem and a disabled service
account token. Its requests are 25m CPU and 16Mi memory; limits are 100m and
64Mi. The Service and HTTP-01 solver fit the preview's ClusterIP-only quota.

All environment Ingresses use `letsencrypt-production` for publicly trusted
TLS. For ACME diagnostics, set the cert-manager annotation to
`letsencrypt-staging` in a reviewed overlay change; that issuer's certificates
require a test trust configuration. The staging issuer refers to the ACME
test service, independently of the application's staging environment.

## Operator rollout and checks

Configure the ACME email value above. Use the platform tier's operator credentials and state encryption
configuration as described in [infra/README.md](../infra/README.md). Apply its
DNS records:

```sh
tofu -chdir=infra/platform apply
```

The A RRsets `@`, `staging` and `*.dev` use
`hcloud_server.node.ipv4_address` with a 300-second TTL and `prevent_destroy`.
The platform manages IPv4 ingress records. Review any existing AAAA records
for these names so clients and ACME validation reach the same serving node.
Delegate the zone to its assigned nameservers and verify public resolution
before requesting certificates. A wildcard DNS record routes preview names;
each preview receives a certificate for its individual hostname.

Publish the reviewed manifests to `main`. From an operator workstation with
cluster access, inspect Argo CD sync and
health for both add-ons and the placeholder Applications. Then run:

```sh
kubectl -n ingress-nginx rollout status deployment/ingress-nginx-controller
kubectl -n cert-manager rollout status deployment/cert-manager
kubectl -n cert-manager rollout status deployment/cert-manager-webhook
kubectl -n cert-manager rollout status deployment/cert-manager-cainjector
kubectl wait --for=condition=Ready clusterissuer/letsencrypt-production --timeout=300s
kubectl wait --for=condition=Ready clusterissuer/letsencrypt-staging --timeout=300s
kubectl -n cichlids-staging wait --for=condition=Ready certificate/placeholder-tls --timeout=300s
kubectl -n cichlids-prod wait --for=condition=Ready certificate/placeholder-tls --timeout=300s
curl --fail --show-error https://staging.cichlids.com/
curl --fail --show-error https://cichlids.com/
```

For a ready same-repository PR with the `preview` label, wait for
`preview-active` admission and the `placeholder-pr-<number>` Application. The
ApplicationSet patches both the Ingress rule host and TLS host to
`pr-<number>.dev.cichlids.com`. The shared overlay's `pr-0` value is a render
sentinel; deploy previews through the ApplicationSet.

Set `PREVIEW_NUMBER` to the admitted PR number on the operator workstation:

```sh
PREVIEW_NUMBER=17
kubectl -n "pr-${PREVIEW_NUMBER}" wait --for=condition=Ready certificate/placeholder-tls --timeout=300s
curl --fail --show-error "https://pr-${PREVIEW_NUMBER}.dev.cichlids.com/"
```

Close or merge the PR and wait for admission and ApplicationSet reconciliation.
Verify that its service and environment Applications, namespace and certificate
Secret disappear. Allow for the 300-second polling interval and finalizer
completion. A DNS name still resolves through the wildcard after teardown;
verify resource deletion and confirm that requests receive the ingress default
response.
For issuance failures, inspect Certificate, Order and Challenge status, public
DNS and port 80 reachability. Check Let's Encrypt rate limits when creating
many preview hosts.

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
The edge tests render real placeholder previews with multiple PR numbers and
check TLS hosts, singleton add-on ownership, HTTP-01 quota bounds, DNS records
and upstream checksums. Certificate and ClusterIssuer schemas are exercised
with valid resources and rejected unknown fields.
A live ApplicationSet controller is required to verify discovery, sync and
cascade deletion on the cluster.

Kubeconform runs in strict mode with local schemas and rejects missing schemas.
`tools/manifest-schemas/sources.json` records immutable upstream commits and
source hashes. Kubernetes schemas use v1.36.0, matching the cluster's minor
version. Argo CD schemas derive from the v3.5.5 CRDs. Certificate and ClusterIssuer
schemas derive from the checksum-pinned cert-manager v1.21.2 CRDs. The CRD
resource schema derives from Kubernetes v1.36.0 OpenAPI at an immutable commit. The conversion extracts
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

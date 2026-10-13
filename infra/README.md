# Infrastructure bootstrap runbook

Operator HOWTO that turns the OpenTofu code in this directory into a running platform. The
decisions, the capability-boundary rationale and the risks are in the ADRs and are not repeated
here: ADR-0017 (project separation), ADR-0016 (remote state), ADR-0014 (destroy safeguards),
ADR-0013 (backup), ADR-0009 (hosting). No secrets belong in this file: every credential comes from
the credential file below (or, for in-cluster secrets, SOPS+age) and is never committed.

## Layout

Two cloud projects (rationale: ADR-0017); a credential in one project cannot act on another:

- **`cichlids`**: all live infrastructure (the node, the database volume, the media-master bucket,
  the DNS zone, and the app tier) plus its own OpenTofu state bucket. Managed by CI/GitOps
  via tofu, guarded against accidents by `prevent_destroy` and `delete_protection`. Holds the only
  cloud token. OpenTofu code in `platform/` (node, volume, media-master, DNS) and `app/` (k3s and Argo CD on
  the node, applied over SSH).
- **`cichlids-backup`**: only the immutable backups (Object Lock Compliance) plus its own OpenTofu
  state bucket. No cloud token; its object-storage key is operator-only and never a CI secret.
  Code in `backup/`, applied out-of-band by the operator.

Everything in `cichlids` is recoverable from the immutable backups, so a mistake there is a
recoverable outage, not data loss. Each tier keeps its state in a remote S3 backend in its own
project, never a local file (ADR-0016).

## Prerequisites

- The platform toolchain meets the minimums in `tools/toolchain.versions`. On your own host install
  it with Homebrew from the repo `Brewfile` (`brew bundle`), then confirm with
  `./tools/verify-toolchain.sh core`; remove it again with `brew uninstall`. (CI and the
  devcontainer use the pinned `tools/install-toolchain.sh`, not your host.)
- You can administer the domain `cichlids.com` at its registrar.

## Credential file

All credentials live in **one key/value file in your home directory**, read by the wrapper
[`infra/tofu.sh`](tofu.sh) so you never export `TF_VAR_*` / `AWS_*` by hand. Copy the template and
fill in each variable as the step below that mints it tells you to; the variables and what each is
are documented in `cichlids.env.example` itself:

```sh
mkdir -p ~/.config/cichlids
cp infra/cichlids.env.example ~/.config/cichlids/cichlids.env
chmod 600 ~/.config/cichlids/cichlids.env
```

It is git-ignored and must never be committed. Override the location with `CICHLIDS_ENV=/path`.

## Bootstrap

Steps in order; each states its **expected result**. Steps 1-7 create the two projects and their
credentials; steps 8-9 apply the backup and platform tiers via `infra/tofu.sh`; step 10 delegates
the domain. SOPS+age and Cloudflare R2 (see "Later") are not needed for these applies.

### 1. SSH key pair

1. On a durable machine, generate an ed25519 key pair: `ssh-keygen -t ed25519 -C operator@cichlids`.
2. Record the **public** key as `CICHLIDS_SSH_PUBLIC_KEY`; keep the private key on your host.

**Expected result:** the public key is available for the apply; the private key stays with you.

### 2. The two Hetzner Cloud projects

1. Create two projects: `cichlids` and `cichlids-backup`.

**Expected result:** two empty projects in the console.

### 3. Cloud API token (cichlids)

1. In **`cichlids`** under **Security → API tokens**, create one **Read & Write** token.
2. Record it as `CICHLIDS_HCLOUD_TOKEN`; it later becomes a GitHub Actions secret for the
   CI-driven apply. This is the only cloud token; none is created for `cichlids-backup`.

**Expected result:** one Read & Write token for `cichlids`.

### 4. Object-storage key (cichlids)

1. In **`cichlids`** Object Storage, create an **S3 access key / secret key** pair; record as
   `CICHLIDS_S3_PRIMARY_ACCESS_KEY` / `_SECRET_KEY`. It serves both the media-master bucket and the
   platform state backend.

**Expected result:** an S3 key pair for the cichlids project.

### 5. Object-storage key (cichlids-backup)

1. In **`cichlids-backup`** Object Storage, create an **S3 access key / secret key** pair; record
   as `CICHLIDS_S3_BACKUP_ACCESS_KEY` / `_SECRET_KEY`. It serves both the backup buckets and the
   backup state backend. Operator-only: never added as a CI secret.

**Expected result:** an S3 key pair for the backup project.

### 6. State buckets (created by hand, before any `tofu init`)

The backend needs a bucket OpenTofu does not manage, one in each project (ADR-0016). In the create
dialog set both identically: **Object Lock: off** and **Visibility: private**; the dialog has no
versioning field, so **enable versioning afterwards**.

1. In **`cichlids`**: create bucket `cichlids-platform-tfstate`.
2. In **`cichlids-backup`**: create bucket `cichlids-backup-tfstate`.

The media-master and backup buckets are created by OpenTofu (Object Lock and private access set in
code), not by hand.

**Expected result:** two private, versioned buckets with Object Lock off
(`cichlids-platform-tfstate`, `cichlids-backup-tfstate`).

### 7. State-encryption passphrase

1. Generate a strong passphrase (at least 16 characters), store it out-of-band, record as
   `CICHLIDS_STATE_PASSPHRASE` (ADR-0016). Never commit it.

**Expected result:** a passphrase held out-of-band only.

### 8. Apply the backup tier

```sh
infra/tofu.sh backup init
infra/tofu.sh backup plan
infra/tofu.sh backup apply
```

**Expected result:** state under `backup/terraform.tfstate` in `cichlids-backup-tfstate`; the
database-backup and media-master-backup buckets exist in `cichlids-backup` with Object Lock
(Compliance) on. No cloud token used.

### 9. Apply the platform tier

```sh
infra/tofu.sh platform init
infra/tofu.sh platform plan
infra/tofu.sh platform apply
```

Non-secret settings come from `infra/platform/terraform.tfvars` (copy the `.example`);
`location` / `server_type` are sizing knobs, scalable anytime.

**Expected result:** state under `platform/terraform.tfstate` in `cichlids-platform-tfstate`; the
node, DB volume, media-master bucket (Object Lock Governance) and DNS zone exist in `cichlids`, all
carrying their guards. `infra/tofu.sh platform output dns_zone_nameservers` prints the nameservers
for step 10.

### 10. Delegate the domain

1. At the registrar for `cichlids.com`, set the nameservers from
   `infra/tofu.sh platform output dns_zone_nameservers`.

**Expected result:** the domain resolves through Hetzner DNS.

## Later (not needed for these applies)

### App (ephemeral) tier

Installs k3s on the node and bootstraps Argo CD over SSH. Needs the node from step 9 and no cloud
token. Steps in order, each with its expected result.

1. Generate a dedicated CI key pair:
   `ssh-keygen -t ed25519 -N "" -C cichlids-ci-app -f ~/.config/cichlids/ci-app`.
   **Expected result:** `ci-app` (private) and `ci-app.pub` exist; the private key is used by nothing else.
2. Authorise the public key on the node:
   `ssh root@<ip> 'cat >> ~/.ssh/authorized_keys' < ~/.config/cichlids/ci-app.pub`.
   **Expected result:** `ssh -i ~/.config/cichlids/ci-app root@<ip> true` succeeds.
3. Read the node host key with `ssh-keyscan -t ed25519 <ip>` and compare its fingerprint
   (`ssh-keyscan -t ed25519 <ip> | ssh-keygen -lf -`) with the one in the Hetzner console or a
   login you already trust. Keep the key line without the leading host name (`ssh-ed25519 AAAA...`).
   **Expected result:** the fingerprints are identical.
4. Choose an app state passphrase (at least 16 characters) and store it out-of-band.
   **Expected result:** a passphrase distinct from the platform one.
5. Set the GitHub secrets `CICHLIDS_APP_SSH_PRIVATE_KEY` (content of `ci-app`),
   `CICHLIDS_APP_STATE_PASSPHRASE`, `CICHLIDS_S3_PRIMARY_ACCESS_KEY` and
   `CICHLIDS_S3_PRIMARY_SECRET_KEY`, and the GitHub variables `CICHLIDS_NODE_IPV4` and
   `CICHLIDS_NODE_HOST_KEY` (the full `ssh-ed25519 AAAA...` line from step 3).
   **Expected result:** four secrets and two variables in the repository settings.
6. For a local apply, add `CICHLIDS_APP_STATE_PASSPHRASE`, `CICHLIDS_NODE_IPV4`,
   `CICHLIDS_NODE_HOST_KEY` and `CICHLIDS_APP_SSH_PRIVATE_KEY_FILE` (path of `ci-app`) to the
   credential file, then run:

   ```sh
   infra/tofu.sh app init
   infra/tofu.sh app plan
   infra/tofu.sh app apply
   ```

   Alternatively run the `infra-app` workflow on `main` via workflow dispatch.
   **Expected result:** state under `app/terraform.tfstate` in `cichlids-platform-tfstate`; the plan
   shows two `terraform_data` resources and the apply finishes without error.
7. Check the cluster over SSH (`ssh root@<ip>`):
   - `kubectl get nodes` lists one node in status `Ready`.
   - `kubectl -n argocd get pods` lists all pods `Running` and ready.
   - `kubectl -n argocd get application root` shows the application `Synced` and `Healthy`.

   **Expected result:** all three checks pass.

The k3s bootstrap writes `/etc/rancher/k3s/config.yaml` with mode 0600 and
`write-kubeconfig-mode: "0600"`. It compares the desired file with the installed
file. A config difference, version mismatch or service flag disabling Traefik
runs the pinned installer with `INSTALL_K3S_EXEC=server` and
`INSTALL_K3S_SKIP_START=true`, then restarts k3s. This reconciles service arguments
and applies config changes even at the pinned version. Matching config and
version require no restart. A `config.yaml.restart-required` marker requests
a retry when installation or restart fails; a successful restart clears it.
OpenTofu trigger hashes cover every uploaded script,
the root Application and all bootstrap Argo CD YAML files.

k3s runs bundled Traefik and ServiceLB. Argo CD owns the Traefik
`HelmChartConfig` in `apps/traefik/overlays/cluster/config.yaml`; the k3s Helm
controller applies its values to the bundled chart. Git reconciliation configures
the ingress after Argo CD bootstrap. The bootstrap uses the local Kubernetes API
and requires no ingress endpoint. See [cluster operations](../clusters/README.md)
for port ownership, TLS and rollout checks.

### SOPS age key

1. Generate the operator key outside the repository:

   ```sh
   mkdir -p ~/.config/cichlids/age
   chmod 700 ~/.config/cichlids/age
   umask 077
   age-keygen -o ~/.config/cichlids/age/keys.txt
   chmod 600 ~/.config/cichlids/age/keys.txt
   age-keygen -y ~/.config/cichlids/age/keys.txt
   ```

2. Store `keys.txt` in the password manager. The complete `age1...` line from
   `age-keygen -y` is the public recipient. Append these entries to
   `creation_rules` in the repository root `.sops.yaml`, after the test-only
   entry. Replace each `OPERATOR_PUBLIC_KEY` with that public line:

   ```yaml
   - path_regex: ^(apps|clusters)/.*\.sops\.ya?ml$
     encrypted_regex: ^(data|stringData)$
     age: OPERATOR_PUBLIC_KEY
   - path_regex: ^infra/.*\.sops\.(yaml|yml|json)$
     age: OPERATOR_PUBLIC_KEY
   ```

   Commit the public recipient configuration. Use the first rule for Kubernetes
   Secret manifests and the second for IaC credential documents. The repository
   configuration contains only the dedicated CI fixture rule until these
   operator entries are present.

3. On the operator machine, select the intended cluster context and provision
   the private key after the Argo CD namespace exists:

   ```sh
   kubectl config current-context
   kubectl -n argocd create secret generic sops-age \
     --from-file=keys.txt="$HOME/.config/cichlids/age/keys.txt" \
     --dry-run=client -o yaml | kubectl apply -f -
   kubectl -n argocd rollout restart deployment/argocd-repo-server
   kubectl -n argocd rollout status deployment/argocd-repo-server --timeout=300s
   ```

   Keep this command output out of log files. Run these commands directly on
   the operator machine. The private key belongs in the password manager and
   the cluster Secret, outside Git and CI secrets.

4. Set the local decryption key and edit an encrypted manifest at an existing
   service overlay path:

   ```sh
   export SOPS_AGE_KEY_FILE="$HOME/.config/cichlids/age/keys.txt"
   sops edit apps/example/overlays/staging/credentials.sops.yaml
   ```

   Set `apiVersion: v1`, `kind: Secret`, its `metadata`, and the intended
   `data` or `stringData`. Save through SOPS. Refer to the manifest in a KSOPS
   generator and list that generator under `generators` in the overlay's
   `kustomization.yaml`. Use the generator shape in
   `tools/fixtures/secrets/ksops-generator.yaml` with your manifest path.
   Install the local plugin with `bash tools/install-ksops.sh` and verify:

   ```sh
   kustomize build --enable-alpha-plugins --enable-exec apps/example/overlays/staging >/dev/null
   python3 tools/check-secret-manifests.py
   ```

   Review and commit the encrypted manifest and generator. Refresh its Argo CD
   Application and check that it is `Synced` and `Healthy`. Check the Secret
   name using `kubectl -n <namespace> get secret <name>`. These cluster checks
   verify in-cluster decryption with the operator key.

5. Rotate with both private keys available. Generate another key outside Git,
   add its public recipient to the applicable `.sops.yaml` entries, and deploy
   a `keys.txt` containing both private keys through the command in step 3.
   For each encrypted file, run `sops updatekeys <file>` and
   `sops rotate --in-place <file>` with the operator keys available. Commit the
   encrypted files and verify reconciliation. Remove the old public recipient,
   run `sops updatekeys <file>` for each file, and verify reconciliation again.
   Provision a `keys.txt` containing only the new private key. Retain recovery
   keys for historical ciphertext in the password manager according to the
   backup retention policy.

To check the public CI fixture locally:

```sh
bash tools/install-ksops.sh
python3 tools/check-secret-manifests.test.py
python3 tools/check-secret-manifests.py
python3 tools/test-sops-roundtrip.py
python3 infra/app/ksops_test.py
```

The fixture key at `tools/fixtures/secrets/age-test-key.txt` is public and test
only. Use the operator recipient for every real secret. The bootstrap mounts
`sops-age` optionally at `/var/run/sops-age`, with `SOPS_AGE_KEY_FILE` pointing
at `keys.txt`. Argo CD starts when that Secret is absent; encrypted manifest
rendering requires the key.

The KSOPS init container uses `viaductoss/ksops:v4.5.1` pinned to its image index
digest. `tools/install-ksops.sh` verifies Linux release archives against
`tools/ksops-checksums.txt`. Upstream installation instructions:
[KSOPS Argo CD integration](https://github.com/viaduct-ai/kustomize-sops#argo-cd-integration-).
Release artifacts:
[KSOPS v4.5.1](https://github.com/viaduct-ai/kustomize-sops/releases/tag/v4.5.1).

### Cloudflare account, R2, and S3 token (serving layer)

Create a Cloudflare account, an R2 bucket and an R2 S3 key pair for media serving (the master copy
stays on Hetzner; R2 is the serving cache, ADR-0009). Wired in with the serving workloads.

### GitHub Actions secrets

For the CI-driven infrastructure apply and media operations:

- Cloud token (step 3): platform provisioning and DNS records. The cluster holds no Hetzner Cloud token; cert-manager issues HTTP-01 certificates per host through Traefik.
- cichlids object-storage S3 key (step 4): media operations and the platform/app state backend.
- State-encryption passphrase (step 7): so CI can read/write the platform state.
- App tier: `CICHLIDS_APP_SSH_PRIVATE_KEY` and `CICHLIDS_APP_STATE_PASSPHRASE` as secrets,
  `CICHLIDS_NODE_IPV4` and `CICHLIDS_NODE_HOST_KEY` as variables (see "App (ephemeral) tier").
- Cloudflare R2 S3 key: media serving, when deployed.

Never added as CI secrets: the backup-project key (`cichlids-backup`, step 5), the age private key
(a cluster secret), and any cloud token for `cichlids-backup` (none exists). The backups stay out
of CI's reach (ADR-0017).

### Branch protection on `main`

Require pull requests and passing CI checks before merge (ADR-0005).

> Branch protection needs settings unavailable on a private, non-organisation repository. Until
> then the green gate is enforced by convention: do not merge a PR whose checks are red.

<!-- Story↔test binding (ADR-0015): task-2.3, task-2.16 -->

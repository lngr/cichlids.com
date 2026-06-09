# Infrastructure bootstrap runbook

Operator HOWTO that turns the OpenTofu code in this directory into a running platform. The
decisions, the capability-boundary rationale and the risks are in the ADRs and are not repeated
here: ADR-0017 (project separation), ADR-0016 (remote state), ADR-0014 (destroy safeguards),
ADR-0013 (backup), ADR-0009 (hosting). No secrets belong in this file: every credential comes from
the credential file below (or, for in-cluster secrets, SOPS+age) and is never committed.

## Layout

Two cloud projects (rationale: ADR-0017); a credential in one project cannot act on another:

- **`cichlids`**: all live infrastructure (the node, the database volume, the media-master bucket,
  the DNS zone, and later the app tier) plus its own OpenTofu state bucket. Managed by CI/GitOps
  via tofu, guarded against accidents by `prevent_destroy` and `delete_protection`. Holds the only
  cloud token. OpenTofu code in `platform/` (node, volume, media-master, DNS) and later `app/`.
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
2. Record it as `CICHLIDS_HCLOUD_TOKEN`. It also authorises the project's DNS zone and later
   becomes a GitHub Actions secret for the CI-driven apply. This is the only cloud token; none is
   created for `cichlids-backup`.

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

When `infra/app/` exists, apply it the same way (its own state key in `cichlids-platform-tfstate`,
the cichlids cloud token). It installs single-node k3s via cloud-init and bootstraps Argo CD, then
reconciles from Git, referencing no data resources directly (ADR-0010).

### SOPS age key

1. `age-keygen -o keys.txt` (prints the `age1...` public key).
2. Put the public key into `.sops.yaml` as the recipient (safe to commit).
3. Keep the private key out-of-band and provision it into the cluster for Argo CD's KSOPS plugin
   (ADR-0010). Never commit it.

### Cloudflare account, R2, and S3 token (serving layer)

Create a Cloudflare account, an R2 bucket and an R2 S3 key pair for media serving (the master copy
stays on Hetzner; R2 is the serving cache, ADR-0009). Wired in with the serving workloads.

### GitHub Actions secrets

For the CI-driven apply, preview environments and the DNS-01 solver:

- Cloud token (step 3): the apply, previews, and cert-manager DNS-01 for `*.dev`.
- cichlids object-storage S3 key (step 4): media operations and the platform/app state backend.
- State-encryption passphrase (step 7): so CI can read/write the state.
- Cloudflare R2 S3 key: media serving, when deployed.

Never added as CI secrets: the backup-project key (`cichlids-backup`, step 5), the age private key
(a cluster secret), and any cloud token for `cichlids-backup` (none exists). The backups stay out
of CI's reach (ADR-0017).

### Branch protection on `main`

Require pull requests and passing CI checks before merge (ADR-0005).

> Branch protection needs settings unavailable on a private, non-organisation repository. Until
> then the green gate is enforced by convention: do not merge a PR whose checks are red.

<!-- Story↔test binding (ADR-0015): task-2.3, task-2.16 -->

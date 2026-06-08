# Infrastructure bootstrap runbook

This is the operator runbook that turns the OpenTofu code in this directory into a running
platform, together with the credential model that keeps it safe. It is written for a human
operator working on their own host. **No secrets or tokens belong in this file** — every
credential is supplied at apply time through environment variables, or stored encrypted with
SOPS+age, and never committed.

## Layout

The infrastructure is split across **three separate cloud projects** so that the capability to
reach the irreplaceable data never exists where automation runs (see "Credential model"). Each
project is a separate isolation boundary; a credential in one project cannot see or act on
another.

- **`foundation/`** — the **primary** project's **stateful** tier: the compute node and its
  disk, the database volume, the media-master bucket (the authoritative copy of the originals),
  and the DNS zone. All live, rebuildable infrastructure lives in this one project. Rarely
  changed, applied out-of-band by the operator, protected against destruction.
- **`backup/`** — the **backup** project's tier: only the immutable backups — the database
  PITR/WAL backups and an off-master copy of the media originals, under Object Lock Compliance.
  It manages object storage only and holds **no cloud token at all**. Applied out-of-band by the
  operator with a write-scoped object-storage credential; never touched by CI.
- `app/` — the **ephemeral** tier (not built yet): k3s (via cloud-init) and the Argo CD
  bootstrap, in the primary project. It holds no protected data resources and is what routine
  automation may apply.

Each tier keeps its OpenTofu state in a remote backend on Hetzner Object Storage (ADR-0016),
never in a local file. The state lives in a third project, `cichlids-tfstate`, holding only the
state bucket — under a distinct state key per tier in that one dedicated bucket.

## Credential model (ADR-0017, refining ADR-0014)

Protection against an accidental catastrophic destroy is **capability-based**: the ability to
reach the irreplaceable data never exists where automation can reach it.

A Hetzner Cloud project API token has only two levels — **Read** or **Read & Write** — with no
finer "no-delete" scope, and a Read & Write token can disable a resource's delete-protection
through the raw cloud API and then delete it, bypassing the OpenTofu guards. Least-privilege is
therefore **not** enforceable through the token. The enforceable boundary is the **project**:

- The **primary project (`cichlids`)** holds all live, rebuildable infrastructure. It has a
  single **Read & Write** cloud token, used by the operator and (for the future app tier) by CI.
  Everything here is "cattle" — rebuildable, with its data recoverable from the immutable
  backups. Within this project the destroy guards still apply: per-resource `delete_protection`
  and `prevent_destroy` (ADR-0014 Layer 1) and the fail-closed plan-diff policy gate (Layer 3).
- The **backup project (`cichlids-backup`)** holds the irreplaceable backups and has **no cloud
  token anywhere** — not for the operator's automation and not in CI. It is reached only through
  a write-scoped object-storage credential, and Compliance-mode Object Lock prevents deleting a
  backup object even with that credential.
- The **state project (`cichlids-tfstate`)** holds only the OpenTofu state bucket and likewise
  has **no cloud token**; CI and the operator receive only object-storage credentials to read
  and write state. Integrity comes from versioning, private access, and state encryption — not
  Object Lock (a retention would make the backend's lock object undeletable, ADR-0016).

**Break-glass** is not a separate API token but **privileged console / project-owner access**,
held out-of-band by the operator. A deliberate destroy is performed only through it, as a logged
un-protect-then-destroy procedure — never wired into the repo, CI, or the devcontainer. Because
coding agents and CI run under the operator's own GitHub identity, this separation — not any
in-CI approval or label — is what makes a catastrophic destroy impossible for automation
(ADR-0014 Layer 2).

**Residual exposures, stated honestly:** the DNS API is account-level rather than
project-scoped, so a DNS credential held by automation can change records — mitigated by using
the most-scoped DNS credential available; the zone is reconstructible and delegation lives at
the registrar. The object-storage credential held for state can delete state objects —
mitigated by versioning, which allows rollback.

## Prerequisites

- The pinned toolchain is present: run `./tools/verify-toolchain.sh core` (installs via
  `tools/install-toolchain.sh` if needed). This provides `tofu`, `sops`, `age`, and the rest.
- You can administer the domain `cichlids.com` at its registrar (to change nameservers).

## Bootstrap

Follow the steps in order. Each step states the **expected result** so you can confirm it before
moving on. Steps 1–9 prepare the three projects and their credentials; steps 10–11 apply the
backup and foundation tiers; step 12 delegates the domain. SOPS+age and Cloudflare R2 (see
"Later") are **not** needed for the foundation apply.

### 1. SSH key pair

1. On a durable machine (not a throwaway container), generate an ed25519 key pair:
   `ssh-keygen -t ed25519 -C operator@cichlids`.
2. Use the **public** key as `ssh_public_key` (via `terraform.tfvars` or `TF_VAR_ssh_public_key`)
   for the foundation tier. Keep the private key on your host.

**Expected result:** an ed25519 key pair exists; the public key is available for the apply, the
private key stays on your machine.

### 2. The three Hetzner Cloud projects

1. Create a Hetzner Cloud account and, inside it, **three** projects: `cichlids` (primary),
   `cichlids-backup`, and `cichlids-tfstate`.

**Expected result:** three empty Hetzner Cloud projects you can open in the console.

### 3. Primary-project cloud API token

1. In the **`cichlids`** project, under **Security → API tokens**, create one **Read & Write**
   token.
2. Provide it to the foundation apply as `TF_VAR_hcloud_token`, and later store it as a GitHub
   Actions secret for the CI-driven app tier (see "Later"). This is the **only** cloud token in
   the system.

**Expected result:** one Read & Write Cloud API token for the primary project, held out-of-band.
**No cloud token is ever created for `cichlids-backup` or `cichlids-tfstate`.**

### 4. Primary-project object-storage S3 credentials

1. In the **`cichlids`** project's Object Storage, create an **S3 access key / secret key** pair.

The foundation tier uses these (as `TF_VAR_s3_access_key` / `TF_VAR_s3_secret_key`) to manage the
media-master bucket.

**Expected result:** an S3 access key and secret key for the primary project, held out-of-band.

### 5. Backup-project object-storage S3 credentials

1. In the **`cichlids-backup`** project's Object Storage, create an **S3 access key / secret
   key** pair.

The backup tier uses these (as `TF_VAR_s3_access_key` / `TF_VAR_s3_secret_key` for that tier) to
manage the backup buckets, and the running backup workload later uses a write-scoped key to write
backups. **These credentials are held out-of-band only and are never added as a CI secret.**

**Expected result:** an S3 access key and secret key for the backup project, held out-of-band.

### 6. State bucket (created out-of-band, before any `tofu init`)

The remote backend needs a bucket that OpenTofu does **not** manage. Create it once, by hand, in
the **`cichlids-tfstate`** project:

1. Enable **Object Storage** in `cichlids-tfstate` and create a bucket named `cichlids-tfstate`.
2. **Enable versioning** on it.
3. Keep it **private**. Do **not** enable Object Lock — an Object-Lock retention would make the
   backend's lock object undeletable and cause stuck locks. Integrity here comes from versioning,
   private access, and state encryption (step 9).
4. Create an **S3 access key / secret key** pair in this project for reading and writing state.

**Expected result:** a private, versioned `cichlids-tfstate` bucket with Object Lock off, and an
S3 key pair for it. Both the operator and (later) CI use this key pair as the standard
`AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` backend credentials.

### 7. Hetzner DNS token

1. In the Hetzner DNS Console, create an **API token** for managing the zone.
2. Provide it to the foundation apply as `TF_VAR_hetznerdns_token`. Delegation of the domain
   happens **after** the apply (step 12), using the nameservers the apply outputs.

**Expected result:** a DNS API token exists, held out-of-band. (The DNS API is account-level, not
project-scoped — see "Residual exposures" above.)

### 8. State-encryption passphrase

1. Generate a strong passphrase (at least 16 characters) for native OpenTofu state encryption and
   store it out-of-band (e.g. a password manager). State holds secrets in cleartext, so it is
   encrypted at rest (ADR-0016).
2. Provide it as `TF_VAR_state_encryption_passphrase` whenever you run `plan`/`apply` on either
   tier. Never commit it.

**Expected result:** a state-encryption passphrase exists, held out-of-band only.

### 9. Apply the backup tier

The backup tier manages object storage only and takes **no cloud token**. Supply the backup
project's object-storage keys as the tier's provider credentials, and the state project's keys as
the backend credentials:

```sh
cd infra/backup
cp terraform.tfvars.example terraform.tfvars   # set endpoint/region (no secrets)

# Backend (state bucket) credentials — standard AWS S3 env vars (state project, step 6):
export AWS_ACCESS_KEY_ID=…
export AWS_SECRET_ACCESS_KEY=…

# Resource and encryption credentials (backup project, step 5; passphrase, step 8):
export TF_VAR_s3_access_key=…                # backup project object storage
export TF_VAR_s3_secret_key=…
export TF_VAR_state_encryption_passphrase=…

tofu init      # initialises the remote S3 backend (key backup/terraform.tfstate)
tofu plan      # review: creates the database-backup and media-master-backup buckets
tofu apply
```

For the production buckets at go-live, set `enable_object_lock = true` in `terraform.tfvars` so
the backups become immutable for their Compliance retention window (ADR-0013).

**Expected result:** state is stored and locked under `backup/terraform.tfstate` in
`cichlids-tfstate`; the two backup buckets exist in the `cichlids-backup` project, carrying their
protection guardrails. No cloud token was used.

### 10. Apply the foundation tier

Supply all credentials through the environment so they never land in a file, then apply:

```sh
cd infra/foundation
cp terraform.tfvars.example terraform.tfvars   # set ssh_public_key, location, etc. (no secrets)

# Backend (state bucket) credentials — standard AWS S3 env vars (state project, step 6):
export AWS_ACCESS_KEY_ID=…
export AWS_SECRET_ACCESS_KEY=…

# Resource and encryption credentials:
export TF_VAR_hcloud_token=…                 # primary-project Read & Write Cloud token (step 3)
export TF_VAR_hetznerdns_token=…             # DNS token (step 7)
export TF_VAR_s3_access_key=…                # primary-project object storage (step 4)
export TF_VAR_s3_secret_key=…
export TF_VAR_state_encryption_passphrase=…  # state encryption (step 8)

tofu init      # initialises the remote S3 backend (key foundation/terraform.tfstate)
tofu plan      # review: creates node, DB volume, media-master bucket, DNS zone
tofu apply
```

The backend's bucket, endpoint, and region default to the `nbg1` location; override them at init
with `-backend-config` for another location. For the production node at go-live, override
`location` and `server_type` (e.g. `ash`, `cpx41`) and set `enable_object_lock = true` in
`terraform.tfvars` (ADR-0009, ADR-0013).

**Expected result:** state is stored and locked under `foundation/terraform.tfstate`; the node,
database volume, `media-master` bucket, and the DNS zone exist in the `cichlids` project, all
carrying the protection guardrails. `tofu output dns_zone_nameservers` prints the nameservers for
step 12.

### 11. Delegate the domain

1. At the registrar for `cichlids.com`, set the nameservers to the ones from
   `tofu output dns_zone_nameservers` (run in `infra/foundation`).

**Expected result:** the domain resolves through Hetzner DNS.

## Later (not required for the foundation apply)

### App (ephemeral) tier

Once the app-tier configuration is present under `infra/app/`, apply it the same way, with the
same remote-backend pattern (a separate state key in `cichlids-tfstate`) and the primary
project's Read & Write token. This tier installs single-node k3s (with the bundled Traefik
disabled) via cloud-init and bootstraps Argo CD; from then on the cluster reconciles from Git. It
references no foundation data resources directly (ADR-0010, ADR-0014 Layer 2).

### SOPS age key

1. Generate an age key pair: `age-keygen -o keys.txt` (prints the `age1...` **public** key).
2. Put the **public** key into `.sops.yaml` at the repository root as the recipient for encrypted
   files. (Public keys are safe to commit; the private key is not.)
3. Keep the **private** key out-of-band and provision it into the cluster as the decryption secret
   that Argo CD's KSOPS plugin uses (ADR-0010). Never commit the private key.

### Cloudflare account, R2, and S3 token (serving layer)

1. Create a Cloudflare account and an **R2** bucket for media serving, plus an **R2
   S3-compatible** access key / secret key pair. The authoritative master copy stays on Hetzner
   Object Storage; R2 is the serving cache in front of it (ADR-0009). Wired in when the serving
   workloads are deployed.

### GitHub Actions secrets

Set these repository secrets so CI can run the app-tier apply, preview create/teardown, and the
DNS-01 certificate solver:

- **Primary-project Read & Write Hetzner Cloud token** (step 3) — app-tier `tofu apply` and
  preview environments.
- **Primary-project Hetzner Object Storage S3 access/secret key** (step 4) — media operations.
- **State-project Hetzner Object Storage S3 access/secret key** (step 6) — the app-tier backend
  (state).
- **State-encryption passphrase** (step 8) — so CI can read/write encrypted app-tier state.
- **Hetzner DNS token** (step 7) — cert-manager DNS-01 wildcard issuance for `*.dev`.
- **Cloudflare R2 S3 access/secret key** — media serving, when those workloads land.

**No cloud token for `cichlids-backup` or `cichlids-tfstate` is ever added — none exists.** The
**backup project's** object-storage key (step 5) is **not** a GitHub secret either; it stays
out-of-band and is provided to the running backup workload only. Privileged console /
project-owner access (break-glass) is **never** added here. The age **private** key is **not** a
GitHub secret — it is a cluster secret consumed by KSOPS.

### Branch protection on `main`

1. Enable branch protection on `main`: require pull requests and require the CI status checks to
   pass before merging (ADR-0005). This makes the green gate enforced rather than advisory.

> Branch protection requires repository settings that are unavailable on a private,
> non-organisation repository. Until the repository is moved to an organisation (or upgraded so
> the setting is offered), the green gate is enforced by convention: do not merge a PR whose
> checks are red.

## Break-glass: a deliberate destroy

A legitimate destroy of a protected resource never runs in CI and is never enabled by an in-CI
label or approval. It runs **only** as a manually-invoked local procedure on the operator host,
through privileged console / project-owner access, and it removes the relevant
`prevent_destroy`/delete-protection as a deliberate, logged step before the destroy (ADR-0014
Layer 3). Backups live in their own project with Compliance-mode immutability, reachable by no
cloud token, so even a successful destroy of the primary project is recoverable (ADR-0013,
ADR-0014 Layer 4, ADR-0017).

<!-- Story↔test binding (ADR-0015): task-2.3, task-2.16 -->

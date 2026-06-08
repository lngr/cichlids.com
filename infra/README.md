# Infrastructure bootstrap runbook

This is the operator runbook that turns the OpenTofu code in this directory into a running
platform, together with the credential model that keeps it safe. It is written for a human
operator working on their own host. **No secrets or tokens belong in this file** — every
credential is supplied at apply time through environment variables, or stored encrypted with
SOPS+age, and never committed.

The layout follows the two-tier split required by ADR-0010 and ADR-0014:

- `foundation/` — the **stateful** tier: the compute node and its disk, the database volume,
  the object-storage buckets, and the DNS zone. Rarely changed, applied out-of-band by the
  operator, protected against destruction.
- `app/` — the **ephemeral** tier: k3s (via cloud-init) and the Argo CD bootstrap. It holds no
  protected data resources and is what routine automation may apply.

Both tiers keep their OpenTofu state in a remote backend on Hetzner Object Storage (ADR-0016),
never in a local file.

## Credential model (ADR-0014)

Protection against an accidental catastrophic destroy is **capability-based**: the ability to
delete protected resources never exists where automation can reach it.

A Hetzner Cloud project API token has only two levels — **Read** or **Read & Write** — with no
finer "no-delete" scope. Least-privilege is therefore **not** enforced through the token. There
is a single **Read & Write** token, used by both the operator and CI; the protection against an
accidental destroy comes from the layered guardrails, not the token:

- per-resource `delete_protection` and `prevent_destroy` (ADR-0014, Layer 1), and
- the fail-closed plan-diff policy gate in CI that denies any delete/replace of a critical
  resource (ADR-0014, Layer 3).

**Break-glass** is not a separate API token but **privileged console / project-owner access**,
held out-of-band by the operator. A deliberate destroy is performed only through it, as a
logged un-protect-then-destroy procedure — never wired into the repo, CI, or the devcontainer.
Because coding agents and CI run under the operator's own GitHub identity, this separation —
not any in-CI approval or label — is what makes a catastrophic destroy impossible for
automation (ADR-0014, Layer 2).

## Prerequisites

- The pinned toolchain is present: run `./tools/verify-toolchain.sh core` (installs via
  `tools/install-toolchain.sh` if needed). This provides `tofu`, `sops`, `age`, and the rest.
- You can administer the domain `cichlids.com` at its registrar (to change nameservers).

## Foundation bootstrap

Follow the steps in order. Each step states the **expected result** so you can confirm it
before moving on. Steps 1–7 prepare credentials and the state bucket; step 8 applies the
foundation. SOPS+age and Cloudflare R2 (see "Later") are **not** needed for the foundation
apply.

### 1. SSH key pair

1. On a durable machine (not a throwaway container), generate an ed25519 key pair:
   `ssh-keygen -t ed25519 -C operator@cichlids`.
2. Use the **public** key as `ssh_public_key` (via `terraform.tfvars` or
   `TF_VAR_ssh_public_key`). Keep the private key on your host.

**Expected result:** an ed25519 key pair exists; the public key is available for the apply, the
private key stays on your machine.

### 2. Hetzner Cloud account and project

1. Create a Hetzner Cloud account and, inside it, a project named `cichlids`.

**Expected result:** an empty Hetzner Cloud project you can open in the console.

### 3. State bucket (created out-of-band, before any `tofu init`)

The remote backend needs a bucket that OpenTofu does **not** manage — the foundation creates
the media-master and backup buckets, so the state cannot live in either of them (ADR-0016).
Create it once, by hand:

1. Enable **Object Storage** in the project and create a bucket named `cichlids-tfstate`.
2. **Enable versioning** on it.
3. Keep it **private**. Do **not** enable Object Lock — an Object-Lock retention would make the
   backend's lock object undeletable and cause stuck locks. Integrity here comes from
   versioning, private access, and state encryption (step 7).

**Expected result:** a private, versioned `cichlids-tfstate` bucket exists, with Object Lock
off.

### 4. Cloud API token

1. Under **Security → API tokens**, create one **Read & Write** token.
2. Provide it to the foundation apply as `TF_VAR_hcloud_token`, and store it as a GitHub Actions
   secret for the CI-driven app tier (see "Later").

**Expected result:** one Read & Write Cloud API token exists, held out-of-band.

### 5. Object storage S3 credentials

1. In the project's Object Storage, create an **S3 access key / secret key** pair.

These credentials serve two roles: the OpenTofu **resources** use them (as
`TF_VAR_s3_access_key` / `TF_VAR_s3_secret_key`) to manage the media-master and backup buckets,
and the OpenTofu **backend** uses them (as the standard `AWS_ACCESS_KEY_ID` /
`AWS_SECRET_ACCESS_KEY` environment variables) to read and write state in the `cichlids-tfstate`
bucket.

**Expected result:** an S3 access key and secret key for Hetzner Object Storage, held
out-of-band.

### 6. Hetzner DNS token

1. In the Hetzner DNS Console, create an **API token** for managing the zone.
2. Provide it as `TF_VAR_hetznerdns_token`. Delegation of the domain happens **after** the apply
   (step 9), using the nameservers the apply outputs.

**Expected result:** a DNS API token exists, held out-of-band.

### 7. State-encryption passphrase

1. Generate a strong passphrase (at least 16 characters) for native OpenTofu state encryption
   and store it out-of-band (e.g. a password manager). State holds secrets in cleartext, so it
   is encrypted at rest (ADR-0016).
2. Provide it as `TF_VAR_state_encryption_passphrase` whenever you run `plan`/`apply`. Never
   commit it.

**Expected result:** a state-encryption passphrase exists, held out-of-band only.

### 8. Apply the foundation (stateful) tier

Supply all credentials through the environment so they never land in a file, then apply:

```sh
cd infra/foundation
cp terraform.tfvars.example terraform.tfvars   # set ssh_public_key, location, etc. (no secrets)

# Backend (state bucket) credentials — standard AWS S3 env vars:
export AWS_ACCESS_KEY_ID=…           # object storage access key (step 5)
export AWS_SECRET_ACCESS_KEY=…       # object storage secret key (step 5)

# Resource and encryption credentials:
export TF_VAR_hcloud_token=…                 # Read & Write Cloud token (step 4)
export TF_VAR_hetznerdns_token=…             # DNS token (step 6)
export TF_VAR_s3_access_key=…                # object storage (step 5)
export TF_VAR_s3_secret_key=…
export TF_VAR_state_encryption_passphrase=…  # state encryption (step 7)

tofu init      # initialises the remote S3 backend in cichlids-tfstate
tofu plan      # review: creates node, DB volume, two buckets, DNS zone
tofu apply
```

The backend's bucket, endpoint, and region default to the `nbg1` development location; override
them at init with `-backend-config` for another location. For the production node at go-live,
override `location` and `server_type` (e.g. `ash`, `cpx41`) and set `enable_object_lock = true`
in `terraform.tfvars` (ADR-0009, ADR-0013).

**Expected result:** state is stored and locked in `cichlids-tfstate`; the node, database
volume, `media-master` and `backup` buckets, and the DNS zone exist, all carrying the
protection guardrails. `tofu output dns_zone_nameservers` prints the nameservers for step 9.

### 9. Delegate the domain

1. At the registrar for `cichlids.com`, set the nameservers to the ones from
   `tofu output dns_zone_nameservers`.

**Expected result:** the domain resolves through Hetzner DNS.

## Later (not required for the foundation apply)

### App (ephemeral) tier

Once the app-tier configuration is present under `infra/app/`, apply it the same way, with the
same remote-backend pattern (a separate state key in `cichlids-tfstate`) and the Read & Write
token. This tier installs single-node k3s (with the bundled Traefik disabled) via cloud-init
and bootstraps Argo CD; from then on the cluster reconciles from Git. It references no
foundation data resources directly (ADR-0010, ADR-0014, Layer 2).

### SOPS age key

1. Generate an age key pair: `age-keygen -o keys.txt` (prints the `age1...` **public** key).
2. Put the **public** key into `.sops.yaml` at the repository root as the recipient for
   encrypted files. (Public keys are safe to commit; the private key is not.)
3. Keep the **private** key out-of-band and provision it into the cluster as the decryption
   secret that Argo CD's KSOPS plugin uses (ADR-0010). Never commit the private key.

### Cloudflare account, R2, and S3 token (serving layer)

1. Create a Cloudflare account and an **R2** bucket for media serving, plus an **R2
   S3-compatible** access key / secret key pair. The authoritative master copy stays on Hetzner
   Object Storage; R2 is the serving cache in front of it (ADR-0009). Wired in when the serving
   workloads are deployed.

### GitHub Actions secrets

Set these repository secrets so CI can run the app-tier apply, preview create/teardown, and the
DNS-01 certificate solver:

- **Read & Write Hetzner Cloud token** (step 4) — app-tier `tofu apply` and preview
  environments.
- **Hetzner Object Storage S3 access/secret key** (step 5) — the app-tier backend (state) and
  backup/media operations.
- **State-encryption passphrase** (step 7) — so CI can read/write encrypted app-tier state.
- **Hetzner DNS token** (step 6) — cert-manager DNS-01 wildcard issuance for `*.dev`.
- **Cloudflare R2 S3 access/secret key** — media serving, when those workloads land.

Privileged console / project-owner access (break-glass) is **never** added here. The age
**private** key is **not** a GitHub secret either — it is a cluster secret consumed by KSOPS.

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
`prevent_destroy`/delete-protection as a deliberate, logged step before the destroy (ADR-0014,
Layer 3). Backups are immutable and held off the routine path, so even a successful destroy is
recoverable (ADR-0013, ADR-0014, Layer 4).

<!-- Story↔test binding (ADR-0015): task-2.3 -->

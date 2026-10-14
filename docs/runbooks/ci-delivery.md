# CI and GitOps delivery

## Repository settings

The operator configures the following settings in `lngr/cichlids.com`:

- Under Actions, General, Workflow permissions, enable "Allow GitHub Actions to
  create and approve pull requests". Jobs declare their own token permissions.
- Under General, Pull Requests, enable squash merging and "Allow auto-merge".
- Protect `main` with the `required` check from the CI workflow. Require the
  branch to be up to date before merging. Required reviews also apply to staging
  bump PRs, so the operator supplies any required approval.
- Allow the SHA-pinned actions used in `.github/workflows/` in the Actions policy.
- Grant this repository's Actions token write access to the GHCR packages
  `cichlids-api`, `cichlids-migration` and `cichlids-web`. Set their visibility to
  public. Digest resolution and cluster image pulls use anonymous registry reads.
- Configure a main-branch ruleset bypass for the release automation's push
  identity, including the required PR and check rules. Verify that the repository
  supports bypass for that identity. `contents: write` alone does not bypass
  branch protection. If `github-actions[bot]` cannot receive the required bypass,
  use an operator-managed GitHub App installation token for the release checkout
  and push, with repository contents write access and an explicit ruleset bypass
  for that App. Store its credential through the operator's secret process and
  adjust the workflow token source. A rejected push fails the release workflow;
  it never opens a production promotion PR.

The staging job uses `GITHUB_TOKEN` to create its PR and explicitly dispatches
`ci.yml` on the bump branch. This requires `actions: write` in that job. GitHub
places ordinary PR workflow runs created through `GITHUB_TOKEN` behind workflow
approval; dispatch supplies an automatic run on the branch commit. The operator
checks the head commit and `required` result on the PR before enabling repository
protection. See [GitHub's workflow triggering rules](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow).

## Infrastructure and deployment inputs

CI's `infra-policy` job plans the app tier with the routine SSH and state-storage
capabilities. The `infra-app` workflow plans matching PR changes and applies the
gated plan after a merge to `main`. Neither job holds a Hetzner Cloud token.
Plans and applies use state locking with a five-minute lock wait.

Set repository variables `CICHLIDS_DATABASE_VOLUME_ID`, `CICHLIDS_NODE_IPV4` and
`CICHLIDS_NODE_HOST_KEY`. Supply repository secrets
`CICHLIDS_APP_SSH_PRIVATE_KEY`, `CICHLIDS_APP_STATE_PASSPHRASE`,
`CICHLIDS_S3_PRIMARY_ACCESS_KEY` and `CICHLIDS_S3_PRIMARY_SECRET_KEY` from the
operator's secret store. Fork PRs run the offline policy fixtures; they receive
no infrastructure credentials and skip the live plan step. Every required job
still executes. Same-repository PRs and CI dispatches require the live inputs.
The routine SSH capability is the app tier execution credential.

Before deployment, configure the environment media bucket and public URL in
`apps/api/overlays/<environment>/settings.yaml`, and provision the Secrets in the
[application rollout instructions](../../apps/api/README.md). The pipeline stops
at preflight while a bucket or image placeholder exists. CI manifest validation
permits the publication sentinels so repository checks work before activation.

## Workflows and job permissions

All workflows default to an empty permissions map. Every job declares its own
permissions. Action references use full commit SHAs and version comments.

| Workflow | Jobs | Token permissions |
| --- | --- | --- |
| `ci.yml` | `adr-index`, `story-test-binding`, `toolchain`, `infra-validate`, `infra-policy`, `secrets-scan`, `manifests`, `backend`, `client-core`, `e2e`, `keycloak-realm`, `stack-bootstrap` | `contents: read` |
| `ci.yml` | `required` | Empty |
| `images.yml` | `select` | `contents: read`, `pull-requests: read` |
| `images.yml` | `build` | `contents: read`, `packages: write` |
| `images.yml` | `staging` | `contents: write`, `pull-requests: write`, `actions: write` |
| `release.yml` | `promote` | `contents: write` |
| `infra-app.yml` | `plan-apply` | `contents: read` |
| `previews.yml` | `admission` | `contents: read`, `pull-requests: read`, `issues: write` |
| `exposure-watch.yml` | `public-exposure` | `contents: read` |

The `required` job always runs after all twelve CI jobs. A failed, cancelled or
skipped dependency fails the summary. CI runs on every PR and main push, plus
explicit dispatches. Backend and client checks use the application test scripts;
the E2E job runs the Playwright write specs against the compose stack. Native
Maestro paths and the seeded Playwright smoke have separate execution contracts.

## Image publication and staging

A main push builds the API and migration Dockerfile targets in `app/api`, and
the web Dockerfile with `app` as its context. The registry repositories are
`ghcr.io/lngr/cichlids-api`, `ghcr.io/lngr/cichlids-migration` and
`ghcr.io/lngr/cichlids-web`; every tag is the full source commit SHA.

After all builds succeed, staging resolves the published manifest digests through
Buildx and runs `tools/delivery.py bump staging`. The helper uses Kustomize image
overrides and deployment preflight. The job stages only the API and web staging
kustomizations and opens `deploy/staging-<sha>` into `main`, with squash auto-merge.
Its commit subject is `chore(deploy): stage <sha>`. An existing delivery branch
causes a safe failure; inspect its PR before retrying. Delivery runs serialize.
Pushes that change only staging or prod image overlays skip the image workflow,
which prevents publication loops after a staging bump merge.

Kustomize may serialize the kustomization formatting. Tests compare all fields
outside `images` and require their values to match. The staging job never changes
environment settings, Secrets, replica counts or production files.

## Release promotion

Publish a GitHub release whose tag starts with `v` and resolves to a commit on
`main`. `tools/delivery.py promote` resolves that commit's three SHA tags from
GHCR, validates every digest and updates only the API and web prod image
overrides. Missing manifests, zero digests and tags outside main fail before
promotion. Deployment preflight and manifest checks precede one commit on main
with subject `chore(deploy): promote <tag>`. Argo CD reconciles that commit.
Concurrent main updates reject the push; inspect and rerun against main.

Promotion changes image references. Production activation requires a separate
review of replicas, migration suspension, Secrets and routing. The production
API and web deployments have zero replicas, the migration Job is suspended and
the production placeholder owns the public route. The web image built on main
contains staging API and Keycloak addresses. Production web activation requires
a portable web configuration or an explicitly production-configured image and
its own verified publication contract. Promoting these digests is not evidence
of a working production web login. See the
[environment activation contract](../../apps/api/README.md#environment-activation-and-placeholder-ownership).

## Preview delivery

The Preview admission workflow executes trusted default-branch code through
`pull_request_target`, schedules and manual dispatch. It only reconciles labels.
It never checks out a PR head. The Images workflow reacts to successful admission
completion and reads the eligible same-repository PR list from GitHub. It builds
only open, ready PRs admitted with both `preview` and `preview-active`, subject to
the admission age and slot limits. Fork PRs never enter the build matrix.

The build checks out each selected `head.sha` using `pull_request`-independent
workflow execution, publishes all three SHA tags, and builds the preview web
image for `https://pr-<number>.dev.cichlids.com`. Its API address is that preview
origin. The Keycloak build input is the staging authority. The preview web scope
provides static UI rendering; its overlay has no API or identity integration.
API and migration images are published for the commit and have no active preview
workload. Full authenticated preview flows require an isolated API, Secrets,
identity policy and migration lifecycle within the quota.

The preview ApplicationSet discovers overlays at `head_sha` and supplies matching
Kustomize image overrides. The `placeholder` preview deployment serves the web
image using its entrypoint and bundled files. Its `unpublished` tag is a render
sentinel; deploy it through the ApplicationSet. Argo CD can temporarily report an
image pull failure while publication completes. After closure, expiry or label
removal, the generators stop selecting the PR and finalizers remove its service
Applications and disposable namespace. Admission runs every fifteen minutes;
ApplicationSet polling runs every 300 seconds.

## Local verification

```sh
python3 tools/test-delivery.py
actionlint
tools/verify-toolchain.sh core
tools/check-cluster-manifests.sh
python3 tools/check-secret-manifests.test.py
python3 tools/check-secret-manifests.py
conftest verify -p policy
node tools/check-story-test-binding.mjs
```

The delivery tests use disposable Git repositories, configured media fixtures and
published-looking fixture digests. They exercise Kustomize rendering, release
ancestry, missing registry responses and the actual summary shell step. These
checks need no registry login, GitHub write access or live cluster. Publication,
auto-merge, branch-rule bypass and Argo CD reconciliation require operator checks
in the configured repository and cluster.

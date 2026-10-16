# API and web workloads

## Build images

Run these commands from the repository root with Docker available:

```sh
docker build --target api -t cichlids-api:task-2.31 app/api
docker build --target migration -t cichlids-migration:task-2.31 app/api
docker build -f app/mobile/Dockerfile -t cichlids-web:task-2.31 app
python3 tools/test-app-images.py
```

The API and migration targets publish Release assemblies with the pinned .NET 10
SDK and use digest-pinned runtime images. They run as UID/GID 1654. The API target
contains its application settings; the migration target receives its settings
through environment variables. Development settings and local ETL configuration
are excluded from the build context. Each runtime supports a read-only root
filesystem and a memory-backed `/tmp` with a 64Mi bound.

The web build uses the pinned Node image and pnpm version from `app/package.json`,
builds `client-core`, and exports Expo with `APP_VARIANT=staging`. BusyBox httpd
serves the files as UID/GID 65534 on port 8080. Its error page serves `index.html`
for SPA deep links with HTTP 404. Static assets and the root page return HTTP 200.
The web container requires no writable volume.

Use `EXPO_PUBLIC_API_URL=https://staging.cichlids.com` and
`EXPO_PUBLIC_KEYCLOAK_URL=https://auth.staging.cichlids.com` for staging. The client
appends `/api` to the origin, including its web session routes. Its effective API
address is `https://staging.cichlids.com/api`. Supplying a base URL ending in `/api`
produces `/api/api` requests. The addresses are public build inputs and cannot be
changed through container environment variables.

Build production web assets separately with the production addresses:

```sh
docker build -f app/mobile/Dockerfile -t cichlids-web:prod \
  --build-arg APP_VARIANT=prod \
  --build-arg EXPO_PUBLIC_API_URL=https://cichlids.com \
  --build-arg EXPO_PUBLIC_KEYCLOAK_URL=https://auth.cichlids.com app
```

The smoke script uses only its own network, disposable PostgreSQL container and
`workload_test` database. It generates temporary Data Protection keys, applies
schema migrations, requests health and picture endpoints, checks refusal without
a slug secret, and checks the web files and SPA fallback. Its cleanup removes
its containers and network. It requires local .NET 10, the `postgres:17` image, installed app dependencies
and Playwright Chromium. Its browser smoke forwards staging requests to the local
images and checks gallery requests and the sign-in screen. It simulates Ingress
routing; certificate issuance and a real Keycloak login require operator checks.

## Secret contract

Provision these Secrets in `cichlids-staging` before syncing the API. Keep all
values in the operator's secret store. Use the corresponding namespace and
independent values for production.

| Secret | Keys | Source and use |
| --- | --- | --- |
| `postgres-app` | `pgpass` | CloudNativePG creates this Secret for the `postgres` Cluster, database and owner `cichlids`. Both workloads mount this key at `/db/pgpass`. |
| `api-slugs` | `secret` | Operator supplies the stable environment slug secret as `CICHLIDS_SLUG_SECRET`. Share it with any import workload for this environment. |
| `api-data-protection` | `key-<uuid>.xml` files | Operator supplies a valid ASP.NET Core Data Protection key ring. All files mount read-only at `/keys`. |
| `keycloak-web-bff` | `client-secret` | Use the same confidential client credential as the Keycloak realm apply for `cichlids-web-bff`. Staging enables web login. |
| `api-media` | `access-key`, `secret-key` | Operator supplies the application credential scoped to environment writes and golden media reads. |
| `api-web-push` | `public-key`, `private-key` | Optional. Operator supplies the VAPID key pair with which the API identifies itself to the push services of the browsers. The API reads the keys as `WebPush__VapidPublicKey` and `WebPush__VapidPrivateKey`. |

[CloudNativePG](https://cloudnative-pg.io/docs/devel/applications/) also supplies
`username` and `password` in `postgres-app`; the
workloads use its generated passfile to handle escaping in the password. The
connection specifies `postgres-rw:5432`, database `cichlids` and owner `cichlids`.
The job migrates the schema with the database owner. A narrower runtime database
role requires a separate grant and secret contract.

Generate the key ring with ASP.NET Core Data Protection using application name
`cichlids-api`, then create the Secret from its directory. Include a currently
active key. Back up the key ring separately from the database, restrict access,
and provision a replacement key before expiry. The API disables automatic key
generation outside Development. Restart the API after key rotation so its key
cache reads the mounted files. Verify login and session renewal after rotation.

Load Secret values from protected files, for example:

```sh
kubectl -n cichlids-staging create secret generic api-slugs \
  --from-file=secret="$HOME/.config/cichlids/staging/slug-secret"
kubectl -n cichlids-staging create secret generic api-data-protection \
  --from-file="$HOME/.config/cichlids/staging/data-protection"
kubectl -n cichlids-staging create secret generic api-media \
  --from-file=access-key="$HOME/.config/cichlids/staging/media-access-key" \
  --from-file=secret-key="$HOME/.config/cichlids/staging/media-secret-key"
kubectl -n cichlids-staging create secret generic keycloak-web-bff \
  --from-file=client-secret="$HOME/.config/cichlids/staging/web-bff-client-secret"
```

For encrypted Git delivery, use the repository's KSOPS pattern and real operator
recipients. Keep plaintext payloads outside the repository. Follow the
[Keycloak operator rollout](../keycloak/README.md#operator-rollout) and apply its
realm with the same BFF Secret before testing sign-in.

## Web Push reminders

The web app reminds members of due care through Web Push. The API signs its requests with a
VAPID key pair (RFC 8292) and encrypts every payload for the receiving browser (RFC 8291). The
private key exists only in the `api-web-push` Secret and in the memory of the API process. Both
overlays set `WebPush__Enabled: 'false'`. With Web Push enabled, the API refuses to start unless
both keys and `WebPush__Subject` are set, so a half-configured deployment never serves members.

Generate the key pair outside the repository. The tool writes the private key readable by its
owner only and never overwrites a file. Keep the files in the operator's secret store.

```sh
dotnet run tools/create-vapid-keys.cs -- files "$HOME/.config/cichlids/staging/web-push"
```

Provision the Secret directly:

```sh
kubectl -n cichlids-staging create secret generic api-web-push \
  --from-file=public-key="$HOME/.config/cichlids/staging/web-push/public-key" \
  --from-file=private-key="$HOME/.config/cichlids/staging/web-push/private-key"
```

For GitOps, configure the operator recipient and `argocd/sops-age` with the
[SOPS age key](../../infra/README.md#sops-age-key) instructions first. The repository's fixture
recipient is unsuitable for staging and production. Encrypt directly from the pipe:

```sh
kubectl -n cichlids-staging create secret generic api-web-push \
  --from-file=public-key="$HOME/.config/cichlids/staging/web-push/public-key" \
  --from-file=private-key="$HOME/.config/cichlids/staging/web-push/private-key" \
  --dry-run=client -o yaml | \
  sops --encrypt --filename-override apps/api/overlays/staging/web-push.sops.yaml \
    /dev/stdin > apps/api/overlays/staging/web-push.sops.yaml
```

Add a `ksops-generator.yaml` beside the encrypted file with `tools/fixtures/secrets/ksops-generator.yaml`
as the shape and `files: [web-push.sops.yaml]`, and list it under `generators` in the overlay's
Kustomization. `python3 tools/check-secret-manifests.py` verifies that only ciphertext is committed.

To switch Web Push on for an environment, set `WebPush__Enabled: 'true'` and `WebPush__Subject` in
its `settings.yaml`. The subject is a `mailto:` or `https:` contact of the operator and holds no
personal data. Restart the API pods so they read the Secret. A new key pair makes the subscriptions
of the old pair useless: members switch the reminders of their browser off and on again.

`WebPush__AllowedPushHosts__0` and further numbered entries replace the default allowlist of push
services (`fcm.googleapis.com`, `push.services.mozilla.com`, `push.apple.com`,
`notify.windows.com`); an entry matches the host and its subdomains. Each API instance runs the
sender every five minutes (`WebPushSender__Interval`). A reminder is recorded in the database
before it is sent, so several instances never send it twice. The pods need outbound HTTPS access to
the push services. The offline checks cover the manifests; the delivery of a message by a real
push service needs an operator check with a browser that subscribed on the deployed web app.

Webhook targets default to an empty allowlist and dispatch is disabled. To enable
delivery, configure `Webhooks__AllowedTargetHosts__0` and further numbered entries
in `settings.yaml`, review each target host, and set
`OutboxDispatcher__Enabled=true`. Webhook signing credentials belong to the
subscription records managed through the authenticated admin API. This workload
requires no separate webhook Secret.

## Configure and deploy staging

1. Confirm the namespace and its PostgreSQL Cluster are ready. Wait for the
   `postgres-app` Secret. The migration PreSync hook depends on this independently
   managed database Application, so provision the database before syncing the API.
2. Provision the Secrets above and apply the Keycloak realm. Confirm public DNS
   for `staging.cichlids.com` and `auth.staging.cichlids.com` reaches Traefik.
3. Configure the shared non-production media bucket and its tailnet gateway URL
   in `apps/api/overlays/staging/settings.yaml`. Confirm endpoint and region match
   the provisioned media store and `ObjectStorage__Bucket` matches the restore
   Job's `GOLDEN_BUCKET`. Snapshot media keys include their published golden
   version. `ObjectStorage__WritePrefix=environments/staging/` isolates staging
   writes.
   See the [golden snapshot procedure](../../tools/golden-snapshot/README.md).
4. Set each `images[].digest` in the API and web staging kustomizations to a
   published `sha256` digest. Set `images[].newName` if the registry repository
   differs from `ghcr.io/lngr/cichlids-api`, `cichlids-migration` or `cichlids-web`.
   Use a staging web build. Private registries require an image pull Secret
   referenced through `imagePullSecrets` in both Deployment and Job pods.
5. Run the offline checks and deployment preflight:

   ```sh
   tools/check-cluster-manifests.sh
   python3 tools/check-secret-manifests.py
   python3 tools/check-app-images.py staging
   ```

   The all-zero image digests are explicit publication placeholders. CI validates
   their syntax. Deployment preflight rejects them and unconfigured media bucket
   placeholders. Run this preflight before enabling Argo CD sync. The
   [delivery pipeline](../../docs/runbooks/ci-delivery.md) builds images, publishes
   SHA tags and promotes validated digests through Kustomize image overrides.
6. Review and publish the overlay changes through the repository workflow. Sync
   `api-staging` first. Its `api-migrate` PreSync hook runs only `migrate`, has a
   five-minute deadline and two retries, and is deleted after success. A failed
   migration blocks that Application's sync. ConfigMaps are needed only by the
   Deployment. EF migrations use their database lock and migration history.
   Review schema compatibility with any running API before an image update.
7. Sync `web-staging`. The Ingress routes the `/api` prefix to Service `api:80`
   and `/` to `web:80`; it preserves the API prefix. cert-manager issues
   `web-tls` through `letsencrypt-production`. Same-origin browser requests use
   the API's host-only secure cookie. Deployed environments require no CORS.
8. Check the hook completion, pod readiness and certificate, then open the site,
   inspect successful `/api/pictures` requests, and verify Keycloak login, renewal
   and sign-out. `/healthz` is an internal process health check, independent of
   database and identity availability. The startup probe allows five minutes.

The API enables ASP.NET Core forwarded headers for Traefik TLS termination.
Expose the API only through the internal ClusterIP Service and the trusted
Ingress. Requests from an untrusted direct pod connection can supply forwarded
headers, so restrict pod network access when permitting untrusted workloads.

## Environment activation and placeholder ownership

Staging discovers API and web overlays. Production discovers both with zero
Deployment replicas, a suspended migration Job and no web Ingress. Production
web login is disabled by omitting its callback and client secret settings.
The production placeholder serves its host. The placeholder preview overlay
serves the SHA-tagged web image for static UI review. API and web have no
dedicated preview overlays. Authenticated previews require isolated addresses,
identity policy, Secrets and migration lifecycle within the preview quota.
See the [delivery runbook](../../docs/runbooks/ci-delivery.md) for promotion
prerequisites and the preview build contract.

For production activation, publish a production web build, provision production
Secrets and bucket settings, review schema compatibility, and configure replicas
and the migration lifecycle. Public web activation also requires an explicit
web scope and a reviewed Ingress that replaces the production placeholder owner.

The staging placeholder overlay is absent. Argo CD's persistent ApplicationSet
uses `create-update` and preserves resources on deletion, so removing an overlay
does not delete an existing `placeholder-staging` Application. Before enabling
the web Ingress, disable its automated sync, remove the old placeholder Ingress
and retire that Application and its stateless resources explicitly. Confirm the
production and preview Applications are unaffected. Only one Ingress may own
`staging.cichlids.com` with a catch-all route.

## Media overlay configuration

The snapshot database stores full golden media keys under
`golden/<version>/media/variants/`. Reads use those keys directly. Public URL
construction encodes keys and performs no storage requests. The snapshot publisher
and restore Job select the version through `GOLDEN_VERSION`.

`ObjectStorage:WritePrefix` scopes environment uploads, reads, URLs, deletes and
listings. Discards and expired draft cleanup use this scope. The option defaults
to empty, as used in production. Writes and deletes under `golden/` are refused
regardless of configuration, and a golden write prefix is invalid. Database
references to golden objects can be discarded while the shared objects stay intact.

Provision the API credential with reads on the selected golden media prefix and
reads/writes/deletes on `environments/staging/` only. List permissions must allow
the environment prefix. The bucket stays private. Its tailnet-only delivery gateway
must serve both golden variants and environment variants at the URLs built from
`ObjectStorage:PublicBaseUrl`. API configuration does not create bucket policies
or gateway routes. Prefixes for previews use `environments/pr-<number>/` and the
same versioned golden subset. Shared media survives environment cleanup.

## Capacity

| Workload | CPU request / limit | Memory request / limit |
| --- | --- | --- |
| API, one active staging replica | 250m / 1 | 256Mi / 512Mi |
| Migration, transient staging job | 100m / 1 | 256Mi / 512Mi |
| Web, one active staging replica | 25m / 100m | 16Mi / 64Mi |

For a CX33 with 4 vCPUs and 8Gi, both PostgreSQL Clusters, production Barman,
both Keycloaks, the production placeholder and active staging app request
1400m/2976Mi. Reserve 500m/1024Mi for Argo CD, 350m/512Mi for add-ons and
500m/1024Mi for node services: total 2750m/5536Mi. Two Keycloak apply jobs
and the staging migration raise this to 3050m/6816Mi. A production API and
web replacing its placeholder raise the steady budget to 3000m/5792Mi,
or 3400m/7328Mi with both environments' migration and Keycloak apply jobs.
Check actual controller requests and resident memory before activation. Limits
allow bursts and do not guarantee simultaneous peak capacity on a single node.

# Keycloak realm configuration

Each deployed environment has one reviewed realm file. The apply combines it with
mounted secrets at runtime
([ADR-0025](../../docs/adr/0025-keycloak-realm-configuration-per-environment.md)).

| Environment | File | Hostnames |
|-------------|------|-----------|
| staging | `overlays/staging/cichlids-realm.json` | `auth.staging.cichlids.com`, `api.staging.cichlids.com`, web `staging.cichlids.com` |
| production | `overlays/prod/cichlids-realm.json` | `auth.cichlids.com`, `api.cichlids.com`, web `cichlids.com` |

The realm file of the local stack, `app/stack/keycloak/cichlids-realm.json`, is for local
development only and is never deployed. It must not be copied into these directories.

Two clients sign members in
([ADR-0027](../../docs/adr/0027-session-model-for-native-and-web-clients.md)):

- `cichlids-app`, the public client of the native app, allows `cichlids://auth` in both files.
- `cichlids-web-bff`, the confidential client through which the API signs in members of the web
  app (backend for frontend). It has the standard flow only, requires PKCE `S256` and offers
  `offline_access`. Staging allows the web login callback of the API,
  `https://staging.cichlids.com/api/auth/callback`, and the post logout redirect URI
  `https://staging.cichlids.com/me`, the Me tab that the browser returns to after a sign-out.
  Production lists no redirect URI, so the client cannot complete a login there. The web app and
  the API share one origin, so the client needs no web origin.

The two files differ only in `displayName`, in the `redirectUris` and `webOrigins` of both clients
and in the post logout redirect URIs of `cichlids-web-bff`.

### Client secret of the web backend

Provision `keycloak-web-bff` with key `client-secret` in the environment namespace.
The realm apply injects it for `cichlids-web-bff`. Use the same secret in the API's
`WebSession:ClientSecret` setting. Without this Secret, Keycloak generates a secret
on client creation and repeated applies preserve it. Web authentication requires
the API and Keycloak to use the same value.

## Rules enforced by CI

`tools/check-keycloak-realm.py` runs in the `keycloak-realm` job and fails when a file breaks
one of these rules:

- `sslRequired` is `all`.
- `registrationAllowed` is not set. The key must stay out of the files, because re-applying a file
  would close a registration that the operator opened after the account import.
- The files contain no `users`, `credentials` or `federatedIdentities`.
- Every client sets `directAccessGrantsEnabled` to false, none enables the implicit flow, and
  public clients require PKCE `S256`.
- No client contains a `secret`.
- `cichlids-web-bff` exists, is confidential (`publicClient` false, not bearer-only) with the
  `client-secret` authenticator, requires PKCE `S256`, enables the standard flow and has neither a
  service account nor the device grant.
- Redirect URIs are `https://` URLs or exactly `cichlids://auth`, without `*`. Web origins are
  exact `https://host` values without `+` or `*`.
- `verifyEmail` and `bruteForceProtected` are true and `duplicateEmailsAllowed` is not true.
- `offlineSessionMaxLifespanEnabled` is false, `offlineSessionIdleTimeout` is at least ten years
  and `cichlids-app` and `cichlids-web-bff` offer the `offline_access` client scope.
- No local address, `.test` domain, development credential or secret name or `exp://` URL appears.
- Both files are equal apart from the allowed differences above.

Run `python3 tools/check-keycloak-realm.py --self-test` and `python3 tools/check-keycloak-realm.py`
before committing a change to a realm file.

## Reserved usernames

The user profile of the realm contains a pattern validator on the username. It refuses
`community-archive`, `former-member-<id>` and `forum-member-<id>`, the names of the system and
placeholder profiles, on the registration form and in the admin API. The user profile is part of
the realm file, so both deployed files and the local realm file hold the same definition. The API
adds a second guard: a first login whose username is reserved gets a generated handle.

## Offline sessions

Members stay signed in on a device until they sign out
([ADR-0027](../../docs/adr/0027-session-model-for-native-and-web-clients.md)). The app and the web
backend request `offline_access`; offline sessions have no maximum lifespan and an idle timeout of
ten years (`315360000` seconds). A user needs the `offline_access` realm role for that scope.
Users created through the admin API or the registration form receive it through the default role
`default-roles-cichlids`; `etl keycloak-import` assigns that role to every account it creates,
since a partial import grants no default roles. A user without the role cannot sign in to the app.

## Accounts

The files contain no accounts. Create application operator and tester accounts with
kcadm through private administration:

```sh
kcadm.sh create users -r cichlids -s username=<name> -s email=<email> -s enabled=true -s emailVerified=true
kcadm.sh set-password -r cichlids --username <name> --new-password '<password>'
kcadm.sh add-roles -r cichlids --uusername <name> --rolename admin
```

The `admin` realm role grants application permissions. Keycloak console access
requires the appropriate `realm-management` client roles or a master operator
account. Test accounts receive the realm role `tester` (`--rolename tester`). The staging reset keeps
every account with the realm role `admin` or `tester` and deletes all others.

## Registration

Registration opens at runtime, after the account import.

- Production go-live order: apply the realm, run `etl keycloak-import --open-registration`
  (it opens registration only when no account is missing), then create the admin accounts.
- Staging: registration stays closed. Staging imports run without `--open-registration`, and
  an operator creates the tester accounts.

## Deploy staging and production

Deploy `overlays/staging` at `auth.staging.cichlids.com` and `overlays/prod` at
`auth.cichlids.com`. The environment ApplicationSets discover both Kustomizations.
Keycloak has no preview overlay: accounts are persistent environment state, and
per-PR identity servers exceed the disposable preview budget. Use staging identities
for preview authentication only when a reviewed client configuration permits the
exact preview callback. The realm files permit no wildcard callbacks.

Run one replica with `start`, PostgreSQL, a fixed HTTPS hostname and
`KC_PROXY_HEADERS=xforwarded`. Traefik terminates TLS. The ClusterIP Service exposes
8080; health probes use the unexposed management port 9000. Enable health and
metrics so readiness includes the database connection pool, as required by the
[Keycloak health guide](https://www.keycloak.org/observability/health). `Recreate` permits a
single instance during upgrades and introduces downtime. The image performs its
build during startup, so the startup probe permits ten minutes.

The HTTPS-only Ingress routes `/realms/cichlids`, `/resources/` and `/js/`. The realm
prefix includes discovery, OIDC, login actions, broker callbacks and the account
console. Kubernetes Prefix matching uses path elements, so `/realms/cichlids-other`
does not match. `/admin`, `/realms/master`, `/` and management endpoints have no
public route. The shared Traefik HTTP router redirects ordinary requests to HTTPS;
cert-manager HTTP-01 challenges use its exception. See the
[Keycloak proxy guide](https://www.keycloak.org/server/reverseproxy) and
[26.6.4 admin console source](https://github.com/keycloak/keycloak/blob/26.6.4/services/src/main/java/org/keycloak/services/resources/admin/AdminConsole.java).

### Images and resources

Images include an immutable registry digest in the manifests:

| Component | Version | Digest |
| --- | --- | --- |
| Keycloak | 26.6.4 | `sha256:0aae0de7fca85525f727d3354df17896092de8bb26ae4c12d89c77e5df8cbce4` |
| keycloak-config-cli | 6.5.1-26 (Keycloak client 26.1.0) | `sha256:1b22dfaa9ae0c71f74b0342f9221a6510f272da5def683dbba26a98e6b1b1411` |
| Python renderer | 3.13.7-alpine3.22 | `sha256:9ba6d8cbebf0fb6546ae71f2a1c14f6ffd2fdab83af7fa5669734ef30ad48844` |

Each Keycloak requests 250m CPU and 768Mi memory, with limits of 1 CPU and 1536Mi.
Its heap uses 128Mi initially and at most 512Mi. Each apply Job requests 100m CPU
and 512Mi memory, with limits of 1 CPU and 1Gi. The renderer requests 50m and 32Mi,
with limits of 250m and 64Mi. Kubernetes schedules the Job with the larger of its
init and main container requests.

The two PostgreSQL instances request 500m and 1024Mi in total; the production
Barman sidecar adds 100m and 128Mi. Two placeholders add 50m and 32Mi. Together
with both Keycloaks these workloads request 1150m and 2720Mi. For a CX33 with
4 vCPUs and 8Gi memory, budget 500m/512Mi for two API instances,
500m/1024Mi for Argo CD, 350m/512Mi for add-ons and 500m/1024Mi for the node
services. This planning budget totals 3000m/5792Mi, or 3200m/6816Mi with both
apply Jobs running. API and Argo CD figures are reservations for capacity planning;
check their actual requests and peak usage before deployment. Limits permit bursts
and do not guarantee simultaneous peak capacity. Monitor JVM resident memory and
PostgreSQL load on the single node.

### Database and backup

The `postgres` Application owns a `keycloak` Database and managed login role in
both persistent environments. `keycloak-db` supplies the role password and the
Deployment's credentials; its username must be exactly `keycloak`. Use Secret type
`kubernetes.io/basic-auth` and label `cnpg.io/reload: "true"` for password
reconciliation ([CloudNativePG role management](https://cloudnative-pg.io/docs/1.29/declarative_role_management/)). Database
ownership permits Keycloak schema migration without PostgreSQL superuser access.
The CloudNativePG Database uses `ensure: present`, `databaseReclaimPolicy: retain`,
`Prune=false,Delete=false` and the resource finalizer. The Cluster and its PVCs use
the protected stateful policy described in the [PostgreSQL runbook](../postgres/README.md).

Production Keycloak data shares the protected PostgreSQL volume and the Cluster's
Barman Cloud base backups and WAL archive. There is one ObjectStore and one backup
path for all databases in that Cluster. A PITR restore restores the whole Cluster;
restoring only Keycloak requires an isolated full restore followed by a database
export. Staging uses its own empty Cluster without backup bucket access. Previews
have no Keycloak Database or login role. Provision `keycloak-db` before syncing
the PostgreSQL Applications and verify the role and Database readiness before
syncing Keycloak. Argo CD does not order separate Applications by resource waves.

### Realm apply

Use the pinned keycloak-config-cli as an Argo CD `PostSync` hook Job. The Job
imports the reviewed environment JSON after Keycloak becomes healthy. The init
container reads that JSON and mounted secrets, writes a rendered file to a memory
volume, and sets its mode to 0600. ConfigMaps contain only the source JSON and the
renderer. The apply container receives the rendered file and admin credentials.
Configuration and HTTP trace logging are disabled to protect secret values.

The apply uses `no-delete` for managed entities, contains no users and makes no
realm deletion call. It omits `registrationAllowed` on creation and update. A new
realm has registration closed; an existing realm retains the operator's setting.
The cache and remote state are disabled: every complete sync reapplies the source,
and Keycloak stores no config-cli state containing rendered secrets. The apply
preserves runtime users and an existing BFF secret when no BFF Secret is mounted.
The [config-cli settings](https://github.com/adorsys/keycloak-config-cli/blob/v6.5.1/src/main/resources/application.properties)
and [realm apply code](https://github.com/adorsys/keycloak-config-cli/blob/v6.5.1/src/main/java/de/adorsys/keycloak/config/service/RealmImportService.java)
define these behaviors. Run the local container proof below for an image upgrade.

The renderer requires complete SMTP settings and authenticated STARTTLS. It rejects
empty values, `not-configured`, `${...}` and `$(...)` in secret settings. Disabled
identity providers without mounted credentials have their `clientId` and
`clientSecret` fields omitted. An enabled provider requires both credentials.
Credentials do not enable a provider: set its `enabled` flag through a reviewed
realm change. Keep environment parity checks green. Built-in user-profile labels
such as `${username}` remain Keycloak messages; config-cli variable substitution
is disabled. BFF credentials are injected as JSON values when the optional BFF
Secret exists.

Use complete Argo CD syncs for realm changes. Selective sync does not run hooks.
A failed Job leaves the existing realm usable and marks the sync failed; inspect
its status, correct the source or Secret and run a complete sync. Hook retries
require a new sync. Apply is a sequence of Admin API calls, so failure can leave
partially applied settings. Reapply converges managed values. `no-delete` preserves
entities removed from Git; review and remove obsolete entities explicitly through
the private Admin API. Manual changes to managed fields converge at the next sync,
not continuously between syncs. Updating a Secret requires a full Keycloak sync;
rotating DB or bootstrap credentials also requires a Deployment restart. Keep the
admin login in `keycloak-admin` valid for the hook, because bootstrap environment
variables only create an account in an empty database.

### Operator secrets

Create distinct values in each namespace, `cichlids-staging` and `cichlids-prod`.
Keep values in the operator's secret store. These names and keys are the contract:

| Secret | Keys | Requirement |
| --- | --- | --- |
| `keycloak-admin` | `username`, `password` | Required; strong, unique master admin credentials |
| `keycloak-db` | `username`, `password` | Required; type `kubernetes.io/basic-auth`, username exactly `keycloak`, label `cnpg.io/reload: "true"` |
| `keycloak-smtp` | `host`, `port`, `user`, `password`, `from` | Required; authenticated STARTTLS, normally port 587 |
| `keycloak-providers` | `google-client-id`, `google-client-secret`, `facebook-client-id`, `facebook-client-secret` | Optional; supply complete pairs for each configured provider |
| `keycloak-web-bff` | `client-secret` | Optional until web authentication is deployed; use the same value for the API's `WebSession:ClientSecret` |

The production Cluster also requires `postgres-backup-writer` with
`ACCESS_KEY_ID` and `ACCESS_SECRET_KEY`, provisioned through the PostgreSQL runbook.
Staging receives no backup writer Secret. cert-manager creates `keycloak-tls`.

The repository contains only a fixture SOPS recipient. Provision secrets directly
from mode-0600 files outside Git until the operator configures the real recipient
and `argocd/sops-age` using the [SOPS instructions](../../infra/README.md#sops-age-key).
For example, prepare the required keys in `$HOME/.config/cichlids/keycloak/staging/`
and run from the operator workstation:

```sh
kubectl config current-context
kubectl -n cichlids-staging create secret generic keycloak-db \
  --type=kubernetes.io/basic-auth \
  --from-env-file="$HOME/.config/cichlids/keycloak/staging/keycloak-db.env" \
  --dry-run=client -o yaml | kubectl apply -f -
kubectl -n cichlids-staging label secret keycloak-db cnpg.io/reload=true --overwrite
for keycloak_secret in keycloak-admin keycloak-smtp; do
  kubectl -n cichlids-staging create secret generic "$keycloak_secret" \
    --from-env-file="$HOME/.config/cichlids/keycloak/staging/$keycloak_secret.env" \
    --dry-run=client -o yaml | kubectl apply -f -
done
```

Use `cichlids-prod` and the production credential directory for production.
Provision optional Secrets through the same command when their complete keys exist.
For GitOps, encrypt Secret manifests with the operator recipient, reference them
through a KSOPS generator and verify decryption without logging rendered values.
The Database password Secret belongs in the PostgreSQL overlay, the other Secrets
in the Keycloak overlay. Give each Secret one Application owner. Commit only
ciphertext and generator references; keep `.sops.yaml` fixture keys out of real
secret encryption.

### Operator rollout

1. Review the platform DNS plan and apply the protected A RRsets `auth` and
   `auth.staging` using `infra/tofu.sh platform plan` and
   `infra/tofu.sh platform apply`. Verify both names resolve to the node.
   Confirm the ACME contact, Traefik, cert-manager and CloudNativePG controllers.
2. Provision the required Secrets above and the production backup writer. Verify
   the protected database volume and the Barman backup path using the PostgreSQL
   runbook. Publish the reviewed manifests to `main` through the project merge gate.
3. Sync PostgreSQL in staging, then production. Confirm managed role reconciliation
   and each `Database/keycloak` Ready condition. Check the production Cluster's WAL
   archiving and a completed base backup. Inspect `Prune=false,Delete=false` and
   the resource finalizers before enabling Keycloak writers.
4. Run a full sync of `keycloak-staging`, then `keycloak-prod`. Wait for the
   Deployment, certificate and successful PostSync hook. Use Argo CD's sync result
   for the hook, because successful Jobs are deleted.
5. Configure private administration below. Create a permanent master operator
   account through the private console, update `keycloak-admin` to its credentials
   and remove the temporary bootstrap account. The hook requires a master account
   with permission to create and manage the cichlids realm.
6. Verify discovery, HTTP redirect and public admin isolation:

   ```sh
   kubectl -n cichlids-staging rollout status deployment/keycloak --timeout=600s
   kubectl -n cichlids-staging wait --for=condition=Ready certificate/keycloak-tls --timeout=300s
   curl --fail --show-error https://auth.staging.cichlids.com/realms/cichlids/.well-known/openid-configuration
   curl --head http://auth.staging.cichlids.com/realms/cichlids/account/
   curl --head https://auth.staging.cichlids.com/admin/master/console/
   curl --head https://auth.staging.cichlids.com/realms/master/.well-known/openid-configuration
   ```

   Discovery returns the HTTPS issuer for that host. Plain HTTP redirects to HTTPS.
   Both excluded paths return the Traefik default 404. Repeat for `auth.cichlids.com`
   and `cichlids-prod`. Check the login page's resources and the account console
   through `/realms/cichlids/account/` with a tester account.
7. Create operator and tester accounts outside Git as described below. Request a
   tester password reset through the public login page and verify receipt and
   successful use of the HTTPS reset link. Verify email confirmation as well.
   This step needs the real mail provider and mailbox.
8. Run the production legacy account import before opening registration. Use
   `etl keycloak-import --open-registration` only in production after every
   required account exists. Staging registration is closed. Re-sync and verify
   production registration and user count are preserved. Perform an isolated
   backup restore and verify the restored Keycloak database and account IDs.

### Private administration

Use loopback port-forward from the operator workstation:

```sh
kubectl -n cichlids-staging port-forward service/keycloak 18080:8080
```

`KC_HOSTNAME_ADMIN=http://localhost:18080` fixes the console URL. The console's
login uses the master realm's frontend URL, so configure that runtime attribute
once using a second terminal. Enter the master credentials interactively; keep the
kcadm token file outside Git:

```sh
kubectl -n cichlids-staging exec -it deployment/keycloak -- \
  /opt/keycloak/bin/kcadm.sh config credentials \
  --server http://localhost:8080 --realm master --user <operator-username>
kubectl -n cichlids-staging exec deployment/keycloak -- \
  /opt/keycloak/bin/kcadm.sh update realms/master \
  -s attributes.frontendUrl=http://localhost:18080
```

Open `http://localhost:18080/admin/master/console/`. Use the same local port for
production in a separate port-forward session. Keep the cichlids realm's
`sslRequired: all` and public HTTPS hostname. The master frontend attribute is
operator runtime state outside the cichlids realm files. Restrict workstation
access and keep port-forward bound to loopback. The public Ingress never routes
the master realm or Admin API.

## Deployment checks

Run offline checks before committing:

```sh
./tools/check-cluster-manifests.sh
python3 tools/check-keycloak-realm.py --self-test
python3 tools/check-keycloak-realm.py
python3 tools/check-secret-manifests.py
```

Run the apply proof with a local Docker daemon:

```sh
python3 tools/check-keycloak-apply.test.py
```

The proof uses the manifest image pins and apply environment, creates a disposable
Keycloak development container, and removes its container and network on exit.
It verifies creation with registration closed, forced re-apply with registration
open, preservation of a runtime user and BFF secret, the discovery issuer, PKCE
login resources, the account console and private admin frontend URL. It uses synthetic
SMTP settings and does not establish delivery, the public HTTP redirect or admin
route exclusion, real TLS issuance, Argo CD hook
execution, PostgreSQL readiness or backup recoverability. Operator rollout checks
provide that runtime evidence.

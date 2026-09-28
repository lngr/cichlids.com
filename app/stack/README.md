# Local dev stack

Docker Compose services for local API development: Postgres, RustFS (S3-compatible object
storage), Keycloak.

## Prerequisites

- .NET SDK installed at `~/.dotnet` (the version pinned in `app/api/global.json`); invoke it as
  `~/.dotnet/dotnet`.
- [pnpm](https://pnpm.io) for the app (`app/mobile`, `app/client-core`).
- Docker with the Compose plugin.
- The legacy MySQL stack (`workspace-legacy/legacy-stack`) reachable on port 3306, and the legacy
  image originals available on disk (default `/workspaces/legacy-data/images/userpics`, see
  `app/api/src/Cichlids.Etl/appsettings.json`).

## Legacy MySQL with the imported dump

The ETL reads the legacy databases from the MySQL 5.7 stack in `workspace-legacy/legacy-stack`
(root password `legacy`). `bootstrap.sh` starts that stack when port 3306 is closed, and it expects
the dump to be imported into the stack's volume `cichlids-legacy_db_data` beforehand. The import
runs once per fresh volume and takes a few hours:

```sh
docker compose -f workspace-legacy/legacy-stack/docker-compose.yml up -d
zcat /workspaces/legacy-data/db-dump/frontosa-mysql-2023-09-20-06-00.sql.gz \
  | awk '/^-- Current Database: `mysql`/ { skip = 1 }
         /^\/\*!40103 SET TIME_ZONE=@OLD_TIME_ZONE/ { skip = 0 }
         !skip' \
  | docker exec -i cichlids-legacy-db-1 mysql -uroot -plegacy --default-character-set=utf8mb4
```

The `awk` filter leaves out the dump's `mysql` system schema. Importing it replaces the grant
tables with those of the legacy host, and after the next MySQL restart `root`/`legacy` is rejected.

To restore `root`/`legacy` on a volume that holds the legacy grant tables:

```sh
docker stop cichlids-legacy-db-1
docker run -d --name legacy-grant-reset -v cichlids-legacy_db_data:/var/lib/mysql mysql:5.7 \
  --skip-grant-tables --skip-networking
docker exec legacy-grant-reset mysql -e "FLUSH PRIVILEGES;
  ALTER USER 'root'@'localhost' IDENTIFIED BY 'legacy';
  CREATE USER IF NOT EXISTS 'root'@'%' IDENTIFIED BY 'legacy';
  ALTER USER 'root'@'%' IDENTIFIED BY 'legacy';
  GRANT ALL PRIVILEGES ON *.* TO 'root'@'%' WITH GRANT OPTION;"
docker rm -f legacy-grant-reset
docker start cichlids-legacy-db-1
```

## Bootstrap

`bootstrap.sh` brings a clean checkout up to the full local stand: Docker services, legacy MySQL,
ETL migration and seed, the Keycloak realm import, and the legacy account import into Keycloak. It
is idempotent: on an already-bootstrapped stack the service, legacy MySQL and realm steps report
their work as done, the ETL, media and account import steps rerun without changing the result,
and the script exits green.

```sh
app/stack/bootstrap.sh
```

Environment variables (all optional):

| Variable                     | Default                                                  | Purpose |
|-------------------------------|-----------------------------------------------------------|---------|
| `CICHLIDS_LEGACY_STACK_DIR`   | `/workspaces/cichlids.com/workspace-legacy/legacy-stack`  | Where to find the legacy MySQL stack's `docker-compose.yml` if MySQL is not already reachable on 3306. |
| `CICHLIDS_MEDIA_LIMIT`        | `3000`                                                     | How many media items the ETL media step seeds; `0` seeds the full legacy image set. |
| `CICHLIDS_AUTH0_EXPORT`       | `/workspaces/legacy-data/auth0/auth0-cichlids.json`        | Auth0 export (newline-delimited JSON) for the Keycloak account import; the step is skipped when the file does not exist. |
| `DOTNET`                      | `~/.dotnet/dotnet`                                         | Path to the `dotnet` executable. |

To run the Keycloak account import on its own:

```sh
cd app/api && CICHLIDS_ETL_AUTH0_EXPORT=/path/to/auth0-export.json \
  ~/.dotnet/dotnet run --project src/Cichlids.Etl -- keycloak-import
```

The command reads the Keycloak connection from the `Keycloak` section of
`app/api/src/Cichlids.Etl/appsettings.json`, overridable with `CICHLIDS_KEYCLOAK_URL`,
`CICHLIDS_KEYCLOAK_REALM`, `CICHLIDS_KEYCLOAK_ADMIN_USER` and `CICHLIDS_KEYCLOAK_ADMIN_PASSWORD`.
Repeated runs create only missing users and missing profile links.

Before it creates users, the import adds the `email_verified` mapper to the realm's `email`
client scope when the scope lacks it, which covers an existing realm that `bootstrap.sh` skips.
The API links a login to a migrated profile by email only when the token reports that email as
verified. A realm without an Auth0 export gets the mapper with:

```sh
docker compose -f app/stack/compose.yaml exec -T keycloak sh -c '
  /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user admin --password admin &&
  id=$(/opt/keycloak/bin/kcadm.sh get client-scopes -r cichlids --fields id,name --format csv --noquotes | grep ",email$" | cut -d, -f1) &&
  /opt/keycloak/bin/kcadm.sh create "client-scopes/$id/protocol-mappers/models" -r cichlids \
    -s "name=email verified" -s protocol=openid-connect -s protocolMapper=oidc-usermodel-property-mapper \
    -s "config.\"user.attribute\"=emailVerified" -s "config.\"claim.name\"=email_verified" \
    -s "config.\"jsonType.label\"=boolean" -s "config.\"access.token.claim\"=true" -s "config.\"id.token.claim\"=true"'
```

## Start / stop the Docker services directly

```sh
docker compose -f app/stack/compose.yaml up -d
docker compose -f app/stack/compose.yaml down
```

Data persists in named volumes across `down`/`up` cycles; add `-v` to `down` to wipe them.

## Services

| Service  | Port | Credentials                          |
|----------|------|---------------------------------------|
| Postgres | 5432 | db `cichlids`, user/pass `cichlids`/`cichlids` |
| RustFS   | 9000 | access/secret key `cichlids`/`cichlids-dev-secret` |
| Keycloak | 8180 | admin console user/pass `admin`/`admin` |

Keycloak's `cichlids` realm has two seeded local users (password `dev-password` for both):
`dev-user` (no realm role) and `dev-mod` (`moderator` and `admin` realm roles).

These are local-development defaults only and are not used in any deployed environment.

## Start the API and the app

```sh
cd app/api/src/Cichlids.Api && ~/.dotnet/dotnet run
```

The API listens on `http://localhost:5045` (see `Properties/launchSettings.json`).

```sh
cd app/mobile && pnpm install && pnpm exec expo start --web --port 8081
```

## Smoke test

The Playwright smoke script drives the Expo web build against a running API and asserts the
gallery, a picture detail, the tanks list and detail, and the community threads all render with
real seeded data:

```sh
cd app/mobile && pnpm run e2e:web
```

It expects the app at `PLAYWRIGHT_BASE_URL` (default `http://localhost:8081`, matching the `expo
start --web --port 8081` above) and the API reachable at the app's configured API URL (default
`http://localhost:5045`, see `app/mobile/src/api/client.ts`). Screenshots are written to
`app-screens/` at the repo root (override with `PLAYWRIGHT_OUT_DIR`).

## Write E2E stack

Write-path E2E specs (upload, comments, ...) must never write into the seeded dev database, the
dev bucket or through the dev API, since those carry the migrated legacy data used for manual
testing and demos. `app/mobile/e2e/write-stack.sh` gives them an isolated stand instead, reusing
the already-running Postgres, RustFS and Keycloak containers above:

- a dedicated database, `cichlids_e2e`, dropped and recreated on every `up` and migrated with the
  ETL (`dotnet run --project src/Cichlids.Etl -- migrate`);
- a dedicated bucket, `cichlids-e2e`, on the same RustFS instance, with the same public-read
  policy as the dev bucket so the app can load uploaded images;
- a second `Cichlids.Api` instance built in Release to a temp directory and run on
  `http://localhost:5046`, pointed at that database and bucket, with the outbox dispatcher
  disabled;
- a static export of the Expo web app (`expo export --platform web`), built against that API and
  served on `http://localhost:8082`; `@cichlids/client-core` is compiled first, since the app
  imports its `dist/` output.

```sh
bash app/mobile/e2e/write-stack.sh up      # bring the write stack up
bash app/mobile/e2e/write-stack.sh down    # tear it down (safe to rerun, and after a partial up)
bash app/mobile/e2e/write-stack.sh run <command...>   # up, run command with its env exported, always down
```

`run` exports `E2E_API_URL`, `E2E_WEB_URL`, `E2E_KEYCLOAK_URL` and `E2E_DB` for the command it
runs. `app/mobile`'s `pnpm run e2e:write` uses it to run every `*.write.mjs` Playwright spec under
`app/mobile/e2e/playwright/` sequentially, since they share one write database and one bucket.

The script only ever drops or empties the fixed names `cichlids_e2e` and `cichlids-e2e`; it never
touches the seeded dev database (`cichlids`) or the dev bucket (`cichlids-media`). It expects the
Postgres, RustFS and Keycloak containers from this directory's `compose.yaml` to already be
running and fails fast with a clear message otherwise; it does not start or stop them.

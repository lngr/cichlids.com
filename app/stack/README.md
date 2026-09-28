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
ETL migration and seed, and the Keycloak realm import. It is idempotent: re-running it against an
already-bootstrapped stack reports each step as already done and exits green.

```sh
app/stack/bootstrap.sh
```

Environment variables (all optional):

| Variable                     | Default                                                  | Purpose |
|-------------------------------|-----------------------------------------------------------|---------|
| `CICHLIDS_LEGACY_STACK_DIR`   | `/workspaces/cichlids.com/workspace-legacy/legacy-stack`  | Where to find the legacy MySQL stack's `docker-compose.yml` if MySQL is not already reachable on 3306. |
| `CICHLIDS_MEDIA_LIMIT`        | `3000`                                                     | How many media items the ETL media step seeds; `0` seeds the full legacy image set. |
| `DOTNET`                      | `~/.dotnet/dotnet`                                         | Path to the `dotnet` executable. |

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

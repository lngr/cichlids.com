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
cd app/mobile && pnpm install && pnpm exec expo start --web --port 8098
```

## Smoke test

The Playwright smoke script drives the Expo web build against a running API and asserts the
gallery, a picture detail, the tanks list and detail, and the community threads all render with
real seeded data:

```sh
cd app/mobile && pnpm run e2e:web
```

It expects the app at `PLAYWRIGHT_BASE_URL` (default `http://localhost:8098`, matching the `expo
start --web --port 8098` above) and the API reachable at the app's configured API URL (default
`http://localhost:5045`, see `app/mobile/src/api/client.ts`). Screenshots are written to
`app-screens/` at the repo root (override with `PLAYWRIGHT_OUT_DIR`).

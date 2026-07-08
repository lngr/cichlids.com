# Local dev stack

Docker Compose services for local API development: Postgres, RustFS (S3-compatible object
storage), Keycloak.

## Start

```sh
docker compose -f app/stack/compose.yaml up -d
```

## Stop

```sh
docker compose -f app/stack/compose.yaml down
```

Data persists in named volumes across `down`/`up` cycles; add `-v` to `down` to wipe them.

## Services

| Service  | Port | Credentials                          |
|----------|------|---------------------------------------|
| Postgres | 5432 | db `cichlids`, user/pass `cichlids`/`cichlids` |
| RustFS   | 9000 | access/secret key `cichlids`/`cichlids-dev-secret` |
| Keycloak | 8180 | admin console user/pass `admin`/`admin` |

These are local-development defaults only and are not used in any deployed environment.

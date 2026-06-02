# cichlids.com — Legacy Stack Architecture (Forensic Analysis)

Read-only analysis of the Docker / application-component backups under
`/workspaces/_extract/root_home/root/` cross-referenced with the live container
list in `/workspaces/docker.txt`.

> **Note on secrets:** Where credentials were found, the **value is redacted** but
> the name and what it connects to are documented. One plaintext DB password and
> one base64 registry credential were present in the backup (see §Secrets).

---

## 1. Live containers (ground truth)

From `/workspaces/docker.txt` (5 running containers, all bound to `127.0.0.1`,
i.e. fronted by a host reverse proxy — almost certainly nginx/Apache on the host):

| Container name | Image | Host port → container | Age | Role |
|---|---|---|---|---|
| `cichlids-api` | `cichlids-api` | `127.0.0.1:8081 → 80` | built ~13 mo | PHP 5.6 / Apache upload + image API (CentOS 6) |
| `cich-node-prod` | `cichlids-node-server` | `127.0.0.1:8084 → 4000` | built ~13 mo | Node SSR/API server, run via `pm2-runtime dist/server` |
| `nginx-image-server` | `quay.io/wantedly/nginx-image-server:latest` | `127.0.0.1:8082 → 8080` (also 80, 8090) | ~4 yr | On-the-fly image resizing (ngx_small_light) |
| `cichlidsbackend_api_1` | `cloud.canister.io:5000/redrainbow/cichlids.api` | `127.0.0.1:8086 → 80` | ~4 yr | .NET (`dotnet Cichlids.Api`) backend API |
| `phpmyadmin_phpmyadmin_1` | `phpmyadmin/phpmyadmin` | `127.0.0.1:8085 → 80` | ~4 yr | DB admin UI for MySQL |

**Important mismatch:** the running `cichlids-node-server` (port 4000, started with
`pm2-runtime dist/server`) is **newer** than the `docker-web` build dir in the backup
(which builds image `cichlids-web`, exposes `3001`, runs `node dist/server/bundle.js`).
The `docker-web` backup is an **earlier iteration** of the same Angular Universal
front-end; the deployed prod node server is a later rebuild not present as source here.

---

## 2. Component-by-component

### 2.1 `docker-web` — Angular 2 Universal front-end (`cichlids-web`)
- **Files:** `docker-web/Dockerfile`, `run.sh`, `build.sh`, `sync.sh`, `files/run-app.sh`, `files/package.json`, `files/dist/{server,client}/…`
- **Purpose:** The SPA front-end of the modern cichlids.com, server-side-rendered (Angular Universal) so HTML is delivered by a Node process.
- **Stack:** `node:5.9` base image. Angular `2.0.0-beta.11` + `angular2-universal-preview`, Express 4, RxJS 5 beta, TypeScript 1.8, Webpack 1, jQuery, Bootstrap 3 (`files/package.json`). Named `cichlids-frontend-web` v3.0.0, author `Alexander Langer <alex@cichlids.com>`, forked from `angular2-webpack-starter`.
- **Ports:** `EXPOSE 3001`; `run.sh` maps `127.0.0.1:8083:3001` (dev). Prod node server instead listens on 4000 → host 8084.
- **Entry:** `files/run-app.sh` → `node dist/server/bundle.js`.
- **Backend it talks to:** hard-coded `http://www.cichlids.com/api/` and `http://www.cichlids.com/disc/` (found in `dist/server/bundle.js` and `dist/client/main.*.bundle.js`). So the SPA calls the `/api/` path on the public host (which the host proxy routes to the PHP and/or .NET API) and `/disc/` (the discussion/forum = Phorum). Routes seen in client bundle: `/`, `/pic/:slug`, `/user/<name>`, `/users/`.
- **Deploy:** `sync.sh` rsyncs `../web/dist` + `package.json` into `files/` then rsyncs the whole dir to `root@www.cichlids.com:docker-web` — i.e. built locally, shipped to the host, `docker build` there (`build.sh`).

### 2.2 `docker-cichlids-api` — PHP 5.6 upload/image API (`cichlids-api`)
- **Files:** `docker-cichlids-api/Dockerfile`, `run.sh`, `build.sh`, `httpd-config.conf`, `sysconfig-httpd`.
- **Purpose:** PHP application serving the `/api/` path: photo uploads and serving user pictures. Operates directly on the TYPO3 community photo storage on disk.
- **Stack:** `centos:6` + EPEL + Webtatic. PHP 5.6 (`php56w` + opcache, mcrypt, pdo, mysql), **ImageMagick**, Apache `httpd` (`apachectl start`, `OPTIONS="-D FOREGROUND"`).
- **Port:** `EXPOSE 80`; `run.sh` maps `127.0.0.1:8081:80`. VirtualHost `www.cichlids.com`, `AllowOverride All`, `upload_max_filesize 50M` / `post_max_size 50M` (`httpd-config.conf`) — sized for photo uploads.
- **Volume mounts (`run.sh`) — this is the storage wiring:**
  - app code: `/var/www/html/www-cichlids/cichlids.extra/api` → `/var/www/html` (lives inside the legacy TYPO3 tree on the host)
  - `/data1/userpics/api_uploads` → `/var/www/uploads` (incoming uploads)
  - `/data1/userpics/user_pics` → `/var/www/user_pics` (the canonical photo store)
  - `/var/log/cichlids-api` → `/var/log/httpd`
- The PHP source itself is not in this backup (it is on the host under `cichlids.extra/api`), but the mounts prove this is the component that writes uploads and reads originals from `/data1/userpics`.

### 2.3 `docker-nginx-imgsrv` — on-the-fly image resizer (`nginx-image-server`)
- **Files:** `docker-nginx-imgsrv/run.sh`, `README`.
- **Purpose:** Dynamic image resizing/cropping in front of the photo store, so the SPA can request arbitrary sizes via URL parameters.
- **Stack:** Off-the-shelf image `quay.io/wantedly/nginx-image-server:latest` (`README` points at `github.com/wantedly/nginx-image-server` and `github.com/cubicdaiya/ngx_small_light`). It is **nginx + the `ngx_small_light` module** (ImageMagick/GD-backed), which does resize/crop on the fly from URL params (e.g. `.../sw=200,sh=200,...`).
- **Port:** container `8080` → host `127.0.0.1:8082` (`run.sh`); image also exposes `80` and `8090`. `-e SERVER_NAME=www.cichlids.com`.
- **Storage:** `-v /data1/userpics:/var/www/nginx/images` — mounts the **same** `/data1/userpics` tree that the PHP API writes to. So originals written by the PHP API under `/data1/userpics/user_pics/<user>/<filename>` are served and resized on demand by this nginx container. No separate resized-cache directory is configured here; `ngx_small_light` resizes per request (the module can cache internally).

### 2.4 `cichlidsbackend_api_1` — .NET API (`Cichlids.Api`)
- **Evidence:** live container only — image `cloud.canister.io:5000/redrainbow/cichlids.api`, command `dotnet Cichlids.Api…`, `127.0.0.1:8086 → 80`. Registry auth in `/root/.docker/config.json` (`cloud.canister.io:5000`, account `redrainbow`).
- **Purpose/role:** the "redrainbow" backend rewrite of the cichlids.com API (`Cichlids.Api`), a .NET Core service. It is the `redrainbow` company’s newer backend.
- **No source in this backup**, so endpoints, domain model, and whether it is event-sourced **cannot be confirmed from these files**. The naming (`cichlidsbackend`, a docker-compose-style `_api_1` suffix) indicates it was deployed via its own `docker-compose` project (`cichlidsbackend`) not included here. Event-sourcing is **unverified** — no `*.csproj`, `Startup.cs`, EventStore config, or migrations are present in the analyzed directories. *(Flagged as a gap; would need the `cichlidsbackend` repo / image to answer §7 definitively.)*

### 2.5 `aurora` — database migration staging
- **Files:** `aurora/script.sh`, `aurora/cichlids_typo3.sql` (~422 MB), `aurora/cichlids_phorum5.sql` (~409 MB).
- **Purpose:** **Not a runtime server.** This is a DB export staging area, named after **AWS Aurora** — prepared for migrating the two MySQL databases into Aurora/RDS. `script.sh` is a `mysqldump … --databases cichlids_typo3 --single-transaction --compress --order-by-primary` with a commented-out pipe into `mysql -u <RDS_user> --host=<host_name>` (the RDS import command, left as a template).
- **Databases dumped:**
  - `cichlids_typo3` — the TYPO3 CMS + community DB. Tables include the full TYPO3 core (`pages`, `tt_content`, `fe_users`, `be_users`, `sys_*`, `cache_*`) plus the community extension **`tx_cwtcommunity_*`**: `photos`, `photo_comments`, `albums`, `guestbook`, `buddylist`, `abuse`, `message`, `profileviews`, `icons`. Photo records (`tx_cwtcommunity_photos`) store `filename`, `width`, `height`, `size`, `cruser_id`, `album_uid` — the on-disk path is `user_pics/<user>/<filename>`.
  - `cichlids_phorum5` — the **Phorum 5** discussion forum DB (`phorum_messages`, `phorum_users`, `phorum_forums`, `phorum_pm_*`, …). This backs the `/disc/` path referenced by the SPA.
- **Auth0 migration evidence:** table `fe_users_auth0` maps Auth0/Facebook/Google identity `sub` → TYPO3 `fe_users.uid`. So authentication had been migrated from TYPO3-native logins to **Auth0** (social: Facebook, Google, plus `auth0|…` accounts). The front-end login images (`google_login`, `yahoo_login`, `hotmail_logo`, `f_logo`) in `docker-web/.../static/img` corroborate social login.

### 2.6 `.docker/config.json`
- Docker client credential store: a single registry login for `cloud.canister.io:5000` (user `redrainbow`, email `alexander.langer@redrainbow.de`). This is how the host pulled the `.NET` `cichlids.api` image. **The `auth` token is a base64 credential and is a secret (see below).**

---

## 3. Image pipeline (how photos were stored, resized, served)

```
Upload:   SPA → POST /api/ (host proxy → PHP cichlids-api :8081)
                 │ ImageMagick, 50M max
                 ▼
          /data1/userpics/api_uploads  (staging)  →  /data1/userpics/user_pics/<user>/<file>
                                                      (canonical originals; DB path "user_pics/<user>/<file>")

Serve:    SPA <img src=".../<resize params>/user_pics/<user>/<file>">
                 │  host proxy → nginx-image-server :8082 (ngx_small_light)
                 ▼
          reads /var/www/nginx/images (= /data1/userpics) → resizes/crops on the fly → JPEG/PNG out
```

- **Originals** live on host disk at `/data1/userpics/user_pics/<user>/<filename>` (confirmed by `move_pics.php` which sets DB `image = user_pics/<user>/<file>`, and by the PHP API volume mount).
- **Uploads** land in `/data1/userpics/api_uploads` first (PHP API mount).
- **Resizing** is done **on demand** by the `wantedly/nginx-image-server` (`ngx_small_light`) container, which mounts the same `/data1/userpics` tree read-only-ish at `/var/www/nginx/images`. No pre-rendered thumbnail directory is configured; sizes are generated per request from URL params.
- ImageMagick is also installed in the PHP API container (likely for upload-time normalization / initial sizing).

---

## 4. Secrets / credentials / hostnames found (values redacted)

| Where | Name | Connects to | Notes |
|---|---|---|---|
| `alex_home/.../scripts/cichlids/config.php` | `cichlids_db_username` = `cichlids_typo3` | MySQL `cichlids_typo3` DB on `localhost` | maintenance scripts |
| same | `cichlids_db_password` | MySQL | **plaintext password present in backup — redacted here** |
| same | `cichlids_db_host` = `localhost`, `cichlids_db_db` = `cichlids_typo3` | MySQL | |
| `.docker/config.json` | `auths["cloud.canister.io:5000"].auth` | private container registry (canister.io) | base64 `user:pass` for `redrainbow` — **secret, redacted** |
| `docker-cichlids-api/run.sh` | volume paths `/data1/userpics/{api_uploads,user_pics}` | host disk | not a secret but key infra path |
| SPA bundles | `http://www.cichlids.com/api/`, `/disc/` | PHP API + Phorum | hard-coded endpoints |
| `nginx-imgsrv/run.sh` | `SERVER_NAME=www.cichlids.com` | image server | |
| `cichlids_typo3.sql` | `fe_users_auth0` (Auth0/FB/Google `sub`s) | Auth0 | identity mapping, contains real social IDs |

---

## 5. Overall architecture / request flow

```
                              Internet
                                 │  https://www.cichlids.com
                                 ▼
                  ┌───────────────────────────────────┐
                  │   Host reverse proxy (nginx/Apache)│   all containers bound to 127.0.0.1
                  │   path-based routing               │
                  └───────────────────────────────────┘
            /                /api/        /disc/        (image URLs)
            │                  │             │                │
            ▼                  ▼             ▼                ▼
  ┌──────────────────┐  ┌─────────────┐  ┌────────┐  ┌────────────────────┐
  │ cich-node-prod   │  │ cichlids-api│  │ Phorum5│  │ nginx-image-server │
  │ Angular Universal│  │ PHP5.6+IM   │  │ (PHP)  │  │ ngx_small_light    │
  │ SSR  :8084→4000  │  │ Apache:8081 │  │ legacy │  │ :8082→8080         │
  └──────────────────┘  └──────┬──────┘  └───┬────┘  └─────────┬──────────┘
                               │             │                 │
        ┌──────────────────────┼─────────────┼─────────────────┘
        │                      ▼             ▼                  ▼
        │              ┌───────────────┐  /data1/userpics  (user_pics/<user>/<file>,
        │              │ MySQL (host)  │  on host disk        api_uploads/)
        │              │ cichlids_typo3│
        │              │ cichlids_phorum5
        │              └───────────────┘
        ▼                       ▲
  ┌──────────────────┐          │  (admin)
  │ cichlidsbackend  │   ┌──────────────────┐
  │ _api_1 (.NET     │   │ phpMyAdmin :8085 │
  │ Cichlids.Api)    │   └──────────────────┘
  │ :8086→80         │
  └──────────────────┘
        ▲
        │ image pulled from cloud.canister.io:5000/redrainbow/cichlids.api
   Auth: Auth0 (Facebook / Google / Auth0) → fe_users_auth0 maps sub→TYPO3 uid
```

**How legacy and new coexisted.** The original site was **TYPO3** (CMS in `cichlids_typo3`)
with the **`cwtcommunity`** extension providing the photo community (albums, photos,
comments, guestbook, buddylist) and a separate **Phorum 5** forum. A modernization layer
was added *on top of the same data*: an **Angular 2 Universal SPA** (`cich-node-prod`)
became the front-end, calling a thin **PHP 5.6 `/api/`** that reads/writes the existing
TYPO3 photo storage on `/data1/userpics`, while the SPA still linked to the legacy
**Phorum** under `/disc/`. Authentication was lifted out of TYPO3 into **Auth0** (social
login), with `fe_users_auth0` bridging Auth0 subjects to legacy `fe_users`. Image delivery
was modernized with the **wantedly nginx-image-server** doing on-the-fly resizing over the
same on-disk originals. A separate **.NET `Cichlids.Api`** (the "redrainbow" rewrite,
pulled from a private canister.io registry) ran alongside as a newer backend. The `aurora`
directory shows the next planned step: dumping both MySQL DBs for migration into AWS
Aurora/RDS.

---

## 6. Gaps / unverified

- **.NET `Cichlids.Api` internals** (endpoints, domain, whether event-sourced) **could not be determined** — no source, `*.csproj`, `Startup.cs`, or migrations are in the analyzed directories; only the running image reference exists. Needs the `cichlidsbackend` compose project / image inspection.
- The **deployed** `cichlids-node-server` (pm2, port 4000) source is not in the backup; only the older `cichlids-web` (port 3001) build artifacts are present.
- PHP source of the `/api/` app lives on the host under `cichlids.extra/api` (mounted, not in this backup).

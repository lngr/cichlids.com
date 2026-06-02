# 04 — Server / Runtime Configuration (Forensic Analysis)

Read-only analysis of the legacy server configuration extracted under
`/workspaces/_extract/etc/etc` (host OS: **CentOS 6.10 Final**, `cpe:/o:centos:linux:6:GA`).
Cross-referenced with the running-container snapshot in `/workspaces/docker.txt`.

The host ran **nginx as the public TLS-terminating edge/reverse-proxy** on the public IPs
`176.9.120.235` (IPv4) and `2a01:4f8:151:74e2::2` (IPv6). nginx routed traffic to
Apache (TYPO3) and to several Docker containers. Apache itself listened only on
loopback `:8080` (TYPO3 backend), never directly on the public interface.

---

## 1. Domains / Hostnames / Subdomains

Derived from nginx vhosts (`server_name`), Apache vhost (`ServerName`), and the three
Let's Encrypt certificates (each is a single-domain cert; SAN == CN).

| Domain / Hostname | Source(s) | Role |
|---|---|---|
| `www.cichlids.com` | nginx ssl + http, Apache vhost, LE cert | Primary public site (canonical host) |
| `cichlids.com` | nginx http (`virtual.conf`) | Apex → 301 redirect to `www.cichlids.com` |
| `dev.cichlids.com` | nginx ssl + http, LE cert | Staging/dev site (`X-Robots-Tag noindex`) |
| `api.cichlids.fishauth.net` | nginx ssl + http, LE cert | Auth/API host (static root) |
| `cichlids.fishauth.net` | nginx ssl + http | Alias of the fishauth host (uses api cert) |

**Let's Encrypt certificates** (`/etc/letsencrypt/live`, all via ACME v02, certbot + nginx installer):
- `www.cichlids.com` (single domain)
- `dev.cichlids.com` (single domain)
- `api.cichlids.fishauth.net` (single domain; also serves `cichlids.fishauth.net`)

Server admin contact (Apache `ServerAdmin`): `alex@cichlids.com`.

---

## 2. Reverse-Proxy / vhost → backend routing

Public routing is done by **nginx** (`/etc/nginx/conf.d/ssl.conf`), not Apache.
Container ports per `docker.txt`: 8081=api(Apache/PHP), 8084=node(prod), 8082=image-server(nginx),
8086=dotnet, 8085=phpMyAdmin. Note 8083 (dev node) and 8080 (host Apache/TYPO3) are **not**
in `docker.txt` — 8080 is the host's Apache; 8083 is the dev node server (container/process not in the snapshot).

### `www.cichlids.com` (HTTPS :443, default_server) — the main router

| Location / pattern | proxy_pass target | Backend (per docker.txt) | Notes |
|---|---|---|---|
| `/api` | `http://127.0.0.1:8081` | **cichlids-api** (PHP/Apache) | strips `/api` prefix |
| `/api2` | `http://127.0.0.1:8086` | **cichlidsbackend_api_1** (.NET dotnet) | rewrites `/api2/* → /api/*` |
| `/p/n/{100,200,400,800,1600,400c}/…` | `http://127.0.0.1:8082` | **nginx-image-server** | dynamic resize of `images/user_pics`; proxy_cache 1y |
| `/p/u/{100,200,400,800,1600}/…` | `http://127.0.0.1:8082` | **nginx-image-server** | resize of `images/api_uploads/files`; proxy_cache 1y |
| `/p/` | (local alias) | — | `cichlids.extra/static/pics/`; hashed thumb rewrites |
| `/assets`, `*.bundle.(js\|css)` | (local files) | — | Angular browser build under `/var/www/html/docker/prod/dist/browser` |
| `/fileadmin`, `/typo3temp`, `/uploads` | `@apache` | **host Apache :8080** (TYPO3) | `try_files` then fall back to Apache |
| `/typo3temp/pics/`, `/uploads/tx_usercichlids/user_pics/` | (local alias) | — | served directly from TYPO3 dirs |
| `/` (catch-all) | `@nodejs → http://127.0.0.1:8084` | **cich-node-prod** (PM2 Node SSR) | default app backend |

The www vhost also defines a block of legacy SEO **301 rewrites**
(`/tank-pictures → /pictures`, `/browse* → /pictures`, `/auth0/callback → /`, `/nc/* → /*`, etc.).

### `dev.cichlids.com` (HTTPS :443)

| Location | Target | Backend | Notes |
|---|---|---|---|
| `/` | `@nodejs → http://127.0.0.1:8083` | dev Node server (not in docker.txt snapshot) | root `/var/www/html/docker/dev/browser`; gzip on; `X-Robots-Tag noindex` |

### `api.cichlids.fishauth.net` / `cichlids.fishauth.net` (HTTPS :443)

Static file serving only (no proxy): roots `/var/www/html/api.cichlids.fishauth.net`
and `/var/www/html/cichlids.fishauth.net`. HSTS header set on the api host.

### HTTP :80 (`virtual.conf`) — redirects only
- `www.cichlids.com` & `cichlids.com` → `https://www.cichlids.com$request_uri` (301)
- `api.cichlids.fishauth.net` / `cichlids.fishauth.net` → `https://$host…` (301)
- `dev.cichlids.com` → `https://dev.cichlids.com…` (301)
- `client_max_body_size 20M` (matches PHP `upload_max_filesize`)

### Apache (`httpd/conf.d/vhosts.conf`) — TYPO3 backend, loopback only
- `Listen 8080`; `<VirtualHost *:8080>` `ServerName www.cichlids.com`
- `DocumentRoot /var/www/html/www-cichlids/cichlids` (TYPO3 install, `AllowOverride All`)
- Aliases: `/p → cichlids.extra/static/pics`, `/disc → cichlids.extra/disc`, `/rss → cichlids.rss`
- httpd.conf also has `Listen 443` + `NameVirtualHost *:443`, but the active `ssl.conf` is intentionally **empty** (see `conf.d/HINWEIS`: a yum-generated ssl.conf once blocked Apache startup, so an empty one is kept). TLS is handled by nginx, not Apache.

---

## 3. PHP version, image extensions & limits

- **PHP 5** on the host — `httpd/conf.d/php.conf` loads `libphp5.so` / `libphp5-zts.so`,
  handler `php5-script`. (Host php.ini dates to 2012; this is the TYPO3 host runtime.
  The `cichlids-api` container runs its own PHP — not captured here.)
- **Image extension: GD** (`php.d/gd.ini` → `extension=gd.so`). **No ImageMagick/imagick**
  PHP extension is enabled (no `imagick.ini` in `php.d/`); a standalone ImageMagick binary
  config exists at `/etc/ImageMagick`, and dynamic image resizing is delegated to the
  **nginx-image-server (small_light)** container, not PHP.
- Other enabled extensions (`php.d/`): bcmath, curl, dom, fileinfo, **gd**, json, **mbstring**,
  **mcrypt**, mysql, mysqli, pdo, pdo_mysql, pdo_sqlite, phar, posix, sqlite3, sysv{msg,sem,shm},
  tidy, wddx, xmlreader, xmlwriter, xsl, zip. (`apc.ini` present as `.rpmsave` → APC disabled.)
- **Upload / resource limits (`/etc/php.ini`):**

| Directive | Value |
|---|---|
| `upload_max_filesize` | **20M** |
| `post_max_size` | 8M |
| `memory_limit` | 128M |
| `max_execution_time` | 30 s |
| `max_input_time` | 60 s |
| `file_uploads` | On |

> Note: `post_max_size` (8M) is **smaller** than `upload_max_filesize` (20M) and the nginx
> `client_max_body_size` (20M); large uploads through PHP would be capped at 8M unless the
> upload path bypassed PHP (uploads were handled by the API container, not the TYPO3 host).

---

## 4. Scheduled jobs (`/etc/crontab` + `/root/bin`)

App-relevant cron jobs (running as `root`, scripts in `/root/bin`, recovered from
`/workspaces/_extract/root_home/root/bin/`):

| Schedule | Command | Purpose |
|---|---|---|
| `0 6 * * *` | `mysql-backup.sh` | `mysqldump -A` of all DBs to `/data1/mysql-backup`; keeps 7 days (`find -mtime +7 -delete`) |
| `0 5 * * 0` (Sun) | `mysql-prune.sh` | Truncates TYPO3 cache/log tables in DB `cichlids_typo3` (`tx_realurl_*`, `sys_log`, `cache_hash`, `user_cichlids_ofthehour`); deletes `fe_sessions` >30d |
| `* * * * *` | `testload.sh` | Load-average watchdog; dumps top/iostat/httpd error_log when load > 6 |
| `0 0 0 * *` | `rm …/typo3conf/deprecation*.log` | Clears TYPO3 deprecation logs |
| `12 17 * * 0` (Sun) | `certbot-auto renew --nginx` | Let's Encrypt renewal |
| `55 23 * * *` | `send-api-errors.sh` | Greps daily `/var/log/cichlids-api/error_log` for PHP errors (filters known notices) |
| `* * * * *` | `sync-user-logins.sh` | `docker exec cichlids-api … php sync_user_logins.php` |
| `0 22 * * *` | `sync-comments.sh` | `docker exec cichlids-api … php sync_comments_sqs.php` (SQS-driven comment sync) |

Additional recovered scripts (not all wired in crontab here): `duplicity-job.sh`,
`duplicity-list.sh`, `backup-space.sh` (offsite FTP du-report to `88.198.42.91`),
`testload_stop_httpd.sh`.

> Security note: `mysql-backup.sh` / `mysql-prune.sh` contain a hardcoded MySQL `backup`
> user password, and `backup-space.sh` contains hardcoded FTP credentials. These are
> live secrets present in the backup and should be treated as compromised/rotated.

`/etc/cron.d` only holds standard CentOS entries (`0hourly`, `raid-check`, `sysstat`).

---

## 5. MySQL server configuration (`/etc/my.cnf`)

Charset/collation is consistently **utf8mb4** across client, mysql, and server:

| Setting | Value |
|---|---|
| `[client] default-character-set` | `utf8mb4` |
| `[mysql] default-character-set` | `utf8mb4` |
| `character-set-server` | `utf8mb4` |
| `collation-server` | `utf8mb4_unicode_ci` |
| `character-set-client-handshake` | `FALSE` (forces server charset, ignores client handshake) |

Other notable `[mysqld]` settings: `datadir=/var/lib/mysql`, `max_connections=1500`,
binary logging on (`log-bin=frontosa-bin`, `server-id=1` → replication-capable / "frontosa"
host), `innodb_file_per_table`, `innodb_buffer_pool_size=2G`, `innodb_flush_log_at_trx_commit=1`,
slow-query log enabled (`long_query_time=0.5`, `log-queries-not-using-indexes=1`).
`my.cnf.d` is empty; `my.cnf.rpmnew` is the unused stock MySQL 5.7 default.

> For restoring dumps: the production DB used **utf8mb4 / utf8mb4_unicode_ci** with
> `character-set-client-handshake = FALSE`. Restores must use `utf8mb4` end-to-end to
> avoid mojibake. Primary application database name: `cichlids_typo3`.

---

## Source files referenced
- `/workspaces/_extract/etc/etc/httpd/conf.d/vhosts.conf`, `httpd/conf.d/php.conf`, `httpd/conf/httpd.conf`, `httpd/conf.d/HINWEIS`
- `/workspaces/_extract/etc/etc/nginx/nginx.conf`, `nginx/conf.d/virtual.conf`, `nginx/conf.d/ssl.conf`
- `/workspaces/_extract/etc/etc/letsencrypt/live/*`, `letsencrypt/renewal/*.conf`
- `/workspaces/_extract/etc/etc/php.ini`, `php.d/*.ini`
- `/workspaces/_extract/etc/etc/my.cnf`
- `/workspaces/_extract/etc/etc/crontab`, `/workspaces/_extract/root_home/root/bin/*.sh`
- `/workspaces/docker.txt`

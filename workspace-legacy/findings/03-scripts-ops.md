# Forensic Analysis 03 — Operational Scripts & Automation

Read-only analysis of operator scripts and automation found under
`/workspaces/_extract/alex_home/home/alex` and `/workspaces/_extract/root_home/root`.
Focus: image processing/migration, backups, DB dumps, file sync (lsyncd), cron, and any
script that could have **converted, overwritten or deleted original images**.

---

## TL;DR — Image-Loss Risk

There is **no script in this collection that resizes images, runs ImageMagick `convert`/`mogrify`,
or deletes image files on disk.** All image-touching scripts here only **copy** files between
TYPO3 storage trees and **soft-delete** (set DB `deleted=1`) — they never `unlink()` originals,
with **one** narrow exception (`make_caseins.php`, which `unlink()`s a source *after* copying it
elsewhere — a rename, not a data loss).

The genuinely dangerous behaviour is more subtle: the **restore scripts can repopulate the
"originals" location from a 450×600 *resized* derivative** when no true original is found. This is
the most plausible mechanism by which "originals" became downsized copies. See
[Critical findings](#critical-findings).

Note: the actual image **upload/resize pipeline** (the API container has `ImageMagick` installed —
`docker-cichlids-api/Dockerfile`) and the runtime resizing proxy live **outside** this backup
slice (API PHP source under `/var/www/html/...www-cichlids/cichlids.extra/api`, and `libcichlids/lib_common.php`,
neither present here). Those should be inspected separately for in-place resizing.

---

## Storage layout referenced by the scripts

The scripts reference several parallel image trees (TYPO3-era and newer):

| Path | Role |
|------|------|
| `/data3/cichlids/typo3.uploads/tx_usercichlids/` | TYPO3 extension upload dir (working copy, flat) |
| `/data3/cichlids/typo3.fileadmin/user_pics/<user>/` | TYPO3 fileadmin per-user tree (treated as "originals") |
| `/data2/cichlids/static/pics/` | **Resized** static renditions (450×600 etc.) |
| `/data3/cichlids/userpics/user_pics/<user>/` | Newer "userpics" target tree |
| `/data1/userpics`, `/data1/userpics/user_pics`, `/data1/userpics/api_uploads` | Current (Docker-era) image roots served by API + nginx image server |
| `/data1/static/pics` | Current static renditions root (lsynced offsite) |

`cichlids_getImageFilename($uid, $filename, 450, 600, false)` (from the missing
`libcichlids/lib_common.php`) computes the path of the **450×600 resized** rendition. Several restore
scripts use this to locate a fallback image.

---

## Script inventory

### Image / migration tools — `~/alex/scripts/cichlids/tools/`

| Script | Lang | Purpose | Touches images? | Destructive? |
|--------|------|---------|-----------------|--------------|
| `restore_images.php` | PHP | Crash-recovery: rebuilds missing `uploads/` files; falls back to `fileadmin/`, then `/data3/restore/`, then the **450×600 resized** static rendition. | Yes (copy) | **Risky** — can copy a resized rendition into the originals tree; `unlink()`s files under `/data3/restore/` |
| `restore/restore_images.php` | PHP | Same crash-recovery, newer variant: missing `uploads/` restored from `fileadmin/`, else from **450×600 resized** `static/pics` (writes into *both* uploads and fileadmin). | Yes (copy) | **Risky** — resized rendition can be written into the "originals" (fileadmin) tree |
| `move_pics.php` | PHP | One-off migration: copies pics from `typo3.uploads` into the newer `/data3/.../userpics/user_pics/<user>/` tree; rewrites DB `image` path; collision-safe (`.copy-N.jpg`). | Yes (copy) | Low — copy only, no deletes |
| `make_caseins.php` | PHP | Resolves case-insensitive filename collisions: renames an original to `<user>____file_9999_<name>`, updates DB, then `unlink()`s the old path. | Yes (copy + unlink) | **Moderate** — `unlink()` of source, but only after it was copied to the new name (rename, no net loss) |
| `find_duplicates.php` | PHP | Reports DB rows sharing the same `image`; hard-`DELETE`s DB rows that are hidden/deleted/empty or whose fileadmin file is missing. File-copy/rename block is commented out. | Reads files | **DB-destructive** (row `DELETE`); no file deletion |
| `find_images.php` | PHP | De-dupes case-variant filenames; for `albums/*` sets `deleted=1`; otherwise copies original to a hashed name and `rename()`s it. | Yes (copy + rename) | Moderate — DB soft-delete + file rename; no `unlink` of unique data |
| `list_pics.php` | PHP | Reports DB pics whose file is missing under `/data3/.../userpics/`. | Reads | No |
| `images_per_day.php` | PHP | Stats: image counts grouped by month. | No | No |
| `find_case_duplicates.sh` | sh | Loops CWD, finds case-insensitive name collisions, invokes `make_caseins.php`. | Indirect | Via `make_caseins.php` |
| `restore/lesen.txt` | txt | German runbook for the crash-recovery procedure (run `restore_images.php`, then a Windows `restore_data3`, then `chown` fixups). Confirms restore-from-resized was an accepted fallback. | — | — |

### User / content admin — `~/alex/scripts/cichlids/`

All connect to MySQL `cichlids_typo3` (creds in `config.php`). These operate on the **database only**;
image "deletes" are soft (`deleted=1` flag), so files on disk are untouched.

| Script | Lang | Purpose | Touches images? | Destructive? |
|--------|------|---------|-----------------|--------------|
| `config.php` | PHP | DB credentials (`cichlids_typo3` / `FmfDtPAADFHeHwbu`). | No | No |
| `delete_picture.php` | PHP | Soft-delete one picture (`deleted=1` by uid). | Refers to pics | DB soft-delete only |
| `delete_pics_from_user.php` | PHP | Soft-delete all pics of a user (sets reason/timestamp). | Refers to pics | DB soft-delete only |
| `undelete_picture.php` / `undelete_picture_by_id.php` | PHP | Clear `deleted`/`hidden` flags (by realurl alias / by uid). | No | No (un-does soft delete) |
| `delete_user.php`, `ban_user.php`, `active_user.php` | PHP | User state management. | No | DB updates |
| `merge_users.php` | PHP | Merge two users across many `user_cichlids_*` tables (header note: now also needs Auth0 step). | No | DB updates |
| `cleanup_users.php` | PHP | Find/clean duplicate-email accounts, reassign pics to a kept user. | No | DB updates |
| `delete_comments_from_user.php`, `delete_duplicate_comments_from_user.php` | PHP | Remove user comments / dupes. | No | DB deletes |
| `change_email.php`, `change_password2.php`, `auth0-pw-change.php` | PHP | Credential changes; `auth0-pw-change.php` calls the Auth0 Management API (client secret embedded). | No | External (Auth0) + DB |
| `export_users.php` | PHP | Export `fe_users` to JSON (latin1→utf8) for Auth0 import. | No | No (read/export) |
| `merge`/`show`/`get_*`/`species_list.php`/`show_mods.php`/`get_strange_users.php`/`get_spam_comments.php` etc. | PHP | Read-only reporting/lookup queries. | No | No |

### Root ops — `~/root/bin/`

| Script | Lang | Purpose | Touches images? | Destructive? |
|--------|------|---------|-----------------|--------------|
| `mysql-backup.sh` | sh | Daily full `mysqldump -A` to `/data1/mysql-backup`; **deletes dumps older than 7 days** (`find -mtime +7 -delete`). Creds: user `backup`. | No | Prunes old dumps only |
| `mysql-prune.sh` | sh | Truncates TYPO3 cache/log tables (`cache_hash`, `sys_log`, realurl caches), prunes old `fe_sessions`. | No | DB truncate (caches) |
| `duplicity-job.sh` | sh | Encrypted offsite backup to Hetzner FTP (`u23634.your-backup.de`); full on day 17, else incremental; `remove-older-than 7D`. GPG key `665E1F97`. | Indirectly (whatever filelist includes) | Removes backups >7d offsite |
| `duplicity-list.sh` | sh | Lists/inspects current duplicity backup set. | No | No |
| `backup-space.sh` | sh | `du -hs` of a backup FTP account via lftp. | No | No |
| `sync-comments.sh` | sh | `docker exec` cichlids-api → `php sync_comments_sqs.php` (pull comments from AWS SQS). | No | App-level |
| `sync-user-logins.sh` | sh | `docker exec` cichlids-api → `php sync_user_logins.php`. | No | App-level |
| `send-api-errors.sh` | sh | Greps today's API error_log (filters PHP notices) — for cron email. | No | No |
| `testload.sh` | sh | Alert if load >6: dumps top/iostat/blocked procs/error_log. | No | No |
| `testload_stop_httpd.sh` | sh | If load >15 stop httpd; else (re)start if down. Overload guard. | No | Stops/starts httpd |
| `authprogs`, `rrsync` | perl | Standard SSH command-restriction wrappers (authprogs; rsync-only shell). Used to lock down the lsyncd/rsync push key. | No | No (security tooling) |

### lsyncd — `~/root/lsyncd/` (live file replication)

| Config | Source | Targets | Notes |
|--------|--------|---------|-------|
| `static_pics` | `/data1/static/pics` | `frontosa.cichlids.com:/data1/static/pics` **and** `worker.rheuma-online.de:/raid5/backup/.../data1/static/pics` | rsync `-ltu -caH --numeric-ids`; live mirror of **resized static renditions** to peer + offsite backup. |
| `userpics` | `/data1/userpics` | `worker.rheuma-online.de:/raid5/backup/frontosa.cichlids.com/data1/userpics` | rsync `-ltu -caH`; live one-way backup of the **userpics originals** tree offsite. |

lsyncd does **not delete** on target (no `--delete` in rsyncOps) — additive mirror, not destructive.
The `-u` (update) flag means newer-mtime wins; if a resized file ever overwrote an original *at the
source*, lsyncd would faithfully propagate it, but lsyncd itself is not the cause.

### Docker / deployment — `~/root/docker-*`

| File | Lang | Purpose | Image-relevant? |
|------|------|---------|-----------------|
| `docker-cichlids-api/Dockerfile` | docker | CentOS 6 + PHP 5.6 + **ImageMagick** API image. | **Yes** — the resize/convert logic almost certainly lives in the API code (not in this backup). |
| `docker-cichlids-api/run.sh` | sh | Mounts host `/data1/userpics/{api_uploads,user_pics}` into the API container at `/var/www/{uploads,user_pics}`. | Yes — defines where uploaded/served images live. |
| `docker-cichlids-api/{build.sh,httpd-config.conf,sysconfig-httpd}` | sh/conf | Build + Apache config. | No |
| `docker-nginx-imgsrv/run.sh` + `README` | sh | Runs `quay.io/wantedly/nginx-image-server` (uses `ngx_small_light`) with `/data1/userpics` mounted read-as-source; serves **on-the-fly resized** images. | Yes, but **read-only** at request time — does not rewrite originals on disk. |
| `docker-web/{Dockerfile,run.sh,build.sh,run-app.sh}` | docker/sh | Node 5.9 SSR Angular frontend (prebuilt `dist/`). | No |
| `docker-web/sync.sh` | sh | rsync built web bundle + `package.json` to `root@www.cichlids.com:docker-web`. | No |

### DB migration — `~/root/aurora/`

| File | Purpose |
|------|---------|
| `script.sh` | `mysqldump --databases cichlids_typo3 --single-transaction --compress --order-by-primary` (commented pipe into an AWS RDS/Aurora endpoint) — the TYPO3→Aurora migration dump command. |
| `cichlids_typo3.sql` (402 MB), `cichlids_phorum5.sql` (408 MB) | Full DB dumps captured for the Aurora migration (2018-09-26). Data, not scripts. |

### Other

- `~/alex/bin/composer` — vendored Composer binary (not a custom script).
- `~/alex/bin/onlyonsmallload` + `~/alex/onlyonsmallload.c` — tiny C wrapper that refuses to `execve`
  its argument if 1-min loadavg >10. Used to gate cron jobs during high load. No image involvement.
- `~/alex/foo/` — throwaway Composer sandbox.
- `~/root/dix_easylogin*` — third-party TYPO3 social-login extension (OpenID/OAuth/Facebook). Auth, no image handling.

---

## Cron / scheduling

No crontab files are in this slice, but the script set strongly implies cron entries (likely gated by
`onlyonsmallload`): `mysql-backup.sh` (daily), `duplicity-job.sh` (daily, full on the 17th),
`mysql-prune.sh`, `testload*.sh` (frequent), `sync-comments.sh` / `sync-user-logins.sh` (frequent
SQS pulls), `send-api-errors.sh` (daily digest). lsyncd runs as a daemon, not cron.

---

## Critical findings — possible original-image loss

1. **Restore-from-resized fallback (highest suspicion).**
   Both `tools/restore_images.php` and `tools/restore/restore_images.php`, when an image is missing
   from `uploads/` *and* `fileadmin/`, fall back to the **450×600 resized** rendition under
   `/data2/cichlids/static/pics` (via `cichlids_getImageFilename(..., 450, 600, false)`) and
   `copy()` it **into the originals locations** (`typo3.uploads` and `typo3.fileadmin/user_pics`).
   After such a "restore", the file occupying the originals slot is a downsized 450×600 JPEG, not the
   true original. `restore/lesen.txt` documents this as the standard post-crash procedure, so it was
   run for real. **This is the most plausible mechanism for originals having silently become resized
   copies.** Affected images would be exactly those that were missing at restore time.

2. **`make_caseins.php` `unlink($orig)`** (line 37) deletes the source file — but only *after*
   copying it to the renamed target in both trees, so it is a rename, not data loss. Safe **iff**
   both prior `copy()` calls succeeded (no error checking — a failed copy followed by the unlink
   would lose the file). Low likelihood but worth noting.

3. **`find_images.php` / `find_duplicates.php`** issue DB `DELETE`/`deleted=1` against pictures and
   `rename()` files; the file-deleting branches are commented out. They can orphan DB rows but do not
   delete unique image files. (`find_duplicates.php` hard-`DELETE`s DB rows whose fileadmin file is
   already missing.)

4. **Out of scope but high priority:** the actual upload→resize pipeline is in the **API PHP source**
   (ImageMagick is installed in `docker-cichlids-api`) and the runtime resizer is
   `wantedly/nginx-image-server` (ngx_small_light). If originals were overwritten *at upload time*
   (e.g. resize-in-place before storing), the evidence is in that API code, which is **not present in
   this backup slice** and must be analysed separately.

## Secrets observed (for the owner's awareness)
DB password in `config.php`; MySQL `backup` user password in `mysql-backup.sh`/`mysql-prune.sh`;
Hetzner FTP creds in `duplicity-job.sh`/`duplicity-list.sh`/`backup-space.sh`; Auth0 client secret in
`auth0-pw-change.php`; GPG key id `665E1F97` for duplicity encryption.

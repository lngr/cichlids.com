# 02 — TYPO3 Application Forensics (cichlids.com)

Read-only analysis of the legacy TYPO3 / PHP photo-sharing application from the
backups under `/workspaces/_extract`. Focus: photo-SHARING domain logic, custom
extensions, auth, DB tables, and image storage/thumbnailing.

## TL;DR

- The site ran on **TYPO3 4.5–4.6** (classic, pre-namespace `t3lib_*` API). No
  TYPO3 source tree survived in the backup — only the **MySQL dump** and the
  **uploaded user images** are present.
- The core photo-sharing feature was a **custom in-house extension keyed
  `user_cichlids`** (all `user_cichlids_*` tables; `tx_usercichlids` upload
  folder). Its PHP source is NOT in the backup; its behaviour is reconstructed
  from the DB schema + maintenance scripts.
- Login originally used the third-party **`dix_easylogin`** extension
  (OpenID/OAuth/Facebook/Twitter + felogin). The site later **migrated to
  Auth0** (`fe_users_auth0` table, `cichlids.eu.auth0.com`, Angular/Node
  frontend in `docker-web`).
- A separate **Phorum 5** forum (`phorum_*` tables) ran alongside.

## 1. TYPO3 version

Determined indirectly (no `typo3_src`, no `LocalConfiguration.php`/`localconf.php`
in the backup):

- `dix_easylogin` `ext_emconf.php` constraint:
  `'typo3' => '4.5.0-4.6.99'`
  (`/workspaces/_extract/root_home/root/dix_easylogin-2017-05-18/ext_emconf.php:37`).
- All extension code uses the **TYPO3 4.x procedural API**: `t3lib_div`,
  `t3lib_extMgm`, `tslib_pibase`, `$GLOBALS['TYPO3_DB']`, `Tx_Fluid_*`,
  `Tx_Extbase_*` — i.e. classic class-name API, not the 6.x+ namespaced
  `\TYPO3\CMS\...` API.
- DB dump contains TYPO3-4.x-era tables: `cache_*` AND `cachingframework_cache_*`,
  `sys_workspace*`, `tx_templavoila_*`, `tx_realurl_*`, `tx_extbase_cache_*`
  (`/workspaces/_extract/root_home/root/aurora/cichlids_typo3.sql`).
- The string "TYPO3 3.7.0" appearing in the dump is binary/data noise (BLOB/RGB
  bytes), not a version marker; ignore it.

**Conclusion: TYPO3 4.5 LTS (range 4.5–4.6).**

## 2. Custom / notable extensions

### `dix_easylogin` (third-party, by Markus Kappe / dix.at)
Two copies in the backup:
- `/workspaces/_extract/root_home/root/dix_easylogin` (v0.2.7, dated 2012)
- `/workspaces/_extract/root_home/root/dix_easylogin-2017-05-18` (later, adds
  `class.ext_update.php`, `class.tx_dixeasylogin_realurl.php`,
  `ext_conf_template.txt`)

Title: *"Easy Login and Register with OpenID (FE)"*. Federated front-end login
via OpenID / OAuth1 / Facebook / Yahoo / Google / myOpenID / WordPress / Twitter,
plus standard felogin (username/password). Depends on extbase + fluid.

Key files / mechanics:
- `pi1/class.tx_dixeasylogin_pi1.php` — plugin entry, dispatches by provider
  type (`FACEBOOK`/`OAUTH1`/`OPENID`), renders Fluid templates `login.tmpl` /
  `xrds.tmpl`.
- `pi1/class.tx_dixeasylogin_div.php` — the auth core:
  - `fetchUserByIdentifier()` looks up `fe_users` by the external identity stored
    in column **`tx_dixeasylogin_openid`** (added to `fe_users` TCA + SQL by
    `ext_tables.php` / `ext_tables.sql`).
  - `createUser()` auto-provisions an `fe_users` row (email/username/name from
    the provider) with a **random 32-hex password** and configured
    `pid`/`usergroup`.
  - `login()` calls `$GLOBALS['TSFE']->fe_user->createUserSession()`.
- `class.ext_update.php` — one-off migration: randomizes `fe_users.password` for
  all federated accounts (`tx_dixeasylogin_openid <> ''`) on upgrade from ≤0.2.5.
- The later version introduced a separate identifier table
  **`tx_dixeasylogin_identifiers`** (InnoDB; columns `conn_type`, `conn_name`,
  `identifier`, `user`) — a normalized replacement for the single
  `fe_users.tx_dixeasylogin_openid` column, allowing multiple linked identities.

### `user_cichlids` — the photo-sharing extension (custom, in-house)
**Source code NOT present** in the backup, but it is unambiguously the core app:
all `user_cichlids_*` tables, the `tx_usercichlids` upload folder, and front-end
plugin var `user_cichlids_pi1[...]` (seen in admin scripts building URLs like
`pictures.html?user_cichlids_pi1[picture]=…`,
`tank-examples.html?user_cichlids_pi1[tank]=…`). See sections 3–6.

### Other extensions evidenced in the DB dump
- `cwt_community` (tables `tx_cwtcommunity_*`: albums, photos, photo_comments,
  buddylist, guestbook, message, profileviews) — a community/profile extension
  (likely legacy/secondary to `user_cichlids`).
- `vjchat` (`tx_vjchat_*`) — front-end chat.
- `realurl` (`tx_realurl_*`) — speaking URLs (easylogin integrates with it).
- `templavoila`, `staticinfotables`, extbase/fluid, scheduler.

## 3. Photo-sharing domain logic

Tables `user_cichlids_*` (schema from
`/workspaces/_extract/root_home/root/aurora/cichlids_typo3.sql`):

- **Pictures** — `user_cichlids_pictures` (AUTO_INCREMENT ≈ 190 777, so ~190k
  picture records). Columns: `fe_user` (uploader), `title`, `description`,
  `species` (blob — serialized species refs), `image` (varchar(1000): the
  relative path, e.g. `user_pics/5229/01_HPIM3766.JPG`), `rating` (float),
  `rating_count`, `views`, `reported`, plus soft-delete/moderation
  (`deleted`, `hidden`, `delete_tstamp`, `delete_reason`, `delete_user`).
- **Galleries** — `user_cichlids_gallery` + M:N join
  `user_cichlids_gallery_pictures_mm` (with `sorting`). User-curated albums.
- **Comments** — `user_cichlids_comments` (AUTO_INCREMENT ≈ 1.6M). `type`
  distinguishes target: `type=1` → picture, else → tank (confirmed in
  `get_comments_from_user.php`/`get_spam_comments.php` URL building). `item` =
  target uid, `note` = body, `rating`, `score`, `poster`, `ip`, `fe_user`, plus
  soft-delete/moderation columns. FULLTEXT index on `note`.
- **Comment rating** — `user_cichlids_comments_rated` (one row per
  user-rates-comment: `comment_uid`, `fe_user`, `rated`, `tstamp`) — prevents
  double-voting.
- **Tanks** — `user_cichlids_tanks` (aquarium setups: dimensions, water params
  ph/kh/gh/no2/no3/po4, plants, filtration, fish list, multiple image blobs).
  Commentable like pictures.
- **"Of the hour"** — `user_cichlids_ofthehour` (`tstamp`, `picture`, `tank`):
  featured-item rotation.

### Species / taxonomy
- `user_cichlids_species` — main species record (genus/species/common/category
  FKs, ph/gh/kh/temp/max_size, breeding, aggro, diet, origin, habitat,
  description, `morphs` blob, `images` blob; UNIQUE on `(genus,species)`).
- Name lookup tables: `user_cichlids_genus_names`, `user_cichlids_species_names`,
  `user_cichlids_common_name`, `user_cichlids_morph_names`.
- `user_cichlids_category` (≈9 categories).
- M:N joins: `user_cichlids_species_common_mm`,
  `user_cichlids_species_pictures_mm` (with `tablenames` discriminator + `sorting`).

So a **picture is categorized by species**; the `pictures.species` blob plus the
`species_pictures_mm` join relate uploads to the species taxonomy.

### Anonymous uploads
`fe_user = 0` means anonymous; admin scripts map `0 → "anonymous"`
(`tools/move_pics.php:17`, `tools/restore_images.php:33`). Confirmed on disk:
`/workspaces/_extract/data1/userpics/user_pics/anonymous/`.

## 4. DB table names referenced in code / present in dump

Cross-reference list for the MySQL dump
(`/workspaces/_extract/root_home/root/aurora/cichlids_typo3.sql`, 103 tables).

**Referenced directly in PHP** (admin scripts + easylogin):
`fe_users`, `tx_realurl_uniqalias`, `user_cichlids_pictures`,
`user_cichlids_species`, `user_cichlids_comments`,
`user_cichlids_comments_rated`, `user_cichlids_gallery`,
`user_cichlids_tanks`.

**Custom photo-sharing tables (`user_cichlids_*`):**
`user_cichlids_category`, `user_cichlids_comments`,
`user_cichlids_comments_rated`, `user_cichlids_common_name`,
`user_cichlids_gallery`, `user_cichlids_gallery_pictures_mm`,
`user_cichlids_genus_names`, `user_cichlids_morph_names`,
`user_cichlids_ofthehour`, `user_cichlids_pictures`,
`user_cichlids_species`, `user_cichlids_species_common_mm`,
`user_cichlids_species_names`, `user_cichlids_species_pictures_mm`,
`user_cichlids_tanks`.

**Auth / login:** `fe_users`, `fe_users_auth0`, `tx_dixeasylogin_identifiers`,
`fe_groups`, `fe_groups_language_overlay`, `fe_sessions`, `fe_session_data`.
(Also note `fe_users.tx_dixeasylogin_openid` column added by easylogin.)

**Community / chat ext:** `tx_cwtcommunity_abuse`, `tx_cwtcommunity_albums`,
`tx_cwtcommunity_buddylist`, `tx_cwtcommunity_buddylist_approval`,
`tx_cwtcommunity_guestbook`, `tx_cwtcommunity_guestbook_data`,
`tx_cwtcommunity_icons`, `tx_cwtcommunity_message`,
`tx_cwtcommunity_photo_comments`, `tx_cwtcommunity_photos`,
`tx_cwtcommunity_profileviews`, `tx_vjchat_entry`, `tx_vjchat_room`,
`tx_vjchat_room_feusers_mm`, `tx_vjchat_session`.

**realurl:** `tx_realurl_chashcache`, `tx_realurl_errorlog`,
`tx_realurl_pathcache`, `tx_realurl_redirects`, `tx_realurl_uniqalias`,
`tx_realurl_urldecodecache`, `tx_realurl_urlencodecache`.

**TYPO3 core / system:** `backend_layout`, `be_groups`, `be_sessions`,
`be_users`, `cache_extensions`, `cache_hash`, `cache_imagesizes`,
`cache_md5params`, `cache_pages`, `cache_pagesection`, `cache_treelist`,
`cache_typo3temp_log`, `cachingframework_cache_hash`,
`cachingframework_cache_hash_tags`, `cachingframework_cache_pages`,
`cachingframework_cache_pages_tags`, `cachingframework_cache_pagesection`,
`cachingframework_cache_pagesection_tags`, `pages`, `pages_language_overlay`,
`queries`, `static_countries`, `static_country_zones`, `static_currencies`,
`static_languages`, `static_territories`, `static_tsconfig_help`,
`sys_be_shortcuts`, `sys_domain`, `sys_filemounts`, `sys_history`,
`sys_language`, `sys_lockedrecords`, `sys_log`, `sys_news`, `sys_note`,
`sys_preview`, `sys_refindex`, `sys_refindex_rel`, `sys_refindex_res`,
`sys_refindex_words`, `sys_registry`, `sys_template`, `sys_ter`,
`sys_workspace`, `sys_workspace_cache`, `sys_workspace_cache_tags`,
`sys_workspace_stage`, `tt_content`, `tx_extbase_cache_object`,
`tx_extbase_cache_object_tags`, `tx_extbase_cache_reflection`,
`tx_extbase_cache_reflection_tags`, `tx_impexp_presets`,
`tx_scheduler_task`, `tx_staticinfotables_hotlist`,
`tx_templavoila_datastructure`, `tx_templavoila_tmplobj`.

**Forum (separate DB dump
`/workspaces/_extract/root_home/root/aurora/cichlids_phorum5.sql`):**
`phorum_banlists`, `phorum_files`, `phorum_forum_group_xref`, `phorum_forums`,
`phorum_groups`, `phorum_messages`, `phorum_messages_edittrack`,
`phorum_pm_buddies`, `phorum_pm_folders`, `phorum_pm_messages`,
`phorum_pm_xref`, `phorum_search`, `phorum_settings`, `phorum_spamhurdles`,
`phorum_subscribers`, `phorum_user_custom_fields`, `phorum_user_group_xref`,
`phorum_user_newflags`, `phorum_user_permissions`, `phorum_users`.

## 5. Login / auth approach and the Auth0 migration

1. **Original (TYPO3-native):** standard `fe_users`/felogin, augmented by
   `dix_easylogin` for federated OpenID/OAuth/Facebook/Twitter login. The
   external identity was stored in `fe_users.tx_dixeasylogin_openid` (later
   normalized into `tx_dixeasylogin_identifiers`). Federated accounts had random
   passwords.
2. **Migration to Auth0:** the site moved to **Auth0** tenant
   `cichlids.eu.auth0.com`.
   - `fe_users` were exported to Auth0 (`export_users.php` emits Auth0 bulk-import
     JSON, `connection: Username-Password-Authentication`, latin1→utf8
     re-encoded; large Auth0 export at `/workspaces/_extract/auth0-cichlids.json`,
     ~4.4 MB).
   - Mapping table **`fe_users_auth0`** (`sub` = Auth0 subject →
     `user_id` = local `fe_users.uid`) links Auth0 identities back to legacy
     users.
   - `auth0-pw-change.php` uses the Auth0 Management API
     (`/api/v2/users…`, client-credentials grant; client_id/secret are
     hard-coded in the script — credentials are exposed in the backup).
   - A new SPA frontend (`docker-web`: Angular/Node SSR bundle, run via
     `node dist/server/bundle.js`) + a PHP 5.6 API container
     (`docker-cichlids-api`, CentOS 6 + php56w + ImageMagick) replaced the TYPO3
     frontend. API host `api.cichlids.fishauth.net` (Let's Encrypt certs present).
   - The whole stack was orchestrated via `docker-compose` (referenced in root's
     `.bash_history`).
   - Note: `merge_users.php` carries a comment that user-merge "no longer works,
     the Auth0 thing must also be done" — confirming the cut-over.

## 6. Image storage, filename conventions, thumbnailing

### Stored path convention
- DB `user_cichlids_pictures.image` holds a **relative** path
  `user_pics/<fe_user>/<filename>` (anonymous → `user_pics/anonymous/<filename>`).
  Confirmed by sample rows in the dump and on-disk layout
  `/workspaces/_extract/data1/userpics/user_pics/<userid>/<file>`.
- Server-side absolute roots changed over the app's life (seen in
  `scripts/cichlids/tools/*`):
  - TYPO3 fileadmin originals: `/data3/cichlids/typo3.fileadmin/user_pics/<user>/<file>`
  - TYPO3 upload folder (extension `tx_usercichlids`):
    `/data3/cichlids/typo3.uploads/tx_usercichlids/<file>`
  - Later consolidated public store: `/data3/cichlids/userpics/user_pics/<user>/<file>`
    (this matches the extracted `data1/userpics/user_pics/...`).
  - Generated/resized cache: `/data2/cichlids/static/pics/...`.

### Filename conventions
- Uploaded files are stored as **`NN_<originalname>.<ext>`** where `NN` is a
  zero-padded 2-digit sequence/slot prefix (observed `01_`, `03_`, `10_`, `12_`;
  e.g. `01_HPIM3766.JPG`, `12_fish_017.jpg`).
- A legacy/alternate naming scheme exists:
  **`<user>____file_<NN>_<name>`** (e.g. `4516____file_9999_12_fish_011.jpg`),
  matched by regex `^[0-9]+____file.*` in `tools/restore_images.php` and
  generated by `tools/make_caseins.php` (`"%d____file_%02d_%s"`, padding 9999).
- The on-disk files are **full-size originals** (multi-MB JPEGs, e.g. 2.4 MB
  `01_HPIM2803.jpg` in user 5229's dir) — these are the originals, not thumbnails.

### Resize / thumbnail logic
- The actual resize code lived in a shared library **`lib_common.php`**
  (`require_once "/data1/www/www-cichlids/libcichlids/lib_common.php"`), which is
  **NOT present** in the backup.
- Its thumbnail-naming function is used as
  `cichlids_getImageFilename($uid, $filename, 450, 600, false)`
  (`tools/restore_images.php:29`) — i.e. resized variants are keyed by
  (picture uid, original filename, max-width 450, max-height 600, crop-flag) and
  cached under `/data2/cichlids/static/pics/`. TYPO3 itself would otherwise use
  GIFBUILDER/ImageMagick into `typo3temp/`.
- The Auth0-era API container installs **ImageMagick**
  (`docker-cichlids-api/Dockerfile:8`), so on-the-fly resizing moved into that PHP
  API after the migration.

## 7. Gaps / caveats

- **No TYPO3 source tree** (no `typo3conf`, `typo3_src`, `localconf.php`/
  `LocalConfiguration.php`, no `user_cichlids` extension PHP). Photo-sharing logic
  is reconstructed from schema + admin scripts only.
- **`libcichlids/lib_common.php`** (image helper used by all `tools/*` scripts) is
  missing — thumbnail dimensions/crop rules are only inferable from call sites.
- The exact patch-level TYPO3 version cannot be pinned beyond the 4.5–4.6 range.
- **Secrets exposed in backup:** DB credentials
  (`scripts/cichlids/config.php`), Auth0 Management API client_id/secret
  (`auth0-pw-change.php`), plus an SSH private key under
  `/workspaces/_extract/root_home/root/.ssh/id_rsa`. Treat the whole backup as
  sensitive.

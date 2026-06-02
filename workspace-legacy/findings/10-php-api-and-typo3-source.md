# PHP REST API, TYPO3 Extension `user_cichlids`, and the `libcichlids` Resize Library

Forensic source review of three components of the legacy cichlids.com codebase:

1. The live PHP REST API (Slim 3) at `…/cichlids.extra/api`
2. The custom TYPO3 extension `user_cichlids`
3. The image resize library `libcichlids`

All paths are absolute under the extracted web tree
`/workspaces/_extract/data1/www/html/www-cichlids`. Secret values (DB passwords,
AWS keys, JWT signing material) are present in the source and are noted by name
only.

---

## Part 1 — The PHP REST API (`cichlids.extra/api`)

### Framework and libraries

`composer.json` declares a Slim 3 micro-framework REST application:

| Package | Purpose |
|---------|---------|
| `slim/slim ^3.0` | HTTP routing / middleware framework |
| `firebase/php-jwt ^3.0` | JWT decode/encode |
| `tuupola/slim-jwt-auth 2.1.0` | Slim JWT authentication middleware |
| `akrabat/rka-ip-address-middleware ^0.4.0` | Client IP resolution behind proxy |
| `ramsey/uuid ^2.9` | UUIDs (largely unused) |
| `hashids/hashids ^1.0` | Encodes numeric picture UIDs into URL slugs |
| `aws/aws-sdk-php ^3.82` | AWS SNS publishing |

`composer.lock` pins these with transitive deps (guzzle, nikic/fast-route,
pimple, psr/*, paragonie/random_compat). The minimum platform requirement
derived from the dependency set is **PHP >= 5.5** (random_compat allows
`^5.3.3 || ^7.0`); no explicit `platform` constraint is set in `composer.lock`.
Autoloading is PSR-4: namespace `Cichlids\` → `src/`.

It is **not** Silex, Leeroy, or Auth0's SDK. Authentication uses Auth0-issued
JWTs verified locally against a hard-coded Auth0 tenant certificate.

### Entry point and routing (`index.php`)

`index.php` bootstraps the Slim app, adds the IP-address middleware
(trusted proxy `172.17.42.1`), and installs the JWT middleware
(`\Slim\Middleware\JwtAuthentication`) configured with:

- `algorithm: RS256`
- `secret`: the Auth0 public certificate from `Config::Auth0JWTPubkey()`
- a callback that stores the decoded token in the container as `jwt`
- a rule making auth **optional** for three GET routes
  (`/pictures/{slug}/comments`, `/tanks/{tankid}/comments`, `/comments`) — the
  token is only enforced if an `Authorization: Bearer` header is present.

CORS is handled per-handler by writing `Access-Control-Allow-Origin: *` headers,
or via `Util::addCors()` which whitelists
`http://localhost:4200`, `http://localhost:4000`, `https://www.cichlids.com`,
`https://dev.cichlids.com`. Every mutating route has a matching `OPTIONS`
preflight handler.

### Endpoints

| Method | Route | Handler | Auth |
|--------|-------|---------|------|
| GET | `/pictures` | `Pictures::listPictures` | none (optional bearer to see own hidden pics) |
| GET | `/pictures/{slug}` | `Pictures::viewPicture` | none (increments `views`) |
| DELETE | `/pictures/{slug}` | `Pictures::deletePicture` | JWT, admin/moderator |
| PUT | `/pictures/{slug}` | `Pictures::editPutPicture` | JWT, owner |
| GET | `/pictures/{slug}/edit` | `Pictures::editGetPicture` | JWT, owner |
| GET | `/pictures/{slug}/comments` | `Pictures::listComments` | optional JWT (admins see deleted) |
| POST | `/pictures/{slug}/comments` | `Pictures::postComment` | JWT |
| PUT | `/pictures/{slug}/type` | `Pictures::changeType` | JWT, admin/moderator |
| POST | `/pictures/{slug}/discard` | `Pictures::discardPicture` | JWT, owner, only if still hidden |
| GET | `/upload/files` | `Upload::listAvailFiles` | JWT |
| POST | `/upload/files/{fileid}/convertToPicture` | `Upload::convertToPicture` | JWT |
| POST | `/upload/files/{fileid}/convertToTankPicture` | `Upload::convertToTankPicture` | JWT, tank owner |
| POST | `/upload/files/{fileid}/convertToProfileImage` | `Upload::convertToProfileImage` | JWT |
| POST | `/upload/files/{fileid}/convertToAvatarImage` | `Upload::convertToAvatarImage` | JWT |
| GET | `/tanks` | `Tanks::listTanks` | none (optional bearer for own hidden) |
| GET | `/tanks/{tankid}` | `Tanks::viewTank` | none |
| PUT | `/tanks/{tankid}` | `Tanks::editPutTank` | JWT, owner |
| DELETE | `/tanks/{tankid}` | `Tanks::deleteTank` | JWT, admin/moderator |
| GET | `/tanks/{tankid}/edit` | `Tanks::editGetTank` | JWT, owner |
| POST | `/tanks/addFromPicture` | `Tanks::addTankFromPicture` | JWT, picture owner |
| DELETE | `/tanks/{tankid}/images/{pictureid}` | `Tanks::deletePictureFromTank` | JWT, owner or admin/mod |
| POST | `/tanks/{tankid}/images/{pictureid}/asMainImage` | `Tanks::setPictureAsMainImage` | JWT |
| GET | `/tanks/{tankid}/comments` | `Tanks::listComments` | none |
| POST | `/tanks/{tankid}/comments` | `Tanks::postComment` | JWT |
| GET | `/comments` | `Comments::listComments` | optional JWT |
| DELETE | `/comments/{id}` | `Comments::deleteComment` | JWT, admin/moderator |
| GET | `/userid/{userid}` | `Users::viewUser` | none |
| GET | `/authuser` | `Users::getAuthUser` | JWT |
| GET | `/auth0/login` | `Users::auth0Login` | HTTP basic (legacy, unused) |
| GET | `/auth0/getByEmail/{email}` | `Users::auth0getByEmail` | none (legacy, unused) |

The fine-uploader binary upload itself is served outside Slim: `.htaccess`
rewrites `upload/.*` to `upload/index.php` (the Fine Uploader PHP handler);
`upload/files` is rewritten to Slim so the converted-files listing flows through
the API. `.htaccess` also forwards `Authorization` into the env
(`HTTP_AUTHORIZATION`) so PHP can read the bearer token.

### Authentication model

Auth is **Auth0 JWT verification done in-process** — there is no Auth0 SDK or
network call to Auth0. Tokens are RS256-signed by the Auth0 tenant
`cichlids.eu.auth0.com`; the matching X.509 certificate is **hard-coded** in
`src/Config.php` (`Config::Auth0JWTPubkey()`). Roles are read from the custom
claim `http://www.cichlids.com/roles`; privileged routes check membership in
`cichlids-admins` / `cichlids-moderators`.

`Users::mapJwtSubjectToUserId()` (`src/Users.php`) maps an Auth0 `sub` to a
TYPO3 `fe_users.uid`:
1. look up `fe_users_auth0.sub`;
2. else match by `fe_users.email`;
3. else match a prior Facebook login via `fe_users.tx_dixeasylogin_openid`
   (`facebook-<id>`);
4. else create a new `fe_users` row (`pid=4`, `usergroup=1`).
The mapping is persisted in `fe_users_auth0`, and every successful map triggers
`Sns::sync_user_login()`. `getAuthUser` additionally refreshes
username/email/avatar from the token, updates `lastlogin`, and sets a long-lived
`userid` cookie on `cichlids.com`.

`src/JWT.php` contains a legacy self-issued HS256 token generator
(`Config::JWTSecret()`, which is not defined in the current `Config.php`); it is
vestigial.

### Data access

`src/Db.php` returns a singleton PDO connection to **MySQL database
`cichlids_typo3`** on a hard-coded remote host (IP `176.9.120.235`), user
`cichlids_api`, with a hard-coded password (redacted: DB password is inline in
source), using `utf8mb4` and exceptions. All queries use PDO prepared
statements.

Tables read/written (the TYPO3 `user_cichlids_*` schema plus core TYPO3 tables):

- `user_cichlids_pictures` — pictures. `pid` encodes the type: 21 = CICHLIDS,
  29 = TANKS, 62 = TANKS_TEC, 63 = TANKS_DECO, 109 = OFFTOPIC, 131 = CONTEST,
  137/138/139 = tank/profile/avatar uploads. Soft-delete via
  `deleted`/`hidden`/`delete_tstamp`/`delete_reason`/`delete_user`.
- `user_cichlids_tanks` — aquariums, with comma-separated UID lists in
  `image`/`tank_images`/`deco_images`/`tec_images` referencing picture UIDs.
- `user_cichlids_comments` — comments and ratings; `type` 1 = picture, 2 = tank;
  `item` = target UID; `dispatched_sqs` flag for SNS sync.
- `user_cichlids_category` — tank categories (Tanganyika, Malawi, etc.; mapped
  in `src/Categories.php`).
- `fe_users` — TYPO3 front-end users (profile, avatar, Auth0 avatar URL).
- `fe_users_auth0` — Auth0 `sub` → `user_id` mapping plus `dispatched_sqs`.
- `tx_realurl_uniqalias` — RealURL slug table; the API reuses it to map picture
  UID ↔ slug, minting new slugs with Hashids salt (`Pictures::makeSlug`).

Picture sort options: `newest` (tstamp), `views`/`popular`, and a Bayesian
`rating` formula. Listings are paginated (`offset`/`count`, capped at 20) and a
correlated subquery returns `total_count`.

### File upload pipeline

`upload/index.php` + `upload/handler.php` implement the Fine Uploader
"traditional" endpoint (chunked and non-chunked). It verifies the bearer JWT,
resolves the user, and stores raw uploads under
`/var/www/uploads/files/<userid>/`. Allowed extensions include images and many
video formats; filenames are sanitized; orientation is normalised with
`mogrify -auto-orient` (ImageMagick) via `exec` (`fixOrientation`).

`src/Upload.php` then "converts" a staged file into a picture
(`convertToPictureImpl`): it moves the file from
`Config::uploadDir()` (`/var/www/uploads/`) to `Config::imageDir()`
(`/var/www/user_pics/`), inserts a `user_cichlids_pictures` row with
`image = user_pics/<userid>/<file>` (initially `hidden=1`), and returns the new
UID/slug. Tank, profile, and avatar conversions reuse the same impl with
different `pid` values and update the owning tank or `fe_users` row.

### Image URLs returned by the API

The API never serves image bytes; it returns a **size → URL map**
(`Pictures::buildImageMap`) for widths 100/200/400/800/1600 via
`Pictures::imgPath()`:

```
//www.cichlids.com/p/n/<width>/<path-without-user_pics-prefix>
```

Staged (not-yet-converted) uploads use the `/p/u/<width>/<userid>/<file>`
variant (`Upload::uploadImgPath`). The `/p/` image server is a separate
component (not part of this Slim app) that performs the actual resize/serve.

### AWS SNS integration (the `sync_*` scripts)

Despite the filename, the sync uses **SNS** (not SQS directly). The repo states
`region us-east-1`, account `894559175927`.

- `sync_comments_sqs.php` — selects new, visible, undispatched
  `user_cichlids_comments` (since 2017-06-30), serialises each via
  `Comments::convertInternal` (embedding parent picture/tank + poster), publishes
  to SNS topic `cichlids_comment_posted`, and sets `dispatched_sqs=1`. Dedupes by
  parent picture.
- `sync_user_logins.php` + `src/Sns.php` — publishes new `fe_users_auth0` rows to
  SNS topic `cichlids_user_logins` and marks them dispatched.
  `Sns::sync_user_login()` is also called inline on every JWT→user mapping.

**Security note:** AWS access key + secret are **hard-coded** in both
`src/Sns.php` and `sync_comments_sqs.php` (redacted here — key id begins `AKIA…`,
secret inline). The DB password and JWT signing material are likewise inline in
source.

### Timeline (git)

The API has its own git repo. Last commit `bd321ce "fix user login etc"`,
committed **2018-03-26**. History runs from `1593e76 "Initial"` through comment
posting/deletion, picture type filtering/deletion, file upload, tank examples,
EXIF orientation correction, and the **Auth0** migration (commits `fde7ab3`,
`433c134`), ending with main-image selection and unpublished-image discarding.
This dates the API's active development to roughly 2016–2018, with Auth0
introduced near the end.

---

## Part 2 — TYPO3 Extension `user_cichlids`

Location: `cichlids/typo3conf/ext/user_cichlids` (reference copy at
`www-cichlids/user_cichlids`). `ext_emconf.php` titles it **"cichlids.com"**,
state `stable`, version `0.0.0` (managed by the TER on upload), depends on the
`cms` extension. This is a classic (pre-Extbase) TYPO3 4.x "pibase" extension;
several source files still carry mtimes from 2007–2013.

### Schema (`ext_tables.sql`)

Defines the full `user_cichlids_*` schema confirming the API's table usage:

- `user_cichlids_pictures` — uploaded pictures (title, fe_user, image blob,
  species blob, description, `rating`, `rating_count`, `views`).
- `user_cichlids_tanks` — aquarium profiles: dimensions (width/height/depth/unit),
  category, image/tank_images/deco_images/tec_images blobs, decoration
  (gravel/plants/more_deco), technical (light/filtration/more_tec), water
  parameters (ph/kh/gh/no2/no3/po4), food, fish/fish_count/fish_images, etc.
- `user_cichlids_category` — categories (with image).
- `user_cichlids_comments` — comments/ratings (type, item, parent, rating,
  poster, ip, note, fe_user).
- Taxonomy: `user_cichlids_species` (genus/species/morphs, water params,
  aggression, diet, origin/habitat, links, images),
  `user_cichlids_genus_names`, `user_cichlids_species_names`,
  `user_cichlids_common_name`, `user_cichlids_morph_names`, plus MM join tables
  `user_cichlids_species_common_mm`, `user_cichlids_species_pictures_mm`.
- Galleries: `user_cichlids_gallery` and `user_cichlids_gallery_pictures_mm`.

`ext_tables.php` registers TCA (backend editing forms) for all these tables,
allows them on standard pages, and registers three front-end plugins
(`_pi1`, `_pi2`, `_pi3`) as `list_type` content elements. `tca.php` holds the
detailed field configuration; `locallang_db.php` the backend labels.
`ext_localconf.php` enables "save & new" for categories/pictures/comments/species
and wires the three plugins into the static template.

### Front-end plugins (pi1 / pi2 / pi3)

All three extend `tslib_pibase`, include `banlist.php`, and require the external
`libcichlids` library by absolute path. Each dispatches on the content element's
`select_key` (set in TypoScript) and on `piVars`. The reference copy of pi1/pi2/
pi3 has been kept in sync (mtimes 2018-03-18), so all three were live alongside
the new API.

- **pi1 — `class.user_cichlids_pi1.php`** (largest, ~70 KB): the main read-side
  display + legacy moderation plugin. `select_key` cases: `tanks`,
  `list_species`, `profiles`, `pictures`, `delete_comment`, `delete_picture`,
  `suspend_user`. Renders category lists, user lists, species lists with a custom
  page browser, and picture listings. Uses the **xajax** AJAX library to expose
  `reportPicture` and `rateComment` actions. Stores the FE user id in a TSFE
  register. Disables caching per request.

- **pi2 — `class.user_cichlids_pi2.php`** (~20 KB): the write/upload side.
  `select_key` cases: `edit_or_post_pic`, `edit_or_post_tank`, `galleries`; the
  default action is a tanks + pictures `listing`. Handles posting/editing
  pictures (`save_picture_from_request`, including type tank/offtopic/contest),
  posting/editing tanks (main/tank/deco/tec image slots), a species selectbox,
  and a small MVC-style gallery manager (`controller`/`flow`/`templating` driving
  the `scripts/gallery_editor/*` views). Ships a static TypoScript template
  ("Cichlids.com User Upload").

- **pi3 — `class.user_cichlids_pi3.php`** (~40 KB): a newer, object-oriented
  rendering layer (extends `cichlids_tslib_pibase`, uses the `classes/` domain
  objects and `scripts/`-based templating). Defines domain wrappers
  (`cichlids_tank` with inch/cm conversion, `cichlids_profile`,
  `cichlids_category`, etc.) and methods to show tanks, pictures, profiles,
  categories, users, genus lists, picture/tank comment listings, comment posting,
  a page browser, species editing, and picture/comment deletion. Note its
  `prefixId` is intentionally `user_cichlids_pi1` for `piVars` compatibility with
  pi1.

### Supporting code

- `classes/` — a small domain/manager layer: entity/basic-object base classes,
  managers for pictures, comments, species, galleries; gallery browser/editor;
  fe-user and picture/comment/species/gallery value objects.
- `scripts/` — PHP view templates included by the plugins:
  `picture_viewer/` (list/show pictures, related-by user/category/species/out,
  pagebrowser, link_to_page), `gallery_editor/` and `gallery_browser/` (full
  gallery CRUD), `user_cichlids_pi3/` (startseite, google, logostuff).
  `picture_viewer/list_pictures.php` renders the picture grid with Browse
  (Most Recent / Most Viewed / Top Rated), Type (Cichlids / Tank / Photo
  Contest), and Category filters, 16 per page.
- `banlist.php` — a hard-coded front-end **ban/spam filter**: terminates the
  request (`die("")`) for specific Facebook openid IDs, e-mail addresses, and
  `X-Forwarded-For` IPs. Included by every plugin.

### Feature map

| Feature | Where |
|---------|-------|
| Picture listing / viewing / rating / reporting | pi1, pi3, `scripts/picture_viewer/*` |
| Picture & tank upload / editing | pi2 |
| Comments + ratings (post/list/delete) | pi1 (xajax), pi3, `classes/class.comments_manager.php` |
| Tanks (full aquarium profiles) | pi2 (edit), pi1/pi3 (display) |
| Species taxonomy (genus/species/morph/common, MM relations) | pi1/pi3, `classes/class.species_manager.php`, schema |
| Galleries | pi2 + `scripts/gallery_*`, `classes/class.gallery_*` |
| User profiles | pi1/pi3 `list_profiles`/`render_user` |
| Moderation / banlist | pi1 (`delete_*`, `suspend_user`), `banlist.php` |

---

## Part 3 — The `libcichlids` Resize Library

Location: `www-cichlids/libcichlids`. This is the shared image/HTML caching layer
required by the TYPO3 plugins. `libcichlids.php` wires up `tools.php`,
`lib_common.php`, `lib_pictures.php`, `lib_comments.php` and defines the global
paths:

| Global | Value |
|--------|-------|
| `cichlids_convert` | `/usr/bin/convert` (ImageMagick) |
| `cichlids_webimgpath` | `/p` (public URL prefix) |
| `cichlids_imgpath` | `/var/www/html/www-cichlids/cichlids.extra/static/pics` (resize cache) |
| `cichlids_origpath` | `/var/www/html/www-cichlids/cichlids/uploads/tx_usercichlids` (TYPO3 originals) |
| `cichlids_staticpath` | `/var/www/html/www-cichlids/cichlids.extra/static/html` (static HTML cache) |

### Cache-path scheme — `cichlids_getImageFilename()` (`lib_common.php`)

A rendition's cache filename is a content hash of
`"<uid>-<filename>-<width>-<height>-<border>"`:

```php
$hash = md5("$uid-$filename-$width-$height-$border");
return "{$width}x{$height}/" . substr($hash,0,2) . "/" . substr($hash,2,2) . "/$hash.jpg";
```

So a rendition lives at
`<imgpath>/<W>x<H>/<aa>/<bb>/<md5>.jpg` and its public URL is
`/p/<W>x<H>/<aa>/<bb>/<md5>.jpg` (`cichlids_getImageUrl`). The two-level
`aa/bb` fan-out keeps directory sizes bounded. (This is the older TYPO3-side
scheme; the new Slim API instead emits `/p/n/<width>/…` URLs, served by the
standalone `/p` image server.)

### Resize generation — `cichlids_createImage()` (`lib_pictures.php`)

`cichlids_generatePicture($uid)` loads the picture row and, unless forced, skips
work when 1-minute load average exceeds 13 (overload guard). If the source
`image` column is empty it soft-deletes the row. Otherwise it generates a fixed
set of renditions from the original:

- **100×75**, black border — mini previews
- **450×600**, no border — single (detail) view
- **130×97**, black border — picture-listing (next generation)
- **167×123**, black border — start page and old listing

Each rendition is produced by shelling out to ImageMagick `convert`:

```
convert -geometry <W>x<H> [ -bordercolor black -border 200 -gravity center -crop <W>x<H>+0+0 +repage ] <orig>[0] -auto-orient <target>
```

`[0]` selects the first frame (multi-page/animated safety); `-auto-orient`
applies EXIF rotation; the bordered variants pad-then-crop to force exact
dimensions. It also pre-renders static HTML fragments (startseite, listing_old,
listing, related, rss) via `cichlids_createHtmlForPicture` into the static-HTML
cache.

### Originals are never overwritten

`cichlids_createImage` reads only from `cichlids_origpath`
(`…/cichlids/uploads/tx_usercichlids`) and writes only to
`cichlids_imgpath` (`…/cichlids.extra/static/pics`); the two roots are distinct.
Before writing, it deletes a stale **target** rendition only
(`if(file_exists($targetfilename)) unlink($targetfilename)`), never the source.
Renditions are regenerated only when missing or older than both the DB `tstamp`
and the original's mtime. **Originals are treated as read-only**; all derived
sizes are cached copies under the separate `static/pics` tree (on the live
server; this generated cache directory is not present in the extract).

### Operational scripts (`libcichlids/scripts`)

Cron/maintenance helpers: `cronjob_gen_newest_pictures.php`,
`cronjob_gen_newest_comments.php`, `cronjob_gen_online_users.php`,
`generate_all_pictures.php`, `gen_pic.php`/`gen_pictures.php`,
`update_rating.php`, and `alte_webseite_latest_comments.php` — these drive
`cichlids_generatePicture` to pre-warm the resize/HTML caches.

---

## Summary of cross-component flow

1. A browser uploads a file (Fine Uploader → `upload/index.php`, JWT-checked,
   `mogrify -auto-orient`) into `/var/www/uploads/files/<userid>/`.
2. The Angular front-end calls `…/convertToPicture` (Slim API), which moves the
   file to `/var/www/user_pics/…` and inserts a `user_cichlids_pictures` row
   (initially hidden) in MySQL `cichlids_typo3`.
3. The API returns rendition URLs (`//www.cichlids.com/p/n/<width>/…`); the
   `/p` image server resizes/serves on demand.
4. The legacy TYPO3 site renders the same data through pi1/pi2/pi3, using
   `libcichlids` to produce fixed-size cached renditions (incl. 450×600) under
   `cichlids.extra/static/pics` and static HTML under `static/html`.
5. New comments and Auth0 logins are fanned out to AWS SNS by the `sync_*`
   scripts and inline `Sns` calls.

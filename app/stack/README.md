# Local dev stack

Docker Compose services for local API development: Postgres, RustFS (S3-compatible object
storage), Keycloak.

## Prerequisites

- .NET SDK installed at `~/.dotnet` (the version pinned in `app/api/global.json`); invoke it as
  `~/.dotnet/dotnet`.
- [pnpm](https://pnpm.io) for the app (`app/mobile`, `app/client-core`).
- Docker with the Compose plugin.
- `ffmpeg` and `ffprobe` on the PATH for video transcoding and the video tests; `tools/install-ffmpeg.sh` installs the pinned build into `~/.local/bin`.
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

`bootstrap.sh` brings a checkout to the full local stand and starts every run from the state the
production import starts from. It runs in this order:

1. `services-up.sh` starts Postgres, RustFS and Keycloak and fails when Postgres is not ready
   within `CICHLIDS_POSTGRES_WAIT_SECONDS`.
2. The legacy MySQL stack is started when port 3306 is closed.
3. `etl reset` drops and rebuilds the database schemas, deletes the realm users without the realm
   role `admin` and closes self-registration.
4. The `cichlids` realm is deleted and imported from `keycloak/cichlids-realm.json`, which holds
   `dev-user` and `dev-mod`.
5. `etl all` imports the legacy data into the empty database and verifies it.
6. `etl media` seeds the media and reads the capture time of the archive photos.
7. `etl keycloak-import --open-registration` imports the legacy accounts and opens
   self-registration once every account exists.
8. `etl verify` checks the finished stand against the legacy source.

```sh
app/stack/bootstrap.sh
```

Every run resets the dev database and the realm, so rows and accounts created by hand are lost.
Media objects in the bucket stay. Stop the API before running the script: a connection holding a
lock makes the reset fail after ten seconds. Run the reset on the dev stack only through
`bootstrap.sh`, because the realm import that follows it restores `dev-user`, which the write E2E
stack signs in with. To only start the services, use `docker compose -f app/stack/compose.yaml up -d`
or `services-up.sh`.

The full run (`etl all`) writes only into an empty, fully migrated database and stops with the
reason otherwise. The reset runs only with `DOTNET_ENVIRONMENT` set to `Development` or `Staging`;
`bootstrap.sh` sets `Development`. Single write steps (`etl profiles`, `etl tanks`, ...) skip the
empty-target check, write over existing rows and run only in `Development`.

`etl verify` computes every expected count from the legacy source and compares it with whole
target tables, so it expects a target that holds the import and nothing else: a row created
through the API or by hand shows up as a mismatch. Every report row names the migration rule its
expectation follows from. The oidc identities of the account import appear as an information row.

Environment variables (all optional):

| Variable                     | Default                                                  | Purpose |
|-------------------------------|-----------------------------------------------------------|---------|
| `CICHLIDS_LEGACY_STACK_DIR`   | `<repository>/workspace-legacy/legacy-stack`               | Where to find the legacy MySQL stack's `docker-compose.yml` if MySQL is not already reachable on 3306. |
| `CICHLIDS_MEDIA_LIMIT`        | `3000`                                                     | How many media items the ETL media step processes; `0` processes the full legacy image set. The ETL uploads only objects that are missing from the bucket or whose metadata differs. |
| `CICHLIDS_AUTH0_EXPORT`       | `/workspaces/legacy-data/auth0/auth0-cichlids.json`        | Auth0 export (newline-delimited JSON) for the Keycloak account import; the step is skipped when the file does not exist. |
| `CICHLIDS_POSTGRES_WAIT_SECONDS` | `60`                                                    | How long to wait for Postgres before the script fails. |
| `DOTNET`                      | `~/.dotnet/dotnet`                                         | Path to the `dotnet` executable. |

To run the Keycloak account import on its own:

```sh
cd app/api && DOTNET_ENVIRONMENT=Development CICHLIDS_ETL_AUTH0_EXPORT=/path/to/auth0-export.json \
  ~/.dotnet/dotnet run --project src/Cichlids.Etl -- keycloak-import
```

The command reads the Keycloak connection from the `Keycloak` section of
`app/api/src/Cichlids.Etl/appsettings.json`, overridable with `CICHLIDS_KEYCLOAK_URL`,
`CICHLIDS_KEYCLOAK_REALM`, `CICHLIDS_KEYCLOAK_ADMIN_USER` and `CICHLIDS_KEYCLOAK_ADMIN_PASSWORD`.
The local `admin`/`admin` credentials apply in the Development environment only. In any other
environment the command aborts unless both admin variables (or `Keycloak:AdminUser` and
`Keycloak:AdminPassword`) hold other values.
Repeated runs create only missing users and missing profile links. A member's login username is
the legacy username when Keycloak accepts it, it holds no email address and no other member keeps
the same name; otherwise it is a generated `user<number>` derived with the slug secret
(`CICHLIDS_SLUG_SECRET` or `Slugs:Secret`, with the ETL's development default). Members with an
email address sign in with it as well. The API reaches a migrated profile only through the oidc
identity the import creates for the member's Keycloak user, so the import has to run before an
environment opens self-registration.

Before it creates users, the import adds the `email_verified` mapper to the realm's `email`
client scope when the scope lacks it, which covers a realm imported from a file without the mapper,
so tokens carry the standard `email_verified` claim. A realm without an Auth0 export gets the
mapper with:

```sh
docker compose -f app/stack/compose.yaml exec -T keycloak sh -c '
  /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user admin --password admin &&
  id=$(/opt/keycloak/bin/kcadm.sh get client-scopes -r cichlids --fields id,name --format csv --noquotes | grep ",email$" | cut -d, -f1) &&
  /opt/keycloak/bin/kcadm.sh create "client-scopes/$id/protocol-mappers/models" -r cichlids \
    -s "name=email verified" -s protocol=openid-connect -s protocolMapper=oidc-usermodel-property-mapper \
    -s "config.\"user.attribute\"=emailVerified" -s "config.\"claim.name\"=email_verified" \
    -s "config.\"jsonType.label\"=boolean" -s "config.\"access.token.claim\"=true" -s "config.\"id.token.claim\"=true"'
```

## Services without legacy data

`services-up.sh` starts Postgres, RustFS and Keycloak and waits until each answers. It needs no
legacy MySQL and no legacy files, which makes it the entry point for the write E2E stack on a
fresh host. `bootstrap.sh` runs it as its first step. The flag `--wait-realm` additionally waits
for the imported `cichlids` realm.

```sh
app/stack/services-up.sh --wait-realm
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

Postgres always publishes on `127.0.0.1`. RustFS and Keycloak publish on the address in
`CICHLIDS_STACK_BIND`, which defaults to `127.0.0.1`. The devcontainer sets it to `0.0.0.0`,
because the devcontainer's own port publishing to the host reaches the stack only through the
container's interfaces and itself binds to the host loopback. Outside the devcontainer, set the
variable only to expose RustFS and Keycloak to other machines deliberately.

Keycloak's `cichlids` realm has two seeded local users (password `dev-password` for both):
`dev-user` (no realm role) and `dev-mod` (`moderator` and `admin` realm roles).

These are local-development defaults only and are not used in any deployed environment.

### Local only, never deploy

The realm file `app/stack/keycloak/cichlids-realm.json` is for this stack alone. Its display name
says so on the login page. It requires no TLS, keeps the password grant on the public client,
seeds two users in the realm, and accepts redirects to any localhost port. Staging and production import
their own files from `apps/keycloak/overlays/`, which a CI check keeps free of these settings.

The realm starts with self-registration closed. `bootstrap.sh` opens it with
`etl keycloak-import --open-registration`, which does so only after the account import has
created every legacy account. Without an Auth0 export the import is skipped and registration
stays closed. The reset closes registration as well. To close it by hand:

```sh
docker compose -f app/stack/compose.yaml exec -T keycloak sh -c '
  /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user admin --password admin &&
  /opt/keycloak/bin/kcadm.sh update realms/cichlids -s registrationAllowed=false'
```

### Test login as a migrated member

Imported legacy accounts have no password (the Auth0 export carries none) and require a password
reset by email on first login, which the local stack cannot send. For testing, the legacy account
`alex` (profile `Alex`, legacy uid 1) gets the local dev password `alex-dev-password`. Setting a
permanent password removes the update-password action; the terms action stays, so the first login
shows the terms page and then lands in the migrated profile. The account import does not set this
password, so after a fresh realm or a new import run, set it again:

```sh
docker compose -f app/stack/compose.yaml exec -T keycloak sh -c '
  /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user admin --password admin &&
  /opt/keycloak/bin/kcadm.sh set-password -r cichlids --username alex --new-password alex-dev-password' # gitleaks:allow
```

The native app logs in through the public client `cichlids-app` (authorization code with PKCE).
Its redirect URIs cover any localhost port and the native app scheme `cichlids://`; its web
origins list the dev Expo web server (`http://localhost:8081`) and the write E2E stack
(`http://localhost:8082`). `bootstrap.sh` imports the realm fresh from its file on every run. A
realm kept outside `bootstrap.sh` (an older volume started with `docker compose up -d`, for
instance) gets these client settings with:

```sh
docker compose -f app/stack/compose.yaml exec -T keycloak sh -c '
  /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user admin --password admin &&
  id=$(/opt/keycloak/bin/kcadm.sh get clients -r cichlids -q clientId=cichlids-app --fields id --format csv --noquotes) &&
  /opt/keycloak/bin/kcadm.sh update "clients/$id" -r cichlids \
    -s "redirectUris=[\"http://localhost:*\",\"cichlids://*\"]" \
    -s "webOrigins=[\"http://localhost:8081\",\"http://localhost:8082\",\"+\"]"'
```

### Web login

The API signs in members of the web app through the confidential client `cichlids-web-bff`
([ADR-0027](../../docs/adr/0027-session-model-for-native-and-web-clients.md)). The realm file
gives it the local development secret `cichlids-web-bff-dev-secret`, which
`app/api/src/Cichlids.Api/appsettings.Development.json` holds as `WebSession:ClientSecret`. Its
redirect URIs are the callbacks of the dev API (`http://localhost:5045/api/auth/callback`) and of
the write E2E stack (port 5046); a sign-out returns through Keycloak's end session endpoint to the
Me tab of the dev web app (`http://localhost:8081/me`, `WebSession:PostLogoutRedirectUrl`) or of
the write E2E stack (port 8082). A realm kept outside `bootstrap.sh` gets the client from the
realm file with:

```sh
python3 -c 'import json; print(json.dumps(next(c for c in json.load(open("app/stack/keycloak/cichlids-realm.json"))["clients"] if c["clientId"] == "cichlids-web-bff")))' \
  | docker compose -f app/stack/compose.yaml exec -T keycloak sh -c '
  /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user admin --password admin &&
  /opt/keycloak/bin/kcadm.sh create clients -r cichlids -f -'
```

The API stores the tokens of web sessions encrypted with ASP.NET Core Data Protection. The dev
API keeps its key ring in `app/stack/data-protection-keys/` (`DataProtection:KeyRingPath`,
relative to the API's content root), which Git ignores and the API creates on the first web login.
Deleting the directory ends every local web session.

## Media objects and their privacy

The ETL gives the bucket a policy that lets anyone read the objects under `variants/` and
`forum_attachments/`. Originals under `originals/` answer 403 without credentials, and the API
returns variant URLs only. Every stored original has its GPS data, camera serial numbers and
owner tags removed; capture date, orientation, colour profile and camera model stay. A legacy
picture libvips cannot decode, mostly a BMP file, is shown from its sanitized public copy under
`variants/copy/`.

To inspect a stored original, download it with the RustFS keys through the AWS CLI container and
read it with `exiftool` (installed in the dev container). `RUSTFS_ACCESS` and `RUSTFS_SECRET` hold
the keys from `compose.yaml`; `KEY` is the original's storage key, for example the newest upload:

```sh
docker compose -f app/stack/compose.yaml exec postgres psql -U cichlids -d cichlids -c \
  "select storage_key from media_item where storage_key like 'originals/uploads/%' order by id desc limit 1"
curl -s -o /dev/null -w '%{http_code}\n' "http://127.0.0.1:9000/cichlids-media/$KEY"   # 403
docker run --rm --network host -e AWS_ACCESS_KEY_ID="$RUSTFS_ACCESS" -e AWS_SECRET_ACCESS_KEY="$RUSTFS_SECRET" \
  -v "$PWD:/w" amazon/aws-cli --endpoint-url http://127.0.0.1:9000 s3 cp "s3://cichlids-media/$KEY" /w/original.jpg
exiftool -a -G1 -s original.jpg | grep -Ei 'gps|serial|owner'   # prints nothing
exiftool -DateTimeOriginal -Orientation -ICC_Profile:ProfileDescription -Model original.jpg
```

`etl media --missing-out report.json` writes a report that lists missing sources, every
undecodable original (`broken`), rejected sources with the reason (`rejected`: files over
`ObjectStorage:MaxDownloadBytes`, images beyond the size limits and TIFF files whose metadata
cannot be removed) and the undecodable originals that have a public copy (`copies`).

## Capture times of photos

`media_item` stores the capture time of a photo as `taken_at_local` (the wall-clock time the
camera recorded, EXIF `DateTimeOriginal`, a `timestamp` without time zone) and
`taken_at_offset_minutes` (EXIF `OffsetTimeOriginal` in minutes from UTC). A null offset means the
camera recorded none and the wall-clock time has no known zone; it is not UTC. The API returns the
value as `takenAt` with the offset, or with `+00:00` when none is recorded. Dates before 1990,
dates more than a day in the future, the zero date and malformed dates are not stored. No other EXIF
field, in particular no GPS data, is read or kept in the database.

Uploads and care photos read the capture time from the uploaded file. `etl media` and the
read-only `etl media-dates` read it from the legacy originals of the archive photos:

```sh
cd app/api && DOTNET_ENVIRONMENT=Development \
  ~/.dotnet/dotnet run --project src/Cichlids.Etl -- media-dates [--limit N] [--workers P]
```

The command examines archive photos that have neither a capture time nor a check timestamp
(`capture_checked_at`). It opens the legacy files for reading only and reads the leading 256 KB of
each file, or the whole file when the header does not parse within them. Every row whose original
was read gets the check timestamp, so a rerun skips it; a row whose original is missing stays a
candidate. The legacy image tree comes from `Etl:LegacyImagesRoot` or
`CICHLIDS_ETL_LEGACY_IMAGES_ROOT`. The report lists candidates, examined, found, no date, implausible, unreadable,
source missing and the bytes read. Set `CICHLIDS_ETL_TARGET_CONNECTION` for a database other than
the one in `appsettings.json`.

## Start the API and the app

`dotnet run` here defaults to the Development environment, which accepts the ETL's development
slug secret. Any other environment (`ASPNETCORE_ENVIRONMENT` for the API, `DOTNET_ENVIRONMENT` for
the ETL) refuses to start without `CICHLIDS_SLUG_SECRET` set to a secret of its own.

Outside Development the API also refuses to start without `DataProtection:KeyRingPath` naming an
existing directory. There it only reads the key ring, which comes from a SOPS-managed Secret
mounted read-only, and creates no keys. `tools/create-data-protection-key.cs` adds a key to a key
ring directory, valid for five years unless a number of years follows the directory:

```sh
dotnet run tools/create-data-protection-key.cs -- <directory> [<years valid>]
```

The files of the directory form the content of the Secret. A new key encrypts from the moment it
is created and the older keys keep decrypting, so a rotation adds a key and replaces the Secret with
the extended directory before the newest key expires. Web sign-in
(`WebSession:CallbackUrl` set) also needs `WebSession:ClientSecret`, the secret of
`cichlids-web-bff` from the secret store (see `apps/keycloak/README.md`), and
`WebSession:PostLogoutRedirectUrl`, an HTTPS address on one of `WebSession:AllowedOrigins`.

```sh
cd app/api/src/Cichlids.Api && ~/.dotnet/dotnet run
```

The API listens on `http://localhost:5045` (see `Properties/launchSettings.json`).

```sh
cd app/mobile && pnpm install && pnpm exec expo start --web --port 8081
```

The app imports `@cichlids/client-core` from its compiled `dist/`: an install that adds packages
builds it through the package's `prepare` script, an install that finds nothing to do skips that
script, so after a change to the client-core sources `pnpm --filter @cichlids/client-core build`
rebuilds it.

## Web Push

The API sends care reminders to browsers through Web Push. It identifies itself to the push
services of the browsers with a VAPID key pair (RFC 8292). `WebPush:Enabled` is true in
Development, and Web Push stays unavailable until the key file
`app/stack/web-push-vapid-keys.json` exists (Git ignores it). Create it once; the tool never
overwrites an existing file and writes the private key readable by its owner only:

```sh
dotnet run tools/create-vapid-keys.cs -- json app/stack/web-push-vapid-keys.json
```

Restart the API afterwards. The web app then offers the reminder setting of the browser. A new key
pair makes the subscriptions of the old one useless: members switch the setting off and on again.
Outside Development the API reads the keys from `WebPush:VapidPublicKey` and
`WebPush:VapidPrivateKey`, needs `WebPush:Subject` (a `mailto:` or `https:` contact of the operator
without personal data) and refuses to start with `WebPush:Enabled` true and no keys. The allowlist
`WebPush:AllowedPushHosts` names the push services a subscription may point to; each entry matches
the host and its subdomains, and the default covers the services of Chrome, Edge, Firefox and
Safari.

## Device tokens

Installations of the app that registered no account authenticate with a device token. The API
issues these tokens itself (ES256, fifteen minutes, no refresh token) under the issuer
`cichlids-device-tokens` and the audience `cichlids-api-device`, after the installation has signed
a one-time challenge with the private key it registered. The signing keys are a JSON key set: the
current key with its private part and the public parts of the keys it replaced. The API loads the
set from `DeviceTokens:KeySet` (the JSON itself, for an environment variable) or from the file named
by `DeviceTokens:KeySetFile`, a relative path resolved against the content root.

Development needs no setup: without a configured key set the API generates a key in memory, and
with a `DeviceTokens:KeySetFile` that does not exist yet it writes the file, readable by its owner
only. Outside Development the API refuses to start without a key set. `tools/create-device-token-key.cs`
creates it:

```sh
dotnet run tools/create-device-token-key.cs -- new <file>      # a key set with one new key
dotnet run tools/create-device-token-key.cs -- rotate <file>   # a new current key, the former one stays as a previous key
```

The file is the content of the secret that the deployed API mounts read-only, like the Data
Protection key ring. A rotation replaces the secret with the rotated file. Tokens that the former
key signed validate until they expire, at most fifteen minutes after the API instances read the
rotated file. Every instance of the API has to read the same set. The repository and the images
never hold a key set; local and test keys are generated at run time.

The limits `DeviceAuth:MaxRegistrationsPerAddressPerHour` (default 30) and
`DeviceAuth:MaxTokenRequestsPerAddressPerMinute` (default 120) count per network address in the
memory of each API instance; IPv6 addresses count per /64 network. A challenge lives
`DeviceAuth:ChallengeLifetime` (default two minutes), and the timestamp of a signed challenge may
differ from the time of the API by `DeviceAuth:TimestampWindow` (default two minutes). Challenges are
stored in the database, so a challenge is accepted once across all instances.

## Provisional accounts

A caller with a device token acts for a provisional profile. It reads what a visitor reads and
keeps its own tanks, care, photos, videos, reminders, streaks, vacations and media library, all of
it private. Every route that reaches other people answers 403 with the code `registration_required`:
follows and follow requests, selected members, comments and ratings, stories (including the upload
for a story and the quick share), avatar and header, onboarding, interests, Web Push and a tank
level other than private. Routes of this kind are declared with the authorization policy
`registered`, and a test lists every authenticated route so that a new one has to be classified.

Provisional profiles have lower limits, configured next to the limits of members:

| Setting | Default | Meaning |
| --- | --- | --- |
| `UploadLimits:ProvisionalMaxUploadsPerHour` | 10 | Photos and videos one provisional profile may upload within an hour (members: `UploadLimits:MaxUploadsPerHour`, 30) |
| `VideoLimits:ProvisionalMaxDuration` | `00:00:30` | Longest video of a provisional profile; the size limit follows from it and `VideoLimits:MaxBitrate` |
| `VideoLimits:ProvisionalMaxOpenUploads` | 1 | Unfinished video uploads at the same time (members: `VideoLimits:MaxOpenUploads`, 3) |
| `VideoLimits:ProvisionalMaxVideos` | 10 | Videos in the media library of a provisional profile |

### Registration of a provisional account

The three routes under `/api/me/provisional` (`preview`, `upgrade`, `move`) hand the content of an
installation to a Keycloak account. Each call carries the Keycloak token as bearer and a signed
challenge of the device (the body of the token exchange) and refuses either alone: a missing or
invalid proof answers 403 with `device_auth_refused`, a device token as bearer answers
`registration_required`, and no bearer answers 401.

- `upgrade` applies to an account without a profile. The provisional profile becomes the profile of
  the account with its first public handle (the preferred username of the token, else a generated
  handle) and keeps all content. Its device credentials are retired. An account that has a profile
  answers 409 `account_has_profile`.
- `move` applies to an account with a member profile. In one transaction the tanks with their care,
  plans, streak data and decommissionings, the media items, to-dos, reminder snoozes, water change
  sessions, video uploads and used checksums move into that profile, the retired credentials move
  with them, and the provisional profile is deleted. A vacation moves like one the member enters now:
  it is dropped when it overlaps a vacation of the member, or when it starts more than seven days
  before the local date of the call and would repair a period of a live tank the member already has
  (`vacationsDropped` and `vacationsDroppedRepair` count the two kinds). The moved tanks are not part
  of that judgement. `preview` and `move` take the query parameters `date` and `utcOffsetMinutes` of
  the vacation routes. The settings and the handle of the member stay. An account without
  a profile answers 409 `account_has_no_profile`, a suspended account 403 `account_suspended`.
- `preview` answers the mode and the amounts a registration would bring over without changing
  anything.

A proof that names a retired credential, a credential of another kind of profile or a profile the
cleanup has removed is refused like any other bad proof. The provisional profile row is locked in the
order the cleanup uses, so a registration and a cleanup of the same profile exclude each other.
Registrations emit the outbox events `profile.registered` and `profile.provisional_content_moved`,
which hold ids and counts only.

### The app of a provisional account

The native app creates the device key at its first start without a Keycloak session. The private
key is an ECDSA P-256 key in the secure store of the operating system (Keychain on iOS,
Keystore-encrypted storage on Android, readable by the app on that device only). Signing uses the
audited `@noble/curves` library, since the Expo runtime has no ECDSA primitive. The key is never
logged or exported. A key that lives inside the secure hardware (Secure Enclave, StrongBox) needs
a native module and is a provider of its own behind the same `DeviceKeyProvider` interface.

The app registers the public key, keeps the credential and signs a challenge whenever the device
token expires. A start without a network does not block the app: it works as a visitor and retries
the registration after 5 seconds, 15 seconds, one minute and then every five minutes, and when it
returns to the foreground. The API client sends the device token while no Keycloak session exists.

Before the first tank the app shows a note that the account lives on this device until the
registration, once. Actions that reach other people open a registration sheet with the reason;
every `registration_required` answer to a write opens the same sheet. After the Keycloak sign-in
the app holds the member back (status loading) until the handover of the content is done, so no
request creates a profile for the account first. It then asks `preview`: an account without a
profile is upgraded; for an account with a profile the member decides whether to take the tanks,
entries and photos along. On yes the content moves and the vacations that were dropped are shown.
On no the device account stays on the device, hidden while the member is signed in. The Me tab
offers to take it along later, and signing out brings the device account back. An installation
without content moves without a question. After a handover the credential and the key are
deleted.

The web app has no provisional accounts. The web build of the write E2E stack contains a WebCrypto
test key provider (non-extractable keys in IndexedDB) because `write-stack.sh` sets
`EXPO_PUBLIC_DEVICE_TEST_KEYS=1` for its export. A browser enables the provider with the entry
`cichlids.deviceAccount.testKeys` set to `1` in its localStorage, so only specs that opt in see
device accounts. No other build sets the variable, and `resolveAppConfig` rejects it for every
variant but development.

## Webhooks and the outbox

The API delivers outbox events as signed webhooks only to hosts on the allowlist
`Webhooks:AllowedTargetHosts`. It lives in Git: `appsettings.json` holds the empty production
list, `appsettings.Development.json` allows `localhost` and `127.0.0.1`, and each deployment adds
its own hosts in its configuration. An entry is a bare host name or IP address. The API logs the
effective list at startup, and a registration for an `ftp://` URL or an unlisted host returns 400.

A failing delivery is retried with a delay that doubles from 5 seconds up to one hour, counted
from the previous attempt. An event is given up seven days after it occurred
(`OutboxDispatcher:GiveUpAfter`). An admin lists given-up events and triggers their redelivery,
which starts a new seven-day period; `dev-mod` has the admin role:

```sh
TOKEN=$(curl -s -d client_id=cichlids-app -d grant_type=password -d username=dev-mod \
  -d password=dev-password http://localhost:8180/realms/cichlids/protocol/openid-connect/token | jq -r .access_token)  # gitleaks:allow
curl -s -H "Authorization: Bearer $TOKEN" 'http://localhost:5045/api/admin/outbox/events?state=given-up'
curl -s -X POST -H "Authorization: Bearer $TOKEN" "http://localhost:5045/api/admin/outbox/events/$EVENT_ID/redeliver"
curl -s -X POST -H "Authorization: Bearer $TOKEN" http://localhost:5045/api/admin/outbox/given-up/redeliver
```

The open events with their schedule:

```sh
docker compose -f app/stack/compose.yaml exec postgres psql -U cichlids -d cichlids -c \
  "select id, event_type, attempt_count, last_attempt_at, next_attempt_at, given_up_at, acknowledged_subscription_ids, last_error
   from outbox_event where dispatched_at is null order by next_attempt_at"
```

While a dispatcher instance delivers an event, its `next_attempt_at` is the end of that
instance's five-minute reservation.

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
`app-screens/` at the repo root, which `.gitignore` excludes (override with `PLAYWRIGHT_OUT_DIR`).

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
  polling every second and giving events up after fifteen seconds, its own Data Protection key
  ring, its own VAPID key pair and its own device token key set in the state directory (removed
  on `down`), the sender loop of
  Web Push reminders switched off and the Me tab of the write web build as the return address
  after a web sign-out;
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

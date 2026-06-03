# cichlids.com — Projekt-Kontext

> Strukturierte Wissensbasis (Ist-Zustand). Diese Datei zuerst lesen.
> Voll-Report: [`workspace-legacy/cichlids-legacy-bestandsaufnahme.html`](workspace-legacy/cichlids-legacy-bestandsaufnahme.html).

---

## 0. Einstieg für neue Sessions — Dokumentations-Landkarte

Diese Datei ist der zentrale Einstieg. Empfohlene Lese-Reihenfolge:

1. `AGENTS.md` — Kurzfassung, Konventionen + Agent-Workflow (`CLAUDE.md` lädt automatisch und verweist nur dorthin).
2. Dieser Abschnitt + der Rest von `PROJECT-CONTEXT.md` — vollständiger Kontext.
3. `workspace-legacy/cichlids-legacy-bestandsaufnahme.html` — visueller Gesamt-Report (im Browser).
4. Bei Bedarf gezielt in `workspace-legacy/findings/` (Detail) und `workspace-legacy/planning-notes/` (Feature-Ideen).

Welche Quelle beantwortet welche Frage:

| Frage | Quelle |
|---|---|
| **Architektur-Entscheidungen** (Stack, Hosting/Auslieferung, Event-Driven, GitOps, KI-Agenten, Video, Backup/DR, Destruktiv-Schutz, Test-First, User Stories/Specs) | **`docs/adr/`** (ADR-0001–0015, Einstieg `docs/adr/index.md`) |
| **Produktvision / PMF** (Engagement-Logik, „ein Becken – zwei Blickwinkel", Epics — was/warum für Nutzer) | **`PRODUCT-VISION.md`** |
| **Transformations-Roadmap** (Legacy→neu: Domänenmodell, ETL, Auth0→Keycloak, Phasen) | **`PLAN.md`** |
| **User Stories / Specs / Tasks** (Backlog.md, Definition of Done) | **`backlog/`** + `docs/adr/0015`; Agent-Workflow in `AGENTS.md` |
| Schnellüberblick + Arbeitskonventionen + Agent-Workflow | `AGENTS.md` (`CLAUDE.md` verweist nur dorthin) |
| Gesamter Projektkontext (Architektur, DB, Bilder, Auth0, Orte, Gotchas) | `PROJECT-CONTEXT.md` (diese Datei) |
| Visueller Report mit Tabs/Diagrammen | `workspace-legacy/cichlids-legacy-bestandsaufnahme.html` |
| Architektur / Komponenten | `findings/01-architecture.md`, `07-dotnet-backend.md`, `08-cichlids-com-app.md` |
| PHP-API + TYPO3-Extension (App-Logik, Schema, Resize) | `findings/10-php-api-and-typo3-source.md` |
| DB-Schema & Tabellen | `findings/02-typo3-app.md`, `10-…` + Report-Tab „Datenbank" |
| Bild-Verifikation (Originale/Resized) | `findings/06-image-verification.txt` + `findings/img_*.csv` |
| Nutzer & Auth0-Migration | `findings/05-auth0-users.md` |
| Ops-Scripts / Server-Config / Domains | `findings/03-scripts-ops.md`, `04-server-config.md` |
| **Geplante Features / Produktvision** | `findings/09-vision-and-serverless.md` + **`planning-notes/`** (Original-Notizen) + Report-Tab „Vision" |
| Migrations-Empfehlung (Legacy→Neu) | Report-Tab „Migration" + §12 dieser Datei |
| Lauffähige Daten + kuratierte Quellen | `workspace-legacy/` (siehe dortige `README.md`) |

Die großen **Daten** (Bilder, DB-Dump, Auth0-Export) liegen außerhalb des Repos unter
`/workspaces/legacy-data/` (siehe §8) und sind nicht versioniert.

---

## 1. Projekt

**cichlids.com** ist eine seit den 2000er-Jahren bestehende, große englischsprachige
**Foto-Sharing- & Bewertungs-Community für Buntbarsch-Enthusiasten** ("Instagram für Cichliden"),
seit **2023 offline**. Datenquelle: Host-Backup **frontosa (2023-09-20)**.

**Ziel:** moderne **Mobile-First-App** auf Basis dieser Bestandsdaten; beim Deployment eine einmalige
ETL-Migration. **Stand:** Datenbasis und vollständiger Anwendungs-Quellcode liegen vor; Neuentwicklung
noch nicht begonnen.

---

## 2. Architektur (finaler Live-Stand)

Öffentlicher TLS-Endpunkt **nginx** (CentOS 6.10) routet pfadbasiert auf lokale Backend-Container:

| Container | Tech | Port | Pfad | Rolle |
|---|---|---|---|---|
| cich-node-prod | **Angular 7 + Universal SSR** (Node, PM2) | 8084 | `/` | Frontend |
| cichlids-api | **PHP 5.6, Slim 3** + ImageMagick | 8081 | `/api` | REST-API (Bilder/Tanks/Kommentare/Upload/User) |
| nginx-image-server | ngx_small_light | 8082 | `/p/…` | On-the-fly Resize aus Originalen |
| Apache/TYPO3 | TYPO3 4.x (Ext `user_cichlids`) | 8080 | `/fileadmin,/uploads` | Legacy-Webroot |
| cichlidsbackend_api_1 | .NET Core / Orleans | 8086 | `/api2` | Prototyp ohne Fachlogik (Event-Store leer) |
| phpmyadmin | phpMyAdmin | 8085 | — | DB-Admin |

Ergänzend **AWS Lambda (C#)**: Member-Settings-API (API-Gateway, Auth0 + „PayingUsers"-Policy),
Login-Tracking (SQS→DynamoDB), Kommentar-Mails (SNS/SQS→Mailgun). Die PHP-API publiziert Events auf SNS
(`sync_*.php`). Auth: **Auth0** (`cichlids.eu.auth0.com`), JWT-Verifikation in der PHP-API.

Domains: `www.cichlids.com` (kanonisch), `cichlids.com` (→www), `dev.cichlids.com`,
`api.cichlids.fishauth.net`, `cichlids.fishauth.net`.

Der Bildserver speichert **keine** Thumbnails — Größen werden pro Request aus dem Original erzeugt
(Cache unter `/p`). Auf Platte liegen die **Originale**.

---

## 3. Quellcode-Bestand (vollständig vorhanden)

Im Web-Baum `/workspaces/_extract/data1/www/html/www-cichlids/` und in `/workspaces/legacy-source/`.
Kuratierte Kern-Kopien unter `workspace-legacy/app-source/`.

| Komponente | Tech | Ort |
|---|---|---|
| Frontend | Angular 7 SSR (TS, Material/**Fuse**-Template), Auth0, FineUploader, Sentry | `legacy-source/cichlids-com` |
| **PHP-REST-API** | **Slim 3**, firebase/php-jwt + tuupola/slim-jwt-auth (Auth0 RS256), hashids, ramsey/uuid, aws-sdk-php | `…/cichlids.extra/api` · `app-source/php-api` |
| **TYPO3-Ext `user_cichlids`** | TYPO3 4.x pibase (pi1/pi2/pi3), `ext_tables.sql`, `tca.php`, `banlist.php` | `…/cichlids/typo3conf/ext` · `app-source/typo3-ext-user_cichlids` |
| **Resize-Lib `libcichlids`** | PHP + ImageMagick (`cichlids_getImageFilename`) | `…/libcichlids` · `app-source/libcichlids` |
| AWS-Serverless | AWS Lambda (C#, .NET Core 2.1), DynamoDB, SNS/SQS, Mailgun | `legacy-source/cichlids-serverless` |
| Forum | Phorum 5.x | `…/cichlids.extra/phorum-5.2.17` |
| TYPO3-Cores | typo3_src 4.1/4.3/4.4/4.5 | `…/www-cichlids/typo3_src-4.*` |
| .NET-Prototyp | .NET Core 2.0, MS Orleans 2.1, CQRS/ES | `legacy-source/cichlids.backend` |

**PHP-API-Endpunkte:** pictures (list/view/edit/delete/discard/type + comments), upload (`/upload/files`
Fine Uploader → `convertToPicture|TankPicture|ProfileImage|AvatarImage`), tanks (list/view/edit/delete,
addFromPicture, comments), comments (list/delete), users (`/userid/{id}`, `/authuser`). Daten in
`cichlids_typo3` (`user_cichlids_*`, `fe_users`, `fe_users_auth0`, `tx_realurl_uniqalias` für Slugs).

**`user_cichlids`-Plugins:** pi1 = Anzeige + Legacy-Moderation (xajax); pi2 = Upload/Edit + Galerien;
pi3 = OO-Rendering (Tanks/Pictures/Profile/Species/Comments).

**3 Generationen:** (1) TYPO3 4.x + Phorum → Daten in `cichlids_typo3`/`_phorum5`. (2) finaler Live-Stand:
Angular-7-SSR ⇄ PHP-Slim-API + nginx-image-server + AWS-Lambda + Auth0. (3) .NET/Orleans-Prototyp
(DB `cichlids`, leer, unfertig — nur HelloWorld-Beispiel, keine Fachlogik).

---

## 4. Datenbanken (MySQL 5.7, charset-verifiziert)

| DB | Tab. | Charset | Bedeutung |
|---|---|---|---|
| **cichlids_typo3** | 101 | utf8mb4_unicode_ci | **Kerndaten** |
| cichlids_phorum5 | 20 | latin1 | Forum: 65.545 Beiträge / 3.963 User |
| cichlids | 9 | utf8 | Event-Store des .NET-Prototyps — **leer** |

Kern (`cichlids_typo3`): `user_cichlids_pictures` 189.571 (180.455 aktiv), `user_cichlids_comments`
1.647.849, `user_cichlids_comments_rated` 13.378, `user_cichlids_tanks` 6.729, `user_cichlids_species`
829 (+_names 824/_genus 182), `user_cichlids_species_pictures_mm` 185.778, `user_cichlids_gallery`
263, `fe_users` 11.948, `fe_users_auth0` 816.

> `tx_cwtcommunity_*` (TYPO3-Community-Ext: Alben/Buddylist/Messages/Gästebuch) ist **leer** — maßgeblich
> ist `user_cichlids_*`.

---

## 5. Bilder

- Speicherort: `/workspaces/legacy-data/images/userpics/` (~235 GB; `user_pics/` Originale,
  `api_uploads/`, `resize.php`). Mapping: `user_cichlids_pictures.image` = Pfad relativ zu `userpics/`.
- **Originale praktisch vollständig erhalten.** Die Resize-Pipeline (`libcichlids`) liest aus dem Original
  und schreibt nur in einen separaten Cache — **Originale werden im Normalbetrieb nie überschrieben.**
- Verifikation (188.664 DB-Verweise): 185.382 Datei vorhanden; 176.783 Originale (>600px); 8.394 ≤600px
  (überwiegend klein-von-Herkunft, ~281–618 mit 450×600-Restsignatur); 5 aktive Bilder fehlen;
  3.880 verwaiste Dateien (v.a. `api_uploads/chunks`). Script: `workspace-legacy/tools/verify_images.py`,
  Listen: `findings/img_*.csv`.

---

## 6. Nutzer & Auth0

- Auth0-Export `/workspaces/legacy-data/auth0/auth0-cichlids.json`: NDJSON, **12.147 Konten ohne Passwort-Hashes**.
  95,3 % Username/Password, 2,6 % Google, 2,1 % Facebook; 99,2 % E-Mail verifiziert.
- **Kein Legacy-ID-Feld** → Zuordnung `Auth0 → fe_user → user_pics/<id>` via **E-Mail-Join** bzw.
  `fe_users_auth0` (sub→uid).
- Login-Migration: Auth0-bcrypt-Hash-Export (nahtlos) oder Reset bei Erst-Login; Social re-link; Merge bei ~75 Mehrfach-E-Mails.

---

## 7. Produktvision / Roadmap

> Die **aktuelle, verfeinerte** Produktrichtung (Zielbild, Engagement-Logik, Epics) steht in
> [`PRODUCT-VISION.md`](PRODUCT-VISION.md). Die folgenden Notizen sind die **historischen
> Eingangs-Ideen** der Legacy-Plattform.

Original-Notizen im Repo unter `workspace-legacy/planning-notes/` (features, marketplace, Blog-Themen,
root-/serverless-/frontend-TODO, frontend-notes); Synthese in `findings/09-vision-and-serverless.md`.
Vision = **Flickr/Pinterest-Sharing + StackOverflow-Reputation/Q&A + Marketplace**.
Themen: Content/Sharing (Sortierung Newest/Most-viewed/Top-rated, „Interesting", Pin→Galerie) ·
Social (Follow, Photostream, Collections, PN, Feed) · Reputation/Gamification (Reputation, Badges,
Leaderboards, Voting) · Q&A (Bounty, Tags) · Discovery/Explore (Highlights, Tag-Taxonomie) ·
Marketplace („Trusted Profiles") · Notifications (E-Mail live, In-App offen) ·
Monetarisierung („PayingUsers", Affiliate-Blogs) · Ranking/ML.
Zusatz-Asset `Checklist_of_the_Cichlid_Fishes_of_Lake….pdf` = Arten-Checkliste (Taxonomie-Quelle).
Detail: `findings/09-vision-and-serverless.md`.

---

## 8. Speicherorte

| Was | Ort | Größe |
|---|---|---|
| Bild-Originale | `/workspaces/legacy-data/images/userpics/` | ~235 GB |
| MySQL-Daten | Docker-Volume `cichlids-legacy_db_data` | ~1,3 GB |
| Web-Baum (TYPO3/PHP-API/Forum/Cores) | `/workspaces/_extract/data1/www/html/www-cichlids/` | ~61 GB |
| Quellcode-Repos | `/workspaces/legacy-source/` | ~1,3 GB |
| Kuratierte Kern-Quellen | `workspace-legacy/app-source/` | ~6 MB |
| Roh-DB-Dump | `/workspaces/legacy-data/db-dump/*.sql.gz` (außerhalb Repo) | 400 MB |
| Auth0-Export | `/workspaces/legacy-data/auth0/` (außerhalb Repo) | 4,3 MB |
| Detail-Analysen | `workspace-legacy/findings/` (01–10 + CSVs) | ~1,3 MB |

---

## 9. Befehle

```bash
# MySQL-Stack (MySQL 5.7 :3306 root/legacy + phpMyAdmin :8085)
cd /workspaces/cichlids.com/workspace-legacy/legacy-stack && docker compose up -d

# DB-Query (INLINE aufrufen — nicht über Shell-Variable)
docker exec cichlids-legacy-db-1 mysql -uroot -plegacy --default-character-set=utf8mb4 \
  cichlids_typo3 -e "SELECT COUNT(*) FROM user_cichlids_pictures;"

# Backlog.md (User Stories / Specs; Web-UI als Devcontainer-Service auf :6480, vom Host erreichbar)
backlog task list                 # Tasks auflisten (TUI: backlog board)
backlog task create "Title" --ac "Given … When … Then …" --priority high
# Coding-Agent nutzt stattdessen den MCP-Server `backlog` (.mcp.json) — siehe AGENTS.md
```

---

## 10. Gotchas

- **MySQL inline** aufrufen (eine Shell-Variable `D="docker exec … mysql"` lieferte in dieser zsh leere Ergebnisse).
- **MyISAM:** `information_schema.TABLE_ROWS` unzuverlässig → echte `COUNT(*)`.
- **Charsets:** `cichlids_typo3` utf8mb4, `cichlids_phorum5` latin1; Dump bringt eigene `SET NAMES`-Logik mit.
- **`data1` enthält mehr als `userpics`** — auch den kompletten Web-Baum unter `data1/www/html/www-cichlids/`.
- Große bz2: `lbzip2 -dc … | tar xf -` (parallel).
- Dateinamen teils Unicode/`%20` → beim Re-Key normalisieren.

---

## 11. ⚠️ Sicherheit

Quellcode/Backups enthalten **Klartext-Secrets** + PII (als kompromittiert behandeln, rotieren):
DB-Passwort (`php-api/src/Db.php`), AWS-Keys (`php-api/src/Sns.php`), Auth0-Cert/Token
(`php-api/src/Config.php`, `admin-scripts/auth0-pw-change.php`), MySQL-/FTP-Creds (Backup-Scripts),
Registry-Token (`.docker/config.json`), SSH-Keys. `workspace-legacy/.gitignore` beachten — vor jedem Commit prüfen.

---

## 12. Nächste Schritte

1. Datenqualität sichten (phpMyAdmin): Spam/Test-Accounts, gelöschte Inhalte, Moderation.
2. Tech-Stack der neuen App festlegen (Backend/DB, Objektspeicher, IdP, Mobile-Framework).
3. ETL Legacy-MySQL → neues Schema (idempotent, `legacy_id`-Brücke). Mapping im HTML-Report (Tab „Migration").
4. Bild-Migration in Objektspeicher (Key `user_pics/<uid>/<datei>`) + Transform-Layer.
5. Feature-Roadmap (Tab „Vision") priorisieren; PHP-Slim-API + Angular-Frontend als Referenz-Spezifikation.

### Verweise
- Voll-Report: `workspace-legacy/cichlids-legacy-bestandsaufnahme.html`
- Analysen: `workspace-legacy/findings/01–10`
- Kuratierte Daten: `workspace-legacy/` (siehe `workspace-legacy/README.md`)

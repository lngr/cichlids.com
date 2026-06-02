# cichlids.com — Legacy-Bestandsaufnahme & Datenrettung

Kuratierte Daten und Analysen aus dem Host-Vollbackup **frontosa (2023-09-20)** der
Foto-Sharing-Community cichlids.com (seit 2023 offline). Grundlage für die geplante
moderne Mobile-First-App.

## Report zuerst lesen

➡️ **[`cichlids-legacy-bestandsaufnahme.html`](./cichlids-legacy-bestandsaufnahme.html)**
— vollständiger Single-File-Report (im Browser öffnen): Architektur, Datenbank, Bilder-Verifikation,
Nutzer/Auth0, Speicherorte, Migrationsempfehlungen, Sicherheit.

## Wichtigste Befunde (Kurz)

- **Datenbasis vollständig gerettet** und charset-korrekt in MySQL 5.7 restauriert (3 DBs).
- Kerndaten in **`cichlids_typo3`** (101 Tab.): 11.948 Nutzer, 189.571 Bilder (180.455 aktiv),
  1.647.849 Kommentare, 6.729 Tanks, 829 Spezies.
- **Bild-Originale praktisch vollständig erhalten** (~235 GB): 176.783 echte Originale (>600px),
  nur 5 aktive Bilder fehlen, ~281 mögliche Resize-Restfälle. Kein großflächiger Verlust.
  Die Resize-Pipeline (`libcichlids`) überschreibt Originale prinzipbedingt nie.
- **Vollständiger Anwendungs-Quellcode vorhanden**: Angular-7-Frontend, **PHP-Slim-3-API** (`/api`),
  TYPO3-Extension `user_cichlids`, Resize-Lib `libcichlids`, AWS-Serverless, Phorum-Forum, TYPO3-Cores.
- Auth0-Export (12.147 Konten) ohne Passwort-Hashes → Login-Migration per Auth0-Hash-Export
  oder Passwort-Reset; Foto-Zuordnung via E-Mail-Join.

## Struktur

| Ordner | Inhalt |
|---|---|
| `legacy-stack/` | docker-compose: MySQL 5.7 + phpMyAdmin (Datenebene) |
| `app-source/` | Kern-Quellen: `php-api` (Slim 3), `typo3-ext-user_cichlids`, `libcichlids`, `docker-web` (Angular), Container-Defs, Admin-Scripts |
| `server-config/` | httpd / nginx / php Konfiguration |
| `migration/` | Migrations-Skript |
| `tools/` | `verify_images.py` (DB↔Datei-Abgleich) |
| `findings/` | Detail-Analysen 01–10 (Markdown) + Bild-CSV-Listen |
| `planning-notes/` | Original-Feature-/Planungsnotizen (Vision für die neue App) |

## Datenebene starten

```bash
cd legacy-stack
docker compose up -d        # MySQL :3306 (root/legacy) + phpMyAdmin :8085
```

## Speicherorte großer Artefakte (außerhalb des Repos)

- Bild-Originale: `/workspaces/legacy-data/images/userpics/` (~235 GB)
- Roh-DB-Dump: `/workspaces/legacy-data/db-dump/frontosa-…sql.gz` (400 MB, PII)
- Auth0-Export: `/workspaces/legacy-data/auth0/auth0-cichlids.json` (PII)
- MySQL-Daten (restauriert): Docker-Volume `cichlids-legacy_db_data`
- Web-Baum & Roh-Extraktion: `/workspaces/_extract/`, Quellcode-Repos: `/workspaces/legacy-source/`

> ⚠️ **Sicherheit:** Die Backups enthalten Klartext-Secrets (DB-/FTP-/Registry-Credentials,
> Auth0-Secret, SSH-Key) und PII. Vor jedem Commit prüfen — siehe Report-Tab „Sicherheit".

# Projektstatus cichlids.com

**Stand: 2026-10-15**

Dieses Dokument ist der Einstiegspunkt, um die Arbeit an cichlids.com ohne Vorwissen aufzunehmen. Es
beschreibt, wo das Projekt steht, welche Arbeitsstränge es gibt und welche Fäden offen sind.

Pflege: Ist ein Faden aus Abschnitt 9 erledigt, wird er dort abgehakt, und das Datum oben wird
aktualisiert. Dauerhafte Entscheidungen gehören in die ADRs (`docs/adr/`), Arbeitspakete in den
Backlog (`backlog/`). Dieses Dokument verweist auf beide und fasst sie nur so weit, wie es für
den Überblick nötig ist.

Weitere Leitdokumente:

- [`PRODUCT-VISION.md`](PRODUCT-VISION.md): Produktidee und PMF-These.
- [`PLAN.md`](PLAN.md): Zielarchitektur, Domänenmodell, ETL-Konzept, Aufbauphasen.
- [`PROJECT-CONTEXT.md`](PROJECT-CONTEXT.md): Altsystem, gerettete Daten, Sicherheitslage.
- [`AGENTS.md`](AGENTS.md): verbindliche Arbeitsregeln für Agents.
- [`TODO.md`](TODO.md): alle offenen Punkte an einer Stelle.
- [`infra/README.md`](infra/README.md): Betriebs-Runbook der Infrastruktur.

---

## 1. Das Projekt in Kürze

cichlids.com ist eine Community für Aquarianer mit Schwerpunkt Cichliden (Buntbarsche). Die
Plattform ist seit 2023 offline. Die gerettete Datenbasis aus dem Host-Backup "frontosa" umfasst:

- MySQL-5.7-Datenbanken:
  - `cichlids_typo3` enthält die Kerndaten: 189.571 Bilder, 1,65 Mio. Kommentare, 6.729 Becken,
    11.948 User und 829 Arten.
  - `cichlids_phorum5` enthält das alte Forum (latin1).
- 235 GB Originalbilder. Die Bilder sind praktisch vollständig; es fehlen 5 aktive Bilder.
- Der Auth0-Export mit 12.147 Konten, ohne Passwort-Hashes.

**Ziel** ist eine moderne, mobile-first Community, bei der das **Becken als Projekt über die Zeit**
im Mittelpunkt steht (Strava-Prinzip). Eine private Pflege-Sicht (Logs, Messwerte, Reminder,
Streaks) erzeugt aus denselben Daten eine öffentliche Stolz-Sicht (Timeline, Zeitraffer,
Meilensteine). Ein reiner Foto-Stream gilt als die PMF-Schwäche der alten Plattform. Details stehen
in `PRODUCT-VISION.md`.

**Zielstack** laut `PLAN.md` und den angenommenen ADRs 0002 bis 0026:

- ASP.NET-Core-API mit OpenAPI als einzigem Vertrag
- Postgres (CloudNativePG mit PITR)
- Keycloak
- Transactional Outbox und Webhooks
- Expo/React Native (Android first)
- Bilder über Object Storage und Cloudflare, Video als HLS
- KI-Agenten als entkoppelter Python-Dienst
- Hetzner mit OpenTofu, k3s und Argo CD, strikt GitOps
- Test-first; jede Phase ist erst mit einem grünen E2E-Pfad fertig (ADR-0003)

**Aufbauphasen** (`PLAN.md` Abschnitt 7):

1. Plattform und strikte GitOps
2. Auth
3. Event-Rückgrat
4. Medien und Galerien, parallel dazu die ETL gegen Staging
5. Stories und Becken
6. Reputation
7. KI-Agenten

Danach folgt der Cutover: finale ETL, Medienmigration, Auth0 nach Keycloak, Go-Live.

---

## 2. Landkarte: Verzeichnisse, Daten, Dienste

| Pfad | Inhalt |
|---|---|
| `/workspaces/cichlids.com` | Projekt-Worktree auf `main`. Enthält Doku, ADRs, Backlog, `app/` (API, ETL, Client, Mobile, lokaler Stack), `infra/`, `clusters/`, `apps/`, `policy/`, `tools/`, `design-system/` und `workspace-legacy/`. |
| `workspace-legacy/` | Bestandsaufnahme des Altsystems: HTML-Report, `findings/` 01 bis 10, kuratierte Altquellen, `legacy-stack/` (Compose für MySQL 5.7 und phpMyAdmin), Server-Konfigurationen und Verifikationswerkzeuge. |
| `/workspaces/legacy-data/db-dump/` | MySQL-Dump des Host-Backups |
| `/workspaces/legacy-data/images/userpics/` | Originalbilder (235 GB) |
| `/workspaces/legacy-data/auth0/` | Auth0-Export (NDJSON) |
| `/workspaces/legacy-source/` | Alte Codebasen und Notizen |
| `/workspaces/_extract/` | Entpacktes Host-Backup mit Verifikationsartefakten |
| `app-screens/` | Git-ignorierte Screenshots des Web-Smokes |
| `infra/cichlids.env.example` | Vorlage für die lokale Credential-Datei des Operators; deren Ort konfiguriert `CICHLIDS_ENV`. |

**Laufende Docker-Dienste:**

- `cichlids-legacy-db-1` (MySQL 5.7, Port 3306, lokale Zugangsdaten root/legacy), Volume
  `cichlids-legacy_db_data` mit importiertem Dump
- `cichlids-legacy-phpmyadmin-1` (Port 8085)
- `stack-postgres-1`, `stack-rustfs-1` und `stack-keycloak-1` mit den persistenten Volumes des
  MVP-Stacks. Die dokumentierte Vollmigration umfasst Daten und Medien; die Verify-Ergebnisse
  stehen in Abschnitt 6.4.

Postgres bindet an Loopback; RustFS und Keycloak binden an `CICHLIDS_STACK_BIND` (Standard:
Loopback, im Devcontainer alle Interfaces). Dump-Import ohne das `mysql`-Schema und Grant-Reset
für root/legacy beschreibt `app/stack/README.md`.

---

## 3. Git-Topologie

Remote: `origin = git@github.com:lngr/cichlids.com.git`. Der lokale Stand liegt vor der lokal
bekannten Referenz `origin/main`. Die Prüfung des Remote-Stands über SSH ist als F3 offen;
der Operator pusht vom eigenen Host.

| Branch | Worktree | Stand |
|---|---|---|
| `main` | `/workspaces/cichlids.com` | Plattform-Code und Legacy-Parity-MVP einschließlich Review-Korrekturen; Operator-Push ausstehend |

`feat/legacy-mvp` und `fix/mvp-review` sind in `main` gemergt (Merge-Commits `2f5f959` und
`07b000d`).

**Untracked auf `main`:** `PRECHECK-IN-REVIEW.md`, `.claude/`, `prototypes/` sowie lokale
Operator-Dateien. Lokale Operator- und Handoff-Dateien sind nicht versioniert und gehören
nicht zu den öffentlichen Projektquellen.

**Git-ignoriert:** Open-Design-Wegwerfartefakte (`*.artifact.json`, `mpz*-image.png`,
`.od-skills/`) und `app-screens/`.

---

## 4. Die zwei Arbeitsstränge im Überblick

| | Strang A: Plattform (`main`) | Strang B: Legacy-Parity-MVP |
|---|---|---|
| Epic | TASK-2 "Foundation and strict GitOps platform" | TASK-3 "Legacy parity MVP on local stack" |
| Ort | Hetzner Cloud (live) | Lokaler Compose-Stack |
| Ergebnis | Cloud-Ressourcen und Backups; App-Tier, Secret-Verwaltung, Argo-CD-Topologie und Ingress sind implementiert, Live-Abnahme offen | API, ETL, Medien-Pipeline, Keycloak und Expo-App mit vollständig migrierten Altdaten |
| Backlog | 6 Done, 5 In Progress, 6 In Review, 11 To Do (aktive Untertasks; zusätzlich 2 unter `completed/`) | 1 Done, 39 In Review, 1 In Progress, 1 To Do (Untertasks) |
| Offene Arbeit | Operator-Apply und Live-Prüfungen von TASK-2.4, 2.6 bis 2.8; Staging-Workloads und Datenbank | Review-Abnahmen, Abschluss TASK-3.42 und Ich-Tab TASK-3.28 |

Wie die Stränge zusammenhängen, steht in Abschnitt 7.

---

## 5. Strang A: Plattform und strikte GitOps (`main`)

### 5.1 Zielarchitektur: zwei Hetzner-Projekte

Die Begründung steht in ADR-0017, ergänzt durch ADR-0014, ADR-0016, ADR-0013, ADR-0009 und
ADR-0010. Ein Credential des einen Projekts kann im anderen nichts ausrichten.

- **`cichlids`** enthält die gesamte live betriebene, rekonstruierbare Infrastruktur und den eigenen
  OpenTofu-State-Bucket. CI/GitOps verwaltet das Projekt per OpenTofu. `prevent_destroy` und
  `delete_protection` schützen es gegen Unfälle. Es hat das **einzige** Cloud-Token.
  - Code in `infra/platform/`: Node, DB-Volume, Media-Master-Bucket, DNS-Zone.
  - Code in `infra/app/`: k3s und Argo CD auf dem bestehenden Node per SSH, eigener State und
    eigene Passphrase, ohne Cloud-Token. Der erste Apply und die Live-Prüfungen sind offen.
- **`cichlids-backup`** enthält ausschließlich die unveränderlichen Backups (Object Lock
  Compliance) und den eigenen State-Bucket.
  - Das Projekt hat kein Cloud-Token.
  - Sein S3-Key gehört nur dem Operator und ist nie ein CI-Secret.
  - Code in `infra/backup/`; der Operator wendet ihn out-of-band an.

Nur die unveränderlichen Backups sind unersetzlich. Alles in `cichlids` lässt sich aus ihnen
rekonstruieren, deshalb darf CI es frei verwalten.

### 5.2 Live-Ressourcen (Region nbg1)

| Projekt | Ressource | Details |
|---|---|---|
| cichlids | `hcloud_server.node` | `cichlids-node`, CX33, Node-IP über `infra/tofu.sh platform output`, Delete- und Rebuild-Protection, `prevent_destroy` |
| cichlids | `hcloud_volume.database` | `cichlids-db`, 10 GB, Delete-Protection, `prevent_destroy`, angehängt |
| cichlids | `hcloud_ssh_key.operator` | `cichlids-operator` |
| cichlids | `aws_s3_bucket.media_master` | `cichlids-media-master`, Object Lock Governance, Versionierung |
| cichlids | `hcloud_zone.primary` | Zone `cichlids.com`, Delete-Protection, `prevent_destroy` |
| cichlids | State | `cichlids-platform-tfstate`, Key `platform/terraform.tfstate`, verschlüsselt |
| cichlids-backup | `aws_s3_bucket.database_backup` | `cichlids-db-backup`, Object Lock Compliance |
| cichlids-backup | `aws_s3_bucket.media_master_backup` | `cichlids-media-backup`, Object Lock Compliance |
| cichlids-backup | State | `cichlids-backup-tfstate`, Key `backup/terraform.tfstate`, verschlüsselt |

- Beide State-Buckets sind versioniert. Der State ist nativ verschlüsselt (pbkdf2, aes_gcm).
- Provider-Pins: hcloud `~> 1.64` (aufgelöst 1.65.0), aws `~> 6.0` (aufgelöst 6.49.0), OpenTofu
  `>= 1.12.0`.
- Der Live-Nachweis für k3s, Argo CD und Workloads auf dem Node ist offen. Die Ressourcen kosten Geld;
  `PLAN.md` schätzt die Startphase auf 15 bis 25 Euro im Monat.
- Ob die Domain `cichlids.com` beim Registrar an die Hetzner-Nameserver delegiert ist, ist offen.
  Die Nameserver liefert `infra/tofu.sh platform output dns_zone_nameservers`.
- `cichlids-db-backup` enthält das Beweisobjekt `layer4-immutability-test.txt`. Seine Compliance-Frist
  ist abgelaufen, das Objekt ist löschbar.

### 5.3 Schutzschichten gegen destruktive Änderungen (ADR-0014)

| Schicht | Mechanismus | Nachweis am Live-System |
|---|---|---|
| 1 | `prevent_destroy` und Delete-Protection bei Hetzner | Erbracht: `plan -destroy` wird abgelehnt, die Konsole verweigert das Löschen |
| 2 | Projekttrennung, der `cichlids`-Key erreicht `cichlids-backup` nicht | Erbracht mit `tools/verify-project-isolation.sh` |
| 3 | Fail-closed Plan-Diff-Gate (`policy/destroy_gate.rego`, Conftest) auf einem echten CI-Plan | **Offen**: Policy, Unit-Tests und App-Tier-Plan/Apply-Workflow existieren; Nachweis am echten CI-Plan und Apply ausstehend (TASK-2.4, 2.5, 2.21) |
| 4 | Object Lock Compliance auf den Backups | Erbracht mit `tools/verify-backup-immutability.sh` |

### 5.4 Werkzeuge und Credentials

- `infra/tofu.sh <platform|backup|app> <tofu-args>` liest die lokale Credential-Datei und setzt je
  Tier die passenden Variablen:
  - `platform`: Cloud-Token, `cichlids`-S3-Key und SSH-Key
  - `backup`: nur den Backup-S3-Key
  - `app`: CI-SSH-Key, Node-Adresse und Host-Key, Primary-S3-Key für den State und eigene
    App-State-Passphrase; kein Cloud-Token
- Credential-Keys:
  - `CICHLIDS_HCLOUD_TOKEN`
  - `CICHLIDS_S3_PRIMARY_*`
  - `CICHLIDS_S3_BACKUP_*`
  - `CICHLIDS_STATE_PASSPHRASE`
  - `CICHLIDS_SSH_PUBLIC_KEY`
  - `CICHLIDS_S3_ENDPOINT` und `CICHLIDS_S3_REGION` (nbg1)
- Der Operator wendet OpenTofu auf dem eigenen Host an. Agents validieren nur statisch.
- Toolchain:
  - Auf dem Host über das `Brewfile`.
  - In CI und Devcontainer über das gepinnte `install-toolchain.sh`.
- Ein Pre-Push-Hook in `.githooks/` prüft die gepushten Commits mit gitleaks auf Klartext-Secrets.
  Er ist nur aktiv, wenn `core.hooksPath` gesetzt ist; `postStartCommand.sh` setzt ihn. Nicht umgehbar ist der
  Secrets-Scan in CI.

### 5.5 Backlog-Stand TASK-2

Die CLI listet 28 aktive Untertasks: 6 Done, 5 In Progress, 6 In Review und 11 To Do.
TASK-2 selbst steht In Progress. TASK-2.1 und TASK-2.2 liegen zusätzlich unter
`backlog/completed/` und erscheinen nicht in `backlog task list --plain`. TASK-1 ist ein
separater Task und steht To Do.

| Task | Titel (gekürzt) | Status |
|---|---|---|
| 2.1 | Reproducible toolchain | Done (`completed/`) |
| 2.2 | OpenTofu foundation tier with safeguards | Done (`completed/`) |
| 2.3 | Bootstrap runbook and credential tiers | Done |
| 2.4 | OpenTofu app tier, k3s und Argo CD per SSH | In Progress, implementiert, erster Apply und Operator-Prüfungen offen |
| 2.5 | Fail-closed Conftest plan-diff gate | In Progress, Live-Nachweis offen |
| 2.6 | SOPS, age, KSOPS | In Review, Operator-Schlüssel und Cluster-Prüfung offen |
| 2.7 | Argo CD app-of-apps und Umgebungen | In Review, Live-Abnahme offen |
| 2.8 | Traefik, HTTP-01-TLS und Platzhalter | In Review, DNS-Apply und Live-Abnahme offen |
| 2.9 | CloudNativePG mit PITR | To Do |
| 2.10 | Erste getestete Restore-Übung | To Do |
| 2.11 | CI-Grün-Gate und Delivery-Pipeline | To Do |
| 2.12 | Branch-Protection auf `main` | To Do |
| 2.13 | Remote State Backend | Done |
| 2.14 | Toolchain-Pins | Done |
| 2.15 | Pre-Push-Hook | Done |
| 2.16 | Drei Hetzner-Projekte | Done, Architektur maßgeblich in ADR-0017 und TASK-2.22 |
| 2.17 | Umgebungsdaten | To Do |
| 2.18 | Hetzner Cloud DNS und Provider-Pins | In Progress, AC #1 und #3 offen |
| 2.19 | Renovate | To Do |
| 2.20 | State nach Vertrauensgrenze trennen | Done, Architektur maßgeblich in ADR-0017 und TASK-2.22 |
| 2.21 | Schutzschichten live verifizieren | In Progress, AC #3 offen |
| 2.22 | Zwei Projekte | In Progress |
| 2.23 | Slug-Secret für Staging und Produktion | To Do |
| 2.24 | Playwright-Smoke mit anonymisierten Daten in CI | To Do |
| 2.25 | Maestro-Flows in CI | To Do |
| 2.26 | gitleaks | In Review, Operator-Prüfungen in CI und auf dem Host offen |
| 2.27 | Keycloak-Realm-Dateien je Umgebung | In Review |
| 2.28 | App-Tests in CI | In Review |
| 2.29 | Isolierter Webhook-Forwarder | To Do |
| 2.30 | Keycloak in Staging und Produktion deployen | To Do |

### 5.6 Implementierung und offene Abnahme

Akzeptanzkriterien werden am echten System geprüft. Die offenen Operator-Prüfungen und
Nachweise stehen in den jeweiligen Task-Notizen und Runbooks.

- **TASK-2.4:** `infra/app/` installiert k3s und Argo CD über SSH mit geprüftem Host-Key auf
  dem bestehenden Node. Der Workflow `infra-app` plant und prüft Änderungen am PR und führt
  den Apply auf `main` aus. CI-Schlüssel, GitHub-Konfiguration, erster Apply und der beobachtete
  Plan/Apply-Pfad sind offen (`infra/README.md`).
- **TASK-2.6:** SOPS/age-Regeln für die öffentliche Test-Fixture, KSOPS im Argo-CD-Repo-Server,
  Klartext-Secret-Prüfung und Roundtrip in CI sind implementiert. Für echte Secrets fehlen der
  öffentliche age-Empfänger in `.sops.yaml`, der private Schlüssel im Cluster-Secret `sops-age`
  und die beobachtete Entschlüsselung im Cluster.
- **TASK-2.7:** Root-Application und ApplicationSets definieren Staging, Produktion und
  PR-Previews. Previews sind auf zwei zugelassene PRs und 48 Stunden begrenzt; Quotas begrenzen
  ihre Ressourcen. Stateful-Ressourcen haben Schutz gegen Prune und Delete. Discovery, Sync
  und Abbau sind am laufenden Argo CD zu prüfen (`clusters/README.md`).
- **TASK-2.8:** k3s bündelt Traefik und ServiceLB; Argo CD verwaltet das `HelmChartConfig`.
  cert-manager stellt HTTP-01-Zertifikate je Host aus. Der Platzhalter hat Overlays für
  `staging.cichlids.com`, `cichlids.com` und `pr-<nummer>.dev.cichlids.com`. A-Records für `@`,
  `staging` und `*.dev` zeigen im Plattform-Code auf die Node-IP. Offen sind ACME-Kontakt,
  DNS-Apply, öffentliche Auflösung, Ports 80/443, HTTPS und der Abbau eines Previews.
- **TASK-2.26:** gitleaks prüft die gepushten Commits im Hook sowie Arbeitsbaum und komplette
  Historie in CI. Die Operator-Prüfungen umfassen einen absichtlich fehlschlagenden CI-Scan
  auf einem Prüfbranch und die Installation auf dem Host.
- **TASK-2.18 und TASK-2.22:** Provider, DNS-Destroy-Gate, Projekttrennung und Schutzmechanismen
  am echten System abnehmen. TASK-2.5 und AC #3 von TASK-2.21 benötigen den echten CI-Plan.

### 5.7 Stolperfallen

- Hetzner Object Storage implementiert `PutBucketTagging` nicht (Antwort 501). Die
  `aws_s3_bucket`-Ressourcen haben deshalb keine Tags; das Policy-Gate erkennt kritische Ressourcen
  am Typ.
- Object Lock schützt geschriebene Objekte. Ein leerer Compliance-Bucket lässt sich in der Konsole
  löschen.
- `tofu providers`, `plan` und `apply` brauchen `CICHLIDS_STATE_PASSPHRASE` (native
  State-Verschlüsselung). `infra/tofu.sh` lädt die Credential-Datei. Statische Validierung mit
  `tofu init -backend=false` und `tofu validate` benötigt keinen Zugriff auf den State.
- Git führt committete Hooks nie automatisch aus. Nach einem frischen Clone ist einmal
  `git config core.hooksPath .githooks` nötig.
- DNS läuft projektbezogen über den `hcloud`-Provider (`hcloud_zone`, `hcloud_zone_rrset`).
- Backlog-CLI:
  - Wird `-s Done` zusammen mit einem langen `--final-summary` gesetzt, wird es manchmal verworfen.
    Den Status deshalb einzeln setzen und prüfen.
  - ACs umformulieren: erst `--remove-ac <n>` (absteigend), dann `--ac "..."`.
  - Auf dem Host läuft die CLI per `npx -y backlog.md@1.45.2`.

---

## 6. Strang B: Legacy-Parity-MVP

### 6.1 Ziel laut Epic TASK-3

> Rebuild the complete legacy feature set on the new stack (ASP.NET Core 10 API, Postgres,
> Keycloak, RustFS object store, Expo client) with the legacy data migrated, usable end to end on
> the local compose stack.

- **Scope:**
  - relationales Domänenschema mit `legacy_id`-Brücke
  - ETL in ein leeres Ziel, mit Reset für Dev und Staging
  - Medien-Pipeline
  - API mit voller Legacy-Parität
  - generierter TS-Client
  - Expo-Kernscreens
  - lokales Keycloak mit Auth0-Import
- **Out of scope:**
  - Produktions-Deployment ("the GitOps app tier is separate work")
  - Video
  - Reputation
  - Pflege-/Tracking-Schicht
  - KI-Agenten

### 6.2 Aufbau von `app/`

| Teil | Inhalt |
|---|---|
| `app/api/` | .NET 10 (SDK 10.0.301), `Cichlids.sln`. Abhängigkeiten der Projekte: `Cichlids.Api` nutzt `Cichlids.Infrastructure`, `Cichlids.Infrastructure` nutzt `Cichlids.Domain`. |
| `Cichlids.Api` | ASP.NET Core Minimal API mit OpenAPI und Scalar. Endpunkte für Pictures (mit Artenfilter), Tanks und Profiles, Species, Comments und Ratings, Community-Archiv, Legacy-Redirects, Me, Auth (Keycloak-JWT) und die Webhook-Administration. Dazu der Outbox-Dispatcher. |
| `Cichlids.Domain` | Entities ohne EF-Abhängigkeit |
| `Cichlids.Infrastructure` | EF Core 10 mit Npgsql und snake_case, Migrationen, S3-Abstraktion (AWSSDK.S3), NetVips für Bildvarianten, `SlugGenerator` |
| `Cichlids.Etl` | Konsolen-ETL (MySqlConnector) mit den Kommandos `migrate`, `reset`, `all`, `verify`, `media`, `media-verify` und `keycloak-import` |
| Tests | `Api.Tests`, `Infrastructure.Tests` und `Etl.Tests`: xunit, Testcontainers (postgres:17, mysql:5.7) |
| `app/client-core/` | Aus `openapi.json` generierter TypeScript-Client (openapi-typescript, openapi-fetch), vitest, msw |
| `app/mobile/` | Expo ~57, expo-router, React Native 0.86, React 19. Screens für Galerie, Bilddetail, Becken, Profil, Community und Arten. i18n mit Englisch als Standard, Theme aus den Design-Tokens. E2E mit Maestro (`e2e/maestro`) und Playwright (`e2e/playwright`). |
| `app/stack/` | `compose.yaml` (postgres:17, RustFS als S3, Keycloak 26.6), deklarativer Realm `keycloak/cichlids-realm.json`, `bootstrap.sh`, `README.md` |
| Workspace | pnpm (`app/pnpm-workspace.yaml`) |

### 6.3 ADRs für Anwendung, Migration und Auth

Der Index `docs/adr/index.md` enthält ADR-0001 bis ADR-0027. ADR-0019 bis ADR-0026 sind
Accepted; ADR-0027 ist Proposed.

- ADR-0019 Persistence and Migration Tooling
- ADR-0020 Relational Domain Schema
- ADR-0021 Community Discussion Archive
- ADR-0022 Legacy Account Import into Keycloak
- ADR-0023 Identity Model: User, Logins and Public Handle
- ADR-0024 Webhook Delivery Boundary and Redelivery
- ADR-0025 Keycloak Realm Configuration per Environment
- ADR-0026 Legacy Import Runs Once Against an Empty Target
- ADR-0027 Session Model for Native and Web Clients (Proposed)

### 6.4 Ergebnisse der Datenmigration (laut Task-Notizen)

- **ETL-Verify** rechnet die Erwartungswerte unabhängig aus der Legacy-Quelle nach. Ergebnis:
  0 Abweichungen bei der dokumentierten Vollmigration und dem isolierten Prüflauf von TASK-3.30.
- **Dokumentierter Umfang des Voll-Seeds (TASK-3.9):**
  - 179.337 von 179.340 Medien im Object Store (3 Quelldateien fehlen)
  - 623.220 Varianten (200/400/800/1600 px, ohne Hochskalieren)
  - 283 GB, Laufzeit 94 Minuten
- **Medien-Pipeline (TASK-3.29 und TASK-3.31):**
  - Originale sind privat und werden bereinigt gespeichert; öffentliche Varianten enthalten
    keine Standort-Metadaten. TASK-3.29 belegt dies auf einem isolierten Voll-Seed.
  - Jeder Lauf prüft Originale und Varianten gegen den Object Store (SHA-256 der Quelldatei und
    Pipeline-Version als Objekt-Metadaten) und lädt nur Fehlendes oder Abweichendes hoch. Nach einem
    Datenbank-Reset schreibt er die Metadaten ohne Uploads in die Datenbank, und verlorene Objekte werden
    aus der Quelle ergänzt.
- **934 Altdateien** lassen sich nicht dekodieren, die meisten sind BMP-Dateien. Ist eine davon in
  einem Format, das Browser darstellen, legt der Medien-Seed eine bereinigte öffentliche Kopie unter
  `variants/copy/` an, und die App zeigt das Bild aus dieser Kopie. Leere Dateien und Nicht-Bilder
  bekommen keine Kopie. Der Seed-Report (`--missing-out`) listet jedes nicht dekodierbare Original
  unter `broken`, und die mit öffentlicher Kopie zusätzlich unter `copies`. Originale selbst sind
  nicht öffentlich lesbar.
- In der Besatzspalte der Altdaten ist jedes live geschaltete Becken leer. Die Becken haben also
  tatsächlich 0 Bewohner.
- Die vereinbarten ETL-Regeln stehen in den Notizen von TASK-3:
  - Bilder, Becken und Galerien
  - verwaiste Inhalte unter nicht gelisteten Platzhalterprofilen
  - ein Archiv-Tresor für Kommentare ohne migriertes Ziel
  - die Slug- und Redirect-Strategie
  - die Forumsrichtung
- Neue Slugs werden per HMAC über `Slugs:Secret` erzeugt.

### 6.5 Backlog-Stand TASK-3

Die CLI listet 42 Untertasks. Das Epic TASK-3 steht To Do; sein Status ist unabhängig von den
Statuswerten der Untertasks. TASK-3.42 steht In Progress, die Umsetzung einschließlich R46
ist in diesem Dokument beschrieben. Der Statuswechsel und die Abnahme sind offen.

| Status | Tasks |
|---|---|
| Done (1) | 3.29 private Originale und bereinigte Metadaten |
| In Review (39) | 3.1 bis 3.27: Schema, ETL, Medien, APIs, Auth, Outbox, Client und Mobile; 3.30 bis 3.41: Review-Korrekturen |
| In Progress (1) | 3.42 Code- und Doku-Korrekturen aus dem Review |
| To Do (1) | 3.28 Ich-Tab mit eigenen Bildern, Becken und Entwürfen |

**Review-Korrekturen:** TASK-3.29 bis TASK-3.42 sind implementiert. Sie umfassen Medienprivatheit,
Import- und Verify-Regeln, Konto-Import, Webhooks, atomare Profilanlage, Bewertungen, Grenzen für
Kommentare und Uploads, Redirects, App-Fehlerzustände, dauerhafte Anmeldung und Code-/Doku-Prüfungen.
Die Zuordnung zu R1 bis R46 steht in `REVIEW-MVP.md`, Abschnitt "Umsetzung". Done ist TASK-3.29;
die übrigen Review-Tasks benötigen die dokumentierten Abnahmen und Nachweise.

**Aktive Drafts (7):**

- DRAFT-1 Mail-Kampagne an Bestandsmitglieder, Mail-Provider und rechtliche Prüfung offen
- DRAFT-3 gespeicherter Link zwischen Bewertung und Kommentar
- DRAFT-6 Änderung des öffentlichen Handles
- DRAFT-7 Google- und Facebook-Login mit den Legacy-Apps
- DRAFT-8 hochgeladenes Foto einem eigenen Becken zuordnen
- DRAFT-9 Arten beim Veröffentlichen eines Fotos markieren
- DRAFT-10 jeden API-Abruf als Ereignis erfassen und Views daraus ableiten

DRAFT-4 (Bewertungen) ist durch R9 entschieden und archiviert. DRAFT-5 (Views durch Smokes)
liegt in `backlog/archive/drafts/`. gitleaks ist TASK-2.26 zugeordnet; ein aktiver DRAFT-2
existiert nicht.

- TASK-3.15 importiert ca. 12.050 Legacy-Konten mit den Required Actions `UPDATE_PASSWORD`
  für Passwortkonten und `TERMS_AND_CONDITIONS` für alle importierten Konten. Das dokumentierte
  Dev-Ergebnis umfasst 12.050 angelegte Konten und 6.447 Profil-Verknüpfungen; der Folgelauf
  erzeugt 0 Konten. Die Operator-Abnahme der ACs ist F21. TASK-3.33 schützt die Identitäten
  und begrenzt Profil-Verknüpfungen auf Mitgliederprofile.
- TASK-3.16, 3.21 und 3.22 liefern Upload, Login und Kommentieren mit Bewertung. Schreibende
  E2E-Tests verwenden den isolierten Schreib-Stack (`app/mobile/e2e/write-stack.sh`: DB
  `cichlids_e2e`, Bucket `cichlids-e2e`, API :5046, Web :8082).
- TASK-3.41 hält native Tokens in SecureStore und verwaltet Web-Sitzungen in der API hinter
  einem httpOnly-Cookie mit CSRF-Schutz. Die Tokens der Web-Sitzungen sind mit Data Protection
  verschlüsselt. Key-Ring, BFF-Client-Secret und Routing unter derselben Origin gehören zum
  offenen Deployment der API und Web-App.
- Die Maestro-Abnahme ist offen (F13). Die lokale Vorprüfung `PRECHECK-IN-REVIEW.md` ist
  untracked; maßgeblich sind die Task-Notizen und die Definition of Done.

### 6.6 Lokal starten

Die Anleitung steht in `app/stack/README.md`.

1. Die Legacy-MySQL muss laufen und den importierten Dump enthalten. `bootstrap.sh` startet sie bei
   Bedarf, importiert den Dump aber nicht.
2. API stoppen und `app/stack/bootstrap.sh` ausführen. Jeder Lauf beginnt mit dem Reset: Stack hoch,
   `etl reset`, Realm löschen und aus der Datei importieren, `etl all`, `etl media` (Standard
   `--limit 3000`), `etl keycloak-import --open-registration`, `etl verify`. Von Hand angelegte
   Zeilen und Konten gehen dabei verloren, Medienobjekte im Bucket bleiben.
   - `CICHLIDS_MEDIA_LIMIT=0` lädt alle Medien.
   - `CICHLIDS_LEGACY_STACK_DIR` zeigt standardmäßig auf `workspace-legacy/legacy-stack` im
     Repository.
   - Nur die Dienste starten: `docker compose -f app/stack/compose.yaml up -d`.
3. API starten: `dotnet run` in `Cichlids.Api` (Port 5045).
4. App starten: `pnpm exec expo start --web --port 8081`. Web-Smoke: `pnpm run e2e:web`; schreibende
   E2E-Specs: `pnpm run e2e:write`.

Das .NET-SDK installiert der Devcontainer bei der Erstellung über `tools/install-dotnet.sh`
in den lokalen SDK-Installationspfad. Die Einrichtung umfasst auch pnpm-Abhängigkeiten und
Playwright-Chromium.

Ports:

| Dienst | Port |
|---|---|
| Postgres | 5432 (`cichlids`/`cichlids`) |
| RustFS | 9000 |
| Keycloak | 8180 (admin/admin) |
| API | 5045 |
| Expo Web | 8081 |
| Schreib-Stack API / Web | 5046 / 8082 |
| Legacy-MySQL | 3306 |
| phpMyAdmin | 8085 |

Dev-User in Keycloak: `dev-user` und `dev-mod` (Moderator und Admin), Passwort jeweils `dev-password`.

### 6.7 Lose Enden in Strang B

- Der Maestro-Runner blockiert beim Device-Attach (F13). TASK-2.25 umfasst die CI-Ausführung;
  der Playwright-Web-Smoke beweist den Web-Pfad.
- Die E2E-Abnahme am deployten Stack und das CI-Grün-Gate sind offen. Die Stories in Review
  sind trotz des Merges in `main` unabgenommen.
- `app/stack/keycloak/cichlids-realm.json` gilt ausschließlich lokal. Die deployten Umgebungen
  verwenden `apps/keycloak/overlays/staging/cichlids-realm.json` und
  `apps/keycloak/overlays/prod/cichlids-realm.json` gemäß ADR-0025. Diese verlangen TLS und
  exakte Redirects und enthalten keine Konten oder Secrets. Registrierung ist Laufzeitzustand;
  Produktion öffnet sie nach vollständigem Konto-Import, Staging hält sie geschlossen.
- Native Anmeldung verwendet den öffentlichen Client `cichlids-app` mit PKCE. Das Web verwendet
  den vertraulichen Client `cichlids-web-bff` mit PKCE und Secret in der API. Staging hat den
  API-Callback `https://staging.cichlids.com/api/auth/callback`; der Produktionsclient hat keine
  Login-Redirect-URI. Der Operator setzt das BFF-Secret aus SOPS nach jedem Realm-Import in
  Keycloak. Details: `apps/keycloak/README.md`.
- Realm-Dateien sind Konfigurationsartefakte; das Keycloak-Deployment ist TASK-2.30.
- Bilddetail-Abrufe erhöhen den View-Zähler. DRAFT-5 ist archiviert; DRAFT-10 beschreibt die
  offene Ereignis-Erfassung für Abrufe.
- ADR-0027 ist Proposed. Native Abnahme auf einem Gerät, Same-Origin-Deployment und das
  Verhalten beim Logout während eines Keycloak-Ausfalls sind offene Prüf- und Betriebspunkte.

---

## 7. Wie hängen die beiden Stränge zusammen?

Strang A stellt die abgesicherte Plattform und GitOps-Auslieferung bereit. Strang B liefert
API, Auth, ETL und Medien-Pipeline als lauffähige Anwendung mit den geretteten Altdaten. TASK-3
grenzt das Produktions-Deployment ausdrücklich aus; dieses gehört zum Plattformstrang.

Die Rolle des Legacy-Parity-MVP ist als O1 geklärt: Er beweist die Migration und bildet die
Relaunch-Basis für die rund 12.000 Bestandsmitglieder. DRAFT-1 beschreibt die Mail-Kampagne.
Die Produktvision mit Pflege-Logs, Timeline und Rückblicken baut auf dieser Anwendung auf.
`MVP-VISION.md`, Abschnitt 8, beschreibt die vorführbaren Slices des Zielbilds.

O2 legt Deployment zuerst fest. Der gemergte MVP dient als erster Staging-Workload. Seine
Abnahmen und die Operator-Prüfungen der für Staging relevanten Review-Tasks sind offen.
Die Slices des Zielbilds beginnen mit einem laufenden Staging-System; Scanner-Spike,
Taxonomie-Zuordnung und Profildaten-Pipeline sind parallel bearbeitbare Arbeit.

Slice 1 des Zielbilds ist das Epic TASK-4 "Slice 1 app frame". Seine Stories TASK-4.1 bis 4.8
stehen In Review: Feature-Flags, die App-Texte in elf Sprachen, die fünf Tabs, Einheiten mit
gespeicherter Eingabeeinheit, das Onboarding mit Region, Interessen und Abzeichen, die
Becken-Seite nach der Hi-Fi-Vorlage mit Zeitraffer, Entdecken mit Hall of Fame je Region und
Heute als Startseite. Die Produktentscheidungen dazu stehen in den Notizen der Tasks; das
vollständige Heute des Zielbilds hält DRAFT-11 fest.

---

## 8. Design-Artefakte

- Das Design-System liegt in `design-system/` (`tokens.css`, `tokens.json`, `README.md`) und in
  `cichlids-design-system.html`.
  - Light-Modus: Palette "Aquatisches Teal"
  - Dark-Modus: "Biotop/Naturstein" mit Pflanzengrün
- Prototypen: `cichlids-tank-prototype.html` und `cichlids-tank-detail-hifi.html`. Sie zeigen das
  Becken-Detail aus der Produktvision, also die Signature-Screens der Pflege- und Stolz-Sicht.
- Plattformen: Android und iOS gleichwertig und zum Launch gleichzeitig; entwickelt wird zuerst
  gegen Android. Der Zuschnitt des Webs (öffentliche Seiten für Suchmaschinen, Startseite als
  Landingpage zur App, Web-App hinter dem Login) ist offen und wird eigens geplant
  (`MVP-VISION.md`, Abschnitt 9).
- Die MVP-App in Strang B leitet ihr Theme aus diesen Tokens ab. Screenshots liegen in
  `app-screens/`.

---

## 9. Offene Fäden

Die IDs sind stabil, damit sich die Fäden einzeln abarbeiten und abhaken lassen.

### Sicherung (dringend)

- [x] **F1** Der Projektstand ist lokal auf `main` committet. Der Push ist Sache des Operators.
- [x] **F2** Die Sicherung von `feat/legacy-mvp` auf `origin` ist abgeschlossen.
- [x] **F20** Der Secret-Scan ist durch lokale Prüfungen belegt. TASK-2.26 implementiert gitleaks
  im Pre-Push-Hook und in CI; die Operator-Abnahme des Tasks ist offen.
- [ ] **F3** Den SSH-Zugriff auf GitHub aus dem Devcontainer reparieren (Host-Key-Prüfung), damit
  der Remote-Stand prüfbar ist.
- [x] **F4** Die Infrastruktur-Dokumentation steht in den versionierten Projektquellen.
  Lokale Handoff-Dateien gehören nicht zu diesen Quellen.

### Strang A

- [ ] **F5** TASK-2.18 abschließen (AC #1 und #3, siehe 5.6).
- [ ] **F6** TASK-2.22 abschließen (siehe 5.6).
- [ ] **F7** Die Delegation der Domain `cichlids.com` beim Registrar prüfen oder durchführen.
- [ ] **F8** Den Umgang mit dem Beweisobjekt `layer4-immutability-test.txt` klären: Die Frist ist
  abgelaufen, es lässt sich also löschen oder behalten.
- [ ] **F9** Die laufenden Kosten der Live-Ressourcen gegen den Nutzen abwägen, solange
  der Live-Nachweis des Workloads offen ist.
- [ ] **F10** TASK-2.4 ist implementiert: k3s und Argo CD per SSH. Ersten Apply und CI-Pfad
  abnehmen; dazu die Live-Prüfungen von TASK-2.6, 2.7, 2.8 und 2.26 durchführen. TASK-2.5 und
  AC #3 von TASK-2.21 benötigen den echten CI-Plan.
- [ ] **F29** Hetzner-Cloud-Firewall für den Node im Plattform-Tier umsetzen. `TODO.md` führt
  öffentlich erreichbares SSH als offenen Punkt; nur die nötigen Ports freigeben.

### Strang B

- [x] **F11** Docker-Volumes des MVP-Stacks geprüft und Stack gestartet: Daten vollständig,
  `bootstrap.sh` grün, `etl verify` 0 Abweichungen, Playwright-Web-Smoke grün. Das .NET-SDK
  installiert der Devcontainer über `tools/install-dotnet.sh`.
- [ ] **F12** Die MVP-Stories in Review abnehmen (siehe O2). Maßgeblich sind die Task-Notizen
  und die Definition of Done; `PRECHECK-IN-REVIEW.md` ist eine lokale, untracked Vorprüfung.
- [ ] **F13** Den Maestro-Runner zum Laufen bringen oder den AC von 3.23 bewusst anpassen.
- [x] **F14** `feat/legacy-mvp` und `fix/mvp-review` sind in `main` gemergt.
- [x] **F15** Die MVP-Stories 3.15, 3.16, 3.21 und 3.22 sind umgesetzt und stehen in Review.
  DRAFT-1 (Mail-Kampagne) ist offen.
- [ ] **F21** Die ACs von TASK-3.15 am Dev-Stack abnehmen. Das dokumentierte Import-Ergebnis
  umfasst 12.050 angelegte Konten, 6.447 Profil-Verknüpfungen und 20 verworfene Datensätze ohne
  Kontakt und Profil. Der idempotente Folgelauf erzeugt 0 Konten. Die Abnahme ist offen.
- [x] **F22** Die Entscheidungen zu Konto-Import, Identitäten und Registrierung stehen in
  ADR-0022, ADR-0023 und den Task-Notizen. DRAFT-6 bis DRAFT-10 beschreiben offene Funktionen.
- [ ] **F23** Über DRAFT-3 (gespeicherter Link Bewertung zu Kommentar) entscheiden. DRAFT-4 ist
  durch R9 entschieden und archiviert, DRAFT-5 ist archiviert; aktive Drafts stehen in 6.5.
- [x] **F24** Die Android-Package-ID `com.cichlids.app` ist bestätigt.
- [x] **F27** Die Befunde des Reviews des gemergten MVP (`REVIEW-MVP.md`, R1 bis R46) sind einzeln
  besprochen und entschieden. Die Webhook-Entscheidung steht in ADR-0024.
- [ ] **F28** Die Review-Korrekturen TASK-3.29 bis TASK-3.42 sind in `main` implementiert.
  TASK-3.29 ist Done; TASK-3.30 bis TASK-3.41 stehen In Review, TASK-3.42 steht In Progress
  und umfasst diese Statusdokumentation (R46). Die Review-Tasks TASK-2.27, 2.28 und 2.26
  sind implementiert und stehen In Review. Offen sind deren Abnahmen und CI-/Live-Nachweise,
  der Abschluss von TASK-3.42, die Annahme von ADR-0027 sowie TASK-2.24 (anonymisierter
  Playwright-Smoke), TASK-2.25 (Maestro in CI) und TASK-2.29 (isolierter Webhook-Forwarder).
  Die Zuordnung steht in `REVIEW-MVP.md`, Abschnitt "Umsetzung"; die hoch priorisierten
  Review-Tasks sind Voraussetzungen für TASK-2.4.
- [ ] **F26** (TASK-2.23) Slug-Secret für Staging und Produktion erzeugen, sicher verwahren (SOPS, TASK-2.6) und
  identisch für ETL und API setzen, bevor der Produktiv-Import läuft. Aus ihm entstehen Bild-Slugs,
  `user<zahl>`-Handles, Login-Usernames und `guest_<nummer>`-Gastnamen; ein anderer Schlüssel ergäbe andere
  Namen und Links. Außerhalb der Entwicklung verweigern ETL und API den Start ohne Secret oder mit dem
  Dev-Wert.
- [x] **F25** Registrierung und Altprofile: Ein Login erreicht ein migriertes Profil nur über die
  Verknüpfung, die der Account-Import anlegt; der Import läuft, bevor die Selbstregistrierung in einer
  Umgebung offen ist (ADR-0023). Die API verknüpft nie über die E-Mail.

### Doku-Korrekturen

- [x] **F16** `PLAN.md` Abschnitt 7 beschreibt die zwei Hetzner-Projekte aus ADR-0017.
- [x] **F17** Die Notiz in TASK-3.10 beschreibt den umgesetzten Artenfilter.
- [x] **F18** Der Expo-Web-Port ist überall 8081 (Devcontainer, README, bootstrap, Playwright,
  Maestro).
- [x] **F19** `app/stack/README.md` beschreibt den Dump-Import ohne das `mysql`-Schema und den
  Grant-Reset für `root/legacy`.

### Klärung

- [x] **O1** Die Rolle des Legacy-Parity-MVP ist geklärt: Er liefert eine lauffähige Anwendung mit
  den echten Altdaten, beweist die Migration und ist die Relaunch-Basis für die Rückgewinnung der
  ca. 12k Bestandsmitglieder. Die Vision-Funktionen bauen später darauf auf.
- [x] **O2** Priorität: zuerst das Deployment (Strang A). Der MVP ist in `main` gemergt und dient als
  Testobjekt für Staging; die Stories in Review benötigen ihre Abnahme.
- [x] **O3** ADR-0015 (Backlog.md) ist angenommen.
- [ ] **O4** Serie in Wochen (TASK-5.5): Die Regel steht in den Notizen des Tasks, offen ist der
  Umgang mit dem Freeze. Einträge beruhen auf Selbstauskunft; der Freeze verhindert erfundene
  Einträge im Urlaub. Zur Wahl stehen eine nur private Serie mit Freeze, eine öffentliche Serie
  mit Freeze, eine Serie ohne Freeze oder keine Serie. TASK-5.5 wartet auf diese Entscheidung.
- [ ] **O5** Sichtbarkeit der Becken und Pflege-Einträge (TASK-5.7): Das Konzept ist entschieden
  (Stufen öffentlich, Follower, ausgewählte Personen, privat; Verlauf erbt die Stufe des Profils;
  ausdrückliche Frage beim Anlegen statt Voreinstellung; Notizen in TASK-5.7). Offen sind
  Altbecken, vor der Frage angelegte Becken, das Ausblenden einzelner Einträge und Folgen ohne
  Konto.
- [ ] **O6** Videolänge nach Reputation (TASK-5.10): Lange Videos (bis 3 Minuten) sollen nur
  Mitglieder mit einer gewissen Reputation hochladen dürfen. Was Reputation ist, wie sie entsteht
  und welche Länge für alle anderen gilt, ist auszuarbeiten; Punkte und Ranglisten sind für die
  App ausgeschlossen.
- [ ] **O7** Vorläufige App-Konten und öffentliches Web als Funnel: Die Installation der App
  ergibt ein anonymes, vorläufiges Konto; das Web zeigt indexierbare Profile und vom Verlauf nur
  die letzten 2 bis 3 Einträge. Rechte vorläufiger Konten und die Architektur der
  Geräte-Anmeldung sind auszuarbeiten (Draft im Backlog).
- [ ] **O8** Fotos zwischen Becken und Galerie: entschieden ist, dass jedes Foto und Video des
  Beckens einmal in der Galerie geteilt werden kann; der Galerie-Eintrag verweist auf das
  bestehende Medium, und der Becken-Bezug erscheint nur Betrachtern, die das Becken sehen dürfen.
  Offen ist die Besprechung des Medien-Datenmodells mit dem User vor der Umsetzung.

---

## 10. Lese- und Arbeitsreihenfolge

1. `AGENTS.md` und `PROJECT-CONTEXT.md`, insbesondere die Dokumentations-Landkarte in Abschnitt 0.
2. Dieses Statusdokument und `TODO.md` für Stand und offene Arbeit; `PRODUCT-VISION.md` und
   `MVP-VISION.md` für Produktziel und Zuschnitt.
3. `docs/adr/index.md` und die relevanten ADRs. Für Arbeit an Stories den Skill
   `skills/backlog-workflow/SKILL.md` und die Backlog-MCP-Anleitung lesen; Task-Details sind
   maßgeblich für Scope, ACs und Nachweise.
4. `REVIEW-MVP.md`, insbesondere die Umsetzungstabelle, für die Zuordnung der Review-Befunde.
5. `infra/README.md`, `clusters/README.md` und `apps/keycloak/README.md` für Apply, GitOps,
   DNS/TLS und Auth; `app/stack/README.md` für den lokalen Anwendungspfad.

Die Arbeitspriorität ist O2: Staging mit dem MVP als erstem Workload. Dafür sind die relevanten
Review-Tasks abzunehmen und die Operator-Schritte aus F10 sowie DNS, Secrets und Node-Firewall
zu bearbeiten. API, Web-App, Keycloak und Datenbank benötigen Deployments und Secrets. Die
übrigen Review-Abnahmen und CI-Pfade stehen in F28; die Slices des Zielbilds folgen dem
laufenden Staging-System.

---

## 11. Arbeitsregeln (Kurzfassung)

Die vollständigen Regeln stehen in `AGENTS.md`.

- Agents committen lokal mit Conventional Commits und ohne Trailer. Der Operator pusht.
  Destruktive Git-Operationen benötigen eine Freigabe.
- Agents greifen auf Tasks über den MCP-Server `backlog` zu und lesen zuerst dessen
  Workflow-Anleitung. Task-Dateien werden ausschließlich über die Tools gepflegt. Agents
  setzen keinen Assignee; Zuweisungen vergibt nur der Projektinhaber.
- Stories sind auf Englisch und spezifizieren einen Given-When-Then-Pfad vor der Umsetzung.
  Red-Green, grüne E2E-Tests, Abdeckung der maßgeblichen Logik und vollständig grünes CI sind
  Teil der Definition of Done. Story und E2E-Test sind bidirektional verknüpft.
- Infra-Abnahmen prüfen die offenen ACs am echten System. OpenTofu-Applies führt der Operator
  aus; Agents prüfen statisch.
- Doku und Kommentare beschreiben den aktuellen Zustand und zeitlose Begründungen im Präsens.
  Session-Bezüge und Beschreibungen von Vorzuständen sind ausgeschlossen. Gedankenstriche
  werden als gewöhnliche Bindestriche geschrieben.
- Die öffentliche Doku enthält keine Secrets, personenbezogenen Daten, konkreten Node-IPs
  oder Host-Key-Fingerprints. Node-Werte liefert `infra/tofu.sh platform output`.
- Die UI folgt den Design-Tokens und Referenz-Screens: mobile-first, Android und iOS
  gleichwertig, Light und Dark, WCAG AA und Touch-Ziele von mindestens 48 dp.

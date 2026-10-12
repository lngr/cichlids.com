# Projektstatus cichlids.com

**Stand: 2026-10-01**

Dieses Dokument ist der Einstiegspunkt, um die Arbeit an cichlids.com ohne Vorwissen aufzunehmen. Es
beschreibt, wo das Projekt steht, welche Arbeitsstränge es gibt und welche Fäden offen sind.

Pflege: Ist ein Faden aus Abschnitt 9 erledigt, wird er dort abgehakt, und das Datum oben wird
aktualisiert. Dauerhafte Entscheidungen gehören in die ADRs (`docs/adr/`), Arbeitspakete in den
Backlog (`backlog/`). Dieses Dokument verweist auf beide und wiederholt sie nur so weit, wie es für
den Überblick nötig ist.

Weitere Leitdokumente:

- [`PRODUCT-VISION.md`](PRODUCT-VISION.md): Produktidee und PMF-These.
- [`PLAN.md`](PLAN.md): Zielarchitektur, Domänenmodell, ETL-Konzept, Aufbauphasen.
- [`PROJECT-CONTEXT.md`](PROJECT-CONTEXT.md): Altsystem, gerettete Daten, Sicherheitslage.
- [`AGENTS.md`](AGENTS.md): verbindliche Arbeitsregeln für Agents.
- [`infra/README.md`](infra/README.md): Betriebs-Runbook der Infrastruktur.
- [`HANDOFF.md`](HANDOFF.md) (untracked, transient): Übergabenotiz zur Infrastruktur mit Stand
  2026-07-08. Ihr Inhalt ist in Abschnitt 5 eingearbeitet.

---

## 1. Das Projekt in Kürze

cichlids.com war eine Community für Aquarianer mit Schwerpunkt Cichliden (Buntbarsche). Die
Plattform ist seit 2023 offline. Aus dem Host-Backup "frontosa" vom 2023-09-20 sind gerettet:

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

**Zielstack** laut `PLAN.md` und den ADRs 0002 bis 0018:

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
| `/workspaces/cichlids.com` | Haupt-Worktree, Branch `main`. Enthält Doku, ADRs, Backlog, `infra/`, `policy/`, `tools/`, `design-system/` und `workspace-legacy/`. |
| `/workspaces/cichlids-mvp` | Zweiter Worktree, Branch `feat/legacy-mvp`. Enthält zusätzlich `app/` (API, ETL, Client, Mobile, lokaler Stack). |
| `/workspaces/cichlids.com/workspace-legacy/` | Bestandsaufnahme des Altsystems: HTML-Report, `findings/` 01 bis 10, kuratierte Altquellen, `legacy-stack/` (Compose für MySQL 5.7 und phpMyAdmin), Server-Konfigurationen, `tools/verify_images.py`. |
| `/workspaces/legacy-data/db-dump/` | `frontosa-mysql-2023-09-20-06-00.sql.gz` (382 MB) |
| `/workspaces/legacy-data/images/userpics/` | Originalbilder (235 GB) |
| `/workspaces/legacy-data/auth0/` | `auth0-cichlids.json` (NDJSON) |
| `/workspaces/legacy-source/` | Alte Codebasen: Angular-7-SSR-Frontend, C#-Lambdas, .NET-Core-2.0/Orleans-Prototyp, Fuse-Template, Mail-Templates und Notizdateien |
| `/workspaces/_extract/` | Entpacktes Host-Backup mit Verifikationsartefakten |
| `/workspaces/app-screens/` | Screenshots der MVP-App (2026-07-09) |
| `/workspaces/NOTIZEN.md` | Idee: das alte Phorum wiederbeleben (umgesetzt als Community-Archiv in Strang B) |
| `~/.config/cichlids/cichlids.env` | Echte Cloud-Credentials auf dem Operator-Host, git-ignoriert. Vorlage: `infra/cichlids.env.example`. |

**Docker-Dienste auf dieser Maschine:**

- **Laufen:**
  - `cichlids-legacy-db-1` (MySQL 5.7, 127.0.0.1:3306, root/legacy), Volume `cichlids-legacy_db_data` mit bereits importiertem Dump
  - `cichlids-legacy-phpmyadmin-1` (127.0.0.1:8085)
  - Die Container des MVP-Stacks `stack-postgres-1`, `stack-rustfs-1` und `stack-keycloak-1` mit den
    Volumes `stack_postgres-data`, `stack_rustfs-data` (270 GB), `stack_rustfs-logs` und
    `stack_keycloak-data`. Geprüft am 2026-09-28: die migrierten Daten und Medien sind vollständig,
    `etl verify` meldet 0 Abweichungen.
- Die Grant-Tabellen der Legacy-MySQL stammten nach dem Dump-Import vom Altserver; `root/legacy` ist
  per Grant-Reset wiederhergestellt. Rezept und Import ohne `mysql`-Schema: `app/stack/README.md`.

---

## 3. Git-Topologie

Remote: `origin = git@github.com:lngr/cichlids.com.git`. Ein Abgleich mit dem Remote ist aus dem
Devcontainer heraus nicht möglich, weil die SSH-Host-Key-Prüfung fehlschlägt. Der Operator pusht
vom eigenen Host.

| Branch | Worktree | Stand |
|---|---|---|
| `main` | `/workspaces/cichlids.com` | Mehrere Commits vor `origin/main`, Push durch den Operator ausstehend |
| `feat/legacy-mvp` | `/workspaces/cichlids-mvp` | 127 Commits über der Merge-Base `c8ad766`; die obersten 45 (2026-09-28 und 29) lokal, Push durch den Operator ausstehend. Lokale Sicherung des Stands vor dem Aufräumen der History: Branch `backup/pre-fold-2026-09-29` (nicht pushen) |
| `backup/pre-secret-scrub` | keiner | Historienstand vor dem Entfernen von AWS- und Auth0-Secrets. Er enthält vermutlich genau diese Secrets und wird nicht gepusht. |

**Details zu den Commits:**

- `fa6e35c` auf `main` und `e8cfea9` auf `feat/legacy-mvp` haben dieselbe Devcontainer-Änderung.
  `fa6e35c` enthält zusätzlich Änderungen an `postCreateCommand.sh` und `postStartCommand.sh`
  (unter anderem `core.hooksPath`). Das Autor-Datum von `fa6e35c` ist 2026-07-08, das Commit-Datum
  2026-09-28.
- `git merge-tree` meldet beim Mergen von `feat/legacy-mvp` in `main` keine Konflikte. Der Branch
  ändert `app/`, `backlog/` (TASK-3, DRAFT-1, DRAFT-2), `docs/adr/` (0019 bis 0021),
  `.devcontainer/`, `.gitignore` und `tools/check-no-plaintext-secrets.py`. `infra/` und die
  `task-2.*`-Dateien fasst er nicht an.
- Draft-IDs werden nur auf `feat/legacy-mvp` vergeben, bis der Branch gemergt ist. Ein Draft auf
  `main` bekäme sonst eine ID, die beim Merge kollidiert.

**Untracked auf `main`:** `HANDOFF.md` (siehe F4) und `.claude/settings.local.json` (lokale
Einstellungen).

**Nie committet (per `.gitignore`):** die Open-Design-Wegwerfartefakte (`*.artifact.json`,
`mpz*-image.png`, `.od-skills/`).

### Zeitlinie

| Datum | Ereignis |
|---|---|
| 2026-06-02 | Devcontainer, Datenrettung aus dem Host-Backup |
| 2026-06-03 | ADRs 0001 bis 0015, Backlog.md mit CI-Gating |
| 2026-06-04 | Produktvision, Design-System und Prototypen, Epic TASK-2, Toolchain, erster OpenTofu-Tier |
| 2026-06-05 bis 06-09 | State-Backend (ADR-0016), Secret-Scan, Pre-Push-Hook, Projekttrennung (ADR-0017), Live-Deployment auf Hetzner. Letzter Infra-Commit: `c8ad766`. |
| danach bis 2026-07-08 | Uncommittete Durchsicht der Tasks 2.x (Abschluss-Durchgang, siehe 5.6) |
| 2026-07-08 18:42 | `HANDOFF.md` für einen Maschinenwechsel geschrieben |
| 2026-07-08 20:50 bis 07-09 16:48 | Branch `feat/legacy-mvp`: Epic TASK-3, 79 Commits |
| 2026-09-28 | Diese Bestandsaufnahme; Scanner-Fix auf `feat/legacy-mvp`, Branch gepusht; Stand von `main` committet |
| 2026-09-28 | Autonome Umsetzung auf `feat/legacy-mvp`: Stack lauffähig, Doku-Fäden F17 bis F19, Forum-ETL-Fix, Stories 3.15, 3.16, 3.21, 3.22 in Review |

---

## 4. Die zwei Arbeitsstränge im Überblick

| | Strang A: Plattform (`main`) | Strang B: Legacy-Parity-MVP (`feat/legacy-mvp`) |
|---|---|---|
| Epic | TASK-2 "Foundation and strict GitOps platform" | TASK-3 "Legacy parity MVP on local stack" |
| Ort | Hetzner Cloud (live) | Lokaler Compose-Stack |
| Ergebnis | Server, Volume, Buckets, DNS-Zone und unveränderliche Backups per OpenTofu, mit Schutzschichten | API, ETL, Medien-Pipeline, Keycloak und Expo-App mit vollständig migrierten Altdaten |
| Backlog | 8 Done, 4 In Progress, 10 To Do | 22 In Review, 4 To Do, 1 Draft, 0 Done |
| Nächster Bau-Schritt | TASK-2.4: k3s und Argo CD | TASK-3.15: Auth0-Import nach Keycloak |

Wie die Stränge zusammenhängen, steht in Abschnitt 7.

---

## 5. Strang A: Plattform und strikte GitOps (`main`)

### 5.1 Zielarchitektur: zwei Hetzner-Projekte

Die Begründung steht in ADR-0017, ergänzt durch ADR-0014, ADR-0016, ADR-0013, ADR-0009 und
ADR-0010. Ein Credential des einen Projekts kann im anderen nichts ausrichten.

- **`cichlids`** enthält die gesamte live betriebene, wiederaufbaubare Infrastruktur und den eigenen
  OpenTofu-State-Bucket. CI/GitOps verwaltet das Projekt per OpenTofu. `prevent_destroy` und
  `delete_protection` schützen es gegen Unfälle. Es hat das **einzige** Cloud-Token.
  - Code in `infra/platform/`: Node, DB-Volume, Media-Master-Bucket, DNS-Zone.
  - Später kommt `infra/app/` hinzu (k3s, Argo CD; noch nicht gebaut).
- **`cichlids-backup`** enthält ausschließlich die unveränderlichen Backups (Object Lock
  Compliance) und den eigenen State-Bucket.
  - Das Projekt hat kein Cloud-Token.
  - Sein S3-Key gehört nur dem Operator und ist nie ein CI-Secret.
  - Code in `infra/backup/`; der Operator wendet ihn out-of-band an.

Nur die unveränderlichen Backups sind unersetzlich. Alles in `cichlids` lässt sich aus ihnen
wiederherstellen, deshalb darf CI es frei verwalten.

### 5.2 Live-Ressourcen (Region nbg1)

| Projekt | Ressource | Details |
|---|---|---|
| cichlids | `hcloud_server.node` | `cichlids-node`, CX33, IPv4 91.99.55.7, Delete- und Rebuild-Protection, `prevent_destroy` |
| cichlids | `hcloud_volume.database` | `cichlids-db`, 10 GB, Delete-Protection, `prevent_destroy`, angehängt |
| cichlids | `hcloud_ssh_key.operator` | `cichlids-operator` |
| cichlids | `aws_s3_bucket.media_master` | `cichlids-media-master`, Object Lock Governance, Versionierung |
| cichlids | `hcloud_zone.primary` | Zone `cichlids.com` (4 Records), Delete-Protection, `prevent_destroy` |
| cichlids | State | `cichlids-platform-tfstate`, Key `platform/terraform.tfstate`, verschlüsselt |
| cichlids-backup | `aws_s3_bucket.database_backup` | `cichlids-db-backup`, Object Lock Compliance |
| cichlids-backup | `aws_s3_bucket.media_master_backup` | `cichlids-media-backup`, Object Lock Compliance |
| cichlids-backup | State | `cichlids-backup-tfstate`, Key `backup/terraform.tfstate`, verschlüsselt |

- Beide State-Buckets sind versioniert. Der State ist nativ verschlüsselt (pbkdf2, aes_gcm).
- Provider-Pins: hcloud `~> 1.64` (aufgelöst 1.65.0), aws `~> 6.0` (aufgelöst 6.49.0), OpenTofu
  `>= 1.12.0`.
- Auf dem Node läuft noch keine Anwendung. Die Ressourcen kosten trotzdem Geld;
  `PLAN.md` schätzt die Startphase auf 15 bis 25 Euro im Monat.
- Ob die Domain `cichlids.com` beim Registrar an die Hetzner-Nameserver delegiert ist, ist offen.
  Die Nameserver liefert `infra/tofu.sh platform output dns_zone_nameservers`.
- `cichlids-db-backup` enthält das Beweisobjekt `layer4-immutability-test.txt`. Seine Compliance-Frist
  lief bis ca. 2026-06-23, das Objekt ist also löschbar.

### 5.3 Schutzschichten gegen destruktive Änderungen (ADR-0014)

| Schicht | Mechanismus | Nachweis am Live-System |
|---|---|---|
| 1 | `prevent_destroy` und Delete-Protection bei Hetzner | Erbracht: `plan -destroy` wird abgelehnt, die Konsole verweigert das Löschen |
| 2 | Projekttrennung, der `cichlids`-Key erreicht `cichlids-backup` nicht | Erbracht mit `tools/verify-project-isolation.sh` |
| 3 | Fail-closed Plan-Diff-Gate (`policy/destroy_gate.rego`, Conftest) auf einem echten CI-Plan | **Offen**: Policy und Unit-Tests existieren, eine CI-Apply-Pipeline gibt es noch nicht (TASK-2.4, 2.11) |
| 4 | Object Lock Compliance auf den Backups | Erbracht mit `tools/verify-backup-immutability.sh` |

### 5.4 Werkzeuge und Credentials

- `infra/tofu.sh <platform|backup> <tofu-args>` liest `~/.config/cichlids/cichlids.env` und setzt je
  Tier die passenden Variablen:
  - `platform`: Cloud-Token, `cichlids`-S3-Key und SSH-Key
  - `backup`: nur den Backup-S3-Key
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
- Ein Pre-Push-Hook in `.githooks/` scannt nach Klartext-Secrets. Er ist nur aktiv, wenn
  `core.hooksPath` gesetzt ist; `postStartCommand.sh` setzt ihn. Nicht umgehbar ist der
  Secrets-Scan in CI.

### 5.5 Backlog-Stand TASK-2 (Milestone m-0)

| Task | Titel (gekürzt) | Status |
|---|---|---|
| 2.1 | Reproducible toolchain | Done (in `backlog/completed/`) |
| 2.2 | OpenTofu foundation tier with safeguards | Done (in `backlog/completed/`) |
| 2.3 | Bootstrap runbook and credential tiers | Done |
| 2.4 | OpenTofu app tier, k3s, Argo CD bootstrap | To Do |
| 2.5 | Fail-closed Conftest plan-diff gate | In Progress (bewusst offen bis 2.4) |
| 2.6 | SOPS, age, KSOPS | To Do |
| 2.7 | Argo CD app-of-apps, Environments | To Do |
| 2.8 | Ingress, TLS, Platzhalter-Workload | To Do |
| 2.9 | CloudNativePG mit PITR | To Do |
| 2.10 | Erste getestete Restore-Übung | To Do |
| 2.11 | CI-Grün-Gate und Delivery-Pipeline | To Do |
| 2.12 | Branch-Protection auf `main` | To Do |
| 2.13 | Remote State Backend | Done |
| 2.14 | Toolchain-Pins aktualisiert | Done |
| 2.15 | Pre-Push-Hook | Done |
| 2.16 | Drei Hetzner-Projekte | Done (abgelöst durch 2.22) |
| 2.17 | Environment-Daten (anonymisierter Snapshot, CoW-Klon, Medien-Overlay) | To Do |
| 2.18 | DNS auf Hetzner Cloud DNS, Provider-Pins | In Progress: AC #2, #4 und #5 abgehakt, #1 und #3 offen |
| 2.19 | Renovate | To Do |
| 2.20 | State nach Vertrauensgrenze trennen | Done (abgelöst durch 2.22) |
| 2.21 | Schutzschichten live verifizieren | In Progress: AC #1, #2 und #4 abgehakt, #3 wartet auf 2.4 |
| 2.22 | Zwei Projekte | In Progress |
| TASK-1 | Erster grüner E2E-Smoke-Pfad | To Do |

### 5.6 Offener Abschluss-Durchgang

In einem Abschluss-Durchgang werden die In-Progress-Tasks nacheinander abgenommen. Die
Akzeptanzkriterien werden dabei in **Ground-Truth-Form** gebracht: Der Operator prüft sie am echten
System, also an der Hetzner-Konsole, mit echten Tools gegen die Realität oder durch ein beobachtetes
Ablehnungsverhalten. Ein Skript des Agents, das "OK" ausgibt, zählt nicht. Ein AC wird erst
abgehakt, wenn der Operator es selbst beobachtet hat.

**TASK-2.18 abschließen** (nur lesende Prüfungen):

```sh
# AC #1: Provider sind hcloud und aws, kein germanbrew
grep 'source ' infra/platform/versions.tf
grep -rn germanbrew infra/
# plus: CI-Job "OpenTofu tiers validate" war auf c8ad766 grün

# AC #3: Destroy-Gate lehnt das Löschen der DNS-Zone ab
echo '{"resource_changes":[{"address":"hcloud_zone.primary","type":"hcloud_zone","change":{"actions":["delete"],"after":null}}]}' | conftest test - -p policy/destroy_gate.rego
# erwartet: 1 test, 0 passed, 1 failure ("critical resource hcloud_zone.primary would be destroyed")
```

Danach setzt folgender Befehl die Häkchen und den Status:

```sh
backlog task edit TASK-2.18 --check-ac 1 --check-ac 3 --check-dod 1 --check-dod 2 --check-dod 3 -s Done
```

Anschließend den Status prüfen und `node tools/check-story-test-binding.mjs` laufen lassen.

**TASK-2.22 abschließen:** Die ACs in Ground-Truth-Form bringen und am echten System prüfen:

- Genau zwei Projekte existieren (Hetzner-Konsole).
- Die Isolation hält (Schicht 2).
- Die Schutzmechanismen sind an den Live-Ressourcen vorhanden.
- ADRs und README beschreiben zwei Projekte.

### 5.7 Stolperfallen

- Hetzner Object Storage implementiert `PutBucketTagging` nicht (Antwort 501). Die
  `aws_s3_bucket`-Ressourcen haben deshalb keine Tags; das Policy-Gate erkennt kritische Ressourcen
  am Typ.
- Object Lock schützt geschriebene Objekte. Ein leerer Compliance-Bucket lässt sich in der Konsole
  löschen.
- `tofu providers`, `plan` und `apply` brauchen `CICHLIDS_STATE_PASSPHRASE` (native
  State-Verschlüsselung). Vorher die Umgebung laden:
  `set -a; . ~/.config/cichlids/cichlids.env; set +a`. `tofu validate -backend=false` kommt ohne
  sie aus.
- Git führt committete Hooks nie automatisch aus. Nach einem frischen Clone ist einmal
  `git config core.hooksPath .githooks` nötig.
- DNS läuft projektbezogen über den `hcloud`-Provider (`hcloud_zone`, `hcloud_zone_rrset`).
- Backlog-CLI:
  - Wird `-s Done` zusammen mit einem langen `--final-summary` gesetzt, wird es manchmal verworfen.
    Den Status deshalb einzeln setzen und prüfen.
  - ACs umformulieren: erst `--remove-ac <n>` (absteigend), dann `--ac "..."`.
  - Auf dem Host läuft die CLI per `npx -y backlog.md@1.45.2`.

---

## 6. Strang B: Legacy-Parity-MVP (`feat/legacy-mvp`)

### 6.1 Ziel laut Epic TASK-3

> Rebuild the complete legacy feature set on the new stack (ASP.NET Core 10 API, Postgres,
> Keycloak, RustFS object store, Expo client) with the legacy data migrated, usable end to end on
> the local compose stack.

- **Scope:**
  - relationales Domänenschema mit `legacy_id`-Brücke
  - idempotente ETL
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
| `Cichlids.Etl` | Konsolen-ETL (MySqlConnector) mit den Kommandos `migrate`, `all`, `verify` und `media` |
| Tests | `Api.Tests`, `Infrastructure.Tests` und `Etl.Tests`: xunit, Testcontainers (postgres:17, mysql:5.7) |
| `app/client-core/` | Aus `openapi.json` generierter TypeScript-Client (openapi-typescript, openapi-fetch), vitest, msw |
| `app/mobile/` | Expo ~57, expo-router, React Native 0.86, React 19. Screens für Galerie, Bilddetail, Becken, Profil, Community und Arten. i18n mit Englisch als Standard, Theme aus den Design-Tokens. E2E mit Maestro (`e2e/maestro`) und Playwright (`e2e/playwright`). |
| `app/stack/` | `compose.yaml` (postgres:17, RustFS als S3, Keycloak 26.6), deklarativer Realm `keycloak/cichlids-realm.json`, `bootstrap.sh`, `README.md` |
| Workspace | pnpm (`app/pnpm-workspace.yaml`) |

### 6.3 Neue ADRs auf dem Branch (alle Accepted)

- ADR-0019 Persistence and Migration Tooling
- ADR-0020 Relational Domain Schema
- ADR-0021 Community Discussion Archive

### 6.4 Ergebnisse der Datenmigration (laut Task-Notizen)

- **ETL-Verify** rechnet die Erwartungswerte unabhängig aus der Legacy-Quelle nach. Ergebnis:
  34 Prüfungen, 0 Abweichungen.
- **Medien-Seed:**
  - 179.337 von 179.340 Medien im Object Store (3 Quelldateien fehlen)
  - 623.220 Varianten (200/400/800/1600 px, ohne Hochskalieren)
  - 283 GB in 94 Minuten übertragen
  - Neustarts sind idempotent
- **934 Altdateien** lassen sich nicht dekodieren. Sie werden nur als Original ausgeliefert.
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

Kein Task ist Done, kein AC ist abgehakt (Stand 2026-09-28).

| Status | Tasks |
|---|---|
| In Review (26) | 3.1 Schema; 3.2 bis 3.9 ETL (Arten, User, Becken, Bilder, Kommentare/Ratings, Galerien, Verify, Medien); 3.10 bis 3.14 (Pictures-, Tanks-, Species-API, Keycloak, Comments/Ratings-API); 3.15 Auth0-Import nach Keycloak; 3.16 Foto-Upload mit Varianten; 3.17 Outbox; 3.18 TS-Client; 3.19, 3.20 und 3.27 Mobile-Screens; 3.21 Mobile Login und Upload; 3.22 Mobile Kommentieren und Bewerten; 3.23 Bootstrap-Smoke; 3.24 Forum-ETL; 3.25 Community-API; 3.26 Legacy-Redirects |
| Draft (5) | DRAFT-1 Mail-Kampagne zur Rückgewinnung alter Mitglieder (Mail-Provider für ca. 12k Mails und rechtliche Prüfung offen); DRAFT-2 gitleaks statt Eigenbau-Scanner; DRAFT-3 gespeicherter Link zwischen Bewertung und Kommentar; DRAFT-4 wie oft ein Mitglied dasselbe Ziel bewerten darf; DRAFT-5 View-Zählung durch Smokes in der geseedeten Dev-DB |

- 3.15 importiert jedes Legacy-Konto mit bekannter E-Mail (ca. 12.050 Konten) mit den Required
  Actions "update password" und "terms", idempotent über die E-Mail; Grundsätze in ADR-0022. Der
  Import ist nicht gegen den Dev-Stack gelaufen; die Prüfbefehle stehen in den Task-Notizen.
- 3.16, 3.21 und 3.22 liefern Upload, Login (PKCE) und Kommentieren mit Bewertung. Schreibende
  E2E-Tests laufen auf einem isolierten Schreib-Stack (`app/mobile/e2e/write-stack.sh`: DB
  `cichlids_e2e`, Bucket `cichlids-e2e`, API :5046, Web :8082), nie gegen die geseedete Dev-DB.
- Die Maestro-Flows der ACs von 3.21 und 3.22 sind geschrieben, aber nicht ausführbar (F13).
- Eine Vorprüfung aller 22 älteren In-Review-Tasks liegt als `PRECHECK-IN-REVIEW.md` (untracked) im
  Haupt-Worktree `/workspaces/cichlids.com`.

### 6.6 Lokal starten

Die Anleitung steht in `app/stack/README.md` im Worktree `/workspaces/cichlids-mvp`.

1. Die Legacy-MySQL muss laufen und den importierten Dump enthalten. `bootstrap.sh` startet sie bei
   Bedarf, importiert den Dump aber nicht.
2. `app/stack/bootstrap.sh` ausführen. Das Skript ist idempotent: Stack hoch, `etl migrate`,
   `etl all`, `etl verify`, `etl media` (Standard `--limit 3000`), Realm-Import.
   - `CICHLIDS_MEDIA_LIMIT=0` lädt alle Medien.
   - `CICHLIDS_LEGACY_STACK_DIR` zeigt standardmäßig auf
     `/workspaces/cichlids.com/workspace-legacy/legacy-stack`.
3. API starten: `dotnet run` in `Cichlids.Api` (http://0.0.0.0:5045).
4. App starten: `pnpm exec expo start --web --port 8081`. Web-Smoke: `pnpm run e2e:web`; schreibende
   E2E-Specs: `pnpm run e2e:write`.

Das .NET-SDK installiert der Devcontainer bei der Erstellung über `tools/install-dotnet.sh` nach
`~/.dotnet`, dazu pnpm-Abhängigkeiten und Playwright-Chromium.

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

- Die Maestro-Flows liefen nie, weil der Runner beim Device-Attach blockiert. 3.23 hat deshalb eine
  offene Lücke zum AC; als Ersatz gibt es nur den Playwright-Web-Smoke.
- Für 3.1 stehen der Merge nach `main` und ein E2E-Lauf auf einem deployten Stack aus. Beides
  braucht die Delivery-Pipeline aus TASK-2.
- Der Dev-Realm von Keycloak ist per kcadm angepasst (cichlids-app: webOrigins 8081 und 8082,
  Redirect `cichlids://*`). Den `email_verified`-Mapper ergänzt der Account-Import beim ersten Lauf.
- Jeder Aufruf eines Bilddetails zählt einen View, auch im Web-Smoke (DRAFT-5).
- In `app/` gibt es keine TODO- oder FIXME-Marker und keine übersprungenen Tests.

---

## 7. Wie hängen die beiden Stränge zusammen?

Eine schriftliche Begründung für Strang B existiert nicht. Was sich belegen lässt:

1. **Strang A stand vor dem Schritt, der eine Anwendung braucht.** Das Cloud-Substrat ist live und
   abgesichert. Der nächste Bau-Schritt 2.4 (k3s und Argo CD) und das offene Gate aus Schicht 3
   brauchen einen Workload und eine Delivery-Pipeline. Eine deploybare App gab es nicht.
2. **Strang B entstand direkt nach dem Maschinenwechsel.** `HANDOFF.md` wurde am 2026-07-08 um
   18:42 geschrieben. Um 20:50 desselben Abends begann `feat/legacy-mvp` in einem eigenen Worktree.
3. **TASK-3 grenzt sich ausdrücklich von der Plattform ab:** "Production deployment (the GitOps app
   tier is separate work)" steht unter Out of scope. Beide Stränge waren also bewusst entkoppelt.
4. **Strang B zieht Teile späterer Phasen lokal vor:**
   - Keycloak (Phase 2)
   - Outbox und Webhooks (Phase 3)
   - Medien-Pipeline und ETL (Phase 4)
   - Becken und Arten (Phase 5, in Legacy-Form)
5. **Strang B zielt auf Legacy-Parität,** also die Funktionen der alten Plattform mit den alten
   Daten. Die Produktvision beschreibt dagegen die Pflege- und Stolz-Sicht als Kern. Keine der
   Vision-Funktionen (Pflege-Logs, Timeline, Streaks) ist in TASK-3 enthalten.
6. **DRAFT-1 und die Neuausrichtung von 3.15** zielen auf einen Relaunch für die rund 12k
   Bestandsmitglieder.

**Naheliegende Lesart** (unbestätigt, vom Projektinhaber zu klären): Die Plattform war so weit, dass
sie eine echte Anwendung brauchte. Strang B sollte deshalb schnell eine lauffähige Anwendung mit den
echten Altdaten liefern. Zugleich sollte er die Migration beweisen und eine Relaunch-Basis für die
Rückgewinnung der Bestandsmitglieder schaffen. Die Vision-Funktionen bauen später darauf auf.

**Offene Frage O1:** Ist diese Lesart richtig, und wie verhält sich der Legacy-Parity-MVP zur
Produktvision? Denkbar sind zwei Rollen:

- ein Relaunch-Zwischenschritt,
- eine reine Migrations- und Datenbasis.

Die Antwort bestimmt, ob als Nächstes Strang A (Deployment) oder Strang B (Login, Upload,
Kampagne) Vorrang hat.

---

## 8. Design-Artefakte

- Das Design-System liegt in `design-system/` (`tokens.css`, `tokens.json`, `README.md`) und in
  `cichlids-design-system.html`.
  - Light-Modus: Palette "Aquatisches Teal"
  - Dark-Modus: "Biotop/Naturstein" mit Pflanzengrün
- Prototypen: `cichlids-tank-prototype.html` und `cichlids-tank-detail-hifi.html`. Sie zeigen das
  Becken-Detail aus der Produktvision, also die Signature-Screens der Pflege- und Stolz-Sicht.
- Plattformen: Android, iOS und Web gleichwertig und zum Launch gleichzeitig; entwickelt wird
  zuerst gegen Android.
- Die MVP-App in Strang B leitet ihr Theme aus diesen Tokens ab. Screenshots liegen in
  `/workspaces/app-screens/`.

---

## 9. Offene Fäden

Die IDs sind stabil, damit sich die Fäden einzeln abarbeiten und abhaken lassen.

### Sicherung (dringend)

- [x] **F1** Den uncommitteten Stand auf `main` committen (Doku-Bereinigung, Tooling, Backlog, dieses
  Dokument). Der Push von `main` ist Sache des Operators.
- [x] **F2** `feat/legacy-mvp` zu `origin` pushen.
- [x] **F20** Die Treffer des Secret-Scanners auf `feat/legacy-mvp` beheben. Keiner der Treffer ist
  ein echtes Secret:
  - Fehlalarme bei Zuweisungen von Bezeichner zu Bezeichner, zum Beispiel die Property
    `CancellationToken` mit der Variablen `cancellationToken` als Wert. Die Heuristik "credential
    assignment" wertet jeden Bezeichner ab 16 Zeichen auf der rechten Seite als Wert.
  - Bewusst lokale Dev- und Testwerte: `cichlids-dev-secret` (RustFS in `app/stack/compose.yaml`
    und in den Test-Fixtures), `cichlids-local-dev-slug-secret` (`EtlContext.cs`) und
    `matching-subscriber-secret` (`OutboxDispatchServiceTests.cs`).

  Die Korrektur ist auf `feat/legacy-mvp` committet und gepusht:
  - Der Scanner wertet unquotierte Bezeichner nicht als Wert und akzeptiert Zeilen mit dem
    Marker `gitleaks:allow`. Der Self-Test deckt beides ab.
  - Die sieben Zeilen mit Dev- und Testwerten haben den Marker.
  - Hook und Scan laufen grün.

  Der Ersatz des Eigenbaus durch gitleaks steht als TASK-2.26 im Backlog.
- [ ] **F3** Den SSH-Zugriff auf GitHub aus dem Devcontainer reparieren (Host-Key-Prüfung), damit
  der Remote-Stand prüfbar ist.
- [x] **F4** `HANDOFF.md` gelöscht; dieses Dokument deckt ihren Inhalt ab.

### Strang A

- [ ] **F5** TASK-2.18 abschließen (AC #1 und #3, siehe 5.6).
- [ ] **F6** TASK-2.22 abschließen (siehe 5.6).
- [ ] **F7** Die Delegation der Domain `cichlids.com` beim Registrar prüfen oder durchführen.
- [ ] **F8** Den Umgang mit dem Beweisobjekt `layer4-immutability-test.txt` klären: Die Frist ist
  abgelaufen, es lässt sich also löschen oder behalten.
- [ ] **F9** Die laufenden Kosten der Live-Ressourcen gegen den Nutzen abwägen, solange kein
  Workload läuft.
- [ ] **F10** TASK-2.4 (k3s und Argo CD) als nächsten Bau-Schritt planen. Damit werden 2.5 und
  2.21 AC #3 abschließbar.

### Strang B

- [x] **F11** Docker-Volumes des MVP-Stacks geprüft und Stack gestartet: Daten vollständig,
  `bootstrap.sh` grün, `etl verify` 0 Abweichungen, Playwright-Web-Smoke grün. Das .NET-SDK
  installiert der Devcontainer über `tools/install-dotnet.sh`.
- [ ] **F12** Die MVP-Stories in Review später abnehmen (bewusst zurückgestellt, siehe O2). Prüfbefehle je
  Story: `/workspaces/cichlids.com/PRECHECK-IN-REVIEW.md` (untracked) und die Notizen von 3.15, 3.16, 3.21
  und 3.22.
- [ ] **F13** Den Maestro-Runner zum Laufen bringen oder den AC von 3.23 bewusst anpassen.
- [ ] **F14** `feat/legacy-mvp` nach `main` mergen (macht der Operator; technisch konfliktfrei). Danach den
  lokalen Sicherungs-Branch `backup/pre-fold-2026-09-29` löschen.
- [x] **F15** Die MVP-Stories 3.15, 3.16, 3.21 und 3.22 sind umgesetzt und stehen in Review.
  DRAFT-1 (Mail-Kampagne) ist offen.
- [ ] **F21** Die ACs von 3.15 am Dev-Stack beobachten. Der Import lief am 2026-09-29: 12.050 Konten
  angelegt, 6.447 Profil-Verknüpfungen, 20 Datensätze ohne Kontakt und Profil verworfen; der zweite
  Lauf legte nichts an.
- [x] **F22** Die Interims-Entscheidungen der autonomen Umsetzung sind am 2026-09-29 mit dem Projektinhaber
  durchgesehen; die Ergebnisse stehen in ADR-0022, ADR-0023, den Task-Notizen und DRAFT-6 bis DRAFT-10.
- [ ] **F23** Über DRAFT-3 (gespeicherter Link Bewertung zu Kommentar), DRAFT-4 (wiederholte
  Bewertungen) und DRAFT-5 (View-Zählung in der geseedeten Dev-DB) entscheiden.
- [x] **F24** Die Android-Package-ID `com.cichlids.app` ist bestätigt.
- [x] **F27** Die Befunde des Reviews des gemergten MVP (`REVIEW-MVP.md`, R1 bis R46) sind einzeln
  besprochen und entschieden. Die Webhook-Entscheidung steht in ADR-0024.
- [ ] **F28** Die Entscheidungen aus `REVIEW-MVP.md` umsetzen. Sie stehen als Tasks im Backlog
  (Zuordnung im Abschnitt "Umsetzung" von `REVIEW-MVP.md`); die neun Tasks mit Priorität hoch sind
  als Abhängigkeiten von TASK-2.4 eingetragen. Reihenfolge (entschieden am 2026-10-12): zuerst die
  für Staging wichtigen Fixes (Sicherheit, Datenintegrität des Imports, Stabilität der API,
  CI-Gate), dann TASK-2.4, danach die übrigen Punkte, die dauerhafte Anmeldung (R8) und der
  externe Webhook-Forwarder (ADR-0024).
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
- [x] **O2** Priorität: zuerst das Deployment (Strang A). Der MVP wird nach `main` gemergt und dient als
  Testobjekt für ein Staging-System; seine Stories bleiben bewusst unabgenommen (In Review).
- [x] **O3** ADR-0015 (Backlog.md) ist angenommen.

---

## 10. Vorgeschlagene Reihenfolge für den Wiedereinstieg

1. **Sichern:** F1 bis F4. Das kostet wenig und verhindert Datenverlust.
2. **Klären:** O1 und O2. Davon hängt alles Weitere ab.
3. **Strang A sauber abschließen:** F5 und F6 sind je nur wenige Befehle. Danach sind nur noch die
   Tasks offen, die bewusst auf 2.4 warten.
4. **Strang B sichtbar machen:** F11. Den Stack hochfahren und die App ansehen. Das hilft auch bei
   der Entscheidung zu O1.
5. **Doku-Korrekturen:** F16 bis F19.
6. **Je nach O2:**
   - *Deployment zuerst:* F10 (TASK-2.4), dann 2.6 bis 2.11. Der MVP ist dann der erste echte
     Workload auf k3s.
   - *MVP zuerst:* F12 bis F15. Das Deployment folgt danach.

---

## 11. Arbeitsregeln (Kurzfassung)

Die vollständigen Regeln stehen in `AGENTS.md`.

- Agents committen, pushen und mergen nie. Der Operator macht alle Git-Operationen. Commit-Messages
  folgen Conventional Commits, ohne Co-Authored-By-Trailer.
- Der Backlog wird über die `backlog`-CLI gepflegt, und ACs werden in Ground-Truth-Form
  formuliert. Ein AC wird erst abgehakt, wenn der Operator es beobachtet hat.
- OpenTofu-Applies macht der Operator auf dem eigenen Host.
- Für Doku und Kommentare gilt: nur der Ist-Zustand und zeitlose Begründungen, keine Em- oder
  En-Dashes.
- READMEs sind reine Anleitungen. Begründungen stehen in den ADRs.
- Neue Artefakte (Code, ADRs) werden auf Englisch geschrieben, Planungsdokumente auf Deutsch.

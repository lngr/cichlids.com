# cichlids.com — Vogelperspektiven-Plan (Transformation Legacy → moderner Stack)

> Entwickler-Planungsdokument (Roadmap & Sequenzierung). Die **verbindlichen Entscheidungen**
> stehen in den ADRs unter [`docs/adr/`](docs/adr/index.md); der **Ist-Zustand der Legacy-Daten** in
> [`PROJECT-CONTEXT.md`](PROJECT-CONTEXT.md). Dieses Dokument verbindet beides zu einem
> Umsetzungspfad. Sprache bewusst Deutsch (internes Planungsdoc); Code und ADRs sind Englisch.

---

## 1. Zielbild & Architektur-Überblick

Eine **mobile-first Medien-Sharing-Community** (Fotos **und** Videos), in der das eigene
**Becken (Tank)** im Zentrum steht und Mitglieder **Anerkennung** für ihre Pflegeleistung
bekommen (ADR-0001). Video ist von Anfang an erstklassig, nicht nachgelagert. Technisch:
**API-first .NET-Backend**, **event-driven** als Pflicht, **globale, kostengünstige
Medienauslieferung** (Bild-Varianten + adaptive Video-Renditions) über ein Edge-CDN,
**KI-Agenten als entkoppelter Dienst**, alles **GitOps**.

```
   BILDPFAD               ┌──────────────── Cloudflare ─────────────────┐
   Mobile (Expo/RN)    ┌──┤  CDN (globale PoPs) ──► R2 (nur Bilder)      │
   Web (RN-Web) ───────┤  │  immutable: Bild-Varianten                   │
        │  ▲ Bild-URLs │  └─────────────────────────────────────────────┘
        │  │           │       ▲ Upload + Variantenerz.
        │ JSON/OpenAPI │       │
        │  │ Video-URLs│  ┌────┴───────────────────┐   VIDEOPFAD (separat, ADR-0012)
        │  └───────────┼─►│ Video-Hosting separat  │◄── async Transcode (ffmpeg→HLS
        ▼              │  │ adaptive HLS + Video-CDN│      o. managed Provider)
   ┌───────────────────┴──┴────────────────────────┘   ┌──────────────────────┐
   │  .NET API (ASP.NET Core, US-Region)                │   │  KI-Agenten (Python) │
   │  - verbindliche Domänenlogik    - OpenAPI-Vertrag  │   │  - API-Client        │
   │  - Transactional Outbox ──── Webhooks (Events) ───────►│  - Bot-Identitäten    │
   └──────┬──────────────────────┬──────────────────────┘   │  - reagieren auf      │
          ▼                      ▼                           │    Domain-Events      │
     ┌──────────┐         ┌──────────────┐                  └──────────────────────┘
     │ Postgres │         │  Keycloak    │  (Auth0-Migration)
     └──────────┘         └──────────────┘
          ▲                      ▲
          └───── Broker/Queue ────┘  (Feed, Reputation, Notifications, Agenten)
```

Leitprinzipien: **OpenAPI als einziger Vertrag** (App, Web, Agenten); **Domänenlogik nur
serverseitig**; **Medien immutable + content-adressiert** — **Bilder** über R2+Cloudflare,
**Video separat gehostet** (adaptive HLS, ADR-0012), um das Bild-/Account-Risiko zu isolieren;
**Events als Rückgrat** für jede nachgelagerte Wirkung; **Hosting-Provider entkapselt**
(S3-API/Standard-HTTP, jederzeit swapbar); **Backup/Recovery von Tag 1** (PITR, getestete
Restores) (ADR-0002, 0008, 0009, 0010, 0011, 0012, 0013).

---

## 2. Domänenmodell (Kernentitäten)

Becken in den Mittelpunkt, Profil eng daran gekoppelt, Anerkennung als Querschnitt.

```
Profile ──< Tank ──< TankPost (Story | Single) ──< MediaItem (Foto | Video)
   │          │                                       │
   │          └──< Inhabitant ──> Species             │
   │                                                   │
   └──< Rating / Comment / Follow / Collection ───────┘
                  │
                  └──► Reputation (serverseitig berechnet)
```

- **Tank (Becken):** erstklassig; trägt Posts (Fotos/Videos), Besatz (Arten), Historie.
- **TankPost:** vereinheitlicht *Einzel-Post* und mehrteilige *Story*; ein Post enthält ein
  oder mehrere **MediaItems**.
- **MediaItem:** Foto **oder** Video; Foto → Bild-Varianten in R2+Cloudflare (ADR-0009),
  Video → adaptive HLS-Renditions im **separaten Video-Hosting** (ADR-0012); beide immutable.
- **Reputation:** abgeleitete, serverseitig berechnete Größe aus Ratings/Views/Badges.
- **Species:** Taxonomie (829 Arten vorhanden) für Besatz & Discovery.
- **legacy_id-Brücke:** jede migrierte Entität trägt ihren Legacy-Schlüssel für idempotente,
  wiederholbare ETL-Läufe und spätere Nachvollziehbarkeit.

Das neue Schema wird gegenüber Legacy neu entworfen (ADR-0002); `user_cichlids_*` ist die
Quelle, nicht die Vorlage.

---

## 3. ETL-Migration (Legacy-MySQL → Postgres) + Bild-Migration

**Datenquelle:** `cichlids_typo3` (MySQL 5.7, utf8mb4) — Bilder 189.571 (180.455 aktiv),
Kommentare 1,6 Mio., Tanks 6.729, User 11.948, Arten 829. Bild-Originale ~235 GB unter
`/workspaces/legacy-data/images/userpics/`.

1. **Schema-Mapping** Legacy → neues Modell festschreiben (Tab „Migration" im HTML-Report als
   Referenz). Jede Zieltabelle bekommt `legacy_id`.
2. **Idempotenter ETL** (wiederholbar; Upsert auf `legacy_id`): User → Profile, Tanks,
   Pictures → MediaItem/TankPost, Comments, Ratings, Species + Verknüpfungen
   (`species_pictures_mm`). Hinweis: Der Legacy-Bestand ist **reine Fotos** — kein Video zu
   migrieren; Video ist eine reine Vorwärts-Funktion (ADR-0012).
3. **Datenhygiene zuerst** (PROJECT-CONTEXT §12): Spam/Test-Accounts, gelöschte/discardete
   Inhalte, Moderationsstatus filtern.
4. **Bild-Migration → R2:** Originale unter stabilem Key (`user_pics/<uid>/<datei>`), Unicode/
   `%20`-Dateinamen normalisieren. Beim Ingest **feste Varianten** (thumb/medium/full) erzeugen,
   immutable, langlebiger `Cache-Control` (ADR-0009). Verwaiste Dateien (v.a.
   `api_uploads/chunks`) auslassen; 5 fehlende aktive Bilder protokollieren.
5. **Verifikation:** Abgleich DB-Verweise ↔ R2-Objekte (Skript-Analog zu `verify_images.py`),
   Abweichungsliste als Artefakt.

**Cutover:** einmaliger ETL beim Deployment; bis dahin wiederholbar gegen eine Staging-DB.

---

## 4. Auth0 → Keycloak-Migration

**Quelle:** Auth0-Export (12.147 Konten, **ohne** Passwort-Hashes), 95,3 % User/Password,
2,6 % Google, 2,1 % Facebook, 99,2 % E-Mail verifiziert.

- **Zuordnung:** kein Legacy-ID-Feld → `Auth0 → fe_user → Bilder` via **E-Mail-Join** bzw.
  `fe_users_auth0` (sub→uid).
- **Passwörter:** bevorzugt **bcrypt-Hash-Export** aus Auth0 (nahtlos importierbar in
  Keycloak), sonst **Reset bei Erst-Login**.
- **Social:** Google/Facebook in Keycloak als Identity-Provider re-linken.
- **Merge:** ~75 Mehrfach-E-Mails konsolidieren (eine Identität, mehrere Legacy-Profile).
- **Bot-Identitäten:** Service-Accounts für die KI-Agenten von Beginn an anlegen (ADR-0011).

---

## 5. Globale Delivery- & Kostenarchitektur

Kernproblem: globales Publikum (90 % USA) bei großem Medienbestand und ohne Monetarisierung →
**Egress ist der Kostentreiber**. Lösung für **Bilder** (ADR-0009):
**Zero-Egress-Objektspeicher (R2) hinter Edge-CDN (Cloudflare)**. **Video** läuft bewusst über
einen **separaten Pfad** (ADR-0012) — eigenes Hosting, eigenes Kostenprofil — um Bild-
Auslieferung und Cloudflare-Account vom Video-spezifischen ToS-/Ban-Risiko zu isolieren.

| Kombination | bei ~2 TB/Mon | bei ~20 TB/Mon |
|---|---|---|
| **R2 + Cloudflare-CDN** (gewählt) | ~$5 | ~$5–8 |
| B2 + Cloudflare-CDN (Alternative) | ~$3 | ~$3 |
| S3 + CloudFront (Gegenbeispiel) | ~$93 | ~$1.567 |

- **Drei getrennte Speicher-Arten** (nicht verwechseln): **Object Storage (S3)** für Medien
  (Originale/Varianten/Video), **Block-Volume** nur für die kleine DB, **Node-Disk** (im Server
  enthalten) für OS/Container. Medien liegen **nie** auf Worker-Platten.
- **Medien-Master:** Originale auf **Hetzner Object Storage** (S3, EU, ~€7,72/TB inkl. 1 TB
  Traffic) als Quelle der Wahrheit. **Serving:** Bilder über **R2 + Cloudflare** (Egress $0,
  global); Video separat (ADR-0012).
- **Bild-Serving-Stack ~$5/Mon, traffic-unabhängig** (Egress = $0).
- **Compute:** **eine kleine Hetzner-Node (Single-Node-k3s, US-Standort), später erweiterbar**
  (ADR-0010); Postgres **in-cluster via CloudNativePG** (kein managed Neon nötig), Keycloak als
  Workload. Grobkosten Anfangsphase **~€15–25/Mon** (Node + DB-Volume + Hetzner-S3-Master + R2).
- **Video (separat, ADR-0012):** eigenes Hosting + eigene Auslieferung. Offene Wahl:
  self-hosted ffmpeg→HLS auf separatem S3-Speicher + Video-CDN **oder** dedizierter
  Video-Provider (z.B. Bunny Stream / Mux). Kostenfaktor: Transcoding-Compute bzw. Pro-Minute.
- **Provider entkapselt, nicht hart verdrahtet** (ADR-0009): Objektspeicher nur über **S3-API**,
  CDN nur über **Standard-HTTP-Caching**, Zugriff über dünne Abstraktion → Provider ist
  IaC/Config-Detail. **Backblaze B2** als Serving-Drop-in-Alternative; **Hetzner Object Storage**
  als unabhängiger Master. Grund: schneller Wechsel binnen Stunden bei (auch grundlosen) Bans.
  Keine proprietären Dienste (Cloudflare Images/Stream).
- Alle Preise Stand 2026-06; Quellen siehe Recherche-Notiz + Hetzner-Preisseiten.

---

## 6. Backup, Replikation & Disaster Recovery (ADR-0013)

Postgres-Recovery ist das kritische Risiko und erfahrungsgemäß der schwierigste Teil — daher
von Anfang an verbindlich, nicht nachgelagert.

- **RPO ≤ 5 min** via **kontinuierliches WAL-Archiving + PITR** (self-hosted: pgBackRest;
  managed/Neon: eingebautes PITR **plus** geplante logische Exporte in eigenen Speicher als
  provider-unabhängige Rückfallebene). Reines `pg_dump`-Cron genügt **nicht**.
- **3-2-1 / Off-Provider:** mind. eine verschlüsselte Backup-Kopie unabhängig vom DB-Host und
  vom Medien-Provider (andere Region/Provider) — passt zur Provider-Entkapselung.
- **Restores werden automatisch getestet** — ein Backup, das nie zurückgespielt wurde, gilt als
  kaputt.
- **Regelmäßiger prod→staging-Refresh:** Aus einem **restaurierten** Prod-Backup wird periodisch
  ein **anonymisierter „Golden"-Snapshot** erzeugt (DB + repräsentative Medien-Teilmenge), der
  **Staging und Preview** speist (ADR-0018). Das beweist den Restore-Pfad *und* liefert
  realistische Daten. Preview-Umgebungen bekommen daraus eine **ephemere, per Copy-on-Write
  geklonte DB** (Volume-Snapshot / Restore-from-Base-Backup); Medien werden **nicht je Umgebung
  kopiert**, sondern über ein **Overlay** ausgeliefert (gemeinsame Read-only-Basis + kleiner
  ephemerer Per-Preview-Schreib-Store). Ein minimaler synthetischer Seed bleibt als schnelle
  Option verfügbar, ist aber nicht der Standard.
- **Standby-Replica** für HA/Failover (getrennt von Read-Scaling-Replicas); Tag-1 sind PITR-
  Backups Pflicht, Hot-Standby der nächste Schritt.
- **Medien:** Durability über Serving-Store **+** Master-Kopie (ADR-0009) + Objekt-Versionierung.
- **Voller Rebuild:** IaC apply → DB-Restore → Medien-Re-Seed (Config/Realm in Git, ADR-0010).

---

## 7. Aufbau-Phasen (jede Phase test-first, mit grünem E2E-Pfad als DoD — ADR-0003)

> Reihenfolge nach Risiko & Fundament: erst das tragende Skelett (Stack, GitOps, Auth,
> Event-Naht), dann die Inhalte, dann Anerkennung, zuletzt KI.

> Die konkreten User Stories/Tasks dieser Phasen werden in **Backlog.md** geführt (Tool-Setup
> und Definition of Done: ADR-0015), nicht in diesem Dokument.

1. **Fundament & strict GitOps.** OpenTofu provisioniert Hetzner-Node + Hetzner-Object-Storage +
   DNS und **bootstrappt Single-Node-k3s + Argo CD**; ab da reconcilet Argo alles aus Git
   (ADR-0010). Die Cloud-Ressourcen liegen über **drei getrennte Hetzner-Projekte** verteilt
   (ADR-0017): das Projekt `cichlids` trägt alle live/wiederherstellbare Infrastruktur (Compute,
   Netze, Firewalls, Load Balancer, DBs und Medien je Umgebung); `cichlids-backup` hält nur die
   unveränderlichen Backups unter Object Lock; `cichlids-tfstate` nur den OpenTofu-State-Bucket.
   CI besitzt ausschließlich das **Read&Write-Token des Projekts `cichlids`** und die S3-Keys für
   den State-Bucket — keine Tokens für die Backup-/State-Projekte. CI mit Grün-Gate + ADR-Index-
   Check, OpenAPI-Pipeline + generierter TS-Client, leere App/API laufen E2E (ADR-0005, 0006).
   **Von Anfang an:** dünne Storage/CDN-Abstraktion (S3-API, Provider als Config — ADR-0009) und
   **Postgres-PITR via CloudNativePG + erster getesteter Restore** (ADR-0013). **Destruktiv-Schutz
   ab Tag 1** (ADR-0014): `prevent_destroy` + Hetzner-Delete-Protection + Bucket-Versioning/
   Object-Lock + fail-closed Plan-Diff-Gate; die Projekt-Trennung (ADR-0017) ist dabei der
   eigentlich durchsetzbare Schutzwall, da Hetzner-Tokens nur Read oder Read&Write kennen.
2. **Auth.** Keycloak deklarativ, Login/Registrierung in der App, Bot-Service-Accounts. DoD:
   E2E-Login grün.
3. **Event-Rückgrat.** Transactional Outbox + Broker/Queue (zum Start ggf. Postgres-basiert),
   Webhook-Auslieferung, idempotente Konsumenten (ADR-0008).
4. **Medien & Galerien.** Foto-Upload → R2 + Variantenerzeugung (Cloudflare-CDN); **Video-Upload
   → async Transcode → separates Video-Hosting** (adaptive HLS, ADR-0012); Becken/Profil-
   Galerien, adaptive Wiedergabe. Parallel: ETL-Bestand (Abschnitt 3) gegen Staging.
5. **Stories & Becken im Fokus.** TankPost (Story + Einzel-Post, Fotos/Videos gemischt),
   Becken als Profil-Zentrum, Besatz/Arten.
6. **Anerkennung/Reputation.** Ratings, Badges, Bestenlisten, Feed/Discovery — serverseitig
   berechnet.
7. **KI-Agenten.** Python-Dienst als API-Client, abonniert Webhooks; Auto-Antworten/Reviews/
   Bewertungen zum Anschieben des Engagements (ADR-0011).

**Cutover:** finaler ETL + Medien-Migration produktiv, Auth0→Keycloak, Go-Live.

---

## 8. Offene Detailentscheidungen (künftige ADRs)

- Persistenz-/Migrations-Tooling (EF Core o.a.); neues DB-Schema im Detail.
- Konkreter Message-Broker/Queue (Postgres-Queue → dedizierter Broker bei Bedarf).
- Konkreter Compute-Anbieter der US-Box; managed vs. self-hosted Postgres.
- Backup-Tooling (an managed/self-hosted gekoppelt), exakte RPO/RTO-Werte & Retention,
  Anonymisierungs-Regeln fürs Staging, Zeitpunkt des Standby-Replica (ADR-0013).
- Variantensatz (Anzahl/Größen) und Erzeugungsort (API vs. Worker).
- **Video-Hosting/Transcoder** (self-hosted ffmpeg+HLS auf separatem S3-Speicher+Video-CDN vs.
  dedizierter Provider wie Bunny Stream/Mux/Cloudflare Stream), Rendition-Ladder,
  Längen-/Größenlimits, Video-Moderation (ADR-0012).
- KI: Modelle/Provider, Agenten-Framework, Kennzeichnung KI-Inhalte, Rate-Limits/Kostenrahmen.

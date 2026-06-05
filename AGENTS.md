# cichlids.com — Projekt- & Agent-Instruktionen

**Vor Arbeit an diesem Projekt zuerst [`PROJECT-CONTEXT.md`](PROJECT-CONTEXT.md) lesen** — die
strukturierte Wissensbasis. Abschnitt §0 enthält die **Dokumentations-Landkarte** (welche Quelle
beantwortet welche Frage) und die empfohlene Lese-Reihenfolge.

Weitere Quellen: **Produktvision/PMF (Zielbild, Engagement-Logik, Epics)** [`PRODUCT-VISION.md`](PRODUCT-VISION.md) ·
Voll-Report `workspace-legacy/cichlids-legacy-bestandsaufnahme.html` · Detail-Analysen
`workspace-legacy/findings/01–10` · historische Feature-Notizen `findings/09` + `workspace-legacy/planning-notes/`.

## Kurzfassung
- Wiederbelebung der Foto-Sharing-Community **cichlids.com** (seit 2023 offline) als moderne **Mobile-First-App**.
- **Datenrettung abgeschlossen** (Stand 2026-06-02): MySQL-5.7-Stack mit 3 restaurierten DBs, ~235 GB Bild-Originale.
- Kerndaten: DB `cichlids_typo3`, Tabellen `user_cichlids_*` (189k Bilder, 1,6 Mio Kommentare, 11.948 Nutzer).
- Bilder: `/workspaces/legacy-data/images/userpics/`. Kuratierte Daten + Voll-Report: `workspace-legacy/`.

## Design & Design System (App-UI)

Die Design-Richtung steht und ist verbindliche Grundlage für jede UI-Umsetzung. Produkt-These:
**das Becken als lebendes Projekt** — die private Pflege-Routine erzeugt automatisch den
öffentlichen Stolz (Begründung: [`PRODUCT-VISION.md`](PRODUCT-VISION.md); Fokus zuerst Epic 1 + 2).

- **Tokens (Quelle der Wahrheit):** [`design-system/tokens.css`](design-system/tokens.css) +
  [`design-system/tokens.json`](design-system/tokens.json) — Palette A „Aquatisches Teal", Light
  **und** Dark, plus Abstände/Typo/Radien/Elevation (OKLCH). Keine Einmal-Farben einführen.
- **Visuelle Doku:** [`cichlids-design-system.html`](cichlids-design-system.html) (Foundations +
  Komponenten, live).
- **Referenz-Screens (Design-Vorlage, keine App):**
  [`cichlids-tank-prototype.html`](cichlids-tank-prototype.html) — vollständiger interaktiver
  Userflow (Light/Dark): Becken-Portfolio, Becken-Detail (Story/Pflege), Quick-Log, Pflege-
  Dashboard, Entdecken, Profil, Onboarding, „Becken anlegen"-Wizard mit Kompatibilitäts-Warnung,
  Becken-Einstellungen (Sichtbarkeits-Stufen). Hi-Fi-Ausarbeitung des Herzstücks:
  [`cichlids-tank-detail-hifi.html`](cichlids-tank-detail-hifi.html).
- **Umsetzungsregeln:** mobile-first **Android zuerst** (iOS später), Light+Dark, Ziel **WCAG AA**,
  Touch-Targets ≥ 48dp; ein Akzent (max. 2× pro Screen); Text auf Akzentflächen über `--on-accent`.
  Eine `design-system/README.md` fasst Einstieg und Regeln zusammen.

## Konventionen
- Antwortsprache: **Deutsch**.
- ⚠️ Backups/Configs enthalten **Klartext-Secrets + PII** — niemals committen; `workspace-legacy/.gitignore` beachten.
- MySQL-Aufrufe **inline** (nicht über Shell-Variable); bei MyISAM echte `COUNT(*)` statt `information_schema`.
- Stack starten: `cd workspace-legacy/legacy-stack && docker compose up -d` (MySQL :3306 root/legacy, phpMyAdmin :8085).
- **Repo-Hygiene:** Open-Design-Wegwerf-Artefakte gehören **nicht** ins Repo und sind in [`.gitignore`](.gitignore): `*.artifact.json` (Vorschau-Sidecars) und `mpz*-image.png` (eingefügte Screenshots). Design-**Quellen** dagegen schon: `design-system/`, `cichlids-design-system.html`, `cichlids-tank-prototype.html`, `cichlids-tank-detail-hifi.html`.

## Grundhaltung

Die Entwicklung erfolgt **test-first und spec-driven**: Eine Story wird zuerst über einen
durchgängigen Anwendungspfad im Given-When-Then-Format spezifiziert, dann nach dem
Red-Green-Prinzip umgesetzt; „fertig" ist sie erst mit grünem End-to-End-Pfad und grünem
CI-Gate. Maßgeblich sind die Architektur-Entscheidungen in [`docs/adr/`](docs/adr/), insbesondere ADR-0003
(Teststrategie/Definition of Done) und ADR-0015 (User-Story-/Spec-Verwaltung).

## Skills

Die konkreten Verfahren liegen als versionierte **Agent Skills** (offenes Format,
<https://agentskills.io>) unter [`skills/`](skills/) — je Skill ein Unterverzeichnis mit
`SKILL.md` — und werden bei Bedarf gelesen:

- **`backlog-workflow`** ([`skills/backlog-workflow/`](skills/backlog-workflow/)) — Verwaltung
  von User Stories und Tasks mit Backlog.md: spec-driven Loop, Story-Schnitt, Priorisierung,
  bidirektionale Verknüpfung zu Maestro-E2E-Tests, Datei- und CLI-Konventionen. Heranzuziehen
  bei jeder Arbeit an Stories, Tasks, Backlog oder Board.

## User Stories / Specs / Tasks (Backlog.md)

User Stories, Specs und Tasks werden mit **Backlog.md** verwaltet (Entscheidung:
[`docs/adr/0015`](docs/adr/0015-user-story-and-spec-management.md)). Sie liegen als Markdown unter
[`backlog/`](backlog/) und sind die einzige Quelle der Wahrheit für das, was gebaut wird.
Stories werden auf **Englisch** verfasst (konsistent mit Code und ADRs).

### Agent-Workflow (über MCP)

Auf Tasks **ausschließlich über den MCP-Server `backlog`** zugreifen (konfiguriert in
[`.mcp.json`](.mcp.json), im Devcontainer automatisch verfügbar). Dateien unter `backlog/`
**nicht** von Hand editieren — nur über die Tools, damit IDs, Metadaten, Beziehungen und
Historie konsistent bleiben.

1. **Erst die Anleitung lesen:** `get_backlog_instructions` (Overview) sowie die Varianten
   `task-creation` / `task-execution` / `task-finalization` beim jeweiligen Schritt.
2. **Vor dem Anlegen suchen:** mit `task_search` / `task_list` bestehende Arbeit finden,
   Details über `task_view` lesen.
3. **Task anlegen, wenn die Arbeit Planung oder eine Entscheidung erfordert** (untersuchen,
   Vorgehen wählen, Struktur entwerfen). Für triviale mechanische Edits, Fragen und reine
   Exploration keinen Task anlegen.
4. **Self-assign & pflegen:** Task sich selbst zuweisen, auf `In Progress` setzen und
   Implementierungsplan, Notizen und Acceptance-Criteria-Häkchen über `task_edit` aktuell halten.
5. **Gegen die Definition of Done abschließen:** Ein Task ist erst *Done*, wenn seine
   DoD-Checkliste erfüllt ist.

### Lesbarkeit der Tasks

**Jedes mehrzeilige Task-Feld** — `## Description`, `## Implementation Plan` und
`## Implementation Notes` — wird als **strukturiertes Markdown** geschrieben, **nie** als
einzelner Fließtext-/Run-on-Block in einer Zeile: Unter-Header (`###`/`####`) und Bullet-Listen
für Ziel, Scope, Mechanik, betroffene Artefakte und Referenzen. Beim Setzen über CLI/MCP
**echte Zeilenumbrüche** im mehrzeiligen String mitgeben (`-d`/`--plan`/`--notes` mit echten
Newlines, nicht alles in einer Zeile), und danach gegenprüfen, dass das Feld nicht zu einer
einzigen Zeile kollabiert ist. So bleibt der Task auf dem Board und im Diff schnell erfassbar.
Acceptance Criteria bleiben im **Given-When-Then**-Format im eigenen Abschnitt; Scope gehört in
die Description, nicht in die DoD.

### Definition of Done (spiegelt ADR-0003)

Die projektweite DoD-Checkliste ist in [`backlog/config.yml`](backlog/config.yml) konfiguriert und
hängt an jedem Task. Sie kodiert den testgebundenen Akzeptanzvertrag aus
[`docs/adr/0003`](docs/adr/0003-test-strategy-and-definition-of-done.md): jede Story spezifiziert mindestens
einen **Given-When-Then**-Pfad, folgt **Red-Green** (zuerst fehlschlagender Test) und ist erst
*Done* mit grünem **End-to-End-Test** für diesen Pfad, Unit-/Integration-Abdeckung der
maßgeblichen Logik und vollständig grüner CI. Verhaltens-Scope gehört in die **Acceptance
Criteria**; die DoD ist eine Abschluss-Checkliste, kein Ort für Scope.

### Story↔Test-Verknüpfung & CI-Gate

Jede Story ist bidirektional mit ihrem E2E-Test verknüpft: die Story referenziert den Testpfad
(`--ref`), der Maestro-Flow (ADR-0004) trägt die Story-ID als `tags`-Eintrag zurück. Das harte
CI-Gate ([`tools/check-story-test-binding.mjs`](tools/check-story-test-binding.mjs), Job
`story-test-binding` in [`.github/workflows/ci.yml`](.github/workflows/ci.yml)) blockiert den Merge,
wenn eine `Done`-Story keinen existierenden, mit ihrer ID rück-getaggten Test referenziert. Details
und Konventionen im Skill `backlog-workflow`.

### Menschliche Nutzung (ohne Agent)

- Web-UI auf `http://localhost:6480` (läuft als Devcontainer-Service, vom Host erreichbar).
- `backlog board` (Kanban-TUI), `backlog task list`, `backlog task create "Title" --ac "Given … When … Then …"`.

## Commit & Dokumentation

Vor Commits und beim Schreiben von Code-Kommentaren/Doku die globalen Regeln beachten
(keine `Co-Authored-By`-Trailer; keine destruktiven Git-Ops ohne Freigabe; Doku nur aktueller
Stand + zeitlose Begründung, keine Session-/Plan-/Milestone-Bezüge).

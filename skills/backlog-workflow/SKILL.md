---
name: backlog-workflow
description: >-
  Verwaltung von User Stories und Tasks mit Backlog.md im Repository (Ordner backlog/):
  spec-driven Arbeitsablauf nach Given-When-Then, Red-Green und Definition of Done,
  Story-Schnitt (Epic/Story/Subtask), Priorisierung, bidirektionale Verknuepfung zu
  Maestro-E2E-Tests sowie Datei- und CLI-Konventionen. Zu verwenden, sobald Stories,
  Tasks, Backlog oder das Board angelegt, geschnitten, spezifiziert, priorisiert,
  fortgeschrieben oder abgenommen werden, oder wenn nach dem naechsten zu bearbeitenden
  Arbeitsschritt gefragt wird.
license: Proprietary
metadata:
  author: cichlids.com
---

# Backlog-Workflow (cichlids.com)

Dieser Skill regelt, wie User Stories und Tasks in diesem Repository gefuehrt werden. Die
ausfuehrliche Bedienungsreferenz der CLI steht in
[`references/backlog-cli.md`](references/backlog-cli.md) und wird nur bei Bedarf gelesen.

## Source of Truth

Stories und Tasks leben als versionierte Markdown-Dateien unter `backlog/` und werden
**ausschliesslich ueber die `backlog`-CLI** (oder den Backlog-MCP-Server) veraendert – niemals
durch direktes Editieren der Task-Dateien. Direktes Editieren bricht Metadaten, ID-Vergabe
und Git-Verknuepfung. Das Board (`backlog board` im Terminal, die Web-UI auf
`http://localhost:6480`) ist nur eine Ansicht auf diese Dateien.

## Spec-driven Loop

Spezifikation vor Code – in dieser Reihenfolge:

1. **Zerlegen.** Vorhaben in kleine, einzeln abnehmbare Stories schneiden
   (`backlog task create`).
2. **Spezifizieren (zuerst).** Bevor Produktivcode entsteht, traegt die Story mindestens
   einen durchgaengigen Anwendungspfad als Akzeptanzkriterium im **Given-When-Then**-Format.
   Ohne diesen Pfad ist die Story nicht umsetzungsreif. Die Definition-of-Done-Checkliste
   wird jeder Story automatisch beigegeben. Stories werden auf **Englisch** verfasst
   (konsistent mit Code und ADRs).
3. **Planen.** Den Loesungsweg als Implementation Plan in die Story schreiben
   (`backlog task edit <id> --plan "…"`).
4. **Red → Green → Refactor.** Zuerst einen scheiternden Test, dann Produktivcode bis gruen,
   dann refaktorisieren – von der Unit-Ebene bis zum Anwendungsfluss.
5. **Abnehmen.** Eine Story ist erst **fertig**, wenn ihr verknuepfter End-to-End-Test den
   spezifizierten Pfad am real hochgefahrenen Stack gruen durchlaeuft **und** der zugehoerige
   Code mit gruenem CI-Gate in `main` gemergt ist. Statuswechsel und das Abhaken der Kriterien
   (`-s`, `--check-ac <index>`, `--check-dod <index>`) passieren **im Feature-Branch**
   zusammen mit Feature und Test – Details siehe „Verknuepfung mit Test und Merge-Ablauf".

Dieser Ablauf setzt die Architektur-Entscheidungen in `docs/adr/` operativ um – insbesondere die
Teststrategie und Definition of Done (ADR-0003), das E2E-Werkzeug Maestro (ADR-0004) und die
User-Story-/Spec-Verwaltung (ADR-0015).

## Story-Schnitt und Backlog-Struktur

- **Granularitaet.** Eine Story ist ein **vertikaler Schnitt**: genau ein durchgaengiger,
  nutzer-beobachtbarer Pfad, der durch einen gruenen End-to-End-Test belegbar ist (ein
  Given-When-Then-Pfad ↔ ein E2E-Test). Rein technische Schritte sind **Subtasks**, keine
  eigenstaendigen Stories.
- **Hierarchie.** Groessere Features werden als **Epic** abgebildet (Parent-Task), die
  zugehoerigen Stories als **Subtasks** (`backlog task create "…" --parent <epic-id>`).
  **Milestones** gruppieren orthogonal dazu nach Release/Zeitziel (`-m <milestone>`).
- **Definition of Ready / Drafts.** Unfertige Ideen leben als **Draft**
  (`backlog task create --draft` bzw. `backlog/drafts/`). Eine Idee wird erst zur echten
  Story, wenn sie ein Given-When-Then-Akzeptanzkriterium traegt – dann aus dem Draft
  heraufstufen.
- **Priorisierung & Reihenfolge.** Prioritaet ueber `--priority high|medium|low`, fachliche
  Reihenfolge ueber **Abhaengigkeiten** (`--depends-on <ids>`). Daraus berechnet
  `backlog sequence` die naechste bearbeitbare Story – so ziehen Agenten selbsttaetig die
  richtige Reihenfolge.

## Verknuepfung mit Test und Merge-Ablauf

- **Bidirektionale Verknuepfung.** Jeder End-to-End-Test traegt eine stabile Referenz auf die
  Story-ID (Maestro-`tags`-Eintrag mit der Story-ID); die Story verweist ueber `--ref` zurueck
  auf den Testpfad. So ist die Zuordnung von beiden Seiten eindeutig und maschinell pruefbar.
  Maestro-Flows liegen unter `app/mobile/e2e/maestro/` (ADR-0004), z.B.:

  ```yaml
  appId: com.example.cichlids
  tags:
    - TASK-1            # Rueckverweis auf die Story-ID
  ---
  - launchApp
  - assertVisible: "…"
  ```

  In der Story: `backlog task edit TASK-1 --ref app/mobile/e2e/maestro/<flow>.yaml`.
- **Status im Feature-Branch.** Feature, E2E-Test und Statusaenderung der Story entstehen im
  **selben Branch / Pull Request** – kein separater Status-Commit. Spaltenfluss (Branch-
  Erkennung ist aktiv):
  - `To Do` – noch nicht begonnen
  - `In Progress` – Branch aktiv, Umsetzung laeuft
  - `In Review` – Pull Request offen; der Branch traegt die fertige Story samt gruenem Test,
    aber noch nicht in `main` gemergt
  - `Done` – in `main` gemergt, CI-Gate gruen
- **Erzwungene Definition of Done.** Das CI-Gate prueft am Pull Request die Konsistenz: Eine als
  fertig markierte Story ohne gruenen, verknuepften E2E-Test blockiert den Merge. Autoritativ ist
  der gemergte, gruene Stand – das Board spiegelt ihn nur. Der erzwingende Check ist
  [`tools/check-story-test-binding.mjs`](../../tools/check-story-test-binding.mjs) (CI-Job
  `story-test-binding`): Jede `Done`-Story muss eine existierende, mit ihrer Story-ID
  rueck-getaggte Testdatei referenzieren.

## Konventionen fuer portable Dateinamen

Backlog.md leitet den Dateinamen einer Story aus ihrem **Titel** ab. Damit die Dateien ueber
Windows, macOS und Linux hinweg portabel bleiben:

- **Titel ausschliesslich in ASCII.** Keine Umlaute/ss (stattdessen `ae`, `oe`, `ue`, `ss`),
  keine Sonderzeichen, insbesondere keine Windows-reservierten Zeichen
  (`: / \ < > " | ? *`), keine fuehrenden/abschliessenden Leerzeichen oder Punkte.
- Umlaute und Sonderzeichen im **Inhalt** (Beschreibung, Akzeptanzkriterien) sind unbedenklich
  – die Regel betrifft nur den Titel, weil daraus der Dateiname entsteht.
- **IDs** sind nicht nullaufgefuellt (`task-1`, `task-2`, …); das ist gewollt.

## Wichtigste Kommandos

```bash
backlog task create "ASCII-Title" -d "Description" --ac "Given … When … Then …"
backlog task create "…" --parent <epic-id> --depends-on <ids> --priority high
backlog task edit <id> --plan "…" --notes "…" --check-ac <index> -s "<Status>"
backlog task list --plain            # Uebersicht (AI-freundlich)
backlog search "…" --plain           # Volltextsuche
backlog sequence                     # naechste bearbeitbare Stories aus Abhaengigkeiten
backlog board                        # Terminal-Kanban
```

Die Web-UI laeuft als Devcontainer-Service auf `http://localhost:6480` (vom Host erreichbar).
Eine vollstaendige Befehlsreferenz steht in [`references/backlog-cli.md`](references/backlog-cli.md).
Zusaetzlich ist die Workflow-Referenz ueber die MCP-Resource `backlog://docs/task-workflow`
bzw. das MCP-Tool `get_backlog_instructions` verfuegbar.

# cichlids.com — Projekt-Instruktionen

**Vor Arbeit an diesem Projekt zuerst [`PROJECT-CONTEXT.md`](PROJECT-CONTEXT.md) lesen** — die
strukturierte Wissensbasis. Abschnitt §0 enthält die **Dokumentations-Landkarte** (welche Quelle
beantwortet welche Frage) und die empfohlene Lese-Reihenfolge.

Weitere Quellen: Voll-Report `workspace-legacy/cichlids-legacy-bestandsaufnahme.html` · Detail-Analysen
`workspace-legacy/findings/01–10` · geplante Features/Vision `findings/09` + `workspace-legacy/planning-notes/`.

## Kurzfassung
- Wiederbelebung der Foto-Sharing-Community **cichlids.com** (seit 2023 offline) als moderne **Mobile-First-App**.
- **Datenrettung abgeschlossen** (Stand 2026-06-02): MySQL-5.7-Stack mit 3 restaurierten DBs, ~235 GB Bild-Originale.
- Kerndaten: DB `cichlids_typo3`, Tabellen `user_cichlids_*` (189k Bilder, 1,6 Mio Kommentare, 11.948 Nutzer).
- Bilder: `/workspaces/legacy-data/images/userpics/`. Kuratierte Daten + Voll-Report: `workspace-legacy/`.

## Konventionen
- Antwortsprache: **Deutsch**.
- ⚠️ Backups/Configs enthalten **Klartext-Secrets + PII** — niemals committen; `workspace-legacy/.gitignore` beachten.
- MySQL-Aufrufe **inline** (nicht über Shell-Variable); bei MyISAM echte `COUNT(*)` statt `information_schema`.
- Stack starten: `cd workspace-legacy/legacy-stack && docker compose up -d` (MySQL :3306 root/legacy, phpMyAdmin :8085).

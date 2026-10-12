---
name: knowledge-articles
description: >-
  Erstellung und Aktualisierung von Fachartikeln der cichlids.com-Wissensdatenbank
  (data/knowledge/articles/): Themenwahl, Agenten-Pipeline aus Autor, zwei unabhängigen
  Faktenprüfern und Schiedsrichter, Dateiformat mit Quelle je Absatz, Validierung und die
  Pflichtregeln zum Urheberrecht (Quellen angeben, nie wörtlich zitieren, immer selbst
  formulieren). Zu verwenden, sobald ein Fachartikel geschrieben, geprüft,
  überarbeitet oder neu gegen die Quellen abgeglichen werden soll, oder wenn eine Frage zu
  Wasserchemie, Haltung oder Biologie besser als Artikel denn als Artenprofil beantwortet wird.
license: Proprietary
metadata:
  author: cichlids.com
---

# Fachartikel der Wissensdatenbank (cichlids.com)

Fachartikel erklären Zusammenhänge, die über eine einzelne Art hinausgehen, etwa das Wasser des
Malawisees und warum Haltungsanleitungen davon abweichen. Die Artenseiten verlinken passende
Artikel, und die KI-Erstantwort der App greift über die Wissensdatenbank auf sie zu. Falsche
Aussagen schaden Fischen und dem Vertrauen in die Plattform; deshalb braucht jede Aussage eine
Quelle, die sie tatsächlich enthält, und zwei unabhängige Prüfungen.

## Pflichtregeln: Urheberrecht und Quellen

Diese Regeln gelten ausnahmslos, für jede Rolle und jede Überarbeitung. cichlids.com ist ein
deutsches Unternehmen; eine Übernahme fremder Texte ist ein rechtliches Risiko für die Plattform.

- **Das Urheberrecht ist zu beachten.** Fremde Texte, Datenbanken, Bücher, Artikel und
  Forenbeiträge sind geschützt. Aus ihnen werden ausschließlich Fakten und Zahlen übernommen.
- **Niemals wörtliche Zitate.** Kein Satz, kein Halbsatz, keine Passage, keine Aufzählung, keine
  Zutatenliste und keine Definition wird aus einer Quelle übernommen, auch nicht in
  Anführungszeichen und auch nicht kurz. Ebenso wenig der Aufbau oder die Gliederung eines
  fremden Textes.
- **Immer selbst formulieren.** Jeder Satz des Artikels ist eine eigene Formulierung, die den
  Sachverhalt mit eigenen Worten und in eigener Reihenfolge erklärt. Wer beim Schreiben eine
  Quelle offen hat, formuliert erst, nachdem er den Fakt verstanden hat, nicht entlang des
  fremden Wortlauts.
- **Quellen sind Pflicht.** Jede Aussage nennt ihre Quelle als Fußnote; jeder Absatz und jeder
  Listenpunkt hat mindestens eine. Zitiert wird nur, was gelesen wurde und die Aussage
  tatsächlich enthält. Eine nicht abrufbare Quelle wird nicht zitiert.
- **Die Prüfung ist Teil des Ablaufs.** Beide Faktenprüfer suchen gezielt nach Stellen, die zu nah
  am Wortlaut einer Quelle liegen. Der Schiedsrichter formuliert jede solche Stelle neu, bevor
  der Artikel fertig ist.
- Fremde Bilder, Grafiken und Tabellen werden nicht übernommen. Eine eigene Tabelle mit
  Messwerten und Quellenangaben ist erlaubt.

## Wann ein Artikel und wann ein Artenprofil

- **Artenprofil** (`data/species/profiles/`): Werte und Texte, die genau eine Art betreffen.
- **Fachartikel** (`data/knowledge/articles/`): Wissen, das für viele Arten gilt oder einen
  verbreiteten Irrtum aufklärt, zum Beispiel Wasserchemie eines Sees, Härte und Pufferung,
  Malawi-Bloat, Kreuzungen und Farbformen, Geschlechterverhältnis im Harem.

Gute Themen entstehen dort, wo die Agenten der Artenprofile wiederholt auf denselben Widerspruch
zwischen Quellen stoßen, oder wo Fragen im Forumsarchiv immer wieder auftauchen.

## Dateien

| Pfad | Inhalt |
|---|---|
| `data/knowledge/articles/<id>.md` | fertiger Artikel, versioniert |
| `data/knowledge/schema/article.schema.json` | Schema der Frontmatter |
| `data/knowledge/work/<id>/` | Arbeitsdateien (Entwurf, Prüfberichte), nicht versioniert |
| `tools/species-data/prompts/articles/` | Prompts der Rollen: `common.md`, `author.md`, `checker.md`, `adjudicator.md` |
| `tools/species-data/validate-articles.mjs` | Validator |

Ein Artikel besteht aus YAML-Frontmatter (id, Titel, Sprache `en`, Zusammenfassung, Themen,
verknüpfte Arten und Gewässer, Quellen, Provenienz) und einem Markdown-Text auf Englisch. Die
Übersetzung übernimmt die App. Zitiert wird mit Fußnoten `[^key]`; jeder Absatz und jeder
Listenpunkt hat mindestens eine Quelle. Die genauen Regeln stehen im Abschnitt "Knowledge
articles" von `tools/species-data/README.md`.

## Ablauf

Der Hauptagent der Session steuert den Ablauf. Die Rollen sind Subagenten, die je ihren Prompt
aus `tools/species-data/prompts/articles/` lesen. Jede Rolle hat ein eigenes Verzeichnis
`data/knowledge/work/<id>/tmp-<rolle>/` für Downloads und Notizen und liest keine
Zwischendateien anderer Rollen.

1. **Thema festlegen:** id (Slug), Titel und die Punkte, die der Artikel abdecken muss. Die
   Vorgabe nennt Fakten, die zu prüfen sind; der Autor darf und soll sie korrigieren, wenn die
   Quellen etwas anderes sagen.
2. **Autor** (starkes Modell, etwa Opus): recherchiert, schreibt
   `data/knowledge/work/<id>/draft.md` und validiert ihn. Beim Entwurf ist nur der Fehler zum
   Dateinamen erlaubt.
3. **Zwei Faktenprüfer**, parallel und unabhängig, mit unterschiedlichen Modellen (etwa Opus und
   Sonnet): Sie prüfen jede Aussage gegen die zitierten Quellen, rechnen Umrechnungen nach und
   suchen Stellen, die zu nah am Wortlaut einer Quelle liegen. Ergebnis sind
   `checker-a.yaml` und `checker-b.yaml` mit einem Urteil je Aussage (supported, partly,
   unsupported, wrong), Korrekturen mit Quellen und fehlenden Fakten.
4. **Schiedsrichter** (starkes Modell): übernimmt Korrekturen, entscheidet strittige Aussagen
   selbst anhand der Quellen, streicht oder belegt unbelegte Aussagen, formuliert zu nahe
   Passagen neu und dokumentiert jede Entscheidung in `provenance.disagreements`. Er schreibt
   `data/knowledge/articles/<id>.md`, der ohne Fehler und Warnungen validieren muss.
5. **Bericht an den User** auf Deutsch: Kernaussagen des Artikels, Zahl der geprüften Aussagen
   je Urteil, die wichtigsten Korrekturen und offene Punkte, die keine Quelle klären konnte.

Validierung:

```
node tools/species-data/validate-articles.mjs data/knowledge/articles/<id>.md
```

## Quellen im Einzelnen

- Vorrang haben Primärliteratur und Messdaten. Ausdrücklich erwünscht sind Fachartikel aus
  Zeitschriften, Monografien und Forschungsberichte mit DOI: Agenten suchen gezielt danach, lesen
  sie (frei zugängliche Fassung, Abstract oder Preprint) und zitieren sie mit der DOI-Adresse
  `https://doi.org/<doi>` als URL. Ist nur der Abstract lesbar, stützt die Quelle nur, was der
  Abstract sagt. Haltungsquellen dienen dazu, zu zeigen, was das
  Hobby empfiehlt; wo sie abweichen, nennt der Artikel, welche Quelle was angibt.
- Die Seewerte stehen geprüft in `data/species/waterbodies.yaml`; zitiert werden die Studien
  dahinter. Interne Dateien des Projekts (Tabellen, Profile, Prompts) sind nie eine Quelle.
- Fachliteratur sucht `node tools/species-data/literature-search.mjs "<suche>"` in den offenen
  Datenbanken OpenAlex, Europe PMC und Crossref (Jahr, Titel, DOI, frei zugängliche Fassung, Abstract). Die Websuche teilen
  sich alle Agenten einer Session (200 Suchen, etwa 100 kommen je Stunde dazu); sie dient nur für
  das, was Literatursuche und direkter Abruf nicht finden.
- Das Forumsarchiv durchsucht `node tools/species-data/archive-search.mjs "<begriff>"`. Forenbeiträge
  sind Erfahrungen der Community mit sehr geringem Gewicht: Sie ergänzen Fachquellen und belegen
  eine Aussage nur, wenn keine Fachquelle dazu existiert; der Text sagt dann, dass es sich um
  Berichte von Haltern handelt. Zitiert
  wird mit der ausgegebenen Legacy-Thread-ID und URL; Namen von Mitgliedern erscheinen nie.
- Volltexte frei zugänglicher Arbeiten holt `xvfb-run -a node tools/species-data/fetch-oa.mjs <doi>`:
  zuerst aus der lokalen Ablage, dann aus der Kopie bei OpenAlex, erst dann von der Verlagsseite.
  Arbeiten, die nicht frei zugänglich sind, lehnt das Werkzeug ab.
- Logins, Bezahlschranken und Captchas werden nie umgangen. Eine Bot-Prüfung vor einer frei
  lesbaren Arbeit passiert nur das Werkzeug `fetch-oa.mjs` (für Cichlid Room Companion `crc.mjs`);
  Agenten steuern selbst keine Browser.
- Cichlid Room Companion dient nur der Verifikation und läuft über
  `tools/species-data/crc.mjs` (gedrosselt, mit Cache).

## Aktualisierung

Artikel laufen erneut durch Faktenprüfer und Schiedsrichter, wenn sich zugrunde liegende Daten
ändern (etwa die Seewerte), wenn neue Literatur erscheint oder wenn die Pipeline der Profile
turnusmäßig über den Bestand läuft. Der Schiedsrichter ergänzt dabei `provenance.runs`; das
Repository speichert jede Fassung.

## Erfahrungswerte

- Die Websuche hat ein Kontingent, das bei vielen parallelen Agenten aufgebraucht wird. Agenten
  weichen dann auf bekannte Adressen aus; Prüfberichte sollen offenlegen, was nicht prüfbar war.
- Typische Fehler im Entwurf: Ursachen, die keine Quelle nennt; Einheiten, die beim Vergleich
  nicht zusammenpassen (Nitrat gegen Nitrat-Stickstoff); Produktaussagen, die die Herstellerseite
  nicht hergibt; zu enge Anlehnung an Zutatenlisten oder Definitionen.

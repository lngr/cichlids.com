# Offene Punkte

Diese Liste sammelt alles, was an cichlids.com offen ist, an einer Stelle. Sie verweist auf die
Dokumente mit den Details und wiederholt sie nur so weit, wie es für den Überblick nötig ist.

Pflege: Ein erledigter Punkt wird gestrichen, ein neuer Punkt kommt in den passenden Abschnitt.
Arbeitspakete mit Akzeptanzkriterien stehen im Backlog (`backlog/`), dauerhafte Entscheidungen in
den ADRs (`docs/adr/`), der Gesamtstand in `PROJECT-STATUS.md`.

---

## 1. Anwendung umsetzen

Reihenfolge laut `PROJECT-STATUS.md` (F28): zuerst die für Staging wichtigen Korrekturen aus
`REVIEW-MVP.md`, dann das Deployment, danach die übrigen Korrekturen und die Slices des Zielbilds.

### 1.1 Korrekturen aus dem MVP-Review, Priorität hoch (Voraussetzung für TASK-2.4)

Umgesetzt und in `main` gemergt. TASK-3.29 ist Done, die übrigen stehen in Review.

- [ ] TASK-3.30 ETL nur gegen eine leere Datenbank, Reset für Dev und Staging (R3, R18, R38): abnehmen
- [ ] TASK-3.31 Medien-Seed anhand des Object Stores (R20): abnehmen
- [ ] TASK-3.32 Verify-Erwartungswerte nur aus den Altdaten (R34, Teile von R9, R16, R19): abnehmen
- [ ] TASK-3.33 Konto-Import und Identitäten absichern (R2, R31, R36): abnehmen
- [ ] TASK-2.27 Getrennte Keycloak-Konfiguration für Staging und Produktion (R7, R33): abnehmen
- [ ] TASK-3.34 Zuverlässige Webhook-Zustellung mit Positivliste (R4, R5, R10, R14): abnehmen
- [ ] TASK-3.35 Profil beim ersten Login atomar anlegen (R12, R31): abnehmen
- [ ] TASK-2.28 App-Tests in CI (R6): abnehmen

### 1.2 Deployment (Strang A)

- [ ] TASK-2.4 OpenTofu-App-Tier, k3s und Argo CD per SSH auf den bestehenden Node; schließt danach TASK-2.5 und AC #3 von TASK-2.21 ab
- [ ] Hetzner-Cloud-Firewall für den Node im Plattform-Tier: SSH ist aus dem ganzen Internet erreichbar, nur die nötigen Ports freigeben
- [ ] TASK-2.6 SOPS, age und KSOPS
- [ ] TASK-2.7 Argo CD app-of-apps und Umgebungen
- [ ] TASK-2.8 Ingress, TLS und Platzhalter-Workload
- [ ] TASK-2.9 CloudNativePG mit PITR
- [ ] TASK-2.10 Erste getestete Restore-Übung
- [ ] TASK-2.11 CI-Grün-Gate und Delivery-Pipeline
- [ ] TASK-2.12 Branch-Protection auf `main`
- [ ] TASK-2.17 Umgebungsdaten (anonymisierter Snapshot, CoW-Klon, Medien-Overlay)
- [ ] TASK-2.23 Slug-Secret für Staging und Produktion (F26)
- [ ] TASK-2.30 Keycloak nach Staging und Produktion
- [ ] TASK-2.19 Renovate
- [ ] TASK-1 Erster grüner E2E-Smoke-Pfad

### 1.3 Korrekturen aus dem MVP-Review, Priorität mittel und niedrig

- [ ] TASK-3.36 Eine Bewertung pro Mitglied und Ziel, keine Selbstbewertung (R9); der ETL-Teil ist mit TASK-3.32 umgesetzt
- [ ] TASK-3.37 Grenzen für Kommentare, Uploads und Entwürfe (R11, R13)
- [ ] TASK-3.38 Exakte Legacy-Weiterleitungen mit Sichtbarkeitsprüfung (R29, R30)
- [ ] TASK-3.39 Korrekturen der ETL-Regeln (R15, R16, R17, R19, R21, R35, R37)
- [ ] TASK-3.40 Konfiguration und Fehlerzustände der App (R25, R26, R27, R41)
- [ ] TASK-3.41 Dauerhafte Anmeldung pro Gerät (R8)
- [ ] TASK-3.42 Code- und Doku-Korrekturen (R32, R39, R40, R42 bis R46)
- [ ] TASK-3.28 Ich-Tab mit eigenen Bildern, Becken und Entwürfen
- [ ] TASK-2.24 Playwright-Smoke mit anonymisierten Daten in CI
- [ ] TASK-2.25 Maestro-Flows in CI (hängt an F13)
- [ ] TASK-2.26 gitleaks statt Eigenbau-Scanner
- [ ] TASK-2.29 Isolierter Webhook-Forwarder (ADR-0024)

### 1.4 Slices des Zielbilds (`MVP-VISION.md`, Abschnitt 8)

Sie beginnen, sobald Staging mit dem MVP als erstem Workload läuft. Für keine Slice gibt es bisher
Stories im Backlog.

- [ ] Slice 1 App-Rahmen: Navigation, Mehrsprachigkeit, Einheiten, Onboarding, Becken-Seite, Entdecken
- [ ] Slice 2 Pflege und Medien: Quick-Log, Pflegeplan, Serien, Erinnerungen, Fotos und Videos
- [ ] Slice 3 Rückblicke: Archiv-Rückblick, Rückkehr-Rückblick, Übernahme der Altbecken
- [ ] Slice 4 Fish: Fish Log, Artenseiten, Besatz-Planer; dazu Import der Profildateien in die
  API, Provenienz nur über Admin-Endpunkte
- [ ] Slice 5 Fragen: KI-Erstantwort mit Wissensdatenbank, Archiv-Fragen, Übersetzung, Kudos
- [ ] Slice 6 Scanner mit dem Vision-Modell

---

## 2. Aufgaben des Operators

Diese Punkte erfordern Zugriff auf Hetzner, GitHub, den Registrar oder eine Abnahme am echten
System. Details in `PROJECT-STATUS.md`, Abschnitt 9.

- [ ] F3 SSH-Zugriff auf GitHub aus dem Devcontainer reparieren
- [ ] F5 TASK-2.18 abschließen (AC #1 und #3)
- [ ] F6 TASK-2.22 abschließen
- [ ] F7 Delegation der Domain `cichlids.com` beim Registrar prüfen
- [ ] F8 Beweisobjekt `layer4-immutability-test.txt` löschen oder behalten
- [ ] F9 Laufende Kosten der Live-Ressourcen gegen den Nutzen abwägen
- [ ] F12 MVP-Stories in Review abnehmen (Prüfbefehle in `PRECHECK-IN-REVIEW.md`, untracked)
- [ ] F13 Maestro-Runner zum Laufen bringen oder den AC von TASK-3.23 anpassen
- [ ] F21 ACs von TASK-3.15 am Dev-Stack beobachten
- [ ] F23 Über DRAFT-3 (Link Bewertung zu Kommentar) und DRAFT-5 (View-Zählung in der Dev-DB) entscheiden
- [ ] DRAFT-1 Mail-Kampagne an die Bestandsmitglieder: Mail-Provider und rechtliche Prüfung
- [ ] Commits auf `main` pushen

---

## 3. Artenprofile

Stand: 55 Profile in `data/species/profiles/`, alle validiert. Verfahren in
`tools/species-data/README.md`. Je Art stimmen Generator, Prüfer A (Opus), Prüfer B (Sonnet) und
Prüfer C (Codex, gpt-6.1-sol) ab.

### 3.1 Weitere Profile

- [ ] Nächste Arten nach Fotoanzahl: 819 Geophagus altifrons, 406 Nimbochromis livingstoni,
  36 Cyphotilapia gibberosa (in Arbeit), 233 Aulonocara maylandi, 309 Fossorochromis rostratus,
  738 Herichthys carpintis, 374 Melanochromis maingano, 557 Hemichromis bimaculatus; danach weiter
  nach Fotoanzahl, Synonym-Paare gemeinsam (etwa 739 Herichthys cyanoguttatus mit
  685 Cichlasoma cyanoguttatum)
- [ ] Die harten Merkmale aller Arten sind vor Slice 4 gesichert
- [ ] Das OpenAlex-Tagesbudget (1 USD, Reset 02:00 Uhr MESZ) reicht für etwa 12 Arten. Danach
  zahlt das Prepaid-Guthaben des Schlüssels (am 2026-10-13 etwa 10 USD), weil die Werkzeuge nur
  bei HTTP 429 oder 402 auf Europe PMC und Crossref ausweichen; Restguthaben im Blick behalten

### 3.2 Offene Fragen zum Verfahren

Zu jeder Frage ist eine vorläufige Umsetzung in Gebrauch; sie wird bestätigt oder umgebaut.

- [ ] **Wasserkonflikt nach Abstand vom Ideal:** Diskus und Malawis berühren sich in der
  Toleranz am Rand (pH 4,8-7,5 gegen 7,2-8,8), deshalb greift "Toleranzen ohne gemeinsamen
  Bereich" nicht. Vorgabe: Der Konflikt hängt von der Größe des gemeinsamen Bereichs und vom
  Abstand zu den Idealen beider Arten ab, denn je weiter ein Fisch von seinem Ideal entfernt lebt,
  desto größer sind Stress und Krankheitsrisiko. Umsetzung im Besatz-Planer, siehe Abschnitt 5.
- [ ] **Quellenarten bekannter Seiten:** Fishkeeper gilt in manchen Profilen als database, in
  anderen als website; Tropical Freshwater Fish und FishProfiles sind als database eingestuft.
  Davon hängt ab, ob ein hartes Merkmal zwei externe Quellen hat. Ebenso offen: ob Cichlid Room
  Companion als eine der zwei Quellen zählt oder nur zusätzlich. Vorschlag: feste Liste der
  Quellenarten in `tools/species-data/prompts/common.md`, Profile danach neu prüfen. Die
  Schiedsrichter stufen Händlerseiten (LiveAquaria, Aquarium Glaser, Guidarium) als website
  ein, Fishkeeper wechselnd (database bei Maylandia greshakei und Chindongo demasoni, website
  bei Pseudotropheus sp. 'acei', Chindongo socolofi und Maylandia lombardoi), den Cichlid Room
  Companion bei Amphilophus labiatus als website.
- [ ] **Heros severus und der Handels-Severum:** Das Profil beschreibt H. severus im engen Sinn
  (Schwarzwasser, pH etwa 4-6). Legacy 749 und die Archivfotos zeigen fast sicher den
  Handels-Severum, meist H. efasciatus. Optionen: zusätzliches Profil H. efasciatus mit
  Zuordnung der Fotos, oder Legacy 749 umhängen. Legacy 709 Cichlasoma severum (187 Fotos) ist
  ein weiterer Eintrag derselben Art und gehört in diese Entscheidung.
- [ ] **Vorsichtsregel bei der Ernährung:** Sie kombiniert Merkmale aus verschiedenen Stimmen und
  wird strenger als jede einzelne (Andinoacara rivulatus; Otopharynx lithobates bekommt bei 3:2
  für Allesfresser den Typ Wirbellosenfresser; Protomelas sp. 'Steveni Taiwan' bekommt bei 4:1
  für Allesfresser den Typ Aufwuchsfresser). Bestätigen oder ändern.
- [ ] **Vorsichtsregel beim Raub:** Bei 3:2 für Stufe 0 entscheidet die Regel Stufe 1
  (Pseudotropheus sp. 'acei', Maylandia greshakei). Belegt ist dort nur, dass Mbuna Jungfische
  gelegentlich mitfressen. Bestätigen oder ändern.
- [ ] **Kreuzungsgruppen nachpflegen:** Die Gruppen sind nach Beleglage kuratiert (28 Gruppen,
  Zuchtformen in den Gruppen ihrer Ausgangsarten). Offen: `provenance`-Texte einiger Profile
  nennen `maylandia` als entschiedene Gruppe, obwohl die Art dort kein Mitglied ist (etwa
  Cynotilapia afra); die Vorsichtsregel
  für Gruppenlisten ohne Mehrheit (alle Gruppen mit mindestens zwei Stimmen, sonst die zuerst
  genannte) bestätigen; eine Forumsmeldung Parachromis managuensis mal Texas-Buntbarsch ergibt
  wegen der Forumsgewichtung keine Gruppe.
- [ ] **Einzelentscheide mit niedriger Sicherheit:** Maylandia zebra Raub 1 bei 3:2 für 0;
  Labeotropheus trewavasae Aggression gegen andere Arten 3 gegen eine 2:1-Mehrheit.
- [ ] **Reihenfolge und Synonyme:** Profile entstehen nach Fotoanzahl, offensichtliche
  Synonym-Einträge gemeinsam (`legacyIds`). Bestätigen.
- [ ] **Zuchtformen und Hybriden als eigene Profile** (Flowerhorn, German Red, Rubescens mit
  Status domestic-form). Bestätigen.
- [ ] **Natürliche KH bei Arten ohne Seetabelle:** `tally.mjs` stimmt sie mit ab. Bestätigen.
- [ ] **Methodik-Artikel:** Artikel mit `kind: methodology` dürfen Verfahrensabsätze ohne Quelle
  haben (Warnung statt Fehler). Bestätigen.
- [ ] **Verhalten bei leerem OpenAlex-Budget:** laufende Arten mit Europe PMC fertigstellen, keine
  neuen beginnen. Bestätigen.
- [ ] **Tie-Breaker mit Sonnet:** Die Sonnet-Tie-Breaker arbeiten knapp (etwa 15
  Werkzeugaufrufe), übernehmen Werte teils aus Suchzusammenfassungen und liefern vereinzelt
  falsche Formen (Ernährung als bloßer Text). Vorschlag: den zweiten Tie-Breaker mit Codex
  besetzen. Bestätigen oder ändern.
- [ ] **Breite Prozessabbrüche:** Ein Generator beendete einen hängenden eigenen Befehl mit
  `pkill -x cat -u alex`, was alle `cat`-Prozesse des Users trifft, auch die der parallelen
  Session. Die Aufträge untersagen breite `pkill`-Aufrufe; klären, ob das in `common.md` gehört.
- [ ] **User-Agent:** Einzelne Agenten haben Seiten mit Browser-User-Agent geladen, gegen die
  Regel. Klären, ob ein Standard-User-Agent ohne Sperre erlaubt sein soll.

### 3.3 Werkzeuge der Pipeline

- [ ] Kleines PDF-zu-Text-Werkzeug ins Repo, weil `pdftotext` fehlt und Agenten sich sonst eigene
  Hilfsskripte bauen
- [ ] Unabhängigkeit der Prüfer technisch absichern: Prüfer lesen fertige Profile nicht
- [ ] `fetch-oa.mjs` nimmt nur DOIs. Frei zugängliche Arbeiten ohne DOI (Cole 1976 zur Limnologie
  der Nicaragua-Seen, Haberyan et al. 2003) laden Agenten deshalb mit curl; einen URL-Modus mit
  denselben Zugangsregeln ergänzen
- [ ] `tally.mjs` gibt der natürlichen KH von Seearten die Verifikation `waterbody`, der
  Validator erlaubt diese Methode nur für Temperatur, pH und GH; die Schiedsrichter lassen die
  Verifikation deshalb weg. Beides angleichen
- [ ] Startskript für Codex als Prüfer C ins Repo übernehmen (Auftrag nach der Prüfer-Vorlage,
  Wiederholung bei "Selected model is at capacity", Ausweichen auf `gpt-6-sol`). Codex läuft im
  Container mit `danger-full-access`, die Regeln stehen nur im Auftrag

### 3.4 Taxonomie-Zuordnung

- [ ] Archivfotos neuen Arten aus Aufspaltungen zuordnen (Tropheus sp. 'red', T. sp. 'black' und
  ähnliche ohne Legacy-Eintrag; Heros efasciatus)
- [ ] Synonyme und gültige Namen aller 829 Legacy-Arten gegen den Catalog of Fishes abgleichen

---

## 4. Wissensdatenbank

- [ ] Weitere Fachartikel zu wiederkehrenden Widersprüchen zwischen Quellen, etwa Härte und
  Pufferung, Malawi-Bloat, Kreuzungen und Farbformen (Verfahren: Skill `knowledge-articles`)
- [ ] Artikel und Profile turnusmäßig neu gegen die Quellen prüfen, wenn das Forumsarchiv oder die
  Literatur wächst

---

## 5. Planung und Entscheidungen

- [ ] Zuschnitt des Webs planen: Teaser ohne Login, Umfang für Suchmaschinen (Artenprofile,
  Fachartikel, Archiv), Startseite als Landingpage zur App, Web-App hinter dem Login. Davon hängen
  ADR-0001, ADR-0002, ADR-0004 und ADR-0006 ab (`MVP-VISION.md`, Abschnitt 9).
- [ ] Verträglichkeitsprüfung kalibrieren: Konfliktfunktionen, Startgewichte und Schwellen.
  Wasserwerte: Je Art gilt der natürliche Bereich als Ideal (Wohlbefinden 1), zur Toleranzgrenze
  fällt es auf 0. Der Konflikt eines Paares ist 1 minus das beste Wohlbefinden der schlechter
  gestellten Art im gemeinsamen Toleranzbereich (Diskus und Malawis beim pH etwa 0,9, also
  Warnung). Schwellen für Achtung und Warnung festlegen; `MVP-VISION.md` (Konfliktfunktion
  Wasserwerte) und den Fachartikel zum Besatz-Planer, die den Wasserkonflikt als reine
  Überlappung beschreiben, entsprechend anpassen
- [ ] Kreuzungsgruppen im Besatz-Planer: Ein Paar mit gemeinsamer Gruppe der Beleglage
  `documented` ergibt eine Warnung, nur `presumed` ein Achtung (`data/species/hybrid-groups.yaml`,
  Feld `basis`; `hybridGroups` je Art ist eine Liste). `MVP-VISION.md` führt die Kreuzungsgruppe
  als harten Konflikt; anpassen
- [ ] Vision-Modell: Anbieter, Kontingent je Mitglied, Gesamtbudget
- [ ] Gemini-Vergleich im Scanner-Spike ausführen; braucht einen Gemini-API-Schlüssel
  (`tools/scanner-spike/RESULTS.md`)
- [ ] Fine-Tuning: Zeitpunkt, Anbieter, Einwilligung der Mitglieder
- [ ] Darstellung der Artenkarten wie Sammelkarten: je Merkmal Zusammenfassung, Toleranz,
  Abweichungen und Quellen; Entscheidungsweg der Agenten nur für Admins und nie über die
  öffentliche API

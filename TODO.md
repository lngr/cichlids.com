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

Stand: 257 Profile in `data/species/profiles/`, alle validiert. Verfahren in
`tools/species-data/README.md`. Solange die Claude-Credits knapp sind, ist je Art nur Prüfer A ein
Claude-Agent (Sonnet); die übrigen Rollen laufen über Codex: Generator und Prüfer B und C bei
Seearten mit gpt-6-luna, bei Freilandarten mit gpt-6.1-sol, Tie-Breaker und Schiedsrichter immer
mit gpt-6.1-sol. Ohne Mehrheit gilt bei Wasserwerten und Toleranzen der Median der Stimmen, den
der Schiedsrichter auf Plausibilität prüft.

### 3.1 Weitere Profile

- [ ] Umfang: Das Artenverzeichnis `data/species/directory.yaml` führt alle 1771 gültigen Arten
  des Catalog of Fishes mit Erstbeschreibung und Links (Stubs); `trade-forms.yaml` führt
  Handelsformen, Hybriden und Zuchtformen. Jeder Eintrag des alten Katalogs bleibt erhalten und
  ist in `legacy-mapping.yaml` einer Art oder Handelsform zugeordnet. Besatzprofile bekommen alle
  Arten des alten Katalogs plus die Arten im Handel (FishBase kommerziell oder Artseite bei
  Seriously Fish); Reihenfolge nach Fotoanzahl, die Handelsarten außerhalb des Katalogs danach.
  Das Verzeichnis markiert 647 Arten als Handelsarten (Feld `trade`); zusammen mit dem alten
  Katalog sind 844 Besatzprofile nötig, davon fehlen 591 (130 Handelsarten stehen nicht im alten
  Katalog)
- [ ] Neun Einträge des alten Katalogs sind ohne Zuordnung, weil keine Quelle ihre Identität
  klärt (etwa "Lamprologus spec.", "Labidochromis zebra", "Aulonocara carolinae",
  "Amphilophus margaritifer"); bei neuen Quellen nachziehen
- [ ] Handelsformen, die eine Population einer beschriebenen Art bezeichnen (etwa
  "Aulonocara masoni" für A. stuartgranti), bekommen kein eigenes Besatzprofil, sondern verweisen
  über `parents` auf das Profil der Art
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
  Cynotilapia afra), und die `verification` von `hybridGroups` beschreibt bei nachträglich
  ergänzten Gruppen die ursprüngliche Abstimmung (etwa Nimbochromis venustus); die Vorsichtsregel
  für Gruppenlisten ohne Mehrheit (alle Gruppen mit mindestens zwei Stimmen, sonst die zuerst
  genannte) bestätigen; eine Forumsmeldung Parachromis managuensis mal Texas-Buntbarsch ergibt
  wegen der Forumsgewichtung keine Gruppe. Erzwungene Laborkreuzungen ergeben eine vermutete
  Gruppe; ältere Profile, die eine solche Kreuzung als nicht zählend verworfen haben (etwa
  Pseudotropheus cyaneorhabdos mit Gerwin et al. 2021), danach nachprüfen.
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
- [ ] Quellenarchiv (`/workspaces/cichlids-source-archive/`) vervollständigen: Beiträge des
  eigenen Forenarchivs (cichlids.com/disc, online nicht mehr erreichbar) aus der geretteten
  Datenbank als Schnappschuss ablegen; Artikel hinter einem 403 der Verlage (etwa Wiley) über
  `fetch-oa.mjs` holen, soweit Open Access; Seiten, die die ehrliche Kennung ablehnen (AquaInfo,
  GCCA, FishProfiles, Gensou), bleiben Quelle ohne Archivkopie
- [ ] `fetch-oa.mjs` nimmt nur DOIs. Frei zugängliche Arbeiten ohne DOI (Cole 1976 zur Limnologie
  der Nicaragua-Seen, Haberyan et al. 2003) laden Agenten deshalb mit curl; einen URL-Modus mit
  denselben Zugangsregeln ergänzen
- [ ] `tally.mjs` gibt der natürlichen KH von Seearten die Verifikation `waterbody`, der
  Validator erlaubt diese Methode nur für Temperatur, pH und GH; die Schiedsrichter lassen die
  Verifikation deshalb weg. Beides angleichen
- [ ] `tally.mjs` übernimmt Quellen-Einträge ungeprüft; ein Titel mit Doppelpunkt ohne
  Anführungszeichen erzeugt einen falschen Schlüssel (gesehen bei Geophagus altifrons). Quellen beim
  Zählen gegen das Schema prüfen und kaputte Einträge melden
- [ ] Die Codex-Pipeline ins Repo übernehmen: Das Treiberskript überspringt jede Rolle, deren
  Datei im Arbeitsordner schon liegt; bei einem erneuten Lauf für eine Legacy-Nummer (etwa 749 für
  Heros efasciatus nach dem Severus-Lauf) muss es alte Dateien beiseitelegen statt sie mitzuzählen.
  Bricht ein Codex-Lauf mit einem Fehlalarm des Inhaltsfilters ab ("flagged for possible
  biological risk", gesehen bei Prüfer B zu Oscura heterospila), wiederholt es den Lauf. Weiter: ein Treiberskript je Art (Generator, Prüfer B und C
  parallel, Warten auf Prüfer A, bis zu drei Tie-Break-Runden, Schiedsrichter, Abschlussprüfung),
  ein Auftragsgenerator je Rolle, ein Gruppenabgleich aus den Validator-Warnungen und ein
  Watchdog. Wiederholung bei "Selected model is at capacity", Ausweichen auf `gpt-6-sol`. Codex
  läuft im Container mit `workspace-write` und Netzzugang, die Regeln stehen nur im Auftrag.
  Bewährt: Prompt-Änderungen erst an einer Kopie testen und dann atomar tauschen; der
  Schiedsrichter überschreibt kein vorhandenes Profil und ändert bestehende Gruppen nur um
  Mitglieder

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
  als harten Konflikt; anpassen. Gruppenmitglieder können auch Zuchtlinien wie der Flowerhorn sein;
  der Planer braucht dafür eigene Einträge für Zuchtlinien, die sich in einen Besatz aufnehmen lassen
- [ ] Besatz-Planer: empfohlene Listen und beliebte Kombinationen ausgeben (etwa bewährte
  Besatzvorschläge je Becken oder Region)
- [ ] Vision-Modell: Anbieter, Kontingent je Mitglied, Gesamtbudget
- [ ] Gemini-Vergleich im Scanner-Spike ausführen; braucht einen Gemini-API-Schlüssel
  (`tools/scanner-spike/RESULTS.md`)
- [ ] Fine-Tuning: Zeitpunkt, Anbieter, Einwilligung der Mitglieder
- [ ] Darstellung der Artenkarten wie Sammelkarten: je Merkmal Zusammenfassung, Toleranz,
  Abweichungen und Quellen; Entscheidungsweg der Agenten nur für Admins und nie über die
  öffentliche API

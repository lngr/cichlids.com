# cichlids.com: Zielbild der Relaunch-App

**Stand: 2026-10-12**

Dieses Dokument beschreibt, welche App aus dem Legacy-Parity-MVP werden soll, damit der Relaunch
Altmitglieder zurückholt und neue Halter überzeugt. Es gleicht das MVP mit der Produktvision
([`PRODUCT-VISION.md`](PRODUCT-VISION.md)), mit vergleichbaren Plattformen anderer Hobbys, mit dem
Aquaristik-Wettbewerb und mit dem tatsächlichen Inhalt der geretteten Daten ab. Daraus folgen fünf
Kernerlebnisse, ein neuer App-Rahmen und eine Reihenfolge für die Umsetzung.

Verhältnis zu anderen Dokumenten:

- [`PRODUCT-VISION.md`](PRODUCT-VISION.md) beschreibt das Warum (PMF-These, Epics). Dieses Dokument
  setzt die Vision in einen konkreten Launch-Umfang um.
- [`PLAN.md`](PLAN.md) und die ADRs beschreiben das Wie.
- [`REVIEW-MVP.md`](REVIEW-MVP.md) enthält die technischen Befunde zum MVP. Sie gelten unabhängig von
  diesem Dokument.

---

## 1. Kurzfassung

Das MVP baut die Website von 2008 als App nach: die alte Navigation (Galerie, Becken, Forum, Arten,
Ich) mit den alten Daten. Technisch ist die Basis brauchbar. ETL, Medien-Pipeline, API, Auth und
Outbox lassen sich weiterverwenden. Als Produkt fehlt dem MVP das, was die Vision als Kern festlegt:
das Becken als lebendes Projekt, dessen Pflege von selbst Inhalte erzeugt.

Die Relaunch-App behält das Backend und bekommt ein neues Produktgerüst mit fünf Kernerlebnissen.
Jedes stützt sich auf Daten, die vorhanden sind oder am ersten Tag entstehen.

---

## 2. Befund zum MVP

1. **Foto-Stream ohne Anlass zur Rückkehr.** Der Startbildschirm ist die Galerie, sortiert nach
   "Newest". Die Vision nennt genau diesen Foto-Stream die PMF-Schwäche der alten Plattform.
2. **Das Archiv wirkt wie ein verlassenes Netzwerk.** "Newest" zeigt die letzten Uploads von 2023;
   in diesem Jahr kamen insgesamt 24 Fotos hinzu. 85 % aller Fotos stammen aus 2006 bis 2013.
3. **Die Community ist schreibgeschützt.** Der Tab "Community" zeigt das Forumsarchiv mit dem Hinweis
   "no new posts". Hilfe zu einem Problem lässt sich nirgends erfragen.
4. **Die Sterne haben keine Aussagekraft.** 96 % aller Bewertungen sind 5 Sterne. Das MVP zeigt sie
   an und bietet "Top rated" als Sortierung.
5. **Die Artenseiten sind leer.** Von 829 Arten haben 4 eine Beschreibung und 2 eine Angabe zur
   Aggression; Wasserwerte hat keine.
6. **Das Design übernimmt nur die Farben.** Die Hi-Fi-Vorlage
   ([`cichlids-tank-detail-hifi.html`](cichlids-tank-detail-hifi.html)) zeigt Hero, Story/Pflege,
   Time-Machine und Timeline. Das MVP nutzt die Tokens, die Screens sind Standard-Listen und
   Datenblätter.
7. **Nichts ist persönlich.** Es gibt kein eigenes Becken, keine fällige Pflege und keine Begrüßung
   für die rund 12.000 Altmitglieder, die über die Rückgewinnungskampagne (DRAFT-1) kommen sollen.

---

## 3. Vergleichbare Plattformen anderer Hobbys

Erfolgreiche Hobby-Plattformen folgen fast alle demselben Muster: Das Ausüben des Hobbys erzeugt den
Inhalt, und ein Katalog macht das Erfassen leicht und die Daten wertvoll.

| Vorbild | Mechanik | Umsetzung bei cichlids.com |
|---|---|---|
| Strava | Eine Aktivität wird automatisch zum Beitrag; Kudos mit einem Tap | Ein Pflege-Eintrag wird zum Timeline-Eintrag; Kudos auf Höhepunkte und Serien |
| Ravelry | Projektseiten plus Katalog; jede Anleitung zeigt Tausende echte Projekte | Jede Artenseite zeigt alle Becken, in denen die Art lebt |
| PCPartPicker | Live-Prüfung der Kompatibilität beim Zusammenstellen | Besatz-Planer mit begründeten Hinweisen |
| Vivino, Seek, iNaturalist | Foto rein, Art raus; bei Unsicherheit nur die Gattung | Fisch-Scanner auf Basis der getaggten Archivfotos |
| Letterboxd, Strava "Year in Sport" | Persönlicher Rückblick als teilbares Erlebnis | Rückblick für Altmitglieder, später "Mein Becken-Jahr" |
| Duolingo | Serie mit Freeze; eine Serie rettet kein schwaches Produkt | Serie in Wochen, Urlaubs-Freeze, abschaltbar |
| Planta, Greg | Pflegeplan entsteht automatisch; erfahrene Halter antworten schnell | Pflegeplan aus Becken und Besatz |
| Printables | Nachbauten ("Makes") verlinken das Original | Später: "nach Becken X eingerichtet" |
| Discogs, Rebrickable | Sammlung und Wunschliste am Katalog | Fish Log mit "gehalten", "gezüchtet", "Wunsch" |

Bekannte Fehlentwicklungen bei solchen Plattformen:

- Punkte für Menge belohnen Masse ohne Wert (Erfahrungen bei Printables und SAP Community).
- Tages-Serien ohne Ausweg erzeugen Druck. Bei Lebewesen kann das zu übereilter Pflege führen.
- Zu strenge Kompatibilitätswarnungen kosten Vertrauen (Kritik an PCPartPicker).
- Gamification ohne nützlichen Kern hält niemanden (Aussage des Duolingo-Retention-Teams).
- Reine Werkzeuge ohne öffentliches Ergebnis wachsen nicht über Empfehlungen.

---

## 4. Aquaristik-Wettbewerb

**Tracking-Apps** (Aquarimate, AquaticLog, Fishi, Aquarium Note, neue KI-Apps wie App-aquatic) sind
Logbücher ohne Community. Wiederkehrende Beschwerden in Rezensionen: Datenverlust nach Updates,
Paywalls, nervende Erinnerungen, zu dünne Artdaten mit viel Handeingabe.

**Communities** (Cichlid-Forum.com, Monster Fish Keepers, Reef2Reef, Planted Tank, UKAPS, Reddit,
Facebook-Gruppen) sind Foren ohne mobiles Logging. Halter behelfen sich mit Tabellen, Kalendern und
privaten Blogs pro Becken. Build-Threads und Journale sind das beliebteste Format, ihre Pflege ist
mühsam.

**Artendatenbanken** (Cichlid Room Companion, Seriously Fish, AqAdvisor) sind entweder
abopflichtig, ohne Bezug zum eigenen Becken oder veraltet. AqAdvisor erklärt seine Bewertung des
Besatzes nicht.

Belegte Lücken, die bei Cichliden besonders zählen:

- **Tracking und Community in einem Produkt** bietet keine der gefundenen Apps.
- **Kompatibilität mit Begründung** für territoriale Arten (Mbuna, Haremshaltung, Seenbecken) fehlt.
- **Zuchtprotokolle** (Maulbrüter-Tage, Gelege, Jungfische) werden in Foren nachgefragt; keine App
  deckt sie ab.
- **Geführter Zeitraffer:** Ratgeber empfehlen, wöchentlich von derselben Position zu fotografieren.
  Keine App hilft beim Ausrichten.
- **Vertrauenswürdiger Tausch:** Betrugswarnungen sind in Kleinanzeigen-Foren ein Dauerthema.

---

## 5. Was die geretteten Daten hergeben

Die Zahlen stammen aus Abfragen gegen die Legacy-Datenbank `cichlids_typo3` und einer Stichprobe von
400 Originalbildern.

### Nutzbar ab dem ersten Tag

- **Persönliche Historie:** 4.689 Mitglieder haben Fotos, 257 davon mehr als 100. Zu jedem Foto gibt
  es Aufrufe (14.993 Fotos über 1.000) und erhaltene Kommentare. 2.170 Mitglieder besitzen
  mindestens ein Becken.
- **Artenfotos:** 54.039 Fotos haben eine Artzuordnung über 507 Arten; 116 Arten haben mindestens
  100 Fotos. Das reicht für Fish Log, Artengalerien und einen Prototyp der Artenerkennung.
- **Wissen:** Rund 92.000 Kommentare enthalten eine Frage, dazu 14.447 Forumsthreads mit 65.545
  Beiträgen.
- **Zeitraffer für ausgewählte Becken:** 80 bis 87 % der Originale haben ein EXIF-Aufnahmedatum.
  Damit haben 178 Becken mindestens 5 datierte Fotos, 21 davon über mehr als ein Jahr und 6 über
  mehr als drei Jahre. Eine Zuordnung über den Besitzer findet rund 150 weitere Mitglieder mit
  mindestens 10 Fotos über mehr als ein Jahr.
- **Becken-Datenblätter:** 4.969 sichtbare Becken, alle mit Biotop-Kategorie, 48 % mit Maßen, 91 %
  mit Hauptbild; Technik, Substrat und Pflanzen als Freitext.

### Nicht vorhanden

- **Besatz:** in allen 4.990 Becken leer.
- **Haltungsdaten der Arten:** Aggression, Endgröße und Wasserwerte sind praktisch leer.
- **Pflegedaten, Follows, Favoriten:** keine.
- **Frische Inhalte:** Seit 2020 haben sich rund 450 Mitglieder angemeldet; 2023 kamen 24 Fotos hinzu.

### Folgerungen

- Der Kompatibilitäts-Check verlangt eine eigene Kuration der Artdaten. Sie ist der größte
  Einzelposten dieses Zielbilds.
- Die Mitglieder kommen überwiegend aus den USA (6.716 von 10.400 mit Länderangabe; 71 aus
  Deutschland), die Kommentare sind fast vollständig englisch. Die App ist von Beginn an
  mehrsprachig mit Englisch als Standardsprache. Maßeinheiten und Beckengrößen richten sich nach dem
  Land (Abschnitt 7).
- Das Archiv enthält ausschließlich Fotos. Videos entstehen erst in der neuen App.
- Das Archiv erscheint in der App als kuratierte Geschichte der Plattform. Eine Sortierung nach
  Upload-Datum würde es als aktuellen Stream ausgeben.

---

## 6. Die fünf Kernerlebnisse

### 6.1 Willkommen zurück: der persönliche Rückblick

Der Rückblick gibt es in zwei Formen. Beide nutzen dieselbe Story-Darstellung und lassen sich
jederzeit unter "Ich" erneut aufrufen.

**Archiv-Rückblick** für Altmitglieder, einmalig beim ersten Login über die Kampagnen-Mail:

- seit wann das Mitglied dabei war, wie viele Fotos es hochgeladen und wie viele Kommentare es
  bekommen hat,
- sein meistgesehenes Foto mit Aufrufzahl,
- seine Becken aus dem Archiv mit dem Angebot, sie zu übernehmen,
- zum Schluss die Frage "Wie sieht dein Becken heute aus?", die direkt zum ersten neuen Becken mit
  erstem Foto führt.

Eine teilbare Karte im Hochformat fasst den Archiv-Rückblick zusammen. Er macht aus der Kampagne
eine persönliche Einladung und kostet das Mitglied keine Mühe.

**Rückkehr-Rückblick** für Mitglieder, die 4 Wochen oder länger nicht in der App waren, höchstens
einmal pro Quartal. Er zeigt, was in der Zwischenzeit passiert ist: überfällige Pflege der eigenen
Becken, erhaltene Kudos und Antworten, Neues aus gefolgten Becken und aus den eigenen Regionen. Am Ende stehen die nächsten Schritte, etwa
versäumte Pflege erfassen oder ein aktuelles Foto für die Zeitmaschine aufnehmen. Archivbezogene Teile
wie die Übernahme von Altbecken kommen darin nicht vor.

Daten: für den Archiv-Rückblick vollständig vorhanden, für den Rückkehr-Rückblick aus dem
Pflege-Ereignisstrom. Aufwand: mittel.

### 6.2 Das Becken als lebendes Projekt

Das Herzstück aus Epic 1 und 2 der Vision:

- **Becken-Seite nach der Hi-Fi-Vorlage** mit Hero, Umschalter Story/Pflege, Kennzahlen,
  Time-Machine und Timeline mit Höhepunkten.
- **Quick-Log:** Ein Tap erfasst den Wasserwechsel; Messwerte lassen sich optional ergänzen.
- **Pflegeplan mit Begründung:** Der Rhythmus folgt aus Volumen und Besatz ("alle 7 Tage, weil 12
  Mbuna in 240 l"), sobald Artdaten vorliegen.
- **Erinnerungen in Maßen:** eine Push-Nachricht am Fälligkeitstag, Schlummern möglich.
- **Serie in Wochen** mit Urlaubs-Freeze und Heatmap, in den Einstellungen abschaltbar.
- **Geführte Zeitraffer-Kamera:** Das letzte Foto liegt halbtransparent über dem Sucher, damit jede
  Aufnahme gleich ausgerichtet ist. Aus der Fotoserie entsteht automatisch ein Zeitraffer-Video.
- **Altbecken übernehmen:** Das Mitglied bestätigt sein Becken aus dem Archiv und ordnet
  vorgeschlagene Fotos zu, sortiert nach Aufnahmedatum.
- **Fische mit Namen:** Viele Halter, besonders in den USA, geben einzelnen Fischen Namen und
  behandeln sie wie Haustiere. Ein einzelner Fisch bekommt deshalb ein eigenes Profil mit Name,
  Art, Fotos, Videos und einer eigenen Timeline: Einzug, Wachstum, Laichen, Umzug in ein anderes
  Becken, Jahrestage. Stirbt der Fisch, bleibt sein Profil als Erinnerung erhalten. Einträge im
  Becken lassen sich einzelnen Fischen zuordnen und erscheinen dann in beiden Timelines.

Daten: Pflegedaten entstehen neu. Die Vorzeige-Becken aus dem Archiv brauchen das EXIF-Datum im ETL
(im Review zu R1 als eigenes Feature vermerkt). Aufwand: groß.

### 6.3 Der Fisch-Scanner

Ein Foto des Fischs liefert die drei wahrscheinlichsten Arten mit Angabe der Sicherheit. Ist das
Ergebnis unsicher, nennt der Scanner nur die Gattung. Ein Tap legt die Art in den Besatz und ins
Fish Log.

Technik: Ein Bild-Embedding-Modell vergleicht das Foto mit den 54.000 getaggten Archivfotos
(Nächste-Nachbarn-Suche, ohne eigenes Training). Das passt in den entkoppelten KI-Dienst aus
ADR-0011.

Fische lassen sich schwer fotografieren, ein kurzes Video gelingt dagegen fast immer. Der Scanner
nimmt deshalb auch Videos an: Er wählt mehrere scharfe Einzelbilder aus verschiedenen Blickwinkeln
und fasst deren Ergebnisse zusammen.

Vorbehalt: Die Artzuordnungen haben Mitglieder gesetzt; Farbformen und Hybriden verwischen die
Grenzen. Ein Spike parallel zum Deployment misst deshalb die Top-3-Trefferquote auf zurückgehaltenen
Fotos der rund 116 häufigen Arten, einmal mit Einzelfotos und einmal mit mehreren Bildern derselben
Aufnahme. Er ermittelt außerdem die Kosten je Aufruf des Vision-Modells. Vor Slice 6 wiederholt ein
kurzer zweiter Durchlauf die Messung mit dem dann aktuellen Stand der Modelle. Erst ein
überzeugendes Ergebnis bringt den Scanner in den Launch. Andernfalls erscheint die App ohne Scanner,
mit Artauswahl von Hand, und der Scanner folgt per Update.

**Zusätzliche Tipps über ein Vision-Modell aus der Cloud.** Auf Wunsch des Mitglieds schickt der
KI-Dienst das Bild an ein günstiges, eingekauftes Vision-Modell. Es ergänzt das Ergebnis um
Hinweise wie Geschlecht, Farbform und passende Haltungstipps aus dem Artenprofil. Krankheitsbefunde
gehören nicht dazu (Abschnitt 7). Wegen der Kosten gelten Grenzen:

- Aufruf nur nach einem ausdrücklichen Tap, nie automatisch bei jedem Scan.
- Kontingent je Mitglied und Zeitraum, dazu ein Gesamtbudget, bei dessen Erreichen die Funktion
  pausiert.
- Ergebnisse werden je Bild zwischengespeichert, damit dasselbe Bild nur einmal Kosten verursacht.
- Der API-Schlüssel liegt nur im Backend; der Anbieter ist im KI-Dienst austauschbar.
- Bilder gehen ohne EXIF- und Ortsdaten an den Anbieter; die Datenschutzhinweise nennen ihn.

Aufwand: Spike klein, Produktfunktion mittel, Vision-Modell klein.

### 6.4 Fish Log und Artenseiten

- **Fish Log, die persönliche Artenliste:** gehalten, gezüchtet, Wunschliste. Die Zahlen darin
  belohnen gute Haltung:
  - Haltungsdauer je Art und je Fisch mit Namen ("Frontosa seit 6 Jahren", "Bruno ist 5");
    Jahrestage sind Anlass für eine Gratulation vom System.
  - Artreine Nachzucht je Art mit Generation (F1, F2). Hybriden und die Zahl der Jungfische zählen
    nicht, damit niemand Kreuzungen oder Massenvermehrung fördert.
  - Erfahrung je Art aus Haltungsdauer, Nachzucht und als hilfreich markierten Antworten zu dieser
    Art, sichtbar an Antworten ("erfahren mit Tropheus moorii").
  - Die Gesamtzahl gehaltener Arten steht als schlichte Angabe im Profil.

  Das Fish Log setzt keine Mengenziele und zeigt keinen Fortschritt je Region: Ein Sammelziel
  belohnt bei Lebewesen Spontankäufe, Überbesatz und das Mischen von Regionen.
- **Artenseite als Gemeinschaftsseite** nach dem Ravelry-Prinzip: die besten Fotos (sortiert nach
  Aufrufen und Kommentaren), die Zahl der Becken mit dieser Art, Besatzpartner aus echten Becken,
  Fragen und Antworten aus dem Archiv, Halter mit Nachzucht.
- **Artenkarte wie im Sammelkartenspiel:** Jede Art erscheint als Karte, deren Merkmale wie die
  Werte einer Spielkarte dastehen: Wasserwerte, Größe, Raubverhalten, Aggression, Ernährung,
  Sozialstruktur und die übrigen Merkmale der Artdatenbank. Hinter jedem Merkmal stehen
  Kurzfassung und Begründung, natürlicher Bereich und Toleranz, die Sicherheit, die Angaben der
  einzelnen Quellen und jede Quelle als Link. Diese Offenheit macht die Daten glaubwürdig und
  zeigt, woher sie stammen. Wie die Agenten einen Wert entschieden haben (Stimmen, Tie-Breaker,
  abweichende Vorschläge), sehen nur Administratoren; die öffentliche API liefert diese Angaben
  nicht aus.
- **Besatz-Planer mit Kompatibilitäts-Hinweisen** nach dem PCPartPicker-Prinzip. Jeder Hinweis hat
  eine Begründung und eine Quelle und blockiert nichts. Dieselben Hinweise erscheinen beim Anlegen
  eines Beckens im Onboarding, sobald der Besatz eine bekannte Unverträglichkeit enthält.

Daten: Fotos und Archiv sind vorhanden, Haltungsdaten fehlen.

#### Artenprofil für jede Art

Jede Art im Katalog bekommt ein vollständiges Profil mit allem, was ein künftiger oder heutiger
Halter wissen muss:

- Namen: gültiger wissenschaftlicher Name, Synonyme, Handelsnamen je Sprache, bekannte Standort-
  und Farbformen.
- Herkunft und Biotop: Gewässer, Lebensraum, Wasserchemie am Fundort.
- Größe und Alter: Endgröße getrennt nach Geschlecht, Lebenserwartung, Wachstum.
- Haltung: Mindestbeckengröße mit Grundfläche, Einrichtung, Temperatur, pH, GH und KH.
- Sozialverhalten: Aggression, Revierverhalten, Gruppenstruktur (Paar, Harem, Gruppe,
  Geschlechterverhältnis), artreine Haltung, wo nötig.
- Ernährung: Ernährungstyp, geeignetes Futter, typische Fütterungsfehler.
- Zucht: Brutpflegetyp (Maulbrüter, Substratlaicher, Höhlenbrüter), Ablauf, Dauer, Aufzucht.
- Geschlechtsunterschiede.
- Verträglichkeit: geeignete und ungeeignete Partner mit Begründung (Abschnitt "Verträglichkeit
  in der Artdatenbank").
- Häufige Probleme und Krankheitsrisiken, etwa Malawi-Bloat bei falscher Fütterung.
- Eignung für Einsteiger.
- Aus dem Archiv: die besten Fotos, Becken mit dieser Art, Fragen und Antworten.

Die Profile entstehen über Agenten, die drei Quellen zusammenführen: ihr eigenes Fachwissen,
Forenbeiträge und Fachliteratur sowie die eigenen Archivinhalte von cichlids.com. Jede Angabe nennt
ihre Herkunft. Beiträge aus dem Forumsarchiv erscheinen als Erfahrungen der Community nach den
Fachquellen; einen Wert belegen sie nur, wenn keine Fachquelle ihn nennt, und dann mit niedriger
Sicherheit. Fremde Texte dienen als Beleg für Fakten; die Profiltexte sind eigene
Formulierungen, weil Datenbanken wie Seriously Fish oder Cichlid Room Companion und Forenbeiträge
urheberrechtlich geschützt sind. Aus fremden Quellen übernehmen die Agenten ausschließlich Fakten und
Zahlen mit ihrer Quelle. Zu jedem Wert speichert das Profil, welche Quelle welche Angabe macht, und die
Artenkarte zeigt diese Angaben nebeneinander, etwa "FishBase: 24 bis 26 °C, Seriously Fish: 23
bis 27 °C". Die Merkmale der Verträglichkeitsprüfung liegen für alle Arten im
Katalog vollständig vor. Zum Launch haben 80 % der Arten ein ausformuliertes Profil; die Reihenfolge
folgt der Häufigkeit im Archiv.

#### Prüfverfahren

Das Fachwissen großer Sprachmodelle übertrifft das einzelner Halter, auch erfahrener. Die Prüfung
der Profile liegt deshalb vollständig bei Agenten. Harte Merkmale brauchen mehr Belege und mehr
Stimmen als die übrigen.

1. **Erzeugung:** Ein Agent ermittelt je Art alle Merkmale mit Quelle und Begründung.
2. **Unabhängige Gegenprüfung:** Mehrere Agenten mit unterschiedlichen Modellen oder Prompts
   bestimmen dieselben Werte, ohne die erste Fassung zu kennen.
3. **Abgleich:** Übereinstimmende Werte gelten als belegt. Bei Abweichungen in mittleren und weichen
   Merkmalen wägt ein schlichtender Agent Begründungen und Quellen ab und entscheidet mit Begründung.
4. **Tie-Breaker für harte Merkmale:** Raub, Ernährung, Kreuzungsgruppe und artreine Haltung brauchen
   je Wert mindestens zwei unabhängige externe Quellen. Ein Wert ist gesichert, wenn Erzeugung und
   beide Gegenprüfungen übereinstimmen. Weichen sie ab, bestimmen zwei weitere unabhängige Agenten
   nur die strittigen Werte, und ein Wert mit mindestens vier von fünf Stimmen ist gesichert. Kommt
   diese Mehrheit nicht zustande, gilt der vorsichtigste der vorgeschlagenen Werte mit niedriger
   Sicherheit, also der Wert, der die strengeren Hinweise der Verträglichkeitsprüfung auslöst. Ein
   Skript zählt die Stimmen, damit jede Entscheidung wiederholbar ist.
5. **Wasserwerte aus dem Lebensraum:** Temperatur, pH, GH und KH folgen den Messungen am natürlichen
   Fundort. Für die Arten des Malawi-, Tanganjika- und Victoriasees gelten die Messwerte des Sees.
   Sie sind je See einmal recherchiert und gegengeprüft und stehen in einer eigenen Tabelle; der
   Malawisee etwa hat weiches, alkalisches Wasser. Haltungsanleitungen zählen für Wasserwerte nicht
   als Quelle, weil sie Härte und Alkalität oft gleichsetzen. Arten anderer Gewässer erhalten die
   Messwerte ihres Fundorts aus der Fachliteratur, Hybriden und Zuchtformen die ihrer Elternarten.
   Zusätzlich hat jeder Wasserwert einen Toleranzbereich: die Spanne, in der die Art nach
   Fachliteratur und Haltungserfahrung dauerhaft gesund bleibt. Für ihn zählen Haltungsanleitungen
   als Quelle. Er umfasst den natürlichen Bereich und wird wie die harten Merkmale abgestimmt.
6. **Plausibilitätsprüfungen:** Automatische Prüfungen markieren Ausreißer, etwa Wasserwerte, die
   stark von nah verwandten Arten desselben Gewässers abweichen, oder eine Endgröße, die nicht zum
   Mindestbecken passt. Die fachlichen Unit-Tests der Verträglichkeitsprüfung laufen bei jeder
   Änderung mit.

Jede Angabe speichert Herkunft, Begründung und Version, damit spätere Korrekturen nachvollziehbar
bleiben.

Den Erstbestand erzeugen parallele Subagents in Claude Code. Die Profile liegen als versionierte
Datendateien im Repository, je Art eine Datei mit Quelle je Angabe, und ein Import lädt sie in die
Datenbank. Neue Arten nach dem Erstbestand pflegt das Team von Hand. Die Pipeline läuft in
Abständen über den ganzen Bestand, damit neue Beiträge der Community und neue Literatur in die
Profile einfließen; dabei gilt dasselbe Verfahren mit Gegenprüfung und Tie-Breakern.

#### Fachartikel

Fachartikel erklären Zusammenhänge, die über eine einzelne Art hinausgehen, etwa das Wasser des
Malawisees und warum Haltungsanleitungen davon abweichen. Agenten schreiben sie mit Quellen je
Aussage. Zwei unabhängige Agenten prüfen jede Aussage gegen ihre Quellen, ein schlichtender Agent
entscheidet Abweichungen. Die Artikel liegen wie die Profile als versionierte Dateien im
Repository und folgen denselben Regeln zum Urheberrecht. Artenseiten verlinken die passenden
Artikel, und die KI-Erstantwort greift über die Wissensdatenbank auf sie zu.

#### Taxonomie auf aktuellem Stand

Die Systematik der Buntbarsche ändert sich laufend; viele Archivnamen sind überholt. Beispiele:
Der Jack Dempsey heißt Rocio octofasciata (Synonym Cichlasoma octofasciatum), der Zebrabuntbarsch
Amatitlania nigrofasciata (Synonyme unter Archocentrus und Cryptoheros), der Demasoni Chindongo
demasoni (Synonym Pseudotropheus demasoni). Bei Maylandia und Metriaclima sind beide Gattungsnamen in
Gebrauch.

- Maßgeblich für gültige Namen und Synonyme ist der Catalog of Fishes der California Academy of
  Sciences. Jede Art speichert, auf welchem Stand dieser Quelle ihr Name beruht.
- Alte Namen bleiben als Synonyme erhalten. Suche, Scanner und Weiterleitungen alter Links finden
  die Art auch unter ihnen.
- Die Artzuordnungen der Archivfotos werden auf die gültigen Namen abgebildet. Fallen zwei
  Archiv-Arten heute zusammen, werden sie zusammengeführt; wurde eine Art aufgeteilt, entscheidet
  die Standortform über die Zuordnung, sonst bleibt die Zuordnung auf Gattungsebene.
- Unbeschriebene Arten mit Handelsbezeichnung (etwa "sp. 'Fundort'") sind eigene Einträge mit
  diesem Status.
- Zuchtformen und Hybriden (etwa Flowerhorn oder Blood Parrot) sind als solche gekennzeichnet und
  keine Arten.

#### Verträglichkeit in der Artdatenbank

Viele Unverträglichkeiten sind in der Aquaristik bekannt, etwa dass mittelamerikanische Buntbarsche
nicht zu Tropheus passen. Die Artdatenbank beschreibt jede Art als Vektor ihrer Merkmale. Ein
einfaches Ähnlichkeitsmaß über den ganzen Vektor genügt nicht:

- Zu ähnliche Arten sind ebenfalls ein Problem: Nah verwandte, gleich gefärbte Arten kreuzen sich
  und bekämpfen sich wie Rivalen der eigenen Art.
- Wasserwerte sind Bereiche, die sich überlappen müssen.
- Raubverhalten ist einseitig: Der große Fisch frisst den kleinen.
- Die Beckengröße entscheidet mit, etwa bei zwei aggressiven Arten.

Jede Art hat diese Merkmale; jeder Wert hat eine Quelle und eine Sicherheit:

| Merkmal | Form |
|---|---|
| Temperatur, pH, GH | Bereich (min, max) |
| Endgröße | cm, das größere Geschlecht |
| Raubverhalten | Stufe 0 bis 3, von "frisst keine Fische" bis "ausgeprägter Fischfresser" |
| Ernährungstyp | Aufwuchs, Allesfresser, Wirbellose, Fische; dazu "Bloat-empfindlich" |
| Aggression gegen Artgenossen | Stufe 0 bis 4 |
| Aggression gegen andere Arten | Stufe 0 bis 4 |
| Revierbedarf | keiner, klein, groß; dazu "nur zur Brutzeit" |
| Lebensraumzone | Menge aus Fels, Sand, Freiwasser, Schneckenhaus, Pflanzen und Holz |
| Sozialstruktur | Paar, Harem mit Verhältnis, Gruppe mit Mindestzahl, einzeln; dazu "artrein empfohlen" |
| Herkunft | Hierarchie aus Kontinent, Region oder See und Fundort |
| Kreuzungsgruppe | kuratierte Gruppe nah verwandter, kreuzbarer Arten |
| Färbung | Grundfarbe und Muster (Balken, Streifen, einfarbig) |
| Fresstempo | langsam, normal, hastig |
| Mindestbecken | Volumen und Länge |

Die Aggression ist zweigeteilt, weil manche Arten wie Tropheus untereinander rabiat und gegen
andere Arten meist ruhig sind. Jede Stufe hat eine feste Beschreibung, damit Agenten und Prüfer sie
gleich vergeben.

Die Prüfung vergleicht je Artenpaar jedes Merkmal mit einer eigenen Konfliktfunktion, die einen
Wert zwischen 0 und 1 liefert:

| Merkmal | Konfliktfunktion |
|---|---|
| Wasserwerte (Temperatur, pH, GH) | Überlappung der natürlichen Bereiche und der Toleranzbereiche |
| Ernährungstyp | Konfliktmatrix, etwa Aufwuchsfresser neben Fleischfresser |
| Größe und Raubverhalten | einseitig: Passt der kleinere Fisch ins Maul des größeren? |
| Aggression und Revierbedarf | Differenz, abgeschwächt durch Beckengröße und Lebensraumzone |
| Ähnlichkeit (Gattung, Kreuzungsgruppe, Färbung) | Konflikt bei zu großer Ähnlichkeit |
| Herkunft | gering gewichtet, stärker bei gewünschtem Biotopbecken |

Es gibt drei Hinweisstufen, keine davon blockiert:

- **Hinweis:** gut zu wissen, etwa "beide revierbildend, mit genug Versteckmöglichkeiten machbar".
- **Achtung:** hängt von Bedingungen ab, etwa "geht ab 150 cm Beckenlänge".
- **Warnung:** bekannt problematisch, etwa Wasserwerte, deren Toleranzbereiche sich nicht überlappen.

Jedes Merkmal hat eine Härteklasse. Sie legt fest, bis zu welcher Stufe ein Konflikt in diesem
Merkmal allein führen darf:

| Härte | Allein höchstens | Merkmale |
|---|---|---|
| Hart | Warnung | Toleranzbereiche der Wasserwerte ohne gemeinsamen Bereich, Raub, Kreuzungsgruppe, "artrein empfohlen", Bloat-empfindlicher Aufwuchsfresser neben Fleischfresser |
| Mittel | Achtung | Aggression gegen andere Arten, Revierbedarf, natürliche Bereiche der Wasserwerte ohne gemeinsamen Bereich, Mindestbecken |
| Weich | keine Stufe, nur Beitrag zur Summe | Färbung, Herkunft, Fresstempo, Lebensraumzone |

Harte Konflikte enden für die Fische meist schlecht, egal wie gut der Rest passt. Mittlere hängen
von Beckengröße und Einrichtung ab. Weiche Merkmale sind allein kein Problem, verstärken aber andere
Konflikte, etwa gleich gefärbte aggressive Revierbildner. Legt ein Mitglied sein Becken als
Biotopbecken an, gilt die Herkunft als mittleres Merkmal.

Die Stufe eines Paars ist die höhere von zwei Stufen: der aus der gewichteten Summe aller
Konfliktwerte und der aus dem stärksten Einzelkonflikt, begrenzt durch seine Härteklasse. So
verwässert die Summe keinen einzelnen schweren Konflikt. Prüfungen auf
Becken-Ebene ergänzen die Paare: Mindestgröße und Grundfläche je Art, Gruppengrößen, Gesamtbesatz
und ausdrückliche Regeln wie "Tropheus nur artrein".

Die Mitglieder müssen jeden Hinweis nachvollziehen können. Deshalb zeigt jeder Hinweis:

- die Begründung in Alltagssprache mit den konkreten Werten beider Arten, etwa "Diskus braucht pH
  5,5 bis 7, Malawi-Buntbarsche 7,8 bis 8,6; die Bereiche überschneiden sich nicht",
- welche Merkmale das Ergebnis wie stark bestimmen, auf Tap aufgeklappt,
- die Quelle jeder Angabe mit Link ins Artenprofil,
- wo möglich einen Ausweg, etwa eine verträgliche Alternative oder die nötige Beckengröße.

Die Gewichte entstehen in drei Schritten: zuerst von Hand aus Fachwissen, dann abgeglichen an einem
Satz bekannter guter und schlechter Kombinationen, später an echten Becken in der App, deren
Besatz lange stabil bleibt.

Fachliche Unit-Tests sichern das Verfahren ab. Jeder Test beschreibt eine bekannte Kombination mit
der erwarteten Hinweisstufe und dem erwarteten Grund, zum Beispiel:

- Tropheus moorii mit Amphilophus citrinellus: Warnung wegen Ernährung, Aggression und artreiner
  Haltung.
- Maylandia estherae mit Maylandia zebra: Warnung wegen Kreuzungsgefahr.
- Cyphotilapia frontosa mit Neolamprologus multifasciatus: Warnung, weil der Frontosa die kleinen
  Schneckenbuntbarsche frisst.
- Symphysodon (Diskus) mit Malawi-Buntbarschen: Warnung wegen der Wasserwerte.
- Labidochromis caeruleus mit Cyrtocara moorii: kein Hinweis.

Die Tests prüfen neben der Stufe auch, dass die angezeigte Begründung die erwarteten Merkmale nennt.
Jede Änderung an Merkmalen, Gewichten oder Schwellen muss diese Tests bestehen.

Aufwand: Fish Log und Artenseiten mittel, Artenprofile groß (überwiegend Datenarbeit).

### 6.5 Fragen mit Beckenkontext

- **Frage an die Runde** aus jedem Foto oder Pflege-Eintrag mit einem Tap. Beckengröße, Besatz,
  letzte Werte und letzter Wasserwechsel hängen automatisch an, damit Helfer nicht nachfragen müssen.
- **Archiv-Antworten zuerst:** Während des Tippens erscheinen passende Antworten aus 92.000
  Archiv-Fragen und 14.000 Threads.
- **KI-Erstantwort:** Jede neue Frage bekommt innerhalb von Sekunden eine gekennzeichnete Antwort
  mit Quellenangaben. Sie entsteht per Retrieval-Augmented Generation (RAG) aus der
  Wissensdatenbank. Nur der Fragesteller sieht sie. Öffentlich sieht die Frage aus wie jede offene
  Frage und zieht menschliche Antworten an; Mitglieder, die antworten, stehen nie neben einer
  fertigen Maschinenantwort.
- **Prüfliste für Erfahrene:** Mitglieder mit Erfahrung bei der betreffenden Art (Fish Log) sehen
  KI-Antworten in einer eigenen Liste, zusammen mit den menschlichen Antworten im selben Thread, und
  bewerten sie. In der Anfangszeit übernimmt das Team die Prüfung. Die Bewertungen dienen
  ausschließlich dem Training des Modells (Abschnitt "Bewertung und Training").
- **"Hilfreich" nur für Menschen:** Nur Antworten von Mitgliedern lassen sich als hilfreich
  markieren. Eine KI-Antwort kann diese Markierung nicht erhalten, damit die Anerkennung bei den
  Mitgliedern landet, die geantwortet haben.
- **Wissensdatenbank:** Sie enthält die Archiv-Fragen und -Threads, die Artenprofile, die
  Fachartikel und neue Antworten von Mitgliedern, die als hilfreich markiert sind. Jeder Eintrag
  nennt seine Herkunft.
- **Archivfragen offen neu gestellt:** Fragen aus dem Archiv erscheinen im Feed mit Jahr und
  ursprünglichem Fragesteller ("2009 gefragt von ..."). Heutige Halter ergänzen eine aktuelle
  Antwort; der ursprüngliche Fragesteller erfährt davon, wenn er zurückkommt.
- **Interessen-Feed:** Fragen aus abonnierten Regionen (Malawi, Tanganjika, Amerika) erscheinen auf
  dem Startbildschirm.
- **Kudos als Reaktion** auf Fotos, Höhepunkte und gehaltene Serien. Die Altsterne erscheinen nicht
  in der Oberfläche.

#### Bewertung und Training

Die Prüfliste erfasst die Signale, die übliche Verfahren für das Fine-Tuning von Sprachmodellen
verwenden:

- **Daumen hoch oder runter** je KI-Antwort. Verfahren wie KTO lernen direkt aus solchen
  Einzelbewertungen.
- **Grund bei Daumen runter**, als Auswahl: sachlich falsch, gefährlich für die Tiere, veraltet
  (etwa überholte Artnamen), am Thema vorbei, Quellen passen nicht.
- **Vergleich mit den menschlichen Antworten:** Welche Antwort im Thread ist besser? Daraus entstehen
  Präferenzpaare für Verfahren wie DPO.
- **Korrigierte Fassung:** Eine verbesserte Antwort ist ein Musterbeispiel für überwachtes
  Fine-Tuning (SFT).
- **Passende Quellen:** ob die abgerufenen Dokumente zur Frage passen. Das verbessert die Suche im
  RAG getrennt vom Modell.

Zu jeder Bewertung speichert der KI-Dienst die Frage mit Beckenkontext, die abgerufenen Dokumente,
Modell- und Prompt-Version sowie die Erfahrung der bewertenden Person. Die bewerteten Antworten
dienen zugleich als Testsatz: Jede neue Modell- oder Prompt-Version muss auf ihnen mindestens so gut
abschneiden wie die vorherige.

#### Engagement in der Anfangszeit

Agenten treten nie als Mitglieder auf. Alles Automatische ist als solches gekennzeichnet:

- **Anerkennung vom System:** cichlids.com gratuliert sichtbar als Plattform zu Meilensteinen wie
  der ersten Pflegewoche, einer gehaltenen Serie oder einem Becken-Jahrestag.
- **Echte Aktivität bündeln:** Heute zeigt die frischen Einträge echter Mitglieder aus den eigenen
  Regionen prominent, mit Kudos per Tap.
- **Gründungsmitglieder:** aktive Altmitglieder und das Team, persönlich eingeladen und als
  Gründungsmitglieder erkennbar.

Aufwand: mittel, Wissensdatenbank und KI-Erstantwort groß.

---

## 7. App-Rahmen

### Navigation

| Tab | Inhalt |
|---|---|
| Heute | Fällige Pflege, eigene Serie, Neues aus gefolgten Becken, Fragen aus den eigenen Regionen |
| Entdecken | Kuratiertes Archiv, Vorzeige-Zeitraffer, Becken nach Region, Suche |
| + | Pflege loggen, Foto oder Video, Frage, Scanner |
| Fish | Fish Log mit eigenen Fischen und gehaltenen Arten, Artenseiten, Besatz-Planer |
| Ich | Eigene Becken, Profil, Rückblick, Einstellungen |

Galerie, Forumsarchiv und Artenliste gehen in Entdecken und Fish auf. Fragen haben keinen eigenen
Tab: Sie erscheinen in Heute, entstehen über + und hängen an Fotos und Einträgen, damit das Becken
im Mittelpunkt steht.

### Kaltstart

Entdecken präsentiert das Archiv offen als "Aus 20 Jahren cichlids.com": Hall of Fame je Region,
Becken der Woche aus dem Archiv, die Vorzeige-Zeitraffer. Abschnitte, die Aktualität zeigen (neue
Becken, aktive Mitglieder, Zähler), sind fertig entwickelt und liegen hinter einem Feature-Flag. Es
wird eingeschaltet, sobald genug Aktivität vorhanden ist.

Das Onboarding beginnt mit der Frage "Hast du ein Becken?":

- **Ja:** Das erste Becken entsteht in unter zwei Minuten, oder ein Altbecken wird übernommen. Die
  Region des Beckens bestimmt die Interessen; weitere lassen sich später ergänzen. Neben den
  Regionen gibt es die Wahl "gemischt", etwa für Malawi-Becken mit Mbuna und Haps.
- **Nein:** Das Mitglied wählt seine Interessen und erkundet Arten und Becken.

Der erste Eintrag wird sofort belohnt.

Die Größenvorschläge beim Anlegen des Beckens folgen den Standardbecken des Landes. In den USA sind
das Gallonen-Größen mit festen Maßen (10, 20 long, 29, 40 breeder, 55, 75, 90, 125 Gallonen), in
Europa Liter-Größen nach den dort üblichen Kantenlängen. Ein Standardbecken bringt seine Maße mit,
damit Hinweise zur Grundfläche ohne weitere Eingabe möglich sind.

### Sprache und Einheiten

Die App ist von Beginn an mehrsprachig. Alle Texte kommen ab dem ersten Screen aus
Übersetzungsdateien, Englisch ist Standard- und Rückfallsprache. Die Einstellung folgt der Sprache
des Geräts und lässt sich ändern. Zahlen- und Datumsformate, Pluralformen und Artnamen
(Handelsnamen je Sprache) folgen der Spracheinstellung; die Artenprofile erscheinen in allen
App-Sprachen.

Beiträge von Mitgliedern (Fragen, Antworten, Kommentare) und Archivinhalte erscheinen automatisch
in der Sprache des Lesers, damit über die Sprachen hinweg eine gemeinsame Community entsteht. Ein
Hinweis wie "maschinell übersetzt aus dem Portugiesischen" steht dabei, das Original ist einen Tap
entfernt. Übersetzt wird beim ersten Abruf; das Ergebnis wird je Beitrag und Sprache
zwischengespeichert. In den Einstellungen lässt sich die automatische Übersetzung abschalten.

Zum Launch erscheint die App in Englisch, Spanisch (Lateinamerika), Portugiesisch (Brasilien),
Deutsch, Französisch, Italienisch, Polnisch, Niederländisch, Tschechisch, Indonesisch und Türkisch.
Brasilien und Mittelamerika sind Heimat vieler Buntbarsch-Arten, Polen und Tschechien haben starke
Züchterszenen, Indonesien ist ein großer Zierfisch-Exporteur. Die Übersetzungen entstehen
maschinell; Rechtstexte und Store-Einträge prüft vor der Veröffentlichung ein Mensch.

Das Layout unterstützt Sprachen mit Schreibrichtung von rechts nach links, etwa Arabisch, Persisch
und Hebräisch, auch wenn noch keine davon angeboten wird.

Einheiten sind getrennt von der Sprache einstellbar und richten sich standardmäßig nach dem Land
des Geräts:

| Größe | Metrisch | US |
|---|---|---|
| Volumen | Liter | US-Gallonen |
| Länge | cm | Zoll |
| Temperatur | °C | °F |
| Härte | °dH | ppm |

Das Backend speichert Werte in metrischen Einheiten und zusätzlich die Eingabeeinheit. So erscheint
ein als 55 Gallonen angelegtes Becken immer als 55 Gallonen, ohne Rundungsfehler aus der
Umrechnung.

### Fotos und Videos

Fische lassen sich schwer fotografieren, mit dem Handy aber leicht filmen. Wo Mitglieder anderen
etwas zeigen und Bewegung dabei hilft, nimmt die App neben Fotos auch kurze Videos an: Videos der
eigenen Fische und ihrer Profile, Becken-Videos, Fragen (etwa um Verhalten oder Atmung zu zeigen),
Artenseiten und Entdecken. Der Scanner nimmt Videos als Eingabe. Zeitmaschine, Zeitraffer und
Timeline bestehen aus Fotos.

Aus jedem Video schlägt die App die schärfsten Einzelbilder als Foto vor. Das ist für viele Halter
der einfachste Weg zu einem guten Fischfoto.

Die Plattform rechnet Videos selbst in HLS um und liefert sie von Hetzner aus, getrennt von den
Bildern ([ADR-0012](docs/adr/0012-video-transcoding-and-adaptive-delivery.md)). Ein externer
Video-Dienst bleibt eine spätere Option. Uploads vom Handy lassen sich nach einem
Verbindungsabbruch fortsetzen.

Speicher und Auslieferung von Videos kosten ein Vielfaches von Fotos. Deshalb gelten Grenzen:

- Länge höchstens 60 Sekunden; das reicht für Balz, Revierkampf, Atmung oder einen Schwenk über das
  Becken.
- Auflösung bis 1080p, ausgeliefert adaptiv in 480p, 720p und 1080p. Höher aufgelöste Originale
  speichert die Plattform nicht.
- Der Ton bleibt erhalten und lässt sich beim Hochladen entfernen. In Listen spielen Videos stumm
  ab.
- Videos für den Scanner werden nach der Auswertung gelöscht, außer das Mitglied legt sie bewusst
  bei einem Fisch oder Becken ab.

### Gestaltung

Alle Screens folgen der Hi-Fi-Vorlage und dem Design-System: randlose Fotos, Dark-Mode "Biotop",
Bewegung an ausgewählten Stellen (Time-Machine, Rückblick). Bei Plattformen wie Letterboxd ist das
Design selbst ein Grund, die App zu öffnen.

### Außerhalb des Launch-Umfangs

- **Marktplatz und Tausch:** hohes Betrugs- und Rechtsrisiko.
- **KI-Krankheitsdiagnose:** Eine falsche Empfehlung kann Fische töten; die vorhandenen Apps dieser
  Art sind nicht validiert.
- **Ranglisten, Ligen, Tages-Serien und Sternebewertungen.**
- **Zuchtprotokoll** (Maulbrüter-Zähler, Gelege, Jungfische): eine echte Cichliden-Lücke und der
  erste Kandidat nach dem Launch.

---

## 8. Technische Folgen

- **Weiterverwendet:** API-Grundgerüst, ETL, Medien-Pipeline, Keycloak, Outbox, Client-Generierung.
  Die Befunde aus `REVIEW-MVP.md` gelten für die Relaunch-App; die hohen sind vor dem Launch zu beheben.
- **Neu im Backend:**
  - Pflege-Ereignisstrom (Logs, Messungen mit Herkunft, Höhepunkte; Produktvision, Abschnitt 8.2)
  - Pflegeplan und Push-Erinnerungen (Expo Push)
  - Sichtbarkeit pro Eintrag
  - Kudos und Fragen
  - Berechnung des Rückblicks
  - Erzeugung teilbarer Karten und Zeitraffer-Videos
  - EXIF-Aufnahmedatum in ETL und `media_item`
  - Videos in der Medien-Pipeline: unterbrechbare Uploads, Transkodierung mit ffmpeg in HLS,
    Auslieferung von Hetzner, Vorschaubild, Auswahl scharfer Einzelbilder, Grenzen für Länge und
    Auflösung
  - Lokalisierung: Übersetzungen, Einheiten mit gespeicherter Eingabeeinheit, Standardbecken je Land
  - Automatische Übersetzung von Beiträgen mit Zwischenspeicher je Beitrag und Sprache
  - Artenprofile mit Quellenangaben je Angabe, Taxonomie mit Synonymen, Merkmalsvektoren und
    Verträglichkeitsregeln; Import der versionierten Profildateien, deren Provenienz (Läufe,
    Stimmen, Abweichungen der Agenten) nur über Admin-Endpunkte abrufbar ist
  - Fisch-Profile mit eigener Timeline
- **KI-Dienst (ADR-0011):** Embeddings, Scanner (Fotos und Videos), Suche nach Archiv-Antworten,
  Anbindung des Vision-Modells mit Kontingenten und Gesamtbudget, Wissensdatenbank mit RAG für die
  KI-Erstantwort, Erfassung der Bewertungen als Trainings- und Testdaten.
- **Feature-Flags** für Funktionen, die erst ab einer gewissen Aktivität sinnvoll sind.
- **Mobile:** neu aufgebaut nach dem Design-System; Komponenten, Auth und Upload-Logik des MVP sind
  wiederverwendbar.
- **Plattformen:** Android und iOS sind gleichwertig, mit vollem Funktionsumfang, und erscheinen
  zum Launch gleichzeitig. Die iOS-Tests laufen auf einem eigenen MacBook als entferntem Runner
  (ADR-0006). Das Web erscheint zum Launch ebenfalls; sein Zuschnitt ist eine eigene Planung
  (Abschnitt 9). Wie bei Instagram und Facebook liegt dort das meiste hinter Login und
  Registrierung: Ohne Login sind Teaser zu sehen, für alles Weitere meldet sich der Besucher an.
  Manche Bereiche haben im Web einen eigenen Wert für Suchmaschinen, etwa Artenprofile und
  Fachartikel, andere passen als Web-App hinter dem Login.
- **Vorgehen:** Stories mit Given-When-Then im Backlog, Red-Green, grüner E2E-Pfad je Story
  (ADR-0003).

### Reihenfolge in vorführbaren Slices

1. **App-Rahmen:** Navigation mit Heute, Entdecken, +, Fish und Ich; Mehrsprachigkeit, lokalisierte
   Einheiten und RTL-fähiges Layout; Onboarding mit "Hast du ein Becken?" und Standardbecken je Land;
   Becken-Seite nach der Hi-Fi-Vorlage mit den Vorzeige-Zeitraffern aus dem Archiv; Entdecken mit den
   vorhandenen Bereichen hinter einem Feature-Flag.
2. **Pflege und Medien:** Quick-Log, Pflegeplan, Serie in Wochen, Erinnerungen; Fotos und Videos.
3. **Rückblicke:** Archiv-Rückblick, Rückkehr-Rückblick und Übernahme der Altbecken.
4. **Fish:** Fish Log mit Fischen mit Namen (Profil und eigene Timeline), Artenseiten und
   Besatz-Planer mit Verträglichkeitsprüfung; deren Hinweise erscheinen zusätzlich im Onboarding.
5. **Fragen:** KI-Erstantwort mit Wissensdatenbank und Prüfliste für Erfahrene, Archiv-Fragen,
   automatische Übersetzung, Kudos und Anerkennung durch das System.
6. **Scanner** mit dem Vision-Modell, sobald der Spike überzeugt.

Die Slices beginnen, sobald Staging mit dem MVP als erstem Workload läuft (O2 in
`PROJECT-STATUS.md`). Schon parallel zum Deployment laufen der Scanner-Spike, die Zuordnung der
Taxonomie und die Agenten-Pipeline für Merkmale, Artenprofile und Fachartikel. Die harten Merkmale aller Arten
sind vor Slice 4 gesichert.

Der Launch folgt nach Slice 6, mit allen fünf Kernerlebnissen einschließlich Scanner. Überzeugt
der Scanner bis dahin nicht, erscheint die App ohne ihn (Abschnitt 6.3).

---

## 9. Offene Entscheidungen

- Fine-Tuning: Zeitpunkt, Anbieter und Einwilligung der Mitglieder in die Nutzung ihrer Fragen als
  Trainingsdaten.
- Verträglichkeitsprüfung: Konfliktfunktionen im Detail, Startgewichte und Schwellen.
- Anbieter, Kontingent je Mitglied und Gesamtbudget des Vision-Modells.
- Zuschnitt des Webs: welche Teaser ohne Login sichtbar sind und wie viel davon Suchmaschinen
  erfassen (etwa bei Artenprofilen, Fachartikeln und Archiv), wie die Startseite als Landingpage
  zur Installation der App führt, welche Funktionen nur in der App erscheinen und welche als
  Web-App hinter dem Login laufen.
  Davon hängen die Aussagen zum Web in ADR-0001, ADR-0002, ADR-0004 und ADR-0006 ab.

---

## Anhang: Quellen

Vergleichbare Plattformen:

- Strava: https://sacra.com/research/strava, https://blog.therunningcollective.fr/strava-2025-chiffres-cles-running-equipement-et-tendances/
- Ravelry: https://api.ravelry.com/about, https://en.wikipedia.org/wiki/Ravelry
- PCPartPicker: https://www.cgdirector.com/pcpartpicker-compatibility-warnings-explained/
- Printables: https://www.fabbaloo.com/news/prusas-new-rewards-and-badging-system-wither-thingiverse
- Untappd: https://www.braze.com/resources/articles/engage-millions-users-mobile-community-building-lessons-untappd
- Vivino: https://screensdesign.com/apps/vivino-buy-the-right-wine/
- Letterboxd: https://www.cactuslab.com/work/letterboxd-year-in-review/
- iNaturalist: https://www.inaturalist.org/pages/computer_vision_demo
- Planta: https://apps.apple.com/us/app/planta-plant-garden-care/id1410126781
- Greg: https://apps.apple.com/app/id1512912236
- Houzz: https://www.proremodeler.com/business/marketing/article/55183875/the-wise-houzz
- Goodreads: https://www.goodreads.com/blog/show/1130
- Duolingo: https://blog.duolingo.com/how-duolingo-streak-builds-habit, https://lennysnewsletter.com/p/behind-the-product-duolingo-streaks
- Gamification-Probleme: https://tildes.net/~tildes/a6/submission_gamification_and_the_karma_problem, https://blogs.sap.com/?p=104858

Aquaristik:

- Aquarimate: https://apps.apple.com/us/app/aquarimate/id587215055
- App-aquatic: https://apps.apple.com/ly/app/id6759092897
- Fishi (Rezensionen): https://appshunter.io/ios/app/fishi-aquarium-manager/id6450653339/reviews
- TankBud: https://alternativeto.net/software/tankbud/about/
- AqAdvisor: https://www.aqadvisor.com
- Behelfslösungen fürs Logging: https://www.capitalcichlids.org/forums/goto/post?id=109273
- Nachfrage nach Zuchtprotokollen: https://forum.aquariumcoop.com/topic/21146-breeding-app/
- Erinnerungen: https://ukaps.org/forum/goto/post?id=794385
- Betrug in Kleinanzeigen: https://monsterfishkeepers.com/forums/threads/is-this-a-scam.763437/page-2
- Mbuna-Kompatibilität: https://fishprofiles.com/forums/Cichlid_Central/Mbunas_-_compatibility/28868/
- Zeitraffer-Dokumentation: https://gensou.sg/how-to-document-aquarium-journey/
- Flowgrow: https://flowgrow.de/db/tanks/first-step

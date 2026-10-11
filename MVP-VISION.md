# cichlids.com: Zielbild der Relaunch-App

**Stand: 2026-10-11**

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
| Discogs, Rebrickable | Sammlung und Wunschliste am Katalog | Dex mit "gehalten", "gezüchtet", "Wunsch" |

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
  100 Fotos. Das reicht für Dex, Artengalerien und einen Prototyp der Artenerkennung.
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
- **Aufnahmedatum im neuen Schema:** Der ETL liest kein EXIF; `media_item` hat dafür keine Spalte.
- **Frische Inhalte:** Seit 2020 haben sich rund 450 Mitglieder angemeldet; 2023 kamen 24 Fotos hinzu.

### Folgerungen

- Der Kompatibilitäts-Check verlangt eine eigene Kuration der Artdaten. Sie ist der größte
  Einzelposten dieses Zielbilds.
- Die Mitglieder kommen überwiegend aus den USA (6.716 von 10.400 mit Länderangabe; 71 aus
  Deutschland), die Kommentare sind fast vollständig englisch. Englisch ist die Standardsprache der
  App, Deutsch die zweite.
- Das Archiv erscheint in der App als kuratierte Geschichte der Plattform. Eine Sortierung nach
  Upload-Datum würde es als aktuellen Stream ausgeben.

---

## 6. Die fünf Kernerlebnisse

### 6.1 Willkommen zurück: der persönliche Rückblick

Ein Altmitglied folgt dem Link aus der Kampagnen-Mail, meldet sich an und sieht eine Story im Stil
eines Jahresrückblicks:

- seit wann es dabei war, wie viele Fotos es hochgeladen und wie viele Kommentare es bekommen hat,
- sein meistgesehenes Foto mit Aufrufzahl,
- seine Becken aus dem Archiv,
- zum Schluss die Frage "Wie sieht dein Becken heute aus?", die direkt zum ersten neuen Becken mit
  erstem Foto führt.

Eine teilbare Karte im Hochformat fasst den Rückblick zusammen. Der Rückblick macht aus der Kampagne
eine persönliche Einladung und kostet das Mitglied keine Mühe.

Daten: vollständig vorhanden. Aufwand: mittel.

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

Daten: Pflegedaten entstehen neu. Die Vorzeige-Becken aus dem Archiv brauchen das EXIF-Datum im ETL
(im Review zu R1 als eigenes Feature vermerkt). Aufwand: groß.

### 6.3 Der Fisch-Scanner

Ein Foto des Fischs liefert die drei wahrscheinlichsten Arten mit Angabe der Sicherheit. Ist das
Ergebnis unsicher, nennt der Scanner nur die Gattung. Ein Tap legt die Art in den Besatz und in den
Dex.

Technik: Ein Bild-Embedding-Modell vergleicht das Foto mit den 54.000 getaggten Archivfotos
(Nächste-Nachbarn-Suche, ohne eigenes Training). Das passt in den entkoppelten KI-Dienst aus
ADR-0011.

Vorbehalt: Die Artzuordnungen haben Mitglieder gesetzt; Farbformen und Hybriden verwischen die
Grenzen. Ein Spike misst deshalb zuerst die Top-3-Trefferquote auf zurückgehaltenen Fotos der rund
116 häufigen Arten. Erst ein überzeugendes Ergebnis bringt den Scanner in den Launch.

Aufwand: Spike klein, Produktfunktion mittel.

### 6.4 Cichliden-Dex und Artenseiten

- **Persönliche Lebensliste:** gehalten, gezüchtet, Wunschliste; Fortschritt je Region, etwa
  "Tanganjika: 14 von 207".
- **Artenseite als Gemeinschaftsseite** nach dem Ravelry-Prinzip: die besten Fotos (sortiert nach
  Aufrufen und Kommentaren), die Zahl der Becken mit dieser Art, Besatzpartner aus echten Becken,
  Fragen und Antworten aus dem Archiv, Halter mit Nachzucht.
- **Besatz-Planer mit Kompatibilitäts-Hinweisen** nach dem PCPartPicker-Prinzip. Jeder Hinweis hat
  eine Begründung und eine Quelle und blockiert nichts.

Daten: Fotos und Archiv sind vorhanden, Haltungsdaten fehlen. Die rund 150 häufigsten Arten (sie
decken fast alle Fotos ab) werden zum Launch kuratiert: KI erstellt Entwürfe, ein Mensch prüft sie,
jede Angabe hat eine Quelle. Fremde Datenbanken wie Seriously Fish oder Cichlid Room Companion sind
urheberrechtlich geschützt und scheiden als Kopiervorlage aus.

Aufwand: Dex und Artenseiten mittel, Kuration groß (überwiegend Datenarbeit).

### 6.5 Fragen mit Beckenkontext

- **Frage an die Runde** aus jedem Foto oder Pflege-Eintrag mit einem Tap. Beckengröße, Besatz,
  letzte Werte und letzter Wasserwechsel hängen automatisch an, damit Helfer nicht nachfragen müssen.
- **Archiv-Antworten zuerst:** Während des Tippens erscheinen passende Antworten aus 92.000
  Archiv-Fragen und 14.000 Threads.
- **Interessen-Feed:** Fragen aus abonnierten Regionen (Malawi, Tanganjika, Amerika) erscheinen auf
  dem Startbildschirm.
- **Kudos als Reaktion** auf Fotos, Höhepunkte und gehaltene Serien. Die Altsterne erscheinen nicht
  in der Oberfläche.

Aufwand: mittel.

---

## 7. App-Rahmen

### Navigation

| Tab | Inhalt |
|---|---|
| Heute | Fällige Pflege, eigene Serie, Neues aus gefolgten Becken, Fragen aus den eigenen Regionen |
| Entdecken | Kuratiertes Archiv, Vorzeige-Zeitraffer, Becken nach Region, Suche |
| + | Pflege loggen, Foto, Frage, Scanner |
| Dex | Lebensliste, Artenseiten, Besatz-Planer |
| Ich | Eigene Becken, Profil, Rückblick, Einstellungen |

Galerie, Forumsarchiv und Artenliste gehen in Entdecken und Dex auf.

### Kaltstart

Entdecken präsentiert das Archiv offen als "Aus 20 Jahren cichlids.com": Hall of Fame je Region,
Becken der Woche aus dem Archiv, die Vorzeige-Zeitraffer. Das Onboarding fragt die Region ab, legt
das erste Becken in unter zwei Minuten an (oder übernimmt ein Altbecken) und belohnt den ersten
Eintrag sofort.

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
- **KI-Dienst (ADR-0011):** Embeddings, Scanner, Suche nach Archiv-Antworten.
- **Mobile:** neu aufgebaut nach dem Design-System; Komponenten, Auth und Upload-Logik des MVP sind
  wiederverwendbar.
- **Vorgehen:** Stories mit Given-When-Then im Backlog, Red-Green, grüner E2E-Pfad je Story
  (ADR-0003).

### Reihenfolge in vorführbaren Scheiben

1. Neuer App-Rahmen und Becken-Seite nach der Hi-Fi-Vorlage, mit den Vorzeige-Zeitraffern aus dem
   Archiv.
2. Quick-Log, Pflegeplan, Serie in Wochen, Erinnerungen.
3. Willkommen zurück und Übernahme der Altbecken.
4. Dex und Artenseiten; parallel die Kuration der ersten 150 Arten.
5. Fragen mit Beckenkontext, Archiv-Antworten, Kudos.
6. Scanner, sobald der Spike überzeugt.

---

## 9. Offene Entscheidungen

- Launch-Umfang: alle fünf Kernerlebnisse oder eine Auswahl.
- Quelle und Verfahren der Artdaten-Kuration.
- Zeitpunkt des Scanner-Spikes.
- Verhältnis zum Deployment-Strang (O2 in `PROJECT-STATUS.md`): parallel oder nacheinander.
- Umbau der Navigation wie in Abschnitt 7.

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

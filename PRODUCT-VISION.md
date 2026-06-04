# cichlids.com — Produktvision (PMF & Epics)

> Strategisches Produktdokument: **was** wir für Nutzer bauen und **warum** — die Grundlage
> für Design, erste Screens und Story-Schnitt. Sprache bewusst Deutsch (internes
> Strategiedoc); Code, ADRs und Stories sind Englisch.
>
> **Verhältnis zu anderen Dokumenten:**
> - [`PROJECT-CONTEXT.md`](PROJECT-CONTEXT.md) — Ist-Zustand der geretteten Legacy-Daten.
> - [`PLAN.md`](PLAN.md) — **technischer** Transformationspfad (Stack, ETL, Phasen).
> - [`docs/adr/`](docs/adr/index.md) — verbindliche Architektur-Entscheidungen (u.a. ADR-0001:
>   Becken im Zentrum, Anerkennung für Pflegeleistung).
> - **Dieses Dokument** — die **Produkt-/PMF-Sicht**: Zielbild, Engagement-Logik, Epics. Es
>   definiert das *Was/Warum*; `PLAN.md` und die ADRs definieren das *Wie*.
> - Historische Eingangs-Ideen der Legacy-Plattform: `workspace-legacy/planning-notes/` +
>   `findings/09-vision-and-serverless.md` (Flickr/Pinterest + StackOverflow + Marktplatz).
>   Dieses Dokument ist die **verfeinerte, aktuelle** Produktrichtung.

---

## 1. Ausgangslage & das Product-Market-Fit-Problem

cichlids.com war eine große Foto-Sharing-Community für Buntbarsch-Enthusiasten und ist seit
2023 offline; die Community ist u.a. zu Facebook abgewandert. Die Datenbasis (≈189k Bilder,
1,6 Mio. Kommentare, ~12k Nutzer, 829 Arten) ist gerettet.

Die Legacy-Plattform war im Kern ein **Foto-Stream** — und genau das ist die PMF-Schwäche.
Ein reiner Foto-Stream ist gegen Instagram/Facebook nicht verteidigbar: Wer nur Bilder folgen
will, folgt dort schon Kanälen. Aquarianer haben jedoch ein **überdurchschnittliches
Teil-Bedürfnis** — sie investieren über Monate und Jahre Arbeit in ihre Becken und Tiere und
sind stolz darauf (vgl. dedizierte Becken-Schau-Seiten wie einrichtungsbeispiele.de).

Der entscheidende Hebel: **Ein Aquarium ist kein Moment, sondern ein lebendes Projekt über
die Zeit** — Einrichtung → Einfahrphase → Bepflanzung → Besatz → Einwachsen → Rescape; ein
Männchen färbt aus; die erste Nachzucht; eine Algenkrise wird überstanden. Instagram erfasst
strukturell nur den *Moment* (ein Foto) und wirft die wertvollste Dimension weg: die **Zeit**.
Genau dort liegt unser Differenzierungsraum.

---

## 2. Produktthese: das Becken als lebendes Projekt — ein Becken, zwei Blickwinkel

**Das Becken (Tank) ist die zentrale Entität, nicht das Einzelfoto.** Jedes Becken ist ein
Projekt mit Timeline, Technik/Specs, Besatz und Pflegehistorie. Daraus folgt der zentrale
Kniff, der „dokumentieren", „dranbleiben" und „stolz zeigen" reibungslos vereint — **ein
Becken, zwei Sichten auf dieselben Daten:**

- **Pflege-Sicht (Utility):** Logs, Messwerte, Verlaufs-Graphen, Erinnerungen,
  Heatmap, Streak. Das ist der **tägliche/wöchentliche Rückkehr-Motor** — der Grund, die App
  auch ohne Posting-Laune zu öffnen.
- **Stolz-Sicht (Portfolio):** schöne Becken-Story, Timeline, Meilensteine,
  Zeitraffer, Specs, Besatz, Fisch-Spotlights. Das ist die **Auszahlung** — was andere sehen
  und bewundern.

Beide Sichten lesen aus **denselben Daten**. Sie sind **Funktions-Sichten**, keine
Sichtbarkeits-Stufen — wer was sieht, regelt die Sichtbarkeit pro Eintrag (§8.1, default
öffentlich). Damit produziert die private Pflege-Routine
(Tracking) ohne Zusatzaufwand den öffentlichen Stolz (Portfolio). Das ist die Übertragung des
Strava-Prinzips auf die Aquaristik: Das *Ausüben des Hobbys selbst* erzeugt automatisch
teilbaren, wiederkehrenden Content.

Darauf baut perspektivisch ein **Utility-Burggraben** auf, den ein reiner Foto-Stream nie
hat (KI-Bestimmung, Diagnose, Pflegewissen — siehe §9): **Utility + Identität + Community =
Klebrigkeit.**

---

## 3. Positionierung & Zielgruppe (Cichlid-first)

**Entscheidung:** Das Produkt startet fokussiert auf **Buntbarsch-Enthusiasten (Cichliden)**
und baut darauf eine **starke, eigenständige Marke** unter `cichlids.com`. Die **Architektur
ist von Anfang an niche-agnostisch** (Tank, Species, Biotop, Tracking sind generisch); die
Plattform ist als **Föderation von Nischen** gebaut, sodass weitere Stämme additiv
hinzukommen. **Expansionspfad** — bewusst gestaffelt, erst wenn der Brückenkopf dicht ist:
zuerst weitere **Süßwasser**-Stämme (Aquascaper/Planted), danach **Reef/Meerwasser**.

Kurz: **breite Architektur, schmaler Go-to-Market.** Das ist hier nicht Kompromiss, sondern
die dominante Strategie — sie verschenkt nichts und schließt nichts aus.

### Begründung

1. **Nische als Wedge, nicht als Deckel.** Die Produktmechanik (Becken-als-Projekt, Tracking,
   Journey, Stolz) ist universell — aber Engagement lebt von **Identität** (Wirkprinzip §4.4).
   Ein flaches „für alle Aquarianer" gehört *niemandem* und konkurriert mit Reddit/Facebook
   auf deren Heimspiel ohne eigenen Wedge. Eine dichte Nische dagegen schafft Zugehörigkeit
   („meine Leute") und lässt sich dominieren. Der Niche-in-Niche-Feed ist zugleich der
   **Generalisierungs-Mechanismus**: „Cichliden" ist selbst eine Nische in „Aquaristik" —
   weitere Stämme später hinzuzunehmen ist additiv, kein Umbau (vgl. Beachhead-Strategie:
   einen Stamm dominieren, dann nach außen wachsen).

2. **Konkurrenzlücke.** Das Feld zerfällt in **Tracking-Apps ohne Stamm** (Logbuch-Tools,
   blass, keine Community) und **Communities ohne modernes Tracking** (Reddit generisch;
   Facebook-Gruppen nach Nische zersplittert; alte Foren wie Cichlid-Forum.com — inhaltlich
   stark, UX von vorgestern, nicht mobil). **Niemand verbindet mobiles Tracking +
   Becken-als-Projekt + dichte Nischen-Community.** Im Cichlid-Bereich gibt es einen klar
   **schlagbaren Incumbent**, den wir auf UX, Mobile und Tracking ausspielen — statt im
   generischen Feld gegen Reddit/Facebook zu verlieren.

3. **Unfairer Startvorteil.** Der Kaltstart-Schatz ist cichlid-spezifisch: ~12k Nutzer,
   ~235 GB Bilder, 829 Arten Taxonomie, die etablierte Marke. In der Nische ist dieser Content
   Gold und die Community ab Tag 1 **dicht** (Netzwerkeffekt startet lokal stärker). Damit eine
   *generische* App zu seeden wäre schief und verschenkte den Vorteil.

4. **Der Wedge selektiert ohnehin die Ernsthaften.** Becken-als-Projekt + Tracking + Streaks
   zieht **Vielpfleger, Züchter und Werte-Tracker** an; Casual-Halter tracken nicht. In
   Süßwasser sind die Ernsthaften überproportional Cichlid- und High-Tech-Planted-Leute —
   „breit" hieße in der Praxis ohnehin zuerst „ernsthafte Süßwasser-Halter", was massiv mit
   Cichliden überlappt. Der Cichlid-Start verliert also kaum jemanden, den der Wedge ohnehin
   anzieht.

5. **Cichlid-Tiefe auf breiter Basis.** Die Stellen, an denen Cichlid-Leute *distinct* sind —
   Brutpaare/Zucht, Einzeltier-Bindung (Fische mit Namen), Biotop-Identität,
   Aggressions-/Territorial-Kompatibilität (§8.4) — sind technisch **generische** Features, die
   nur **zuerst für den Brückenkopf glänzen**. Tiefe schwächt die Breite nicht, sie schärft den
   Start.

6. **Reef bewusst später.** Reef-/Meerwasser-Keeper sind am tracking-affinsten und
   zahlungsbereitesten, haben aber eine **eigene Kultur** und **hardware-gekoppelte Incumbents
   mit Lock-in** (Neptune Apex/Apex Fusion, GHL). Adjazent von Cichliden sind erst andere
   Süßwasser-Stämme; Reef folgt deutlich später, nicht als Teil des Starts.

7. **Marke als einziger echter Lock-in — und aufschiebbar.** `cichlids.com` ist **SEO-Gold**:
   Der Domainname *ist* die Kategorie → organische Akquise, die Foren stark macht. Ein Rebrand
   kostet Identität und SEO, deshalb wird die Marke nicht vorschnell breit gemacht. `cichlids.com`
   muss aber nie umbenannt werden: Es bleibt die **Cichlid-Heimat**; eine etwaige **Dachmarke**
   entsteht erst beim *tatsächlichen* Expandieren — dann mit allem, was bis dahin gelernt wurde.

**Kosten des Späterentscheidens (gering).** Die niche-agnostische Architektur kostet jetzt
nichts (Tank/Species/Biotop/Tracking sind bereits generisch) und nagelt nirgends fest. Der
Onboarding-Interessen-Picker (Epic 4) ist **strukturell identisch** — nur die Taxonomie ist
cichlid-first gefüllt (Malawi / Tanganjika / Süd- & Mittelamerika / Diskus / Apistogramma …).
Die Positionierung blockiert damit das Design von Epic 1 + 2 nicht.

---

## 4. Warum Menschen sich in Hobby-Communities engagieren (Wirkprinzipien)

Aus der Forschung zu Online-Communities (u.a. Kraut & Resnick, *Building Successful Online
Communities*; Social-Identity-/SIDE-Theorie) und aus dem, was bei erfolgreichen Hobby-/
Maker-Plattformen tatsächlich getragen hat, kristallisieren sich wiederkehrende Treiber
heraus. Sie sind die **Begründung** für die Produktentscheidungen in §5–§8.

1. **Status & Identität bei Menschen, deren Urteil zählt.** Ein Like von Fremden ist schwach;
   Anerkennung von Leuten, die *dieselben* Fische halten, ist stark. Niche-in-Niche (Malawi /
   Tanganjika / Südamerika / Apistogramma / Aquascaping / Nano) schlägt „Aquaristik" generisch.
2. **Fortschritt & Meisterschaft sichtbar machen.** Das Hobby *ist* eine Reise. Menschen
   bleiben, wenn sie ihren eigenen Fortschritt sehen — und andere ihn sehen.
3. **Reziprozität & praktischer Nutzen mit hohem Einsatz.** Es geht um lebende Tiere und teures
   Equipment. Wer schnelle, kompetente Hilfe bekommt (Krankheit, Wasserwerte, Algen), revanchiert
   sich — Status entsteht durchs Helfen.
4. **Zugehörigkeit — „meine Leute".** Eine enge Nischen-Identität ist das stärkste Bindemittel.
5. **Sammeln & Vervollständigen.** Menschen katalogisieren das Eigene gern (Untappd-Biere,
   Letterboxd-Filme, Discogs-Platten). Die eigene Arten-/Becken-Liste wird zum „Fishkeeper's Dex".
6. **Schnelle, *bedeutungsvolle* erste Belohnung.** Wer beim ersten Beitrag rasch eine echte,
   spezifische Reaktion bekommt, bleibt. Stille tötet die Bindung — der Kaltstart entscheidet.

**Lehren vergleichbarer Plattformen (je eine Kernlehre):**

| Plattform | Was getragen hat | Lehre für uns |
|---|---|---|
| **Strava** | das Ausüben des Hobbys erzeugt selbst Content (Track, Kudos, Segmente, Clubs) | Pflege-Routine → automatisch Content + Rückkehr-Trigger |
| **Untappd** | Check-in + Badges + Sammeln | Aus Pflege/Beobachtung ein leichtes Achievement-Spiel machen |
| **Letterboxd** | Identität durch Kuratieren (Tagebuch, Listen) + schönes Design | Geschmack/Stolz signalisieren; Design ist Feature |
| **iNaturalist / PlantNet** | Bestimmung + Citizen Science | Bestimmen/Diagnostizieren ist Eigen-Engagement (nutzt unsere Bildbasis) |
| **Reddit Build-Threads / Monster Fish Keepers** | das **Journal/Build-Log** ist das Herz jeder Maker-Community | Becken-Journal als zentrales Format (alte UX = unsere Chance) |
| **Houzz** | Projekt-Portfolios + Ideabooks + Pro-Verzeichnis | „Zeig deine Arbeit" + späterer Monetarisierungs-Pfad |
| **Planta** | Pflege-Erinnerungen erzeugen wiederkehrende Gewohnheit | Reminder-Loop als Habit, auch ohne Sharing-Laune |

---

## 5. Designprinzipien (aus §4 abgeleitet)

- **Das Becken ist die Einheit, nicht das Foto.** Alles hängt an einem Becken als Projekt.
- **Pflege erzeugt Stolz.** Tracking-Eingaben speisen automatisch Timeline, Meilensteine und
  Portfolio — kein doppelter Aufwand.
- **Niedrigste Friktion zuerst, Tiefe optional.** Der häufigste Eintrag (Wasserwechsel) ist ein
  einziger Tap; reiche Erfassung (Messwerte) ist progressiv offengelegt.
- **Man folgt einem Becken wie einer Serie.** Wiederkehrendes Interesse statt Wegscrollen.
- **Anerkennung gezielt, nicht beliebig.** Niche-Feeds und kontextuelle Hilfe statt anonymer
  Reichweite.
- **Selbstbestimmte Sichtbarkeit.** Sichtbarkeit ist pro Inhalt wählbar — Zeigen ohne Angst.
- **Ansporn ohne Druck.** Gamification ist abschaltbar und besatz-basiert begründet, nie
  schuldzuweisend.
- **Lebendig ab Sekunde 1.** Geseedeter Legacy-Content macht den Kaltstart relevant.

---

## 6. Der Engagement-Kreislauf

Die Entscheidungen greifen zu einem selbstverstärkenden Kreislauf ineinander:

```
  Becken anlegen (Projekt + Besatz)
        │
        ▼
  App schätzt aus dem Besatz eine empfohlene Pflege-Frequenz
        │
        ▼
  Erinnerung  ──►  Wasserwechsel (1 Tap)  +  optional Messung (separat)
        │                          │
        │                          ▼
        │                 Heatmap + Streak (Ansporn)
        ▼                          │
  Einträge speisen die Timeline ◄──┘
        │
        ▼
  automatisch: Meilensteine · Zeitraffer · Fisch-Spotlights  (der stolze Content)
        │
        ▼
  in selbst gewählter Sichtbarkeit geteilt
        │
        ▼
  andere folgen dem Becken · Daumen-hoch (auch auf Streaks) · fragen die Runde · helfen
        │
        └──►  (perspektivisch) Einzeltier-Tracking · KI-Diagnose · Marktplatz
```

Kurz: **Die private Pflege-Pflicht produziert den öffentlichen Stolz.** Der Streak hält bei der
Pflicht, die Sichtbarkeitswahl macht das Zeigen angstfrei, der Besatz macht die Erinnerung
schlau — jeder Baustein verstärkt die anderen.

---

## 7. Die vier Epics (v1)

Vier Epics setzen die These um. **Epic 1 + 2 sind das Herz und der Fokus des ersten
Prototyps/Designs**; Epic 3 + 4 werden mitgedacht (damit Architektur und Flows stimmen), aber
visuell nachgelagert gestaltet.

Terminologie konsistent zum Domänenmodell in [`PLAN.md`](PLAN.md) §2 (Tank, TankPost,
MediaItem, Inhabitant→Species, Reputation). Der **Pflege-/Tracking-Layer** (Wasserwechsel,
Messwerte, Reminder, Heatmap, Zeitraffer) ist eine **Erweiterung** dieses Modells — siehe §10.

### 🐟 Epic 1 — „Mein Becken": Profil, Timeline & Time Machine
*Das Projekt als stolzes Schaufenster (Stolz-Sicht).*

- **Jobs:** Becken anlegen (Volumen, Maße, Technik, Substrat, Biotop-Typ, Einrichtungsdatum);
  Besatzliste pflegen (Arten je an den **Species-Katalog** geknüpft → Pflege-Anforderungen,
  Aggressivität, Endgröße; siehe §8.4 — inkl. Kompatibilitäts-Hinweisen bei unpassendem Besatz),
  Anzahl, Einzugsdatum; Hero-Foto; chronologische **Timeline**
  aller Events; **Zeitraffer / Time-Machine** über den Foto-Verlauf des Beckens (Tage/Wochen/
  Monate abspielbar, wie ein Webcam-Timelapse); **Fisch-Spotlights** (ein besonderes Tier
  hervorheben).
- **Schlüssel-Screens:** Becken-Übersicht (Kachel-Portfolio über alle eigenen Becken),
  Becken-Detail mit Timeline-Story, Time-Machine-Player, Spotlight-Karte, „Becken bearbeiten".
- **Löst (siehe §8):** Sichtbarkeit pro Becken; das Becken als Einheit des Stolzes, ergänzt um
  Fisch-Spotlights als Einzelfisch-Dimension.

### 💧 Epic 2 — „Dranbleiben": Pflege-Logging, Reminder & Streaks
*Der tägliche/wöchentliche Rückkehr-Motor (Pflege-Sicht).*

- **Jobs:** **Wasserwechsel als 1-Tap-Event** (Menge in L oder % optional nachtragbar);
  **Messung als separates Event** (Temp, pH, KH, GH, NO₂, NO₃, NH₄, PO₄, Leitwert/TDS) —
  Messen ist unabhängig vom Wasserwechsel möglich und umgekehrt; **Pflege-Plan & Reminder**
  (Default z.B. alle 2 Wochen Wasserwechsel, aus dem Besatz verfeinert); **Wasserwechsel-
  Heatmap** (Kalender-Raster im Stil eines Contribution-Graphen); **Streak** (Duolingo-
  artig); **Verlaufs-Graphen** je Messwert; **jedes Event anreichern** mit Foto/Video (z.B. vom
  Wasserwechsel), Notiz/Kommentar (was ist aufgefallen) oder einem **Todo/Merker für später**
  (z.B. „Wasseraufbereiter/Chemikalien nachkaufen"); **auf Events reagieren / Daumen-hoch**
  (gerade bei gehaltener Streak — §8.1/§8.3).
- **Schlüssel-Screens:** Quick-Log-Sheet (groß, einhändig bedienbar), Mess-Eingabe (progressiv
  offengelegt), Werte-Graphen, Heatmap-/Streak-Dashboard, Reminder-/Pflege-Einstellungen,
  Event-Detail mit Anhängen (Medien/Notiz/Todo) und Reaktionen, Todo-/Merkliste.
- **Löst (siehe §8):** Quick-Log vs. Tiefe sauber getrennt; Gamification mit Opt-out und
  besatz-basierter, begründeter Empfehlung statt Schuld-Druck.

### 🌊 Epic 3 — „Entdecken, Folgen & Helfen": Feed, Becken-Abos, Reaktionen & Q&A
*Aus Dokumentation wird Community — inklusive der „Hilfe, ich hab ein Problem"-Schleife.*

- **Jobs:** Niche-Feed (nach Biotop/Interessen); **einem Becken folgen** wie einer Serie;
  Meilensteine/Updates im Follower-Feed; **Reaktionen / Daumen-hoch** auf Posts und Pflege-Events
  (auch auf Streaks, §8.3); **Kommentare an konkreten Log-/Timeline-Einträgen**.
  - **Becken-Posts mit öffentlicher Frage an die Runde** („kennt jemand sowas?") — ein
    dokumentiertes Problem (Foto-Event, §8.2; ggf. mit KI-Vorschlag, §9) lässt sich mit einem
    Schritt als Frage heben.
  - **Themen-/Nischen-Abos:** Fragen & Diskussionen aus abonnierten Bereichen erscheinen im
    Stream — beantworten oder nicht.
  - **Ignore-/Mute-Funktion** (Benutzer und Threads stummschalten), damit der Stream relevant
    und freundlich bleibt.
- **Schlüssel-Screens:** Feed, Becken-Follow-Ansicht, Frage-Composer, Q&A-/Diskussions-Thread,
  Kommentar-Thread am Event, Entdecken/Explore nach Niche, Themen-Abo-Verwaltung, Ignore-/Mute-
  Einstellungen.
- **Löst (siehe §8):** Sichtbarkeits-Stufen bestimmen, wer im Feed was sieht; bedient den
  Reziprozitäts-/Hilfe-Treiber (§4.3) — schnelle, kompetente Hilfe von Leuten mit denselben
  Fischen.

### 🚀 Epic 4 — „Ankommen": Onboarding, Interessen & lebendiger Start
*Damit die App ab Sekunde 1 relevant wirkt (Kaltstart entscheidet über Retention).*

- **Jobs:** Interessen/Biotop wählen → personalisierter Erst-Feed; **geseedeter Legacy-Content**
  als echte Becken-Timelines; erstes eigenes Becken in unter ~2 Minuten anlegen; schnelle erste
  Belohnung (erster Streak-Tag, erste Reaktion).
- **Schlüssel-Screens:** Onboarding-Flow (Interessen-Picker), „Becken anlegen"-Wizard,
  kuratierter Erst-Feed.

---

## 8. Querschnitts-Entscheidungen

Diese Festlegungen wirken über mehrere Epics und sind beim Design verbindlich.

### 8.1 Sichtbarkeit (Privatsphäre)
Pro Inhalt (Becken und einzelne Einträge) wählbar in vier Stufen:
**privat** · **nur Follower** · **nur ausgewählte Benutzer** · **komplett öffentlich.**
**Default öffentlich — auch für Pflege-/Tracking-Events** (Wasserwechsel, Messungen): So können
andere reagieren und **Daumen-hoch** geben, gerade wenn jemand seine Streak hält (§8.3). Wer mag,
stellt einzelne Einträge oder ganze Becken jederzeit auf eine engere Stufe (sensible Werte bleiben
so granular schützbar). Diese Logik bestimmt, was in Epic 3 im Feed sichtbar wird.

### 8.2 Tracking-Datenmodell
Jede Messung trägt **von Anfang an ihre Herkunft** als **Reading-Event mit
`source = manual | sensor | photo`** — ob der Wert manuell eingetippt, von einem Sensor
geliefert oder per Foto (Teststreifen/Tröpfchentest) ausgelesen wurde. Das ist bewusst schon
in v1 im Modell verankert, obwohl Sensor/Foto erst später kommen (§9): So werden neue
Datenquellen zu *zusätzlichen Quellen* statt zu einem Modell-Umbau, und Auswertungen können
nach Verlässlichkeit der Quelle unterscheiden. Nachträglich lässt sich die Herkunft nicht
rekonstruieren — deshalb von Beginn an mitschreiben.

Wasserwechsel, Pflege-Aktionen, Meilensteine und Journal-Posts sind ebenfalls **Events auf der
Becken-Zeitachse**. So lesen Pflege-Sicht, Stolz-Sicht (und später Diagnose) denselben
Event-Stream. Jedes Event kann **Anhänge** tragen — MediaItems (Foto/Video, z.B. vom
Wasserwechsel), **Notizen/Kommentare** und **Todos/Merker** (offene Aufgaben für später, etwa
„Wasseraufbereiter nachkaufen" — Anknüpfung an Monetarisierung, §9).

**Welche Messwerte überhaupt relevant sind, hängt am Becken-/Nischen-Typ** (Malawi ≠ Aquascape
≠ Reef) und ist daher **konfigurierbar, nicht hartcodiert** — das stützt die niche-agnostische
Architektur aus §3.

### 8.3 Gamification (Heatmap & Streaks)
Heatmap und Streaks sind **default aktiv** und in den Einstellungen **deaktivierbar** (Opt-out).
Die App erinnert default an Pflege (z.B. alle 2 Wochen Wasserwechsel). Aus **Besatz und
Besatzdichte** kann sich die **Empfehlung häufigerer Wasserwechsel** ergeben; wer der Empfehlung
folgt, wird über Streak/Heatmap belohnt. Ziel ist Ansporn, nicht Schuld — die Mechanik bleibt
begründet (warum diese Frequenz) und abschaltbar.

**Soziale Verstärkung:** Da Tracking-Events per Default öffentlich sind (§8.1), können andere
**reagieren und Daumen-hoch** vergeben — eine gehaltene Streak wird so zum gefeierten, sichtbaren
Signal (Epic 3), nicht nur zur Selbstmotivation.

### 8.4 Besatz, Species-Katalog & Kompatibilität
Jedes Becken führt eine **Besatzliste**, deren Einträge an einen **Species-Katalog** geknüpft
sind. Jede Art trägt dort ihre **Haltungs-Eckdaten**: verträgliche/benötigte Wasserwerte (Temp,
pH, GH/KH …), **Aggressivität/Sozialverhalten**, **Endgröße**, Mindestbecken/Schwarmgröße,
Ernährung. (Grundstock: die 829 Arten der Legacy-Taxonomie.) Daraus folgen zwei Wirkungen:

- **Kompatibilitäts-Hinweise & Warnungen.** Passt der geplante/aktuelle Besatz nicht zusammen —
  kein gemeinsames Wasserwert-Fenster, zu aggressiv kombiniert, Becken zu klein für die Endgröße
  — gibt die App **begründete Empfehlungen/Warnungen** statt stiller Fehler. (Aggressions-
  Konflikte sind gerade bei territorialen Arten zentral.)
- **Fundierte Pflege-Empfehlung.** Der Katalog liefert die Datenbasis für die besatz-basierte
  Wasserwechsel-/Pflege-Empfehlung aus §8.3 (Bioload aus Endgröße × Anzahl → Pflegefrequenz).

v1 = Besatz↔Species + Katalog-Grunddaten + einfache Warnungen; Tiefe und Datenpflege wachsen
iterativ. Die spätere becken-übergreifende Problem-Suche (§9) baut darauf auf.

---

## 9. Bewusst später — nicht in v1, aber fest eingeplant

Diese Stränge sind Teil der Produktrichtung und sollen **nicht vergessen** werden; sie folgen
nach den v1-Epics. Das Datenmodell (§8.2) und die Becken-/Fisch-Entitäten sind so zu bauen,
dass sie sich additiv ergänzen lassen.

- **Einzeltier-Tracking.** Viele Nutzer geben ihren Fischen Namen. Einzelne Tiere über die Zeit
  verfolgen (Färbung, Wachstum, Gesundheit). Speist direkt die **Fisch-Spotlights** (Epic 1).
- **Brutpaare / Zuchtpaare.** Paare und Zuchtprojekte abbilden — Grundlage für Nachzucht-Logs
  und den späteren Züchter-/Marktplatz-Strang.
- **Utility / KI-Diagnose & Bestimmung.** Foto von Algen / Krankheit / Fisch / Pflanze →
  **KI-Analyse** (Arten-/Pflanzen-Bestimmung, Krankheits-/Algen-Diagnose), Pflegewissen verknüpft
  mit dem konkreten Besatz. Das Ergebnis wird als **Becken-Event dokumentiert** (§8.2) und lässt
  sich mit einem Schritt als **öffentliche Frage in die Community** heben (Epic 3) — KI-Vorschlag
  *plus* menschliche Hilfestellung. (Der manuelle Pfad „Problem fotografieren → Community fragen"
  funktioniert bereits über Epic 1/2 + Epic 3; die KI automatisiert und beschleunigt ihn.) Nutzt
  die geretteten ~235 GB Bilder als ID-/Trainingsbasis; bildet den Utility-Burggraben gegen
  Instagram.
- **Engagement-Priming via KI-Agenten.** Zum Anschieben des Kaltstarts können KI-Agenten /
  Bot-Identitäten (ADR-0011, [`PLAN.md`](PLAN.md) Phase 7) auf Inhalte reagieren (Auto-Antworten/
  Bewertungen) und so ergänzend zum geseedeten Legacy-Material (Epic 4) frühe Aktivität erzeugen.
  Kennzeichnung von KI-Inhalten beachten.
- **Ähnliche Becken & Problem-Matching.** Becken mit ähnlichem Besatz/Setup finden — um von
  Haltern mit denselben Problemen zu lernen (Aggression im Becken, verkümmernde/nicht wachsende
  Fische, hartnäckige Werte). Nutzt Besatz/Species (§8.4) und die Becken-Historie;
  Discovery-Erweiterung von Epic 3, grenzt an die KI-Diagnose.
- **Sensor-/API-Anbindung & Teststreifen-Foto.** Automatische Messwerte über Hardware
  (`source = sensor`) und Auslesen von Tröpfchentests/Teststreifen per Foto (`source = photo`) —
  beides reine Erweiterung von §8.2.
- **Weitere Nischen / Stamm-Expansion.** Nach dem Cichlid-Brückenkopf (§3): zuerst weitere
  Süßwasser-Stämme (Aquascaper/Planted), danach Reef/Meerwasser — als neue Stämme im
  föderierten Modell, ggf. unter einer späteren Dachmarke.
- **Verbrauchsmaterial & Affiliate.** Pflege-**Todos** wie „Wasseraufbereiter/Chemikalien
  nachkaufen" (§8.2, Epic 2) sind ein natürlicher Aufhänger für **Shop-/Affiliate-Links** zu
  Verbrauchsmaterial, Futter und Zubehör — Monetarisierung direkt aus dem Pflege-Flow.
- **Marktplatz.** Tausch/Verkauf von Fischen & Pflanzen, Züchter-Profile (vgl. Houzz-Pro-Modell);
  knüpft an die Legacy-Vision „Trusted Profiles" an.

---

## 10. Verhältnis zum bestehenden Domänenmodell

[`PLAN.md`](PLAN.md) §2 stellt bereits **Tank, TankPost, MediaItem, Inhabitant→Species,
Reputation** in den Mittelpunkt und verankert „Becken im Zentrum + Anerkennung für Pflege"
(ADR-0001). Diese Produktvision **vertieft** das in zwei Punkten, die bei der nächsten
Modell-/ADR-Iteration zu berücksichtigen sind:

- **Care-/Tracking-Layer als Event-Stream** (§8.2): Reading-Events (Messwerte),
  Maintenance-Events (Wasserwechsel, Pflege), Milestones — als erstklassige Becken-Historie
  neben den TankPosts. Heatmap, Streaks und Reminder leiten sich daraus ab; Events tragen
  **Medien/Notizen/Todos** als Anhänge.
- **Zwei Blickwinkel** (§2) als bewusste UI-Achse (Pflege-Sicht = Utility-Dashboard,
  Stolz-Sicht = Portfolio) über derselben Becken-Entität; die **Sichtbarkeit** ist davon
  unabhängig pro Eintrag wählbar (§8.1, default öffentlich).


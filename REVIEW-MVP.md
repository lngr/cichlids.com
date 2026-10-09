# Review des Legacy-Parity-MVP

**Stand: 2026-10-09**

Gegenstand ist der Merge von `feat/legacy-mvp` nach `main` (Merge-Commit `2f5f959`, Diff
`391b9ad..2f5f959`: 134 Commits, 442 Dateien). Das Review lief in fünf Bereichen:

- API
- Domain und Infrastructure
- ETL
- Mobile und Client-Core
- Stack, Tooling und Doku

Dateien und Zeilen beziehen sich auf den Stand `2f5f959`.

**Status je Punkt:**

| Status | Bedeutung |
|---|---|
| offen | noch nicht besprochen |
| in Besprechung | teilweise entschieden; die offene Frage steht in der Spalte "Entscheidung" |
| besprochen | Entscheidung steht in der Spalte "Entscheidung" |
| erledigt | umgesetzt, mit Commit oder Task-ID |
| verworfen | bewusst nicht umgesetzt, mit Begründung |

Die Punkte werden einzeln besprochen, in der Reihenfolge ihrer IDs. Ist ein Punkt erledigt oder
verworfen, wird er hier nachgetragen. Daraus entstehende Arbeitspakete gehen in den Backlog.

"Verifiziert" heißt: Der Befund wurde nach dem Review zusätzlich direkt im Code nachgeprüft. Bei den
übrigen Punkten stammt der Befund aus dem Review; er wird bei der Besprechung nachgeprüft.

---

## Übersicht

| ID | Schwere | Bereich | Kurzbefund | Status | Entscheidung |
|---|---|---|---|---|---|
| R1 | hoch | Medien | Upload-Originale samt EXIF/GPS öffentlich lesbar | besprochen | Alle Originale privat, GPS und Personenbezug aus Metadaten entfernen |
| R2 | hoch | ETL | Profil-Step löscht Login-Identitäten anderer Provider | besprochen | Nur Altdaten-Provider ersetzen |
| R3 | hoch | ETL | Upsert überschreibt Änderungen aus dem neuen System | besprochen | ETL nur gegen leere DB; Reset vor jedem Lauf auf Dev/Staging; in Produktion genau einmal |
| R4 | hoch | Outbox | Unbehandelte Ausnahme stoppt den API-Host | besprochen | Gründlich absichern |
| R5 | hoch | Outbox | Backoff rechnet ab Event-Erzeugung | besprochen | Zeitplan reparieren, Admin-Retry, Aufgabe nach 7 Tagen |
| R6 | hoch | CI | Keine App-Tests in CI | besprochen | Backend, Frontend und E2E-Schreibtests in CI |
| R7 | hoch | Keycloak | Dev-Realm ungeschützt gegen Wiederverwendung | besprochen | Zwei Konfigurationen, CI-Test, Registrierung nach Import |
| R8 | hoch | Mobile | Netzwerkfehler beim Token-Refresh loggt aus | in Besprechung | Dauerhaft angemeldet pro Gerät, nur manueller Logout; offen: Web |
| R9 | mittel | API/Schema | Beliebig viele Bewertungen pro Mitglied und Ziel | offen | |
| R10 | mittel | API | SSRF über Webhook-URLs | offen | |
| R11 | mittel | API | Kommentartext und Löschgrund ohne Längengrenze | offen | |
| R12 | mittel | API | Race beim ersten Login, verwaistes Profil | offen | |
| R13 | mittel | API | Keine Quota für Uploads und Entwürfe | offen | |
| R14 | mittel | Outbox | Batch-Auswahl, fehlender Index, Sperren während HTTP | offen | |
| R15 | mittel | ETL | Username-Kollision bricht den Profil-Step ab | offen | |
| R16 | mittel | ETL | Leere Galerien werden verworfen | offen | |
| R17 | mittel | ETL/Slugs | Generierte Slugs haben 10 statt 6 bis 8 Zeichen | offen | |
| R18 | mittel | ETL | Dry-Run von `all` irreführend, lädt Forum-Anhänge hoch | offen | |
| R19 | mittel | ETL | Vote-Zeitstempel ungeprüft als UTC gelesen | offen | |
| R20 | mittel | ETL | `media --force` stellt fehlende Originale nicht her | offen | |
| R21 | mittel | ETL | Pfad-Traversal und Wildcards im Legacy-Bildpfad | offen | |
| R22 | mittel | ETL | Deaktivierte Nutzer werden zu Platzhaltern (Klärung) | offen | |
| R23 | mittel | Medien | `strip` entfernt das ICC-Farbprofil | offen | |
| R24 | mittel | Medien | Fehlende Limits in ImageProcessor und S3-Download | offen | |
| R25 | mittel | Mobile | Staging-Build fällt still auf localhost zurück | offen | |
| R26 | mittel | Mobile | Login-Button tot, wenn Keycloak-Discovery scheitert | offen | |
| R27 | mittel | Mobile | Nachladen von Listen bleibt hängen | offen | |
| R28 | mittel | Tooling | Lücken im Secret-Scanner | offen | |
| R29 | niedrig | API | Wildcards im Redirect-Lookup | offen | |
| R30 | niedrig | API | Redirect ohne Sichtbarkeitsprüfung | offen | |
| R31 | niedrig | API | `/api/me` liefert 500 bei Nicht-Member-Profil | offen | |
| R32 | niedrig | API | CORS in Development: `Origin: null` ergibt 500 | offen | |
| R33 | niedrig | API | Kein Startfehler bei fehlender Auth-Authority | offen | |
| R34 | niedrig | ETL | Verify zählt ganze Zieltabellen | offen | |
| R35 | niedrig | ETL | Anonyme Datensätze: Warnung und Upsert pro Zeile | offen | |
| R36 | niedrig | ETL | Keycloak-Admin-Client: PII in Ausnahmen, Fallback admin/admin, kein Backoff | offen | |
| R37 | niedrig | Schema | DiscussionPost-Invariante ohne Check-Constraint | offen | |
| R38 | niedrig | Stack | Schwächen in `bootstrap.sh` | offen | |
| R39 | niedrig | Stack | Lokaler Stack bindet an 0.0.0.0 | offen | |
| R40 | niedrig | Devcontainer | App-Install wird ohne pnpm still übersprungen | offen | |
| R41 | niedrig | Mobile | Fehleranzeige und Barrierefreiheit | offen | |
| R42 | mittel | Tests | Tests prüfen weniger, als ihr Name sagt | offen | |
| R43 | mittel | Doku | C#-Doc-Kommentare mit Inline-Tags | offen | |
| R44 | mittel | Backlog | Notizen zu 3.13 und 3.15 widersprechen ADR-0023 | offen | |
| R45 | niedrig | Doku | Kommentare, die nicht stimmen | offen | |
| R46 | niedrig | Doku | PROJECT-STATUS.md in mehreren Punkten veraltet | offen | |

---

## Hoch

### R1 Upload-Originale samt EXIF/GPS öffentlich lesbar (verifiziert)

- **Stellen:**
  - `app/api/src/Cichlids.Infrastructure/Storage/S3ObjectStore.cs:167-189`: Die Bucket-Policy
    erlaubt `s3:GetObject` anonym auf `bucket/*`.
  - `app/api/src/Cichlids.Api/Features/Uploads/UploadsWriteService.cs:48-60`: Das Original wird
    unverändert unter `originals/uploads/{profileId}/{uuid}.ext` gespeichert.
  - Nur die Varianten werden mit `strip` erzeugt.
- **Folge:** Ein Handyfoto mit GPS-Tags ist im Original samt Standort öffentlich abrufbar. Das gilt
  auch für Entwürfe. Der Client erhält die Original-URL über `MediaUrlBuilder`. Der Kommentar an der
  Policy behauptet, kein Key sei sensibel.
- **Richtung:** Originale privat halten (eigenes Bucket oder Prefix), die Policy auf `variants/*`
  und die nötigen öffentlichen Prefixe beschränken, Metadaten beim Speichern entfernen. Zu klären
  ist auch, ob die migrierten Legacy-Originale öffentlich sein müssen.
- **Entscheidung:**
  - Alle Originale sind privat, auch die migrierten Legacy-Originale.
  - Die Bucket-Policy gibt nur `variants/` und `forum_attachments/` frei.
  - Die API liefert keine Original-URL.
  - Die 934 nicht dekodierbaren Altbilder erhalten beim Medien-Seed eine bereinigte, öffentliche
    Kopie.
  - Zusätzlich entfernt das System aus jedem gespeicherten Original gezielt GPS-Daten,
    Seriennummern und Besitzerangaben. Das gilt für neue Uploads und einmalig rückwirkend für die
    migrierten Originale. Aufnahmedatum, Ausrichtung, Farbprofil und Kameramodell bleiben
    erhalten; bei JPEG geschieht das verlustfrei. Begründung: Auch private Originale sind bei
    einem Einbruch in den Speicher lesbar, Standortdaten sollen dort gar nicht erst liegen.
  - Hinweis für später: Das Aufnahmedatum zusätzlich in die Datenbank zu übernehmen (für Timeline
    und Zeitraffer) ist ein eigenes Feature und nicht Teil dieser Korrektur.

### R2 Profil-Step löscht Login-Identitäten anderer Provider (verifiziert)

- **Stellen:**
  - `app/api/src/Cichlids.Etl/Persistence/PgChildRows.cs:23-28`: Die Methode führt
    `DELETE FROM profile_identity WHERE profile_id = @id` ohne Filter auf den Provider aus.
  - Aufruf in `Steps/ProfileMigrationStep.cs:87`.
- **Folge:** Läuft `profiles` oder `all` nach dem Keycloak-Account-Import erneut, sind alle
  `oidc`-Verknüpfungen weg. Dasselbe gilt für die Verknüpfungen, die die API beim ersten Login
  anlegt. Der nächste Login erzeugt ein leeres Zweitprofil, und das migrierte Profil ist verwaist.
  Kein Test deckt das ab.
- **Entscheidung:**
  - Der ETL löscht und ersetzt nur Identitäten der Altdaten-Provider `auth0` und `legacy-openid`.
  - `oidc`-Verknüpfungen bleiben unberührt.
  - Ein Regressionstest legt vor einem zweiten Lauf eine `oidc`-Identität an und prüft, dass sie
    danach existiert.
  - Der Fix gilt zusätzlich zur Schranke aus R3. Begründung: Er kostet wenig und schützt die
    Zuordnung der Bestandsmitglieder zu ihren Profilen auch dann, wenn die Schranke versagt oder ein
    einzelner ETL-Schritt von Hand läuft.

### R3 Upsert überschreibt Änderungen aus dem neuen System (verifiziert)

- **Stellen:** `app/api/src/Cichlids.Etl/Persistence/PgUpsert.cs:32-36` und `PgBatchUpsert.cs:53-57`.
  Alle Spalten außer `created_at` (bei `post` zusätzlich `rating_*`) werden per `EXCLUDED`
  gesetzt.
- **Was ein erneuter Lauf mit dem neuen System macht:**
  - Bei Profilen fallen `display_name`, `city`, `external_avatar_url` und `last_login_at` auf
    den Legacy-Stand zurück.
  - Bei Tanks werden `state` und die Texte zurückgesetzt.
  - Bei Posts werden `title`, `description`, `state` und `view_count` überschrieben.
  - Bei Kommentaren werden `deleted_at` und `delete_reason` aus der Quelle gesetzt
    (`CommentMigrationStep.cs:177-180`). Ein im neuen System moderierter Kommentar wird also
    wieder sichtbar.
  - Ebenso betroffen: `DELETE FROM tank_media` (`PictureMigrationStep.cs:553`) und
    `DELETE FROM slug_alias` mit neuem `created_at` (`:654-661`). Dasselbe gilt für
    `DELETE FROM post_species` ohne Filter (`SpeciesLinksStep.cs:85`) und für den
    Profilbild-Backfill.
- **Folge:** Ein erneuter Lauf nach dem Go-live ist vorgesehen (Verify erwartet API-Profile), aber
  nicht sicher.
- **Entscheidung:**
  - Der ETL läuft ausschließlich gegen eine leere Zieldatenbank. Vor dem ersten Schreibzugriff
    prüft er, ob Daten vorhanden sind, und bricht in dem Fall mit einer klaren Meldung ab.
  - Auf Dev und Staging geht jedem Lauf ein vollständiger Reset voraus (Datenbank leeren, Schema
    neu aufbauen). Jeder Lauf startet damit vom selben Stand und ist eine Generalprobe für den
    Go-live.
  - In Produktion läuft der ETL genau einmal beim Go-live. Der Reset verweigert sich in
    Produktion.
  - Korrekturen nach dem Go-live laufen über gezielte Datenbank-Migrationen.
  - Umfang des Resets: Datenbank und Keycloak-Konten werden vor jedem Lauf zurückgesetzt, damit
    auch der Konto-Import jedes Mal wie beim ersten Lauf in Produktion abläuft. Die Medien im
    Object Store bleiben liegen; ihre Ablageorte sind deterministisch. Vor dem Go-live läuft
    einmal eine vollständige Generalprobe mit leerem Object Store.
  - Begründung: Die Altdaten sind eingefroren. Nach dem Go-live gibt es keinen Anlass für einen
    weiteren Lauf, und jeder weitere Lauf würde Änderungen aus dem neuen System gefährden.
  - Folgen für andere Punkte: R2 ist dadurch entschärft (eigene Nachfrage), R34 (Verify zählt
    ganze Zieltabellen) richtet sich nach diesem Modell.

### R4 Unbehandelte Ausnahme stoppt den API-Host (verifiziert)

- **Stellen:**
  - `OutboxDispatcherHostedService.cs:23-29`: Um `RunOnceAsync` gibt es kein try/catch.
  - `OutboxDispatchService.cs:109`: Gefangen werden nur `HttpRequestException` und
    `TaskCanceledException`.
  - `WebhookAdminEndpoints.cs:39`: Die URL wird nur per `Uri.TryCreate(..., Absolute)` geprüft.
- **Folge:** Eine Webhook-URL wie `ftp://x` oder `file:///...`, ein kaputtes Payload
  (`JsonException`) oder ein vorübergehender DB-Fehler beendet den BackgroundService. Ab .NET 6
  stoppt das den ganzen Host. Das Event bleibt unverteilt, und jeder Neustart stürzt beim ersten
  Poll wieder ab (Crash-Loop).
- **Richtung:**
  - URL-Schema auf http und https beschränken.
  - Fehler pro Pass und pro Event fangen, protokollieren und als Fehlversuch zählen.
- **Entscheidung:**
  - Die Webhook-Registrierung akzeptiert nur `http` und `https`.
  - Jeder Fehler beim Zustellen eines Ereignisses zählt als Fehlversuch dieses Ereignisses. Nach
    der Höchstzahl an Versuchen wird es aufgegeben und blockiert keine anderen Ereignisse.
  - Jeder Durchlauf des Hintergrundprozesses ist als Ganzes abgesichert und protokolliert seine
    Fehler. Der API-Host läuft bei einem Fehler weiter.
  - Tests decken ungültige URL, kaputtes Ereignis und Datenbankfehler ab.

### R5 Outbox-Backoff rechnet ab Event-Erzeugung (verifiziert)

- **Stelle:** `OutboxDispatchService.cs:63`: `due = OccurredAt + Backoff(AttemptCount)`.
- **Folge:** War ein Subscriber einige Minuten nicht erreichbar, ist jedes ältere Event bei jedem
  Poll sofort fällig. `MaxAttempts` ist in Sekunden aufgebraucht, und die Events werden als
  "gave up" verworfen. Der Test `RunOnceAsync_FailingSubscriberRetriesAndLaterSucceeds` datiert
  `OccurredAt` zurück und verdeckt das Verhalten.
- **Richtung:** Zeitpunkt des letzten oder nächsten Versuchs speichern (Migration).
- **Entscheidung:**
  - Pro Ereignis wird der Zeitpunkt des nächsten Versuchs gespeichert. Der Abstand zählt ab dem
    letzten Versuch. Ein Test simuliert einen längeren Ausfall.
  - Eine Admin-Funktion stößt aufgegebene Ereignisse erneut an.
  - Ein Ereignis wird erst aufgegeben, wenn es älter ist als eine konfigurierbare Zeitspanne;
    bis dahin wird es mindestens stündlich erneut versucht. Die bisherige Grenze von 10 Versuchen
    (rund 85 Minuten) ist zu kurz.
  - Der Standardwert der Zeitspanne ist 7 Tage. Begründung: Das überbrückt ein langes Wochenende
    oder eine Urlaubswoche des Betreibers; Empfänger verarbeiten auch ältere Ereignisse sinnvoll,
    und die stabile Ereignis-ID erlaubt ihnen, Duplikate zu erkennen.

### R6 Keine App-Tests in CI (verifiziert)

- **Stelle:** `.github/workflows/ci.yml:96-102`. Die Jobs `backend`, `client-core` und `e2e` stehen
  nur als Kommentar.
- **Folge:** `dotnet test`, vitest, Typecheck und Playwright laufen nicht. Ein Commit, der API oder
  ETL bricht, bleibt grün. ADR-0003 und ADR-0005 verlangen ein Grün-Gate, und DoD #5 aller Tasks
  verlangt grüne CI-Suiten. Der Story-Test-Binding-Check greift erst bei Done-Stories.
- **Entscheidung:**
  - CI erhält drei App-Jobs:
    - Backend: .NET bauen und alle Tests ausführen, mit Testcontainers.
    - Frontend: Typecheck und vitest für `client-core` und `mobile`.
    - E2E: die Playwright-Schreibtests gegen den Compose-Stack ohne Altdaten.
  - Der Playwright-Smoke mit Altdaten folgt, sobald TASK-2.17 anonymisierte Testdaten liefert:
    TASK-2.24 (To Do, abhängig von TASK-2.17).
  - Die Maestro-Flows folgen mit TASK-2.25 (To Do). Der Task schließt F13 und die Maestro-Befunde
    aus R42 ein.

### R7 Dev-Realm ungeschützt gegen Wiederverwendung (verifiziert)

- **Stelle:** `app/stack/keycloak/cichlids-realm.json`.
  - Zeile 4: `sslRequired: none`.
  - Zeile 5: `registrationAllowed: true` ohne E-Mail-Prüfung und ohne Brute-Force-Schutz.
  - Zeile 212: Password-Grant (`directAccessGrantsEnabled`) am öffentlichen Client `cichlids-app`.
  - Zeile 214: Redirect-URIs mit Wildcards.
  - Zeile 215: `webOrigins` mit `+`.
  - `dev-mod` hat die Rolle Admin und ein bekanntes Passwort.
  - Die Datei selbst sagt nicht, dass sie nur für die Entwicklung gilt.
- **Folge:**
  - Übernimmt Staging die Datei, gibt es einen Admin mit bekanntem Passwort, offene Registrierung
    und Login über HTTP.
  - `bootstrap.sh` importiert den Realm mit offener Registrierung vor dem Account-Import. ADR-0023
    verlangt die umgekehrte Reihenfolge.
- **Richtung:** Ein eigenes Realm-Artefakt für Staging und Produktion, das Dev-Realm als solches
  kennzeichnen und per Test abgrenzen.
- **Entscheidung:**
  - Die Dev-Realm-Datei bleibt und ist deutlich als nur lokal gekennzeichnet.
  - Für Staging und Produktion gibt es eine eigene Konfiguration:
    - HTTPS-Pflicht,
    - Registrierung erst nach dem Konto-Import,
    - kein Password-Grant am öffentlichen Client,
    - exakte Redirect-URIs je Umgebung,
    - keine Testkonten. Admins entstehen außerhalb von Git.
  - Ein CI-Test stellt sicher, dass keine Dev-Einstellung in diese Konfiguration gerät.
  - `bootstrap.sh` öffnet die Registrierung erst nach dem Konto-Import (ADR-0023).

### R8 Netzwerkfehler beim Token-Refresh loggt aus (verifiziert)

- **Stelle:** `app/mobile/src/auth/AuthProvider.tsx:148-164`. Jeder Fehler in `refreshAsync` führt
  zu `outcome = null` und löscht die Tokens.
- **Folge:** Läuft das Access-Token während eines kurzen Funklochs ab, ist der Nutzer ausgeloggt,
  und das gültige Refresh-Token ist verloren. Eine laufende Upload-Maske wird verworfen
  (`app/mobile/app/upload/index.tsx:31`).
- **Richtung:** Nur bei `invalid_grant` beziehungsweise 400 und 401 ausloggen.
- **Entscheidung:**
  - Ein Mitglied meldet sich pro Gerät einmal an und bleibt angemeldet, bis es sich selbst
    abmeldet. Es gibt keine Abmeldung nach Inaktivität oder Zeitablauf und keine Abmeldung bei
    Fehlern. Begründung: Die Plattform hält keine kritischen Daten; jede erzwungene Neuanmeldung
    kostet aktive Mitglieder, und die Schwelle zur Beteiligung soll so niedrig wie möglich sein.
  - Technische Grundlage sind langlebige Sitzungen in Keycloak (Offline-Tokens) ohne praktische
    Ablaufgrenze. Die native App speichert sie im SecureStore.
  - Bei Netzwerk- und Serverfehlern behält die App die Tokens. Der einzelne Aufruf scheitert mit
    einer verständlichen Meldung. Tests decken das ab.
  - Gesperrte oder gelöschte Konten verlieren ihre Sitzungen; das ist Moderation und keine
    Abmeldung im Sinne dieser Regel.
  - Die Eingaben der Upload-Maske (Foto, Titel, Beschreibung) bleiben lokal erhalten, auch über
    eine Abmeldung hinweg, und stehen nach dem nächsten Login wieder bereit.
  - Offen: wo die Web-App die dauerhafte Sitzung speichert.

---

## Mittel

### R9 Beliebig viele Bewertungen pro Mitglied und Ziel (Index verifiziert)

- **Stellen:**
  - `Configurations/RatingConfiguration.cs:16`: Es gibt nur einen Unique-Index auf
    `legacy_comment_id`.
  - `CommentsWriteService.cs:45-56` prüft nichts.
- **Folge:**
  - ADR-0020 nennt "one rating per member per target" als Semantik. Ein Mitglied kann denselben
    Inhalt beliebig oft bewerten und `rating_average` sowie die Sortierung verschieben.
  - Eigene Inhalte zu bewerten ist ebenfalls möglich.
- **Bezug:** DRAFT-4 behandelt die Frage, wie oft bewertet werden darf.

### R10 SSRF über Webhook-URLs

- **Stelle:** `WebhookAdminEndpoints.cs:39`.
- **Befund:** Beliebige Hosts sind erlaubt, auch Link-Local, Loopback und interne Dienste. Der
  Standard-`HttpClient` folgt Redirects.
- **Einordnung:** Nur Admins registrieren, und die Antwort wird nicht zurückgegeben. Interne
  POST-Aufrufe sind trotzdem möglich.

### R11 Kommentartext und Löschgrund ohne Längengrenze

- **Stellen:** `CommentsEndpoints.cs:28`, `CommentConfiguration.cs:13` und
  `DeleteCommentRequest.Reason`.
- **Folge:** Ein Mitglied kann Kommentare bis zur Request-Grenze von Kestrel (30 MB) posten.

### R12 Race beim ersten Login, verwaistes Profil

- **Stelle:** `CurrentProfileService.cs:36-62`: zwei getrennte `SaveChanges` ohne Transaktion und
  ein Check-then-Insert auf `Username`.
- **Folge:**
  - Zwei parallele Erstaufrufe desselben `sub` enden in HTTP 500.
  - Scheitert der zweite Insert, bleibt ein öffentlich sichtbares Profil ohne Identität zurück.
  - Der nächste Aufruf legt ein weiteres Profil an.

### R13 Keine Quota für Uploads und Entwürfe

- **Stellen:** `UploadsEndpoints.cs:32` und `UploadsWriteService.cs`.
- **Folge:** Jedes Mitglied kann unbegrenzt viele Uploads bis 25 MB erzeugen. Jeder davon wird
  dekodiert (bis 100 MP), und Original und Varianten landen im Objektspeicher.

### R14 Outbox: Batch-Auswahl, fehlender Index, Sperren während HTTP

- **Befunde:**
  - `ORDER BY occurred_at LIMIT n` zieht auch Zeilen, die noch nicht fällig sind. Neuere Events
    kommen dann nicht dran.
  - In `OutboxEventConfiguration.cs` fehlt ein partieller Index auf `dispatched_at IS NULL`.
  - Die Transaktion hält die Zeilensperren während der HTTP-Aufrufe.

### R15 Username-Kollision bricht den Profil-Step ab

- **Stelle:** `ProfileMigrationStep.cs:115-138`. Legacy-Usernames werden nur gegen Namen aus
  demselben Lauf geprüft.
- **Folge:** Ein in der API angelegtes Profil, etwa `alice`, blockiert den gleichnamigen
  Legacy-Usernamen. Der Unique-Index schlägt zu, und der gesamte Profil-Step wird
  zurückgerollt.

### R16 Leere Galerien werden verworfen

- **Stelle:** `GalleryMigrationStep.cs:40-45` migriert nur Galerien mit mindestens einer
  Bildzuordnung.
- **Befund:**
  - Die Regel lautet "non-deleted galleries convert to collections". AC 3.7 verlangt jede
    Legacy-Galerie genau einmal.
  - `VerifyStep.cs:650-659` kopiert denselben Filter und prüft hier also nicht unabhängig.
  - Der Klassen-Kommentar beschreibt den Filter falsch.

### R17 Generierte Slugs haben 10 statt 6 bis 8 Zeichen

- **Stellen:** `SlugGenerator.cs:24` (`Length = 10`) und `PictureMigrationStepTests.cs:82,130`
  (Prüfung auf 10).
- **Befund:** Die vereinbarte Slug-Strategie in TASK-3 nennt 6 bis 8 Zeichen.

### R18 Dry-Run von `all` irreführend, lädt Forum-Anhänge hoch

- **Stellen:**
  - `Runtime/EtlRunner.cs:21-24` rollt jeden Step einzeln zurück. Spätere Steps sehen die
    Ergebnisse früherer nicht, Statistik und Verify zeigen deshalb Scheinfehler.
  - Der Kommentar "statistics reflect the real outcome" stimmt nicht.
  - `ForumMigrationStep.cs:420` schreibt Anhänge auch im Dry-Run in den Objektspeicher.

### R19 Vote-Zeitstempel ungeprüft als UTC gelesen

- **Stellen:** `CommentMigrationStep.cs:414,447`. `user_cichlids_comments_rated.tstamp` ist
  `DATETIME` und wird pauschal als UTC markiert.
- **Folge:** Läuft der Legacy-Server auf lokaler Zeit, verschieben sich die Zeitstempel um ein bis
  zwei Stunden.

### R20 `media --force` stellt fehlende Originale nicht her

- **Stelle:** `MediaSeedCommand.cs:161`. Hochgeladen wird nur, wenn die Größe in der Datenbank
  abweicht.
- **Folge:** Ein im Objektspeicher verlorenes Original meldet `media-verify` als fehlend, aber
  `--force` repariert es nicht.

### R21 Pfad-Traversal und Wildcards im Legacy-Bildpfad

- **Stelle:** `LegacyImageLocator.cs:20,24,30`.
- **Befund:**
  - Ein absoluter Pfad aus der Datenbank hebelt `root` aus, und `../` verlässt den Baum.
  - `*` und `?` in Dateinamen wirken in `EnumerateFiles` als Wildcards.

### R22 Deaktivierte Nutzer werden zu Platzhaltern (Klärung)

- **Stelle:** `ProfileMigrationStep.cs:229` filtert `deleted = 0 AND disable = 0`.
- **Folge:** Nutzer mit `disable = 1` und eigenen Inhalten werden zu "Former member"-Platzhaltern,
  ihre Posts werden archiviert. Die Hygieneregel nennt Platzhalter nur für hart gelöschte Besitzer
  und anonyme Uploads.
- **Zu klären:** Ist das gewollt?

### R23 `strip` entfernt das ICC-Farbprofil

- **Stelle:** `Media/ImageProcessor.cs:47`.
- **Folge:** Bei Bildern in Display-P3 oder AdobeRGB verschieben sich die Farben der Varianten.

### R24 Fehlende Limits in ImageProcessor und S3-Download

- **Befunde:**
  - Die Pixelgrenze steht nur im Upload-Service, andere Aufrufer haben keine
    (`ImageProcessor.cs:63`).
  - Jede Variante dekodiert das Bild neu.
  - `S3ObjectStore.GetAsync` (`:56-60`) lädt das ganze Objekt in den Speicher.

### R25 Staging-Build fällt still auf localhost zurück

- **Stellen:** `app/mobile/app.json:43-46`, `src/api/client.ts:7-10` und `src/auth/config.ts:5-8`.
- **Folge:** Fehlen `EXPO_PUBLIC_API_URL` und `EXPO_PUBLIC_KEYCLOAK_URL`, sprechen Builds gegen
  `localhost`. Auf einem Gerät ist das das Gerät selbst. Es gibt keine Fehlermeldung und keine
  `eas.json`.

### R26 Login-Button tot, wenn Keycloak-Discovery scheitert

- **Stellen:** `AuthProvider.tsx:45,193`, `app/(tabs)/me/index.tsx:43` und
  `CommentComposer.tsx:52`.
- **Folge:** Bei falscher Issuer-URL oder CORS-Fehler bleibt der Button ohne Meldung und ohne
  erneuten Versuch deaktiviert.

### R27 Nachladen von Listen bleibt hängen

- **Stelle:** `app/mobile/src/hooks/usePagedList.ts:53-56`.
- **Folge:** Wird ein `loadMore` durch einen Filterwechsel oder `reload()` überholt, bleibt
  `loadingMore` dauerhaft gesetzt, und die Liste lädt keine weiteren Seiten.

### R28 Lücken im Secret-Scanner

- **Stelle:** `tools/check-no-plaintext-secrets.py:68, 92-93, 103`.
- **Befunde:**
  - Eine Zeile mit `gitleaks:allow` wird vollständig übersprungen, auch wenn dort zusätzlich ein
    echter Schlüssel steht.
  - Ein unquotierter Wert nur aus Buchstaben und Unterstrichen gilt nie als Secret.
- **Bezug:** DRAFT-2 (Ersatz durch gitleaks).

### R42 Tests prüfen weniger, als ihr Name sagt

- **Outbox:**
  - `OutboxDispatchServiceTests.RunOnceAsync_TwoConcurrentDispatchersDoNotDeliverTheSameEventTwice`
    arbeitet mit einem einzigen Event und beweist kaum Parallelität.
  - Für Poison-Message, Nicht-HTTP-URL, Backoff nach Stillstand und Doppelbewertung gibt es keine
    Tests.
- **client-core:** `client.test.ts:178-182` heißt "rejects with an ApiError", prüft aber nur, dass
  überhaupt ein Fehler geworfen wird.
- **Maestro:**
  - Die Flows prüfen deutsche Texte, die App startet aber standardmäßig auf Englisch.
  - `browse-gallery.yaml` endet mit `assertVisible: ".+"`, was auf jeden Screen zutrifft.
  - `appId` ist hart auf `localhost:8081` gesetzt.
- **Schema:**
  - `SchemaMigrationTests` prüft nicht, ob Snapshot und Modell übereinstimmen
    (`HasPendingModelChanges`).
  - Unique-Constraints für Bewertungen und Slugs sind nicht abgedeckt.
- **Slugs:** `PictureMigrationStepTests` zementiert die Slug-Länge 10 (siehe R17).
- **ImageProcessor:** Für ihn gibt es keine direkten Tests.

### R43 C#-Doc-Kommentare mit Inline-Tags

- **Stellen:**
  - `Domain/Entities/Rating.cs:5`, `MediaItem.cs:7`, `DiscussionThread.cs:7` und
    `DiscussionPost.cs:4-6`.
  - `Persistence/CichlidsDbContext.cs:10` und `Persistence/ServiceCollectionExtensions.cs:10-12`.
  - `Storage/S3ObjectStore.cs:168`.
  - Weitere Stellen sind möglich; bei der Umsetzung wird das ganze `app/api` durchgesucht.
- **Befund:** Die Projektregel verbietet Inline-Tags wie `<c>`, `<see>` und `<paramref>` in
  Doc-Kommentaren.

### R44 Notizen zu 3.13 und 3.15 widersprechen ADR-0023

- **Stellen:** `backlog/tasks/task-3.15 ...md:82, 93` und `task-3.13 ...md:29`.
- **Befund:** Die Notizen beschreiben eine Verknüpfung über die verifizierte E-Mail. Laut
  `CurrentProfileService.cs:13-16` und ADR-0023 verknüpft die API nie über die E-Mail.
- **Folge:** Eine Abnahme würde ein Verhalten prüfen, das nicht existiert.

---

## Niedrig

### R29 Wildcards im Redirect-Lookup

- **Stelle:** `LegacyRedirectsQueryService.cs:30,62` übergibt den Alias unescaped an `ILIKE`.
- **Folge:** `/pictures/pic/%25.html` leitet auf ein beliebiges Bild um, statt mit 410 zu
  antworten.
- **Gleiches Muster:** die Suchen in `SpeciesQueryService.cs:22` und `CommunityQueryService.cs:52`.
  Dort ist es harmlos.

### R30 Redirect ohne Sichtbarkeitsprüfung

- **Stelle:** `LegacyRedirectsQueryService.cs:22-43` prüft weder `State == Published` noch
  `DeletedAt`.
- **Folge:** Archivierte oder gelöschte Posts erhalten einen 301 auf eine Seite, die 404 liefert.
  Dokumentiert ist 410. Tanks sind gleich betroffen.

### R31 `/api/me` liefert 500 bei Nicht-Member-Profil

- **Stelle:** `MeEndpoints.cs:28`.
- **Folge:** Ist eine Identität an ein Profil mit `Kind != Member` gebunden, wirft der Endpunkt
  `InvalidOperationException`.

### R32 CORS in Development: `Origin: null` ergibt 500

- **Stelle:** `Program.cs:36`.
- **Folge:** `new Uri(origin)` wirft bei `Origin: null` eine `UriFormatException`, die Antwort
  ist 500.

### R33 Kein Startfehler bei fehlender Auth-Authority

- **Stelle:** `appsettings.json`.
- **Befund:** Fehlt `Authentication:Authority` außerhalb von Development, scheitert erst jede
  authentifizierte Anfrage zur Laufzeit. Für das Slug-Secret gibt es dagegen eine Prüfung beim
  Start.

### R34 Verify zählt ganze Zieltabellen

- **Stellen:** `VerifyStep.cs:279,389,558-565,614`.
- **Befunde:**
  - `COUNT(*)` über `tank`, `post`, `comment`, `rating` und `comment_vote` schlägt fehl, sobald
    das neue System eigene Zeilen hat.
  - `VerifyStep.cs:31-33` leitet einen Teil der Erwartung aus dem Ziel ab.

### R35 Anonyme Datensätze: Warnung und Upsert pro Zeile

- **Stellen:** `PictureMigrationStep.cs:284-285`, `TankMigrationStep.cs:138`,
  `GalleryMigrationStep.cs:146` und `CommentMigrationStep.cs:475`.
- **Folge:** Jede anonyme Zeile erzeugt einen DB-Roundtrip und eine Warnung, das bläht den
  Report auf.

### R36 Keycloak-Admin-Client: PII in Ausnahmen, Fallback admin/admin, kein Backoff

- **Stellen:** `KeycloakAdminClient.cs:341-343` und `Program.cs:116-117`.
- **Befunde:**
  - Der komplette Response-Body landet in der Exception und kann Username oder E-Mail enthalten.
  - Fallback-Zugang ist `admin`/`admin`.
  - Bei 429 und 5xx gibt es kein Backoff.

### R37 DiscussionPost-Invariante ohne Check-Constraint

- **Stelle:** `Domain/Entities/DiscussionPost.cs:4-7` und `DiscussionPostConfiguration.cs`.
- **Befund:** Der Kommentar beschreibt eine Invariante: Gastposts haben keine `AuthorProfileId`,
  alle anderen haben keinen `PosterName`. Die Datenbank erzwingt sie nicht.

### R38 Schwächen in `bootstrap.sh`

- **Stellen:** `app/stack/bootstrap.sh`.
- **Befunde:**
  - Zeilen 56-62: Die Postgres-Wartschleife meldet "Postgres is up.", auch wenn sie nach 60
    Sekunden aufgibt.
  - Zeile 15: Der Default für das Legacy-Verzeichnis ist ein absoluter Pfad statt eines Pfads
    relativ zu `REPO_ROOT`.
  - Zeilen 115-117: Ist ein Realm vorhanden, wird er nicht aktualisiert. Realm-Änderungen im
    Repo kommen damit auf bestehenden Stacks nie an.

### R39 Lokaler Stack bindet an 0.0.0.0

- **Stelle:** `app/stack/compose.yaml`.
- **Folge:** Postgres, RustFS und Keycloak (admin/admin) sind auf einem Host mit offenen Ports
  von außen erreichbar.

### R40 App-Install wird ohne pnpm still übersprungen

- **Stelle:** `.devcontainer/postCreateCommand.sh:79`.
- **Befund:** Fehlt `pnpm`, entfällt der Block ohne Warnung.

### R41 Mobile: Fehleranzeige und Barrierefreiheit

- **Fehleranzeige:** Ein Ladefehler bei Kommentaren erscheint als "Noch keine Kommentare"
  (`gallery/[slug].tsx:105`, `tanks/[id].tsx:118`).
- **Barrierefreiheit:** `Button` hat im Busy-Zustand kein `accessibilityLabel`
  (`Button.tsx:42`).
- **Übersetzung:** Fehlertexte der API erscheinen unübersetzt in der UI (`me/index.tsx:63`,
  `uploadForm.ts:64-69`).

### R45 Kommentare, die nicht stimmen

- **`ImageProcessor.cs:55-58`:** `LoadOriented` liest keine Pixeldaten. Abgeschnittene Dateien
  scheitern deshalb erst beim Schreiben.
- **`EtlRunner.cs:6-10`:** siehe R18.
- **`GalleryMigrationStep`:** Der Klassen-Kommentar beschreibt den Filter falsch, siehe R16.
- **`S3ObjectStore.cs:168`:** "kein Key ist sensibel", siehe R1.
- **Archiv-Tresor:** Der Kommentar spricht von der vollen Quellzeile, `RawCommentRow` enthält aber
  nur die selektierten Spalten.
- **`smoke.mjs`:**
  - Zeilen 6-7 verweisen auf eine README, die nicht existiert.
  - Zeile 23: `OUT_DIR` zeigt außerhalb des Repos auf `/workspaces/app-screens`.
- **`app/stack/README.md:191`:** Die README nennt den Repo-Root als Ziel der Screenshots, auch
  das stimmt nicht.
- **`AuthProvider.tsx:129-131`:** Der Kommentar sagt, nach dem Logout frage Keycloak wieder nach
  Zugangsdaten. Das gilt nur, wenn der Back-Channel-Aufruf gelingt.

### R46 PROJECT-STATUS.md in mehreren Punkten veraltet

- **DRAFT-5:** wird als offen geführt, liegt aber in `backlog/archive/drafts/`.
- **ADR-Liste:** Sie nennt 0019 bis 0021. Es existieren 0019 bis 0023.
- **Story-Zahlen:** Genannt werden 22 beziehungsweise 26 In Review. Tatsächlich stehen 27
  Stories auf In Review, 3.28 steht auf To Do.
- **Account-Import:** Eine Stelle sagt, der Import sei nicht gegen den Dev-Stack gelaufen. F21
  sagt, er lief am 2026-09-29.
- **F14:** Der Merge ist erfolgt, F14 ist aber nicht abgehakt.
- **Sonstiges:** Die Worktree-Beschreibungen und die Angabe zum Realm sind überholt.

---

## Ohne Befund

Die Reviewer haben unter anderem diese Bereiche geprüft und als korrekt befunden:

- **Admin- und Moderator-Endpunkte:** Policies und Tests vorhanden.
- **Draft-Besitz:** kein IDOR. Publish und Discard sind atomar.
- **Öffentliche Abfragen:** Sie filtern auf veröffentlichte, nicht gelöschte Inhalte.
- **JWT-Validierung und Slug-Secret:** Die JWT-Validierung ist korrekt, und das Slug-Secret wird
  außerhalb von Development verweigert.
- **Pagination und Uploads:** Die Pagination ist begrenzt. Uploads werden vor dem Dekodieren
  geprüft, und bei Fehlern wird aufgeräumt.
- **Keine Open Redirects, keine N+1-Abfragen.**
- **Datenbank-Constraints:** Unique-Indizes auf allen `legacy_id`-Brücken, Check-Constraints
  für "genau ein Ziel", sinnvolles Cascade-Verhalten.
- **Idempotenz der ETL:** `created_at` ist über Läufe stabil. Die Deduplizierung der Votes und die
  Aggregate stimmen.
- **Account-Import:** deterministische IDs, keine Passwörter, keine PII in Logs.
- **Mobile:** PKCE und State-Prüfung sind korrekt. Tokens liegen nativ in SecureStore, im Web in
  sessionStorage. Es gibt keine clientseitigen Berechtigungsentscheidungen.
- **Konsistenz:** Die i18n-Schlüssel sind vollständig. OpenAPI-Client und API stimmen überein.
- **Isolierter Schreib-Stack, ADR-Index und Backlog-Häkchen:** Der Schreib-Stack für E2E-Tests
  ist sauber isoliert. Der ADR-Index ist konsistent. Im Backlog ist kein AC vorab abgehakt.

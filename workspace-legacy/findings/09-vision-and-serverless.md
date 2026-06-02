# Produktvision & geplante Features — cichlids.com

Synthese aus den Planungsnotizen (`features.txt`, `TODO.txt`, `marketplace.txt`,
`Blog-Themen.txt`, `cichlids-serverless/TODO.txt`, `cichlids-com/assets/notes.txt`,
`cichlids-com/README`) sowie Analyse der geplanten Serverless-/AWS-Architektur
(`cichlids-serverless/`).

Die Notizen sind ein Mix aus Deutsch und Englisch, oft Stichworte. Sie zeichnen
das Bild einer **Foto-Sharing-Community für Cichliden-(Buntbarsch-)Halter**, die
bewusst Mechaniken von **StackOverflow** (Reputation, Q&A, Tags, Badges),
**Flickr/Pinterest** (Photostream, Collections, Galleries, Favorites, Pin) und
einem **vertrauensbasierten Marktplatz** kombiniert.

---

## 1. Content / Sharing (Bilder & Tanks)

Zentrale Inhaltstypen sind **Pictures** (Fischbilder) und **Tanks** (Aquarien /
Einrichtungsbeispiele). Beide bekommen dieselben Sortier-/Entdeckungsachsen
(`features.txt`):

> Pictures:
> - Interesting (bei loggedin usern default) — *questions that might be interesting for you based on your history and trag preference*
> - Newest
> - most viewed (mit timespan: week, month, year, alltime)
> - top rated (mit timespan)
>
> Tanks: wie Pictures

- **Pin-/Sammel-Mechanik à la Pinterest** — explizit, um sich Inspiration zu
  kuratieren: *"Pin (to gallery, z.B. um sich Tanks/Einrichtungsbeispiele
  zusammenzustellen; mal bei Pinterest gucken, wie das geht)"* (`features.txt`).
- **Favorites**, **Photostream**, **Collections**, **Galleries** je User.
- Kommentare mit **Rating** (siehe `Comment.cs`: `Body`, `Rating`, `Poster`,
  Typ `"picture"`/`"tank"`).

## 2. Social / Community

Aus `features.txt` (Single-Picture-Aktionen beim Klick aufs Profilbild) und
`cichlids-serverless/TODO.txt`:

- **Follow** (Freunde/Folgen), **Followers**-Liste
- **Private Nachrichten / PNs** mit Indikator-Icon für neue Nachrichten
- **Profil**, **Message**, **Tags** des Users
- Aktivitäts-Funktionen: *"Private Nachrichten / Follow Funktion / Like Funktion
  / Collect Function? / Shuffle Function für Front page / User engagement mit
  Kommentaren"* (`cichlids-serverless/TODO.txt`)
- **Onboarding** beim ersten OpenID-Login: Terms akzeptieren, Username + Name
  wählen (`cichlids-com/assets/notes.txt`)
- Dashboard/Feed-Idee unter Verweis auf **getstream.io** (`cichlids-serverless/TODO.txt`)

## 3. Reputation & Gamification (StackOverflow-Stil)

Klar an StackOverflow angelehnt (`features.txt`):

- **Reputation** je User, mit Zeitfenstern: *"Users: (jeweils week, month,
  quarter, year, all time): Reputation, New users, Voters, Newest Comments"*
- **Badges** als eigene Explore-Sektion: *"Bagdes — alle badges auflisten und wer
  sie hat, siehe stackoverflow"*
- **Leaderboards / Listen**: *"Top reviewers / top taggers / top uploaders"*
- **Voting** (Voters-Liste, Votes-Sortierung bei Questions)

## 4. Q&A (StackOverflow-Stil)

Eigener Inhaltstyp **Questions** neben Pictures/Tanks (`features.txt`):

> Questions: newest · featured (offener bounty?) · faq · votes · active ??? · unanswered

- **Bounties** (offene Kopfgelder auf Fragen)
- **Unanswered**-Filter, **Active**, **Featured**
- Verzahnung mit Reputation/Tags wie bei StackOverflow

## 5. Discovery / Explore

`features.txt` "Explore"-Bereich:

- **Highlights**: Popular aus Pictures / Tanks
- **Galleries**: User-Galerien
- **Tags**: Themen wie *Tanganyika* usw., *"wie stackoverflow liste aller tags"*
  (Taxonomie/Region als Tag-Achse)
- **Badges**-Übersicht (s.o.)
- **Suche** (rechts oben), Header mit Sortierachsen
- **"Interesting"** als personalisierter Default für eingeloggte User
  (history- und tag-präferenzbasiert) → siehe Ranking/ML

Hierzu passend liegt die Datei
`/workspaces/legacy-source/Checklist_of_the_Cichlid_Fishes_of_Lake.pdf` vor — eine
wissenschaftliche Artenliste, die als **Taxonomie-/Tag-Datenquelle** (Arten,
Seen/Regionen) für die Tags und Suche dienen kann. (PDF nicht geparst, nur als
Quelle vermerkt.)

## 6. Marketplace (vertrauensbasiert)

Aus `marketplace.txt` — ein Marktplatz mit **"Trusted Profiles"** als Kern:

> "Trusted profile" — damit die Leute von einem kaufen, muss man ein
> vertrauenswürdiges Profil anbieten:
> - Bilder von seinen Fischen posten
> - Bilder von seinen Tanks
> - Bilder von seiner Anlage
> - vielleicht sogar Expertenadvice usw.
> - und natürlich ganz klar Empfehlungen

Das Vertrauen speist sich direkt aus den Content-/Reputation-/Community-Bausteinen
(geposteter Content, Empfehlungen, Reputation) — d.h. der Marktplatz ist kein
isoliertes Feature, sondern Monetarisierung des aufgebauten Profils.

## 7. Notifications

`features.txt` (Header-Icons) + `cichlids-serverless/TODO.txt` + die real
implementierten Lambdas:

- **Neue-PN-Indikator**, **Notification-Icon** (*"wenn Freund geblaht hat, neuer
  Kommentar auf eines Users Bild"*), Account-Settings-Icon
- **E-Mail-Benachrichtigungen bei neuen Kommentaren** auf eigene Pictures/Tanks —
  einziger end-to-end implementierter Notification-Pfad (siehe Serverless-Teil),
  inkl. **Opt-out-Settings** (`SkipOnNewCommentsOwnPictures`,
  `SkipOnNewCommentsOwnTanks`).

## 8. Monetization / Affiliate-Blogs

`Blog-Themen.txt` beschreibt ein **Affiliate-Blog-Netzwerk** zum Thema
"Aquariumbedarf" (Amazon-Affiliate-Muster, ursprünglich von Michael Jäckel /
Affiliate-Turbo):

- Ein **Haupt-Blog** "Aquariumbedarf" + zahlreiche **Unter-Blogs** je Produktthema
  (Becken, Beleuchtung, Bücher, Deko, "für Einsteiger", Filter, Fischgläser,
  Futterautomat, Heizung, Hydrometer, Sand, Pumpen, Reinigung, Ständer,
  Thermometer, Zuchttanks).
- Alle Blogs **untereinander verlinkt, gleiches Aussehen**, sodass der Besucher
  den Blog-Wechsel nicht bemerkt — saubere Trennung nach Produktbereich zur
  Affiliate-Conversion.

Zusammen mit dem Marketplace bilden die Affiliate-Blogs die Monetarisierungsschiene.

## 9. Ranking / ML

`TODO.txt` (Root) verweist auf:

- **Online Parameter Selection for Web-based Ranking Problems**
  (blog.acolyer.org) — d.h. Ranking-Parameter (z.B. für "Interesting"/Front-page)
  sollen **online / ML-gestützt** gewählt werden.
- **Polly** (App-vNext) — Resilience/Retry-Bibliothek (für robuste Aufrufe).
- `cichlids-com/README` ergänzt: Paper zu **interpretable new-user clustering und
  churn prediction** für eine mobile Social-App — Signal für geplante
  Churn-/Engagement-Modelle.

Die personalisierte **"Interesting"**-Sortierung (history- und tag-präferenzbasiert)
ist die Produkt-Manifestation dieser Ranking-Ambition.

---

## Serverless / AWS-Intent

**Verortung:** `/workspaces/legacy-source/cichlids-serverless/`. Echter Inhalt:
`cichlids.aws/` (Hauptprojekt `Cichlids.Aws`), `csharp-lambda/` (leeres Boilerplate)
und die TODO-Dateien. `Samples/` ist Vendor-Beispielcode (AWS SDK) und nur als
**Signal für den anvisierten AWS-Stack** relevant: DynamoDB, S3, SES, SQS, RDS,
Glacier, CloudWatch, SimpleDB (+ SimpleDB-Session-Provider, S3 TransferUtility,
PetBoard). `csharp-lambda/` ist ein verworfenes `dotnetcore1.0`-Skeleton (leerer
`Handler.cs`); die Substanz steckt in `Cichlids.Aws`.

**Architektur (aus `serverless.yml`, `Cichlids.Aws.csproj`, Handlern):**

- **Runtime/Deploy:** C# auf **.NET Core 2.1**, deployed via **Serverless
  Framework** (`serverless.yml`), Region `us-east-1`. SAM-Templates (`template.yaml`,
  `sam-test/`) existieren als Experimente.
- **Drei Lambda-Funktionen:**
  1. **`Api`** — `Amazon.Lambda.AspNetCoreServer` hostet eine **ASP.NET-Core-MVC-API**
     hinter API Gateway (`http /{proxy+}`, any method, CORS für localhost:4200/4000,
     dev/www.cichlids.com). **Auth via Auth0 JWT Bearer** (`cichlids.eu.auth0.com`),
     inkl. `PayingUsers`-Authorization-Policy (`IsPayingUserRequirement`). Liefert
     u.a. die **Member-Notification-Settings** (`SettingsController`).
  2. **`StoreUserLogin`** — **SQS-getriggert**; konsumiert eine SNS→SQS-Nachricht
     (`UserLoginDto`), persistiert Logins in DynamoDB.
  3. **`CommentNotifications`** — **SQS-getriggert**; deserialisiert SNS-eingepackte
     `Comment`-Events und versendet **E-Mail-Benachrichtigungen** an den Eigentümer
     von Picture/Tank — gated über Stage (nur Production sendet) und über die
     User-Notification-Settings (`Skip…`).
- **Event-Ingress = SNS → SQS → Lambda:** Beide Worker abonnieren **bestehende
  SNS-Topics** (`arn:aws:sns:us-east-1:894559175927:cichlids_user_logins` und
  `:cichlids_comment_posted`) via SQS-Subscription + Queue-Policy. Die Lambdas
  entpacken die SNS-Hülle aus dem SQS-Body (`SNSEvent.SNSMessage` → Payload).
- **Persistenz:** **DynamoDB** — Tabellen `member_settings`, `users`,
  `user-logins`; `users`/`user-logins` mit **DynamoDB Streams** (`NEW_IMAGE`).
  PAY_PER_REQUEST. `AWSSDK.S3` ist referenziert (für Bilder vorgesehen), aber im
  Code noch nicht genutzt.
- **Mail:** **Mailgun** via `FluentEmail.Mailgun` + Razor-Templates
  (`PictureNotification.cshtml`, `TankNotification.cshtml`). (SES war laut Samples
  zunächst angedacht, real verwendet wird Mailgun; API-Key via SSM Parameter Store.)
- **Cross-cutting (TODOs):** Logging mit **CloudWatch** verheiraten, bei
  Errors/Exceptions Devs benachrichtigen (insb. *"notification, wenn sqs function
  exception hat"*), **X-Ray-Tracing**, **Event-Archivierung** sicherstellen,
  DI-Autoregistrierung via **Scrutor** (`cichlids-serverless/TODO.txt`,
  Root-`TODO.txt`).

**Verhältnis zum .NET-Orleans-Backend** (`/workspaces/legacy-source/cichlids.backend`,
Orleans-Silo + `Cichlids.Api`/`Grains`/`Platform`, eigene EventStore-/Scheduler-
Abstraktionen, SQL via `cichlids.sql`):

- Die Serverless-App ist **kein Ersatz**, sondern eine **lose gekoppelte
  Seitenkomponente / Auslagerung von Side-Effects**. Sie ist ein reiner
  **Consumer**: Im Orleans-Backend wurde **keine AWS-SNS-Publish-Logik gefunden**
  (kein `PublishAsync`/`AWSSDK`/`Amazon`-Topic-Publish im Backend — die Treffer
  dort betreffen das interne Orleans-Event-Streaming, nicht SNS).
- Geplant ist offenbar, dass das Orleans-Backend Domain-Events (User-Login,
  Comment-Posted) nach SNS publiziert; die Lambdas übernehmen daraus
  **asynchrone, idempotenz-unkritische Nebeneffekte**: Login-Tracking,
  E-Mail-Benachrichtigungen. Gemeinsame DTOs (`Comment`, `UserDto`, `PictureDto`,
  `TankDto`, `UserLoginDto`) bilden den Vertrag.
- Auth ist **zwischen beiden geteilt** (Auth0). Die `Api`-Lambda kann ergänzende
  REST-Endpunkte (z.B. Member-Settings) serverless bereitstellen.

**Reifegrad:** Nur der **Comment→E-Mail-Pfad** ist end-to-end ausgeführt und
getestet (`Cichlids.Aws.Test/Notifications`, `PayloadPicture.json`). Login-Store ist
angelegt, S3/Bilder, CloudWatch-Alerting und X-Ray sind TODO. Insgesamt ein
**früher, teils experimenteller Prototyp** (mehrere Sandbox-Ordner: `blueprint/`,
`aws-csharp/`, `sam-test/`).

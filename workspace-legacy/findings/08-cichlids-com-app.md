# Forensic Analysis: `cichlids-com` (frontend app + mail templates)

Repo analyzed: `/workspaces/legacy-source/cichlids-com` (read-only).
Layout: `client/` (the live frontend), `assets/` (Velocity mail templates), plus root build/deploy files (`Makefile`, `bitbucket-pipelines.yml`, `README`). `client/app.orig/` is a dead older prototype; `client/docker-web/` is an older Docker packaging.

---

## 1. Tech stack

### Frontend — `client/web/`
**Angular 7** (TypeScript) SPA with **Angular Universal SSR**.

- `client/web/package.json`: `@angular/* 7.2.4`, `@angular/cli 7.3.1`, `@angular/material ^7.3.1`, `@angular/cdk ^7.3.1`, `@angular/flex-layout 7.0.0-beta.23`, `typescript 3.2.4`, `rxjs ^6.4.0`.
- SSR via `@nguniversal/express-engine 7.1.0` + `@nguniversal/module-map-ngfactory-loader`; `express 4.16.4`; server entrypoint `client/web/server.ts`, webpack server build `client/web/webpack.server.config.js`, `npm run build:ssr` / `serve:ssr`. Browser + server modules: `src/app/app.module.ts`, `src/app/app.server.module.ts`, `src/app/app.shared.module.ts`. State-transfer interceptors `browser-state.interceptor.ts` / `server-state.interceptor.ts`.
- Notable libraries: `auth0-js 9.10.0` + `auth0-lock 11.14.0` + `jwt-decode` (auth); `fine-uploader 5.16.2` (chunked uploads); `ngx-infinite-scroll`, `ngx-cookie-service`, `ngx-color-picker`, `angular2-moment`, `hammerjs`; **`raven-js 3.27.0` → Sentry** error reporting (DSN hardcoded in `app.module.ts`, prod-only — see Redactions).
- Build commit history shows migration from a Webpack-based Angular 2 starter (`client/docker-web/files/package.json`, `angular2 2.0.0-beta.11`) to Angular CLI, finally to Angular 7 (commit `b31027b "Update to Angular 7"`).

### Backend — NOT in this repo
There is **no server/backend source here**. Evidence the backend is a separate JVM/Java service:
- `assets/*.vm` are **Apache Velocity** templates (use `#parse(...)`, `$var`) — a Velocity-rendering (JVM) mail layer. No code in this repo renders them (`grep velocity|.vm` over `client/web/src` = 0 hits; no `pom.xml`/`*.java`/`build.gradle` anywhere).
- `README` contains Maven/JBoss commands: `mvn clean package jboss-as:deploy`, `mvn dependency:sources` → the backend is a Maven/JBoss Java app living elsewhere.
- Sibling repos confirm the split: `/workspaces/legacy-source/cichlids.backend`, `cichlids-serverless`, `orleans`. The frontend talks to it purely over HTTP (see §4).

### Build & deploy
- **Build:** root `Makefile` recurses `client → client/web`; `client/web/Makefile` runs the Angular build. Output `client/web/dist/`. `deploy` target: `rsync` of `client/web/dist` to `$(DEPLOY_TARGET)`.
- **CI:** `bitbucket-pipelines.yml` — on `master`: step 1 build (`node:9.8.0-wheezy`, `make`), step 2 auto-deploy to **test** (`rsync` to `$TEST_DEPLOY_URL`), step 3 **manual** deploy to **production** (`$PROD_DEPLOY_URL`). So deployment = static/SSR bundle pushed via rsync; URLs come from pipeline env vars.
- **Docker (legacy):** `client/docker-web/Dockerfile` (`node:5.9`, runs `dist/server/bundle.js` on port 3001) — an older SSR container, superseded by the rsync flow.

---

## 2. Frontend features (routes / pages)

Routing is split across lazy/feature modules under `src/app/main/content/`. Routes collected from `*.module.ts` `path:` declarations:

| Route | Component / module | Feature |
|---|---|---|
| `''` (home) | `pages/frontpage` | Front page: latest pictures + tanks + recent comments (`frontpage.service.ts` resolves all three) |
| `pictures`, `browse.html` | `pages/pictures/list` | Picture gallery with **filters**: `type` (Cichlids/Tanks/Offtopic/Contest), `category` (all/american/african/malawi/tanganyika), `sort` mode; infinite scroll |
| `pictures/pic/:slug` | `pages/pictures/details` | Picture detail page (image viewer, rating, views, comments) |
| `pictures/edit/:slug` | `pages/pictures/edit` | Edit own picture (title/description/type) |
| `pictures/upload` + `/tanks` `/profile` `/avatar` | `pages/pictures/upload` | Upload flow (FineUploader) with destination select: picture, tank picture, profile image, avatar |
| `tanks` | `pages/tanks/list` | Tank (aquarium) gallery |
| `tanks/details/:id` | `pages/tanks/details` | Tank detail page + comments |
| `tanks/edit/:id` | `pages/tanks/edit` | Edit a tank |
| `members/:userid/:username` (+ `/photos`, `/tanks`) | `pages/profile` | User profile w/ tabs: About, Photos/Videos, Tanks, Timeline |
| `user` | `pages/profile` | Own-profile redirect/shortcut |
| `settings`, `settings/notifications` | `pages/settings` | Notification settings (e.g. mute comment notifications on own pictures/tanks) |
| `library` + `library/...html` | `pages/library` | Static care articles: Lake Malawi / Tanganyika habitats, Beginner guides (nutrition, choosing, filtration, cycling), feature articles (mbuna-kings, tanganyikan-tank, setting-up-a-lake-malawi-aquarium) |
| `terms`, `privacy`, `imprint` | `pages/about` | Legal pages |
| `pages/errors/error-disconnected`, 404, 500 | `pages/errors` | Error/maintenance pages |

**Cross-cutting components/services** (`src/app/core/` and `apps/components/`):
- **Comments**: list, post, per-entity (`comment.service.ts`, `comment.component`, `post-comment`) on pictures and tanks.
- **Image viewer** (`apps/components/imageviewer`) — lightbox, keyboard nav.
- **Moderation** (`apps/components/moderation`): delete dialog with reason, edit-pic-type dialog — admin/mod role gated (Auth0 `roles` claim).
- **Convert-to-tank** button (turn an uploaded picture into a tank entry).
- **Responsive image** pipeline: `dyn-img` + `picture-img` build `srcset`/`sizes` from a server-provided size→URL map (`ImageMap`, `image-calc.ts`).
- **Auth UI**: toolbar login/profile, `nologin-greeting`, Auth0 Lock.
- **Search bar** (`core/components/search-bar`).
- YouTube parser (`youtube-parser.ts`) → video embeds in descriptions.

**Present-but-disabled / scaffolding (Fuse template leftovers, not wired into nav):** `apps/mail` (full mailbox UI), `apps/chat`, `apps/dashboards/project`, `pages/pricing`, and `fuse-fake-db/*` mock data. The real `navigation.model.ts` exposes only Home / Pictures / Tanks; mail & chat are commented out. `TODO` lists unbuilt features: private messages (PN), forum, species listing/profiles, photo contest, picture categories/tagging.

The product vision is a **cichlid-fish photo-sharing community**: members upload fish/aquarium photos, organize them by tank, rate & comment, browse by region/type, read care articles, with moderation and (planned) private messaging + forum.

---

## 3. Transactional email templates (`assets/*.vm`)

Apache Velocity templates. HTML variants `#parse` the shared header/footer; `*Plain.vm` are text alternatives. Each maps to a user-facing event:

| Template | Event | Key vars |
|---|---|---|
| `ConfirmAccountMail.vm` / `ConfirmAccountMailPlain.vm` | **Account email confirmation** (after signup) | `$url`, `$account`, `$code`, `$reseturl`, `$toemail` |
| `RecoverPasswordMail.vm` / `RecoverPasswordMailPlain.vm` | **Password reset** request | `$url`, `$account`, `$code`, `$toemail` |
| `NewPrivateMessageMail.vm` / `NewPrivateMessageMailPlain.vm` | **New private message** received | `$from`, `$subject`, `$url`, `$message` |
| `ReportedMailPlain.vm` | **Content (picture/tank) reported** → to moderators | `$type`, `$entityId`, `$reporterName/Username`, `$posterName/Username`, `$title`, `$url` |
| `CommentReportedMailPlain.vm` | **Comment reported** → to moderators | `$commentId`, `$reporter*`, `$commentPoster*`, `$commentBody`, `$slug`, `$entityId`, `$url` |
| `DeletedMailPlain.vm` | **Content deleted by a moderator** → notify poster | `$type`, `$entityId`, `$modName/Username`, `$poster*`, `$title`, `$description`, `$reason` |
| `HtmlMailHeader.vm` | Shared HTML email header (cichlids.com branding, dark banner) | — |
| `HtmlMailFooter.vm` | Shared HTML footer (postal address) | — |

Confirms these events existed in production: signup confirmation, password recovery, private messaging, content/comment reporting → moderation, and moderator deletion notices. Sender postal identity in footer: *cichlids.com, Muehlenstr. 117, D-40668 Meerbusch, Germany*. (Note: `NewPrivateMessageMail` exists though the PN feature is listed as TODO in the frontend — backend likely had it first.)

---

## 4. Backend / image-server integration

API base URLs are config-driven (`src/environments/environment*.ts`):
- `SITE_BASE_URL = https://www.cichlids.com`
- `API_BASE_URL  = https://www.cichlids.com/api`  — main JVM backend
- `AWS_BASE_URL  = https://<id>.execute-api.us-east-1.amazonaws.com/{dev|prod}` — an **AWS API Gateway** (serverless) endpoint, used for settings: `settings-config.service.ts` → `AWS_BASE_URL + '/api/members'`. (IDs differ dev vs prod — redacted below.)

REST resource roots (built in `*-config.service.ts`, all off `API_BASE_URL`):
- `/api/pictures`, `/api/tanks`, `/api/comments`, `/api/upload` (+ `/upload/files`, `/upload/?done` chunking, `/upload/files/{id}/convertTo{Picture,TankPicture,ProfileImage,AvatarImage}`), `/api/userid`, `/api/authuser`.
- HATEOAS-style: picture/tank detail responses carry `_links.comments`, re-prefixed with `API_BASE_URL` (`picture-details.component.ts`, `tank-details.component.ts`).

**Image server / `/p/...`:** the app does **not** hardcode `/p/n` or `/p/u` path builders. Instead the backend returns a **size→absolute-URL map** (`ImageMap` = `{ [width]: url }`) per picture/avatar; `image-calc.ts` (`availSizes`, `bestSizeFor*`, `imageUrlForPicture`) picks the best size and `dyn-img` emits a responsive `srcset`. So image-server URL structure (incl. any `/p/n`, `/p/u` prefixes) is decided server-side and consumed opaquely. Avatar fallback: `assets/img/default_avatar.png`; auth logo `https://www.cichlids.com/assets/img/calvus.png`.

**Auth (Auth0):** `core/services/auth/auth0-auth.service.ts` — Auth0 Lock + WebAuth.
- Domain `cichlids.eu.auth0.com`, client ID hardcoded (redacted below), logout via `https://cichlids.eu.auth0.com/v2/logout`, callback `${origin}/auth0/callback`.
- Scopes `openid profile email nickname http://www.cichlids.com/roles`; **custom roles claim** `http://www.cichlids.com/roles` drives moderator/admin gating. JWT stored in localStorage (`JWTToken`), silent refresh via `checkSession` every 10 min. On login the app calls backend `/api/authuser` (`receiveProfileForAuthUser`) to resolve username/uid/avatar.

---

## 5. Git history & completeness

- **670 commits.** First commit **2012-10-09**, last commit **2019-02-21** (`master`; branches `master`, `origin/master`, `auth0-tests`).
- Trajectory: long life (2012→2019) spanning a full reframe — earliest era used Bootstrap/LESS (commented-out rules still in root `Makefile`), an Angular 2 webpack starter (`docker-web`), then Angular CLI, then the Fuse Material rebuild, ending on **Angular 7 + SSR + Auth0 + Sentry**.
- Recent commits are incremental polish: notification-settings page, SSR build fixes, Bitbucket pipeline fixes, "Remove unused stuff".
- **Assessment: working but unfinished, then abandoned.** Core photo-community features (galleries, pictures, tanks, comments, upload, profiles, moderation, library, settings, Auth0) are implemented and were CI-deployed. Major planned features never shipped (per `client/web/TODO`): private messaging UI, forum, species listing/profiles, photo contest, picture category tagging. The `apps/mail` & `apps/chat` Fuse modules remain mock-data stubs. No commits after Feb 2019 → development ceased there.

---

## 6. Relationship to purchased / OSS templates

- **Fuse (ThemeForest 12931855 — Angular Material admin template):** This is the **design & code basis of the frontend.** The whole `src/app` structure is Fuse's: `core/scss/fuse.scss`, `fuseUtils.ts`, `matColors.ts`, `core/directives/fuse-if-on-dom`, `main/{toolbar,navbar,content}`, `apps/{mail,chat,dashboards}`, `pages/{pricing,errors,maintenance}`, and `fuse-fake-db/*` are all verbatim Fuse scaffolding. The sibling `themeforest-12931855-fuse-angularjs-material-design-admin-template/` ships `Angular5/` (Fuse2 demo/skeleton zips), `AngularJS/`, `Bootstrap 4 HTML`, `PSDs`. cichlids.com started from the Fuse Angular skeleton and replaced the demo apps with picture/tank/comment/profile features while leaving mail/chat/pricing as unused leftovers.
- **`transactional-email-templates` (Mailgun OSS — `action.html`, `alert.html`, `billing.html`, `styles.css`, MIT):** **Not the basis** of `assets/*.vm`. The cichlids Velocity templates are older (2012–2013 origin), hand-rolled inline-styled HTML with a custom dark header/footer, and predate/diverge from the Mailgun set. The Mailgun repo appears to be a reference/candidate that was **not adopted** for the live `.vm` templates.

---

## Redactions (secrets present in source — values withheld)

- **Auth0 client ID** — `client/web/src/app/core/services/auth/auth0-auth.service.ts` (`AUTH0_CLIENT_ID`), public SPA client ID but redacted here. Domain `cichlids.eu.auth0.com` is non-secret.
- **Sentry DSN** — `client/web/src/app/app.module.ts` (`Raven.config('https://<redacted>@sentry.io/297845')`).
- **AWS API Gateway IDs** — `client/web/src/environments/environment*.ts` (`AWS_BASE_URL`, distinct dev/prod execute-api IDs).
- Deploy targets `$TEST_DEPLOY_URL` / `$PROD_DEPLOY_URL` are injected via Bitbucket env vars (not in repo).

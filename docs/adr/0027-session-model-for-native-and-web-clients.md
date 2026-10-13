# ADR-0027: Session Model for Native and Web Clients

- **Status:** Proposed
- **Date:** 2026-10-13
- **Deciders:** Alexander Langer

## Context and Problem Statement

Members sign in through Keycloak ([ADR-0002](0002-technology-stack.md),
[ADR-0023](0023-identity-model-user-logins-and-public-handle.md)) from the native app and from a
browser. The platform holds no critical data such as payment details, and every sign-in it forces
on a member is a moment where an occasional contributor gives up. Participation matters more here
than a short session lifetime.

Phones lose their connection often, and an access token can expire while the phone is in a tunnel
or in flight mode. A client that treats every failed refresh as the end of the session signs
members out for a dead spot, and the input they were typing is lost with the session.

A browser is a different environment. Any script that runs on the page can read browser storage,
so a token kept there is exposed to every cross-site scripting flaw for as long as it lives, and a
session that lasts until sign-out makes that exposure permanent. A cookie that the browser sends
on its own invites cross-site request forgery. Keycloak keeps a sign-in of its own in the browser,
which can outlive the sign-out of the app on a shared device.

Tokens held on a server are durable credentials at rest. A database dump, a backup or a read
through a flaw in the application would hand out sessions that never expire, unless the stored
tokens are useless without a key kept elsewhere.

Accounts can be suspended or deleted. That has to end their sessions as an act of moderation.

The question is how long a session lives, where each client keeps it, which failures end it, and
how the web app and the API share a session without exposing tokens to scripts.

## Decision

**One sign-in per device, valid until the member signs out.**

- A session ends when the member signs out on that device, or when Keycloak refuses to renew
  it. Inactivity, elapsed time and errors never end it.
- Sessions are Keycloak offline sessions. Both clients request the `offline_access` scope. The
  realm gives offline sessions no maximum lifespan and an idle timeout of ten years, far beyond
  any realistic pause. Every account holds the `offline_access` role through the realm's default
  role, imported accounts included ([ADR-0022](0022-legacy-account-import-into-keycloak.md)).
- The access token stays short-lived; the refresh token renews it.
- Signing out revokes the device's offline session at Keycloak. Every login asks for credentials,
  because Keycloak's own sign-in in the browser can outlive the sign-out of the app, and the next
  person on a shared device must not inherit it. On the web, signing out also ends that sign-in.

**Refresh outcomes.**

- The token endpoint answering HTTP 400 or 401 (for example `invalid_grant`) means Keycloak
  refused the session. The client deletes its tokens and the member is signed out.
- Any other failure, such as no network, a timeout, a 5xx answer or an unreadable answer, keeps
  the tokens. The call that needed the token fails with a message about the connection, and the
  next call tries again.

**Suspension and deletion.** Keycloak refuses to renew the session of a disabled or deleted
account. Opening the app renews the session once, so a suspended member who opens the app while
online is signed out right away. Without that, access ends with the current access token.

**Native app.** The app signs in through a public Keycloak client with the authorization code flow
and PKCE in the system browser and keeps its tokens in the operating system's secure storage.

**Web app: backend for frontend.**

- The API runs the authorization code flow with PKCE against Keycloak through a confidential
  client of its own and authenticates every request to Keycloak (code exchange, renewal,
  revocation) with the client's secret. The secret lives in the secret store of the environment
  and never reaches the browser.
- The API stores the tokens on the server and renews them there under the same refresh rules. A
  refused renewal deletes the stored session. Keycloak refusing the client credentials of the API
  counts as a failed attempt: it reveals a configuration fault, and the sessions stay.
- The stored tokens are encrypted with ASP.NET Core Data Protection under a purpose of their own.
  The key ring lives outside the database: deployed environments mount it read-only from a secret
  managed with SOPS ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)) and the API refuses to
  start without it; local development keeps a local key ring. A stored session that the key ring
  cannot decrypt counts as ended and is deleted.
- A stored session that has not been used for the lifetime of its cookie can no longer be
  reached by any browser. The server revokes its offline session at Keycloak and deletes it; a
  revocation that gets no answer is retried, so no offline session outlives its stored record.
- The browser holds only a random, opaque session id in a cookie that is httpOnly, Secure,
  SameSite=Lax, host-only with the `__Host-` prefix and persistent; opening the app renews its
  expiry. The server stores a hash of the session id, so a read of the session table does not
  reveal usable cookie values. No token is ever visible to script code in the browser.
- State-changing requests authenticated by the cookie carry a per-session CSRF token in a request
  header, and their Origin, when sent, must be one of the configured web origins. The session
  endpoint hands the CSRF token to the app. SameSite=Lax is the second layer.
- The web app and the API share one origin in every deployed environment: the web host forwards
  `/api` to the API. The cookie stays first-party and no CORS configuration is needed. Local
  development serves the app and the API from different localhost ports over plain HTTP; there a
  development-only CORS policy with credentials applies, and the cookie leaves out the Secure
  flag and the prefix.

**Web sign-out.** The API deletes the stored session, revokes its offline session at Keycloak and
clears the cookie, then hands the web app the address of Keycloak's end session endpoint with the
id token of the session as hint and the web app as return address (RP-initiated logout). The
browser passes through Keycloak, which ends its sign-in there and returns to the app. The sign-out
request carries the CSRF token like every other cookie-authenticated write.

**One API, two credentials.** The API accepts a bearer token from the native app and the session
cookie from the web app. A request with an Authorization header is authenticated by its bearer
token alone; the CSRF rules apply to cookie-authenticated requests.

**Local input outlives the session.** Unsent input that takes effort to recreate, such as an
upload with its title and description, is kept on the device across a sign-out and restored
after the next sign-in of the same account.

## Considered Options

- **Offline sessions, tokens in secure storage on native and a backend for frontend on the web
  (chosen).**
  - *Pro:* Members sign in once per device. Connection failures cost one call, never the session.
    Browser scripts never see a token.
  - *Con:* The API keeps per-session state and talks to Keycloak on behalf of web members.
    Deployment needs one origin for web app and API.
- **Keycloak's default session lifetimes with a new sign-in after expiry.**
  - *Pro:* A stolen device or token loses its value quickly.
  - *Con:* Members sign in again every few days or weeks, which works against participation for
    a platform without critical data.
- **Long regular SSO sessions.**
  - *Pro:* No role is needed on the account.
  - *Con:* The device session depends on the browser SSO session and its limits; offline sessions
    are Keycloak's mechanism for long-lived device sessions and survive the end of the SSO
    session.
- **Tokens in browser storage for the web app.**
  - *Pro:* No server-side session state; the web app works like the native app.
  - *Con:* Every script on the page can read the tokens, and a durable session turns a single
    cross-site scripting flaw into durable account access.
- **Web app and API on separate origins with CORS.**
  - *Pro:* Independent hosting of web app and API.
  - *Con:* Every environment needs a CORS policy with credentials, and the cookie depends on
    same-site rules of the browser.
- **The backend for frontend on the public client of the native app.**
  - *Pro:* One client and no secret to manage.
  - *Con:* Anyone who obtains a code or a refresh token of a web session can redeem it without
    proof that the request comes from the API.
- **Stored web tokens in plain text, protected by database access alone.**
  - *Pro:* No key ring to provide and rotate.
  - *Con:* Every copy of the database, backups included, holds usable durable sessions.
- **Fresh credentials at every login without ending the Keycloak sign-in.**
  - *Pro:* No round trip through Keycloak at sign-out.
  - *Con:* The Keycloak sign-in stays in the browser after the sign-out and serves any other client
    that does not force a new login.

## Rationale

Members value staying signed in more than the protection a short session gives to data that is
public or low-risk anyway. Offline sessions give that without custom token handling in Keycloak.
Separating refused renewals from failed attempts is what keeps a dead spot from becoming a
sign-out. On the web the durable session raises the stakes of token theft through scripts, so
the tokens stay on the server and the browser keeps only a cookie that scripts cannot read; the
CSRF token closes the gap that a cookie opens. On the server the tokens are encrypted with a key
that the database does not hold, so a copy of the database grants no session, and the confidential
client binds every code and refresh token to the API. Ending the Keycloak sign-in at sign-out
leaves a shared browser without any sign-in. Serving the web app and the API from one origin keeps
the cookie first-party and the configuration small.

## Consequences

- **Positive:** Members stay signed in on each device until they sign out. Flight mode and server
  outages show a connection message and keep the session. Suspension and deletion end sessions
  through Keycloak. Browser scripts cannot read web tokens, and the database holds them encrypted.
  A web sign-out leaves no Keycloak sign-in in the browser.
- **Negative / Trade-offs:** A lost or stolen device stays signed in until the member signs out
  there or the account is suspended. Keycloak keeps offline sessions of abandoned devices for up
  to ten years. The API stores web tokens and becomes a client of Keycloak's token endpoint. Each
  app start costs one renewal at Keycloak. Every deployed environment needs a key ring and a client
  secret in its secret store, and the client secret in Keycloak must match the one of the API. A
  lost key ring ends every web session; a key ring needs a new key before its key expires.
- **To be decided later:** A view where members see and end the sessions of their devices.

## References

- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0010: GitOps and Infrastructure as Code](0010-gitops-and-infrastructure-as-code.md)
- [ADR-0022: Legacy Account Import into Keycloak](0022-legacy-account-import-into-keycloak.md)
- [ADR-0023: Identity Model: User, Logins and Public Handle](0023-identity-model-user-logins-and-public-handle.md)
- [ADR-0025: Keycloak Realm Configuration per Environment](0025-keycloak-realm-configuration-per-environment.md)

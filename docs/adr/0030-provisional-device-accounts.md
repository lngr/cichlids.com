# ADR-0030: Provisional Device Accounts

- **Status:** Accepted
- **Date:** 2026-10-16
- **Deciders:** Alexander Langer

## Context and Problem Statement

Many people install an aquarium app to try it on their own tank before they decide to sign up.
Asking for an e-mail address and a password at the first start loses most of them. The app should
work from the first tap: a member creates a tank, logs a water change and sees reminders, and
registers later to publish a profile, comment or follow others. Everything created before the
registration stays with the member.

Members sign in through Keycloak ([ADR-0023](0023-identity-model-user-logins-and-public-handle.md))
and keep one session per device ([ADR-0027](0027-session-model-for-native-and-web-clients.md)). A
member without an account has no Keycloak user, so the API needs another way to know that two
requests come from the same installation, without a shared secret that a copy of the app or a
network trace reveals.

Anonymous accounts invite abuse: scripts that create accounts in bulk, uploads without a person
behind them, and storage that grows with installations nobody uses. A provisional account must
never let content reach other people before a person registers.

The question is how an installation authenticates without a login, what a provisional account may
do, and how it becomes a full account.

## Decision

**One key pair per installation.**

- At its first start the native app creates an ECDSA P-256 key pair in the secure store of the
  operating system (Secure Enclave or Keychain on iOS, hardware-backed Keystore on Android where
  available). The private key is not exportable and never leaves the device.
- The app registers the public key with the API. The API creates a provisional profile and stores
  the public key as a device credential of that profile. The provisional profile has a generated
  name and no public handle.
- The web app has no provisional accounts; it is a public window into the platform and invites to
  the app or to registration.

**Signed challenge, short-lived token.**

- To get a token, the app asks the API for a fresh challenge, signs challenge, device credential id
  and timestamp with the private key and sends the signature. The API checks it against the stored
  public key, accepts each challenge once and within a few minutes, and issues an access token for
  the provisional profile.
- Device tokens are issued by the API under an issuer and audience of their own and live fifteen
  minutes. There is no refresh token: the app signs a new challenge when the token expires. Losing
  the network costs one call and never the account, as for Keycloak sessions.
- The API accepts Keycloak tokens and device tokens side by side. A device token carries the
  provisional profile as its subject and a claim that marks it as provisional, so every endpoint
  sees through one check whether the caller is registered.

**What a provisional account may do.**

- It may read everything a visitor without an account reads.
- It may create its own tanks, log care, take photos and videos and use reminders, streaks and the
  media library. All of it is private: the API refuses any visibility other than private for
  content of a provisional profile, and nothing of it appears in public lists, search or feeds.
- Everything that reaches other people needs registration: public or shared tanks, a public
  profile, comments, ratings, kudos, follows, messages and gallery stories.
- Creating provisional accounts and their uploads are rate limited per network address and per
  credential, with lower upload quotas than for registered members.

**Registration keeps everything.**

- The member registers or signs in through Keycloak from the app while the device token is still
  valid. The API links the Keycloak subject to the provisional profile, which becomes a full
  profile with its first public handle. The device credential is retired, and the device session
  continues as a Keycloak session.
- When the Keycloak account already has a profile, the app asks the member whether to take the
  provisional content along. On yes, the API moves the tanks, care data and media of the provisional
  profile into that profile in one transaction and deletes the provisional profile.
  The move needs a signed challenge of the device and the Keycloak token of the account in the same
  request, so neither alone can claim the data of the other.

**Loss and cleanup.**

- A provisional account lives as long as its key. Uninstalling the app or wiping the device loses
  it, and the app says so before the member creates the first tank, with the offer to register.
- A provisional profile without a signed request for twelve months is deleted with its content.

## Considered Options

- **Device key pair with signed challenges (chosen).**
  - *Pro:* No secret travels or sits on the server; a database copy or a network trace does not let
    anyone act as the device. The key stays in hardware where the device offers it.
  - *Con:* The API issues and checks tokens of its own besides Keycloak. A lost device loses an
    unregistered account.
- **Random anonymous token kept by the app.**
  - *Pro:* Simple to build.
  - *Con:* The token is a bearer secret: every copy of it, from a backup of the phone to a log line
    on the server, is the account.
- **Keycloak user per installation (anonymous login in Keycloak).**
  - *Pro:* One token issuer for everything.
  - *Con:* Keycloak fills with users nobody can sign in as, its login flows need custom extensions
    for credential-less users, and the device still needs a secret to prove itself.
- **No provisional accounts; registration at the first start.**
  - *Pro:* One kind of account and no extra security surface.
  - *Con:* Loses the people who want to try the app on their tank first, which is the main way the
    platform grows.

## Rationale

The point of a provisional account is that it needs nothing from the person, so its credential has
to come from the device. A key pair in the secure store is the only option that leaves no usable
secret anywhere outside the device. Short-lived tokens without refresh keep the API side simple: a
device either can sign or it cannot. Keeping every provisional content private, enforced by the
API, removes the abuse path that matters most, spam and unwanted content in front of others, and
leaves the remaining risk to rate limits and storage cleanup.

## Consequences

- **Positive:** The app works from the first start without a form. Registration later keeps every
  tank and care entry. A stolen database or token log does not hand out device accounts.
- **Negative / Trade-offs:** The API runs a second token issuer with its own signing key, which
  needs rotation and the same protection as the session key ring. Every endpoint that publishes must
  check the provisional claim. Unregistered accounts die with their device. Storage holds private
  content of people who never register, until the cleanup removes it.
- **To be decided later:** Restoring a provisional account on a new device (for example through an
  encrypted platform backup of the key), teaser rules of the web funnel, and the exact rate limits.

## References

- [ADR-0023: Identity Model: User, Logins and Public Handle](0023-identity-model-user-logins-and-public-handle.md)
- [ADR-0027: Session Model for Native and Web Clients](0027-session-model-for-native-and-web-clients.md)
- [ADR-0029: Media Links and Stories](0029-media-links-and-stories.md)

# ADR-0022: Legacy Account Import into Keycloak

- **Status:** Accepted
- **Date:** 2026-09-28
- **Deciders:** Alexander Langer

## Context and Problem Statement

Members of the predecessor platform signed in through a hosted identity service with
email and password or with Google or Facebook. The platform identity provider is
Keycloak ([ADR-0002](0002-technology-stack.md)). The export of the old identity service
lists every account with its email, verification flag and sign-up connection, and it
holds no password hashes. Several accounts share one email address across connections
or in different letter case, a few social accounts have no email at all, and the legacy
member table supplies emails for some of those. Migrated member profiles exist for part
of the accounts ([ADR-0020](0020-relational-domain-schema.md)).

Returning members must keep their identity and their profile, and the relaunch
asks every member to accept the new terms before first use. The question is how legacy
accounts enter Keycloak.

## Decision

Every legacy account with a known email, plus every email-less account that has a
migrated profile, becomes a Keycloak user through a repeatable import.

- **One account per email.** The lowercased, trimmed email is the merge key. Export
  records sharing it become one user; its email counts as verified when any of the
  records was verified. The legacy member table fills in the email of a record that
  has none. An email-less record with a migrated profile becomes a user keyed by its
  legacy subject; an email-less record without a profile is left out.
- **Required actions.** Every imported user has to accept the terms on first login.
  Users that had a password connection also have to set a new password, since the
  export carries no hashes.
- **Deterministic ids.** A user's Keycloak id is a name-based UUID over its merge key,
  so repeated runs produce the same id for the same account.
- **Existing users are never altered.** A Keycloak user the import did not create is
  left as it is. It receives the profile link only when its email equals the account's
  email and is verified. A user with that email but an unverified address, or a user
  holding the account's username, blocks the account: it is neither created nor
  linked, and the import reports it as a conflict.
- **Social identities linked ahead of provider configuration.** Google and Facebook
  identities are stored as federated identity links. A record contributes its link
  when its email is verified or when it has no email of its own, since then its legacy
  subject ties it to the account. A social record with an unverified email of its own
  contributes no link, because its owner never confirmed that address. The realm has
  both identity providers, disabled until their client credentials exist.
- **Profile link through the oidc identity.** An account with a migrated profile gets
  an oidc identity on that profile whose subject is the Keycloak user id, so the first
  authenticated call resolves the profile by subject. An oidc identity that already
  binds the subject to another profile stays as it is.

## Considered Options

- **Import all accounts with a known email (chosen).** Every returning member can
  reset a password and sign in with the account they know, including members without
  migrated content. Pros: complete, one campaign reaches everyone. Cons: the realm
  holds many accounts that may stay unused.
- **Import only accounts with a migrated profile.** Pros: smaller realm. Cons: members
  without content lose their account, and legacy addresses stay open for registration
  by anyone.
- **Link on first login only.** Keycloak starts empty and each member registers anew.
  Pros: no import code. Cons: social identities and verified emails are lost, and
  returning members face a registration instead of a password reset.

## Rationale

Importing every known email keeps the email unique in the realm, so a later
registration cannot claim a legacy address. An account registered before the import is
linked only through a verified email, and the API links a login to a migrated profile
by email only when the token reports that email as verified. A social login reaches an
imported account only when its email was verified or its legacy subject belongs to the
account. An imported password account opens only after a password reset, and the reset
mail goes to the account's address, so signing in proves control of that mailbox. An
address someone merely typed in therefore never leads to a migrated profile. Required actions are the
only way to reconcile missing password hashes and the consent requirement with an
account that already exists. Deterministic ids and a hands-off rule for existing users
make the import safe to repeat on a live realm.

## Consequences

- **Positive:** Returning members keep their identity and profile; the import can run
  with every fresh export without duplicates.
- **Negative / Trade-offs:** A password member can only sign in after a password reset,
  which requires working outbound email. Social sign-in works only once the provider
  credentials are configured.
- **To be decided later:** How the reset invitation reaches members, and when the
  imported accounts that never sign in are removed.

## References

- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0020: Relational Domain Schema](0020-relational-domain-schema.md)

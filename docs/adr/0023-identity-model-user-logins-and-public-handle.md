# ADR-0023: Identity Model: User, Logins and Public Handle

- **Status:** Accepted
- **Date:** 2026-09-28
- **Deciders:** Alexander Langer

## Context and Problem Statement

A member signs in with a password, with Google or Facebook, and later with a passkey. The
identity provider is Keycloak ([ADR-0002](0002-technology-stack.md)); member profiles and
everything they publish live in the domain schema ([ADR-0020](0020-relational-domain-schema.md)).
On the predecessor platform one name served as login and as public name, and many members
chose their email address for it. Some wrote an address into their display name as well.
Profiles are public, so any name shown on them is visible to everyone and to search engines.

The question is which identity objects the platform has, which of them are public, how a
member's sign-in methods relate to the profile, and where names come from when a member has
no usable one.

## Decision

- **One user account per member.** The account is a Keycloak user. Its id is the subject of
  every token the member receives, and it is linked to exactly one profile through an oidc
  identity. The account is not public.
- **Several logins per account.** Password with the login username or with the email,
  linked Google and Facebook identities and passkeys are ways to sign in to the same account;
  a new method is merged into the existing account. Logins are not public. The API sees the
  account through the token subject and never depends on the login method.
- **Profile only through the account link.** An account reaches its profile only through the
  oidc identity binding its subject. For a migrated member the legacy account import creates
  that link ([ADR-0022](0022-legacy-account-import-into-keycloak.md)); the API never links a
  profile by email or by any other attribute of a login. In an environment with legacy
  members the import runs before self-registration opens, so a legacy address cannot be
  registered by someone else first.
- **A public handle separate from the login username.** The handle (the profile's username)
  names the member in public and can be changed without touching how the member signs in.
  The login username belongs to the account and stays private.
- **No email address in a public field or a login name.** An email address is never a
  handle, never a display name and never a login username. A candidate that contains an
  address is skipped.
- **No email address as a guest name.** A comment or forum post from a guest, someone with no
  account, shows a poster name typed at the time. An email address typed there is replaced by a
  generated guest handle of the form guest_ followed by a number; the same address always maps
  to the same handle, so a guest is recognisable across their own comments and forum posts
  without their address ever appearing.
- **No avatar URL derived from an email address.** A profile picture is one a member uploads to
  the platform, or an image the platform links to by its own reference; it is never a URL whose
  path or query is a hash of an email address, such as a Gravatar URL. A profile without a
  usable avatar shows the app's default avatar instead.
- **Generated names.** A member without a usable name gets a generated name of the form
  `user` followed by eight digits. It is derived from a keyed hash of a stable key with a
  platform secret, so repeated runs assign the same name and the name reveals nothing about
  its key. Handles and login usernames are derived from separate inputs, so a member's
  generated handle and generated login username are unrelated.

## Considered Options

- **Account, logins and handle as separate concepts (chosen).** Pros: a member can change
  the public name without affecting sign-in, sign-in details stay private, and several
  sign-in methods lead to one profile. Cons: two names per member, which the app has to
  present clearly.
- **One name for login and public profile.** Pros: a single name to remember. Cons: every
  login name becomes public, a rename breaks sign-in, and members who chose their address
  expose it.
- **Email address as login username.** Pros: members already know it. Cons: the username
  appears in places the member does not control, such as tokens and administrative views,
  and a later change of address would change the login name.
- **Profile lookup by email on first login.** Pros: works without an import step. Cons: the
  outcome depends on an attribute of the login method, and control of an address at the
  moment of registration says nothing about who owned it on the predecessor platform.

## Rationale

The account is the only stable anchor that every sign-in method shares, so the profile hangs
off the account and nothing else. Keeping the handle apart from the login username means a
public rename changes only the profile. Email addresses are personal data that members
disclosed only to the platform, which rules them out for every name another person might see. A generated name keyed with a secret is stable
across repeated migrations and still unguessable, and the `user` prefix with a number reads
as a placeholder a member will want to replace.

## Consequences

- **Positive:** Members sign in with whatever method they prefer and always reach the same
  profile; public pages never show an email address; migrations can be repeated without
  renaming anyone.
- **Negative / Trade-offs:** Members without a usable legacy name start with a generated
  handle and login username. A member has to know the login username or the email to sign
  in with a password.
- **To be decided later:** The rules for changing a handle (allowed characters, length,
  uniqueness, rate limit) and how the app presents the login username.

## References

- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0020: Relational Domain Schema](0020-relational-domain-schema.md)
- [ADR-0022: Legacy Account Import into Keycloak](0022-legacy-account-import-into-keycloak.md)

# ADR-0025: Keycloak Realm Configuration per Environment

- **Status:** Accepted
- **Date:** 2026-10-12
- **Deciders:** Alexander Langer

## Context and Problem Statement

Keycloak holds the accounts of every member ([ADR-0002](0002-technology-stack.md),
[ADR-0023](0023-identity-model-user-logins-and-public-handle.md)). A realm file that makes local
development convenient (no TLS, a password grant, seeded users with known passwords, wildcard
redirects, open registration) is dangerous in a deployed environment, where each of those
settings weakens account security. Environments differ legitimately in hostnames and redirect
targets, so one file cannot serve all of them unchanged.

Self-registration has a second constraint. The legacy accounts are imported into Keycloak
([ADR-0022](0022-legacy-account-import-into-keycloak.md)), and an email or username that a new
visitor registers first cannot be given to the legacy member who owns it. Registration
therefore opens after the import.

Environment configuration lives in Git and is applied by the cluster
([ADR-0010](0010-gitops-and-infrastructure-as-code.md)). Whatever Git applies repeatedly must
not undo a state that the operator reached on purpose.

## Decision

- **One realm file per deployed environment.** Each file sits under
  `apps/keycloak/overlays/<environment>/` and is plain Keycloak realm JSON, so reviewers read
  exactly what Keycloak imports. The local development stack keeps its own realm file outside
  that layout and marks itself as local development only.
- **Deployed environments differ only in what is environment specific:** the display name and
  the redirect URIs and web origins of the public client. A CI check compares the files and
  fails on any other difference.
- **Secure by construction.** Deployed realms require TLS for all addresses, forbid the
  password grant and the implicit flow, require PKCE on public clients, list exact redirect
  URIs without wildcards, require verified emails, enable brute force protection and contain
  no users, credentials or federated identities. Accounts of operators and testers are created
  outside Git.
- **Registration state is runtime state.** The realm files do not set the registration flag.
  A new realm starts with registration closed, and the account import opens it after it has
  created every legacy account. Re-applying the files cannot close a registration that the
  operator opened, and no file can open it early.
- **A CI check guards the files.** It fails when a local development setting appears in a
  deployed realm file, with a self-test that proves each rule can fail.
- **Services refuse unsafe authentication settings.** Outside the Development environment the
  API does not start without an HTTPS issuer URL and an audience.

## Considered Options

- **One file per environment, guarded by CI (chosen).**
  - *Pro:* The reviewed file is the imported file. The check reads final values without a
    rendering step. Drift between environments shows up as a failing parity rule.
  - *Con:* Environment-specific values are duplicated, and a change to shared settings is made
    in every file.
- **One template rendered per environment.**
  - *Pro:* Shared settings exist once.
  - *Con:* Reviewers and the check must reason about rendered output. A faulty substitution
    can produce an insecure realm that no file in Git shows.
- **One file with the development settings overridden at deployment.**
  - *Pro:* Least duplication.
  - *Con:* The unsafe settings are the default, and a forgotten override ships them.

## Rationale

Separate files make the secure state the only state a deployed environment can be in, and the
check makes it a merge condition. Leaving the registration flag
out of the files fits the nature of the setting: the moment registration opens depends on the
progress of the account import, which Git does not know. Keeping that decision in the import
guarantees the order of both steps and survives repeated applies from Git.

## Consequences

- **Positive:** A development setting cannot reach a deployed realm unnoticed. Registration
  cannot open before the legacy accounts exist. The files are readable by Keycloak's realm
  import and by tools that apply realm files.
- **Negative / Trade-offs:** Changes to shared settings touch every environment file. A realm
  with TLS required for all addresses rejects every login when the proxy in front of Keycloak
  passes wrong forwarding headers, so the deployment has to configure the proxy headers and an
  HTTPS hostname. Outgoing mail is required for email verification and password resets.
- **To be decided later:** The mechanism that applies the realm files to a running Keycloak.

## References

- [ADR-0002](0002-technology-stack.md)
- [ADR-0010](0010-gitops-and-infrastructure-as-code.md)
- [ADR-0022](0022-legacy-account-import-into-keycloak.md)
- [ADR-0023](0023-identity-model-user-logins-and-public-handle.md)

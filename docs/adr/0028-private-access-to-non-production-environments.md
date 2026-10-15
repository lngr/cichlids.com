# ADR-0028: Private Access to Non-Production Environments

- **Status:** Accepted
- **Date:** 2026-10-15
- **Deciders:** Alexander Langer

## Context and Problem Statement

Staging and the preview environments run the application with realistic, anonymized data and
unreleased features ([ADR-0018](0018-environment-data-provisioning.md)). They must not be open
to the public internet, yet they need valid HTTPS certificates, because the apps, the identity
provider and the browser require TLS. The certificates come from Let's Encrypt through HTTP-01,
which needs public reachability of one path per host, and the cluster holds no cloud token that a
DNS-01 challenge would require ([ADR-0014](0014-safeguards-against-destructive-infrastructure-changes.md)).
Preview hosts change with every pull request. Username and password prompts in front of the
environments are not wanted: they are shared secrets, they break app flows, and they do not
identify devices.

## Decision

- **Staging and previews are reachable only from a self-hosted tailnet.** Headscale runs in the
  cluster as the coordination server with its own public host, and an embedded DERP relay with
  STUN provides connectivity without third-party relays. Devices of the operator and testers
  join this tailnet.
- **Public DNS names stay unchanged.** The node advertises a route for its own public IPv4
  address into the tailnet, and the operator approves it. Tailnet devices therefore reach every
  staging and preview host, including new preview hosts, through the tunnel without any DNS
  change.
- **The ingress decides by the real source address.** Traefik keeps the client source address
  and admits private application routes only from the tailnet address ranges; forwarded headers
  grant no access.
- **Certificates stay public and automatic.** Only the HTTP-01 challenge paths answer publicly,
  so Let's Encrypt issues certificates for every host without a cloud token in the cluster.
- **Production and operator access are independent of the tailnet.** Production ingress stays
  public, and the operator reaches the node over SSH whether or not the tailnet works, so a broken
  coordination server can always be repaired.
- **The coordination state is protected.** Headscale keeps its state on retained storage under
  the stateful safeguards and is backed up outside the cluster.

## Considered Options

- **Self-hosted Headscale tailnet (chosen).**
  - *Pro:* device-based access without shared passwords; full control over the coordination
    server; works for dynamic preview hosts; certificates keep working through HTTP-01.
  - *Con:* one more stateful component to run, back up and update.
- **Hosted Tailscale coordination.**
  - *Pro:* nothing to operate for the coordination server.
  - *Con:* the coordination server and the membership list sit with an external provider.
- **Username and password in front of the environments.**
  - *Pro:* simple to set up.
  - *Con:* shared secrets, no device identity, interferes with app and login flows.
- **DNS-01 certificates and private DNS only.**
  - *Pro:* no public path at all.
  - *Con:* requires a cloud token with full project rights inside the cluster.

## Rationale

Device membership in a tailnet is the access control that fits a small team: it needs no shared
passwords and works for every client the apps use. Running Headscale in the cluster keeps the
coordination under the project's control, and keeping SSH and production independent of it makes
an outage of the tailnet a repairable inconvenience, not a lockout. Routing the node's public
address through the tunnel keeps the public DNS names valid for certificates and makes new preview
hosts reachable without any per-host step.

## Consequences

- **Positive:** non-production environments are invisible to the public apart from certificate
  challenges; access follows devices and can be revoked per device; certificates stay automatic.
- **Negative / Trade-offs:** Headscale and its relay are additional components to keep current
  and backed up; a device without the tailnet client cannot reach staging or previews; the ingress
  must preserve source addresses, which constrains how traffic reaches Traefik.
- **To be decided later:** restricting operator SSH to the tailnet with a public fallback.

## References

- [ADR-0010: GitOps and Infrastructure as Code](0010-gitops-and-infrastructure-as-code.md)
- [ADR-0014: Safeguards Against Destructive Infrastructure Changes](0014-safeguards-against-destructive-infrastructure-changes.md)
- [ADR-0018: Environment Data Provisioning for Staging and Preview](0018-environment-data-provisioning.md)

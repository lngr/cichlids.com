---
id: TASK-2.32
title: Restrict staging and previews to a self-hosted tailnet with Headscale
status: In Review
assignee: []
created_date: '2026-10-15 06:45'
updated_date: '2026-10-15 08:09'
labels: []
dependencies:
  - TASK-2.8
  - TASK-2.31
references:
  - apps/headscale/README.md
  - apps/traefik/overlays/cluster/config.yaml
  - tools/check-cluster-manifests.sh
parent_task_id: TASK-2
priority: high
ordinal: 70000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Staging and every preview are reachable only from a tailnet that cichlids.com operates itself
(Headscale), while their HTTPS certificates stay valid. Decided by the project owner: no
username and password protection, no hosted coordination service.

### Scope
- Headscale as a workload in the cluster (stateful rules of ADR-0014, its own public host such as
  `headscale.cichlids.com` with a certificate), embedded DERP relay and STUN if feasible on the
  single node, so no third-party relay is needed; backup of its state.
- The node joins the tailnet (app tier, idempotent, pre-auth key from the operator).
- Traefik answers `staging.cichlids.com` and `pr-<n>.dev.cichlids.com` only for requests from
  the tailnet; only the ACME HTTP-01 challenge path stays public, so certificates keep working
  without a cloud token. Tailnet clients reach these hosts through the tunnel (split DNS through
  Headscale, or a route of the node address through the tailnet; choose what also works for the
  dynamic preview hosts).
- Production stays public. Operator SSH access to the node does not depend on the tailnet.

### Operator
- Create the first Headscale user and pre-auth keys, join own devices.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a device outside the tailnet, When it opens https://staging.cichlids.com, Then the request is refused, while /.well-known/acme-challenge/ stays reachable for the certificate issuer
- [ ] #2 Given a device in the tailnet, When it opens https://staging.cichlids.com and https://pr-<n>.dev.cichlids.com, Then both load over valid certificates
- [ ] #3 Given the cluster without Headscale, When the operator connects to the node over SSH, Then the access works
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- Headscale 0.29.5 (digest) in the addons project, SQLite on a Retain PVC, embedded DERP relay
  with STUN on UDP 3478 through ServiceLB, public host `headscale.cichlids.com` with a certificate,
  periodic SQLite backup plus documented external copy.
- Node: Tailscale 1.104.1 (SHA-256) installed by the app tier, joins with the optional sensitive
  variable `tailscale_auth_key` (empty: no join), advertises its public IPv4 as a /32 route that
  the operator approves; public DNS names, including dynamic preview hosts, then reach the node
  through the tunnel.
- Traefik: `externalTrafficPolicy: Local`; middleware `tailnet-only` (`ipAllowList` on the
  connection address, 100.64.0.0/10 and the tailnet IPv6 range, forwarded headers grant nothing)
  on the staging and preview Ingresses; separate public ACME solver Ingresses; production
  untouched.
- DNS record for `headscale` in the platform tier.

### Verified by the agent (Codex) and the main session
- Manifest checks red then green (301 valid); local Docker test with two Tailscale nodes: HTTPS
  200 through the /32 route, 403 for staging and previews from outside, production 200, ACME path
  200, HTTP redirect 301, staging through the embedded DERP when direct WireGuard is blocked.
- Not verified: ServiceLB source address on the real node, DNS, certificate issuance, SSH while
  the tailnet is down.

### Open
- Proposed ADR-0010/0014 text (project owner).

- ADR-0028 "Private Access to Non-Production Environments" (Accepted) records the decision;
  ADR-0010 links it.
<!-- SECTION:NOTES:END -->

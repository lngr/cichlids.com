---
id: TASK-2.2
title: OpenTofu foundation tier with day-one destructive-change safeguards
status: In Progress
assignee:
  - claude
created_date: '2026-06-03 16:00'
updated_date: '2026-06-05 10:25'
labels:
  - infra
  - opentofu
  - safeguards
milestone: m-0
dependencies:
  - TASK-2.1
references:
  - infra/foundation/guardrails_test.py
parent_task_id: TASK-2
priority: high
ordinal: 4000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Define the stateful (**foundation**) OpenTofu tier that the operator applies.

### Scope
- The Hetzner node and its disk, and the PostgreSQL block volume.
- Two object-storage buckets: **media-master** (versioning + Object Lock, Governance mode) and
  **backup** (Object Lock, Compliance mode).
- The Hetzner DNS zone; the DNS provider is structured behind a variable so the
  authoritative-zone choice stays adjustable.

### Day-one safeguards (ADR-0014 Layer 1)
- `lifecycle prevent_destroy = true` on every critical resource.
- A `critical = "true"` tag/label where the provider supports it.
- Hetzner delete-protection on the server and volume.
- Object Lock retention durations follow ADR-0013.

### Constraints
- Rarely touched, applied out-of-band by the operator; routine automation must not reference it
  (see app tier).
- No secrets in Git. Validation is static only (`tofu fmt`/`validate`, no real apply by the agent).

### Realises
- ADR-0009 (master bucket), ADR-0010 (provisioning split), ADR-0013 (retention), ADR-0014
  (Layer 1+2 state separation).

### Verifying artifact
- `infra/foundation` + a Conftest assertion over a plan fixture that `prevent_destroy` is
  present on all critical resources.
<!-- SECTION:DESCRIPTION:END -->





## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach (test-first)
1. guardrails_test.py asserts prevent_destroy + delete_protection + bucket versioning on every critical resource (offline, no credentials) — run red, then green.
2. Build infra/foundation:
   - hcloud node + dedicated DB volume + SSH key
   - two S3 buckets on Hetzner Object Storage (versioning always on; Object Lock via enable_object_lock, off in dev / on for prod)
   - Hetzner DNS zone
3. All critical resources carry prevent_destroy, delete_protection (where applicable) and critical = "true".

### Notes
- Defaults CX33 / nbg1 as overridable variables; secrets via TF_VAR_*.
- The plan-level fail-closed Conftest gate in AC #2 is delivered by TASK-2.5.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Verified locally
- guardrails_test.py: red (safeguards stripped → 7 findings) → green (5 critical resources protected).
- tofu fmt clean; tofu init resolves hcloud/hetznerdns/aws; tofu validate → Success.
- No real apply by the agent (operator runs it per B0).

### Changes
- Reference repointed from the infra/foundation/ directory to infra/foundation/guardrails_test.py (carries the task-2.2 back-tag) so the story-test-binding gate reads a file, not a directory.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 At least one Given-When-Then acceptance criterion is specified
- [x] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Given infra/foundation, When tofu fmt -check and tofu validate run, Then both pass
- [ ] #2 Given the foundation plan, When Conftest evaluates it, Then every critical resource (node, disk, DB volume, both buckets, DNS zone) carries prevent_destroy and the media-master/backup buckets carry the required Object Lock mode
- [x] #3 Given a foundation resource without prevent_destroy, When the policy check runs, Then it fails and names the resource
<!-- AC:END -->

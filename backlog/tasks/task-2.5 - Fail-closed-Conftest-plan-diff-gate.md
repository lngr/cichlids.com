---
id: TASK-2.5
title: Fail-closed Conftest plan-diff gate
status: To Do
assignee: []
created_date: '2026-06-03 16:01'
updated_date: '2026-06-05 10:36'
labels:
  - infra
  - policy
  - safeguards
  - ci
milestone: m-0
dependencies:
  - TASK-2.2
references:
  - policy/
parent_task_id: TASK-2
priority: high
ordinal: 7000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Implement the machine backstop of **ADR-0014 Layer 3**: a Conftest/OPA (Rego) policy that
evaluates `tofu plan -json` in CI and fails the pipeline (default deny) on any delete or replace
of a critical resource.

### Scope
- **Critical** = a resource in the foundation/stateful state **or** one carrying the label
  `critical = "true"`.
- No agent or CI path wires up `tofu destroy`, and there is no in-CI allow-destroy override.
- The policy ships with Conftest unit tests over committed plan fixtures.

### Realises
- ADR-0014 (Layer 3, fail-closed, default deny), ADR-0005 (required CI check).

### Verifying artifact
- `policy/` with rego + tests + fixtures; CI job `infra-policy`.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a plan fixture that deletes or replaces a critical resource, When Conftest evaluates it, Then the policy denies and the CI job fails
- [ ] #2 Given a plan fixture that only creates or updates non-critical resources, When Conftest evaluates it, Then the policy allows and the CI job passes
- [ ] #3 Given the policy was written test-first, When conftest verify runs the policy unit tests, Then a deny test exists that initially failed before the rule was implemented
- [ ] #4 Given a plan with an unclassified change, When evaluated, Then the default decision is deny
- [ ] #5 Given the foundation plan, When Conftest evaluates it, Then every critical resource (node, disk, DB volume, both buckets, DNS zone) carries prevent_destroy and the media-master/backup buckets carry the required Object Lock mode
<!-- AC:END -->



## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->

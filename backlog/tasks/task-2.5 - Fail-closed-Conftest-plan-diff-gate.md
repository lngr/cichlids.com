---
id: TASK-2.5
title: Fail-closed Conftest plan-diff gate
status: In Progress
assignee: []
created_date: '2026-06-03 16:01'
updated_date: '2026-10-13 08:15'
labels:
  - infra
  - policy
  - safeguards
  - ci
milestone: m-0
dependencies:
  - TASK-2.2
references:
  - policy/destroy_gate.rego
  - tools/destroy-protection-demo.sh
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





## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Goal
Implement ADR-0014 Layer 3: a Conftest/OPA (Rego) policy over `tofu plan -json` that fails the
pipeline (default deny) on any delete or replace of a critical resource.

### Approach (test-first)
- `policy/destroy_gate_test.rego`: Conftest unit tests written first (deny undefined -> red).
- `policy/destroy_gate.rego`: deny destructive (delete/replace) changes to critical resources
  (critical = protected type OR label/tag `critical="true"`); fail-closed default deny for an
  unclassified destroy; allow create/update/no-op.
- `policy/fixtures/`: a full-destroy plan (denied) and a safe create/update/no-op plan (allowed).
- CI job `infra-policy`: `conftest verify`, assert the safe plan passes and the destroy plan is
  denied, and run the live `prevent_destroy` proof.

### Companion (Layer 1, behavioural proof)
- `tools/destroy-protection-demo.sh`: a real `tofu plan -destroy` against the actual foundation
  and backup configs (synthetic offline state) is refused by `prevent_destroy`.

### Open
- AC #5 predates ADR-0017 ("both buckets" in one project) and asks Conftest to assert
  prevent_destroy/Object-Lock at the config level. Post-ADR-0017 that is covered by the static
  guardrails checks across both tiers; AC #5 wording may need an update — to confirm.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Delivered (with live evidence)

**Layer 3 — Conftest fail-closed plan-diff gate**
- `policy/destroy_gate.rego` + `policy/destroy_gate_test.rego` (5 unit tests) + fixtures.
- Red-green: `conftest verify` initially failed (deny undefined); after the rules: `5 tests, 5
  passed`.
- Gate on `policy/fixtures/destroy_all_plan.json` (node+volume+buckets+DNS+backups all deleted)
  -> 6 DENY, `conftest test` exit 1 (build fails). On `safe_changes_plan.json` -> exit 0.
- CI job `infra-policy` runs verify, asserts the destroy fixture is denied (else the job fails),
  and asserts the safe fixture passes.

**Layer 1 — prevent_destroy actually refuses a destroy (was never exercised before)**
- `tools/destroy-protection-demo.sh`: real `tofu plan -destroy` against the actual configs,
  offline. Output, verbatim:
  `Resource instance hcloud_volume.database has prevent_destroy set, but the plan calls for it
  to be destroyed.` — same for `aws_s3_bucket.media_master`, `aws_s3_bucket.database_backup`,
  `aws_s3_bucket.media_master_backup`. Demo exits 0 (all data stores refused destruction).
- Node and DNS zone carry the identical lifecycle block (asserted by the static guardrails
  checks); they are schema-version-sensitive under synthetic state, so the deterministic
  coverage for them is the Conftest gate.

### Status of ACs
- AC #1 (deny destroy of critical), #2 (allow safe), #3 (red-green deny test), #4 (default deny
  unclassified): met and machine-verified.
- AC #5: see Implementation Plan — wording predates ADR-0017; covered by the guardrails checks,
  pending an AC-wording decision.

### Not done here
- DoD #3 (all CI green) is confirmed after the push runs the `infra-policy` job.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->

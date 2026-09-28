---
id: TASK-2.20
title: 'Separate OpenTofu state by trust boundary: operator-state vs CI-state project'
status: Done
assignee:
  - '@claude'
created_date: '2026-06-09 09:12'
updated_date: '2026-06-09 20:42'
labels:
  - infra
  - opentofu
  - security
milestone: m-0
dependencies:
  - TASK-2.13
references:
  - tools/check-remote-state-backend.py
parent_task_id: TASK-2
priority: high
ordinal: 25000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Bound the blast radius of a state-bucket credential by the only isolation the provider offers: the project. A Hetzner Object Storage S3 key is project-scoped with full access (read/write/delete on every object), and a state-writing credential must be able to delete (overwrite + the use_lockfile lock object), so 'write but not delete state' is impossible and Object Lock is incompatible with mutable state (ADR-0016). The lever that remains is which state a given key can reach.

### Decision
Split the OpenTofu state across two projects by trust boundary so a CI compromise can never reach the foundation/backup state (the irreplaceable management handle):
- cichlids-tfstate (operator-state): foundation + backup state. Its S3 key is operator-only and is NEVER a CI secret.
- cichlids-ci (CI/app-state): the ephemeral app tier's state. Its S3 key is the CI secret.

Proposed buckets: cichlids-tfstate (existing) and cichlids-ci-tfstate. Proposed env keys: CICHLIDS_S3_STATE_* (operator-state, unchanged) and CICHLIDS_S3_CI_* (CI/app-state, new).

### Scope (now)
- Provision cichlids-ci + its bucket + an S3 key out-of-band (operator); the bucket stays empty until the app tier exists.
- cichlids.env.example: add the cichlids-ci key pair; document operator-only vs CI-secret.
- README: add the cichlids-ci project/bucket/key to the bootstrap; mark which state key is a CI secret and which is operator-only.
- check-remote-state-backend.py: assert the app tier's state bucket differs from the foundation/backup state bucket (trust boundary), with a self-test.
- ADR-0016 / ADR-0017: describe the operator-state vs CI-state split and its rationale; reframe the state-deletion note accurately (foundation/backup state unreachable by CI; per-scope state corruption recoverable via versioning), not as an accepted account-wide exposure.

### Out of scope (TASK-2.4)
- The app tier's own backend.tf pointing at cichlids-ci and the wrapper's 'app' tier support land with the app tier.

### Constraints
- No real apply by the agent; the operator provisions the project/bucket/key out-of-band.
- foundation/backup backend configuration is unchanged (still the cichlids-tfstate bucket).

### Realises
- ADR-0014 Layer 2 (blast radius), ADR-0016 (state backend), ADR-0017 (capability boundary by project).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the state layout, When reviewed, Then foundation and backup state live in cichlids-tfstate whose S3 key is never a CI secret, and the app-tier state lives in cichlids-ci whose S3 key is the CI secret
- [ ] #2 Given infra/cichlids.env.example and infra/tofu.sh, When reviewed, Then the operator-state key and the CI-state key are distinct variables, the cichlids-ci key is documented as the CI secret and the cichlids-tfstate key as operator-only
- [ ] #3 Given check-remote-state-backend.py, When it runs, Then it asserts the app-tier state bucket differs from the foundation/backup state bucket, proven by a self-test that fails before the rule and passes after (red-green)
- [ ] #4 Given ADR-0016 and ADR-0017, When reviewed, Then they describe the operator-state vs CI-state split and rationale, and the state-deletion note is reframed accurately rather than as an accepted account-wide exposure
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Test-first (red -> green)
1. tools/check-remote-state-backend.py: add a trust-boundary assertion plus self-test fixtures - the app-tier state bucket must differ from the foundation/backup state bucket (operator-state vs CI-state on different projects). Self-test red before the rule, green after.

### Config / docs (green)
2. infra/cichlids.env.example: add CICHLIDS_S3_CI_ACCESS_KEY/_SECRET_KEY (double-quoted, empty), with a comment that the cichlids-tfstate key (CICHLIDS_S3_STATE_*) is operator-only and never a CI secret, while the cichlids-ci key is the CI secret.
3. infra/tofu.sh: header credential-mapping comment notes the CI-state key feeds the app tier's backend (the app tier itself lands in TASK-2.4); no behavioural change to foundation/backup.
4. infra/README.md: fold both state buckets into the state-bucket step - create cichlids-tfstate (operator-state, foundation+backup) and a bucket in cichlids-ci (CI/app-state), each with its own S3 key; record CICHLIDS_S3_STATE_* (operator-only) and CICHLIDS_S3_CI_* (CI secret). Update the layout/credential-model section to four projects; add the CI-state row to the credential table; fix the GitHub-secrets list so the app-tier backend secret is the cichlids-ci key, and state that the operator-state key is never a CI secret.
5. docs/adr/0016: replace 'To be decided later: app tier uses a separate key in the same bucket' with the operator-state/CI-state split (app tier uses its own state project cichlids-ci) and the rationale (project is the only isolation lever; keep CI off the foundation/backup state).
6. docs/adr/0017: the state tier becomes two projects (operator-state cichlids-tfstate; CI-state cichlids-ci); reframe the state-deletion note - foundation/backup state is unreachable by CI by construction, and per-scope state corruption is recoverable via versioning - rather than an accepted account-wide exposure.

### Verification
- check-remote-state-backend.py self-test + real green; full local gate suite green; secrets-scan over the new quoted env keys.

### Naming (proposed, operator may override)
- Project cichlids-ci; bucket cichlids-ci-tfstate; env CICHLIDS_S3_CI_ACCESS_KEY/_SECRET_KEY.

### Risks / notes
- ADR-0016/0017 are Accepted: amended in place as a current-state correction per the project documentation rule (no superseded-state prose), consistent with how ADR-0017 already absorbed the DNS change.
- No real apply by the agent; the operator provisions cichlids-ci + bucket + key out-of-band. It does not block the foundation/backup applies (those use cichlids-tfstate as before).
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Superseded by TASK-2.22: the two-project model drops the cichlids-ci project and keeps the CI/platform state in the cichlids project. The trust boundary becomes 'the cichlids key cannot reach cichlids-backup' rather than a separate CI-state project.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Superseded by TASK-2.22. The two-project model keeps the CI/platform state in the cichlids project and drops the separate cichlids-ci project, so the cichlids-ci acceptance criteria above no longer apply. The trust boundary it introduced (a CI credential cannot reach the foundation/backup state) is preserved as 'the cichlids key cannot reach cichlids-backup', enforced by the trust-boundary check in tools/check-remote-state-backend.py and verified live in TASK-2.21 Layer 2.
<!-- SECTION:FINAL_SUMMARY:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->

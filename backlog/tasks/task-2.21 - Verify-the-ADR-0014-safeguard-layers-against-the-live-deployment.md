---
id: TASK-2.21
title: Verify the ADR-0014 safeguard layers against the live deployment
status: In Progress
assignee: []
created_date: '2026-06-09 11:57'
updated_date: '2026-10-13 08:15'
labels:
  - infra
  - safeguards
  - verification
milestone: m-0
dependencies:
  - TASK-2.5
references:
  - docs/adr/0014-safeguards-against-destructive-infrastructure-changes.md
parent_task_id: TASK-2
priority: high
ordinal: 23000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Prove, against the real running deployment, that each ADR-0014 destructive-change safeguard layer actually takes effect. Verified sequentially from the innermost layer (the protected resource itself) outward. A layer's acceptance criterion is checked ONLY after the operator personally observes that layer refuse a destructive action on live infrastructure; a red test alone is not sufficient.

### Layers (ADR-0014, ADR-0017)
- Layer 1: prevent_destroy (OpenTofu) and Hetzner delete_protection (provider) on the live critical resources.
- Layer 2: the capability boundary by project (ADR-0017): a credential scoped to one project cannot reach another project's data or state.
- Layer 3: the fail-closed Conftest plan-diff gate denies a destroy of a critical resource (CI fails closed).
- Layer 4: Compliance Object Lock makes a written backup object immutable for its retention window.

### Approach
Innermost first. For each layer the operator performs the destructive attempt and observes the refusal, then the corresponding AC is checked. Nothing is actually destroyed: the point is to watch each layer refuse.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Layer 1: Given the live foundation, When the operator attempts to destroy a critical resource via OpenTofu (plan -destroy) and via the provider (deleting the node in the Hetzner console/API), Then prevent_destroy aborts the OpenTofu run and delete_protection refuses the provider-side deletion, and nothing is destroyed
- [x] #2 Layer 2: Given the project separation, When the cichlids object-storage key (the one CI holds) is used against the cichlids-backup buckets (the backups and the backup state), Then access is refused, showing automation cannot reach the irreplaceable backups
- [ ] #3 Layer 3: Given a tofu plan that deletes or replaces a critical resource in the CI-apply pipeline, When the Conftest plan-diff gate evaluates it, Then it is denied while in-place updates pass (wired with the app/CI-apply tier)
- [x] #4 Layer 4: Given the Compliance Object Lock on a backup bucket, When the operator writes an object and then tries to delete or overwrite it within the retention window, Then the deletion is refused and the object remains
<!-- AC:END -->











## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Each layer's acceptance criterion is checked only after the operator personally observes that layer take effect on the live deployment (not merely a red test)
<!-- DOD:END -->

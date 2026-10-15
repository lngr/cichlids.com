---
id: TASK-5.3
title: Care plan with due dates and reasons
status: In Review
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-15 20:18'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/care-plan.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Care/CarePlanEndpointsTests.cs
parent_task_id: TASK-5
priority: high
ordinal: 83000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Each tank has a care plan with a rhythm per care type and a reason; the plan derives the due date from the last entry. The rhythm follows from volume and stocking as soon as species data exist (slice 4); until then a default applies.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a tank with a water change plan and a last water change, When the rhythm has passed, Then the tank shows the water change as due with the reason of the plan
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
### Default rhythm (decided by the project owner, 2026-10-15)
- Until species data justify a rhythm (slice 4), a water change is due every 7 days; the plan states the reason as the usual recommendation for cichlid tanks. The member can change the rhythm per tank.

### Implementation (2026-10-15)
- Care plan rules per tank and kind (table care_plan_rule: rhythm 1 to 90 days, enabled, reason code with optional JSON parameters, unique per tank and kind). Without a stored rule a water change is due every 7 days with the reason cichlid_default; a measurement has no default rule. Changing the rhythm stores the reason member_choice.
- The due state is derived from the newest entry of the kind: due_in, due_today, overdue (with days) or never_logged (no pressure for a fresh tank); disabled rules have none. The app sends its local date and its UTC offset in minutes, so an entry just after local midnight falls on the right day.
- API: GET /api/tanks/{id}/care/plan, PATCH /api/tanks/{id}/care/plan/{kind} (owner only, outbox care_plan.updated in the same transaction), GET /api/me/care/due (overdue longest first, due today, due in nearest first, never logged) for TASK-5.4.
- App: the care view shows each rule with due state and reason in calm wording and lets the member choose the rhythm (3 to 28 days or custom 1 to 90) or switch the rule off.
- Verification: .NET 335/637 (1 opt-in benchmark skipped)/342 with 0 warnings, client-core 73, mobile 260, both typechecks, all 24 write e2e specs green.
- Tests: app/api/tests/Cichlids.Api.Tests/Features/Care/CarePlanDomainTests.cs, CarePlanEndpointsTests.cs, app/api/tests/Cichlids.Infrastructure.Tests/Persistence/CarePlanRuleConstraintTests.cs, app/client-core/src/carePlan.test.ts, app/mobile/src/tanks/carePlanText.test.ts, app/mobile/e2e/playwright/care-plan.write.mjs.
- Not verified: native Android and iOS runs, review of the translations by native speakers, remote CI.
<!-- SECTION:NOTES:END -->

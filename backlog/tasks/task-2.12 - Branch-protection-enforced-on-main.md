---
id: TASK-2.12
title: Branch protection enforced on main
status: To Do
assignee: []
created_date: '2026-06-05 10:38'
labels:
  - blocked
  - infra
  - ci
  - security
milestone: m-0
dependencies: []
references:
  - tools/check-branch-protection.mjs
parent_task_id: TASK-2
priority: medium
ordinal: 14000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Enforce the hard green gate at the platform level (ADR-0005): merges into main require a pull request and all required status checks to be green.

### Blocked
GitHub branch protection / rulesets are not available on a private, non-organization repository. This story is blocked until one of:
- the repository is made public, or
- it is moved to a GitHub organization / a plan that includes branch protection.

Until then the green gate runs in CI but is not enforced at merge time. This is a known, accepted gap, tracked here so it is not lost.

### Test
An automated check (tools/check-branch-protection.mjs) queries the GitHub branch-protection API for main and asserts: pull request required, the required status checks present, and up-to-date-before-merge. Written red-first once the repository supports protection.

### Realises
ADR-0005 (branch protection enforces the required checks on main).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given main, When a pull request has a failing or missing required status check, Then the merge is blocked
- [ ] #2 Given the repository, When the branch-protection check runs against the GitHub API, Then it confirms main requires a pull request and the green required checks before merge, and fails otherwise
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->

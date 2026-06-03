# ADR-0003: Test Strategy and Definition of Done

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

The application described in [ADR-0001](0001-purpose-and-scope.md) is rebuilt **entirely
with coding agents** on the stack fixed in [ADR-0002](0002-technology-stack.md), starting
from a grown legacy application. This implies a central quality problem: agent-generated
code arises in large volume and at high speed; it must be verifiable for business
correctness **mechanically and demonstrably**, not by manual inspection alone. Authoritative
logic such as reputation/rating computation, feed/ranking assembly, and moderation rules
must not regress unnoticed.

A binding definition is therefore needed of **when** a feature counts as done and **how**
quality is secured structurally — not at discretion.

## Decision

We develop throughout **test-first following the red-green principle** and define a
**test-bound "done" (Definition of Done)**.

1. **User stories are specified testably.** Each story contains at least one concrete,
   end-to-end application path as an acceptance criterion, phrased as *Given – When – Then*.
   Without this path the story is not ready for implementation.

2. **Red-green is mandatory.** For each path, **a failing test is written first (red)**,
   then production code until the test passes (green), then refactoring. This applies from
   the unit level up to the application flow.

3. **Definition of Done.** A feature counts as done **only** when an **automated end-to-end
   test from the user's perspective** exists that runs the path specified in the story
   **demonstrably green**. No passing E2E path ⇒ no done feature.

4. **Test pyramid, not only E2E.** The authoritative domain logic — in particular the
   server-side reputation/ranking computation and the business validation — is secured
   primarily by **fast unit tests**, complemented by **integration tests** of the API
   against a real database. With the event-driven architecture
   ([ADR-0008](0008-event-driven-architecture.md)), reliable publishing (outbox) and correct
   event processing are additionally secured by integration tests. **E2E tests** crown the
   pyramid but do not replace the lower levels.

5. **E2E against the real stack.** The end-to-end user path is verified automatically and
   scriptably against the **fully started stack including real authentication** —
   mobile-first from the perspective of the actually shipped app. The **concrete tool
   choice** of the E2E driver is not subject of this ADR and is fixed in
   [ADR-0004](0004-end-to-end-test-tool.md).

6. **Hard CI gate.** The test suites run automatically in CI. **Without fully green suites
   there is no merge.** New production code without an associated test is rejected.

The decision is deliberately **high-level**: it fixes the *principle* and the *acceptance
criterion*, not the complete tool list (see "Consequences").

## Considered Options

- **Test-first / red-green with E2E as Definition of Done (chosen).**
  - *Pro:* makes business correctness mechanically provable — decisive with agent-generated
    code; the user path is demonstrably covered; regressions are caught early and
    automatically; "done" is objective rather than negotiable.
  - *Con:* higher up-front investment (test harness, discipline); E2E tests are slower and
    can be prone to flakiness, which needs maintenance.
- **Test-after (tests after implementation, without the red-green requirement).**
  - *Pro:* faster first visibility of features.
  - *Con:* tests are, by experience, skipped or "fitted" to existing code; they then prove
    little. No structural protection against agent errors.
- **Only unit/integration tests, no mandatory E2E.**
  - *Pro:* fast, stable, cheap.
  - *Con:* the actually traversed user path (including auth, UI, interplay) stays unproven —
    exactly where stories carry their value.
- **Predominantly manual QA.**
  - *Pro:* low tooling barrier.
  - *Con:* not reproducible, not automatable, does not scale with agent speed.

## Rationale

With fully agent-supported development, mechanically verifiable correctness is the
load-bearing safety net — manual inspection does not scale with code volume. Red-green forces
every path to first provably **fail** and then provably **pass**; this rules out ineffective
or after-the-fact-fitted tests. Binding "done" to a green E2E path ensures that the value
promised in the user story actually — and durably — works from the user's perspective. The
test pyramid keeps the whole thing fast and stable by checking the authoritative logic where
it is cheapest and sharpest: in unit tests.

## Consequences

- **Positive:** an objective, evidence-bound acceptance criterion; an early-acting regression
  net; every user path is durably proven automatically.
- **Negative / Trade-offs:** noticeable up-front investment in test harness and discipline;
  E2E suites are slower and must be maintained against flakiness; every story carries its
  mandatory test load.
- **To be decided later (own ADRs / detail):**
  - **Concrete tool set** for unit and integration tests per language realm and the spinning
    up of real dependencies (DB, broker, auth) in tests.
  - **Coverage thresholds and flakiness/quarantine rules.**
  - **Test and seed data management.**
  - **Non-functional checks** (load, security, accessibility tests).

## References

- [ADR-0001: Purpose and Scope of the Application](0001-purpose-and-scope.md)
- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0004: End-to-End Test Tool](0004-end-to-end-test-tool.md)
- [ADR-0008: Event-Driven Architecture](0008-event-driven-architecture.md)

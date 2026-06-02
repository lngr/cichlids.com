# ADR-0006: End-to-End Execution Environment

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

[ADR-0004](0004-end-to-end-test-tool.md) fixes **Maestro** as the primary E2E driver but
explicitly leaves the **execution environment** open (simulator/emulator vs. real devices,
local vs. cloud). It must now be determined, because the green gate from
[ADR-0003](0003-test-strategy-and-definition-of-done.md) requires an automated, reproducible
E2E run.

The development and CI environment runs on Linux. From this arises the core question: against
which artifact is the binding E2E path executed, without giving up the mobile-first claim.

## Decision

The binding E2E run is performed with **one** Maestro suite against a
**hardware-accelerated Android emulator** that is available both in the containerized
development environment and in CI. **The same** suite is additionally run against the web
channel (browser) to validate the cross-platform one-suite idea.

The **native iOS run** uses the **identical** suite as soon as a macOS-based execution
environment is available. Until then, the absence of the iOS run is a **deliberate,
documented platform boundary** (a native iOS build requires macOS), not a waiver of the
mobile-first acceptance: the native main path is already checked on the real app artifact via
Android.

## Considered Options

- **Android emulator (KVM-accelerated) in development and CI (chosen).**
  - *Pro:* checks the **real native artifact** on the most widespread platform; runs fully
    automated and reproducibly on Linux; with hardware acceleration fast enough for the gate;
    the identical suite is also usable in the browser.
  - *Con:* does not cover iOS; UI-level emulator observation can produce flakiness that must
    be maintained via robust selectors/wait logic.
- **Web run (browser) only as the binding path.**
  - *Pro:* simplest on Linux; no emulator infrastructure.
  - *Con:* misses the mobile-first claim — the actually shipped native path would stay
    unchecked.
- **Device cloud (hosted real devices, iOS+Android).**
  - *Pro:* real devices, both platforms, no own emulator maintenance.
  - *Con:* running costs, external dependency, and data flow to third parties; needlessly
    heavyweight for the current phase.
- **Local macOS machine/runner for iOS now.**
  - *Pro:* immediate native iOS coverage.
  - *Con:* the main development/CI environment is Linux; a continuous macOS path is currently
    disproportionate. Added later.

## Rationale

The Definition of Done requires an automated run on the actually traversed user path. A
KVM-accelerated Android emulator delivers exactly that on Linux: reproducible, fast enough for
the gate, and on the real native artifact. A pure web acceptance would miss the mobile-first
claim; a device cloud or a continuous macOS path are disproportionate in this phase. iOS is
picked up with the same suite as soon as a macOS environment is available — without
maintaining the tests twice.

## Consequences

- **Positive:** the mobile-first main path is checked automatically on the native Android
  artifact; a single suite serves Android and web; fully reproducible in development and CI.
- **Negative / Trade-offs:** iOS coverage only with a later macOS environment; the emulator
  needs hardware virtualization in the environment; UI-level observation requires maintenance
  against flakiness.
- **To be decided later:** concrete macOS execution for iOS; whether a device cloud is added
  for broader device coverage; parallelization and quarantine rules of the E2E run in the
  gate.

## References

- [ADR-0003: Test Strategy and Definition of Done](0003-test-strategy-and-definition-of-done.md)
- [ADR-0004: End-to-End Test Tool](0004-end-to-end-test-tool.md)
- [ADR-0005: CI Platform and Green Gate](0005-ci-platform.md)

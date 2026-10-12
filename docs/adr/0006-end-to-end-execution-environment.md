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
development environment and in CI. **The same** suite runs against the web channel
(browser), which is binding as well, since web offers the full feature set of the apps.

The **native iOS run** uses the **identical** suite on a self-hosted macOS machine that the
pipeline drives remotely, since a native iOS build requires macOS. Android, iOS and web are
released together, so the iOS run is part of the gate for every release. Day-to-day development
checks the main path on the Android artifact and in the browser.

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
- **Self-hosted macOS machine as remote runner for iOS (chosen for the iOS run).**
  - *Pro:* native iOS coverage with the same suite; no running costs or third-party data flow.
  - *Con:* the main development/CI environment is Linux; one more machine to maintain and keep
    reachable.

## Rationale

The Definition of Done requires an automated run on the actually traversed user path. A
KVM-accelerated Android emulator delivers exactly that on Linux: reproducible, fast enough for
the gate, and on the real native artifact. A pure web acceptance would miss the mobile-first
claim; a device cloud is disproportionate. iOS runs the same suite on a self-hosted macOS
machine, without maintaining the tests twice.

## Consequences

- **Positive:** the mobile-first main path is checked automatically on the native Android
  artifact; a single suite serves Android and web; fully reproducible in development and CI.
- **Negative / Trade-offs:** a self-hosted macOS machine to operate for the iOS run; the emulator
  needs hardware virtualization in the environment; UI-level observation requires maintenance
  against flakiness.
- **To be decided later:** whether the iOS run also gates every change or only releases;
  whether a device cloud is added for broader device coverage; parallelization and quarantine
  rules of the E2E run in the gate.

## References

- [ADR-0003: Test Strategy and Definition of Done](0003-test-strategy-and-definition-of-done.md)
- [ADR-0004: End-to-End Test Tool](0004-end-to-end-test-tool.md)
- [ADR-0005: CI Platform and Green Gate](0005-ci-platform.md)

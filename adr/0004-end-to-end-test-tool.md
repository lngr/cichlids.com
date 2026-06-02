# ADR-0004: End-to-End Test Tool

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

[ADR-0003](0003-test-strategy-and-definition-of-done.md) makes the automated end-to-end test
from the user's perspective the binding acceptance criterion ("done" = green E2E path). Which
**tool** drives these E2E tests is deliberately left open there and is decided here.

The choice is shaped by two commitments: the application is **mobile-first** — native apps
for iOS and Android are the main product, a web channel is the second, initially subordinate
channel. The flow that actually needs securing therefore happens in the **actually shipped
native app**, not only in the browser. The frontend is React Native (with Expo). From this
follows the governing criterion: the E2E driver must be able to operate the **bundled native
app** on simulator/emulator/device — a pure web driver is not sufficient.

## Decision

**Maestro** is the **primary, binding E2E driver.** It checks the native iOS and Android apps
and thereby covers the mobile-first main path.

**Playwright** remains foreseen as an **optional, subordinate driver for a dedicated web
channel test** — to be used only once the web channel justifies its own richer test suite
(such as browsing large galleries on a big screen). For the mobile-first Definition of Done
from ADR-0003, **Maestro** is authoritative.

## Considered Options

- **Maestro (chosen, primary).**
  - *Pro:* drives the **real, bundled native app** (iOS + Android) via the accessibility
    layer — **without** npm packages or instrumentation in the app code; thereby tests
    exactly the store artifact. **One** declarative YAML suite runs across platforms (iOS,
    Android, and web). Low entry and maintenance barrier, well scriptable for coding agents;
    currently the de-facto standard in the React Native/Expo space.
  - *Con:* somewhat higher flakiness than a JS-runtime-synchronized approach, because it
    observes at the UI level; the project depends on a single main sponsor.
- **Detox.**
  - *Pro:* lowest flakiness for pure React Native apps, since it synchronizes with the JS
    runtime instead of polling the UI.
  - *Con:* effortful setup, **tight coupling** to specific React Native and Xcode versions
    (maintenance-heavy), **no** web target.
- **Appium.**
  - *Pro:* very universal (native, hybrid, web; language-agnostic), large ecosystem, mature
    device-cloud integration.
  - *Con:* heavyweight, slower, higher setup/maintenance effort; the added value (broad
    device/language zoo) is not needed here.
- **Playwright (web only).**
  - *Pro:* best-in-class for the web channel: fast, stable, excellently scriptable, strong
    debugging tools.
  - *Con:* **cannot operate a native app** — and thus, as the sole driver, misses the
    mobile-first main path. Therefore only as a subordinate web driver.

## Rationale

The acceptance criterion from ADR-0003 refers to the actually traversed user path. Since the
main product is the native app, the E2E driver must operate this app the way a human does.
Maestro does this without intervening in the app code, with a single cross-platform suite and
low maintenance load — the properties most important for agent-supported, mobile-first
development. Detox buys lower flakiness with version coupling and covers no web; Appium is
overdimensioned; Playwright fundamentally cannot operate the native main path and therefore
stays limited to the subordinate web channel.

## Consequences

- **Positive:** the mobile-first main path is checked on the real store artifact; one suite
  for iOS and Android; low maintenance load; no instrumentation of the app code; the web
  channel stays cleanly extensible with Playwright without forcing it now.
- **Negative / Trade-offs:** UI-level observation can lead to flakiness that must be
  maintained via robust selectors and wait logic; potentially **two** E2E tools (Maestro
  native, Playwright web); dependency on a single main sponsor of the tool.
- **To be decided later (own ADRs / detail):**
  - **Execution environment:** simulator/emulator vs. real devices, local CI vs. device
    cloud ([ADR-0006](0006-end-to-end-execution-environment.md)).
  - **Scope and trigger** of a standalone Playwright web suite.
  - **Integration into the CI gate** (parallelization, quarantine of flaky tests).

## References

- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0003: Test Strategy and Definition of Done](0003-test-strategy-and-definition-of-done.md)
- [ADR-0006: End-to-End Execution Environment](0006-end-to-end-execution-environment.md)

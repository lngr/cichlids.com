# ADR-0005: CI Platform and Green Gate

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

[ADR-0003](0003-test-strategy-and-definition-of-done.md) makes a **hard green gate**
binding: without fully green test suites there is no merge, and new production code without a
test is rejected. The concrete **CI platform** that runs this gate was deliberately left open
there and is fixed here.

Decisive are: low entry barrier, good fitness for agent-supported development, broad support
for the stack's building blocks (containers, .NET, Node, a hardware-accelerated Android
emulator for the E2E stage), proximity to the version-control hosting in use, and the ability
to check and roll out the declarative infrastructure
([ADR-0010](0010-gitops-and-infrastructure-as-code.md)) from the same pipeline.

## Decision

**GitHub Actions** is the CI platform. The green gate is implemented as a required check
across multiple jobs (backend unit/integration, client unit/lint/typecheck, end-to-end on the
real app artifact). **A merge into the main branch requires all required checks to be green**;
enforcement is additionally via the host's branch protection (one-time setup). The same
platform also runs the infrastructure pipeline
([ADR-0010](0010-gitops-and-infrastructure-as-code.md)): `plan` as a check in the pull
request, `apply` after the merge.

## Considered Options

- **GitHub Actions (chosen).**
  - *Pro:* directly attached to the repository hosting in use; a very large, well-documented
    action catalog (container services, .NET/Node setup, KVM-accelerated Android emulators,
    infrastructure tooling); free minutes for the project scope; good scriptability for
    agents.
  - *Con:* some vendor lock-in; macOS runners (for native iOS E2E later) are more expensive
    and quota-limited.
- **GitLab CI.**
  - *Pro:* very mature, integrated pipeline; good for self-hosted runners.
  - *Con:* additional hosting/mirroring needed since source control lives elsewhere; no added
    value over the obvious option.
- **Self-hosted CI (e.g. Jenkins/Drone) on own VMs.**
  - *Pro:* full control.
  - *Con:* high operating and maintenance effort; overdimensioned for the current project
    size; diverts capacity from product development.

## Rationale

The gate from ADR-0003 must take effect reliably, with low maintenance, and without friction.
GitHub Actions is directly attached to the existing source control, covers all needed stages
including a hardware-accelerated Android emulator, can at the same time check and roll out the
declarative infrastructure, and is excellently scriptable and traceable for agent-supported
work. A separate CI track or a foreign platform would bring operating or integration effort
without discernible added value.

## Consequences

- **Positive:** low entry barrier; one place for code, reviews, test gate, and infrastructure
  pipeline; the hard green gate is technically enforceable via required checks.
- **Negative / Trade-offs:** vendor lock-in; native iOS E2E later require more expensive macOS
  runners.
- **To be decided later:** coverage thresholds, rules for flaky-test quarantine,
  parallelization, possible self-hosted runners for larger loads, and the exact protection
  rules for the infrastructure `apply` stage.

## References

- [ADR-0003: Test Strategy and Definition of Done](0003-test-strategy-and-definition-of-done.md)
- [ADR-0004: End-to-End Test Tool](0004-end-to-end-test-tool.md)
- [ADR-0006: End-to-End Execution Environment](0006-end-to-end-execution-environment.md)
- [ADR-0010: GitOps and Infrastructure as Code](0010-gitops-and-infrastructure-as-code.md)

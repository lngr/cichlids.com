# ADR-0015: User Story and Spec Management with Backlog.md

- **Status:** Proposed
- **Date:** 2026-06-03
- **Deciders:** Alexander Langer

## Context and Problem Statement

[ADR-0003](0003-test-strategy-and-definition-of-done.md) mandates that every user story
be specified testably — with at least one Given-When-Then acceptance path — and binds
"done" to a green end-to-end test. It deliberately leaves open **where and how** stories
and their acceptance criteria are authored and tracked.

The application is built almost entirely by coding agents, so the story/spec system must be
usable **by agents** as a first-class interface, not only by humans. At the same time it must
remain fully usable **without** agents (authoring, reviewing, a board). A grown SaaS tracker
would split the source of truth away from the repository, add an external dependency and
access-control surface, and make agent integration indirect.

A decision is therefore needed on the tool and format for user stories and specs that is
git-native, agent-addressable, and self-hostable without external services.

## Decision

We adopt **[Backlog.md](https://github.com/MrLesk/Backlog.md)** as the tool for user stories,
specs, and task tracking.

1. **Plaintext in the repository.** Stories/tasks are Markdown files under `backlog/`,
   versioned with the code. The repository is the single source of truth; no external tracker.

2. **Two equal interfaces.** Humans use the CLI, the Kanban TUI, and the web UI; coding agents
   use the built-in MCP server (stdio). Both act on the same files.

3. **The Definition of Done is configured to mirror ADR-0003.** The project-level DoD checklist
   requires a specified Given-When-Then acceptance path, the red-green test order, a green
   end-to-end test for the user path, unit/integration coverage of authoritative logic, and
   fully green CI. A story is not done until its DoD checklist is satisfied.

4. **Stories and specs are written in English**, consistent with the code and these ADRs.

5. **Bidirectional story-to-test binding.** Each story is a vertical slice — one user-observable
   path provable by one green end-to-end test. The story references its test path, and the
   end-to-end test ([ADR-0004](0004-end-to-end-test-tool.md)) carries a stable back-reference to
   the story id (a Maestro `tags` entry). The mapping is thereby unambiguous from both sides and
   machine-checkable.

6. **Enforced acceptance via the green gate.** Status changes live in the same branch and pull
   request as the feature and its test (`To Do` → `In Progress` → `In Review` → `Done`), so the
   merge brings feature, test, and story status to `main` atomically. The CI gate
   ([ADR-0005](0005-ci-platform.md)) rejects the merge if a story marked `Done` does not
   reference an existing end-to-end test that carries the matching back-reference. The merged,
   green state is authoritative; the board only mirrors it.

The decision fixes the *tool, format, DoD binding, and the story↔test traceability mechanism*;
concrete agent operating rules live in the agent instruction file and the `backlog-workflow`
skill rather than in this ADR.

## Considered Options

- **Backlog.md (chosen).**
  - *Pro:* git-native plaintext (versioned, reviewable, no external SaaS); first-class agent
    interface via a built-in MCP server *and* a full human path (CLI/TUI/web); configurable DoD
    that can directly encode the ADR-0003 acceptance contract; offline-capable.
  - *Con:* young single-vendor project; agents must be disciplined to operate through its
    interfaces rather than hand-editing files; cross-branch task-state features depend on git
    remote access.
- **GitHub Issues / Projects.**
  - *Pro:* ubiquitous, integrates with pull requests and CI.
  - *Con:* source of truth lives outside the repository; weaker offline/plaintext story; agent
    access is indirect via an additional API and credentials.
- **Hosted tracker (Jira / Linear).**
  - *Pro:* rich planning and reporting features.
  - *Con:* external SaaS dependency, separate access control, recurring cost; stories decoupled
    from the repository; heavy for an agent-driven, code-first workflow.
- **Hand-written Markdown without tooling.**
  - *Pro:* zero dependency.
  - *Con:* no board, no consistent metadata/IDs, no machine interface for agents, no enforceable
    DoD structure; drifts quickly.

## Rationale

Keeping stories as versioned plaintext in the repository makes the acceptance contract from
ADR-0003 reviewable in the same flow as the code it governs and removes an external dependency.
Backlog.md is the only considered option that offers an equally strong **agent** interface (MCP)
and **human** interface over that same plaintext, and lets the Definition of Done be configured
to encode the ADR-0003 acceptance criterion so that "done" stays objective rather than
negotiable. Restricting authoring to English keeps stories aligned with the code and ADRs they
reference.

## Consequences

- **Positive:** one source of truth in git; the ADR-0003 acceptance contract is encoded in a
  reusable DoD checklist; agents act through a stable machine interface; no external tracker,
  cost, or extra credentials; works offline. Story, test, and code stay traceable in both
  directions, and acceptance is enforced mechanically rather than by convention.
- **Negative / Trade-offs:** dependency on a young single-vendor tool; agents must operate only
  through the documented interfaces (MCP/CLI), not by editing task files directly, or metadata
  and history drift; the binding adds a small authoring discipline (every `Done` story needs a
  back-tagged test) that the gate enforces.
- **To be decided later:** periodic archival/cleanup policy for completed stories; whether story
  state is tracked across branches (depends on git remote access in the execution environment).

## References

- [ADR-0001: Purpose and Scope of the Application](0001-purpose-and-scope.md)
- [ADR-0003: Test Strategy and Definition of Done](0003-test-strategy-and-definition-of-done.md)
- [ADR-0004: End-to-End Test Tool](0004-end-to-end-test-tool.md)
- [ADR-0005: CI Platform and Green Gate](0005-ci-platform.md)
- [ADR-0010: GitOps and Infrastructure as Code](0010-gitops-and-infrastructure-as-code.md)
- [ADR-0011: AI Agents as a Decoupled Service](0011-ai-agents-as-decoupled-service.md)

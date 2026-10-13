---
id: TASK-2.18
title: >-
  Migrate DNS to project-scoped Hetzner Cloud DNS and refresh OpenTofu provider
  pins
status: In Progress
assignee: []
created_date: '2026-06-09 06:15'
updated_date: '2026-10-13 08:15'
labels:
  - infra
  - opentofu
  - dns
milestone: m-0
dependencies:
  - TASK-2.16
references:
  - infra/platform/guardrails_test.py
parent_task_id: TASK-2
priority: high
ordinal: 20000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Replace the deprecated account-level `germanbrew/hetznerdns` provider with the project-scoped DNS resources of the official `hetznercloud/hcloud` provider, and bring every OpenTofu provider pin to the current-latest line. Hetzner migrated DNS into the Cloud Console/API (project-scoped); the old standalone DNS API/provider is deprecated (2025-11-10) and no longer accepts new zones.

### Scope
- foundation tier: swap `hetznerdns_zone.primary` -> `hcloud_zone.primary` (mode=primary, name, delete_protection, prevent_destroy, critical labels); drop the `hetznerdns` provider, token variable, and the account-level DNS credential.
- Provider pins (both tiers): hcloud `~> 1.49` -> `~> 1.64`; aws `~> 5.0` -> `~> 6.0`; remove hetznerdns; required_version `>= 1.10.0` -> `>= 1.12.0`. Regenerate both .terraform.lock.hcl via `tofu init -upgrade`.
- Credential model: DNS now uses the primary project's cloud token (no separate DNS token). Update infra/tofu.sh, infra/cichlids.env.example, infra/foundation/terraform.tfvars.example, infra/README.md (step 7 + remove the account-level-DNS residual exposure), and the ADR-0017 residual-exposures section.
- Guards: foundation guardrails_test.py and policy/destroy_gate.rego (+ fixtures) update the DNS critical type from hetznerdns_zone to hcloud_zone; the zone now also carries delete_protection.

### Constraints
- No real apply by the agent; validation is static (tofu fmt/validate, guardrails, conftest). The operator applies and creates the fresh zone (the existing empty NS/SOA-only zone is deleted in the console first).
- AWS provider 6.x is a breaking major; our usage is S3-only (bucket + versioning + object_lock_configuration) and must still validate.

### Realises
- ADR-0009/0010 (providers), ADR-0014 (Layer 1 guards on the zone), ADR-0017 (capability boundary; removes the account-level-DNS residual exposure).
<!-- SECTION:DESCRIPTION:END -->


## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Test-first ordering (red -> green)
1. Guards red first:
   - infra/foundation/guardrails_test.py: change the DNS critical entry from ("hetznerdns_zone","primary",False) to ("hcloud_zone","primary",True) (True = delete_protection expected, like node/volume). Run -> red, because dns.tf still declares hetznerdns_zone.
   - policy/destroy_gate.rego: in critical_type replace hetznerdns_zone with hcloud_zone; update policy/fixtures/destroy_all_plan.json so the zone deletion is type hcloud_zone. conftest verify + the destroy-fixture-denied assertion -> red until rego and fixture agree.

2. Make green - config migration:
   - infra/foundation/versions.tf: hcloud ~> 1.64, aws ~> 6.0, drop the hetznerdns block, required_version >= 1.12.0.
   - infra/backup/versions.tf: aws ~> 6.0, required_version >= 1.12.0.
   - infra/foundation/providers.tf: remove the hetznerdns provider block.
   - infra/foundation/dns.tf: hcloud_zone "primary" { mode="primary"; name=var.dns_zone_name; delete_protection=true; labels=local.critical_labels; lifecycle { prevent_destroy = true } }.
   - infra/foundation/variables.tf: remove hetznerdns_token.
   - infra/foundation/outputs.tf: dns_zone_id = hcloud_zone.primary.id; dns_zone_nameservers from hcloud_zone.primary.authoritative_nameservers (assigned list - exact expression confirmed against the resolved provider schema).

3. Credential model + docs:
   - infra/tofu.sh: drop the CICHLIDS_HETZNER_DNS_TOKEN require/export (foundation now needs only the cloud token, primary S3 keys, SSH key + the state backend keys).
   - infra/cichlids.env.example: remove CICHLIDS_HETZNER_DNS_TOKEN.
   - infra/foundation/terraform.tfvars.example: drop the DNS-token export comment.
   - infra/README.md: step 7 -> DNS uses the primary project cloud token (project-scoped token created in the Cloud Console); drop the DNS row from the credential-file table; remove the account-level-DNS residual-exposure paragraph; delegation step unchanged (nameservers from the output).
   - docs/adr/0017: replace the account-level-DNS residual-exposure bullet - DNS is now project-scoped, inside the cichlids boundary, guarded like the other resources. Current-state + timeless rationale only; ADR references only ADRs.

4. Regenerate lockfiles + validate:
   - tofu -chdir=infra/foundation init -upgrade and tofu -chdir=infra/backup init -upgrade -> fresh .terraform.lock.hcl (hcloud 1.64.x, aws 6.x; no hetznerdns).
   - tofu fmt -recursive infra; tofu validate both tiers (this is the real proof of aws 6.x compatibility, AC #1).
   - Re-run: guardrails (both tiers), conftest verify, destroy-fixture-denied + safe-pass assertions, project-separation, remote-state, secrets-scan -> all green.

### Story<->test binding (ADR-0015)
- Keep infra/foundation/guardrails_test.py as the referenced verifying artifact and add the task-2.18 back-tag; the binding test is the guardrails check + the Conftest gate over the hcloud_zone fixture.

### Risks / assumptions
- AWS 6.x is a breaking major: our S3 usage is post-v4 style (bucket + versioning + object_lock_configuration); risk is low but validated, not assumed. If a 6.x change bites, pin the latest 6.x that validates and record why.
- hcloud_zone.authoritative_nameservers is a nested attribute; the output expression is confirmed against the resolved schema during validate, not guessed.
- tofu init -upgrade must reach the registry from the devcontainer; if blocked, the operator runs init and commits the regenerated lockfiles.
- No real apply by the agent; the operator deletes the empty NS/SOA-only zone in the console and lets the apply create it fresh.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Delivered
- DNS migrated from the deprecated germanbrew/hetznerdns provider to the project-scoped hcloud DNS API:
  - infra/foundation/dns.tf: hcloud_zone.primary (mode=primary, ttl=3600, delete_protection=true, critical labels, prevent_destroy).
  - providers.tf: hetznerdns provider removed; variables.tf: hetznerdns_token removed; outputs.tf: dns_zone_id/dns_zone_nameservers from hcloud_zone (authoritative_nameservers.assigned).
- Provider pins on the current stable line, lockfiles regenerated:
  - foundation: hcloud ~> 1.64 (1.65.0), aws ~> 6.0 (6.49.0); required_version >= 1.12.0.
  - backup: aws ~> 6.0 (6.49.0); required_version >= 1.12.0.
  - Both .terraform.lock.hcl reference only hcloud and aws; germanbrew/hetznerdns is gone.
- Credential model: DNS rides on the primary project cloud token. infra/tofu.sh, cichlids.env.example and foundation/terraform.tfvars.example carry no DNS token; the hcloud_token variable description reflects the ADR-0017 capability boundary.
- Guards: foundation guardrails_test.py expects hcloud_zone with delete_protection; policy/destroy_gate.rego critical_type and policy/fixtures/destroy_all_plan.json use hcloud_zone.
- ADR-0017 lists DNS as a primary-project resource (project-scoped on the Cloud API).

### Verification
- tofu fmt and validate pass for foundation and backup under hcloud 1.65 + aws 6.49 (the aws v5->v6 major is compatible with the S3-only usage).
- guardrails (foundation + backup), conftest unit tests, the safe and destroy fixtures, project-separation, remote-state and secrets-scan pass; the prevent_destroy demo passes under aws 6.x.
- Validation is static; the foundation and backup tiers are applied out-of-band by the operator (ADR-0010, ADR-0016).
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given infra/platform and infra/backup, When the operator inspects versions.tf and the .terraform.lock.hcl, Then the providers are hetznercloud/hcloud and hashicorp/aws with no germanbrew/hetznerdns, and tofu validate passes (the CI 'OpenTofu tiers validate' job is green)
- [x] #2 Given the live deployment, When the operator inspects the cichlids project DNS, Then cichlids.com exists as an hcloud_zone and dns.tf shows it carries delete_protection and prevent_destroy
- [ ] #3 Given the destroy-gate policy, When conftest evaluates a plan that deletes an hcloud_zone, Then it is denied (hcloud_zone is in the policy critical_type)
- [x] #4 Given the credential model, When the operator inspects cichlids.env.example and infra/tofu.sh, Then the only Hetzner Cloud token is the cichlids project token and the DNS zone is part of the platform tier
- [x] #5 Given both tiers' .terraform.lock.hcl, When the operator inspects them, Then they pin hcloud 1.65.0 and aws 6.49.0 and reference no germanbrew/hetznerdns
<!-- AC:END -->

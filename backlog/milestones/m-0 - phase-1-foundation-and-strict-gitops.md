---
id: m-0
title: "Phase 1: Foundation and strict GitOps"
---

## Description

Standing GitOps platform deployed from day one: OpenTofu-provisioned Hetzner node + object storage + Hetzner DNS, single-node k3s bootstrapped with Argo CD, day-one destructive-change safeguards (ADR-0014) and Postgres PITR backup with a first tested restore (ADR-0013). Persistent prod + staging plus ephemeral per-PR preview environments, with a first green end-to-end smoke path as the Definition-of-Done proof (ADR-0003/0006). Realises PLAN.md phase 1 and ADR-0005/0009/0010.

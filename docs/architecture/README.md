---
title: Architecture
doc_id: arch.index
type: index
audience: [engineers, new-joiners, agents]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [docs.index, adr.index]
---

# Architecture

How the platform fits together, and why. Docs here explain; the decisions themselves live in [adr/](../adr/README.md), and the per-component detail lives in [reference/](../reference/README.md).

Until the planned docs exist, start with:

- [PROJECT-STATE.md](../PROJECT-STATE.md): verified current state, the divergence register and the beta roadmap.
- [adr/](../adr/README.md): messaging (0002), gateway (0003), service boundaries (0005), database (0006), agent security model (0007), agent transport (0008).
- The root `README.md`: the architecture diagram and design principles.

## Docs

| Document | Status |
| --- | --- |
| [authentication-analysis.md](authentication-analysis.md) | **Historical.** Written 2025-12 while the auth decision was pending; the decision has since been made (hybrid BetterAuth + OpenIddict, see issue #120) and the doc's Blazor premise never materialized (frontends are Astro). Kept for background only. Moves to `archive/` (DL-601); the decision gets an ADR (DL-207). |
| [K8S-architecture-decision-and-observability-concerns.md](K8S-architecture-decision-and-observability-concerns.md) | **Proposed / stale (2024-12).** Never promoted to an ADR. Kubernetes deployment is deferred post-beta; Docker Compose (`deploy/compose/docker-compose.services.yml`) is the current deployment target. Moves to `archive/` (DL-601); the decision gets an ADR (DL-217). |

## Planned

| Doc | Covers | Backlog |
| --- | --- | --- |
| `overview.md` | Services, agents, frontends and data stores in one picture | DL-101 |
| `request-path.md` | A request from browser or CLI through the Gateway to a service and its database or bus | DL-102 |
| `agent-connection-model.md` | Outbound-only agents: mTLS, enrollment, the hub, commands | DL-103 |
| `tenancy-and-data.md` | Organizations, which service owns which data, the shared database today | DL-104 |
| `deployment.md` | Compose for beta, Aspire for dev, Kubernetes deferred | DL-105 |

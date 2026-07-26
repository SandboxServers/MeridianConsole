# Architecture

Start here for how the platform fits together:

- **[docs/PROJECT-STATE.md](../PROJECT-STATE.md)** — verified current state, divergence
  register, and beta roadmap (2026-07)
- **[docs/adr/](../adr/)** — Architecture Decision Records (the authoritative "why"):
  messaging (0002), gateway (0003), service boundaries (0005), database (0006), agent
  security model (0007), agent transport (0008)
- **Root `README.md`** — architecture overview diagram and design principles

Documents in this directory:

| Document | Status |
|----------|--------|
| [authentication-analysis.md](authentication-analysis.md) | **Historical.** Written 2025-12 while the auth decision was pending; the decision has since been made (hybrid BetterAuth + OpenIddict, see issue #120) and the doc's Blazor premise never materialized (frontends are Astro/React). Kept for background only. |
| [K8S-architecture-decision-and-observability-concerns.md](K8S-architecture-decision-and-observability-concerns.md) | **Proposed / stale (2024-12).** Never promoted to an ADR. Kubernetes deployment is deferred post-beta; Docker Compose (`deploy/compose/docker-compose.services.yml`) is the current deployment target. |

---
title: Seams Reference
doc_id: ref.seams.index
type: index
audience: [engineers, agents, reviewers]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [ref.index, test.seam-tests]
---

# Seams Reference

A seam is a boundary where two components must agree: a request shape, a route prefix, a token, an event. Each chapter names both sides, the contract type in `Dhadgar.Contracts`, and the test that runs both sides together. A seam is `confirmed-working` only when that test passes.

This is where the platform has broken before: the enrollment shapes disagreed and the Gateway prefixes didn't match the services ([PROJECT-STATE.md](../../PROJECT-STATE.md), DV-1).

## Chapters

None yet.

## Planned

| Chapter | Sides | Expected `implementation` | Backlog |
| --- | --- | --- | --- |
| `enrollment.md` | Agent ↔ Nodes | confirmed-working | DL-331 |
| `login-and-tokens.md` | Frontend ↔ BetterAuth ↔ Identity ↔ SharedAuth | code-exists | DL-332 |
| `gateway-routing.md` | Gateway ↔ every service | code-exists | DL-333 |
| `service-auth.md` | Service ↔ service, admin API key | code-exists | DL-334 |
| `events.md` | Every bus publisher ↔ its consumers | code-exists | DL-335 |
| `agent-hub.md` | Agent ↔ Nodes hub (heartbeat, commands) | mixed (heartbeat confirmed-working by seams S1 to S4; commands planned). Until the chapter exists, read [ADR-0008 as built](../../adr/0008-agent-transport-signalr.md#as-built-sprint-2) | DL-501 |

---
title: Platform Reference
doc_id: ref.platform.index
type: index
audience: [engineers, operators, agents]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [ref.index]
---

# Platform Reference

Facts that hold across every service: where things listen, how they're configured, how they fail, what they emit.

## Chapters

None yet.

## Planned

| Chapter | Covers | Replaces | Backlog |
| --- | --- | --- | --- |
| `ports.md` | The `50x0` port scheme and every address | `SERVICE-PORTS.md` | DL-341 |
| `configuration.md` | Config layering, secrets, every options class | `CONFIGURATION-MANAGEMENT.md` | DL-342 |
| `errors.md` | problem+json, error codes | | DL-343 |
| `observability.md` | Activity sources, meters, log conventions, the collector | | DL-344 |
| `persistence.md` | DbContexts, databases, migrations | the wrong `Migrations/README.md` files | DL-345 |
| `messaging.md` | MassTransit setup, retries, faults | | DL-346 |
| `dependencies.md` | Packages with rules attached (OpenTelemetry pin, SIPSorcery) | | DL-347 |

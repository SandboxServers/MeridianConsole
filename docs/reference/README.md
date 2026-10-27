---
title: Reference
doc_id: ref.index
type: index
audience: [engineers, operators, agents]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [docs.index, meta.conventions]
---

# Reference

What each component does today, and how we know. Every chapter follows the same evidence chain (purpose, decisions, contract, expected behaviour, implementation, evidence and status, gaps, operating it) so you can see where the design and the code part ways. Chapter rules: [meta/conventions.md](../meta/conventions.md#reference-chapters). Template: [meta/templates/reference-chapter.md](../meta/templates/reference-chapter.md).

| Subsection | What's in it |
| --- | --- |
| [services/](services/README.md) | One chapter per control-plane service, plus the CLI |
| [agents/](agents/README.md) | The customer-hosted agent: Core, Windows, the installer, the game-server wrapper |
| [shared/](shared/README.md) | The shared .NET and TypeScript libraries |
| [frontends/](frontends/README.md) | The Astro sites |
| [seams/](seams/README.md) | Each boundary where two components must agree, and the test that proves they do |
| [platform/](platform/README.md) | Cross-cutting facts: ports, configuration, errors, observability, persistence, messaging, dependencies |

Start with [seams/](seams/README.md) when you're changing anything that crosses a service or agent boundary. That's where the platform has broken before.

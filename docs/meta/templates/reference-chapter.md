<!--
  Reference chapter template. Read docs/meta/how-to-write.md first.
  Save as docs/reference/<area>/<name>.md and delete these comments.
  Keep all eight sections; mark one "N/A — <reason>" rather than dropping it.
  No line numbers: cite path + symbol.
-->
---
title: [Component or seam, title case]
doc_id: ref.[area].[topic]
type: reference
audience: [engineers, agents]
status: draft
implementation: [planned | code-exists | confirmed-working | mixed]
last_verified: [YYYY-MM-DD]
verified_by: [handle, or automated-agent while draft]
sources:
  - [src/... path#Type.Member]
evidence:
  # Required for confirmed-working. One of:
  # - test: tests/.../SomeSeamTests.cs
  # - manual: 2026-10-26 steven <what was checked>
decisions: [adr-NNNN, D-XXn]
divergences: [DV-n]
issues: []
related: [ref.x.y]
supersedes: []
---

# [Title]

[One sentence: the question this chapter answers. Two or three sentences of context for a reader who arrived cold.]

## Purpose and boundaries

[What this owns. What it explicitly doesn't own, and who does.]

## Decisions

| Decision | What it fixes | Record |
| --- | --- | --- |
| [Short name] | [One line] | [ADR-NNNN / D-XXn / "none, backfill"] |

## Contract

[Types in src/Shared/Dhadgar.Contracts by name and file. Routes in both forms when they differ. Hub methods, events, status codes, error codes.]

| Route (Gateway) | Route (service) | Method | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| `/api/v1/...` | `/...` | `POST` | `TypeName` | `TypeName` | `401`, `409 code` |

## Expected behaviour

[Numbered, testable claims. Each one should be something a test could check.]

1. [Claim]

## Implementation

| Claim | Where |
| --- | --- |
| 1 | `src/.../File.cs` (`Type.Member`) |

## Evidence and status

| Claim | Implementation | Evidence |
| --- | --- | --- |
| 1 | [planned / code-exists / confirmed-working] | [test path, manual check, or "none"] |

## Gaps and divergences

[Where Implementation differs from Expected behaviour. Each row names a DV-n, an issue or an ADR. "None known" is a valid answer.]

## Operating it

| Item | Value |
| --- | --- |
| Configuration | `Section:Key` (`ENV__VAR`) |
| Port | [from docs/reference/platform/ports.md] |
| Health | `/healthz` |
| Telemetry | [activity source, meter, key log events] |
| Runbook | [link] |

## Related

- [Companion doc](../path.md): [why you'd read it]

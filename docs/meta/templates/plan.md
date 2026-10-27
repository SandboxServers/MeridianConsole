<!--
  Plan template: code not yet written, on a path that is already decided.
  Save as docs/plans/<topic>.md and delete these comments.
  A plan never claims anything works. When the code lands, write the reference
  chapter, mark this plan superseded and move it to docs/archive/.
-->
---
title: [Feature, title case]
doc_id: plan.[topic]
type: plan
audience: [engineers, agents]
status: draft
implementation: planned
last_verified: [YYYY-MM-DD]
verified_by: [handle, or automated-agent while draft]
sources:
  # Existing code the plan builds on or changes.
  - [src/... path]
decisions: [adr-NNNN, D-XXn]
divergences: [DV-n]
issues: []
related: [ref.x.y]
supersedes: []
---

# [Title]

[One paragraph: what will exist when this is built, and why it matters. Link the roadmap phase in PROJECT-STATE.]

## Where it stands

| Part | Implementation | Notes |
| --- | --- | --- |
| [Part] | [planned / code-exists] | [Campaign or PR, if any] |

## Decided

Every row cites the decision that fixed it. If a row has no record, it belongs under Open.

| What | Decision | Record |
| --- | --- | --- |
| [e.g. Hub path] | [`/hubs/agent` on Gateway and Nodes, no prefix transform] | [D-S22] |

## Contract

[The Dhadgar.Contracts types, routes, hub methods and error codes this will add or change. Name the target files.]

## Target layout

| File | Change |
| --- | --- |
| `src/.../NewFile.cs` | [new: what it holds] |

## Seam tests

[The test(s) that will prove it works end to end, and what each fails on while the sides disagree.]

## Open

| Question | Options | Who decides |
| --- | --- | --- |
| [Question] | [A / B] | [Owner] |

## Out of scope

- [Thing people will assume is included]

## Related

- [Campaign ledger, ADR, reference chapter]

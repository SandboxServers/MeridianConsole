<!--
  Explanation template: how things fit together and why.
  Save as docs/architecture/<topic>.md or docs/security/<topic>.md.
  Decisions themselves live in ADRs; link them, don't restate them in full.
-->
---
title: [Topic, title case]
doc_id: [arch | sec].[topic]
type: explanation
audience: [engineers, new-joiners, agents]
status: draft
implementation: [n/a | mixed]
last_verified: [YYYY-MM-DD]
verified_by: [handle, or automated-agent while draft]
decisions: [adr-NNNN]
related: [ref.x.y]
---

# [Title]

[The one-paragraph version. A reader who stops here should have the right mental model.]

## The picture

```mermaid
flowchart LR
  A[Component] -->|what flows| B[Component]
```

## How it works

[Walk the diagram. Name each component and link its reference chapter.]

## Why it is this way

[The trade-offs, with a link to each ADR or decision. What we gave up.]

## What isn't built yet

[Parts of the picture that are planned or code-exists only, with links to plans and DV entries. Keep the reader from mistaking the design for the current state.]

## Related

- [Reference chapters, ADRs, plans]

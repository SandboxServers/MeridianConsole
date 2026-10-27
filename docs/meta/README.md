---
title: Writing the Library
doc_id: meta.index
type: index
audience: [doc authors, agents, reviewers]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [docs.index]
---

# Writing the Library

The rules, tools and templates for writing docs in `docs/`. Read these before you add or change a library doc.

| Doc | What it's for |
| --- | --- |
| [conventions.md](conventions.md) | Frontmatter, doc IDs, doc types, status and implementation, citing code, the reference chapter sections, which source wins. |
| [how-to-write.md](how-to-write.md) | The workflow from "this needs a doc" to a merged doc, and how a doc moves from `draft` to `current`. |
| [glossary.md](glossary.md) | Every platform term, defined once. Link to it instead of redefining a term. |
| [doc-update-map.md](doc-update-map.md) | When you change a given area of code, which docs you must update. |
| [templates/](templates/) | Starting points for each doc type: [reference chapter](templates/reference-chapter.md), [plan](templates/plan.md), [how-to](templates/how-to.md), [runbook](templates/runbook.md), [explanation](templates/explanation.md), [tutorial](templates/tutorial.md). ADRs use [adr/TEMPLATE.md](../adr/TEMPLATE.md). |

Claude sessions use the `documentation-writer` agent ([.claude/agents/documentation-writer.md](../../.claude/agents/documentation-writer.md)), which follows these rules. For where things live and which source wins, agents also read [agents/domain.md](../agents/domain.md).

---
title: Archive
doc_id: archive.index
type: index
audience: [engineers, agents]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [docs.index]
---

# Archive

Docs that have been replaced, kept for history. Nothing here describes the platform as it is. Each archived doc keeps a banner at the top naming what replaced it.

Don't cite an archived doc as a source. Agents read here only to find out why something was once planned differently.

## How a doc gets here

1. Its content has moved into a library doc, or it's been decided against.
2. The doc gets `status: superseded` and `superseded_by`, plus a `> [!WARNING]` banner linking the replacement (format in [meta/conventions.md](../meta/conventions.md)).
3. It moves here with `git mv`, keeping its filename, and the old location's inbound links are updated.

## Archived docs

None yet. Phase 6 of the [seed backlog](../analysis/doc-library/seed-backlog.md#phase-6-migrate-trim-and-archive) lists the candidates.

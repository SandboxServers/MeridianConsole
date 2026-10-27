---
title: How to Write a Doc
doc_id: meta.how-to-write
type: how-to
audience: [doc authors, agents]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [meta.conventions, meta.doc-update-map, meta.glossary]
---

# How to Write a Doc

Use this when you add a doc to the library or rewrite one. Keep [conventions.md](conventions.md) open beside it; this page is the workflow, that page is the rules. The `documentation-writer` agent (`.claude/agents/documentation-writer.md`) follows the same steps.

## 1. Check it doesn't already exist

Search before you write. The library is young and the repo has many older docs, so the content you need is often already written somewhere, just in the wrong place or out of date.

```powershell
rg -i "<topic>" docs src --glob "*.md"
```

Also check `docs/analysis/` (campaign ledgers often hold the freshest findings) and the project's own `src/<Project>/README.md`.

If a doc exists, move and tighten it instead of writing a parallel one. One fact has one home; everything else links to it.

## 2. Pick one question and one type

State the doc's question in one sentence. If you can't, you don't have a doc yet.

| If the question is… | The type is… | It goes in… |
| --- | --- | --- |
| "Get me running for the first time." | `tutorial` | `getting-started/` |
| "How do I add / change / run X?" | `how-to` | `guides/` |
| "X is broken or due. What do I run?" | `runbook` | `runbooks/` |
| "What exactly does X do, and how do we know?" | `reference` | `reference/<area>/` |
| "How does X fit together, and why?" | `explanation` | `architecture/` or `security/` |
| "Why did we choose X over Y?" | `adr` | `adr/` |
| "What will we build for X, on what fixed path?" | `plan` | `plans/` |

Bad questions are surveys ("Nodes overview"). Good ones have an edge ("What does Nodes do between an enrollment request and a signed certificate?").

## 3. Start from the template

Copy the matching file from [templates/](templates/) and fill in the frontmatter first. ADRs use [adr/TEMPLATE.md](../adr/TEMPLATE.md) with frontmatter added on top. Pick a `doc_id` from the prefixes in [conventions.md § Doc IDs](conventions.md#doc-ids) and check nobody has it:

```powershell
rg "doc_id: <your-id>" docs
```

## 4. Gather evidence before you write claims

- **Code:** read the files you will cite. Note paths and symbols, not line numbers.
- **Contract:** find the types in `src/Shared/Dhadgar.Contracts`. If the two sides of a seam use their own copies instead, that is a gap; record it.
- **Decisions:** find the ADR or recorded decision (`D1`–`D4` in PROJECT-STATE, `D-*` in campaign ledgers). If there isn't one, list "ADR to backfill: <decision>" in your report.
- **Tests:** find the test that proves each behaviour. A seam test (one that runs both sides together) is what earns `confirmed-working`.
- **Status:** read the component's row in [PROJECT-STATE.md](../PROJECT-STATE.md). Your doc can't claim more.

## 5. Write

- Lead with the answer. The first paragraph says what the reader gets from this doc.
- Use the second person, active voice and present tense.
- For reference chapters, follow the eight sections in [conventions.md § Reference chapters](conventions.md#reference-chapters).
- For plans, keep "Decided" and "Open" apart, and cite the decision behind every decided item.
- For runbooks, write for someone tired at 2 a.m.: numbered, copy-pasteable, with a rollback and a "how you know it worked" step.
- Security rules are quoted exactly from CLAUDE.md or `docs/security/`, never paraphrased.

## 6. Wire it into the library

In the same change:

1. Add the doc to its section `README.md` and to [docs/README.md](../README.md).
2. Add new terms to [glossary.md](glossary.md).
3. Add or update the row in [doc-update-map.md](doc-update-map.md) that will send the next code change in this area to your doc.
4. Link companions both ways.
5. If the doc replaces an older one, mark the old one `superseded` with a banner and `superseded_by`, and list it for `docs/archive/`.

## 7. Pass the gate

A doc is `draft` until a person walks it against the code. Before you hand it over, check:

- [ ] Frontmatter is complete; `type`, `audience`, `status` and `implementation` are set.
- [ ] Every `confirmed-working` claim has an `evidence` entry.
- [ ] Every gap names a `DV-n`, issue or ADR.
- [ ] No line numbers outside `docs/analysis/`.
- [ ] The indexes, glossary and doc-update map are updated.
- [ ] Comprehension test: someone new to the repo could act on it without asking you.

### Promotion

| From | To | Who | When |
| --- | --- | --- | --- |
| `draft` | `current` | A person | They walked it against `main` and it matched. Set `last_verified` and `verified_by`. |
| `current` | `stale` | Anyone | They found it wrong. Add a banner saying what is wrong, in the same change. |
| `stale` | `current` | A person | It was fixed and re-walked. |
| any | `superseded` | Anyone | A newer doc replaces it. Set `superseded_by`. |

An ADR is promoted from `Proposed` to `Accepted` only by the owner, even when the decision was obviously made long ago.

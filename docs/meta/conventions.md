---
title: Documentation Conventions
doc_id: meta.conventions
type: reference
audience: [doc authors, reviewers, agents]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [meta.how-to-write, meta.doc-update-map, meta.glossary]
---

# Documentation Conventions

This is the rulebook for every doc in `docs/`: frontmatter, doc IDs, the doc types, status values, how to cite code, and formatting. If you are about to write a doc, read [how-to-write.md](how-to-write.md) first; it walks you through the workflow and calls into this page.

## Frontmatter

Every doc in the library starts with a YAML frontmatter block. Required keys must always have a value; if you don't know it yet, the doc is `draft` and the key holds a placeholder (`tbd`, `[]`). Never leave a key blank.

```yaml
---
title: Agent Enrollment
doc_id: ref.seams.enrollment
type: reference
audience: [engineers, agents]
status: current
implementation: confirmed-working
last_verified: 2026-10-26
verified_by: steven
sources:
  - src/Shared/Dhadgar.Contracts/Nodes/EnrollmentContracts.cs
  - src/Dhadgar.Nodes/Services/EnrollmentService.cs#EnrollmentService.EnrollAsync
  - src/Agents/Dhadgar.Agent.Core/Authentication/EnrollmentService.cs
evidence:
  - test: tests/Dhadgar.Nodes.Tests/Integration/AgentEnrollmentContractTests.cs
decisions: [adr-0007, D-S21]
divergences: [DV-1]
related: [ref.services.nodes, ref.agents.core]
supersedes: []
---
```

### Required keys

| Key | Values | Notes |
| --- | --- | --- |
| `title` | string | Plain English, title case. Matches the H1. |
| `doc_id` | string | The stable citation key. See [Doc IDs](#doc-ids). Never reused or renamed. |
| `type` | `tutorial`, `how-to`, `runbook`, `reference`, `explanation`, `adr`, `plan`, `index` | See [Doc types](#doc-types). One per doc. |
| `audience` | list | Any of `engineers`, `operators`, `contributors`, `agents`, `new-joiners`, `doc authors`, `reviewers`. |
| `status` | `draft`, `current`, `stale`, `superseded` | The doc's own health. See [Status](#status). |
| `implementation` | `n/a`, `planned`, `code-exists`, `confirmed-working`, `mixed` | What the code behind the doc has reached. See [Implementation](#implementation). |
| `last_verified` | ISO date | When someone last walked the doc against the code. |
| `verified_by` | handle, or `automated-agent` | `automated-agent` is valid only while `status: draft`. |

### Optional keys

| Key | When to set it |
| --- | --- |
| `sources` | Reference chapters and plans: the code the doc describes, as paths, optionally `#Type.Member`. |
| `evidence` | Required when `implementation` is `confirmed-working`. Each item is `test: <path>` or `manual: <date> <who> <what was checked>`. |
| `decisions` | ADR IDs (`adr-0008`) and recorded decisions (`D2`, `D-S22`) the doc rests on. |
| `divergences` | `DV-n` entries from [PROJECT-STATE.md](../PROJECT-STATE.md) that touch this doc. |
| `issues` | GitHub issue numbers the doc depends on (`[94, 118]`). |
| `related` | Other `doc_id`s. Set it whenever the doc has neighbours. |
| `supersedes` | `doc_id`s this doc replaces. |
| `superseded_by` | Set together with `status: superseded`. |

ADRs keep their existing body format (Status / Context / Decision / Consequences, see [adr/TEMPLATE.md](../adr/TEMPLATE.md)) and add frontmatter on top. The ADR's body `Status` and frontmatter `status` must agree: `Proposed` maps to `draft`, `Accepted` to `current`, `Deprecated` to `stale`, `Superseded` to `superseded`.

Files under `docs/analysis/` (campaign ledgers and worknotes), `src/**/README.md` and `src/**/CLAUDE.md` don't carry frontmatter. They follow their own formats.

## Doc IDs

A `doc_id` is lowercase and dot-separated, with kebab-case segments. Docs cite each other by `doc_id` in frontmatter and by relative link in prose. The filename doesn't have to match the ID; pick a filename people can read.

| Prefix | Section | Example |
| --- | --- | --- |
| `start.` | `getting-started/` | `start.local-dev` |
| `guide.` | `guides/` | `guide.add-a-contract` |
| `ops.` | `runbooks/` | `ops.rotate-agent-ca` |
| `ref.services.` | `reference/services/` | `ref.services.nodes` |
| `ref.agents.` | `reference/agents/` | `ref.agents.windows` |
| `ref.shared.` | `reference/shared/` | `ref.shared.contracts` |
| `ref.frontends.` | `reference/frontends/` | `ref.frontends.panel` |
| `ref.seams.` | `reference/seams/` | `ref.seams.agent-hub` |
| `ref.platform.` | `reference/platform/` | `ref.platform.ports` |
| `arch.` | `architecture/` | `arch.overview` |
| `sec.` | `security/` | `sec.threat-model` |
| `test.` | `testing/` | `test.seam-tests` |
| `adr-NNNN` | `adr/` | `adr-0008` |
| `plan.` | `plans/` | `plan.command-signing` |
| `meta.` | `meta/` | `meta.glossary` |
| `agents.` | `agents/` (how Claude sessions work here) | `agents.domain` |

A section index (`README.md`, `type: index`) takes its section's prefix plus `index`: `ref.services.index`, `sec.index`, `adr.index`. `archive/` has no prefix of its own and uses its folder name: `archive.index`. The master index at `docs/README.md` is `docs.index`.

## Doc types

The library follows [Diátaxis](https://diataxis.fr/), plus three types this repo needs: ADRs, runbooks and plans.

| Type | Answers | Must | Must not |
| --- | --- | --- | --- |
| `tutorial` | "Get me to a first working result." | Lead one path to a visible result; every step works as written. | Branch into options or explain internals. |
| `how-to` | "How do I do X?" | Start from a goal; numbered steps; name the checks that prove it worked. | Teach concepts; list every option. |
| `runbook` | "It's broken or due. What do I run?" | Numbered, copy-pasteable steps; a rollback; how to confirm recovery. | Assume the reader knows the system well. |
| `reference` | "What exactly does X do, and how do we know?" | Be complete and dry; follow the [reference chapter shape](#reference-chapters). | Argue for a design; walk through a task. |
| `explanation` | "How does this fit together, and why?" | Give context, trade-offs and diagrams; link the ADRs. | Be the only home of a decision (that is an ADR). |
| `adr` | "Why X over Y?" | Record one decision, its context, the alternatives and its consequences. | Describe the whole system. |
| `plan` | "What will we build, on what fixed path?" | Separate what is decided (with the decision that fixed it) from what is open; name the target files and contracts. | Claim anything works. `implementation` is always `planned`. |
| `index` | "Where is the doc for X?" | List every doc in its section with a one-line description. | Hold content of its own beyond a short orientation. |

## Status

`status` describes the doc, not the code.

| Value | Meaning |
| --- | --- |
| `draft` | Written but not walked against the code by a person. May be agent-written. |
| `current` | Walked end-to-end against the code on `last_verified` and signed off by a person. |
| `stale` | Known to disagree with the code. Add a banner at the top that says what is wrong. |
| `superseded` | Replaced. Set `superseded_by`, add a banner, and move the file to `docs/archive/` when convenient. |

A doc moves `draft` → `current` only when a person (not an agent) has checked it. Any agent or person who finds a `current` doc wrong flips it to `stale` with a banner in the same change, or fixes it.

## Implementation

`implementation` describes the code behind the doc and uses the same words as [PROJECT-STATE.md](../PROJECT-STATE.md).

| Value | Meaning | Proof required |
| --- | --- | --- |
| `n/a` | The doc doesn't describe code (a tutorial, a meta doc). | None. |
| `planned` | The code isn't written. Only plans and the planned parts of chapters. | A decision or design that fixes the path. |
| `code-exists` | The code is written but nothing proves it works end-to-end. | The `sources` that contain it. |
| `confirmed-working` | A passing seam test, or a manual check the user recorded, proves it. | An `evidence` entry. |
| `mixed` | Parts differ. The chapter's status table gives each part its own value. | Per row. |

A doc never claims more than PROJECT-STATE does. If you think PROJECT-STATE is behind, report it; PROJECT-STATE changes only in a campaign close-out.

## Citing code

- Cite code by repo-relative path, and add a symbol when the file is large: `src/Dhadgar.Nodes/Services/EnrollmentService.cs` (`EnrollmentService.EnrollAsync`).
- **No line numbers** in the library. Code moves, and a stale line number points at the wrong thing without anyone noticing. Line numbers are fine in dated analysis docs under `docs/analysis/`.
- Cite contract types by name and file in `src/Shared/Dhadgar.Contracts`. Don't paste whole DTOs; a short excerpt that shows one point is fine.
- Give a route in both forms when they differ: as the Gateway exposes it (`/api/v1/nodes/...`) and as the service maps it (`/nodes/...`).
- Cite configuration by its full key (`ReverseProxy:Clusters:nodes`) and its environment-variable form when operators set it that way.
- Cite decisions by ID (`ADR-0008`, `D2`, `D-S22`), divergences by `DV-n`, issues as `#94`.

## Reference chapters

A reference chapter answers one question about one component or seam: "what does X do, and how do we know?" It follows this order, so a reader always knows where to look. Mark a section `N/A — <reason>` rather than dropping it.

1. **Purpose and boundaries.** One paragraph: what it owns, what it doesn't.
2. **Decisions.** The ADRs and `D-*` decisions it rests on, one line each.
3. **Contract.** The `Dhadgar.Contracts` types, routes (both forms), events, hub methods, status codes and error codes other components depend on.
4. **Expected behaviour.** What must be true, stated as testable claims.
5. **Implementation.** Where each claim is implemented, by path and symbol.
6. **Evidence and status.** One row per claim: `planned`, `code-exists` or `confirmed-working`, with the test or manual check.
7. **Gaps and divergences.** Where 5 differs from 4. Every gap names a `DV-n`, an issue or an ADR. An unexplained gap is the defect this structure exists to catch.
8. **Operating it.** Configuration keys, ports, health endpoints, telemetry names and the runbook link.

Section 5 is allowed to differ from section 4; that difference is the most useful thing a chapter records.

## When sources disagree

From most to least authoritative for "what does it do":

1. The code on `main`, together with its passing tests.
2. `src/Shared/Dhadgar.Contracts` for any cross-boundary shape.
3. [PROJECT-STATE.md](../PROJECT-STATE.md) for what is confirmed working.
4. ADRs and recorded decisions, for why.
5. Reference chapters, then explanations.
6. Plans (intent, not fact).
7. `src/**/README.md`.
8. `docs/archive/`, `docs/implementation-plans/` and `.planning/` (history only).

When a lower source contradicts a higher one, fix the lower one or mark it `stale` in the same change. When a doc contradicts the code and you think the code is wrong, don't change the doc to match; report it and file an issue.

## Formatting

- Second person, active voice, present tense. Short sentences.
- Expand each acronym on first use, or link it to the [glossary](glossary.md).
- Tables have spaces around pipes: `| Option | Type |`.
- Code blocks always name their language.
- Diagrams are Mermaid in the doc. No screenshots of diagrams or code.
- Headings are sentence case. One H1 per doc, matching `title`.
- Links are relative within the repo.
- Banners for `stale` and `superseded` docs go directly below the H1:

```markdown
> [!WARNING]
> **Stale (2026-10-27):** the routes below predate the `/api/v1` prefix fix (DV-1). See [Gateway routing](../reference/services/gateway.md).
```

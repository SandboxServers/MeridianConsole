---
title: MeridianConsole Documentation
doc_id: docs.index
type: index
audience: [engineers, operators, contributors, agents, new-joiners]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [meta.conventions, meta.how-to-write, meta.glossary]
---

# MeridianConsole Documentation

MeridianConsole (codename Dhadgar) is a multi-tenant control plane for game servers that run on hardware customers own. Customers install an agent on their machines; the agent makes outbound connections to the control plane, which orchestrates game servers on those nodes. This library explains what the platform is, why it's built this way, what each part does today, and what we're building next.

> [!NOTE]
> The library is new (October 2026) and mostly empty. The [seed backlog](analysis/doc-library/seed-backlog.md) lists the docs that will fill it. Until a section has its docs, use [Where the rest lives today](#where-the-rest-lives-today).

## Start here

**If you're a person new to the repo:**

1. [PROJECT-STATE.md](PROJECT-STATE.md): what works, what doesn't, and what's next.
2. [Architecture](architecture/README.md): how the pieces fit.
3. [Getting started](getting-started/README.md): a running stack on your machine.
4. [Glossary](meta/glossary.md): the words we use.

**If you're a Claude session or another agent:**

1. The root [CLAUDE.md](../CLAUDE.md): the rules that apply to every change.
2. [docs/agents/domain.md](agents/domain.md): where things live and which source wins when two disagree.
3. [PROJECT-STATE.md](PROJECT-STATE.md): the plan of record and the status of each feature.
4. The reference chapter for the component you're touching, then its plan if the work isn't built yet.

## Sections

| Section | Answers | Diátaxis type |
| --- | --- | --- |
| [getting-started/](getting-started/README.md) | "How do I get a first result?" | tutorial |
| [guides/](guides/README.md) | "How do I do X?" (engineering tasks) | how-to |
| [runbooks/](runbooks/README.md) | "How do I operate or fix X?" | runbook |
| [reference/](reference/README.md) | "What exactly does X do, and how do we know?" | reference |
| [architecture/](architecture/README.md) | "How do the pieces fit, and why?" | explanation |
| [security/](security/README.md) | "What do we defend against, and how?" | explanation, reference |
| [testing/](testing/README.md) | "How do we test, and what proves a seam works?" | explanation, reference |
| [adr/](adr/README.md) | "Why did we decide X?" | ADR |
| [plans/](plans/README.md) | "What are we building next, on what fixed path?" | plan |
| [agents/](agents/ai-sdlc.md) | "How do Claude sessions work in this repo?" | how-to |
| [analysis/](analysis/README.md) | Campaign ledgers. Working files, not library docs. | none |
| [archive/](archive/README.md) | Superseded docs kept for history. | none |
| [meta/](meta/README.md) | "How do I write a doc for this library?" | reference |

## Reading the frontmatter

Every library doc starts with frontmatter. Two keys tell you how far to trust it:

| `status` | Meaning |
| --- | --- |
| `draft` | Written, not yet checked by a person. Useful, not authoritative. |
| `current` | Checked against the code on `last_verified`. |
| `stale` | Known to be out of date; a banner says what's wrong. |
| `superseded` | Replaced; `superseded_by` says by what. |

| `implementation` | Meaning |
| --- | --- |
| `n/a` | The doc doesn't describe code (a how-to, a glossary). |
| `planned` | The code isn't written. |
| `code-exists` | The code is written, but nothing proves it works end to end. |
| `confirmed-working` | A passing seam test or a recorded manual check proves it; `evidence` names it. |
| `mixed` | Parts differ; the body says which. |

Full rules: [meta/conventions.md](meta/conventions.md).

## Where the rest lives today

Docs written before this library still hold most of what we know. The campaign moves each into its section and archives the original ([seed backlog, Phase 6](analysis/doc-library/seed-backlog.md#phase-6-migrate-trim-and-archive)). Until then:

| Topic | Current doc | Will move to |
| --- | --- | --- |
| Project status and decisions | [PROJECT-STATE.md](PROJECT-STATE.md) | stays |
| Development setup | [DEVELOPMENT_SETUP.md](DEVELOPMENT_SETUP.md) | `getting-started/local-dev.md` |
| Configuration | [CONFIGURATION-MANAGEMENT.md](CONFIGURATION-MANAGEMENT.md) | `reference/platform/configuration.md` |
| Gateway | [gateway-routing-reference.md](gateway-routing-reference.md) (stale), [gateway-authentication.md](gateway-authentication.md) | `reference/services/gateway.md` |
| Identity | [identity-api-reference.md](identity-api-reference.md), [identity-claims-reference.md](identity-claims-reference.md), [identity-oauth-providers.md](identity-oauth-providers.md), [identity-webhooks.md](identity-webhooks.md) | `reference/services/identity.md` |
| BetterAuth | [betterauth-api-reference.md](betterauth-api-reference.md) | `reference/services/betterauth.md` |
| Secrets | [secrets-api-reference.md](secrets-api-reference.md) plus three planning docs | `reference/services/secrets.md` |
| Shared libraries | [libraries/](libraries/) | `reference/shared/` |
| Service plans (pre-2026) | [implementation-plans/](implementation-plans/) | `plans/` or `archive/` |
| Self-hosted edition | [kip/](kip/README.md) | `plans/kip.md` |
| Linting, scanning, containers | `LINTER_*.md`, `SECURITY-SCANNING-*.md`, `CONTAINER-*.md` | `plans/lint-and-sast.md` or `archive/` |
| Per-project detail | `src/<Project>/README.md` | landing pages linking to `reference/` |
| Decisions | [adr/](adr/README.md), PROJECT-STATE §4, campaign ledgers | `adr/` (backfill in Phase 2) |

`.planning/` at the repo root is legacy input, not the plan of record.

## Contributing to the library

Read [meta/how-to-write.md](meta/how-to-write.md), start from a [template](meta/templates/), and check the [doc-update map](meta/doc-update-map.md) when you change code. Claude sessions can dispatch the `documentation-writer` agent ([.claude/agents/documentation-writer.md](../.claude/agents/documentation-writer.md)).

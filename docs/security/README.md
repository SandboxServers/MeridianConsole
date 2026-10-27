---
title: Security
doc_id: sec.index
type: index
audience: [engineers, reviewers, agents]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [docs.index]
---

# Security

What we defend against, the controls that stop it, and where each control is enforced in code. The binding rules are in the root [CLAUDE.md](../../CLAUDE.md#security-critical); docs here quote them exactly and explain them, never restate them differently. To report a vulnerability, see [SECURITY.md](../../SECURITY.md).

Every doc in this section gets a `security-advisor` read before it moves past `draft`.

## Docs

None yet.

## Planned

| Doc | Covers | Backlog |
| --- | --- | --- |
| `threat-model.md` | Trust boundaries, assets, attackers, and what each control stops | DL-401 |
| `rules.md` | Path validation, command injection, SSRF, sensitive data: each rule, why, and where it's enforced | DL-402 |
| `agent-security.md` | Agent certificates, command validation, signing status, process containment | DL-403 |
| `auth-model.md` | Who authenticates how, at which layer | DL-404 |
| `review-areas.md` | What needs a security review and how to ask for one | DL-405 |

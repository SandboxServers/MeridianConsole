<!--
  Runbook template: an operator procedure. Write for someone tired at 2 a.m.
  Save as docs/runbooks/<service-or-task>.md.
  Every procedure has: when to use it, steps, how to confirm, rollback.
-->
---
title: [Service Operations, or Task Name]
doc_id: ops.[topic]
type: runbook
audience: [operators]
status: draft
implementation: [code-exists | confirmed-working]
last_verified: [YYYY-MM-DD]
verified_by: [handle, or automated-agent while draft]
sources:
  - [deploy/... or src/... path]
evidence:
  # A runbook is confirmed-working only once someone has run it. Record when.
  # - manual: YYYY-MM-DD <who> ran <procedure> against <environment>
related: [ref.services.x]
---

# [Title]

[What this covers and which environment(s) it applies to: local compose, staging, production.]

## Health at a glance

| Check | Command or URL | Healthy looks like |
| --- | --- | --- |
| [Liveness] | `curl -fsS http://localhost:50x0/healthz` | `200` |

## [Procedure name]

**Use when:** [symptom or schedule].

1. [Step]

   ```bash
   [exact command]
   ```

**Confirm:** [what proves it worked].

**Rollback:**

1. [Step]

## Escalation

[Who or where to go when this runbook doesn't fix it. Link the issue tracker label or the relevant reference chapter.]

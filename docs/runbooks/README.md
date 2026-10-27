---
title: Runbooks
doc_id: ops.index
type: index
audience: [operators, engineers]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [docs.index]
---

# Runbooks

Operator procedures for running and fixing Meridian Console. Each procedure says when to use it, the exact steps, how to confirm it worked, and how to roll back. Template: [meta/templates/runbook.md](../meta/templates/runbook.md).

## Runbooks

These date from January 2026 and predate the template. DL-431 refreshes them against current code.

| Runbook | Service | Description |
| --- | --- | --- |
| [Gateway Operations](./gateway-operations.md) | Gateway | YARP proxy, rate limiting, circuit breaker, diagnostics |
| [Identity Service Deployment](./identity-service-deployment.md) | Identity | User and org management, OAuth providers |
| [Nodes Service Operations](./nodes-service-operations.md) | Nodes | Hardware inventory, agent enrollment, health monitoring |
| [Secrets Service Operations](./secrets-service-operations.md) | Secrets | Azure Key Vault integration, audit logging |

## Planned

| Runbook | Covers | Backlog |
| --- | --- | --- |
| `compose-stack.md` | Run, upgrade and reset the stack with Docker Compose | DL-432 |
| `windows-agent-install.md` | Build, sign and install the Windows agent MSI; uninstall cleanly | DL-433 |
| `agent-certificates.md` | Rotate the agent CA, revoke a node, re-enroll | DL-434 |
| `troubleshooting.md` | First-day failures and their fixes | DL-435 |

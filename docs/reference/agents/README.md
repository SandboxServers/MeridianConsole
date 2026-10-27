---
title: Agents Reference
doc_id: ref.agents.index
type: index
audience: [engineers, operators, agents]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [ref.index, arch.agent-connection-model]
---

# Agents Reference

The agent that customers install on their own hardware. It connects outbound to the control plane, authenticates with a client certificate and runs game servers. "Agent" here means the product's agent, not a Claude agent.

Changes to these projects need a security review (see the root [CLAUDE.md](../../../CLAUDE.md#code-review)).

## Chapters

| Chapter | Project | `implementation` |
| --- | --- | --- |
| [windows-installer.md](windows-installer.md) | WiX MSI under `src/Agents/Dhadgar.Agent.Windows/Installer` | mixed |

## Planned

| Chapter | Project | Expected `implementation` | Backlog |
| --- | --- | --- | --- |
| `core.md` | Dhadgar.Agent.Core | code-exists | DL-311 |
| `windows.md` | Dhadgar.Agent.Windows | code-exists | DL-312 |
| `game-server-wrapper.md` | Dhadgar.Agent.GameServerWrapper | code-exists | DL-314 |

The Linux agent is a stub with no decided path yet; it's tracked as a plan (`plans/agent-linux.md`, DL-509). The agent hub and heartbeat are recorded in [ADR-0008 as built](../../adr/0008-agent-transport-signalr.md#as-built-sprint-2); a seam chapter (`agent-hub.md`) is planned under [seams/](../seams/README.md).

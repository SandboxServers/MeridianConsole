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

None yet.

## Planned

| Chapter | Project | Expected `implementation` | Backlog |
| --- | --- | --- | --- |
| `core.md` | Dhadgar.Agent.Core | code-exists | DL-311 |
| `windows.md` | Dhadgar.Agent.Windows | code-exists | DL-312 |
| `windows-installer.md` | WiX MSI under `src/Agents/Dhadgar.Agent.Windows/Installer` | code-exists | DL-313 |
| `game-server-wrapper.md` | Dhadgar.Agent.GameServerWrapper | code-exists | DL-314 |

The Linux agent is a stub with no decided path yet; it's tracked as a plan (`plans/agent-linux.md`, DL-509). Agent hub and heartbeat docs wait for the `sprint2-agent-nodes` campaign to close.

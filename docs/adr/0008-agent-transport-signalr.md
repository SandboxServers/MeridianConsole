# ADR-0008: SignalR Hub as Agent Transport

## Status

Accepted (2026-07-26) — implementation pending (see `docs/PROJECT-STATE.md`, roadmap Phase 2)

## Context

The agent subsystem and the Nodes service were implemented in parallel from separate
implementation plans and ended up with incompatible transports:

- `Dhadgar.Agent.Core`'s `ControlPlaneClient` is built around a SignalR `HubConnection`
  to `/hubs/agent` (heartbeat, command push via `ReceiveCommand`, command results,
  telemetry), with HTTP used only for enrollment and certificate renewal.
- `Dhadgar.Nodes` exposes HTTP-only agent endpoints (`/agents/enroll`,
  `/agents/{nodeId}/heartbeat`, `/agents/{nodeId}/certificates/renew`) and hosts no
  SignalR hub. No `/hubs/agent` hub exists anywhere in the solution.
- ADR-0007 described a third model ("command queue polling, not push") that neither
  side implemented.

A decision was required before any agent work could proceed; there is no configuration
in which the current code interoperates.

Options considered:

1. **HTTP-first**: align the agent to Nodes' existing HTTP endpoints; add a command
   long-poll/queue endpoint. Smallest server change, but polling latency for commands,
   and discards the agent's completed SignalR client work.
2. **SignalR hub**: implement `/hubs/agent` in Nodes to match the agent's design.
   Real-time push, single persistent outbound connection, natural fit for console
   streaming later; costs new server-side surface and mTLS-over-WebSocket handling.
3. **RabbitMQ/AMQP**: agents as message-bus clients. Rejected: exposes the broker to
   customer networks and conflicts with the outbound-HTTPS-only trust model.

## Decision

Implement a SignalR hub (`/hubs/agent`) in the Nodes service as the canonical transport
for agent ↔ control-plane communication after enrollment.

- **Enrollment and certificate renewal remain HTTP** (existing Nodes endpoints), with
  the request/response contracts reconciled into `Dhadgar.Contracts` as the single
  source of truth for both sides.
- **Hub surface (initial)**: `Heartbeat`, `ReceiveCommand` (server→agent),
  `CommandResult`, `Telemetry`.
- **Authentication**: mTLS client certificate (issued at enrollment) validated by the
  existing `MtlsMiddleware` path; the certificate's `node_id` binds the connection.
- Nodes' existing HTTP heartbeat endpoint stays during migration and is retired once
  the hub path is proven.
- Heartbeats received via the hub feed the existing `HeartbeatService`/health-scoring
  pipeline; command dispatch to the hub becomes the integration point for the Servers
  service's lifecycle operations.

This supersedes the "command queue polling (not push)" line of ADR-0007. All other
ADR-0007 security requirements (mTLS, command signing per issue #94, audit trail)
apply unchanged to the hub transport.

## Consequences

### Positive

- Unblocks the agent vertical with the least rework of already-reviewed agent code
- Push-based commands: no polling latency, single persistent outbound connection
- Foundation for real-time console streaming (Console service) and live telemetry

### Negative

- New security-critical server surface in Nodes that must be built and tested
  (connection lifecycle, groups-per-node, reconnect semantics, backpressure)
- mTLS over WebSockets needs explicit verification behind the Gateway (session
  affinity for SignalR is already configured there, but client-cert forwarding is not)
- Scale-out later requires a SignalR backplane (Redis is already in the stack)

### Neutral

- API versioning (issue #116) must cover hub method contracts before the first agent
  binary ships to customer hardware
- The agent's `CommandReceived` event and dispatcher wiring still need a bootstrap
  hosted service on the agent side (this ADR fixes the transport, not the missing
  agent runtime)

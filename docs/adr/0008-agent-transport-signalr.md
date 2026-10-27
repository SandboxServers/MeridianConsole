# ADR-0008: SignalR Hub as Agent Transport

## Status

Accepted (2026-07-26). Implemented in Sprint 2 (campaign `sprint2-agent-nodes`, closed out 2026-10-27), except the command methods and the Redis backplane. See [As built (Sprint 2)](#as-built-sprint-2) and `docs/PROJECT-STATE.md`.

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

## As built (Sprint 2)

*Draft: written by the Sprint 2 close-out (P15) from the campaign ledger and the code; the owner confirms it.* The Decision above is unchanged. This section records what was built, where it differs from the Decision, and what stays open. The decisions cited as `D-S*` are in the [campaign README](../analysis/sprint2-agent-nodes/README.md#decisions). The campaign ships as one PR (draft #235), so this is true of `main` once that PR merges.

### What was built

| Decision text | As built | Record |
| --- | --- | --- |
| Hub at `/hubs/agent` in Nodes | `AgentHub` in `src/Dhadgar.Nodes/Hubs/AgentHub.cs`, mapped with `RequireAuthorization("AgentPolicy")`. The Gateway routes the same path with no prefix transform. | D-S22 |
| Contracts reconciled into `Dhadgar.Contracts` | Hub interfaces, heartbeat DTOs, path, protocol and error-code constants in `src/Shared/Dhadgar.Contracts/Nodes/AgentHubContracts.cs` (`IAgentHub`, `IAgentHubClient`, `AgentHeartbeat`, `AgentHeartbeatAck`, `AgentHubProtocol`, `AgentHubErrorCodes`). The certificate profile and renewal constants are in `AgentCertificateProfile.cs` and `AgentCertificateRenewalPolicy.cs` beside it; the renewal DTOs are in `EnrollmentContracts.cs`. | D-S21 |
| Hub surface: `Heartbeat`, `ReceiveCommand`, `CommandResult`, `Telemetry` | **Narrower.** `IAgentHub` declares only `Heartbeat`. `IAgentHubClient` declares `ReceiveCommand(string)` and `ReceivePing(string)` (the agent's existing shapes). The `CommandResult` and `Telemetry` server methods wait for the commands campaign; no code path calls them. | D-S29 |
| API versioning before the first agent ships (#116) | The protocol version travels in the `X-Agent-Protocol` header, currently `1`. Nodes aborts a connection that sends any other value and treats a missing header as `1`. | D-S22 |
| Certificate `node_id` binds the connection | The node ID comes only from the `node_id` claim of the client certificate; `AgentHeartbeat` has no `NodeId` field and the agent no longer sends `X-Node-Id`. | D-S23 |
| (not in the Decision) Error surface | Business errors return in `AgentHeartbeatAck.ErrorCode` and are never thrown. Authentication failures are HTTP 401 or 403 problem+json on the negotiate or connect request. | D-S24 |
| Heartbeats feed the existing health pipeline | Hub heartbeats update `NodeHealth`, `LastHeartbeat` and status through `IHeartbeatService`. `AgentHeartbeatValidator` bounds every field before any database write. | D-S29; [Nodes README § Agent hub](../../src/Dhadgar.Nodes/README.md#agent-hub) |
| Authentication by the existing mTLS path | Nodes validates the chain, the SPIFFE ID and revocation in `MtlsMiddleware`; `/hubs/agent` is a protected path. A new `AgentMtls` authentication scheme writes the 401 and 403 problem+json. The hub applies the same usable-certificate rule on connect and on every heartbeat. | D-S27, D-S212 |
| Enrollment and renewal stay HTTP | Both stay HTTP. Renewal is on the agent-held key: the agent sends only a public key (`RenewCertificateRequest.PublicKey`) and Nodes signs it; no private key or PKCS#12 bundle is created. | D-S211 |
| HTTP heartbeat stays during migration | `POST /agents/{nodeId}/heartbeat` is unchanged; the agent heartbeats only over the hub. Retiring the endpoint is a follow-up. | D-S28 |

### Routes

| Purpose | Gateway route (client sees) | Nodes route (service maps) | Gateway policy and limiter |
| --- | --- | --- | --- |
| Hub | `/hubs/agent/{**catch-all}` (`agent-hub-route`) | `/hubs/agent` | `AgentCertificate`, `PerAgent`, forwards the client certificate |
| Enroll | `POST /api/v1/agents/enroll` (`agents-enroll-route`) | `/agents/enroll` | `Anonymous`, `Auth` |
| Renew and HTTP heartbeat | `/api/v1/agents/{**catch-all}` (`agents-route`) | `/agents/{nodeId}/certificates/renew`, `/agents/{nodeId}/heartbeat` | `AgentCertificate`, `PerAgent`, forwards the client certificate |

The `/api/v1` prefix is removed by `PathRemovePrefix` on the two agent HTTP routes and is absent from the hub route. This is the DV-1 prefix seam; `GatewayAgentHubSeamTests` and `GatewayAgentRenewalSeamTests` pin it.

### Certificate forwarding and the trust model

The Decision left client-certificate forwarding open ("client-cert forwarding is not [configured]"). It is built as follows.

- **Forwarding (D-S25).** The Gateway sends the TLS client certificate to Nodes in `X-Client-Cert` (base64 DER). It strips any incoming `X-Client-Cert` on every route and sets it only on routes whose metadata says `ForwardClientCertificate=true`. Nodes accepts the header only when `Mtls:ForwardedCertificate:Enabled` is set, the remote address is inside `Mtls:ForwardedCertificate:TrustedProxies`, the header appears exactly once, the connection has no TLS client certificate of its own, and the value parses as base64 DER. Nodes stays the only validator of the certificate.
- **Trust model (D-S210 (A), owner ruling 2026-10-27).** Agents reach the Gateway's own Kestrel directly on a DNS-only (not Cloudflare-proxied) hostname. The Gateway accepts any client certificate at TLS (`AgentIngress:ClientCertificateMode=AllowCertificate`; the default is `NoCertificate`) and forwards it. Nodes trusts the header only from the Gateway's network (`TrustedProxies` set to the Gateway pod CIDR). There is no shared secret and no internal mTLS. The production Helm values for this are **not built** (follow-up below).
- **Gateway policy (D-S26).** The `AgentCertificate` policy requires a client certificate whose issuer CN equals `AgentCertificateProfile.CaCommonName`. `PerAgent` partitions by client IP, never by certificate thumbprint, because the Gateway cannot validate the certificate.

### Renewal and the grace window

Renewal is `POST /agents/{nodeId}/certificates/renew`. On renewal the presented certificate is marked revoked with `GraceUntil = now + Mtls:RenewalGraceMinutes` (default 60, range 0 to 1440; 0 revokes at once), so an agent whose renewal response was lost can renew again with the certificate it still holds (D-S212). `AgentCertificateState.IsUsable` is the only implementation of the usable rule; certificate validation, the hub's connect and heartbeat checks, and the renew endpoint all use it. A renewal from a certificate in grace does not extend `GraceUntil` and hard-revokes the node's other certificates, including the orphan the lost response left behind.

### Scale-out

The Redis backplane is **still deferred**. Nodes runs as a single replica for beta; cookie session affinity is configured on the `nodes` cluster and the agent's `HubConnection` keeps cookies, so negotiate and connect reach the same replica if Nodes ever scales (D-S217).

### Consequences check

| Consequence the Decision predicted | Outcome |
| --- | --- |
| "mTLS over WebSockets needs explicit verification behind the Gateway" | Verified: client-certificate forwarding is built, and the Windows UAT step `S-hub-ws` passed in all three recorded runs (`hubTransport=WebSockets` through the Gateway). The CI seam tests use the long-polling transport. See [PROJECT-STATE](../PROJECT-STATE.md#status-update-2026-10-27-sprint-2-the-agent-talks-to-nodes). |
| "New security-critical server surface in Nodes that must be built and tested" | Built, with seam tests S1 (`AgentHubSeamTests`) and S2 (`GatewayAgentHubSeamTests`) in addition to the unit tests. |
| "Scale-out later requires a SignalR backplane" | Unchanged; deferred (D-S217). |
| "The agent's `CommandReceived` event and dispatcher wiring still need a bootstrap hosted service" | The bootstrap service is built (`AgentBootstrapService`, #101, seam S4). Command handlers and signing are not (#118, #94). |

### Still open

- Command handlers (#118), command signing (#94), and the `CommandResult` and `Telemetry` server methods (D-S29).
- Retire the HTTP heartbeat endpoint (D-S28).
- The production ingress values for D-S210 (A) in `deploy/kubernetes/helm`, including a check that `AllowCertificate` shows no certificate picker to browser users on the agent hostname.
- The Redis backplane before Nodes runs more than one replica.
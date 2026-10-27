# P10 ruling 1: security note S-N2 (per-handshake hub certificate)

Inputs: `.claude/reports/sprint2-agent-nodes/P10-security-1.md` (N2), `worknotes/P10-fix.md` (in the P10 worktree), P10/P11/P12 specs, D-S211, D-S212, D-S216.

## Ruling

**(a) Rejected as a security finding, recorded as decision D-S231.** P10 adds one seam test and one code comment, and P12 gets two test amendments. No owner decision is needed.

## Why

What the code does now, checked in the P10 worktree:

| Fact | Evidence |
| --- | --- |
| Every `ConnectAsync` disposes the old `HubConnection`, reads the store again and builds a new connection with that certificate. | `src/Agents/Dhadgar.Agent.Core/Communication/ControlPlaneClient.cs:82-124` |
| The certificate is pinned only for one `HubConnection`. `WithAutomaticReconnect` reuses it, and `RetryPolicy.NextRetryDelay` never returns null, so an automatic reconnect never gives up by itself. | `ControlPlaneClient.cs:115`, `:118`, `:538-555` |
| `ConnectAsync` returns early only for `Connected`/`Connecting`. A client in `Reconnecting` or `Failed` is rebuilt from the store. | `ControlPlaneClient.cs:60` |
| Nodes enforces the usable predicate at negotiate and on every heartbeat. A connection whose certificate has left grace gets 401, or a heartbeat that is not acknowledged. | `tests/Dhadgar.Nodes.Tests/Integration/AgentRenewalSeamTests.cs:121-132`; D-S212, wave1-ruling-1 |
| The seam harness passes the pinned `ClientCertificates` to the server, so a hub test sees the certificate `ConnectAsync` chose. | `AgentHubSeamTests.cs:233-256` (`CertificateInjectingHandlerFactory`) |

1. **No security hole.** Access control is on the server. A stale certificate in a handshake is either still in grace, which D-S212 allows, or refused. N2's scenario is a liveness problem: after grace, a node retries with a refused certificate until something rebuilds the connection.
2. **The liveness problem is closed by rebuilding the connection, not by a callback.** P12's bootstrap disconnects after every successful renewal, and its loop calls `ConnectAsync` whenever `State != Connected`, which includes `Reconnecting`. That rebuild reads the renewed certificate. The window is at most one `ConnectionCheckIntervalSeconds` (60 s), well inside the 60-minute grace. If the process dies between renewal and reconnect, startup connects from the store.
3. **N2's fix is not available, and it would be worse if it were.** The fixer reports that the WebSockets transport takes a fixed `ClientCertificates` list and does not use the `SocketsHttpHandler` selection callback. I did not verify transport internals for the pinned SignalR client version; the ruling does not depend on that check. Even where a callback could reach the transport, binding the hub to `provider.Current` would let `Invalidate()` (S-N1's one-generation retired slot) dispose a certificate that a long-lived hub connection still holds. The hub owning its own store copy (`_clientCertificate`, disposed on the next connect) is the cleaner ownership model.
4. The remaining risk is a renewal path that does not disconnect. The tests below pin the disconnect and the rebuild, so removing either goes red.

## What P10 must do now (fix pass 2, same worktree and branch)

1. **Seam test**, direct (D-S216): add `RealAgent_HubReconnectAfterRenewal_PresentsRenewedCertificate` to `tests/Dhadgar.Nodes.Tests/Integration/AgentRenewalSeamTests.cs`, reusing that file's helpers (`CreateFactoryWithRealCa` with `GraceSettings`, `EnrollAgentAsync`, `CreateRenewingAgent`, `CreateAgentClient`, `ConnectAgentAsync`, `SendAgentHeartbeatAsync`):

   ```csharp
   // enroll → store; client = CreateAgentClient(factory, enrolled, store, meter, activity); await ConnectAgentAsync(client);
   // renew through CreateRenewingAgent(..., store, enrolled, _baseFactory.TimeProvider) → Assert success
   // await client.DisconnectAsync(); await ConnectAgentAsync(client);      // same ControlPlaneClient instance
   // _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(61));         // old certificate leaves grace
   // var ack = await client.SendHeartbeatAsync(...);  Assert.True(ack.IsSuccess && ack.Value.Acknowledged);
   ```

   Revert check: if `ConnectAsync` reused the certificate from its first connect (for example, kept `_clientCertificate` when it is not null), the heartbeat after grace is not acknowledged, the same way the old-certificate client fails at :127-132.
2. **Comment** at `ControlPlaneClient.cs:115`, above `options.ClientCertificates = [clientCert];`: `// D-S231: one certificate per HubConnection (the WebSockets transport takes a fixed list). A renewed certificate is presented when ConnectAsync rebuilds the connection; AgentBootstrapService disconnects after renewal.` Make no other change to `ControlPlaneClient`. N2's suggested move to `IClientCertificateProvider` is rejected.
3. **Docs:** add one sentence to the renewal section of `src/Agents/Dhadgar.Agent.Core/README.md`, which P10 already owes: "HTTP calls (`ControlPlaneMtls`) pick the current certificate on each handshake; the hub keeps one certificate per connection and picks up a renewed one when it reconnects (D-S231)."
4. **Report:** list S-N2 under `## Not fixed` as "Rejected by P10-ruling-1 (D-S231); verified by `RealAgent_HubReconnectAfterRenewal_PresentsRenewedCertificate`".
5. Checks are unchanged (P10's list). The new test runs in `Dhadgar.Nodes.Tests`.

Security pass 2 should accept N2 as closed by this ruling. It should check only that the test exists and passes, and that the comment and README sentence are present.

## Added to P12 (edited in `work-packets.md`)

- The renewal block in step 3 carries a D-S231 comment on the disconnect.
- `RenewalDue_RenewsThenReconnects` now asserts the order `RenewCertificateAsync` → `DisconnectAsync` → `ConnectAsync` with `Received.InOrder`.
- New `ClientReconnecting_IsRebuiltByConnectAsync`: with `State == Reconnecting`, the loop calls `ConnectAsync` within one check interval.

P11 is unchanged. It edits `ControlPlaneClient` only for identity reads (:353, :363) and must leave the per-connect certificate read in place.

## Owner decisions

None.

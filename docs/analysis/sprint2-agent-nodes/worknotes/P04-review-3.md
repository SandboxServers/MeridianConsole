# P04 review, pass 3 (fbe1a19)

## Verdict

ship

## Previous notes

- N1 (certificate-expiry tests): applied. `tests/Dhadgar.Nodes.Tests/Hubs/AgentHubCertificateExpiryTests.cs` has the four facts. Each builds `AgentHub` directly with its own `FakeTimeProvider`. Fact 1 asserts not acknowledged, `HeartbeatFailed`, `Abort()` received once, and `ProcessHeartbeatAsync` not called. Fact 2 is the before-NotAfter control. Fact 3 covers `AllowExpiredCertificates = true`. Fact 4 covers the missing claim. Flipping `>` at AgentHub.cs:116 makes facts 1 and 2 fail, and deleting the block makes fact 1 fail. The coder reports running the `>` to `<` mutation (2 failed). The three deviations (helper name `CreateHubContext`, the extra `Dhadgar.Nodes.Models` using, `Task.FromResult(ServiceResult.Ok(true))`) do not change behaviour.
- The pass 2 to pass 3 diff touches only that test file. Nothing in `src/` changed.
- Tests: `AgentHub|AgentHeartbeatValidator` filter gives 36 passed, 2 failed. The two failures are `AgentHubSeamTests` `Heartbeat_UpdatesCertificateNode_NotPayloadNode` and `RealAgent_ConnectsWithEnrolledCertificate_AndHeartbeatUpdatesNodeHealth`, the seam tests carried red until P05 (the real agent still invokes `Heartbeat` with its old payload). This is the same as pass 2.

## Fix notes

None.

## Not fixed

- security-review label needed: `AgentHub` auth path, `MtlsMiddleware` claims, certificate revocation and expiry handling (mTLS and agent-adjacent area).
- Accepted: a decommissioned or unknown node gets its ack and the connection stays open. Whether the server closes it afterwards is for P05, where the agent's reaction to `node_decommissioned` is defined.
- Accepted: the revoked-at-connect test asserts refusal, not HTTP 401. `MtlsMiddleware` does not check revocation.
- Decision, owner or P05: `AgentHubSeamTests` 1 and 4 stay red until P05 changes the agent's heartbeat to `AgentHeartbeat`. A status doc must not say "confirmed working" for the agent-to-hub path until they pass.
- Pre-existing, not P04: `CertificateValidationService.cs:149-150` compares `NotAfter` / `NotBefore` (Kind `Local`) with `now` (Kind `Utc`) using DateTime operators, so expiry is off by the local offset on non-UTC hosts. Needs a separate fix.
- Carried (security, orchestrator decision): the CA validation does not bind the SPIFFE node ID to a stored thumbprint, and there is no per-connection heartbeat rate limit.
- Informational: `HeartbeatService.ProcessHeartbeatAsync` can still throw on database errors, which the hub surfaces as a `HubException`.

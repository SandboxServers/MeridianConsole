# P05 security review, pass 2 (range 4405e8f..bfa7f0e; fix commit bfa7f0e)

## Verdict

ship

Pass 1 N1 is fixed. No regressions in the connection path.

## Pass 1 note check

- N1 (control characters, surrogate split): fixed. `HeartbeatPayloadMapper.Sanitize` replaces every `char.IsControl` with a space, the same predicate `AgentHeartbeatValidator.HasControlCharacter` uses. It truncates to the same limits the validator enforces. It backs off one char when the cut would leave a high surrogate. Empty-after-sanitize issues are dropped before the cap. Applied to `AgentVersion` and every health issue. The seam test `MappedHeartbeat_WithControlCharactersAndSurrogates_PassesNodesValidator` runs the mapped output through `AgentHeartbeatValidator.IsValid`, so the two sides fail together if they disagree.
- Check of the new `catch (InvalidOperationException)` in `SendHeartbeatAsync` (ControlPlaneClient.cs:260): returns a fixed string, logs, and does not touch certificate validation or the connection. `ex.Message` goes to the activity status only; that is local SignalR text, not server-controlled. No finding.

## Fix notes

None.

## Not fixed

- Low, informational. `Sanitize` handles a surrogate pair split at the cut only. A lone surrogate elsewhere in an agent-supplied string (for example a warning built from a malformed exception message) passes the Nodes validator, which checks only `char.IsControl`. If the Nodes side then stores or serializes it, Npgsql's strict UTF-8 encoding or `JsonSerializer` could throw. That is a Nodes-side robustness issue and is not exploitable for code execution. Consider rejecting or replacing lone surrogates in `AgentHeartbeatValidator` and in the mapper in a later packet.
- Pre-existing (from pass 1, still true). `ControlPlaneClient.OnCommandReceived` trusts an unsigned `CommandEnvelope` (#94), and it skips the NodeId and OrganizationId checks when the options are unset. Nothing in `src/Agents` subscribes to `CommandReceived` or calls `ConnectAsync` yet. Decide before P11 or the commands packet that the handler fails closed when `NodeId` or `OrganizationId` is unset, and that signature verification is in place before dispatch.
- Pre-existing. `ControlPlane.Endpoint` is not checked against a trusted-host allowlist before the client certificate is presented. It comes from local config. Low.
- Informational. A hub `Acknowledged == false` for a revoked, expired or decommissioned certificate only logs a Warning on the agent. P11 should decide the agent behaviour.

security-review label needed

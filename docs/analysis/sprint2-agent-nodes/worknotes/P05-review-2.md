# P05 review, pass 2

Reviewed head `bfa7f0e` (range `4405e8f..bfa7f0e`). I ran `dotnet test tests/Dhadgar.Agent.Core.Tests` (16 pass) and `dotnet test tests/Dhadgar.Nodes.Tests --filter FullyQualifiedName~AgentHubSeamTests` (5 pass).

## Verdict

ship

## Fix notes

None.

Pass 1 notes checked against the code:

- Review N1 is applied correctly. `Sanitize` replaces `char.IsControl` characters with a space before truncating, for `AgentVersion` and every health issue. Both control-character tests assert the exact strings from the note.
- Review N2 is applied correctly. The fixture gives memory 10% and disk 10%, S1 asserts both values, and `Online` is still asserted and passes.
- Review N3 is applied correctly. The `InvalidOperationException` catch returns `[ControlPlane.NotConnected]`. The README already lists that code. The missing stopped-hub test is accepted: `_hubConnection` is private and only exists after a real `ConnectAsync`, so the coder's reason holds.
- Review N4 is applied correctly. `ToContract_ManyRunningProcesses_AreCappedAtMax` adds 10003 Running processes and fails without the cap. `ToContract_DiskAboveHundred_ClampsToHundred` is green, but removing only one of the two clamps leaves it green, because the per-disk clamp and the outer clamp cover each other. The outer `Clamp(diskUsage)` is therefore redundant, not a defect.
- Security N1 is applied correctly.
  - `Sanitize` backs off one character when the cut would end on a high surrogate.
  - Empty issues are dropped before `Take`.
  - `ToContract_SurrogatePairAtCut_IsNotSplit` fails without the surrogate guard.
  - The new seam test `MappedHeartbeat_WithControlCharactersAndSurrogates_PassesNodesValidator` runs the mapped heartbeat through `AgentHeartbeatValidator.IsValid`, so the two sides fail together if they disagree.

## Not fixed

- Security-review label needed: `IControlPlaneClient` interface change, `Dhadgar.Agent.Core` (an agent project), client-certificate mTLS handling in `ControlPlaneClient.ConnectAsync`.
- The coder skipped the mutation checks from review N2 and N4. The tests give the same result when read against the code, so this is not blocking.
- The items listed as not fixed in `P05-security-1.md` stay with later packets, as that report says. They are not P05 defects.
  - `OnCommandReceived` fails open when `NodeId` or `OrganizationId` is unset, and the command envelope is unsigned (#94). The owner is P11 or the commands packet. Nothing in `src/Agents` calls `ConnectAsync` or subscribes to `CommandReceived` yet.
  - `ControlPlane.Endpoint` is not checked against a trusted-host allowlist.
  - The agent keeps the connection open after `Acknowledged == false`. P11 decides what happens on a revoked certificate.
  - The `HubException` text goes onto the activity status.
- From pass 1, still open: `RecordHeartbeatSent()` is also called when the ack is false, and the Debug "Heartbeat sent" line is also logged for an unacknowledged ack. Both are cosmetic.

# P04 review fixes, pass 2

## Fix notes applied

- N1 (certificate-expiry branch of `AgentHub.IsCertificateCurrentAsync`): applied. New file `tests/Dhadgar.Nodes.Tests/Hubs/AgentHubCertificateExpiryTests.cs` (namespace `Dhadgar.Nodes.Tests.Hubs`) with four facts, each using its own `FakeTimeProvider` and a directly constructed `AgentHub`:
  1. `Heartbeat_CertificatePastNotAfter_IsRefusedAndAborted`
  2. `Heartbeat_CertificateBeforeNotAfter_IsAcknowledged`
  3. `Heartbeat_ExpiredCertificate_WithAllowExpired_IsAcknowledged`
  4. `Heartbeat_MissingNotAfterClaim_IsRefused`
- Mutation check: `timeProvider.GetUtcNow() > ...` at `src/Dhadgar.Nodes/Hubs/AgentHub.cs:116` flipped to `<`. Facts 1 and 2 failed (2 failed, 2 passed). The line was restored; `git diff` on `AgentHub.cs` is empty.

## Deviations from the notes

- Helper naming: the note's `CreateContext` for the hub context was named `CreateHubContext`, because the DB helper also uses `CreateContext` and the two would be confusing. The DB helper is still `CreateContext()`. No behaviour change.
- The note's usings list did not include `Dhadgar.Nodes.Models` (needed for `HeartbeatRequest` in `DidNotReceive`). It is added.
- The note's `Returns(...)` for the heartbeat service was written as `Task.FromResult(ServiceResult.Ok(true))`; same shape, avoids relying on NSubstitute's Task wrapping.

## Not applied

- None. Only N1 is a fix note in pass 2.

## Checks

- `dotnet build src\Dhadgar.Nodes -c Release -p:TreatWarningsAsErrors=true`: Build succeeded.
- `dotnet test tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName~AgentHubCertificateExpiryTests"`: Passed! - Failed: 0, Passed: 4, Total: 4.
- `dotnet test ... --filter "FullyQualifiedName~AgentHub|FullyQualifiedName~AgentHeartbeatValidator"`: Failed! - Failed: 2, Passed: 36, Total: 38. The two failures are `AgentHubSeamTests` 1 and 4 (`Heartbeat_UpdatesCertificateNode_NotPayloadNode`, `RealAgent_ConnectsWithEnrolledCertificate_AndHeartbeatUpdatesNodeHealth`), which the review lists as carried red until P05. The 30 previously passing tests plus the 4 new ones pass; 36 = 30 + 4 + 2 non-seam tests in the filter.

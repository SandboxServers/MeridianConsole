# P05 review fixes, pass 1

Applied `P05-review-1.md` N1 to N4 and `P05-security-1.md` N1.

## Files changed

- src/Agents/Dhadgar.Agent.Core/Health/HeartbeatPayloadMapper.cs
- src/Agents/Dhadgar.Agent.Core/Communication/ControlPlaneClient.cs
- tests/Dhadgar.Agent.Core.Tests/Health/HeartbeatPayloadMapperTests.cs
- tests/Dhadgar.Nodes.Tests/Integration/AgentHubSeamTests.cs

## Fix notes applied

- Review N1: `Truncate` is replaced by `Sanitize`, which turns each control character into a space before truncating. It is applied to `AgentVersion` and each health issue. Tests `ToContract_ControlCharactersInWarning_AreReplaced` and `ToContract_ControlCharactersInVersion_AreReplaced` added.
- Security N1: `Sanitize` also avoids leaving a lone high surrogate at the cut. Health issues that are empty after sanitizing are dropped before the cap is applied. Tests `ToContract_SurrogatePairAtCut_IsNotSplit` added. Pure seam test `MappedHeartbeat_WithControlCharactersAndSurrogates_PassesNodesValidator` added to `AgentHubSeamTests.cs`, which runs each mapped value through `AgentHeartbeatValidator.IsValid`.
- Review N2: fixture changed to `AvailableMemoryBytes = 900` and disk `AvailableBytes = 90`. Asserts added for `MemoryUsagePercent == 10.0` and `DiskUsagePercent == 10.0`. `Online` still asserted and passes.
- Review N3: `catch (InvalidOperationException)` added to `SendHeartbeatAsync`, returning `[ControlPlane.NotConnected]`. The README line 301 already lists `[ControlPlane.NotConnected]`, so no README edit was needed.
- Review N4: tests `ToContract_ManyRunningProcesses_AreCappedAtMax` and `ToContract_DiskAboveHundred_ClampsToHundred` added.

## Deviations

- Review N3 test not added. `_hubConnection` is private and is created only inside `ConnectAsync` against a real endpoint, so a stopped-hub test cannot be arranged without a new seam. The `State != Connected` guard at the top of the method still returns `[ControlPlane.NotConnected]` deterministically. The new catch covers only the race after that check.
- The "temporarily revert the mapper, S1 must fail" mutation checks in review N2 and N4 were not run.

## Checks

- `dotnet build src/Agents/Dhadgar.Agent.Core -c Release -p:TreatWarningsAsErrors=true`: Build succeeded. 0 errors.
- `dotnet test tests/Dhadgar.Agent.Core.Tests --no-restore`: Passed! - Failed: 0, Passed: 16, Skipped: 0, Total: 16.
- `dotnet build tests/Dhadgar.Nodes.Tests -c Release -p:TreatWarningsAsErrors=true`: Build succeeded.
- `dotnet test tests/Dhadgar.Nodes.Tests --no-restore --filter FullyQualifiedName~AgentHubSeamTests`: Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5.

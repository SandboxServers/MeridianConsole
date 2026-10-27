# P04 coder report: Nodes AgentHub and the heartbeat bridge

Branch `s2/p04-nodes-agent-hub`, base `82f8d74`.

## Files changed

- `src/Dhadgar.Nodes/Hubs/AgentHub.cs` (new): `AgentHub` on `Hub<IAgentHubClient>, IAgentHub`. Protocol-version check, `node_id` claim check, node group, `Heartbeat` mapped to `IHeartbeatService` with business errors in `AgentHeartbeatAck.ErrorCode`.
- `src/Dhadgar.Nodes/Hubs/AgentHeartbeatValidator.cs` (new): `IsValid(AgentHeartbeat?)` with the C1 limits.
- `src/Dhadgar.Nodes/Hubs/AgentHeartbeatMapper.cs` (new): field copy to `Models.HeartbeatRequest`.
- `src/Dhadgar.Nodes/Program.cs`: `AddSignalR` options (64 KiB, no detailed errors), `MapHub<AgentHub>(AgentHubProtocol.Path).RequireAuthorization("AgentPolicy")` after `AgentEndpoints.Map(app)`, and the two `using`s.
- `tests/Dhadgar.Nodes.Tests/Hubs/AgentHubTests.cs` (new): five tests as named in the spec.

No csproj change. `Microsoft.AspNetCore.SignalR.Client` was already visible to the test project through `Dhadgar.Agent.Core`, so no `PackageReference` was added.

## Checks

1. `dotnet build src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true`: Build succeeded. 0 Warning(s), 0 Error(s).
2. `dotnet test tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName!~AgentHubSeamTests"`: Passed! Failed: 0, Passed: 415, Skipped: 0, Total: 415.
   - One earlier run failed `Heartbeat_Valid_AcknowledgesAndUpdatesHealth` in `StartAsync` (cold start, right after the first Release build). It passed in three isolated reruns and in the full rerun. Not reproduced after that.
3. `dotnet test ... --filter "FullyQualifiedName~AgentHubSeamTests"` (informational): Failed! Failed: 2, Passed: 2, Skipped: 0, Total: 4.
   - Passing: `Negotiate_WithoutCertificate_Returns401ProblemJson` and `Negotiate_WithForeignCaCertificate_Returns401`.
   - Failing, as expected until P05: `RealAgent_ConnectsWithEnrolledCertificate_AndHeartbeatUpdatesNodeHealth` and `Heartbeat_UpdatesCertificateNode_NotPayloadNode`. Both fail with `HubException: Failed to invoke 'Heartbeat' due to an error on the server.` The server hides the detail (`EnableDetailedErrors = false`). This is the agent's non-typed payload failing to bind to `AgentHeartbeat`.

## Deviations

- The revert check (negotiate returns 404 without the hub, and out-of-range writes without the validator) was not run as a separate experiment. The `CpuOver100` test asserts the DB is unchanged, which covers the validator path.
- Commit trailer uses `Co-Authored-By: Claude Sonnet 5.5`, as the brief specifies. The session-level attribution reminder names Haiku 5.5; the brief's line was used.

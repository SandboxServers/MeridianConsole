# P02 coder report: red agent hub seam test (S1) and hub transport hook

Branch `s2/p02-hub-seam-red`. Commit `167bad2`. Result: done (seam tests red as specified).

## Files changed

- `src/Agents/Dhadgar.Agent.Core/Communication/ControlPlaneHubTransport.cs` (new, C7 shape, with XML docs noting DI/constructor-only use)
- `src/Agents/Dhadgar.Agent.Core/Communication/ControlPlaneClient.cs` (optional last ctor parameter `transport`; `WithUrl` sets `Transports` and `HttpMessageHandlerFactory` when present)
- `src/Agents/Dhadgar.Agent.Core/Hosting/AgentServiceCollectionExtensions.cs` (`TryAddSingleton(new ControlPlaneHubTransport())`)
- `tests/Dhadgar.Nodes.Tests/Integration/AgentSeam/AgentSeamHarness.cs` (new: `CreateFactoryWithRealCa` with `enableMtls`/`configureMtls`, `CreateEnrollmentTokenAsync`, `CreateEnrollmentService`, `EnrollAgentAsync`, `CertificateInjectingHandlerFactory`)
- `tests/Dhadgar.Nodes.Tests/Integration/AgentSeam/TestServerClientFactory.cs` (new: `TestServerClientFactory`, `StripApiPrefixHandler`, both public)
- `tests/Dhadgar.Nodes.Tests/Integration/AgentSeam/InMemoryCertificateStore.cs` (new, moved and extended: `GetClientCertificate()` returns a cert with its key, PKCS#8 with SEC1 fallback; `PrivateKey` kept)
- `tests/Dhadgar.Nodes.Tests/Integration/AgentSeam/ForeignCa.cs` (new: self-signed CA with CN `AgentCertificateProfile.CaCommonName`, correct SPIFFE SAN, `CN={nodeId}`, client-auth EKU)
- `tests/Dhadgar.Nodes.Tests/Integration/AgentEnrollmentContractTests.cs` (uses the harness; assertions unchanged)
- `tests/Dhadgar.Nodes.Tests/Integration/AgentHubSeamTests.cs` (new: 4 seam tests)

## Checks

1. `dotnet build src\Agents\Dhadgar.Agent.Core\Dhadgar.Agent.Core.csproj -c Release -p:TreatWarningsAsErrors=true`: Build succeeded (0 errors; pre-existing IL2026 trim warnings are printed, not errors).
2. `dotnet build tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`: Build succeeded.
3. `dotnet test tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName!~AgentHubSeamTests"`: Passed! Failed: 0, Passed: 391, Skipped: 0, Total: 391.
4. `dotnet test ... --filter "FullyQualifiedName~AgentHubSeamTests"`: Failed! Failed: 4, Passed: 0, Total: 4 (expected red).

Red test reasons (first line of each message):

- `RealAgent_ConnectsWithEnrolledCertificate_AndHeartbeatUpdatesNodeHealth`: `System.Net.Http.HttpRequestException : Response status code does not indicate success: 404 (Not Found).` (negotiate 404)
- `Heartbeat_UpdatesCertificateNode_NotPayloadNode`: `System.Net.Http.HttpRequestException : Response status code does not indicate success: 404 (Not Found).` (negotiate 404)
- `Negotiate_WithoutCertificate_Returns401ProblemJson`: `Assert.Equal() Failure: Values differ`, Expected `Unauthorized`, Actual `NotFound`.
- `Negotiate_WithForeignCaCertificate_Returns401`: `Assert.Equal() Failure: Values differ`, Expected `Unauthorized`, Actual `NotFound`.

## Deviations and choices

- Harness approach: `TestServer.CreateHandler(Action<HttpContext>)` exists in the referenced TestHost 10.x (checked its XML doc in the NuGet cache), so the spec's `IStartupFilter` fallback was not needed.
- Time advance: tests call `_baseFactory.TimeProvider.Advance(...)`, not `factory.TimeProvider`. `factory` is a `WebApplicationFactory<Program>` returned by `WithWebHostBuilder`, which has no `TimeProvider`. The derived factory reuses the base's `FakeTimeProvider` instance.
- Seeding node B (test 4): seeded through the derived factory's `NodesDbContext` in a private helper, not `factory.SeedNodeAsync`, for the same type reason.
- `EnrollAgentAsync` returns the unwrapped `EnrollmentResult` (it asserts success inside), so `r.NodeId` works as the spec's test body implies.
- `AgentEnrollmentContractTests` calls `CreateFactoryWithRealCa(..., enableMtls: false)`, which leaves its behaviour unchanged.
- `ControlPlaneClient` still builds the endpoint from the literal `"/hubs/agent"`. Switching to `AgentHubProtocol.Path` was not part of P02 and is left for P05.
- Commit trailer uses the brief's `Co-Authored-By: Claude Sonnet 5.5` line, not the packet's Opus line, as the brief instructs.

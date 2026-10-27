# P01 coder report: Contracts for the hub, heartbeat and certificate profile

Status: done. Commit `fea4dc9a989d9e31ab01cfdd88d10cbd2ddbd61d` on branch `s2/p01-contracts` (local, not pushed).

## Files changed

- `src/Shared/Dhadgar.Contracts/Nodes/AgentHubContracts.cs` (new): C1, exactly as specified.
- `src/Shared/Dhadgar.Contracts/Nodes/AgentCertificateProfile.cs` (new): C2, exactly as specified.
- `src/Dhadgar.Nodes/Services/CertificateAuthorityService.cs`: the private SPIFFE format string, `OrganizationName`, `CaCommonName` and `AgentCertificateValidityDays` are removed. Issuance now uses `AgentCertificateProfile.SpiffeId(nodeId)`, `.Organization`, `.CaCommonName` and `.ValidityDays`. Unused `System.Globalization` and `System.Text` usings removed, and `using Dhadgar.Contracts.Nodes;` added.
- `src/Dhadgar.Nodes/Auth/MtlsOptions.cs`: `SpiffeTrustDomain` default is `AgentCertificateProfile.SpiffeTrustDomain`; `using Dhadgar.Contracts.Nodes;` added.
- `tests/Dhadgar.Contracts.Tests/Nodes/AgentHubContractsTests.cs` (new): 4 tests (heartbeat wire JSON and round-trip, ack wire JSON and round-trip, `SpiffeId_FormatsNodeIdWithHyphens`, `NodeGroup_IsStable`).
- `tests/Dhadgar.Nodes.Tests/CertificateAuthorityPublicKeyIssuanceTests.cs`: added `IssuedCertificate_MatchesSharedProfile`, plus `using Dhadgar.Contracts.Nodes;`.

## Checks

1. `dotnet build src\Shared\Dhadgar.Contracts\Dhadgar.Contracts.csproj -c Release -p:TreatWarningsAsErrors=true`: Build succeeded. 0 Warning(s), 0 Error(s).
2. `dotnet build src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true`: Build succeeded. 0 Warning(s), 0 Error(s).
3. `dotnet test tests\Dhadgar.Contracts.Tests\Dhadgar.Contracts.Tests.csproj`: Passed! Failed: 0, Passed: 5, Skipped: 0, Total: 5.
4. `dotnet test tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj`: Passed! Failed: 0, Passed: 391, Skipped: 0, Total: 391.

Check 4 printed IL2026 trim warnings from `Dhadgar.Agent.Core` (pre-existing, not in any file this packet touches). The Nodes and Contracts projects built clean under TreatWarningsAsErrors.

## Deviations

- `AgentHubContractsTests.cs` has `using Xunit;`. The Contracts test project has no global Xunit using, so the build failed without it. This is a missing using, not a spec change.
- Test namespace is `Dhadgar.Contracts.Tests.Nodes`.
- The heartbeat round-trip test asserts individual properties, not record equality. Record equality on `IReadOnlyList` compares by reference, so it would always fail.
- Per the Contracts CLAUDE.md, no logic is in the new contracts beyond the spec's `SpiffeId` / `NodeGroup` one-line helpers, which the spec itself requires.

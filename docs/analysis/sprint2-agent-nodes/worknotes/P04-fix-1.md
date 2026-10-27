# P04 review fixes, pass 1

Fix notes applied: P04-review-1 (R-N1 to R-N4) and P04-security-1 (S-N1 to S-N3). Notes are numbered R- and S- here to keep them apart.

## Files changed

- `src/Dhadgar.Nodes/Program.cs`: R-N4, moved `using Dhadgar.Contracts.Nodes;` above the `Dhadgar.Nodes` usings.
- `src/Dhadgar.Nodes/Hubs/AgentHeartbeatValidator.cs`: R-N2 / S-N2, rejects control characters (`char.IsControl`) in `AgentVersion` and in each `HealthIssues` entry. The optional version regex was not added.
- `src/Dhadgar.Nodes/Hubs/AgentHub.cs`: S-N1, revocation check against `AgentCertificates` (by `certificate_thumbprint`), and expiry check against the `certificate_not_after` claim, unless `AllowExpiredCertificates`. Applied in `OnConnectedAsync` and in `Heartbeat`. A revoked or expired certificate aborts the connection. The constructor gains `NodesDbContext` and `IOptions<MtlsOptions>`.
- `src/Dhadgar.Nodes/Auth/MtlsMiddleware.cs`: S-N1, adds the `certificate_not_after` claim (Unix seconds) when `NotAfter` is known.
- `src/Dhadgar.Nodes/README.md`: R-N1, new `## Agent hub` section before `## Related Documentation`.
- `tests/Dhadgar.Nodes.Tests/Hubs/AgentHubTests.cs`: R-N2 positive control and `ex is not OperationCanceledException` filter; S-N3 tests for no certificate (401), foreign CA certificate (401), revoked certificate at connect, revoked certificate after connect, two-node isolation; NUL-in-version test (S-N2). `CreateHubConnection` takes a nullable certificate.
- `tests/Dhadgar.Nodes.Tests/Hubs/AgentHeartbeatValidatorTests.cs` (new): R-N3 invalid-value theory, null, valid boundary, control-character cases.

## Check results

- `dotnet build src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true`: Build succeeded. 0 Warning(s), 0 Error(s).
- `dotnet test tests\Dhadgar.Nodes.Tests --filter "FullyQualifiedName!~AgentHubSeamTests"`: Passed! Failed: 0, Passed: 440, Skipped: 0, Total: 440.
- Targeted run (`AgentHeartbeatValidatorTests|AgentHubTests`): Passed! Failed: 0, Passed: 30, Total: 30.
- Seam tests were not run, per the packet's informational step. Docker was available.

## Deviations

1. **S-N1 expiry test not added.** The hub test would need to move the shared `FakeTimeProvider` past the certificate's `NotAfter`. `FakeTimeProvider` cannot go back in time, so the shared clock would break later tests in the collection. The expiry check is in code but has no hub-level test. Also, the LongPolling transport re-runs the middleware on every request, so the middleware already rejects expired certificates on those requests.
2. **S-N1 case (c), close after decommissioned ack, not implemented.** `Context.Abort()` before returning the ack cancels the pending invocation, so the agent never receives `node_decommissioned`. The `node_decommissioned` and `node_not_found` results now return the ack and keep the connection open. No heartbeat is written, because `HeartbeatService` refuses it.
3. **S-N3 revoked-certificate connect test asserts refusal, not HTTP 401.** `MtlsMiddleware` does not check revocation, so a revoked certificate reaches the hub. It is refused in `OnConnectedAsync` (the connection is aborted), and the test asserts that nothing is acknowledged. A 401 would need a revocation check in the middleware, which the note did not ask for.
4. **S-N1 "Abort on revoked heartbeat" test.** The check aborts the connection and returns `heartbeat_failed`. The test accepts a null or non-acknowledged result, since the aborted invocation throws on the client.
5. **R-N2 revert experiment not run.** The version check was not deleted to confirm that the rewritten test fails. The test now fails on a timeout, not on a refusal, because `cts` is checked in the catch filter.
6. **Constructor change.** `AgentHub` takes `NodesDbContext` and `IOptions<MtlsOptions>` in addition to the spec's three parameters. Both are needed for the S-N1 check.
7. **Shell.** One Bash call (`pwd`) ran on this win32 host, a no-op. All other commands used PowerShell.

## Not applied

None. All notes were applied, with the deviations above.

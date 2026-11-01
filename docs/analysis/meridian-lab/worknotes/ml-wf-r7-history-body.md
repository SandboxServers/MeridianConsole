# ml-wf-r7-history-body (D-ML117, R7-1)

## What changed

- New `src/Tools/Dhadgar.Lab/Http/LabWireJson.cs`: `LabWireJson.Options` is `new(JsonSerializerDefaults.Web)`, the camelCase shape product endpoints bind. Tool arguments and results stay on `LabJson.Options`.
- `History/HistoryClient.cs` (enroll body, line 267): serializes `EnrollNodeRequest` with `LabWireJson.Options`. Was `LabJson.Options` (snake_case `public_key`), which the Nodes minimal API rejected with 400 `invalid_public_key`. The observation lines (`LabJson.Options` at 203 and 317) are unchanged.
- `Tools/Nodes/NodesTools.cs` (line 151): private `WireOptions` removed, uses `LabWireJson.Options`. Single-sourcing only.
- `Fuzz/FuzzTargets.cs` (line 72): private `JsonOptions` removed, uses `LabWireJson.Options`. Single-sourcing only.
- `tests/Dhadgar.Lab.Tests/History/HistoryClientTests.cs`: `Deserialize` uses a literal `new JsonSerializerOptions(JsonSerializerDefaults.Web)`. `Enroll_Body_IsTheSharedEnrollNodeRequest` asserts the raw body contains `"publicKey"`, not `"public_key"`, and `CpuCores > 0`. `FakeSink` moved out.
- `tests/Dhadgar.Lab.Tests/History/FakeObservationSink.cs` (new, internal): the shared sink.
- `tests/Dhadgar.Lab.Tests/Seams/HistoryEnrollSeamTests.cs` (new, S-L2h, `[Trait("Seam", "S-L2h")]`): `HistoryClient` with a lab-minted token against the in-process Nodes app. Asserts 201 and one node, then a replay with the same token gets 401.

## Product-body sites (grep of `src/Tools/Dhadgar.Lab`)

Grep for `SerializeToUtf8Bytes|SerializeToElement|PostAsJsonAsync|JsonContent.Create|ReadFromJsonAsync|Deserialize<`.

| Site | Body | Options |
|---|---|---|
| `History/HistoryClient.cs:267` | Product: `EnrollNodeRequest` (enroll POST) | `LabWireJson.Options` (fixed) |
| `Tools/Nodes/NodesTools.cs:151` | Product: `EnrollNodeRequest` (enroll probe) | `LabWireJson.Options` |
| `Fuzz/FuzzTargets.cs:72` | Product: `EnrollNodeRequest` (fuzz body) | `LabWireJson.Options` |

The spec's pre-edit line numbers were 266, 154 and 73. The sites are the same ones, shifted by the new using lines.

Not product bodies, left on `LabJson.Options`: `HistoryClient.cs:203, 317` (observation lines); `Tools/*` tool arguments and results (`LabTool.cs`, `FaultTools.cs`, `LabToolDispatcher.cs`, `LabMcpServer.cs`); `LabDb.cs`, `KindSupport.cs`, `ToxicFault.cs`, `PartitionFault.cs`, `SpecLoader.cs`, `MutationRunJob.cs`, `UatRunTool.cs`, `HistoryRunTool.cs`, `BundleWriter.cs`. Other `SerializeToElement` calls are internal payloads (invariant evidence, twin diffs, fault args). None of them send to a product endpoint. No `ReadFromJsonAsync` or `PostAsJsonAsync` in the lab.

## Revert check

Put `LabJson.Options` back at `HistoryClient.cs:267`, ran `HistoryEnrollSeamTests` and `HistoryClientTests`:

- `Enroll_HistoryClient_CreatedThenUnauthorizedOnReplay`: FAIL, `Expected: 201 Actual: 400`.
- `Enroll_Body_IsTheSharedEnrollNodeRequest`: FAIL, `Assert.Contains()` (`"publicKey"` missing).
- `NewKeyRetry_SendsADifferentSpki`: FAIL, the `Deserialize` of the snake_case body returned null.
- Result: `Failed!  - Failed: 3, Passed: 5, Total: 8`.

The fix was restored after the revert check. The 0-node count is not asserted in the failing run, because the test stops at the 201 assertion.

## Seam endpoint deviation

The spec built the endpoint as `new Uri(client.BaseAddress!, "/agents/enroll")`. The in-memory TestServer base address is not a lab loopback URI, so `HistoryClient` refused it with `[Lab.Untrusted] the enroll URI is not a lab endpoint`. The test now uses `http://127.0.0.1:{LabPorts.Barrier}/agents/enroll`, the same form as `HistoryClientTests`. The in-memory handler ignores the host, so the request still reaches the Nodes app.

## Checks

- Non-Docker suite: `dotnet test tests\Dhadgar.Lab.Tests -c Release --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"`: `Passed!  - Failed: 0, Passed: 1437, Skipped: 5, Total: 1442`.
- Targeted rerun on the restored code, FullyQualifiedName~HistoryEnrollSeamTests|FullyQualifiedName~HistoryClientTests: Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8.

## Left

Nothing in this packet.

# L09 ruling 2: CA certificate through the Gateway returns 401

Architect ruling, 2026-10-27. Trigger: after L09f (#237), `ServicesDockerTests` gets Nodes and Gateway ready, then fails on `GET http://127.0.0.1:15000/api/v1/agents/ca-certificate` → 401 (`.claude/reports/meridian-lab/L09-coder.md` in `ml-l09`).

## Verified facts

| # | Fact | Evidence |
| --- | --- | --- |
| 1 | The CA certificate is meant to be anonymous. Nodes maps `GET /agents/ca-certificate` with `.AllowAnonymous()`, and `/ca-certificate` is a `MtlsOptions.ExemptPaths` entry. The repo README lists it as anonymous. | `src/Dhadgar.Nodes/Endpoints/AgentEndpoints.cs:52-57`; `src/Dhadgar.Nodes/Auth/MtlsOptions.cs:84`; `README.md:578` |
| 2 | The Gateway has no anonymous route for it. `/api/v1/agents/ca-certificate` falls into `agents-route` (`/api/v1/agents/{**catch-all}`, Order 30, `AgentCertificate`). Only `agents-enroll-route` (exact `/api/v1/agents/enroll`, Order 25) is `Anonymous`. A request with no client certificate gets 401 at the Gateway and is never forwarded. | `src/Dhadgar.Gateway/appsettings.json:187-207` |
| 3 | So the 401 is a real product defect, and it is already known and owned. Vertical-slice F-04 records it. Packet **VS-07R** (vertical-slice wave 2, security review yes, status `BlockedDependency`) adds `agents-ca-route` (exact match, `Anonymous`, Order 22) and the seam `GatewayAgentIngressSeamTests.CaCertificate_IsFetchableAnonymously`. The older `AgentIngressThroughGatewaySeamTests.CaCertificate_IsFetchableAnonymously` is skipped with "Pending VS-07 (F-04)". | `docs/analysis/vertical-slice/README.md` F-04 and packet table; `docs/analysis/vertical-slice/work-packets.md` VS-07R (C-1 route table); `tests/Dhadgar.Seams.Tests/AgentIngress/AgentIngressThroughGatewaySeamTests.cs:11` |
| 4 | The defect does not block the lab's enroll path. Agents get the CA in the enrollment response (`EnrollNodeResponse.CaCertificate`), and enrollment goes through the anonymous `agents-enroll-route`. S-L2 (L11) and the L17 rows `enroll-gateway` and `enroll-ingress` use that route. | `AgentEndpoints.cs:81-88`; `appsettings.json:187-196`; `work-packets.md` L17 item 2 |
| 5 | Four lab specs assumed an anonymous CA route through the Gateway: the L09 acceptance criterion (item 8), the L17 `gateway-up` row, the S-L7 latency probe in L19, and the `gw-nodes-down` row in L23. This is a planning defect, like ruling 1 fact 8. | `work-packets.md:1577`, `:1843`, `:1992`, `:2140` |
| 6 | Ruling 1's acceptance line ("200 on 15000 and 25000") was wrong too. Both ports reach the Gateway: 15000 is `LabPorts.Gateway` and 25000 is Toxiproxy `agent-ingress` → Gateway. Nodes' direct port is 15040. | `src/Tools/Dhadgar.Lab/LabPorts.cs:9,12,17`; `ProxyCatalog.cs:39` |
| 7 | `POST /api/v1/agents/enroll` with body `{}` is forwarded to Nodes, which answers 400 ProblemDetails with error code `invalid_platform`. Nodes has no validation filter, the record binds with null members, and the platform check runs first. The Gateway itself never produces `invalid_platform`, so that body proves the request went Gateway → `gateway-nodes` → Nodes. Each call writes one `EnrollmentFailed` audit row. | `AgentEndpoints.cs:24-30,60-76`; `EnrollmentService.cs:54-70`; `ProblemDetailsHelper.cs:49` |

## Decision

**Option (b). Make no product change in meridian-lab.** The lab tests probe routes that are reachable through the Gateway today. The product fix stays with VS-07R in the vertical-slice campaign. A second copy of the fix in a lab packet would race VS-07R on the same `appsettings.json` routes and on `RouteConfigurationTests`, and it would skip VS-07R's security review. Record this as **D-ML63** with finding **F53** (facts 2, 3, 5).

The lab must not pin the defect either. No lab test asserts 401 on the CA route, because VS-07R will turn it into 200 and that would break the lab for no reason.

No owner question. L09 changes one lab test file and touches no security-review area (this is not an agent project, `IControlPlaneClient`, `CommandEnvelope`, a process manager or Gateway auth). `packet-reviewer` only.

## What changes in L09 (part 8, `ml-l09`, branch unchanged)

**File:** `tests/Dhadgar.Lab.Tests/Services/ServicesDockerTests.cs` only. No product file and no other lab file.

**Rules (inline):** no package versions and no new references; nullable on, warnings as errors; `using` on every `HttpResponseMessage` and `StringContent`; xUnit `[Fact]` with the existing `RequiresDocker` and `RequiresNetwork` traits; never log or assert on certificate bodies beyond the PEM marker; ports come from `LabPorts`, never literals.

1. Delete `CaCertificatePath` and `AssertCaCertificateAsync`. Rename the test `BuildUpStart_NodesAndGateway_ServeCaDirectAndForwardThroughIngress`, and update the class summary to match.
2. Replace the two asserts at lines 98-99 with:

   ```csharp
   // Nodes direct (15040): the CA endpoint is anonymous at Nodes.
   using (HttpResponseMessage ca = await http.GetAsync($"http://127.0.0.1:{LabPorts.Nodes}/agents/ca-certificate", ct))
   {
       Assert.Equal(HttpStatusCode.OK, ca.StatusCode);
       Assert.Contains("BEGIN CERTIFICATE", await ca.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
   }

   // Agent ingress (25000) reaches the Gateway.
   using (HttpResponseMessage ingress = await http.GetAsync($"http://127.0.0.1:{LabPorts.AgentIngress}/healthz", ct))
   {
       Assert.Equal(HttpStatusCode.OK, ingress.StatusCode);
   }

   // Gateway forwards to Nodes through gateway-nodes, via both host paths. Only Nodes emits invalid_platform.
   foreach (int port in new[] { LabPorts.Gateway, LabPorts.AgentIngress })
   {
       using var body = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
       using HttpResponseMessage enroll = await http.PostAsync($"http://127.0.0.1:{port}/api/v1/agents/enroll", body, ct);
       Assert.Equal(HttpStatusCode.BadRequest, enroll.StatusCode);
       Assert.Contains("invalid_platform", await enroll.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
   }
   ```

3. **Why it fails when reverted:** with the old asserts the test is red on a product defect the lab does not own. With these asserts, if the lab wiring were wrong (Nodes not on 15040, `agent-ingress` not pointing at the Gateway, or `ReverseProxy__Clusters__nodes__Destinations__d1__Address` not pointing at `gateway-nodes`), the enroll probe would get 502/503 or a body without `invalid_platform`.
4. If the run shows a status or body other than the ones above, report `blocked` with the exact status and the first 200 characters of the body. Do not widen the file list.

**Checks (in order; `<W>` = the `ml-l09` worktree):**

```powershell
dotnet build <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~ServicesDockerTests"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ServicesDockerTests"   # Docker Desktop running, linux engine
git -C <W> status --porcelain   # no packages.lock.json change
```

The offline run may still show `NodesEnrollSeamTests.Enroll_MintedToken_ReturnsCreatedWithNodeId` red ("mint token", red by design until L11). Every other test is green.

**Commit subject:** `test(lab): probe Nodes CA directly and Gateway forwarding via enroll [L09]`.

**Docs owed:** none beyond L09's existing list. If `src/Tools/Dhadgar.Lab/README.md` describes the Docker test as "CA through the Gateway", change it to this ruling's wording.

**Worknote:** drop the stale "Gateway 400" diagnosis section from `worknotes/L09.md`. The coder report already says it no longer happens.

## Ledger changes (orchestrator applies in the campaign worktree)

- **Ruling 1's ledger changes have not landed.** `README.md` has neither F52 nor D-ML62, and there is no `L09f` packet row. Apply ruling 1's "Ledger changes" first, with L09f's status `Done (#237)`.
- README: add F53 (facts 2, 3, 5) and D-ML63 (this decision). Add an external dependency note: "Gateway CA route through the Gateway is vertical-slice VS-07R; the lab does not test or fix it."
- `work-packets.md` L09 item 8 (line 1577): replace the `ServicesDockerTests` clause with "`ServicesDockerTests` (`RequiresDocker`, slow): build, stack up, start Nodes and Gateway, both ready; Nodes `GET 15040/agents/ca-certificate` → 200 PEM; `GET 25000/healthz` → 200; `POST /api/v1/agents/enroll` `{}` via 15000 and 25000 → 400 `invalid_platform` (forwarded by the Gateway, ruling L09-2)."
- L17 item 2 (line 1843), row `gateway-up`: `http via=gateway` GET `/healthz` status 200 (was the CA route).
- L19, S-L7 (line 1992): the latency probe becomes "`POST /api/v1/agents/enroll` body `{}` via the Gateway's direct port takes at least 1000 ms and returns 400". Every other part of the bullet stays.
- L23 (line 2140), row `gw-nodes-down`: the probe becomes `POST /api/v1/agents/enroll` body `{}` via `gateway` → status `any [502, 503]`. The rest of the row stays. Add one sentence to L23: the Nodes invariant checks tolerate `EnrollmentFailed` audit rows with reason `invalid_platform` (fact 7).
- No packet owns the product fix inside meridian-lab. VS-07R owns it. When VS-07R merges, nothing in the lab has to change.

## Noted, not ruled

L23's `gw-nodes-down` expects Gateway `/readyz` not 200 after the disable. `YarpReadinessCheck` counts `AvailableDestinations`, and YARP counts destinations with unknown health as available. With the `nodes` cluster's 30 s active-check interval, the flip can take more than one interval. L23's spec author should set an explicit wait (for example, within 75 s) when L23 is next touched.

## Status

No BlockedDecision. L09 continues with part 8 from this note, using a fresh `packet-coder`.

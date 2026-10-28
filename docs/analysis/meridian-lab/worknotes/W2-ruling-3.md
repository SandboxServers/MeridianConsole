# Wave 2 ruling 3: doctor shape and checks, Gateway probes, ledger catch-up, registration areas

Architect ruling, 2026-10-27. Trigger: wave 2 review pass 1 (`.claude/reports/meridian-lab/wave2-review-1.md`) ended on `decision`. Not fixed: D1, D2, D3, Q1, M1; the doctor fix notes N2-N4. Inputs: `worknotes/L09-ruling-1.md`, `worknotes/L09-ruling-2.md`.

This note decides. It edits no source file. The ledger edits listed under "Ledger changes" are applied in the campaign worktree with this note, uncommitted.

## Verified facts

| # | Fact | Evidence |
| --- | --- | --- |
| 1 | `lab_doctor` returns `DoctorCheck(string Name, string Status, string Detail)`, with status `ok`, `warn`, `fail` or `info`. C4 says `{checks:[{name, ok, detail}]}`. No packet reads the result yet: L31 and L35 only add checks. | `src/Tools/Dhadgar.Lab/Tools/Doctor/LabDoctorTool.cs:17,22`; `work-packets.md` C4, L31 item 3, L35 item 1 |
| 2 | The tool ships four constant lines `"not checked in this packet"` (ports, docker-users, token, repo-root). The docker check builds its own `Process` without `--context` (R-DOCKER). | `LabDoctorTool.cs:62-66,93-137` |
| 3 | The token length is reachable only through the internal `EdgeState`, so the public tool cannot take it as a constructor parameter (CS0051). The daemon registers `edge` and `paths` as singletons. `LabPaths.RepoRoot` is empty when `Lab:RepoRoot` is unset. | `src/Tools/Dhadgar.Lab/Daemon/LabDaemon.cs:108,257-258`; `LabPaths.cs:35,56` |
| 4 | `POST /api/v1/agents/enroll` goes through `agents-enroll-route` (`Anonymous`, rate limiter `Auth`). The `Auth` limiter is a fixed window of 30 per 60 s per client IP (`RateLimiting:Policies:Auth:*`). `PerAgent` (the agent routes) is 500 per 60 s per client IP. All lab traffic reaches the Gateway from one container-network IP. | `src/Dhadgar.Gateway/appsettings.json:33-52,187-196`; `src/Dhadgar.Gateway/Program.cs:194-240` |
| 5 | The clause poll default is `poll_ms = 500`. A 30 s recovery poll on the enroll probe is up to 60 calls, twice the `Auth` budget, so the row would see 429 from the lab's own polling. | `work-packets.md` C7 (`timeout_ms`, `poll_ms`) |
| 6 | Enroll with body `{}` returns 400 with `invalid_platform` from Nodes (`AgentEndpoints.cs:72`, `EnrollmentService.cs:54-70`). Each call writes one `EnrollmentFailed` audit row. Only Nodes emits that code, so the body proves Gateway → `gateway-nodes` → Nodes. | ruling L09-2 fact 7 |
| 7 | `AddLabMcp` registers 24 tools in one list (`LabMcpServer.cs:48-88`). The DI test pins `Assert.Equal(24, ...)` (`LabDaemonServiceLifetimeTests.cs:47`). Every wave 2 branch pair that added tools conflicted on these two files (review M1). | review M1; the two files |
| 8 | `ToolPolicy.PlannedTools` holds 69 names. 9 of them are registered now (`stack_status`, `db_query`, `stack_up`, `stack_down`, `stack_reset_data`, `http_request`, `nodes_token_mint`, `nodes_enroll_probe`, `nodes_cert_time_shift`). The gate table holds 84 names: 84 − 24 = 60 planned. `PolicyTable_HasNoUnknownNames` only checks registered ∪ planned ⊇ gates, so the stale names pass. | `src/Tools/Dhadgar.Lab/Policy/ToolPolicy.cs:8-175` |
| 9 | S-L3 is green on the campaign tip and not vacuous. Sprint 2 put `WindowsCertificateStore.CaStoreName` on `main` before wave 2 (D-ML60). The registry key path and `EnrollmentTokenValueName` are not compared yet (N5). | review Q1, N5 |

## D1. The `lab_doctor` result shape

**C4 changes to the code's richer status, plus a top-level `ok`:**

```json
{ "ok": true, "checks": [ { "name": "docker", "status": "ok", "detail": "linux" } ] }
```

- `status` is one of `ok`, `warn`, `fail`, `info`.
  - `ok`: checked, fine.
  - `warn`: checked; the lab works, but something is degraded or unknown.
  - `fail`: checked; a lab action will fail.
  - `info`: a fact, not a verdict (OS name, dev ports in use, "not Windows").
- `info` never means "not checked". A check the packet does not implement is not emitted at all.
- The top-level `ok` is true iff no check has status `fail`.

The reason: informational lines (dev ports, the OS name, the build number off Windows) have no honest boolean. A bool `ok` with the severity hidden in `detail` (N3) would make consumers parse prose. Nothing reads the result yet (fact 1), so the change costs nothing downstream.

This replaces N3. Code shape for the L12 fix pass:

```csharp
public static class DoctorStatus
{
    public const string Ok = "ok";
    public const string Warn = "warn";
    public const string Fail = "fail";
    public const string Info = "info";
}

public sealed record DoctorCheck(string Name, string Status, string Detail);

public sealed record LabDoctorResult(bool Ok, IReadOnlyList<DoctorCheck> Checks)
{
    public static LabDoctorResult From(IReadOnlyList<DoctorCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        return new LabDoctorResult(checks.All(c => c.Status != DoctorStatus.Fail), checks);
    }
}
```

Consumers to align: C4 row (`{ok, checks:[{name, status, detail}]}`, packets L12, L31, L35); L31 item 3 and L35 item 1 name their check statuses (below).

## N4. Doctor checks: implemented now, deferred, exact behaviour

All checks below are emitted in this order by the L12 fix pass. The output never contains "not checked".

| Name | Implemented | Behaviour |
| --- | --- | --- |
| `os` | L12 (now) | `info`, `Environment.OSVersion.Platform` as today. |
| `build` | L12 (now) | As today: Windows registry `CurrentBuildNumber` → `ok <build>`; not found or denied → `warn`; off Windows → `info "not Windows"`. |
| `dotnet` | L12 (now) | As today (`VersionCheckAsync`, dotnet only). |
| `docker` | L12 (now, N2) | `IDockerCli.RunAsync(DockerEngine.Linux, ["version", "--format", "{{.Server.Os}}"], ct)`. Result failure → `fail` with the redacted error. Exit ≠ 0 → `fail "docker server not reachable on the lab context"`. Exit 0 and trimmed stdout `linux` → `ok "linux"`. Exit 0 and anything else → `fail "lab Linux context answers with <os>"`. |
| `docker-users` | L12 (now) | Windows only: `using var identity = WindowsIdentity.GetCurrent();` then `new WindowsPrincipal(identity).IsInRole("docker-users")`. A member → `ok "member"`. Not a member → `warn "not a member; the engine answered anyway"` when `docker` is `ok`, else `fail "not a member of docker-users"`. `SystemException` from the lookup → `warn "docker-users group not found"`. Off Windows → `info "not Windows"`. |
| `ports` | L12 (now) | Checked set: `LabPorts.ServicePorts` ∪ {`Postgres`, `RabbitMq`, `RabbitMqManagement`, `Redis`, `ToxiproxyApi`, `Sink`}, sorted. Probe each with `using var listener = new TcpListener(IPAddress.Loopback, port)`, then `Start()` and `Stop()`. `SocketError.AddressAlreadyInUse` means in use. `SocketError.AccessDenied` means reserved by the OS (Hyper-V excluded range). Lab-owned ports come from `docker --context <linux> ps --filter label=meridian-lab=1 --format {{.Ports}}` through `IDockerCli`; parse `127\.0\.0\.1:(\d+)->`. Verdicts: every port free or lab-owned → `ok "<n> free, <m> held by lab containers"`. Any port in use and not lab-owned, or reserved → `fail "in use by something else: 15000, 15432"` (reserved ones listed as `15000 (reserved by the OS)`). Docker unreachable while some port is in use → `warn "in use, owner unknown (docker unreachable): ..."`. |
| `dev-ports` | L12 (now) | Always `info`. Set: 5000 (Gateway), 5040 (Nodes), 5432, 5672, 6379, 15672 (F34). The coder checks `src/Dhadgar.AppHost/Program.cs`, drops any port the AppHost does not bind as fixed, and lists the change in the report. Same probe. Detail `"in use: 5432, 15672"` or `"none in use"`. |
| `token` | L12 (now) | New public `Daemon/DaemonTokenInfo.cs`: `public sealed record DaemonTokenInfo(int ByteLength);`, registered next to `edge` in `LabDaemon.BuildApplication` as `builder.Services.AddSingleton(new DaemonTokenInfo(edge.Token.Length));`. Length ≥ `LabDefaults.MinTokenBytes` → `ok "<n> bytes (min 32)"`, else `fail` with the same text. Never the value or any prefix of it. |
| `repo-root` | L12 (now) | From `LabPaths.RepoRoot`. Empty → `fail "Lab:RepoRoot is not set"`. Not `Directory.Exists` → `fail "Lab:RepoRoot does not exist"`. Exists without `Dhadgar.sln` → `warn "Lab:RepoRoot is not a MeridianConsole checkout"`. Else `ok "exists"`. Never the path (it carries the user name into bundles). |
| `windows-context` | **L31** | Not emitted before L31. L31: `Lab:Docker:WindowsContext` unset → `info "not configured"`; set and `docker --context <win> version` exit 0 → `ok`; else `fail`. |
| `windows-isolation`, `windows-member-image` | **L31** | As L31 item 3, with statuses `ok`/`fail` and `ok`/`warn "run lab_build"`. |
| `playwright` | **L35** | Not emitted before L35. L35: chromium present under `PLAYWRIGHT_BROWSERS_PATH` → `ok`; else `warn "run meridian-lab ui install"`. |

Constructor: `LabDoctorTool(Redactor redactor, IDockerCli docker, LabPaths paths, DaemonTokenInfo token)`. Description: `"Environment checks: OS, build, dotnet, Docker on the lab context, docker-users, lab ports, dev ports, token length, repo root. Read-only."`

For tests, keep the port verdict a pure function: `internal static DoctorCheck EvaluatePorts(IReadOnlyList<int> ports, Func<int, PortState> probe, IReadOnlySet<int>? labOwned)` with `internal enum PortState { Free, InUse, Reserved }`. A null `labOwned` means docker was unreachable.

Tests (`tests/Dhadgar.Lab.Tests/Tools/LabDoctorToolTests.cs`, extend or create):

- `Result_Serialises_OkAndStatusPerCheck`: each check has a string `status` in the four values; top-level `ok` is a bool.
- `Ok_IsFalse_WhenAnyCheckFails`.
- `Docker_UsesLinuxEngineAndVersionArgs`: a recording fake `IDockerCli` sees `DockerEngine.Linux` and `["version","--format","{{.Server.Os}}"]` (N2).
- `Ports_HeldByForeignListener_Fails`: a `TcpListener` on port 0 is held by the test; that port with an empty `labOwned` → `fail` naming the port.
- `Ports_HeldByLabContainer_IsOk`.
- `Ports_DockerUnreachable_Warns`.
- `Token_ReportsLengthNeverValue`.
- `RepoRoot_Unset_Fails`, `RepoRoot_Missing_Fails`, `RepoRoot_Checkout_IsOk` (temp dir with an empty `Dhadgar.sln`).
- `Output_HasNoNotCheckedText`.
- `DockerUsers_OnWindows_IsNeverInfo`, a `[SkippableFact]` run on Windows only.

**Fails when reverted:** with the four constant lines, `Output_HasNoNotCheckedText` and the port, token and repo-root tests fail. With the private `Process`, the recording fake sees no call.

Docs: `src/Tools/Dhadgar.Lab/README.md`, doctor section (the "info (not checked yet)" text near line 206). It lists the nine checks, the status meanings, and "`windows-*` from L31, `playwright` from L35".

The L12 fix pass applies N2 (with the verdicts above), D1 in place of N3, this table in place of N4, and N5 as written.

## D2. Replacement probes that avoid the Gateway CA route

Every probe uses the forwarded enroll route. Recovery polls are spaced so the lab never trips the `Auth` limiter (facts 4, 5). Applied in `work-packets.md`:

- **L17 `gateway-up`:** `svc_start gateway`. Step 1: `http via=gateway GET /healthz`, status 200. Step 2: `http via=gateway POST /api/v1/agents/enroll` with body `{}`, status 400, and the body contains `invalid_platform`. Step 2 proves the Gateway forwards to Nodes.
- **L19 S-L7:** the latency probe becomes "`POST /api/v1/agents/enroll` body `{}` via the Gateway's direct port takes at least 1000 ms and returns 400". The rest stays.
- **L23 `gw-nodes-down`:** `POST /api/v1/agents/enroll` body `{}` via `gateway` → status `any [502, 503]`. Gateway `/readyz` is not 200 within 75 s (YARP marks unknown-health destinations available, so the flip can take more than one 30 s active-check interval; ruling L09-2 "Noted"). After heal, the enroll probe returns 400 `invalid_platform` within 75 s, with `poll_ms = 5000`.
- **L23 `gw-nodes-latency`:** the enroll probe returns 400 `invalid_platform`, and `/duration_ms` is `gte 3000`. This replaces "→ 200" on an unnamed route that meant the CA route.
- **L23 `gw-reset-peer`:** the enroll probe returns `any [502, 503]`. After heal it returns 400 `invalid_platform` within 30 s, with `poll_ms = 5000`.
- **L23 invariant sentence:** the Nodes invariant checks tolerate `EnrollmentFailed` audit rows with reason `invalid_platform`, because the probes write them.
- **Rate-limit rule (L17, L19, L23; written into L23's rules):**
  - Any clause or step that polls a Gateway route under `Auth` (`/api/v1/agents/enroll`) sets `poll_ms = 5000`.
  - A section's enroll-route calls, probes and real enrolls together, stay at or under 25 per 60 s by the spec's own count. The L23 coder writes that count per row in the report.
  - A 429 in these rows is a lab spec defect, never a product finding.
- **L28, L42, L57** (high-volume paths through one IP) each gain one line in their rules: size the run under fact 4's buckets, or raise them through a lab-only Gateway env key (`RateLimiting__Policies__<Policy>__PermitLimit`) in that packet's adapter change, with a report line. Their rows that test the limiter itself keep product values.

No lab test asserts anything about `/api/v1/agents/ca-certificate` through the Gateway (D-ML63).

## D3 and N9. Ledger changes applied

- **README:**
  - F52 (ruling 1 facts 1, 4, 5) and F53 (ruling 2 facts 2, 3, 5).
  - F54 (fact 4 here) and F55 (fact 7).
  - D-ML62, D-ML63, D-ML64 (D1 and N4) and D-ML65 (M1).
  - Packet rows `L09f` (Done, #237) and `L12a` (Ready). L09 depends on L07, L08, L09f.
  - Statuses: L00 Parked (Windows containers off; the owner skipped them), L01-L12 Integrated.
  - S-L3: green in L12. Wave 2 order includes L09f. Wave 3 starts with L12a.
  - The wave 2 gate adds "S-L3 green". The wave 8 gate says "S-L3 still green".
  - The F13 packet cell and the L39 row are updated.
  - External dependency note: "Gateway CA route through the Gateway is vertical-slice VS-07R; the lab does not test or fix it."
- **work-packets.md:**
  - L09 item 3 becomes "`RabbitMq__Port=20021` (key added by L09f)".
  - L09 item 8: the `ServicesDockerTests` clause is replaced with ruling 2's text verbatim (N9).
  - The L09f block is added, in short form; ruling 1 holds the full spec.
  - L34 item 2: vhost `/`.
  - L36 item 2: `RabbitMq__Port=20051` and `20062`. C10 gains the `discord-rabbitmq` proxy (20062) and the Discord dependency cell.
  - L12: the title, item 7 (this table) and item 10 (green from L12, plus the N5 registry comparisons).
  - L39: depends on the W8 ruling note only; "S-L3 stays green".
  - `Seam!=S-L3&` is removed from every test filter (dispatch rule "Red seams": the packet that turns a seam green removes its exclusion).

## M1. Packet L12a: registration areas, tool set asserted from `ToolPolicy`

**Ruling: yes, before wave 3.** Without it, every tool packet in waves 3-10 edits the same list and the same literal (fact 7), and the waves that run in parallel (4H, 5, 5E, 6, 7, 9) conflict on every merge. Sequencing the packets instead would serialise half the plan. Record it as **D-ML65**.

### L12a. Tool registration areas; tool set asserted from `ToolPolicy`

- **Branch / worktree / commit:** `ml/l12a-tool-areas` / `meridian-lab-l12a` / `refactor(lab): register tools by area and assert the tool set from the policy table`
- **Depends on:** L12, including its wave 2 fix pass. Branch from the campaign tip after every wave 2 fix pass has merged. **Blocks:** L13 and every later packet that registers a service.
- **Security review:** yes (campaign rule: every code packet). No behaviour change, no security-review area.
- **Rules (inline):**
  - No behaviour change: the same 24 tools and the same services, with the same lifetimes.
  - R-DI: `LabDaemonServiceLifetimeTests` stays green with `ValidateScopes` and `ValidateOnBuild`.
  - R-CPM: no package or project reference added.
  - R-NULL: nullable on, `-p:TreatWarningsAsErrors=true`, CA analyzers clean.
  - R-XUNIT: `Subject_Condition_Expectation`.
  - Nothing outside the file list.

**Files**

1. `src/Tools/Dhadgar.Lab/Mcp/LabMcpServer.cs`: make it `public static partial class LabMcpServer`. `AddLabMcp` keeps `AddMcpServer()...`, `LabToolRegistry` and `LabToolDispatcher`, then calls every area hook once, in this order, and nothing else. Declare all 32 hooks here, so no later packet edits this file:

   ```csharp
   // Area hooks (D-ML65, R-REG). A hook with no implementing file compiles away. Order is fixed; never reorder.
   static partial void AddCoreArea(IServiceCollection services);        // L05, L12: lab_status, leases, lab_doctor
   static partial void AddLogsArea(IServiceCollection services);        // L07
   static partial void AddWindowsArea(IServiceCollection services);     // L12
   static partial void AddStackArea(IServiceCollection services);       // L08, L11
   static partial void AddNodesArea(IServiceCollection services);       // L11, L58
   static partial void AddProbesArea(IServiceCollection services);      // L10
   static partial void AddServicesArea(IServiceCollection services);    // L09
   static partial void AddUatArea(IServiceCollection services);         // L13-L17, L22, L40
   static partial void AddEventsArea(IServiceCollection services);      // L48
   static partial void AddFaultsArea(IServiceCollection services);      // L19, L20, L33
   static partial void AddInvariantsArea(IServiceCollection services);  // L21
   static partial void AddHistoryArea(IServiceCollection services);     // L49-L51
   static partial void AddResultsArea(IServiceCollection services);     // L25, L26
   static partial void AddJobsArea(IServiceCollection services);        // L27
   static partial void AddFuzzArea(IServiceCollection services);        // L28, L44
   static partial void AddChaosArea(IServiceCollection services);       // L29
   static partial void AddCoverageArea(IServiceCollection services);    // L52
   static partial void AddTwinsArea(IServiceCollection services);       // L53
   static partial void AddMutantsArea(IServiceCollection services);     // L54, L55
   static partial void AddPoolArea(IServiceCollection services);        // L30, L31
   static partial void AddAgentsArea(IServiceCollection services);      // L32, L43, L59
   static partial void AddAuthStackArea(IServiceCollection services);   // L34
   static partial void AddUiArea(IServiceCollection services);          // L35
   static partial void AddNotifyStackArea(IServiceCollection services); // L36
   static partial void AddCliArea(IServiceCollection services);         // L37
   static partial void AddIdentityArea(IServiceCollection services);    // L56
   static partial void AddTlsArea(IServiceCollection services);         // L41
   static partial void AddFleetArea(IServiceCollection services);       // L42
   static partial void AddEnvelopesArea(IServiceCollection services);   // L57
   static partial void AddPeerArea(IServiceCollection services);        // L58
   static partial void AddVersionsArea(IServiceCollection services);    // L60, L61
   static partial void AddGuidedArea(IServiceCollection services);      // L62
   ```

   `AddLabTool<TTool>` stays private in this file; area files are parts of the same class, so they can call it.
2. Seven new files under `src/Tools/Dhadgar.Lab/Mcp/Areas/`. Each holds one `static partial void Add<Area>Area(IServiceCollection services)` body in `public static partial class LabMcpServer`. The lines move unchanged from `AddLabMcp`:
   - `CoreArea.cs`: `LabStatusTool`, the four lease tools, `LabDoctorTool`.
   - `LogsArea.cs`: `LogsTailTool`.
   - `WindowsArea.cs`: `AgentStateReader`, `CertStoreListTool`, `RegistryReadTool`, `StateFileReadTool`.
   - `StackArea.cs`: `StackUpTool`, `StackDownTool`, `StackStatusTool`, `LabDataReset`, `StackResetDataTool`.
   - `NodesArea.cs`: the `INodesLabAdapter` factory and the three Nodes tools.
   - `ProbesArea.cs`: `LabDb`, `LabHttp`, the named `HttpClient`, `DbQueryTool`, `HttpRequestTool`.
   - `ServicesArea.cs`: `BuildTool` and the four `svc_*` tools.

   Registration order inside `ILabTool` may change (`LabDoctorTool` now follows the lease tools). If any test depends on that order, report it; do not reorder to suit it.
3. `src/Tools/Dhadgar.Lab/Policy/ToolPolicy.cs`, `PlannedTools`:
   - Remove the 9 registered names (fact 8). That leaves exactly 60.
   - Group the names by the packet that registers them. Each group starts with a comment line (`// L13`) and is separated from the next by that line. `GateTable` does not change.
   - Groups:
     - `L13`: `uat_list_specs`, `uat_plan`
     - `L15`: `uat_run`
     - `L16`: `uat_report`, `bundle_read`, `uat_attest`
     - `L19`: `fault_list_targets`, `faults_active`, `fault_toxic_add`, `fault_proxy_disable`, `fault_partition`, `fault_heal`, `fault_reset`
     - `L20`: `fault_kill`, `fault_pause`, `fault_disk_fill`, `fault_corrupt_file`, `fault_db`
     - `L21`: `invariants_check`
     - `L25`: `results_query`
     - `L26`: `triage_show`, `results_known_issue_set`, `triage_draft_issue`, `triage_mark`
     - `L27`: `job_status`, `job_cancel`, `results_flake_check`, `hunt_model_run`
     - `L28`: `fuzz_run`
     - `L29`: `chaos_run`, `soak_run`
     - `L30`: `pool_list`, `pool_acquire`, `pool_release`, `member_reset`
     - `L32`: `agent_install`, `agent_uninstall`, `agent_start`, `agent_stop`, `agent_token_write`, `agent_config_set`
     - `L33`: `fault_corrupt_registry`
     - `L35`: `ui_flow_list`, `ui_flow_run`
     - `L37`: `cli_run`
     - `L42`: `fleet_run`
     - `L43`: `agent_msi_install`, `agent_msi_uninstall`
     - `L48`: `events_read`
     - `L51`: `history_run`
     - `L52`: `coverage_report`
     - `L53`: `twin_run`
     - `L54`: `mutation_run`
     - `L56`: `identity_user_token`
     - `L57`: `envelope_run`
     - `L58`: `nodes_decommission`, `peer_run`
     - `L59`: `agent_kill`
     - `L60`: `version_run`
     - `L62`: `guided_ab_run`
   - The XML summary says: "Grouped by the packet that registers them. A packet removes only its own names and keeps its `// Lnn` line, so parallel packets never edit adjacent lines."
4. `tests/Dhadgar.Lab.Tests/Daemon/LabDaemonServiceLifetimeTests.cs`: replace line 47 (`Assert.Equal(24, ...)`) with the set assertion below, and add `using Dhadgar.Lab.Policy;` and `using ModelContextProtocol.Server;`:

   ```csharp
   string[] registered = services.GetServices<ILabTool>().Select(t => t.Name).ToArray();
   Assert.Equal(registered.Length, registered.Distinct(StringComparer.Ordinal).Count());
   string[] expected = ToolPolicy.Gates.Keys
       .Where(name => !ToolPolicy.PlannedTools.Contains(name))
       .Order(StringComparer.Ordinal)
       .ToArray();
   Assert.Equal(expected, registered.Order(StringComparer.Ordinal).ToArray());
   Assert.Equal(registered.Length, services.GetServices<McpServerTool>().Count());
   ```

   **Fails when reverted:** with today's stale `PlannedTools`, `expected` has 15 names against 24 registered, so the assertion fails. Likewise, a later packet that registers a tool but leaves its name planned (or the reverse) fails it. That is the tool count, derived from the policy table instead of a literal.
5. `src/Tools/Dhadgar.Lab/README.md`, section "Adding a tool", rewritten as four steps:
   1. Write the tool.
   2. Register it with `AddLabTool<T>` in your area file `Mcp/Areas/<Area>Area.cs`.
   3. Remove its name from your `// Lnn` group in `PlannedTools`.
   4. Never edit the hook list, the tool-set test or `LabDaemon.BuildApplication`.

**Acceptance**

- The checks below pass, and `services.GetServices<ILabTool>()` still yields the same 24 names.
- Conflict proof, not committed. From the L12a tip, create two throwaway local branches. On `a`, add a dummy `AddLabTool<LabStatusTool>` line to a new `Mcp/Areas/UatArea.cs` and remove `uat_list_specs`. On `b`, create `Mcp/Areas/EventsArea.cs` likewise and remove `events_read`. Then run `git -C <W> merge-tree --write-tree a b`, which must exit 0 with no conflict. Delete both branches. Paste the command output into the report.

**Checks (in order; `<W>` = the `meridian-lab-l12a` worktree)**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
git -C <W> status --porcelain   # only the files above; no packages.lock.json change
```

**Docs owed:** the README section in item 5.

### Later packets: the one-line change

New repo rule **R-REG** (added to the rules table):

- A packet registers its tools, and the services it introduces, only in its area file `Mcp/Areas/<Area>Area.cs`.
- It removes only its own names from its `// Lnn` group in `PlannedTools`.
- It never edits the hook list, the tool-set test or `LabDaemon.BuildApplication`. The one exception is replacing a placeholder its spec names (L19 replaces `NoFaults`).
- A new area needs an architect ruling.

Each of these packets gains one rules line, `R-REG: area <Area>.`:

| Area | Packets |
| --- | --- |
| Uat | L13, L14, L15, L16, L17, L22, L40 |
| Events | L48 |
| Faults | L19, L20, L33 |
| Invariants | L21 |
| History | L49, L50, L51 |
| Results | L25, L26 |
| Jobs | L27 |
| Fuzz | L28, L44 |
| Chaos | L29 |
| Coverage | L52 |
| Twins | L53 |
| Mutants | L54, L55 |
| Pool | L30, L31 |
| Agents | L32, L43, L59 |
| AuthStack | L34 (its `ILabServiceAdapter` registrations too) |
| Ui | L35 |
| NotifyStack | L36 (its `ILabServiceAdapter` registrations too) |
| Cli | L37 |
| Identity | L56 |
| Tls | L41 |
| Fleet | L42 |
| Envelopes | L57 |
| Peer | L58 (`nodes_decommission` goes in `Nodes`) |
| Versions | L60, L61 |
| Guided | L62 |

L13's dependencies gain L12a. Every later packet depends on L13, directly or through other packets, so it reaches L12a that way.

## Status

No BlockedDecision; nothing here needs the owner.

- The L12 fix pass runs from this note: N2, D1, the N4 table and N5.
- L12a is Ready once the wave 2 fix passes merge. L13 waits for L12a.

# L19n ruling 1: S-L15 red/green record after NR-1 reached the tip; Gateway route table

Architect, 2026-11-02. Input: review pass 1 of L19n (`decision`), the coder report, [l19n worknote](l19n.md) on `ml/l19n` (head `360682c6`, base `8191eb8f`), [nodes-ef-retry-packet](nodes-ef-retry-packet.md) section 5, and the ledger.

Outcome: the S-L15 test and the S-L7 refactor stand. One small test edit (a Nodes log tail and the build id in the test output) is prescribed. The red is recorded on a local scratch branch whose `src/Dhadgar.Nodes` is restored to pre-NR-1 `37560f0d`. The green is recorded on `ml/l19n` itself (D-ML144). The `GatewayRouteTableTests` failure is not L19n's. It also hides a stale lab route table (F126), so it gets its own packet, L21g (D-ML145). No owner decision is needed. The Docker runs need the user's yes, which is a run permission, not a design decision.

## 1. Facts checked for this ruling

- NR-1 is on the base. `git merge-base --is-ancestor 159a72da 8191eb8f` is true. `8191eb8f` merges `d3406570` (PR #256, NR-1) from `main`. `ml/l19n` `src/Dhadgar.Nodes/Program.cs:49` calls `NodesDatabase.Configure(...)`.
- NR-1 is the only `src/Dhadgar.Nodes` change between pre-NR-1 `main` and the L19n head. `git diff --stat 37560f0d 360682c6 -- src/Dhadgar.Nodes` lists exactly NR-1's five files: `Data/NodesDatabase.cs` and `Data/NodesRetryingExecutionStrategy.cs` (new), `Program.cs`, `README.md` and `Services/CapacityReservationService.cs`. Restoring `src/Dhadgar.Nodes` to `37560f0d` therefore gives "the campaign tip without NR-1" exactly.
- `tests/Dhadgar.Lab.Tests` references `Dhadgar.Nodes`, but not `Dhadgar.Nodes.Tests`. No lab file names `NodesDatabase` or `NodesRetryingExecutionStrategy`, so the scratch tree still builds.
- `LabBuilder.BuildAsync` builds from `rig.RepoRoot`, which is the worktree the test binaries run from (`FindRepoRoot`). The build id is the first 8 characters of the commit, plus `-dirty-<time>` for a dirty tree (`LabBuilder.cs:73-77`). The id therefore proves which source a run used.
- The S-L15 `finally` deletes `home`, which includes `logs/nodes.jsonl`, and `stack.DownAsync` removes the container. As written, the Nodes log lines the packet asks for cannot be recorded. `LabProcessSupervisor.ReadLines("nodes")` returns the redacted captured lines while the follower is alive. The follower's process name is the service name (`ContainerLogFollower.FollowAsync`, `LabServices.cs:206`).
- NR-1 merged to `main` (#256) before its section 4.6 scratch green existed, so step 3 of the packet's order was skipped. L19n's green on the tip is now the only seam evidence for NR-1.
- `GatewayRouteTableTests` fails in `JsonDocument.Parse` on the `//` lines at `src/Dhadgar.Gateway/appsettings.json:5-6,9-10` (VS-32R, #243). With comments skipped, it would still fail. The same `8191eb8f` merge brought VS-07R (#242): `servers-org-route` (`/api/v1/organizations/{organizationId}/servers/{**catch-all}` → `servers`, Order 20), `console-org-route` (`…/servers/{serverId}/console/{**catch-all}` → `console`, Order -1), `agents-ca-route` (`/api/v1/agents/ca-certificate`, Order 22), and the removal of the `/api/v1/servers/` and `/api/v1/console/` routes. `src/Tools/Dhadgar.Lab/Invariants/GatewayRouteTable.cs:13-28` still maps `/api/v1/organizations/` to `nodes`, still maps the removed prefixes, and has no CA entry. Its prefix model cannot express an `{organizationId}` segment. `FaultImpactMap.ImpactSet` (`FaultImpactMap.cs:59-77`), which I1 uses (`I1UnhandledInvariant.cs:91`), therefore judges Gateway 502-504s on org-scoped Servers and Console paths against the Nodes impact set. This is F126.

## 2. Review points: rulings

| # | Review point | Ruling |
| --- | --- | --- |
| 1 | The red cannot be taken on the tip | **Apply (D-ML144).** Take the red on scratch branch `ml/l19n-red-scratch`, which is the L19n head with `src/Dhadgar.Nodes` restored to `37560f0d`. Take the green on `ml/l19n`. The packet's hard stop (a 401 means `blocked spec`) applies to the scratch run only. The procedure is in section 4. |
| 2 | The test was judged by reading only | **Apply.** The section 3 edit makes the run self-recording: build id, timings, Nodes log tail. A green is accepted only with log evidence that the dead-connection path ran (section 4, step C3). |
| 3 | `SeamRig` exposes `Docker`, `Paths` and `Validator` | **Accept** as a deviation. Section 3 adds `Supervisor` for the same reason. |
| 4 | Packet check 2 filter `Category!=Docker` | **Ledger fix**, section 6.1. The orchestrator edits the packet text. No code change. |
| 5 | `GatewayRouteTableTests` red in the non-Docker suite | **Separate packet L21g (D-ML145, F126, KG-W5-22)**, section 5. Fix the lab, not the Gateway: the comments guard the token-leak log levels (`HubTokenTests`). L19n accepts exactly this one failure in its non-Docker check until L21g merges. |
| 6 | Commit trailer names Haiku | **Accept.** No amend, because the trailer names the model that ran. The orchestrator's briefs keep the attribution line from the session reminder. |

Owner questions: none. `BlockedDecision`: none.

## 3. Prescribed edit (packet-coder, worktree `ml-l19n`, step A, no Docker)

File: `tests/Dhadgar.Lab.Tests/Seams/ToxiproxySeamTests.cs` only.

Rules: nullable is on and CI treats warnings as errors. Use `using` on every `IDisposable`. Lab code returns `Result`, so read logs through `ReadLines` and never throw for a missing log. Change no assertion, wait, timing or message in S-L7 or S-L15. Add no package and no `.csproj` change. Never `git worktree prune`.

1. In `SeamRig`, after `public HttpClient Http => _http;`, add:

   ```csharp
   public LabProcessSupervisor Supervisor => _supervisor;
   ```

2. In `PostgresCutWhileIdle_FirstEnrollAfterHeal_Answers401`, directly after `Assert.True(build.IsSuccess, …)`, add:

   ```csharp
   _output.WriteLine($"S-L15 build {build.Value.BuildId}");
   ```

   Use the same `build.Value` access form as the existing `services.StartAsync("nodes", build.Value.BuildId, ct)` line.

3. In that test's `finally`, insert these two lines as the **first** statements, before `services.StopAsync`:

   ```csharp
   await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None); // let the follower flush the last lines
   WriteNodesLogTail(rig.Supervisor, 60);
   ```

4. Add this private instance method next to `ProbeAsync`:

   ```csharp
   /// <summary>
   /// Writes the last <paramref name="count"/> captured Nodes log lines (already redacted) to the test output. S-L15's record
   /// needs them: the red shows the dead-connection 500, and the green shows EF's retry (D-ML144).
   /// </summary>
   private void WriteNodesLogTail(LabProcessSupervisor supervisor, int count)
   {
       Result<IReadOnlyList<LogLine>> lines = supervisor.ReadLines("nodes");
       if (lines.IsFailure)
       {
           _output.WriteLine($"S-L15 nodes-log unavailable: {lines.Error}");
           return;
       }

       foreach (LogLine line in lines.Value!.TakeLast(count))
       {
           _output.WriteLine($"S-L15 nodes-log {line.Timestamp:O} {line.Level} {line.Category} {line.Message}");
       }
   }
   ```

   `LogLine` is in `Dhadgar.Lab.Processes`, which the file already imports. Keep the `!` only if the compiler needs it. Match the existing `Result` usage in the file.

Checks, in order:

```powershell
dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true"
```

The second check expects exactly one failure: `Dhadgar.Lab.Tests.Invariants.GatewayRouteTableTests.EveryGatewayRoute_IsInTheTable_WithTheSameCluster` (F126, L21g). Any other failure is `failed`. If L21g has been merged into `ml/l19n` by then, expect 0 failures.

Commit subject: `fix(lab): apply L19n ruling 1, S-L15 records its build and the Nodes log [L19n]`. The body says: "S-L15 is green on this tip because NR-1 arrived with the main merge (8191eb8f). Its red is recorded with NR-1 reverted (D-ML144)." Update `worknotes/l19n.md`: replace the "Not done: the red record" and "Next step" sections with "Pending: section 4 of l19n-ruling-1 (Docker, needs the user's yes)", and keep the check lines current.

No security-advisor pass (test-only lab code).

## 4. Docker record procedure (D-ML144)

The user gives one yes for the pair: the red run (B) and the green run (C). Both runs use the same Docker daemon, **one after the other, never in parallel**. Both use the `meridian-lab-*` container names, so no other lab run (no other `ml-*` worktree, no hunt) may use the daemon until step C ends.

### B. Scratch tree (ops, then the packet-coder)

1. ops: `git worktree add C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l19n-red -b ml/l19n-red-scratch <step-A head>`. The scratch branch is local and is never pushed or merged.
2. packet-coder, in `ml-l19n-red`:

   ```powershell
   $s = 'C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l19n-red'
   git -C $s restore --source=37560f0d --staged --worktree -- src/Dhadgar.Nodes
   git -C $s status --porcelain
   git -C $s diff --cached --stat
   ```

   Expected: exactly the five NR-1 paths, with `Data/NodesDatabase.cs` and `Data/NodesRetryingExecutionStrategy.cs` deleted. If either file still exists, run `git -C $s rm -- src/Dhadgar.Nodes/Data/NodesDatabase.cs src/Dhadgar.Nodes/Data/NodesRetryingExecutionStrategy.cs`. Then `git -C $s diff --cached 37560f0d -- src/Dhadgar.Nodes` must print nothing. If it prints anything, stop with `blocked spec`.
3. Commit on the scratch branch only: `chore(scratch): restore pre-NR-1 Nodes source for the S-L15 red record [L19n]`. Note the scratch sha.
4. `dotnet build $s\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`. If any `packages.lock.json` changes (`git -C $s status --porcelain`), stop and report. Never use `publish -r` or `restore -r`.

### C. Runs (packet-coder, after the user's yes)

0. Preconditions, before each run:
   - `docker --context desktop-linux ps -a --filter "name=meridian-lab-" --format "{{.Names}}"` prints nothing.
   - `docker --context desktop-linux network ls --filter "name=meridian-lab" --format "{{.Name}}"` prints nothing.
   - No `tests\Dhadgar.Lab.Tests\TestResults\` folder exists in the worktree under test.

   If any check fails, stop with `blocked` and remove nothing yourself; teardown is label-only and belongs to the lab.
1. **Red (scratch):**

   ```powershell
   dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l19n-red\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "Seam=S-L15" --logger "console;verbosity=detailed" --logger "trx;LogFileName=s-l15-red.trx" --results-directory "$env:TEMP\l19n-records\red"
   ```

   - **Expected:** `Failed: 1`, S-L15, with the message `First enroll after an idle Postgres cut answered 500, not 401 (F114 product half, D-ML128).` The `S-L15 build` id starts with the scratch sha's first 8 characters. The `S-L15 nodes-log` lines show the Npgsql read failure ("Exception while reading from stream" or `EndOfStreamException`) and then "Unhandled exception (fallback handler)".
   - **Hard stop:** if the first probe answers 401, the seam does not reproduce F114. Record everything, skip the green run, and report `blocked spec`. Do not tune the waits and do not rerun to fish for a red.
   - If the test fails at any earlier step (stack up, build, baseline, warm-up or cut), report `failed` with the output. Do not run the green.
2. Repeat the step 0 checks, which also confirm the red run's teardown.
3. **Green (`ml-l19n`, step-A head):**

   ```powershell
   dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l19n\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ToxiproxySeamTests" --logger "console;verbosity=detailed" --logger "trx;LogFileName=s-l15-green.trx" --results-directory "$env:TEMP\l19n-records\green"
   ```

   - **Expected:** `Passed: 2` (S-L7 and S-L15). The `S-L15 build` id starts with the step-A head's first 8 characters. The `S-L15 nodes-log` lines include, after the cut, either EF's retry warning (category `Microsoft.EntityFrameworkCore.*`, message containing "will be retried") or an Npgsql connection-failure line, followed by a 401 request.
   - **Hollow-green guard:** if S-L15 passes but the tail shows no retry line and no connection-failure line, the green does not prove that NR-1 ran. Report `blocked spec` with the tail. Do not rerun.
   - If either Fact fails, report `failed` with the output. Do not retry.
4. Repeat the step 0 checks.
5. Record both runs in `worknotes/l19n.md` on `ml/l19n`, under a new section "S-L15 record (D-ML144)". For each run, give: the command, the result line, the build id, every `S-L15 timing` line, and the `S-L15 nodes-log` lines from the cut to the last probe (all of them, as printed). Also give the scratch sha and the output of `git -C <ml-l19n> diff --stat 360682c6 <scratch sha>`. Commit only that file: `test(lab): S-L15 red with NR-1 reverted, green on the tip [L19n]`.
6. Leave the scratch worktree and branch in place for the reviewer.

### D. Reviewer checks (pass 2; read-only, no Docker)

1. The step-A diff touches only `ToxiproxySeamTests.cs`. It adds `Supervisor`, the build line and `WriteNodesLogTail`. The tail runs in `finally` before `services.StopAsync`. No S-L7 or S-L15 assertion, wait or message changed (diff against `360682c6`).
2. Scratch identity:
   - `git diff 37560f0d ml/l19n-red-scratch -- src/Dhadgar.Nodes` is empty.
   - `git diff --name-only <step-A sha> ml/l19n-red-scratch` lists exactly the five NR-1 paths under `src/Dhadgar.Nodes`.
   - `git merge-base --is-ancestor <step-A sha> ml/l19n-red-scratch` is true.
3. Red record: the build id prefix equals the scratch sha. The result is `Failed: 1` with status 500 in the message. The log tail shows the read failure and the fallback-handler line.
4. Green record: the build id prefix equals the step-A sha, the result is `Passed: 2`, and the tail passes the hollow-green guard.
5. The non-Docker suite line shows only the `GatewayRouteTableTests` failure, or 0 failures if L21g is already in.
6. `docker --context desktop-linux ps -a --filter "name=meridian-lab-"` is empty.

After `ship`: ops removes the `ml-l19n-red` worktree and deletes the local branch `ml/l19n-red-scratch` (`git branch -D`; if that is outside ops' list, the orchestrator notes it in RESUME). Then ops merges `ml/l19n` into the campaign locally, as usual.

## 5. Packet L21g: Gateway route table models templates and Order (F126, D-ML145)

| Field | Value |
| --- | --- |
| Wave | 5 (follow-up) |
| Branch / worktree | `ml/l21g-route-table` / `ml-l21g`, from the `campaign/meridian-lab` tip |
| Ships as | local merge into the campaign (D-ML131); independent of L19n, either can merge first |
| Size | small, about 25k tokens; no Docker |
| Security | no security-advisor pass: a pure in-memory function with no I/O, process, auth or secrets |
| Commit subject | `fix(lab): Gateway route table matches templates and Order like YARP (F126) [L21g]` |
| Files | `src/Tools/Dhadgar.Lab/Invariants/GatewayRouteTable.cs`, `tests/Dhadgar.Lab.Tests/Invariants/GatewayRouteTableTests.cs` only |

Contract: none. No `Dhadgar.Contracts` type changes. The seam is the lab table against the Gateway's `ReverseProxy:Routes`, and the parity test is its seam test.

Rules (packet-coder does not read CLAUDE.md):

- Nullable is on, and CI treats warnings as errors.
- No package versions in a `.csproj`, and no new `PackageReference`.
- Never use `publish -r` or `restore -r`. Never `git worktree prune`.
- `ClusterFor` has no failure path. It keeps its `string?` return (null means unrouted) and never throws for any input.
- xUnit `[Fact]` and `[Theory]`, with `Subject_Case_Result` names, as the file already uses.
- Do not edit `src/Dhadgar.Gateway/appsettings.json`. Its comments guard the token-leak log levels.

Steps:

1. Replace the tuple table in `GatewayRouteTable.cs`:

   ```csharp
   /// <summary>One Gateway route: its YARP route id, its <c>Match:Path</c> template, its cluster and its <c>Order</c>.</summary>
   public sealed record GatewayRoute(string RouteId, string Template, string ClusterId, int Order);

   public static class GatewayRouteTable
   {
       /// <summary>The Gateway's routes, in <c>appsettings.json</c> order.</summary>
       public static IReadOnlyList<GatewayRoute> Routes { get; } =
       [
           new("identity-internal-block", "/api/v1/identity/internal/{**catch-all}", "identity", 1),
           new("nodes-reservations-block", "/api/v1/organizations/{organizationId}/nodes/{nodeId}/reservations/{**catch-all}", "nodes", 1),
           new("betterauth-route", "/api/v1/betterauth/{**catch-all}", "betterauth", 10),
           new("identity-route", "/api/v1/identity/{**catch-all}", "identity", 10),
           new("servers-org-route", "/api/v1/organizations/{organizationId}/servers/{**catch-all}", "servers", 20),
           new("nodes-org-route", "/api/v1/organizations/{organizationId}/nodes/{**catch-all}", "nodes", 20),
           new("enrollment-route", "/api/v1/organizations/{organizationId}/enrollment/{**catch-all}", "nodes", 20),
           new("console-org-route", "/api/v1/organizations/{organizationId}/servers/{serverId}/console/{**catch-all}", "console", -1),
           new("console-hub-route", "/hubs/console/{**catch-all}", "console", 20),
           new("agent-hub-route", "/hubs/agent/{**catch-all}", "nodes", 20),
           new("notifications-route", "/api/v1/notifications/{**catch-all}", "notifications", 20),
           new("secrets-route", "/api/v1/secrets/{**catch-all}", "secrets", 20),
           new("discord-route", "/api/v1/discord/{**catch-all}", "discord", 20),
           new("agents-ca-route", "/api/v1/agents/ca-certificate", "nodes", 22),
           new("agents-enroll-route", "/api/v1/agents/enroll", "nodes", 25),
           new("agents-route", "/api/v1/agents/{**catch-all}", "nodes", 30),
       ];

       private static readonly IReadOnlyList<GatewayRoute> ByOrder = Routes.OrderBy(route => route.Order).ToList(); // stable

       public static string? ClusterFor(string? requestPath) { /* see the rules below */ }

       internal static bool Matches(string template, string path) { /* see the rules below */ }
   }
   ```

   Matching rules, a subset of ASP.NET route templates, which is what YARP uses:

   - Strip the query at the first `?`.
   - Split the path and the template on `/` with `StringSplitOptions.RemoveEmptyEntries`.
   - A template segment `{**name}` must be the last segment. It matches the remaining zero or more path segments.
   - A template segment `{name}` matches exactly one path segment.
   - Any other segment is a literal, compared `OrdinalIgnoreCase`.
   - A route matches when every template segment is consumed, and either every path segment is consumed or the last template segment is a catch-all.
   - `ClusterFor` returns the cluster of the first matching route in `ByOrder`: the lowest Order wins, and ties go to table order. It returns null for null, empty or unmatched input.

   Update the class `<summary>`: it describes templates and Order, and names the parity test.
2. Rewrite `GatewayRouteTableTests.cs`. Keep `FindRepoRoot` and `UnroutedPath_HasNoCluster`.
   - **`EveryGatewayRoute_IsInTheTable_WithTheSameTemplateClusterAndOrder`** (replaces `EveryGatewayRoute_IsInTheTable_WithTheSameCluster`):
     - Parse with `new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }`, which is what the .NET JSON configuration provider accepts.
     - For each route, assert that `Match` has exactly one property, named `Path`. The failure message is `$"Gateway route {id} matches on more than Path; GatewayRouteTable models Path only."`
     - Read Order as `TryGetProperty("Order", out JsonElement o) ? o.GetInt32() : 0`.
     - Assert that exactly one `Routes` entry has that `RouteId`, with an equal `Template`, `ClusterId` and `Order`.
     - After the loop, assert that `GatewayRouteTable.Routes.Count` equals the Gateway route count (no stale entries), and that the route ids are distinct.
     - Reverting the comment option throws `JsonException`, and the old table fails on ids and count.
   - **`[Theory] ClusterFor_RoutesLikeTheGateway(string path, string? cluster)`**, with these rows:

     | Path | Cluster |
     | --- | --- |
     | `/api/v1/organizations/o1/servers/s1/console/attach` | `console` |
     | `/api/v1/organizations/o1/servers/s1/console` | `console` |
     | `/api/v1/organizations/o1/servers/s1/start` | `servers` |
     | `/api/v1/organizations/o1/servers` | `servers` |
     | `/api/v1/organizations/o1/nodes/n1/reservations/r1` | `nodes` |
     | `/api/v1/organizations/o1/nodes/n1` | `nodes` |
     | `/api/v1/organizations/o1/enrollment/tokens` | `nodes` |
     | `/api/v1/organizations/o1/audit-logs` | null |
     | `/api/v1/organizations` | null |
     | `/api/v1/servers/s1` | null |
     | `/api/v1/console/s1` | null |
     | `/api/v1/identity/internal/users` | `identity` |
     | `/api/v1/betterauth/sign-in` | `betterauth` |
     | `/hubs/console/negotiate?negotiateVersion=1` | `console` |
     | `/hubs/agent/negotiate` | `nodes` |
     | `/api/v1/agents/ca-certificate` | `nodes` |
     | `/api/v1/agents/enroll` | `nodes` |
     | `/api/v1/agents/a1/heartbeat` | `nodes` |
     | `/API/V1/SECRETS/s1` | `secrets` |

     The old table fails the `servers` rows (it answers `nodes`) and the `null` rows for removed and unrouted paths. Ignoring Order fails the `console` rows.
   - **`GatewayImpactSet_OrgScopedServersPath_IsServersNotNodes`**: `FaultImpactMap.ImpactSet("gateway", "/api/v1/organizations/o1/servers/s1/start")` contains `"d:servers"` and `"c:servers"`, and does not contain `"d:nodes"`. The old table fails it.
3. Checks, in order:

   ```powershell
   dotnet build tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
   dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Dhadgar.Lab.Tests.Invariants"
   dotnet test tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true"
   ```

   The last check must show 0 failures. That makes the non-Docker suite green on the tip again.
4. Docs owed: none in the library. The lab README does not describe the route table. The architect updates F126 and KG-W5-22 from the merge.

## 6. Ledger edits (orchestrator applies)

1. `worknotes/nodes-ef-retry-packet.md`:
   - Line 215: change `--filter "Category!=Docker"` to `--filter "RequiresDocker!=true"`.
   - Section 2 table, L19n row: change the branch `ml/l19n-pool-seam` to `ml/l19n`.
   - Section 5 "Expected results", first bullet: prefix it with "Superseded by D-ML144 ([l19n ruling 1](l19n-ruling-1.md) section 4): the red is taken on a scratch branch with `src/Dhadgar.Nodes` at `37560f0d`, and the hard stop applies to that run only."
   - Section 2 order, step 3: append "Not done: NR-1 merged (#256) first; L19n's tip green replaces it (D-ML144)."
2. `README.md` findings: add **F126**: "The main merge `8191eb8f` brought VS-07R's Gateway routes (org-scoped Servers and Console, the CA route; `/api/v1/servers/` and `/api/v1/console/` removed) and VS-32R's `//` comments. `GatewayRouteTableTests` fails on the comments, which hides that `GatewayRouteTable` is stale. `/api/v1/organizations/{id}/servers/…` maps to `nodes`, removed prefixes still map, and the prefix model cannot express `{organizationId}`. I1 judges Gateway 502-504s on those paths against the wrong impact set." Evidence: `GatewayRouteTable.cs:13-28`; `GatewayRouteTableTests.cs:15`; Gateway `appsettings.json:5-10,110,140,195`; `FaultImpactMap.cs:59-77`. Disposition: D-ML145; L21g.
3. `README.md` decisions:
   - **D-ML144**: "S-L15's red is recorded on local scratch branch `ml/l19n-red-scratch`: the L19n head with `src/Dhadgar.Nodes` restored to `37560f0d`. Its green is recorded on `ml/l19n` (base `8191eb8f`, NR-1 included). The hard stop applies to the scratch run. A green without an EF retry or connection-failure line in the Nodes log is not accepted. NR-1 merged (#256) before its section 4.6 green, so this tip green is its seam evidence." Status: Accepted (architect). Basis: [l19n ruling 1](worknotes/l19n-ruling-1.md) sections 1 and 4.
   - **D-ML145**: "`GatewayRouteTable` holds (route id, Path template, cluster, Order) and matches like ASP.NET templates (literal, `{param}`, `{**catch-all}`), with the lowest Order winning. The parity test reads the Gateway config with comments skipped and checks both directions. The Gateway's comments stay." Status: Accepted (architect). Basis: F126; section 5.
4. `README.md` seam table, S-L15 row: "Written red in" becomes `L19n (scratch, NR-1 reverted; D-ML144)`. "Green in" becomes `L19n (tip 8191eb8f, NR-1 via main)`.
5. `README.md` packets:
   - NR-1 status becomes: `Merged to main (#256, d3406570) and into the campaign (8191eb8f); section 4.6 scratch green not run; tip green via L19n (D-ML144)`.
   - L19n: the branch becomes `ml/l19n`. The status becomes `Ruling 1: step A edit, then the red/green record (l19n-ruling-1 sections 3-4; user's yes for Docker)`.
   - Add an L21g row: `| L21g | 5 (follow-up) | Gateway route table: templates and Order like YARP; parity test skips JSON comments and checks both ways (F126, D-ML145) (ml/l21g-route-table, worktree ml-l21g) | none (no Docker) | no | Ready ([l19n ruling 1](worknotes/l19n-ruling-1.md) section 5) |`.
6. `README.md` known gaps:
   - KG-W5-17 becomes: "**Moot on the tip:** NR-1 reached the campaign at `8191eb8f` before L19n. Closes on L19n's green record (D-ML144). The campaign review (D-ML74) still reruns S-L15."
   - Add **KG-W5-22**: "**Live (F126):** I1's Gateway impact set is wrong for org-scoped Servers and Console paths and for the removed `/api/v1/servers/` and `/api/v1/console/` prefixes. The non-Docker suite has one red (`GatewayRouteTableTests`)." Closes with L21g.
7. `README.md` next steps and `RESUME.md`:
   - (1) L21g, which can start now with no Docker.
   - (2) L19n step A, then ops creates the scratch, then the user's yes, then the record, then review pass 2, then the scratch is removed and L19n merged.
   - Drop "NR-1's PR, which the owner merges" and "merge `main` into the campaign branch", because both are done at `8191eb8f`.
   - The post-merge rerun of `RequiresDocker!=true` waits for L21g.
   - RESUME line 11: NR-1 is merged.

## 7. Side observation (not ruled here)

`GatewayAdapter.cs:62` sets `Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Information` in the lab Gateway, so that I1 can read request lines. VS-32R's comments say this level writes the hub `access_token` query into the log. Lab log lines pass through `Redactor`, which redacts vault-known values, before they reach the ring and `logs/<name>.jsonl`. `SensitiveText`, which would redact `access_token=…`, is applied only in `BundleWriter` and I1. A console-hub token not held in the vault may therefore reach `logs/gateway.jsonl` in clear text. This is unverified. The orchestrator should send it to a short read-only `security-advisor` check. If confirmed, it becomes F127 with its own packet. It does not block L19n or L21g.

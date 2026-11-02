# L37 ruling 1

Architect, 2026-11-02. This ruling covers review pass 1 of L37. Worktree `ml-l37`, branch `ml/l37-cli`, range `1cf07e96..dc08f635` (head `dc08f635`).

Inputs, all in the `ml-l37` worktree under `.claude/reports/meridian-lab/`:

- `l37-review-1.md`: packet-reviewer, verdict `decision`.
- `l37-security-1.md`: security-advisor, verdict `fix`.
- `l37-coder.md`: the coder report.

Spec: L37 in [work-packets.md](../work-packets.md) (line 3739 onward) and the [next-batch plan](next-batch-plan.md).

**Outcome:** a fix pass by one Sonnet packet-coder (section 4), then packet-reviewer pass 2 and security-advisor pass 2.

- Two owner decisions are raised: D-ML165 and D-ML166 (section 6). **Neither blocks the L37 merge.** Until they are ruled, the rows they affect stay `required = false`.
- The live `cli.toml` run has never happened. It **needs a new yes from the user** (section 5).

**IDs.** This ruling uses F135-F141, D-ML162-D-ML166, KG-W7-7 and KG-W7-8. KG-W5-24 is not used. The l35m ruling took F133/F134, D-ML160/161 and KG-W7-6. Next free after this ruling: **F142, D-ML167, KG-W5-24, KG-W7-9**.

## 1. Facts checked for this ruling

| # | Fact | Evidence |
| --- | --- | --- |
| 1 | `cli_run` asks the guard for fault kind `"cli"`, and `KnownFaultKinds` has no `"cli"`. **Every** `cli_run` call therefore fails with `[Lab.InvalidArgument] unknown fault kind` before `docker exec`, even `--version`. The exec also names `found.ContainerName` from the pool listing, not the container the guard inspected. | `Tools/Cli/CliRunTool.cs:350,357`; `Faults/FaultTargetGuard.cs:30-33,61-64` |
| 2 | `FaultTargetGuard.AllowedFor` feeds `fault_list_targets`. Adding `"cli"` to `AllowedKinds[Member]` would advertise it as a fault. `ResolveMemberAsync` (label, name, context, tripwire) is private. | `FaultTargetGuard.cs:42,151,158` |
| 3 | The lab Identity has no `dev-client`. `IdentityLabAdapter.Environment` sets no `OpenIddict__DevClient__*`. `SeedDevOpenIddictClientAsync` returns when either value is blank. The `cli.toml` header sentence "the security review ... confirms they match the lab's identity seed" is false. Compose seeds the same pair: `OpenIddict__DevClient__ClientId: dev-client` and `ClientSecret: dev-secret`. | `Services/IdentityLabAdapter.cs:89-106`; `src/Dhadgar.Identity/Program.cs:870-878`; `deploy/compose/docker-compose.services.yml:73-74`; `cli.toml:5-7` |
| 4 | When seeded, `dev-client` gets permission for every scope `auth login` asks for (`openid profile email servers:read servers:write nodes:manage`). The same seed also creates two WIF-only service accounts with random secrets, and skips `betterauth-service`, which has no secret. No lab code or spec reads `OpenIddictApplications`. | `Identity/Program.cs:886-905,927-1013`; `LoginCommand.cs:74`; grep of `src/Tools/Dhadgar.Lab`, `tests/Dhadgar.Lab.Tests`, `docs/guides/uat-specs` |
| 5 | The CLI's default URLs match no Gateway route. `EffectiveIdentityUrl` = gateway + `/identity` and `EffectiveNodesUrl` = gateway + `/nodes`. The Gateway serves Identity at `/api/v1/identity/{**}` (PathRemovePrefix `/api/v1/identity`) and Nodes at `/api/v1/organizations/{organizationId}/nodes/{**}` (PathRemovePrefix `/api/v1`). The CLI's Refit paths are `POST /connect/token` and `GET /organizations/{orgId}/nodes`. | `src/Dhadgar.Cli/Configuration/CliConfig.cs:55,61`; `Infrastructure/Clients/IIdentityApi.cs:12`, `INodesApi.cs:17`; `src/Dhadgar.Gateway/appsettings.json` (identity-route, nodes-org-route) |
| 6 | The Gateway's `AllowedHosts` is `localhost;api.meridianconsole.com;*.meridianconsole.com`. The member calls it as `meridian-lab-gateway`, so every CLI call gets **400** from host filtering. `GatewayAdapter` does not override `AllowedHosts`. The lab's own probes send `Host: localhost` (`LabServices.ProbeHostHeader`), so adding a host keeps them working. | `Gateway/appsettings.json:14`; `Services/GatewayAdapter.cs:70-84`; `Services/LabServices.cs:34` |
| 7 | The lab Gateway and Nodes validate JWTs with Authority = `Auth:Issuer` = `https://meridianconsole.com/api/v1/identity`, the **public** host. Neither adapter sets `Auth__MetadataAddress` or `Auth__InternalBaseUrl`. The lab network is not `--internal`, so discovery goes to the internet, not to the lab Identity: no lab bearer call can validate, and the lab is not hermetic. Compose sets both keys for Nodes and Secrets (`http://identity:8080/...`). `UrlRewritingDocumentRetriever` rewrites the issuer prefix to the internal base. No lab test has sent a bearer token through the Gateway so far, so L37 is the first to hit this. | `src/Shared/Dhadgar.ServiceDefaults/Security/ServiceJwtAuthenticationExtensions.cs:37-98`, `UrlRewritingDocumentRetriever.cs:53-64`; `Services/NodesAdapter.cs:60-68`; `Stack/LabStack.cs:359`; `docker-compose.services.yml:110-113` |
| 8 | Several verb actions discard their exit code: the `SetAction` lambdas for `auth`, `nodes`, `identity` and `enrollment` await without returning. Those processes always exit 0. The `notifications` and `discord` actions do return the code. | `src/Dhadgar.Cli/Program.cs:99-105` and the nodes actions |
| 9 | `auth login` has three problems. It prints "Authenticated successfully" whenever `config.IsAuthenticated()` holds afterwards, so a failed request on top of a still-valid stored token reports success. It catches only `ApiException`, so a transport failure (`HttpRequestException`) prints a stack trace. With no `--client-id`/`--client-secret` it prompts, and `cli_run` has no TTY. On a 5xx it prints `Authentication failed: <code> <Name>`. | `Commands/Auth/LoginCommand.cs:37-52,77-117` |
| 10 | `auth status` prints `✓ Authenticated` or `⚠ Not authenticated`. `ContainsOp` is ordinal, so `contains "Authenticated"` cannot match the failure text. | `Commands/Auth/StatusCommand.cs`; `Uat/Eval/ClauseOps.cs:146` |
| 11 | A client_credentials token carries no `org_id`, and the Nodes `TenantScoped` policy has `RequireClaim("org_id")`. A CLI service login therefore gets **403** on `nodes list`, even when every seam is right. The Gateway's `TenantScoped` only requires an authenticated user. | `Identity/Program.cs:517-620`; `src/Dhadgar.Nodes/Program.cs:170-177`; `Gateway/Program.cs:101-104` |
| 12 | `CliAllowlist.Check` looks only at `args[0]` and `args[1]`. `--identity-url <any>`, `--opt=value`, `--opt:value` and `@responsefile` tokens all pass. System.CommandLine accepts both `=`/`:` forms and expands `@file` response files by default. `CliAllowlistTests.cs:86` asserts that `--identity-url` is accepted. | `CliRunTool.cs:59-100`; `tests/Dhadgar.Lab.Tests/Cli/CliAllowlistTests.cs:86` |
| 13 | Rows `auth-login` and `gateway-down` never start Identity or the Gateway, and `notifications-admin` starts no stack at all. `pool_acquire` runs before any row step, and the vault holds the admin keys only after `stack_up`. So the null admin key at acquire time can never be fixed there. | `cli.toml`; `Tools/Pool/PoolTools.cs:111-130`; `Services/LabServices.cs:464` |
| 14 | If `pool_acquire` fails during CLI install or config, it returns without releasing what it acquired. `PoolManager.ReleaseAsync(memberId, ct)` destroys a member and refuses only the host. | `PoolTools.cs:115-128`; `Pool/PoolManager.cs:217` |
| 15 | `UatVars.Expand` substitutes `${...}` inside every string in a JSON value, including array items. Expectation values are expanded too. So `["nodes","list","--org","${org_id}"]` and `value = "${node_id}"` work. | `Uat/Eval/UatVars.cs:117-128,175-177`; `Uat/Eval/ClauseEvaluator.cs:115`; `Uat/RowExecutor.cs:456` |
| 16 | `GatewayRouteTable.ClusterFor(path)` mirrors the shipped Gateway routes, and `GatewayRouteTableTests` keeps it in sync. That makes it the non-Docker half of a CLI-to-Gateway seam test. | `src/Tools/Dhadgar.Lab/Invariants/GatewayRouteTable.cs:47` |
| 17 | `Dhadgar.Cli.csproj` runs `InstallGlobalTool` after a Release build unless `SkipGlobalToolInstall=true`. A test `ProjectReference` to the CLI would install a global tool on the workstation, so the seam test reads the CLI's source files instead. | `src/Dhadgar.Cli/Dhadgar.Cli.csproj`; `Build/LabBuilder.cs:131-134` |

## 2. Decisions

| ID | Decision |
| --- | --- |
| D-ML162 | **The dev client may go on the CLI's command line in the lab only, under five conditions.** `auth login` in `cli_run` may carry `--client-id dev-client --client-secret dev-secret` on argv. **(a)** The values are constants `IdentityLabAdapter.DevClientId` / `DevClientSecret`, and the allowlist accepts no other values. **(b)** `IdentityLabAdapter.Environment` seeds the same constants as `OpenIddict__DevClient__ClientId`/`ClientSecret`, adapter-only and never overlay keys, matching compose. **(c)** `cli_run` refuses `auth login` unless both options are present, so the CLI never prompts. **(d)** `CliSpecTests` pins every login step in `cli.toml` to the constants. **(e)** No other credential may travel on a `cli_run` argv. **Why:** these are the repo's public development fixtures (compose, the CLI's own prompt defaults, its README), not vault secrets. The client exists only in the throwaway lab Identity, and the allowlist closes the free-form channel (F141). The clean alternative, the CLI reading the secret from env or stdin, is a product change and goes to the owner under D-ML166. |
| D-ML163 | **The lab fixes the CLI-to-Gateway-to-Identity/Nodes seam on its own side.** **(a)** The member config adds `identity_url = http://meridian-lab-gateway:8080/api/v1/identity`, `nodes_url = http://meridian-lab-gateway:8080/api/v1` and `discord_url = http://meridian-lab-discord:8080`. **(b)** `GatewayAdapter` adds `AllowedHosts = "localhost;meridian-lab-gateway"`. **(c)** `GatewayAdapter` and `NodesAdapter` add `Auth__MetadataAddress = http://meridian-lab-identity:8080/.well-known/openid-configuration` and `Auth__InternalBaseUrl = http://meridian-lab-identity:8080`, as compose does. The discovery and JWKS fetch goes direct, not through the `gateway-identity` proxy. A `gateway-identity` fault therefore cuts the route but not token validation; that is deliberate and stated in the adapter comment. **(d)** A non-Docker seam test (`CliGatewaySeamTests`) runs the member config URLs plus the CLI's Refit path literals, read from the CLI sources, through `GatewayRouteTable`. It also checks that the member's Gateway host is in the adapter's `AllowedHosts`. The live half of the seam test is `cli.toml` `auth-login` plus `nodes-list` (section 5). |
| D-ML164 | **Rows assert on CLI text, not exit codes, for verbs that discard their exit code** (fact 8: auth, nodes). Exit-code expectations on those verbs stay in the rows with `required = false` and point at KG-W7-7. `notifications` returns its code, so `notifications-admin` keeps `exit_code eq 0` as required. Every row that logs in runs `auth logout` first. The member is shared across the sequential section, so this stops a stored token from making a failed login print success (fact 9). |
| D-ML165 | **BlockedDecision (owner):** org scope for a CLI service login, F140. See section 6. Until it is ruled, the `nodes-list` check that the enrolled node appears in the output is `required = false` (KG-W7-8). |
| D-ML166 | **BlockedDecision (owner):** the CLI product fixes, F139. See section 6. Until it is ruled, D-ML164 stands and KG-W7-7 stays open. |

Why the architect decides D-ML162 to D-ML164 and the owner does not:

- They change only lab adapters, a lab tool and a lab spec.
- They match what compose already does on the dev stack.
- They do not touch `src/Dhadgar.Cli`, Identity or Nodes.

Every product change is left to the owner (D-ML165, D-ML166), as F132 did for its Identity follow-up.

## 3. Rulings on each item

### Packet-reviewer pass 1

| Item | Ruling | Reason |
| --- | --- | --- |
| N1 (rows start no services) | **Accept, amended.** Every row that calls `auth` or `nodes` runs `svc_start identity` (plus `nodes` where needed), then `svc_start gateway`. In `gateway-down` the fault starts after the control login and logout (section 4 step 9), not after `start-gateway`. | Fact 13. Starting the fault after a proven login is what makes the row non-vacuous (security N3). |
| N2 (admin key never seeded) | **Accept, amended.** For an admin verb, `cli_run` writes the member config with the key from the vault right before the exec. It rewrites it **without** the key in `finally`, so the key is not left at rest in the member. A failed scrub fails the call. Helper `CliMemberInstall.AdminCredentialFor(verb)` with a test. The row gains `stack_up` and `svc_start notifications`. The comments, README and header are corrected. | Fact 13. Each verb needs its own key (Notifications and Discord keys differ), so it must be per call. The rewrite also drops any stored login; the tool description says so, and rows log in again anyway (D-ML164). |
| N3 (`discord_url` missing) | **Accept.** Plus `identity_url` and `nodes_url` (D-ML163 a). Rename the test to `RenderMemberConfig_WithoutKey_HoldsTheServiceUrlsOnly`. | Facts 5 and 6. |
| N4 (`auth login` exit 0 is theatre) | **Accept, extended.** Add an `auth status` step that must contain `Authenticated` (fact 10). The login step must contain `Authenticated successfully`. The exit check becomes `required = false` (D-ML164). | Fact 8. Ordinal `contains` was verified. |
| N5 (`CliPublishArgs` untested) | **Accept.** | Fact 17: that line is the one that keeps `lab_build` from installing a global tool. |
| N6 (`LatestCliDirectory` untested) | **Accept.** | It decides whether any member gets the CLI. |
| D1 (credentials) | **Decided:** D-ML162 (seed and pin). | Facts 3 and 4. |
| D2 (exit code discarded) | **Decided for the lab** (D-ML164, KG-W7-7). **Product fix to the owner** (D-ML166). | Fact 8. Changing `src/Dhadgar.Cli` is outside L37. |
| D3 (security-review label) | **Accept.** | `cli_run` runs `docker exec` as root and writes an admin key into a member. |
| D4 (clean items) | **Noted.** | |

### Security-advisor pass 1

| Item | Ruling | Reason |
| --- | --- | --- |
| N1 (`"cli"` kind never resolves; exec uses the listing name) | **Accept as written.** Add `FaultTargetGuard.ResolveMemberForExecAsync(targetId, ct)`: host refusal, registry, `Kind == Member`, then the private `ResolveMemberAsync`. The exec uses `resolved.ContainerId`. `"cli"` stays out of both kind tables. Tests use a real guard. | Facts 1 and 2. Today the tool fails closed but is dead. This makes it live behind the same R-BLAST checks as a member fault. |
| N2 (args after `[1]` are free) | **Accept, amended into a per-subcommand option table** (section 4 step 2). Any `-`-prefixed token that is not exactly an allowed name is refused, which also refuses `--x=v`, `--x:v`, `-abc` and `--`. Any token starting with `@` is refused anywhere. `--identity-url` is refused everywhere. The login values are pinned (D-ML162). `nodes maintenance` needs `start` or `stop`. The line-86 test is flipped. Refusal messages never echo a value. | Fact 12. The reviewer's "denylist of five options" would miss the next option the CLI adds. An allowlist cannot drift open. |
| N3 (false header; vacuous `gateway-down`) | **Accept option (a), the seed**, plus: delete the false sentence, add a control login before the fault, and add a 5xx text assertion. | Facts 3 and 9. The row must show the fault caused the failure. A control login that succeeds, then a login that prints a 5xx from the Gateway, does that. |
| N4 (members leak when install fails) | **Accept.** On an install or config failure, release every acquired member (`CancellationToken.None`), then return the original error. | Fact 14. |
| N5 (staged key file) | **Accept, amended.** **Reject `FileOptions.DeleteOnClose`:** `docker cp` is a separate process that must open the file after we close our handle, and on Windows a delete-on-close handle blocks that. Instead: a dedicated staging folder, `FileMode.CreateNew`, deletion in `finally` (already there), and a sweep of stale `cli-config-*.json` (older than 5 minutes) on every config write. The key comes only from the vault, never from tool args. | The sweep covers the process-kill window without the share-mode trap. |
| Residual: lease ownership and member state not checked by Leased tools | **Out of L37.** An orchestrator/dispatcher question for every Leased tool. Not a new finding here. | Same for all Leased tools. |
| Residual: CLI runs as root in the member | **Accept as residual.** | Throwaway lab container. `HOME=/home/lab` isolates the config. |

## 4. Fix packet (one Sonnet packet-coder, about 40k)

- **Worktree / branch:** `ml-l37` / `ml/l37-cli`. Continue on head `dc08f635`.
- **Commit subject:** `fix(lab): apply L37 ruling 1 fix notes [L37]`
- **Security review:** yes. Run security-advisor pass 2 beside packet-reviewer pass 2. The PR carries `security-review`.

**Rules (inline):**

- Use `Result<T>` for validation, IO and Docker errors, and never `Result<T>.Success(null)`.
- Never print, log or put in an error message the admin key, a lease id, a client secret or any argument value.
- Every `docker` call is an `ArgumentList`-style item list, never a joined string.
- Every file path goes through `IPathValidator` under `LabPaths`.
- Put `using`/`await using` on every `IDisposable`.
- No package versions in `.csproj` and no package changes at all. **Never** add a `ProjectReference` to `Dhadgar.Cli` (fact 17).
- Nullable is on. Build with `-p:TreatWarningsAsErrors=true`.
- Tests are xUnit `[Fact]`/`[Theory]` with `Assert.*`, using fakes, not mocks.
- Never run `git worktree prune`, `publish -r` or `restore -r`.
- Edit only the files named below.

### Step 1: `src/Tools/Dhadgar.Lab/Faults/FaultTargetGuard.cs`

Add after `ResolveAsync` (no change to `KnownFaultKinds` or `AllowedKinds`):

```csharp
/// <summary>
/// Resolves a pool member for a non-fault exec (<c>cli_run</c>). Same registry, inspect and tripwire checks as a
/// member fault, but no fault kind, so <c>fault_list_targets</c> never lists it. Every refusal is <c>[Lab.BlastRadius]</c>.
/// </summary>
public async Task<Result<ResolvedTarget>> ResolveMemberForExecAsync(string targetId, CancellationToken ct)
{
    ArgumentNullException.ThrowIfNull(targetId);
    if (string.Equals(targetId, HostTargetId, StringComparison.Ordinal))
    {
        return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] host is read-only");
    }

    var target = registry.Find(targetId);
    if (target is null)
    {
        return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] target is not registered");
    }

    if (target.Kind != LabTargetKind.Member)
    {
        return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] cli_run runs only on a pool member");
    }

    return await ResolveMemberAsync(target, ct).ConfigureAwait(false);
}
```

### Step 2: `src/Tools/Dhadgar.Lab/Tools/Cli/CliRunTool.cs`: `CliAllowlist`

Replace the `SafeSubcommands` check after `args[1]` with a per-subcommand shape table. Keep the existing count, length and control-character checks and the `--help`/`--version` rule. Keep `Verbs`. Keep the single-verb case (`["auth"]` prints help).

```csharp
private sealed record Shape(IReadOnlySet<string> ValueOptions, IReadOnlySet<string> FlagOptions, int MaxPositionals, IReadOnlySet<string>? Actions = null);

private static readonly IReadOnlyDictionary<string, Shape> Shapes = new Dictionary<string, Shape>(StringComparer.Ordinal)
{
    ["auth login"] = new(Set("--client-id", "--client-secret"), Set(), 0),
    ["auth status"] = new(Set(), Set(), 0),
    ["auth logout"] = new(Set(), Set(), 0),
    ["nodes list"] = new(Set("--org", "--status", "--skip", "--take"), Set(), 0),
    ["nodes get"] = new(Set("--org"), Set(), 1),
    ["nodes update"] = new(Set("--display-name", "--org"), Set(), 1),
    ["nodes decommission"] = new(Set("--org"), Set("--force"), 1),
    ["nodes maintenance"] = new(Set("--org"), Set(), 1, Set("start", "stop")),
    ["notifications logs"] = new(Set("--limit", "--status", "--org"), Set(), 0),
    ["notifications test"] = new(Set("--title", "--message", "--severity", "--org"), Set(), 0),
    ["discord status"] = new(Set(), Set(), 0),
    ["discord channels"] = new(Set("--guild"), Set(), 0),
};
```

The option names come from `src/Dhadgar.Cli/Program.cs`: auth login 93-95, nodes 838-929, notifications 1008-1028, discord 1058. `--identity-url` is deliberately absent.

The `SafeSubcommands` dictionary stays as the source of `Verbs`. `config` keeps an empty subcommand set, so `["config", "x"]` is refused.

After `args[1]` is found in `Shapes`, walk the tokens from index 2. If the shape has `Actions`, index 2 must be in it (else `[Lab.InvalidArgument] subcommand needs start or stop`), and the walk starts at 3. Rules, first match wins:

1. A token starting with `@` → `[Lab.InvalidArgument] '@' arguments (response files) are refused`.
2. A token starting with `-`:
   - in `ValueOptions` → the next token is its value. Fail if it is missing or starts with `-` or `@` (`[Lab.InvalidArgument] option needs a plain value`).
   - in `FlagOptions` → no value.
   - anything else → `[Lab.InvalidArgument] option is not allowed for this subcommand`.
   - An option seen twice → `[Lab.InvalidArgument] option given twice`.
3. Any other token is a positional. More than `MaxPositionals` → `[Lab.InvalidArgument] too many positional arguments`.
4. For `auth login`, after the walk both options must be present, with values exactly `IdentityLabAdapter.DevClientId` and `IdentityLabAdapter.DevClientSecret` (ordinal). Else `[Lab.InvalidArgument] auth login takes only the lab's development client`.

No message includes a token's text.

Update the class `<summary>` to say: per-subcommand options, `@` refused, login pinned (D-ML162).

### Step 3: `CliRunTool.cs`: `CliMemberInstall`

- Add constants and keep each URL single-sourced:
  - `MemberIdentityUrl = "http://meridian-lab-gateway:8080/api/v1/identity"`
  - `MemberNodesUrl = "http://meridian-lab-gateway:8080/api/v1"`
  - `MemberDiscordUrl = "http://meridian-lab-discord:8080"`
- `RenderMemberConfig` writes them as `identity_url`, `nodes_url`, `discord_url` beside `gateway_url` and `notifications_url`.
- Add `public static string? AdminCredentialFor(string verb)`: `"notifications"` → `LabCredentialRefs.NotificationsAdmin`, `"discord"` → `LabCredentialRefs.DiscordAdmin`, anything else → null.
- Add `public static string StagingDirectory(LabPaths paths) => Path.Combine(paths.State, "cli-config");`. Use it from both callers. Never call `_paths.State` directly for staging again.
- In `InstallConfigAsync`:
  - Validate the staging path with the `IPathValidator` the caller already holds. Add an `IPathValidator validator` parameter and validate `stagingDirectory` under `[paths.State]`. If you would rather not thread `LabPaths`, validate against the directory's parent; record which in the report.
  - Before writing, call `SweepStale(stagingDirectory, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5))`.
  - Create the file with `new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None)` inside `await using`, and write the JSON through a `StreamWriter`. Close it before the `docker cp`.
- Add `internal static int SweepStale(string directory, DateTimeOffset now, TimeSpan maxAge)`. It deletes `cli-config-*.json` whose `LastWriteTimeUtc` is older than `now - maxAge` and returns the count. It swallows `IOException` and `UnauthorizedAccessException`, and returns 0 when the directory does not exist.
- Update the `RenderMemberConfig` doc: client credentials are still never written (D-ML162 puts them on argv only).

### Step 4: `CliRunTool.cs`: `CliRunTool`

Change the constructor to `CliRunTool(PoolManager pool, FaultTargetGuard guard, IDockerCli docker, Redactor redactor, LabCredentialRefs credentials, SecretVault vault, LabPaths paths, IPathValidator validator)`, null-checked into fields. All are already registered (PoolAcquireTool, LabServices, InvariantsArea, NodesArea). `CliArea.cs` needs no change because `AddLabTool<CliRunTool>` resolves through DI.

In `ExecuteAsync`, after `CliAllowlist.Check`:

```csharp
Result<ResolvedTarget> resolved = await _guard.ResolveMemberForExecAsync(found!.TargetId, ct).ConfigureAwait(false);
if (resolved.IsFailure) { return Result<object>.Failure(resolved.Error); }
string container = resolved.Value.ContainerId ?? string.Empty;
if (container.Length == 0) { return Result<object>.Failure("[Lab.BlastRadius] the member's container could not be resolved"); }

string? credential = CliMemberInstall.AdminCredentialFor(args.Args[0]);
if (credential is not null)
{
    string? reference = _credentials.Get(credential);
    if (reference is null) { return Result<object>.Failure("[Lab.NotFound] the stack has no admin keys; run stack_up first"); }
    Result<string> key = _vault.Use(reference, v => v.ToString());
    if (key.IsFailure) { return Result<object>.Failure(key.Error); }
    Result seeded = await CliMemberInstall.InstallConfigAsync(_docker, container, CliMemberInstall.StagingDirectory(_paths), key.Value, /* validator/paths per step 3 */ ct).ConfigureAwait(false);
    if (seeded.IsFailure) { return Result<object>.Failure(seeded.Error); }
}

try
{
    // exec with `container` (the resolved id), not found.ContainerName
}
finally
{
    if (credential is not null)
    {
        // CancellationToken.None: the scrub must run even when the call was cancelled.
        Result scrubbed = await CliMemberInstall.InstallConfigAsync(_docker, container, CliMemberInstall.StagingDirectory(_paths), null, /* ... */ CancellationToken.None).ConfigureAwait(false);
        scrubFailed = scrubbed.IsFailure;
    }
}
if (scrubFailed) { return Result<object>.Failure("[Lab.Docker] could not remove the admin key from the member's CLI config"); }
```

Restructure the shape so that a scrub failure wins over a successful exec result. Keep the key string's lifetime to this method. Do not log it.

Update `Description`: "... Admin verbs (notifications, discord) write the vault's admin key into the member config for the call and remove it afterwards; that rewrite also clears any stored login."

### Step 5: `src/Tools/Dhadgar.Lab/Tools/Pool/PoolTools.cs`: `PoolAcquireTool.ExecuteAsync`

- Use `CliMemberInstall.StagingDirectory(_paths)` and pass the validator as in step 3.
- Replace the line-123 comment with: "The admin key is written per call by cli_run, because the vault has none until stack_up."
- On `installed.IsFailure` or `configured.IsFailure`, call `ReleaseAcquiredAsync(acquired.Value!)` before returning the original error:

```csharp
private async Task ReleaseAcquiredAsync(IReadOnlyList<PoolMember> members)
{
    foreach (PoolMember member in members)
    {
        // CancellationToken.None: a failed acquire must never leave members leased. A release failure is ignored here;
        // the caller already gets the install error, and pool_reset (D-ML…) cleans up the rest.
        _ = await _pool.ReleaseAsync(member.MemberId, CancellationToken.None).ConfigureAwait(false);
    }
}
```

Release every acquired member, not only the containers: the whole acquire failed. `ReleaseAsync` itself refuses the host. Drop the `(D-ML…)` placeholder from the comment and just say `pool_reset`.

### Step 6: `src/Tools/Dhadgar.Lab/Services/IdentityLabAdapter.cs`, `GatewayAdapter.cs`, `NodesAdapter.cs`

- **`IdentityLabAdapter`:**
  - Add `public const string DevClientId = "dev-client";` and `public const string DevClientSecret = "dev-secret";`. Their `<summary>` says: "the repo's public development client (compose, CLI prompt defaults), seeded only in the lab Identity; never a vault secret (D-ML162)".
  - Add `["OpenIddict__DevClient__ClientId"] = DevClientId` and `["OpenIddict__DevClient__ClientSecret"] = DevClientSecret` to `Environment`. Do **not** add them to `OverlayKeys`.
  - Add `public const string MetadataAddress = "http://meridian-lab-identity:8080/.well-known/openid-configuration";` and `public const string InternalBaseUrl = "http://meridian-lab-identity:8080";`, built from `Container` text. If the coder prefers `$"http://{ContainerName}:8080"` with a const container name, that is fine.
- **`GatewayAdapter.Environment`:**
  - Add `["AllowedHosts"] = "localhost;meridian-lab-gateway"`.
  - Add `["Auth__MetadataAddress"] = IdentityLabAdapter.MetadataAddress` and `["Auth__InternalBaseUrl"] = IdentityLabAdapter.InternalBaseUrl`.
  - Comment: "discovery and JWKS go direct to the lab Identity, as in compose; without these the Gateway fetches the public issuer (F138). A gateway-identity proxy fault cuts the route, not token validation."
  - `localhost` must stay, because the lab probes send it (fact 6).
- **`NodesAdapter.Environment`:** add the same two `Auth__*` keys.

### Step 7: tests (all non-Docker)

**`tests/Dhadgar.Lab.Tests/Pool/PoolTestSupport.cs`.** Add to `FakeDockerCli`:

- `public Func<string[], int>? ExitCodeFor { get; set; }` (default exit 0);
- `public Action<string[]>? OnCall { get; set; }`, invoked after `Calls.Add`.

This is a test-support change only.

**`tests/Dhadgar.Lab.Tests/Cli/CliAllowlistTests.cs`.**

Line 86: replace the `--identity-url` case with `Assert.True(CliAllowlist.Check(["auth","login","--client-id","dev-client","--client-secret","dev-secret"]).IsSuccess)` and `Check(["nodes","list","--org","o1"])` success.

New tests:

| Test | Asserts | Red when |
| --- | --- | --- |
| `Login_IdentityUrl_IsRefused` (`[Theory]`) | `--identity-url http://x`, `--identity-url=http://x` and `--identity-url:http://x`, each with valid client options, all fail. | Line 86 is reverted to the old accept, or the shape walk is removed. |
| `Login_OnlyThePinnedDevClient_IsAccepted` (`[Theory]`) | Fails for: `--client-secret real`, `--client-secret=dev-secret`, a missing `--client-secret`, a missing `--client-id`, both options twice. | The pin or the both-present rule is reverted. |
| `AtArguments_AreRefused` | `["nodes","list","@/etc/passwd"]` and `["notifications","test","--title","@x"]` fail. | |
| `UnknownOptions_AreRefused` | `["nodes","list","--reveal"]`, `["discord","channels","--url","x"]`, `["nodes","list","--"]` and `["nodes","list","-o","x"]` fail. | |
| `Maintenance_NeedsStartOrStop` | `["nodes","maintenance","n1"]` fails; `["nodes","maintenance","start","n1"]` succeeds. | |
| `Positionals_AreBounded` | `["nodes","get","a","b"]` fails; `["nodes","get","a"]` succeeds; `["nodes","list","x"]` fails. | |
| `Refusals_NeverEchoTheValue` | The error for `--client-secret hunter2xyz` does not contain `hunter2xyz`. | |

**`tests/Dhadgar.Lab.Tests/Cli/CliMemberConfigTests.cs`.**

- Rename to `RenderMemberConfig_WithoutKey_HoldsTheServiceUrlsOnly`. Assert all five URL properties equal the constants and that `admin_api_key` is absent. Red when any of `identity_url`, `nodes_url` or `discord_url` is dropped.
- `AdminCredentialFor_MapsOnlyTheAdminVerbs`: notifications → `NotificationsAdmin`, discord → `DiscordAdmin`, `nodes` → null.
- `CliPublishArgs_SkipsGlobalToolInstall_AndHasNoRid`: the reviewer's N5 asserts verbatim. Red when the `-p:` item is removed.
- `LatestCliDirectory_ReturnsTheCliFolder` and `LatestCliDirectory_WithoutCliFolder_IsNull`. Copy the stamp setup from `tests/Dhadgar.Lab.Tests/Build/LastBuildReaderTests.cs`. Red when `Directory.Exists` is replaced with `true`.
- `SweepStale_RemovesOnlyOldConfigFiles`:
  - write `cli-config-a.json` with `File.SetLastWriteTimeUtc` 10 minutes old;
  - write `cli-config-b.json` fresh;
  - write `other.json` 10 minutes old.
  - Only `a` is removed. Red when the sweep is a no-op.
- `InstallConfigAsync_WithKey_StagesUnderTheStagingFolder_AndNoArgCarriesTheKey`: use `OnCall` to read the staged file when `args[0]=="cp"`. Assert it contains the key and sits under `StagingDirectory(paths)`, and that no recorded arg contains the key. Update the existing `InstallConfigAsync_*` tests for the new parameter.

**`tests/Dhadgar.Lab.Tests/Cli/CliRunToolTests.cs` (new).**

Setup:

- Build a `PoolHarness` with a `FakePoolProvider` of `LinuxContainer` dimensions and `LabMemberInspector`.
- Acquire one member with `harness.Manager.AcquireAsync(...)`; copy the constraints from `PoolManagerTests.AnyLinux`.
- Build the tool with `new FaultTargetGuard(harness.Registry, <inspector>, new PassingTripwire())`, `harness.Docker`, `new Redactor(vault)`, `new LabCredentialRefs()`, `vault` (`using var vault = new SecretVault()`), `harness.Paths` and a `PathValidator`.
- Invoke through `tool.InvokeAsync(JsonSerializer.SerializeToElement(new { member = "m:<id>", args = new[] { "--version" } }), new LabCallContext("c1", "lease-1", "test"), CancellationToken.None)`.

The `exec` call is the one whose `args[0..2]` is `exec`, `-e`, `HOME=/home/lab`.

| Test | Asserts | Red when |
| --- | --- | --- |
| `Version_RunsInTheResolvedContainer` | Success. The `exec` call equals `["exec","-e","HOME=/home/lab","live-<id>","/opt/lab/cli/dhadgar","--version"]`. | `"cli"` is put back, or `found.ContainerName` is used. |
| `UnlabelledMember_IsBlastRadius_AndNeverExecs` (`[Theory]`) | Guard built with a `FixedInspector`: (a) labels lacking `meridian-lab`; (b) lacking `meridian-lab-member`; (c) context `other-ctx`. Error starts `[Lab.BlastRadius]`, and no `exec` call was recorded. | |
| `HostMember_IsRefused_BeforeAnyDocker` | `member = "m:host"` → `[Lab.Refused]`; `harness.Docker.Calls` is unchanged by the call. | |
| `AdminVerb_SeedsTheKeyForTheCall_ThenScrubsIt` | Setup: `vault.Add(SecretKind.Password, "adm-key-0123456789")` → ref; `refs.Set(LabCredentialRefs.NotificationsAdmin, ref)`; args `["notifications","logs"]`; record staged-file texts via `OnCall` on `cp`. Asserts: two config writes, the first before the exec with the key and the second after it without; no recorded arg contains the key; the staging folder holds no `cli-config-*.json` afterwards. | The `finally` scrub is removed. |
| `AdminVerb_WithoutStack_IsNotFound_AndNeverExecs` | Refs not set → `[Lab.NotFound] the stack has no admin keys; run stack_up first`, and no `exec` call. | |
| `AdminVerb_ScrubFailure_FailsTheCall` | `ExitCodeFor` returns 1 for the second `cp` only → failure `[Lab.Docker] could not remove the admin key ...`. | |

**`tests/Dhadgar.Lab.Tests/Pool/PoolAcquireCliInstallTests.cs` (new).**

`InstallFailure_ReleasesTheAcquiredMembers`:

- Use a `PoolHarness` with a linux `FakePoolProvider`.
- Write a last-build stamp whose id folder has a `cli` subfolder (copy from `LastBuildReaderTests`).
- Set `ExitCodeFor = a => a[0] == "cp" ? 1 : 0`.
- Invoke `PoolAcquireTool` with lease `lease-1`.

Assert:

- the failure is `[Lab.Docker] installing the CLI into the member failed`;
- the provider's `Destroyed` contains the member;
- `(await harness.Manager.ListAsync(ct)).Members` holds no member of this lease.

Red when `ReleaseAcquiredAsync` is removed.

**`tests/Dhadgar.Lab.Tests/Services/AuthStackAdapterTests.cs`.**

- `Identity_SeedsTheLabDevClient_FromConstants`: env has both `OpenIddict__DevClient__*` keys equal to the constants, and `OverlayKeys` contains no `OpenIddict` key. Red when the seed is removed.

**`tests/Dhadgar.Lab.Tests/Services/ServiceAdapterTests.cs`.**

- `Gateway_AllowsTheLabNetworkHost_AndKeepsLocalhost`: split `AllowedHosts` on `;`; it contains `localhost` and `meridian-lab-gateway`.
- `Gateway_And_Nodes_FetchDiscoveryFromTheLabIdentity`: both envs have `Auth__MetadataAddress == IdentityLabAdapter.MetadataAddress` and `Auth__InternalBaseUrl == IdentityLabAdapter.InternalBaseUrl`, and neither value contains `meridianconsole.com`.

**`tests/Dhadgar.Lab.Tests/Cli/CliGatewaySeamTests.cs` (new, the seam test of D-ML163 d).** The repo root comes from `LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory)`.

- `CliRefitPaths_ArePinned`: `src/Dhadgar.Cli/Infrastructure/Clients/IIdentityApi.cs` contains `[Post("/connect/token")]`, and `INodesApi.cs` contains `[Get("/organizations/{orgId}/nodes")]`. If the CLI moves these, this test, not the live run, goes red first.
- `MemberConfig_LoginAndNodesList_RouteThroughTheGateway`:
  - `GatewayRouteTable.ClusterFor(new Uri(MemberIdentityUrl).AbsolutePath + "/connect/token") == "identity"`;
  - `ClusterFor(new Uri(MemberNodesUrl).AbsolutePath + "/organizations/o1/nodes") == "nodes"`;
  - the CLI defaults are **not** routed: `ClusterFor("/identity/connect/token")` and `ClusterFor("/nodes/organizations/o1/nodes")` are both null. That documents fact 5 and goes red if D-ML166 fixes the CLI, which is the prompt to drop the lab override.
  - Red when `identity_url` or `nodes_url` is dropped or mistyped.
  - If `ClusterFor` returns null for the nodes path without a trailing segment, **stop and report**; do not change `GatewayRouteTable`.
- `MemberGatewayHost_IsAllowedByTheLabGateway`: `new Uri(MemberGatewayUrl).Host` is in the `AllowedHosts` list from `GatewayAdapter.Environment`.

**`tests/Dhadgar.Lab.Tests/Uat/CliSpecTests.cs`.** Keep the row-id test, updated for any row changes, and add:

- `EveryCliRunStep_PassesTheAllowlist`: for each step with `tool == "cli_run"`, `CliAllowlist.Check(args)` succeeds. `${...}` literals are fine.
- `EveryLoginStep_UsesThePinnedDevClient`: for each `auth login` step, the values after `--client-id` and `--client-secret` equal the `IdentityLabAdapter` constants.
- `EveryLoginRow_LogsOutFirst`: in each row with an `auth login` step, an `auth logout` step comes earlier.

### Step 8: `docs/guides/uat-specs/cli.toml`

Header:

- Delete the "security review ... confirms they match the lab's identity seed" sentence.
- Say instead: `IdentityLabAdapter` seeds `dev-client`/`dev-secret` (D-ML162); `cli_run` writes the admin key per call (ruling N2); verbs that drop their exit code are asserted on text (D-ML164, KG-W7-7).
- Drop "fails until the key is seeded".

Rows (step ids in order; `svc` = `svc_start`):

**`version`:** unchanged.

**`auth-login`:**

- Steps: `up` (`stack_up`, `wipe = true`), `start-identity` (svc identity), `start-gateway` (svc gateway), `logout` (`auth logout`), `login` (pinned), `status` (`auth status`).
- Expects:
  - `login-ok`: login `/result/stdout` contains `Authenticated successfully`, required.
  - `login-no-secret-echo`: unchanged, required.
  - `status-authenticated`: status stdout contains `Authenticated`, required.
  - `login-exit`: exit eq 0, `required = false`, text ends "(KG-W7-7)".

**`nodes-list`:**

- Steps: `up`, `start-identity`, `start-nodes`, `start-gateway`, `mint` (capture `token_ref = "/token_ref"`, `org_id = "/organization_id"`), `probe` (capture `node_id = "/node_id"`), `logout`, `login`, `list` (`["nodes","list","--org","${org_id}"]`).
- Required expects:
  - `enroll-201`.
  - `login-ok` (contains `Authenticated successfully`).
  - `list-stdout` ne `""`.
  - `list-not-host-refused` not_contains `400 BadRequest`.
  - `list-routed` not_contains `404 NotFound`.
  - `list-token-accepted` not_contains `401 Unauthorized`.
  - `list-authenticated` not_contains `not_authenticated`.
  - `list-no-stack-trace` (stderr not_contains `"   at "`).
- `required = false`:
  - `list-shows-node`: stdout contains `${node_id}`, text "(KG-W7-8, D-ML165)".
  - `list-exit`: eq 0 "(KG-W7-7)".

**`notifications-admin`:**

- Steps: `up`, `start-notifications` (svc notifications), `logs`.
- Expects:
  - `logs-exit` eq 0, required. Notifications returns its code (fact 8).
  - `logs-has-key`: stdout not_contains `Admin API key`, required.
- `expected` loses the "fails until ..." clause.

**`gateway-down`:**

- Steps: `up`, `start-identity`, `start-gateway`, `logout`, `login-control` (pinned), `logout2`, `login-down` (pinned), `status-after` (`auth status`).
- Fault `identity-route`: `start = "after_step:logout2"`, `heal = "after_steps"`.
- Required expects:
  - `control-ok`: login-control stdout contains `Authenticated successfully`.
  - `down-5xx`: login-down stdout matches `Authentication failed:.{0,40}\b5[0-9]{2}\b`.
  - `down-no-success`: login-down stdout not_contains `Authenticated successfully`.
  - `status-after-not-authenticated`: status-after stdout contains `Not authenticated`.
  - `no-stack-trace`: login-down stderr not_contains `"   at "`.
- `required = false`:
  - `login-fails`: exit ne 0 "(KG-W7-7)".

Copy the exact field layout of the existing expects. Use TOML literal strings (`'...'`) for the regex so the backslashes survive.

### Step 9: `src/Tools/Dhadgar.Lab/README.md` (line 78, CLI bullet)

Replace the last two sentences with:

> `auth login` takes the client secret only from argv, so `cli_run` accepts it only as the lab's seeded development client (`IdentityLabAdapter.DevClientId`/`DevClientSecret`, D-ML162). Options are allowlisted per subcommand and `@` arguments are refused. For admin verbs, `cli_run` writes the vault's admin key into the member config for that call and removes it afterwards. The member config points `identity_url` and `nodes_url` at the Gateway's `/api/v1` routes (D-ML163). The CLI drops exit codes for auth and nodes (KG-W7-7), so those rows assert on text.

The bullet's config field list becomes `gateway_url`, `identity_url`, `nodes_url`, `notifications_url`, `discord_url` and, per admin call, `admin_api_key`.

### Checks (in order, non-Docker; `<W>` = `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l37`)

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "(FullyQualifiedName~Dhadgar.Lab.Tests.Cli|FullyQualifiedName~CliSpecTests|FullyQualifiedName~ShippedSpecs|FullyQualifiedName~ServiceAdapterTests|FullyQualifiedName~AuthStackAdapterTests|FullyQualifiedName~NotifyAdapterTests|FullyQualifiedName~BlastRadius|FullyQualifiedName~FaultTargetGuard|FullyQualifiedName~GatewayRouteTable|FullyQualifiedName~LabBuilderTests|FullyQualifiedName~LastBuildReader|FullyQualifiedName~Dhadgar.Lab.Tests.Pool|FullyQualifiedName~LabDaemonServiceLifetimeTests)&RequiresDocker!=true"
```

- `LabDaemonServiceLifetimeTests` and `BlastRadiusTests` build the daemon's DI container. They go red if a new `CliRunTool` parameter does not resolve.
- If a filter term matches no test, say so in the report; do not invent a trait (`RequiresDocker` exists).
- Do not run Docker tests or the full suite (D-ML153).

Hand mutations: run each, record red, restore and rebuild, and list all three in the report.

1. Put `ResolveAsync(found!.TargetId, "cli", ct)` back. Expect `Version_RunsInTheResolvedContainer` red.
2. Drop `identity_url` from `RenderMemberConfig`. Expect `CliGatewaySeamTests` and `RenderMemberConfig_WithoutKey_HoldsTheServiceUrlsOnly` red.
3. Add `"--identity-url"` to the `auth login` value options. Expect `Login_IdentityUrl_IsRefused` red.

Finish with `git status`. Only the named files may change, and no `packages.lock.json` may change; revert and report one if it does.

## 5. Docker and approvals

- **The live `cli.toml` run needs a new yes from the user.** It has never been run, and no agent message is that approval. After the fix pass and green pass-2 reviews, the orchestrator asks the user.
- **What the run needs:**
  - Docker Desktop on context `desktop-linux`;
  - a lab build that includes `cli` (`lab_build`);
  - one Linux container pool member.
- **What the run does:** `lab_uat_run` on `docs/guides/uat-specs/cli.toml`.
- **It is the live half of the D-ML163 seam test and the L37 merge gate:** `version`, `auth-login`, `notifications-admin` and `gateway-down` must pass, and `nodes-list` must pass its required expectations.
- **If the run goes red, these results come back to the architect** (they are not coder fixes):
  - `401 Unauthorized` on `nodes-list`: token audience or issuer, a new finding.
  - `400` or `404`: a seam miss.
  - A `gateway-down` 5xx that does not match the regex.
- `IdentityLabAdapter`, `GatewayAdapter` and `NodesAdapter` changes also reach every Docker test that starts those services (`AuthStackDockerTests`, the nodes enroll specs). They need no extra run for L37, but the wave 7 Docker run (D-ML74) covers them.

## 6. Owner decisions (BlockedDecision)

| ID | Question (plain language) | Recommendation | Until ruled |
| --- | --- | --- | --- |
| D-ML165 | A CLI that signs in as a service (client_credentials) gets a token with no organization in it, and Nodes refuses every org-scoped call without one. So `dhadgar nodes list` after `dhadgar auth login` can never work. Should a service client be able to act inside an organization, and how? | Yes, as a product follow-up outside this campaign: Identity adds `org_id` to client_credentials tokens for a client bound to one organization, and the lab seeds `dev-client` bound to the minted org. Until then the lab only observes it. | `nodes-list` "shows the node" is `required = false` (KG-W7-8). The row still proves the seam (no 400, 404 or 401). |
| D-ML166 | The CLI has four small defects that the lab works around. (1) `auth`, `nodes`, `identity` and `enrollment` commands always exit 0. (2) Its default Identity and Nodes URLs (`/identity`, `/nodes`) match no Gateway route. (3) `auth login` reports success after a failed request when an old token is still valid. (4) Two error messages name an environment variable, `DHADGAR_ADMIN_API_KEY`, that the CLI never reads. Should one small product PR fix them? | Yes: one PR on `main` outside the campaign, like F132's Identity follow-up. Optionally it also reads the client secret from stdin or env, which would retire D-ML162's argv exception. | D-ML164: rows assert on text; KG-W7-7 is open; the member config overrides the URLs. |

## 7. Ledger edits for the orchestrator

| Where | Edit |
| --- | --- |
| README findings | **F135:** "`cli_run` resolved members with fault kind `"cli"`, which the guard does not know, so every call failed before `docker exec`; the exec also used the listing's container name. Evidence: `Tools/Cli/CliRunTool.cs:350,357`, `Faults/FaultTargetGuard.cs:30`. Owner: L37 fix pass 1 (`ResolveMemberForExecAsync`)." |
| README findings | **F136:** "The lab Identity seeded no `dev-client` (no `OpenIddict__DevClient__*` in `IdentityLabAdapter`), and the `cli.toml` header claimed it did. Evidence: `IdentityLabAdapter.cs:89-106`, `Identity/Program.cs:870-878`. Owner: L37 fix pass 1 (D-ML162)." |
| README findings | **F137:** "CLI-to-Gateway seam: the CLI's default `/identity` and `/nodes` URLs match no Gateway route, and the Gateway's `AllowedHosts` refuses `meridian-lab-gateway` (400). Evidence: `CliConfig.cs:55,61`, `Gateway/appsettings.json:14`. Owner: L37 fix pass 1 (D-ML163); product side D-ML166." |
| README findings | **F138:** "The lab Gateway and Nodes fetched OIDC discovery from the public issuer `https://meridianconsole.com/...` (no `Auth__MetadataAddress`/`InternalBaseUrl`), so no lab bearer call could validate and the lab was not hermetic. Evidence: `GatewayAdapter.cs:70-84`, `NodesAdapter.cs:60-68`, `ServiceJwtAuthenticationExtensions.cs:37-98`. Owner: L37 fix pass 1 (D-ML163 c)." |
| README findings | **F139:** "CLI product gaps: auth, nodes, identity and enrollment exit codes are discarded; `auth login` reports success over a stale token; the `DHADGAR_ADMIN_API_KEY` message names an unread variable. Evidence: `Dhadgar.Cli/Program.cs:99-105`, `LoginCommand.cs:102-117`, `SendTestCommand.cs:22`. Owner: D-ML166 (product follow-up, not a campaign packet); lab side D-ML164, KG-W7-7." |
| README findings | **F140:** "A client_credentials token has no `org_id` and Nodes `TenantScoped` requires it, so a CLI service login gets 403 on `nodes list`. Evidence: `Identity/Program.cs:517-620`, `Nodes/Program.cs:170-177`. Owner: D-ML165; KG-W7-8." |
| README findings | **F141:** "The `cli_run` allowlist checked only `args[0..1]`: `--identity-url`, `--opt=value` and `@responsefile` passed, so a caller could choose a credential and where the CLI posts it. Evidence: `CliRunTool.cs:59-100`, `CliAllowlistTests.cs:86`. Owner: L37 fix pass 1 (per-subcommand option table)." |
| README decisions | Add **D-ML162**, **D-ML163** and **D-ML164**, "Accepted (architect)", source [l37 ruling 1](worknotes/l37-ruling-1.md) (text in section 2). Add **D-ML165** and **D-ML166**, "BlockedDecision (owner)". |
| README known gaps | **KG-W7-7:** "CLI auth and nodes verbs exit 0 on failure; `cli.toml` exit expectations are `required = false` \| D-ML166 product fix, then flip them to required \| L37 / L38 catalog". **KG-W7-8:** "`nodes-list` cannot show the node until a service token carries `org_id` \| D-ML165 \| L37". |
| README packet row L37 (line 443) | Status: "Fix pass 1 ([l37 ruling 1](worknotes/l37-ruling-1.md)); security-review; live `cli.toml` run needs the user's yes". |
| README packet row L38 | Append: "inherits D-ML164 (text over exit codes) and KG-W7-7". |
| work-packets.md L37 (line 3739) | Add under the heading: "Amended by [l37 ruling 1](worknotes/l37-ruling-1.md) (D-ML162 to D-ML164): dev client seeded and pinned, member config routes through the Gateway's `/api/v1` paths, per-subcommand allowlist, admin key per call." |
| next-batch-plan.md (L37 entry) | Same amendment line. |
| RESUME | Next free IDs: **F142, D-ML167, KG-W5-24, KG-W7-9**. L37 next steps: fix pass 1 → reviewer and security pass 2 → user's yes for the live `cli.toml` run → merge. Add D-ML165 and D-ML166 to the owner-decision table with the section 6 recommendations. |
| RESUME and README (carry-over) | The l35m ruling's ledger edits (F133, F134, D-ML160, D-ML161, KG-W7-6) are not in README or RESUME yet; RESUME line 3 still says next free F133. Apply those first so the IDs stay in order. |
| PR | `security-review` label (reviewer D3, security verdict). |

## Addendum 1

Architect, 2026-11-02. Worktree `ml-l37`, branch `ml/l37-cli`, head `3bbbafc5`. Input: fix report `ml-l37\.claude\reports\meridian-lab\l37-fix-1.md`, which stopped on two failing existing tests outside section 4's file list.

**Outcome:** one short fix pass (Sonnet packet-coder, about 10k) on the same branch, then packet-reviewer pass 2 and security-advisor pass 2 as section 4 already says. No owner decision. **IDs:** F142, D-ML167. Next free after this addendum: **F143, D-ML168, KG-W5-24, KG-W7-9**.

### A1.1 Facts

| # | Fact | Evidence |
| --- | --- | --- |
| A1 | `cli_run` is registered (`CliArea` → `AddLabTool<CliRunTool>`) but still listed in `ToolPolicy.PlannedTools` under `// L37`. `LabDaemonServiceLifetimeTests` expects registered = `Gates.Keys` minus `PlannedTools`, so it is red. The README rule for packets is already right ("Remove its name from your `// Lnn` group ... and keep the comment line"). | `Policy/ToolPolicy.cs:130-131`; `tests/.../Daemon/LabDaemonServiceLifetimeTests.cs:63-67`; `src/Tools/Dhadgar.Lab/README.md:356` |
| A2 | `EveryLeasedToolWithATarget_ImplementsIFaultTool` requires `IFaultTool` for every Leased tool whose schema has `target`, `proxy`, `member`, `database` or `file`. `cli_run` is Leased with `member`. | `tests/.../Faults/BlastRadiusTests.cs:26-46`; `ToolPolicy.cs:80` |
| A3 | Making `cli_run` an `IFaultTool` is wrong. `IFaultTool` means "has a fault kind", which ruling step 1 kept out (F135). It would also enter `EveryFaultTool_RefusesAnUnsafeTarget`, which wants `[Lab.BlastRadius]` or `[Lab.InvalidArgument]` from a daemon call. `cli_run` refuses `c:`, `p:`, `d:` and `m:host` with `[Lab.Refused]` in `CheckMember`, after `PoolManager.ListAsync`, which probes the real providers (`IsAvailableAsync`, Docker). | `CliRunTool.cs:542-568,575-583`; `Pool/PoolManager.cs:307-318`; `BlastRadiusTests.cs:54-85` |
| A4 | The guard call is already proved with fakes and a real `FaultTargetGuard`: `CliRunToolTests.UnlabelledMember_IsBlastRadius_AndNeverExecs` (three cases) asserts `[Lab.BlastRadius]` and no `exec`. It goes red if the `ResolveMemberForExecAsync` block is replaced by the listing's container name. | `tests/.../Cli/CliRunToolTests.cs:41-73`; `CliRunTool.cs:591-601` |
| A5 | `cli_run` is the first Leased member-targeting tool that is not a fault. L32 (`agent_install`, `agent_uninstall`, `agent_start`, `agent_stop`, `agent_token_write`, `agent_config_set`) and L43 (`agent_msi_install`, `agent_msi_uninstall`) will be next, all Leased with `member`. | `work-packets.md` C4 rows 353-360; `ToolPolicy.cs:118-136` |
| A6 | Both tests were red since L37's first pass. The coder ran only the `Dhadgar.Lab.Tests.Cli` filter, review pass 1 ran no daemon tests, and section 4's file list did not name `ToolPolicy.cs` or `BlastRadiusTests.cs`. Ruling 1's check filter caught them. | `l37-coder.md:24`; `l37-fix-1.md:22-30`; section 4 "Checks" |

### A1.2 Finding and decision

| ID | Text |
| --- | --- |
| F142 | "L37 registered `cli_run` but left it in `ToolPolicy.PlannedTools` and outside the `BlastRadiusTests` rule, so `LabDaemonServiceLifetimeTests` and `EveryLeasedToolWithATarget_ImplementsIFaultTool` were red from the first pass. Its checks ran only the Cli filter. Evidence: `ToolPolicy.cs:130-131`, `BlastRadiusTests.cs:28-46`, `l37-coder.md:24`. Owner: L37 ruling 1 addendum 1." |
| D-ML167 | **A Leased tool that targets a pool member but injects no fault gets a named entry in `BlastRadiusTests.GuardedExecTools`, not `IFaultTool`.** Each entry must hold in a test: **(a)** the tool is registered, Leased, has a `member` property and is not an `IFaultTool` (a stale entry goes red); **(b)** its single public constructor takes a `FaultTargetGuard`; **(c)** the entry names a test class and method that exist in the test assembly and prove, with fakes and a real `FaultTargetGuard`, that an unlabelled or out-of-context member is refused `[Lab.BlastRadius]` with no `exec`. **Why:** `IFaultTool` means a fault kind, which `cli_run` must not have (F135); the daemon-level refusal theory cannot reach the guard without Docker (A3); a bare name skip would let the next tool through unguarded. **Not now:** a production marker interface. With one tool it adds a type and proves nothing more; L32 decides whether to promote the list when it adds the agent tools (A5). L32 also rewords `ResolveMemberForExecAsync`'s "cli_run runs only on a pool member" message if it reuses the method. |

### A1.3 Fix packet (one Sonnet packet-coder, about 10k)

- **Worktree / branch:** `ml-l37` / `ml/l37-cli`, on head `3bbbafc5`.
- **Commit subject:** `fix(lab): apply L37 ruling 1 addendum 1, planned list and guarded exec tools [L37]`
- **Security review:** yes; this diff is in security-advisor pass 2's scope (it changes the blast-radius gate).
- **Rules (inline):** edit only the three files below (the hand mutations touch `CliRunTool.cs` and `ToolPolicy.cs` temporarily and must be restored). No package or `.csproj` changes, no `ProjectReference` to `Dhadgar.Cli`. Nullable is on; build with `-p:TreatWarningsAsErrors=true`. xUnit `[Fact]`/`Assert.*`, no mocks. No test message echoes a secret. Never run `git worktree prune`, `publish -r` or `restore -r`.

**Step 1: `src/Tools/Dhadgar.Lab/Policy/ToolPolicy.cs`.** In `PlannedTools`, replace

```csharp
        // L37
        "cli_run",
```

with

```csharp
        // L37 (registered)
```

No other line changes. `GateTable` keeps `["cli_run"] = LabGate.Leased`.

**Step 2: `tests/Dhadgar.Lab.Tests/Faults/BlastRadiusTests.cs`.**

Add below `TargetProperties`:

```csharp
    /// <summary>
    /// Leased tools that take a <c>member</c> but inject no fault: they exec in a pool member and resolve it through
    /// <see cref="FaultTargetGuard.ResolveMemberForExecAsync"/>, which has no fault kind, so <c>fault_list_targets</c> never
    /// lists them (D-ML167). Each entry names the fake-backed test that proves the guard refuses an unsafe member before any
    /// exec. A new entry needs the same proof; it is not a skip list.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string TestType, string TestMethod)> GuardedExecTools =
        new Dictionary<string, (string TestType, string TestMethod)>(StringComparer.Ordinal)
        {
            ["cli_run"] = ("Dhadgar.Lab.Tests.Cli.CliRunToolTests", "UnlabelledMember_IsBlastRadius_AndNeverExecs"),
        };
```

In `EveryLeasedToolWithATarget_ImplementsIFaultTool`, change the condition to

```csharp
                if (leased && HasTargetProperty(tool.InputSchema) && tool is not IFaultTool && !GuardedExecTools.ContainsKey(tool.Name))
```

Add after that test:

```csharp
    [Fact]
    public async Task EveryGuardedExecTool_IsALeasedMemberTool_ThatTakesTheGuard_AndHasAGuardTest()
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);
            ILabTool[] tools = daemon.Services.GetServices<ILabTool>().ToArray();

            foreach (KeyValuePair<string, (string TestType, string TestMethod)> entry in GuardedExecTools)
            {
                string name = entry.Key;
                (string testType, string testMethod) = entry.Value;
                ILabTool? tool = tools.SingleOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
                Assert.True(tool is not null, name + " is not registered");
                Assert.Equal(LabGate.Leased, ToolPolicy.GateFor(name));
                Assert.True(tool!.InputSchema.TryGetProperty("properties", out JsonElement properties) && properties.TryGetProperty("member", out _), name + " has no member property");
                Assert.False(tool is IFaultTool, name + " is an IFaultTool; remove its GuardedExecTools entry");

                var constructor = Assert.Single(tool.GetType().GetConstructors());
                Assert.Contains(constructor.GetParameters(), p => p.ParameterType == typeof(FaultTargetGuard));

                Type? proof = typeof(BlastRadiusTests).Assembly.GetType(testType);
                Assert.True(proof?.GetMethod(testMethod) is not null, name + " cites a guard test that does not exist");
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }
```

Update the class `<summary>`: "... every mutating tool that takes a target implements <see cref="IFaultTool"/> or is a guarded exec tool (D-ML167), and every fault tool refuses a target the guard must never let through."

**Step 3: `src/Tools/Dhadgar.Lab/README.md`, section "Faults and blast radius".** After the first bullet (line 138), add:

> - A Leased tool that execs in a pool member without injecting a fault (`cli_run`) resolves the member through `FaultTargetGuard.ResolveMemberForExecAsync`: host refusal, registry, member kind, then the same inspect and tripwire checks. It has no fault kind, so `fault_list_targets` never lists it. `BlastRadiusTests.GuardedExecTools` names each such tool and the test that proves its guard refusal (D-ML167).

**Tests and why they go red on revert:**

| Test | Red when |
| --- | --- |
| `LabDaemonServiceLifetimeTests.Services_BuildWithScopeAndBuildValidation_ResolveEveryLabService` | `"cli_run"` is put back in `PlannedTools` (today's red). |
| `EveryLeasedToolWithATarget_ImplementsIFaultTool` | The `cli_run` entry is removed from `GuardedExecTools` (today's red). |
| `EveryGuardedExecTool_IsALeasedMemberTool_ThatTakesTheGuard_AndHasAGuardTest` (new) | The entry names an unregistered tool, the tool loses its `FaultTargetGuard` parameter or `member` property, becomes an `IFaultTool`, or the cited test is renamed or deleted. |
| `CliRunToolTests.UnlabelledMember_IsBlastRadius_AndNeverExecs` (existing, the cited proof) | The `ResolveMemberForExecAsync` block in `CliRunTool.ExecuteAsync` is removed. |

**Checks (in order; `<W>` = `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l37`):**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "(FullyQualifiedName~BlastRadiusTests|FullyQualifiedName~LabDaemonServiceLifetimeTests|FullyQualifiedName~ToolPolicyTests|FullyQualifiedName~CliRunToolTests)&RequiresDocker!=true"
```

Then rerun the section 4 "Checks" filter unchanged. Expect Failed 0 and one more test than the fix-1 total. Do not run Docker tests or the full suite (D-ML153).

**Hand mutations** (run each, record red, restore, rebuild; list all four in the report):

1. Put `"cli_run",` back under `// L37` in `PlannedTools`. Expect `LabDaemonServiceLifetimeTests` red.
2. Delete the `["cli_run"]` entry from `GuardedExecTools`. Expect `EveryLeasedToolWithATarget_ImplementsIFaultTool` red.
3. Change the cited method to `UnlabelledMember_IsBlastRadius_AndNeverExecsX`. Expect the new test red.
4. In `CliRunTool.ExecuteAsync`, replace lines 591-601 (the guard block) with `string container = found!.ContainerName!; _ = _guard;`. Expect all three `UnlabelledMember_IsBlastRadius_AndNeverExecs` cases red. Build this mutation without `-p:TreatWarningsAsErrors=true` if an analyzer still objects.

Finish with `git status`: only the three files change, and no `packages.lock.json`. Report to `<W>\.claude\reports\meridian-lab\l37-fix-1a.md`.

### A1.4 Other lists and docs

- **No other planned-tool list changes.** `PlannedTools` is the only one. `ToolPolicyTests.PolicyTable_HasNoUnknownNames` reads it and stays green (`cli_run` is registered). `InvariantsWiringTests` checks only `invariants_check`.
- **Lab README:** the "Tool tiers" row (`cli_run | Leased | yes, lease_id required`, line 232) and the packet rule at line 356 are already right. Only the step 3 bullet is new.
- **Ledger edits for the orchestrator** (meridian-lab worktree, not the coder):

| Where | Edit |
| --- | --- |
| README findings | Add **F142** (text in A1.2). |
| README decisions | Add **D-ML167**, "Accepted (architect)", source [l37 ruling 1, addendum 1](worknotes/l37-ruling-1.md). |
| work-packets.md line 31 ("Blast radius" rule) | Append: "A Leased tool that execs in a member without a fault kind goes in `BlastRadiusTests.GuardedExecTools` with a guard-refusal test instead (D-ML167)." |
| work-packets.md L32 and L43 blocks | Add: "The agent tools are Leased with `member` and are not faults: add each to `BlastRadiusTests.GuardedExecTools` with its guard test, or promote the list to a marker interface (D-ML167). Reword `ResolveMemberForExecAsync`'s cli-specific refusal if reused." |
| README packet row L37 | Append: "addendum 1 (F142, D-ML167)". |
| RESUME | Next free IDs: **F143, D-ML168, KG-W5-24, KG-W7-9**. L37 next step: addendum 1 fix pass, then pass-2 reviews. |

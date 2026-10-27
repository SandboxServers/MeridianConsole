# meridian-lab: work packets

Ledger: [README.md](README.md). Resume card: [RESUME.md](RESUME.md).

## Dispatch rules

- **Campaign branch.** `campaign/meridian-lab`, cut from `main`, worktree `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab`. Nothing is pushed until the end (D-ML16).
- **Packet worktree.** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab-lNN` on branch `ml/lNN-<slug>`, cut from the campaign tip once the packet's dependencies are merged there. In this file `<W>` means that absolute worktree path.
- **Report.** `<W>\.claude\reports\meridian-lab\LNN-<role>.md`. Return one line: `<packet> <done|blocked|failed> <commit-sha|-> <report-path> <reason>`.
- **Commit.** One local commit with the packet's subject; the body ends with:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Wz3WjKW6AyWbyMqsoRqG8
```

- **Merge.** At `ship` (reviewer and security-advisor), a Sonnet `general-purpose` agent merges the packet branch into `campaign/meridian-lab` with `--no-ff` and reports the new tip. No push, no PR per packet or wave.
- **The one PR.** After the campaign review and L47, ops pushes `campaign/meridian-lab` once and opens one PR to `main` with the `security-review` label. The body ends with:

```text
🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_015Wz3WjKW6AyWbyMqsoRqG8
```

- **Security.** Every code packet gets a `security-advisor` pass beside the reviewer. Doc packets get a security-advisor read.
- **Red seams.** Seam tests carry `[Trait("Seam","S-Lx")]`. A packet's `dotnet test` filter excludes seams the README lists as still red; the packet that turns a seam green removes its exclusion.
- **Lock files.** `RestorePackagesWithLockFile=true`. A packet that adds a project or a package runs `dotnet restore <csproj> --force-evaluate` and commits every changed `packages.lock.json`.
- **Docker, Windows containers and network.** Tests tagged `RequiresDocker` need Docker Desktop's Linux engine; `RequiresWindowsContainers` also needs `MERIDIAN_LAB_WINDOWS_CONTAINERS=1` and the Windows engine named by L00; `RequiresNetwork` pulls images or packages. CI (ubuntu-24.04) excludes all three with `--filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"`. The packet-coder runs them locally when the packet's checks list them. Nothing in the lab needs elevation; the daemon refuses to run elevated (D-ML12 is superseded by the container pool, D-ML29; the Machine gate is reserved, D-ML43).
- **L00 is not a code packet.** A Sonnet `general-purpose` agent runs it and writes a worknote; no worktree, no review loop beyond the architect reading the outcome.
- **Blast radius.** Every fault, kill, pause, fill, corruption or reset goes through `FaultTargetGuard` (R-BLAST). A packet that adds a mutating tool adds it to `BlastRadiusTests` in the same commit, or the test fails.
- **Stop rule.** If an API named here does not exist with the given shape (MCP SDK, Tomlyn, Nodes internals), adapt the call site only, keep every contract type in this file unchanged, and list the difference in the report. If the contract itself cannot hold, return `blocked spec`.

## Repo rules (short form; each packet repeats the ones it needs)

| ID | Rule |
| --- | --- |
| R-RESULT | Return `Result` or `Result<T>` (`Dhadgar.Shared.Results`) for validation, IO, network and process errors; error strings are `"[Lab.Code] message"`. Never throw for those. |
| R-PATH | Every path from config, tool arguments or specs goes through `IPathValidator.ValidatePath(path, allowedBasePaths)` (`Dhadgar.Agent.Core.Files`) against `LabPaths` roots. |
| R-ARGS | Processes use `ProcessStartInfo.ArgumentList` with `UseShellExecute = false`; never build an `Arguments` string. No exceptions. |
| R-HOST | Outbound HTTP goes only to `LabHosts.IsLabEndpoint(uri)` (loopback, lab ports); anything else is `Lab.Untrusted`. No redirects. |
| R-ZERO | `CryptographicOperations.ZeroMemory` on token, key and password buffers after use; secrets are held as `char[]`/`byte[]`, not `string`, inside the vault. |
| R-USING | `using` on every `IDisposable`: `Process`, `X509Store`, `X509Certificate2`, `RegistryKey`, `NpgsqlConnection`, pipes, `HttpResponseMessage`. |
| R-CPM | No `Version=` in any `.csproj`; versions only in `Directory.Packages.props`; commit lock-file changes. |
| R-NULL | Nullable on. Build with `-p:TreatWarningsAsErrors=true`. CI also runs `AnalysisMode AllEnabledByDefault`: fix CA warnings rather than suppressing them; justify any `#pragma` in the report. |
| R-XUNIT | xUnit v2: `[Fact]`/`[Theory]`, plain `Assert`, names `Subject_Condition_Expectation`. Windows tests: `[SkippableFact]` plus `Skip.IfNot(OperatingSystem.IsWindows())`. Docker: `[Trait("RequiresDocker","true")]`. Windows containers: `[Trait("RequiresWindowsContainers","true")]` plus `Skip.IfNot(Environment.GetEnvironmentVariable("MERIDIAN_LAB_WINDOWS_CONTAINERS") == "1")`. Image or package pulls: `[Trait("RequiresNetwork","true")]`. Seeded property tests use CsCheck with a fixed seed in the test (`Gen...Sample(..., seed: "...")`) so CI replays the same cases. |
| R-LOG | Never log tool arguments, tokens, keys, passwords, certificates or connection strings. Anything that might carry a secret passes through `Redactor` first. |
| R-OS | Windows APIs sit behind `[SupportedOSPlatform("windows")]` and an `OperatingSystem.IsWindows()` check; on other OSes the tool returns `[Lab.Unavailable] ...`. |
| R-TIME | Inject `TimeProvider`; never `DateTime.UtcNow`/`DateTimeOffset.UtcNow` in logic. Tests use `FakeTimeProvider`. |
| R-CONTRACT | Cross-service types live only in `src/Shared/Dhadgar.Contracts`. The lab calls product types; it never copies them. |
| R-DI | `LabDaemonServiceLifetimeTests` builds the daemon's service provider with `ValidateScopes = true, ValidateOnBuild = true`; every packet that registers services keeps it green. |
| R-BLAST | A mutating tool resolves its target only through `FaultTargetGuard.Resolve(targetId)`: registry membership, live `docker inspect` (label `meridian-lab=1`, name prefix `meridian-lab-`, lab context) and the tripwire (refuse names or ids matching `postgres`, `cache`, `messaging`, `dhadgar*` without the lab prefix, and any volume). A refusal is `[Lab.BlastRadius] ...`. Never `docker volume`, `docker system`, `docker rm -v` or a name built from user input without the guard. |
| R-SEED | Every random choice in a run, hunt, fuzz, chaos, soak or fleet comes from `LabRandom(runSeed, stream)` (C17). `System.Random.Shared` and `Guid.NewGuid()` for data are refused by review. Secrets (tokens, passwords) still come from `RandomNumberGenerator` and are never part of a replay. |
| R-OBSERVE | Lab observations (readiness, `db_query`, invariant reads, log follow, RabbitMQ management) use the direct host ports, never a Toxiproxy proxy, so a fault never blinds the grader. |
| R-DOCKER | Every docker call is `docker --context <Lab:Docker:LinuxContext or WindowsContext> ...` through `IDockerCli` with `ArgumentList`; ports bind to `127.0.0.1`; containers carry `--label meridian-lab=1 --restart no` and the `meridian-lab-` name prefix; no named volumes. |

## Contract

### C1. Paths, ports, environment

```csharp
namespace Dhadgar.Lab;

public static class LabDefaults
{
    public const int DaemonPort = 8789;
    public const string TokenEnvVar = "MERIDIAN_LAB_TOKEN";
    public const string HomeEnvVar = "MERIDIAN_LAB_HOME";          // overrides LabPaths.Root (tests)
    public const string WindowsContainersTestsEnvVar = "MERIDIAN_LAB_WINDOWS_CONTAINERS";
    public const string ContainerPrefix = "meridian-lab-";
    public const string ContainerLabel = "meridian-lab";            // value "1"
    public const string NetworkName = "meridian-lab-net";
    public const int MinTokenBytes = 32;
    public const string McpPath = "/mcp";
    public const string StatusPath = "/status";
}

public static class LabPorts
{
    // Services (host side, observation path; C10)
    public const int Gateway = 15000;
    public const int GatewayTls = 15443;          // L41
    public const int Identity = 15010;            // L34
    public const int Nodes = 15040;
    public const int Notifications = 15090;       // L36
    public const int Discord = 15120;             // L36
    public const int BetterAuth = 15130;          // L34
    public const int Panel = 14321;               // L34
    public const int AgentIngress = 25000;        // Toxiproxy agent-ingress -> gateway
    public const int AgentIngressTls = 25443;     // L41
    public const int Barrier = 25080;             // L49: in-daemon lab-only HTTP barrier proxy -> AgentIngress (C10)
    // Infrastructure (host side)
    public const int Postgres = 15432;
    public const int RabbitMq = 15673;
    public const int RabbitMqManagement = 16672;  // L21
    public const int Redis = 16379;
    public const int ToxiproxyApi = 18474;
    public const int Sink = 18090;                // L36
    public static readonly IReadOnlySet<int> ServicePorts = new HashSet<int>
        { Gateway, GatewayTls, Identity, Nodes, Notifications, Discord, BetterAuth, Panel, AgentIngress, AgentIngressTls, Barrier };
    public static readonly IReadOnlySet<int> InfraPorts = new HashSet<int>
        { RabbitMqManagement, ToxiproxyApi, Sink };     // HTTP observation endpoints only
}

public static class LabIds
{
    public static readonly Guid OrganizationId = new("4d4c4142-0000-4000-8000-000000000001");
    public static readonly Guid TenantBOrganizationId = new("4d4c4142-0000-4000-8000-000000000002"); // L56 twin variant tenant
    public const string CreatedByUserId = "meridian-lab";
}

public sealed class LabPaths
{
    // Root: %LOCALAPPDATA%\meridian-lab on Windows; $XDG_STATE_HOME/meridian-lab or ~/.local/state/meridian-lab elsewhere;
    // MERIDIAN_LAB_HOME overrides.
    public string Root { get; }
    public string Bin => Path.Combine(Root, "bin");
    public string Builds => Path.Combine(Root, "builds");        // builds/<build8>/<project>
    public string Logs => Path.Combine(Root, "logs");            // logs/<process>.jsonl
    public string Audit => Path.Combine(Root, "audit");          // audit/audit-<yyyyMMdd>.jsonl
    public string UatRuns => Path.Combine(Root, "uat-runs");
    public string Config => Path.Combine(Root, "config");        // config/<service>.json overlays
    public string State => Path.Combine(Root, "state");          // state/nodes-ca, targets.json, faults.json, ...
    public string Results => Path.Combine(Root, "results");      // results/lab-results.db (L25)
    public string Triage => Path.Combine(Root, "triage");        // triage/ledger.md, triage/drafts/<bucket>.md (L26)
    public string Playwright => Path.Combine(Root, "playwright");// browsers (L35)
    public string FaultBackups => Path.Combine(Root, "fault-backups"); // corrupted-file originals (L20)
    public string Jobs => Path.Combine(Root, "jobs");            // jobs/<jobId>/ (L27)
    public string PidFile => Path.Combine(Root, "daemon.pid");
    public string RepoRoot { get; }                              // Lab:RepoRoot, set by install.ps1
    public static LabPaths Create(IConfiguration config);
}

public static class LabHosts
{
    // true only for http/https, host 127.0.0.1 | localhost | [::1], port in LabPorts.ServicePorts
    public static bool IsLabEndpoint(Uri uri);
    // true only for http, loopback host, port in LabPorts.InfraPorts (Toxiproxy API, RabbitMQ management, sink)
    public static bool IsLabInfraEndpoint(Uri uri);
}
```

The daemon reads its own settings from `labsettings.json` next to the exe, then environment. It never reads `appsettings.json` (referenced web projects copy theirs into the output).

### C2. HTTP edge

| Route | Methods | Auth | Success | Refusals |
| --- | --- | --- | --- | --- |
| `/mcp` | POST, GET, DELETE (MCP streamable HTTP, stateless) | bearer | per MCP | see order below |
| `/status` | GET | bearer | 200 `StatusDto` | see order below |
| anything else | any | bearer | 404 | see order below |

Refusal order, applied to every request before routing:

1. `Host` not in `{127.0.0.1:<port>, localhost:<port>, [::1]:<port>}` (port = the bound port) → **403**, empty body.
2. Any `Origin` header present → **403**, empty body.
3. Missing, malformed or wrong `Authorization: Bearer <token>` → **401**, empty body, header `WWW-Authenticate: Bearer`. Same response for every cause.

```csharp
namespace Dhadgar.Lab.Daemon;

public sealed record StatusDto(
    string Version, int Pid, DateTimeOffset StartedAt,
    LeaseStatusDto Lease,
    string StackState,                       // "down" | "starting" | "up" | "degraded"
    IReadOnlyList<ProcessStatusDto> Processes,
    int ActiveFaults,                        // 0 until L19
    IReadOnlyList<string> LeasedMembers,     // empty until L30
    string? RunningJob);                     // job id, null until L27
public sealed record LeaseStatusDto(bool Held, string? Owner, string? Purpose, DateTimeOffset? Since, DateTimeOffset? ExpiresAt); // never the id
public sealed record ProcessStatusDto(string Name, string State, int? Pid, int Restarts, DateTimeOffset? StartedAt);

public sealed class LabDaemon : IAsyncDisposable
{
    public static Task<Result<LabDaemon>> StartAsync(LabDaemonOptions options, CancellationToken ct);
    public Uri BaseAddress { get; }          // http://127.0.0.1:<bound port>/
    public IServiceProvider Services { get; }
}
public sealed record LabDaemonOptions(
    int Port, string Token, string DataRoot,
    bool EnforceSingleInstance = true, bool RefuseElevated = true,
    TimeProvider? Time = null, string? RepoRoot = null);
```

Exit codes of `meridian-lab daemon run`: 0 clean stop, 2 config refusal (short token, non-loopback bind, elevated token), 3 already running.

### C3. Core types

```csharp
namespace Dhadgar.Lab.Policy;
public enum LabGate { Open, Leased, OwnLease, Machine }

public static class ToolPolicy
{
    // The only table of gates. One entry per tool name in C4.
    public static IReadOnlyDictionary<string, LabGate> Gates { get; }
    public static LabGate? GateFor(string toolName);   // null => unclassified => refused
}

namespace Dhadgar.Lab.Tools;
public sealed record LabCallContext(string CallId, string? LeaseId, string Caller); // Caller: "mcp" | "uat:<runId>" | "cli"

public interface ILabTool
{
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }
    Task<Result<JsonElement>> InvokeAsync(JsonElement arguments, LabCallContext context, CancellationToken ct);
}

// Base for every tool: parses TArgs with LabJson.Options
// (snake_case, UnmappedMemberHandling.Disallow), builds InputSchema with JsonSchemaExporter,
// maps parse failures to [Lab.InvalidArgument].
public abstract class LabTool<TArgs> : ILabTool where TArgs : class
{
    protected abstract Task<Result<object>> ExecuteAsync(TArgs args, LabCallContext context, CancellationToken ct);
}

public sealed record LabToolResult(bool IsError, JsonElement Payload); // payload already redacted

public sealed class LabToolDispatcher
{
    // 1 resolve tool (unknown => Lab.UnknownTool)
    // 2 gate = ToolPolicy.GateFor (null => Lab.Unclassified)
    // 3 Leased: args.lease_id must equal the current lease (Lab.LeaseRequired / Lab.LeaseHeld), then Touch
    //   OwnLease: the tool manages the lease itself
    //   Machine: reserved; always Lab.MachineReserved (no tool carries it; D-ML43)
    // 4 audit begin (no args)  5 invoke  6 Redactor.Redact(payload)  7 audit end
    public Task<LabToolResult> InvokeAsync(string toolName, JsonElement arguments, LabCallContext context, CancellationToken ct);
}

namespace Dhadgar.Lab.Leasing;
public sealed record LeaseOptions(int DefaultTtlSeconds = 600, int MinTtlSeconds = 30, int MaxTtlSeconds = 3600, int HistorySize = 50);
public sealed record LeaseInfo(string Owner, string Purpose, DateTimeOffset Since, DateTimeOffset ExpiresAt, int TtlSeconds);
public sealed record LeaseGrant(string LeaseId, LeaseInfo Info);
public enum LeaseEndReason { Released, Expired, TakenOver }
public sealed record LeaseHistoryEntry(string Owner, string Purpose, DateTimeOffset Since, DateTimeOffset EndedAt,
    LeaseEndReason Reason, string? TakenOverBy, string? TakeoverReason);

public sealed class LeaseBook
{
    public LeaseBook(TimeProvider time, ILabEvents events, LeaseOptions options);
    public Result<LeaseGrant> Acquire(string owner, string purpose, int? ttlSeconds, bool force, string? reason);
    public Result<LeaseInfo> Renew(string leaseId, int? ttlSeconds);
    public Result Release(string leaseId);
    public Result<LeaseInfo> Touch(string leaseId);        // every guarded call; extends ExpiresAt by the lease's ttl
    public LeaseInfo? Current { get; }
    public IReadOnlyList<LeaseHistoryEntry> History { get; }
    public int Sweep();                                    // expires an overdue lease; returns 0 or 1
    public event EventHandler<LeaseHistoryEntry>? LeaseEnded;
}

namespace Dhadgar.Lab.Audit;
public sealed record LabEvent(string Name, DateTimeOffset At, IReadOnlyDictionary<string, string?> Fields); // Name e.g. "lab.lease", "lab.call"
public interface ILabEvents { void Emit(LabEvent labEvent); }
public sealed class NullLabEvents : ILabEvents { public void Emit(LabEvent labEvent) { } }

namespace Dhadgar.Lab.Secrets;
public enum SecretKind { EnrollmentToken, PrivateKey, Password, BearerToken }
public sealed class SecretVault : IDisposable
{
    public string Add(SecretKind kind, ReadOnlySpan<char> value);            // returns "tok-1", "key-1", "pwd-1", "brr-1"
    public Result<T> Use<T>(string secretRef, Func<ReadOnlySpan<char>, T> use);
    public bool Contains(string secretRef);
    public void Remove(string secretRef);                                    // zeroes
    public void Dispose();                                                   // zeroes all
}
public sealed class Redactor
{
    public Redactor(SecretVault vault);
    public string Redact(string text);          // vault values (length >= 8) -> "***<ref>"; PEM private key blocks;
                                                // "Bearer <x>"; Password=/Pwd= segments; the daemon token
    public JsonElement Redact(JsonElement json);
}
```

Error JSON (the payload when `IsError` is true):

```json
{ "error": { "code": "Lab.LeaseHeld", "message": "lease held by s1 for \"uat\" since 2026-11-02T10:00:00Z, expires 2026-11-02T10:10:00Z", "holder": { "owner": "s1", "purpose": "uat", "since": "2026-11-02T10:00:00Z", "expires_at": "2026-11-02T10:10:00Z" } } }
```

`holder` appears only for `Lab.LeaseHeld`. The code comes from the `[Lab.Code]` prefix of the `Result` error.

### C4. Tool catalogue

Every Leased tool takes a required `lease_id` string; it is not repeated below. Arguments and results are snake_case JSON. `target` in a fault tool is always a lab target id from `fault_list_targets` (C10), never a container name, PID, volume or path. `member` is a pool member id from `pool_acquire` (C13).

| Tool | Gate | Arguments | Result | Packet |
| --- | --- | --- | --- | --- |
| `lab_status` | Open | - | `StatusDto` | L05 |
| `lab_doctor` | Open | - | `{checks:[{name, ok, detail}]}` | L12, L31 |
| `lab_lease_acquire` | Open | `owner`, `purpose`, `ttl_s?`, `force?`, `reason?` | `{lease_id, owner, purpose, since, expires_at}` | L05 |
| `lab_lease_renew` | Open | `lease_id`, `ttl_s?` | `LeaseInfo` | L05 |
| `lab_lease_release` | Open | `lease_id` | `{released:true}` | L05 |
| `lab_lease_status` | Open | - | `{current: LeaseInfo?, history:[...]}` | L05 |
| `logs_tail` | Open | `process` (C10 service or infra name, `daemon`, or `member:<id>`), `since?` (ISO or `-5m`), `contains?`, `level?`, `category?`, `max_lines?` (200, max 2000) | `{lines:[{ts, level, category, event_id, message}]}` | L07 |
| `stack_status` | Open | - | `{state, network, containers:[{target_id, name, image, ports, state, restarts}]}` | L08 |
| `db_query` | Open | `database` (`nodes`, `identity`, `notifications`, `discord`, `betterauth`), `sql`, `params?`, `max_rows?` (100, max 500) | `{columns, rows, row_count, truncated}` | L10 |
| `certstore_list` | Open | `store` (`machine_my`, `machine_root`, `agent_ca`, `current_user_my`), `member?`, `subject_contains?`, `issuer_contains?`, `issuer_thumbprint?` | `{certs:[{thumbprint, subject, issuer, serial, not_before, not_after, has_private_key}]}` | L12, L32 |
| `registry_read` | Open | `key` (`agent`), `member?` | `{exists, values:[{name, kind, present, length}], acl:[{sid, rights}]}`; never a value | L12, L32 |
| `state_file_read` | Open | `file` (`agent_state`), `member?` | `{exists, path, last_write, json}` (redacted) | L12, L32 |
| `uat_list_specs` | Open | `specs_dir?` | `{specs:[{file, section, rows, sha256}]}` | L13 |
| `uat_plan` | Open | `specs`, `sections?`, `rows?` | `{ok, rows:[{section, id, steps, clauses, faults, depends_on}], errors:[{file, line, message}]}` | L13 |
| `uat_report` | Open | `run_id?` (default latest) | `{run_id, bundle, seed, overall, summary, new_failures, known_failures, violations, rows:[{section, id, grade, reason}]}` | L16, L22 |
| `bundle_read` | Open | `run_id`, `path` (relative, inside the bundle) | `{path, text}` | L16 |
| `fault_list_targets` | Open | `kind?` (`container`, `proxy`, `database`, `file`, `member`) | `{targets:[{target_id, kind, name, service, faults_allowed:[...]}]}` | L19 |
| `faults_active` | Open | - | `{faults:[{fault_id, kind, target_id, started_at, deadline, args}]}` | L19 |
| `invariants_check` | Open | `since?` (default last checkpoint) | `{checked_at, violations:[{invariant, service, signature, detail, at}]}` | L21 |
| `results_query` | Open | `view` (`runs`, `rows`, `buckets`, `flakes`, `trend`, `known_issues`, `findings`), `filter?` (object), `limit?` (50, max 500) | `{view, items:[...]}` | L25, L26 |
| `triage_show` | Open | `bucket_id` | `{bucket, signature, hits, first_seen, last_seen, runs, known_issue, repro_path, draft_path}` | L26 |
| `job_status` | Open | `job_id?` (default all) | `{jobs:[{job_id, kind, state, started_at, progress, result?}]}` | L27 |
| `pool_list` | Open | - | `{providers:[{kind, available, reason}], members:[PoolMember]}` | L30 |
| `ui_flow_list` | Open | - | `{flows:[{name, description}]}` | L35 |
| `lab_build` | Leased | `checkout` (repo root or under `<repo>\.claude\worktrees\`), `projects?` (C10 build projects), `baseline?` (L60: a name from `tools/lab/baselines.toml`; then `checkout` is ignored and the build comes from `git archive <sha>`), `mutant?` (L54: a lab-only mutant id, C19) | `{build_id, commit, dirty, path, projects, images, duration_ms, baseline?, mutant?}` | L09, L54, L60 |
| `stack_up` | Leased | `wipe?` | `stack_status` result | L08 |
| `stack_down` | Leased | `wipe?` | `stack_status` result | L08 |
| `stack_reset_data` | Leased | `databases?` (default all lab DBs) | `{truncated:[{database, tables}]}` | L11 |
| `svc_start`, `svc_stop`, `svc_restart` | Leased | `service` (C10 services), `build_id?` (default latest) | `{target_id, state, image}` | L09 |
| `svc_config_set` | Leased | `service`, `key`, `value` (string or null to clear) | `{service, overlay:{...}}` | L09 |
| `http_request` | Leased | `via` (`gateway`, `agent_ingress`, `sink` (L36), or a C10 service), `method`, `path`, `headers?`, `body?` (JSON; `${secret:<ref>}` placeholders), `bearer_ref?` (L56: a `brr-N` vault ref sent as `Authorization: Bearer`, redacted in evidence) | `{status, headers, body, duration_ms, outcome}` (redacted, body max 64 KiB; `outcome` `ok`/`fail`/`unknown` per C18, from L48; a transport error after sending returns `outcome: unknown` with the error code) | L10, L48, L56 |
| `nodes_token_mint` | Leased | `label?`, `validity_minutes?` (60, 1 to 1440), `organization_id?` (L56: `LabIds.OrganizationId` (default), `LabIds.TenantBOrganizationId`, or an organization id present in the lab Identity database; anything else `[Lab.InvalidArgument]`) | `{token_ref, token_id, expires_at, organization_id}` | L11, L56 |
| `nodes_enroll_probe` | Leased | `token_ref`, `platform` (`windows`, `linux`), `via` (`gateway`, `agent_ingress`, `nodes`), `hostname?` (L51; `^[a-z0-9-]{1,48}$`, default `meridian-lab-probe`), `key_ref?` (L51; reuse a vaulted key for a retry, default a new key) | `{status, node_id?, certificate_thumbprint?, key_ref, error?}` | L11, L51 |
| `nodes_cert_time_shift` | Leased | `node_id`, `shift_minutes` (-525600..0), `fields?` (`issued_at`, `not_before`, `not_after`, `grace_until`) | `{rows_updated}` | L11, L40 |
| `nodes_decommission` | Leased | `node_id` (a node the lab enrolled in this lease, else `[Lab.BlastRadius]`) | `{status}` | L58; runs `INodeService.DecommissionNodeAsync` through the Nodes lab adapter (the user route needs a user token, F38) |
| `fault_toxic_add` | Leased | `proxy` (C10), `toxic` (`latency`, `bandwidth`, `timeout`, `reset_peer`, `slow_close`, `slicer`, `limit_data`), `stream` (`upstream`, `downstream`), `toxicity?` (1.0), `attributes`, `heal_after_s?` | `ActiveFault` | L19 |
| `fault_proxy_disable` | Leased | `proxy`, `heal_after_s?` | `ActiveFault` | L19 |
| `fault_partition` | Leased | `from` (service), `to` (service or dependency), `heal_after_s?` | `ActiveFault` (one fault id over every proxy on the edge) | L19 |
| `fault_heal` | Leased | `fault_id` | `{healed:true, reason}` | L19 |
| `fault_reset` | Leased | - | `{healed:[fault_id...]}` | L19 |
| `fault_kill` | Leased | `target`, `signal?` (`KILL`, `TERM`), `restart_after_s?` | `ActiveFault` | L20, L33 |
| `fault_pause` | Leased | `target`, `heal_after_s?` | `ActiveFault` | L20, L33 |
| `fault_disk_fill` | Leased | `target`, `mount` (declared, C10), `leave_kib?` (64), `heal_after_s?` | `ActiveFault` | L20, L33 |
| `fault_corrupt_file` | Leased | `file` (logical name, C10), `member?`, `mode` (`truncate`, `garbage`, `delete`), `heal_after_s?` | `ActiveFault` | L20, L33 |
| `fault_corrupt_registry` | Leased | `member`, `value` (`enrollment_token`), `mode` (`garbage`, `delete`), `heal_after_s?` | `ActiveFault` | L33 |
| `fault_db` | Leased | `database`, `action` (`terminate_backends`, `connection_limit`, `lock_table`, `read_only`), `table?`, `limit?`, `heal_after_s?` | `ActiveFault` | L20 |
| `results_flake_check` | OwnLease | `run_id`, `rows?`, `reruns?` (2, max 5), `lease_id?` | `{job_id}` | L27 |
| `results_known_issue_set` | Leased | `bucket_id`, `known_issue_id` (must exist in `known-issues.toml`) | `{bucket_id, known_issue_id}` | L26 |
| `triage_draft_issue` | Leased | `bucket_id` | `{draft_path}` | L26 |
| `triage_mark` | Leased | `bucket_id`, `state` (`new`, `known`, `wontfix`, `filed`), `issue_url?` (`https://github.com/SandboxServers/MeridianConsole/issues/<n>` only) | `{bucket_id, state}` | L26 |
| `hunt_model_run` | OwnLease | `model` (`enrollment`), `seed?`, `iterations?` (25, max 500), `budget_s?` (600, max 3600), `replay?` (job-relative `repro.toml`), `lease_id?` | `{job_id, seed}` | L27 |
| `fuzz_run` | OwnLease | `target` (C10 fuzz target or `all`), `seed?`, `cases?` (200, max 5000), `replay?`, `lease_id?` | `{job_id, seed}` | L28, L44 |
| `chaos_run` | OwnLease | `plan` (`docs/guides/uat-specs/chaos/<name>.toml`), `seed?`, `duration_s` (60..14400), `replay?`, `lease_id?` | `{job_id, seed}` | L29 |
| `soak_run` | OwnLease | `spec`, `rows?`, `duration_s` (300..86400), `seed?`, `lease_id?` | `{job_id, seed}` | L29 |
| `fleet_run` | OwnLease | `agents` (1..1000), `scenario` (`enroll_heartbeat`, `churn`, `reconnect_storm`), `duration_s`, `seed?`, `lease_id?` | `{job_id, seed}` | L42 |
| `job_cancel` | Leased | `job_id` | `{job_id, state}` | L27 |
| `pool_acquire` | Leased | `constraints` (C13), `purpose` | `{members:[PoolMember]}` | L30 |
| `pool_release` | Leased | `member` | `{released:true}` | L30 |
| `member_reset` | Leased | `member` | `PoolMember` (recreated) | L30 |
| `agent_install` | Leased | `member`, `mode` (`service`, `console`), `build_id?` | `{member, mode, binary_path}` | L32 |
| `agent_uninstall` | Leased | `member` | `{removed}` | L32 |
| `agent_start`, `agent_stop` | Leased | `member` | `{member, state}` | L32 |
| `agent_kill` | Leased | `member` | `{member, pid}`; kills only the agent process, not its tree, through the probe's `kill-agent`, so the row sees the job object's own behaviour (F44) | L59 |
| `agent_token_write` | Leased | `member`, `token_ref` | `{written:true}` | L32 |
| `agent_config_set` | Leased | `member`, `key`, `value` (or null) | `{member, overlay:{...}}` | L32 |
| `agent_msi_install`, `agent_msi_uninstall` | Leased | `member`, `build_id?`, `token_ref?` | `{member, exit_code, log_path}` | L43 |
| `cli_run` | Leased | `member`, `args` (array; first token in the C10 CLI verb allowlist) | `{exit_code, stdout, stderr, duration_ms}` (redacted) | L37 |
| `ui_flow_run` | Leased | `flow` (C14), `seed?` | C14 `UiFlowResult` | L35 |
| `uat_run` | OwnLease | `specs`, `sections?`, `rows?`, `plan_only?`, `label?`, `seed?`, `replay?` (bundle-relative `repro/*.toml`), `lease_id?` | `{run_id, bundle, seed, overall, summary, rows:[...]}` | L15, L22 |
| `uat_attest` | Leased | `run_id`, `section`, `row`, `clause_id`, `verdict` (`held`, `not_held`), `note` | `{section, row, grade}` | L16 |
| `identity_user_token` | Leased | `tenant` (`a`, `b`) | `{bearer_ref, organization_id, user_id}` (signs up or signs in a seeded lab user per tenant through the Gateway, exchanges for an Identity access token with `org_id`; the token goes to the vault as `brr-N`) | L56 |
| `events_read` | Open | `run_id`, `after_seq?` (0), `actor?`, `kind?`, `max?` (500, max 5000) | `{events:[LabObservation], next_seq}` (C18) | L48 |
| `history_run` | OwnLease | `scenario` (C19 scenario id), `clients?` (2..8), `seed?`, `minimise?` (true), `replay?`, `lease_id?` | `{run_id, verdict:"linearizable"\|"violation"\|"unknown", violations:[{rule, ops}], minimal_repro?}` (synchronous, at most 300 s) | L51 |
| `twin_run` | OwnLease | `experiment` (`docs/guides/uat-specs/twins/<name>.toml`), `seed?`, `lease_id?` | `{experiment_id, base_run_id, variant_run_id, relation, held, diffs:[...]}` | L53, L56 |
| `mutation_run` | OwnLease | `mutants?` (default all product mutants), `lease_id?` | `{job_id}`; the job writes `mutants.json` and `mutants.md` (C19) | L54 |
| `coverage_report` | Open | `since_run_id?`, `kind?` | `{dimensions:[{kind, total, hit, unhit:[...]}]}` (C18) | L52 |
| `envelope_run` | OwnLease | `envelope` (`docs/guides/uat-specs/envelopes/<name>.toml`), `cells?` (cell ids, default all), `seed?`, `lease_id?` | `{job_id}`; the job writes `envelope.json` and `envelope.md` (C20) | L57 |
| `peer_run` | OwnLease | `attack` (C19 peer attack id or `all`), `seed?`, `lease_id?` | `{job_id}` | L58 |
| `version_run` | OwnLease | `pair` (`docs/guides/uat-specs/versions/<name>.toml`), `seed?`, `lease_id?` | `{job_id}` (C20) | L60, L61 |
| `guided_ab_run` | OwnLease | `job` (`hunt_model_run` or `chaos_run`), `args` (that tool's arguments without `seed`), `runs?` (100 per arm, 20..200), `seed?`, `lease_id?` | `{job_id}`; the job writes `ab.json` and `ab.md` (C20) | L62 |

`hunt_model_run` and `chaos_run` gain `mode?` (`random` (default), `guided`) and `random_share?` (0.3, 0.3..1.0) in L62; `guided` is refused with `[Lab.InvalidArgument] guided mode disabled` while `tools/lab/guided.toml` says `enabled = false` (C20).

No tool has gate `Machine` (D-ML43).

`svc_config_set` allowlist: every service: `Logging:LogLevel:Default`, `Logging:LogLevel:Dhadgar`, `Logging:LogLevel:Microsoft.AspNetCore`; `nodes`: `Mtls:RenewalGraceMinutes`; `gateway`: `Readyz:RequiredClusters:0` to `:3`. L41 adds the TLS keys. `agent_config_set` allowlist: `Logging:LogLevel:Default`, `Logging:LogLevel:Dhadgar`, `Agent:Security:CertificateRenewalThresholdDays`.

`ActiveFault`: `{fault_id, kind, target_id, started_at, deadline, args}`; `fault_id` is `f-` plus 8 hex.

Error codes: `Lab.UnknownTool`, `Lab.Unclassified`, `Lab.InvalidArgument`, `Lab.LeaseRequired`, `Lab.LeaseHeld`, `Lab.MachineReserved`, `Lab.BlastRadius`, `Lab.FaultActive`, `Lab.PoolExhausted`, `Lab.JobRunning`, `Lab.Unavailable`, `Lab.NotFound`, `Lab.Timeout`, `Lab.ProcessFailed`, `Lab.Docker`, `Lab.Untrusted`, `Lab.ReadOnly`, `Lab.NotImplemented`, `Lab.Secret`.

### C5. Contracts additions (cross-boundary)

```csharp
// src/Shared/Dhadgar.Contracts/Agents/AgentWindowsFootprint.cs  (L12; the agent adopts it in L39)
namespace Dhadgar.Contracts.Agents;

public static class AgentWindowsFootprint
{
    public const string ServiceName = "DhadgarAgent";
    public const string RegistryKeyPath = @"SOFTWARE\Meridian Console\Agent";   // under HKLM
    public const string EnrollmentTokenValueName = "EnrollmentToken";
    public const string CaStoreName = "MeridianConsoleAgent";                    // LocalMachine
    public const string StateDirectoryRelative = @"Meridian Console\Agent";     // under %ProgramData%
    public const string StateFileName = "agent-state.json";
    public const string EventLogSourceName = "Meridian Console Agent";
}
```

`src/Shared/Dhadgar.Contracts/Agents/ProbeFootprint.cs` (L12) is the JSON the probe prints inside a Windows member and the lab reads; its shape is in L12. It crosses a process and container boundary, so it lives in Contracts.

The lab reuses existing product contracts unchanged: `Dhadgar.Contracts.Nodes.EnrollNodeRequest(Token, Platform, Hardware, PublicKey, AgentVersion?)`, `EnrollNodeResponse`, `HardwareInventoryRequest`, `AgentCertificateProfile`, `CommandEnvelope` (L44 fuzzes it) and `AgentHubContracts` (`IAgentHub`, `IAgentHubClient`, `AgentHubProtocol`; on `main` since Sprint 2, D-ML60). The fleet (L42) drives the real `Dhadgar.Agent.Core` classes; it never re-implements an agent message.

### C6. Seam stubs written in L01

```csharp
namespace Dhadgar.Lab.Nodes;

public interface INodesLabAdapter
{
    Task<Result<MintedToken>> MintTokenAsync(Guid organizationId, string label, TimeSpan validity, CancellationToken ct);
    Task<Result<int>> ShiftCertificateTimesAsync(Guid nodeId, TimeSpan shift, IReadOnlyCollection<CertTimeField> fields, CancellationToken ct);
}
public sealed record MintedToken(Guid TokenId, string TokenRef, DateTimeOffset ExpiresAt);
public enum CertTimeField { IssuedAt, NotBefore, NotAfter, GraceUntil }

public static class EnrollProbe
{
    // Builds the product contract request; the plain token is read from the vault by ref.
    public static Result<EnrollNodeRequest> BuildRequest(SecretVault vault, string tokenRef, string platform, out string keyRef);
}

// NodesLabAdapter(Func<NodesDbContext> createContext, SecretVault vault, TimeProvider time, ILoggerFactory loggers)
// L01: every method returns Failure("[Lab.NotImplemented] ..."). L11 implements.
// GraceUntil: L11 implements it; Sprint 2's AgentCertificate.GraceUntil column is on main (D-ML60).
// L58 adds: Task<Result<NodeStatus>> DecommissionNodeAsync(Guid nodeId, CancellationToken ct); // through the product's NodeService
```

### C7. UAT spec (TOML, schema 1)

Specs live in `docs/guides/uat-specs/*.toml` (flows) and `docs/guides/uat-specs/catalog/*.toml` (expected-failure catalogs, L23 and L38), one `[section]` per file. Invariant definitions live in `docs/guides/uat-specs/invariants/*.toml` (C11) and known issues in `docs/guides/uat-specs/known-issues.toml` (C12).

```toml
schema = 1

[section]
id = "lab-selftest"                 # unique across specs; [a-z0-9-]+
title = "Lab self-test"
sequential = true                   # rows run in file order (default true)
requires = ["docker"]               # any of: docker, windows, windows_containers, network
seed = 42                           # optional; default: the run seed (C17)
allow_invariants = []               # invariant ids (C11) this section expects to trip, e.g. ["I1"]
requires_pool = { os = "windows", sku = "servercore", kind = "container" }   # optional (C13); binds ${member}

[[row]]
id = "token-mint"                   # unique in section
title = "A minted enrollment token is stored hashed and unused"
expected = "One enrollment token row with the label, not used"
depends_on = ["stack-up"]           # rows in this section
known_issue = ""                    # optional KI id (C12): a claim, honoured only when every failure signature of the row matches that issue (C12 "Matching")
reuse_member = false                # sections with requires_pool: skip the member reset before this row
allow_invariants = []               # row-level additions

  [[row.fault]]                     # zero or more; injected after the baseline, healed after the steps (C10)
  id = "pg-down"
  inject = { tool = "fault_proxy_disable", args = { proxy = "nodes-postgres" } }   # args per the tool's C4 schema
  start = "before_steps"            # before_steps | after_step:<id>
  heal = "after_steps"              # after_steps | after_step:<id> | never (heals at row end anyway)

  [[row.step]]
  id = "mint"                       # optional; needed for since = "step:<id>" and source = "step"
  tool = "nodes_token_mint"         # never lease_* / uat_* / fault_* (faults go in [[row.fault]]) / job tools
  args = { label = "selftest-${run_id}", member = "${member}" }   # ${member} only with requires_pool
  capture = { token_ref = "/token_ref", token_id = "/token_id" }   # var -> JSON pointer into the step result
  expect_error = ""                 # optional "Lab.Code" or "http:<status>": the step is expected to fail this way; a success is a FAIL, another error is BLOCKED

  [[row.step]]
  wait_ms = 1000                    # a pause step (max 60000)

  [[row.expect]]
  id = "token-row"
  text = "token row exists and is unused"
  source = "db"
  sql = "select used_at from enrollment_tokens where id = @id::uuid"
  params = { id = "${token_id}" }   # bound as parameters, never interpolated into sql
  pointer = "/rows/0/used_at"
  op = "absent"
  timeout_ms = 0                    # 0 = one check; else poll every poll_ms (default 500) until held or timeout (max 300000)
  required = true                   # default true
```

Rules: `lease_id` never appears in a spec (the runner injects it). `${run_id}`, `${row_id}`, `${section_id}`, `${seed}`, `${member}` (sections with `requires_pool`) and captured vars are available; an unknown var is a plan error. `${secret:<ref>}` is allowed only inside `http` bodies. Fault ids are unique per row; a fault tool outside the C4 fault list is a plan error.

**Catalog rule.** A catalog row states how the product should break. When a row fails, the fix is the product or a known issue (C12), never a changed expected value to match what the product did. Review refuses a diff that edits an `expected`, `op` or `value` in `catalog/` together with a product change in the same packet unless the README records a decision.

Clause sources and their fields:

| Source | Fields | Result the pointer and op apply to |
| --- | --- | --- |
| `db` | `sql`, `params?`; `@since` is bound automatically | `{rows:[...], row_count}` |
| `log` | `process`, `contains` or `matches`, `category?`, `level?`, `event_id?`, `since?` | `{matches:[...], count}` |
| `http` | `via`, `method`, `path`, `headers?`, `body?` | `{status, headers, body}` |
| `tool` | `tool` (OPEN tools only), `args?` | the tool's result |
| `certstore` | `store`, `subject_contains?`, `issuer_contains?`, `issuer_thumbprint?` | `{certs:[...], count}` |
| `registry` | `key` | `registry_read` result |
| `state_file` | `file` | `state_file_read` result |
| `step` | `step` (a step id in this row) | `{ok, error, error_code, duration_ms, result}` of that step |
| `invariant` | `id` (C11 id), `since?` | `{violations:[...], count}` |
| `hub` (L40) | `event` (`connected`, `disconnected`), `node_id`, `since?` | `{events:[...], count}` |
| `agent_log` (L40) | like `log`, for the agent in a member (`member`, `contains` or `matches`) | `{matches:[...], count}` |
| `game` (L59) | `member`, `server_id` (the synthetic game's id in this row) | `{alive, pid, counter, counter_at, restarts}` read inside the member by the probe mode `game-tick` (C20) |
| `human` | `text` only | none; pending until attested |

`since` is `run_start`, `row_start` (default), `step:<id>` or `fault_heal:<id>` (the moment that fault was healed; recovery clauses use it). `certstore`, `registry` and `state_file` take `member?` (L32); without it they read the host.

Ops: `eq`, `ne`, `contains`, `not_contains`, `matches` (regex, 1 s timeout), `gt`, `gte`, `lt`, `lte`, `exists`, `absent`, `truthy`, `falsy`, `len_gte`, `approx` (`value = [target, tolerance]`), `delta` (the source is read before the row's first step and after the last; `value` is the expected change of the pointed number), `any` (`value` is a list), `min_rows`, `max_rows` (on `row_count` or `count`). `op` is always required.

### C8. Grades

Clause states: `Held`, `NotHeld`, `Unverified` (source error, OS unavailable, pointer missing for any op except `absent`), `Pending` (human, not attested).

| Row grade | When |
| --- | --- |
| SKIPPED | not run: filtered out, a `depends_on` row is not PASS, or a `requires` capability is missing |
| BLOCKED | a step returned an error (including a lost lease); later steps and the clauses are not run |
| FAIL | any required clause `NotHeld` |
| UNVERIFIED | no required clause `NotHeld`, at least one `Unverified`, or the row has no required clause |
| NEEDS_HUMAN | every required clause `Held` or `Pending`, at least one `Pending` |
| PASS | every required clause `Held` |

Precedence once a row ran: BLOCKED > FAIL > UNVERIFIED > NEEDS_HUMAN > PASS. Non-required clauses are recorded but never change the grade. Attestation applies only to `human` clauses and re-grades the row.

**Invariants (L22).** After each row the runner checkpoints the invariant monitor (C11). A violation in that row's window whose id is not in the section's or row's `allow_invariants` turns a PASS or NEEDS_HUMAN into FAIL with reason `invariant <id>: <summary>`. An invariant check error (C11 `InvariantCheckError`) in a row's window turns a PASS or NEEDS_HUMAN into UNVERIFIED with reason `invariant <id> unchecked: <error>`: a judge that could not look never counts as a pass. Violations and check errors outside any row window are graded as synthetic rows `invariants/<id>` (FAIL for violations, UNVERIFIED for check errors, one per id).

**Overall (L15 computes it, L25 adds known-issue linking; defect fix D-ML46).** A *failure* is a FAIL, BLOCKED or UNVERIFIED row (synthetic rows included) or an unallowed violation. Rows excluded by the request's `section`/`rows` filter are not part of the run and are not recorded. In precedence order:

| Overall | When |
| --- | --- |
| `fail` | any failure that is new (before L25 every failure is new) |
| `incomplete` | no new failure, and at least one of: a NEEDS_HUMAN row, a SKIPPED row, no row ran at all |
| `pass_with_known` | no new failure, nothing incomplete, at least one failure linked to an open known issue (C12 "Matching") |
| `pass` | no failure, nothing incomplete |

`incomplete` is never evidence: the status-honesty gate (C12) accepts only `pass` and `pass_with_known`. Attesting the last pending human clause re-computes `overall` (L16).

```csharp
namespace Dhadgar.Lab.Uat;
public enum RowGrade { Pass, Fail, Blocked, Skipped, NeedsHuman, Unverified }
public enum ClauseState { Held, NotHeld, Unverified, Pending }
public enum RunOverall { Pass, PassWithKnown, Incomplete, Fail }   // JSON: "pass", "pass_with_known", "incomplete", "fail"
public static class RowGrader
{
    public static (RowGrade Grade, string Reason) Grade(bool ran, string? skipReason, string? stepError, IReadOnlyList<(bool Required, ClauseState State)> clauses);
}
public static class RunOutcome
{
    // failures: one entry per failed row or unallowed violation; isKnown is false for every entry until L25.
    public static RunOverall Compute(IReadOnlyList<RowGrade> grades, IReadOnlyList<(string FailureKey, bool IsKnown)> failures);
}
```

CLI exit codes (`meridian-lab uat run`, L17): 0 `pass`, 1 `fail`, 2 config or plan error, 4 `incomplete`, 5 `pass_with_known`. Code 3 stays the daemon's "already running" (F1) and is never used by `uat run`.

### C9. Evidence bundle

```text
<LabPaths.UatRuns>\<yyyyMMdd>-<build8|nobuild>-<runid6>\
  run.json
  rows\<section>\<row>.json
  logs\<process>.jsonl          (redacted log slices for the run window; containers and members too)
  invariants.json               (L21: every violation in the run, redacted)
  events.jsonl                  (L48: the ordered observation stream, C18)
  fingerprint.json              (L48: the reproducibility fingerprint, C18)
  repro\repro.toml              (L22: seed, build, specs and their sha256, row filter, fault list, `fidelity` (C18); `uat_run {replay}` reads it)
  ledger.md
```

`run.json`: `{schema:1, run_id, label, seed, started_at, ended_at, daemon_version, os, build:{id, commit, dirty, checkout}, lease_owner, members:[{id, dims}], specs:[{file, sha256}], tools:[...], summary:{PASS, FAIL, BLOCKED, SKIPPED, NEEDS_HUMAN, UNVERIFIED}, overall:"pass"|"pass_with_known"|"incomplete"|"fail", incomplete_reasons:[<text>], new_failures:[<signature id>], known_failures:[{signature, known_issue}], violations:<int>, redactions, secret_scan:"passed"|"failed", fingerprint_sha256, fidelity:"exact"|"schedule_approximate"|"evidence_only", pair:null|{experiment_id, role, partner_run_id, relation}, replay:null|{of, fingerprint_diff:[<text>], reproduced:true|false|null}, events:<int>}`. Fields from later packets are present from L16 (L48 for the C18 fields) with empty, null or zero values.

Row JSON: `{section, id, title, expected, grade, reason, started_at, ended_at, member, faults:[{id, tool, target, fault_id, injected_at, healed_at, heal_error}], steps:[{id, tool, ok, error, error_code, expected_error, duration_ms, result}], clauses:[{id, text, source, op, value, required, state, observed, attempts, checked_at}], violations:[{id, summary, at}], attestations:[{clause_id, verdict, note, at}]}`. `result` and `observed` are redacted and truncated to 16 KiB.

`ledger.md`:

```markdown
# UAT run 20261102-a1b2c3d4-9f8e7d

| Section | Row | Result | Time (UTC) | Build |
| --- | --- | --- | --- | --- |
| lab-selftest | token-mint | PASS | 2026-11-02T10:00:00Z | a1b2c3d4 |

## Recording results

### lab-selftest/token-mint: A minted enrollment token is stored hashed and unused

- System: meridian-lab 1.0.0+a1b2c3d4 on Windows 10.0.26300
- Step id: lab-selftest/token-mint
- Result: PASS
- Time: 2026-11-02T10:00:00Z
- Build: a1b2c3d4 (commit 0123456789ab, clean)
- Saw: token-row: /rows/0/used_at absent (held)
- Expected: One enrollment token row with the label, not used
```

Secret scan: before finalising, `SecretScanner` checks every file for vault values, the daemon token, PEM private key blocks, `Bearer <20+ chars>` and `Password=`/`Pwd=`. A hit deletes that file, sets `secret_scan:"failed"` and forces every row to BLOCKED with reason `secret scan failed`.

### C10. Lab topology, targets and fault surface

Every lab container is on Docker network `meridian-lab-net`, named `meridian-lab-<name>`, labelled `meridian-lab=1`, started with `--restart no`, no named volumes, and every published port is bound to `127.0.0.1`. Every docker call passes `--context <name>` from `labsettings.json` (`Lab:Docker:LinuxContext`, default `desktop-linux`; `Lab:Docker:WindowsContext`, set from the L00 worknote).

Services (Linux containers built from `tools/lab/images/`; container port 8080):

| Service | Container | Host port (direct, observation) | Build project | Databases | Dependencies reached through Toxiproxy | Packet |
| --- | --- | --- | --- | --- | --- | --- |
| `gateway` | `meridian-lab-gateway` | 15000 (15443 TLS, L41) | `src/Dhadgar.Gateway` | - | `gateway-redis`, `gateway-nodes`, `gateway-identity`, `gateway-betterauth`, `gateway-notifications`, `gateway-discord` | L09 |
| `nodes` | `meridian-lab-nodes` | 15040 | `src/Dhadgar.Nodes` | `meridian_lab_nodes` | `nodes-postgres`, `nodes-rabbitmq` | L09 |
| `identity` | `meridian-lab-identity` | 15010 | `src/Dhadgar.Identity` | `meridian_lab_identity` | `identity-postgres`, `identity-redis`, `identity-rabbitmq` | L34 |
| `betterauth` | `meridian-lab-betterauth` | 15130 | `src/Dhadgar.BetterAuth` (npm) | `meridian_lab_betterauth` | `betterauth-postgres` | L34 |
| `panel` | `meridian-lab-panel` | 14321 | `src/Dhadgar.Panel` (npm) | - | - (the browser calls the Gateway) | L34 |
| `notifications` | `meridian-lab-notifications` | 15090 | `src/Dhadgar.Notifications` | `meridian_lab_notifications` | `notifications-postgres`, `notifications-rabbitmq`, `notifications-webhook` | L36 |
| `discord` | `meridian-lab-discord` | 15120 | `src/Dhadgar.Discord` | `meridian_lab_discord` | `discord-postgres`, `discord-webhook` | L36 |

Infrastructure:

| Name | Container | Host port | Image | Packet |
| --- | --- | --- | --- | --- |
| `postgres` | `meridian-lab-postgres` | 15432 | AppHost's Postgres image and major tag | L08 |
| `rabbitmq` | `meridian-lab-rabbitmq` | 15673 AMQP, 16672 management | `tools/lab/images/rabbitmq.Dockerfile` (management image plus `rabbitmq_delayed_message_exchange`, pinned version and sha256) | L08 |
| `redis` | `meridian-lab-redis` | 16379 | AppHost's Redis image and major tag | L08 |
| `toxiproxy` | `meridian-lab-toxiproxy` | 18474 API; 25000 and 25443 agent ingress | `ghcr.io/shopify/toxiproxy:2.13.0` | L08 |
| `sink` | `meridian-lab-sink` | 18090 | `tools/lab/images/probe.Dockerfile` running `Dhadgar.Lab.Probe sink` | L36 |

Toxiproxy proxies (`ProxyCatalog`; listen addresses are inside the Toxiproxy container; services are configured with `meridian-lab-toxiproxy:<port>`):

| Proxy | Listen | Upstream | Packet |
| --- | --- | --- | --- |
| `gateway-redis` | 20010 | `meridian-lab-redis:6379` | L08 |
| `gateway-identity` | 20011 | `meridian-lab-identity:8080` | L34 |
| `gateway-nodes` | 20012 | `meridian-lab-nodes:8080` | L08 |
| `gateway-betterauth` | 20013 | `meridian-lab-betterauth:8080` | L34 |
| `gateway-notifications` | 20014 | `meridian-lab-notifications:8080` | L36 |
| `gateway-discord` | 20015 | `meridian-lab-discord:8080` | L36 |
| `nodes-postgres` | 20020 | `meridian-lab-postgres:5432` | L08 |
| `nodes-rabbitmq` | 20021 | `meridian-lab-rabbitmq:5672` | L08 |
| `identity-postgres` | 20030 | `meridian-lab-postgres:5432` | L34 |
| `identity-redis` | 20031 | `meridian-lab-redis:6379` | L34 |
| `identity-rabbitmq` | 20032 | `meridian-lab-rabbitmq:5672` | L34 |
| `betterauth-postgres` | 20040 | `meridian-lab-postgres:5432` | L34 |
| `notifications-postgres` | 20050 | `meridian-lab-postgres:5432` | L36 |
| `notifications-rabbitmq` | 20051 | `meridian-lab-rabbitmq:5672` | L36 |
| `notifications-webhook` | 20052 | `meridian-lab-sink:8080` | L36 |
| `discord-postgres` | 20060 | `meridian-lab-postgres:5432` | L36 |
| `discord-webhook` | 20061 | `meridian-lab-sink:8080` | L36 |
| `agent-ingress` | 25000 (published `127.0.0.1:25000`) | `meridian-lab-gateway:8080` | L08 |
| `agent-ingress-tls` | 25443 (published `127.0.0.1:25443`) | `meridian-lab-gateway:8443` | L41 |

Agent ingress is also published on the host's container-NAT address only if the L00 worknote shows Windows members cannot reach `127.0.0.1` on the host; never on `0.0.0.0`.

Lab-only HTTP barrier proxy (L49, D-ML47): an in-daemon Kestrel listener on `127.0.0.1:25080` (`LabPorts.Barrier`), upstream `http://127.0.0.1:25000` (the `agent-ingress` Toxiproxy listener, so the real path Gateway → `gateway-nodes` → Nodes stays in place). Plain HTTP only. `LabPorts.ServicePorts` gains 25080 so `LabHosts.IsLabEndpoint` accepts it. Only history clients (C19) send through it; nothing in a product container is configured to use it.

Fault impact map (`FaultImpactMap`, L21; defect fix D-ML46). A fault on target T can explain a 502, 503 or 504 answered by service S only when T is in S's impact set:

| Answering service | Impact set (fault targets) |
| --- | --- |
| S other than `gateway` | `c:<S>`, `d:<S>`, every `p:<proxy>` in S's "Dependencies reached through Toxiproxy" cell, the infra container upstream of each of those proxies (`c:postgres`, `c:rabbitmq`, `c:redis`, `c:sink`), and `c:toxiproxy` |
| `gateway`, request path routed to cluster K that is a lab service | `c:gateway`, `p:gateway-redis`, `c:redis`, `c:toxiproxy`, `p:gateway-<K>`, and the impact set of K from the row above (one hop: the Gateway's 503 on `/api/v1/agents/enroll` is explained by `p:nodes-postgres`, never by `p:gateway-discord`) |
| `gateway`, request path routed to a cluster with no lab service, or on no route | `c:gateway`, `p:gateway-redis`, `c:redis`, `c:toxiproxy` |

Gateway route → cluster table (from `src/Dhadgar.Gateway/appsettings.json` `ReverseProxy:Routes`; L21 copies it into the static `GatewayRouteTable`; `GatewayRouteTableTests` reads the file and fails when the two differ): a path matches the most specific route whose `Match.Path` template matches (`/api/v1/agents/enroll` before `/api/v1/agents/{**catch-all}`, `/api/v1/identity/internal/...` before `/api/v1/identity/...`); the route's `ClusterId` names K. Verified 2026-10-27: `nodes` serves `/api/v1/agents/enroll`, `/api/v1/agents/{**catch-all}`, `/hubs/agent/{**catch-all}`, `/api/v1/organizations/{organizationId}/nodes/...` and `.../enrollment/...`. Clusters with no lab service (servers, tasks, files, console, mods, billing, secrets) excuse nothing beyond the Gateway's own set.

Observation paths (R-OBSERVE, D-ML34): `db_query` and invariant SQL use `127.0.0.1:15432`; health probes, `http_request via=<service>` and `via=gateway` use the direct host ports; log followers use `docker logs`; RabbitMQ reads use 16672. Only `http_request via=agent_ingress`, `nodes_enroll_probe via=agent_ingress`, members and the fleet go through Toxiproxy.

Target ids (`LabTargetRegistry`, persisted at `LabPaths.State\targets.json`):

| Kind | Id form | Registered when | Faults allowed |
| --- | --- | --- | --- |
| `container` | `c:<service or infra name>` | the lab creates the container | kill, restart, pause, disk fill (declared mounts) |
| `proxy` | `p:<proxy name>` | L08/L34/L36 create it in Toxiproxy | toxics, disable, partition |
| `database` | `d:<service>` | L08 creates `meridian_lab_<service>` | `fault_db` |
| `file` | `f:<logical name>` | fixed table below | corrupt, restore |
| `member` | `m:<member id>` | `pool_acquire` creates it | kill, pause, disk fill, corrupt file and registry inside it |

Declared disk-fill mounts: every Linux service container runs with `--tmpfs /tmp:rw,size=64m`; mount `/tmp`. Windows members: `C:\` only when the L00 worknote shows `--storage-opt size=` works, else `fault_disk_fill` on members returns `[Lab.Unavailable]`.

Logical files: `nodes-ca-key` and `nodes-ca-cert` (host files under `LabPaths.State\nodes-ca\`, names from `LocalFileCaStorageProvider`), `agent-state` (member: `%ProgramData%\` + `AgentWindowsFootprint.StateDirectoryRelative` + `StateFileName`), `agent-client-cert` (member: the client certificate in `LocalMachine\My`; the probe replaces it with a same-subject self-signed certificate and keeps the original as a backup).

Blast-radius guard (R-BLAST, D-ML21): `FaultTargetGuard.Check(targetId)` refuses with `[Lab.BlastRadius] <reason>` unless all three hold:

1. **Registry.** The id is in `LabTargetRegistry`, and for containers and members the stored container id still matches.
2. **Live inspect.** `docker --context <lab context> inspect <container id>` shows label `meridian-lab=1` and a name starting `/meridian-lab-`. Proxies: the name is in `ProxyCatalog` and Toxiproxy lists it, and `c:toxiproxy` passes checks 1 and 2. Databases: the name matches `^meridian_lab_[a-z]+$` on the lab Postgres connection. Files: the resolved path is under `LabPaths.State` (host) or is a fixed member path.
3. **Tripwire.** The resolved name is not `postgres`, `cache`, `messaging`, does not start with `dhadgar` (any case), is not a volume name, and the container has no mount of a named volume.

`LabTargetRegistry` drops an entry whose container is gone. No lab tool deletes a volume or runs `docker volume`, `docker system` or `docker rm -v` on anything but a registered target.

Build projects (`lab_build`): `gateway`, `nodes`, `identity`, `notifications`, `discord`, `cli`, `betterauth`, `panel`, `agent_windows`, `probe`. .NET projects publish portable after a locked restore (`dotnet restore --locked-mode`, then `dotnet publish --no-restore -c Release`, never `-r`); images are tagged `meridian-lab/<project>:<build_id>` with `--label meridian-lab=1`.

CLI verb allowlist (`cli_run`, L37): `auth`, `nodes`, `notifications`, `discord`, `config`, `--help`, `--version`. Never `config` subcommands that print secrets (the packet reads the CLI and lists the safe ones).

Fuzz targets (`FuzzTargets`, L28 and L44): one entry per route, `{name, via, method, path_template, body_type, auth}` where `body_type` is a `Dhadgar.Contracts` type. L28 ships `enroll` (`POST /api/v1/agents/enroll`, `EnrollNodeRequest`) and the target table; L38 adds `identity_exchange`, `identity_refresh` and `notifications_admin`. L44 adds `hub_heartbeat` (`AgentHeartbeat`) and `command_envelope` (`CommandEnvelope` into `CommandValidator`).

### C11. Invariants

```csharp
namespace Dhadgar.Lab.Invariants;

public sealed record InvariantWindow(DateTimeOffset From, DateTimeOffset To, string? Section, string? Row);
public sealed record InvariantViolation(
    string InvariantId,      // I1..I7
    string Service,          // C10 service, infra name, "lab" or "member:<id>"
    string CheckId,          // e.g. "unhandled", "outbox-age", "nodes/node-without-cert"
    string Detail,           // redacted, max 512 chars
    DateTimeOffset At,
    JsonElement? Evidence);  // redacted, max 4 KiB
public interface IInvariant
{
    string Id { get; }
    Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct);
}
public sealed record InvariantCheckError(string InvariantId, string Error);   // the check could not run (source down, parse error)
public sealed record InvariantReport(IReadOnlyList<InvariantViolation> Violations, IReadOnlyList<InvariantCheckError> Errors);
public sealed class InvariantMonitor
{
    // Runs every registered invariant; one failing check becomes an InvariantCheckError and the others still run.
    public Task<InvariantReport> CheckpointAsync(InvariantWindow window, CancellationToken ct);
}
```

A check error inside a row window turns that row's PASS or NEEDS_HUMAN into UNVERIFIED with reason `invariant <id> unchecked: <error>`; outside any row it becomes a synthetic `invariants/<id>` row graded UNVERIFIED.

| Id | Invariant | How it is checked | Packet |
| --- | --- | --- | --- |
| I1 | No unhandled exception and no unexplained 5xx | Log lines at Error or Critical from `Microsoft.AspNetCore.Diagnostics.*` or `Dhadgar.ServiceDefaults` `GlobalExceptionHandler`, and any line containing `Unhandled exception`, always count. `Microsoft.AspNetCore.Hosting.Diagnostics` "Request finished" lines (every lab service runs with that category at Information) count when the status is 500, 501 or 505-599, and for 502, 503 and 504 unless a fault on a target in the answering service's impact set (C10 `FaultImpactMap`, Gateway lines resolved through the route → cluster table) was active, or healed less than 10 s before the line (`FaultBook` history). A fault on any other target never excuses the line (D-ML46): a 503 while one of the service's own dependencies is down is the designed answer, a 500 never is. The violation records `excused_by: <fault_id>` when excused, for audit | L21 |
| I2 | The outbox drains | Per service with an EF outbox: rows in `OutboxMessage` older than 60 s, and `OutboxState` rows not delivered after 60 s, count is zero (table and column names from the service's model snapshot) | L21 |
| I3 | DB consistency | Every `[[check]]` in `docs/guides/uat-specs/invariants/<service>.toml` returns zero rows | L21, L23, L38 |
| I4 | No secret in a log | Every raw log line is checked **before** redaction for vault values, the daemon token, PEM private key blocks, `Bearer <20+ chars>`, `Password=`/`Pwd=` with a value; the violation stores only the redacted line | L21 |
| I5 | No orphans | Labelled containers not in the registry; RabbitMQ queues named `*_error` or `*_skipped` with messages; Toxiproxy toxics or disabled proxies with no `FaultBook` entry; members marked leased with no lease; leftover backup folders | L21, L30 |
| I6 | No unexpected restart | A container's `RestartCount` or `State.StartedAt` changed, or a supervised process exited, unless one of these on the same target explains it: a `fault_kill` or `fault_pause` on that container or member, an audited `svc_restart` of that service, or a `member_reset` of that member, each between 30 s before the change and 30 s after its heal. A fault on another target, or a Toxiproxy fault on the service's dependency, never explains a restart (a service that crashes when its database drops is the finding) | L21 |
| I7 | No resource trend (soak only) | Memory, handle or fd and thread counts sampled every 30 s; a least-squares slope over the soak above the limits in `docs/guides/uat-specs/invariants/soak.toml` | L29 |

Invariant TOML:

```toml
schema = 1
service = "nodes"

[[check]]
id = "node-without-cert"
database = "nodes"
text = "every enrolled node has exactly one active certificate"
sql = """select n."Id" from "Nodes" n left join "AgentCertificates" c on c."NodeId" = n."Id" and c."RevokedAt" is null
group by n."Id" having count(c."Id") <> 1"""
```

Table and column names above are placeholders; the packet that writes each file takes them from the model snapshot.

### C12. Results DB, signatures, known issues, triage

SQLite file `LabPaths.Results\lab-results.db`, `PRAGMA journal_mode=WAL`, schema version table.

```sql
create table schema_version (version integer not null);
create table runs (run_id text primary key, kind text not null, -- uat|hunt|fuzz|chaos|soak|fleet|ui|history|twin|mutation|envelope|peer|version|ab
  label text, seed integer not null, build_id text, commit_sha text, dirty integer, started_at text, ended_at text,
  overall text, -- pass|pass_with_known|incomplete|fail
  fingerprint_sha256 text, fidelity text, bundle_path text not null);
create table rows (run_id text not null, section text not null, row_id text not null, grade text not null, reason text,
  seed integer not null, known_issue text, signature_id text, -- primary signature, for bucketing
  signatures_json text not null default '[]', -- every failure signature id of the row (Matching, below)
  known_match text, -- null | <KI id> | claimed_mismatch | regressed:<KI id>
  started_at text, ended_at text,
  primary key (run_id, section, row_id));
create table pairs (experiment_id text not null, run_id text not null, role text not null, -- base|variant
  partner_run_id text not null, relation text not null, held integer, primary key (experiment_id, run_id));
create table coverage (run_id text not null, kind text not null, key text not null, hits integer not null,
  primary key (run_id, kind, key)); -- C18 behavioural coverage (L52)
create table faults (run_id text not null, fault_id text not null, kind text not null, target_id text not null,
  args_json text, started_at text, healed_at text, heal_reason text, primary key (run_id, fault_id));
create table invariant_violations (run_id text not null, invariant_id text not null, service text not null,
  check_id text not null, signature_id text not null, at text not null, allowed integer not null, section text, row_id text, detail text);
create table signatures (signature_id text primary key, kind text not null, text text not null,
  first_seen text not null, last_seen text not null, hits integer not null,
  state text not null, -- new|known|flaky|wontfix|filed
  known_issue_id text, issue_url text);
create table findings (finding_id text primary key, signature_id text not null, first_run_id text not null,
  repro_path text, draft_path text, state text not null, -- new|drafted|filed|dismissed
  created_at text not null);
create table flakes (section text not null, row_id text not null, seed integer not null,
  run_ids_json text not null, grades_json text not null, detected_at text not null);
```

Signature text, then `signature_id` = first 16 lowercase hex of SHA-256 over the UTF-8 text:

| Kind | Text | Normalisation |
| --- | --- | --- |
| `exc` | `exc:<exception type>\|<frame1>\|<frame2>\|<frame3>` | frames are `Namespace.Type.Method`; drop line numbers, offsets, generic arity, compiler names (`<>c__DisplayClass*`, `<Method>b__*`, `d__*` become the enclosing method) |
| `http` | `http:<status>\|<errorCode or ->\|<METHOD>\|<route template>\|<fault kinds or none>` | route from the endpoint log line, else the path with GUIDs as `{id}` and digit runs as `{n}`; fault kinds sorted, joined with `+` |
| `clause` | `clause:<section>/<row>/<clause id>\|<state>` | - |
| `inv` | `inv:<invariant id>\|<service>\|<check id>` | - |
| `hist` | `hist:<model>\|<rule id>\|<scenario>` | from L51; one per history rule violated (C19) |
| `twin` | `twin:<experiment id>\|<pointer>` | from L53; one per `compare` pointer whose relation did not hold (C19) |
| `peer` | `peer:<attack>\|<expected>\|<observed>` | from L58; one per attack whose required expectation failed (C19) |
| `env` | `env:<envelope id>\|<cell id>\|metastable` | from L57; one per cell graded `metastable` (C20); `slow` cells produce no signature |
| `ver` | `ver:<pair id>\|<row id>\|<grade>` | from L60; one per required mixed-version row that is not PASS (C20) |

A failed row carries **every** failure signature it produced: each `exc`, each `http` with status 500-599, each NotHeld or Unverified clause, each unallowed violation inside its window and each **required** `hist` rule violated (a non-required one, such as E5 before D-ML49, stays in the row JSON `history.violations` with `required: false` and produces no signature). Its **primary** signature, used only for bucketing, is its first `exc`, else its first 5xx `http`, else its first failing `clause`, else its first `inv` or `hist`.

Matching (D-ML46; fixes the "known issue by claim" defect). A row is a known failure only when every one of its signatures matches the same open issue; one unmatched signature makes the whole row new. An issue matches a signature when the signature text equals one of its `signatures`, or one of its `patterns` matches, **and** the row and signature are inside the issue's `scope` (every listed scope key must hold; an absent key does not narrow). The row's `known_issue` field is a claim, never a pass: when the claimed issue does not match, the row is new with reason `known_issue <id> claimed but signature <text> does not match` and `known_match = claimed_mismatch`. A match on a `fixed` issue is new with `known_match = regressed:<id>`. A standalone violation (outside any row) matches by the same rules on its one signature.

Loader rules (`KnownIssues.Load` returns `Result` with every problem listed): an issue with neither `signatures` nor `patterns` is invalid; every pattern must start with `^(exc|http|clause|inv|hist|twin|peer|env|ver):` and is compiled with a 1 s timeout; a pattern that matches an empty string, or is `.*`/`.+` after the kind prefix, is invalid; duplicate ids are invalid.

Known issues, `docs/guides/uat-specs/known-issues.toml`:

```toml
schema = 1

[[issue]]
id = "KI-DV13"
title = "Stub services and the Console hub have no auth (DV-13)"
link = "docs/PROJECT-STATE.md"
signatures = []                                   # exact signature texts
patterns = ["^http:2\\d\\d\\|.*\\|/api/v1/(servers|tasks|files|mods|billing)/"]  # .NET regex over signature text, anchored with a kind prefix
state = "open"                                    # open | fixed

[issue.scope]                                     # optional; every listed key must hold
sections = []                                     # spec sections the row may be in (empty = any)
rows = []                                         # row ids (empty = any)
services = ["gateway"]                            # the answering service (http, inv) or the logging service (exc)
invariants = []                                   # invariant ids (inv signatures only)
```

L26 seeds: `KI-DV13`, `KI-DV12` (localhost cookie domain), `KI-DV3` (refresh untested; removed when S-L9 passes), `KI-NOTIF-OUTBOX` (Notifications outbox tables missing), `KI-RABBIT-HOST` (compose `ConnectionStrings__RabbitMqHost` ignored), `KI-DISCORD-SECRETS-PORT` (fallback `:5080`), `KI-DELAYED-PLUGIN` (AppHost and compose lack the plugin), `KI-94` (`CommandValidator` signature check).

Run summary: `new_failures` (FAIL, BLOCKED or UNVERIFIED rows and unallowed violations whose signature is not linked to an open known issue), `known_failures` (the rest of the failures). `overall` is C8's `RunOutcome.Compute`: `fail` (any new failure), else `incomplete` (a NEEDS_HUMAN or SKIPPED row, or no row ran), else `pass_with_known` (only known failures), else `pass`. The evidence gate for status honesty is `pass`, or `pass_with_known` with every known failure listed; `incomplete` is never evidence.

Flake: the same row with the same seed and build graded differently across runs (`results_flake_check` reruns, or two ingested runs). A flaky signature gets state `flaky`; a flake is never a pass.

Triage files: `LabPaths.Triage\ledger.md` (one table row per bucket: id, kind, state, hits, first and last seen, known issue, repro, draft) and `LabPaths.Triage\drafts\<bucket_id>.md`. A draft has: title, signature text, first and last run, hit count, build, seed, `repro.toml` path and its replay command (`meridian-lab uat run --replay <path>`), the redacted evidence excerpt, and the line `Draft only. Filed by the owner after approval (D-ML28, D-ML42).` No tool calls GitHub.

### C13. Pool

```csharp
namespace Dhadgar.Lab.Pool;

public sealed record PoolDimensions(
    string Os,          // "windows" | "linux"
    int? OsBuild,       // e.g. 26100
    string Sku,         // "servercore" | "nanoserver" | "debian" | "host"
    string Isolation,   // "process" | "hyperv" | "none"
    string Kind,        // "container" | "host" | "vm"
    string Arch,        // "x64" | "arm64"
    string Clock,       // "host" (shared); "own" only for vm
    bool Reboot);       // false for containers
public sealed record PoolConstraints(string? Os, int? MinOsBuild, string? Sku, string? Isolation, string? Kind, string? Arch,
    bool? Reboot, int Count = 1);
public enum MemberState { Provisioning, Ready, Leased, Resetting, Failed }
public sealed record PoolMember(string MemberId, string TargetId, string? ContainerName, PoolDimensions Dimensions,
    MemberState State, DateTimeOffset CreatedAt);
public interface IPoolProvider
{
    string Kind { get; }
    Task<Result<bool>> IsAvailableAsync(CancellationToken ct);          // false with a reason in the Result error
    Task<Result<PoolMember>> ProvisionAsync(PoolConstraints constraints, CancellationToken ct);
    Task<Result<PoolMember>> ResetAsync(PoolMember member, CancellationToken ct);   // recreate from the image
    Task<Result> DestroyAsync(PoolMember member, CancellationToken ct);
}
```

Member ids: `win-01`, `lin-01`, ...; containers `meridian-lab-m-<member id>`. Members are destroyed at `pool_release` and at lease end. Max members: 8 Linux, `Lab:Pool:MaxWindows` Windows (default 3; L31 sets it from the L00 timings). The `host` provider exposes one member `host` with reads only: every mutating tool refuses `m:host` with `[Lab.BlastRadius] host is read-only`. The `vm` provider returns `[Lab.Unavailable] vm tier is a follow-up (D-ML31)`.

Spec use: `[section] requires_pool = { os = "windows", sku = "servercore", kind = "container" }`; the runner acquires one member per section, binds `${member}`, resets it before each row unless the row sets `reuse_member = true`, and releases it at section end.

### C14. UI flows

```csharp
namespace Dhadgar.Lab.Ui;
public sealed record UiStep(string Name, bool Ok, long DurationMs, string Url, string? Error);
public sealed record UiFlowResult(string Flow, bool Ok, IReadOnlyList<UiStep> Steps,
    UiStorage Storage, IReadOnlyList<string> Screenshots,       // bundle- or job-relative PNG paths
    IReadOnlyList<string> ConsoleErrors, IReadOnlyList<string> NetworkFailures);
public sealed record UiStorage(bool AccessTokenPresent, bool RefreshTokenPresent, DateTimeOffset? ExpiresAt, string? RefreshTokenSha256Prefix);
```

| Flow | Steps |
| --- | --- |
| `login_email` | Seeded user `lab-<seed hex>@lab.invalid` with a vault password: BetterAuth `sign-up/email` (first use) and `sign-in/email` through the Gateway by API; read the session cookie from `Set-Cookie`; add it to the browser context for `localhost`; open `http://localhost:14321/callback`; wait for `/dashboard`; read `sessionStorage` keys `meridian_access_token`, `meridian_refresh_token`, `meridian_expires_at` (presence and a hash prefix only) |
| `refresh` | `login_email`, then trigger the Panel's own refresh path (the packet finds it in `src/Dhadgar.Panel/src/lib/auth/`); if the Panel has no page-triggered refresh, call `{gateway}/api/v1/identity/refresh` from the page with `fetch` and the stored refresh token, and record `refresh_path = "direct"`; then replay the old refresh token, expect refusal |
| `logout` | `login_email`, open `/logout`; the three keys are gone; `/dashboard` redirects to `/login` |
| `dashboard_guard` | fresh context; open `/dashboard`; expect `/login` |

Tokens never leave the browser context: the result carries presence flags and an 8-hex SHA-256 prefix of the refresh token, so a rotation is visible without exposing it.

### C15. Coverage manifest

`src/Tools/Dhadgar.Lab/lab-coverage.json`:

```json
{
  "schema": 1,
  "entries": [
    { "project": "src/Dhadgar.Gateway", "status": "real", "adapter": "gateway", "catalog": "docs/guides/uat-specs/catalog/gateway.toml" },
    { "project": "src/Dhadgar.Identity", "status": "real-pending", "until": "L38", "adapter": "identity", "catalog": "docs/guides/uat-specs/catalog/identity.toml" },
    { "project": "src/Dhadgar.Servers", "status": "stub", "note": "DV-13: stub without auth; promote by shipping adapter and catalog rows" }
  ]
}
```

Rules (`LabCoverageTests`): every `src/**/*.csproj` outside `src/Tools/`, `src/Shared/` and `src/Dhadgar.AppHost` whose `Sdk` is `Microsoft.NET.Sdk.Web` or whose `OutputType` is `Exe` (this catches the CLI and the agents), and every `src/Dhadgar.*/package.json` gives a project directory; each project directory has exactly one entry (a directory with both a `.csproj` and a `package.json` is one entry). `real` needs `adapter` (a registered `ILabServiceAdapter.Name`) and `catalog` (a spec file whose rows include at least one with a `[[row.fault]]`). `real-pending` needs `until` naming a packet in this file. `stub` needs `note`. A promoted service ships its adapter, its catalog rows and its invariant TOML in the same PR.

### C16. MCP client configuration

Root `.mcp.json`:

```json
{
  "mcpServers": {
    "meridian-lab": {
      "type": "http",
      "url": "http://127.0.0.1:8789/mcp",
      "headers": { "Authorization": "Bearer ${MERIDIAN_LAB_TOKEN}" },
      "timeout": 1800000
    }
  }
}
```

Tool names as Claude Code sees them: `mcp__meridian-lab__<tool>`.

### C17. Fleet and seeds

```csharp
namespace Dhadgar.Lab.Random;
public sealed class LabRandom
{
    public LabRandom(ulong runSeed, string stream);   // stream e.g. "runner", "fault:<id>", "fleet:agent-0042", "fuzz:enroll"
    public ulong NextUInt64();
    public int Next(int minInclusive, int maxExclusive);
    public double NextDouble();
    public void NextBytes(Span<byte> buffer);
    public static ulong NewSeed();                    // RandomNumberGenerator; the only non-seeded source
}
```

The stream generator is SplitMix64 seeded with `runSeed ^ FNV-1a-64(stream)`; same seed and stream give the same sequence on every OS. Secrets (tokens, passwords, keys, lease ids) still come from `RandomNumberGenerator`; they are not part of replay.

```csharp
namespace Dhadgar.Lab.Fleet;
public sealed record FleetAgentOptions(int Index, Uri Ingress, string TokenRef, TimeProvider Time, LabRandom Random);
public sealed class FleetAgent : IAsyncDisposable    // one ServiceProvider built with AddAgentCore() plus fakes
{
    public static Task<Result<FleetAgent>> CreateAsync(FleetAgentOptions options, SecretVault vault, CancellationToken ct);
    public Task<Result<Guid>> EnrollAsync(CancellationToken ct);
    public Task<Result> RunAsync(CancellationToken ct);  // hub connection and heartbeats until cancelled
}
```

### C18. Observations, identities, fingerprint, pairing, replay fidelity (reserved in L48)

These are lab-internal types (namespace `Dhadgar.Lab.Observe`); nothing in a product project sees them, so they stay out of `Dhadgar.Contracts`. L48 ships every type, the sink, the files and the empty columns; later packets fill them. Names avoid C3's `Dhadgar.Lab.Audit.LabEvent`, which stays the audit event.

```csharp
namespace Dhadgar.Lab.Observe;

// Logical actor: who did it, independent of process, container or connection.
// lab | runner | driver | client:<n> | agent:<nnnn> | member:<id> | peer:<name> | tenant:<a|b>
public static class ActorIds
{
    public const string Lab = "lab", Runner = "runner", Driver = "driver";
    public static string Client(int n) => $"client:{n}";
    public static string Agent(int index) => $"agent:{index:D4}";
    public static Result<string> Validate(string actorId); // ^(lab|runner|driver|(client|agent|member|peer|tenant):[a-z0-9-]{1,32})$
}

// Logical operation: one attempt by one actor. A retry is a new OpId with CausedBy = the first.
public readonly record struct OpId(string RunId, string Actor, long Seq)
{
    public override string ToString() => $"{RunId}/{Actor}/{Seq}";
    public static Result<OpId> Parse(string text);
}

public enum ObservationKind { Invoke, Complete, FaultPrepare, FaultInject, FaultHeal, Observe, Note }
public enum OpOutcome { Ok, Fail, Unknown }

public sealed record LabObservation(
    long Seq,                 // assigned by the sink: 1, 2, 3 ... per run, total order of Append calls
    string RunId,
    DateTimeOffset At,        // TimeProvider
    string Actor,
    string? OpId,             // set on Invoke and Complete, and on Observe lines caused by an op
    ObservationKind Kind,
    string Op,                // tool or operation name, e.g. "enroll", "fault_toxic_add", "proxy_upstream_completed"
    JsonElement Args,         // redacted
    OpOutcome? Outcome,       // Complete only
    JsonElement Result,       // redacted; status, error code, ids
    string? CausedBy);        // the OpId or fault id this line follows from

public interface ILabObservationSink   // one singleton; one open writer per run
{
    Result BeginRun(string runId, string runFolder);     // opens <runFolder>\events.jsonl; a second BeginRun for the same id fails
    Task<Result<long>> EndRunAsync(string runId, CancellationToken ct); // closes open ops as Unknown, flushes, returns the count
    OpId NextOp(string runId, string actor);
    Result<long> Append(LabObservation draft);  // ignores draft.Seq; assigns it; redacts; appends one line to events.jsonl; no open run -> failure
    IReadOnlyList<LabObservation> Read(string runId, long afterSeq, int max);
}

public enum ReplayFidelity { Exact, ScheduleApproximate, EvidenceOnly }
public sealed record PairInfo(string ExperimentId, string Role, string PartnerRunId, string Relation); // Role: base | variant

public sealed record LabFingerprint(
    int Schema, string Commit, bool Dirty, string? BuildId, string? Baseline, string? Mutant,
    IReadOnlyDictionary<string, string> ImageDigests,           // image tag -> sha256 id
    string LockFilesSha256,                                     // SHA-256 over sorted "<path>\n<sha256>\n" of every packages.lock.json in the build tree
    IReadOnlyDictionary<string, string> EffectiveConfigSha256,  // service -> SHA-256 of its overlay plus lab env, secrets replaced by their refs
    IReadOnlyList<SpecHash> Specs, string DaemonVersion, string ToxiproxyVersion,
    ulong Seed, string Os, IReadOnlyDictionary<string, string> EngineVersions); // docker context -> server version
public sealed record SpecHash(string File, string Sha256);
public static class FingerprintHasher { public static string Sha256(LabFingerprint fingerprint); } // canonical JSON: sorted keys, no whitespace
```

Outcome rules (the history checker depends on them):

| Situation | Outcome |
| --- | --- |
| A response was read (any status) | `Ok` for the status the op defines as success, else `Fail` with the status |
| Connection refused, DNS or TLS failure, or the barrier refused before anything was forwarded | `Fail` (definitely not applied) |
| The request was written, then a timeout, reset, abort or cancellation before the response was read | `Unknown` (may or may not have been applied) |
| The client process or actor died with an op open | the recorder closes it as `Unknown` at run end |

Every `Invoke` has exactly one `Complete` with the same OpId by the end of a run (the recorder closes stragglers as `Unknown`). Faults write `FaultPrepare`, `FaultInject`, `FaultHeal` with `CausedBy = fault_id`. A run holds at most 100,000 observations; the 100,001st becomes one `Note` `truncated`, later appends are dropped, and the run's fidelity becomes `EvidenceOnly`.

Fidelity: `Exact` when every choice came from `LabRandom` and one actor ran at a time (plain `uat_run`); `ScheduleApproximate` when actors ran concurrently or timing toxics were active (history, chaos, soak, fleet, twins with faults); `EvidenceOnly` after truncation, a human attestation, or when the build in the fingerprint is gone. `uat_run {replay}` and `history_run {replay}` compare the stored fingerprint with the current one, list every difference in the replay's `run.json` `replay:{of, fingerprint_diff:[...], reproduced:true|false|null}`, and set `reproduced` by fidelity: `Exact` → the same grades and the same primary signatures; `ScheduleApproximate` → the original primary signature appears within 5 attempts; `EvidenceOnly` → `null`.

Behavioural coverage (L52). Keys recorded per run in C12 `coverage`; a universe per kind gives the "unhit" list:

| Kind | Key | Universe |
| --- | --- | --- |
| `route_status` | `<service> <METHOD> <route template> <status class>` (`2xx`, `4xx`, `5xx`), the path normalised to its template (GUIDs and digit runs matched against the universe's templates) | the templates in `src/Tools/Dhadgar.Lab/coverage-universe.json` × {`2xx`, `4xx`}; a `5xx` hit is listed but never part of the universe. Nodes' templates are kept equal to its `EndpointDataSource` by `NodesEndpointUniverseTests` in `tests/Dhadgar.Nodes.Tests` (it reads the file; no project reference); the Gateway's come from `GatewayRouteTable`; other services are added when their catalog ships |
| `fault_target` | `<fault kind>@<target id>` | `fault_list_targets` × `faults_allowed` |
| `error_code` | `<service>:<errorCode>` from problem+json bodies and hub error codes | `coverage-universe.json` `error_codes` (Nodes: the `AgentHubErrorCodes` constants and the problem `type` values in `src/Dhadgar.Nodes/Endpoints`, listed by L52) |
| `invariant_check` | `<invariant>/<check id>` that ran without error | every registered invariant and I3 check |
| `transition` | `<entity>:<from>-><to>` from successive `db_query` snapshots the runner takes at row ends (Node status, certificate active/revoked, token unused/used/revoked) | `coverage-universe.json` `transitions`: the designed transitions L52 lists from the Nodes code (`NodeStatus` assignments, certificate revocation, token use and revocation) |

### C19. History, barrier proxy, twins, mutants and the adversarial peer

```csharp
namespace Dhadgar.Lab.History;

public sealed record HistoryOp(string OpId, string Actor, string Key, string F, JsonElement Input,
    long InvokeSeq, long? CompleteSeq, OpOutcome Outcome, JsonElement Output);   // from C18 observations
public sealed record History(string RunId, string Scenario, IReadOnlyList<HistoryOp> Ops, JsonElement FinalState); // FinalState: db_query snapshot at run end

public interface IHistoryModel<TState> where TState : IEquatable<TState>
{
    string Name { get; }
    TState Initial(string key, JsonElement finalStateForKey);
    // The sequential spec: may op take effect in state s, and with what output? Unknown ops may also never take effect.
    bool TryStep(TState s, HistoryOp op, out TState next);
    bool FinalMatches(TState s, JsonElement finalStateForKey);
}

public sealed record HistoryViolation(string Rule, IReadOnlyList<string> OpIds, string Detail);
public sealed record CheckResult(string Verdict, IReadOnlyList<HistoryViolation> Violations, History? Minimal); // "linearizable" | "violation" | "unknown"

public static class LinearizabilityChecker
{
    // Wing-Gong-Lowe search with memoised (linearized-set, state) pairs, per key. More than 12 ops on one key
    // returns Verdict "unknown" with reason "history too long" (never a pass).
    public static CheckResult Check<TState>(History history, IHistoryModel<TState> model, TimeSpan budget) where TState : IEquatable<TState>;
    // Checker-level minimisation: delta-debugging over op subsets (complete ops only; unknown ops kept), deterministic.
    public static History Minimise<TState>(History failing, IHistoryModel<TState> model, TimeSpan budget) where TState : IEquatable<TState>;
}
```

`EnrollmentHistoryModel` (`History/Models/`; not L27's `Hunt/EnrollmentModel`, which is a CsCheck state model; key = token id; ops `enroll(public_key, hostname)`; state `Unused | Used(node_id, public_key) | Revoked`), today's product spec: `enroll` on `Unused` → `Ok(node_id)`, state `Used`; on `Used` or `Revoked` → `Fail(401)`. Under D-ML49 (if the owner rules yes), `enroll` on `Used(n, pk)` with the same `pk` → `Ok(n)`. Each op carries a seeded unique `Hostname` (`lab-<run>-<actor>-<seq>`), so the final state attributes every node row to the op that made it. Rules reported as violations:

| Rule | Statement | Required |
| --- | --- | --- |
| E1 | At most one `Ok` per token | yes |
| E2 | Every `Ok` node exists at the end with exactly one active certificate | yes |
| E3 | A definite `Fail` op made no node row (its hostname is absent) | yes |
| E4 | No `Fail(401)` whose `Complete` precedes the `Invoke` of the op that took effect (the token was refused before anyone used it) | yes |
| E5 | After an `Unknown` op that took effect (its hostname exists), a retry by the same actor with the same key does not leave the actor without a node it holds a certificate for (no orphan) | no until D-ML49; linked to `KI-ENROLL-RETRY` |
| E6 | A used token has `UsedByNodeId` equal to the one node its ops created; an unused token created no node | yes |

Scenarios (`docs/guides/uat-specs/catalog/enroll-history.toml`, read by `history_run`, not by `uat_run`):

| Id | Shape | Barrier rules |
| --- | --- | --- |
| `enroll-race` | 2 to 8 clients, one token, distinct keys | every client `hold_request(start)`; release when all arrived |
| `enroll-race-pg-latency` | as above with a 200 ms latency toxic on `nodes-postgres` | as above |
| `enroll-lost-response` | 1 client; its first enroll's response is dropped after Nodes answered; the client retries with the same key | first op `drop_response`; retry `pass` |
| `enroll-lost-response-new-key` | as above; the retry uses a new key | as above |

The barrier proxy (D-ML47):

```csharp
namespace Dhadgar.Lab.Barrier;
public enum BarrierAction { Pass, HoldRequest, HoldResponse, DropResponse }
public sealed record BarrierRule(string OpId, BarrierAction Action, string? Barrier, int HoldMs);
public sealed class BarrierProxy : IAsyncDisposable
{
    // Binds 127.0.0.1:LabPorts.Barrier only; upstream must pass LabHosts.IsLabEndpoint; started by history_run, disposed at its end.
    public static Task<Result<BarrierProxy>> StartAsync(Uri upstream, int port, ILabObservationSink sink, TimeProvider time, CancellationToken ct); // port: LabPorts.Barrier, or 0 in tests
    public Result Arm(string runId, IReadOnlyList<BarrierRule> rules);
    public Task<Result> WaitArrivedAsync(string barrier, int count, TimeSpan timeout, CancellationToken ct);
    public Result Release(string barrier);
}
```

Header `X-Lab-Op-Id: <OpId>` selects the rule and is removed before forwarding; a request without the header, or with an unknown OpId, is refused 400 (`lab.barrier.no_op`). `HoldRequest`: wait for the barrier's release (30 s max, then 504 `lab.barrier.timeout`), then forward. `HoldResponse`: forward, read the whole upstream response, write `Observe proxy_upstream_completed {status}` with `CausedBy = op`, wait `HoldMs` or the barrier, return it. `DropResponse`: forward, read the whole upstream response, write `proxy_upstream_completed`, then `HttpContext.Abort()`: the client sees a reset after sending, so its outcome is `Unknown`, while the history knows the server answered. Bodies are capped at 1 MiB; no redirects; plain HTTP only. Fallback for paths the proxy cannot carry (mTLS on 25443, after L41): a Toxiproxy `timeout` toxic with `timeout = 0` on `agent-ingress-tls` downstream, which holds data until removed; whether Nodes committed is then taken from `db_query`, not from the proxy. Assumption: Toxiproxy applies a newly added toxic to already-open connections (its README does not say; L51 checks it and records the answer).

Twin experiments (`docs/guides/uat-specs/twins/<name>.toml`, read by `twin_run`):

```toml
schema = 1
id = "enroll-latency-invariance"
relation = "equal_outcome"          # equal_outcome | isolated
seed = 42                           # both worlds use it
[base]
spec = "catalog/twin-enroll.toml"
rows = ["enroll-once"]
[variant]
spec = "catalog/twin-enroll.toml"
rows = ["enroll-once"]
faults = [{ tool = "fault_toxic_add", args = { proxy = "p:nodes-postgres", toxic = "latency", stream = "downstream", attributes = { latency = 1500 } } }]
# pre = { spec = "...", rows = ["..."] }   # optional, variant only: rows run on the reset stack before the compared rows (L56: tenant B acts first)
[[compare]]
pointer = "/rows/enroll-once/steps/enroll/status"
[[compare]]
pointer = "/rows/enroll-once/grade"
```

Pointers are RFC 6901 JSON pointers into a run projection `{rows:{<row id>:{grade, steps:{<step id>:{status, error_code, outcome}}, captures:{<var>:<value>}}}}`. A `[[compare]]` is either `pointer = "..."` (equal in both worlds) or `variant_pointers = ["...", "..."]` with `op = "disjoint"` (two lists in the variant world share no element); `disjoint` is allowed only under `isolated`. `equal_outcome`: every `compare` pointer has the same value in both worlds after normalisation (GUIDs, timestamps, durations and thumbprints masked). `isolated` (tenant flagship, L56): the variant world adds a second tenant's data and actions; tenant A's observed sets (`compare` pointers that name a list) are equal in both worlds, and tenant B never observes an id tenant A created. Both worlds run sequentially on a reset stack (`stack_reset_data`) with the same seed; each run's `run.json` carries `pair`. A relation that does not hold is a `twin:<experiment id>|<pointer>` failure (C12 kind `twin`) on both runs with the diff as evidence; it is never excused by a fault, because the variant's faults are the experiment. If any row named in `[base]` is not PASS, the relation is not evaluated: `held = null`, and the synthetic row `twin/<experiment id>` in both bundles is SKIPPED with reason `twin precondition: <row> <grade>`, so neither run's `overall` can be better than `incomplete` and the twin is never a pass; the failing base row keeps its own grade. When the relation is evaluated, the synthetic row is PASS (held) or FAIL (not held, with the `twin` signatures).

Mutants (lab-only artifacts, D-ML51). `tools/lab/mutants/<id>/mutant.toml` plus `<id>/change.patch` (a `git diff` against the commit named in `base`):

```toml
schema = 1
id = "M1-token-no-concurrency"
kind = "product"                    # product | lab
base = "<commit sha the patch applies to>"
title = "Drop the UsedAt concurrency token"
files = ["src/Dhadgar.Nodes/Data/Configurations/EnrollmentTokenConfiguration.cs"]
services = ["nodes"]                # images rebuilt from the patched tree and started for the detector
detector = { tool = "history_run", args = { scenario = "enroll-race", clients = 4 }, expect_pattern = "^hist:enrollment\\|(E1|E6)\\|enroll-race$" }
```

`expect_pattern` follows C12's pattern rules (kind prefix, 1 s timeout, never `.*`). A lab mutant (`kind = "lab"`) has `files` under `src/Tools/` and `detector = { test = "<fully qualified test name>" }` instead.

| Id | Kind | Change | Expected detector |
| --- | --- | --- | --- |
| M1 | product | remove `.IsConcurrencyToken()` on `EnrollmentToken.UsedAt` (`EnrollmentTokenConfiguration.cs`) | `history_run enroll-race`: E1 or E6 |
| M2 | product | drop `.Where(n => n.OrganizationId == organizationId)` in `NodeService.GetNodesAsync` | L56 tenant twin `isolated` |
| M3 | product | `TenantScopedHandler` succeeds without comparing the route org with `org_id` | L56 cross-tenant row |
| M4 | product | remove the `catch (DbUpdateConcurrencyException)` in `EnrollmentService` enrollment | `history_run enroll-race`: I1 500 |
| M5 | product | log the plaintext enrollment token in `EnrollmentService` | I4 |
| M6 | product | renewal skips superseding the presented certificate (`AgentEndpoints` renewal: the `if (!presentedWasInGrace)` block that sets `IsRevoked`, `RevokedAt` and `GraceUntil`) | L58 `peer_run stale-cert-after-renewal`: `^peer:stale-cert-after-renewal\|` (the old certificate stays usable after the grace shift) |
| M7 | product | `EnrollmentService.GenerateUniqueNodeNameAsync` drops `n.OrganizationId == organizationId &&` from both queries (name uniqueness leaks across tenants) | L56 tenant twin, enrollment leg: tenant A's node name changes when tenant B enrolled the same hostname first |
| LM1 | lab | `FaultBook` journals after inject | `FaultBook_JournalWrittenBeforeInject` |
| LM2 | lab | `RecoverAsync` skips `Prepared` entries | `FaultBook_PreparedNeverInjected_HealSucceeds` |
| LM3 | lab | lease end does not heal | `LeaseEnd_HealsAllFaults` |
| LM4 | lab | `Tripwire.Check` always passes | `FaultTargetGuardTests` tripwire case |
| LM5 | lab | `FaultImpactMap.Explains` returns true for any fault | `I1_503OnNodes_FaultOnDiscordWebhook_Counts` |
| LM6 | lab | `KnownIssues.Match` honours the row's claim without matching | `Match_ClaimedIssueSignatureDiffers_IsNew` |
| LM7 | lab | `RunOutcome.Compute` returns `pass` when no row ran | `Overall_AllSkipped_IsIncomplete` |
| LM8 | lab | `db_query` reads through `nodes-postgres` instead of 15432 (R-OBSERVE) | `ObservationPathTests` (L55 adds it) |

Mutant rules: a product mutant is built only by `lab_build {mutant}` from `git archive <HEAD of RepoRoot>` into `LabPaths.Builds\mutant-<id>-<build8>`, `git apply --check` then `git apply` there, never in a checkout; its images are tagged `meridian-lab/<project>:mutant-<id>-<build8>` with labels `meridian-lab=1` and `meridian-lab-mutant=<id>`, never with a plain build tag; `stack_status` and `StatusDto` show the mutant id while one runs; `mutation_run` ends with `svc_start` of the last plain build. A mutant's verdict is `detected` (the expected signal appeared), `detected_other` (another new failure appeared), `survived`, `stale` (`git apply --check` failed) or `unreachable` (the detector could not run; the verdict names the reason and a known issue, e.g. `KI-NODES-USER-AUTH` for M2 and M3 while Nodes accepts no user token, README F38). Gate MD (README) accepts only `detected`, `detected_other`, or `unreachable` with an open known issue. Lab mutants are applied only by `Dhadgar.Lab.SelfCheck` in a temp copy and judged by `dotnet test` of the named test (L55).

Adversarial peer (L58; D-ML52). `Dhadgar.Lab.Peer` actors (`peer:<attack>`) use only identities the lab enrolled in this run and connect only to `127.0.0.1:25000`/`25443` (agent ingress) or `15040` (Nodes direct, for the hub-only cases); `PeerTargetGuard` refuses anything else with `[Lab.BlastRadius]`:

| Attack | What the peer does | Expected now | Rule |
| --- | --- | --- | --- |
| `clone` | two hub connections with the same node certificate and key | D-ML53 (BlockedDecision); until ruled, observed only | D-ML53 |
| `replay-enroll` | replays a captured successful enroll body | 401, no second node | E1, E6 |
| `foreign-node-id` | node A's certificate calls `POST /api/v1/agents/{nodeB}/certificates/renew` | 403 or 404, B's certificates unchanged | yes |
| `bad-protocol` | hub connect with `X-Agent-Protocol` `"0"`, `"999"` and missing | `"0"` and `"999"`: aborted, no group join. Missing: accepted, because `AgentHub.OnConnectedAsync` treats a missing header as current (`AgentHub.cs:37`); recorded, not failed | yes for `"0"` and `"999"` |
| `post-revocation` | keeps an established hub connection open across a node decommission and a certificate revocation; one variant keeps heartbeating, one stays idle | heartbeating: the first `Heartbeat` after the change returns `HeartbeatFailed` or `NodeDecommissioned` and the hub aborts the connection (`AgentHub.cs:75-79`, `95-99`). Idle: the connection stays open, because nothing re-checks it without a heartbeat; observed only until D-ML54 | yes for heartbeating; D-ML54 for idle |
| `stale-cert-after-renewal` | renews node A, then connects to the hub and calls renew with the old certificate twice: inside the grace window, and after `nodes_cert_time_shift fields=["grace_until"] shift_minutes=-120` on the old row | inside grace: accepted (Sprint 2's grace, D-S212, `AgentCertificateState.IsUsable`); after grace: hub connect refused and renew 401 `thumbprint_mismatch` | yes for "after grace" |
| `oversize` | hub message of 65 KiB and an enroll body of 2 MiB | refused; I1 clean | yes |
| `slowloris` | 50 connections sending headers 1 byte per second to the ingress | the Gateway sheds them; legitimate enroll still 201 within 10 s | yes |

Peer identities: `nodes_enroll_probe` vaults the issued certificate PEM as `crt-N` beside `key-N` (L58 adds it; the result gains `cert_ref`). A peer builds its client certificate with `X509Certificate2.CreateFromPem(certPem, keyPem)` and, on Windows, re-imports it through a PKCS#12 export so `SslStream` can use the key; every exported buffer is zeroed. An attack's row JSON carries `{attack, required, expected, observed, held}`; a required attack that does not hold yields one `peer` signature (C12). A non-required one (`clone`, idle `post-revocation`) records `observed` and produces no signature.

### C20. Envelopes, version pairs, synthetic game and guided mode (wave 9)

Recovery envelopes (L57; D-ML61), `docs/guides/uat-specs/envelopes/<name>.toml`:

```toml
schema = 1
id = "nodes-postgres-outage"
workload = { kind = "fleet", scenario = "enroll_heartbeat" }      # fleet (L42) | spec = { spec = "...", rows = ["..."] } looped
fault = { tool = "fault_proxy_disable", args = { proxy = "p:nodes-postgres" } }   # one C4 fault tool; heal_after_s is set per cell
duration_s = [10, 30, 90]                                          # fault duration axis
load = [10, 50, 100]                                               # fleet agents, or concurrent spec loops
healthy = { success_ratio = 0.95, window_s = 30 }                  # ops that succeeded / ops attempted, over a sliding window
recovered_s = 120                                                  # D-ML61
metastable_floor_s = 360                                           # D-ML61; a cell's limit = max(floor, 3 x duration)
```

Cell id `d<duration>-l<load>`. Per cell, in seeded order: `stack_reset_data` → start the workload at the cell's load → warm-up 60 s, which must reach `healthy` (else the cell is `invalid`) → inject the fault with `heal_after_s = duration` → after the heal, the recovery time is the start of the first window in which `healthy` holds and C11 invariants are clean → stop the workload. Grades:

| Cell grade | Rule | Synthetic row `envelope/<id>/<cell>` |
| --- | --- | --- |
| `recovered` | recovery time ≤ `recovered_s` | PASS |
| `slow` | `recovered_s` < recovery time ≤ limit | PASS with reason `slow <n> s` (reported, never a signature) |
| `metastable` | not healthy by the limit, with the fault healed | FAIL, one `env` signature (C12) |
| `invalid` | warm-up never healthy, or the fault could not be injected or healed | SKIPPED with the reason (so `overall` is at best `incomplete`) |

Required: no `metastable` cell. Workload success is read from the workload's own observations (fleet metrics, row grades), never from the faulted path's proxy. Shipped envelopes: `nodes-postgres-outage` (above), `ingress-storm` (`fault_proxy_disable p:agent-ingress`, fleet `enroll_heartbeat`), `nodes-kill` (`fault_kill c:nodes` with `restart_after_s = duration`, fleet `enroll_heartbeat`). A job runs at most 4 h; cells left over are `invalid` with reason `job budget`.

Baselines and version pairs (L60, L61). `tools/lab/baselines.toml` names builds by commit; the repo has no git tags (F47):

```toml
schema = 1
[baseline.sprint2]
commit = "6df33384de62434bfaaf9777237740f1ac622e3a"   # Sprint 2 squash on main, PR #235
[baseline.pre-sprint2]
commit = "2d8e4d0b5c89b55072da7a4c6e1891972dfa4395"   # main before Sprint 2: no GraceUntil column, no agent hub
```

`lab_build {baseline}` builds from `git archive <commit>` into `LabPaths.Builds\baseline-<name>-<build8>` (the L54 mutant path without a patch) and tags images `meridian-lab/<project>:baseline-<name>-<build8>`. Version pairs, `docs/guides/uat-specs/versions/<name>.toml`:

```toml
schema = 1
id = "old-agent-new-nodes"
builds = { nodes = "head", gateway = "head", agent = "baseline:sprint2" }   # head | baseline:<name>, per service and for the agent
spec = "docs/guides/uat-specs/agent-console.toml"
rows = ["enroll", "heartbeat", "restart-no-reenroll"]
required = true                      # false: rows are run and recorded, no ver signature (D-ML58, D-ML59 rows)
# Optional, L61: a phase that runs first on the same data (no reset between), with its own builds and rows.
# prepare = { builds = { nodes = "baseline:pre-sprint2" }, spec = "docs/guides/uat-specs/time.toml", rows = ["seed-node"] }
# Optional, L61: kill a service once when its log shows a line, then restart it.
# kill_on_log = { service = "nodes", pattern = "^Applying migrations for NodesDbContext", restart_after_s = 5, timeout_s = 120 }
```

`version_run`: `stack_reset_data` once at the start; builds any missing baseline; runs `prepare` if present (its builds, then its rows; a prepare row that is not PASS makes every main row SKIPPED with reason `prepare: <row> <grade>`); `svc_start` each service with its main build id (`kill_on_log` is armed before this start: the job tails the service's log, `fault_kill`s it on the first match with `restart_after_s`, and records `{matched, matched_at, killed}`; no match within `timeout_s` records `matched = false`, never a failure by itself); `agent_install {build_id}` for the agent; runs the rows with the fingerprint's `Baseline` set; then `svc_start` the head build of every service it changed (also on cancel and failure). A required row that is not PASS yields one `ver` signature (C12). `pattern` is a C12-style regex (1 s timeout, never `.*`) over the message text of the JSON log line.

Synthetic game (L59). `src/Tools/Dhadgar.Lab.SyntheticGame` (console, lab-only): `--server-id <id> --port <p> --state <file>`; increments a counter every second and writes `{counter, at}` to the state file; answers `TICK\n` on `127.0.0.1:<port>` with `<counter>\n`; exits 0 on stdin `stop`. It runs inside a member under the agent's GameServerWrapper path (F36) or the agent's process manager, whichever the coder finds the agent uses (both if both exist). The probe mode `game-tick --server-id <id> --port <p>` reads it inside the member and prints `{alive, pid, counter, counter_at, restarts}` (the C7 `game` source). Continuity rows are `required = false` until D-ML56 is ruled.

Guided mode (L62; D-ML57). `tools/lab/guided.toml`: `enabled = false` until an A/B passes. Under `mode = "guided"`, each choice the hunter or chaos scheduler makes is drawn from `LabRandom` with probability `random_share` (≥ 0.3), else weighted towards C18 coverage keys that are still unhit (weight 1 + 4 × unhit keys the choice can reach, from a static choice-to-keys map the packet writes). `guided_ab_run` runs `runs` jobs per arm with paired seeds (`seed + i` in both arms) and writes `{arm, runs, distinct_new_signatures, findings_per_100, coverage_hit}` per arm. Guided ships enabled only when its `findings_per_100` is strictly higher than random's; a tie or a loss leaves `enabled = false` and the report says so.

---

## Wave 0: Windows containers spike (no code)

### L00. Spike: can this host run Windows Server Core containers, how, and beside Linux?

- **Agent:** Sonnet `general-purpose` (not `packet-coder`; there is no code). Runs unelevated first; steps marked (admin) need the elevated session the owner already uses for UAT (D-S239). It never enables a Windows feature, never reboots, never changes Docker Desktop settings files by hand, and never touches the dev Aspire containers or volumes.
- **Branch / worktree / commit:** none for the spike itself. Output `C:\Users\Steve\source\projects\MeridianConsole\docs\analysis\meridian-lab\worknotes\L00-windows-containers-spike.md`; ops commits it on the campaign branch with subject `docs(lab): record windows containers spike` after L01 cuts the branch.
- **Security review:** no.
- **Why first:** wave 6 (the Windows pool) depends on facts nobody knows yet (F33). The plan is written for each answer (D-ML30); this spike picks the branch.

**Steps.** Record each command and its raw output (trimmed) in the worknote. Stop a step on its first error, record it, and continue with the next step that does not depend on it.

1. Host facts: `Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' | Select-Object CurrentBuildNumber, UBR, DisplayVersion, EditionID`; `(Get-CimInstance Win32_ComputerSystem).HypervisorPresent`; `Get-CimInstance Win32_OptionalFeature | Where-Object Name -in 'Containers','Microsoft-Hyper-V','Microsoft-Hyper-V-All','VirtualMachinePlatform','Containers-DisposableClientVM' | Select-Object Name, InstallState`; `Get-Service vmcompute, vmms -ErrorAction SilentlyContinue`; `wsl --status`. Is the user in `docker-users`: `whoami /groups | Select-String docker-users`.
2. Linux engine: start Docker Desktop (`Start-Process "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe"`), wait for `docker version` (5 min max); `docker context ls`; `docker --context desktop-linux run --rm hello-world`.
3. Windows engine: is there a `desktop-windows` context? If not, record what Docker Desktop offers (`& "$env:ProgramFiles\Docker\Docker\DockerCli.exe" -SwitchWindowsEngine` exists?) and try the switch; record whether the Linux engine stays reachable while Windows is active (`docker --context desktop-linux version` during the switch).
4. Pull and run, with timings: `mcr.microsoft.com/windows/servercore:ltsc2025`, `mcr.microsoft.com/windows/nanoserver:ltsc2025`, `mcr.microsoft.com/dotnet/runtime:10.0-windowsservercore-ltsc2025`. For each: `docker run --rm --isolation=process <image> cmd /c ver`, then the same with `--isolation=hyperv`. Record the exact errors (host 26300 vs image 26100 is the expected process-isolation mismatch).
5. Side by side: with the Windows engine active, can `desktop-linux` run a container at the same time? Try both orders.
6. Inside a running Server Core container that worked in step 4 (`docker run -d --name meridian-lab-spike-01 --label meridian-lab=1 <image> ping -t localhost`), via `docker exec`: `sc.exe create LabSpike binPath= "C:\Windows\System32\ping.exe -t localhost"` then `sc.exe query LabSpike`; `reg add HKLM\SOFTWARE\MeridianLabSpike /v Probe /t REG_SZ /d x /f`; `powershell -Command "New-SelfSignedCertificate -Subject CN=meridian-lab-spike -CertStoreLocation Cert:\LocalMachine\My"`, export its public part and import it into `Cert:\LocalMachine\Root` inside the container. Then on the host: `Get-ChildItem Cert:\LocalMachine\Root | Where-Object Subject -eq 'CN=meridian-lab-spike'` must be empty, and `HKLM:\SOFTWARE\MeridianLabSpike` must not exist. Record both.
7. Networking: on the Linux engine run `docker run -d --name meridian-lab-spike-web --label meridian-lab=1 -p 127.0.0.1:18999:80 nginx:alpine`; from the Windows container try `curl.exe -s -o NUL -w "%{http_code}" http://host.docker.internal:18999/` and the host's NAT gateway address (`ipconfig` inside the container, `Get-NetIPAddress` on the host); record which address works and whether `127.0.0.1`-only publishing is reachable from the Windows container.
8. Limits: `--storage-opt size=20G` and `--memory 2g` on a Server Core run; start 5 Server Core containers at once and record start time, `docker stats --no-stream` memory and disk used (`docker system df`).
9. Clean up only what this spike created (`docker rm -f meridian-lab-spike-01 meridian-lab-spike-web` and the other spike names), switch back to the Linux engine if you switched, and confirm `docker --context desktop-linux ps` works. Leave the pulled images.

**Worknote shape.** Sections: Host, Linux engine, Windows engine, Isolation results (table: image, process, hyperv, start ms), Side by side, Inside the container, Networking, Limits, Outcome, Recommended `labsettings.json` values (`Lab:Docker:WindowsContext`, isolation, agent-ingress address, `Lab:Pool:MaxWindows`, storage-opt yes or no).

**Outcome (pick exactly one, with evidence):**

| Outcome | Meaning | What happens next |
| --- | --- | --- |
| A | Process isolation runs a Server Core image here | Wave 6 as written; the architect fills L31's settings from the worknote |
| B | Only Hyper-V isolation works, or it needs the `Containers`/`Microsoft-Hyper-V` features enabled | Orchestrator raises D-ML37 (and D-ML38 if side by side failed); wave 6 waits for the ruling |
| C | Neither works even with the features (or the owner declines D-ML37) | Architect re-slices wave 6 to the VM tier (D-ML31) or revives a narrow elevated helper (D-ML12) as a ruling; waves 7 to 9 proceed, L43 waits |

Also record: does side by side work (decides D-ML38), and which address the agent ingress needs (decides C10's NAT publishing).

**Checks:** the worknote exists, names one outcome, and step 6's two host checks are recorded.

---

## Wave 1: daemon foundation (no Sprint 2 dependency)

### L01. Scaffold, packages, isolation test, seam stubs, red S-L1 and S-L2

- **Branch / worktree / commit:** `ml/l01-scaffold` / `meridian-lab-l01` / `feat(lab): scaffold meridian-lab projects and red seam tests`
- **Depends on:** none (cut from `main`, which is the campaign branch's base).
- **Security review:** yes (new tool that will control the machine).
- **Rules:** R-CPM, R-NULL, R-XUNIT, R-CONTRACT, R-RESULT, R-DI.
  - R-CPM: no `Version=` in `.csproj`; add versions only to `Directory.Packages.props`; commit lock files.
  - R-NULL: build with `-p:TreatWarningsAsErrors=true`; fix analyzer warnings.
  - R-XUNIT: `[Fact]`, plain `Assert`, `Subject_Condition_Expectation`; seams carry `[Trait("Seam","S-L1")]`/`S-L2`.
  - R-CONTRACT: use `Dhadgar.Contracts.Nodes.EnrollNodeRequest`, never a copy.
  - R-RESULT: stubs return `Result.Failure("[Lab.NotImplemented] ...")`, never throw.

**Files**

1. `Directory.Packages.props`: add `ModelContextProtocol` 2.3.0, `ModelContextProtocol.AspNetCore` 2.3.0, `Tomlyn` 2.10.1. If restore reports NU1608/NU1109 downgrades, pin the transitive version the SDK needs in the same file and list it in the report.
2. `src/Tools/Dhadgar.Lab/Dhadgar.Lab.csproj`: `Microsoft.NET.Sdk.Web`, `OutputType Exe`, `AssemblyName meridian-lab`, `RootNamespace Dhadgar.Lab`, `IsPackable false`, `InvariantGlobalization true`. References: `Dhadgar.Shared`, `Dhadgar.Contracts`, `Dhadgar.Agent.Core`, `Dhadgar.Nodes`; packages `ModelContextProtocol.AspNetCore`, `Tomlyn`, `System.CommandLine`, `Npgsql`. Add `labsettings.json` (CopyToOutputDirectory PreserveNewest) with `{ "Lab": { "Port": 8789 } }`.
3. `src/Tools/Dhadgar.Lab/LabProgram.cs`: `namespace Dhadgar.Lab; public static class LabProgram { public static Task<int> Main(string[] args) => Task.FromResult(0); }` and `<StartupObject>Dhadgar.Lab.LabProgram</StartupObject>`. No top-level statements (Nodes' public `Program` would clash, CS0436).
4. Contract files exactly as in C1, C2 (types plus the `LabDaemon.StartAsync` stub returning `Failure("[Lab.NotImplemented] daemon")`), C3 (`LabGate`, `ILabEvents`, `NullLabEvents`, `LabEvent`, `LabCallContext`), C6 (interface, records, `NodesLabAdapter` stub, `EnrollProbe` stub). Folder per namespace: `Daemon/`, `Policy/`, `Tools/`, `Audit/`, `Nodes/`, root for `LabDefaults`, `LabPorts`, `LabIds`, `LabPaths`, `LabHosts` (implement `LabPaths.Create` and `LabHosts.IsLabEndpoint` fully).
5. `tests/Dhadgar.Lab.Tests/Dhadgar.Lab.Tests.csproj`: references `Dhadgar.Lab`, `Dhadgar.Nodes`; packages `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Xunit.SkippableFact`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.Extensions.TimeProvider.Testing`, `ModelContextProtocol`. Copy the conventions of `tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj`. If `Program` is ambiguous (CS0433/CS0436), put `Aliases="nodes"` on the Nodes reference and use `extern alias nodes;`.
6. `Dhadgar.sln`: add both projects under a `Tools` solution folder (`dotnet sln add ... --solution-folder Tools`; tests under the existing tests folder).
7. `tests/Dhadgar.Lab.Tests/LabIsolationTests.cs`: walks up from `AppContext.BaseDirectory` to the folder holding `Dhadgar.sln`; fails if any `*.csproj` outside `src/Tools/` and `tests/Dhadgar.Lab.*/` contains `Dhadgar.Lab`, or if any file under `deploy/`, `src/Dhadgar.AppHost/`, `.github/workflows/` or named `Dockerfile*` / `*.Dockerfile` contains `Dhadgar.Lab` or `meridian-lab`. Exempt: everything under `tools/lab/` (the lab's own images live in `tools/lab/images/`). A second fact, `LabIsolation_CatchesPlantedReference`, runs the same scan over a temp tree holding a planted `deploy/x.yaml` with `meridian-lab` and asserts it is reported (so the scan cannot pass vacuously).
8. `tests/Dhadgar.Lab.Tests/LabHostsTests.cs`: theory over accept/refuse cases (`http://127.0.0.1:15040/` yes; `http://127.0.0.1:5040/` no; `http://10.0.0.5:15040/` no; `file:///` no; `http://localhost.evil.com:15040/` no).
9. `tests/Dhadgar.Lab.Tests/Seams/McpEdgeSeamTests.cs` (S-L1, red): start `LabDaemon.StartAsync(new LabDaemonOptions(Port: 0, Token: <32 random bytes base64url>, DataRoot: <temp dir>, EnforceSingleInstance: false, RefuseElevated: false))`; assert success; connect an MCP client (`McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = new Uri(daemon.BaseAddress, "mcp"), AdditionalHeaders = { ["Authorization"] = "Bearer " + token } }))`, adapting to the SDK's actual names); `ListToolsAsync` contains `lab_status`; `CallToolAsync("lab_status")` is not an error. Separate facts with raw `HttpClient`: no token → 401; wrong token → 401; `Origin: http://evil` → 403; `Host: evil:8789` → 403.
10. `tests/Dhadgar.Lab.Tests/Seams/NodesEnrollSeamTests.cs` (S-L2, red): `LabNodesFactory : WebApplicationFactory<Program>` configured like `NodesWebApplicationFactory` (read it: environment, InMemory database name, auth replacements). Create a `NodesLabAdapter` whose `createContext` builds a `NodesDbContext` on the same InMemory store. Mint a token, `EnrollProbe.BuildRequest(...)`, POST to `/agents/enroll`, assert 201 and a `NodeId`. Red because the stubs fail.

**Tests and why they fail when reverted:** `LabIsolationTests` fails if a product project references the lab. S-L1 and S-L2 are red now on purpose; they fail as long as the stubs remain.

**Checks**

```powershell
dotnet restore <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj --force-evaluate
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L1&Seam!=S-L2"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L1|Seam=S-L2"   # expect failures; record them
```

**Docs owed:** `src/Tools/Dhadgar.Lab/README.md` (purpose, "dev and lab only, never production", layout, ports table C1).

### L02. Daemon edge

- **Branch / worktree / commit:** `ml/l02-daemon-edge` / `meridian-lab-l02` / `feat(lab): loopback daemon edge with bearer token, host and origin guards`
- **Depends on:** L01. **Security review:** yes.
- **Rules:**
  - R-RESULT: `StartAsync` returns `Failure` for every refusal; `daemon run` maps refusals to exit codes.
  - R-ZERO: decode the token into a `byte[]`, compare with `CryptographicOperations.FixedTimeEquals`, zero the per-request buffer.
  - R-USING: `Mutex`, pidfile `FileStream`.
  - R-LOG: never log the token or the `Authorization` header; log refusals as `lab.edge.refused {reason}` without values.
  - R-NULL, R-XUNIT, R-TIME.

**Contract excerpt:** C2 in full (routes, refusal order, `StatusDto`, `LabDaemon`, exit codes); `LabDefaults` from C1.

**Files**

1. `Daemon/LabDaemon.cs`: real `StartAsync`. `WebApplication.CreateSlimBuilder()`, config from `labsettings.json` and environment; `builder.WebHost.UseKestrel(o => { o.Listen(IPAddress.Loopback, port); if (Socket.OSSupportsIPv6) o.Listen(IPAddress.IPv6Loopback, port); })` and ignore `ASPNETCORE_URLS` (`builder.WebHost.UseUrls()` cleared, `PreferHostingUrls(false)`). Port 0 is allowed for tests; `BaseAddress` reports the bound port from `IServerAddressesFeature`.
2. `Daemon/TokenValidator.cs`: `static Result<byte[]> ParseToken(string? raw)`: base64url decode, at least `LabDefaults.MinTokenBytes`, else `[Lab.InvalidArgument]`.
3. `Daemon/EdgeGuardMiddleware.cs`: one middleware in the refusal order of C2. Host allowlist built from the bound port after start.
4. `Daemon/SingleInstanceGuard.cs`: `Result<SingleInstanceGuard> Acquire(LabPaths paths)`; mutex `Local\MeridianLab-<current user SID>` (on Linux `Local\MeridianLab-<uid>`); writes `daemon.pid`; on `Dispose` deletes the pidfile and releases. Already held → `[Lab.AlreadyRunning]` (exit 3).
5. `Daemon/ElevationCheck.cs`: Windows: `new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)` with the token elevation type (`GetTokenInformation(TokenElevationType)`); Linux: `geteuid() == 0`. Elevated and `RefuseElevated` → `[Lab.Elevated]` (exit 2).
6. `Daemon/StatusEndpoint.cs`: maps `/status` to a `StatusDto` with `Lease = new(false, null, null, null, null)`, `StackState = "down"`, empty processes, `ActiveFaults = 0`, empty `LeasedMembers`, `RunningJob = null` (L05, L07, L08, L19, L27 and L30 fill these through an `ILabStatusSource` interface defined here: `void Contribute(StatusBuilder builder)`).
7. `Cli/LabCli.cs` (System.CommandLine): `meridian-lab daemon run` (token from `MERIDIAN_LAB_TOKEN`; runs until Ctrl+C or `SIGTERM`), `meridian-lab token new` (prints a fresh 32-byte base64url token once). `LabProgram.Main` delegates to it.
8. `tests/Dhadgar.Lab.Tests/Daemon/EdgeGuardTests.cs`, `TokenValidatorTests.cs`, `SingleInstanceGuardTests.cs` (second acquire in the same process fails while the first is held; pidfile removed on dispose), `LabCliExitCodeTests.cs` (short token → 2; bind address config `Lab:BindAddress=0.0.0.0` → 2; there is no supported way to bind elsewhere, so `Lab:BindAddress` anything other than loopback is a refusal).

**Tests and why they fail when reverted:** every refusal test fails if a guard is removed (a request without a token would reach `/status` with 200). S-L1's four raw-HTTP facts go green here; its MCP fact stays red until L05.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L2&FullyQualifiedName!~McpEdgeSeamTests.Mcp"
```

**Docs owed:** README section "Daemon edge" (refusal order, exit codes).

### L03. Lease book

- **Branch / worktree / commit:** `ml/l03-lease` / `meridian-lab-l03` / `feat(lab): lease book with ttl, forced takeover and history`
- **Depends on:** L01. **Security review:** yes.
- **Rules:**
  - R-TIME: all time from the injected `TimeProvider`; tests use `FakeTimeProvider`.
  - R-RESULT: refusals are `Failure` with the C3 message format.
  - R-ZERO: lease id is 16 bytes from `RandomNumberGenerator`, hex-encoded; compare ids with `FixedTimeEquals` over the UTF-8 bytes.
  - R-LOG: events carry owner, purpose, action and reason; never the lease id.
  - R-NULL, R-XUNIT.

**Contract excerpt:** C3 `Dhadgar.Lab.Leasing` block and `ILabEvents`, `LabEvent`.

**Files**

1. `Leasing/LeaseBook.cs`: as C3. Thread-safe with a private `Lock`. `owner` and `purpose` required (1..100 chars, no control chars) else `[Lab.InvalidArgument]`. ttl clamped to `[Min, Max]`, default 600. `Acquire` while held and not forced → `[Lab.LeaseHeld] lease held by <owner> for "<purpose>" since <o>, expires <o>`. `force` without `reason` → `[Lab.InvalidArgument] force needs a reason`. Forced takeover records history `TakenOver` with `TakenOverBy` and `TakeoverReason` and emits `lab.lease {action: taken_over}` (the daemon logs it at Warning in L05). Wrong id on `Renew`/`Release`/`Touch` → `[Lab.LeaseRequired]` when free, `[Lab.LeaseHeld]` when someone else holds it. History capped at `HistorySize`.
2. `Leasing/LeaseSweeper.cs`: `BackgroundService` with `PeriodicTimer(TimeSpan.FromSeconds(5), time)` calling `Sweep()`.
3. Events: one `lab.lease` per change with `action` in `acquired`, `renewed`, `released`, `expired`, `taken_over`, `refused`.
4. `tests/Dhadgar.Lab.Tests/Leasing/LeaseBookTests.cs`: acquire/refuse naming the holder; ttl clamp (10 → 30, 9999 → 3600); touch extends expiry; expiry at exactly `ExpiresAt` via `FakeTimeProvider.Advance`; forced takeover needs a reason and records history; release with a wrong id fails; `LeaseEnded` fires on release, expiry and takeover; no event field contains the lease id.

**Tests and why they fail when reverted:** each semantic has its own fact; removing the ttl clamp, the reason check or the expiry breaks a named test.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~Leasing"
```

**Docs owed:** README section "Lease".

### L04. Audit log, secret vault, redactor

- **Branch / worktree / commit:** `ml/l04-audit-secrets` / `meridian-lab-l04` / `feat(lab): audit log, secret vault and redactor`
- **Depends on:** L01. **Security review:** yes.
- **Rules:**
  - R-ZERO: vault stores `char[]`; `Remove` and `Dispose` call `CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(span))`.
  - R-LOG: the audit entry has no arguments field at all.
  - R-PATH: audit files only under `LabPaths.Audit`.
  - R-USING: the audit writer owns one `FileStream` per day, disposed on roll-over.
  - R-RESULT, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C3 `Dhadgar.Lab.Secrets` and `Dhadgar.Lab.Audit` blocks; C9 secret-scan patterns (the redactor uses the same list).

**Files**

1. `Secrets/SecretVault.cs`: refs `tok-N`, `key-N`, `pwd-N`, `brr-N` (per-kind counters). `Use` copies nothing out: the delegate receives a `ReadOnlySpan<char>` over the stored array.
2. `Secrets/Redactor.cs`: replaces every vault value of length ≥ 8 (longest first) with `***<ref>`; regexes (compiled, 1 s timeout): `-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----` → `***pem`, `Bearer\s+[A-Za-z0-9._~+/=-]{20,}` → `Bearer ***`, `(Password|Pwd)=[^;]*` → `$1=***`. The daemon token is registered in the vault as `brr-1` at start. `Redact(JsonElement)` walks strings recursively.
3. `Audit/AuditLog.cs`: implements `ILabEvents`; writes JSON lines `{ts, name, ...fields}` to `audit-<yyyyMMdd>.jsonl`; `RecordCall(string callId, string tool, LabGate? gate, string caller, string? leaseOwner, string outcome, long durationMs)` writes a `lab.call` event.
4. Tests: `SecretVaultTests` (refs, `Use`, `Remove` zeroes: inspect via a test hook `internal char[]? PeekForTests(string ref)` with `InternalsVisibleTo`), `RedactorTests` (vault value inside text and JSON, PEM block, Bearer, connection string), `AuditLogTests` (one line per call, no argument data, daily roll-over with `FakeTimeProvider`).

**Tests and why they fail when reverted:** `RedactorTests` fail if any pattern is dropped; `SecretVaultTests.Remove_ZeroesStorage` fails if zeroing is removed.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~Secrets|FullyQualifiedName~Audit"
```

**Docs owed:** README section "Secrets and audit".

### L05. Tool registry, policy gate, MCP projection; S-L1 green

- **Branch / worktree / commit:** `ml/l05-tools-policy` / `meridian-lab-l05` / `feat(lab): deny-by-default tool policy and MCP tool projection`
- **Depends on:** L02, L03, L04. **Security review:** yes.
- **Rules:**
  - R-RESULT: tool failures become `LabToolResult(IsError: true, payload)` per the C3 error JSON; the MCP result sets `isError = true`.
  - R-LOG: audit has no arguments; `lab.lease` taken_over logs at Warning.
  - R-DI: add `LabDaemonServiceLifetimeTests`.
  - R-TIME, R-NULL, R-XUNIT, R-CONTRACT.

**Contract excerpt:** C3 (`ToolPolicy`, `ILabTool`, `LabTool<TArgs>`, `LabToolDispatcher`, error JSON), C4 rows for `lab_status` and `lab_lease_*`, C2 `StatusDto`.

**Files**

1. `Policy/ToolPolicy.cs`: the gate table with an entry for every tool in C4 (all of them, including tools later packets implement). `GateFor` returns null for any other name.
2. `Tools/LabTool.cs`, `Tools/LabJson.cs` (`JsonSerializerOptions` snake_case, `UnmappedMemberHandling.Disallow`, `JsonSchemaExporter.GetJsonSchemaAsNode` for the schema), `Tools/LabToolRegistry.cs` (all `ILabTool` from DI, name → tool; duplicate names fail at start).
3. `Tools/LabToolDispatcher.cs`: the seven steps of C3. The Machine gate is reserved: a tool carrying it is always refused with `[Lab.MachineReserved] no tool may use the machine gate`. No approval interface exists.
4. `Mcp/LabMcpServer.cs`: `services.AddMcpServer().WithHttpTransport(o => o.Stateless = true)`, `app.MapMcp(LabDefaults.McpPath)` behind the edge guard. Project each `ILabTool` into the SDK: preferred, one `McpServerTool` subclass `LabMcpTool` per tool whose `ProtocolTool` carries name, description and `InputSchema`, and whose `InvokeAsync` calls `LabToolDispatcher.InvokeAsync(name, arguments, new LabCallContext(callId, leaseIdFromArgs, "mcp"), ct)` and returns `CallToolResult { Content = [TextContentBlock(payload json)], StructuredContent = payload, IsError = isError }`. If the SDK's extension points differ, use its list/call handler hooks instead; the dispatcher must remain the only call path.
5. `Tools/Lab/LabStatusTool.cs`, `Tools/Lab/LeaseTools.cs` (acquire, renew, release, status). Wire `LeaseBook` (singleton), `LeaseSweeper`, `AuditLog`, `SecretVault`, `Redactor` into `LabDaemon`. Define `Leasing/ILeaseEndHook.cs` (`Task OnLeaseEndedAsync(LeaseHistoryEntry entry, CancellationToken ct)`); the daemon calls every registered hook on `LeaseEnded`, each in its own try/catch with a Warning log (no hook can stop another). L19 registers the FaultBook heal-all hook, L27 the job cancel hook, L30 the pool release hook. This packet registers none.
6. Tests:
   - `Policy/ToolPolicyTests.cs`: `EveryRegisteredTool_IsClassified` (build the daemon's provider, enumerate `ILabTool`, assert `GateFor` non-null); `PolicyTable_HasNoUnknownNames` (every policy entry is either registered or listed in a `PlannedTools` set that later packets empty out); `Dispatcher_UnclassifiedTool_IsRefused` (register a test tool missing from the table → `Lab.Unclassified`); `Dispatcher_LeasedWithoutLease_IsRefused`; `Dispatcher_MachineGate_IsReserved` (a test tool with the Machine gate and a valid lease → `Lab.MachineReserved`); `PolicyTable_HasNoMachineTools` (no C4 entry carries `Machine`); `LeaseEnd_RunsEveryHook_EvenWhenOneThrows`.
   - `LabDaemonServiceLifetimeTests.cs`: R-DI.
   - S-L1 now green in full.

**Tests and why they fail when reverted:** removing the null-gate check makes `Dispatcher_UnclassifiedTool_IsRefused` fail; dropping the MCP projection turns S-L1 red.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L2"
```

**Docs owed:** README sections "Tool tiers" (the C4 table with gates) and "Adding a tool" (implement `LabTool<TArgs>`, add the policy entry, add a test).

### L06. Install scripts, scheduled task, `.mcp.json`

- **Branch / worktree / commit:** `ml/l06-install` / `meridian-lab-l06` / `feat(lab): install scripts, scheduled task and MCP client config`
- **Depends on:** L05. **Security review:** yes.
- **Rules:**
  - R-ARGS: scripts call executables with argument arrays (`& $exe @args`), never `Invoke-Expression` or string-built commands.
  - R-LOG: scripts never print the token; `Set-Content`/`Write-Host` never receive it.
  - R-PATH: scripts resolve paths with `Join-Path` from `$env:LOCALAPPDATA` and `$PSScriptRoot`; refuse to run outside the repo.
  - R-XUNIT, R-NULL.

**Contract excerpt:** C1 (`LabDefaults`, `LabPaths.Bin`, `Lab:RepoRoot`), C16.

**Files**

1. `tools/lab/install.ps1`: `dotnet publish <repo>\src\Tools\Dhadgar.Lab -c Release -o $env:LOCALAPPDATA\meridian-lab\bin`; writes `Lab:RepoRoot` into the published `labsettings.json`; if the user env var `MERIDIAN_LAB_TOKEN` is empty, runs `meridian-lab token new`, captures it into a variable and stores it with `[Environment]::SetEnvironmentVariable('MERIDIAN_LAB_TOKEN', $t, 'User')`, then removes the variable; calls `daemon.ps1 -Install`. Prints "restart your terminal so Claude Code sees MERIDIAN_LAB_TOKEN". The script may run elevated (the UAT runner agent runs elevated, D-S239), but the task it registers always runs `RunLevel Limited` as the logged-on user (`New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited`), so the daemon itself never runs elevated. There is no machine switch.
2. `tools/lab/daemon.ps1`: `-Install` (scheduled task `MeridianLabDaemon`, trigger at logon of the current user, `RunLevel Limited`, action `meridian-lab.exe daemon run`, restart on failure 3 times every minute, no execution time limit), `-Uninstall`, `-Start`, `-Stop`, `-Status` (reads `/status` with the token header).
3. `tools/lab/README.md`: install, start, stop, token rotation (`token new` then restart the task and the terminal).
4. `.mcp.json` at the repo root, exactly C16.
5. `tests/Dhadgar.Lab.Tests/Install/McpConfigTests.cs`: parses `.mcp.json`; asserts the `meridian-lab` entry's type, url (port equals `LabDefaults.DaemonPort`, host `127.0.0.1`, path `LabDefaults.McpPath`) and the header value. `ScriptsParseTests` (skip if `pwsh` is not on PATH): runs `pwsh -NoProfile -NonInteractive -Command` with `ArgumentList` to call `[System.Management.Automation.Language.Parser]::ParseFile` on each script and asserts no parse errors; asserts neither script contains `Invoke-Expression` or `RunLevel Highest`.

**Tests and why they fail when reverted:** `McpConfigTests` fails if the port or path drift from the daemon defaults.

**Checks**

```powershell
dotnet build <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L2"
```

**Wave 1 gate (agent-run, D-S239):** after the wave merges, the orchestrator dispatches a Sonnet `general-purpose` agent to run `tools\lab\install.ps1` from the campaign worktree and `daemon.ps1 -Status` (200, lease free) and record both in `worknotes/wave1-gate.md`. The one owner step is restarting the terminal so Claude Code sees `MERIDIAN_LAB_TOKEN`; after it, the next session confirms `claude mcp list` shows `meridian-lab` connected and `lab_status` answers, and appends that to the worknote.

**Docs owed:** `tools/lab/README.md`.

---

## Wave 2: stack, services and reads (no Sprint 2 dependency)

### L07. Process supervisor, log capture, watchdog, `logs_tail`

- **Branch / worktree / commit:** `ml/l07-supervisor` / `meridian-lab-l07` / `feat(lab): process supervisor with job objects, log capture and watchdog`
- **Depends on:** L05. **Security review:** yes (process control).
- **Rules:**
  - R-ARGS: `ArgumentList`, `UseShellExecute = false`, `RedirectStandardOutput/Error = true`.
  - R-USING: `Process`, job handles (`SafeHandle`).
  - R-OS: Job Object code behind `[SupportedOSPlatform("windows")]`; on Linux kill the process tree with `Process.Kill(entireProcessTree: true)`.
  - R-LOG: children's environment (it carries connection strings) is never logged; log lines pass through `Redactor` before the ring buffer and the file.
  - R-TIME, R-RESULT, R-PATH (log files under `LabPaths.Logs`), R-NULL, R-XUNIT.

**Contract excerpt:** C4 `logs_tail`; C2 `ProcessStatusDto`; D-ML19 (max 3 relaunches in 5 minutes, only while a lease is held); C11 I4 (raw-line observer) and I6 (restart counting).

**Files**

1. `Processes/LabProcessSpec.cs`: `sealed record LabProcessSpec(string Name, string FileName, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment, string WorkingDirectory, Func<CancellationToken, Task<bool>>? Readiness)`.
2. `Processes/LabProcessSupervisor.cs`: `Task<Result<ProcessStatusDto>> StartAsync(LabProcessSpec, CancellationToken)`, `StopAsync(name)`, `RestartAsync(name)`, `IReadOnlyList<ProcessStatusDto> Snapshot()`; implements L02's `ILabStatusSource` for processes. Watchdog: on unexpected exit, relaunch if `LeaseBook.Current != null` and fewer than 3 relaunches in 5 minutes; else state `crashed`. `LeaseEnded` stops nothing (processes keep running) but disables relaunch.
3. `Processes/WindowsJobObject.cs`: `LibraryImport` `CreateJobObjectW`, `SetInformationJobObject` with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, `AssignProcessToJobObject`. One job per daemon; children die with the daemon.
4. `Processes/LogRing.cs` (per process, 20 000 lines) and `Processes/JsonLogLineParser.cs` (parses `Logging__Console__FormatterName=json` lines: `Timestamp`, `LogLevel`, `Category`, `EventId`, `Message`; a non-JSON line is kept as `message` with level `Raw`). Also appends to `logs/<name>.jsonl`.
5. `Tools/Logs/LogsTailTool.cs`: `logs_tail` per C4; `since` accepts ISO or `-<n>m|-<n>s`.
6. `Processes/ContainerLogFollower.cs`: a container's logs are captured by a supervised child process, not by the Docker API: `LabProcessSpec(Name: <service>, FileName: "docker", Arguments: ["--context", <Lab:Docker:LinuxContext>, "logs", "-f", "--since", <ISO start>, "meridian-lab-<service>"], ...)` with `Relaunch = false`. `Task<Result> FollowAsync(string service, string container, CancellationToken)`; L09 calls it after each container start, L31 for members (with the Windows context). Add `bool Relaunch = true` as the last `LabProcessSpec` member.
7. `Processes/IRawLineObserver.cs`: `void Observe(string process, ReadOnlySpan<char> rawLine)`, called for each line **before** redaction and never stored; default `NullRawLineObserver`. L21's I4 implements it to catch a secret the product logged. `Processes/IFaultTargets.cs`: `bool IsFaulted(string targetId)`, default `NoFaults` (false); the watchdog never relaunches a process whose target (`c:<name>` or `proc:<name>`) is faulted, and does not count that exit as a crash. L19's `FaultBook` implements it.
8. Restart counter: `ProcessStatusDto.Restarts` counts unexpected exits only; `event EventHandler<ProcessExit>? Exited` (`ProcessExit(string Name, int? ExitCode, bool Expected, DateTimeOffset At)`) feeds L21's I6.
9. `tests/Dhadgar.Lab.TestChild/` (new console exe, in `Dhadgar.sln` under tests): `--lines <n> --json --exit <code> --sleep-ms <n>`; prints JSON console-format log lines. Referenced by the tests as a content copy or located via its build output path.
10. Tests: `SupervisorTests` (start, capture lines, stop; crash relaunch with a lease; no relaunch without a lease; 4th crash in 5 minutes stays `crashed`, with `FakeTimeProvider`; a faulted target's exit is neither relaunched nor counted), `JsonLogLineParserTests`, `LogsTailToolTests` (filters, cap 2000, redaction of a vault value in a log line), `RawLineObserverTests` (the observer sees the raw secret; the ring and file see `***<ref>`), `ContainerLogFollowerTests` (argument list starts with `--context`, `Relaunch` false).

**Tests and why they fail when reverted:** `Supervisor_CrashWithoutLease_IsNotRelaunched` fails if the lease check is dropped; `Supervisor_FaultedTarget_IsNotRelaunched` fails if a kill fault would be undone by the watchdog; the redaction fact fails if logs bypass `Redactor`.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\tests\Dhadgar.Lab.TestChild\Dhadgar.Lab.TestChild.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L2"
```

**Docs owed:** README section "Processes and logs".

### L08. Docker layer, target registry, lab network and infrastructure

- **Branch / worktree / commit:** `ml/l08-stack` / `meridian-lab-l08` / `feat(lab): lab-owned docker network, infrastructure and target registry`
- **Depends on:** L05. **Security review:** yes (docker control, passwords, blast radius).
- **Rules:**
  - R-DOCKER: every docker call is `docker --context <Lab:Docker:LinuxContext> ...` built with `ArgumentList`; port bindings always `127.0.0.1:<lab port>:<container port>`.
  - R-ZERO / R-LOG: passwords from `RandomNumberGenerator` (24 bytes base64url) go into the vault (`pwd-N`) and reach `docker run` only through `--env-file` (a temp file under `LabPaths.State` with a user-only ACL, deleted right after the run), never `-e NAME=value`.
  - R-BLAST: the stack only creates, inspects and removes containers it registered; it never runs `docker volume`, `docker system` or `docker rm -v` on anything else.
  - R-RESULT: docker missing or failing → `[Lab.Docker] ...`.
  - R-USING, R-PATH, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C1 `LabPorts`; C10 (network, infrastructure table, proxies `gateway-redis`, `gateway-nodes`, `nodes-postgres`, `nodes-rabbitmq`, `agent-ingress`, target ids, registry rules); C4 `stack_up`, `stack_down`, `stack_status`; D-ML6, D-ML36.

**Files**

1. `Docker/IDockerCli.cs`, `Docker/DockerCli.cs`: `Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)`; `enum DockerEngine { Linux, Windows }`; the context comes from config and is always the first two arguments (`--context`, `<name>`). `DockerOutput(int ExitCode, string StdOut, string StdErr)`. Never pass a string command line.
2. `Docker/LabTargetRegistry.cs`: `ILabTargetRegistry` with `Result<LabTarget> Register(string targetId, LabTargetKind kind, string name, string? containerId)`, `LabTarget? Find(string targetId)`, `IReadOnlyList<LabTarget> All`, `Remove(string targetId)`, `Task<int> ReconcileAsync(CancellationToken)` (drops entries whose container is gone). Persisted to `LabPaths.State\targets.json` (write temp, then `File.Move` overwrite). `LabTarget(string TargetId, LabTargetKind Kind, string Name, string? ContainerId, DateTimeOffset CreatedAt)`.
3. `Stack/LabStack.cs`: `UpAsync(bool wipe)`: network `meridian-lab-net` (create if missing, labelled); containers `meridian-lab-postgres`, `meridian-lab-rabbitmq`, `meridian-lab-redis`, `meridian-lab-toxiproxy` per C10, each `--label meridian-lab=1 --restart no --network meridian-lab-net`; registers `c:<name>`. Images: read `src/Dhadgar.AppHost/Program.cs` for the Postgres and Redis image and tag (if unpinned use `postgres:17` and `redis:7`, as constants). Readiness (60 s): `pg_isready`, `rabbitmq-diagnostics -q ping`, `redis-cli ping`, Toxiproxy `GET /version`. Then databases `meridian_lab_nodes`, `meridian_lab_identity`, `meridian_lab_notifications`, `meridian_lab_discord`, `meridian_lab_betterauth`, roles `lab_owner` (owns every lab DB) and `lab_reader` (`CONNECT`, `USAGE ON SCHEMA public`, `ALTER DEFAULT PRIVILEGES FOR ROLE lab_owner IN SCHEMA public GRANT SELECT ON TABLES TO lab_reader`) via `docker exec meridian-lab-postgres psql -v ON_ERROR_STOP=1 -U postgres -c <sql>` (ArgumentList); registers `d:<service>`. RabbitMQ: user `lab` (vault password), vhost `/`. `wipe` removes the registered containers (anonymous volumes with them) before starting. `DownAsync(bool wipe)`. Implements `ILabStatusSource` for stack state.
4. `tools/lab/images/rabbitmq.Dockerfile`: `FROM rabbitmq:4-management` (match the AppHost major), `ADD` the `rabbitmq_delayed_message_exchange` `.ez` for that RabbitMQ minor from its GitHub release with `--checksum=sha256:<hash>` (look up the hash on the release page and write it in), `RUN rabbitmq-plugins enable --offline rabbitmq_delayed_message_exchange`. Built by the stack as `meridian-lab/rabbitmq:<sha8 of the Dockerfile>` with the label; Docker tests tagged `RequiresNetwork` too.
5. `Faults/ToxiproxyClient.cs` (minimal here; L19 adds toxics): `HttpClient` at `http://127.0.0.1:18474` (refuse any other base address: loopback and `LabPorts.ToxiproxyApi` only); `CreateProxyAsync(ProxySpec)`, `ListProxiesAsync()`, `ResetAsync()`. `ProxySpec(string Name, string Listen, string Upstream, bool Enabled)`. `Faults/ProxyCatalog.cs`: the C10 proxy table as static data with the owning packet; `LabStack` creates the L08 rows after Toxiproxy is ready and registers `p:<name>`.
6. `Stack/LabConnectionStrings.cs`: `Result<string> ServiceDb(string service)` (host `meridian-lab-toxiproxy`, the service's proxy port, `meridian_lab_<service>`, `lab_owner`), `Result<string> Reader(string service)`, `OwnerDirect(string service)` (the daemon's own writes: L11 mint and reset) and `Admin()` (superuser, L20 DB faults), all host `127.0.0.1:15432` (observation path, R-OBSERVE), `RabbitMq(string service)`, `Redis(string service)`; built inside `SecretVault.Use`; passed only to container env files and Npgsql.
7. `Tools/Stack/StackTools.cs`: `stack_up`, `stack_down`, `stack_status`.
8. Tests: `LabStackTests` with a fake `IDockerCli` recording argument lists: every call starts `--context`, labels present, ports bound to `127.0.0.1`, no password in any argument, `--restart no`; `stack_down` never touches an unregistered name; `LabTargetRegistryTests` (persist, reload, reconcile drops a vanished container); `ProxyCatalogTests` (no duplicate names or listen ports; every proxy's owning packet is in C10); `LabStackDockerTests` (`RequiresDocker`, `RequiresNetwork`): up, `stack_status` is `up`, Toxiproxy lists the five L08 proxies, `lab_reader` cannot `CREATE TABLE`, the delayed-message plugin is enabled (`rabbitmq-plugins list -e`), down with wipe leaves no labelled container.

**Tests and why they fail when reverted:** `Up_BindsPortsToLoopbackOnly`, `Up_NeverPutsPasswordsInArguments`, `EveryDockerCall_HasExplicitContext` and `Down_IgnoresUnregisteredContainers` fail on the matching regression.

**Checks**

```powershell
dotnet restore <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj --locked-mode
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L2&RequiresDocker!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~LabStackDockerTests"   # Docker running
```

**Docs owed:** README section "Lab stack" (C10 tables, never the dev containers, ports or volumes).

### L09. `lab_build`, lab service image, Gateway and Nodes containers behind proxies

- **Branch / worktree / commit:** `ml/l09-build-services` / `meridian-lab-l09` / `feat(lab): portable builds and gateway and nodes containers behind toxiproxy`
- **Depends on:** L07, L08. **Security review:** yes.
- **Rules:**
  - R-PATH: `checkout` validates under `Lab:RepoRoot` or `<RepoRoot>\.claude\worktrees\`; outputs only under `LabPaths.Builds`.
  - R-ARGS / R-DOCKER: `dotnet`, `git` and `docker` through `ArgumentList`; `git -C <checkout> rev-parse HEAD`, `git -C <checkout> status --porcelain`.
  - Lock files (D-S241, F14): `dotnet restore <csproj> --locked-mode` then `dotnet publish <csproj> -c Release --no-restore -o <out>`. Never `-r`. A restore that would change a lock file fails the build with `[Lab.ProcessFailed] lock file drift`.
  - R-LOG: never log container env files or the child environment.
  - R-RESULT, R-USING, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C4 `lab_build`, `svc_*`, `svc_config_set` and its allowlist; C10 (services table, proxies, build projects, observation paths, declared `/tmp` tmpfs); C1 ports.

**Files**

1. `Build/LabBuilder.cs`: projects `gateway`, `nodes`, `probe` in this packet (`agent_windows` in L31, Node apps in L34, the rest in L36 and L37; unknown project → `[Lab.InvalidArgument]`). `build8` = first 8 of the commit; a dirty tree gets `-dirty-<yyyyMMddHHmmss>`. Publishes to `<Builds>\<build_id>\<project>`, then `docker build` with `tools/lab/images/dotnet-service.Dockerfile` (`--build-arg PROJECT_DLL=<name>.dll`, context = the publish folder) to `meridian-lab/<project>:<build_id>` with `--label meridian-lab=1`. Writes `build.json` `{build_id, commit, dirty, checkout, projects, images, at}` and `<Builds>\latest.txt`.
2. `tools/lab/images/dotnet-service.Dockerfile`: `FROM mcr.microsoft.com/dotnet/aspnet:10.0`, `WORKDIR /app`, `COPY . .`, `ENV ASPNETCORE_URLS=http://+:8080`, `USER app`, `ENTRYPOINT ["dotnet", "/app/${PROJECT_DLL}"]` (use an `ARG` plus a shell-free exec form via a tiny `entrypoint.sh` if `ENTRYPOINT` cannot expand the arg; record which).
3. `Services/ILabServiceAdapter.cs`: `string Name`, `string Container`, `int HostPort`, `IReadOnlyList<string> Proxies`, `Result<IReadOnlyDictionary<string,string>> Environment(LabServiceContext ctx)`, `string HealthPath`. `Services/GatewayAdapter.cs`, `Services/NodesAdapter.cs`. Read `src/Dhadgar.Nodes/appsettings*.json`, `Program.cs`, `src/Dhadgar.Gateway/appsettings.json` and `Dhadgar.ServiceDefaults` to name the keys exactly. Environment for both: `ASPNETCORE_ENVIRONMENT=Development`, `Logging__Console__FormatterName=json`, `Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Information` (I1 reads it). Nodes: `ConnectionStrings__Postgres` = `ServiceDb("nodes")`, `RabbitMq__Host=meridian-lab-toxiproxy`, `RabbitMq__Port=20021` (or the key Nodes reads), `RabbitMq__Username`/`__Password`, the CA storage path → `/lab-state/nodes-ca` with a bind mount from `LabPaths.State\nodes-ca` (so the lab CA is never the dev CA). Gateway: `ReverseProxy__Clusters__nodes__Destinations__d1__Address=http://meridian-lab-toxiproxy:20012/`, `Redis__ConnectionString=meridian-lab-toxiproxy:20010`, `Cloudflare__EnableDynamicFetch=false`, `Readyz__RequiredClusters__0=nodes` (L34 adds `identity`).
4. `Services/LabServices.cs`: `StartAsync(service, buildId)`: `docker run -d --name meridian-lab-<service> --label meridian-lab=1 --restart no --network meridian-lab-net --tmpfs /tmp:rw,size=64m -p 127.0.0.1:<port>:8080 --env-file <temp> meridian-lab/<project>:<build_id>`; registers `c:<service>`; starts an L07 container log follower; readiness GET `http://127.0.0.1:<port><HealthPath>` until 200, 90 s. `StopAsync`, `RestartAsync`. Services table driven by the registered adapters.
5. `Services/ConfigOverlay.cs`: `config/<service>.json`, allowlist per C4; an unknown key → `[Lab.InvalidArgument] key not allowed`; applied at the next start.
6. `Tools/Services/BuildTool.cs`, `ServiceTools.cs` (`svc_start` refuses when the stack is not `up`: `[Lab.NotFound] stack is down`).
7. `tools/lab/images/probe.Dockerfile` (used by L36): `FROM mcr.microsoft.com/dotnet/runtime:10.0`, copies the `probe` publish folder, `ENTRYPOINT ["dotnet","/app/meridian-lab-probe.dll"]`.
8. Tests: `LabBuilderTests` (argument lists: `--locked-mode`, `--no-restore`, no `-r`; checkout outside the repo refused; `..` refused), `ServiceAdapterTests` (Nodes reaches Postgres and RabbitMQ only through Toxiproxy ports; Gateway reaches Nodes through `gateway-nodes`; CA path under lab state; `Cloudflare__EnableDynamicFetch=false`; no key outside the allowlist; overlay applied), `ServicesDockerTests` (`RequiresDocker`, slow): build, stack up, start Nodes and Gateway, both ready, `GET http://127.0.0.1:15000/api/v1/agents/ca-certificate` → 200; the same through `127.0.0.1:25000` (agent ingress) → 200.

**Tests and why they fail when reverted:** `Nodes_ReachesPostgresOnlyThroughProxy` fails if an adapter points straight at the database (faults would stop working); `Build_NeverPassesRuntimeIdentifier` fails on a bare `-r`; `Nodes_CaStorage_IsUnderLabState` fails if the lab points at the dev CA.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L2&RequiresDocker!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~ServicesDockerTests"   # Docker running
git -C <W> status --porcelain   # must show no packages.lock.json change after the Docker test
```

**Docs owed:** README section "Builds and services" (portable publish, images, why no `-r`).

### L10. `db_query` and `http_request`

- **Branch / worktree / commit:** `ml/l10-db-http` / `meridian-lab-l10` / `feat(lab): read-only lab db queries and lab-only http probes`
- **Depends on:** L08. **Security review:** yes (SQL, SSRF).
- **Rules:**
  - R-HOST: `LabHosts.IsLabEndpoint` on the composed URI; `AllowAutoRedirect = false`; refuse a `Host` header override.
  - R-RESULT: a write attempt maps to `[Lab.ReadOnly]` (Postgres SQLSTATE `25006` or `42501`).
  - R-USING: `NpgsqlConnection`, `NpgsqlCommand`, `NpgsqlDataReader`, `HttpResponseMessage`.
  - R-LOG / R-ZERO: `${secret:<ref>}` is resolved inside `SecretVault.Use` right before sending; the request body is never logged or returned.
  - R-NULL, R-XUNIT.

**Contract excerpt:** C4 `db_query`, `http_request`; C7 note that params are bound, never interpolated; C10 observation paths and the `agent_ingress` via (R-OBSERVE: `db_query` reads `127.0.0.1:15432` directly, never through Toxiproxy).

**Files**

1. `Data/LabDb.cs`: `Task<Result<DbQueryResult>> QueryAsync(string database, string sql, IReadOnlyDictionary<string, JsonElement> parameters, int maxRows, CancellationToken)`. `database` is one of the C4 names, else `[Lab.InvalidArgument]`. Connects with `LabConnectionStrings.Reader(database)` (direct host port); `BEGIN TRANSACTION READ ONLY`; `SET LOCAL statement_timeout = 5000`; refuses SQL containing a `;` other than one trailing (a small scanner that skips quoted strings and comments); binds `@name` parameters from JSON (string → text, number → numeric, bool, null; authors cast in SQL); reads `maxRows + 1` rows; values to JSON (timestamps ISO 8601). `DbQueryResult(IReadOnlyList<string> Columns, IReadOnlyList<Dictionary<string, JsonElement>> Rows, int RowCount, bool Truncated)`.
2. `Http/LabHttp.cs`: named `HttpClient` (`IHttpClientFactory`), 30 s timeout, body limit 1 MiB in and 64 KiB returned; JSON bodies parsed, others returned as text; `duration_ms` from `TimeProvider.GetTimestamp`. `via` maps: `gateway` → `127.0.0.1:15000`, `agent_ingress` → `127.0.0.1:25000` (through Toxiproxy, so agent-path faults apply), a service name → its C1 port (only services that exist in the build; others `[Lab.NotFound]`). A request timeout is a result `{status: 0, error: "timeout", duration_ms}`, not a tool error, so fault rows can assert on it.
3. `Tools/Data/DbQueryTool.cs`, `Tools/Http/HttpRequestTool.cs`.
4. Tests: `SqlStatementScannerTests` (`select 1; drop table x` refused; `select ';'` allowed), `LabHttpTests` (non-lab URL refused; redirect not followed using a `TestServer`-backed handler; placeholder resolved and redacted in the returned echo), `LabDbDockerTests` (`RequiresDocker`): `insert` → `Lab.ReadOnly`; parameter binding keeps `'; drop table` inert; `database = "postgres"` → `Lab.InvalidArgument`; with the `nodes-postgres` proxy disabled through the Toxiproxy API, `db_query database=nodes select 1` still answers (R-OBSERVE).

**Tests and why they fail when reverted:** `HttpRequest_NonLabHost_IsRefused` and `Query_Insert_IsReadOnly` cover the two security properties.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L2&RequiresDocker!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~LabDbDockerTests"   # Docker running
```

**Docs owed:** README section "Reads and probes".

### L11. Nodes adapter; S-L2 green

- **Branch / worktree / commit:** `ml/l11-nodes-adapter` / `meridian-lab-l11` / `feat(lab): mint enrollment tokens and probe enrollment through product types`
- **Depends on:** L09, L10. **Security review:** yes (enrollment tokens, keys, certificates).
- **Rules:**
  - R-CONTRACT: mint through the product's `EnrollmentTokenService` (`IEnrollmentTokenService.CreateTokenAsync(organizationId, createdByUserId, label, validity, ct)` returns `(EnrollmentToken Token, string PlainTextToken)`); post `Dhadgar.Contracts.Nodes.EnrollNodeRequest`.
  - R-ZERO: the plain token goes straight into the vault (`tok-N`); the private key bytes into the vault (`key-N`) and the exported buffer is zeroed.
  - R-USING: `NodesDbContext` per call; `ECDsa`/`RSA` key objects.
  - R-DI: the adapter is a singleton that creates a context per call; never holds one.
  - R-RESULT, R-TIME, R-LOG, R-NULL, R-XUNIT.

**Contract excerpt:** C6 in full; C4 `nodes_token_mint`, `nodes_enroll_probe`, `nodes_cert_time_shift`, `stack_reset_data`; C1 `LabIds`.

**Files**

1. `Nodes/NodesLabAdapter.cs`: real implementation. Construct `EnrollmentTokenService` with the context and whatever else its constructor needs (read it). If enrollment tokens need an organization row or the org must exist, seed `LabIds.OrganizationId` idempotently. `ShiftCertificateTimesAsync` updates `AgentCertificate` rows of the node: adds `shift` (negative) to the chosen fields, including `GraceUntil` (Sprint 2's column is on `main`, D-ML60; a null `GraceUntil` stays null). Production `createContext` uses `UseNpgsql(LabConnectionStrings.OwnerDirect("nodes"))` (the daemon's own writes go straight to Postgres, never through a fault proxy).
1a. `Stack/LabDataReset.cs` and `stack_reset_data` in `Tools/Stack/StackTools.cs`: for each requested lab database, as `OwnerDirect(db)`, list `information_schema.tables` in schema `public` of type `BASE TABLE` except `__EFMigrationsHistory`, then one `TRUNCATE TABLE <list> RESTART IDENTITY CASCADE` with each name quoted by `NpgsqlCommandBuilder.QuoteIdentifier` (or `format('%I')` server side). Re-seeds `LabIds.OrganizationId` for Nodes. Refused while a job runs (`[Lab.JobRunning]`, after L27).
2. `Nodes/EnrollProbe.cs`: build the key exactly as Nodes' `EnrollmentService.EnrollAsync` parses `PublicKey` (read it: algorithm, encoding, PEM or base64 SPKI); fixed lab hardware inventory (`HardwareInventoryRequest` with hostname `meridian-lab-probe`, plausible numbers); `AgentVersion = "meridian-lab"`.
3. `Tools/Nodes/NodesTools.cs`: `nodes_token_mint`, `nodes_enroll_probe` (via `gateway` → `/api/v1/agents/enroll`, via `nodes` → `/agents/enroll`), `nodes_cert_time_shift`.
4. Tests: S-L2 green (no change to the test; only the stubs go). `NodesSeamDockerTests` (`RequiresDocker`, `[Trait("Seam","S-L2")]`): full lab stack, `nodes_token_mint`, `nodes_enroll_probe via=gateway` → 201; a second probe with the same token → 401; `stack_reset_data` then `db_query database=nodes select count(*) from enrollment_tokens` → 0 and the migrations history is intact. `NodesToolsTests`: the mint result contains `token_ref` and no plain token; shift with a positive value refused; `LabDataResetTests` (fake connection: `__EFMigrationsHistory` never in the truncate list; names quoted).

**Tests and why they fail when reverted:** S-L2 fails if the lab and Nodes disagree on token hashing, the request shape or the key encoding.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~NodesSeamDockerTests"   # Docker running
```

**Docs owed:** README section "Nodes adapter" (why the lab mints locally: no user-auth path in Nodes).

### L12. `Dhadgar.Lab.Probe`, `AgentWindowsFootprint`, host reads, `lab_doctor`, red S-L3

- **Branch / worktree / commit:** `ml/l12-probe-reads` / `meridian-lab-l12` / `feat(lab): footprint contract, probe and windows reads`
- **Depends on:** L05. **Security review:** yes (cert stores, registry, Contracts change).
- **Rules:**
  - R-OS: Windows calls behind `[SupportedOSPlatform("windows")]` and `OperatingSystem.IsWindows()`; elsewhere `[Lab.Unavailable]`.
  - R-USING: `X509Store`, each `X509Certificate2` from `store.Certificates`, `RegistryKey`.
  - R-LOG: never export a certificate or private key; never return a registry value (presence and length only).
  - R-PATH: the state file path is built from `%ProgramData%` + `AgentWindowsFootprint` and validated.
  - R-CONTRACT: footprint names only from `AgentWindowsFootprint`; the probe references `Dhadgar.Contracts` and nothing from the lab daemon.
  - R-CPM, R-RESULT, R-NULL, R-XUNIT.

**Contract excerpt:** C5; C4 `certstore_list`, `registry_read`, `state_file_read`, `lab_doctor` (the `member` argument arrives in L32; here it is refused with `[Lab.Unavailable] members arrive in L32`).

**Files**

1. `src/Shared/Dhadgar.Contracts/Agents/AgentWindowsFootprint.cs`: exactly C5.
2. `src/Tools/Dhadgar.Lab.Probe/Dhadgar.Lab.Probe.csproj`: `Microsoft.NET.Sdk`, `net10.0`, `OutputType Exe`, `AssemblyName meridian-lab-probe`, `InvariantGlobalization true`, references `Dhadgar.Contracts` only; add to `Dhadgar.sln` under `Tools`. `LabIsolationTests` already exempts `src/Tools/`.
3. `Probe/Program.cs` (explicit `Main`, System.CommandLine): `footprint --json` prints `ProbeFootprint` JSON to stdout; exit 0, or 2 with a one-line error. Other modes come later: `token-write`, `run-agent` and `agent-log` (L32), `corrupt` and `restore` (L33), `sink` (L36), `trust-add` and `trust-remove` (L41), `msi-install` (L43); unknown mode → exit 2.
4. `src/Shared/Dhadgar.Contracts/Agents/ProbeFootprint.cs` (the probe and the lab both build against it; it crosses a process boundary): `ProbeFootprint(ServiceInfo? Service, RegistryInfo Registry, IReadOnlyList<CertInfo> MachineMy, IReadOnlyList<CertInfo> MachineRoot, IReadOnlyList<CertInfo> AgentCa, StateFileInfo StateFile, bool EventSourceExists)`, with `ServiceInfo(string State, string StartType, string Account, string BinaryPath)`, `RegistryInfo(bool Exists, IReadOnlyList<RegistryValueInfo> Values)`, `RegistryValueInfo(string Name, string Kind, bool Present, int Length)`, `CertInfo(string Thumbprint, string Subject, string Issuer, string Serial, DateTimeOffset NotBefore, DateTimeOffset NotAfter, bool HasPrivateKey)`, `StateFileInfo(bool Exists, string Path, DateTimeOffset? LastWrite)`.
5. `src/Tools/Dhadgar.Lab.Probe/FootprintReader.cs`: fills `ProbeFootprint` from `AgentWindowsFootprint` (service via `sc.exe qc` and `sc.exe query` with `ArgumentList`, or `System.ServiceProcess` if already in CPM; certs metadata only; registry presence and length only).
6. Lab side, `Windows/CertStoreReader.cs`, `Windows/AgentRegistryReader.cs`, `Windows/AgentStateReader.cs` for host reads (as in the old plan: stores `machine_my`, `machine_root`, `agent_ca` with `OpenExistingOnly` and missing → empty, `current_user_my`; registry access denied → `[Lab.Unavailable] needs an elevated read; use a member`; state JSON through `Redactor` plus blanking properties named like `token`, `key`, `secret`).
7. `Tools/Doctor/LabDoctorTool.cs`: OS and build (registry `CurrentBuildNumber`, not the API version), `docker --context <linux> version`, Windows context reachable (if configured), `docker-users` membership, `dotnet --version`, lab ports free or owned by lab containers, dev ports seen in use (informational), token length, `Lab:RepoRoot` exists, Playwright browsers present (after L35).
8. `Tools/Windows/WindowsReadTools.cs`: `certstore_list`, `registry_read`, `state_file_read` (host).
9. Tests: `FootprintReaderTests` (Windows, `SkippableFact`: runs against the host read-only and never reports a private key export), `ProbeCliTests` (unknown mode → 2; `footprint --json` output deserialises as `ProbeFootprint`), `AgentStateReaderTests`, `WindowsToolsOnLinuxTests` (`Skip.If(OperatingSystem.IsWindows())`) → `Lab.Unavailable`.
10. `tests/Dhadgar.Lab.Tests/Seams/AgentFootprintSeamTests.cs` (S-L3, red until L39): reference `Dhadgar.Agent.Windows` (alias `agentwin` if `Program` clashes); assert the agent's service name, CA store name (reflection: `GetField("CaStoreName", BindingFlags.NonPublic | BindingFlags.Static)` on `WindowsCertificateStore`), registry key path and state file name equal `AgentWindowsFootprint`. Red on `main` because the CA store constant does not exist yet.

**Tests and why they fail when reverted:** S-L3 fails while the agent and the lab disagree on any footprint name; `ProbeCliTests` fails if the probe's JSON drifts from the Contracts type.

**Checks**

```powershell
dotnet restore <W>\src\Tools\Dhadgar.Lab.Probe\Dhadgar.Lab.Probe.csproj --force-evaluate
dotnet build <W>\src\Shared\Dhadgar.Contracts\Dhadgar.Contracts.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\src\Tools\Dhadgar.Lab.Probe\Dhadgar.Lab.Probe.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
dotnet test <W>\tests\Dhadgar.Contracts.Tests\Dhadgar.Contracts.Tests.csproj -c Release   # if the project exists
```

**Docs owed:** README section "Probe and Windows reads"; a line in `src/Shared/Dhadgar.Contracts/README.md` (if present) for `AgentWindowsFootprint` and `ProbeFootprint`.

---

## Wave 3: UAT engine (no Sprint 2 dependency)

### L13. Spec model, TOML loader, validator, `uat_list_specs`, `uat_plan`

- **Branch / worktree / commit:** `ml/l13-spec` / `meridian-lab-l13` / `feat(lab): uat spec model, toml loader and plan-only validation`
- **Depends on:** L05. **Security review:** yes (spec input drives the machine).
- **Rules:**
  - R-PATH: `specs_dir` defaults to `<RepoRoot>\docs\guides\uat-specs` (and its `catalog\` subfolder); any other dir must validate under the repo root or its worktrees; spec names are file names or `catalog/<file>` only.
  - R-RESULT: every load and validation problem is a list entry `{file, line, message}`, never an exception.
  - R-NULL, R-XUNIT, R-CONTRACT.

**Contract excerpt:** C7 in full; C10 target id forms; C11 invariant ids; C13 `PoolConstraints` field names.

**Files**

1. `Uat/Spec/UatSpec.cs`: records `UatSpec(int Schema, UatSection Section, IReadOnlyList<UatRow> Rows, string File, string Sha256)`, `UatSection(Id, Title, bool Sequential, IReadOnlyList<string> Requires, ulong? Seed, IReadOnlyList<string> AllowInvariants, IReadOnlyDictionary<string, JsonElement>? RequiresPool)`, `UatRow(Id, Title, Expected, IReadOnlyList<string> DependsOn, string? KnownIssue, bool ReuseMember, IReadOnlyList<string> AllowInvariants, IReadOnlyList<UatFault> Faults, IReadOnlyList<UatStep> Steps, IReadOnlyList<UatClause> Expect)`, `UatFault(string Id, string Tool, JsonElement Args, string Start, string Heal)`, `UatStep(string? Id, string? Tool, JsonElement? Args, IReadOnlyDictionary<string,string> Capture, int? WaitMs, string? ExpectError)`, `UatClause(Id, Text, Source, Op, JsonElement? Value, string? Pointer, string Since, int TimeoutMs, int PollMs, bool Required, IReadOnlyDictionary<string, JsonElement> SourceFields)`.
2. `Uat/Spec/SpecLoader.cs`: `Tomlyn.Toml.ToModel` → `TomlTable` → records; TOML values converted to `JsonElement` for args and values. Line numbers from Tomlyn's syntax tree where available.
3. `Uat/Spec/SpecValidator.cs`: schema must be 1; ids `[a-z0-9-]+` and unique; `depends_on` targets exist and come earlier; every row has at least one step and one required clause; step tools exist, are classified and are not `lab_lease_*`, `uat_*`, `fault_*`, `faults_active`, `job_*`, `hunt_*`, `fuzz_run`, `chaos_run`, `soak_run`, `fleet_run`, `results_*`, `triage_*`, `pool_acquire` or `pool_release`, and no tool whose gate is `OwnLease` (so the later `history_run`, `twin_run`, `mutation_run`, `envelope_run` and `peer_run` are refused without editing this list); no `lease_id` key in any args; `requires` entries in `docker`, `windows`, `windows_containers`, `network`; fault ids unique per row; fault `tool` is one of `fault_toxic_add`, `fault_proxy_disable`, `fault_partition`, `fault_kill`, `fault_pause`, `fault_disk_fill`, `fault_corrupt_file`, `fault_corrupt_registry`, `fault_db`; fault `args` validate against that tool's `InputSchema` (from the registry; `lease_id` excluded) and a `target` value has a C10 id form (`c:`, `p:`, `d:`, `f:`, `m:` or `${member}`); `start`/`heal` reference steps of the row; `since = fault_heal:<id>` names a fault of the row; `allow_invariants` ids are `I1`..`I7`; `known_issue` matches `^KI-[A-Z0-9-]+$` (existence is checked in L25); `requires_pool` keys are C13 `PoolConstraints` names; `${member}` only in sections with `requires_pool`; `expect_error` matches `^(Lab\.[A-Za-z]+|http:\d{3})$`; `source` known; source fields present (C7 table); `op` known and value shape right for it (`approx` two numbers, `any` list); `${...}` references resolvable (built-ins or captured earlier in the row); `${secret:...}` only in `http` bodies; `timeout_ms` ≤ 300000; `since = step:<id>` names a step of the row.
4. `Tools/Uat/UatPlanTools.cs`: `uat_list_specs`, `uat_plan` (no lease; touches nothing).
5. Tests: `SpecLoaderTests` (a full valid spec with a fault, a pool section and `expect_error`), `SpecValidatorTests` (one theory row per rule, each with a tiny invalid TOML string; includes "fault tool used as a step" and "`since = fault_heal:x` with no fault x"), `UatPlanToolTests` (lists `catalog/` specs too).

**Tests and why they fail when reverted:** each validator rule has a case that turns valid if the rule is removed.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
```

**Docs owed:** none beyond code comments (L45 writes the spec guide).

### L14. Clause evaluator: sources, ops, variables, capture

- **Branch / worktree / commit:** `ml/l14-clauses` / `meridian-lab-l14` / `feat(lab): uat clause sources, operators and variable capture`
- **Depends on:** L13, L10, L12. **Security review:** yes.
- **Rules:**
  - R-RESULT: a source failure is a `ClauseState.Unverified` with the error text, never an exception.
  - R-TIME: polling uses `TimeProvider` delays.
  - R-LOG: observed values pass through `Redactor`.
  - R-CONTRACT: `db` uses `LabDb`, `http` uses `LabHttp`, `tool` goes through `LabToolDispatcher` (OPEN tools only), `certstore`/`registry`/`state_file` reuse the L12 readers. No second implementation.
  - R-NULL, R-XUNIT.

**Contract excerpt:** C7 (sources, ops, `since`, variables) and C8 `ClauseState`.

**Files**

1. `Uat/Eval/IClauseSource.cs`: `string Name { get; }`, `Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)`; `ClauseWindow(DateTimeOffset RunStart, DateTimeOffset RowStart, IReadOnlyDictionary<string, DateTimeOffset> StepStarts, IReadOnlyDictionary<string, DateTimeOffset> FaultHeals, IReadOnlyDictionary<string, StepOutcome> Steps)`; `StepOutcome(bool Ok, string? Error, string? ErrorCode, long DurationMs, JsonElement? Result)`. A `since = fault_heal:<id>` whose fault has not healed is `Unverified` with `fault <id> not healed`.
2. Sources: `DbSource` (binds `@since`; takes `database`), `LogSource` (over `LogRing`), `HttpSource`, `ToolSource`, `CertStoreSource`, `RegistrySource`, `StateFileSource`, `StepSource` (reads `window.Steps[step]`; lets a row assert on an expected error or a duration), `HumanSource` (always `Pending`). The `invariant` source is registered by L21; until then the validator accepts it and the evaluator returns `Unverified` with `invariants arrive in L21`.
3. `Uat/Eval/JsonPointer.cs`: RFC 6901 over `JsonElement` (`~0`, `~1`); missing → not found.
4. `Uat/Eval/ClauseOps.cs`: every op of C7; numbers compared as `decimal`; strings ordinal; `matches` with `RegexOptions.CultureInvariant` and a 1 s timeout.
5. `Uat/Eval/UatVars.cs`: `${name}` substitution in strings inside JSON args and clause fields; capture from a step result with a pointer; unknown var → `Result.Failure("[Lab.InvalidArgument] unknown var")`.
6. `Uat/Eval/ClauseEvaluator.cs`: one check or poll until held or `timeout_ms`; returns `(ClauseState, JsonElement? Observed, int Attempts, DateTimeOffset CheckedAt, string? Error)`. `delta` takes the baseline before the row's first step (the runner calls `CaptureBaselineAsync`).
7. Tests: `ClauseOpsTests` (a theory per op, including type mismatches → `NotHeld`, missing pointer → `Unverified` except `absent` → `Held`), `JsonPointerTests`, `UatVarsTests`, `ClauseEvaluatorTests` with fake sources (polling until held, timeout → `NotHeld`, source error → `Unverified`).

**Tests and why they fail when reverted:** `MissingPointer_IsUnverified_NotHeld` protects the "missing evidence never passes" rule; `SinceFaultHeal_NotHealed_IsUnverified` stops a recovery clause from passing on pre-fault evidence.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
```

**Docs owed:** none (L45).

### L15. Runner and grader, `uat_run`

- **Branch / worktree / commit:** `ml/l15-runner` / `meridian-lab-l15` / `feat(lab): uat runner and grader with own-lease runs`
- **Depends on:** L14. **Security review:** yes.
- **Rules:**
  - R-RESULT: a step error makes the row BLOCKED and stops it; the run continues with the next row.
  - R-TIME, R-LOG (step args never logged), R-NULL, R-XUNIT.
  - R-CONTRACT: steps run through `LabToolDispatcher` with `LabCallContext(callId, leaseId, "uat:<runId>")`; the runner injects `lease_id` into every LEASED step.
  - R-SEED: the run seed is `request.seed ?? LabRandom.NewSeed()`; a section's `seed` overrides it for that section; every random choice the runner makes uses `new LabRandom(seed, "runner")`. Implement `Random/LabRandom.cs` exactly as C17 here (SplitMix64, FNV-1a-64 of the stream name) with `LabRandomTests` pinning the first five outputs for seed 42 and stream `runner` (compute them once, write them in the test, so any change to the generator breaks replays visibly).

**Contract excerpt:** C8 in full; C4 `uat_run` (`seed?`, `replay?`); C17 `LabRandom`; OwnLease semantics: with a `lease_id` that matches the current lease the run uses it; without one the run acquires its own (`owner = "uat-run/<runId>"`, `purpose = label or "uat"`, ttl 600, renewed by every step) and releases it at the end; if another holder has it → `Lab.LeaseHeld` before any row runs.

**Files**

1. `Uat/RowGrader.cs`: exactly C8, including `RunOverall` and `RunOutcome.Compute` (until L25 every failure is passed with `IsKnown = false`).
2. `Uat/UatRunner.cs`: `Task<Result<UatRunResult>> RunAsync(UatRunRequest request, IUatRunSink sink, CancellationToken ct)`. `plan_only` → validate and return without a lease. Order: sections as given, rows in file order (`sequential` true) or in order with dependencies respected; `requires` checked (`docker` via `stack_status`/docker probe, `windows` via OS, `windows_containers` via an `ICapabilityProbe` that answers false until L31, `network` true unless `Lab:Offline=true`); a missing capability → SKIPPED with the capability named; `wait_ms` steps; captures; `expect_error` (the step's error code or `http:<status>` equals it → the step counts as ok and its outcome is recorded; success → FAIL with `expected error <x>, got success`; another error → BLOCKED); clause evaluation after the last step. Until L22, a row with `[[row.fault]]` is BLOCKED with reason `faults arrive in L22`; until L30, a section with `requires_pool` has every row SKIPPED with reason `pool unavailable`. `run_id` = 6 hex chars from `RandomNumberGenerator`. `UatRunResult` carries `Seed`.
3. `Uat/IUatRunSink.cs`: `RunStarted`, `RowFinished`, `RunFinished` (in-memory implementation here; L16 adds the bundle writer).
4. `Tools/Uat/UatRunTool.cs`: `uat_run`.
5. Tests: `RowGraderTests` (every row of the C8 table plus precedence combinations and "no required clause → UNVERIFIED"), `RunOutcomeTests` (`Overall_AllPass_IsPass`, `Overall_AllSkipped_IsIncomplete`, `Overall_NoRows_IsIncomplete`, `Overall_NeedsHuman_IsIncomplete`, `Overall_FailAndSkipped_IsFail`, `Overall_OnlyKnownAndSkipped_IsIncomplete`, `Overall_OnlyKnown_IsPassWithKnown`), `UatRunnerTests` with fake tools and sources (dependency failure → SKIPPED, step error → BLOCKED, own lease acquired and released, held lease → refused, lease lost mid-run → remaining rows BLOCKED, `expect_error` matched → PASS path, `expect_error` but success → FAIL, fault row → BLOCKED until L22, same seed → same `${seed}` and same runner choices), `LabRandomTests`.

**Tests and why they fail when reverted:** `Grade_NoRequiredClauses_IsUnverified` and `Grade_PendingHuman_IsNeedsHuman` pin the PASS rule; `Overall_AllSkipped_IsIncomplete` fails if a run where nothing ran reports `pass` (D-ML46); `ExpectError_Success_IsFail` stops an expected-failure row from passing when the product stops failing the way the catalog says.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
```

**Docs owed:** none (L45).

### L16. Evidence bundle, ledger, secret scan, report, attest, `bundle_read`

- **Branch / worktree / commit:** `ml/l16-bundle` / `meridian-lab-l16` / `feat(lab): uat evidence bundle, ledger and attestation`
- **Depends on:** L15. **Security review:** yes (secrets on disk).
- **Rules:**
  - R-PATH: bundle files only under `LabPaths.UatRuns\<run>`; `bundle_read` validates `path` inside that folder.
  - R-LOG / R-ZERO: everything written passes through `Redactor`; `SecretScanner` runs last.
  - R-RESULT, R-USING, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C9 in full; C4 `uat_report`, `uat_attest`, `bundle_read`; C8 attestation rule (human clauses only).

**Files**

1. `Uat/Bundle/BundleWriter.cs` (`IUatRunSink`): writes C9 files (every field, later-packet fields empty or zero: `overall` from `RunOutcome.Compute` with every failure new (`pass`, `incomplete` or `fail`) until L25, and recomputed by `uat_attest`; `violations` 0 until L21, `members` empty until L30); log slices for the run window from `logs/<process>.jsonl`; `repro\repro.toml` with `seed`, `build_id`, `specs` (`file`, `sha256`), `section`, `rows` filter and `label`. `uat_run {replay = <path>}` (wired here) loads it, refuses with `[Lab.InvalidArgument] spec changed since the run` when a sha256 differs, and runs with that seed and filter.
2. `Uat/Bundle/LedgerRenderer.cs`: exactly the C9 `ledger.md` shape; "Saw" lists each clause `id: pointer observed (state)`; tables with spaces around pipes.
3. `Uat/Bundle/SecretScanner.cs`: C9 patterns plus every vault value and the daemon token.
4. `Tools/Uat/UatReportTools.cs`: `uat_report`, `uat_attest` (refuses non-`human` clauses with `[Lab.InvalidArgument]`; re-grades via `RowGrader`; rewrites the row JSON and `ledger.md`; records `{clause_id, verdict, note, at}`), `bundle_read`.
5. Wire `BundleWriter` as the sink for `uat_run`.
6. Tests: `LedgerRendererTests` (golden text), `SecretScannerTests` (planted vault value → file removed, run marked failed, every row BLOCKED), `UatAttestTests` (human clause held → PASS; non-human clause refused), `BundleReadTests` (`..\run.json` of another run refused), `ReproTests` (a run writes `repro.toml`; replay uses its seed; an edited spec is refused).

**Tests and why they fail when reverted:** `SecretScan_PlantedToken_BlocksRun` fails if the scanner is skipped; `Replay_SpecChanged_IsRefused` fails if a replay could silently run different rows.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
```

**Docs owed:** none (L45).

### L17. CLI `status` and `uat`, `lab-selftest.toml`, S-L6

- **Branch / worktree / commit:** `ml/l17-cli-selftest` / `meridian-lab-l17` / `feat(lab): uat cli and lab self-test spec`
- **Depends on:** L16, L11. **Security review:** yes.
- **Rules:**
  - R-HOST: the CLI talks only to `http://127.0.0.1:<port>`; token from `MERIDIAN_LAB_TOKEN`, never an argument.
  - R-RESULT: the exit code is C8's for `overall`: 0 `pass`, 1 `fail`, 2 config or plan error, 4 `incomplete`, 5 `pass_with_known` (3 is the daemon's "already running"). `LabCliExitCodeTests` pins the map.
  - R-PATH, R-USING, R-NULL, R-XUNIT.

**Contract excerpt:** C7 (spec), C8, C9; C16 (same MCP path as Claude).

**Files**

1. `Cli/LabCli.cs`: `meridian-lab status [--json]` (GET `/status`); `meridian-lab uat run --specs <file>... [--section <id>] [--rows a,b] [--plan-only] [--label <text>] [--seed <n>] [--replay <repro.toml>] [--json]` as an MCP client calling `uat_run`; prints the seed, the summary table and the bundle path. `--replay` excludes `--specs`, `--section`, `--rows` and `--seed`.
2. `docs/guides/uat-specs/lab-selftest.toml`, section `lab-selftest`, `requires = ["docker"]`, rows: `stack-up` (`stack_up wipe=true`; expect `stack_status` `/state` eq `up`), `nodes-up` (`svc_start nodes`; `http` GET health eq 200), `gateway-up` (`svc_start gateway`; `http via=gateway` GET `/api/v1/agents/ca-certificate` status 200), `token-mint` (C7 example), `enroll-direct` (`nodes_enroll_probe via=nodes`; expect status 201, `db database=nodes` node row exists for the captured `node_id`), `enroll-gateway` (fresh token, `via=gateway`, 201), `enroll-ingress` (fresh token, `via=agent_ingress`, 201: proves the Toxiproxy path the agents use), `token-single-use` (reuse the `enroll-gateway` token with `expect_error = "http:401"`; `step` clause `/error_code` eq `http:401`), `nodes-log` (`log process=nodes contains="nroll"`, `min_rows` 1 since `run_start`). Use real table and column names from the Nodes EF model (read the migrations snapshot).
3. `tests/Dhadgar.Lab.Tests/Uat/LabSelfTestE2ETests.cs` (S-L6, `RequiresDocker`, `[Trait("Seam","S-L6")]`, 15-minute timeout): daemon in process on port 0 with a temp data root and `RepoRoot` = the test's repo root; `lab_build` the repo; `uat_run lab-selftest.toml`; every row PASS; the bundle's `secret_scan` is `passed`; `ledger.md` exists.
4. `tests/Dhadgar.Lab.Tests/Uat/ShippedSpecsValidateTests.cs`: every `docs/guides/uat-specs/*.toml` passes `uat_plan` (runs in CI).

**Tests and why they fail when reverted:** S-L6 fails if any link from runner to stack breaks; `ShippedSpecsValidateTests` fails on a spec typo.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L6"   # Docker running
```

**Docs owed:** README section "CLI".

### L18. `uat-driver` agent, `lab-uat` skill, S-L5

- **Branch / worktree / commit:** `ml/l18-driver-skill` / `meridian-lab-l18` / `feat(lab): uat-driver agent and lab-uat operating skill`
- **Depends on:** L16 (parallel with L17). **Security review:** yes (tool restriction).
- **Rules:**
  - R-XUNIT, R-NULL.
  - Agent files use the frontmatter format of `.claude/agents/packet-coder.md` (`name`, `description`, `model`, `omitClaudeMd`, `effort`, `maxTurns`, `tools`).
  - Docs: tables with spaces around pipes; every code block names its language.

**Contract excerpt:** C4 (tool names and gates), D-ML18; C8 grades; C9 bundle.

**Files**

1. `.claude/agents/uat-driver.md`: `model: haiku`, `omitClaudeMd: true`, `effort: low`, `maxTurns: 50`, `tools:` exactly `mcp__meridian-lab__` + `lab_status`, `lab_doctor`, `lab_lease_acquire`, `lab_lease_renew`, `lab_lease_release`, `lab_lease_status`, `stack_status`, `stack_up`, `stack_down`, `stack_reset_data`, `lab_build`, `svc_start`, `svc_stop`, `svc_restart`, `svc_config_set`, `logs_tail`, `db_query`, `http_request`, `certstore_list`, `registry_read`, `state_file_read`, `nodes_token_mint`, `nodes_enroll_probe`, `nodes_cert_time_shift`, `uat_list_specs`, `uat_plan`, `uat_run`, `uat_report`, `bundle_read`. Body: runs exactly the steps in the brief; one line per step `<step> <ok|error> <raw result or error code>`; retries a failed step once unchanged; stops on any unexpected state and reports it raw; never diagnoses, never edits, never uses `force` on a lease, never invents rows or args; always releases a lease it acquired; ends with `driver done <run_id|-> <bundle path|->`.
2. `.claude/skills/lab-uat/SKILL.md`: when to use; before taking the lease, ask the user (AskUserQuestion) unless they asked for the run; run `uat_plan` first; nothing in the lab changes the workstation (Windows rows run in pool members from wave 6), so no approval step exists; dispatch `uat-driver` with an exact step list and the lease owner name; afterwards read `uat_report` and the bundle's `ledger.md` and check every claim the driver made against it; human clauses only by `uat_attest` after the user says what they saw; copy `ledger.md` into the campaign `worknotes/` when it is evidence for status; batches of about 5 rows; read `overall`, `seed` and the bundle path from `uat_report`; troubleshooting (`lab_doctor`, lease held by whom, stack degraded). L24 extends the skill with faults and invariants, L26 with results and triage, L38 with the pool, UI and CLI tools.
3. `tests/Dhadgar.Lab.Tests/Agents/DriverToolListTests.cs` (S-L5): parse the frontmatter `tools`; every entry starts `mcp__meridian-lab__`; every tool exists in the registry; none has gate `Machine`; none is in the forbidden set `uat_attest`, `triage_draft_issue`, `triage_mark`, `results_known_issue_set`, `fault_reset`; no non-MCP tool (Read, Edit, Write, Bash, PowerShell, Agent); the body contains the sentence forbidding `force` on a lease. Later packets (L24 for fault and invariant tools, L26 for `results_query` and `triage_show`, L38 for pool, member, UI and CLI tools) add their tools to the frontmatter and this test keeps the forbidden set.

**Tests and why they fail when reverted:** adding a forbidden tool or `Bash` to the driver turns S-L5 red.

**Checks**

```powershell
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
```

**Docs owed:** the agent and skill files are the docs.

### L48. Observation stream, actor and op ids, fingerprint, pairing and fidelity (C18 reserved)

- **Branch / worktree / commit:** `ml/l48-observe` / `meridian-lab-l48` / `feat(lab): ordered observation stream, reproducibility fingerprint and replay fidelity`
- **Depends on:** L16 (parallel with L17 and L18). **Security review:** yes (every observation is evidence on disk).
- **Rules:**
  - R-LOG / R-ZERO: `Args` and `Result` pass through `Redactor` inside `Append`, before the line is written; `SecretScanner` adds `events.jsonl` and `fingerprint.json` to the files it scans.
  - R-TIME: `At` comes from `TimeProvider`; `Seq` comes from the sink's per-run counter under one lock, never from the clock.
  - R-PATH: `events_read` resolves `run_id` through the run index under `LabPaths.UatRuns` and `LabPaths.Jobs` (match on the folder's `run_id` in `run.json`); it never treats `run_id` as a path; `..`, separators or an unknown id → `[Lab.NotFound]`.
  - R-ARGS / R-DOCKER: image digests via `docker --context <ctx> image inspect --format "{{.Id}}" <tag>` and engine versions via `docker --context <ctx> version --format "{{.Server.Version}}"`, both through `IDockerCli`.
  - R-HOST: Toxiproxy's version from `GET http://127.0.0.1:18474/version` only.
  - R-RESULT (a missing fingerprint part becomes `"unknown"` plus a warning in `run.json`, never a failed run), R-USING, R-DI (one singleton sink), R-NULL, R-XUNIT.

**Contract excerpt:** C18 in full; C9 `events.jsonl`, `fingerprint.json`, `run.json` fields `fingerprint_sha256`, `fidelity`, `pair`, `replay`, `events`; C4 `events_read`, `http_request` `outcome`.

**Files** (all under `src/Tools/Dhadgar.Lab/` unless named)

1. `Observe/ActorIds.cs`, `Observe/OpId.cs`, `Observe/LabObservation.cs` (with `ObservationKind`, `OpOutcome`, `ReplayFidelity`, `PairInfo`): C18 exactly. JSON uses `JsonNamingPolicy.SnakeCaseLower` and string enums (`invoke`, `fault_prepare`, `schedule_approximate`).
2. `Observe/LabObservationSink.cs` (`ILabObservationSink`, singleton): per run a `FileStream` (`FileMode.CreateNew`, `FileShare.Read`) and a `StreamWriter`; `Append` takes the run's lock, assigns `Seq = ++counter`, redacts, writes one line and calls `Flush()`; tracks open ops (`Invoke` without `Complete`); `EndRunAsync` appends a `Complete` with `Outcome = Unknown` and `Result = {"reason":"run ended"}` for each, then closes; the 100,001st append writes one `Note` `truncated` and later appends return a failure `[Lab.Truncated]` that callers ignore. `Read(runId, afterSeq, max)` reads the file (open or closed) and returns lines with `Seq > afterSeq`.
3. `Observe/OutcomeClassifier.cs`: `static OpOutcome Classify(HttpResponseMessage? response, Exception? error)` per the C18 outcome table: a response → `Ok` or `Fail` by the op's success statuses (a parameter); `HttpRequestException` with `HttpRequestError.ConnectionError`, `NameResolutionError` or `SecureConnectionError` → `Fail`; any other `HttpRequestException`, `IOException`, `TaskCanceledException` or `OperationCanceledException` after `SendAsync` began → `Unknown`. `http_request` (L10 tool) returns `outcome` from it.
4. `Observe/FingerprintBuilder.cs`: builds `LabFingerprint` for a run from the active build record (`commit`, `dirty`, `build_id`, images; `baseline` and `mutant` stay null until L60 and L54), the SHA-256 over sorted `"<relative path>\n<sha256>\n"` of every `packages.lock.json` in the build's source tree, each lab service adapter's effective environment (read how L09 adapters produce env; vault values replaced by `vault:<name>` before hashing), the run's specs, `DaemonVersion` (assembly informational version), Toxiproxy version, `Seed`, `RuntimeInformation.OSDescription` and the engine versions. `FingerprintHasher.Sha256` serialises with sorted keys, no whitespace, then SHA-256 lowercase hex.
5. `Observe/FidelityRules.cs`: `static ReplayFidelity For(FidelityInput input)` with `public sealed record FidelityInput(bool ConcurrentActors, bool TimingFaults, bool Truncated, bool Attested, bool BuildPresent);` → `EvidenceOnly` if `Truncated`, `Attested` or `!BuildPresent`; else `ScheduleApproximate` if `ConcurrentActors` or `TimingFaults`; else `Exact`.
6. `Observe/ReplayJudge.cs`: compares the stored and current fingerprints field by field (`fingerprint_diff`: one text per differing field, values hashed when they could hold paths) and decides `reproduced` per C18 using grades only (L25 adds primary signatures).
7. `Uat/UatRunner.cs`: `BeginRun` at run start; each step is `NextOp(runId, "runner")`, an `Invoke` (`Op` = tool name, redacted args), the dispatch, then a `Complete` with the outcome (`http_request` and `nodes_enroll_probe`: their `outcome`; any other tool: `Ok` on success, `Fail` on error); `EndRunAsync` at run end.
8. `Uat/Bundle/BundleWriter.cs`: `fingerprint.json` at run start; `run.json` `fingerprint_sha256`, `fidelity` (from `FidelityRules`; plain `uat_run` passes `ConcurrentActors = false`, `TimingFaults = false` until L22), `pair: null`, `replay` (set on replays), `events` (the count); `repro.toml` gains `fidelity` and `fingerprint_sha256`.
9. `Tools/Observe/EventsReadTool.cs`: `events_read` (gate Open), filters `actor` and `kind`, `max` capped at 5000.
10. `Program.cs` / DI: register the sink and builder; `LabDaemonServiceLifetimeTests` stays green.
11. Tests under `tests/Dhadgar.Lab.Tests/Observe/`: `ActorIdsTests` (`Validate_Uppercase_Fails`, `Validate_ClientWithIndex_Ok`), `OpIdTests` (parse round trip; `Parse_MissingSeq_Fails`), `LabObservationSinkTests` (`Sink_ParallelAppends_SeqTotalOrder`: 8 tasks × 1000 appends → `Seq` 1..8000 with no gap or repeat, file lines in `Seq` order; `Sink_VaultValueInArgs_Redacted`; `Sink_OverCap_TruncatedNoteOnce`; `EndRun_OpenOp_ClosedUnknown`; `Append_NoOpenRun_Fails`), `OutcomeClassifierTests` (`ConnectionError_IsFail`, `ResponseEnded_IsUnknown`, `Cancelled_IsUnknown`, `Status401_IsFail`, `Status201_IsOk`), `FingerprintTests` (`SameInputs_SameHash`, `KeyOrder_DoesNotChangeHash`, `SecretValueChange_DoesNotChangeHash`, `LockFileChange_ChangesHash`), `FidelityRulesTests` (each row of the rule), `ReplayJudgeTests` (`Diff_ListsChangedImageDigest`), `EventsReadToolTests` (`RunIdWithDotDot_NotFound`).

**Tests and why they fail when reverted:** `Sink_ParallelAppends_SeqTotalOrder` fails if `Seq` comes from the clock or is assigned outside the lock, which would let L50's checker order a completion before its invocation. `ResponseEnded_IsUnknown` fails if a request that may have committed is recorded as a failure, which would make the checker report false E3 violations. `SecretValueChange_DoesNotChangeHash` fails if a secret enters the fingerprint.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
```

**Docs owed:** none (L45).

---

## Wave 4: faults, invariants and the first catalogs (no Sprint 2 dependency)

Every packet in this wave touches the blast-radius boundary, so every one gets a `security-advisor` pass. The base test filter from here on is `F = "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"`.

### L19. Blast-radius guard, `FaultBook`, Toxiproxy faults; S-L7, BR

- **Branch / worktree / commit:** `ml/l19-fault-core` / `meridian-lab-l19` / `feat(lab): blast-radius guard, write-ahead fault book and toxiproxy faults`
- **Depends on:** L08, L09, L48 (`ILabObservationSink`). **Security review:** yes (blast radius).
- **Rules:**
  - R-BLAST: every mutating tool resolves its target through `FaultTargetGuard`; the three checks are separate injectable classes so each is tested alone.
  - R-DOCKER, R-OBSERVE (S-L7 reads `/readyz` on the direct port, never through a proxy).
  - R-HOST: the Toxiproxy API (`127.0.0.1:18474`) passes only through `LabHosts.IsLabInfraEndpoint`.
  - R-TIME: deadlines and the sweeper run on `TimeProvider`; tests use `FakeTimeProvider`.
  - R-PATH: the journal is `LabPaths.State\faults.json`, written durably: temp file in the same folder, `FileStream.Flush(flushToDisk: true)`, then `File.Move(..., overwrite: true)` (`FaultJournal.WriteAsync`).
  - Write-ahead (D-ML46): the journal entry exists, state `Prepared`, **before** anything changes a target. A crash at any point leaves either nothing changed or a journal entry that recovery heals.
  - R-RESULT, R-LOG (fault args go through `Redactor` before the audit log), R-DI, R-NULL, R-XUNIT.

**Contract excerpt:** C4 rows `fault_list_targets`, `faults_active`, `fault_toxic_add`, `fault_proxy_disable`, `fault_partition`, `fault_heal`, `fault_reset`, and `ActiveFault`; C10 "Toxiproxy proxies", "Target ids", "Blast-radius guard"; D-ML21, D-ML22. Toxiproxy API: `GET /proxies`; `POST /proxies/{name}` body `{"enabled": false|true}`; `POST /proxies/{name}/toxics` body `{name, type, stream, toxicity, attributes}`; `DELETE /proxies/{name}/toxics/{toxic}`; `POST /reset` (enables every proxy and removes every toxic).

**Files**

1. `Faults/ToxiproxyClient.cs` (extends L08's minimal client): `ListProxiesAsync`, `SetEnabledAsync(string proxy, bool enabled)`, `AddToxicAsync(string proxy, ToxicSpec spec)`, `RemoveToxicAsync(string proxy, string toxicName)`, `ResetAsync()`; all `Task<Result...>`; a 404 maps to `[Lab.NotFound]`. Toxic names are `<fault_id>-<stream>`.
2. `Faults/ToxicValidator.cs`: per type, the only attributes allowed and their ranges: `latency` {`latency` 0..60000, `jitter` 0..60000}; `bandwidth` {`rate` 0..1000000}; `timeout` {`timeout` 0..900000}; `reset_peer` {`timeout` 0..900000}; `slow_close` {`delay` 0..60000}; `slicer` {`average_size` 1..65536, `size_variation` 0..65536, `delay` 0..1000000}; `limit_data` {`bytes` 0..1073741824}. `toxicity` 0.0..1.0. Anything else → `[Lab.InvalidArgument]`.
3. `Faults/FaultTargetGuard.cs`:

   ```csharp
   public sealed record ResolvedTarget(string TargetId, string Kind, string Name, string? ContainerId, string? Service);
   public sealed class FaultTargetGuard(ILabTargetRegistry registry, IDockerInspector inspector, ITripwire tripwire, ProxyCatalog proxies)
   {
       // faultKind: "toxic" | "proxy_disable" | "partition" | "kill" | "pause" | "disk_fill" | "corrupt_file" | "corrupt_registry" | "db" | "reset"
       public Task<Result<ResolvedTarget>> ResolveAsync(string targetId, string faultKind, CancellationToken ct);
   }
   ```

   Order: registry (id present, kind allows `faultKind`, stored container id matches), live inspect via `Docker/IDockerInspector.cs` (`InspectAsync(containerId)` → labels, name, mounts, `RestartCount`, `StartedAt`, context; label `meridian-lab=1`, name starts `/meridian-lab-`, context equals the lab context), tripwire `Faults/Tripwire.cs` (`ITripwire.Check(string name, IReadOnlyList<MountInfo> mounts)`: name without the leading `/` equal to `postgres`, `cache` or `messaging`, or starting `dhadgar` in any case, or any mount of `Type == "volume"` → refused). `m:host` refuses every fault kind with `[Lab.BlastRadius] host is read-only` (the pool arrives in L30; the rule lives here). Proxies: in `ProxyCatalog` and listed by Toxiproxy, and `c:toxiproxy` passes the container checks.
4. `Faults/FaultBook.cs` (singleton; implements `IFaultTargets` from L07, `ILeaseEndHook` from L05, `ILabStatusSource` from L02, `IHostedService`):

   ```csharp
   public interface IFaultKind
   {
       string Kind { get; }
       // Computes everything heal needs (names, prior values, backups written to LabPaths.FaultBackups\<faultId>\) without changing the target.
       Task<Result<JsonElement>> PrepareAsync(ResolvedTarget target, JsonElement args, string faultId, CancellationToken ct);
       Task<Result> InjectAsync(ResolvedTarget target, JsonElement args, JsonElement healData, string faultId, CancellationToken ct);
       // Idempotent, and safe when the fault was never injected or only partly injected ("not paused", "already running",
       // "no such toxic", "file absent" are ok).
       Task<Result> HealAsync(FaultJournalEntry entry, CancellationToken ct);
       // Daemon start only, after journalled entries are healed: removes this kind's traces that no journal entry owns
       // (a crash between inject and a lost journal write, or a journal file deleted by hand). Lab targets only, through the guard.
       Task<Result<int>> ReconcileAsync(CancellationToken ct);
   }
   public enum FaultState { Prepared, Active, Healing, HealFailed }
   public sealed record FaultJournalEntry(string FaultId, string Kind, string TargetId, JsonElement Args, JsonElement HealData,
       FaultState State, DateTimeOffset StartedAt, DateTimeOffset Deadline, string? HealError);
   ```

   `BeginAsync(kind, targetId, args, healAfterS, ct)`, in this order: guard; refuse a second active fault of the same kind on the same target (`[Lab.FaultActive] <fault_id>`) and more than 16 active faults; `PrepareAsync`; journal the entry as `Prepared` (durable write); append a C18 `FaultPrepare` observation; `InjectAsync`; on success journal `Active`, append `FaultInject`, return `ActiveFault`; on failure run `HealAsync` on the entry (partial inject), drop it from the journal when the heal succeeds (else keep it `HealFailed`), and return the inject error. `heal_after_s` default 120, 1..900. `HealAsync(faultId, reason)`: journal `Healing`, heal, then drop the entry (or `HealFailed` with `heal_error`), append `FaultHeal`. `HealAllAsync(reason)`. A sweeper on a 1 s `TimeProvider` timer heals overdue faults. `StartAsync` runs `RecoverAsync`: heal every journalled entry in any state (`Prepared` too: prepare may have written backups, and inject may have started), then every registered kind's `ReconcileAsync` (the Toxiproxy kinds: `ToxiproxyClient.ResetAsync()`), and logs the counts. `StopAsync` heals all. A heal error is retried once, then kept in the journal with `heal_error` and reported by `faults_active`. `IsFaulted(targetId)` is true while a fault on it is active or healed less than 10 s ago. History (last 500 entries with `healed_at` and reason) is kept in memory for I1 and I6 (L21). Lease end (`ILeaseEndHook`) heals all.
5. Fault kinds in `Faults/Kinds/`: `ToxicFault` (`toxic`), `ProxyDisableFault` (`proxy_disable`), `PartitionFault` (`partition`: every `ProxyCatalog` entry named `<from>-<to>` or `<to>-<from>`, disabled under one fault id; no such proxy → `[Lab.InvalidArgument] no proxy on that edge`).
6. `Tools/Faults/FaultTools.cs`: the seven tools; mutating ones implement `IFaultTool { JsonElement SampleArgs(string targetId); }`; add them to `ToolPolicy` (gates per C4) and to `StatusDto.ActiveFaults`.
7. Tests:
   - `FaultTargetGuardTests`: with fakes, each check refuses on its own while the other two pass (unregistered id; registered id whose inspect lacks the label; registered and labelled container named `dhadgar-gateway`; a named-volume mount); `m:host` refused.
   - `BlastRadiusTests`: reflect over the tool registry; every Leased tool whose input schema has a `target`, `proxy`, `member`, `database` or `file` property must implement `IFaultTool` (else the test fails naming it); call each with `SampleArgs` for `c:not-registered`, `p:not-a-proxy`, `d:postgres` and `m:host` and assert `Lab.BlastRadius` or `Lab.InvalidArgument`, never success.
   - `FaultBookTests`: deadline heals at `heal_after_s`; lease end heals all; recovery heals a journalled entry and runs every `ReconcileAsync`; second fault same kind and target → `FaultActive`; heal error retried once then reported; `IsFaulted` window; `FaultBook_JournalWrittenBeforeInject` (a fake kind asserts, inside `InjectAsync`, that the journal file already holds the entry as `Prepared`); `FaultBook_CrashAfterInjectBeforeActive_RecoveryHeals` (a fake journal that throws on the `Active` write; a new `FaultBook` on the same state folder heals the target); `FaultBook_PreparedNeverInjected_HealSucceeds`; `FaultBook_InjectFails_PartialHealed`; `FaultJournal_TempThenMove` (no partial `faults.json` after an interrupted write).
   - `ToxicValidatorTests`.
   - `BlastRadiusDockerTests` (`RequiresDocker`): start `meridian-lab-decoy` (labelled, not registered) and `dhadgar-decoy` (unlabelled) from `alpine:3` with `sleep 600`; forge a registry entry `c:decoy` pointing at `dhadgar-decoy`'s id; `ResolveAsync` refuses both; both still running; the test removes them in `finally` by id.
   - `Seams/ToxiproxySeamTests.cs` (S-L7, `RequiresDocker`): stack up, Nodes up; `fault_proxy_disable nodes-postgres` → Nodes `/readyz` on 15040 not 200 within 30 s; `fault_heal` → 200 within 30 s; a 1000 ms latency toxic on `gateway-nodes` downstream → `GET /api/v1/agents/ca-certificate` via the Gateway's direct port takes at least 1000 ms; `fault_reset` leaves Toxiproxy with no toxics.

**Tests and why they fail when reverted:** removing any one guard check turns its `FaultTargetGuardTests` case red; a new mutating tool without `IFaultTool` turns `BlastRadiusTests` red; dropping the lease-end hook turns `LeaseEnd_HealsAllFaults` red; moving the journal write after inject turns `FaultBook_JournalWrittenBeforeInject` red; dropping reconcile turns the crash test red.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L7|FullyQualifiedName~BlastRadiusDockerTests"   # Docker running
```

**Docs owed:** `src/Tools/Dhadgar.Lab/README.md` section "Faults and blast radius" (the three checks, lifetimes, what is refused).

### L20. Container, disk, file and DB faults

- **Branch / worktree / commit:** `ml/l20-more-faults` / `meridian-lab-l20` / `feat(lab): kill, pause, disk, file and database faults`
- **Depends on:** L19. **Security review:** yes (CA key files, database DDL).
- **Rules:**
  - R-BLAST, R-DOCKER, R-ARGS (`docker exec <id> <tool> <args...>` as separate `ArgumentList` items; never `sh -c` with built text).
  - R-PATH: logical files resolve only from the C10 table; backups under `LabPaths.FaultBackups\<fault_id>\`.
  - R-ZERO: the CA key backup buffer is zeroed after restore; the backup file is deleted after a successful heal.
  - R-SEED: `garbage` bytes come from `LabRandom(runSeed, "fault:<fault_id>")` (seed from the call context; `LabRandom.NewSeed()` outside a run).
  - SQL identifiers: database names must match `^meridian_lab_[a-z]+$` and exist in `pg_database`; table names must exist in `information_schema.tables` (schema `public`) for that database; both are then double-quoted. DDL cannot take parameters, so nothing else reaches the SQL text.
  - R-RESULT, R-USING (`NpgsqlConnection`), R-TIME, R-LOG, R-NULL, R-XUNIT.

**Contract excerpt:** C4 `fault_kill`, `fault_pause`, `fault_disk_fill`, `fault_corrupt_file`, `fault_db`; C10 "Declared disk-fill mounts", "Logical files"; L08's `LabConnectionStrings.Admin()`.

**Files**

Every kind implements L19's four members. Prepare records what heal needs and writes any backup; inject changes the target; heal is idempotent and tolerates a never-injected or partly injected target; reconcile (daemon start only) removes unowned traces, each found through `FaultTargetGuard` over the registry's lab targets, never by a name pattern alone:

| Kind | Prepare | Heal tolerates | Reconcile |
| --- | --- | --- | --- |
| kill | record `RestartCount`, `StartedAt` | "already running" | start every registered lab container that is `exited` while the stack is `up` and no journal entry owns it; log each |
| pause | - | "is not paused" | `docker unpause` every registered lab container in state `paused` |
| disk_fill | read free space | absent `/tmp/.lab-fill` | `docker exec <id> rm -f /tmp/.lab-fill` on every running registered Linux service container |
| corrupt_file | copy to `LabPaths.FaultBackups\<fault_id>\` and record SHA-256 | backup equal to the live file | for each backup folder with no journal entry: restore when the live file's hash differs from the recorded one, then delete the folder |
| db | read current `datconnlimit` and `default_transaction_read_only` for the database | "already the default" | on every `meridian_lab_*` database: `ALTER DATABASE ... RESET default_transaction_read_only` and `CONNECTION LIMIT -1` |
| lock_table | - | connection already gone | nothing (the session died with the daemon) |

1. `Faults/Kinds/KillFault.cs`: `docker kill --signal <KILL|TERM>`; heal = `docker start <id>` (immediately at heal, or after `restart_after_s` when given, which also sets the deadline); the watchdog does not relaunch while `IsFaulted`.
2. `Faults/Kinds/PauseFault.cs`: `docker pause` / `docker unpause`.
3. `Faults/Kinds/DiskFillFault.cs`: mount must be `/tmp` for Linux services (C10); read free space with `docker exec <id> df -k --output=avail /tmp`; write `(avail - leave_kib)` KiB with `docker exec <id> fallocate -l <n>K /tmp/.lab-fill`, falling back to `dd if=/dev/zero of=/tmp/.lab-fill bs=1024 count=<n>` when fallocate fails; heal `docker exec <id> rm -f /tmp/.lab-fill`. Members return `[Lab.Unavailable] members arrive in L33`.
4. `Faults/Kinds/CorruptFileFault.cs`: host logical files `nodes-ca-key`, `nodes-ca-cert`; copy to the backup folder; `truncate` (0 bytes), `garbage` (same length, seeded bytes), `delete`; heal writes the backup bytes back, verifies SHA-256 equality, zeroes the buffer, deletes the backup. `member` → `[Lab.Unavailable] members arrive in L33`.
5. `Faults/Kinds/DbFault.cs` (connection `LabConnectionStrings.Admin()` direct on 15432):
   - `terminate_backends`: `select pg_terminate_backend(pid) from pg_stat_activity where datname = @db and pid <> pg_backend_pid()`; one-shot, journalled with `healed_at = started_at`.
   - `connection_limit`: `ALTER DATABASE "<db>" CONNECTION LIMIT <limit>` (`limit` 0..100); heal `CONNECTION LIMIT -1`.
   - `lock_table`: a dedicated `OwnerDirect(service)` connection runs `BEGIN; LOCK TABLE "<table>" IN ACCESS EXCLUSIVE MODE;` and is held by the fault until heal (`ROLLBACK`, dispose). After a daemon crash the connection is gone, so recovery heal is a no-op.
   - `read_only`: `ALTER DATABASE "<db>" SET default_transaction_read_only = on`, then terminate backends so new sessions pick it up; heal `ALTER DATABASE "<db>" RESET default_transaction_read_only`, then terminate backends again (pooled sessions keep the old setting otherwise). Record both terminations in the fault's args.
6. `Tools/Faults/FaultTools.cs`: the five tools, each `IFaultTool` with `SampleArgs`.
7. Tests: `KillFaultTests`, `PauseFaultTests`, `DiskFillFaultTests` (undeclared mount refused; exact `ArgumentList` for both paths), `ReconcileTests` (per kind with fakes: an unowned trace on a lab target is removed; the same trace on a non-lab container is never touched; a `CorruptFile` backup with no entry is restored and deleted; `Heal_NeverInjected_IsOk` for each kind), `CorruptFileFaultTests` (heal restores identical bytes; `garbage` with the same seed gives the same bytes; unknown logical name refused), `DbFaultTests` (`d:postgres`, `meridian_lab_x; drop` and an unknown table all refused before any SQL runs; `ALTER` text for a valid name is exact), `BlastRadiusTests` cases for the five new tools. Docker (`RequiresDocker`, `FaultKindsDockerTests`): kill Nodes then heal → `/readyz` 200 within 60 s; pause and unpause; fill `/tmp` and heal (free space back); `lock_table` on the Nodes nodes table blocks a `db_query` with a 2 s statement timeout, heal releases it; `read_only` makes an enroll probe fail and heal restores it.

**Tests and why they fail when reverted:** `FaultDb_InjectedName_IsRefusedBeforeSql` fails if a name reaches SQL unvalidated; `CorruptFile_Heal_RestoresExactBytes` fails if the backup is skipped; `Reconcile_NonLabContainer_Untouched` fails if reconcile bypasses the guard.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~FaultKindsDockerTests|FullyQualifiedName~BlastRadiusDockerTests"   # Docker running
```

**Docs owed:** README "Faults and blast radius": the fault kinds table.

### L21. Invariant monitors I1-I6, `invariants_check`

- **Branch / worktree / commit:** `ml/l21-invariants` / `meridian-lab-l21` / `feat(lab): always-on invariants i1 to i6`
- **Depends on:** L16, L19 (parallel with L20). **Security review:** yes (raw log scanning sees secrets before redaction).
- **Rules:**
  - R-OBSERVE: invariant SQL uses `LabConnectionStrings.Reader(service)`; RabbitMQ management on `127.0.0.1:16672` and Toxiproxy on 18474 through `LabHosts.IsLabInfraEndpoint`.
  - R-LOG / R-ZERO: I4 sees raw lines through L07's `IRawLineObserver` and stores only the redacted line; it never logs the raw line, never keeps it after the check.
  - R-RESULT: one failing invariant is an `InvariantCheckError`; the others still run (C11).
  - R-TIME, R-DI, R-USING, R-NULL, R-XUNIT.

**Contract excerpt:** C11 in full (types, the I1-I7 table, invariant TOML, the check-error rule); C7 `invariant` source; C9 `invariants.json`.

**Files**

1. `Invariants/InvariantMonitor.cs` per C11; registered invariants run in parallel with a 30 s timeout each.
2. `Invariants/I1UnhandledInvariant.cs`: reads the L07 log store for the window; C11's I1 rule including the fault-excused 502-504, excused only through `FaultImpactMap.Explains(service, path, fault)` over `FaultBook` history.
2a. `Invariants/FaultImpactMap.cs` and `Invariants/GatewayRouteTable.cs` (C10 "Fault impact map"): `IReadOnlySet<string> ImpactSet(string service, string? requestPath)`; `bool Explains(string service, string? requestPath, FaultHistoryEntry fault, DateTimeOffset at)` (target in the set, and active at `at` or healed less than 10 s before). The service dependency cells come from `ProxyCatalog` (one source); the route table is a static array of `(pathPrefix, clusterId)` copied from `src/Dhadgar.Gateway/appsettings.json` `ReverseProxy:Routes`, most specific prefix first.
3. `Invariants/I2OutboxInvariant.cs`: per service a `(database, outboxMessageTable, outboxStateTable, sentColumn)` entry; Nodes now (names from `src/Dhadgar.Nodes` model snapshot: `OutboxMessage`, `OutboxState`; read the snapshot for the exact column names); others are added by L34 and L36.
4. `Invariants/I3DbChecksInvariant.cs`: loads `docs/guides/uat-specs/invariants/*.toml` (C11 format; `schema = 1`; the file `soak.toml` is I7's and is skipped here); each `[[check]]` runs read-only with `max_rows` 50; any row is a violation with the first five rows as evidence.
5. `Invariants/I4SecretLeakInvariant.cs`: subscribes to `IRawLineObserver`; patterns per C11 plus every `SecretVault` value and the daemon token; queues violations with the redacted line.
6. `Invariants/I5OrphanInvariant.cs`: `docker ps -a --filter label=meridian-lab=1` vs `LabTargetRegistry`; RabbitMQ `GET /api/queues` (management user from the vault) for queues ending `_error` or `_skipped` with `messages > 0`; Toxiproxy toxics and disabled proxies with no `FaultBook` entry; `LabPaths.FaultBackups` folders with no active fault. Members are added by L30.
7. `Invariants/I6RestartInvariant.cs`: a baseline snapshot of `RestartCount` and `StartedAt` per registered container plus supervisor `ProcessExit` events; a change is excused only by C11's I6 rule: a `fault_kill` or `fault_pause` whose target is that container, an audited `svc_start`, `svc_restart` or `stack_up` of that service, or a `member_reset` of that member, inside the 30 s margins. Any other fault, including one on the service's own dependency, does not excuse it.
8. `Tools/Invariants/InvariantsCheckTool.cs` (`invariants_check`; `since` default = the last checkpoint); `Uat/Eval/InvariantSource.cs` (clause source `invariant`).
9. `LabServices`: every service starts with `Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Information`.
10. `UatRunner`: checkpoint at run start (window from daemon start) and at run end; `BundleWriter` writes `invariants.json` (violations and errors) and `run.json` `violations`. Per-row windows and grading are L22.
11. Tests: one test class per invariant with fakes; `I4_PlantedVaultValue_ViolationIsRedacted` (the stored detail does not contain the value); `I6_RestartExplainedByKillOnSameTarget_NoViolation`, `I6_UnexplainedRestart_Violation` and `I6_RestartDuringDependencyFault_Violation` (Nodes restarts while `nodes-postgres` is disabled: a finding, not an excuse); `I1_503OnNodes_FaultOnNodesPostgres_Excused`, `I1_503OnNodes_FaultOnDiscordWebhook_Counts`, `I1_503OnGateway_NodesProxyDown_Excused` (route `/api/v1/agents/...` → cluster `nodes`), `I1_503OnGateway_UnroutedPath_FaultOnNodes_Counts`, `I1_503_FaultHealed15sBefore_Counts` and `I1_500DuringFault_Counts`; `GatewayRouteTableTests` (every route in the Gateway's `appsettings.json` is in the table with the same cluster, so a new route fails the test until the table is updated); `Monitor_OneCheckThrowsOrFails_OthersStillRun`. S-L6 now also asserts `violations = 0` and no check errors.

**Tests and why they fail when reverted:** `I4_PlantedVaultValue_ViolationIsRedacted` fails if the raw line is stored; `I1_500DuringFault_Counts` fails if faults excuse every 5xx; `I1_503OnNodes_FaultOnDiscordWebhook_Counts` fails if any fault anywhere excuses a 503 (D-ML46).

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L6"   # Docker running
```

**Docs owed:** README section "Invariants".

### L22. Runner executes faults; invariant grading; `repro.toml` fault timeline

- **Branch / worktree / commit:** `ml/l22-fault-runner` / `meridian-lab-l22` / `feat(lab): fault rows, invariant grading and replayable fault timelines`
- **Depends on:** L20, L21. **Security review:** yes.
- **Rules:**
  - R-CONTRACT: faults are injected by dispatching the C4 fault tool through `LabToolDispatcher` with the run's lease and caller `uat:<runId>`; the runner never calls `FaultBook` directly (one path, one guard).
  - R-SEED: fault args that take randomness get the stream `fault:<section>/<row>/<fault id>`.
  - R-RESULT, R-TIME, R-LOG, R-NULL, R-XUNIT.

**Contract excerpt:** C7 `[[row.fault]]`, `allow_invariants`, `since = "fault_heal:<id>"`; C8 invariants paragraph; C11 check-error rule; C9 row JSON `faults`, `violations`, `repro.toml`.

**Files**

1. `Uat/RowExecutor.cs` (extracted from `UatRunner`): per row: open window → inject `before_steps` faults → steps, injecting `after_step:<id>` faults and healing `after_step:<id>` faults after that step → heal `after_steps` faults → heal anything left (row end) → settle `Lab:Uat:SettleMs` (default 5000) → clauses (the `fault_heal:<id>` anchor is the heal time) → invariant checkpoint for `[row_start, now]` → grade. An inject error → BLOCKED `fault <id>: <error>`, steps skipped, any injected faults still healed. A heal error → BLOCKED `heal <id>: <error>`, then `fault_reset`; if `faults_active` is not empty afterwards, every remaining row is BLOCKED `lab not clean`.
2. `Uat/RowGrader.cs`: apply C8's invariant rule with the union of section and row `allow_invariants`; allowed violations stay in the row JSON with `allowed: true`.
3. `UatRunner`: delete the "faults arrive in L22" branch; synthetic rows `invariants/<id>` (FAIL for violations, UNVERIFIED for check errors) for anything outside row windows.
4. `Uat/Bundle/BundleWriter.cs` and `ReproWriter`: row `faults` with `fault_id`, `injected_at`, `healed_at`, `heal_error`; `repro.toml` gains `[[fault]]` entries `{row, id, tool, args (redacted), injected_ms, healed_ms}` relative to row start. Replay re-runs the rows; the timeline is evidence, not a schedule (the spec drives injection). A run with any `fault_toxic_add` of `latency`, `bandwidth`, `slow_close` or `timeout`, or any `fault_pause`, passes `TimingFaults = true` to L48's `FidelityRules`, so its fidelity is `schedule_approximate`.
5. `uat_report`: `violations` and per-row `violations`.
6. Tests (`RowExecutorTests` with fake tools): `FaultRow_StepBlocks_FaultStillHealed`; `FaultRow_InjectFails_IsBlockedAndOthersHealed`; `HealFails_LabNotClean_RemainingRowsBlocked`; `Violation_NotAllowed_FailsPassRow`; `Violation_Allowed_RecordedNotFailed`; `CheckError_InRow_IsUnverified`; `Violation_OutsideRows_SyntheticRow`; `SinceFaultHeal_UsesHealTime`. Docker: `tests/Dhadgar.Lab.Tests/Uat/Specs/fault-smoke.toml` (one row: disable `nodes-postgres`; Nodes `/readyz` not 200; recovery clause `since = "fault_heal:pg"`, `timeout_ms = 30000`, status 200) passes in `FaultSmokeE2ETests` (`RequiresDocker`).

**Tests and why they fail when reverted:** `FaultRow_StepBlocks_FaultStillHealed` fails if a blocked row leaks its fault; `Violation_NotAllowed_FailsPassRow` fails if invariants stop grading.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L6|FullyQualifiedName~FaultSmokeE2ETests"   # Docker running
```

**Docs owed:** none (L45).

### L23. Catalogs `gateway.toml` and `nodes.toml`; Nodes invariant checks

- **Branch / worktree / commit:** `ml/l23-catalogs-core` / `meridian-lab-l23` / `test(lab): expected-failure catalogs for gateway and nodes`
- **Depends on:** L22. **Security review:** yes (specs corrupt the lab Nodes CA key).
- **Rules:**
  - C7 format; catalog rule (C7): each row states the designed failure. Take the design from the code and its docs (Gateway YARP config and `appsettings.json`, `src/Dhadgar.Nodes/Endpoints/AgentEndpoints.cs`, `ErrorCodes.cs`, the service READMEs). Where the design says nothing, assert the floor every row shares: no 500 (I1 stays unallowed), no partial write (I3), recovery after heal; list each such row under `ExpectationAssumed` in the report.
  - Real table and column names from the Nodes model snapshot; SQL params bound; each row has at least one required non-human clause and a recovery clause with `since = "fault_heal:<id>"`.
  - A row that fails on the packet's local run stays as written: record grade and reason in the report as a finding (the orchestrator triages; L26 seeds known issues). Never edit an expectation to match.

**Files**

1. `docs/guides/uat-specs/catalog/gateway.toml`, section `catalog-gateway`, `requires = ["docker"]`, rows (each mints what it needs first and sets `allow_invariants = []`):
   - `gw-nodes-down`: disable `gateway-nodes`; `GET /api/v1/agents/ca-certificate` via `gateway` → status `any [502, 503]`; Gateway `/readyz` not 200 (`Readyz:RequiredClusters:0 = nodes`); after heal 200 within 30 s.
   - `gw-nodes-latency`: 3000 ms latency toxic on `gateway-nodes` downstream → 200 and the step's `/duration_ms` `gte 3000`.
   - `gw-reset-peer`: `reset_peer` toxic on `gateway-nodes` → `any [502, 503]`, then 200 after heal.
   - `gw-redis-down`: disable `gateway-redis` → routed requests still 200 if the Gateway treats Redis as optional (F28), else the documented status; I1 clean; recovery.
2. `docs/guides/uat-specs/catalog/nodes.toml`, section `catalog-nodes`, `requires = ["docker"]`, rows:
   - `nodes-pg-down-enroll`: mint, then disable `nodes-postgres` (`start = "after_step:mint"`); enroll `via=gateway` with `expect_error` per the designed status (503 from the DB health path; the coder confirms); the token row is still unused after heal; a fresh enroll with the same token after heal → 201 within 60 s.
   - `nodes-pg-latency`: 2000 ms latency on `nodes-postgres`; enroll 201; `/duration_ms` `gte 2000`.
   - `nodes-rabbit-down-outbox`: disable `nodes-rabbitmq`; enroll 201 (the outbox decouples it); after heal the outbox rows for the enrollment are delivered within 60 s (db clause on `OutboxMessage`); I2 clean at row end.
   - `nodes-killed-enroll`: mint; `fault_kill c:nodes`; enroll via gateway → `any [502, 503]`; heal starts Nodes; the same token enrolls (201) within 90 s: a failed attempt never burns a token.
   - `nodes-pg-terminate`: `fault_db terminate_backends d:nodes`; the next enroll → 201 (pool recovery) or one documented retryable status; no 500.
   - `nodes-pg-readonly`: `fault_db read_only d:nodes`; enroll fails with the designed status; token unused; after heal enroll 201.
   - `nodes-ca-key-corrupt`: `fault_corrupt_file nodes-ca-key garbage`, then `svc_restart nodes`; Nodes either refuses to become ready or enroll fails with a designed status, never a 500 and never a node row without a certificate; heal, `svc_restart nodes`, enroll 201.
   - `nodes-token-expired`: mint with `validity_minutes = 1`; two `wait_ms = 60000`/`5000` steps; enroll `expect_error = "http:401"`.
   - `nodes-token-reuse-after-failure`: enroll fails under `nodes-postgres` disabled, heal, enroll 201, enroll again with the same token `expect_error = "http:401"`.
   - `nodes-enroll-malformed`: `http_request via=gateway POST /api/v1/agents/enroll` with an empty body, a wrong-typed field and a 1 MiB string field (three steps, each `expect_error = "http:400"`); `step` clauses show `application/problem+json`.
3. `docs/guides/uat-specs/invariants/nodes.toml` (C11): `node-without-active-cert`, `used-token-without-node`, `cert-without-node`, `duplicate-active-thumbprint`.
4. `ShippedSpecsValidateTests` already covers `catalog/`; extend it to `invariants/*.toml` parsing.

**Tests and why they fail when reverted:** `ShippedSpecsValidateTests` fails on any spec or invariant file error. Local evidence: one `uat_run` of each catalog, bundle paths and grades in the report.

**Checks**

```powershell
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~ShippedSpecsValidateTests"
```

Then, with Docker running, the daemon from this worktree, and the lease: `meridian-lab uat run --specs <W>\docs\guides\uat-specs\catalog\gateway.toml <W>\docs\guides\uat-specs\catalog\nodes.toml --label l23`.

**Docs owed:** none (L45 writes the catalog guide).

### L24. `lab-coverage.json`, `LabCoverageTests`; driver and skill for faults

- **Branch / worktree / commit:** `ml/l24-coverage` / `meridian-lab-l24` / `feat(lab): coverage manifest and fault-aware driver`
- **Depends on:** L23, L18. **Security review:** yes (driver tool list).
- **Rules:** R-XUNIT, R-NULL, R-PATH (the test reads files only under the repo root); C15 rules exactly; D-ML18 forbidden set.

**Contract excerpt:** C15 in full; D-ML33; D-ML18.

**Files**

1. `src/Tools/Dhadgar.Lab/lab-coverage.json` (copied to output): `real`: Gateway (`gateway`, `catalog/gateway.toml`), Nodes (`nodes`, `catalog/nodes.toml`). `real-pending` until L38: Identity, BetterAuth, Panel, Notifications, Discord, CLI, SharedAuth (`src/Dhadgar.SharedAuth`, exercised through the Panel; adapter `panel`). `real-pending` until L43: `src/Agents/Dhadgar.Agent.Windows`, `src/Agents/Dhadgar.Agent.GameServerWrapper`. `stub` with note "DV-13: stub without auth; promote by shipping adapter and catalog rows": Servers, Tasks, Files, Console, Mods, Billing. `stub` with note "out of scope until promoted (owner, 2026-10-27)": Agent.Linux, Secrets, Scope, ShoppingCart. Any other matching project the test finds gets `stub` with the out-of-scope note.
2. `tests/Dhadgar.Lab.Tests/Coverage/LabCoverageTests.cs`: C15 rules; `real-pending` `until` must name a packet in `docs/analysis/meridian-lab/README.md`'s packet table; `ManifestIsComplete`, `RealEntriesHaveAdapterAndFaultRow`, `PendingEntriesNameAPacket`, `StubEntriesHaveNote`.
3. `.claude/agents/uat-driver.md`: add `fault_list_targets`, `faults_active`, `invariants_check`, `fault_toxic_add`, `fault_proxy_disable`, `fault_partition`, `fault_kill`, `fault_pause`, `fault_disk_fill`, `fault_corrupt_file`, `fault_db`, `fault_heal`. Never `fault_reset`.
4. `.claude/skills/lab-uat/SKILL.md`: section "Faults": faults belong in specs; ad-hoc faults only when the brief lists them; every ad-hoc fault is followed by `fault_heal` and `faults_active` must be empty before the driver stops; the orchestrating session (not the driver) may call `fault_reset`; reading `invariants.json`; the catalog rule.
5. `DriverToolListTests` keeps the forbidden set; add `Driver_HasNoFaultReset`.

**Tests and why they fail when reverted:** adding a web project without a manifest entry turns `ManifestIsComplete` red; flipping Identity to `real` without a catalog fault row turns `RealEntriesHaveAdapterAndFaultRow` red.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

**Docs owed:** none (L45; owner action O1 adds the promotion line to CLAUDE.md).

---

## Wave 4H: history checking (no Sprint 2 dependency)

Jepsen-style histories over enrollment (D-ML47, D-ML48). L49 and L50 run in parallel; L51 needs both and turns S-L11 green. Every packet gets a `security-advisor` pass (the barrier proxy is a listener; history clients carry enrollment tokens). Base filter `F` as in wave 4.

### L49. Barrier proxy; red S-L11

- **Branch / worktree / commit:** `ml/l49-barrier` / `meridian-lab-l49` / `feat(lab): lab-only barrier proxy and red enrollment history seam`
- **Depends on:** L48, L09, L10. **Security review:** yes (new listener).
- **Rules:**
  - R-HOST: the listener binds `IPAddress.Loopback` only, on `LabPorts.Barrier` (or port 0 when a test passes 0); `StartAsync` refuses any other port and any upstream that fails `LabHosts.IsLabEndpoint` (`[Lab.BlastRadius]`).
  - Forwarding: `HttpClient` over `SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false }`, one per proxy instance, disposed with it (R-USING). Hop-by-hop headers (`Connection`, `Keep-Alive`, `Transfer-Encoding`, `Upgrade`, `Proxy-*`, `TE`, `Trailer`) and `X-Lab-Op-Id` are never forwarded.
  - R-LOG / R-ZERO: bodies are never logged; observations carry the status and the byte counts only.
  - R-TIME: holds and the 30 s cap run on `TimeProvider`; tests use `FakeTimeProvider` where no socket timing is involved.
  - R-RESULT, R-DI (the proxy is created per `history_run`, never registered as a singleton listener), R-NULL, R-XUNIT.

**Contract excerpt:** C19 "The barrier proxy" in full; C10 "Lab-only HTTP barrier proxy"; C1 `LabPorts.Barrier = 25080`; C18 `ILabObservationSink`, `ObservationKind.Observe`.

**Files** (under `src/Tools/Dhadgar.Lab/` unless named)

1. `LabPorts.cs`: confirm `Barrier = 25080` is in `ServicePorts` (C1; L01 ships it) and add it if missing, so `LabHosts.IsLabEndpoint` accepts the barrier; nothing else changes.
2. `Barrier/BarrierTypes.cs`: `BarrierAction`, `BarrierRule` per C19.
3. `Barrier/BarrierProxy.cs`: `WebApplication.CreateSlimBuilder()`, `WebHost.ConfigureKestrel(k => { k.Listen(IPAddress.Loopback, port); k.Limits.MaxRequestBodySize = 1 << 20; })`, one catch-all `app.Map("/{**path}", HandleAsync)`. `StartAsync` returns the started proxy and its bound port (`BoundPort`, read from `IServerAddressesFeature`). `Arm(runId, rules)` replaces the rule set; `WaitArrivedAsync(barrier, count, timeout, ct)` completes when `count` held requests of that barrier have arrived; `Release(barrier)` lets them go. `HandleAsync`:
   - read `X-Lab-Op-Id`; missing or not an armed `OpId` → 400 problem+json `type = "lab.barrier.no_op"`, nothing forwarded;
   - write `Observe proxy_request_arrived {method, path, bytes}` (`CausedBy` = the op);
   - `HoldRequest`: wait for the barrier, at most 30 s, then 504 `lab.barrier.timeout`;
   - forward method, path, query, headers (minus the stripped set) and body to `upstream`; read the whole response (capped 1 MiB; over the cap → 502 `lab.barrier.too_large`);
   - write `Observe proxy_upstream_completed {status, bytes}`;
   - `HoldResponse`: wait `HoldMs` or the barrier, then copy status, headers and body back;
   - `DropResponse`: write `Observe proxy_response_dropped`, then `HttpContext.Abort()`;
   - `Pass`: copy back.
4. `tests/Dhadgar.Lab.Tests/Barrier/BarrierProxyTests.cs` (a test upstream on port 0 built with `CreateSlimBuilder`; the proxy on port 0; real sockets):
   - `HoldRequest_ReleasedTogether`: three requests on barrier `start`; `WaitArrivedAsync(start, 3)` completes; the upstream saw none; after `Release` it saw three.
   - `DropResponse_UpstreamCompletedClientUnknown`: the upstream counts one call; the client's `SendAsync` throws `HttpRequestException` with `HttpRequestError.ResponseEnded` (or `IOException`), so L48's `OutcomeClassifier` returns `Unknown`; the sink holds `proxy_upstream_completed` with the upstream status.
   - `NoOpHeader_Refused400`, `UnknownOpId_Refused400` (the upstream saw nothing).
   - `OpHeader_StrippedBeforeUpstream`, `HopByHopHeaders_Stripped`.
   - `NonLabUpstream_Refused` (`http://example.com`), `Port_NotBarrier_Refused` (port 25081).
   - `HoldRequest_Timeout_504` (hold 30 s on a real clock is too slow: make the cap an internal constructor parameter set to 500 ms in this test only).
   - `Body_Over1MiB_413`.
5. `tests/Dhadgar.Lab.Tests/Seams/EnrollHistorySeamTests.cs` (S-L11, `[Trait("Seam","S-L11")]`, `RequiresDocker`): daemon in process with the stack up (S-L6's fixture); `history_run {scenario = "enroll-race", clients = 4, seed = 11}` through `LabToolDispatcher`; asserts success, `verdict = "linearizable"`, no required violation, and that the run's `events.jsonl` holds 4 `Invoke` and 4 `Complete` lines for actors `client:1`..`client:4`; then `history_run {scenario = "enroll-lost-response", seed = 12}` asserts `verdict` is not `unknown` and E1-E4 and E6 hold. Red now: `history_run` is not registered (`[Lab.UnknownTool]`).

**Tests and why they fail when reverted:** `DropResponse_UpstreamCompletedClientUnknown` fails if the proxy drops before forwarding (the history would then wrongly know the op never ran). `OpHeader_StrippedBeforeUpstream` fails if lab metadata reaches the product. `NonLabUpstream_Refused` fails if the proxy can be pointed outside the lab.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&Seam!=S-L11&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L11"   # Docker running; expected red, record the failure line in the report
```

**Docs owed:** none (L45).

### L50. Linearizability checker, enrollment model and rules, minimiser

- **Branch / worktree / commit:** `ml/l50-history-checker` / `meridian-lab-l50` / `feat(lab): linearizability checker and enrollment history model`
- **Depends on:** L48 (parallel with L49). **Security review:** yes (the verdicts gate mutants and findings).
- **Rules:**
  - Pure code: no Docker, no network, no clock (the budget is a `Stopwatch` deadline passed in, checked every 1024 search steps).
  - Deterministic: the same history gives the same verdict, the same violations in the same order and the same minimal history. Iterate ops in `InvokeSeq` order; no `HashSet` iteration order in outputs.
  - Never a false pass: a budget overrun, more than 12 ops on one key or a history the recorder could not build → `unknown` with a reason. `unknown` is never `linearizable`.
  - R-RESULT (malformed observations → a failed `Result`, not an exception), R-NULL, R-XUNIT (CsCheck is already pinned for L27; add the `PackageReference` without a version if `Dhadgar.Lab.Tests` lacks it, R-CPM).

**Contract excerpt:** C19 history types, `IHistoryModel<TState>`, `LinearizabilityChecker`, `EnrollmentHistoryModel`, rules E1-E6; C18 outcome rules (an `Unknown` op may take effect at any point after its invoke, or never); C12 signature `hist:<model>|<rule id>|<scenario>`.

**Files** (under `src/Tools/Dhadgar.Lab/History/`)

1. `HistoryTypes.cs`: C19's records.
2. `HistoryRecorder.cs`: `static Result<History> FromObservations(string runId, string scenario, IReadOnlyList<LabObservation> obs, JsonElement finalState, Func<LabObservation, (string Key, string F, JsonElement Input)?> select)`: pairs `Invoke` and `Complete` by `OpId`; an `Invoke` with no `Complete` → `Outcome = Unknown`, `CompleteSeq = null`; a `Complete` with no `Invoke`, or two `Complete`s for one op → failure `[Lab.HistoryMalformed]`.
3. `LinearizabilityChecker.cs`: Wing-Gong-Lowe per key. An op's interval is `[InvokeSeq, CompleteSeq]`; an `Unknown` op's return is +∞, and it may also be left unlinearized at the end (it never took effect). Memoise visited `(bitset of linearized ops, state)` pairs. At the end, the final state must satisfy `FinalMatches` for that key; this is what makes an `Unknown` op's effect decidable. Violation output: the smallest set of op ids the search could not order, as `HistoryViolation("linearizability", ...)`.
4. `Minimiser.cs` (`LinearizabilityChecker.Minimise` calls it): ddmin over the `Ok`/`Fail` ops (Unknown ops kept), re-checking each candidate; stops at the budget; returns the smallest failing history found.
5. `Models/EnrollmentHistoryModel.cs`: state `abstract record EnrollState` with `Unused`, `Used(string NodeName, string PublicKey)`, `Revoked`; `Initial` reads the token's row (`revoked` → `Revoked`, else `Unused`; the run starts from a fresh mint); `TryStep` per C19 with constructor flag `idempotentRetry` (false = today's spec; true = D-ML49 "yes"); `FinalMatches`: the token row's `used_at`/`used_by_node_id` and the set of node names created in the run agree with the state. Name `"enrollment"`.
6. `Rules/EnrollmentRules.cs`: `static IReadOnlyList<HistoryViolation> Evaluate(History h)` for E1-E6 over the ops and `FinalState` (`{token:{id, used_at, used_by_node_id, revoked}, nodes:[{id, name, active_certs}]}`, built by L51). An op's node is the one whose `name` equals the op's sanitised hostname (lowercase; Nodes keeps `[a-z0-9-]`, `EnrollmentService.SanitizeNodeName`). `Required` per the C19 table, with E5 required only when the model's `idempotentRetry` is true.
7. Tests (`tests/Dhadgar.Lab.Tests/History/`, histories written by hand as observation lists):
   - `Check_TwoOkOneToken_Violation`, `Check_OkAndConcurrent401_Linearizable`, `Check_401BeforeAnyEffect_Violation` (E4: the 401's `Complete` precedes the winner's `Invoke`).
   - `Check_UnknownTookEffect_RetryGets401_Linearizable_E5Orphan` (today's model: linearizable, and E5 reports an orphan, not required).
   - `Check_UnknownTookEffect_IdempotentModel_RetryOk_Linearizable`, `Check_UnknownNoEffect_Linearizable`.
   - `Check_13OpsOneKey_Unknown`, `Check_BudgetExceeded_Unknown` (budget `TimeSpan.Zero`).
   - `Minimise_SixOps_ReducesToTwo`, `Recorder_InvokeWithoutComplete_Unknown`, `Recorder_DoubleComplete_Malformed`.
   - `Rules_E3_FailedOpMadeNode_Violation`, `Rules_E6_UsedByMismatch_Violation`, `Rules_E2_TwoActiveCerts_Violation`.
   - `SequentialHistories_AlwaysLinearizable` (CsCheck: generate 1..10 ops, apply the model sequentially, build non-overlapping intervals; the checker must say `linearizable`).

**Tests and why they fail when reverted:** `Check_TwoOkOneToken_Violation` fails if the checker accepts two winners (M1 would survive). `Check_13OpsOneKey_Unknown` and `Check_BudgetExceeded_Unknown` fail if an unfinished search is reported as a pass. `Check_UnknownNoEffect_Linearizable` fails if an `Unknown` op is forced to take effect, which would report false E1 violations on every lost response.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~Dhadgar.Lab.Tests.History"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&Seam!=S-L11&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

**Docs owed:** none (L45).

### L51. `history_run`, enrollment scenarios, Toxiproxy timeout check; S-L11 green

- **Branch / worktree / commit:** `ml/l51-history-run` / `meridian-lab-l51` / `feat(lab): enrollment history runs through the barrier proxy`
- **Depends on:** L49, L50, L22, L11. **Security review:** yes (clients send real enrollment tokens).
- **Rules:**
  - R-CONTRACT: history clients send `Dhadgar.Contracts.Nodes.EnrollNodeRequest` and read `EnrollNodeResponse`; never a local copy.
  - R-ZERO / R-USING: each client key is an `ECDsa.Create(ECCurve.NamedCurves.nistP256)` in a `using`; only `ExportSubjectPublicKeyInfo()` leaves it; the token plaintext is read from the vault into a `char[]`/`byte[]` that is zeroed after the body is serialised (record the unavoidable managed-string copy as a residual, as Sprint 2 did).
  - R-LOG: request bodies go into `Invoke` args through `Redactor` (the token becomes `tok-N`).
  - R-OBSERVE: the final state comes from `db_query` (direct 15432), never through `nodes-postgres`.
  - R-SEED: client order, keys' labels and hostnames come from `LabRandom(seed, "history:<scenario>")`.
  - R-BLAST: faults in a scenario go through `LabToolDispatcher` like L22 (one path, one guard).
  - R-RESULT, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C4 `history_run`, `nodes_enroll_probe` (`hostname?`, `key_ref?`), `nodes_token_mint`; C19 scenarios, rules and barrier; C18 outcome rules and fidelity; C12 `hist` signatures and `runs.kind = history`; C1 `LabPorts.Barrier`, `LabPorts.AgentIngress`.

**Files** (under `src/Tools/Dhadgar.Lab/` unless named)

1. `docs/guides/uat-specs/catalog/enroll-history.toml`: `schema = 1`, one `[[scenario]]` per C19 row with `id`, `clients` (default; `history_run.clients` overrides for the race scenarios), `retry` (`none` | `same_key` | `new_key`), `first_op_action` (`hold_request` | `drop_response`), `faults` (C7 `[[row.fault]]` shape, `start = "before_steps"`).
2. `History/HistoryScenario.cs` and its loader (Tomlyn, as L13; unknown keys refused).
3. `History/HistoryClient.cs`: one actor `client:<n>`; hostname `lab-<run6>-c<n>-<seq>` (`run6` = the first six hex chars of the run id); `POST http://127.0.0.1:25080/api/v1/agents/enroll` with `X-Lab-Op-Id`; `Invoke` before `SendAsync` with `{hostname, key_label}` (never the key), `Complete` with `OutcomeClassifier` (success status 201) and `{status, node_id?, error_code?}`. Retry: a new `OpId` with `CausedBy` = the first op, same or new key per `retry`.
4. `History/HistoryRunJob.cs`: `stack_reset_data {databases = ["nodes"]}` → `nodes_token_mint` → start `BarrierProxy` (upstream `http://127.0.0.1:{LabPorts.AgentIngress}`) → arm rules → inject faults → start clients → `WaitArrivedAsync` then `Release` → await clients (60 s cap; stragglers closed `Unknown` by `EndRunAsync`) → heal faults → final state by `db_query` (the token row; nodes whose name starts `lab-<run6>-` with their active certificate count; table and column names from the Nodes model snapshot) → `HistoryRecorder` → checker (budget 10 s) → `EnrollmentRules` → when `minimise` and the verdict is `violation`: `Minimise`, then reruns with `clients` halved down to 2 while the violation recurs, at most 20 reruns and inside the 300 s cap → bundle. Fidelity `schedule_approximate` (concurrent actors), via L48.
5. `Uat/Bundle/BundleWriter.cs`: the history bundle is a normal run folder (`run.json` `kind = "history"`) with `history.json` (the `History`), `check.json` (`CheckResult`), `minimal.json` when minimised, and one synthetic row `history/<scenario>` whose JSON gains `history:{verdict, violations:[{rule, ops, required}]}`; the row grade is PASS for `linearizable` with no required violation, FAIL for a required violation, UNVERIFIED for `unknown`. L25 reads `history.violations` to emit `hist:` signatures.
6. `Tools/History/HistoryRunTool.cs` (`history_run`, OwnLease; `replay` re-runs with the stored seed and compares through L48's `ReplayJudge`); register in `ToolPolicy`.
7. `Nodes/NodesAdapter.cs` (L11): `nodes_enroll_probe` gains `hostname?` (`^[a-z0-9-]{1,48}$`, default `meridian-lab-probe`) and `key_ref?` (reuse a key the vault holds; new keys are stored as `key-N` and returned as `key_ref`).
8. `docs/guides/uat-specs/known-issues.toml`: seed `KI-ENROLL-RETRY` (`signatures = ["hist:enrollment|E5|enroll-lost-response", "hist:enrollment|E5|enroll-lost-response-new-key"]`, `[issue.scope] sections = ["history"]`, title "A lost enroll response plus a retry leaves an orphan node and a 401 (D-ML49)"). It matches nothing while E5 is non-required (no signature is emitted); it exists so that the day D-ML49 makes E5 required, the failure is linked rather than new until the product changes.
9. `tests/Dhadgar.Lab.Tests/Faults/ToxiproxyTimeoutToxicDockerTests.cs` (`RequiresDocker`): open a keep-alive connection through `agent-ingress`, send one request (200), add a `timeout` toxic with `timeout = 0`, send a second request on the same connection, assert it does not complete in 3 s, remove the toxic. If the assumption is false (the second request completes), delete the test, record "Toxiproxy does not apply new toxics to open connections" in the report, and leave the C19 fallback marked unverified; nothing else depends on it.
10. Tests: `HistoryScenarioLoaderTests` (unknown key refused; every shipped scenario loads), `HistoryClientTests` (fake handler: body is `EnrollNodeRequest`; the token never appears in the `Invoke` args; `same_key` retry sends the same SPKI, `new_key` a different one), `HistoryRunJobTests` (fakes: a straggler is `Unknown`; a required violation grades the row FAIL; `unknown` grades UNVERIFIED; minimisation reruns stop at 20), `NodesEnrollProbeTests` (`Hostname_Invalid_Refused`).

**Tests and why they fail when reverted:** S-L11 (`EnrollHistorySeamTests`) goes green here and fails if the history client and Nodes disagree on the route, the body or the status codes, or if the checker's model disagrees with Nodes' enrollment behaviour. `HistoryClientTests` fails if the token reaches the evidence. `HistoryRunJobTests` fails if `unknown` is graded PASS.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L11|FullyQualifiedName~ToxiproxyTimeoutToxicDockerTests"   # Docker running
```

Then, with the daemon from this worktree and the lease, one `history_run` of each scenario; record verdicts, violations and bundle paths in the report. A required violation on `enroll-race` is a finding for the orchestrator, not a spec to edit.

**Docs owed:** none (L45).

---

## Wave 5: results, triage and hunting (no Sprint 2 dependency)

### L25. Results DB, ingest, signatures, known-issue matching, `overall`, `results_query`

- **Branch / worktree / commit:** `ml/l25-results-db` / `meridian-lab-l25` / `feat(lab): results database, signatures and overall grading`
- **Depends on:** L22. **Security review:** yes (evidence store).
- **Rules:**
  - R-PATH: the DB is `LabPaths.Results\lab-results.db`; ingest reads only bundles under `LabPaths.UatRuns` and `LabPaths.Jobs`.
  - SQL: `Microsoft.Data.Sqlite` with parameters only; `PRAGMA journal_mode=WAL`; a schema version table and forward-only migrations.
  - R-LOG: only redacted text from bundles enters the DB.
  - R-CPM (`Microsoft.Data.Sqlite` is pinned; keep the `SQLitePCLRaw.bundle_e_sqlite3` security pin), R-RESULT, R-USING, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C12 in full (schema, signature kinds and normalisation, known-issue TOML, run summary, `overall`, flakes); C8 `overall`; C4 `results_query`.

**Files**

1. `Results/ResultsDb.cs` (open, migrate, typed inserts and queries).
2. `Results/SignatureBuilder.cs`: C12 table exactly; `BuildAll(row)` returns every failure signature of the row (each exception block in the row window, each 5xx from step results and Hosting.Diagnostics lines, each failing clause, each unallowed violation, each required `hist` violation from the row JSON `history.violations` (L51), each `twin`, `peer`, `env` and `ver` entry once L53, L58, L57 and L60 write them) and `Primary(row)` the bucketing one; `SignatureId(text)`.
3. `Results/KnownIssues.cs`: `static Result<KnownIssues> Load(string path)` with C12's loader rules (absent file → empty; every problem listed, none thrown); `KnownMatch Match(RowFailure row)` per C12 "Matching": all signatures, one open issue, scope checked, the row's `known_issue` treated as a claim that must match, `fixed` issue → `regressed:<id>`. Patterns are .NET regex with a 1 s timeout; a timeout counts as no match and is logged.

   ```csharp
   public sealed record RowFailure(string Section, string RowId, string? ClaimedKnownIssue, IReadOnlyList<FailureSignature> Signatures);
   public sealed record FailureSignature(string Kind, string Text, string? Service, string? InvariantId);
   public sealed record KnownMatch(string? IssueId, string State, string? Reason); // State: "known" | "new" | "claimed_mismatch" | "regressed"
   ```
4. `Results/BundleIngestor.cs`: idempotent by `run_id`; inserts runs (with `fingerprint_sha256`, `fidelity`), rows (with `signatures_json`, `known_match`), `pairs` when `run.json` has `pair`, faults, violations, signatures (upsert, hits, first and last seen); detects flakes on ingest (same section, row, seed and build graded differently).
5. `Uat/Bundle/BundleWriter.cs`: at run end, ingest, then compute `overall`, `new_failures`, `known_failures` (C12) and write them to `run.json` and `ledger.md`; `uat_report` returns them. L48's `ReplayJudge` gains the C18 signature rule: an `exact` replay reproduces only with the same grades and the same primary signature per failed row; a `schedule_approximate` replay reproduces when the original primary signature appears in one of up to 5 attempts.
6. `Tools/Results/ResultsQueryTool.cs` (`results_query`; views `runs`, `rows`, `buckets`, `flakes`, `trend` (per row and per signature: date, grade or hits), `known_issues`, `findings` (empty until L26)).
7. Tests: `SignatureBuilderTests` (golden normalisation: compiler-generated frames, GUIDs and digits in routes, fault kinds sorted; `BuildAll` returns the 500 and the NotHeld clause of one row), `KnownIssuesTests` (`Match_ClaimedIssueSignatureDiffers_IsNew`, `Match_OneOfTwoSignaturesUnmatched_IsNew`, `Match_OutOfScopeService_IsNew`, `Match_FixedIssue_IsRegressed`, `Match_AllSignaturesSameIssue_IsKnown`, `Load_UnanchoredPattern_IsInvalid`, `Load_DotStarPattern_IsInvalid`, `Load_NoSignaturesNoPatterns_IsInvalid`), `BundleIngestorTests` (ingest twice → one run; `signatures_json` filled), `OverallTests` (`pass`, `pass_with_known`, `incomplete`, `fail`; an unallowed violation linked to no issue → `fail`; known failures plus a SKIPPED row → `incomplete`), `FlakeOnIngestTests`.

**Tests and why they fail when reverted:** `Overall_UnlinkedViolation_IsFail` pins status honesty; `Match_ClaimedIssueSignatureDiffers_IsNew` and `Match_OneOfTwoSignaturesUnmatched_IsNew` fail if a claim or one matching signature hides a new failure (D-ML46); `Signature_CompilerFrames_Normalised` keeps buckets stable across builds.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

**Docs owed:** none (L45).

### L26. Triage: buckets, known issues, drafts, ledger

- **Branch / worktree / commit:** `ml/l26-triage` / `meridian-lab-l26` / `feat(lab): triage buckets, known issues and issue drafts`
- **Depends on:** L25. **Security review:** yes (drafts leave the lab as text).
- **Rules:**
  - No tool calls GitHub or runs `gh` (D-ML28); R-HOST already refuses non-lab hosts.
  - R-PATH: drafts and the ledger only under `LabPaths.Triage`; `repro_path` must be inside a bundle.
  - R-LOG: drafts carry only redacted excerpts; `SecretScanner` (L16) runs on every draft before it is written; a hit refuses the draft.
  - `issue_url` must match `^https://github\.com/SandboxServers/MeridianConsole/issues/\d+$`.
  - R-RESULT, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C12 "Known issues", "Triage files"; C4 `triage_show`, `triage_draft_issue`, `triage_mark`, `results_known_issue_set`; D-ML28, D-ML42.

**Files**

1. `docs/guides/uat-specs/known-issues.toml`: the C12 seeds (`KI-DV13`, `KI-DV12`, `KI-DV3`, `KI-NOTIF-OUTBOX`, `KI-RABBIT-HOST`, `KI-DISCORD-SECRETS-PORT`, `KI-DELAYED-PLUGIN`, `KI-94`) with titles, links to `docs/PROJECT-STATE.md` or the issue, and patterns that pass C12's loader rules, each with a `scope` naming the service (and sections where known) so an issue cannot excuse the same text from another service; `KnownIssuesShippedFileTests` loads the shipped file and fails on any loader error; add one entry per L23 finding the orchestrator has accepted as known (the brief lists them).
2. `Triage/TriageService.cs`: after each ingest, a signature never seen before becomes a `findings` row (`state new`, `repro_path` = the bundle's `repro.toml`); rewrites `LabPaths.Triage\ledger.md` (C12 columns; tables with spaces around pipes).
3. `Triage/DraftWriter.cs`: C12 draft contents, ending with the line `Draft only. Filed by the owner after approval (D-ML28, D-ML42).`
4. `Tools/Triage/TriageTools.cs`: `triage_show`, `triage_draft_issue`, `triage_mark`, `results_known_issue_set` (the KI id must exist in the file).
5. `.claude/skills/lab-uat/SKILL.md` section "Triage": read `overall`; `new_failures` go to the user with `triage_show` and the draft path; never file; tell the user the draft path and the replay command. If D-ML42 has been ruled when this packet runs, add the ruled filing step after the user's explicit yes per draft; otherwise leave that sentence out. `.claude/agents/uat-driver.md` gains `results_query` and `triage_show` (read-only) in `tools:`; `DriverToolListTests` (S-L5) stays green because neither is in the forbidden set.
6. Tests: `TriageServiceTests` (new signature → finding; seen signature → hits only), `DraftWriterTests` (golden; contains the "Draft only" line; planted vault value → refused), `TriageMarkTests` (other host or repo in `issue_url` refused), `NoGitHubCallsTests` (scans `src/Tools/Dhadgar.Lab/**/*.cs` for `api.github.com`, `"gh"` process names and `Octokit`; none).

**Tests and why they fail when reverted:** `NoGitHubCallsTests` fails if anything starts filing; `Draft_PlantedSecret_IsRefused` fails if the scan is skipped.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

**Docs owed:** none (L45).

### L27. Async jobs, `results_flake_check`, CsCheck enrollment hunter

- **Branch / worktree / commit:** `ml/l27-jobs-hunter` / `meridian-lab-l27` / `feat(lab): async jobs and a seeded model-based enrollment hunter`
- **Depends on:** L25 (parallel with L26). **Security review:** yes.
- **Rules:**
  - R-SEED: the hunter's CsCheck seed derives from the run seed (below); every op choice comes from CsCheck's generator, nothing from `System.Random`.
  - R-CONTRACT: hunter ops dispatch lab tools through `LabToolDispatcher` with the job's lease; no direct `NodesLabAdapter` calls.
  - R-RESULT: a CsCheck failure is caught where `Sample` is called and becomes a `Result` with the shrunk case; nothing else throws.
  - R-TIME, R-PATH (job folders under `LabPaths.Jobs\<job_id>\`), R-CPM (add `CsCheck` 5.0.0 to `Directory.Packages.props`), R-DI, R-NULL, R-XUNIT.
  - **API check first:** read CsCheck 5.0.0's XML docs in the NuGet cache. The packet assumes `PCG(uint stream, ulong seed)`, `PCG.ToString()` as the seed string, and `Gen<T>.Sample(Func<T,bool>, ..., seed: string, iter: long, time: int, threads: int)`. If a shape differs, apply the Stop rule and record the real calls in the report.

**Contract excerpt:** C4 `job_status`, `job_cancel`, `hunt_model_run`, `results_flake_check`; C12 `runs.kind`, flakes; C17 seeds.

**Files**

1. `Jobs/JobRunner.cs`: one job at a time (`[Lab.JobRunning] <job_id>`); OwnLease semantics like `uat_run` (own lease `job/<job_id>`, renewed); `CancellationTokenSource` per job; lease-end hook cancels; job folder with `job.json` (`{job_id, kind, seed, state, started_at, ended_at, progress, result}`) and a bundle in C9 shape that `BundleIngestor` ingests with `runs.kind` = the job kind. `StatusDto.RunningJob`.
2. `Tools/Jobs/JobTools.cs`: `job_status`, `job_cancel`.
3. `Results/FlakeCheckJob.cs` + `results_flake_check`: reruns the named rows (default: the run's failed rows) with the same seed and the current build `reruns` times through `uat_run`; ingest marks flakes.
4. `Hunt/EnrollmentModel.cs`: model state `{tokens: [{state: unused|used|expired}], nodes: int, pgDown: bool}`; ops `Mint(validityMinutes)`, `Enroll(tokenIndex, via)`, `EnrollAgain(tokenIndex)`, `PgDown()`, `PgUp()`, `RestartNodes()`. Expected results: `Enroll` on an unused token with Postgres up → 201 and `nodes + 1`; on a used token → 401; with Postgres down → no 2xx and the token stays unused; after each op the DB agrees with the model (node count, used-token count via `db_query`) and `invariants_check` shows no I1 or I3 violation.
5. `Hunt/ModelHunter.cs`: `Gen` of 1..15 ops; iteration = `stack_reset_data databases=["nodes"]`, `fault_reset`, run the ops, check; CsCheck seed string = `new PCG(0u, runSeed).ToString()`; `Sample(..., iter: iterations, time: budget_s, threads: 1)`. On failure: the shrunk op list, CsCheck's reported seed, the first diverging op and the observed vs expected values go to `repro.toml` (`kind = "hunt"`, `model`, `run_seed`, `cscheck_seed`, `ops`, `build_id`) and a row `hunt/enrollment` FAIL in the bundle. `hunt_model_run {replay}` runs exactly the recorded ops once and reports whether the failure reproduces.
6. Tests: `JobRunnerTests` (second job refused; lease end cancels; job bundle ingested), `ModelHunterTests` against a fake lab with a planted bug (reused token returns 201): the hunter finds it, shrinks to at most three ops (`Mint`, `Enroll`, `EnrollAgain`), and the replay reproduces it; same seed → same first op list. `HunterDockerTests` (`RequiresDocker`): 3 iterations against the real lab finish with a result and a bundle.

**Tests and why they fail when reverted:** `Hunter_PlantedReuseBug_ShrinksAndReplays` fails if shrinking or replay breaks; `Job_LeaseEnd_Cancels` fails if a job outlives its lease.

**Checks**

```powershell
dotnet restore <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj --force-evaluate
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~HunterDockerTests"   # Docker running
```

**Docs owed:** none (L45).

### L28. Contract fuzzer through the Gateway

- **Branch / worktree / commit:** `ml/l28-fuzzer` / `meridian-lab-l28` / `feat(lab): seeded contract fuzzer with problem-details oracle`
- **Depends on:** L27. **Security review:** yes.
- **Rules:**
  - R-CONTRACT: request bodies are built from the `Dhadgar.Contracts` type named by the target, serialised with the product's JSON options, then mutated.
  - R-SEED: case `n` uses `LabRandom(seed, "fuzz:<target>:<n>")`, so any single case replays alone.
  - R-HOST (`http_request` only), R-LOG (bodies redacted in the bundle), R-RESULT, R-NULL, R-XUNIT.

**Contract excerpt:** C10 "Fuzz targets"; C4 `fuzz_run`; F29 error shape.

**Files**

1. `Fuzz/FuzzTargets.cs`: the C10 record and the `enroll` entry.
2. `Fuzz/Mutators.cs`: drop a property, null a property, swap a type (string ↔ number ↔ array ↔ object), boundary strings (empty, 64 KiB, 1 MiB, NUL, control characters, RTL and combining marks, `../`, `%00`), numeric extremes, extra unknown properties, duplicate keys, truncated JSON, wrong `Content-Type`, empty body.
3. `Fuzz/FuzzOracle.cs`: per case: status is never 500-599; a 4xx has `application/problem+json` with `type`, `title`, `status`; latency under 10 s; no vault value or daemon token in the response; an `invariants_check` over the job window at the end. Each failing case becomes a row `fuzz/<target>/<n>` with an `http` signature.
4. `Fuzz/FuzzJob.cs` + `fuzz_run` (job kind `fuzz`); `replay` runs one case by index.
5. Tests: `MutatorsTests` (same seed and index → same bytes), `FuzzOracleTests` (500 → failure; 400 without problem details → failure; 400 problem details → ok), `FuzzJobTests` with a fake HTTP that returns 500 for one mutation → one failing row and a replayable index.

**Tests and why they fail when reverted:** `Oracle_400WithoutProblemDetails_Fails` pins the error contract; `Mutators_SameSeed_SameCase` pins replay.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

**Docs owed:** none (L45).

### L29. Chaos scheduler, soak runs, resource sampler, I7

- **Branch / worktree / commit:** `ml/l29-chaos-soak` / `meridian-lab-l29` / `feat(lab): seeded chaos plans, soak runs and resource-trend invariant`
- **Depends on:** L27 (parallel with L28). **Security review:** yes (unattended faults).
- **Rules:**
  - R-SEED: the schedule (which fault, when, how long) comes from `LabRandom(seed, "chaos")`; the timeline is written as it happens.
  - R-BLAST: chaos injects only through the fault tools; a plan may name only C4 fault tools and C10 targets, validated before the job starts.
  - R-TIME, R-RESULT, R-DOCKER (`docker stats --no-stream --format "{{json .}}"` for registered containers only), R-NULL, R-XUNIT.

**Contract excerpt:** C4 `chaos_run`, `soak_run`; C11 I7.

**Files**

1. Chaos plan format, `docs/guides/uat-specs/chaos/<name>.toml`:

   ```toml
   schema = 1
   [plan]
   id = "nodes-enroll"
   workload = { spec = "docs/guides/uat-specs/lab-selftest.toml", rows = ["enroll-gateway"] }   # looped
   max_concurrent = 2
   quiet_s = [5, 30]                 # gap between faults, seeded uniform
   [[fault]]
   tool = "fault_toxic_add"
   args = { proxy = "nodes-postgres", toxic = "latency", stream = "downstream", attributes = { latency = 1500 } }
   weight = 3
   duration_s = [5, 45]
   ```

2. `Chaos/ChaosPlan.cs` (load and validate), `Chaos/ChaosJob.cs` (workload loop, seeded schedule, invariant checkpoint every 30 s, everything healed at the end, bundle kind `chaos` with the timeline in `repro.toml`; replay re-runs the same schedule).
3. `Soak/SoakJob.cs` + `soak_run` (loops a spec's rows for `duration_s`), `Soak/ResourceSampler.cs` (every 30 s: memory, PIDs per lab container; the daemon's own handle count), `Invariants/I7ResourceTrendInvariant.cs` (least-squares slope per series over the soak; limits from `docs/guides/uat-specs/invariants/soak.toml`: `memory_mib_per_hour`, `pids_per_hour`).
4. `docs/guides/uat-specs/chaos/nodes-enroll.toml` and `docs/guides/uat-specs/invariants/soak.toml` (memory 50 MiB/h, PIDs 10/h; change only with a README decision).
5. Tests: `ChaosPlanTests` (non-fault tool refused; unknown proxy refused), `ChaosScheduleTests` (same seed → same timeline), `I7Tests` (flat series clean; rising series over the limit violates), `ChaosJobTests` with fakes (all faults healed at end, even on cancel).

**Tests and why they fail when reverted:** `ChaosJob_Cancelled_HealsEverything` and `ChaosSchedule_SameSeed_SameTimeline`.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

**Docs owed:** none (L45).

### L52. Behavioural coverage and `coverage_report`

- **Branch / worktree / commit:** `ml/l52-coverage` / `meridian-lab-l52` / `feat(lab): behavioural coverage of routes, faults, error codes, invariants and transitions`
- **Depends on:** L25, L23 (parallel with L26 to L29). **Security review:** no (read-only accounting over redacted evidence); the reviewer checks that `NodesEndpointUniverseTests` adds no product code and no project reference.
- **Rules:**
  - Lab isolation: `tests/Dhadgar.Nodes.Tests` reads `src/Tools/Dhadgar.Lab/coverage-universe.json` as a file found from the repo root (walk up to the folder holding `Dhadgar.sln`); it gains no `ProjectReference`. `LabIsolationTests` stays green.
  - R-LOG: coverage keys hold templates, status classes, error codes and ids of checks only; never a path with a real id, a body or a header.
  - R-OBSERVE: transition snapshots use `db_query` (direct 15432, reader role).
  - R-RESULT, R-NULL, R-XUNIT.

**Contract excerpt:** C18 "Behavioural coverage" table; C12 `coverage` table; C4 `coverage_report`; C10 "Fault impact map" (`GatewayRouteTable`).

**Files** (under `src/Tools/Dhadgar.Lab/` unless named)

1. `coverage-universe.json` (copied to output):

   ```json
   {
     "schema": 1,
     "services": {
       "nodes":   { "routes": [ { "method": "POST", "template": "/api/v1/agents/enroll" } ], "error_codes": [ "invalid_token" ], "transitions": [ "node:Online->Degraded" ] },
       "gateway": { "routes": [ { "method": "*", "template": "/api/v1/agents/{**catch-all}" } ], "error_codes": [], "transitions": [] }
     }
   }
   ```

   Fill Nodes' `routes` from its endpoint files (every `Map*` in `src/Dhadgar.Nodes/Endpoints/*.cs` and the hub path), with the public path as the Gateway exposes it; `error_codes` from the `ServiceResult.Fail` codes in `src/Dhadgar.Nodes/Services/*.cs`, the problem `type` values in the endpoints and `AgentHubErrorCodes`; `transitions` from every assignment to a `NodeStatus`, certificate `IsRevoked` and token `UsedAt`/`IsRevoked` in Nodes' services. Gateway `routes` are its YARP route prefixes from `GatewayRouteTable` with method `*`.
2. `Coverage/CoverageUniverse.cs`: load and validate (unknown service or duplicate route → failure).
3. `Coverage/PathTemplateMatcher.cs`: matches a concrete path to the most specific template (a `{name}` segment matches one segment; `{**x}` the rest); no match → key `<service> <METHOD> ?unmatched <class>` (listed, never in the universe).
4. `Coverage/CoverageCollector.cs`: after each run, keys from the run's Hosting.Diagnostics lines in the L07 log store (`route_status`), the run's `faults` (`fault_target`), problem+json `type` or `errorCode` in step results and hub error codes in `hub` sources (`error_code`), the run's `invariants.json` checks that ran without error (`invariant_check`), and `TransitionSnapshotter` output (`transition`). Writes C12 `coverage` rows through `ResultsDb`.
5. `Coverage/TransitionSnapshotter.cs`: at each row end, for Nodes: `select id, status from nodes`, `select id, is_revoked from agent_certificates`, `select id, used_at is not null, is_revoked from enrollment_tokens` (exact table and column names from the Nodes model snapshot); a changed value for the same id between consecutive snapshots is a transition `<entity>:<from>-><to>`.
6. `Tools/Coverage/CoverageReportTool.cs` (`coverage_report`, Open): totals, hits and unhit keys per kind over runs since `since_run_id` (default: all); `5xx` and `?unmatched` keys listed under `outside_universe`.
7. `tests/Dhadgar.Nodes.Tests/Lab/NodesEndpointUniverseTests.cs`: with `NodesWebApplicationFactory`, read every `RouteEndpoint` from `EndpointDataSource` (method from `HttpMethodMetadata`, template from `RoutePattern.RawText`, mapped to the public path by the same prefix rule the file uses); the set must equal the file's `nodes.routes`; the failure message lists missing and extra entries and says "update src/Tools/Dhadgar.Lab/coverage-universe.json".
8. Tests (`tests/Dhadgar.Lab.Tests/Coverage/`): `PathTemplateMatcherTests` (GUID segment → `{nodeId}`; most specific template wins; unmatched listed), `CoverageCollectorTests` (a 503 counts as `5xx`, outside the universe; a 401 hits `4xx`), `CoverageUniverseTests` (the shipped file loads; every `transitions` entry names a known entity), `CoverageReportToolTests` (unhit = universe minus hits), `TransitionSnapshotterTests` (fake snapshots: `Online` then `Offline` → one transition).

**Tests and why they fail when reverted:** `NodesEndpointUniverseTests` fails when a Nodes route is added without updating the universe, so "unhit" never silently shrinks. `CoverageCollectorTests` fails if a 5xx inflates coverage.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj -c Release --filter "FullyQualifiedName~NodesEndpointUniverseTests"
```

**Docs owed:** none (L45).

---

## Wave 5E: experiments and planted defects (no Sprint 2 dependency)

Twins (D-ML50), mutants (D-ML51) and the lab's own self-check. L53 and L54 run in parallel; L55 needs L54. Every packet gets a `security-advisor` pass (mutants build deliberately broken product images; self-check plants defects in the lab). Base filter `F`.

### L53. Twin engine; red S-L12

- **Branch / worktree / commit:** `ml/l53-twins` / `meridian-lab-l53` / `feat(lab): paired twin experiments with equal-outcome and isolation relations`
- **Depends on:** L25, L22. **Security review:** yes (variant worlds inject faults).
- **Rules:**
  - R-BLAST / R-CONTRACT: variant faults are C4 fault tools dispatched through `LabToolDispatcher` by `RowExecutor`, exactly like spec faults; `TwinSpec` validation refuses any tool that is not a C4 fault tool.
  - R-SEED: both worlds use the experiment's `seed` (or the call's `seed`); the variant's extra choices use the stream `twin:<id>:variant`.
  - R-PATH: experiment files only under `docs/guides/uat-specs/twins/`; the name matches `^[a-z0-9-]{1,64}$`.
  - R-RESULT, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C19 "Twin experiments" in full; C18 `PairInfo`; C12 `pairs`, signature kind `twin`; C4 `twin_run`, `stack_reset_data`.

**Files** (under `src/Tools/Dhadgar.Lab/` unless named)

1. `Twins/TwinSpec.cs` and loader (Tomlyn; unknown keys refused; `disjoint` outside `isolated` refused; each pointer parsed as RFC 6901; `[base]` rows must exist in the spec).
2. `Twins/RunProjection.cs`: builds C19's projection from a finished run's row JSON (`grade`, step `status`, `error_code`, `outcome`, captures after redaction).
3. `Twins/TwinComparer.cs`: masks GUIDs, ISO timestamps, `duration_ms` and 64-hex thumbprints in both values, then compares; `disjoint` compares two arrays from the variant; returns `held` and a diff list `[{pointer, base, variant}]`.
4. `Uat/UatRunOverlay.cs`: `sealed record UatRunOverlay(IReadOnlyList<FaultSpec> ExtraFaults, string? PreSpec, IReadOnlyList<string> PreRows, PairInfo? Pair)`; `UatRunner` accepts it internally (not a tool argument): pre rows run first (graded, recorded), extra faults are added `before_steps` to every compared row, `Pair` goes to `run.json`.
5. `Twins/TwinRunner.cs`: base: `stack_reset_data` → run `[base]` rows with `Pair(role = base)`; variant: `stack_reset_data` → run `[variant]` with the overlay; compare if every base row is PASS (else C19's precondition rule); write the synthetic row `twin/<id>` to both bundles; ingest both (L25 fills `pairs`).
6. `Tools/Twins/TwinRunTool.cs` (`twin_run`, OwnLease).
7. `docs/guides/uat-specs/catalog/twin-enroll.toml` (row `enroll-once`: mint, enroll via the Gateway, status 201, capture `node_id`) and `docs/guides/uat-specs/twins/enroll-latency-invariance.toml` (C19's example).
8. `tests/Dhadgar.Lab.Tests/Seams/TenantTwinSeamTests.cs` (S-L12, `RequiresDocker`, `[Trait("Seam","S-L12")]`): `twin_run {experiment = "tenant-isolation-enroll", seed = 21}` must return `held = true`. Red now: the experiment file arrives in L56 (`[Lab.NotFound]`).
9. Tests: `TwinSpecTests`, `TwinComparerTests` (`GuidsMasked_Equal`, `GradeDiffers_NotHeld`, `Disjoint_Overlap_NotHeld`), `TwinRunnerTests` with fakes (`Twin_PreconditionFailed_IsIncompleteNotPass`: base row FAIL → `held = null`, synthetic rows SKIPPED; `Twin_NotHeld_TwinSignatureOnBothRuns`), `TwinLatencyDockerTests` (`RequiresDocker`: `enroll-latency-invariance` holds).

**Tests and why they fail when reverted:** `Twin_PreconditionFailed_IsIncompleteNotPass` fails if a broken base world reports "relation held" (the twin defect class: comparing two failures as equal). `Disjoint_Overlap_NotHeld` fails if a leak between tenants compares as isolated.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~TwinLatencyDockerTests"   # Docker running
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L12"   # Docker running; expected red, record the failure line
```

**Docs owed:** none (L45).

### L54. Mutant infrastructure; M1, M4, M5

- **Branch / worktree / commit:** `ml/l54-mutants` / `meridian-lab-l54` / `feat(lab): lab-only product mutants and mutation runs`
- **Depends on:** L27, L51, L23 (parallel with L53). **Security review:** yes (deliberately broken product images; `git` processes).
- **Rules:**
  - Isolation (D-ML51): patches live only under `tools/lab/mutants/`; no product project, props file or workflow references that folder; a product mutant is applied only in a `git archive` copy under `LabPaths.Builds`; the coder **never commits a product change**: make each patch by editing the product file in the worktree, `git diff -- <file> > tools/lab/mutants/<id>/change.patch`, then `git checkout -- <file>`, and confirm `git status` shows only `tools/lab/` and `src/Tools/` changes before committing.
  - R-ARGS: `git` runs with `ArgumentList` (`-C`, `<dir>`, `archive`, `--format=tar`, `HEAD`; `apply`, `--check`, `<patch>`); never a shell.
  - R-PATH: extraction only into `LabPaths.Builds\mutant-<id>-<build8>\`; `TarFile.ExtractToDirectory` refuses entries outside it (check each entry's full path before writing, as `TarFile` does not reject `..` on every platform; assumption to verify in the packet).
  - R-DOCKER: mutant images carry `meridian-lab-mutant=<id>`; the job refuses to start a mutant image outside `mutation_run`; it always ends with `svc_start` of the last plain build, also on cancel and failure.
  - R-RESULT, R-USING (`Process`), R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C19 "Mutants" in full (TOML, table, rules, verdicts); C4 `lab_build {mutant}`, `mutation_run`; C11 I5; README gate MD.

**Files** (under `src/Tools/Dhadgar.Lab/` unless named)

1. `tools/lab/mutants/M1-token-no-concurrency/`, `M4-no-concurrency-catch/`, `M5-token-logged/`: `mutant.toml` (C19; `base` = the worktree's HEAD) and `change.patch`. M1 detector `history_run enroll-race`; M4 detector `history_run enroll-race` with `expect_pattern = "^inv:I1\\|nodes\\|"`; M5 detector `uat_run lab-selftest.toml rows=["enroll-gateway"]` with `expect_pattern = "^inv:I4\\|nodes\\|"`. `tools/lab/mutants/README.md`: what a mutant is, how to refresh a stale patch, and that nothing here ships.
2. `Mutants/MutantSpec.cs` and loader (kinds `product` and `lab`; `files` must exist at HEAD).
3. `Mutants/MutantBuilder.cs`: `lab_build {mutant}` (extend L09's build tool): `git -C <RepoRoot> archive --format=tar HEAD` into a temp tar under `LabPaths.Builds`, extract, `git apply --check change.patch` then `git apply change.patch` with the extracted folder as working directory (outside any repo, `git apply` acts on plain files), then the normal locked restore and publish of `services` only; images tagged `meridian-lab/<project>:mutant-<id>-<build8>`. A failed `--check` → verdict `stale`, nothing built.
4. `Mutants/MutationRunJob.cs` (`mutation_run`, job kind `mutation`): per mutant: build → `svc_start` the mutant images → run the detector through `LabToolDispatcher` (one attempt for `exact` fidelity, up to 5 for `schedule_approximate`) → verdict per C19 from the detector run's signatures (`detected` if one matches `expect_pattern`, `detected_other` if another new signature appeared, else `survived`; `unreachable` when the detector cannot run, with the reason and the open known issue) → `svc_start` the plain build. Writes `mutants.json` (`[{id, verdict, detector_run_ids, signatures, reason}]`) and `mutants.md`.
5. `Invariants/I5OrphanInvariant.cs`: a container labelled `meridian-lab-mutant` while no `mutation` job runs is a violation.
6. `tests/Dhadgar.Lab.Tests/LabIsolationTests.cs`: add `NoProductProjectReferencesMutants` (no `*.csproj`, `*.props`, `*.targets` or `.github/workflows/*` outside `src/Tools/` and `tests/Dhadgar.Lab*` mentions `tools/lab/mutants`) and `MutantPatchesTouchOnlyDeclaredFiles` (every `diff --git a/<p>` path is in the mutant's `files`; product mutants touch only `src/` outside `src/Tools/`; lab mutants only `src/Tools/`).
7. `tests/Dhadgar.Lab.Tests/Mutants/MutantPatchesApplyTests.cs` (runs in CI; needs `git`): `git -C <repo root> apply --check <patch>` for every mutant; a failure names the mutant and points at the README's refresh steps. This is the early warning that a product change made a mutant stale.
8. Tests: `MutantSpecTests`, `MutantBuilderTests` (fake process runner: exact `ArgumentList`; `--check` failure → `stale`; a tar entry with `..` refused), `MutationRunJobTests` (fakes: each verdict; plain build restored after a detector failure and after cancel), `I5_MutantImageOutsideJob_Violation`, `MutationDockerTests` (`RequiresDocker`, 30-minute timeout: `mutation_run {mutants = ["M1-token-no-concurrency"]}` → `detected`).

**Tests and why they fail when reverted:** `MutationRunJob_DetectorFails_PlainBuildRestored` fails if a mutant can outlive its job. `MutantPatchesTouchOnlyDeclaredFiles` fails if a patch reaches beyond its declared files. `MutationDockerTests` fails if the history checker cannot see a missing concurrency token (then the verdict is `survived` and the orchestrator asks for a ruling; never weaken the mutant).

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~MutationDockerTests"   # Docker running
git -C <W> status --porcelain   # only tools/lab/, src/Tools/ and tests/Dhadgar.Lab.Tests/ paths
```

Then, with the daemon and the lease, `mutation_run` for M1, M4 and M5; record each verdict in the report. `survived` is a finding for the orchestrator.

**Docs owed:** `tools/lab/mutants/README.md` (above).

### L55. `Dhadgar.Lab.SelfCheck`: lab mutants LM1-LM8 and crash recovery

- **Branch / worktree / commit:** `ml/l55-selfcheck` / `meridian-lab-l55` / `feat(lab): self-check harness that plants defects in the lab itself`
- **Depends on:** L54, L19, L21, L25, L03. **Security review:** yes (spawns `dotnet test` and a child daemon).
- **Rules:**
  - Lab isolation: `src/Tools/Dhadgar.Lab.SelfCheck` is a console project referenced by nothing; add it to the lab project set in `LabIsolationTests` and to the solution in the `Tools` folder.
  - R-ARGS / R-USING: `git` and `dotnet` run with `ArgumentList` in `using var process`; `dotnet test <copy>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~<test>" --logger "trx;LogFileName=<id>.trx" --results-directory <dir>`.
  - R-PATH: copies only under `%TEMP%\meridian-lab-selfcheck\<guid>\`, deleted at the end (kept with `--keep`).
  - Judging: a lab mutant is `caught` only when the named test **ran** and **failed** in the TRX (`outcome="Failed"`); a build failure, zero tests run or a skipped test is `invalid`, never `caught`. A baseline pass over the unmodified copy runs first: every named test must pass there, else the whole self-check is `invalid`.
  - R-RESULT, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C19 lab mutant rows LM1-LM8 and the `kind = "lab"` TOML; README gate LS; C11 I5; L19 `FaultBook` recovery; C3 lease book.

**Files**

1. `tools/lab/mutants/LM1-journal-after-inject/` … `LM8-db-query-through-proxy/`: `mutant.toml` (`kind = "lab"`, `detector = { test = "..." }` per the C19 table) and `change.patch` (made as in L54; this time the patched files are lab files, still never committed in patched form).
2. `src/Tools/Dhadgar.Lab.SelfCheck/Dhadgar.Lab.SelfCheck.csproj` (console, `net10.0`, no package versions) and `Program.cs`: `selfcheck [--only <id>] [--keep] [--recovery]`; writes `selfcheck.json` (`[{id, test, verdict: caught|missed|invalid, detail}]`) and `selfcheck.md` under `LabPaths.Results\selfcheck\<timestamp>\`; exit code 0 only when every lab mutant is `caught`.
3. `src/Tools/Dhadgar.Lab.SelfCheck/TrxReader.cs`: reads `UnitTestResult` `testName` and `outcome`.
4. `tests/Dhadgar.Lab.Tests/ObservationPathTests.cs` (LM8's detector): the `db_query` tool's resolved connection uses port 15432 and the reader role, never a Toxiproxy port; `http_request` with `via = "direct"` resolves to the service's direct port.
5. Recovery mode (`--recovery`): start a child daemon (`dotnet <copy>\...\Dhadgar.Lab.dll` with `MERIDIAN_LAB_HOME` set to a temp folder and its own port), acquire a lease, add a latency toxic on `nodes-postgres`, `Process.Kill(entireProcessTree: true)`, start a new child on the same home, then assert: the toxic is gone (Toxiproxy `GET /proxies`), `faults_active` is empty, no lease is held, I5 reports nothing, and the run that was open is not ingested as `pass`.
6. Tests: `TrxReaderTests` (failed, passed, not run), `SelfCheckJudgeTests` (`BuildFailed_IsInvalidNotCaught`, `ZeroTestsRun_IsInvalid`, `BaselineFails_AllInvalid`), `LabMutantPatchesApplyTests` (CI, like L54's), `SelfCheckE2ETests` (`RequiresNetwork` for restore: `selfcheck --only LM1-journal-after-inject` → `caught`), `SelfCheckRecoveryTests` (`RequiresDocker`: the recovery mode passes).

**Tests and why they fail when reverted:** `BuildFailed_IsInvalidNotCaught` fails if a patch that breaks the build counts as "the test caught it". `SelfCheckRecoveryTests` fails if a daemon crash leaves a toxic or a lease behind (the D-ML46 write-ahead fix, now proven end to end).

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab.SelfCheck\Dhadgar.Lab.SelfCheck.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet run --project <W>\src\Tools\Dhadgar.Lab.SelfCheck -c Release -- selfcheck   # every LM caught; attach selfcheck.md to the report
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~SelfCheckRecoveryTests"   # Docker running
```

**Docs owed:** `src/Tools/Dhadgar.Lab/README.md` section "Self-check" (what gate LS means and how to run it).

---

## Wave 6: the machine pool and the Windows agent in containers (no Sprint 2 dependency)

**Wave 6 gate (orchestrator):** the L00 worknote names outcome A or B, and under B D-ML37 (and D-ML38 if side by side failed) are ruled yes and done by the owner. The architect then fills L31's "Settings from L00" block from the worknote before dispatch (a ruling note, not a re-plan). Under outcome C the orchestrator asks the architect for a re-slice ruling; L30 still ships (Linux and host providers), L31 to L33 wait, and waves 7 and 8 proceed except L43.

### L30. Pool model, constraints, Linux and host providers, `pool_*` tools

- **Branch / worktree / commit:** `ml/l30-pool` / `meridian-lab-l30` / `feat(lab): machine pool with linux container and host providers`
- **Depends on:** L19. **Security review:** yes (new mutating target kind).
- **Rules:**
  - R-BLAST: a member is registered as `m:<member id>` before its container starts; `pool_release`, `member_reset` and lease end destroy only containers registered as members; `m:host` is read-only.
  - R-DOCKER: `docker run -d --name meridian-lab-m-<id> --label meridian-lab=1 --label meridian-lab-member=<id> --network meridian-lab-net --restart no --memory 1g <image>`; no `-v`, no `-p`.
  - R-PATH: pool state at `LabPaths.State\pool.json` (atomic write).
  - R-RESULT, R-TIME, R-DI, R-USING, R-NULL, R-XUNIT.

**Contract excerpt:** C13 in full; C4 `pool_list`, `pool_acquire`, `pool_release`, `member_reset`; C7 `requires_pool`, `reuse_member`, `${member}`; C9 `members`.

**Files**

1. `Pool/PoolTypes.cs`: C13 records exactly.
2. `Pool/PoolManager.cs` (singleton; `ILeaseEndHook`, `ILabStatusSource` (`LeasedMembers`), `IHostedService`): `AcquireAsync(constraints, purpose, leaseId)` picks the first available provider whose dimensions match, enforces the per-OS limits (`[Lab.PoolExhausted]`), provisions `Count` members; `ReleaseAsync`, `ResetAsync` (state `Resetting`, provider reset, back to `Leased`); lease end destroys all members of that lease; `StartAsync` destroys every member listed in `pool.json` (crash recovery) and clears it.
3. `Pool/LinuxContainerProvider.cs`: image `meridian-lab/linux-member:<build_id>` from `tools/lab/images/linux-member.Dockerfile` (`FROM mcr.microsoft.com/dotnet/runtime:10.0`, copies the probe to `/opt/lab/probe`, `CMD ["sleep", "infinity"]`); dimensions `{linux, null, debian, none, container, x64, host, false}`; reset = destroy + provision with the same id.
4. `Pool/HostProvider.cs`: one member `host`, dimensions `{windows, <CurrentBuildNumber>, host, none, host, x64, host, false}`; provision returns it; reset and destroy return `[Lab.BlastRadius] host is read-only`.
5. `Pool/VmProvider.cs`: every call `[Lab.Unavailable] vm tier is a follow-up (D-ML31)`.
6. `Pool/WindowsContainerProvider.cs` stub: `IsAvailableAsync` → `[Lab.Unavailable] windows members arrive in L31`.
7. `FaultTargetGuard`: kind `member` resolves through the registry and live inspect (label `meridian-lab-member=<id>` too); tripwire as for containers.
8. `Tools/Pool/PoolTools.cs`: four tools; `lab_build` project `probe` also builds `linux-member`.
9. `UatRunner`: `requires_pool` acquires one member per section, binds `${member}`, calls `member_reset` before each row except `reuse_member = true` rows, releases at section end; a pool failure makes the section's rows SKIPPED with reason `pool: <error>`; `run.json.members`.
10. `I5OrphanInvariant`: containers labelled `meridian-lab-member` not in the pool are orphans.
11. Tests: `PoolManagerTests` with a fake provider (constraint matching; exhaustion; lease end destroys; recovery from `pool.json`), `HostProviderTests` (`member_reset m:host` refused; `BlastRadiusTests` gains `m:host` for every member tool), `RunnerPoolTests` (`${member}` bound; reset between rows; release at section end even when a row blocks). Docker: `LinuxPoolDockerTests` acquire, `docker exec` the probe `footprint --json` fails cleanly on Linux (exit 2), reset gives a new container id, release removes it.

**Tests and why they fail when reverted:** `LeaseEnd_DestroysMembers` and `Runner_ReleasesMemberWhenRowBlocks` pin cleanup; `MemberReset_Host_IsRefused` pins the read-only host.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~LinuxPoolDockerTests"   # Docker running
```

**Docs owed:** README section "Pool".

### L31. Windows container provider, member image, member reset; red S-L8

- **Branch / worktree / commit:** `ml/l31-windows-pool` / `meridian-lab-l31` / `feat(lab): windows server core container members`
- **Depends on:** L30, the wave 6 gate. **Security review:** yes.
- **Rules:**
  - R-DOCKER: every call uses `--context <Lab:Docker:WindowsContext>`; the registry stores the context per target and `IDockerInspector` inspects in that context.
  - R-BLAST: as L30. Windows members never get a bind mount or a published port.
  - R-OS: the provider is `[SupportedOSPlatform("windows")]`; on Linux it is unavailable.
  - R-XUNIT: Windows-container tests carry `RequiresWindowsContainers` and skip unless `MERIDIAN_LAB_WINDOWS_CONTAINERS=1`.
  - R-RESULT, R-TIME (provision timeout `Lab:Pool:WindowsProvisionTimeoutS`, default 300), R-NULL.

**Settings from L00 (the architect fills this block from the worknote before dispatch):**

| Setting | Value | Source |
| --- | --- | --- |
| `Lab:Docker:WindowsContext` | from the worknote | step 3 |
| isolation | `process` (outcome A) or `hyperv` (outcome B) | step 4 |
| base image | the dotnet `aspnet` Server Core LTSC tag that ran in step 4 (it carries both runtimes) | step 4 |
| `Lab:Pool:AgentIngressHost` | the address step 7 reached, plus port 25000 | step 7 |
| `--storage-opt size=` | `20G` if step 8 accepted it, else omitted (member disk fill stays unavailable) | step 8 |
| `Lab:Pool:MaxWindows` | from step 8's timings and memory, at most 3 | step 8 |

**Contract excerpt:** C13; C10 member targets and declared mounts; S-L8 row.

**Files**

1. `tools/lab/images/windows-member.Dockerfile`: `FROM <base image>`; `COPY probe/ C:/lab/probe/`; `SHELL` not used; `CMD ["powershell", "-NoProfile", "-Command", "while ($true) { Start-Sleep -Seconds 3600 }"]`. `lab_build` builds it as `meridian-lab/windows-member:<build_id>` in the Windows context when that context is configured.
2. `Pool/WindowsContainerProvider.cs`: `docker run -d --name meridian-lab-m-win-<nn> --label meridian-lab=1 --label meridian-lab-member=win-<nn> --isolation <iso> --memory 2g [--storage-opt size=20G] --restart no <image>`; no network flag (Windows `nat` default); readiness = `docker exec <id> C:\lab\probe\Dhadgar.Lab.Probe.exe footprint --json` exits 0; dimensions `{windows, <image build from docker inspect OsVersion>, servercore, <iso>, container, x64, host, false}`.
3. `Tools/Doctor/LabDoctorTool.cs`: checks `windows_context` (reachable), `windows_isolation` (a 30 s `cmd /c ver` run), `windows_member_image` (present for the latest build); capability `windows_containers` for `requires`.
4. `tests/Dhadgar.Lab.Tests/Seams/ContainerAgentEnrollSeamTests.cs` (S-L8, `RequiresWindowsContainers`, `RequiresDocker`, red until L33): acquire a Windows member; `agent_install mode=console`; `nodes_token_mint`; `agent_token_write`; `agent_start`; within 120 s a node with platform `windows` exists in Nodes and `certstore_list store=machine_my member=<id>` shows one certificate issued by the lab Nodes CA with a private key; `pool_release` removes the container. Red now because the agent tools return `[Lab.NotImplemented]`.
5. Tests: `WindowsProviderArgsTests` (exact `ArgumentList` with and without storage-opt; isolation from settings), `WindowsPoolDockerTests` (`RequiresWindowsContainers`): acquire, probe footprint shows an empty footprint, reset gives a new id, release removes it; the host's `Cert:\LocalMachine\Root` count and the host `HKLM\SOFTWARE\Meridian Console` key are unchanged before and after.

**Tests and why they fail when reverted:** `WindowsPool_HostUnchanged` pins isolation from the workstation; S-L8 stays red until L33.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&Seam!=S-L8&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
$env:MERIDIAN_LAB_WINDOWS_CONTAINERS = '1'; dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~WindowsPoolDockerTests"
```

**Docs owed:** README "Pool": the Windows provider and its settings.

### L32. Agent-in-member tools: install, start, stop, token, config, footprint reads, agent log

- **Branch / worktree / commit:** `ml/l32-agent-in-member` / `meridian-lab-l32` / `feat(lab): install and drive the windows agent inside pool members`
- **Depends on:** L31, L12. **Security review:** yes (agent install, enrollment token).
- **Rules:**
  - R-ARGS: `docker exec <id> sc.exe create <AgentWindowsFootprint.ServiceName> binPath= <path> start= demand` as separate `ArgumentList` items; never a command line built as one string.
  - R-ZERO: the token goes from `SecretVault` to `docker exec -i <id> C:\lab\probe\Dhadgar.Lab.Probe.exe token-write` on stdin as UTF-8 bytes; the byte buffer is zeroed after the write; the token never appears in an argument, an environment variable or a log.
  - R-CONTRACT: every footprint name from `AgentWindowsFootprint`; member reads return `ProbeFootprint` shapes.
  - R-PATH: binaries come only from `LabPaths.Builds\<build_id>\agent_windows\`; inside the member, under `C:\lab\agent\`.
  - R-BLAST: every tool resolves `member` through `FaultTargetGuard`; `m:host` refused.
  - R-LOG, R-RESULT, R-USING, R-NULL, R-XUNIT.

**Contract excerpt:** C4 `agent_install`, `agent_uninstall`, `agent_start`, `agent_stop`, `agent_token_write`, `agent_config_set` (allowlist), and the `member?` argument of `certstore_list`, `registry_read`, `state_file_read`, `logs_tail process=member:<id>`; C5.

**Files**

1. Probe modes (`src/Tools/Dhadgar.Lab.Probe/`):
   - `token-write`: reads stdin to end, writes the token the way main's Windows agent reads it (the coder reads `src/Agents/Dhadgar.Agent.Windows` for the source: today the installer writes `HKLM\` + `RegistryKeyPath` value `EnrollmentTokenValueName`, which `EnrollmentTokenCleanup` deletes after enrollment); zeroes the buffer; prints nothing.
   - `run-agent`: starts `C:\lab\agent\Dhadgar.Agent.Windows.exe` in console mode with `ArgumentList`, redirects stdout and stderr to `C:\lab\agent\agent.jsonl`, writes the PID to `C:\lab\agent\agent.pid`; `stop-agent` kills that PID.
   - `agent-log`: prints lines of `agent.jsonl` after `--since <ISO>` and, for service mode, Application Event Log entries of source `EventLogSourceName` after `--since`, as JSON lines `{ts, level, message}`.
2. `Agents/AgentInMember.cs`: `InstallAsync(member, mode, buildId)`: `docker cp` the build folder to `C:\lab\agent\`, `docker cp` a generated `appsettings.Lab.json` (control-plane URL `http://<Lab:Pool:AgentIngressHost>`, logging) and set `DOTNET_ENVIRONMENT=Lab` for the agent (service: `reg add` of the service `Environment` multi-string through `ArgumentList`; console: `run-agent --env`); `service` mode creates the service; `StartAsync`, `StopAsync` (`sc.exe start|stop` or the probe), `UninstallAsync` (`sc.exe delete`), `SetConfigAsync` (rewrites the overlay; allowlist per C4).
3. `Tools/Agents/AgentTools.cs`: the six tools.
4. Member reads: `certstore_list`, `registry_read` and `state_file_read` with `member` run `docker exec <id> C:\lab\probe\Dhadgar.Lab.Probe.exe footprint --json` and project the C4 result from `ProbeFootprint` (state file JSON redacted by the lab `Redactor` after the read); `logs_tail process=member:<id>` uses `agent-log`.
5. Tests: `AgentInMemberArgsTests` (exact `ArgumentList`s; the token is not in any argument; `m:host` refused), `TokenWriteTests` (probe reads stdin; buffer zeroed through an injectable sink), `MemberReadProjectionTests` (a `ProbeFootprint` fixture → C4 shapes; no registry value returned), `AgentConfigAllowlistTests`. Docker (`RequiresWindowsContainers`): install console, write a token, start, `logs_tail member:<id>` shows agent lines, stop, uninstall; record whether S-L8 already passes in the report.

**Tests and why they fail when reverted:** `TokenNeverInArguments` fails if the token moves to an argument; `MemberRead_NeverReturnsRegistryValue` pins the read rule.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\src\Tools\Dhadgar.Lab.Probe\Dhadgar.Lab.Probe.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&Seam!=S-L8&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
$env:MERIDIAN_LAB_WINDOWS_CONTAINERS = '1'; dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~AgentInMemberDockerTests|Seam=S-L8"
```

**Docs owed:** README section "The agent in a member".

### L33. Member faults, `agent-container-enroll.toml`, S-L8 green

- **Branch / worktree / commit:** `ml/l33-member-faults` / `meridian-lab-l33` / `feat(lab): faults inside members and the container enrollment spec`
- **Depends on:** L32. **Security review:** yes (agent state, client certificate, registry).
- **Rules:**
  - R-BLAST: member faults resolve `member` through the guard; backups stay inside the member under `C:\lab\backup\<fault_id>\` (a member reset discards them, which also heals).
  - R-ZERO: the probe zeroes key and token buffers it reads for backup; the client-certificate backup is exported with an ephemeral password from `RandomNumberGenerator` passed on stdin, never as an argument.
  - R-SEED for `garbage`; R-ARGS; R-RESULT; R-NULL; R-XUNIT.

**Contract excerpt:** C4 `fault_kill`, `fault_pause`, `fault_disk_fill`, `fault_corrupt_file member`, `fault_corrupt_registry`; C10 logical files `agent-state`, `agent-client-cert`; S-L8.

**Files**

1. Probe modes `corrupt` and `restore`: `corrupt --what agent-state|agent-client-cert|registry:enrollment_token --mode truncate|garbage|delete --backup <dir>` (garbage bytes read from stdin, supplied by the lab from `LabRandom`); `restore --backup <dir>` puts back the exact bytes, the certificate (with its private key) or the registry value, and deletes the backup.
2. `Faults/Kinds/MemberFaults.cs`: `fault_kill` and `fault_pause` on `m:` targets (kill: `docker kill`; heal: `docker start` and readiness; the agent is restarted by `agent_start` only when the spec says so); `fault_disk_fill` on `C:\` when L31 enabled storage-opt (fill with `fsutil file createnew C:\lab\.lab-fill <bytes>` after reading free space with `fsutil volume diskfree C:`), else `[Lab.Unavailable]`; `fault_corrupt_file member` and `fault_corrupt_registry` through the probe. Remove the "members arrive in L33" refusals from L20.
3. `docs/guides/uat-specs/agent-container-enroll.toml`, section `agent-container-enroll`, `requires = ["docker", "windows_containers"]`, `requires_pool = { os = "windows", kind = "container" }`, rows: `enroll-console` (install console, mint, token write, start; db node row platform `windows`; `certstore machine_my member` one lab-CA cert with private key; `registry member` token value absent after enrollment), `restart-no-reenroll` (`reuse_member = true`; stop, start; `delta` 0 on node count), `no-ca-in-root` (`certstore machine_root member` `issuer_thumbprint` lab CA `max_rows` 0), `state-corrupt` (catalog: corrupt `agent-state` garbage, restart the agent; the agent does not crash-loop silently: the expectation comes from the agent's documented behaviour, else I6-style floor `agent_log` shows an error line and no new node row; heal restores), `token-garbage` (catalog: corrupt the registry token before first start; enrollment fails with 401 at Nodes; no node row; the agent logs the failure), `gateway-down-at-enroll` (catalog: `fault_proxy_disable agent-ingress` before start; after heal the agent enrolls within 120 s).
4. Tests: `MemberFaultArgsTests`, `BlastRadiusTests` (all five tools with `m:host` and an unregistered member), `ShippedSpecsValidateTests` covers the new spec. S-L8 green.

**Tests and why they fail when reverted:** S-L8 goes red if any agent tool regresses; `MemberFault_Host_IsRefused`.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
$env:MERIDIAN_LAB_WINDOWS_CONTAINERS = '1'; dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L8"
```

Then one `uat run --specs <W>\docs\guides\uat-specs\agent-container-enroll.toml --label l33`; grades in the report.

**Docs owed:** none (L45).

---

## Wave 7: the rest of the in-scope control plane (no Sprint 2 dependency)

### L34. Identity, BetterAuth and Panel in the lab; red S-L9

- **Branch / worktree / commit:** `ml/l34-auth-stack` / `meridian-lab-l34` / `feat(lab): identity, betterauth and panel services`
- **Depends on:** L22. **Security review:** yes (auth services, signing keys).
- **Rules:**
  - R-ZERO: the Identity exchange key pair is generated per stack at `stack_up` into `LabPaths.State\identity\exchange-*.pem`; the private key goes to BetterAuth by env from the vault (never a file in an image); buffers zeroed after writing.
  - R-HOST, R-DOCKER, R-OBSERVE.
  - R-XUNIT: the Node image build needs the npm registry: `RequiresNetwork`.
  - Product code is not changed here (D-ML40 is separate); if a service cannot start without a product change, stop and report.
  - R-RESULT, R-PATH, R-DI, R-NULL.

**Contract excerpt:** C10 rows `identity`, `betterauth`, `panel` and their proxies; F21-F23, F28; D-ML32, D-ML36.

**Files**

1. `tools/lab/images/node-app.Dockerfile`: `ARG APP_DIR`; `FROM node:<major from package.json engines>-bookworm-slim`; copy `src/Dhadgar.SharedAuth` (when the app depends on it) and the app; `npm ci`; Panel: `npm run build` with `PUBLIC_GATEWAY_URL=http://localhost:15000`; `CMD` per app (`node dist/server/entry.mjs` for the Panel, the BetterAuth start script).
2. `Services/IdentityLabAdapter.cs`: env `ConnectionStrings__*` through `identity-postgres` (20030), `Redis` through `identity-redis` (20031), `RabbitMq__Host=meridian-lab-toxiproxy`, `RabbitMq__Port=20032`, the vhost and credentials; `Auth__Exchange__PublicKeyPem` from the generated key; issuer and audience equal to BetterAuth's; `ASPNETCORE_ENVIRONMENT=Development` (auto-migrate, F23).
3. `Services/BetterAuthLabAdapter.cs`: env mode (no Secrets service, F22), database through 20040, trusted origins `http://localhost:14321`, the exchange private key, the Gateway URL; health path from the app.
4. `Services/PanelLabAdapter.cs`: no dependencies; health `GET /login` 200.
5. Gateway: clusters `identity` and `betterauth` through proxies 20011 and 20013; `Readyz:RequiredClusters` = `[nodes, identity]`.
6. `ProxyCatalog` and `LabTargetRegistry`: the new proxies and databases; `stack_reset_data` gains `identity` and `betterauth` (table lists from their models or migrations); `db_query` databases.
7. `I2OutboxInvariant`: Identity's outbox tables if its model has them.
8. `tests/Dhadgar.Lab.Tests/Seams/PanelLoginSeamTests.cs` (S-L9, `RequiresDocker`, `RequiresNetwork`, red until L35): `ui_flow_run login_email` reaches `/dashboard`; `refresh` rotates the refresh-token hash prefix; replay is refused. Red now: `ui_flow_run` does not exist.
9. Tests: adapter env tests (exact keys; no secret in the args list, only in env from the vault), `AuthStackDockerTests` (`RequiresDocker`, `RequiresNetwork`): the three services become ready; `http_request via=gateway POST /api/v1/betterauth/sign-up/email` with a seeded user → 200.

**Tests and why they fail when reverted:** `AuthStack_SignUpThroughGateway` proves the wiring; S-L9 red.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&Seam!=S-L9&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~AuthStackDockerTests"   # Docker running, network
```

**Docs owed:** README "Services": Identity, BetterAuth, Panel.

### L35. Playwright, `ui_flow_*`, the four flows; S-L9 green

- **Branch / worktree / commit:** `ml/l35-playwright` / `meridian-lab-l35` / `feat(lab): playwright panel flows for login, refresh and logout`
- **Depends on:** L34. **Security review:** yes (session cookies, tokens).
- **Rules:**
  - R-ZERO / R-LOG: tokens and cookies never leave the browser context except as presence flags and the 8-hex SHA-256 prefix (C14); screenshots are taken only on pages without tokens in the DOM (the coder checks; `/callback` is never screenshotted); Playwright traces are off.
  - R-PATH: browsers in `LabPaths.Playwright` (`PLAYWRIGHT_BROWSERS_PATH`); screenshots in the bundle or job folder.
  - R-USING: `IPlaywright`, `IBrowser`, `IBrowserContext` disposed; the Playwright driver process is assigned to the daemon's Job Object (L07) so it dies with the daemon.
  - R-HOST: the browser context routes every request not to `localhost:14321` or `localhost:15000` to abort (`context.RouteAsync("**/*", ...)`), so the Panel cannot call out.
  - R-CPM (`Microsoft.Playwright` 1.64.1), R-RESULT, R-SEED (user email from `${seed}`), R-NULL, R-XUNIT.

**Contract excerpt:** C14 in full; C4 `ui_flow_list`, `ui_flow_run`; D-ML32; S-L9.

**Files**

1. `Ui/PlaywrightHost.cs`: lazy start; `lab_doctor` check "playwright browsers"; install path `meridian-lab ui install` runs `Microsoft.Playwright.Program.Main(["install", "chromium"])` with `PLAYWRIGHT_BROWSERS_PATH` set (`RequiresNetwork`).
2. `Ui/Flows/*.cs`: the four C14 flows; `login_email` signs up and in through `http_request via=gateway` (vault password), parses `Set-Cookie` for BetterAuth's session cookie name, adds it with `Domain = "localhost"`, `Path = "/"`, `Secure = true`, `HttpOnly = true`, `SameSite = None`, then opens `/callback`. `refresh` records `refresh_path` (`panel` or `direct`).
3. `Tools/Ui/UiTools.cs`; clause source: `ui_flow_run` is a step tool, and `step` clauses read `UiFlowResult`.
4. `docs/guides/uat-specs/panel.toml` (section `panel`, `requires = ["docker", "network"]`): rows `login`, `refresh-rotates`, `refresh-replay-refused` (DV-3), `logout-clears`, `dashboard-guard`.
5. S-L9 green. Tests: `UiStorageTests` (hash prefix only), `RouteGuardTests` (an external URL is aborted), `FlowDockerTests` (`RequiresDocker`, `RequiresNetwork`).

**Tests and why they fail when reverted:** S-L9 is the cross-component DV-3 test; `UiResult_NeverCarriesToken`.

**Checks**

```powershell
dotnet restore <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj --force-evaluate
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L9"   # Docker running, network
```

**Docs owed:** README section "Panel flows".

### L36. Notifications, Discord, webhook sink

- **Branch / worktree / commit:** `ml/l36-notify-discord` / `meridian-lab-l36` / `feat(lab): notifications, discord and a webhook sink`
- **Depends on:** L22, L12. **Security review:** yes (admin API keys, webhooks).
- **Rules:**
  - R-HOST: the services' webhook and email targets point only at `meridian-lab-sink` through proxies 20052 and 20061; if a service validates webhook hosts against `discord.com` (read the code), the rows that need delivery become catalog rows asserting the refusal and a known issue, not a product change.
  - R-ZERO / R-LOG: `X-Admin-Api-Key` values come from the vault and are redacted; the sink stores bodies redacted and drops `Authorization` headers.
  - D-ML40 not ruled → no SMTP switch: email rows assert the designed failure with `known_issue = "KI-NOTIF-OUTBOX"` or the Graph provider's refusal; D-ML41 recommended out → the Discord bot stays absent, and Discord's `/readyz` 503 is the asserted state (F26).
  - R-RESULT, R-DOCKER, R-NULL, R-XUNIT.

**Contract excerpt:** C10 rows `notifications`, `discord`, `sink` and their proxies; C4 `http_request via=sink`; F25-F27.

**Files**

1. Probe mode `sink` (ASP.NET minimal app in the probe): `POST /{**path}` records `{at, path, headers (minus Authorization), body (redacted, max 64 KiB)}` in memory (last 500); `GET /_sink/requests?since=` returns them; `DELETE /_sink/requests` clears. Image `meridian-lab/probe:<build_id>` runs it as `meridian-lab-sink`.
2. `Services/NotificationsLabAdapter.cs`, `Services/DiscordLabAdapter.cs`: env through their proxies; `RabbitMq__Host` explicit (F27); Discord `Discord__WebhookUrl=http://meridian-lab-toxiproxy:20061/webhook`, `SecretsService__Url` unset or a lab value so the `:5080` fallback is visible (KI-DISCORD-SECRETS-PORT); admin API keys generated into the vault.
3. Gateway clusters `notifications` and `discord` through 20014 and 20015.
4. `stack_reset_data`, `db_query`, `ProxyCatalog`, `LabTargetRegistry`, `I2OutboxInvariant` (Notifications' outbox, if the tables exist; else the I2 check reports `InvariantCheckError` "outbox tables missing", which is KI-NOTIF-OUTBOX).
5. Tests: `SinkTests` (Authorization dropped; redaction), adapter env tests, `NotifyDockerTests` (`RequiresDocker`): both services start; a Discord webhook test call reaches the sink, or the refusal is recorded.

**Tests and why they fail when reverted:** `Sink_DropsAuthorization`; the Docker test proves the wiring.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\src\Tools\Dhadgar.Lab.Probe\Dhadgar.Lab.Probe.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~NotifyDockerTests"   # Docker running
```

**Docs owed:** README "Services": Notifications, Discord, sink.

### L37. CLI in a Linux member

- **Branch / worktree / commit:** `ml/l37-cli` / `meridian-lab-l37` / `feat(lab): run the cli inside a linux pool member`
- **Depends on:** L30, L34. **Security review:** yes (CLI credentials).
- **Rules:**
  - The CLI never runs on the host (F30): `cli_run` refuses `m:host` and any non-Linux member.
  - R-ARGS: `docker exec -e HOME=/home/lab <id> /opt/lab/cli/dhadgar <args...>`, each arg its own item; the first arg must be in the C10 verb allowlist; the packet reads `src/Dhadgar.Cli` and writes the exact safe subcommand list into C10 (no subcommand that prints a secret).
  - R-LOG: stdout and stderr are redacted with the vault before they return.
  - R-RESULT, R-NULL, R-XUNIT.

**Contract excerpt:** C4 `cli_run`; C10 CLI allowlist; F30.

**Files**

1. `lab_build` project `cli`: portable publish into `LabPaths.Builds\<build_id>\cli\`; `pool_acquire` for a Linux member copies it to `/opt/lab/cli/` when the build has a CLI.
2. `Tools/Cli/CliRunTool.cs`; the CLI's config in the member at `/home/lab/.dhadgar/config.json` written by the lab (Gateway URL `http://meridian-lab-gateway:8080`, client id and secret for a lab client created through Identity's admin path or seeded by the Identity adapter; the coder records which).
3. `docs/guides/uat-specs/cli.toml` (section `cli`, `requires_pool = { os = "linux", kind = "container" }`): rows `version`, `auth-login` (client_credentials through the Gateway), `nodes-list` (after an enrollment), `notifications-admin` (direct with the admin key), `gateway-down` (catalog: disable `gateway-identity`; `auth login` exits non-zero with a readable error, no stack trace in stderr).
4. Tests: `CliAllowlistTests` (unknown verb refused; `m:host` refused; Windows member refused), `CliArgsTests`.

**Tests and why they fail when reverted:** `CliRun_Host_IsRefused` keeps the CLI off the workstation.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

**Docs owed:** README "Services": CLI.

### L38. Catalogs for Identity, BetterAuth, Panel, Notifications, Discord, CLI; coverage flips

- **Branch / worktree / commit:** `ml/l38-catalogs-auth` / `meridian-lab-l38` / `test(lab): expected-failure catalogs for auth, notifications, discord and cli`
- **Depends on:** L35, L36, L37. **Security review:** yes (driver tool list).
- **Rules:** as L23 (catalog rule, `ExpectationAssumed`, findings stay findings); D-ML18 forbidden set.

**Files**

1. `catalog/identity.toml`: `identity-redis-down-exchange` (exchange fails with the designed status, no 500; recovery), `identity-pg-down-refresh`, `identity-exchange-replay` (the same exchange token twice → refused), `identity-exchange-expired` (wait past 60 s → refused), `identity-rabbit-down` (refresh still works if the bus is not on the path).
2. `catalog/betterauth.toml`: `betterauth-pg-down-signin`, `betterauth-wrong-password` (401, no user enumeration difference between unknown user and wrong password: compare status and body shape), `betterauth-gateway-latency`.
3. `catalog/panel.toml`: `panel-identity-down-login` (the flow fails with a visible error, `ConsoleErrors` recorded, no token stored), `panel-refresh-identity-down` (the Panel keeps the old token or logs out; whichever the code designs).
4. `catalog/notifications.toml`, `catalog/discord.toml` (webhook sink down → retry or dead-letter per design; I5 `_error` queues allowed only where designed), `catalog/cli.toml` merged into L37's `cli.toml` if the coder prefers one file (state which).
5. `invariants/identity.toml`, `invariants/notifications.toml` (data checks the coder derives from the models: refresh tokens with no user, revoked-but-active sessions, and so on; each with a comment naming the rule).
6. `lab-coverage.json`: Identity, BetterAuth, Panel, SharedAuth, Notifications, Discord, CLI become `real`.
7. `FuzzTargets`: `identity_exchange`, `identity_refresh`, `notifications_admin`.
8. `.claude/agents/uat-driver.md` gains `ui_flow_list`, `ui_flow_run`, `cli_run`, `pool_list`, `pool_acquire`, `pool_release`, `member_reset`, the agent tools from L32 and the member fault tools; `DriverToolListTests` stays green.

**Tests and why they fail when reverted:** `LabCoverageTests` (no `real-pending` owned by waves 4 to 7), `ShippedSpecsValidateTests`, `DriverToolListTests`. Local evidence: one run of each catalog, grades in the report.

**Checks**

```powershell
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~ShippedSpecsValidateTests|FullyQualifiedName~LabCoverageTests|FullyQualifiedName~DriverToolListTests"
```

**Docs owed:** none (L45).

### L56. Tenant isolation twins; M2, M3, M7; S-L12 green

- **Branch / worktree / commit:** `ml/l56-tenant-twin` / `meridian-lab-l56` / `test(lab): tenant isolation twin experiments and tenant mutants`
- **Depends on:** L35, L53, L54, L11. **Security review:** yes (user tokens, a second tenant, product mutants).
- **Rules:**
  - Both tenants are lab-owned: organization ids `LabIds.OrganizationId` (A) and `LabIds.TenantBOrganizationId` (B), or organizations the lab created in its own Identity database; users are seeded lab users. Nothing reaches a real account.
  - R-ZERO / R-LOG: bearer tokens go to the vault as `brr-N` and are redacted everywhere; `identity_user_token` never returns the token.
  - Stop rule: if the Identity sign-in and exchange cannot produce a token with `org_id` for a lab user in the lab stack, stop after files 1 to 4 and 6, record the step that failed, and leave `tenant-isolation-http` and `identity_user_token` for a follow-up packet; S-L12 does not depend on them.
  - Mutant patches as in L54 (never commit a product change).
  - R-RESULT, R-SEED, R-NULL, R-XUNIT.

**Contract excerpt:** C19 twins (`isolated`, `pre`, `disjoint`, precondition rule) and mutants M2, M3, M7; C4 `nodes_token_mint` (`organization_id?`), `nodes_enroll_probe` (`hostname?`), `identity_user_token`, `twin_run`; C1 `LabIds`; README F38 and `KI-NODES-USER-AUTH`.

**Files** (under `src/Tools/Dhadgar.Lab/` unless named)

1. `Nodes/NodesLabAdapter.cs` and `Tools/Nodes/NodesTools.cs`: `nodes_token_mint` takes `organization_id?` per C4 and returns it; `stack_reset_data` re-seeds tenant B beside A where Nodes needs a seed row.
2. `docs/guides/uat-specs/catalog/twin-tenant.toml` (section `twin-tenant`): row `tenant-a-enroll` (mint for A; `nodes_enroll_probe via=gateway hostname=tenant-shared-01` → 201; clause: the response `organization_id` equals A; `db_query` of A's node names and ids, captured as `a_node_name`, `a_node_names`, `a_node_ids`); row `tenant-b-enroll` (the same for B with the same hostname; captures `b_node_ids`).
3. `docs/guides/uat-specs/twins/tenant-isolation-enroll.toml` (the flagship, S-L12): `relation = "isolated"`; `[base] rows = ["tenant-a-enroll"]`; `[variant] pre = { spec = "catalog/twin-tenant.toml", rows = ["tenant-b-enroll"] }`, `rows = ["tenant-a-enroll"]`; `[[compare]]` pointers `/rows/tenant-a-enroll/grade`, `/rows/tenant-a-enroll/steps/enroll/status`, `/rows/tenant-a-enroll/captures/a_node_name`, `/rows/tenant-a-enroll/captures/a_node_names`; `[[compare]] variant_pointers = ["/rows/tenant-a-enroll/captures/a_node_ids", "/rows/tenant-b-enroll/captures/b_node_ids"]`, `op = "disjoint"`. Today it holds: tenant A's node is named `tenant-shared-01` whether or not B used that hostname first (`GenerateUniqueNodeNameAsync` filters by organization).
4. `tools/lab/mutants/M7-node-name-cross-tenant/` (`mutant.toml`, `change.patch`: drop `n.OrganizationId == organizationId &&` from both queries in `EnrollmentService.GenerateUniqueNodeNameAsync`); detector `twin_run tenant-isolation-enroll` with `expect_pattern = "^twin:tenant-isolation-enroll\\|/rows/tenant-a-enroll/captures/a_node_name$"` (under M7, A's node becomes `tenant-shared-01-1` in the variant).
5. `Identity/IdentityUserTokenTool.cs` (`identity_user_token`): per tenant, a seeded lab user (credentials generated into the vault on first use); sign up or sign in through the Gateway's BetterAuth route, exchange through Identity for an access token carrying `org_id` (read the Panel flow from L35 and Identity's exchange endpoint for the exact calls); vault the token as `brr-N`; return `{bearer_ref, organization_id, user_id}`. `http_request` gains `bearer_ref?` (sets `Authorization: Bearer`, redacted in evidence).
6. `docs/guides/uat-specs/twins/tenant-isolation-http.toml` and rows in `twin-tenant.toml`: `tenant-a-list` (`identity_user_token tenant=a`; `GET /api/v1/organizations/{A}/nodes` via the Gateway → 200; capture `a_listed_ids`), `tenant-b-cross` (B's token on A's path → 403 or 404, never 200). `[[compare]]` on `a_listed_ids` and `disjoint` between A's listed ids and B's node ids. **Expected today: incomplete.** Nodes accepts only its mTLS scheme (README F38), so `tenant-a-list` fails with 401 and the twin precondition rule makes the experiment `held = null`. Seed `KI-NODES-USER-AUTH` in `known-issues.toml` (`patterns = ["^http:401\\|.*\\|GET\\|/api/v1/organizations/\\{id\\}/nodes"]`, `[issue.scope] services = ["gateway", "nodes"]`, sections `["twin-tenant"]`).
7. `tools/lab/mutants/M2-list-nodes-no-org/` and `M3-tenant-handler-always/` (patches per C19); detectors `twin_run tenant-isolation-http`. While F38 stands, `mutation_run` returns `unreachable` for both with reason "twin precondition: tenant-a-list FAIL" and known issue `KI-NODES-USER-AUTH`; gate MD accepts that only while the issue is open.
8. Tests: `NodesTokenMintTests` (`OrganizationId_Unknown_Refused`, tenant B accepted), `IdentityUserTokenToolTests` (fake HTTP: token vaulted, never returned; `tenant` other than `a`/`b` refused), `TwinTenantSpecsTests` (both experiments load), and S-L12 (`TenantTwinSeamTests`, from L53) goes green.

**Tests and why they fail when reverted:** S-L12 fails if tenant B's activity changes what tenant A sees in the enrollment path, or if the lab and Nodes disagree on tokens minted for a second organization. `mutation_run` M7 → `detected` proves S-L12 can fail.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L12|FullyQualifiedName~MutantPatchesApplyTests"   # Docker running
git -C <W> status --porcelain   # only tools/lab/, src/Tools/, tests/Dhadgar.Lab.Tests/ and docs/guides/uat-specs/ paths
```

Then `twin_run tenant-isolation-http` (expect `held = null`, reason recorded) and `mutation_run {mutants = ["M7-node-name-cross-tenant", "M2-list-nodes-no-org", "M3-tenant-handler-always"]}`; verdicts in the report.

**Docs owed:** none (L45).

---

## Wave 8: Sprint 2 flows, the fleet and agent-protocol fuzzing

Sprint 2 is on `main` (squash `6df3338`, PR #235, D-ML60), and `campaign/meridian-lab` is cut after it, so no merge is needed.

**Wave 8 gate (orchestrator):** before dispatching L40 to L44, the architect re-reads the Sprint 2 code on the campaign branch and writes a ruling note `worknotes/W8-sprint2-deltas.md` listing every name in L39 to L44 that differs from this plan (hub method names, config keys, the `GraceUntil` column, the MSI path); packets use the ruling's names. L39 needs only L12 and the ruling and may be dispatched as soon as both exist, in any earlier wave's slot.

### L39. Agent consumes `AgentWindowsFootprint`; S-L3 green

- **Branch / worktree / commit:** `ml/l39-footprint` / `meridian-lab-l39` / `refactor(agent-windows): take footprint names from Dhadgar.Contracts`
- **Depends on:** L12 and the W8 ruling note. **Security review:** yes (agent project).
- **Rules:** R-CONTRACT (names only from `AgentWindowsFootprint`; delete the local literals), R-NULL, R-XUNIT, R-CPM; no behaviour change.

**Contract excerpt:** C5. If Sprint 2's merged names differ from C5 (for example the state file name), C5 changes to match the agent and the report says so: the agent is the source of truth for values, Contracts for location.

**Files:** `src/Agents/Dhadgar.Agent.Windows/Program.cs` (`ServiceName` and `EventLogSourceName` become aliases of the Contracts constants), `Windows/WindowsCertificateStore.cs` (`CaStoreName`), `Installation/EnrollmentTokenCleanup.cs` (`EnrollmentTokenValueName`, the key path), the state file name in `AgentOptions` or its consumer (find each literal with Grep), the MSI's WiX constants if Sprint 2 shares them through a generated file (otherwise list them in the report as a follow-up). `AgentFootprintSeamTests` switches from reflection to direct comparison where the constant is now public.

**Tests:** S-L3 green; `tests/Dhadgar.Agent.Windows.Tests` stays green.

**Checks**

```powershell
dotnet build <W>\src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Agent.Windows.Tests\Dhadgar.Agent.Windows.Tests.csproj -c Release
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

**Docs owed:** none.

### L40. `GraceUntil` shift, hub events, `hub` and `agent_log` sources

- **Branch / worktree / commit:** `ml/l40-s2-evidence` / `meridian-lab-l40` / `feat(lab): grace, hub and agent log evidence for uat`
- **Depends on:** wave 8 gate, L32. **Security review:** yes (Nodes hub logging).
- **Rules:** R-LOG (hub log events carry `NodeId` and the connection id only, never certificates), R-CONTRACT, R-RESULT, R-TIME, R-NULL, R-XUNIT, R-DI.

**Contract excerpt:** C7 `hub` and `agent_log` rows; C6 `CertTimeField.GraceUntil`.

**Files**

1. `NodesLabAdapter.ShiftCertificateTimesAsync`: confirm L11's `GraceUntil` shift matches the column and the `IsUsable` rule named in the W8 ruling; change it only if the ruling lists a difference.
2. Nodes AgentHub (path from the W8 ruling): if `OnConnectedAsync` and `OnDisconnectedAsync` do not already log structured events with the node id, add `LoggerMessage` events `AgentHubConnected` (EventId 4101) and `AgentHubDisconnected` (4102) with `NodeId`; a Nodes test asserts both (Nodes test conventions).
3. `Uat/Eval/HubSource.cs` (filters Nodes container log lines by those EventIds and `NodeId`); `Uat/Eval/AgentLogSource.cs` (reads `logs_tail member:<id>` through the probe's `agent-log`, L32).
4. Tests: `HubSourceTests`, `AgentLogSourceTests` (fixture lines), adapter grace shift test (InMemory).

**Checks**

```powershell
dotnet build <W>\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj -c Release
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

**Docs owed:** Nodes README line for the two events.

### L41. Lab Gateway TLS and mTLS; trust inside members (D-ML14)

- **Branch / worktree / commit:** `ml/l41-lab-tls` / `meridian-lab-l41` / `feat(lab): lab gateway tls and member trust`
- **Depends on:** L40. **Security review:** yes (certificates, trust stores).
- **Rules:**
  - R-ZERO: the lab TLS CA key and server key; `CryptographicOperations.ZeroMemory` on exported key bytes.
  - R-PATH: cert files under `LabPaths.State\tls`.
  - D-ML14: trust is added only inside members, never on the host. Probe modes `trust-add` and `trust-remove` accept a certificate on stdin and refuse it unless its thumbprint equals the lab TLS CA or the lab Nodes CA thumbprint the lab passes as an argument and the probe re-computes; no tool adds trust on the host.
  - R-CONTRACT: Sprint 2's Gateway agent-ingress config keys and `AgentCertificateProfile` (names from the W8 ruling).
  - R-RESULT, R-USING, R-NULL, R-XUNIT.

**Files**

1. `Tls/LabTlsAuthority.cs`: creates a lab-only CA and a server certificate for `localhost`, `127.0.0.1`, `meridian-lab-gateway` and the agent ingress host, once per lab state; files in `LabPaths.State\tls`.
2. Gateway adapter: Kestrel HTTPS on container port 8443 (published to 15443) with the server certificate, and Sprint 2's agent-ingress mTLS settings; `agent-ingress-tls` proxy (25443) live; `svc_config_set` allowlist gains the Sprint 2 keys the specs need (from the W8 ruling).
3. `AgentInMember`: when TLS is on, the overlay points at `https://<AgentIngressHost>:25443`, and `agent_install` runs `trust-add` for the lab TLS CA inside the member (and the Nodes CA only if Sprint 2's agent trusts its own CA store instead; the ruling says which).
4. Tests: `TrustAddTests` (a non-lab thumbprint is refused by the probe; the lab passes only the two CA thumbprints), `LabTlsAuthorityTests` (SANs; key buffer zeroed), `TlsDockerTests` (`RequiresDocker`): `http_request via=agent_ingress` over TLS reaches `/api/v1/agents/enroll`; with `RequiresWindowsContainers`, an agent in a member enrolls over mTLS (S-L8 variant) and the host's Root store is unchanged.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\src\Tools\Dhadgar.Lab.Probe\Dhadgar.Lab.Probe.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
$env:MERIDIAN_LAB_WINDOWS_CONTAINERS = '1'; dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~TlsDockerTests|Seam=S-L8"
```

**Docs owed:** README section "TLS in the lab".

### L42. Fleet simulator on real `Agent.Core`; S-L10

- **Branch / worktree / commit:** `ml/l42-fleet` / `meridian-lab-l42` / `feat(lab): fleet of real agent-core instances against the lab`
- **Depends on:** L41, L27. **Security review:** yes (agent core, client keys).
- **Rules:**
  - R-CONTRACT: the fleet uses `Dhadgar.Agent.Core` through `AddAgentCore()` (F31); it never re-implements an enrollment, heartbeat or hub message. Replace only the F31 list per agent: `ICertificateStore` (an in-memory store in the lab, modelled on Sprint 2's test `InMemoryCertificateStore`), `IAgentStateStore`, `ISystemMetricsCollector`, `IHardwareInventoryCollector`, `IProcessManager` (refuses every start), `IHttpClientFactory` (handler with the agent's client certificate, base address the agent ingress).
  - R-SEED: per-agent `LabRandom(seed, "fleet:agent-<nnnn>")` drives hardware inventory values and jitter; secrets still come from `RandomNumberGenerator`.
  - R-ZERO: per-agent private keys live in the in-memory store and are zeroed at dispose.
  - R-TIME: `HeartbeatService` uses the real clock (F31); scenarios are wall-clock bounded.
  - R-RESULT, R-USING (`await using` each `FleetAgent`), R-DI, R-NULL, R-XUNIT.

**Contract excerpt:** C17 `FleetAgent`; C4 `fleet_run`; D-ML35; S-L10.

**Files**

1. `Fleet/FleetAgent.cs` per C17; `Fleet/InMemoryAgentServices.cs` (the fakes).
2. `Fleet/FleetJob.cs` + `fleet_run`: mints one token per agent, starts agents at a seeded ramp (max 20 per second), scenarios `enroll_heartbeat` (all enroll, heartbeat until the end), `churn` (agents stop and start at seeded times; a restarted agent reuses its state and does not re-enroll), `reconnect_storm` (a seeded `fault_proxy_disable agent-ingress` for 10 to 30 s, then every agent reconnects). Metrics per second: enrolled, connected, heartbeat errors, p50 and p99 enroll latency; the job bundle carries `fleet.json` and invariant checkpoints.
3. `docs/guides/uat-specs/invariants/fleet.toml`: online node count equals connected agents within 60 s of the end; no node with two active certificates.
4. `tests/Dhadgar.Lab.Tests/Seams/FleetSeamTests.cs` (S-L10, `RequiresDocker`): 20 agents enroll and heartbeat; Nodes shows 20 online nodes within 120 s.
5. Tests: `FleetAgentTests` (two agents in one process do not share state), `FleetScheduleTests` (same seed → same ramp and churn times).

**Tests and why they fail when reverted:** S-L10 goes red if the fleet and Nodes disagree on any protocol step; `FleetAgents_DoNotShareState`.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L10"   # Docker running
```

**Docs owed:** README section "Fleet".

### L43. Sprint 2 specs in containers: console, service, MSI, renewal and grace, GameServerWrapper

- **Branch / worktree / commit:** `ml/l43-agent-specs` / `meridian-lab-l43` / `test(lab): sprint 2 agent flows as specs in windows members`
- **Depends on:** L41, L33. **Security review:** yes (MSI, agent install).
- **Rules:**
  - C7 format; real table and column names from the merged Nodes model; SQL params bound; every row has a required non-human clause; catalog rule for failure rows.
  - The MSI is built with Sprint 2's build arguments from its packet (locked RID-less restore, then `--no-restore`; never a bare `publish -r`, D-S241); `agent_msi_install` copies it into the member and sends the token on stdin to the probe mode `msi-install`, which runs `msiexec /i <msi> /qn /l*v C:\lab\msi\install.log <TOKEN property>=<token>` with `ArgumentList`. The token is on msiexec's command line only inside the member, because that is how Sprint 2's MSI takes it; it never reaches a host argument or log. It is a `Hidden` property (D-S245), and the lab runs `SecretScanner` over `install.log` before the log enters the bundle; a hit fails the row.
  - Legs of Sprint 2's UAT scripts that need a reboot or the host are listed as not covered (D-ML31), never faked.
  - R-ARGS, R-ZERO, R-RESULT, R-NULL, R-XUNIT.

**Files**

1. `Tools/Agents/AgentMsiTools.cs`: `agent_msi_install`, `agent_msi_uninstall`; `lab_build` project `agent_msi` (Windows only) per Sprint 2's MSI build.
2. `docs/guides/uat-specs/agent-console.toml`, `agent-service.toml`, `agent-msi.toml` (each `requires_pool = { os = "windows", kind = "container" }`), rows: `enroll`, `heartbeat` (db last heartbeat within 120 s; `hub connected` for the node), `restart-no-reenroll`, `no-ca-in-root` (member), `renew-within-grace` (`agent_config_set Agent:Security:CertificateRenewalThresholdDays 90`, restart; a new certificate row; the old one revoked with `GraceUntil` in the future; hub reconnects), `after-grace` (`nodes_cert_time_shift fields=["grace_until"] shift_minutes=-120` on the old certificate; a request with the old certificate through `agent_ingress` is refused; the agent stays connected), and for MSI: `install-footprint`, `uninstall-clean` (footprint empty in the member), `msi-log-no-token`.
3. `docs/guides/uat-specs/gameserverwrapper.toml`: rows that start a wrapper-hosted dummy process through the agent's pipe (F36) inside the member, read `MeridianGS_<id>` service state, and kill the child to see the wrapper report it; the coder reads the wrapper and the agent's `AgentPipeServer` for the exact flow and lists any row that needs a real game server as not covered.
4. `docs/analysis/meridian-lab/worknotes/L43-sprint2-uat-map.md`: every Sprint 2 UAT script leg, the row that covers it, or "not covered: <reason>".
5. `ShippedSpecsValidateTests` covers the new files.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~ShippedSpecsValidateTests|FullyQualifiedName~AgentMsiArgsTests"
```

Then one run of each spec in Windows members; grades in the report.

**Docs owed:** the worknote in file 4.

### L44. Hub and `CommandEnvelope` fuzzing; fleet chaos catalog

- **Branch / worktree / commit:** `ml/l44-agent-fuzz` / `meridian-lab-l44` / `feat(lab): fuzz the agent hub and command envelopes; fleet chaos`
- **Depends on:** L42, L28, L29. **Security review:** yes (`CommandEnvelope`, hub).
- **Rules:**
  - `CommandEnvelope` fuzzing runs in-process against `CommandValidator` (and `CommandDispatcher` with fake handlers that record and refuse); the oracle: no unhandled exception, every invalid envelope rejected with a `Result` failure, never dispatched; issue #94 (no signature check) is KI-94, so unsigned envelopes that pass are known, not new.
  - Hub fuzzing goes through a fleet agent's authenticated connection with mutated `AgentHeartbeat` payloads; oracle: the hub never crashes the Nodes process (I1, I6), rejects malformed payloads, and other fleet agents stay connected.
  - R-SEED, R-CONTRACT (types from Contracts and `Agent.Core`), R-RESULT, R-NULL, R-XUNIT.

**Files**

1. `FuzzTargets`: `hub_heartbeat`, `command_envelope` (C10).
2. `Fuzz/CommandEnvelopeFuzzer.cs`, `Fuzz/HubFuzzer.cs`.
3. `docs/guides/uat-specs/chaos/fleet-storm.toml`: 100 agents, `agent-ingress` latency, resets and disables, Nodes kill and Postgres latency in the pool.
4. Tests: `CommandEnvelopeFuzzerTests` (a planted validator that accepts an expired envelope is found and replays by index), `HubFuzzerTests` with a fake hub.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

**Docs owed:** none (L45).

---

## Wave 9: adversaries, envelopes, continuity and time

The adversarial peer (D-ML52), recovery envelopes (D-ML61), gameplay continuity with a synthetic game (D-ML56 pending), baselines and mixed-version pairs (D-ML60), time rows (resurrection, interrupted migration, rollback) and guided mode (D-ML57). L57, L58, L60 and L62 can start in parallel once their dependencies are merged; L59 needs L43; L61 needs L60. Every packet gets a `security-advisor` pass. Base filter `F`. Clock-skew and reboot cases stay in the VM tier (D-ML31); a DB-restore resurrection needs a snapshot tool and is deferred (README scope).

### L57. Recovery envelopes

- **Branch / worktree / commit:** `ml/l57-envelope` / `meridian-lab-l57` / `feat(lab): recovery envelopes over fault duration and load`
- **Depends on:** L42, L29, L21. **Security review:** yes (injects faults in a loop).
- **Rules:**
  - R-BLAST: the envelope's `fault` is validated exactly like a chaos plan step (C4 fault tools only, targets through `FaultTargetGuard`); `heal_after_s` is always set; the job heals at the end of each cell and on cancel or failure (in `finally`, through the same `FaultBook` heal path the lease-end hook uses, L19).
  - R-SEED: cell order from `LabRandom(seed, "envelope:<id>")`; the fleet workload uses its own L42 streams.
  - R-OBSERVE: success ratios come from the workload's observations (fleet per-second metrics, or row grades for `spec` workloads), never from the proxy that carries the fault.
  - R-TIME: windows and limits measured with the injected `TimeProvider`; tests use `FakeTimeProvider`.
  - R-PATH: envelope files only under `docs/guides/uat-specs/envelopes/`, name `^[a-z0-9-]{1,64}$`.
  - R-RESULT, R-NULL, R-XUNIT.

**Contract excerpt:** C20 "Recovery envelopes" in full; C4 `envelope_run`; C12 kind `env`; C11 (invariants clean is part of `healthy`); D-ML61.

**Files** (under `src/Tools/Dhadgar.Lab/` unless named)

1. `Envelopes/EnvelopeSpec.cs` and loader (Tomlyn; unknown keys refused; axes non-empty, `duration_s` 5..600, `load` 1..1000, `success_ratio` 0.5..1.0, `metastable_floor_s` ≥ `recovered_s`).
2. `Envelopes/RecoveryClock.cs`: `sealed class RecoveryClock(TimeProvider time, double successRatio, TimeSpan window)` with `void Record(DateTimeOffset at, bool ok)`, `void MarkInvariantViolation(DateTimeOffset at)` and `TimeSpan? RecoveredAfter(DateTimeOffset healedAt)`: the start of the first full window after `healedAt` whose ratio ≥ `successRatio` and with no invariant violation inside it.
3. `Envelopes/EnvelopeGrader.cs`: `static CellGrade Grade(EnvelopeSpec spec, int durationS, bool warmupHealthy, bool injected, bool healed, TimeSpan? recoveredAfter)` per the C20 table; limit = `max(metastable_floor_s, 3 × duration)`.
4. `Envelopes/EnvelopeJob.cs` (job kind `envelope`): per cell as C20 says; writes the synthetic rows `envelope/<id>/<cell>` into the job bundle, `envelope.json` (`[{cell, duration_s, load, grade, recovered_after_s, reason}]`) and `envelope.md` (a duration × load grid of grades); ingests (L25) so `env` signatures land; the 4 h budget marks the rest `invalid` with reason `job budget`.
5. `Tools/Envelopes/EnvelopeRunTool.cs` (`envelope_run`, OwnLease; `cells` filters by id, unknown ids refused).
6. `docs/guides/uat-specs/envelopes/nodes-postgres-outage.toml`, `ingress-storm.toml`, `nodes-kill.toml` (C20).
7. Tests: `EnvelopeSpecTests`, `RecoveryClockTests` (`RatioReachedAfterHeal_RecoveredAtWindowStart`, `InvariantViolationInWindow_NotRecovered`), `EnvelopeGradeTests` (`NeverHealthyAfterHeal_IsMetastable`, `WarmupUnhealthy_IsInvalidNotPass`, `HealFailed_IsInvalid`, `SlowButRecovered_PassWithReason_NoSignature`), `EnvelopeScheduleTests` (same seed → same cell order), `EnvelopeJobTests` with fakes (cancel mid-cell → the cell's fault healed, remaining cells `invalid`), `EnvelopeDockerTests` (`RequiresDocker`, 20-minute timeout: `nodes-postgres-outage` with `cells = ["d10-l10"]` grades `recovered` or `slow`).

**Tests and why they fail when reverted:** `WarmupUnhealthy_IsInvalidNotPass` fails if a stack that was never healthy counts as "recovered" (the same defect class as an all-skipped run reading `pass`). `NeverHealthyAfterHeal_IsMetastable` fails if the grader treats "fault healed" as "service recovered". `InvariantViolationInWindow_NotRecovered` fails if a window full of 5xx from a crash loop still counts as healthy because the workload retried.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~EnvelopeDockerTests"   # Docker running
```

Then, with the daemon and the lease, one full `envelope_run nodes-postgres-outage`; attach `envelope.md` to the report. A `metastable` cell is a finding for the orchestrator, not a reason to change the thresholds (D-ML61 is the owner's).

**Docs owed:** none (L45).

### L58. Adversarial peer; `nodes_decommission`; M6; S-L13

- **Branch / worktree / commit:** `ml/l58-peer` / `meridian-lab-l58` / `feat(lab): adversarial peer against lab-enrolled identities`
- **Depends on:** L41, L42, L54. **Security review:** yes (attack traffic, client certificates and keys, a Nodes adapter path that decommissions nodes).
- **Rules:**
  - D-ML52 scope: a peer uses only identities enrolled by `nodes_enroll_probe` in this run (their `key_ref`/`cert_ref` in the vault) and connects only to `127.0.0.1:25000`, `127.0.0.1:25443` or `127.0.0.1:15040`. `PeerTargetGuard.Check(Uri)` refuses any other host or port with `[Lab.BlastRadius]` before a socket opens; the peer never takes a URL from a tool argument.
  - `nodes_decommission` accepts only a node id that this lease enrolled (the lease's enrolled-node set, filled by `nodes_enroll_probe`); any other id is `[Lab.BlastRadius]`.
  - R-ZERO: private key bytes, PKCS#12 exports and their passwords are zeroed with `CryptographicOperations.ZeroMemory` after the `X509Certificate2` is built; certificates are disposed (`using`).
  - R-CONTRACT: hub path, header and version from `AgentHubProtocol`; hub method names from `IAgentHub`; request and response types from `Dhadgar.Contracts.Nodes` (`EnrollNodeRequest`, `RenewCertificateRequest`); no local copies.
  - R-HOST: SignalR client and `HttpClient` base addresses come only from `PeerTargetGuard`-approved URIs; certificate validation pins the lab TLS CA (L41), never `DangerousAcceptAnyServerCertificateValidator`.
  - R-CPM: `Microsoft.AspNetCore.SignalR.Client` in `Dhadgar.Lab.csproj` with no version (it is already in `Directory.Packages.props` if Sprint 2's agent uses it; otherwise add it there after checking nuget.org for the latest stable, and say so in the report).
  - R-RESULT, R-USING, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C19 "Adversarial peer" in full (table, peer identities, row JSON) and mutant M6; C4 `peer_run`, `nodes_decommission`, `nodes_enroll_probe` (`cert_ref`), `nodes_cert_time_shift`; C6 `DecommissionNodeAsync`; C12 kind `peer`; D-ML52, D-ML53, D-ML54.

**Files** (under `src/Tools/Dhadgar.Lab/` unless named)

1. `tests/Dhadgar.Lab.Tests/Seams/PeerSeamTests.cs` (S-L13, `RequiresDocker`, `[Trait("Seam","S-L13")]`), written first: `peer_run {attack = "all", seed = 31}`; every attack with `required = true` has `held = true`, and the job's signatures contain no `peer:` entry. Run it before the rest and record the red line (`[Lab.NotFound] peer_run`).
2. `Peer/PeerTargetGuard.cs`; `Peer/PeerIdentity.cs` (`static Result<PeerIdentity> Load(SecretVault vault, string keyRef, string certRef)`: `X509Certificate2.CreateFromPem`, then on Windows `X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pkcs12, pwd), pwd)` with a random `pwd`; zero the export and the password; `IDisposable`).
3. `Peer/Attacks/*.cs`, one `IPeerAttack` per C19 row (`string Id`, `bool Required`, `Task<Result<PeerAttackResult>> RunAsync(PeerContext ctx, CancellationToken ct)`; `PeerAttackResult(string Attack, bool Required, string Expected, string Observed, bool? Held)`). `post-revocation` runs two variants (`heartbeating`, `idle`); `stale-cert-after-renewal` runs the inside-grace leg, calls `nodes_cert_time_shift fields=["grace_until"] shift_minutes=-120` on the old row, then the after-grace leg; `clone` and idle `post-revocation` set `Required = false` and `Held = null`.
4. `Peer/PeerRunJob.cs` (job kind `peer`) and `Tools/Peer/PeerRunTool.cs` (`peer_run`): per attack, enroll fresh identities with `nodes_enroll_probe`, run, write the row JSON, emit `peer:<attack>|<variant>` signatures only for required attacks that did not hold.
5. `Services/NodesLabAdapter.cs`: `DecommissionNodeAsync(Guid nodeId, CancellationToken ct)` per C6: build a scope from the adapter's own service provider and resolve `INodeService` (as L11 does for `IEnrollmentTokenService`; `NodeService` takes `NodesDbContext`, `IPublishEndpoint`, `IAuditService`, `TimeProvider`, `IOptions<NodesOptions>`, `ILogger<NodeService>`), call the product's `DecommissionNodeAsync` and map its result to `Result<NodeStatus>`. `Tools/Nodes/NodesDecommissionTool.cs` (`nodes_decommission`, C4).
6. `Tools/Nodes/NodesEnrollProbeTool.cs`: vault the issued certificate PEM as `crt-N`; return `cert_ref`; record the node id in the lease's enrolled-node set.
7. `tools/lab/mutants/M6-renew-no-supersede/` (`mutant.toml`, `change.patch` made as L54 says; detector `peer_run {attack = "stale-cert-after-renewal"}`, `expect_pattern = "^peer:stale-cert-after-renewal\\|"`).
8. Tests: `PeerTargetGuardTests` (`127.0.0.1:15040` allowed; `127.0.0.1:5040`, `localhost:25000` by name resolution to another host, and any non-loopback refused), `PeerIdentityTests` (export buffer zeroed), `PeerRunJobTests` with fake attacks (`Peer_NonRequired_NoSignature`, `Peer_RequiredNotHeld_OneSignature`), `NodesDecommissionToolTests` (an id not enrolled in this lease → `[Lab.BlastRadius]`), adapter test (InMemory: decommission → every certificate revoked, `GraceUntil` null).

**Tests and why they fail when reverted:** S-L13 is red while any required attack gets through or while the peer and Nodes disagree on hub path, header or DTOs. `Peer_NonRequired_NoSignature` fails if an observed-only behaviour (D-ML53, D-ML54 unruled) is reported as a regression. `NodesDecommissionToolTests` fails if the lab can decommission a node it did not create. `mutation_run M6` → `detected` proves the after-grace leg can see a renewal that no longer supersedes.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L13"   # Docker running; green at the end
git -C <W> status --porcelain   # only tools/lab/, src/Tools/ and tests/Dhadgar.Lab.Tests/ paths
```

Then, with the daemon and the lease, `mutation_run {mutants = ["M6-renew-no-supersede"]}`; record the verdict and every attack's `observed` (clone and idle post-revocation feed D-ML53 and D-ML54).

**Docs owed:** none (L45).

### L59. Gameplay continuity with a synthetic game

- **Branch / worktree / commit:** `ml/l59-continuity` / `meridian-lab-l59` / `test(lab): synthetic game and gameplay continuity rows`
- **Depends on:** L43, L33. **Security review:** yes (runs a process under the agent's process manager and wrapper in members).
- **Rules:**
  - Lab isolation: `src/Tools/Dhadgar.Lab.SyntheticGame` is a console project referenced by nothing; add it to the lab project set in `LabIsolationTests` and to the solution's `Tools` folder. It binds `127.0.0.1` only.
  - R-PATH: `--state` must be under `C:\lab\game\` inside the member; the program refuses anything else.
  - R-ARGS / R-USING: the probe starts nothing; the agent starts the game. The probe's `game-tick` only connects and reads.
  - Continuity rows are `required = false` until D-ML56 is ruled; every row still records `observed`.
  - The agent has no command sender (F46): the game is started through the agent's own path that exists today (the coder reads `AgentPipeServer`, the wrapper and `IProcessManager` callers and uses the one L43's `gameserverwrapper.toml` uses). End-to-end start by a control-plane command is "not covered: no command sender (F46)" in the worknote.
  - R-RESULT, R-NULL, R-XUNIT.

**Contract excerpt:** C20 "Synthetic game"; C7 `game` source; F44 (`KillOnJobClose`), F45 (wrapper pipe never reconnects); C4 `fault_proxy_disable`, `agent_kill`, `agent_stop`, `agent_start`; D-ML56.

**Files**

1. `src/Tools/Dhadgar.Lab.SyntheticGame/Dhadgar.Lab.SyntheticGame.csproj` (console, `net10.0`, no package versions) and `Program.cs` per C20 (counter, state file written atomically via temp file and rename, `TICK` listener, stdin `stop`).
2. `src/Tools/Dhadgar.Lab.Probe/Modes/GameTickMode.cs` (`game-tick`) and `Modes/KillAgentMode.cs` (`kill-agent`: finds the agent process by the exe name in `AgentWindowsFootprint`, `Process.Kill(entireProcessTree: false)` on that one process, prints `{pid}`; refuses when zero or more than one process matches); `lab_build` publishes the game with the probe for Windows members. `Tools/Agents/AgentKillTool.cs` (`agent_kill`, C4): resolves `member` through `FaultTargetGuard`, runs the probe mode; added to `BlastRadiusTests`.
3. `src/Tools/Dhadgar.Lab/Uat/Eval/GameSource.cs` (C7 `game`).
4. `docs/guides/uat-specs/continuity.toml` (`requires_pool = { os = "windows", kind = "container" }`), rows in this order: `cp-disconnect` (start the game; `fault_proxy_disable p:agent-ingress heal_after_s=120`; during and after: `alive`, `counter` strictly increasing, `restarts = 0`; after heal: hub reconnected within 120 s), `agent-restart` (`agent_stop`, wait 30 s, `agent_start`: game alive? recorded), `agent-crash` (`agent_kill member`: under the process-manager path the game is expected to die with the agent's job object, F44; under the wrapper path it is expected to live and the wrapper's pipe is not re-attached, F45; both recorded).
5. `docs/analysis/meridian-lab/worknotes/L59-continuity.md`: per row and path the observed behaviour, and the `cp-disconnect` result first; it is the evidence for D-ML56.
6. Tests: `SyntheticGameTests` (counter increases; `TICK` answer; `stop` exits 0; `--state` outside the root refused), `GameSourceTests` (fixture JSON), `ShippedSpecsValidateTests` covers `continuity.toml`, `LabIsolationTests` passes with the new project.

**Tests and why they fail when reverted:** `SyntheticGame_StateOutsideRoot_Refused` fails if the game writes wherever it is told. `LabIsolationTests` fails if a product project references the game.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab.SyntheticGame\Dhadgar.Lab.SyntheticGame.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\src\Tools\Dhadgar.Lab.Probe\Dhadgar.Lab.Probe.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

Then one `uat_run continuity.toml` in a Windows member; grades and `observed` in the report and the worknote.

**Docs owed:** the worknote in file 5.

### L60. Baselines and mixed-version pairs; S-L14

- **Branch / worktree / commit:** `ml/l60-versions` / `meridian-lab-l60` / `feat(lab): baseline builds and mixed-version runs`
- **Depends on:** L54, L43. **Security review:** yes (`git` processes, builds from archived trees).
- **Rules:**
  - Reuse L54's `MutantBuilder` archive path with no patch (`ArchiveBuilder` extracted from it if the coder needs to; the mutant path keeps its tests green). `git -C <RepoRoot> archive --format=tar <commit>`; the commit must be a full 40-hex SHA from `tools/lab/baselines.toml` and `git -C <RepoRoot> cat-file -e <commit>^{commit}` must succeed, else `[Lab.InvalidArgument] unknown baseline commit`.
  - R-ARGS, R-PATH (only `LabPaths.Builds\baseline-<name>-<build8>\`; tar entries checked as in L54), R-USING, R-DOCKER (baseline images labelled `meridian-lab-baseline=<name>`; `version_run` always restores head builds in `finally`).
  - A baseline build uses the archived tree's own `Directory.Packages.props` and lock files (locked RID-less restore, then `--no-restore`, D-S241); it never mixes head packages into an old tree.
  - R-RESULT, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C20 "Baselines and version pairs" in full (including `prepare` and `kill_on_log`, which L61 uses); C4 `lab_build {baseline}`, `version_run`, `agent_install {build_id}`; C18 fingerprint `Baseline`; C12 kind `ver`; D-ML60.

**Files** (under `src/Tools/Dhadgar.Lab/` unless named)

1. `tests/Dhadgar.Lab.Tests/Seams/MixedVersionSeamTests.cs` (S-L14, `RequiresDocker` and `RequiresWindowsContainers`, `[Trait("Seam","S-L14")]`), written first: `version_run {pair = "old-agent-new-nodes"}`; the run's fingerprint shows the agent's image commit = `baseline.sprint2` and Nodes' = HEAD, and every row is PASS. Red now (`[Lab.NotFound] version_run`).
2. `tools/lab/baselines.toml` (C20) and `Versions/BaselineCatalog.cs` (loader; names `^[a-z0-9-]{1,32}$`).
3. `Build/LabBuildTool.cs`: the `baseline` argument per C4.
4. `Versions/VersionPairSpec.cs` and loader (C20 keys; every `builds` value `head` or `baseline:<known name>`).
5. `Versions/VersionRunJob.cs` (job kind `version`) and `Tools/Versions/VersionRunTool.cs` (`version_run`), with `prepare` and `kill_on_log` per C20.
6. `docs/guides/uat-specs/versions/old-agent-new-nodes.toml` (C20) and `new-agent-old-nodes.toml` (`builds = { nodes = "baseline:sprint2", gateway = "baseline:sprint2", agent = "head" }`, same rows). Note in each file: while `baseline.sprint2` equals `main`, the pair runs one version on both sides; it starts to bite when the protocol changes.
7. Tests: `BaselineCatalogTests` (short SHA refused), `VersionPairSpecTests`, `VersionRunJobTests` with fakes (`HeadRestoredAfterFailure`, `HeadRestoredAfterCancel`, `PrepareFailed_MainRowsSkipped`, `KillOnLog_NoMatch_RecordedNotFailed`, `RequiredRowFail_OneVerSignature`), `LabBuildToolTests` (exact `git` `ArgumentList` for a baseline).

**Tests and why they fail when reverted:** S-L14 is red while an old agent and the head Nodes disagree, or while the fingerprint does not say which build ran (a mixed-version pass that cannot prove it ran mixed is worthless). `HeadRestoredAfterFailure` fails if a baseline build outlives its job.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
$env:MERIDIAN_LAB_WINDOWS_CONTAINERS = '1'; dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam=S-L14"   # Docker running; green at the end
```

**Docs owed:** none (L45).

### L61. Time rows: resurrection, interrupted migration, rollback

- **Branch / worktree / commit:** `ml/l61-time` / `meridian-lab-l61` / `test(lab): resurrection, interrupted migration and rollback rows`
- **Depends on:** L60, L58. **Security review:** yes (decommissions lab nodes; kills Nodes during migration).
- **Rules:**
  - Only TOML and catalog changes plus the `seed-node` helper rows; no new tool. If a row needs a tool C4 does not have, stop and report (the orchestrator asks for a ruling).
  - The lab's Nodes runs with `ASPNETCORE_ENVIRONMENT=Development` (L09), so `AutoMigrateDatabaseAsync` applies migrations at start and logs `Applying migrations for NodesDbContext...` (`DatabaseMigrationExtensions.cs:39`); `kill_on_log` matches that line.
  - Assumption to verify in the packet: the `pre-sprint2` Nodes starts with the head `NodesLabAdapter` environment. If it does not, the rows that need it are SKIPPED with reason `baseline config: <key>` and the report names the key.
  - R-XUNIT (spec validation tests only).

**Contract excerpt:** C20 version pairs (`prepare`, `kill_on_log`); C4 `nodes_decommission`, `agent_stop`, `agent_start`; C7 `hub`, `agent_log`, `db`; D-ML58, D-ML59.

**Files**

1. `docs/guides/uat-specs/time.toml`: rows `seed-node` (mint, enroll via the Gateway, capture `node_id`), `enroll-after` (a fresh enroll returns 201), and `resurrection` (`requires_pool = { os = "windows", kind = "container" }`: an agent in a member enrolls and connects; `agent_stop`; `nodes_decommission node_id`; `agent_start` with the same state; required clauses: the node's certificates are revoked in `db` and no `hub` connected event for the node within 120 s; recorded: what `agent_log` shows the agent doing). `resurrection` is `required = false` until D-ML58.
2. `docs/guides/uat-specs/versions/interrupted-migration.toml`: `prepare = { builds = { nodes = "baseline:pre-sprint2" }, rows = ["seed-node"] }`; main `builds = { nodes = "head", gateway = "head" }` with `kill_on_log` on the migration line, `restart_after_s = 5`; rows `enroll-after` plus a `db` clause that `__EFMigrationsHistory` holds every migration in the head tree's `Data/Migrations/` (the coder lists them from the folder at HEAD into the row); `required = true`. The run records whether the kill landed (`matched`); a run where it did not is still graded, and the report says the kill missed.
3. `docs/guides/uat-specs/versions/rollback-old-nodes-new-schema.toml`: `prepare = { builds = { nodes = "head" }, rows = ["seed-node"] }`; main `builds = { nodes = "baseline:pre-sprint2", gateway = "head" }`; rows: Nodes healthy, `enroll-after`; `required = false` until D-ML59.
4. `ShippedSpecsValidateTests` covers the new files; `VersionPairSpecTests` gains the three files.

**Tests and why they fail when reverted:** `ShippedSpecsValidateTests` fails if a time row lacks a required non-human clause or names an unknown tool.

**Checks**

```powershell
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~ShippedSpecsValidateTests|FullyQualifiedName~VersionPairSpecTests"
```

Then, with the daemon and the lease, `version_run` for `interrupted-migration` and `rollback-old-nodes-new-schema`, and `uat_run time.toml rows=["resurrection"]` in a Windows member; grades, `matched` and the observed agent behaviour in the report (they feed D-ML58 and D-ML59).

**Docs owed:** none (L45).

### L62. Guided mode and its A/B

- **Branch / worktree / commit:** `ml/l62-guided` / `meridian-lab-l62` / `feat(lab): coverage-guided choices for the hunter and chaos, behind an a/b`
- **Depends on:** L52, L29, L27. **Security review:** yes (changes which faults the chaos scheduler picks).
- **Rules:**
  - D-ML57: guided ships with `tools/lab/guided.toml` `enabled = false`; `mode = "guided"` is refused with `[Lab.InvalidArgument] guided mode disabled` while it is false, except inside `guided_ab_run`. `random_share` is clamped to 0.3..1.0; the chooser draws "random" first, with `LabRandom`, so the share is honoured exactly per draw.
  - R-BLAST: guided changes only which choice is made from the plan's allowed set; it never adds a fault, target or duration the plan does not allow.
  - R-SEED: guided draws use the stream `guided:<job>`; the A/B uses paired seeds `seed + i` in both arms.
  - The flip of `enabled` is a commit only when the A/B shows a strictly higher `findings_per_100` for guided; a tie or a loss leaves it false and the worknote says so.
  - R-RESULT, R-NULL, R-XUNIT.

**Contract excerpt:** C20 "Guided mode"; C4 `guided_ab_run`, the `mode?` and `random_share?` arguments on `hunt_model_run` and `chaos_run`; C18 coverage keys; C15; D-ML57.

**Files** (under `src/Tools/Dhadgar.Lab/` unless named)

1. `tools/lab/guided.toml` (`schema = 1`, `enabled = false`, `random_share = 0.3`).
2. `Guided/ChoiceKeyMap.cs`: a static map from each hunter command and each chaos fault choice to the C18 coverage keys it can reach (written from L52's key list; a test fails if a key in the map is not a known key).
3. `Guided/ICoverageView.cs` (`bool IsHit(string key)`, implemented over L52's coverage store) and `Guided/GuidedChooser.cs`: `T Choose<T>(IReadOnlyList<T> options, Func<T,string> choiceId, ICoverageView coverage)`: with probability `random_share` a uniform choice, else weight `1 + 4 × unhit keys` from `ChoiceKeyMap`.
4. `Hunt/` and `Chaos/` call sites take `IChoiceStrategy` (random or guided); random stays the default and is byte-for-byte the old behaviour for a given seed.
5. `Guided/GuidedAbJob.cs` (job kind `ab`) and `Tools/Guided/GuidedAbRunTool.cs` (`guided_ab_run`): writes `ab.json` and `ab.md` per C20.
6. `docs/analysis/meridian-lab/worknotes/L62-guided-ab.md`: the A/B result and whether `enabled` flipped.
7. Tests: `GuidedChooserTests` (`RandomShare_AtLeast30Percent`: 10 000 draws with a fixed seed and an all-hit coverage view; the uniform share is within 0.30 ± 0.02), `GuidedDisabled_Refused`, `RandomStrategy_SameSeed_SameChoicesAsBefore` (a recorded choice sequence from L27/L29 fixtures), `AbJob_TieLeavesDisabled`, `ChoiceKeyMap_KeysAreKnown`.

**Tests and why they fail when reverted:** `RandomStrategy_SameSeed_SameChoicesAsBefore` fails if adding the strategy seam changed old replays (every earlier `repro.toml` would stop reproducing). `RandomShare_AtLeast30Percent` fails if guided can starve the random share. `AbJob_TieLeavesDisabled` fails if a non-win enables guided.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
```

Then, with the daemon and the lease, one `guided_ab_run {job = "hunt_model_run", args = {model = "enrollment", iterations = 25}, runs = 100}`; record it in the worknote; flip `enabled` in a separate commit only on a strict win.

**Docs owed:** the worknote in file 6.

---

## Wave 10: docs, live evidence, close-out

### L45. Docs: ADR, guides, catalog and triage guides, threat model (documentation-writer)

- **Agent:** `documentation-writer`. **Branch / worktree / commit:** `ml/l45-docs` / `meridian-lab-l45` / `docs(lab): meridian-lab adr, guides, catalogs, triage and threat model`
- **Depends on:** L44, L38, L55-L62. **Security review:** security-advisor read.
- **Rules:** tables with spaces around pipes; every code block names its language; Diátaxis placement per `docs/meta/`; no secrets or private IPs; check `docs/meta/doc-update-map.md`.

**Files**

1. `docs/adr/<next free number>-meridian-lab.md` from `docs/adr/TEMPLATE.md`: context (F1-F51), decision (D-ML1 to D-ML61, including the superseded D-ML12 and D-ML13, and each BlockedDecision as ruled or still open), consequences; update `docs/adr/README.md`.
2. `docs/guides/meridian-lab.md` (how-to): install, start and stop, leases, Docker contexts, the pool, `lab_doctor`, faults and their limits, jobs, troubleshooting.
3. `docs/guides/automated-uat.md` (reference): C7, C8, C9, C11 in prose and tables; how to write a row and a catalog row; the shipped specs table; history scenarios, twins, mutants and gates MD and LS, the peer, envelopes, version pairs and guided mode (C19, C20) with what each can and cannot prove.
4. `docs/guides/lab-triage.md` (how-to): reading `overall`, buckets, known issues, replaying a `repro.toml`, approving a draft (D-ML42 as ruled).
5. `docs/security/meridian-lab-threat-model.md`: assets, trust boundaries (Claude Code ↔ daemon, daemon ↔ Docker, lab ↔ dev stack, member ↔ host), each control and the test that pins it (BR, `LabIsolationTests`, `NoGitHubCallsTests`, `TokenNeverInArguments`, `WindowsPool_HostUnchanged`, `NoProductProjectReferencesMutants`, `PeerTargetGuardTests`, `NodesDecommissionToolTests`); "dev and lab only".
6. Links from `docs/guides/README.md`, `docs/testing/README.md`, `docs/security/README.md`.

**Checks:** links resolve (Grep each relative link target).

### L46. Live runs by the UAT runner agent

- **Agent:** the UAT runner (`uat-driver` through the `lab-uat` skill, D-ML44); not a packet-coder packet.
- **Steps:** `lab_build` the campaign tip; `uat_run` `lab-selftest`, every catalog, `panel`, `cli`, `agent-container-enroll`, `agent-console`, `agent-service`, `agent-msi`, `gameserverwrapper`; one `hunt_model_run` (25 iterations), one `fuzz_run all` (200 cases), one `chaos_run nodes-enroll` (1800 s), one `soak_run` of `lab-selftest` (3600 s), one `fleet_run enroll_heartbeat` (200 agents, 600 s); `history_run` `enroll-race` and `enroll-lost-response`; `twin_run` `enroll-latency-invariance` and `tenant-isolation-enroll`; `mutation_run` (all product mutants) and `selfcheck`; `envelope_run` for the three shipped envelopes; `peer_run all`; `version_run` `old-agent-new-nodes` and `new-agent-old-nodes`; `uat_run continuity.toml`; `coverage_report`. Copy each `run.json`, `ledger.md` and the triage ledger to `docs/analysis/meridian-lab/worknotes/uat-<run_id>/` (redacted files only).
- **Done when:** every `overall` is `pass` or `pass_with_known`, or each new failure has a draft and a follow-up packet or decision in the README; gate MD and gate LS hold; `mutants.md`, `selfcheck.md`, `envelope.md` and `coverage_report` output are copied to the worknotes.

### L47. Close-out

- **Agent:** `documentation-writer` for the docs, then `ops` for the board handoff. **Branch / worktree / commit:** `ml/l47-closeout` / `meridian-lab-l47` / `docs: close out meridian-lab campaign`
- **Depends on:** campaign review `ship`, L46. **Security review:** security-advisor read.
- **Files:** `docs/PROJECT-STATE.md` (the only packet that edits it): "meridian-lab: confirmed working" only for the parts with a green seam test (S-L1 to S-L14) and, for mutants and the self-check, gates MD and LS and an L46 ledger in `worknotes/`; "code exists" for the rest. README status table to Done; RESUME to final state; a note for the orchestrator listing which Sprint 2 UAT legs the lab now covers (from the L43 worknote; the orchestrator updates the Sprint 2 ledger, this packet does not).

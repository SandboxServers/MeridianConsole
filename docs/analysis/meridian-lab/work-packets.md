# meridian-lab: work packets

Ledger: [README.md](README.md). Resume card: [RESUME.md](RESUME.md).

## Dispatch rules

- **Campaign branch.** `campaign/meridian-lab`, cut from `main`, worktree `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab`. Nothing is pushed until the end (D-ML16).
- **Packet worktree.** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\meridian-lab-lNN` on branch `ml/lNN-<slug>`, cut from the campaign tip once the packet's dependencies are merged there. In this file `<W>` means that absolute worktree path.
- **Report.** `<W>\.claude\reports\meridian-lab\LNN-<role>.md`. Return one line: `<packet> <done|blocked|failed> <commit-sha|-> <report-path> <reason>`.
- **Commit.** One local commit with the packet's subject; the body ends with:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Wz3WiSWFFL9NvwfvRqhrt
```

- **Merge.** At `ship` (reviewer and security-advisor), a Sonnet `general-purpose` agent merges the packet branch into `campaign/meridian-lab` with `--no-ff` and reports the new tip. No push, no PR per packet or wave.
- **The one PR.** After the campaign review and L28, ops pushes `campaign/meridian-lab` once and opens one PR to `main` with the `security-review` label. The body ends with:

```text
🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_015Wz3WiSWFFL9NvwfvRqhrt
```

- **Security.** Every code packet gets a `security-advisor` pass beside the reviewer. Doc packets get a security-advisor read.
- **Red seams.** Seam tests carry `[Trait("Seam","S-Lx")]`. A packet's `dotnet test` filter excludes seams the README lists as still red; the packet that turns a seam green removes its exclusion.
- **Lock files.** `RestorePackagesWithLockFile=true`. A packet that adds a project or a package runs `dotnet restore <csproj> --force-evaluate` and commits every changed `packages.lock.json`.
- **Docker and elevation.** Docker tests need Docker Desktop running; they are excluded in CI. Tests tagged `RequiresElevation` run only with `MERIDIAN_LAB_ELEVATED_TESTS=1` in an elevated shell, which the orchestrator asks the user to do. A packet never elevates itself.
- **Stop rule.** If an API named here does not exist with the given shape (MCP SDK, Tomlyn, Nodes internals), adapt the call site only, keep every contract type in this file unchanged, and list the difference in the report. If the contract itself cannot hold, return `blocked spec`.

## Repo rules (short form; each packet repeats the ones it needs)

| ID | Rule |
| --- | --- |
| R-RESULT | Return `Result` or `Result<T>` (`Dhadgar.Shared.Results`) for validation, IO, network and process errors; error strings are `"[Lab.Code] message"`. Never throw for those. |
| R-PATH | Every path from config, tool arguments or specs goes through `IPathValidator.ValidatePath(path, allowedBasePaths)` (`Dhadgar.Agent.Core.Files`) against `LabPaths` roots. |
| R-ARGS | Processes use `ProcessStartInfo.ArgumentList` with `UseShellExecute = false`; never build an `Arguments` string. The only exception is the helper launch in L20. |
| R-HOST | Outbound HTTP goes only to `LabHosts.IsLabEndpoint(uri)` (loopback, lab ports); anything else is `Lab.Untrusted`. No redirects. |
| R-ZERO | `CryptographicOperations.ZeroMemory` on token, key and password buffers after use; secrets are held as `char[]`/`byte[]`, not `string`, inside the vault. |
| R-USING | `using` on every `IDisposable`: `Process`, `X509Store`, `X509Certificate2`, `RegistryKey`, `NpgsqlConnection`, pipes, `HttpResponseMessage`. |
| R-CPM | No `Version=` in any `.csproj`; versions only in `Directory.Packages.props`; commit lock-file changes. |
| R-NULL | Nullable on. Build with `-p:TreatWarningsAsErrors=true`. CI also runs `AnalysisMode AllEnabledByDefault`: fix CA warnings rather than suppressing them; justify any `#pragma` in the report. |
| R-XUNIT | xUnit v2: `[Fact]`/`[Theory]`, plain `Assert`, names `Subject_Condition_Expectation`. Windows tests: `[SkippableFact]` plus `Skip.IfNot(OperatingSystem.IsWindows())`. Docker: `[Trait("RequiresDocker","true")]`. Elevation: `[Trait("RequiresElevation","true")]` plus `Skip.IfNot(Environment.GetEnvironmentVariable("MERIDIAN_LAB_ELEVATED_TESTS") == "1")`. |
| R-LOG | Never log tool arguments, tokens, keys, passwords, certificates or connection strings. Anything that might carry a secret passes through `Redactor` first. |
| R-OS | Windows APIs sit behind `[SupportedOSPlatform("windows")]` and an `OperatingSystem.IsWindows()` check; on other OSes the tool returns `[Lab.Unavailable] ...`. |
| R-TIME | Inject `TimeProvider`; never `DateTime.UtcNow`/`DateTimeOffset.UtcNow` in logic. Tests use `FakeTimeProvider`. |
| R-CONTRACT | Cross-service types live only in `src/Shared/Dhadgar.Contracts`. The lab calls product types; it never copies them. |
| R-DI | `LabDaemonServiceLifetimeTests` builds the daemon's service provider with `ValidateScopes = true, ValidateOnBuild = true`; every packet that registers services keeps it green. |

## Contract

### C1. Paths, ports, environment

```csharp
namespace Dhadgar.Lab;

public static class LabDefaults
{
    public const int DaemonPort = 8789;
    public const string TokenEnvVar = "MERIDIAN_LAB_TOKEN";
    public const string HomeEnvVar = "MERIDIAN_LAB_HOME";          // overrides LabPaths.Root (tests)
    public const string ElevatedTestsEnvVar = "MERIDIAN_LAB_ELEVATED_TESTS";
    public const int MinTokenBytes = 32;
    public const string McpPath = "/mcp";
    public const string StatusPath = "/status";
}

public static class LabPorts
{
    public const int Gateway = 15000;
    public const int GatewayTls = 15443;   // L25
    public const int Nodes = 15040;
    public const int Postgres = 15432;
    public const int RabbitMq = 15673;
    public const int Redis = 16379;
    public static readonly IReadOnlySet<int> ServicePorts = new HashSet<int> { Gateway, GatewayTls, Nodes };
}

public static class LabIds
{
    public static readonly Guid OrganizationId = new("4d4c4142-0000-4000-8000-000000000001");
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
    public string State => Path.Combine(Root, "state");          // state/nodes-ca, ...
    public string PidFile => Path.Combine(Root, "daemon.pid");
    public string RepoRoot { get; }                              // Lab:RepoRoot, set by install.ps1
    // Machine (Windows): %ProgramFiles%\Meridian Lab\ (helper, agent copies), %ProgramData%\Meridian Lab\lab-machine.marker
    public static LabPaths Create(IConfiguration config);
}

public static class LabHosts
{
    // true only for http/https, host 127.0.0.1 | localhost | [::1], port in LabPorts.ServicePorts
    public static bool IsLabEndpoint(Uri uri);
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
    LeaseStatusDto Lease, ApprovalStatusDto Approval,
    string StackState,                       // "down" | "starting" | "up" | "degraded"
    IReadOnlyList<ProcessStatusDto> Processes);
public sealed record LeaseStatusDto(bool Held, string? Owner, string? Purpose, DateTimeOffset? Since, DateTimeOffset? ExpiresAt); // never the id
public sealed record ApprovalStatusDto(bool Active, DateTimeOffset? Until);
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
    // 3 Leased/Machine: args.lease_id must equal the current lease (Lab.LeaseRequired / Lab.LeaseHeld), then Touch
    //   Machine: also IMachineApproval.IsActive (Lab.ApprovalRequired) and the marker (Lab.MachineNotMarked)
    //   OwnLease: the tool manages the lease itself
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

Every LEASED and MACHINE tool takes a required `lease_id` string; it is not repeated below. Arguments and results are snake_case JSON.

| Tool | Gate | Arguments | Result | Packet |
| --- | --- | --- | --- | --- |
| `lab_status` | Open | - | `StatusDto` | L05 |
| `lab_doctor` | Open | - | `{checks:[{name, ok, detail}]}` | L12, L20 |
| `lab_lease_acquire` | Open | `owner`, `purpose`, `ttl_s?`, `force?`, `reason?` | `{lease_id, owner, purpose, since, expires_at}` | L05 |
| `lab_lease_renew` | Open | `lease_id`, `ttl_s?` | `LeaseInfo` | L05 |
| `lab_lease_release` | Open | `lease_id` | `{released:true}` | L05 |
| `lab_lease_status` | Open | - | `{current: LeaseInfo?, history:[...]}` | L05 |
| `logs_tail` | Open | `process` (`gateway`, `nodes`, `agent`, `daemon`), `since?` (ISO or `-5m`), `contains?`, `level?`, `category?`, `max_lines?` (200, max 2000) | `{lines:[{ts, level, category, event_id, message}]}` | L07 |
| `stack_status` | Open | - | `{state, containers:[{name, image, port, state}]}` | L08 |
| `db_query` | Open | `sql`, `params?` (object), `max_rows?` (100, max 500) | `{columns:[...], rows:[{...}], row_count, truncated}` | L10 |
| `certstore_list` | Open | `store` (`machine_my`, `machine_root`, `agent_ca`, `current_user_my`), `subject_contains?`, `issuer_contains?`, `issuer_thumbprint?` | `{certs:[{thumbprint, subject, issuer, serial, not_before, not_after, has_private_key}]}` | L12 |
| `registry_read` | Open | `key` (`agent`) | `{exists, values:[{name, kind, present, length}], acl:[{sid, rights}]}`; never a value | L12 |
| `state_file_read` | Open | `file` (`agent_state`) | `{exists, path, last_write, json}` (redacted) | L12 |
| `uat_list_specs` | Open | `specs_dir?` | `{specs:[{file, section, rows, sha256}]}` | L13 |
| `uat_plan` | Open | `specs` (file names), `sections?`, `rows?` | `{ok, rows:[{section, id, steps, clauses, depends_on}], errors:[{file, line, message}]}` | L13 |
| `uat_report` | Open | `run_id?` (default latest) | `{run_id, bundle, summary:{PASS, FAIL, ...}, rows:[{section, id, grade, reason}]}` | L16 |
| `bundle_read` | Open | `run_id`, `path` (relative, inside the bundle) | `{path, text}` | L16 |
| `lab_build` | Leased | `checkout` (repo root or under `<repo>\.claude\worktrees\`), `projects?` (`gateway`, `nodes`, `agent_windows`) | `{build_id, commit, dirty, path, projects, duration_ms}` | L09 |
| `stack_up` | Leased | `wipe?` | `stack_status` result | L08 |
| `stack_down` | Leased | `wipe?` | `stack_status` result | L08 |
| `svc_start`, `svc_stop`, `svc_restart` | Leased | `service` (`gateway`, `nodes`), `build_id?` (default latest) | `ProcessStatusDto` | L09 |
| `svc_config_set` | Leased | `service`, `key`, `value` (string or null to clear) | `{service, overlay:{...}}` | L09 |
| `http_request` | Leased | `via` (`gateway`, `nodes`), `method`, `path`, `headers?`, `body?` (JSON; `${secret:<ref>}` placeholders) | `{status, headers, body}` (redacted, body max 64 KiB) | L10 |
| `nodes_token_mint` | Leased | `label?`, `validity_minutes?` (60, max 1440) | `{token_ref, token_id, expires_at}` | L11 |
| `nodes_enroll_probe` | Leased | `token_ref`, `platform` (`windows`, `linux`), `via` (`gateway`, `nodes`) | `{status, node_id?, certificate_thumbprint?, error?}` | L11 |
| `nodes_cert_time_shift` | Leased | `node_id`, `shift_minutes` (-525600..0), `fields?` (`issued_at`, `not_before`, `not_after`, `grace_until`) | `{rows_updated}` | L11, L24 |
| `uat_run` | OwnLease | `specs`, `sections?`, `rows?`, `plan_only?`, `label?`, `lease_id?` | `{run_id, bundle, summary, rows:[{section, id, grade, reason}]}` | L15, L16 |
| `uat_attest` | Leased | `run_id`, `section`, `row`, `clause_id`, `verdict` (`held`, `not_held`), `note` | `{section, row, grade}` | L16 |
| `lab_machine_approval_request` | Leased | `minutes?` (60, max 120) | `ApprovalStatusDto` | L20 |
| `lab_machine_approval_revoke` | Leased | - | `ApprovalStatusDto` | L20 |
| `agent_config_set` | Leased | `key`, `value` (or null) | `{overlay:{...}}` | L21 |
| `agent_service_install` | Machine | `build_id?` | `{service, binary_path}` | L21 |
| `agent_service_uninstall` | Machine | - | `{removed}` | L21 |
| `agent_service_start`, `agent_service_stop` | Machine | - | `{state}` | L21 |
| `agent_console_start` | Machine | `build_id?` | `{pid}` | L21 |
| `agent_console_stop` | Machine | - | `{stopped}` | L21 |
| `agent_token_write` | Machine | `token_ref` | `{written:true}` | L21 |
| `agent_footprint_reset` | Machine | `confirm` (must equal `"reset-agent-footprint"`) | `{service_removed, certs_removed, key_removed, state_removed}` | L21 |

`svc_config_set` allowlist: `gateway` and `nodes`: `Logging:LogLevel:Default`, `Logging:LogLevel:Dhadgar`, `Logging:LogLevel:Microsoft.AspNetCore`; `nodes`: `Mtls:RenewalGraceMinutes`. L25 extends it. `agent_config_set` allowlist: `Logging:LogLevel:Default`, `Logging:LogLevel:Dhadgar`, `Agent:Security:CertificateRenewalThresholdDays`.

Error codes: `Lab.UnknownTool`, `Lab.Unclassified`, `Lab.InvalidArgument`, `Lab.LeaseRequired`, `Lab.LeaseHeld`, `Lab.ApprovalRequired`, `Lab.MachineNotMarked`, `Lab.Unavailable`, `Lab.NotFound`, `Lab.Timeout`, `Lab.ProcessFailed`, `Lab.Docker`, `Lab.Untrusted`, `Lab.ReadOnly`, `Lab.Helper`, `Lab.NotImplemented`, `Lab.Secret`.

### C5. Contracts additions (cross-boundary)

```csharp
// src/Shared/Dhadgar.Contracts/Agents/AgentWindowsFootprint.cs  (L12; the agent adopts it in L23)
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

The lab reuses existing product contracts unchanged: `Dhadgar.Contracts.Nodes.EnrollNodeRequest(Token, Platform, Hardware, PublicKey, AgentVersion?)`, `EnrollNodeResponse`, `HardwareInventoryRequest`, `AgentCertificateProfile` and `AgentHubContracts` (after Sprint 2).

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
```

### C7. UAT spec (TOML, schema 1)

Specs live in `docs/guides/uat-specs/*.toml`, one `[section]` per file.

```toml
schema = 1

[section]
id = "lab-selftest"                 # unique across specs; [a-z0-9-]+
title = "Lab self-test"
sequential = true                   # rows run in file order (default true)
requires = ["docker"]               # any of: docker, windows, machine

[[row]]
id = "token-mint"                   # unique in section
title = "A minted enrollment token is stored hashed and unused"
expected = "One enrollment token row with the label, not used"
depends_on = ["stack-up"]           # rows in this section

  [[row.step]]
  id = "mint"                       # optional; needed for since = "step:<id>"
  tool = "nodes_token_mint"         # never lease_* / approval / uat_* tools; MACHINE only if requires has "machine"
  args = { label = "selftest-${run_id}" }
  capture = { token_ref = "/token_ref", token_id = "/token_id" }   # var -> JSON pointer into the step result

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

Rules: `lease_id` never appears in a spec (the runner injects it). `${run_id}`, `${row_id}`, `${section_id}` and captured vars are available; an unknown var is a plan error. `${secret:<ref>}` is allowed only inside `http` bodies.

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
| `hub` (L24) | `event` (`connected`, `disconnected`), `node_id`, `since?` | `{events:[...], count}` |
| `agent_log` (L24) | like `log`, for the agent (console file or Event Log) | `{matches:[...], count}` |
| `human` | `text` only | none; pending until attested |

`since` is `run_start`, `row_start` (default) or `step:<id>`.

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

```csharp
namespace Dhadgar.Lab.Uat;
public enum RowGrade { Pass, Fail, Blocked, Skipped, NeedsHuman, Unverified }
public enum ClauseState { Held, NotHeld, Unverified, Pending }
public static class RowGrader
{
    public static (RowGrade Grade, string Reason) Grade(bool ran, string? skipReason, string? stepError, IReadOnlyList<(bool Required, ClauseState State)> clauses);
}
```

### C9. Evidence bundle

```text
<LabPaths.UatRuns>\<yyyyMMdd>-<build8|nobuild>-<runid6>\
  run.json
  rows\<section>\<row>.json
  logs\<process>.jsonl          (redacted log slices for the run window)
  ledger.md
```

`run.json`: `{schema:1, run_id, label, started_at, ended_at, daemon_version, os, machine_marker, build:{id, commit, dirty, checkout}, lease_owner, specs:[{file, sha256}], tools:[...], summary:{PASS, FAIL, BLOCKED, SKIPPED, NEEDS_HUMAN, UNVERIFIED}, redactions, secret_scan:"passed"|"failed"}`.

Row JSON: `{section, id, title, expected, grade, reason, started_at, ended_at, steps:[{id, tool, ok, error, duration_ms, result}], clauses:[{id, text, source, op, value, required, state, observed, attempts, checked_at}], attestations:[{clause_id, verdict, note, at}]}`. `result` and `observed` are redacted and truncated to 16 KiB.

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

### C10. Elevated helper protocol (L19, L20)

- Launch: `meridian-lab-elevated.exe --parent-pid <int> --pipe <name> --nonce <64 hex> --until <unix seconds>`, started with `Verb = "runas"`. Pipe name `meridian-lab-helper-<16 hex>`.
- Pipe: `NamedPipeServerStream` with `maxNumberOfServerInstances: 1`, `PipeOptions.FirstPipeInstance | PipeOptions.Asynchronous`, `PipeSecurity` granting only the current user SID `ReadWrite`. On connect, `GetNamedPipeClientProcessId` must equal `--parent-pid`, otherwise disconnect and exit. One client for the helper's lifetime.
- Frame: 4-byte little-endian length (max 1 MiB) then UTF-8 JSON.

```json
{ "v": 1, "id": "7d0c...", "nonce": "<64 hex>", "verb": "service_start", "args": {} }
```

```json
{ "v": 1, "id": "7d0c...", "ok": false, "error": { "code": "Lab.Helper.Denied", "message": "unknown verb" } }
```

- Nonce compared with `CryptographicOperations.FixedTimeEquals`. Three protocol errors, the `until` time, the parent's exit or `shutdown` end the helper.
- Verbs: `ping`, `shutdown` (L19); `service_install {build_id}`, `service_uninstall`, `service_start`, `service_stop`, `service_query`, `console_start {build_id, control_plane_url}`, `console_stop`, `registry_token_write {token}`, `registry_read`, `footprint_reset {lab_ca_thumbprint}` (L21); `trust_root_add {thumbprint}`, `trust_root_remove {thumbprint}` (L25, only if D-ML14 rules Root).

```csharp
namespace Dhadgar.Lab.Protocol;
public sealed record HelperRequest(int V, string Id, string Nonce, string Verb, JsonElement Args);
public sealed record HelperError(string Code, string Message);
public sealed record HelperResponse(int V, string Id, bool Ok, JsonElement? Data, HelperError? Error);
public static class HelperFrame
{
    public const int MaxFrameBytes = 1 << 20;
    public static Task<Result> WriteAsync(Stream stream, ReadOnlyMemory<byte> json, CancellationToken ct);
    public static Task<Result<byte[]>> ReadAsync(Stream stream, CancellationToken ct);
}
public static class HelperVerbs { /* const strings for every verb above */ }
```

### C11. MCP client configuration

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
7. `tests/Dhadgar.Lab.Tests/LabIsolationTests.cs`: walks up from `AppContext.BaseDirectory` to the folder holding `Dhadgar.sln`; fails if any `*.csproj` outside `src/Tools/` and `tests/Dhadgar.Lab.Tests/` contains `Dhadgar.Lab`, or if any file under `deploy/`, `src/Dhadgar.AppHost/` or named `Dockerfile*` contains `Dhadgar.Lab` or `meridian-lab`.
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
6. `Daemon/StatusEndpoint.cs`: maps `/status` to a `StatusDto` with `Lease = new(false, null, null, null, null)`, `Approval = new(false, null)`, `StackState = "down"`, empty processes (L05, L07, L08 fill these through an `ILabStatusSource` interface defined here).
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
3. `Tools/LabToolDispatcher.cs`: the seven steps of C3. Machine gate in this packet: always `[Lab.ApprovalRequired]` through an `IMachineApproval` interface (`bool IsActive { get; }`, `DateTimeOffset? Until { get; }`) with a `NoMachineApproval` default; L20 replaces it.
4. `Mcp/LabMcpServer.cs`: `services.AddMcpServer().WithHttpTransport(o => o.Stateless = true)`, `app.MapMcp(LabDefaults.McpPath)` behind the edge guard. Project each `ILabTool` into the SDK: preferred, one `McpServerTool` subclass `LabMcpTool` per tool whose `ProtocolTool` carries name, description and `InputSchema`, and whose `InvokeAsync` calls `LabToolDispatcher.InvokeAsync(name, arguments, new LabCallContext(callId, leaseIdFromArgs, "mcp"), ct)` and returns `CallToolResult { Content = [TextContentBlock(payload json)], StructuredContent = payload, IsError = isError }`. If the SDK's extension points differ, use its list/call handler hooks instead; the dispatcher must remain the only call path.
5. `Tools/Lab/LabStatusTool.cs`, `Tools/Lab/LeaseTools.cs` (acquire, renew, release, status). Wire `LeaseBook` (singleton), `LeaseSweeper`, `AuditLog`, `SecretVault`, `Redactor` into `LabDaemon`; `LeaseBook.LeaseEnded` → `IMachineApproval` revoke hook (no-op until L20).
6. Tests:
   - `Policy/ToolPolicyTests.cs`: `EveryRegisteredTool_IsClassified` (build the daemon's provider, enumerate `ILabTool`, assert `GateFor` non-null); `PolicyTable_HasNoUnknownNames` (every policy entry is either registered or listed in a `PlannedTools` set that later packets empty out); `Dispatcher_UnclassifiedTool_IsRefused` (register a test tool missing from the table → `Lab.Unclassified`); `Dispatcher_LeasedWithoutLease_IsRefused`; `Dispatcher_MachineWithoutApproval_IsRefused`.
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

**Contract excerpt:** C1 (`LabDefaults`, `LabPaths.Bin`, `Lab:RepoRoot`), C11.

**Files**

1. `tools/lab/install.ps1`: `dotnet publish <repo>\src\Tools\Dhadgar.Lab -c Release -o $env:LOCALAPPDATA\meridian-lab\bin`; writes `Lab:RepoRoot` into the published `labsettings.json`; if the user env var `MERIDIAN_LAB_TOKEN` is empty, runs `meridian-lab token new`, captures it into a variable and stores it with `[Environment]::SetEnvironmentVariable('MERIDIAN_LAB_TOKEN', $t, 'User')`, then removes the variable; calls `daemon.ps1 -Install`. Prints "restart your terminal so Claude Code sees MERIDIAN_LAB_TOKEN". Refuses when run elevated (the daemon must run unelevated). Leaves a `-LabMachine` switch that writes "not implemented until L20" and exits 1.
2. `tools/lab/daemon.ps1`: `-Install` (scheduled task `MeridianLabDaemon`, trigger at logon of the current user, `RunLevel Limited`, action `meridian-lab.exe daemon run`, restart on failure 3 times every minute, no execution time limit), `-Uninstall`, `-Start`, `-Stop`, `-Status` (reads `/status` with the token header).
3. `tools/lab/README.md`: install, start, stop, token rotation (`token new` then restart the task and the terminal).
4. `.mcp.json` at the repo root, exactly C11.
5. `tests/Dhadgar.Lab.Tests/Install/McpConfigTests.cs`: parses `.mcp.json`; asserts the `meridian-lab` entry's type, url (port equals `LabDefaults.DaemonPort`, host `127.0.0.1`, path `LabDefaults.McpPath`) and the header value. `ScriptsParseTests` (skip if `pwsh` is not on PATH): runs `pwsh -NoProfile -NonInteractive -Command` with `ArgumentList` to call `[System.Management.Automation.Language.Parser]::ParseFile` on each script and asserts no parse errors; asserts neither script contains `Invoke-Expression` or `RunLevel Highest`.

**Tests and why they fail when reverted:** `McpConfigTests` fails if the port or path drift from the daemon defaults.

**Checks**

```powershell
dotnet build <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L2"
```

**Manual check for the user (wave 1 gate):** run `tools\lab\install.ps1`, restart the terminal, `claude mcp list` shows `meridian-lab` connected, `lab_status` answers.

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

**Contract excerpt:** C4 `logs_tail`; C2 `ProcessStatusDto`; D-ML19 (max 3 relaunches in 5 minutes, only while a lease is held).

**Files**

1. `Processes/LabProcessSpec.cs`: `sealed record LabProcessSpec(string Name, string FileName, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment, string WorkingDirectory, Func<CancellationToken, Task<bool>>? Readiness)`.
2. `Processes/LabProcessSupervisor.cs`: `Task<Result<ProcessStatusDto>> StartAsync(LabProcessSpec, CancellationToken)`, `StopAsync(name)`, `RestartAsync(name)`, `IReadOnlyList<ProcessStatusDto> Snapshot()`; implements L02's `ILabStatusSource` for processes. Watchdog: on unexpected exit, relaunch if `LeaseBook.Current != null` and fewer than 3 relaunches in 5 minutes; else state `crashed`. `LeaseEnded` stops nothing (processes keep running) but disables relaunch.
3. `Processes/WindowsJobObject.cs`: `LibraryImport` `CreateJobObjectW`, `SetInformationJobObject` with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, `AssignProcessToJobObject`. One job per daemon; children die with the daemon.
4. `Processes/LogRing.cs` (per process, 20 000 lines) and `Processes/JsonLogLineParser.cs` (parses `Logging__Console__FormatterName=json` lines: `Timestamp`, `LogLevel`, `Category`, `EventId`, `Message`; a non-JSON line is kept as `message` with level `Raw`). Also appends to `logs/<name>.jsonl`.
5. `Tools/Logs/LogsTailTool.cs`: `logs_tail` per C4; `since` accepts ISO or `-<n>m|-<n>s`.
6. `tests/Dhadgar.Lab.TestChild/` (new console exe, in `Dhadgar.sln` under tests): `--lines <n> --json --exit <code> --sleep-ms <n>`; prints JSON console-format log lines. Referenced by the tests as a content copy or located via its build output path.
7. Tests: `SupervisorTests` (start, capture lines, stop; crash relaunch with a lease; no relaunch without a lease; 4th crash in 5 minutes stays `crashed`, with `FakeTimeProvider`), `JsonLogLineParserTests`, `LogsTailToolTests` (filters, cap 2000, redaction of a vault value in a log line).

**Tests and why they fail when reverted:** `Supervisor_CrashWithoutLease_IsNotRelaunched` fails if the lease check is dropped; the redaction fact fails if logs bypass `Redactor`.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\tests\Dhadgar.Lab.TestChild\Dhadgar.Lab.TestChild.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L2"
```

**Docs owed:** README section "Processes and logs".

### L08. Docker stack and lab database roles

- **Branch / worktree / commit:** `ml/l08-stack` / `meridian-lab-l08` / `feat(lab): lab-owned docker stack with loopback ports and read-only role`
- **Depends on:** L05. **Security review:** yes.
- **Rules:**
  - R-ARGS: `docker` via `ArgumentList` only; port bindings always `127.0.0.1:<lab port>:<container port>`.
  - R-ZERO / R-LOG: passwords generated with `RandomNumberGenerator` (24 bytes base64url), stored in the vault (`pwd-N`), passed to `docker run` via `--env-file` written to a temp file under `LabPaths.State` with user-only ACL and deleted right after start (never `-e PASSWORD=...` on the command line, which other users can read).
  - R-RESULT: docker missing or failing → `[Lab.Docker] ...`.
  - R-USING, R-PATH, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C1 `LabPorts`; C4 `stack_up`, `stack_down`, `stack_status`; D-ML6.

**Files**

1. `Stack/IDockerCli.cs` and `Stack/DockerCli.cs`: `Task<Result<DockerOutput>> RunAsync(IReadOnlyList<string> args, CancellationToken)`.
2. `Stack/LabStack.cs`: containers `meridian-lab-postgres`, `meridian-lab-rabbitmq`, `meridian-lab-redis`, each `--label meridian-lab=1`; images: the same image and major tag the AppHost uses (read `src/Dhadgar.AppHost/Program.cs`; if it does not pin, use `postgres:17`, `rabbitmq:4-management`, `redis:7`) and record them as constants. `UpAsync(wipe)`: wipe removes labelled containers and their anonymous volumes first; no named volumes. Readiness: `docker exec meridian-lab-postgres pg_isready`, `rabbitmq-diagnostics -q ping`, `redis-cli ping`, polling up to 60 s. Then create database `meridian_lab_platform`, role `lab_owner` (owner, password `pwd-1`) and `lab_reader` (password `pwd-2`, `CONNECT`, `USAGE ON SCHEMA public`, `ALTER DEFAULT PRIVILEGES FOR ROLE lab_owner IN SCHEMA public GRANT SELECT ON TABLES TO lab_reader`) via `docker exec ... psql -v ON_ERROR_STOP=1 -c` with ArgumentList. `DownAsync(wipe)`. Implements `ILabStatusSource` for stack state. Refuses to act on any container without the label.
3. `Stack/LabConnectionStrings.cs`: `Result<string> Owner()`, `Result<string> Reader()`, `Result<string> RabbitMq()`, `Result<string> Redis()`, built inside `SecretVault.Use`; the strings are passed only to child-process environments and Npgsql, never logged.
4. `Tools/Stack/StackTools.cs`: `stack_up`, `stack_down`, `stack_status`.
5. Tests: `LabStackTests` with a fake `IDockerCli` asserting argument lists (labels present, ports bound to `127.0.0.1`, no password in any argument, refuses unlabelled container names); `LabStackDockerTests` (`RequiresDocker`): up, `stack_status` is `up`, connect as `lab_reader` and fail to `CREATE TABLE`, down with wipe.

**Tests and why they fail when reverted:** `Up_BindsPortsToLoopbackOnly` and `Up_NeverPutsPasswordsInArguments` fail on the obvious regressions.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L2&RequiresDocker!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~LabStackDockerTests"   # Docker running
```

**Docs owed:** README section "Lab stack" (ports, never the dev volumes).

### L09. `lab_build`, Gateway and Nodes services, config overlays

- **Branch / worktree / commit:** `ml/l09-build-services` / `meridian-lab-l09` / `feat(lab): build checkouts and run gateway and nodes on lab ports`
- **Depends on:** L07, L08. **Security review:** yes.
- **Rules:**
  - R-PATH: `checkout` must validate under `Lab:RepoRoot` or `<RepoRoot>\.claude\worktrees\`; build output only under `LabPaths.Builds`.
  - R-ARGS: `dotnet publish` and `git` via `ArgumentList` (`git -C <checkout> rev-parse HEAD`, `git -C <checkout> status --porcelain`).
  - R-LOG: never log the child environment.
  - R-RESULT, R-USING, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C4 `lab_build`, `svc_*`, `svc_config_set` and its allowlist; C1 ports.

**Files**

1. `Build/LabBuilder.cs`: publishes `src/Dhadgar.Gateway`, `src/Dhadgar.Nodes` and (when asked) `src/Agents/Dhadgar.Agent.Windows` with `-c Release -o <Builds>\<build8>\<project>`; `build8` = first 8 of the commit; a dirty tree gets suffix `-dirty-<yyyyMMddHHmmss>`. Writes `<Builds>\<build_id>\build.json` `{build_id, commit, dirty, checkout, projects, at}`. Latest build pointer `<Builds>\latest.txt`.
2. `Services/LabServices.cs`: specs for `gateway` and `nodes`. Read `src/Dhadgar.Nodes/appsettings*.json`, `Program.cs` and `src/Dhadgar.Gateway/appsettings.json` to name the exact keys, then set through environment: `ASPNETCORE_URLS=http://127.0.0.1:<port>`, `ASPNETCORE_ENVIRONMENT=Development` (Nodes auto-migrates only there), `ConnectionStrings__Postgres` (owner), the RabbitMQ keys MassTransit reads, `Redis__ConnectionString` (Gateway), `ReverseProxy__Clusters__nodes__Destinations__d1__Address=http://127.0.0.1:15040/`, `Logging__Console__FormatterName=json`, the Nodes CA storage path key → `<LabPaths.State>\nodes-ca` (so the lab CA is never the dev CA), plus overlay values. Readiness: GET the service's health path (find it in `Dhadgar.ServiceDefaults`) until 200, 90 s timeout.
3. `Services/ConfigOverlay.cs`: `config/<service>.json`, allowlist per C4; an unknown key → `[Lab.InvalidArgument] key not allowed`. Applied at the next start.
4. `Tools/Services/BuildTool.cs`, `ServiceTools.cs` (`svc_start` refuses when the stack is not `up`: `[Lab.NotFound] stack is down`).
5. Tests: `LabBuilderTests` (argument lists; checkout outside the repo refused; `..` refused), `LabServicesTests` (environment composition: lab ports, CA path under lab state, no key outside the allowlist, overlay applied), `ServicesDockerTests` (`RequiresDocker`, slow): build the current checkout, stack up, start Nodes and Gateway, both ready, `GET http://127.0.0.1:15000/api/v1/agents/ca-certificate` returns 200 through the Gateway.

**Tests and why they fail when reverted:** `Overlay_UnknownKey_IsRefused`; `Nodes_CaStorage_IsUnderLabState` fails if the lab ever points at the dev CA path.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L2&RequiresDocker!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~ServicesDockerTests"   # Docker running
```

**Docs owed:** README section "Builds and services".

### L10. `db_query` and `http_request`

- **Branch / worktree / commit:** `ml/l10-db-http` / `meridian-lab-l10` / `feat(lab): read-only lab db queries and lab-only http probes`
- **Depends on:** L08. **Security review:** yes (SQL, SSRF).
- **Rules:**
  - R-HOST: `LabHosts.IsLabEndpoint` on the composed URI; `AllowAutoRedirect = false`; refuse a `Host` header override.
  - R-RESULT: a write attempt maps to `[Lab.ReadOnly]` (Postgres SQLSTATE `25006` or `42501`).
  - R-USING: `NpgsqlConnection`, `NpgsqlCommand`, `NpgsqlDataReader`, `HttpResponseMessage`.
  - R-LOG / R-ZERO: `${secret:<ref>}` is resolved inside `SecretVault.Use` right before sending; the request body is never logged or returned.
  - R-NULL, R-XUNIT.

**Contract excerpt:** C4 `db_query`, `http_request`; C7 note that params are bound, never interpolated.

**Files**

1. `Data/LabDb.cs`: `Task<Result<DbQueryResult>> QueryAsync(string sql, IReadOnlyDictionary<string, JsonElement> parameters, int maxRows, CancellationToken)`. Connects as `lab_reader`; `BEGIN TRANSACTION READ ONLY`; `SET LOCAL statement_timeout = 5000`; refuses SQL containing a `;` other than one trailing (a small scanner that skips quoted strings and comments); binds `@name` parameters from JSON (string → text, number → numeric, bool, null; authors cast in SQL); reads `maxRows + 1` rows; values to JSON (timestamps ISO 8601). `DbQueryResult(IReadOnlyList<string> Columns, IReadOnlyList<Dictionary<string, JsonElement>> Rows, int RowCount, bool Truncated)`.
2. `Http/LabHttp.cs`: named `HttpClient` (`IHttpClientFactory`), 30 s timeout, body limit 1 MiB in and 64 KiB returned; JSON bodies parsed, others returned as text.
3. `Tools/Data/DbQueryTool.cs`, `Tools/Http/HttpRequestTool.cs`.
4. Tests: `SqlStatementScannerTests` (`select 1; drop table x` refused; `select ';'` allowed), `LabHttpTests` (non-lab URL refused; redirect not followed using a `TestServer`-backed handler; placeholder resolved and redacted in the returned echo), `LabDbDockerTests` (`RequiresDocker`): `insert` → `Lab.ReadOnly`; parameter binding keeps `'; drop table` inert.

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

**Contract excerpt:** C6 in full; C4 `nodes_token_mint`, `nodes_enroll_probe`, `nodes_cert_time_shift`; C1 `LabIds`.

**Files**

1. `Nodes/NodesLabAdapter.cs`: real implementation. Construct `EnrollmentTokenService` with the context and whatever else its constructor needs (read it). If enrollment tokens need an organization row or the org must exist, seed `LabIds.OrganizationId` idempotently. `ShiftCertificateTimesAsync` updates `AgentCertificate` rows of the node: adds `shift` (negative) to the chosen fields; `GraceUntil` → `[Lab.Unavailable] needs Sprint 2` until L24. Production `createContext` uses `UseNpgsql(LabConnectionStrings.Owner())`.
2. `Nodes/EnrollProbe.cs`: build the key exactly as Nodes' `EnrollmentService.EnrollAsync` parses `PublicKey` (read it: algorithm, encoding, PEM or base64 SPKI); fixed lab hardware inventory (`HardwareInventoryRequest` with hostname `meridian-lab-probe`, plausible numbers); `AgentVersion = "meridian-lab"`.
3. `Tools/Nodes/NodesTools.cs`: `nodes_token_mint`, `nodes_enroll_probe` (via `gateway` → `/api/v1/agents/enroll`, via `nodes` → `/agents/enroll`), `nodes_cert_time_shift`.
4. Tests: S-L2 green (no change to the test; only the stubs go). `NodesSeamDockerTests` (`RequiresDocker`, `[Trait("Seam","S-L2")]`): full lab stack, `nodes_token_mint`, `nodes_enroll_probe via=gateway` → 201; a second probe with the same token → 401. `NodesToolsTests`: the mint result contains `token_ref` and no plain token; shift with a positive value refused.

**Tests and why they fail when reverted:** S-L2 fails if the lab and Nodes disagree on token hashing, the request shape or the key encoding.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~NodesSeamDockerTests"   # Docker running
```

**Docs owed:** README section "Nodes adapter" (why the lab mints locally: no user-auth path in Nodes).

### L12. Windows reads, `lab_doctor`, `AgentWindowsFootprint`, red S-L3

- **Branch / worktree / commit:** `ml/l12-windows-reads` / `meridian-lab-l12` / `feat(lab): windows cert store, registry and state reads plus footprint contract`
- **Depends on:** L05. **Security review:** yes (cert stores, registry, Contracts change).
- **Rules:**
  - R-OS: every Windows call behind `[SupportedOSPlatform("windows")]` and `OperatingSystem.IsWindows()`; Linux → `[Lab.Unavailable]`.
  - R-USING: `X509Store`, each `X509Certificate2` from `store.Certificates`, `RegistryKey`.
  - R-LOG: never export a certificate or private key; never return a registry value (presence and length only).
  - R-PATH: the state file path is built from `%ProgramData%` + `AgentWindowsFootprint` and validated.
  - R-CONTRACT: footprint names only from `AgentWindowsFootprint`.
  - R-RESULT, R-NULL, R-XUNIT.

**Contract excerpt:** C5; C4 `certstore_list`, `registry_read`, `state_file_read`, `lab_doctor`.

**Files**

1. `src/Shared/Dhadgar.Contracts/Agents/AgentWindowsFootprint.cs`: exactly C5.
2. `Windows/CertStoreReader.cs`: stores `machine_my` (`LocalMachine\My`), `machine_root` (`LocalMachine\Root`), `agent_ca` (`LocalMachine\<CaStoreName>`, `OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly`; missing → empty list), `current_user_my`. Metadata only.
3. `Windows/AgentRegistryReader.cs`: `HKLM\` + `RegistryKeyPath`; value names, kinds, presence and length; ACL as SID + rights. Access denied → `[Lab.ApprovalRequired] needs an elevated read` (L21 adds the helper path).
4. `Windows/AgentStateReader.cs`: reads the state JSON, runs `Redactor`, also blanks any property whose name contains `token`, `key` or `secret` (case-insensitive).
5. `Tools/Doctor/LabDoctorTool.cs`: checks OS, `docker version`, `dotnet --version`, lab ports free or owned by the lab, token length, `Lab:RepoRoot` exists. (L20 adds marker, helper and UAC checks.)
6. `Tools/Windows/WindowsReadTools.cs`: `certstore_list`, `registry_read`, `state_file_read`.
7. Tests: `CertStoreReaderTests` (Windows, `SkippableFact`): lists `current_user_my` without error and never sets a private-key field from export; `AgentStateReaderTests` (temp file through an injected path, redaction of token-like properties); `WindowsToolsOnLinuxTests` (`Skip.If(OperatingSystem.IsWindows())`) → `Lab.Unavailable`.
8. `tests/Dhadgar.Lab.Tests/Seams/AgentFootprintSeamTests.cs` (S-L3, red until L23): add a project reference to `Dhadgar.Agent.Windows` (alias `agentwin` if `Program` clashes). Assert `Program.ServiceName`, the CA store name constant in `WindowsCertificateStore` (read it with reflection: `GetField("CaStoreName", BindingFlags.NonPublic | BindingFlags.Static)`), the registry key path and the state file name used by the agent all equal `AgentWindowsFootprint`. On `main` this is red because the CA store constant does not exist yet.

**Tests and why they fail when reverted:** S-L3 fails while the agent and the lab disagree on any footprint name.

**Checks**

```powershell
dotnet build <W>\src\Shared\Dhadgar.Contracts\Dhadgar.Contracts.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
```

**Docs owed:** README section "Windows reads"; a line in `src/Shared/Dhadgar.Contracts/README.md` (if present) for `AgentWindowsFootprint`.

---

## Wave 3: UAT engine (no Sprint 2 dependency)

### L13. Spec model, TOML loader, validator, `uat_list_specs`, `uat_plan`

- **Branch / worktree / commit:** `ml/l13-spec` / `meridian-lab-l13` / `feat(lab): uat spec model, toml loader and plan-only validation`
- **Depends on:** L05. **Security review:** yes (spec input drives the machine).
- **Rules:**
  - R-PATH: `specs_dir` defaults to `<RepoRoot>\docs\guides\uat-specs`; any other dir must validate under the repo root or its worktrees; spec names are file names only.
  - R-RESULT: every load and validation problem is a list entry `{file, line, message}`, never an exception.
  - R-NULL, R-XUNIT, R-CONTRACT.

**Contract excerpt:** C7 in full.

**Files**

1. `Uat/Spec/UatSpec.cs`: records `UatSpec(int Schema, UatSection Section, IReadOnlyList<UatRow> Rows, string File, string Sha256)`, `UatSection(Id, Title, bool Sequential, IReadOnlyList<string> Requires)`, `UatRow(Id, Title, Expected, IReadOnlyList<string> DependsOn, IReadOnlyList<UatStep> Steps, IReadOnlyList<UatClause> Expect)`, `UatStep(string? Id, string? Tool, JsonElement? Args, IReadOnlyDictionary<string,string> Capture, int? WaitMs)`, `UatClause(Id, Text, Source, Op, JsonElement? Value, string? Pointer, string Since, int TimeoutMs, int PollMs, bool Required, IReadOnlyDictionary<string, JsonElement> SourceFields)`.
2. `Uat/Spec/SpecLoader.cs`: `Tomlyn.Toml.ToModel` → `TomlTable` → records; TOML values converted to `JsonElement` for args and values. Line numbers from Tomlyn's syntax tree where available.
3. `Uat/Spec/SpecValidator.cs`: schema must be 1; ids `[a-z0-9-]+` and unique; `depends_on` targets exist and come earlier; every row has at least one step and one required clause; step tools exist, are classified and are not `lab_lease_*`, `lab_machine_approval_*` or `uat_*`; MACHINE tools only when `requires` has `machine`; no `lease_id` key in any args; `source` known; source fields present (C7 table); `op` known and value shape right for it (`approx` two numbers, `any` list); `${...}` references resolvable (built-ins or captured earlier in the row); `${secret:...}` only in `http` bodies; `timeout_ms` ≤ 300000; `since = step:<id>` names a step of the row.
4. `Tools/Uat/UatPlanTools.cs`: `uat_list_specs`, `uat_plan` (no lease; touches nothing).
5. Tests: `SpecLoaderTests` (a full valid spec), `SpecValidatorTests` (one theory row per rule, each with a tiny invalid TOML string), `UatPlanToolTests`.

**Tests and why they fail when reverted:** each validator rule has a case that turns valid if the rule is removed.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
```

**Docs owed:** none beyond code comments (L22 writes the spec guide).

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

1. `Uat/Eval/IClauseSource.cs`: `string Name { get; }`, `Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)`; `ClauseWindow(DateTimeOffset RunStart, DateTimeOffset RowStart, IReadOnlyDictionary<string, DateTimeOffset> StepStarts)`.
2. Sources: `DbSource` (binds `@since`), `LogSource` (over `LogRing`), `HttpSource`, `ToolSource`, `CertStoreSource`, `RegistrySource`, `StateFileSource`, `HumanSource` (always `Pending`).
3. `Uat/Eval/JsonPointer.cs`: RFC 6901 over `JsonElement` (`~0`, `~1`); missing → not found.
4. `Uat/Eval/ClauseOps.cs`: every op of C7; numbers compared as `decimal`; strings ordinal; `matches` with `RegexOptions.CultureInvariant` and a 1 s timeout.
5. `Uat/Eval/UatVars.cs`: `${name}` substitution in strings inside JSON args and clause fields; capture from a step result with a pointer; unknown var → `Result.Failure("[Lab.InvalidArgument] unknown var")`.
6. `Uat/Eval/ClauseEvaluator.cs`: one check or poll until held or `timeout_ms`; returns `(ClauseState, JsonElement? Observed, int Attempts, DateTimeOffset CheckedAt, string? Error)`. `delta` takes the baseline before the row's first step (the runner calls `CaptureBaselineAsync`).
7. Tests: `ClauseOpsTests` (a theory per op, including type mismatches → `NotHeld`, missing pointer → `Unverified` except `absent` → `Held`), `JsonPointerTests`, `UatVarsTests`, `ClauseEvaluatorTests` with fake sources (polling until held, timeout → `NotHeld`, source error → `Unverified`).

**Tests and why they fail when reverted:** `MissingPointer_IsUnverified_NotHeld` protects the "missing evidence never passes" rule.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
```

**Docs owed:** none (L22).

### L15. Runner and grader, `uat_run`

- **Branch / worktree / commit:** `ml/l15-runner` / `meridian-lab-l15` / `feat(lab): uat runner and grader with own-lease runs`
- **Depends on:** L14. **Security review:** yes.
- **Rules:**
  - R-RESULT: a step error makes the row BLOCKED and stops it; the run continues with the next row.
  - R-TIME, R-LOG (step args never logged), R-NULL, R-XUNIT.
  - R-CONTRACT: steps run through `LabToolDispatcher` with `LabCallContext(callId, leaseId, "uat:<runId>")`; the runner injects `lease_id` into every LEASED and MACHINE step.

**Contract excerpt:** C8 in full; C4 `uat_run`; OwnLease semantics: with a `lease_id` that matches the current lease the run uses it; without one the run acquires its own (`owner = "uat-run/<runId>"`, `purpose = label or "uat"`, ttl 600, renewed by every step) and releases it at the end; if another holder has it → `Lab.LeaseHeld` before any row runs.

**Files**

1. `Uat/RowGrader.cs`: exactly C8.
2. `Uat/UatRunner.cs`: `Task<Result<UatRunResult>> RunAsync(UatRunRequest request, IUatRunSink sink, CancellationToken ct)`. `plan_only` → validate and return without a lease. Order: sections as given, rows in file order (`sequential` true) or in order with dependencies respected; `requires` checked (`docker` via `stack_status`/docker probe, `windows` via OS, `machine` via `IMachineApproval.IsActive`); `wait_ms` steps; captures; clause evaluation after the last step. `run_id` = 6 hex chars from `RandomNumberGenerator`.
3. `Uat/IUatRunSink.cs`: `RunStarted`, `RowFinished`, `RunFinished` (in-memory implementation here; L16 adds the bundle writer).
4. `Tools/Uat/UatRunTool.cs`: `uat_run`.
5. Tests: `RowGraderTests` (every row of the C8 table plus precedence combinations and "no required clause → UNVERIFIED"), `UatRunnerTests` with fake tools and sources (dependency failure → SKIPPED, step error → BLOCKED, own lease acquired and released, held lease → refused, lease lost mid-run → remaining rows BLOCKED).

**Tests and why they fail when reverted:** `Grade_NoRequiredClauses_IsUnverified` and `Grade_PendingHuman_IsNeedsHuman` pin the PASS rule.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
```

**Docs owed:** none (L22).

### L16. Evidence bundle, ledger, secret scan, report, attest, `bundle_read`

- **Branch / worktree / commit:** `ml/l16-bundle` / `meridian-lab-l16` / `feat(lab): uat evidence bundle, ledger and attestation`
- **Depends on:** L15. **Security review:** yes (secrets on disk).
- **Rules:**
  - R-PATH: bundle files only under `LabPaths.UatRuns\<run>`; `bundle_read` validates `path` inside that folder.
  - R-LOG / R-ZERO: everything written passes through `Redactor`; `SecretScanner` runs last.
  - R-RESULT, R-USING, R-TIME, R-NULL, R-XUNIT.

**Contract excerpt:** C9 in full; C4 `uat_report`, `uat_attest`, `bundle_read`; C8 attestation rule (human clauses only).

**Files**

1. `Uat/Bundle/BundleWriter.cs` (`IUatRunSink`): writes C9 files; log slices for the run window from `logs/<process>.jsonl`.
2. `Uat/Bundle/LedgerRenderer.cs`: exactly the C9 `ledger.md` shape; "Saw" lists each clause `id: pointer observed (state)`; tables with spaces around pipes.
3. `Uat/Bundle/SecretScanner.cs`: C9 patterns plus every vault value and the daemon token.
4. `Tools/Uat/UatReportTools.cs`: `uat_report`, `uat_attest` (refuses non-`human` clauses with `[Lab.InvalidArgument]`; re-grades via `RowGrader`; rewrites the row JSON and `ledger.md`; records `{clause_id, verdict, note, at}`), `bundle_read`.
5. Wire `BundleWriter` as the sink for `uat_run`.
6. Tests: `LedgerRendererTests` (golden text), `SecretScannerTests` (planted vault value → file removed, run marked failed, every row BLOCKED), `UatAttestTests` (human clause held → PASS; non-human clause refused), `BundleReadTests` (`..\run.json` of another run refused).

**Tests and why they fail when reverted:** `SecretScan_PlantedToken_BlocksRun` fails if the scanner is skipped.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
```

**Docs owed:** none (L22).

### L17. CLI `status` and `uat`, `lab-selftest.toml`, S-L6

- **Branch / worktree / commit:** `ml/l17-cli-selftest` / `meridian-lab-l17` / `feat(lab): uat cli and lab self-test spec`
- **Depends on:** L16, L11. **Security review:** yes.
- **Rules:**
  - R-HOST: the CLI talks only to `http://127.0.0.1:<port>`; token from `MERIDIAN_LAB_TOKEN`, never an argument.
  - R-RESULT: exit 0 all PASS, 1 any FAIL/BLOCKED/UNVERIFIED, 2 config error, 4 NEEDS_HUMAN only.
  - R-PATH, R-USING, R-NULL, R-XUNIT.

**Contract excerpt:** C7 (spec), C8, C9; C11 (same MCP path as Claude).

**Files**

1. `Cli/LabCli.cs`: `meridian-lab status [--json]` (GET `/status`); `meridian-lab uat run --specs <file>... [--section <id>] [--rows a,b] [--plan-only] [--label <text>] [--json]` as an MCP client calling `uat_run`; prints the summary table and the bundle path.
2. `docs/guides/uat-specs/lab-selftest.toml`, section `lab-selftest`, `requires = ["docker"]`, rows: `stack-up` (`stack_up wipe=true`; expect `stack_status` `/state` eq `up`), `nodes-up` (`svc_start nodes`; `http` GET health eq 200), `gateway-up` (`svc_start gateway`; `http via=gateway` GET `/api/v1/agents/ca-certificate` status 200), `token-mint` (C7 example), `enroll-direct` (`nodes_enroll_probe via=nodes`; expect status 201, `db` node row exists for the captured `node_id`), `enroll-gateway` (fresh token, `via=gateway`, 201), `token-single-use` (reuse the `enroll-gateway` token; status 401), `nodes-log` (`log process=nodes contains="nroll"`, `min_rows` 1 since `run_start`). Use real table and column names from the Nodes EF model (read the migrations snapshot).
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

1. `.claude/agents/uat-driver.md`: `model: haiku`, `omitClaudeMd: true`, `effort: low`, `maxTurns: 50`, `tools:` exactly `mcp__meridian-lab__` + `lab_status`, `lab_doctor`, `lab_lease_acquire`, `lab_lease_renew`, `lab_lease_release`, `lab_lease_status`, `stack_status`, `stack_up`, `stack_down`, `lab_build`, `svc_start`, `svc_stop`, `svc_restart`, `svc_config_set`, `agent_config_set`, `logs_tail`, `db_query`, `http_request`, `certstore_list`, `registry_read`, `state_file_read`, `nodes_token_mint`, `nodes_enroll_probe`, `nodes_cert_time_shift`, `uat_list_specs`, `uat_plan`, `uat_run`, `uat_report`, `bundle_read`. Body: runs exactly the steps in the brief; one line per step `<step> <ok|error> <raw result or error code>`; retries a failed step once unchanged; stops on any unexpected state and reports it raw; never diagnoses, never edits, never uses `force` on a lease, never invents rows or args; always releases a lease it acquired; ends with `driver done <run_id|-> <bundle path|->`.
2. `.claude/skills/lab-uat/SKILL.md`: when to use; before taking the lease, ask the user (AskUserQuestion) unless they asked for the run; run `uat_plan` first; MACHINE rows need the user to request approval themselves (`lab_machine_approval_request` from the main session; the user clicks UAC; the driver never requests it); dispatch `uat-driver` with an exact step list and the lease owner name; afterwards read `uat_report` and the bundle's `ledger.md` and check every claim the driver made against it; human clauses only by `uat_attest` after the user says what they saw; copy `ledger.md` into the campaign `worknotes/` when it is evidence for status; batches of about 5 rows; troubleshooting (`lab_doctor`, lease held by whom, approval expired).
3. `tests/Dhadgar.Lab.Tests/Agents/DriverToolListTests.cs` (S-L5): parse the frontmatter `tools`; every entry starts `mcp__meridian-lab__`; every tool exists in the registry; none has gate `Machine`; none is `lab_machine_approval_request`, `lab_machine_approval_revoke` or `uat_attest`; no non-MCP tool (Read, Edit, Write, Bash, PowerShell, Agent).

**Tests and why they fail when reverted:** adding a MACHINE tool or `Bash` to the driver turns S-L5 red.

**Checks**

```powershell
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true"
```

**Docs owed:** the agent and skill files are the docs.

---

## Wave 4: MACHINE tier (needs D-ML12; Windows; no Sprint 2 dependency)

### L19. Elevated helper project, pipe protocol, red S-L4

- **Branch / worktree / commit:** `ml/l19-helper` / `meridian-lab-l19` / `feat(lab): elevated helper with fixed verbs over a locked-down pipe`
- **Depends on:** L05; D-ML12 ruled. **Security review:** yes (elevation boundary).
- **Rules:**
  - R-OS: Windows-only pipe security and PID checks behind `[SupportedOSPlatform("windows")]`; the helper exits 2 on non-Windows.
  - R-ZERO: the nonce is parsed to bytes and compared with `FixedTimeEquals`; `registry_token_write` payloads are zeroed after use (L21).
  - R-USING: pipe streams, handles.
  - R-RESULT: every verb returns a `HelperResponse`, never throws over the pipe.
  - R-LOG: the helper writes its own log to `%ProgramData%\Meridian Lab\logs\helper.jsonl` without verb arguments.
  - R-CPM, R-NULL, R-XUNIT.

**Contract excerpt:** C10 in full.

**Files**

1. `src/Tools/Dhadgar.Lab.Protocol/Dhadgar.Lab.Protocol.csproj` (class library) with C10 types and `HelperFrame`.
2. `src/Tools/Dhadgar.Lab.Elevated/Dhadgar.Lab.Elevated.csproj` (exe, `AssemblyName meridian-lab-elevated`, references Protocol, `Dhadgar.Shared`, `Dhadgar.Contracts`; not the daemon). Add an `app.manifest` with `requestedExecutionLevel level="requireAdministrator"`.
3. `HelperArgs.cs` (parse and validate `--parent-pid`, `--pipe` `^meridian-lab-helper-[0-9a-f]{16}$`, `--nonce` 64 hex, `--until` future and ≤ now + 120 minutes), `HelperPipeServer.cs` (C10 pipe rules; a `Func<int>`-injectable client PID check for tests), `VerbTable.cs` (verb → handler; unknown → `Lab.Helper.Denied`), `ParentWatch.cs` (exit when the parent exits).
4. Daemon side stub `src/Tools/Dhadgar.Lab/Machine/HelperClient.cs`: `Task<Result<JsonElement>> CallAsync(string verb, object args, CancellationToken)` returning `[Lab.NotImplemented]` (L20 implements).
5. Both new projects added to `Dhadgar.sln`; the daemon references Protocol; `Dhadgar.Lab.Tests` references Protocol and Elevated (alias if needed).
6. Tests: `HelperFrameTests` (round trip; oversize frame refused; truncated frame → failure), `HelperArgsTests`, `VerbTableTests` (unknown verb denied; bad nonce denied; three protocol errors end the session). `Seams/HelperPipeSeamTests.cs` (S-L4, Windows `SkippableFact`, unelevated is fine because the test hosts the server class directly): real `HelperPipeServer` and real `HelperClient` on a random pipe name exchange `ping`; a second client is refused; a wrong nonce is refused. Red until L20.

**Tests and why they fail when reverted:** `Pipe_SecondClient_IsRefused` and `Verb_WrongNonce_IsDenied` guard the boundary.

**Checks**

```powershell
dotnet restore <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj --force-evaluate
dotnet build <W>\src\Tools\Dhadgar.Lab.Elevated\Dhadgar.Lab.Elevated.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&Seam!=S-L4&RequiresDocker!=true"
```

**Docs owed:** `src/Tools/Dhadgar.Lab.Elevated/README.md` (verbs, why each is safe, what it refuses).

### L20. Approval flow, helper launcher, machine marker; S-L4 green

- **Branch / worktree / commit:** `ml/l20-approval` / `meridian-lab-l20` / `feat(lab): uac-approved machine window and helper launcher`
- **Depends on:** L19; D-ML12. **Security review:** yes.
- **Rules:**
  - R-ARGS: the one `UseShellExecute = true` launch (needed for `Verb = "runas"`): use `ArgumentList` if the runtime accepts it with shell execute; otherwise build `Arguments` only from validated ints and hex strings (no user-supplied text) and unit-test the exact string.
  - R-PATH: the helper path must be `%ProgramFiles%\Meridian Lab\meridian-lab-elevated.exe`; refuse a path under any user-writable folder.
  - R-ZERO: nonce bytes zeroed when the window ends.
  - R-TIME, R-RESULT, R-USING, R-OS, R-NULL, R-XUNIT.

**Contract excerpt:** C10 launch and pipe; C4 `lab_machine_approval_request`, `lab_machine_approval_revoke`; `IMachineApproval` from L05; D-ML12, D-ML13.

**Files**

1. `Machine/MachineApproval.cs` (implements `IMachineApproval`): `RequestAsync(minutes)` → checks the marker, the helper path, the lease; launches the helper; connects the `HelperClient` within 120 s (the UAC prompt waits for the user; a decline → `[Lab.ApprovalRequired] declined`); `IsActive` true until `Until`, lease end, helper exit or `RevokeAsync`. Registered as the singleton replacing `NoMachineApproval`; `LeaseBook.LeaseEnded` → `RevokeAsync`.
2. `Machine/HelperLauncher.cs`, `Machine/MachineMarker.cs` (`%ProgramData%\Meridian Lab\lab-machine.marker` exists and is not writable by the current user, checked through its ACL).
3. `Machine/HelperClient.cs`: real; one connection; request ids; 30 s per call.
4. `Tools/Machine/ApprovalTools.cs`; `StatusDto.Approval` filled.
5. `LabDoctorTool`: add marker, helper path and `ConsentPromptBehaviorAdmin` (`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System`; 0 means no prompt → doctor warns that approval would be silent).
6. `tools/lab/install.ps1 -LabMachine`: must run elevated; copies the published helper to `%ProgramFiles%\Meridian Lab\`; creates `%ProgramData%\Meridian Lab\` with ACL Administrators and SYSTEM full, Users read; writes the marker.
7. Tests: S-L4 green; `MachineApprovalTests` with a fake launcher (window expiry with `FakeTimeProvider`; lease release revokes; helper path in `%LOCALAPPDATA%` refused); `HelperLauncherTests` (exact argument string); `MachineHelperElevatedTests` (`RequiresElevation`, Windows): real launch, `ping`, revoke, helper process gone.

**Tests and why they fail when reverted:** `LeaseRelease_RevokesApproval` and `HelperPath_UserWritable_IsRefused`.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresElevation!=true"
```

The orchestrator asks the user to run once in an elevated shell with `MERIDIAN_LAB_ELEVATED_TESTS=1`:

```powershell
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresElevation=true"
```

**Docs owed:** README section "Machine approval".

### L21. MACHINE verbs: agent service and console, registry token, footprint reset

- **Branch / worktree / commit:** `ml/l21-machine-verbs` / `meridian-lab-l21` / `feat(lab): machine-tier agent service, console, token and footprint verbs`
- **Depends on:** L20, L12; D-ML12 (D-ML13 for live use). **Security review:** yes (services, LocalMachine stores, registry).
- **Rules:**
  - R-PATH: the agent binary runs only from `%ProgramFiles%\Meridian Lab\agent\<build_id>\` (the helper copies it there from `LabPaths.Builds`; a LocalSystem service must never run a user-writable binary).
  - R-ARGS: no `sc.exe`; create and delete services with `CreateServiceW`/`DeleteService` (`LibraryImport`); start/stop with `ServiceController`. The console agent starts with `ArgumentList`.
  - R-ZERO: the token travels daemon → helper inside one frame; both sides zero their buffers after writing the registry value.
  - R-CONTRACT: every name from `AgentWindowsFootprint`.
  - R-OS, R-RESULT, R-USING, R-LOG, R-NULL, R-XUNIT.

**Contract excerpt:** C4 MACHINE rows and `agent_config_set` with its allowlist; C5; C10 verbs for L21.

**Files**

1. Helper verbs in `src/Tools/Dhadgar.Lab.Elevated/Verbs/`: `ServiceVerbs.cs` (install: copy build → ProgramFiles, write `appsettings.Lab.json` beside it with the control-plane URL of the lab Gateway and the overlay, set the service environment `DOTNET_ENVIRONMENT=Lab` via the `Environment` REG_MULTI_SZ under `HKLM\SYSTEM\CurrentControlSet\Services\DhadgarAgent`, `CreateServiceW` as LocalSystem, demand start; uninstall; start; stop; query), `ConsoleVerbs.cs` (start the agent exe elevated with `DOTNET_ENVIRONMENT=Lab`, stdout/stderr redirected to `%ProgramData%\Meridian Lab\logs\agent.jsonl`, readable by Users; stop kills it), `RegistryVerbs.cs` (`registry_token_write`, `registry_read` returning metadata and ACL), `FootprintVerbs.cs` (`footprint_reset {lab_ca_thumbprint}`: stop and delete the service; remove from `LocalMachine\My` only certificates whose issuer chains to that thumbprint; remove that CA from the agent CA store and from `LocalMachine\Root` only if the thumbprint matches; delete the agent registry key; delete the state directory after validating it equals `%ProgramData%\` + `StateDirectoryRelative`). Read the agent's `AgentOptions` for the exact control-plane URL key.
2. Daemon tools in `src/Tools/Dhadgar.Lab/Tools/Machine/AgentMachineTools.cs` and `agent_config_set` (Leased; overlay file `config/agent.json`, allowlist C4). `agent_footprint_reset` fetches the lab CA thumbprint from lab Nodes `GET /agents/ca-certificate` and refuses without it. `registry_read` uses the helper when the unprivileged read is denied and approval is active. `logs_tail process=agent` reads `agent.jsonl`.
3. Tests: `FootprintVerbsTests` (selection logic over in-memory certificate lists: a non-lab certificate is never selected; state path mismatch refused), `ServiceVerbsTests` (binary path outside ProgramFiles refused; environment block composition), `AgentMachineToolsTests` (without approval → `Lab.ApprovalRequired`; without marker → `Lab.MachineNotMarked`; wrong `confirm` refused). `MachineVerbsElevatedTests` (`RequiresElevation`): install, start, stop, uninstall a dummy service built from `Dhadgar.Lab.TestChild` under a test service name injected only in test builds.

**Tests and why they fail when reverted:** `FootprintReset_NeverSelectsNonLabCertificates` and `ServiceInstall_UserWritableBinary_IsRefused`.

**Checks**

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab.Elevated\Dhadgar.Lab.Elevated.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "Seam!=S-L3&RequiresDocker!=true&RequiresElevation!=true"
```

**Docs owed:** helper README verbs section updated.

### L22. Docs: ADR, guides, security page (documentation-writer)

- **Agent:** `documentation-writer`. **Branch / worktree / commit:** `ml/l22-docs` / `meridian-lab-l22` / `docs(lab): meridian-lab adr, operating guide, uat spec guide and threat model`
- **Depends on:** L18, L21. **Security review:** security-advisor read.
- **Rules:** tables with spaces around pipes; every code block names its language; Diátaxis placement per `docs/meta/`; no secrets or private IPs.

**Files**

1. `docs/adr/<next free number>-meridian-lab-daemon.md` from `docs/adr/TEMPLATE.md`: context (F1-F6, F12), decision (D-ML1 to D-ML12, D-ML19), consequences; update `docs/adr/README.md`.
2. `docs/guides/meridian-lab.md` (how-to): install, `-LabMachine`, start/stop, lease etiquette, approval flow, ad-hoc probes, troubleshooting with `lab_doctor`.
3. `docs/guides/automated-uat.md` (reference): C7, C8, C9 in prose and tables; how to write a row; the `lab-uat` skill and `uat-driver`.
4. `docs/security/meridian-lab-threat-model.md`: assets, trust boundaries (Claude Code ↔ daemon, daemon ↔ helper, lab ↔ machine), each control and its test; "dev and lab only".
5. Link from `docs/guides/README.md`, `docs/testing/README.md`, `docs/security/README.md`.

**Checks:** links resolve (Grep each relative link target).

---

## Wave 5: Sprint 2 flows (after Sprint 2's PR is on `main`)

**Wave 5 gate (orchestrator):** Sprint 2's PR merged to `main`; a Sonnet `general-purpose` agent merges `origin/main` into `campaign/meridian-lab`, resolves conflicts (expected only in `Directory.Packages.props`, `Dhadgar.sln` and lock files), and runs `dotnet test` on `tests/Dhadgar.Lab.Tests` (non-Docker) green except S-L3.

### L23. Agent consumes `AgentWindowsFootprint`; S-L3 green

- **Branch / worktree / commit:** `ml/l23-footprint` / `meridian-lab-l23` / `refactor(agent-windows): take footprint names from Dhadgar.Contracts`
- **Depends on:** wave 5 gate, L12. **Security review:** yes (agent project).
- **Rules:** R-CONTRACT (names only from `AgentWindowsFootprint`; delete the local literals), R-NULL, R-XUNIT, R-CPM; no behaviour change.

**Contract excerpt:** C5. If Sprint 2's merged names differ from C5 (for example the state file name), C5 changes to match the agent and the report says so; the agent is the source of truth for values, Contracts for location.

**Files:** `src/Agents/Dhadgar.Agent.Windows/Program.cs` (`ServiceName` and `EventLogSourceName` become aliases of the Contracts constants), `Windows/WindowsCertificateStore.cs` (`CaStoreName`), the class holding the registry key path, the state file name in `AgentOptions` or its consumer (find with Grep for each literal). `AgentFootprintSeamTests` switches from reflection to direct comparison where the constant is now public.

**Tests:** S-L3 green; `tests/Dhadgar.Agent.Windows.Tests` stays green.

**Checks**

```powershell
dotnet build <W>\src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Agent.Windows.Tests\Dhadgar.Agent.Windows.Tests.csproj -c Release
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&RequiresElevation!=true"
```

**Docs owed:** none.

### L24. Sprint 2 evidence: `GraceUntil`, hub events, `hub` and `agent_log` sources

- **Branch / worktree / commit:** `ml/l24-s2-evidence` / `meridian-lab-l24` / `feat(lab): grace, hub and agent log evidence for uat`
- **Depends on:** wave 5 gate, L21. **Security review:** yes (Nodes hub logging).
- **Rules:** R-LOG (hub log events carry `NodeId` and connection id only, never certificates), R-CONTRACT, R-RESULT, R-OS (Event Log reading), R-TIME, R-NULL, R-XUNIT, R-DI.

**Contract excerpt:** C7 `hub` and `agent_log` rows; C6 `CertTimeField.GraceUntil`.

**Files**

1. `NodesLabAdapter.ShiftCertificateTimesAsync`: `GraceUntil` now updates `AgentCertificate.GraceUntil`.
2. Nodes AgentHub (path from Sprint 2): if `OnConnectedAsync`/`OnDisconnectedAsync` do not already log structured events with the node id, add `LoggerMessage` events `AgentHubConnected` (EventId 4101) and `AgentHubDisconnected` (4102) with `NodeId`; add a Nodes test asserting both are logged (use the Nodes test conventions).
3. `Uat/Eval/HubSource.cs` (filters Nodes log lines by those EventIds and `NodeId`), `Uat/Eval/AgentLogSource.cs` (console: `agent.jsonl`; service: Windows Application Event Log, source `AgentWindowsFootprint.EventLogSourceName`, entries after `since`).
4. Tests: `HubSourceTests`, `AgentLogSourceTests` (console file path; Event Log read in a `SkippableFact`), adapter grace shift test (InMemory).

**Checks**

```powershell
dotnet build <W>\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj -c Release
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true&RequiresElevation!=true"
```

**Docs owed:** Nodes README line for the two events; automated-uat guide rows for `hub` and `agent_log`.

### L25. Lab Gateway TLS and mTLS, agent trust wiring

- **Branch / worktree / commit:** `ml/l25-lab-tls` / `meridian-lab-l25` / `feat(lab): lab gateway tls and agent trust per D-ML14`
- **Depends on:** L24; **D-ML14 ruled**. **Security review:** yes (certificates, trust stores).
- **Rules:** R-ZERO (lab server key), R-PATH (cert files under `LabPaths.State\tls`), R-CONTRACT (Sprint 2's config keys and `AgentCertificateProfile`), R-OS, R-RESULT, R-USING, R-NULL, R-XUNIT.

**Spec:** generate a lab server certificate for `127.0.0.1` and `localhost` (lab-only CA under `LabPaths.State\tls`); run the Gateway with Kestrel HTTPS on `LabPorts.GatewayTls` and the Sprint 2 agent-ingress settings (D-S210 keys; read the merged Gateway config); extend the `svc_config_set` allowlist with the Sprint 2 keys the UAT needs (`Mtls:RenewalGraceMinutes` already; the ingress client-certificate mode); point the agent overlay at `https://127.0.0.1:15443`; make the agent trust the lab server certificate and the lab agent CA by the mechanism D-ML14 rules. If it rules Root: add helper verbs `trust_root_add`/`trust_root_remove` (thumbprint must equal the lab TLS CA or the lab Nodes CA) and MACHINE tools of the same names to C4 and `ToolPolicy`.

**Tests:** a Docker test that `http_request via=gateway` reaches `/api/v1/agents/enroll` over TLS; `trust_root_*` refuses a non-lab thumbprint (if added).

**Checks:** as L24 for the lab projects, plus `dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker=true"` with Docker running.

**Docs owed:** meridian-lab guide section "TLS in the lab".

### L26. UAT specs for Sprint 2 flows

- **Branch / worktree / commit:** `ml/l26-agent-specs` / `meridian-lab-l26` / `test(lab): uat specs for agent enrollment, heartbeat, restart and renewal`
- **Depends on:** L25. **Security review:** yes (specs drive MACHINE tools).
- **Rules:** C7 format; real table/column names from the merged Nodes EF model; SQL params bound; every row at least one non-human required clause; tables in docs with spaces around pipes.

**Files**

1. `docs/guides/uat-specs/agent-console.toml`, section `agent-console`, `requires = ["docker", "windows", "machine"]`, rows: `reset` (`agent_footprint_reset`; certstore `machine_my` with `issuer_thumbprint` = lab CA `max_rows` 0), `enroll` (`nodes_token_mint`, `agent_token_write`, `agent_console_start`; db node row for platform windows; `certstore machine_my` 1 cert from lab CA with private key; `registry` `EnrollmentToken` not present after enroll; `state_file` has the node id; `agent_log contains "nroll"`), `heartbeat` (db `NodeHealth` last heartbeat within 120 s, `timeout_ms` 120000; `hub connected` for the node), `restart-no-reenroll` (`agent_console_stop`, `agent_console_start`; `delta` 0 on the node count; no new certificate row), `no-ca-in-root` (`certstore machine_root` `issuer_thumbprint` lab CA `max_rows` 0), `renew-within-grace` (`agent_config_set Agent:Security:CertificateRenewalThresholdDays 90`, restart; a new certificate row; old row revoked with `GraceUntil` in the future; hub reconnects), `after-grace` (`nodes_cert_time_shift` on the old certificate fields `grace_until` −120 minutes; an old-certificate request through `http_request` is refused; the agent stays connected on the new certificate).
2. `docs/guides/uat-specs/agent-service.toml`: the same flows with `agent_service_install/start/stop` and the `agent_log` Event Log source.
3. `ShippedSpecsValidateTests` covers both files.

**Checks**

```powershell
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~ShippedSpecsValidateTests"
```

**Docs owed:** automated-uat guide: a table of the shipped specs.

### L27. First live UAT run (the user)

- **Owner:** the user, guided by the `lab-uat` skill; not a packet-coder packet. Needs D-ML13 and the user's UAC click.
- **Steps:** `install.ps1 -LabMachine` (once, elevated); `lab_build` the campaign tip; `uat_run lab-selftest`, then `agent-console`, then `agent-service`; attest any human clause; copy each `ledger.md` and `run.json` to `docs/analysis/meridian-lab/worknotes/uat-<run_id>/`.
- **Done when:** all rows PASS, or each non-PASS row has a follow-up packet or decision in the README.

## Wave 6: close-out

### L28. Close-out

- **Agent:** `documentation-writer` for the docs, then `ops` for the board handoff. **Branch / worktree / commit:** `ml/l28-closeout` / `meridian-lab-l28` / `docs: close out meridian-lab campaign`
- **Depends on:** campaign review `ship`, L27. **Security review:** security-advisor read.
- **Files:** `docs/PROJECT-STATE.md` (the only packet that edits it): "meridian-lab: confirmed working" only with S-L1 to S-L6 green and the L27 ledgers in `worknotes/`; otherwise "code exists". README status table to Done; RESUME to final state; a note for the orchestrator listing which Sprint 2 UAT items this campaign covered (the orchestrator updates the Sprint 2 ledger; this packet does not).

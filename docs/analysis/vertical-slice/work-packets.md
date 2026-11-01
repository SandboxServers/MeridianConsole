# vertical-slice: contract and work packets

Ledger: [README.md](README.md). Resume card: [RESUME.md](RESUME.md).

## Dispatch rules

- One packet, one worktree, one `packet-coder`. Worktree `/home/claude/meridianconsole/.claude/worktrees/vertical-slice-vsNN`, branch `vs/NN-<slug>`, created by ops from `origin/main` **after the previous wave has merged** (the wave 1 base was `2c4c19c`; wave 2 bases on `main` after #234, #229, #232 and VS-34 have merged on top of `5eead35`, which already holds Sprint 2 `6df3338`, #233, #230, #231 and #236). A packet marked "stacks on VS-MM" is created from VS-MM's branch instead, and its PR uses `--base vs/MM-<slug>` until VS-MM merges.
- **Merging main (D-38).** `main` is brought into PR branches by merge, never rebase or force-push: ops runs `git fetch origin && git merge origin/main` on the branch, a `packet-coder` resolves conflicts in that merge commit as the packet or ruling says, and the packet's checks run again before review. Reviewed SHAs stay in the branch history.
- Report path: `<worktree>/.claude/reports/vertical-slice/VS-NN-coder.md`. Return line: `VS-NN <done|blocked|failed> <sha|-> <report-path> <reason>`.
- Shipping: **one PR per packet**, squash-merged in the merge order given per wave, CI green, `security-review` label where the packet says "Security review: yes".
- **Merge authority (D-41).** Only the owner's main session merges, and only after the owner says yes to that PR. This applies to every PR, not only security-review ones. A green CI run, a `ship` verdict, or a yes given for another PR or an earlier head does not count. Every other session, the orchestrator included, stops at "ready to merge" and reports.
- Within a wave, dispatch every packet whose dependencies have merged, in one message. Packets whose dependencies are still open stay BlockedDependency until those merge.
- **Environment from 2026-10-28: Windows.** The campaign now runs on the owner's Windows machine. Worktrees are `E:\Code\MeridianConsole\.claude\worktrees\vertical-slice-<name>`, the shell is PowerShell, and `gh` is `C:\Program Files\GitHub CLI\gh.exe` (ops opens PRs with `gh pr create --body-file`, or `gh api` REST if GraphQL is refused). Packets written before this date show the Linux container paths and bash below; translate the paths, run the same `git`/`dotnet` commands, and skip the `sed` CRLF steps (a Windows checkout writes CRLF already; check `git diff --stat` shows only intended lines). Every worktree shows `scripts/bootstrap-dev.sh`, `scripts/test-scripts.sh`, `scripts/verify-environment.sh` and `deploy/scripts/configure-cloudflare-dns.sh` as modified (file-mode noise from the Windows checkout): never stage or commit them unless the packet edits them. Tests still use `WebApplicationFactory` with EF InMemory; no packet uses Testcontainers unless it says so.
- Environment before 2026-10-28 (Linux container, bash; `pwsh` is not assumed). Start every shell call with:

```bash
cd /home/claude/meridianconsole/.claude/worktrees/vertical-slice-vsNN && export DOTNET_ROOT=/home/claude/.dotnet PATH=/home/claude/.dotnet:$HOME/.dotnet/tools:$PATH CI=true DOTNET_NOLOGO=1
```

- `CI=true` matches CI: warnings are errors and the full analyzer set runs. There is no Docker daemon: never use Testcontainers. Tests use `WebApplicationFactory` with EF InMemory.
- **Lock files (D-39).** After any `PackageReference` or `ProjectReference` change, run `dotnet restore Dhadgar.sln --force-evaluate`: always the whole solution, because every project that references the changed one gets a new lock file too. For every `packages.lock.json` that `git status` shows as changed, run `sed -i 's/\r\?$/\r/' <file>` (lock files are CRLF), then `dotnet restore Dhadgar.sln --locked-mode` must pass, and every changed lock file is committed with the packet. When merging `main` into a branch, resolve each conflicted lock file by taking `main`'s copy (`git checkout origin/main -- <file>`), finish the merge, then run the two restores above. Never hand-edit lock-file JSON. Never run a bare `dotnet publish -r <rid>` or `dotnet restore -r <rid>` (D-S241: it rewrites lock files with RID sections); publish with a RID-less `--locked-mode` restore followed by `--no-restore`.
- **Line endings.** Keep each existing file's line endings. New `.cs`, `.csproj` and `.json` files under `src/` and `tests/` are CRLF (finish with `sed -i 's/\r\?$/\r/' <file>`). New `.md`, `.yml` and `.ts`/`.tsx` files match their neighbours (check with `grep -c $'\r' <neighbour>`).
- Windows-only code builds on Linux; Windows-only tests use `[SkippableFact]` with `Skip.IfNot(OperatingSystem.IsWindows())` and are skipped here and in CI. A packet with Windows-only behaviour says how it is verified.
- Seam tests that cannot pass yet use `[Fact(Skip = "Pending VS-NN: <reason>")]` (D-13). The writing packet runs them once with the skip removed and quotes the failure line in its report; packet VS-NN removes the skip. The quoted failure must be the seam reason (no route, wrong status from the server, missing event), never an exception thrown inside the test's own client.
- **A seam test that asserts a rejection must prove the server side rejected it** (VS-05 review-1 and security-1 N1). Passing because a client-side guard threw proves nothing: `ControlPlaneClient` refused to send without a certificate, and the test stayed green while the Gateway and Nodes accepted anyone. Every rejection assertion therefore:
  1. reads the status from the server's response: `response.StatusCode`, `HttpRequestException.StatusCode`, a ProblemDetails `errorCode`, a hub ack `ErrorCode` (D-28) or the connection's close reason. Never `Assert.ThrowsAnyAsync<Exception>`, and never an exception type the client can throw on its own;
  2. proves which server answered. If the backend rejected it, assert the backend received the call: the recording forwarder in `tests/Dhadgar.AgentSeam.Tests` `GatewayTestFactory` (VS-07R) recorded a request for that cluster and path, or a recording handler or middleware on the backend factory saw it. If the Gateway rejected it, assert the forwarder recorded **no** request for that path;
  3. has a positive twin in the same class that differs only in the variable under test (certificate present, right org, valid payload) and passes, so a 401 or 404 is about that variable and not a missing route or JWT.
- **Client-certificate test shims stay in the test project that owns them.** A startup filter or middleware that turns a test header (`X-Seam-Client-Cert`, `X-Test-Client-Cert`) into `Connection.ClientCertificate` lives in that test project, is registered only in `ConfigureTestServices`, and is never placed in `src/`, in the shared `tests/Dhadgar.Testing` library (VS-32), or behind `InternalsVisibleTo`. A test that needs one copies or reuses the shim inside its own project.
- **D-43 overrides the paths in the next rule and in every packet below:** `SeamJwt` is `src/Shared/Dhadgar.Testing/Authentication/SeamJwt.cs`, namespace `Dhadgar.Testing.Authentication`, referenced as `<ProjectReference Include="..\..\src\Shared\Dhadgar.Testing\Dhadgar.Testing.csproj" />` plus `<Using Include="Dhadgar.Testing.Authentication" />` (already in `Dhadgar.Gateway.Tests` and `Dhadgar.AgentSeam.Tests` on `main`). Read `tests/Dhadgar.Testing` as `src/Shared/Dhadgar.Testing` and `Dhadgar.Testing.Auth` as `Dhadgar.Testing.Authentication`.
- **Test JWTs come from one helper (D-22).** Mint and accept test user tokens only with `SeamJwt` (`CreateUserToken(userId, organizationId)`, `Apply(services)`). VS-32R creates it at `tests/Dhadgar.Testing/Auth/SeamJwt.cs` from #231's copy on `main` (`git show 6895cd1:tests/Dhadgar.Seams.Tests/Infrastructure/SeamJwt.cs`; VS-34 deletes the working copy, the commit keeps it) (namespace `Dhadgar.Testing.Auth`, global `<Using>` in the consuming test projects). Never add another token minter or another `PostConfigure<JwtBearerOptions>` test-key block. Service test factories that use a test authentication scheme (the `NodesWebApplicationFactory` pattern) are not JWT helpers and stay as they are.
- **Agent-originated data is bounded by C-2 "Limit enforcement" (D-23).** A packet that receives agent data (hub methods, events carrying agent text, agent HTTP bodies) enforces its rows with one test per bound. A packet that sends agent data truncates free text to the same constants.

## The contract

Everything below is the single source of truth for both sides of each seam. Types live only in `src/Shared/Dhadgar.Contracts`; never copy one into a service or the agent.

### C-1 Routes

| Public path (through the Gateway) | Gateway route id, policy, transform | Service and service path | Service auth | Success | Errors (`errorCode`) |
| --- | --- | --- | --- | --- | --- |
| `POST /api/v1/agents/enroll` | `agents-enroll-route`, Anonymous, strip `/api/v1` | Nodes `POST /agents/enroll` | token in body | 201 `EnrollNodeResponse` | 400 `invalid_platform`/`invalid_public_key`, 401 `invalid_token` |
| `GET /api/v1/agents/ca-certificate` | `agents-ca-route` (VS-07R), Anonymous, strip `/api/v1`, `Order: 22` (before `agents-enroll-route` 25 and `agents-route` 30) | Nodes `GET /agents/ca-certificate` | none | 200 `application/x-pem-file` | 400 `ca_not_initialized` |
| `POST /api/v1/agents/{nodeId}/certificates/renew` | `agents-route`, `AgentCertificate` (Sprint 2), strip `/api/v1` | Nodes `POST /agents/{nodeId}/certificates/renew` | mTLS, `AgentPolicy` | 200 `RenewCertificateResponse` | 400 `invalid_public_key` (also for a `PublicKey` over 4096 chars, checked before decoding), 401 `certificate_not_current`, 403, 404 `node_not_found` |
| `POST /api/v1/agents/{nodeId}/heartbeat` (legacy HTTP, kept per ADR-0008) | `agents-route` | Nodes `POST /agents/{nodeId}/heartbeat` | mTLS, `AgentPolicy` | 200 | 400, 403, 404 |
| `/hubs/agent` (negotiate and connection) | `agent-hub-route` (Sprint 2), `AgentCertificate`, no transform | Nodes `AgentHubProtocol.Path` | mTLS, `AgentPolicy`, header `X-Agent-Protocol` = `AgentHubProtocol.CurrentVersion`, missing counts as "1" (D-32) | connected | 401/403 missing/invalid certificate; connection aborted on wrong protocol (Sprint 2) or unknown/decommissioned node (VS-08R) |
| `/api/v1/organizations/{organizationId}/nodes/{**}` | `nodes-org-route`, TenantScoped, strip `/api/v1` | Nodes `/organizations/{organizationId}/nodes/...` | JWT, `TenantScoped` | existing | existing |
| `/api/v1/organizations/{organizationId}/enrollment/{**}` | `enrollment-route`, TenantScoped, strip `/api/v1` | Nodes `/organizations/{organizationId}/enrollment/...` | JWT, `TenantScoped` | 201/200 | existing |
| `/api/v1/organizations/{organizationId}/servers/{**}` | `servers-org-route` (VS-07, replaces `servers-route`), TenantScoped, strip `/api/v1` | Servers `/organizations/{organizationId}/servers/...` | JWT, `TenantScoped` | 200/201/204 | 400 validation, 404 `server_not_found`, 409 `insufficient_capacity`/`invalid_node` |
| `POST /api/v1/organizations/{organizationId}/servers/{serverId}/start` and `/stop` | `servers-org-route` | Servers same path minus `/api/v1` | JWT | **202** (VS-23) | 404 `server_not_found`, 409 `node_offline`, 409 `invalid_state` |
| `/api/v1/organizations/{organizationId}/servers/{serverId}/console/{**}` | `console-org-route` (VS-07, replaces `console-api-route`), TenantScoped, strip `/api/v1`, `Order: -1` | Console `/organizations/{organizationId}/servers/{serverId}/console/...` | JWT | 200 | 404 |
| `/hubs/console` | `console-hub-route`, TenantScoped (Gateway reads the JWT from the `access_token` query on `/hubs/console`, VS-04) | Console `/hubs/console` | JWT (also from `access_token` query) | connected | 401 |
| not routed (Gateway must 404) | none | Nodes `POST /internal/organizations/{organizationId}/nodes/{nodeId}/commands` | JWT `TenantScoped`; Servers forwards the caller's token | **202** `DispatchAgentCommandResponse` | 400 `invalid_command_type`/`invalid_payload` (payload fails the typed `AgentJson` parse or a C-2 limit, or `ServerId` disagrees with the payload), 404 `node_not_found`, 409 `node_offline`, 409 `server_not_on_node` (server command without a Claimed reservation for that server on that node) |
| `/api/v1/organizations/{organizationId}/nodes/{nodeId}/reservations/{**}` (Gateway must refuse) | `nodes-reservations-block` (VS-07R, D-45), `DenyAll`, `Order: 1` (before `nodes-org-route`) | none | - | never forwarded | 401/403 from the Gateway |
| not routed (internal; Servers calls Nodes directly, D-45) | none | Nodes `POST /organizations/{org}/nodes/{node}/reservations`, `GET .../reservations`, `GET .../reservations/capacity` | JWT, `TenantScoped` | 201/200 | existing |
| not routed (internal) | none | Nodes `GET /reservations/{token}`, `POST /reservations/{token}/claim`, `DELETE /reservations/{token}` | JWT, policy `ReservationCaller` (VS-36): a user principal with `org_id`; the reservation's node must belong to that org | 200/200/204 | 401 no identity, 403 agent identity or no `org_id`, 404 `reservation_not_found` (also for another org's reservation), 400 `reservation_expired`/`reservation_claimed`/`reservation_released`/`reservation_already_released` (existing) |

Error shape everywhere: RFC 7807 from `Dhadgar.ServiceDefaults.Problems.ProblemDetailsHelper`, `{ "type", "title", "status", "detail", "errorCode", "traceId" }`. Clients branch on `errorCode`. Hubs never throw `HubException` for business results: agent → Nodes methods return `AgentHeartbeatAck` or `AgentHubAck` with an `AgentHubErrorCodes` value (D-28); protocol and identity failures abort the connection.

### C-2 Agent wire types

Two files, one home each (D-26), after VS-03R:

- **Hub surface:** `src/Shared/Dhadgar.Contracts/Nodes/AgentHubContracts.cs`, shipped by Sprint 2 (D-S21) and extended by VS-03R. Block C-2a.
- **Messages, payloads, events, limits:** `src/Shared/Dhadgar.Contracts/Agents/`, from VS-03 (`eabdbe5`) as changed by VS-03R. Block C-2b.

Synced on 2026-10-27 to `main` `6df3338` and the Sprint 2 reconcile ruling. The code is the reference for anything these blocks abbreviate (XML docs, remarks). Until VS-03R merges, `vs/03-agent-contracts` still has the old shapes.

#### C-2a Hub surface (`Dhadgar.Contracts.Nodes`)

```csharp
namespace Dhadgar.Contracts.Nodes;

using Dhadgar.Contracts.Agents;

public static class AgentHubProtocol                    // Sprint 2
{
    public const string Path = "/hubs/agent";           // same public and service path
    public const string VersionHeader = "X-Agent-Protocol";
    public const string CurrentVersion = "1";           // a missing header counts as "1" (D-32)
    public const string NodeIdClaim = "node_id";
    public static string NodeGroup(Guid nodeId) => $"node:{nodeId:D}";

    // VS-03R. Nodes HubOptions.MaximumReceiveMessageSize (VS-08R, D-30). Size of one SignalR JSON frame AFTER
    // escaping: the limits count unescaped chars or UTF-8 bytes, and a control or non-ASCII char escapes to
    // 6 bytes (\uXXXX), so 64 KiB of content can be 384 KiB on the wire.
    public const int MaxReceiveMessageBytes = 512 * 1024;
}

public static class AgentHubErrorCodes
{
    public const string NodeNotFound = "node_not_found";             // Sprint 2
    public const string NodeDecommissioned = "node_decommissioned";  // Sprint 2
    public const string InvalidHeartbeat = "invalid_heartbeat";      // Sprint 2
    public const string HeartbeatFailed = "heartbeat_failed";        // Sprint 2
    public const string InvalidTelemetry = "invalid_telemetry";      // VS-03R
    public const string InvalidResult = "invalid_result";            // VS-03R
    public const string InvalidStatus = "invalid_status";            // VS-03R
    public const string InvalidConsoleBatch = "invalid_console_batch"; // VS-03R
    public const string UnknownCommand = "unknown_command";          // VS-03R
    public const string ServerNotOnNode = "server_not_on_node";      // VS-03R
    public const string NotSupported = "not_supported";              // VS-03R; stub answer until the method is built
}

public static class AgentHeartbeatLimits                // Sprint 2
{
    public const int MaxAgentVersionLength = 50;        // also bounds PingResult.AgentVersion (VS-03R)
    public const int MaxHealthIssues = 20;
    public const int MaxHealthIssueLength = 256;
    public const int MaxActiveGameServers = 10_000;
}

/// <summary>Implemented by Nodes' AgentHub. Called by the agent.</summary>
public interface IAgentHub
{
    Task<AgentHeartbeatAck> Heartbeat(AgentHeartbeat heartbeat);        // Sprint 2, unchanged
    Task<AgentHubAck> Telemetry(AgentTelemetry telemetry);              // VS-03R stub; VS-08R implements
    Task<AgentHubAck> CommandResult(AgentCommandResult result);         // VS-03R stub; VS-33 implements
    Task<AgentHubAck> ServerStatusChanged(AgentServerStatus status);    // VS-03R stub; VS-33 implements
    Task<AgentHubAck> ConsoleOutput(AgentConsoleBatch batch);           // VS-03R stub; VS-33 implements
}

/// <summary>Implemented by the agent. Called by Nodes through IHubContext&lt;AgentHub, IAgentHubClient&gt;.</summary>
public interface IAgentHubClient
{
    Task ReceiveCommand(AgentCommandEnvelope command);  // VS-03R: was string (D-29)
    Task ReceivePing(string message);                   // Sprint 2, unchanged; bounded by MaxNonceLength
}

public sealed record AgentHeartbeat(                    // Sprint 2, unchanged; no NodeId (D-S23)
    double CpuUsagePercent,
    double MemoryUsagePercent,
    double DiskUsagePercent,
    int ActiveGameServers,
    string? AgentVersion,
    IReadOnlyList<string>? HealthIssues);

public sealed record AgentHeartbeatAck(bool Acknowledged, DateTimeOffset ServerTime, string? ErrorCode = null); // Sprint 2

/// <summary>Business result of every other agent → Nodes hub call (D-28). Never thrown.</summary>
public sealed record AgentHubAck(bool Accepted, string? ErrorCode = null);   // VS-03R
```

#### C-2b Messages, payloads, events (`Dhadgar.Contracts.Agents`)

```csharp
namespace Dhadgar.Contracts.Agents;

// VS-03R deletes class AgentProtocol (duplicated AgentHubProtocol) and Agents/AgentHubContracts.cs.

/// Strings are UTF-16 chars, bytes are UTF-8 bytes of the unescaped string, unless stated otherwise.
public static class AgentMessageLimits
{
    public const int MaxCommandTypeLength = 128;
    public const int MaxPayloadJsonBytes = 64 * 1024;       // UTF-8 bytes of AgentCommandEnvelope.PayloadJson
    public const int MaxResultJsonBytes = 64 * 1024;
    public const int MaxErrorMessageLength = 1024;          // also bounds AgentServerStatus.Message
    public const int MaxCorrelationIdLength = 128;
    public const int MaxConsoleLinesPerBatch = 200;
    public const int MaxConsoleLineLength = 4096;           // chars; longer lines are truncated by the sender
    public const int MaxConsoleBatchChars = 64 * 1024;      // sum of Text lengths in one batch
    // VS-03R deletes MaxHeartbeatWarnings, MaxWarningLength, MaxAgentVersionLength and MaxServersPerHeartbeat:
    // the heartbeat is Sprint 2's and is bounded by AgentHeartbeatLimits (C-2a).
    public const int MaxTelemetryMetrics = 500;
    public const int MaxMetricNameLength = 128;
    public const int MaxNonceLength = 64;                   // PingPayload.Nonce, PingResult.Nonce, ReceivePing(nonce)
    public const int MaxCommandLifetimeSeconds = 60;        // ExpiresAt - IssuedAt
    public const int MaxClockSkewSeconds = 30;              // IssuedAt may be at most now + 30 s
    public const int MaxMinecraftVersionLength = 32;
    public const string MinecraftVersionPattern = "^[0-9A-Za-z._-]{1,32}$";
    public const int MaxServerJarUrlLength = 2048;
    public const int ServerJarSha1HexLength = 40;           // lower- or upper-case hex
    public const int MinServerMemoryMb = 512;
    public const int MaxServerMemoryMb = 32 * 1024;
    public const int MinServerPort = 1024;
    public const int MaxServerPort = 65535;
    public const int MinGracefulTimeoutSeconds = 1;
    public const int MaxGracefulTimeoutSeconds = 300;

    // VS-09 appends these two (security-2: ErrorCode had no bound):
    public const int MaxErrorCodeLength = 64;               // AgentCommandResult.ErrorCode, AgentCommandCompleted.ErrorCode
    public const string ErrorCodePattern = "^[A-Za-z0-9._-]{1,64}$";
}

public static class AgentCommandTypes
{
    public const string Ping = "Ping";
    public const string StartServer = "StartServer";
    public const string StopServer = "StopServer";
    public const string ServerStatus = "ServerStatus";
    public static IReadOnlyCollection<string> All { get; } = [Ping, StartServer, StopServer, ServerStatus];
    public static bool IsKnown(string? commandType) =>
        commandType is not null && All.Contains(commandType, StringComparer.Ordinal);
}

/// <summary>JSON options for PayloadJson and ResultJson on both sides (camelCase, enums as numbers).
/// Lenient about unknown members (D-40), strict about everything else, and read-only: unknown properties
/// are skipped; a missing required property, a null in a non-nullable member, a duplicate property
/// or nesting deeper than 8 throws JsonException.</summary>
public static class AgentJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip, // D-40 (supersedes D-21's Disallow)
            AllowDuplicateProperties = false,                          // D-40: one meaning per member
            MaxDepth = 8,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

// IAgentHub and IAgentHubClient live in Dhadgar.Contracts.Nodes (C-2a, D-26).

public sealed record AgentCommandEnvelope(          // Nodes → agent: keeps NodeId and OrganizationId
    Guid CommandId,
    string CommandType,
    Guid NodeId,
    Guid OrganizationId,
    Guid? InitiatedByUserId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string PayloadJson,
    string Signature,          // base64, ECDSA P-256 SHA-256, IEEE P1363 (64 bytes)
    string SigningKeyId,       // AgentCommandSigning.ComputeKeyId(SPKI of the signing key)
    string? CorrelationId);

public enum AgentCommandStatus { Succeeded = 0, Failed = 1, Rejected = 2, TimedOut = 3, Cancelled = 4 }

// Agent → Nodes records carry no NodeId: Nodes takes it from the certificate (D-S23, D-27).
public sealed record AgentCommandResult(
    Guid CommandId,
    AgentCommandStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? ResultJson,
    string? ErrorMessage,
    string? ErrorCode,
    string? CorrelationId);

public enum AgentServerState { Stopped = 0, Installing = 1, Starting = 2, Running = 3, Stopping = 4, Crashed = 5 }

// VS-03R deletes AgentServerProcess and the Agents AgentHeartbeat: the heartbeat is Sprint 2's (C-2a);
// per-server state travels in AgentServerStatus.

public sealed record AgentServerStatus(
    Guid ServerId, AgentServerState State, int? ExitCode, string? Message, DateTimeOffset Timestamp);

public sealed record AgentConsoleLine(DateTimeOffset Timestamp, bool IsError, string Text);

public sealed record AgentConsoleBatch(Guid ServerId, IReadOnlyList<AgentConsoleLine> Lines);

public sealed record AgentTelemetry(DateTimeOffset Timestamp, IReadOnlyDictionary<string, double> Metrics);

// Command payloads (PayloadJson / ResultJson, serialized with AgentJson.Options)
public sealed record PingPayload(string? Nonce = null);   // default: "{}" is a valid Ping payload (review-2 N1)
public sealed record PingResult(string AgentVersion, DateTimeOffset AgentTime, string? Nonce); // AgentVersion ≤ AgentHeartbeatLimits.MaxAgentVersionLength
public sealed record StartServerPayload(
    Guid ServerId, string MinecraftVersion, string ServerJarUrl, string ServerJarSha1,
    int MemoryMb, int Port, bool EulaAccepted);
public sealed record StopServerPayload(Guid ServerId, int GracefulTimeoutSeconds);
public sealed record ServerStatusPayload(Guid ServerId);
public sealed record ServerStatusResult(Guid ServerId, AgentServerState State, int? OsPid, long UptimeSeconds);

// MassTransit events published by Nodes: keep NodeId, set by Nodes from the certificate
public sealed record AgentCommandCompleted(
    Guid CommandId, Guid NodeId, Guid OrganizationId, string CommandType, Guid? ServerId,
    AgentCommandStatus Status, string? ResultJson, string? ErrorCode, string? ErrorMessage,
    string? CorrelationId, DateTimeOffset CompletedAt);

public sealed record AgentServerStatusChanged(
    Guid NodeId, Guid OrganizationId, Guid ServerId, AgentServerState State,
    int? ExitCode, string? Message, DateTimeOffset OccurredAt);

public sealed record ServerConsoleOutput(
    Guid ServerId, Guid OrganizationId, Guid NodeId, IReadOnlyList<AgentConsoleLine> Lines);
```

JSON rules (both sides):

- Every positional record parameter is **required** in `AgentJson` unless it has a default value. So `PingPayload(string? Nonce = null)` accepts `{}`, VS-29's `JavaRuntimes = null` is optional, and any future optional field must get a default. Unknown properties are **ignored** (D-40, superseding D-21); duplicate properties throw. Lenient means tolerating unknown members only: every C-2 limit, required field, `Enum.IsDefined` check and null check still applies. A field whose meaning an older receiver must not ignore (a new restriction or safety setting) still bumps `AgentHubProtocol.CurrentVersion`, and Nodes sends it only to connections that announced the new version (D-40).
- A payload Nodes signs carries only members Nodes understood: Nodes parses the caller's `PayloadJson` with `AgentJson.Options`, validates the typed record, and signs `JsonSerializer.Serialize(typed, AgentJson.Options)`, never the caller's string (D-40; VS-15).
- PayloadJson is untrusted on the agent: verify the signature over the **raw** `PayloadJson` string before parsing it. After parsing, check every enum with `Enum.IsDefined` (numbers are not range-checked by the serializer).
- SignalR binds hub-method arguments with its own JSON hub protocol options, **not** `AgentJson`. A non-nullable string or list can arrive null, unknown properties are ignored and enums are not range-checked. Hub receivers therefore null-check and `Enum.IsDefined`-check every field (table below). Do not swap the hub protocol options.

Command acceptance on the agent (the `AgentCommandEnvelope` remarks in the code; VS-13 filter, VS-14 validator). The agent answers `Rejected` unless all of these hold:

1. `AgentCommandTypes.IsKnown(CommandType)`.
2. `NodeId` equals the agent's own node id and `OrganizationId` equals the enrolled organization (neither `Guid.Empty`).
3. `0 < ExpiresAt - IssuedAt <= MaxCommandLifetimeSeconds`; `IssuedAt <= now + MaxClockSkewSeconds`; `now < ExpiresAt`.
4. `SigningKeyId` equals the id of the key pinned at enrollment (D-09), ordinal. The agent never selects or fetches a key by this value.
5. `Signature` decodes (`Convert.TryFromBase64String`) to exactly `AgentCommandSigning.SignatureBytes` (64) bytes and verifies with `DSASignatureFormat.IeeeP1363FixedFieldConcatenation` over `AgentCommandSigning.TryBuildSigningInput(envelope, out input)`. `TryBuildSigningInput` returning false is a rejection, not an exception.
6. The replay cache has not seen `CommandId`. The cache key is `CommandId`, never the signature (ECDSA signatures are malleable), and an entry is kept until `ExpiresAt + MaxClockSkewSeconds`. A command is registered only after every other check passed.

Hub rules (Nodes side; Sprint 2, VS-08R and VS-33):

- The connection's node is the `AgentHubProtocol.NodeIdClaim` claim from the certificate (set by `MtlsMiddleware`). Agent → Nodes payloads carry no node id (D-27), so there is no `node_mismatch` case.
- On connect (Sprint 2): a `X-Agent-Protocol` other than `CurrentVersion` (missing counts as "1") aborts; a missing `node_id` claim or a certificate that is not current aborts; otherwise the connection joins `AgentHubProtocol.NodeGroup(nodeId)`. VS-08R adds: an unknown or decommissioned node aborts, and the connection is recorded in `IAgentConnectionRegistry`.
- Every method returns an ack (D-28). Business failures are `Accepted = false` with an `AgentHubErrorCodes` code; a method never throws `HubException` for them.
- `ServerStatusChanged` and `ConsoleOutput` require a **Claimed** capacity reservation on that node whose `ServerId` equals `serverId.ToString("D")`; otherwise ack `server_not_on_node`.
- `CommandResult` for an unknown command id, or one belonging to another node, answers ack `unknown_command`; a second result for a finished command is ignored and answers `Accepted = true`.
- `AgentCommandCompleted` takes `OrganizationId`, `CommandType`, `ServerId` and `CorrelationId` from Nodes' own `AgentCommand` row, never from the agent's result. `AgentServerStatusChanged` and `ServerConsoleOutput` take `OrganizationId` from the node record (`Context.Items["OrganizationId"]`, set on connect).

#### Limit enforcement (D-23)

Every receiver of agent-originated data enforces these limits; a conforming sender never trips them. **Receivers reject; they never truncate** (truncating on receipt hides sender bugs and lets a compromised agent store data that differs from what it sent). **Senders truncate** free text so they stay inside the limits. Every validator uses the constants above, never a local copy or a stricter or looser local rule. One test per row: at the limit passes, limit + 1 fails with the listed code.

| Message (direction) | Field rules | Receiver (packet) | Violation |
| --- | --- | --- | --- |
| Any hub frame (agent → Nodes) | escaped frame ≤ `AgentHubProtocol.MaxReceiveMessageBytes` | SignalR `MaximumReceiveMessageSize` (VS-08R; `main` has 64 KiB) | connection closed by SignalR |
| `AgentHeartbeat` (Sprint 2) | as `AgentHeartbeatValidator` on `main` enforces with `AgentHeartbeatLimits` | `AgentHeartbeatValidator` (Sprint 2, done) | ack `invalid_heartbeat` |
| `AgentTelemetry` | `Metrics` non-null, ≤ `MaxTelemetryMetrics`; each name 1..`MaxMetricNameLength` with no control chars; each value finite; `Timestamp` not `default` | `AgentTelemetryValidator` (VS-08R) | ack `invalid_telemetry` |
| `AgentCommandResult` | `Status` defined; `ResultJson` null or ≤ `MaxResultJsonBytes` UTF-8 bytes; `ErrorMessage` null or ≤ `MaxErrorMessageLength`; `ErrorCode` null or matches `ErrorCodePattern`; `CorrelationId` null or ≤ `MaxCorrelationIdLength` (bounded, then ignored: the event uses the row's value) | VS-33 | ack `invalid_result` (row unchanged) |
| `AgentServerStatus` | `State` defined; `Message` null or ≤ `MaxErrorMessageLength` | VS-33 | ack `invalid_status` |
| `AgentConsoleBatch` | `Lines` non-null, 1..`MaxConsoleLinesPerBatch`; each line non-null, `Text` non-null and ≤ `MaxConsoleLineLength`; sum of `Text` lengths ≤ `MaxConsoleBatchChars` | VS-33 | ack `invalid_console_batch` |
| `ServerConsoleOutput` event (Nodes → Console, agent-originated text) | `Lines.Count` ≤ `MaxConsoleLinesPerBatch`, each `Text` ≤ `MaxConsoleLineLength`; text never logged; rendered only as text | Console consumer (VS-17), Panel (VS-26) | event dropped with a warning that carries no text |
| `AgentCommandCompleted` event (agent-originated `ResultJson`, `ErrorCode`, `ErrorMessage`) | `Status` defined before use; `ResultJson` parsed only with `AgentJson.Options` inside a `try`, failure = ignore the result body | Servers consumers (VS-23) | event skipped with a warning |
| `AgentCommandEnvelope` (Nodes → agent) | `CommandType` non-null, ≤ `MaxCommandTypeLength`; `PayloadJson` non-null, ≤ `MaxPayloadJsonBytes`; `CorrelationId` null or ≤ `MaxCorrelationIdLength`; `Signature` null/empty or exactly `AgentCommandSigning.SignatureBase64Length` (88) chars; `SigningKeyId` null/empty or exactly `AgentCommandSigning.KeyIdLength` (64) chars; then the acceptance rules above | agent `InboundCommandFilter` (VS-13: sizes, identity, time) and `CommandValidator` (VS-14: type, signature, key id, replay) | dropped with a log (filter) or `Rejected` result (validator) |
| `ReceivePing(nonce)` (Nodes → agent) | non-null, ≤ `MaxNonceLength` | agent `ControlPlaneClient` (VS-13) | dropped with a log; the nonce is not logged |
| Typed payloads (Servers → Nodes dispatch, then agent) | parse with `AgentJson.Options` by `CommandType`; `PingPayload.Nonce` ≤ `MaxNonceLength`; `StartServerPayload`: `MinecraftVersion` matches `MinecraftVersionPattern`, `ServerJarUrl` absolute `https` and ≤ `MaxServerJarUrlLength`, `ServerJarSha1` exactly `ServerJarSha1HexLength` hex chars, `MemoryMb` and `Port` in range; `StopServerPayload.GracefulTimeoutSeconds` in `MinGracefulTimeoutSeconds..MaxGracefulTimeoutSeconds` (rejected, never clamped) | Nodes dispatcher (VS-15) before signing; agent handlers again (VS-16, VS-19, VS-20) | `invalid_payload` (Nodes); `Failed` result with a `[Category.Code]` error (agent) |
| `RenewCertificateRequest.PublicKey` (agent → Nodes, HTTP) | as Sprint 2's renew endpoint enforces it (D-36) | Nodes renew endpoint (Sprint 2, done) | 400 `invalid_public_key` |

### C-3 Command signing (`AgentCommandSigning`, VS-03; used by VS-09 and VS-14)

```csharp
namespace Dhadgar.Contracts.Agents;

public static class AgentCommandSigning
{
    public const string Domain = "dhadgar-cmd-v1";

    // VS-09 appends these three (security-2: Signature and SigningKeyId had no bound):
    public const int SignatureBytes = 64;           // IEEE P1363 P-256
    public const int SignatureBase64Length = 88;    // Convert.ToBase64String of 64 bytes, with "==" padding
    public const int KeyIdLength = 64;              // lower-case hex SHA-256

    // Signer side (Nodes). Throws on caller bugs.
    public static byte[] BuildSigningInput(AgentCommandEnvelope envelope);   // calls the overload below

    public static byte[] BuildSigningInput(
        Guid commandId, Guid nodeId, Guid organizationId, string commandType,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt, Guid? initiatedByUserId,
        string? correlationId, string payloadJson);

    // Verify side (agent) and pre-sign validation (Nodes dispatcher). Never throws for network input:
    // false for a null envelope, null CommandType or PayloadJson, CR/LF in CommandType or CorrelationId,
    // or a lone surrogate in any signed string.
    public static bool TryBuildSigningInput(AgentCommandEnvelope envelope, [NotNullWhen(true)] out byte[]? input);

    /// <summary>Lower-case hex SHA-256 of the DER SubjectPublicKeyInfo.</summary>
    public static string ComputeKeyId(ReadOnlySpan<byte> subjectPublicKeyInfo);
}
```

The signing input is **strict** UTF-8 (`new UTF8Encoding(false, throwOnInvalidBytes: true)`) of ten fields joined by `\n` (LF) with no trailing newline: `Domain`; `commandId` `"D"`; `nodeId` `"D"`; `organizationId` `"D"`; `commandType`; `issuedAt.ToUnixTimeMilliseconds()` (invariant culture); `expiresAt.ToUnixTimeMilliseconds()`; `initiatedByUserId?.ToString("D") ?? ""`; `correlationId ?? ""`; lower-case hex SHA-256 of the strict-UTF-8 `payloadJson`. In `BuildSigningInput`, `commandType` or `correlationId` containing `\n` or `\r` throws `ArgumentException`, and a lone surrogate in any string throws `EncoderFallbackException` (caller bugs; validators reject such input first). Receivers of network input use `TryBuildSigningInput` instead. Signature: `ECDsa.SignData(input, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)`, base64.

Golden vector (the VS-03 test pins it): commandId `11111111-1111-1111-1111-111111111111`, nodeId `22222222-…-2222`, organizationId `33333333-…-3333`, commandType `Ping`, issuedAt `2026-10-26T12:00:00Z` (`1793016000000`), expiresAt +60 s (`1793016060000`), initiatedByUserId null, correlationId `corr-1`, payloadJson `{"nonce":"abc"}` (SHA-256 `7685965bd35af178255728a43edb00ed348b2717b663ffc7376a547e131590f9`). Expected input string:

```text
dhadgar-cmd-v1
11111111-1111-1111-1111-111111111111
22222222-2222-2222-2222-222222222222
33333333-3333-3333-3333-333333333333
Ping
1793016000000
1793016060000

corr-1
7685965bd35af178255728a43edb00ed348b2717b663ffc7376a547e131590f9
```

SHA-256 of that input: `723f9365d61f8ac48c6acb7801e582204a05fc72e9a4fbfcb0467ca9add11c3a`. `ComputeKeyId` of the 91 bytes `0x01..0x5B`: `4678fe8a3cf5c74e029e7249e4313c06ab59cb2b07d6a7ab15c6d1850c2c8e25`.

### C-4 Dispatch, enrollment, renewal and reservation DTOs

```csharp
namespace Dhadgar.Contracts.Nodes;

// VS-03
public sealed record DispatchAgentCommandRequest(string CommandType, string PayloadJson, Guid? ServerId, string? CorrelationId);
public sealed record DispatchAgentCommandResponse(Guid CommandId, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

// VS-09: two parameters appended to the existing record
public record EnrollNodeResponse(
    Guid NodeId, Guid OrganizationId, string CertificateThumbprint, string Certificate, string CaCertificate,
    DateTimeOffset NotBefore, DateTimeOffset NotAfter,
    string CommandSigningPublicKey,   // base64 DER SubjectPublicKeyInfo, ECDSA P-256
    string CommandSigningKeyId);      // AgentCommandSigning.ComputeKeyId(...)

// Sprint 2 (on main, D-36; VS-10 superseded): the renewal records, unchanged
public record RenewCertificateRequest(string PublicKey);   // base64 DER SPKI of the agent's NEW key pair, at most 4096 chars
public record RenewCertificateResponse(
    string CertificateThumbprint, string Certificate /* PEM */, string CaCertificate /* PEM */,
    DateTimeOffset NotBefore, DateTimeOffset NotAfter);

// VS-22: moved unchanged from src/Dhadgar.Nodes/Models/ReservationModels.cs and Data/Entities
// CreateReservationRequest, ClaimReservationRequest, ReservationResponse, ReservationStatus
// ClaimReservationRequest.ServerId is the Servers server id as Guid.ToString("D").
```

### C-5 Servers and Console public API (MVP)

- `CreateServerRequest` (from #88, `Dhadgar.Contracts.Servers`) gains three trailing optional parameters in VS-22: `Guid? NodeId = null, string? MinecraftVersion = null, bool EulaAccepted = false`. MVP validation: `GameType` is `minecraft` (case-insensitive), `NodeId` required, `EulaAccepted` true, `MinecraftVersion` null or in the catalog (D-14), `MemoryLimitMb` 1024..16384, `DiskLimitMb` ≥ 1024.
- Start and stop return `202 Accepted` with no body. Restart and kill return `409` `not_supported_in_mvp`.
- Console hub (`/hubs/console`): methods `JoinServer(JoinServerRequest)`, `LeaveServer(LeaveServerRequest)`, `Ping()`; client events `history` (`ConsoleHistoryDto`), `output` (`ConsoleOutputDto`, one per line, in order), `joined`, `left`, `error` (string), `pong`. Group name `server-{serverId}`.

### C-6 Headers and configuration keys

| Key or header | Where | Meaning |
| --- | --- | --- |
| `X-Agent-Protocol` = `AgentHubProtocol.CurrentVersion` | agent → hub | Protocol version; missing counts as "1" (D-32) |
| `X-Client-Cert` (`AgentCertificateProfile.ForwardedCertificateHeader`) | Gateway → Nodes | the TLS client certificate, set by `AgentCertificateForwarding.Apply`; any incoming copy is stripped (Sprint 2) |
| `AgentIngress:ClientCertificateMode` | Gateway | Kestrel client-certificate mode (default `NoCertificate`; compose sets `AllowCertificate`; Sprint 2) |
| `Mtls:ForwardedCertificate:Enabled`, `Mtls:ForwardedCertificate:TrustedProxies` | Nodes | accept `X-Client-Cert` only from these CIDRs (D-31, D-S25, D-S210 A); no shared secret |
| `CommandSigning:KeyPath`, `CommandSigning:CommandTtlSeconds` (60, range 10..`AgentMessageLimits.MaxCommandLifetimeSeconds` = 60) | Nodes | signing key file (optional), command lifetime; a TTL above 60 would make every conforming agent reject every command |
| `Auth:Issuer`, `Auth:Audience`, `Auth:MetadataAddress`, `Auth:InternalBaseUrl`, `Auth:ClockSkewSeconds` | Gateway, Nodes, Servers, Console, Secrets | JWT validation (VS-04 helper) |
| `Services:Nodes:BaseUrl` | Servers | e.g. `http://nodes:8080` |
| `Services:Servers:BaseUrl` | Console | e.g. `http://servers:8080` (exists in #88) |
| `Agent:StateDirectory` (`AgentOptions.StateDirectory`) | agent | directory for `agent-state.json` (Sprint 2, D-37) |
| `Agent:EnrollmentToken` | agent | optional; Windows also reads `HKLM\SOFTWARE\Meridian Console\Agent\EnrollmentToken` |
| `Agent:Security:RequireSignedCommands` (true), `Agent:Security:CommandSigningPublicKey` | agent | D-09 |
| `Agent:Minecraft:AllowedDownloadHosts` (`["piston-data.mojang.com"]`), `Agent:Minecraft:MaxJarBytes` | agent (`MinecraftOptions`, VS-19) | D-19: server jar download |
| `Agent:Minecraft:JavaPath` (empty), `JavaDownloadEnabled` (true), `JavaRuntimePath`, `JavaDownloadHosts` (`["github.com"]`), `JavaRedirectHosts` (`["release-assets.githubusercontent.com", "objects.githubusercontent.com"]`), `MaxJavaArchiveBytes`, `MaxJavaExtractedBytes`, `MaxJavaArchiveEntries`, `JavaRuntimeRetentionDays` | agent (`JavaRuntimeOptions`, VS-29) | D-19: managed JRE; a non-empty `JavaPath` wins over everything (C-7) |

### C-7 Java runtime (D-19; VS-29 adds it on top of VS-03's files)

VS-03 built C-2. VS-29 then adds one record and one trailing optional parameter. Old JSON without `javaRuntimes` still deserializes (the parameter has a default, so `AgentJson` does not require it), and every `new StartServerPayload(...)` call with seven arguments still compiles. An agent built before VS-29 ignores `javaRuntimes` (`UnmappedMemberHandling.Skip`, D-40) and starts the server with its configured Java, or fails with its existing no-runtime error. No agent ships before the VS-27 UAT, and every UAT agent includes VS-29, so no protocol bump is needed.

```csharp
namespace Dhadgar.Contracts.Agents;

/// <summary>Values for JavaRuntimeArtifact.Os and .Architecture (Adoptium naming).</summary>
public static class JavaRuntimePlatforms
{
    public const string Windows = "windows";
    public const string Linux = "linux";
    public const string X64 = "x64";
    public const string Aarch64 = "aarch64";
}

/// <summary>
/// One pinned JRE archive. Servers' JavaRuntimeCatalog is the only producer. The agent never trusts
/// the archive without ArchiveSha256, and the whole payload is covered by the command signature (C-3).
/// </summary>
public sealed record JavaRuntimeArtifact(
    string Os,               // JavaRuntimePlatforms.Windows | Linux
    string Architecture,     // JavaRuntimePlatforms.X64 | Aarch64
    int MajorVersion,        // 21
    string Version,          // "21.0.12.1+1"; logs and marker file only, never part of a path
    string ArchiveUrl,       // https://github.com/adoptium/temurin21-binaries/releases/download/<tag>/<file>.zip
    string ArchiveSha256,    // 64 lower-case hex
    long ArchiveSizeBytes)   // exact size in bytes
{
    public const int MaxPerPayload = 8;
}

// In Agents/AgentCommandPayloads.cs (VS-29 appends the last parameter):
public sealed record StartServerPayload(
    Guid ServerId, string MinecraftVersion, string ServerJarUrl, string ServerJarSha1,
    int MemoryMb, int Port, bool EulaAccepted,
    IReadOnlyList<JavaRuntimeArtifact>? JavaRuntimes = null);
```

Pins as of 2026-10-26 (Temurin `jdk-21.0.12.1+1`, JRE zip; Servers `JavaRuntimeCatalog`, VS-29):

| Os | Architecture | ArchiveUrl | ArchiveSha256 | ArchiveSizeBytes |
| --- | --- | --- | --- | --- |
| `windows` | `x64` | `https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.12.1%2B1/OpenJDK21U-jre_x64_windows_hotspot_21.0.12.1_1.zip` | `d35f31e712f0fcf6ac5a093edc90204fbff22f720ba3950bd09d331d5e621636` | `48999141` |
| `windows` | `aarch64` | `https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.12.1%2B1/OpenJDK21U-jre_aarch64_windows_hotspot_21.0.12.1_1.zip` | `e82cc17e0bf89a25b0b0ed106d072f2ea420587d0a6870534b71b1dce3ae28c3` | `40078274` |

Both entries use `MajorVersion` 21 and `Version` `21.0.12.1+1`. There are no Linux entries (the Linux agent is parked, D-03).

Agent rules, in order (`JavaRuntimeResolver`, VS-29; installer VS-30; hardening VS-31):

1. **Override wins.** A non-empty `Agent:Minecraft:JavaPath` must be fully qualified, pass `IPathValidator.IsSafePath`, exist, and have the file name `java.exe` (Windows, case-insensitive) or `java`. Otherwise the result is `[Java.ConfiguredPathInvalid]`. The agent never falls back to a download, never makes a network call and ignores `JavaRuntimes`.
2. `JavaDownloadEnabled=false` → `[Java.NotConfigured]`.
3. Selection: exactly one artifact whose `Os` matches the current OS and whose `Architecture` matches `RuntimeInformation.OSArchitecture` (`X64` → `x64`, `Arm64` → `aarch64`). None → `[Java.NoRuntimeForPlatform]`; more than one → `[Java.AmbiguousRuntime]`; more than `MaxPerPayload` entries → `[Java.InvalidArtifact]`.
4. Artifact validation (`JavaRuntimeArtifactValidator`), all before any request, each failure `[Java.InvalidArtifact] <field>`: `MajorVersion` 17..99; `Version` matches `^[0-9]+(\.[0-9]+){0,4}(\+[0-9]+)?$` and is at most 32 chars; `ArchiveSha256` matches `^[0-9a-f]{64}$`; `ArchiveSizeBytes` is 1..`MaxJavaArchiveBytes`. `ArchiveUrl` is absolute `https`, port 443, no user info, no query or fragment, host equal (ordinal, ignore case, no subdomains) to an entry of `JavaDownloadHosts`, and its `Uri.AbsolutePath` matches `^/adoptium/temurin[0-9]{2}-binaries/releases/download/[A-Za-z0-9.%+_-]+/OpenJDK[0-9]{2}U-jre_(x64|aarch64)_(windows|linux)_hotspot_[0-9._]+\.zip$`, with the arch and os tokens equal to `Architecture` and `Os`.
5. Download (VS-30): `AllowAutoRedirect=false`. The agent follows at most 3 redirects (301, 302, 303, 307, 308) by hand. Every hop must be absolute `https` on port 443 with a host in `JavaDownloadHosts` ∪ `JavaRedirectHosts`. A relative `Location` resolves against the current URL and is checked again. A wrong `Content-Length`, more bytes than `ArchiveSizeBytes`, a different final size or a different SHA-256 fails, and the partial file is deleted.
6. Install (VS-30) under `JavaRuntimePath` only. Every path goes through `IPathValidator.ValidatePath(path, [JavaRuntimePath])`, and every zip entry also through `ValidatePath(dest, [stagingDir])`. The runtime directory is `{JavaRuntimePath}/{os}-{arch}-{sha256[..16]}` (no string from the payload except validated hex reaches a path). Marker `.meridian-runtime.json` is written last; a directory without a valid marker is deleted and reinstalled.
7. Permissions (VS-30 interface, VS-31 Windows): before the first write, `IJavaRuntimeDirectorySecurer.Secure(JavaRuntimePath)` must succeed. Windows applies the protected DACL `O:SYD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)(A;OICI;0x1200a9;;;SU)`: SYSTEM and Administrators full control, Users and SERVICE read and execute, no inheritance from `ProgramData`. Game-server accounts can run the JRE but never change it.
8. Upgrade and cleanup (VS-31): a new pin installs side by side, so running servers keep their JRE until their next start. After a managed resolve, at most once per hour, runtime directories other than the one just resolved, whose marker `lastUsedAt` is older than `JavaRuntimeRetentionDays` (default 7; 0 disables), are renamed into `.trash` and deleted. A failed rename means the runtime is in use on Windows, so it is skipped. Leftover `.staging` and `.downloads` entries older than 1 h are deleted. Directories that do not match the runtime-name pattern are never touched.

---

## Wave 1: foundation

Five independent packets, dispatched together from `origin/main` `2c4c19c` on 2026-10-26. The packet texts VS-01 to VS-05 below are the specs the five PRs were built from. **The merge order changed after Sprint 2 (#235, squash `6df3338`) merged first.** Then, on 2026-10-27, another session merged #233 VS-02, #230 VS-04 and #231 VS-05 without the owner's go-ahead. #236 fixed #231's lock file, and `main` is now `5eead35` (ruling § 9). The remaining order is #234 ledger, #229 VS-03R, #232 VS-01, then VS-34 (D-42). Each needs the owner's yes, and the owner's main session merges it (D-41). The next section says what each PR needs now.

### Wave 1 reconcile after Sprint 2 (2026-10-27)

Ruling: [worknotes/sprint2-reconcile-ruling.md](worknotes/sprint2-reconcile-ruling.md). For every PR below, ops first merges `origin/main` into the branch (D-38: merge, never rebase or force-push). Then a `packet-coder` resolves conflicts in that merge commit, as listed here, and runs the checks again. After a review pass the PR ships in this order. Each later PR gets the new `main` merged in the same way before it ships.

| Order | PR | Packet | Branch / worktree | What it needs | Security re-review |
| --- | --- | --- | --- | --- | --- |
| 1 | #234 | ledger | `campaign/vertical-slice-ledger` / `vertical-slice-ledger` | ops merges main `5eead35`, commits the 2026-10-27 ledger edits | No |
| 2 | #229 | VS-03R | `vs/03-agent-contracts` / `vertical-slice-vs03` | merge main (clean on `5eead35`), then the VS-03R packet | Yes |
| 3 | #232 | VS-01 | `vs/01-park-projects` / `vertical-slice-01-park-projects` | merge-main resolution (below, revised for `5eead35`) | No (keeps the owner's label) |
| 4 | new | VS-34 | `vs/34-fold-seams` / `vertical-slice-vs34` | the VS-34 packet (below); merge main after #232 | Yes |
| - | #230 | VS-04 | - | merged as `2440ada` (no owner yes); post-merge security pass FU-41a | owed (FU-41a) |
| - | #233 | VS-02 | - | merged as `32779b5` (no owner yes); matches its verdict | - |
| - | #231 | VS-05 | - | merged as `6895cd1`, lock fix #236 `5eead35`; VS-34 folds and removes it | VS-34's pass |

Ops recreates a worktree from its existing branch if it was removed. Never `git worktree prune`.

### VS-03R Agent contracts on Sprint 2's hub surface

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/03-agent-contracts` (PR #229) / `vertical-slice-vs03` |
| Commit subject | `fix(contracts): move the agent hub surface onto Sprint 2's AgentHubContracts; drop NodeId from agent payloads [VS-03R]` |
| Security review | Yes (agent contracts, `AgentCommandEnvelope`, a Nodes hub edit) |
| Depends on | ops has merged `origin/main` (`5eead35` or later) into the branch. `git merge-tree` against `5eead35` is clean. VS-03R touches no Gateway auth, agent or Seams.Tests file, so VS-04's helper and #231's hook do not interact with it (ruling § 9.4). |
| Size | about 25k tokens |

**Goal.** The hub surface has one home: `src/Shared/Dhadgar.Contracts/Nodes/AgentHubContracts.cs`, which Sprint 2 shipped (D-26). VS-03R extends that file and deletes #229's parallel copy. Payloads the agent sends carry no node id (D-27). Hub methods answer with acks (D-28), `ReceiveCommand` is typed (D-29), and the frame limit is one contract constant (D-30). `AgentJson` ignores unknown members but keeps every other check (D-40).

**Rules.**
- Contract first: every type the agent and Nodes share stays in `src/Shared/Dhadgar.Contracts`. Never copy one into a service or the agent.
- Do not change any member Sprint 2 shipped in `Contracts/Nodes/AgentHubContracts.cs`. That covers `AgentHubProtocol.Path`, `VersionHeader`, `CurrentVersion`, `NodeIdClaim` and `NodeGroup`, the four existing error codes, `AgentHeartbeatLimits`, `AgentHeartbeat`, `AgentHeartbeatAck`, `IAgentHub.Heartbeat` and `IAgentHubClient.ReceivePing`. Agents built from Sprint 2 call these.
- Business failures are acks (`AgentHubAck(false, code)`), never a `HubException`. The four new hub methods are stubs in this packet.
- Edit no agent project. `ControlPlaneClient` still subscribes by name (`On<string>(nameof(IAgentHubClient.ReceiveCommand), ...)`) and keeps compiling. VS-13 moves it to the typed handler.
- `Dhadgar.Contracts.csproj` gets no package references. No `Version=` in any `.csproj` (Central Package Management, `Directory.Packages.props`).
- Keep CRLF in the `.cs` files you edit (dispatch rules, line endings). Nullable is enabled and warnings are errors under `CI=true`.

**Contract excerpt.** C-2a in this file, the exact text: `AgentHubProtocol.MaxReceiveMessageBytes`, the 7 new `AgentHubErrorCodes`, the 5-method `IAgentHub`, typed `IAgentHubClient.ReceiveCommand(AgentCommandEnvelope)` and `AgentHubAck`. Also C-2b's comments on the members it removes, and C-2's `AgentJson` block and "JSON rules" (D-40).

**Changes.**
1. Start by listing what you will touch:

```bash
git grep -nE "AgentProtocol\.|AgentServerProcess|MaxHeartbeatWarnings|MaxWarningLength|MaxServersPerHeartbeat|MaxAgentVersionLength|Contracts\.Agents\.(IAgentHub|AgentHeartbeat)" -- src tests
```

   Expected hits outside the files below: `Agents/AgentCommandPayloads.cs` line 12 (a `PingResult` doc reference), `Contracts/README.md` line 742, `tests/Dhadgar.Contracts.Tests/Agents/AgentMessageLimitsTests.cs` and `AgentMessagesSerializationTests.cs`. Report any other hit; fix it if it is in a file this packet already edits, otherwise return `blocked` with reason `spec`.
2. Delete `src/Shared/Dhadgar.Contracts/Agents/AgentHubContracts.cs`. In `src/Shared/Dhadgar.Contracts/Nodes/AgentHubContracts.cs`, add exactly the C-2a members: `MaxReceiveMessageBytes`, the seven error codes, the four new `IAgentHub` methods, typed `ReceiveCommand`, and `public sealed record AgentHubAck(bool Accepted, string? ErrorCode = null);`. Give each the XML doc C-2a's comments describe, and add `using Dhadgar.Contracts.Agents;`.
3. `Contracts/Agents/AgentMessages.cs`: delete `AgentHeartbeat` and `AgentServerProcess`. Remove the `NodeId` parameter, and its `<param>` doc, from `AgentCommandResult`, `AgentServerStatus`, `AgentConsoleBatch` and `AgentTelemetry`. `AgentCommandEnvelope` and the three events keep `NodeId`.
4. `Contracts/Agents/AgentProtocol.cs`: delete class `AgentProtocol` (lines 9-33 on #229) and keep the rest of the file. In `AgentMessageLimits`, delete `MaxHeartbeatWarnings`, `MaxWarningLength`, `MaxServersPerHeartbeat` and `MaxAgentVersionLength`. In `Agents/AgentCommandPayloads.cs`, point the `PingResult.AgentVersion` doc at `AgentHeartbeatLimits.MaxAgentVersionLength` (add `using Dhadgar.Contracts.Nodes;` or use the full `cref`).
4a. Lenient agent JSON (D-40, owner decision 2026-10-27, superseding D-21). In `AgentJson.Create()` (`Contracts/Agents/AgentProtocol.cs`), set `UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip` in place of `Disallow`, and add `AllowDuplicateProperties = false`. Keep `RespectNullableAnnotations`, `RespectRequiredConstructorParameters`, `MaxDepth = 8` and `MakeReadOnly`. Rewrite the class `<summary>`/`<remarks>` to C-2's text: unknown properties are skipped; missing required properties, nulls in non-nullable members, duplicate properties and depth over 8 throw `JsonException`. If the compiler reports that `AllowDuplicateProperties` does not exist, return `blocked` with reason `spec`; do not drop it silently.
5. `src/Dhadgar.Nodes/Hubs/AgentHub.cs`: add the four methods, each with a one-line XML doc naming the packet that implements it (VS-08R for `Telemetry`, VS-33 for the rest):

```csharp
public Task<AgentHubAck> Telemetry(AgentTelemetry telemetry) =>
    Task.FromResult(new AgentHubAck(false, AgentHubErrorCodes.NotSupported));
// CommandResult, ServerStatusChanged, ConsoleOutput: the same body
```

   Change nothing else in Nodes.
6. Tests in `tests/Dhadgar.Contracts.Tests/Agents/`:
   - `AgentMessageLimitsTests.cs`: drop the pins for the four deleted limits and for `AgentProtocol.MaxHubMessageBytes`. Pin `AgentHubProtocol.MaxReceiveMessageBytes == 512 * 1024`. Delete the `AgentHeartbeat` worst-case case. Every escaped-frame worst case left (telemetry, console batch, command result, server status) asserts against `AgentHubProtocol.MaxReceiveMessageBytes`.
   - `AgentMessagesSerializationTests.cs`: delete the `AgentServerProcess` and `AgentHeartbeat` cases, and remove `NodeId` from the four records' constructions. For D-40:
     - Replace `StartServerPayload_UnknownProperty_Throws` with `StartServerPayload_UnknownProperty_IsIgnored`. The same JSON with `"extra":1` deserializes, and it equals the payload built without `extra`.
     - Add `StartServerPayload_UnknownNestedObject_IsIgnored` (`"extra":{"a":[1,2]}`) and `AgentCommandEnvelope_UnknownProperty_IsIgnored`.
     - Add `StartServerPayload_DuplicateProperty_Throws` (`"port"` twice).
     - Add `StartServerPayload_NullForNonNullable_Throws` (`"serverJarUrl":null`).
     - Add `StartServerPayload_DepthOverLimit_Throws`: an unknown member nested 9 levels deep. Skipping must not bypass `MaxDepth`.
     - Keep `StartServerPayload_MissingRequiredProperty_Throws` and `PingPayload_EmptyObject_DeserializesWithNullNonce` unchanged.
     - `AgentJson_Options_AreReadOnly`: `AgentJson.Options.IsReadOnly`, `UnmappedMemberHandling == Skip` and `AllowDuplicateProperties == false`.
   - New `AgentHubContractTests.cs`:
     - `IAgentHub_HasExactlyTheFiveMethods`: by reflection, the method names, parameter types and return types are exactly C-2a's.
     - `IAgentHubClient_ReceiveCommand_IsTyped`: its single parameter is `AgentCommandEnvelope`.
     - `AgentSentPayloads_CarryNoNodeId` (D-27 guard): `AgentCommandResult`, `AgentServerStatus`, `AgentConsoleBatch` and `AgentTelemetry` have no `NodeId` property.
     - `AgentHubTypes_HaveOneHome`: no public type named `IAgentHub`, `IAgentHubClient` or `AgentHeartbeat` exists in namespace `Dhadgar.Contracts.Agents`.
     - Every one of these fails on #229's head before this packet.
7. `src/Shared/Dhadgar.Contracts/README.md`: fix the line-742 table row, and add one paragraph saying the hub surface (protocol, error codes, interfaces, heartbeat, acks) is in `Contracts.Nodes` and the messages, payloads, limits and signing are in `Contracts.Agents`.

**Why the tests fail when reverted.** Without the move, `IAgentHub` has one method and `ReceiveCommand` takes a string. The `NodeId` properties are back on the payloads. The duplicate types in `Contracts.Agents` exist again. With #229's `Disallow`, the unknown-property cases throw. With duplicates allowed, the duplicate case passes when it should throw.

**Checks (in order).**

```bash
dotnet build src/Shared/Dhadgar.Contracts/Dhadgar.Contracts.csproj -c Release
dotnet test tests/Dhadgar.Contracts.Tests/Dhadgar.Contracts.Tests.csproj -c Release
dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj -c Release
# only while the project still exists (VS-34 deletes it):
test -d tests/Dhadgar.Seams.Tests && dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
dotnet build Dhadgar.sln -c Release
```

Sprint 2's hub, bootstrap and renewal seams in `Dhadgar.AgentSeam.Tests`, and `Nodes.Tests/Integration/AgentHubSeamTests`, must stay green. They are the seam test for this contract change. The solution build catches any agent or UAT project that used a deleted member.

**Docs owed.** Change 7. No PROJECT-STATE change.

**Done when.** Checks pass, the report lists every grep hit and how it was handled, and the PR description says the hub contract's home is `Contracts.Nodes` (D-26).

### Merge-main resolutions for #230, #232 and #233

Each is a `packet-coder` dispatch on the existing worktree after ops has run `git fetch origin && git merge origin/main`. The coder resolves the conflicts, finishes the merge commit with subject `merge: origin/main (5eead35) into <branch> [VS-NN]`, runs the checks and reports. The lock-file rule applies to every conflicted `packages.lock.json`: take `main`'s copy, finish the merge, then run both restores. Never hand-merge lock-file JSON.

Only #232 still needs its resolution. #230 and #233 were merged on 2026-10-27 without these steps (ruling § 9). Their texts below are kept for the record. Both PRs as merged match them, and #230's security questions move to FU-41a.

**#230 VS-04** (merged as `2440ada`; record only; the security questions are FU-41a):
- `src/Dhadgar.Gateway/Program.cs`: keep everything Sprint 2 added, which is the Kestrel `AgentIngress:ClientCertificateMode` block, the `AgentCertificate` policy, `.AddTransforms(AgentCertificateForwarding.Apply)` and the `PerAgent` limiter. Replace only the old `AddAuthentication(JwtBearerDefaults...).AddJwtBearer(...)` block (lines 96-113 on `6df3338`) with VS-04's `AddDhadgarJwtBearer(..., "/hubs/console")` call. Leave policy `Agent` alone; VS-07R deletes it.
- `src/Dhadgar.Gateway/appsettings.json`: keep both sides, meaning Sprint 2's `AgentIngress` section and `agents-route` with `AgentCertificate`, plus VS-04's `Auth` keys.
- Checks: `dotnet restore Dhadgar.sln --locked-mode`, `dotnet build Dhadgar.sln -c Release`, then `dotnet test` for `tests/Dhadgar.Gateway.Tests`, `tests/Dhadgar.ServiceDefaults.Tests`, `tests/Dhadgar.Secrets.Tests` and `tests/Dhadgar.AgentSeam.Tests`. `GatewayAgentHubSeamTests` now runs through VS-04's helper and must stay green.
- Security re-review questions: does the JWT default scheme leave `AgentCertificate` evaluation unchanged (it asserts on `Connection.ClientCertificate`, not on a principal)? Is the `access_token` query fallback still limited to `/hubs/console`, never `/hubs/agent`?

**#232 VS-01** (`vertical-slice-01-park-projects`; revised 2026-10-27 for `main` `5eead35`). `git merge-tree` of `1ea65d6` with `5eead35` is clean: git auto-merges `Dhadgar.sln` and `_dotnet-build-test.yml`.
- `Dhadgar.sln`: keep VS-01's 12 removals and all four projects added since `2c4c19c`: Sprint 2's `Dhadgar.AgentSeam.Tests`, `Dhadgar.Agent.Windows.Uat` and `Dhadgar.Agent.Windows.Uat.Tests`, and **#231's `Dhadgar.Seams.Tests`**. Keep each one's project block, configuration lines and nesting line. VS-34 removes Seams.Tests later. The file stays LF.
- `.github/workflows/_dotnet-build-test.yml`: keep Sprint 2's PSScriptAnalyzer step and VS-01's removal of `src/Dhadgar.ShoppingCart` from the npm list.
- **Parked lock files.** #230 rewrote the `packages.lock.json` of 8 projects that VS-01 moves: Billing, Files, Mods and Tasks, and their `.Tests`. Rename detection carries `main`'s content to the `parked/` path. Check that each matches `main`, for example with `git diff origin/main:src/Dhadgar.Billing/packages.lock.json HEAD:parked/src/Dhadgar.Billing/packages.lock.json`, which must print nothing. If one conflicts, take `main`'s content at the `parked/` path (`git show origin/main:<old path> > parked/<old path>`). Do not run `--locked-mode` for those files, because they are outside the solution.
- Any other conflict: keep both sides' intent and report it.
- Checks: VS-01's checks. **The final `git grep` now prints exactly two hits**, `src/Dhadgar.Scope/src/sections/Agents.astro` and `src/Dhadgar.Scope/src/sections/Repos.astro`. Both appear on `1ea65d6` before the merge, and D-20 gives them to VS-28. Any other hit is a leftover: fix it if it is in a file VS-01 edits, otherwise report it. Then run `dotnet test` on `tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj`, `tests/Dhadgar.Agent.Windows.Uat.Tests/Dhadgar.Agent.Windows.Uat.Tests.csproj` and `tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj`, each with `-c Release`. To list the PR's files, use `gh api repos/{owner}/{repo}/pulls/232/files --paginate` (`gh pr view --json files` stops at 100).
- Optional, if it is still in the merge (FU-41c): `src/Agents/Dhadgar.Agent.Windows/README.md:1682` links to `/src/Agents/Dhadgar.Agent.Linux/README.md`. Point it at `/parked/src/Agents/Dhadgar.Agent.Linux/README.md`. If it is not fixed here, VS-34 fixes it.

**#233 VS-02** (merged as `32779b5`; record only):
- `src/Dhadgar.Gateway/appsettings.json`: keep Sprint 2's `agent-hub-route`, `agents-route` with `AgentCertificate`, and the `AgentIngress` section. Delete `billing-route`, `tasks-route`, `files-route`, `mods-route` and their clusters. The result is **14 routes and 8 clusters**.
- `tests/Dhadgar.Gateway.Tests/RouteConfigurationTests.cs`: 14 routes (test `ReverseProxyShouldHave14Routes`) and 8 clusters; keep Sprint 2's rows. In `RateLimiterConfigurationTests.cs`, keep the `agent-hub-route` row and delete the four parked rows.
- `deploy/compose/docker-compose.services.yml`: keep Sprint 2's gateway env (`AgentIngress__ClientCertificateMode`) and nodes env (`Mtls__ForwardedCertificate__*`, port `127.0.0.1:5040:8080`). Delete only the parked services and their gateway cluster env lines.
- Checks: VS-02's checks, unchanged.

### VS-34 Fold Seams.Tests into AgentSeam.Tests; remove `IHubConnectionConfigurator`

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/34-fold-seams` / `vertical-slice-vs34`, created by ops from `origin/main` (`5eead35` or later) |
| Commit subject | `refactor(seams,agent): fold Seams.Tests into AgentSeam.Tests and remove IHubConnectionConfigurator [VS-34]` |
| Security review | Yes. It touches `ControlPlaneClient`, the `IControlPlaneClient` implementation in Agent.Core. |
| Depends on | Nothing to start. It merges after #232, because both edit `Dhadgar.sln`. Ops merges `main` in before the review if #232 merged first (D-38). |
| Size | about 25k tokens |
| PR | its own PR, `security-review` label, merged by the owner's main session on the owner's yes (D-41) |

**Status:** merged as `05a69d2` (#239, 2026-10-28), packet and security review pass 1 `ship`.

**Goal (D-42).** There is one seam project and one agent transport hook. Delete `tests/Dhadgar.Seams.Tests` and the `IHubConnectionConfigurator` hook that only it used. Keep its one live test's coverage as `GatewayAgentEnrollmentSeamTests` on Sprint 2's `GatewayNodesHarness`. `ControlPlaneHubTransport` stays the only way tests change the agent's hub transport.

**Rules.**
- No contract change. This packet changes no type in `src/Shared/Dhadgar.Contracts`.
- `IControlPlaneClient.ConnectAsync` returns `Result`, never throws, for every validation failure (D-S227). The HTTPS refusal (`ControlPlane.InsecureEndpoint`) and the missing-certificate refusal (`ControlPlane.NoCertificate`) must stay exactly as they are on `6df3338`.
- Do not change `ControlPlaneHubTransport`, `AgentServiceCollectionExtensions` or any other agent file apart from the two below.
- No `Version=` in any `.csproj` (Central Package Management). This packet should need no `.csproj` change. If it does, apply the lock-file rule (D-39) and say why in the report.
- New `.cs` files are CRLF. Nullable is enabled and warnings are errors under `CI=true`.
- `using` on every `IDisposable` in the test (`ECDsa`, the public key, `X509Chain`, the harness).
- Leave `tests/Dhadgar.Testing` alone. `SeamJwt` goes there in VS-32R, from `git show 6895cd1:tests/Dhadgar.Seams.Tests/Infrastructure/SeamJwt.cs`.
- Follow the xUnit conventions of `tests/Dhadgar.AgentSeam.Tests`: a sealed class, a temporary CA directory created in the constructor and deleted in `Dispose`, as `GatewayAgentHubSeamTests` does.

**Changes.**
1. List what you will touch:

```bash
git grep -nE "IHubConnectionConfigurator|SeamHubConfigurator|Seams\.Tests" -- . ':!docs/analysis' ':!.claude/reports'
git log --oneline 6df3338..HEAD -- src/Agents/Dhadgar.Agent.Core/Communication/ControlPlaneClient.cs
```

   Expected grep hits:
   - `ControlPlaneClient.cs`, `IHubConnectionConfigurator.cs` and `tests/Dhadgar.Agent.Core.Tests/Communication/HubConnectionConfiguratorTests.cs`;
   - files under `tests/Dhadgar.Seams.Tests/`;
   - `Dhadgar.sln`.

   Report any other hit. Fix a doc hit; return `blocked` with reason `spec` for a code hit. The `git log` must list only `6895cd1`. If it lists another commit, return `blocked` with reason `spec`.
2. Restore `ControlPlaneClient.cs` to the shape it had before the hook: `git checkout 6df3338 -- src/Agents/Dhadgar.Agent.Core/Communication/ControlPlaneClient.cs`. This removes:
   - the `_configurators` field;
   - the `configurators` constructor parameter;
   - the `LogWarning`;
   - the configurator loop and the two `InvalidOperationException` checks inside `WithUrl`.

   `git diff 6df3338 -- <that file>` must print nothing.
3. `git rm src/Agents/Dhadgar.Agent.Core/Communication/IHubConnectionConfigurator.cs tests/Dhadgar.Agent.Core.Tests/Communication/HubConnectionConfiguratorTests.cs`.
4. `dotnet sln Dhadgar.sln remove tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj`, then `git rm -r tests/Dhadgar.Seams.Tests`. `git diff Dhadgar.sln` must show only deleted lines: the project block, its configuration lines and its nesting line. If the tool rewrote anything else (line endings, ordering), run `git checkout -- Dhadgar.sln` and delete those lines by hand. The file stays LF.
5. New `tests/Dhadgar.AgentSeam.Tests/GatewayAgentEnrollmentSeamTests.cs`, ported from `git show 6895cd1:tests/Dhadgar.Seams.Tests/Enrollment/AgentEnrollmentThroughGatewaySeamTests.cs`:

```csharp
namespace Dhadgar.AgentSeam.Tests;

/// <summary>
/// Seam: the real agent enrollment runs through the real Gateway into the real Nodes. Beyond success, the enrollment
/// leaves the node, inventory and certificate rows, a certificate for the agent's own key, and a chain to the CA the
/// agent stored. Moved from tests/Dhadgar.Seams.Tests (D-42).
/// </summary>
public sealed class GatewayAgentEnrollmentSeamTests : IDisposable
{
    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"gateway-agent-enroll-seam-ca-{Guid.NewGuid()}");

    // constructor creates the directory; Dispose deletes it best-effort (copy GatewayAgentHubSeamTests)

    [Fact]
    public async Task RealAgent_EnrollsThroughGateway_PersistsNodeAndIssuesChainedCertificateForAgentKey()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        var orgId = Guid.NewGuid();
        var (enrolled, store) = await harness.EnrollThroughGatewayAsync(orgId);

        Assert.NotEqual(Guid.Empty, enrolled.NodeId);
        Assert.Equal(orgId, enrolled.OrganizationId);

        using (var scope = harness.Nodes.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            var node = await db.Nodes.SingleAsync(n => n.Id == enrolled.NodeId);
            Assert.Equal(orgId, node.OrganizationId);
            Assert.True(await db.HardwareInventories.AnyAsync(h => h.NodeId == node.Id));
            Assert.True(await db.AgentCertificates.AnyAsync(c => c.NodeId == node.Id));
        }

        var certificate = Assert.IsType<X509Certificate2>(store.StoredClientCertificate);
        var privateKey = Assert.IsType<byte[]>(store.GetPrivateKey());
        using var agentKey = ECDsa.Create();
        agentKey.ImportPkcs8PrivateKey(privateKey, out _);
        using var certKey = certificate.GetECDsaPublicKey();
        Assert.NotNull(certKey);
        Assert.Equal(agentKey.ExportSubjectPublicKeyInfo(), certKey.ExportSubjectPublicKeyInfo());

        var ca = Assert.IsType<X509Certificate2>(store.StoredCaCertificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = certificate.NotBefore.AddMinutes(1);
        Assert.True(chain.Build(certificate), string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation)));
    }
}
```

   Use the store members as `tests/Dhadgar.Nodes.Tests/Integration/AgentSeam/InMemoryCertificateStore.cs` names them (`StoredClientCertificate`, `GetPrivateKey()`, `StoredCaCertificate`). If a name differs, use the real one; do not add members to the store. Drop the old `host.Forwarder.ForwardedClusters` assertion: `GatewayNodesHarness` reaches Nodes only through the Gateway's forwarder, so success already proves forwarding. VS-07R adds the recording forwarder.
6. New `tests/Dhadgar.Agent.Core.Tests/Communication/ControlPlaneClientTransportHookTests.cs`, with one test, `ControlPlaneClient_HasOnlyTheTransportHook`. By reflection it asserts:
   - `ControlPlaneClient` has exactly one public constructor;
   - that constructor's parameter types are, in order: `IOptions<AgentOptions>`, `ICertificateStore`, `AgentMeter`, `AgentActivitySource`, `ILogger<ControlPlaneClient>`, `IAgentIdentityProvider` and `ControlPlaneHubTransport`;
   - `typeof(ControlPlaneClient).Assembly.GetTypes()` contains no type named `IHubConnectionConfigurator`.

**Why the tests fail when reverted.**
- Restore the hook and `ControlPlaneClient_HasOnlyTheTransportHook` fails: the constructor has an eighth parameter and the type exists.
- Break enrollment through the Gateway and `GatewayAgentEnrollmentSeamTests` fails. That covers removing or mis-stripping the enrollment route, Nodes storing no inventory or certificate row, a certificate issued for a server-side key, or a CA that does not sign the certificate.
- Before committing, run two scratch mutations of the new test, one at a time. Quote each failing assertion in the report, then revert it.
  - Import a fresh `ECDsa.Create()` key in place of the stored private key. The SPKI assertion must fail.
  - Build the chain for `ForeignCa.CreateClientCertificate(enrolled.NodeId)` (from `tests/Dhadgar.Nodes.Tests/Integration/AgentSeam/ForeignCa.cs`) in place of `certificate`. The chain assertion must fail.

**Checks (in order).**

```bash
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
dotnet test tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet restore Dhadgar.sln --locked-mode
dotnet build Dhadgar.sln -c Release
git status --porcelain -- '*packages.lock.json'   # must print nothing
```

`GatewayAgentHubSeamTests`, `AgentBootstrapSeamTests` and `GatewayAgentRenewalSeamTests` must stay green. They prove that `ControlPlaneHubTransport` alone still carries the agent through the Gateway.

**Docs owed.**
- `docs/testing/seam-tests.md`: drop any `tests/Dhadgar.Seams.Tests` entry and list `GatewayAgentEnrollmentSeamTests` under AgentSeam.Tests.
- `docs/reference/seams/enrollment.md`: if it lists seam-test evidence, add the new test.
- FU-41c, if #232's merge did not fix it: `src/Agents/Dhadgar.Agent.Windows/README.md` line 1682 points at `/parked/src/Agents/Dhadgar.Agent.Linux/README.md`.
- No PROJECT-STATE change.

**Done when.**
- The checks pass.
- `git grep -nE "IHubConnectionConfigurator|Seams\.Tests" -- src tests Dhadgar.sln` prints nothing.
- The report quotes both mutation failures.
- The PR description names D-42 and says that production behaviour does not change: no configurator was ever registered.

### VS-01 Park projects: move, solution, AppHost, CI

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/01-park-projects` / `vertical-slice-vs01` |
| Commit subject | `chore(park): move Billing, Tasks, Files, Mods, Agent.Linux, ShoppingCart and Helm to parked/ [VS-01]` |
| Security review | No |
| Size | about 35k tokens, mostly mechanical |

**Goal.** D-03: the parked projects leave `Dhadgar.sln`, AppHost and CI, and keep their code under `parked/`.

**Rules.**
- Use `git mv` so history follows the files. Do not delete code.
- Never put `Version=` on a `PackageReference`.
- Keep `Dhadgar.sln` LF. Lock files stay CRLF (dispatch rules).
- Do not touch Gateway, compose or Discord files; VS-02 owns them.

**Changes.**
1. Remove the twelve projects from the solution **before** moving them:
   `dotnet sln Dhadgar.sln remove src/Dhadgar.Billing/Dhadgar.Billing.csproj src/Dhadgar.Tasks/Dhadgar.Tasks.csproj src/Dhadgar.Files/Dhadgar.Files.csproj src/Dhadgar.Mods/Dhadgar.Mods.csproj src/Dhadgar.ShoppingCart/Dhadgar.ShoppingCart.csproj src/Agents/Dhadgar.Agent.Linux/Dhadgar.Agent.Linux.csproj tests/Dhadgar.Billing.Tests/Dhadgar.Billing.Tests.csproj tests/Dhadgar.Tasks.Tests/Dhadgar.Tasks.Tests.csproj tests/Dhadgar.Files.Tests/Dhadgar.Files.Tests.csproj tests/Dhadgar.Mods.Tests/Dhadgar.Mods.Tests.csproj tests/Dhadgar.ShoppingCart.Tests/Dhadgar.ShoppingCart.Tests.csproj tests/Dhadgar.Agent.Linux.Tests/Dhadgar.Agent.Linux.Tests.csproj`. If `dotnet sln` rewrote line endings, restore LF (`sed -i 's/\r$//' Dhadgar.sln`).
2. `git mv` each to the same relative path under `parked/`: `src/Dhadgar.{Billing,Tasks,Files,Mods,ShoppingCart}` → `parked/src/...`, `src/Agents/Dhadgar.Agent.Linux` → `parked/src/Agents/Dhadgar.Agent.Linux`, the six test projects → `parked/tests/...`, `deploy/kubernetes/helm` → `parked/deploy/kubernetes/helm`, `.github/workflows/helm-publish.yml` → `parked/.github/workflows/helm-publish.yml` (outside `.github`, so GitHub no longer runs it).
3. In each moved `.csproj`, fix `ProjectReference` paths so they still point into `src/` (one level deeper now: `..\Shared\X` becomes `..\..\..\src\Shared\X`, `..\..\src\X` in tests becomes `..\..\..\src\X`, and so on). Parked projects are not built in CI, but they must still build on demand.
4. `src/Dhadgar.AppHost/Dhadgar.AppHost.csproj`: remove the `ProjectReference`s to Billing, Tasks, Mods (and Files if present). `src/Dhadgar.AppHost/Program.cs`: remove the `billing`, `tasks` and `mods` resources and every `.WithReference(...)`/`.WaitFor(...)` that names them. Regenerate the AppHost lock file (dispatch rules).
5. `tests/Dhadgar.AppHost.Tests/AppHostIntegrationTests.cs`: remove `Billing_HealthCheck_ReturnsHealthy`, the Tasks test (around line 216) and `Mods_HealthCheck_ReturnsHealthy`.
6. CI and tooling:
   - `.github/workflows/_dotnet-build-test.yml`: drop `src/Dhadgar.ShoppingCart` from the `npm ci (parallel)` list.
   - `.github/workflows/containers.yml`: drop `Billing Tasks Files Mods` from `all=(...)` (line 68) and from any other service list in the file.
   - `.github/workflows/static-web-apps.yml`: drop the ShoppingCart path filter, matrix entry and JSON entry.
   - `.github/workflows/code-quality.yml`: drop the `helm-publish.yml` line (around line 89).
   - `.github/dependabot.yml`: drop entries whose `directory` is a parked project.
   - `azure-pipelines.yml`, `azure-pipelines-containers.yml`: drop parked service entries.
7. New `parked/README.md`: a table of what is parked and its original path, why (D-03, campaign `vertical-slice`), and how to unpark (move back, `dotnet sln add`, restore routes/compose/CI entries; see VS-02 for routes).

**Tests.** No new tests. The guard is the solution build: if any remaining project still references a parked one, it fails.

**Checks (in order).**

```bash
for d in src/Dhadgar.BetterAuth src/Dhadgar.SharedAuth src/Dhadgar.Scope src/Dhadgar.Panel; do (cd "$d" && npm ci --no-audit --no-fund); done
dotnet restore Dhadgar.sln --locked-mode
dotnet build Dhadgar.sln -c Release --no-restore
dotnet test tests/Dhadgar.AppHost.Tests/Dhadgar.AppHost.Tests.csproj -c Release --no-build --filter "RequiresDocker!=true"
dotnet build parked/src/Dhadgar.Billing/Dhadgar.Billing.csproj -c Release
git grep -nE "Dhadgar\.(Billing|Tasks|Files|Mods|ShoppingCart|Agent\.Linux)" -- . ':!parked' ':!docs' ':!*.md' ':!src/Dhadgar.Gateway' ':!tests/Dhadgar.Gateway.Tests' ':!deploy/compose' ':!src/Dhadgar.Discord' ':!tests/Dhadgar.Discord.Tests' ':!src/Shared/Dhadgar.ServiceDefaults'
```

The last command must print nothing (the excluded paths belong to VS-02). If the parked Billing build fails for a reason other than reference paths, record it and move on (parked code is not maintained).

**Docs owed.** `parked/README.md`.

**Done when.** Solution builds without the twelve projects, AppHost tests pass, CI files no longer mention parked projects, and the commit holds only moves and the listed edits.

### VS-02 Park routes: Gateway, compose, Discord map, docs

**Status:** merged as `32779b5` (#233) on 2026-10-27 without the owner's yes. As merged, it matches the reconcile verdict (ruling § 9.4). This text is the spec it was built from.

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/02-park-routes` / `vertical-slice-vs02` |
| Commit subject | `chore(park): remove parked services from Gateway, compose and Discord service map [VS-02]` |
| Security review | No |
| Size | about 25k tokens |

**Goal.** D-03: nothing routes to, starts or health-checks a parked service.

**Rules.**
- Keep `appsettings.json` CRLF and the compose files LF.
- Do not touch `servers-route`, `console-api-route` or the agent routes; VS-07 changes those.
- Do not touch BetterAuth settings (follow-up only).

**Changes.**
1. `src/Dhadgar.Gateway/appsettings.json`: delete routes `billing-route`, `tasks-route`, `files-route`, `mods-route` and clusters `billing`, `tasks`, `files`, `mods`. That leaves 13 routes and 8 clusters.
2. `tests/Dhadgar.Gateway.Tests/RouteConfigurationTests.cs`: route count 17 → 13 (rename the test to `ReverseProxyShouldHave13Routes` and fix its comment), cluster count 12 → 8, delete the four `InlineData` rows for billing, tasks, files, mods. `tests/Dhadgar.Gateway.Tests/RateLimiterConfigurationTests.cs`: delete the `billing-route`, `tasks-route`, `files-route`, `mods-route` rows. `git grep -n "billing\|tasks-route\|files-route\|mods" tests/Dhadgar.Gateway.Tests src/Dhadgar.Gateway` must then find nothing route-related.
3. `deploy/compose/docker-compose.services.yml`: delete services `billing`, `tasks`, `files`, `mods`, `shoppingcart` and the gateway env lines `ReverseProxy__Clusters__{billing,tasks,files,mods}__Destinations__d1__Address`. Make the same deletions in `deploy/compose/docker-compose.services.acr.yml`, and in `docker-compose.dev.yml` if it names them.
4. Discord service map: `src/Dhadgar.Discord/appsettings.json` (the `Billing`, `Tasks`, `Files`, `Mods` entries near line 29) and `src/Dhadgar.Discord/Services/PlatformHealthService.cs` (dictionary near line 44). Update any test in `tests/Dhadgar.Discord.Tests` that counts or names those services.
5. `src/Shared/Dhadgar.ServiceDefaults/Tracing/TracingExtensions.cs`: if it names a parked service in a list or switch, remove that entry; if it only appears in a comment, leave it.
6. Docs: `SERVICE-PORTS.md` marks 5020/5050/5060/5080 and the ShoppingCart port as "parked (see `parked/README.md`)"; the service table in `README.md` says the same.
7. CLI: no change (it has no defaults for parked services). Say so in the report.

**Tests.** Updated route tests. They fail when reverted because the counts and rows no longer match `appsettings.json`.

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Gateway/Dhadgar.Gateway.csproj -c Release
dotnet test tests/Dhadgar.Gateway.Tests/Dhadgar.Gateway.Tests.csproj -c Release
dotnet build src/Dhadgar.Discord/Dhadgar.Discord.csproj -c Release
dotnet test tests/Dhadgar.Discord.Tests/Dhadgar.Discord.Tests.csproj -c Release
POSTGRES_PASSWORD=x RABBITMQ_PASSWORD=x docker compose -f deploy/compose/docker-compose.dev.yml -f deploy/compose/docker-compose.services.yml config -q
```

**Docs owed.** `SERVICE-PORTS.md`, `README.md` service table.

**Done when.** Gateway and Discord tests pass and `docker compose config` is clean.

### VS-03 Agent contracts in `Dhadgar.Contracts.Agents`

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/03-agent-contracts` / `vertical-slice-vs03` |
| Commit subject | `feat(contracts): agent hub, command, signing and event contracts [VS-03]` |
| Security review | Yes (defines `CommandEnvelope`'s wire form and the signing input) |
| Size | about 30k tokens |

**Goal.** The contract in C-2, C-3 and the dispatch records of C-4 exist, compile and are pinned by tests, so later packets build against them.

**Rules.**
- Records only, no service logic, except `AgentCommandSigning` (pure functions) and `AgentCommandTypes.IsKnown`.
- `Dhadgar.Contracts.csproj` gets **no** package references (BCL only: `System.Security.Cryptography`, `System.Text.Json`).
- Nullable enabled; XML doc comments on every public type, as `Nodes/EnrollmentContracts.cs` does.
- Do not change any existing contract file in this packet.
- Throwing `ArgumentException` for a newline in `commandType`/`correlationId` is allowed here (a programming error, not validation).

**Changes.** New files, CRLF, namespace `Dhadgar.Contracts.Agents` unless stated:
1. `src/Shared/Dhadgar.Contracts/Agents/AgentProtocol.cs`: `AgentProtocol`, `AgentMessageLimits`, `AgentCommandTypes`, `AgentJson` exactly as in C-2.
2. `Agents/AgentHubContracts.cs`: `IAgentHub`, `IAgentHubClient`.
3. `Agents/AgentMessages.cs`: `AgentCommandEnvelope`, `AgentCommandStatus`, `AgentCommandResult`, `AgentServerState`, `AgentServerProcess`, `AgentHeartbeat`, `AgentServerStatus`, `AgentConsoleLine`, `AgentConsoleBatch`, `AgentTelemetry`.
4. `Agents/AgentCommandPayloads.cs`: `PingPayload`, `PingResult`, `StartServerPayload`, `StopServerPayload`, `ServerStatusPayload`, `ServerStatusResult`.
5. `Agents/AgentEvents.cs`: `AgentCommandCompleted`, `AgentServerStatusChanged`, `ServerConsoleOutput`. If the records in `Nodes/NodeEvents.cs` implement `Dhadgar.Contracts.Messaging.IIntegrationEvent`, do the same; otherwise do not.
6. `Agents/AgentCommandSigning.cs`, as in C-3:

```csharp
public static byte[] BuildSigningInput(
    Guid commandId, Guid nodeId, Guid organizationId, string commandType,
    DateTimeOffset issuedAt, DateTimeOffset expiresAt, Guid? initiatedByUserId,
    string? correlationId, string payloadJson)
{
    ArgumentNullException.ThrowIfNull(commandType);
    ArgumentNullException.ThrowIfNull(payloadJson);
    RejectLineBreaks(commandType, nameof(commandType));
    RejectLineBreaks(correlationId, nameof(correlationId));
    var payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));
    var text = string.Join('\n',
        Domain,
        commandId.ToString("D"), nodeId.ToString("D"), organizationId.ToString("D"),
        commandType,
        issuedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
        expiresAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
        initiatedByUserId?.ToString("D") ?? string.Empty,
        correlationId ?? string.Empty,
        payloadHash);
    return Encoding.UTF8.GetBytes(text);
}

public static string ComputeKeyId(ReadOnlySpan<byte> subjectPublicKeyInfo) =>
    Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo));
```

7. `src/Shared/Dhadgar.Contracts/Nodes/AgentCommandDispatchContracts.cs` (namespace `Dhadgar.Contracts.Nodes`): `DispatchAgentCommandRequest`, `DispatchAgentCommandResponse` (C-4).

**Tests** (`tests/Dhadgar.Contracts.Tests`, xUnit, match its existing style):
- `Agents/AgentCommandSigningTests.cs`:
  - `BuildSigningInput_MatchesGoldenVector`: the C-3 inputs give exactly the C-3 string (compare `Encoding.UTF8.GetString`) and SHA-256 `723f9365…1c3a`.
  - `ComputeKeyId_MatchesGoldenVector`: bytes `0x01..0x5B` give `4678fe8a…8e25`.
  - `SignAndVerify_RoundTrips`: ECDsa P-256, sign with IEEE P1363, verify succeeds; changing `PayloadJson` by one character makes verification fail.
  - `BuildSigningInput_RejectsLineBreaks` for `commandType` and `correlationId`.
  - `EnvelopeOverload_EqualsFieldOverload`.
- `Agents/AgentMessagesSerializationTests.cs`: each record round-trips through `System.Text.Json` with `JsonSerializerDefaults.Web` (what SignalR's JSON protocol and `AgentJson.Options` use); `AgentCommandEnvelope` serializes with camelCase names `commandId`, `payloadJson`, `signingKeyId`; enums serialize as numbers.
- `Agents/AgentCommandTypesTests.cs`: `IsKnown` true for the four types, false for `ping` (case-sensitive), null and `GetStatus`.

Why they fail when reverted: the types do not exist (compile failure), and any change to field order, separators, time units or hash encoding breaks the golden vector. That vector is what keeps Nodes (VS-09) and the agent (VS-14) agreeing.

**Checks (in order).**

```bash
dotnet build src/Shared/Dhadgar.Contracts/Dhadgar.Contracts.csproj -c Release
dotnet test tests/Dhadgar.Contracts.Tests/Dhadgar.Contracts.Tests.csproj -c Release
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release
```

**Docs owed.** `src/Shared/Dhadgar.Contracts/README.md`: an "Agents" section listing the files and pointing to `docs/analysis/vertical-slice/work-packets.md#the-contract`.

**Done when.** All four checks pass and the golden-vector tests pass.

### VS-04 Shared service JWT auth helper; Gateway and Secrets adopt it

**Status:** merged as `2440ada` (#230) on 2026-10-27 without the owner's yes. As merged, it matches the reconcile verdict. The post-merge security pass is FU-41a. This text is the spec it was built from.

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/04-service-jwt` / `vertical-slice-vs04` |
| Commit subject | `feat(auth): shared JWT bearer helper with internal metadata and hub query tokens [VS-04]` |
| Security review | Yes (authentication plumbing for every promoted service) |
| Size | about 40k tokens (the lock-file churn is mechanical) |

**Goal.** One helper validates user JWTs for Gateway, Secrets, Nodes, Servers and Console (D-07). It supports an internal metadata address for compose (F-18, F-25) and reads `access_token` from the query string on hub paths (F-14).

**Rules.**
- Behaviour of Secrets and Gateway authentication must not change except for hub query-token support on the Gateway.
- Never log tokens. Never accept a query-string token outside the listed hub prefixes.
- No `Version=` in `.csproj`. Add the package reference only to `Dhadgar.ServiceDefaults.csproj`.
- `using` on every disposable; nullable enabled.

**Changes.**
1. `src/Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj`: add `<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />` (already pinned at 10.0.0 in `Directory.Packages.props`). Then `dotnet restore Dhadgar.sln --force-evaluate` and CRLF-fix **every** changed lock file (every project that references ServiceDefaults changes).
2. Move `src/Dhadgar.Secrets/Infrastructure/UrlRewritingDocumentRetriever.cs` with `git mv` to `src/Shared/Dhadgar.ServiceDefaults/Security/UrlRewritingDocumentRetriever.cs`; namespace `Dhadgar.ServiceDefaults.Security`; make it `public sealed`. Fix usings in Secrets and its tests.
3. New `src/Shared/Dhadgar.ServiceDefaults/Security/ServiceJwtAuthenticationExtensions.cs`:

```csharp
namespace Dhadgar.ServiceDefaults.Security;

public static class ServiceJwtAuthenticationExtensions
{
    /// Registers JwtBearer as the default scheme. Reads Auth:Issuer (required), Auth:Audience (required),
    /// Auth:MetadataAddress, Auth:InternalBaseUrl, Auth:ClockSkewSeconds (default 60).
    /// For requests whose path starts with one of hubPathPrefixes, a missing Authorization header
    /// falls back to the access_token query value.
    public static AuthenticationBuilder AddDhadgarJwtBearer(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        params string[] hubPathPrefixes);
}
```

   Body: the JwtBearer configuration from `src/Dhadgar.Secrets/Program.cs:65-110` (Authority, Audience, `RequireHttpsMetadata = !environment.IsDevelopment()`, `ValidIssuer = issuer`, lifetime, signing key, clock skew, `RefreshOnIssuerKeyNotFound`, `MetadataAddress`, and the `UrlRewritingDocumentRetriever` configuration manager when `Auth:InternalBaseUrl` is set) plus:

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        if (string.IsNullOrEmpty(context.Token)
            && !context.Request.Headers.ContainsKey(HeaderNames.Authorization)
            && hubPathPrefixes.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase))
            && context.Request.Query.TryGetValue("access_token", out var token)
            && !StringValues.IsNullOrEmpty(token))
        {
            context.Token = token.ToString();
        }
        return Task.CompletedTask;
    }
};
```

   Missing `Auth:Issuer` or `Auth:Audience` throws `InvalidOperationException` at registration (startup configuration error).
4. New `src/Shared/Dhadgar.ServiceDefaults/MultiTenancy/TenantScopedAuthorization.cs`: `TenantScopedRequirement`, `TenantScopedHandler` and `TenantScopedAuthorizationExtensions.AddTenantScopedAuthorization(this IServiceCollection, IConfiguration, IHostEnvironment, params string[] hubPathPrefixes)`. Take the requirement and handler verbatim from `git show 0761b97:src/Shared/Dhadgar.ServiceDefaults/MultiTenancy/TenantScopedAuthorization.cs`. The extension calls `AddDhadgarJwtBearer(...)` instead of its own `AddJwtBearer`, then `AddHttpContextAccessor`, registers the handler as a singleton and adds policy `TenantScoped` (`RequireAuthenticatedUser` plus the requirement). Keeping #88's name and signature lets VS-11 and VS-17 import #88's `Program.cs` unchanged.
5. `src/Dhadgar.Secrets/Program.cs`: replace lines 65-110 with `builder.Services.AddDhadgarJwtBearer(builder.Configuration, builder.Environment);`. If a Secrets or Gateway test host now fails because `Auth:Issuer`/`Auth:Audience` is missing, add the keys to that test factory's in-memory configuration; do not relax the helper.
6. `src/Dhadgar.Gateway/Program.cs`: replace the `AddAuthentication(...).AddJwtBearer(...)` block (lines 83-99) with `builder.Services.AddDhadgarJwtBearer(builder.Configuration, builder.Environment, "/hubs/console");`. Keep `AddAuthorization` and its policies unchanged.

**Tests.**
- `tests/Dhadgar.ServiceDefaults.Tests/Security/ServiceJwtAuthenticationExtensionsTests.cs`: build a minimal `WebApplication` on `TestServer` with `AddDhadgarJwtBearer(config, env, "/hubs/test")`, override validation via `services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => { o.Authority = null; o.Configuration = new OpenIdConnectConfiguration(); o.TokenValidationParameters = new() { ValidIssuer = "t", ValidAudience = "a", IssuerSigningKey = key }; })`, map `GET /hubs/test/x` and `GET /api/x` requiring auth. Cases: query token accepted on `/hubs/test/x`; query token rejected (401) on `/api/x`; header token accepted on both; missing `Auth:Issuer` throws; `Auth:MetadataAddress` ends up in `JwtBearerOptions.MetadataAddress`.
- `tests/Dhadgar.ServiceDefaults.Tests/MultiTenancy/TenantScopedHandlerTests.cs`: route org equals claim → success; different → fail; no `org_id` claim → fail.
- `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`: Gateway factory with the same `PostConfigure` test key. `GET /hubs/console/negotiate?access_token=<valid>` → status is not 401 (the backend is unreachable, so 502/503 is expected); without a token → 401.

Why they fail when reverted: without the helper there is no query-token path, so the Gateway returns 401 for browser hub connections, and Secrets loses nothing but the file move breaks compilation.

**Checks (in order).**

```bash
dotnet restore Dhadgar.sln --locked-mode
dotnet build src/Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj -c Release
dotnet test tests/Dhadgar.ServiceDefaults.Tests/Dhadgar.ServiceDefaults.Tests.csproj -c Release
dotnet test tests/Dhadgar.Secrets.Tests/Dhadgar.Secrets.Tests.csproj -c Release
dotnet test tests/Dhadgar.Gateway.Tests/Dhadgar.Gateway.Tests.csproj -c Release
```

**Docs owed.** `src/Shared/Dhadgar.ServiceDefaults/README.md`: section "Service JWT authentication" with the config keys from C-6 and the hub-path rule.

**Done when.** All checks pass and the lock files are CRLF.

### VS-05 Seams test project and hub-connection hook

**Status:** merged as `6895cd1` (#231) on 2026-10-27 without the owner's yes; #236 (`5eead35`) fixed its lock file. VS-34 folds the project into AgentSeam.Tests and removes the hook (D-42). This text is the spec it was built from.

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/05-seams-harness` / `vertical-slice-vs05` |
| Commit subject | `test(seams): cross-service seam test project, agent enrollment through the Gateway [VS-05]` |
| Security review | Yes (adds an extension point to `ControlPlaneClient`, a security-review area) |
| Size | about 40k tokens |

**Goal.** A test project that runs the Gateway and backends together (D-12), the first green seam (agent enrollment through the Gateway), and two pending seams that later packets turn green.

**Rules.**
- Test-only infrastructure lives in the test project. The one production change is the `IHubConnectionConfigurator` hook.
- `ControlPlaneClient` is security-critical: the hook runs **after** the agent's own options, never removes the client certificate, and a warning is logged when any configurator is registered.
- No `Version=` in `.csproj`; every package you reference must already be pinned in `Directory.Packages.props` (all listed below are).
- `using` on every `IDisposable`; zero key material in tests after use is not required, but never commit keys.

**Proven shape.** The architect's prototype on 2026-10-26 confirmed: `extern alias` per web project lets `WebApplicationFactory<gateway::Program>` and `WebApplicationFactory<nodes::Program>` coexist; registering `IForwarderHttpClientFactory` in `ConfigureTestServices` makes YARP forward into a backend `TestServer`; and SignalR long polling through the Gateway into a backend hub works with `PostConfigure<JwtBearerOptions>` test keys. Nodes needs EF InMemory with its **own internal service provider** (copy `NodesWebApplicationFactory.cs:54-64`) or EF complains about two providers.

**Changes.**
1. Agent.Core hook. New `src/Agents/Dhadgar.Agent.Core/Communication/IHubConnectionConfigurator.cs`:

```csharp
namespace Dhadgar.Agent.Core.Communication;

/// <summary>
/// Adjusts SignalR connection options after the agent has applied its own (endpoint, client certificate,
/// headers). For tests and diagnostics only; production hosts register none.
/// </summary>
public interface IHubConnectionConfigurator
{
    void Configure(Microsoft.AspNetCore.Http.Connections.Client.HttpConnectionOptions options);
}
```

   `ControlPlaneClient`: add constructor parameter `IEnumerable<IHubConnectionConfigurator> configurators` (last position), store it as an array, log `LogWarning("{Count} hub connection configurator(s) registered; test/diagnostic only", n)` once in the constructor when n > 0, and at the end of the `WithUrl(endpoint, options => { ... })` lambda call `foreach (var c in _configurators) c.Configure(options);`. Do not register any configurator in `AddAgentCore`.
2. New project `tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj` (copy the shape of `tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj`):
   - Packages: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio` (PrivateAssets all), `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.AspNetCore.SignalR.Client`, `System.IdentityModel.Tokens.Jwt`, `Microsoft.Extensions.TimeProvider.Testing`, and the `System.Security.Cryptography.Xml` pin with the same comment Nodes.Tests has.
   - References: `<ProjectReference Include="..\..\src\Dhadgar.Gateway\Dhadgar.Gateway.csproj" Aliases="gateway" />`, `<ProjectReference Include="..\..\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj" Aliases="nodes" />`, `<ProjectReference Include="..\..\src\Agents\Dhadgar.Agent.Core\Dhadgar.Agent.Core.csproj" />`.
   - `<Using Include="Xunit" />`, `xunit.runner.json` copied from Nodes.Tests, with `parallelizeTestCollections` false.
   - Add it to the solution: `dotnet sln Dhadgar.sln add tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj` with the same solution folder the other test projects use (inspect `Dhadgar.sln`; keep the file LF). Create its lock file (dispatch rules).
3. Harness (`tests/Dhadgar.Seams.Tests/Infrastructure/`, every file starting with the needed `extern alias` lines):
   - `SeamJwt.cs`: a 32-byte symmetric test key, issuer `https://seams.test/identity`, audience `meridian-api`; `string CreateUserToken(Guid userId, Guid organizationId)` with claims `sub` and `org_id`, 10 min lifetime; `void Apply(IServiceCollection services)` does the `PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, ...)` shown in VS-04 tests.
   - `SeamSecrets.cs`: `public const string ForwardedCertificate = "seams-forwarded-cert-secret-0123456789abcdef";`.
   - `CapturingPublishEndpoint.cs`: implements `MassTransit.IPublishEndpoint`; records every published message in a thread-safe list; `Task<T> WaitForAsync<T>(Func<T,bool> match, TimeSpan timeout)`. Copy the approach of `TestNodesEventPublisher` in `tests/Dhadgar.Nodes.Tests`.
   - `NodesSeamFactory.cs` : `WebApplicationFactory<nodes::Program>`: environment `Testing`; in-memory config `RabbitMq:Username`/`Password` dummies, `Nodes:CaStoragePath` = a per-factory temp dir, `Nodes:CaKeySize` 2048, `Mtls:Enabled` true; services: EF InMemory with its own internal provider (unique db name), remove MassTransit descriptors and register `CapturingPublishEndpoint`, `RemoveAll<IHostedService>()`, `FakeTimeProvider` is **not** used (real CA validity needs real time). Nodes registers no auth scheme yet (F-01), so this factory adds `services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();` plus `SeamJwt.Apply(services)`, commented `// VS-06 removes this once Nodes registers JWT itself`. `Dispose` deletes the temp dir.
   - `GatewaySeamFactory.cs` : `WebApplicationFactory<gateway::Program>`: environment `Testing`; config `ReverseProxy:Clusters:<id>:Destinations:d1:Address` = `http://<id>.seams/` for each backend registered; `ConfigureTestServices`: `SeamJwt.Apply`, register `IForwarderHttpClientFactory` = `SeamForwarderFactory` (a dictionary cluster id → `Func<HttpMessageHandler>`; `CreateClient(ForwarderHttpClientContext ctx)` returns `new HttpMessageInvoker(handlers[ctx.ClusterId](), disposeHandler: false)`; unknown cluster → handler that returns 503); register `IStartupFilter` `SeamClientCertificateStartupFilter`, which adds, first in the pipeline, middleware that turns header `X-Seam-Client-Cert` (base64 DER) into `context.Connection.ClientCertificate` and then removes the header. This stands in for the TLS handshake, which `TestServer` cannot do; the comment must say so.
   - `SeamHost.cs`: creates `NodesSeamFactory` first (touch `.Server`), then `GatewaySeamFactory` wired with `"nodes" → nodes.Server.CreateHandler()`. Exposes `Nodes`, `Gateway`, `GatewayHandler()`, `CreateEnrollmentTokenAsync(Guid orgId)` (via `IEnrollmentTokenService.CreateTokenAsync(orgId, "seam-user", "seam")` in a Nodes scope, as `AgentEnrollmentContractTests.cs:121-129` does).
   - `SeamAgent.cs`: builds real agent pieces against the Gateway: `InMemoryCertificateStore` (copy of `AgentEnrollmentContractTests.cs:178-211`), an `IHttpClientFactory` whose clients use `new ClientCertHeaderHandler(store) { InnerHandler = gatewayHandler }` and `BaseAddress = https://localhost/` (for client name `ControlPlaneMtls` the handler adds `X-Seam-Client-Cert` from the store; for `ControlPlane` it does not), the real `EnrollmentService`, and `CreateControlPlaneClient(Guid nodeId, Guid orgId)`, which builds `ControlPlaneClient` with `Options.Create(new AgentOptions { NodeId, OrganizationId, ControlPlane = { Endpoint = "https://localhost" } })`, `AgentMeter`, `AgentActivitySource`, and a `SeamHubConfigurator` that sets `options.Transports = HttpTransportType.LongPolling` and `options.HttpMessageHandlerFactory = _ => new ClientCertHeaderHandler(store) { InnerHandler = gatewayHandler }`.
4. Seam tests:
   - `Enrollment/AgentEnrollmentThroughGatewaySeamTests.cs` → `RealAgent_EnrollsThroughGateway`: enrollment token for a new org, `SeamAgent.EnrollAsync(token)` succeeds, `OrganizationId` matches, Nodes DB has the node and certificate (same assertions as `AgentEnrollmentContractTests.RealAgent_EnrollsAgainstRealNodes_AndHoldsAWorkingClientCertificate`), and the `SeamForwarderFactory` recorded at least one request for cluster `nodes`. **Green in this packet.**
   - `AgentIngress/AgentIngressThroughGatewaySeamTests.cs` → `CaCertificate_IsFetchableAnonymously` (`GET /api/v1/agents/ca-certificate` → 200 with `BEGIN CERTIFICATE`). `[Fact(Skip = "Pending VS-07: Gateway agents-route requires a JWT (F-04)")]`.
   - `AgentHub/AgentClientHubSeamTests.cs` → `Heartbeat_ThroughGateway_UpdatesNode` (enroll, `CreateControlPlaneClient`, `ConnectAsync`, `SendHeartbeatAsync` with a payload whose `Metrics.CpuUsagePercent` is 12.5, then poll the Nodes DB for up to 5 s until `Node.LastHeartbeat` moved and the latest `NodeHealth` CPU is 12.5) and `Connect_WithoutCertificate_IsRejected` (a client whose store holds no certificate gets an exception from `ConnectAsync`). Both `[Fact(Skip = "Pending VS-13: no /hubs/agent hub and no typed agent wire yet")]`.
   - Run the two pending tests once with `Skip` removed and paste their failure lines in the report (D-13).
5. `tests/Dhadgar.Seams.Tests/README.md`: what a seam test is, the extern-alias pattern, how to add a backend (alias, factory, `SeamForwarderFactory` entry), the client-certificate stand-in, and the pending-skip rule.

**Tests in Agent.Core.** `tests/Dhadgar.Agent.Core.Tests/Communication/HubConnectionConfiguratorTests.cs`: a `ControlPlaneClient` pointing at `https://127.0.0.1:1` with a configurator that sets a flag; `ConnectAsync` throws (nothing listens) and the flag is set, proving the hook runs. Fails when reverted because the parameter does not exist.

**Checks (in order).**

```bash
dotnet restore Dhadgar.sln --locked-mode
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

The seams run must show 1 passed and 3 skipped.

**Docs owed.** `tests/Dhadgar.Seams.Tests/README.md`.

**Done when.** Checks pass; the report quotes the red failures of the pending tests.

---

## Wave 2: control-plane side of the agent seam, Servers import

**Status (2026-10-28, reconstructed after the cloud session was lost).** Stage 1 was dispatched from `main` `c5ca3c4`. Merged: VS-34 #239 `05a69d2`, VS-11 #241 `cada473`, VS-32R #243 `a859315`, VS-07R #242 `300bc33`, and the FU-41a hotfixes HF-01 #238 `28c23a3` and HF-02D #240 `101c36d`. `main` is `101c36d`. In flight: VS-08R (pushed `b0e6509`, main merged in locally as `6d0a82f`, coder finishing) and VS-09 (fresh coder; the first run was never pushed). Ready: HF-02 (rebuilt spec below; the first run was never pushed), VS-06R, VS-35, VS-36. Remaining merge order: HF-02, VS-09, VS-08R, VS-36, VS-06R, VS-35. HF-02 touches no .NET service and can merge whenever it is ready. VS-09, VS-08R, VS-36 and VS-06R all edit `src/Dhadgar.Nodes/Program.cs` on different lines; each later one merges `main` in and resolves (D-38). VS-35 adds Gateway tests only (one helper extraction in `Dhadgar.Gateway.Tests`) and can merge in any position. Every branch created from now on starts at `origin/main` `101c36d` or later, once `main`'s CI run on that commit is green.

Re-sliced on 2026-10-27 after Sprint 2 (ruling [worknotes/sprint2-reconcile-ruling.md](worknotes/sprint2-reconcile-ruling.md) § 3). The base is `main` after #234, #229, #232 and VS-34 have merged on top of `5eead35`, which already holds Sprint 2 `6df3338`, #233, #230, #231 and #236 (ruling § 9). Stage 1 is dispatched together once those have merged and FU-41a (the post-merge Gateway auth security pass) is closed: VS-11, VS-32R, VS-07R, VS-09 and VS-08R. Stage 2 is VS-06R, after VS-32R and VS-07R merge. VS-10 is superseded (D-36). Merge order: VS-11, VS-32R, VS-07R, VS-09, VS-08R, VS-06R. Each packet ships as its own PR, and all six need a security review. Between merges, ops merges the new `main` into the next branch (D-38) and that packet's checks run again.

Stage 1 file ownership. Parallel packets stay disjoint; where two share a file, the second to merge resolves it when `main` is merged in:

| File | Stage-1 owner |
| --- | --- |
| `tests/Dhadgar.AgentSeam.Tests/GatewayNodesHarness.cs` | VS-07R (recording forwarder) |
| `tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj` and lock file | VS-32R (reference to `Dhadgar.Testing`) |
| `tests/Dhadgar.AgentSeam.Tests/GatewayAgentHubSeamTests.cs`, `src/Dhadgar.Nodes/Hubs/*` | VS-08R |
| `tests/Dhadgar.AgentSeam.Tests/AgentBootstrapSeamTests.cs`, `src/Agents/**`, `tests/Dhadgar.Nodes.Tests/NodesWebApplicationFactory.cs` | VS-09 |
| `src/Dhadgar.Nodes/Program.cs` | VS-08R (SignalR, registry) and VS-09 (signer): different lines; VS-08R merges second and resolves |
| `src/Dhadgar.Gateway/**`, `tests/Dhadgar.Gateway.Tests/Route*`, `RateLimiter*` | VS-07R |
| `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`, `tests/Dhadgar.Gateway.Tests/*.csproj` | VS-32R |

Contract: C-2a and C-2b (rewritten 2026-10-27; #229 carries them after VS-03R), the C-1 rows `agents-ca-route`, `servers-org-route` and `console-org-route`, and C-6. Every wave-2 packet reads C-2 "Limit enforcement" and the dispatch rules on rejection seams, certificate shims, test JWTs, merging main and lock files. Seams go in `tests/Dhadgar.AgentSeam.Tests` (D-33). `tests/Dhadgar.Seams.Tests` is gone after VS-34 (D-42), and `IHubConnectionConfigurator` with it.

The common rule for every wave-2 packet (cited as "the wave-2 seam rule"): test user tokens are minted and accepted only with `SeamJwt` (`src/Shared/Dhadgar.Testing/Authentication/SeamJwt.cs`, from VS-32R; D-43). Never add another minter or another `PostConfigure<JwtBearerOptions>` block. Client-certificate shims stay inside the test project that owns them. A rejection seam proves which server answered and has a positive twin (dispatch rules).

### VS-11 Import Servers from PR #88

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/11-servers-import` / `vertical-slice-vs11` |
| Commit subject | `feat(servers): import Servers service implementation from PR #88 (0761b97) [VS-11]` |
| Security review | Yes (service-level auth on a promoted service, DV-13) |
| Size | about 35k tokens |

**Status:** merged as `cada473` (#241, 2026-10-28). As merged it also qualifies `Dhadgar.Nodes.Models.FilteredPagedResponse<T>` in Nodes (D-47), restores `main`'s advisory pins for `System.Security.Cryptography.Pkcs`/`.Xml`, removes the `REPLACE_ME` RabbitMQ credential default (startup fails closed), and requires authentication on `GET /templates/public`.

Unchanged from the wave-2 plan of 2026-10-26, except the guard checks. Sprint 2 touched only the two Servers lock files among the files this packet imports (`git diff --stat 2c4c19c 6df3338` on the import paths), so the import loop is still right.

**Goal.** D-02: Servers' implementation from #88 lands on current `main`, builds under CI rules, authenticates users itself with VS-04's helper, and its tests pass. It adds no new behaviour; placement and lifecycle come in VS-22 and VS-23.

**Rules.**
- Import file contents with `git show 0761b97:<path> > <path>`. Never use `git checkout`, `git cherry-pick` or `git restore`. Ops has fetched `refs/pull/88/head` as `origin/pr-88`. Check that `git rev-parse origin/pr-88` prints `0761b97fba08f62b75925b0772c3b3364b699c77`; otherwise return `blocked` with reason `tool`.
- Do **not** import `src/Shared/Dhadgar.ServiceDefaults/MultiTenancy/TenantScopedAuthorization.cs` (VS-04 rebuilt it) or `src/Shared/Dhadgar.ServiceDefaults/RabbitMqOptions.cs` (F-17).
- Do not import anything for Mods, Console or the agents.
- No `Version=` in any `.csproj`. Every package #88's csproj references must already be pinned in `Directory.Packages.props` (FluentValidation 12.1.1 is); if one is not, return `blocked` with reason `spec`.
- `Result<T>`, not exceptions, for validation and IO in any code you have to fix. Nullable is enabled and warnings are errors (`CI=true`).

**Changes.**
1. Import with this loop (bash), then `git add` the paths:

```bash
rev=0761b97
for p in src/Dhadgar.Servers tests/Dhadgar.Servers.Tests; do
  git ls-tree -r --name-only "$rev" -- "$p" | while read -r f; do mkdir -p "$(dirname "$f")"; git show "$rev:$f" > "$f"; done
done
for f in src/Shared/Dhadgar.Contracts/Servers/ServerEvents.cs src/Shared/Dhadgar.Contracts/Servers/ServerRequests.cs \
         src/Shared/Dhadgar.Contracts/Servers/ServerResponses.cs src/Shared/Dhadgar.Contracts/Pagination.cs \
         src/Shared/Dhadgar.Shared/Data/DatabaseHelpers.cs src/Shared/Dhadgar.Shared/Data/DhadgarDbContext.cs; do
  git show "$rev:$f" > "$f"
done
```

   Files under `src/Dhadgar.Servers` that exist on `main` but not in #88 stay as they are. Check with `git status`; there should be none besides `packages.lock.json`, which you regenerate with the lock-file rule.
2. New `src/Shared/Dhadgar.ServiceDefaults/Messaging/RabbitMqOptions.cs`, namespace `Dhadgar.ServiceDefaults.Messaging`: a copy of `src/Dhadgar.Nodes/RabbitMqOptions.cs` (empty `Username`/`Password` defaults, `[Required]`). In `src/Dhadgar.Servers/Program.cs`, add `using Dhadgar.ServiceDefaults.Messaging;`. Do not touch Nodes' copy; a follow-up can dedupe.
3. `src/Dhadgar.Servers/Program.cs` already calls `AddTenantScopedAuthorization(builder.Configuration, builder.Environment)`, which now resolves to VS-04's helper. Add `Auth:Issuer`, `Auth:Audience` and `Auth:ClockSkewSeconds` to `src/Dhadgar.Servers/appsettings.json`, with the Gateway's values.
4. Lock files: apply the lock-file rule (D-39, whole solution).
5. Make it build under `CI=true`. #88's last commits fixed CI analyzer errors, but `main` has moved since. Fix only what the compiler and analyzers report in the imported Servers files. If a fix needs a change outside `src/Dhadgar.Servers`, `tests/Dhadgar.Servers.Tests` or the files listed above, stop and report it.
6. `tests/Dhadgar.Servers.Tests/ServersWebApplicationFactory.cs`: if its tests relied on #88's own JwtBearer registration, switch to a test scheme as `NodesWebApplicationFactory` does (default authenticate and challenge schemes overridden). If #88 brought its own test-token minter (a signing key plus `PostConfigure<JwtBearerOptions>`), delete it and use the test scheme; do not keep another JWT helper (D-22). `DhadgarDbContext` changes are additive, and other services' tests must stay green.

**Tests.** #88's Servers tests (endpoints, lifecycle, templates, validators) come with the import and must pass. Add `tests/Dhadgar.Servers.Tests/Auth/ServersAuthTests.cs`: `GET /organizations/{org}/servers` without credentials → 401, and with a test identity for another org → 403. These fail when reverted, because #88's endpoints would no longer be protected by the shared helper.

**Checks (in order).**

```bash
git rev-parse origin/pr-88
dotnet restore Dhadgar.sln --locked-mode
dotnet build src/Dhadgar.Servers/Dhadgar.Servers.csproj -c Release
dotnet test tests/Dhadgar.Servers.Tests/Dhadgar.Servers.Tests.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.Identity.Tests/Dhadgar.Identity.Tests.csproj -c Release
dotnet test tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj -c Release
dotnet build Dhadgar.sln -c Release
```

Identity, Nodes and AgentSeam guard the shared `DhadgarDbContext`, `Pagination` and Contracts changes.

**Docs owed.** `src/Dhadgar.Servers/README.md`: keep #88's text and add the status line "Imported from PR #88 (D-02); placement and agent dispatch arrive in VS-22/VS-23".

**Done when.** The checks pass.

### VS-32R One test JWT helper; access tokens never reach traces

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/32-test-jwt` / `vertical-slice-vs32` |
| Commit subject | `test(shared): single test JWT helper in Dhadgar.Testing; prove access_token never reaches traces [VS-32R]` |
| Security review | Yes (token hygiene in shared telemetry) |
| Depends on | wave 1 merged, VS-34 included (VS-04's `HubTokenTests` is on `main` as `2440ada`). `SeamJwt` is read from commit `6895cd1`, which is in `main`'s history, so no branch has to be kept. |
| Size | about 25k tokens |

**Status:** merged as `a859315` (#243, 2026-10-28), with D-43: `SeamJwt` went into the existing `src/Shared/Dhadgar.Testing` (namespace `Dhadgar.Testing.Authentication`), not a new `tests/Dhadgar.Testing`. Redaction needed no production change (the instrumentation's default query redaction holds; the red run sets `DOTNET_SYSTEM_NET_HTTP_DISABLEURIREDACTION=true`). It also added `RepoFiles` and `TestingLibraryIsolationTests` (FU-41a N3).

**Goal.** D-22: one test JWT helper. VS-04's private minter (`HubTokenWebApplicationFactory`, a fixed key plus its own `PostConfigure<JwtBearerOptions>`) is replaced by `SeamJwt`, which #231 wrote and which now lives in a shared test-support library. The packet also closes VS-04 review-1's open item: hub requests carry the user JWT in `?access_token=`, and nothing yet proves that the OpenTelemetry instrumentation keeps it out of traces. VS-32 had a user-JWT seam; it moved to VS-06R, which is the packet that gives Nodes real JWT auth.

**Rules.**
- No `Version=` in any `.csproj` (Central Package Management). `Microsoft.AspNetCore.Authentication.JwtBearer` and `System.IdentityModel.Tokens.Jwt` are already pinned in `Directory.Packages.props`. Any other package means returning `blocked` with reason `spec`.
- `tests/Dhadgar.Testing` is a library, not a test project: `IsPackable` false, `IsTestProject` false, no xUnit reference, and **no client-certificate shims** (dispatch rule). For now it holds `SeamJwt` only.
- Never log or print a token. Tests use `Assert.DoesNotContain(token, ...)` and never write the token to output.
- Change `src/Shared/Dhadgar.ServiceDefaults` only if the redaction test (change 5) fails without the change. If it passes as is, the packet ships the test as a guard and changes no production code.
- Lock files follow the lock-file rule (D-39). Line endings: new `.cs`, `.csproj` and `.json` files are CRLF; `Dhadgar.sln` stays LF.
- Nullable is enabled and warnings are errors (`CI=true`).

**Changes.**
1. New `tests/Dhadgar.Testing/Dhadgar.Testing.csproj`. Copy `TargetFramework`, `Nullable` and `IsPackable` from `tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj` and add `<IsTestProject>false</IsTestProject>`. References: `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, `<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />` and `<PackageReference Include="System.IdentityModel.Tokens.Jwt" />`. Run `dotnet sln Dhadgar.sln add` to put it in the same solution folder as `Dhadgar.AgentSeam.Tests`.
2. `git show 6895cd1:tests/Dhadgar.Seams.Tests/Infrastructure/SeamJwt.cs > tests/Dhadgar.Testing/Auth/SeamJwt.cs` (identical to `760af6a`; VS-34 has deleted the working copy), then change only the namespace to `Dhadgar.Testing.Auth` and convert to CRLF. The class keeps its name and members (`Issuer`, `Audience`, `CreateUserToken(Guid userId, Guid organizationId)`, `Apply(IServiceCollection)`), its per-process random key, issuer `https://seams.test/identity` and audience `meridian-api`.
3. `tests/Dhadgar.Gateway.Tests/Dhadgar.Gateway.Tests.csproj` and `tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj`: add `<ProjectReference Include="..\Dhadgar.Testing\Dhadgar.Testing.csproj" />` and `<Using Include="Dhadgar.Testing.Auth" />`. AgentSeam.Tests has no caller yet (VS-06R is the first), but the reference lands here so that stage 2 needs no `.csproj` or lock-file change.
4. `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`: in `HubTokenWebApplicationFactory`, replace the `PostConfigure<JwtBearerOptions>` block with `SeamJwt.Apply(services);`. Delete `KeyBytes`, `CreateUserToken()` and the `Issuer`/`Audience` constants once nothing uses them. Callers use `SeamJwt.CreateUserToken(Guid.NewGuid(), Guid.NewGuid())`. Keep `CapturingLoggerProvider` and the log assertions (VS-04 security N1).
5. Trace redaction test, `tests/Dhadgar.Gateway.Tests/AccessTokenTraceRedactionTests.cs` (it uses `HubTokenWebApplicationFactory`). Register an `ActivityListener` before the request (`ShouldListenTo = _ => true`, `Sample = AllDataAndRecorded`, `ActivityStopped` adds each activity to a `ConcurrentBag`), and dispose it afterwards. Send `GET /hubs/console/negotiate?negotiateVersion=1&access_token={token}` with a fresh `SeamJwt` token.
   - Positive twin `HubRequest_IsTraced`: at least one stopped activity from source `Microsoft.AspNetCore` has a `url.path` (or `http.target`) tag containing `/hubs/console/negotiate`. This proves the instrumentation ran in the test host. If no such activity exists, because tracing is not registered under the `Testing` environment, return `blocked` with reason `spec` and quote what you found at the `AddAspNetCoreInstrumentation` calls in `src/Shared/Dhadgar.ServiceDefaults/ServiceDefaultsExtensions.cs` (find them with `git grep -n AddAspNetCoreInstrumentation src/Shared`).
   - `AccessToken_NeverAppearsInTraceTags`: no tag value and no `DisplayName` of any captured activity contains the token string. That covers ASP.NET Core and the forwarded `System.Net.Http` request.
   - Prove it red once (D-13 style; quote the failing line in the report). Set the instrumentation's documented switch that disables URL query redaction for the run. Find the exact environment variable name in the README or CHANGELOG of `opentelemetry.instrumentation.aspnetcore` and `opentelemetry.instrumentation.http` **1.20.1** (the versions pinned on `main`) under `~/.nuget/packages`.
   - If the test fails **without** that switch, fix it. In `ServiceDefaultsExtensions.cs`, at each `AddAspNetCoreInstrumentation()` call, set `EnrichWithHttpRequest` so that when the query contains `access_token`, the `url.query` tag is rewritten with that value replaced by `Redacted`. Do the same for `url.full` in the HttpClient instrumentation options if the forwarded request leaks. Put the helper in one internal static method with its own unit test in `tests/Dhadgar.ServiceDefaults.Tests`.
6. Docs: new `tests/Dhadgar.Testing/README.md`, 5-10 lines. It says what belongs here (shared test helpers with no service references) and what never does (certificate shims, service factories). If change 5 touched ServiceDefaults, add a "Telemetry: query redaction" note to `src/Shared/Dhadgar.ServiceDefaults/README.md`.

**Tests.** Change 5. The Gateway hub-token tests must stay green after the swap. They fail when reverted: without the reference and the `<Using>`, nothing compiles, and without redaction (if it was needed), `AccessToken_NeverAppearsInTraceTags` fails.

**Checks (in order).**

```bash
dotnet restore Dhadgar.sln --locked-mode
dotnet build tests/Dhadgar.Testing/Dhadgar.Testing.csproj -c Release
dotnet test tests/Dhadgar.Gateway.Tests/Dhadgar.Gateway.Tests.csproj -c Release
dotnet test tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj -c Release
dotnet test tests/Dhadgar.ServiceDefaults.Tests/Dhadgar.ServiceDefaults.Tests.csproj -c Release   # only if ServiceDefaults changed
dotnet build Dhadgar.sln -c Release
```

**Docs owed.** Change 6.

**Done when.** The checks pass. The report says whether redaction was already on (the test is green without a code change) or needed the `EnrichWithHttpRequest` change, and it quotes the red run.

### VS-07R Gateway: anonymous CA route, org-scoped Servers and Console routes, recording forwarder

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/07-gateway-routes` / `vertical-slice-vs07` |
| Commit subject | `fix(gateway): anonymous agent CA route, org-scoped Servers and Console routes, drop the JWT Agent policy [VS-07R]` |
| Security review | Yes (Gateway authorization policies and a new anonymous route) |
| Depends on | wave 1 merged, VS-34 included (#233 left 14 routes, on `main` as `32779b5`); FU-41a closed |
| Size | about 30k tokens |

**Status:** merged as `300bc33` (#242, 2026-10-28). The lost ledger amended this spec with FU-41a N1(e), N2 and N4 before dispatch (reconstructed from the commits): the packet also added `nodes-reservations-block` (D-45), removed the `client_type`/`agent_id` to `X-Client-Type`/`X-Agent-Id` enrichment in `RequestEnrichmentMiddleware` (both headers stay stripped, D-31), added `RouteAuthorizationGuardTests` (every route names a registered policy; `AgentCertificate` only on the forwarding routes; the JWT `Agent` policy is not registered; `/internal/...commands` is not routed), and changed the routing docs to say the Gateway does not check `org_id`, the services do. The text below is the spec as written on 2026-10-27.

**Goal.** Sprint 2 already shipped certificate ingress: the `AgentCertificate` policy, `AgentCertificateForwarding` and `agent-hub-route`. VS-07R does what is left of F-15:
- The agent CA certificate becomes fetchable without a client certificate. Today `/api/v1/agents/ca-certificate` falls into `agents-route`, which requires `AgentCertificate`, even though Nodes serves it anonymously (`AgentEndpoints.cs` line 57, and `/ca-certificate` is in `MtlsOptions.ExemptPaths`).
- The Servers and Console routes match the services' organization-scoped paths.
- The unused JWT `Agent` policy goes.
- The seam harness records every forwarded request, so rejection seams can prove which server answered.

The forwarding middleware and shared secret from VS-07 are dropped (D-31).

**Rules.**
- The Gateway sets `X-Client-Cert` only through Sprint 2's `AgentCertificateForwarding`. Add no second forwarding path, no shared secret and no header that Nodes would trust.
- The new anonymous route matches exactly `/api/v1/agents/ca-certificate`, with no catch-all and no parameter.
- Never log certificate bodies or tokens.
- No `Version=` in any `.csproj`. Nullable is enabled and warnings are errors (`CI=true`).
- Apply the wave-2 seam rule.

**Contract excerpt (C-1).**

| Route | Match | Cluster | Policy | Transform | Order | Rate limiter |
| --- | --- | --- | --- | --- | --- | --- |
| `agents-ca-route` (new) | `/api/v1/agents/ca-certificate` | `nodes` | `Anonymous` | `PathRemovePrefix /api/v1` | 22 | `Auth` (as `agents-enroll-route`) |
| `servers-org-route` (replaces `servers-route`) | `/api/v1/organizations/{organizationId}/servers/{**catch-all}` | `servers` | `TenantScoped` | `PathRemovePrefix /api/v1` | 20 | `PerTenant` |
| `console-org-route` (replaces `console-api-route`) | `/api/v1/organizations/{organizationId}/servers/{serverId}/console/{**catch-all}` | `console` | `TenantScoped` | `PathRemovePrefix /api/v1` | -1 | `PerTenant` |

In YARP the lower `Order` wins. `agents-ca-route` (22) beats `agents-enroll-route` (25) and `agents-route` (30). `console-org-route` (-1) beats `servers-org-route`, whose catch-all also matches console paths.

**Changes.**
1. `src/Dhadgar.Gateway/appsettings.json` (`ReverseProxy:Routes`): add `agents-ca-route`, and replace `servers-route` and `console-api-route` with the two org routes, exactly as in the table. Copy any other keys from the replaced routes, but not their `PathRemovePrefix` values. The result is **15 routes and 8 clusters**.
2. `src/Dhadgar.Gateway/Program.cs`: delete `options.AddPolicy("Agent", ...)` (line 106 on `5eead35`). First run `git grep -n '"Agent"' -- src tests deploy`. If a route or test still uses the policy, fix it when it is a test row; otherwise return `blocked` with reason `spec`.
3. `tests/Dhadgar.Gateway.Tests/RouteConfigurationTests.cs`: rename `ReverseProxyShouldHave14Routes` to `ReverseProxyShouldHave15Routes`, and update the rows for the three routes. Assert that `agents-ca-route` is `Anonymous` with an `Order` lower than both `agents-enroll-route` and `agents-route`, that both org routes strip `/api/v1`, and that `console-org-route` has `Order` -1. In `RateLimiterConfigurationTests.cs`, update the rows. Delete any test of the removed `Agent` policy.
4. `tests/Dhadgar.AgentSeam.Tests/GatewayNodesHarness.cs`: the forwarder records what it forwards. Add `public sealed record ForwardedRequest(string ClusterId, string Method, string Path);` to the file. `GatewayTestFactory` owns a `ConcurrentQueue<ForwardedRequest>` and exposes it as `public IReadOnlyCollection<ForwardedRequest> ForwardedRequests`. `NodesForwarderHttpClientFactory.CreateClient` wraps the TestServer handler in a small `DelegatingHandler` that enqueues `(context.ClusterId, request.Method.Method, request.RequestUri!.AbsolutePath)` before calling the inner handler. Keep the remote-address callback and the `CA2000` comment. `GatewayNodesHarness` exposes the same collection as `ForwardedRequests`.
5. New seam `tests/Dhadgar.AgentSeam.Tests/GatewayAgentIngressSeamTests.cs`, which uses `GatewayNodesHarness` (a fresh one per test, as the neighbouring classes do):
   - `CaCertificate_IsFetchableAnonymously`: `GET /api/v1/agents/ca-certificate` through the Gateway, with no certificate and no token, returns 200. The body is the CA PEM (read `GetCaCertificate`, `AgentEndpoints.cs` line 317, for the exact shape and assert it). `ForwardedRequests` contains `("nodes", "GET", "/agents/ca-certificate")`. Run it once before change 1 and quote the failure (expected: 401 or 403 from `AgentCertificate`, nothing forwarded).
   - `RenewEndpoint_WithoutCertificate_IsRejectedByGateway`: the rejection twin, which proves the anonymous route did not open the agent paths. `POST /api/v1/agents/{guid}/certificates/renew` with no certificate gets the Gateway's status (assert the exact value the run shows: 401 or 403), and no `ForwardedRequests` entry ends with `/certificates/renew`.
   - `CaCertificatePathVariant_IsNotAnonymous`: `GET /api/v1/agents/ca-certificate/x` with no certificate is not forwarded. This proves the route is an exact match.
   - Add no spoofed-header case. Sprint 2's `AgentCertificateForwardingTests.EnrollRoute_SpoofedHeader_IsStripped` and `GatewayAgentHubSeamTests.Hub_SpoofedForwardedHeaderWithoutTlsCert_IsRejected` cover it.

**Tests.** Changes 3 and 5. They fail when reverted: the CA request hits `agents-route` and is refused without forwarding, and the route count and route names no longer match.

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Gateway/Dhadgar.Gateway.csproj -c Release
dotnet test tests/Dhadgar.Gateway.Tests/Dhadgar.Gateway.Tests.csproj -c Release
dotnet test tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj -c Release
dotnet build Dhadgar.sln -c Release
```

**Docs owed.** `src/Dhadgar.Gateway/README.md`: the route table rows, and a sentence saying the CA certificate is public, while the rest of `/api/v1/agents/*` requires the client certificate. `docs/gateway-routing-reference.md`: the three rows.

**Done when.** The checks pass, the CA seam is green, and its red run is quoted.

### VS-09 Command signing on the control plane; key in the enrollment response

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/09-command-signing` / `vertical-slice-vs09` |
| Commit subject | `feat(nodes,agent): ECDSA command signing key, signer, and the signing key in the enrollment response and agent state (#94) [VS-09]` |
| Security review | Yes (key material; agent `EnrollmentService` and state store) |
| Depends on | wave 1 merged (#229 with VS-03R) |
| Size | about 40k tokens |

**Status (2026-10-28):** Writing, fresh coder in `vertical-slice-vs09` (base `c5ca3c4`). The first run's commits were never pushed and are lost. Two refinements the orchestrator gave that run, kept here because the lost ledger did not record them: test signing keys are written to temporary files (never a fixed path in the repo), and `ErrorCodePattern` must not accept a trailing newline (anchor with `\z`, or match with `RegexOptions` that make `$` mean end of input; add a `"code\n"` rejection row). Before review, ops merges `main` `101c36d` or later into the branch (D-38): `main` now has VS-11's Nodes edits (D-47) and VS-07R's `GatewayNodesHarness` changes; VS-08R also edits `src/Dhadgar.Nodes/Program.cs`, and the second of the two to merge resolves.

Amended 2026-10-27 for Sprint 2. The agent now persists the key in Sprint 2's `agent-state.json` (D-37), the seam is in `Dhadgar.AgentSeam.Tests`, and the construction sites are on `main`.

**Goal.** Nodes owns a P-256 signing key, signs `AgentCommandEnvelope`s per C-3, and hands the public key to agents at enrollment (D-09). The agent checks the key and keeps it in its state file, so VS-14 can verify commands after a restart.

**Rules.**
- Apply the wave-2 seam rule.
- Private key bytes and PEM buffers are zeroed after use (`CryptographicOperations.ZeroMemory`). `ECDsa` instances are disposed, or held by a singleton that disposes them.
- The key file is written with owner-only permissions on Unix (`File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite)` when not on Windows).
- File paths come from configuration only. Normalise them with `Path.GetFullPath`, and reject a relative `CommandSigning:KeyPath` and any path with `..` segments.
- The agent rejects a malformed signing key at enrollment with a `Result` failure; no exception escapes. `Result<T>` everywhere on the agent side. The shared `Result<T>` throws on a null success value, so never return `Success(null)`.
- `Dhadgar.Contracts.csproj` gets no package references. No `Version=` in any `.csproj`. Keep the line endings of edited files; new `.cs` files are CRLF.
- Nullable is enabled and warnings are errors (`CI=true`).
- Contract excerpt: C-3 (`AgentCommandSigning`, including the three constants this packet adds), C-4 (`EnrollNodeResponse` with `CommandSigningPublicKey` and `CommandSigningKeyId` appended after `NotAfter`), C-6 `CommandSigning:*`, and `AgentMessageLimits.MaxCommandLifetimeSeconds` (C-2).

**Changes.**
1. `src/Shared/Dhadgar.Contracts/Nodes/EnrollmentContracts.cs`: append `string CommandSigningPublicKey, string CommandSigningKeyId` to `EnrollNodeResponse` (7 parameters on `main`, 9 after), with XML docs (C-4). Fix every construction of **`Dhadgar.Contracts.Nodes.EnrollNodeResponse`** that the compiler reports. On `main` these are `src/Dhadgar.Nodes/Endpoints/AgentEndpoints.cs` line 81 and `tests/Dhadgar.Agent.Core.Tests/Authentication/EnrollmentServicePkcs8Tests.cs` line 147, plus any others the build finds. Leave the Nodes-local `Dhadgar.Nodes.Models.EnrollNodeResponse` (`LocalEnrollNodeResponse`) unchanged; `AgentEndpoints` maps it to the contract and adds the two fields there.
1a. Contract constants (C-2, C-3; security-2 found these fields unbounded). Append them with XML docs in the style of the neighbours, keeping CRLF:
   - `src/Shared/Dhadgar.Contracts/Agents/AgentProtocol.cs`, class `AgentMessageLimits`: `public const int MaxErrorCodeLength = 64;` and `public const string ErrorCodePattern = "^[A-Za-z0-9._-]{1,64}$";`. These bound `AgentCommandResult.ErrorCode` and `AgentCommandCompleted.ErrorCode`. The agent's `[Category.Code]` codes, such as `Java.NoRuntimeForPlatform`, match the pattern.
   - `src/Shared/Dhadgar.Contracts/Agents/AgentCommandSigning.cs`: `public const int SignatureBytes = 64;`, `public const int SignatureBase64Length = 88;` and `public const int KeyIdLength = 64;`.
   - Do not change any existing member.
2. New `src/Dhadgar.Nodes/Services/CommandSigning/CommandSigningOptions.cs`: section `CommandSigning`, with `string? KeyPath` and `int CommandTtlSeconds = 60` carrying `[Range(10, AgentMessageLimits.MaxCommandLifetimeSeconds)]`, so 10..60. A TTL above 60 would make every conforming agent reject every command (C-2 acceptance rule 3).
3. New `src/Dhadgar.Nodes/Services/CommandSigning/ICommandSigner.cs` and `CommandSigner.cs` (singleton):

```csharp
public interface ICommandSigner
{
    string PublicKeySpkiBase64 { get; }
    string KeyId { get; }
    Task InitializeAsync(CancellationToken ct = default);   // load or create the key
    AgentCommandEnvelope Sign(AgentCommandEnvelope unsigned); // returns a copy with Signature and SigningKeyId set
}
```

   - Key source: `CommandSigning:KeyPath` if it is set. That is a PEM file (`-----BEGIN PRIVATE KEY-----` or `EC PRIVATE KEY`) holding a P-256 key.
   - Otherwise, `command-signing-key.pem` in the same directory `LocalFileCaStorageProvider` uses (read that class and reuse its directory resolution). On first start, create the key with `ECDsa.Create(ECCurve.NamedCurves.nistP256)` and write it with `ExportPkcs8PrivateKeyPem()`.
   - `Sign` uses `AgentCommandSigning.BuildSigningInput(unsigned)` and `SignData(..., HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)`.
4. `src/Dhadgar.Nodes/Program.cs`: bind `CommandSigningOptions` with validation, register `ICommandSigner`, and call `InitializeAsync` at startup right after the CA initialization, with the same try/log pattern.
5. `src/Dhadgar.Nodes/Endpoints/AgentEndpoints.cs` `Enroll`: inject `ICommandSigner` and fill the two new fields.
6. Agent enrollment (`src/Agents/Dhadgar.Agent.Core/Authentication/EnrollmentService.cs`):
   - After deserializing the response, check `CommandSigningPublicKey`: it is base64, it imports with `ECDsa.ImportSubjectPublicKeyInfo`, and `KeySize == 256`. Also check that `AgentCommandSigning.ComputeKeyId(spki) == CommandSigningKeyId`.
   - Otherwise return `Result.Failure("[Enrollment.InvalidSigningKey] Control plane returned an invalid command signing key")`, **before** storing the certificate.
   - Add `CommandSigningPublicKey` and `CommandSigningKeyId` to `EnrollmentResult` (line 665).
6a. Agent persistence (D-37; this was VS-12's job before Sprint 2):
   - `src/Agents/Dhadgar.Agent.Core/Identity/AgentIdentity.cs`: append `string? CommandSigningPublicKey = null, string? CommandSigningKeyId = null` to the record. Existing callers keep compiling.
   - `FileAgentStateStore.cs`: add the two nullable fields to `AgentStateFile` (line 228). Map them both ways (the load at line 86, the save at line 149).
   - On load, a file with exactly one of the two fields set is invalid. It returns the same failure the store already returns for an invalid state file (read lines 70-100 for it). A file with neither field set loads as an identity without a key: files written by Sprint 2 agents. VS-14 decides what an agent without a key does with commands.
   - `src/Agents/Dhadgar.Agent.Core/Hosting/AgentBootstrapService.cs` line 263: build the identity with the two values from `enrolled.Value`. The seeded identity at line 210 keeps nulls.
7. `tests/Dhadgar.Nodes.Tests/NodesWebApplicationFactory.cs` and `TestCertificateAuthorityService.cs`: the factory needs a signer. Either register the real `CommandSigner` with `CommandSigning:KeyPath` pointing at a key generated into a per-factory temp file, or register a `TestCommandSigner` that wraps a fresh in-memory key. Choose the smaller change and say which in the report. `AgentSeamHarness.CreateFactoryWithRealCa` derives from this factory, so the seams get the signer too.

**Tests.**
- `tests/Dhadgar.Nodes.Tests/CommandSigning/CommandSignerTests.cs`:
  - a signature verifies with the public key over `AgentCommandSigning.BuildSigningInput`;
  - the signature string is exactly `SignatureBase64Length` chars and decodes to `SignatureBytes`;
  - `KeyId == ComputeKeyId(spki)`, and it has `KeyIdLength` chars;
  - a second signer over the same directory loads the same key (same `KeyId`);
  - a `KeyPath` with a P-384 key fails initialization, and a relative `KeyPath` fails validation;
  - `CommandTtlSeconds` 61 and 9 fail options validation, while 60 and 10 pass.
- `tests/Dhadgar.Contracts.Tests/Agents/AgentMessageLimitsTests.cs`: pin `MaxErrorCodeLength` and `ErrorCodePattern`. `Java.NoRuntimeForPlatform` and `AgentBusy` match; `a b`, `x\ny`, a 65-char code and the empty string do not.
- `AgentCommandSigningTests.cs`: `Convert.ToBase64String(new byte[SignatureBytes]).Length == SignatureBase64Length`, and `ComputeKeyId(...)` of the golden SPKI has `KeyIdLength` chars.
- `tests/Dhadgar.Agent.Core.Tests/Authentication/EnrollmentServiceSigningKeyTests.cs`: a stub `HttpMessageHandler` returns an enrollment response.
  - A malformed key gives a failure with `InvalidSigningKey`, and the cert store is untouched.
  - A key-id mismatch gives a failure.
  - A valid key gives success, with the fields on `EnrollmentResult`.
- `tests/Dhadgar.Agent.Core.Tests/Identity/FileAgentStateStoreTests.cs` (extend it):
  - the two fields round-trip;
  - a Sprint 2 file without them loads with nulls;
  - a file with only one of them fails.
- `tests/Dhadgar.Agent.Core.Tests/Hosting/AgentBootstrapServiceTests.cs`: a successful enrollment saves an identity carrying the result's key fields.
- Seam, in `tests/Dhadgar.AgentSeam.Tests/AgentBootstrapSeamTests.cs`. Extend the test that enrolls through the Gateway and reads the saved state (name it in the report). The persisted `AgentIdentity.CommandSigningKeyId` equals `harness.Nodes.Services.GetRequiredService<ICommandSigner>().KeyId`, and `ComputeKeyId(Convert.FromBase64String(CommandSigningPublicKey))` equals it too. This runs the real agent, the Gateway and Nodes together.

Why they fail when reverted: the response lacks the key fields (a compile failure in the seam), and the saved state has no key for VS-14 to verify commands with.

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet test tests/Dhadgar.Contracts.Tests/Dhadgar.Contracts.Tests.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
dotnet test tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj -c Release
dotnet build Dhadgar.sln -c Release
```

**Docs owed.**
- `docs/runbooks/nodes-service-operations.md`: the key file, backing it up with the CA, the `CommandSigning:*` keys, and "rotation not supported in MVP".
- `src/Agents/Dhadgar.Agent.Core/README.md`: the two state-file fields.
- ADR-0007: a dated note under its divergence note saying signing is implemented per D-09.

**Done when.** The checks pass.

### VS-08R Nodes agent hub: connection registry, connect-time node check, frame limit, Telemetry

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/08-agent-hub-registry` / `vertical-slice-vs08` |
| Commit subject | `feat(nodes): agent connection registry, connect-time node check, 512 KiB frame limit and Telemetry ack on /hubs/agent [VS-08R]` |
| Security review | Yes (agent-facing hub) |
| Depends on | wave 1 merged (#229 with VS-03R: `AgentHubAck`, `Telemetry`, `MaxReceiveMessageBytes`) |
| Size | about 35k tokens |

**Status (2026-10-28):** Writing. Pushed: `1c717d7` (part 1: registry, connect-time check, frame limit, `Telemetry`) and `b0e6509` (part 2: README, `GatewayAgentHubSeamTests`); local `6d0a82f` merges `main` `101c36d` in. The first coder ran out of context; a continuation is finishing. Reported so far: Nodes.Tests 518/518, AgentSeam 9/9, and one extra seam beyond this spec (a decommissioned node whose certificate stays active is still refused, which fails without the connect-time check; the spec's case passes on revocation alone). No review has passed yet; no PR exists.

**Goal.** Sprint 2's hub accepts heartbeats. Wave 3 also needs:
- Nodes knows which nodes are connected (VS-15 uses `IsConnected`).
- A connection for an unknown or decommissioned node is refused when it connects, not at its first heartbeat.
- Frames up to C-2's escaped worst case pass and bigger ones close (D-30).
- `Telemetry` is validated and acknowledged (D-28).

**Rules.**
- The node comes only from the `node_id` claim (D-S23). Payloads carry no node id (D-27).
- Business errors are acks (`new AgentHubAck(false, code)`), never a `HubException`. Never echo agent input into logs or error text; log the node id and counts only.
- Validate every field. SignalR binds hub arguments with its own JSON options, so null-check every reference field, require `double.IsFinite` for numbers and use `Enum.IsDefined` for enums. Never truncate on receipt. Unknown members are ignored (D-40; SignalR's binding already skips them), so do not add a check that rejects them. Missing or null required fields are still `invalid_telemetry`.
- Keep Sprint 2's existing connect checks and their order: protocol header (a missing header counts as `"1"`, D-32), `node_id`, then the current certificate. Keep `Heartbeat` as it is. The new checks come after them.
- `EnableDetailedErrors = false` stays.
- Hubs are transient and `NodesDbContext` is scoped. The registry is a singleton that holds no scoped service.
- The registry is single-instance for the MVP. Say in a comment that scale-out needs a Redis backplane (ADR-0008).
- Nullable is enabled and warnings are errors (`CI=true`).
- Apply the wave-2 seam rule.

**Contract excerpt.** C-2a: `AgentHubProtocol.MaxReceiveMessageBytes` (512 KiB), `AgentHubProtocol.NodeGroup`, `AgentHubErrorCodes.InvalidTelemetry`, `IAgentHub.Telemetry(AgentTelemetry) → AgentHubAck`. C-2: `AgentTelemetry(DateTimeOffset Timestamp, IReadOnlyDictionary<string, double> Metrics)`, `AgentMessageLimits.MaxTelemetryMetrics` (500) and `MaxMetricNameLength` (128). The "Limit enforcement" rows for the hub frame and for `AgentTelemetry`: `Metrics` non-null and ≤ 500 entries; each name 1..128 characters with no control characters; each value finite; `Timestamp` not `default`.

**Changes.**
1. New `src/Dhadgar.Nodes/Hubs/IAgentConnectionRegistry.cs` and `AgentConnectionRegistry.cs`, with `void Add(Guid nodeId, string connectionId)`, `void Remove(Guid nodeId, string connectionId)` (a no-op when absent), `bool IsConnected(Guid nodeId)` and `int ConnectedNodeCount`. Back it with a `ConcurrentDictionary<Guid, ImmutableHashSet<string>>` updated by `AddOrUpdate`; remove the key when its set becomes empty.
2. New `src/Dhadgar.Nodes/Hubs/AgentTelemetryValidator.cs`. Give it the same shape as Sprint 2's `AgentHeartbeatValidator` in the same folder (read it, and match its signature and code style). It returns either valid or `AgentHubErrorCodes.InvalidTelemetry`, and implements exactly the C-2 row.
3. `src/Dhadgar.Nodes/Hubs/AgentHub.cs`:
   - Inject `IAgentConnectionRegistry registry`.
   - `OnConnectedAsync`, after the certificate check and before the group join: load the node with `AsNoTracking`, selecting only `OrganizationId` and `Status` (check the entity's property names). If it is missing or `Decommissioned`, log a warning with the node id, call `Context.Abort()` and return. Then set `Context.Items["OrganizationId"] = organizationId`. Keep Sprint 2's `Groups.AddToGroupAsync(..., AgentHubProtocol.NodeGroup(nodeId))`, and call `registry.Add(nodeId, Context.ConnectionId)` last.
   - New `OnDisconnectedAsync`: if the connection has a node id, call `registry.Remove(nodeId, Context.ConnectionId)`, then call the base method.
   - `Telemetry`: replace VS-03R's stub. A failed validation returns `new AgentHubAck(false, code)`. A valid message is logged at Debug as `"Telemetry from node {NodeId}: {MetricCount} metrics"`, never the names or values, and returns `new AgentHubAck(true)`. It writes nothing to the database. The certificate re-check stays on `Heartbeat` (Sprint 2), which closes a connection whose certificate went bad.
4. `src/Dhadgar.Nodes/Program.cs`: set `o.MaximumReceiveMessageSize = AgentHubProtocol.MaxReceiveMessageBytes;` (it is `64 * 1024` on `main`, line 54) and register `builder.Services.AddSingleton<IAgentConnectionRegistry, AgentConnectionRegistry>();`.

**Tests.**
- `tests/Dhadgar.Nodes.Tests/Hubs/AgentConnectionRegistryTests.cs`: two connections for one node; removing one leaves the node connected, removing both leaves it disconnected; removing an unknown id is a no-op; `ConnectedNodeCount` is right.
- `tests/Dhadgar.Nodes.Tests/Hubs/AgentTelemetryValidatorTests.cs`: one `[Theory]` row per bound, each at the limit (valid) and one past it (`invalid_telemetry`):
  - 500 and 501 metrics;
  - a 128- and a 129-char name, an empty name, a name containing `\n`;
  - `NaN` and `PositiveInfinity` values;
  - null `Metrics`, and a `default` `Timestamp`.
- `tests/Dhadgar.Nodes.Tests/Hubs/AgentHubTests.cs` (extend it, using its existing connection helper):
  - Connect for an unknown node, and for a decommissioned node: the connection closes (the `Closed` event fires within 5 s, or `StartAsync` throws).
  - While a connection is open, the registry from `factory.Services` reports `IsConnected` true; after `DisposeAsync` it reports false (poll for up to 5 s).
  - Valid telemetry answers `Accepted == true`; 501 metrics answers `Accepted == false` with `ErrorCode == "invalid_telemetry"`.
  - `Telemetry_WithUnknownMember_IsAccepted` (D-40): `connection.InvokeAsync<AgentHubAck>("Telemetry", new { timestamp = now, metrics = new Dictionary<string, double> { ["cpu"] = 1 }, extra = "x" })` answers `Accepted == true`. `Telemetry_WithoutMetrics_IsRejected`: `new { timestamp = now, extra = "x" }` answers `invalid_telemetry`.
  - `Telemetry_AtEscapedWorstCase_IsAccepted`: 500 metric names of 128 `'é'` each. That is about 375 KiB on the wire, because each `é` is escaped to `é`. It is acknowledged and the connection stays open. It fails with `main`'s 64 KiB limit, which proves the limit was raised.
  - `Frame_OverLimit_ClosesConnection`: 700 metric names of 128 `'é'` each, about 525 KiB escaped, which is over 512 KiB. The `Closed` event fires within 5 s and no ack arrives. It must be the close and not an `invalid_telemetry` ack, which proves the frame limit is still enforced.
- Scope validation, in `AgentHubTests` or a new `HostScopeValidationTests.cs`: build `AgentSeamHarness.CreateFactoryWithRealCa(..., validateScopes: true)`, send one request, and resolve `IAgentConnectionRegistry`. The `Testing` environment hides singleton-to-scoped captures unless this runs.
- Seam, in `tests/Dhadgar.AgentSeam.Tests/GatewayAgentHubSeamTests.cs` (reuse the class's helpers for enrolling and for connecting through the Gateway with the client certificate):
  - `Telemetry_ThroughGateway_IsAccepted` (positive twin): enroll, connect through the Gateway and invoke `Telemetry`. The ack is `Accepted == true`, and Nodes' registry (`harness.Nodes.Services`) reports the node as connected.
  - `Connect_ForDecommissionedNode_IsClosedByNodes`: enroll, then decommission the node through `INodeService` in a scope from `harness.Nodes.Services` (read `NodeService.cs` around line 272 for the method). Connect through the Gateway with the same certificate. `StartAsync` either completes and `Closed` fires within 5 s, or throws. Either way, assert the negotiate reached Nodes: the Gateway hosts no `/hubs/agent` hub, so a completed negotiate came from Nodes. If `StartAsync` throws, assert on `HttpRequestException.StatusCode`, as the dispatch rules require. Do not depend on VS-07R's `ForwardedRequests`; that packet runs in parallel.

Why they fail when reverted: no registry exists (the code does not compile, or `IsConnected` stays false); a decommissioned node stays connected until its first heartbeat; the worst-case telemetry frame closes the connection; 501 metrics are accepted.

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj -c Release
dotnet build Dhadgar.sln -c Release
```

**Docs owed.** `src/Dhadgar.Nodes/README.md` "Agent hub": the connect checks in order, the registry, the 512 KiB frame limit, the `Telemetry` ack codes, and the single-instance note.

**Done when.** The checks pass and both new `GatewayAgentHubSeamTests` cases are green.

### VS-06R Nodes user JWT beside agent mTLS; strict AgentPolicy

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/06-nodes-user-jwt` / `vertical-slice-vs06` |
| Commit subject | `fix(nodes): user JWT beside agent mTLS via a path-selected default scheme; AgentPolicy accepts only mTLS identities [VS-06R]` |
| Security review | Yes (Nodes authentication and authorization) |
| Depends on | VS-32R (`SeamJwt`, AgentSeam's reference to `Dhadgar.Testing`) and VS-07R (`ForwardedRequests`) merged; #230 (`AddDhadgarJwtBearer`) |
| Size | about 40k tokens |

**Status (2026-10-28):** Ready. VS-32R (#243) and VS-07R (#242) are on `main` `101c36d`. Branch from `origin/main`. The lost ledger folded some FU-41a notes into this packet; their text was not recovered. Assumption: everything FU-41a raised about Nodes' own auth is either already in this spec (scheme selector, strict `AgentPolicy`, both rejection directions) or moved to VS-36 (reservation tokens). The reviewer and `security-advisor` check the merged packet against F-35 and VS-35's Gateway cases; anything else they find becomes a follow-up, not scope creep here. Use `SeamJwt` from `Dhadgar.Testing.Authentication` (D-43); AgentSeam.Tests already references it. Note for the selector: `/reservations/{token}` selects `JwtBearer`, which VS-36 relies on.

**Goal.**
- F-01: Nodes registers user JWT auth itself, instead of relying on a test scheme.
- F-02: `AgentPolicy` stops accepting any authenticated user.
- D-35: a path-selected default scheme keeps the two schemes apart, so a bearer token is never evaluated on agent paths and a certificate never on user paths.
- A `SeamJwt` user token reaches Nodes' tenant endpoints through the Gateway.

Dropped from VS-06, because Sprint 2 already shipped or covers them:
- the forwarded-certificate middleware and its secret (D-S25, D-S210 A, D-31);
- `AdditionalProtectedPrefixes`;
- the other-CA and `X-Node-Id` seams, which `Nodes.Tests/Integration/AgentHubSeamTests` covers.

**Rules.**
- Errors at HTTP boundaries are ProblemDetails or the existing `MtlsProblemWriter`. Add no new exception types.
- mTLS may only be disabled in Development, which `AddMtlsAuthentication` already enforces. Do not weaken it.
- Agent identity comes only from the certificate: `MtlsMiddleware` sets `node_id` from the SPIFFE SAN. No code may read a header or body field to decide which node a caller is.
- Do not change Sprint 2's `ForwardedClientCertificateMiddleware`, `TrustedProxies`, `AgentMtlsAuthenticationHandler` or the hub's connect checks.
- Change nothing in `src/Shared/Dhadgar.ServiceDefaults`. VS-04's `AddDhadgarJwtBearer` is used as it is; Nodes sets its own defaults after both registrations.
- No `Version=` in any `.csproj`. Nullable is enabled and warnings are errors (`CI=true`).
- Apply the wave-2 seam rule.

**Contract excerpt.**
- C-1: `nodes-org-route` (`TenantScoped`, strips `/api/v1`), `agents-route` and `agent-hub-route` (`AgentCertificate`).
- C-6: `Auth:Issuer`, `Auth:Audience` and `Auth:ClockSkewSeconds`.
- Policies: `TenantScoped` (unchanged) and `AgentPolicy` (an mTLS identity with `node_id`).

**Changes.**
1. New `src/Dhadgar.Nodes/Auth/NodesAuthenticationSchemes.cs`:

```csharp
public static class NodesAuthenticationSchemes
{
    /// <summary>Policy scheme that picks agent mTLS or user JWT by path (D-35).</summary>
    public const string Default = "NodesAuth";

    public static string SelectScheme(HttpContext context)
    {
        var mtls = context.RequestServices.GetRequiredService<IOptions<MtlsOptions>>().Value;
        var path = context.Request.Path;
        return path.StartsWithSegments(Prefix(mtls.AgentEndpointPrefix), StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments(Prefix(mtls.HubPathPrefix), StringComparison.OrdinalIgnoreCase)
            ? AgentMtlsAuthenticationHandler.SchemeName
            : JwtBearerDefaults.AuthenticationScheme;
    }
    // Prefix(): the same normalisation MtlsMiddleware uses (NormalizePrefix, around line 167); make that helper internal and reuse it.
}
```

2. `src/Dhadgar.Nodes/Program.cs`:
   - After `builder.Services.AddMtlsAuthentication(builder.Configuration);` (line 71), add `builder.Services.AddDhadgarJwtBearer(builder.Configuration, builder.Environment);`. Read the helper's signature; Nodes hosts no user hub, so pass no hub path.
   - Then add the selector. It must come after both registrations, because each of them sets `DefaultScheme` and the last `Configure` wins:

```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = NodesAuthenticationSchemes.Default;
    options.DefaultChallengeScheme = NodesAuthenticationSchemes.Default;
    options.DefaultForbidScheme = NodesAuthenticationSchemes.Default;
})
.AddPolicyScheme(NodesAuthenticationSchemes.Default, "Agent mTLS or user JWT",
    o => o.ForwardDefaultSelector = NodesAuthenticationSchemes.SelectScheme);
```

   - In `AgentPolicy`, keep `RequireAuthenticatedUser()` and add `policy.AddRequirements(new AgentIdentityRequirement());`. Register `builder.Services.AddSingleton<IAuthorizationHandler, AgentIdentityHandler>();`. `TenantScoped` stays as it is.
3. New `src/Dhadgar.Nodes/Auth/AgentIdentityRequirement.cs`, holding both the requirement and the handler:

```csharp
public sealed class AgentIdentityRequirement : IAuthorizationRequirement { }

public sealed class AgentIdentityHandler(IOptions<MtlsOptions> mtls) : AuthorizationHandler<AgentIdentityRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AgentIdentityRequirement requirement)
    {
        var hasNodeId = Guid.TryParse(context.User.FindFirst("node_id")?.Value, out _);
        var viaMtls = context.User.Identities.Any(i => i.IsAuthenticated && i.AuthenticationType == MtlsMiddleware.AuthenticationType);
        // mTLS can only be disabled in Development (AddMtlsAuthentication enforces it); tests rely on this.
        if (hasNodeId && (viaMtls || !mtls.Value.Enabled)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
```

4. `src/Dhadgar.Nodes/Auth/MtlsMiddleware.cs`: add `public const string AuthenticationType = "mTLS";` and use it at line 152 (`new ClaimsIdentity(claims, AuthenticationType)`). Check `AgentMtlsAuthenticationHandler` for a second literal `"mTLS"` and use the constant there too.
5. `src/Dhadgar.Nodes/appsettings.json`: add the `Auth` section with the same keys and values as the Gateway's file after #230 (the helper's required keys).
6. Seam harness opt-in, in `tests/Dhadgar.AgentSeam.Tests/GatewayNodesHarness.cs`. Real user auth must run in Nodes and in the Gateway:
   - `GatewayNodesHarness` gets a constructor parameter `bool realUserAuth = false`. When it is true, the Nodes factory from `CreateFactoryWithRealCa(..., validateScopes: true)` is wrapped with `.WithWebHostBuilder(b => b.ConfigureTestServices(s => { s.PostConfigure<AuthenticationOptions>(o => { o.DefaultAuthenticateScheme = null; o.DefaultChallengeScheme = null; }); SeamJwt.Apply(s); }))`. This undoes `NodesWebApplicationFactory`'s test-scheme override (lines 128-129), so `Program.cs`'s defaults apply.
   - `GatewayTestFactory` gets an optional `Action<IServiceCollection>? configureTestServices` constructor parameter, invoked inside its `ConfigureTestServices`. The harness passes `SeamJwt.Apply` when `realUserAuth` is set.
   - Existing harness users are unchanged.

**Tests.**
- `tests/Dhadgar.Nodes.Tests/Auth/AgentIdentityHandlerTests.cs`:
  - an mTLS identity with `node_id` succeeds;
  - a `"Bearer"` identity with `node_id` and mTLS enabled fails;
  - the same identity with mTLS disabled succeeds;
  - an identity with no `node_id` fails.
- `tests/Dhadgar.Nodes.Tests/Auth/NodesAuthenticationSchemesTests.cs` (`DefaultHttpContext` whose `RequestServices` hold default `MtlsOptions`):
  - `/agents/x/heartbeat` and `/hubs/agent/negotiate` select `AgentMtls`, and so does `/AGENTS/x` (the match ignores case);
  - `/agentsx` and `/organizations/o/nodes` select `JwtBearer`, which proves the match is segment-aware.
- Seam `tests/Dhadgar.AgentSeam.Tests/GatewayUserJwtSeamTests.cs`, with `new GatewayNodesHarness(dir, realUserAuth: true)` per test:
  - `DefaultSchemes_AreTheNodesSelector`: from `harness.Nodes.Services`, `IAuthenticationSchemeProvider.GetDefaultAuthenticateSchemeAsync()` and `GetDefaultChallengeSchemeAsync()` are both `NodesAuth`. Reverted, the last registration wins and this fails.
  - `UserToken_ThroughGateway_ReachesNodes`: `GET /api/v1/organizations/{O}/nodes` through the Gateway with `SeamJwt.CreateUserToken(user, O)` returns 200, the body deserializes as `PagedNodesResponse`, and `ForwardedRequests` contains `("nodes", "GET", "/organizations/{O}/nodes")`.
  - `TokenForOtherOrganization_IsRejectedByNodes`: the same request with a token for org P returns 403, and the request **was** forwarded. The Gateway's `TenantScoped` policy only requires an authenticated user, so the 403 comes from Nodes' tenant check. If Nodes answers something else, assert that exact status and say so in the report.
  - `NoToken_IsRejectedByGateway`: returns 401, and nothing is forwarded for that path.
  - `UserToken_OnAgentEndpoint_IsRejectedByNodes`: directly against Nodes, `POST /agents/{nodeId}/heartbeat` with the user token and no certificate returns 401. Its twin sends the same request with the enrolled certificate in Sprint 2's forwarded header (`AgentCertificateProfile.ForwardedCertificateHeader`; read it for the encoding) from `GatewayTestFactory.GatewayAddress`, and gets 200. To make that client, use `harness.Nodes.Server.CreateHandler(ctx => ctx.Connection.RemoteIpAddress = GatewayTestFactory.GatewayAddress)`, and read `AgentEndpoints.cs` for the heartbeat body.
  - `AgentCertificate_OnUserRoute_IsRejectedByNodes`: directly against Nodes, `GET /organizations/{O}/nodes` with the enrolled certificate forwarded and no token returns 401.
  - Run `DefaultSchemes_AreTheNodesSelector` and `UserToken_ThroughGateway_ReachesNodes` once before change 2 and quote both failures.
- Existing `Nodes.Tests` and `AgentSeam.Tests` stay green. `NodesWebApplicationFactory` keeps its test-scheme override, and with mTLS disabled `AgentIdentityHandler` accepts `node_id` claims.

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj -c Release
dotnet test tests/Dhadgar.Gateway.Tests/Dhadgar.Gateway.Tests.csproj -c Release
dotnet build Dhadgar.sln -c Release
```

**Docs owed.**
- `src/Dhadgar.Nodes/README.md`, auth section: user JWT on `/organizations/...`, agent mTLS on `/agents` and `/hubs/agent`, the `NodesAuth` selector, and `AgentPolicy`.
- `docs/runbooks/nodes-service-operations.md`: the `Auth:*` keys.

**Done when.** The checks pass, and the report quotes the two red runs.

## Wave 2 hotfixes and FU-41a follow-ups (2026-10-28)

FU-41a, the post-merge security pass on `main`'s Gateway auth block, ran on 2026-10-28. Its report was lost with the cloud session. What survives: N1(e), N2 and N4 went to VS-07R (merged), N3 to VS-32R (merged), N5 to HF-01 (merged). HF-01's own security pass then found the zone-wide cookie and the wildcard DNS (F-32), which became HF-02 and HF-02D after the owner's OD-3 and OD-4. The remaining Gateway separation checks became VS-35, and the reservation tokens became VS-36 (F-35). Each packet below states where its detail had to be assumed.

### HF-01 Drop the parked cart origin (record)

Merged as `28c23a3` (#238). It removes `https://cart.meridianconsole.com` from Gateway `Cors:AllowedOrigins`, BetterAuth trusted origins (compose) and `exchange.js`'s `client_app` mapping; it removes the dead `firewall` entry from `OpenApiAggregationService`, and the matching README lines. The owner said on 2026-10-28 that the ShoppingCart site is parked and the host is not served. Packet review 2 passes, security review 2 passes.

### HF-02D DNS records without wildcards (record)

Merged as `101c36d` (#240). `deploy/scripts/configure-cloudflare-dns.sh` creates explicit proxied records (D-48 b), never a wildcard, and deletes the environment's `*.` CNAME only after every explicit record succeeded. The repository change does not touch Cloudflare; the owner runs it. The coder's checklist was lost with the `vertical-slice-hf02d` worktree; HF-02's "Owner steps" carry a rebuilt one.

### HF-02 Host-only login cookie; BetterAuth fails closed; drop `localhost:4322`

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/hf02-auth-surface` / `vertical-slice-hf02` |
| Base | `origin/main` `101c36d` or later. The lost spec stacked it on `vs/hf01-parked-origins`; HF-01 merged as `28c23a3` and that branch is deleted, so `main` is the base and the PR targets `main`. |
| Commit subject | `fix(security): host-only BetterAuth cookie, fail-closed trusted origins, drop localhost:4322 [HF-02]` |
| Security review | **Yes** (session cookie scope, CORS, a CI workflow) |
| Decisions | D-48 (a), D-49, D-50 (owner, OD-3) |
| Size | about 25k tokens |

**Why the cookie needs one host (F-33, traced 2026-10-28).** Every cookie-bearing request goes to the Gateway host that serves `/api/v1/betterauth`: SharedAuth's `signIn` (sets BetterAuth's OAuth state cookie), the provider callback at `BETTER_AUTH_URL/callback/<provider>` (sets the session cookie), `get-session`, `exchange` and `sign-out`, each with `credentials: 'include'` (`src/Dhadgar.SharedAuth/src/client.ts`). Identity's `/exchange` and `/refresh` take tokens in the body. The Panel keeps Identity tokens in `sessionStorage` and sends bearer tokens. Scope calls no auth endpoint. So the cookie needs no `Domain` attribute: a host-only cookie on the Gateway host is enough, and every other subdomain stops receiving it. `SameSite=None; Secure; HttpOnly; Partitioned` stay as they are (D-48 does not change them; `SameSite=None` keeps form-post OAuth callbacks such as Apple's working). This works only while `BETTER_AUTH_URL`'s host equals the Panel's `PUBLIC_GATEWAY_URL` host, which is true in compose (both `dev.meridianconsole.com`).

**Rules.**
- No new npm dependency and no `package-lock.json` change. Tests use Node's built-in `node:test` and `node:assert/strict` (Node 22, the Dockerfile's base).
- Never log a cookie, a session token or the secret. Error messages name the variable and the offending origin only.
- Do not change SharedAuth, the Panel, Identity or the Gateway's C# code. The Gateway change is `appsettings.json` only.
- Keep each file's line endings. No `Version=` in any `.csproj`.

**Changes.**
1. New `src/Dhadgar.BetterAuth/src/config.js`, pure functions with no imports and no side effects:

```javascript
// HF-02 (D-48, D-49): trusted origins and cookie options, kept pure so node:test can load them.
export function parseTrustedOrigins(raw) {
  return (raw ?? "").split(",").map((o) => o.trim()).filter(Boolean);
}

export function isDevelopment(env) {
  return env.NODE_ENV === "development"; // unset is NOT development: fail closed
}

// Throws outside development when the list is empty, holds "*", or holds an entry that is not an exact origin.
export function assertTrustedOrigins(origins, env) {
  if (isDevelopment(env)) return;
  if (origins.length === 0) throw new Error("BETTER_AUTH_TRUSTED_ORIGINS must be set outside development (NODE_ENV=development)");
  for (const o of origins) {
    if (o === "*") throw new Error("BETTER_AUTH_TRUSTED_ORIGINS cannot contain '*' outside development");
    let url;
    try { url = new URL(o); } catch { throw new Error(`BETTER_AUTH_TRUSTED_ORIGINS entry is not a URL: ${o}`); }
    if ((url.protocol !== "https:" && url.protocol !== "http:") || url.origin !== o) {
      throw new Error(`BETTER_AUTH_TRUSTED_ORIGINS entry must be an exact origin (scheme://host[:port], no path or trailing slash): ${o}`);
    }
  }
}

export function loadTrustedOrigins(env) {
  const origins = parseTrustedOrigins(env.BETTER_AUTH_TRUSTED_ORIGINS);
  assertTrustedOrigins(origins, env);
  return origins;
}

export function isCorsOriginAllowed(origin, origins, env) {
  if (!origin) return true;                               // non-browser callers, unchanged
  if (origins.includes(origin)) return true;
  return origins.length === 0 && isDevelopment(env);      // allow-all only in development
}

// D-48 (a): no crossSubDomainCookies and no Domain attribute, so cookies are host-only.
export function buildAdvancedOptions() {
  return {
    defaultCookieAttributes: { sameSite: "none", secure: true, httpOnly: true, partitioned: true },
  };
}
```

2. `src/Dhadgar.BetterAuth/src/auth.js`: replace the `trustedOrigins` expression with `export const trustedOrigins = loadTrustedOrigins(process.env);` (import from `./config.js`), and replace the whole `advanced: { ... }` block, comments included, with `advanced: buildAdvancedOptions(),`.
3. `src/Dhadgar.BetterAuth/src/server.js`:
   - Next to `parseTrustProxy` (before `loadSecrets()`), call `loadTrustedOrigins(process.env);` so a bad value stops startup before secrets or migrations. Extend the comment above it to say so.
   - In the CORS `origin` callback, replace `if (trustedOrigins.length === 0 || trustedOrigins.includes(origin))` and the `!origin` branch with `if (isCorsOriginAllowed(origin, trustedOrigins, process.env)) return callback(null, true);`.
4. `src/Dhadgar.BetterAuth/package.json`: add `"test": "node --test"` to `scripts`. New `src/Dhadgar.BetterAuth/test/config.test.js` (below).
5. `src/Dhadgar.BetterAuth/.env.example`: add `NODE_ENV=development` with a comment that any other value (or none) requires `BETTER_AUTH_TRUSTED_ORIGINS`.
6. D-50: remove `http://localhost:4322` from `src/Dhadgar.Gateway/appsettings.json` `Cors:AllowedOrigins` and from `deploy/compose/docker-compose.services.yml` `BETTER_AUTH_TRUSTED_ORIGINS` (line 205). Keep `http://localhost:4321`.
7. CI: in `.github/workflows/_dotnet-build-test.yml`, right after the `npm ci (parallel)` step, add:

```yaml
      - name: BetterAuth unit tests
        if: inputs.run-tests
        working-directory: src/Dhadgar.BetterAuth
        run: npm test
```

8. New `tests/Dhadgar.Gateway.Tests/AllowedOriginsGuardTests.cs` (uses `RepoFiles.FindRepoRoot()` from VS-32R and `System.Text.Json`):
   - `GatewayCorsOrigins_AreExactlyTheServedFrontends`: `Cors:AllowedOrigins` in `src/Dhadgar.Gateway/appsettings.json` equals, as a set, `https://panel.meridianconsole.com`, `https://meridianconsole.com`, `https://dev.meridianconsole.com`, `http://localhost:4321`.
   - `ComposeTrustedOrigins_AreExactlyTheServedFrontends`: the `BETTER_AUTH_TRUSTED_ORIGINS:` value in `deploy/compose/docker-compose.services.yml`, split on commas, equals `https://dev.meridianconsole.com`, `https://panel.meridianconsole.com`, `http://localhost:4321`.
   - Each failure message names the extra or missing origin, so a future change has to edit the test on purpose.

**Tests (`test/config.test.js`, `node:test`).**
- `parseTrustedOrigins`: trims, drops empties, `undefined` gives `[]`.
- `assertTrustedOrigins`: throws for `[]` with `NODE_ENV` `production` and with `NODE_ENV` unset; throws for `["*"]`, for `["https://panel.meridianconsole.com/"]` (trailing slash) and for `["panel.meridianconsole.com"]`; passes for the compose list; passes for `[]` with `development`.
- `isCorsOriginAllowed`: `production` with an empty list and origin `https://evil.example` is **false** (reverting the old `length === 0 ||` makes this true); a listed origin is true; no origin is true; `development` with an empty list is true; an unlisted origin with a non-empty list is false in both modes.
- `buildAdvancedOptions`: has no `crossSubDomainCookies` key and no `domain` anywhere in its JSON; `defaultCookieAttributes` is exactly the four attributes above. Reverted (zone-wide domain) this fails.

Why they fail when reverted: the old `auth.js` sets a zone domain, the old CORS callback allows everything with an empty list, the old startup accepts an empty list, and the guard tests find `localhost:4322`.

**Checks (in order, PowerShell, in the worktree).**

```powershell
npm ci --prefix src/Dhadgar.BetterAuth --no-audit
npm test --prefix src/Dhadgar.BetterAuth
node --check src/Dhadgar.BetterAuth/src/server.js
node --check src/Dhadgar.BetterAuth/src/auth.js
dotnet build src/Dhadgar.Gateway/Dhadgar.Gateway.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests/Dhadgar.Gateway.Tests/Dhadgar.Gateway.Tests.csproj -c Release
```

Also run once, and quote in the report: `$env:NODE_ENV='production'; $env:BETTER_AUTH_TRUSTED_ORIGINS=''; node -e "import('./src/Dhadgar.BetterAuth/src/config.js').then(m => m.loadTrustedOrigins(process.env))"` exits non-zero with the "must be set outside development" message.

**Docs owed.**
- `src/Dhadgar.BetterAuth/README.md`: the env table (`BETTER_AUTH_TRUSTED_ORIGINS` is required outside development, no longer "empty = all"; `NODE_ENV` has no default in code, and anything but `development` fails closed); "Cookie Configuration" (host-only, why, the `BETTER_AUTH_URL` host = Panel `PUBLIC_GATEWAY_URL` host invariant); remove `crossSubDomainCookies` from the examples and the "Cross-subdomain cookie support" bullet; the troubleshooting "Domain mismatch" item.
- `src/Dhadgar.Gateway/README.md` and `docs/gateway-routing-reference.md`: drop `http://localhost:4322` from the CORS examples.
- `src/Dhadgar.Panel/README.md` "Port already in use": replace `PORT=4322` with `PORT=4331`, and say that a second local port must also be added to the local BetterAuth and Gateway origin lists, never committed.

**Owner steps (outside the code; the PR body lists them).**
1. HF-02D, if not done yet (rebuilt checklist, reconstructed 2026-10-28): (a) in Cloudflare, export or screenshot the zone's DNS records, and list the records the script will overwrite (apex, `www`, `api`, `panel`, `dev`, `api.dev`, `staging`, `api.staging`, `staging.panel`); (b) run the `infra` workflow's DNS job (`configure-cloudflare-dns.sh <env> <ipv4>`) for dev, check, then staging, then prod; (c) after each, `nslookup` every name above (each resolves, proxied) and a made-up name such as `nohost-<random>.meridianconsole.com` (must not resolve), then load each served host over HTTPS; (d) rollback: re-create the deleted `*.` CNAME as it was (proxied to the environment apex) and re-run the previous script version.
2. Before deploying HF-02 anywhere outside development: confirm `BETTER_AUTH_TRUSTED_ORIGINS` is set there (BetterAuth now refuses to start without it), and that `BETTER_AUTH_URL`'s host equals the Panel's `PUBLIC_GATEWAY_URL` host.
3. After deploying HF-02: browsers still hold the old zone-wide cookies for up to 7 days. Revoke them by deleting BetterAuth's sessions (`DELETE FROM "session";` in the BetterAuth database, `dhadgar_platform` in compose); every user signs in again, and the old cookies map to no session. The signed session cache cookie expires within 5 minutes; rotating `BETTER_AUTH_SECRET` instead also works and invalidates both at once.

**Assumptions (the lost spec could not be recovered).** The first HF-02 run reached `cdd5dac` with both reviews `ship` and was summarised as "the cookie now covers only the exact host that set it, BetterAuth refuses to start without a trusted-origins list outside development, and localhost:4322 is removed". The split into `config.js`, the `node:test` file, the CI step and the Gateway guard tests are this rebuild's choices. Whether the first run also changed SameSite or the cookie prefix is unknown; this spec keeps both. A `__Host-` cookie prefix is a possible follow-up, not part of D-48.

**Done when.** The checks pass, the fail-closed run is quoted, and the PR body carries the owner steps.

### VS-35 Gateway auth-separation tests

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/35-gateway-auth-separation` / `vertical-slice-vs35` |
| Base | `origin/main` `101c36d` or later |
| Commit subject | `test(gateway): user JWTs and agent certificates cannot cross routes; no secrets in logs [VS-35]` |
| Security review | Yes (Gateway authorization; it pins security behaviour) |
| Depends on | VS-32R (`SeamJwt`, `RepoFiles`) and VS-07R (route set, `RouteAuthorizationGuardTests`), both merged |
| Size | about 25k tokens |

**Goal (FU-41a, reconstructed 2026-10-28).** FU-41a reviewed the Gateway file where agent certificates and user logins now meet, and asked for tests proving that neither credential opens the other's routes, that tokens and certificates do not reach logs, and that the dead `Agent` policy cannot come back (VS-07R pins the last one). VS-35 adds those runtime tests. It changes no production code. If a test shows the Gateway does the wrong thing, the packet stops with `blocked` reason `spec`, quoting the response, and the orchestrator opens a fix packet. Assumption: the exact FU-41a note list was lost; the cases below are the separation properties the transcript names, checked against `main`'s routes.

**Rules.**
- Tests only, in `tests/Dhadgar.Gateway.Tests`. Never log or print a token or a certificate; assert with `Assert.DoesNotContain`.
- Tokens from `SeamJwt` only (D-22, D-43). Certificates from the existing helper in `AgentCertificateForwardingTests` (`CreateCertificate(AgentCertificateProfile.CaCommonName)`).
- Every rejection asserts the Gateway's status **and** that the capture handler saw no request; every rejection has a positive twin that differs only in the credential.
- Nullable is enabled and warnings are errors.

**Changes.**
1. Move `AgentCertificateForwardingTests`' private helpers (`CreateApp`, `SendAsync`, `CaptureHandler` with its `Requests` record, `CreateCertificate`) unchanged into a new `internal static class GatewayCaptureHost` in `tests/Dhadgar.Gateway.Tests/GatewayCaptureHost.cs`, and make `AgentCertificateForwardingTests` call it. Add an optional `Action<IServiceCollection>? configureServices` parameter to `CreateApp` (invoked in its `ConfigureTestServices`) and an optional bearer token parameter to `SendAsync`. Existing tests stay green and unchanged in behaviour.
2. New `tests/Dhadgar.Gateway.Tests/GatewayAuthSeparationTests.cs`, same collection as `AgentCertificateForwardingTests`, with `SeamJwt.Apply` passed to `CreateApp` and a `CapturingLoggerProvider` (from `HubTokenTests`) registered:
   - `UserJwt_OnAgentRoute_IsRejected` (`POST /api/v1/agents/{guid}/heartbeat`, bearer only): 401 or 403 (assert the value the run shows), nothing captured. Twin: the same request with the agent certificate is captured.
   - `UserJwt_OnAgentHub_IsRejected` (`POST /hubs/agent/negotiate`, bearer only): same shape, twin with the certificate.
   - `AgentCertificate_OnUserRoute_IsRejected` (`GET /api/v1/organizations/{O}/nodes`, certificate only): 401 or 403 (assert the value the run shows), nothing captured. Twin: bearer for `O` is captured.
   - `BothCredentials_OnUserRoute_ForwardNoClientCertHeader`: bearer plus certificate on the org nodes route is captured, and the captured request has no `AgentCertificateProfile.ForwardedCertificateHeader`.
   - `QueryToken_OnlyCountsOnConsoleHub`: `GET /api/v1/organizations/{O}/nodes?access_token=<token>` with no header is 401 and not captured; the twin `GET /hubs/console/negotiate?negotiateVersion=1&access_token=<token>` is not 401 (VS-04 behaviour).
   - `QueryToken_DoesNotOpenAgentRoutes`: `POST /hubs/agent/negotiate?access_token=<token>` without a certificate is rejected and not captured.
   - `NoSecretsInGatewayLogs`: after the cases above run in one app, no captured log line contains the token string, the certificate's base64 DER, or its PEM body.
3. `docs/gateway-authentication.md`: one short "Credential separation" paragraph listing what the tests pin.

**Why they fail when reverted** (proved by mutation, quoted in the report): temporarily set `agents-route`'s policy to `TenantScoped` in a local copy of `appsettings.json` and show `UserJwt_OnAgentRoute_IsRejected` fails; temporarily register the JWT handler's query-token hook for all paths (or point `console-hub-route`'s match at `/api/v1/organizations/{**}`) and show `QueryToken_OnlyCountsOnConsoleHub` fails. Revert both before committing.

**Checks (in order).**

```powershell
dotnet build tests/Dhadgar.Gateway.Tests/Dhadgar.Gateway.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests/Dhadgar.Gateway.Tests/Dhadgar.Gateway.Tests.csproj -c Release
```

**Docs owed.** Change 3.

**Done when.** The checks pass and the two mutation runs are quoted.

### VS-36 Lock down Nodes reservation-token endpoints

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/36-reservation-tokens` / `vertical-slice-vs36` |
| Base | `origin/main` `101c36d` or later |
| Commit subject | `fix(nodes): reservation token endpoints accept only users of the owning organization [VS-36]` |
| Security review | Yes (Nodes authorization) |
| Depends on | none to start. VS-06R and VS-09/VS-08R also edit `src/Dhadgar.Nodes/Program.cs`; whichever merges later merges `main` in and resolves (D-38). |
| Size | about 25k tokens |

**Goal (F-35).** `GET /reservations/{token}`, `POST /reservations/{token}/claim` and `DELETE /reservations/{token}` stop trusting "any authenticated principal". Only a **user** principal with an `org_id` claim may call them, and only for a reservation whose node belongs to that organization. Another org's reservation looks exactly like a missing one (404 `reservation_not_found`), so the token endpoints are no longer an existence oracle. Paths stay the same, because VS-22's Servers client calls them (C-1, C-4).

**Assumptions (FU-41a's note text was lost).** (1) The caller is Servers forwarding the user's JWT (D-07, D-45); there is no service identity in the MVP. (2) Agents never call these endpoints; agent principals (mTLS, `node_id`, no `org_id`) get 403. (3) Until VS-06R merges, Nodes has no JWT scheme outside tests (F-01), so this packet's tests run under `NodesWebApplicationFactory`'s test scheme; VS-06R's selector sends `/reservations` to `JwtBearer`.

**Rules.**
- `ServiceResult`/`Result` for every failure; no new exception types. ProblemDetails through the existing `ProblemDetailsHelper`.
- Never log the reservation token at Information or above in new code; log the node id and organization id.
- Do not change the org-scoped reservation endpoints, the service's existing method signatures (their unit tests stay as they are) or any path.
- Nullable is enabled and warnings are errors.

**Changes.**
1. `src/Dhadgar.Nodes/Services/ICapacityReservationService.cs` and `CapacityReservationService.cs`: add `Task<Guid?> GetOwningOrganizationIdAsync(Guid reservationToken, CancellationToken ct = default)`, one `AsNoTracking` query: `CapacityReservations.Where(r => r.ReservationToken == reservationToken).Select(r => (Guid?)r.Node.OrganizationId).FirstOrDefaultAsync(ct)` (check `Node`'s property name).
2. New `src/Dhadgar.Nodes/Auth/ReservationCallerPolicy.cs`: `public const string Name = "ReservationCaller";` and a static `Configure(AuthorizationPolicyBuilder p)` that requires an authenticated user, `RequireClaim("org_id")`, and a requirement whose handler fails any principal carrying a `node_id` claim. Register it in `src/Dhadgar.Nodes/Program.cs` next to `TenantScoped` (`.AddPolicy(ReservationCallerPolicy.Name, ReservationCallerPolicy.Configure)`), and register the handler as a singleton `IAuthorizationHandler`.
3. `src/Dhadgar.Nodes/Endpoints/ReservationEndpoints.cs`:
   - `tokenGroup.RequireAuthorization()` becomes `.RequireAuthorization(ReservationCallerPolicy.Name)`.
   - Each token handler takes `ClaimsPrincipal user`, parses `org_id` (unparseable → 403 via `ProblemDetailsHelper`), calls `GetOwningOrganizationIdAsync`, and returns `ProblemDetailsHelper.NotFound("reservation_not_found")` when it is null **or** differs from the caller's org. Only then does it call the existing `GetByTokenAsync`/`ClaimAsync`/`ReleaseAsync`. Put the shared check in one private static helper.
4. Contract: C-1 already has the row (`ReservationCaller`, VS-36). `src/Dhadgar.Nodes/README.md` reservations section: the policy and the 404 rule.

**Tests.**
- New `tests/Dhadgar.Nodes.Tests/Integration/ReservationOwnershipTests.cs`, using the factory and helpers of `ReservationApiIntegrationTests` (read how it sets the test identity and org claim):
  - Org A creates a reservation on its node. A caller with `org_id` B: `GET`, `claim` and `DELETE` on that token each return 404 `reservation_not_found`, and the reservation is still `Pending` afterwards (read it back through the service). Twin: org A gets 200/200/204.
  - A caller with no `org_id` claim gets 403 on `claim`. A caller with `org_id` A **and** a `node_id` claim (an agent-shaped principal) gets 403 on `claim`.
  - An unknown token returns 404 for org A, the same body shape as the cross-org case.
- `tests/Dhadgar.Nodes.Tests/CapacityReservationServiceTests.cs`: `GetOwningOrganizationIdAsync` returns the node's org for a known token and null for an unknown one.
- Existing `ReservationApiIntegrationTests` token cases keep passing; if their test identity has no `org_id` matching the node's org, give it one (do not loosen the policy).

Why they fail when reverted: with `RequireAuthorization()` and no ownership check, org B claims org A's reservation (200) and the agent-shaped principal is accepted.

**Checks (in order).**

```powershell
dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj -c Release
dotnet build Dhadgar.sln -c Release
```

**Docs owed.** Change 4.

**Done when.** The checks pass and the report quotes one cross-org claim returning 200 before change 3.

### VS-10 Certificate renewal on the agent-held-key path (superseded)

Superseded by Sprint 2 (D-36). `main` at `6df3338` already renews on the agent-held key:
- the C-4 records with `CaCertificate`;
- the thumbprint taken from the mTLS identity;
- no server-generated private key;
- `GatewayAgentRenewalSeamTests` through the Gateway.

Do not dispatch it. Any renewal gap found later becomes a new packet against Sprint 2's code.

---

## Wave 3: agent runtime, command pipeline, Console import

> Do not dispatch wave 3 from this text. Contract sync 2 (ruling [worknotes/sprint2-reconcile-ruling.md](worknotes/sprint2-reconcile-ruling.md) § 4) first rewrites VS-12, VS-13, VS-14, VS-15, VS-16 and VS-33, and every `tests/Dhadgar.Seams.Tests` path, against D-25..D-39 and Sprint 2's code.

Base: `origin/main` after all of wave 2. Stage 1, dispatched together: VS-12, VS-15, VS-17. Stage 2: VS-33 (after VS-15 merges), VS-13 (stacks on VS-12; needs VS-33 merged before it can unskip the round-trip seam) and VS-14 (stacks on VS-12). Stage 3: VS-16 after VS-13, VS-14, VS-15 and VS-33 merge. Merge order: VS-15, VS-17, VS-12, VS-33, VS-14, VS-13, VS-16. VS-13 and VS-14 both stack on VS-12 but touch disjoint files (`Communication/*` vs `Commands/CommandValidator.cs`, `Commands/CommandReplayCache.cs`, `Configuration/SecurityOptions.cs`).

Re-slice (contract sync, 2026-10-26): enforcing C-2 "Limit enforcement" pushed VS-15 past 40k, so its hub half is now **VS-33** (`CommandResult`, `ServerStatusChanged`, `ConsoleOutput`, their events and the round-trip seam). VS-15 keeps the command entity, migration, dispatcher, endpoint and timeout sweep. Wave 3 now has 7 packets.

### VS-12 Agent identity store and live options (#101)

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/12-agent-identity` / `vertical-slice-vs12` |
| Commit subject | `fix(agent-core): persist NodeId, OrganizationId and signing key; read options live (#101) [VS-12]` |
| Security review | Yes (agent project) |
| Size | about 40k tokens |

**Goal.** F-08: after enrollment the agent remembers who it is across restarts, and every service sees the NodeId without a restart.

**Rules.**
- Seams harness and test JWTs (D-22): mint and accept test user tokens only with `SeamJwt` (`tests/Dhadgar.Testing/Auth/SeamJwt.cs` after VS-32, which removes VS-04's duplicate minter in `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`); never add another minter or `PostConfigure<JwtBearerOptions>` test-key block. Client-certificate shims stay inside the test project that owns them. A seam that asserts a rejection proves the server answered: the status or code comes from the server's response, the forwarder or a backend recorder shows which server it reached, and a positive twin passes (dispatch rules).
- `Result<T>` for every IO path; no exceptions for missing or corrupt files.
- Every file path goes through `IPathValidator.ValidatePath(path, [statePath])` before use.
- Write atomically: write `agent-identity.json.tmp` in the same directory, then `File.Move(tmp, final, overwrite: true)`.
- The identity file holds no private keys and no enrollment token. Never log the token.
- `using` on every disposable; keep the existing `[Category.Code] message` style for `Result` failures.

**Changes.**
0. `src/Agents/Dhadgar.Agent.Core/Commands/CommandEnvelope.cs`: add `[MaxLength(64)] public string? SigningKeyId { get; init; }` with an XML doc ("Key id of the control-plane signing key; see AgentCommandSigning.ComputeKeyId"). VS-13 and VS-14 both use it, so it lands here.
1. `src/Agents/Dhadgar.Agent.Core/Configuration/AgentOptions.cs`: add `public string StatePath { get; set; } = DefaultStatePath();` (Windows: `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Meridian Console", "Agent", "state")`; otherwise `/var/lib/dhadgar-agent`) and `public string? EnrollmentToken { get; set; }`. `SecurityOptions.cs`: add `public string? CommandSigningPublicKey { get; set; }` and `public string? CommandSigningKeyId { get; set; }`.
2. New `src/Agents/Dhadgar.Agent.Core/Authentication/AgentIdentity.cs`: `public sealed record AgentIdentity(Guid NodeId, Guid OrganizationId, string CommandSigningPublicKey, string CommandSigningKeyId, DateTimeOffset EnrolledAt);`.
3. New `Authentication/IAgentIdentityStore.cs` and `FileAgentIdentityStore.cs`: `Result<AgentIdentity?> Load()` (missing file → `Success(null)`; unreadable or invalid JSON → `Failure("[Identity.Corrupt] ...")`), `Task<Result> SaveAsync(AgentIdentity identity, CancellationToken ct)` (creates the directory), `Task<Result> ClearAsync(CancellationToken ct)`. File `Path.Combine(options.StatePath, "agent-identity.json")`, `System.Text.Json` with `AgentJson.Options`. Those options require every positional parameter without a default (C-2 JSON rules; unknown properties are ignored, D-40), so a field added to `AgentIdentity` later must have a default value, or every agent with an older file would report `[Identity.Corrupt]`. Say so in the record's XML doc.
4. New `Authentication/AgentIdentityPostConfigure.cs` (`IPostConfigureOptions<AgentOptions>`): when `NodeId` is null, load the identity and set `NodeId`, `OrganizationId`, and `Security.CommandSigningPublicKey`/`CommandSigningKeyId` unless configuration already set them (configuration wins). A load failure logs a warning and leaves options unchanged.
5. New `Authentication/AgentIdentityService.cs` (singleton): `Task<Result> PersistEnrollmentAsync(EnrollmentResult result, CancellationToken ct)` saves the identity, then calls `IOptionsMonitorCache<AgentOptions>.TryRemove(Options.DefaultName)` so the next `CurrentValue` re-runs the post-configure.
6. `Authentication/EnrollmentService.cs`: after the certificate is stored, call `AgentIdentityService.PersistEnrollmentAsync`; a failure returns `Failure("[Enrollment.IdentityNotPersisted] ...")`. Read `NodeId` in `RenewCertificateAsync` from `IOptionsMonitor<AgentOptions>.CurrentValue`.
7. Switch from `IOptions<AgentOptions>` to `IOptionsMonitor<AgentOptions>` and read `CurrentValue` at use time in `Communication/ControlPlaneClient.cs` (connect, inbound command checks), `Commands/CommandValidator.cs`, `Commands/CommandDispatcher.cs`, `Files/FileTransferService.cs`, and `Hosting/AgentServiceCollectionExtensions.cs` (both named HttpClient handlers; the handler factory runs again per handler lifetime, so a later NodeId is picked up). `src/Agents/Dhadgar.Agent.Windows/Program.cs` keeps `IOptions` for startup logging only.
8. `Hosting/AgentServiceCollectionExtensions.cs` `AddAgentCore`: register `IAgentIdentityStore` → `FileAgentIdentityStore`, `AgentIdentityService`, `IPostConfigureOptions<AgentOptions>` → `AgentIdentityPostConfigure`.
9. Update construction sites in tests: `tests/Dhadgar.Agent.Core.Tests`, `tests/Dhadgar.Nodes.Tests/Integration/AgentEnrollmentContractTests.cs` (`CreateAgent` now needs an `AgentIdentityService` over a temp `StatePath`), `tests/Dhadgar.Seams.Tests/Infrastructure/SeamAgent.cs` (identity service over a temp dir; `CreateControlPlaneClient` builds an `IOptionsMonitor` from a `ServiceCollection` with `AddOptions<AgentOptions>()` instead of `Options.Create`).

**Tests** (`tests/Dhadgar.Agent.Core.Tests/Authentication/`):
- `FileAgentIdentityStoreTests`: round trip in a temp dir; missing → null; corrupt → failure; a `StatePath` that resolves outside itself (for example `StatePath = "/tmp/x/../.."`) → failure from the validator.
- `AgentIdentityPostConfigureTests`: a stored identity fills `NodeId`; configuration values win.
- `AgentIdentityServiceTests`: in a `ServiceCollection` with `AddOptions<AgentOptions>()` plus the post-configure, `CurrentValue.NodeId` is null, `PersistEnrollmentAsync(...)` runs, then `CurrentValue.NodeId` equals the enrolled id. This is the #101 regression test.
- `CommandValidatorLiveOptionsTests`: the validator built before persistence rejects an envelope for node X, and accepts it after persistence sets NodeId X.

Why they fail when reverted: NodeId stays null after enrollment and is lost on restart.

**Checks (in order).**

```bash
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet build src/Agents/Dhadgar.Agent.Windows/Dhadgar.Agent.Windows.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
dotnet test tests/Dhadgar.Agent.Windows.Tests/Dhadgar.Agent.Windows.Tests.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Agents/Dhadgar.Agent.Core/README.md`: "Identity and state" (file location, contents, how to re-enroll by deleting it).

**Done when.** Checks pass.

### VS-13 Agent client on the typed hub contract

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/13-agent-wire` / `vertical-slice-vs13` (stacks on VS-12) |
| Commit subject | `feat(agent-core): typed hub wire for ControlPlaneClient, protocol header, server status and console sends [VS-13]` |
| Security review | Yes (`IControlPlaneClient`, `CommandEnvelope`) |
| Size | about 40k tokens (at the limit after the contract sync; a context stop continues from the worknote with the "Tests" list) |

**Goal.** The agent talks to `/hubs/agent` with the C-2 types (F-06, D-05). The two agent-side seams go green.

**Rules.**
- Seams harness and test JWTs (D-22): mint and accept test user tokens only with `SeamJwt` (`tests/Dhadgar.Testing/Auth/SeamJwt.cs` after VS-32, which removes VS-04's duplicate minter in `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`); never add another minter or `PostConfigure<JwtBearerOptions>` test-key block. Client-certificate shims stay inside the test project that owns them. A seam that asserts a rejection proves the server answered: the status or code comes from the server's response, the forwarder or a backend recorder shows which server it reached, and a positive twin passes (dispatch rules).
- Inbound commands are checked before any event is raised (C-2 "Limit enforcement", `AgentCommandEnvelope` row, and acceptance rules 2 and 3): field sizes, NodeId and OrganizationId match (Guid.Empty rejected), lifetime, skew and expiry. Signature, key id, command type and replay are VS-14's (`CommandValidator`), so that those rejections answer `Rejected` rather than being dropped. Keep the existing log messages and metrics sanitising, and never log `PayloadJson`, `Signature` or a nonce.
- Outbound records stay inside the limits (D-23: senders truncate): the mapper bounds every string and list it sends (change 1).
- Hub method names come from `nameof(IAgentHub.X)` and `nameof(IAgentHubClient.X)`; no string literals.
- `Result`/`bool` returns as today; telemetry stays best-effort.
- The mTLS requirements in `IControlPlaneClient`'s doc comment stay true. VS-05's post-configurator re-checks (HTTPS URL, client certificate still present) stay the last statements in the `WithUrl` lambda.

**Contract excerpt.** All of C-2, especially `IAgentHub`, `IAgentHubClient`, `AgentCommandEnvelope`, `AgentCommandResult`, `AgentHeartbeat`, `AgentServerProcess`, `AgentServerStatus`, `AgentConsoleBatch`, `AgentProtocol.VersionHeader`, the acceptance rules and the "Limit enforcement" table; `AgentCommandSigning.SignatureBase64Length` and `KeyIdLength` (C-3, added by VS-09).

**Changes.**
1. New `src/Agents/Dhadgar.Agent.Core/Communication/AgentWireMapper.cs` (`internal static`; add `[assembly: InternalsVisibleTo("Dhadgar.Agent.Core.Tests")]` if not present):
   - `AgentHeartbeat ToWire(HeartbeatPayload p)`: CPU = `p.Metrics?.CpuUsagePercent ?? 0`; memory = `p.Metrics?.MemoryUsagePercent ?? 0`; disk = the highest `UsagePercent` in `p.Metrics?.Disks`, else 0; each percentage clamped to 0..100 and `NaN` mapped to 0; servers from `p.ActiveProcesses` (`ProcessState` Starting→Starting, Running→Running, Stopping→Stopping, Stopped→Stopped, Failed→Crashed; uptime in whole seconds), at most `MaxServersPerHeartbeat`; `AgentVersion` truncated to `MaxAgentVersionLength`; warnings at most `MaxHeartbeatWarnings`, each truncated to `MaxWarningLength`.
   - `AgentCommandResult ToWire(CommandResult r)`: status by name (`Succeeded`→`Succeeded`, …); `ErrorMessage` truncated to `MaxErrorMessageLength`; `ErrorCode` kept only if it matches `AgentMessageLimits.ErrorCodePattern` (VS-09), else `"AgentError"`; `CorrelationId` over `MaxCorrelationIdLength` → null; `ResultJson` over `MaxResultJsonBytes` UTF-8 bytes → `ResultJson = null`, `Status = Failed`, `ErrorCode = "ResultTooLarge"`.
   - `AgentTelemetry ToWire(TelemetryPayload t)`: metrics only (events are dropped on the wire in the MVP); names longer than `MaxMetricNameLength` or containing control characters, and non-finite values, are dropped; at most `MaxTelemetryMetrics` entries.
   - `CommandEnvelope FromWire(AgentCommandEnvelope e)`: every field, including `Signature`, `ExpiresAt` and `SigningKeyId` (the property VS-12 added).
2. New `Communication/InboundCommandFilter.cs` (`internal static`): `string? Reject(AgentCommandEnvelope e, AgentOptions options, DateTimeOffset now)` returns null to accept or a reason code. Move the checks from `OnCommandReceived` here so they can be unit-tested. Checks in this order, all from C-2 (constants, not literals):
   - `MissingCommandType` (null or empty), `MissingPayload` (null `PayloadJson`);
   - `FieldTooLong`: `CommandType` over `MaxCommandTypeLength`, `CorrelationId` over `MaxCorrelationIdLength`, a non-empty `Signature` whose length is not `AgentCommandSigning.SignatureBase64Length`, a non-empty `SigningKeyId` whose length is not `AgentCommandSigning.KeyIdLength`;
   - `PayloadTooLarge` (`Encoding.UTF8.GetByteCount(PayloadJson)` over `MaxPayloadJsonBytes`);
   - `NodeIdMismatch`, `OrganizationIdInvalid` (`Guid.Empty`, or not the enrolled organization);
   - `LifetimeInvalid` (`ExpiresAt - IssuedAt` ≤ 0 or over `MaxCommandLifetimeSeconds`), `IssuedInFuture` (`IssuedAt` later than `now + MaxClockSkewSeconds`), `Expired` (`now >= ExpiresAt`).
   Unknown command types are **not** filtered here: VS-14's validator rejects them so the agent answers `Rejected`.
3. `Communication/ControlPlaneClient.cs`:
   - In the `WithUrl` lambda set `options.Headers[AgentProtocol.VersionHeader] = AgentProtocol.Version.ToString(CultureInfo.InvariantCulture);` **before** the configurator loop, and after the loop, next to VS-05's re-checks, fail closed if the header is missing or different (`InvalidOperationException("Hub connection configurators must not change the agent protocol header")`).
   - Ruling on the `HttpMessageHandlerFactory` residual (VS-05 security-2 (b), D-24): **no post-hook check** for a replaced `HttpMessageHandlerFactory`. A delegate cannot be inspected for what its handler does, and a check on "was it replaced" would break `SeamHubConfigurator`, the only registered configurator. Registering a configurator needs code in the agent host's own DI, which is the agent's own trust level. Keep the warning log and the XML doc that says the replacement is unchecked and test-only. Revisit (make the interface `internal` with `InternalsVisibleTo`) only if agent plugins or extensions are introduced.
   - `_hubConnection.On<AgentCommandEnvelope>(nameof(IAgentHubClient.ReceiveCommand), OnCommandReceived)` and `On<string>(nameof(IAgentHubClient.ReceivePing), OnPingReceived)`. `OnPingReceived`: a null nonce or one longer than `MaxNonceLength` is dropped with a log that does not contain the nonce.
   - `OnCommandReceived(AgentCommandEnvelope e)`: `InboundCommandFilter.Reject` → log and drop; else map with `FromWire` and raise `CommandReceived`.
   - Heartbeat, result and telemetry sends invoke `nameof(IAgentHub.Heartbeat)` and so on with the mapped records instead of JSON strings.
   - New methods (also added to `IControlPlaneClient` with XML docs): `Task<bool> SendServerStatusAsync(AgentServerStatus status, CancellationToken ct = default)` and `Task<bool> SendConsoleOutputAsync(AgentConsoleBatch batch, CancellationToken ct = default)`. Both return false when not connected or on failure, with the invocation timeout pattern of `SendCommandResultAsync`.
   - Add `"ServerStatus"` to `KnownCommandTypes` (metrics allowlist).
4. Seams: remove the `Skip` from `AgentHub/AgentClientHubSeamTests` (all three: `Heartbeat_ThroughGateway_UpdatesNode`, `Connect_WithoutCertificate_IsRejected` and VS-06's `Connect_WithCertificateFromAnotherCa_IsRejected`). Keep their server-side assertions as written (status from the server plus the forwarder record). After VS-07, a client with no certificate is rejected by the **Gateway** (`AgentCertificate` policy) and is never forwarded. So if `Connect_WithoutCertificate_IsRejected` still asserts `ForwardedClusters` contains `nodes`, change that line to assert the forwarder recorded no `/hubs/agent` request, and say so in the report: that 401 now comes from the Gateway (dispatch rule on rejection seams, point 2). The other-CA test still proves the Nodes side. Add `AgentClientHubSeamTests.ReceiveCommand_FromNodes_RaisesCommandReceived`: resolve `IHubContext<AgentHub, IAgentHubClient>` from the Nodes factory, send an `AgentCommandEnvelope` to group `AgentHubGroups.Node(nodeId)`, and assert the agent's `CommandReceived` fires within 5 s with the same `CommandId`, `PayloadJson` and `SigningKeyId`.
5. Seams: remove the `Skip` from `Commands/AgentCommandRoundTripSeamTests` (written in VS-33, so VS-33 must have merged; if it has not, leave that skip and say so in the report; VS-16 then removes it).

**Tests** (`tests/Dhadgar.Agent.Core.Tests/Communication/`):
- `AgentWireMapperTests`: each mapping; `Enum.GetValues<CommandResultStatus>()` and `Enum.GetValues<ProcessState>()` all map (fails if someone adds an enum member without mapping it); disk picks the highest disk. Bounds, each at the limit and one past it: `AgentVersion` 64/65 chars; 20/21 warnings; a 256/257-char warning; 64/65 servers; `ErrorMessage` 1024/1025; `ErrorCode` `Java.NoRuntimeForPlatform` kept, `bad code\n` → `AgentError`; `ResultJson` of exactly 64 KiB kept, 64 KiB + 1 byte → `Failed`/`ResultTooLarge`; a 129-char metric name dropped; 501 metrics → 500; a `NaN` CPU → 0.
- `InboundCommandFilterTests`: each reason code at its boundary (`CommandType` 128/129 chars; `Signature` 88 chars accepted, 87 and 89 → `FieldTooLong`; `SigningKeyId` 64/63; lifetime exactly 60 s accepted, 60.001 s and 0 s rejected; `IssuedAt` at now + 30 s accepted, now + 31 s rejected; `now == ExpiresAt` → `Expired`); accepts a valid envelope; an unknown `CommandType` such as `Format` is **accepted** by the filter (VS-14 rejects it).
- `ControlPlaneClientProtocolHeaderTests`: a configurator that removes `X-Agent-Protocol` makes `ConnectAsync` throw `InvalidOperationException` before any network call; a 65-char ping nonce raises no reply and no log line containing it (capturing logger).
- Agent-side seams (above). They are the acceptance: they fail when reverted because the hub rejects JSON-string arguments for typed parameters.

**Checks (in order).**

```bash
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet build src/Agents/Dhadgar.Agent.Windows/Dhadgar.Agent.Windows.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Agents/Dhadgar.Agent.Core/README.md` "Control-plane wire" (hub path, protocol header, the contract location). `docs/adr/0008-agent-transport-signalr.md`: no status change (close-out does that).

**Done when.** Checks pass; `AgentClientHubSeamTests` green (and `AgentCommandRoundTripSeamTests` if VS-33 is merged).

### VS-14 Agent signature verification and replay cache (#94)

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/14-agent-signature` / `vertical-slice-vs14` (stacks on VS-12) |
| Commit subject | `feat(agent-core): verify ECDSA command signatures and reject replays (#94) [VS-14]` |
| Security review | Yes |
| Size | about 40k tokens (was 35k; the contract sync added acceptance rules 1 to 6) |

**Goal.** F-09: with `RequireSignedCommands=true` (default) the agent accepts exactly the commands Nodes signed, once (D-09).

**Rules.**
- Seams harness and test JWTs (D-22): mint and accept test user tokens only with `SeamJwt` (`tests/Dhadgar.Testing/Auth/SeamJwt.cs` after VS-32, which removes VS-04's duplicate minter in `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`); never add another minter or `PostConfigure<JwtBearerOptions>` test-key block. Client-certificate shims stay inside the test project that owns them. A seam that asserts a rejection proves the server answered: the status or code comes from the server's response, the forwarder or a backend recorder shows which server it reached, and a positive twin passes (dispatch rules).
- Fail closed: no key configured, a key id mismatch, a missing `ExpiresAt` or a bad signature all reject.
- The verifier caches the imported `ECDsa` per key id and disposes it when the key changes.
- Signature format IEEE P1363, SHA-256, curve P-256 only (`KeySize == 256`).
- Verify over the **raw** `PayloadJson` string before anything parses it (C-2 JSON rules). Never throw for network input: build the input with `AgentCommandSigning.TryBuildSigningInput`, never the throwing overload.
- The pinned key comes only from `Security.CommandSigningPublicKey`/`CommandSigningKeyId` (enrollment, VS-12). Never select, fetch or import a key by the envelope's `SigningKeyId`.
- `RequireSignedCommands=false` skips only the signature and key checks; type, identity, time and replay checks still run. When it is false, log a warning once per process: "Unsigned commands are accepted (Agent:Security:RequireSignedCommands=false); development only".
- Contract excerpt: C-3 in full (field order, `TryBuildSigningInput`, `SignatureBytes`, `SignatureBase64Length`, `KeyIdLength`); C-2 acceptance rules 1 to 6 and `AgentMessageLimits.MaxCommandLifetimeSeconds`, `MaxClockSkewSeconds`.

**Changes.**
1. New `src/Agents/Dhadgar.Agent.Core/Commands/ICommandSignatureVerifier.cs` and `EcdsaCommandSignatureVerifier.cs`: `Result Verify(CommandEnvelope envelope)` using `IOptionsMonitor<AgentOptions>.CurrentValue.Security.CommandSigningPublicKey`/`CommandSigningKeyId`. Steps:
   1. `SigningKeyId` must equal the pinned key id (ordinal) → else `UnknownSigningKey`.
   2. `Convert.TryFromBase64String(e.Signature, buffer, out var written)` with a 64-byte buffer must succeed with `written == AgentCommandSigning.SignatureBytes` → else `InvalidSignature`.
   3. Build `var wire = new AgentCommandEnvelope(e.CommandId, e.CommandType, e.NodeId, e.OrganizationId, e.InitiatedByUserId, e.IssuedAt, e.ExpiresAt!.Value, e.PayloadJson, e.Signature, e.SigningKeyId ?? "", e.CorrelationId);` and call `AgentCommandSigning.TryBuildSigningInput(wire, out var input)` → false gives `MalformedEnvelope` (line breaks, lone surrogates; no exception escapes).
   4. `ecdsa.VerifyData(input, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)` → false gives `InvalidSignature`.
2. New `Commands/ICommandReplayCache.cs` and `CommandReplayCache.cs` (singleton, takes `TimeProvider`): `bool TryRegister(Guid commandId, DateTimeOffset expiresAt)`; a `ConcurrentDictionary<Guid, DateTimeOffset>` keyed by `CommandId` (never the signature, which is malleable); an entry is retained until `expiresAt + MaxClockSkewSeconds`; entries past retention are swept on insert when the count exceeds 1,000; hard cap 10,000 (beyond it, sweep, and if still full reject with `ReplayCacheFull`).
3. `Commands/CommandValidator.cs`: replace the fail-closed block at lines 100-116. Order, cheap checks first:
   1. `AgentCommandTypes.IsKnown(CommandType)` → `UnknownCommandType` (so an unknown type is answered `Rejected`, not dropped);
   2. NodeId and OrganizationId (existing checks, read live since VS-12);
   3. `ExpiresAt` null → `MissingExpiry`; lifetime over `MaxCommandLifetimeSeconds` or ≤ 0 → `LifetimeInvalid`; `IssuedAt` later than now + `MaxClockSkewSeconds` → `IssuedInFuture`; now ≥ `ExpiresAt` → `Expired` (constants, not literals);
   4. missing signature → `MissingSignature`; key not configured → `SigningKeyUnavailable`; then `Verify` (codes above);
   5. after **all** checks pass (signature, payload, sizes), `replayCache.TryRegister` → false gives `ReplayedCommand`. The replay cache comes last, so a rejected command never occupies it.
4. `Hosting/AgentServiceCollectionExtensions.cs`: register the verifier and the replay cache.

**Tests** (`tests/Dhadgar.Agent.Core.Tests/Commands/CommandValidatorSignatureTests.cs`): generate a P-256 key, put its SPKI and key id in options, sign envelopes with `AgentCommandSigning`:
- a valid signed command passes;
- payload changed by one character → `InvalidSignature`;
- other key id → `UnknownSigningKey`;
- no key configured → `SigningKeyUnavailable`;
- the same command twice → second `ReplayedCommand`;
- `RequireSignedCommands=false` accepts unsigned, but still rejects an expired or unknown-type command;
- an expired command never reaches the replay cache;
- a valid signature re-encoded to 63 bytes (drop one byte, base64 again) → `InvalidSignature`; a non-base64 signature → `InvalidSignature`;
- `CorrelationId` containing a lone surrogate (`"\uD800"`) → `MalformedEnvelope`, and no exception escapes `Validate`;
- `CommandType` `"Format"` → `UnknownCommandType`;
- lifetime 61 s → `LifetimeInvalid`; `IssuedAt` at now + 31 s → `IssuedInFuture`, at now + 30 s accepted;
- `CommandReplayCacheTests` (`FakeTimeProvider`): `TryRegister(id, expiresAt)`, then at `expiresAt + 29 s` the same id → false, and after advancing past `expiresAt + 31 s` and inserting 1,001 other ids (forcing a sweep), the same id → true.

Seam `tests/Dhadgar.Seams.Tests/Commands/CommandSigningSeamTests.cs`: take Nodes' real `ICommandSigner` from the Nodes factory and an agent `CommandValidator` configured with the public key the agent received at enrollment through the Gateway; a Nodes-signed `AgentCommandEnvelope`, copied field by field into the agent's `CommandEnvelope` in the test (`AgentWireMapper` is internal and arrives in VS-13), validates. A tampered one (one payload character changed) fails with exactly `InvalidSignature`, not any earlier code: the dispatch rule on rejection seams applies to in-process seams too, so assert the specific reason. Fails when reverted because the validator rejects every signed command.

**Checks (in order).**

```bash
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Agents/Dhadgar.Agent.Core/README.md` "Command signing" (config keys, fail-closed behaviour, replay window). `CLAUDE.md` "Open Issues": change the #94 line to "implemented in VS-09/VS-14; key rotation open".

**Done when.** Checks pass.

### VS-15 Nodes command pipeline: dispatch endpoint, command store, timeout sweep

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/15-command-pipeline` / `vertical-slice-vs15` |
| Commit subject | `feat(nodes): validated and signed agent command dispatch, command store and timeout sweep [VS-15]` |
| Security review | Yes (dispatches commands to customer hardware) |
| Size | about 38k tokens (re-sliced by contract sync: the hub half is VS-33) |

**Goal.** D-08 and D-16, dispatch half. Servers can ask Nodes to send a signed command to a connected node. Nodes validates the payload against the contract before signing, stores the command and times it out. Results, server status and console output arrive in VS-33.

**Rules.**
- Seams harness and test JWTs (D-22): mint and accept test user tokens only with `SeamJwt` (`tests/Dhadgar.Testing/Auth/SeamJwt.cs` after VS-32, which removes VS-04's duplicate minter in `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`); never add another minter or `PostConfigure<JwtBearerOptions>` test-key block. Client-certificate shims stay inside the test project that owns them. A seam that asserts a rejection proves the server answered: the status or code comes from the server's response, the forwarder or a backend recorder shows which server it reached, and a positive twin passes (dispatch rules).
- Only `AgentCommandTypes.IsKnown` types. The payload is validated as the typed record for its command type with `AgentJson.Options` and against the C-2 limits **before** it is signed (C-2 "Limit enforcement", typed-payloads row). Nodes signs and sends `JsonSerializer.Serialize(typed, AgentJson.Options)` of the record it validated, never the caller's string, so unknown members never reach a signature (D-40).
- The org check is the `TenantScoped` policy plus a DB check that the node belongs to the route org (a node in another org is 404, not 403).
- Server commands (`StartServer`, `StopServer`, `ServerStatus`) need `request.ServerId` equal to the payload's `ServerId` and a **Claimed** capacity reservation on that node whose `ServerId` is `serverId.ToString("D")`. This also proves the server belongs to the caller's organization, because the node does. `Ping` needs `request.ServerId == null`.
- `ServiceResult<T>` pattern as in Nodes' other services; ProblemDetails at the endpoint (C-1 status codes).
- Persist the command before sending it; a send failure marks it `Failed` and returns 409 `node_offline`.
- Never log `PayloadJson`, the signature or the bearer token.
- Expected migration side effect (F-16): the generated migration also creates `InboxState`, `OutboxState` and `OutboxMessage`. Keep them.

**Contract excerpt.** `DispatchAgentCommandRequest`/`Response` (C-4); `AgentCommandEnvelope`, `AgentCommandTypes`, the payload records, `AgentJson`, `AgentMessageLimits` and the "Limit enforcement" typed-payloads row (C-2); `AgentCommandSigning.TryBuildSigningInput` (C-3); `AgentCommandCompleted` (C-2, timeout sweep only); the dispatch route row (C-1), now with 409 `server_not_on_node`.

**Changes.**
1. New `src/Dhadgar.Nodes/Data/Entities/AgentCommand.cs` (`Id` = command id, `NodeId`, `OrganizationId`, `CommandType` (128), `ServerId` (Guid?), `CorrelationId` (128)?, `InitiatedByUserId` (Guid?), `PayloadSha256` (64), `IssuedAt`, `ExpiresAt`, `State` (enum `AgentCommandState { Sent = 0, Succeeded = 1, Failed = 2, Rejected = 3, TimedOut = 4, Cancelled = 5 }` in the same file), `CompletedAt`?, `ErrorCode` (`AgentMessageLimits.MaxErrorCodeLength`)?, `ErrorMessage` (`MaxErrorMessageLength`)?, `ResultJson` (text)?). Configuration `Data/Configurations/AgentCommandConfiguration.cs` with an index on `(NodeId, State)`; `DbSet<AgentCommand> AgentCommands` in `NodesDbContext`.
2. Migration: `dotnet ef migrations add AddAgentCommands --project src/Dhadgar.Nodes --startup-project src/Dhadgar.Nodes --output-dir Data/Migrations`, then `dotnet ef migrations has-pending-model-changes --project src/Dhadgar.Nodes --startup-project src/Dhadgar.Nodes` must report none. Neither needs a database.
3. New `src/Dhadgar.Nodes/Services/AgentCommands/AgentCommandPayloadValidator.cs` (`internal static`): `string? Validate(string commandType, string payloadJson, Guid? requestServerId)` returns null or `invalid_payload`. Steps: UTF-8 byte count ≤ `MaxPayloadJsonBytes`; deserialize with `AgentJson.Options` to `PingPayload`, `StartServerPayload`, `StopServerPayload` or `ServerStatusPayload` by type, catching `JsonException` (a null result is also invalid); then the typed-payloads row: `Nonce` ≤ `MaxNonceLength`; `MinecraftVersion` matches `MinecraftVersionPattern` (a `[GeneratedRegex]` on the constant); `ServerJarUrl` ≤ `MaxServerJarUrlLength`, an absolute `https` URI with no user info; `ServerJarSha1` exactly `ServerJarSha1HexLength` hex chars; `MemoryMb` in `MinServerMemoryMb..MaxServerMemoryMb`; `Port` in `MinServerPort..MaxServerPort`; `GracefulTimeoutSeconds` in `MinGracefulTimeoutSeconds..MaxGracefulTimeoutSeconds`; for server commands the payload `ServerId` equals `requestServerId` and is not empty; for `Ping`, `requestServerId` is null. (`StartServerPayload.JavaRuntimes` does not exist yet; the agent validates it, VS-29.)
4. New `src/Dhadgar.Nodes/Services/AgentCommands/IAgentCommandDispatcher.cs` and `AgentCommandDispatcher.cs`: `Task<ServiceResult<DispatchAgentCommandResponse>> DispatchAsync(Guid organizationId, Guid nodeId, DispatchAgentCommandRequest request, Guid? userId, CancellationToken ct)`. Steps:
   1. type known (`invalid_command_type`); `CorrelationId` null or ≤ `MaxCorrelationIdLength` (`invalid_payload`); `AgentCommandPayloadValidator.Validate` (`invalid_payload`);
   2. node exists in the org and is not decommissioned (`node_not_found`);
   3. for server commands, the Claimed reservation (`server_not_on_node`);
   4. `registry.IsConnected(nodeId)` (`node_offline`);
   5. build the unsigned envelope (`CommandId = Guid.NewGuid()`, `IssuedAt = timeProvider.GetUtcNow()`, `ExpiresAt = IssuedAt + CommandTtlSeconds`, `Signature = ""`, `SigningKeyId = ""`), and `AgentCommandSigning.TryBuildSigningInput(unsigned, out _)` false gives `invalid_payload` (line breaks or lone surrogates in `CorrelationId`), so `signer.Sign` can never throw on caller input;
   6. `signer.Sign`; save `AgentCommand` (`State = Sent`); `hubContext.Clients.Group(AgentHubGroups.Node(nodeId)).ReceiveCommand(signed)`; return.
5. New `src/Dhadgar.Nodes/Endpoints/AgentCommandEndpoints.cs`: `app.MapGroup("/internal/organizations/{organizationId:guid}/nodes/{nodeId:guid}").RequireAuthorization("TenantScoped")`, `MapPost("/commands", ...)`; `userId` from claim `sub` when it parses; map `invalid_command_type`/`invalid_payload` → 400, `node_not_found` → 404, `node_offline` and `server_not_on_node` → 409, success → `Results.Accepted(value: response)`. Wire `AgentCommandEndpoints.Map(app)` in `Program.cs`.
6. New `src/Dhadgar.Nodes/BackgroundServices/AgentCommandTimeoutService.cs`: every 60 s (`TimeProvider`, `PeriodicTimer` pattern of `StaleNodeDetectionService`) marks `Sent` commands with `ExpiresAt + 5 min < now` as `TimedOut`, error code `agent_timeout`, and publishes `AgentCommandCompleted` for each (all fields from the row). Register it as a hosted service.

**Tests** (`tests/Dhadgar.Nodes.Tests`):
- `AgentCommands/AgentCommandPayloadValidatorTests.cs`, one row per bound, at the limit and one past it: payload of exactly 64 KiB (padded inside a valid `Nonce`-free JSON, e.g. whitespace) accepted and 64 KiB + 1 rejected; `{}` is a valid Ping; `{"nonce":"<64 chars>"}` accepted, 65 rejected; `StartServer` with an unknown property is **accepted** and the signed `PayloadJson` does not contain it (D-40); `StartServer` with a duplicate property, a missing property, `"port":1023`, `"port":65536`, `"memoryMb":511`, `"memoryMb":32769`, a 39-char SHA-1, `http://` jar URL, a URL with user info, version `1.21.11/../x`; `StopServer` with `gracefulTimeoutSeconds` 0 and 301 rejected, 1 and 300 accepted; `ServerId` mismatch with the request; `Ping` with a request `ServerId`.
- `AgentCommands/AgentCommandDispatcherTests.cs` (InMemory DB, fake registry, NSubstitute `IHubContext<AgentHub, IAgentHubClient>`): unknown type → `invalid_command_type`; payload not JSON → `invalid_payload`; `CorrelationId` with `\n` → `invalid_payload` and the signer is never called; other org's node → `node_not_found`; `StartServer` for a server with no Claimed reservation on the node → `server_not_on_node`; disconnected → `node_offline`; connected → `ReceiveCommand` called once with an envelope whose signature verifies (use the factory signer's public key and `AgentCommandSigning`) and whose `ExpiresAt - IssuedAt` is `CommandTtlSeconds`, and a `Sent` row saved.
- `AgentCommands/AgentCommandEndpointTests.cs` (factory with the test scheme): 202 with body; 409 offline; 409 `server_not_on_node`; 400; 404 for another org.
- `BackgroundServices/AgentCommandTimeoutServiceTests.cs` with `FakeTimeProvider`.
- Seam `Commands/InternalRoutesNotExposedSeamTests.cs`: `POST` through the Gateway to `/internal/organizations/{org}/nodes/{node}/commands` and to `/api/v1/internal/...` both → 404 read from the Gateway's response, and the forwarder recorded **no** `nodes` request (dispatch rule on rejection seams; the Gateway has no route, so a backend 404 must not be what passes it). Positive twin: the same `POST` sent to the Nodes factory directly with a `SeamJwt` token for the node's org and a `Ping` payload → not 404 (409 `node_offline`, since no agent is connected; assert that `errorCode`).

Why they fail when reverted: no dispatch endpoint exists; without the typed validation a `StartServer` with port 80 is signed and sent.

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release
dotnet ef migrations has-pending-model-changes --project src/Dhadgar.Nodes --startup-project src/Dhadgar.Nodes
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Dhadgar.Nodes/README.md` "Agent commands" (internal endpoint, not routed by the Gateway, payload validation, statuses, timeout). `docs/runbooks/nodes-service-operations.md` (`AgentCommands` table, timeout sweep, outbox tables now in the migration).

**Done when.** Checks pass.

### VS-33 Nodes hub inbound: command results, server status, console output

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/33-hub-inbound` / `vertical-slice-vs33` (wave 3 stage 2: from `origin/main` after VS-15 merges) |
| Commit subject | `feat(nodes): bounded agent results, server status and console output on /hubs/agent, with events [VS-33]` |
| Security review | Yes (new agent-facing hub surface; agent text flows to other services) |
| Depends on | VS-15 (the `AgentCommand` entity), VS-08 (hub, validator file), VS-09 (`MaxErrorCodeLength`, `ErrorCodePattern`) |
| Size | about 32k tokens |

**Goal.** D-08, D-15 and D-16, return half. The three hub methods VS-08 stubbed accept bounded, node-checked agent messages and publish `AgentCommandCompleted`, `AgentServerStatusChanged` and `ServerConsoleOutput`. The round-trip seam is written here.

**Rules.**
- Seams harness and test JWTs (D-22): mint and accept test user tokens only with `SeamJwt` (`tests/Dhadgar.Testing/Auth/SeamJwt.cs` after VS-32, which removes VS-04's duplicate minter in `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`); never add another minter or `PostConfigure<JwtBearerOptions>` test-key block. Client-certificate shims stay inside the test project that owns them. A seam that asserts a rejection proves the server answered: the status or code comes from the server's response, the forwarder or a backend recorder shows which server it reached, and a positive twin passes (dispatch rules).
- C-2 hub rules: node identity from the `node_id` claim only (`node_mismatch`), `server_not_on_node`, `unknown_command`, idempotent results, and event fields taken from Nodes' own records (`OrganizationId`, `CommandType`, `ServerId`, `CorrelationId` from the `AgentCommand` row; `OrganizationId` from `Context.Items["OrganizationId"]`).
- C-2 "Limit enforcement" rows `AgentCommandResult`, `AgentServerStatus`, `AgentConsoleBatch` (D-23): **reject, never truncate**, with `HubException("invalid_result" | "invalid_status" | "invalid_console_batch")`. Null-check every reference field and `Enum.IsDefined` every enum (SignalR does not use `AgentJson`). Never echo input in error text.
- Never log console `Text`, `ResultJson`, `ErrorMessage` or `ErrorCode` values; log ids and lengths only.
- `Result`/`ServiceResult` in services; no `SaveChanges` for console output.

**Contract excerpt.** `IAgentHub.CommandResult`, `ServerStatusChanged`, `ConsoleOutput`; `AgentCommandResult`, `AgentCommandStatus`, `AgentServerStatus`, `AgentServerState`, `AgentConsoleBatch`, `AgentConsoleLine`; events `AgentCommandCompleted`, `AgentServerStatusChanged`, `ServerConsoleOutput`; `AgentMessageLimits` including `MaxErrorCodeLength` and `ErrorCodePattern` (C-2); the hub rules and the three "Limit enforcement" rows.

**Changes.**
1. `src/Dhadgar.Nodes/Hubs/AgentHubMessageValidator.cs` (VS-08's file): add `string? Validate(AgentCommandResult r)`, `string? Validate(AgentServerStatus s)` and `string? Validate(AgentConsoleBatch b)`, exactly the three C-2 rows, using the constants (`ErrorCodePattern` through a `[GeneratedRegex]` on the constant).
2. `src/Dhadgar.Nodes/Hubs/AgentHub.cs`, replacing the three `not_supported_yet` stubs:
   - `CommandResult`: null → `invalid_result`; node match; validator; load the `AgentCommand` by `result.CommandId` **and** node (else `unknown_command`); already terminal → return; map status, set `CompletedAt`, `ErrorCode`, `ErrorMessage`, `ResultJson` as received (already bounded); publish `AgentCommandCompleted` with `OrganizationId`, `CommandType`, `ServerId` and `CorrelationId` from the row, and `SaveChangesAsync`.
   - `ServerStatusChanged`: null → `invalid_status`; node match; validator; server-on-node check (query `CapacityReservations` for `NodeId`, `ServerId == status.ServerId.ToString("D")`, `Status == Claimed`); publish `AgentServerStatusChanged` with the org from `Context.Items["OrganizationId"]`.
   - `ConsoleOutput`: null → `invalid_console_batch`; node match; validator; server-on-node check cached in `IMemoryCache` for 60 s keyed by `(nodeId, serverId)`; publish `ServerConsoleOutput` with the lines as received (already bounded). No `SaveChanges`.
   - Register `AddMemoryCache()` if Nodes does not already.

**Tests.**
- `tests/Dhadgar.Nodes.Tests/Hubs/AgentHubMessageValidatorTests.cs` (extend VS-08's), one row per bound, at the limit and one past it: `ResultJson` 64 KiB and 64 KiB + 1 byte; `ErrorMessage` 1024/1025; `ErrorCode` `Java.NoRuntimeForPlatform` valid, 64/65 chars, `a b`, `x\ny`, empty string; `CorrelationId` 128/129; `Status = (AgentCommandStatus)9`; `AgentServerStatus.Message` 1024/1025 and an undefined `State`; console batch with 0, 200 and 201 lines, a 4096/4097-char line, 16 lines of 4096 chars (65,536 total, valid) plus one more char (invalid), a null line, a null `Text`.
- `tests/Dhadgar.Nodes.Tests/Hubs/AgentHubCommandTests.cs` (raw `HubConnection` as in VS-08):
  - `CommandResult` for a seeded `Sent` command publishes `AgentCommandCompleted` (via `TestNodesEventPublisher`) and sets the row; a second result is ignored; unknown id → `HubException` `unknown_command`;
  - a result whose `CorrelationId` differs from the row's → the event carries the **row's** `CorrelationId` and `CommandType`;
  - `ErrorCode` of 65 chars → `HubException` `invalid_result`, the row still `Sent`, nothing published;
  - `ConsoleOutput` for an unclaimed server → `server_not_on_node`; for a claimed one → `ServerConsoleOutput` published with the lines unchanged; 201 lines → `invalid_console_batch` and nothing published;
  - frame at the escaped worst case: `ConsoleOutput_AtEscapedWorstCase_IsAccepted`, 16 lines of 4096 `'é'` (65,536 chars, about 384 KiB escaped), is accepted and published.
- Seam `tests/Dhadgar.Seams.Tests/Commands/AgentCommandRoundTripSeamTests.cs` → `Ping_RoundTripsThroughAgentClient`: enroll through the Gateway; connect the agent's `ControlPlaneClient`; on `CommandReceived`, reply with `SendCommandResultAsync(CommandResult.Success(cmd.CommandId, nodeId, DateTimeOffset.UtcNow, "{}"))`; `POST` the internal dispatch endpoint **on the Nodes factory directly** with a `SeamJwt` user token for the node's org and a Ping; assert `CapturingPublishEndpoint` receives `AgentCommandCompleted` with `Succeeded` for that command id. `[Fact(Skip = "Pending VS-13: agent client still sends JSON strings")]`; run it once without the skip and quote the failure (D-13; it must be the hub rejecting the JSON-string argument or no event arriving, not a client exception before the request).

Why they fail when reverted: the hub methods throw `not_supported_yet`; without the validator a 65-char `ErrorCode` is stored and published.

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Dhadgar.Nodes/README.md` "Agent hub": the three methods, their limits and error codes, and the events (extends VS-08's section).

**Done when.** Checks pass; the round-trip seam is written and proven red.

### VS-16 Agent bootstrap hosted service and Ping handler

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/16-agent-bootstrap` / `vertical-slice-vs16` (from `origin/main` after VS-13, VS-14, VS-15, VS-33) |
| Commit subject | `feat(agent-core): bootstrap service (enroll, persist, connect, dispatch, renew) and Ping handler (#118) [VS-16]` |
| Security review | Yes |
| Size | about 40k tokens |

**Goal.** F-10: the agent runs on its own: enroll if needed, persist, connect with retry, dispatch commands, send results, renew certificates. Ping proves it end to end.

**Rules.**
- Seams harness and test JWTs (D-22): mint and accept test user tokens only with `SeamJwt` (`tests/Dhadgar.Testing/Auth/SeamJwt.cs` after VS-32, which removes VS-04's duplicate minter in `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`); never add another minter or `PostConfigure<JwtBearerOptions>` test-key block. Client-certificate shims stay inside the test project that owns them. A seam that asserts a rejection proves the server answered: the status or code comes from the server's response, the forwarder or a backend recorder shows which server it reached, and a positive twin passes (dispatch rules).
- No crash loops: every failure is logged and retried with capped backoff (5 s doubling to 5 min, with jitter); an `invalid_token` enrollment failure stops retrying and logs a critical message.
- Never log the enrollment token. After a successful enrollment call `IEnrollmentTokenCleanup.CleanupEnrollmentToken()` when one is registered.
- Commands run one at a time from a bounded `Channel<CommandEnvelope>` (capacity 100, `BoundedChannelFullMode.Wait` with the producer using `TryWrite`; a full channel answers `Rejected` with `AgentBusy`).
- Validation happens inside `ICommandDispatcher.DispatchAsync` (it calls the validator); do not validate a second time, or the replay cache rejects the command.
- `Result<T>` everywhere; `using` on every disposable.

**Changes.**
1. New `src/Agents/Dhadgar.Agent.Core/Authentication/IEnrollmentTokenSource.cs` (`string? GetToken()`), `ConfigurationEnrollmentTokenSource.cs` (reads `AgentOptions.EnrollmentToken`), registered by `AddAgentCore` with `TryAddSingleton`.
2. New `src/Agents/Dhadgar.Agent.Windows/Installation/RegistryEnrollmentTokenSource.cs`: reads `HKLM\SOFTWARE\Meridian Console\Agent\EnrollmentToken` (same constants as `EnrollmentTokenCleanup`), falls back to `AgentOptions.EnrollmentToken`. Register it in `src/Agents/Dhadgar.Agent.Windows/Program.cs` before `AddAgentCore` so `TryAdd` keeps it.
3. New `src/Agents/Dhadgar.Agent.Core/Commands/Handlers/PingCommandHandler.cs` (`CommandHandlerBase<PingPayload>`, type `AgentCommandTypes.Ping`): returns `CommandResult.Success(..., JsonSerializer.Serialize(new PingResult(agentVersion, now, payload.Nonce), AgentJson.Options))`, with `agentVersion` truncated to `AgentMessageLimits.MaxAgentVersionLength`. A `Nonce` longer than `MaxNonceLength` is a failure `[Ping.InvalidNonce]` (Nodes already rejects it, VS-15; the agent checks again, C-2 "Limit enforcement"). Register as `ICommandHandler`.
4. New `src/Agents/Dhadgar.Agent.Core/Hosting/AgentBootstrapService.cs` (`BackgroundService`):
   1. If `CurrentValue.NodeId` is null: get the token; none → log critical "Agent is not enrolled and no enrollment token is configured" and wait 60 s, then check again. Otherwise `EnrollAsync(token)` (it persists identity since VS-12), then token cleanup.
   2. Register every `ICommandHandler` from DI with `ICommandDispatcher.RegisterHandler` (skip a type already registered).
   3. Subscribe `IControlPlaneClient.CommandReceived` → `channel.Writer.TryWrite(e.Command)`.
   4. Connect loop: `ConnectAsync` with backoff until it succeeds or the host stops. Subscribe `StateChanged`: when the state becomes `Disconnected` or `Failed` after automatic reconnect gives up, run the connect loop again.
   5. Worker loop: read the channel, `DispatchAsync`, then `SendCommandResultAsync(result.IsSuccess ? result.Value : CommandResult.Rejected(cmd.CommandId, nodeId, result.Error, code))`, where `code` is the validator's reason code (for example `UnknownCommandType`, `InvalidSignature`) when it matches `AgentMessageLimits.ErrorCodePattern`, else `"Rejected"`. VS-13's mapper bounds the rest.
   6. Renewal: at start and every 24 h, if `certificateStore.NeedsRenewal(Security.CertificateRenewalThresholdDays)` and `EnableCertificateAutoRenewal`, call `RenewCertificateAsync`; after a successful renewal, disconnect and reconnect so the new certificate is used.
   Register it in `AddAgentCore` with `AddHostedService<AgentBootstrapService>()`.

**Tests** (`tests/Dhadgar.Agent.Core.Tests/Hosting/AgentBootstrapServiceTests.cs`, fakes for enrollment, client, dispatcher, token source, cert store):
- not enrolled with a token → enroll once, then connect;
- enrolled → no enroll call;
- `invalid_token` → no second attempt within the test window (use `FakeTimeProvider` or a short backoff option);
- a received command reaches the dispatcher once and its result is sent; a dispatcher failure sends `Rejected`;
- 101 queued commands → the 101st answered `Rejected`/`AgentBusy`.
- `Commands/PingCommandHandlerTests.cs`: echoes the nonce; `{}` (no nonce) succeeds with a null nonce; a 64-char nonce succeeds and a 65-char one fails with `Ping.InvalidNonce`.

Seam `tests/Dhadgar.Seams.Tests/Commands/AgentBootstrapPingSeamTests.cs` (**the end-to-end agent seam**): build a real agent host in-process (`Host.CreateApplicationBuilder()`, `AddOptions<AgentOptions>().Bind(...)` with `ControlPlane:Endpoint=https://localhost`, `StatePath` = temp dir, `EnrollmentToken` = a fresh token from the Nodes factory; `AddAgentCore()`; the seams `InMemoryCertificateStore` as `ICertificateStore`; a no-op `IProcessManager`; the seams `IHttpClientFactory` replacement and `SeamHubConfigurator` registered as `IHubConnectionConfigurator`). Start the host; wait until Nodes' `IAgentConnectionRegistry.IsConnected(nodeId)` (read the node id from the agent's identity file or the Nodes DB); `POST` a Ping to the Nodes internal dispatch endpoint with a `SeamJwt` token; assert `AgentCommandCompleted` `Succeeded` whose `ResultJson` deserializes to a `PingResult` with the nonce. Also remove the `Skip` from `AgentCommandRoundTripSeamTests` if VS-13 left it.

Why it fails when reverted: nothing enrolls, connects or dispatches, so no result ever arrives.

**Checks (in order).**

```bash
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet build src/Agents/Dhadgar.Agent.Windows/Dhadgar.Agent.Windows.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
dotnet test tests/Dhadgar.Agent.Windows.Tests/Dhadgar.Agent.Windows.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

Windows: `RegistryEnrollmentTokenSource` is verified by the owner's manual run (VS-27 runbook); add a `[SkippableFact]` that reads a missing key and returns null on Windows.

**Docs owed.** `src/Agents/Dhadgar.Agent.Core/README.md` "Runtime" (startup sequence, retry policy, renewal). `src/Agents/Dhadgar.Agent.Windows/README.md` "Enrollment token" (registry value or `Agent:EnrollmentToken`).

**Done when.** Checks pass; `AgentBootstrapPingSeamTests` green.

### VS-17 Import Console from PR #88; hub auth; console-output consumer

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/17-console-import` / `vertical-slice-vs17` |
| Commit subject | `feat(console): import Console from PR #88, authenticated hub, console output consumer [VS-17]` |
| Security review | Yes (hub authentication, cross-service token forwarding) |
| Size | about 40k tokens |

**Goal.** D-02 and D-15: Console's implementation from #88 lands, its hub authenticates browser connections (F-14), ownership checks forward the caller's token, and `ServerConsoleOutput` events reach the browser. After this merges, ops closes PR #88 with a comment pointing at VS-11 and VS-17.

**Rules.**
- Same import rules as VS-11 (`git show 0761b97:...`, check `origin/pr-88`, nothing from Mods, agents or #88's shared `RabbitMqOptions`/`TenantScopedAuthorization`).
- The caller's token is forwarded only to `Services:Servers:BaseUrl` (configuration, never user input); check that the request URI host equals that host before sending.
- Never log tokens. Hub errors go to the caller as the existing `error` event with fixed strings. Browsers send the JWT as `?access_token=` on `/hubs/console` (VS-04), so keep `Microsoft.AspNetCore` at `Warning` in Console's `appsettings*.json` (hosting's `Request starting` line includes the query string) and prove it with a log-capture test (change 7). Trace tags are covered by VS-32.
- Console text is agent-originated and untrusted (C-2 `AgentConsoleLine` remarks, D-23): never log a line's `Text`; the consumer re-checks the `ServerConsoleOutput` row of "Limit enforcement" and drops a violating event whole, with a warning that carries only ids and counts.
- Test JWTs: `SeamJwt` only (D-22). If #88's `ConsoleWebApplicationFactory` brings its own token minter (a signing key plus `PostConfigure<JwtBearerOptions>`), replace it with a test authentication scheme (the `NodesWebApplicationFactory` pattern) or with `SeamJwt` from `tests/Dhadgar.Testing`; do not keep a third helper.
- No `Version=` in `.csproj`; the two Redis packages are added to `Directory.Packages.props` only.

**Contract excerpt.** `ServerConsoleOutput`, `AgentConsoleLine`, `AgentMessageLimits.MaxConsoleLinesPerBatch`/`MaxConsoleLineLength`, the `ServerConsoleOutput` row of "Limit enforcement" (C-2); console hub methods and events (C-5); route rows `console-hub-route` and `console-org-route` (C-1).

**Changes.**
1. Import (same loop as VS-11) `src/Dhadgar.Console`, `tests/Dhadgar.Console.Tests` and `src/Shared/Dhadgar.Contracts/Console/ConsoleContracts.cs` from `0761b97`.
2. `Directory.Packages.props`: add `<PackageVersion Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" Version="10.0.14" />` and `<PackageVersion Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="10.0.14" />` next to the other Microsoft.AspNetCore 10.0.x entries. If restore cannot find 10.0.14, use the highest 10.0.x that exists and say so. Regenerate lock files.
3. `src/Dhadgar.Console/Program.cs`: keep `app.MapHub<ConsoleHub>("/hubs/console").RequireAuthorization()` on the default authenticated policy. Never put the hub under `TenantScoped`: it has no `organizationId` route value, and the shared handler fails closed without one (VS-04 security-1). The org check is `JoinServer`'s ownership call. `using Dhadgar.ServiceDefaults.Messaging;` (VS-11's `RabbitMqOptions`); change to `AddTenantScopedAuthorization(builder.Configuration, builder.Environment, "/hubs/console")`; register `x.AddConsumer<ServerConsoleOutputConsumer>()` in the MassTransit block; add `Auth:*` keys to `appsettings.json` (Gateway values).
4. `src/Dhadgar.Console/Services/IServerOwnershipValidator.cs` and `ServerOwnershipValidator.cs`: signature `Task<bool> ValidateOwnershipAsync(Guid serverId, Guid organizationId, string bearerToken, CancellationToken ct = default)`; send `GET /organizations/{org}/servers/{id}` with `Authorization: Bearer {token}`. `Hubs/ConsoleHub.cs` `JoinServer`: take the token from the connection's `HttpContext` (`Authorization` header without the `Bearer ` prefix, else the `access_token` query); none → `error` "Not authenticated".
5. New `src/Dhadgar.Console/Consumers/ServerConsoleOutputConsumer.cs` (`IConsumer<ServerConsoleOutput>`): first, a null `Lines`, more than `MaxConsoleLinesPerBatch` lines, or any null line or `Text` longer than `MaxConsoleLineLength` → log a warning (`ServerId`, line count) and return without sending or storing anything. Then for each line in order, append to history through `IConsoleHistoryService` (read its interface for the append method; `ConsoleOutputType.StdErr` when `IsError`, else `StdOut`), then `hubContext.Clients.Group($"server-{msg.ServerId}").SendAsync("output", new ConsoleOutputDto(...))`, one per line. Build `ConsoleOutputDto` from its definition in `ConsoleContracts.cs`.
6. Seams: add `<ProjectReference Include="..\..\src\Dhadgar.Console\Dhadgar.Console.csproj" Aliases="console" />` and `<ProjectReference Include="..\..\src\Dhadgar.Servers\Dhadgar.Servers.csproj" Aliases="servers" />`; new `Infrastructure/ConsoleSeamFactory.cs` (mirror #88's `ConsoleWebApplicationFactory` for Redis and DB substitutes; `SeamJwt.Apply`; `Services:Servers:BaseUrl = http://servers.seams/` with the typed client's primary handler swapped for the Servers `TestServer` handler; MassTransit replaced by `AddMassTransitTestHarness(x => x.AddConsumer<ServerConsoleOutputConsumer>())`) and `Infrastructure/ServersSeamFactory.cs` (mirror `ServersWebApplicationFactory`; `SeamJwt.Apply`; MassTransit test harness). Extend `SeamHost` with optional Servers and Console and the Gateway clusters `servers` and `console`.
7. Token hygiene test `tests/Dhadgar.Console.Tests/Hubs/ConsoleHubTokenLoggingTests.cs`: copy the capturing-logger approach of VS-04's `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs` (`CapturingLoggerProvider`, kept inside the Console test project). `GET /hubs/console/negotiate?negotiateVersion=1&access_token={token}` with a valid test token; no captured message or scope contains the token. Positive twin: the same request with `Authorization: Bearer` is logged at all, or the provider captured at least one entry for it, so an empty log does not pass the test vacuously.

**Tests.**
- #88's Console tests come with the import and must pass after the signature change.
- `tests/Dhadgar.Console.Tests/Consumers/ServerConsoleOutputConsumerTests.cs`: a batch of 3 lines produces 3 `output` sends in order to group `server-{id}` and 3 history rows; `IsError` maps to `StdErr`.
- `tests/Dhadgar.Console.Tests/Services/ServerOwnershipValidatorTests.cs`: the request carries the bearer token; 404 → false.
- `ServerConsoleOutputConsumerTests` also: 201 lines → no `output` send and no history row; a 4097-char line → same; a captured log for that case does not contain the line text.
- Seam `tests/Dhadgar.Seams.Tests/Console/ConsoleTailSeamTests.cs`: seed a server for org O in the Servers factory DB; a SignalR client (long polling, `AccessTokenProvider` = `SeamJwt` token for O) connects to the **Gateway** `/hubs/console`, invokes `JoinServer(new JoinServerRequest(serverId, O, 10))`, and receives `joined`; publish a `ServerConsoleOutput` with two lines on the Console test harness bus; the client receives two `output` events in order. A second test (rejection, dispatch rule): a token for org P joining O's server → `error`, no `joined`. The Gateway's `TenantScoped` policy only requires an authenticated user, so the refusal must come from Console's ownership check against Servers: wrap the Servers handler that `ConsoleSeamFactory` injects in a recording `DelegatingHandler` and assert it saw `GET /organizations/{O}/servers/{serverId}` carrying P's token and that Servers answered 403 or 404. Without that record, the `error` could come from a client-side or Console-side shortcut.

Why they fail when reverted: without the token forwarding Servers answers 401 and the join fails; without the consumer no `output` event arrives.

**Checks (in order).**

```bash
git rev-parse origin/pr-88
dotnet restore Dhadgar.sln --locked-mode
dotnet build src/Dhadgar.Console/Dhadgar.Console.csproj -c Release
dotnet test tests/Dhadgar.Console.Tests/Dhadgar.Console.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Dhadgar.Console/README.md`: status line (imported from #88; MVP is tail plus history; `SendCommand` publishes `ExecuteServerCommand`, which nothing consumes yet).

**Done when.** Checks pass. Report "close PR #88" as the next ops action.

---

## Wave 4: Minecraft on the node, servers through the control plane

D-17 (A), D-18 (A) and D-19 (agent downloads a JRE, D-19 ruling 1) are decided. The JRE work adds three packets (VS-29 to VS-31), so wave 4 is split in two waves of a size one reviewer can hold. Each packet ships as its own PR (dispatch rules), and each wave gets its own wave review. Wave 5's "after all of wave 4" means after wave 4b.

**Wave 4** (base: `origin/main` after all of wave 3): VS-18, VS-19, VS-29, VS-22, VS-20, VS-30. Stage 1: VS-18, VS-19, VS-22, VS-29. Stage 2: VS-20 (after VS-19 and VS-29), VS-30 (after VS-29). Merge order: VS-18, VS-29, VS-22, VS-19, VS-30, VS-20. VS-19, VS-29, VS-30 and VS-20 each add one registration to `Hosting/AgentServiceCollectionExtensions.cs`; whichever merges later rebases (different lines).

**Wave 4b** (base: `origin/main` after all of wave 4): VS-23, VS-21, VS-31, all dispatched together. Merge order: VS-23, VS-21, VS-31. VS-21 and VS-31 both add registrations to `src/Agents/Dhadgar.Agent.Windows/Program.cs`; VS-31 rebases onto VS-21.

### VS-18 Agent.Core test debt

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/18-agent-test-debt` / `vertical-slice-vs18` |
| Commit subject | `test(agent-core): cover enrollment, dispatcher and control-plane client paths (DV-14) [VS-18]` |
| Security review | No (tests only) |
| Size | about 35k tokens |

**Goal.** DV-14 and roadmap Phase 2.7: the security-critical Agent.Core paths not already covered by VS-05 and VS-12..VS-16 get unit tests.

**Rules.**
- Test files only, in `tests/Dhadgar.Agent.Core.Tests`. If a path cannot be tested without a production change, list it in the report instead of changing code.
- xUnit plus NSubstitute, as the project already uses; stub HTTP with a `DelegatingHandler` subclass, never real network.
- No sleeps longer than 100 ms; use `FakeTimeProvider` where the code accepts a `TimeProvider`.

**Changes (new test files).**
1. `Authentication/EnrollmentServiceTests.cs`: happy path stores cert and private key (key bytes in the store equal the generated key, cert key matches); non-success status → failure with the status; a body over the size cap → `ResponseTooLarge`; a `Content-Length` over the cap → failure before reading; a certificate for another key → `KeyMismatch`; an HTTP endpoint (not HTTPS) → refused; already enrolled → failure without an HTTP call.
2. `Commands/CommandDispatcherTests.cs`: unknown type → failure; a handler returning failure → failure result with the message; a handler throwing → failure (no exception escapes); node mismatch → rejected before the handler runs; duplicate handler registration → `InvalidOperationException` (programming error, allowed).
3. `Communication/ControlPlaneClientTests.cs`: `ConnectAsync` with an `http://` endpoint throws before any network call; an enrolled agent without a certificate fails fast; `SendHeartbeatAsync` when not connected logs and returns; the protocol header is set (capture `HttpConnectionOptions` through an `IHubConnectionConfigurator` and assert `Headers[AgentProtocol.VersionHeader] == "1"`).
4. `Files/PathValidatorTests.cs` (only if `tests/Dhadgar.Agent.Core.Tests` has none): `..` traversal, null byte, control characters, a path outside the allowed base, a valid nested path.

**Checks (in order).**

```bash
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release --collect "XPlat Code Coverage" --results-directory /tmp/vs18-cov
```

Report the line coverage of `Authentication/`, `Commands/` and `Communication/` from the Cobertura file (`grep -o 'line-rate="[0-9.]*"'` on the package and class entries). Target: 60 % or more for each folder. Below that, list the uncovered members instead of stretching the packet.

**Docs owed.** None.

**Done when.** Tests pass and coverage is reported.

### VS-19 Minecraft provisioning on the agent (jar, EULA, properties)

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/19-mc-provisioning` / `vertical-slice-vs19` |
| Commit subject | `feat(agent-core): Minecraft server provisioning with pinned jar download and EULA gate [VS-19]` |
| Security review | Yes (downloads executable content onto customer hardware) |
| Status | D-19 decided (ruling 1). This packet is the jar, EULA and properties half; the Java runtime is VS-29 and VS-30. No dependency on either. |
| Size | about 38k tokens |

**Goal.** Before a start, the agent can lay out a server directory, fetch the pinned vanilla server jar from Mojang and write `eula.txt` and `server.properties`. Platform-neutral, so it is tested on Linux. Finding the `java` executable is **not** this packet's job: the StartServer handler (VS-20) gets it from `IJavaRuntimeResolver` (VS-29), and `MinecraftInstallation` carries no Java path.

**Rules.**
- Trusted hosts: the jar URL must be `https`, its host must be in `Agent:Minecraft:AllowedDownloadHosts` (default `["piston-data.mojang.com"]`), and its path must end in `/server.jar`. Check before any request.
- The HttpClient disables redirects; any 3xx is a failure. Cap the body at `MaxJarBytes` (default 200 MiB) while streaming.
- Every path goes through `IPathValidator.ValidatePath(path, [ProcessOptions.ServerBasePath])`. The server directory is `Path.Combine(ServerBasePath, serverId.ToString("N"))`.
- SHA-1 is used only because Mojang publishes SHA-1; the value is pinned by the control plane (D-14). Suppress the weak-hash analyzer on that line only, with that justification.
- `eula.txt` is written only when `StartServerPayload.EulaAccepted` is true; otherwise fail with `[Minecraft.EulaNotAccepted]`.
- `Result<T>` for all validation, IO and network errors; `using` on streams, hashers and responses; delete partial downloads on any failure.

**Contract excerpt.**

```csharp
public sealed record StartServerPayload(
    Guid ServerId, string MinecraftVersion, string ServerJarUrl, string ServerJarSha1,
    int MemoryMb, int Port, bool EulaAccepted);
```

**Changes.**
1. New `src/Agents/Dhadgar.Agent.Core/Minecraft/MinecraftOptions.cs` (section `Agent:Minecraft`; add `public MinecraftOptions Minecraft { get; set; } = new();` to `AgentOptions`): `string[] AllowedDownloadHosts = ["piston-data.mojang.com"]`, `long MaxJarBytes = 200L * 1024 * 1024`. **No `JavaPath` here**: VS-29's `JavaRuntimeOptions` binds the Java keys from the same `Agent:Minecraft` section (C-6). Do not create `Minecraft/Java/`; that folder belongs to VS-29.
2. New `Minecraft/MinecraftInstallation.cs`: `public sealed record MinecraftInstallation(Guid ServerId, string WorkingDirectory, string JarPath, IReadOnlyList<string> JavaArguments);` with `JavaArguments` = `["-Xmx{MemoryMb}M", "-jar", jarPath, "nogui"]`.
3. New `Minecraft/IMinecraftInstaller.cs` and `MinecraftInstaller.cs`: `Task<Result<MinecraftInstallation>> EnsureInstalledAsync(StartServerPayload payload, CancellationToken ct)`:
   1. Validate with the C-2 constants, never local copies (D-23; review-2 item 7): `ServerId` not empty; `MinecraftVersion` matches `AgentMessageLimits.MinecraftVersionPattern` (a `[GeneratedRegex]` on the constant; the old stricter `^[0-9]+(\.[0-9]+){1,2}$` is dropped, because which versions exist is Servers' catalog decision, D-14, and the path is protected by `IPathValidator` anyway); `ServerJarSha1` exactly `ServerJarSha1HexLength` hex chars, either case, compared with the computed hash ordinal-ignore-case; `ServerJarUrl` at most `MaxServerJarUrlLength` plus the URL rules; `MemoryMb` in `MinServerMemoryMb..MaxServerMemoryMb`; `Port` in `MinServerPort..MaxServerPort`; `EulaAccepted`. Each failure is `[Minecraft.InvalidPayload] <field>`.
   2. Create the server directory.
   3. If `server.jar` exists and its SHA-1 matches, skip the download. Else stream to `server.jar.download` in the directory while hashing incrementally; on a hash mismatch or size overflow delete it and fail `[Minecraft.JarHashMismatch]`/`[Minecraft.JarTooLarge]`; else `File.Move(..., "server.jar", overwrite: true)`.
   4. Write `eula.txt`: `#Accepted via Meridian Console on {UTC ISO-8601}` then `eula=true`.
   5. `server.properties`: if missing, write `server-port={Port}`, `enable-rcon=false`, `motd=Meridian Console`; if present, replace or append only the `server-port` line.
4. `Hosting/AgentServiceCollectionExtensions.cs`: register `IMinecraftInstaller` and a named HttpClient `MinecraftDownloads` (`AllowAutoRedirect = false`, timeout 10 min).

**Tests** (`tests/Dhadgar.Agent.Core.Tests/Minecraft/MinecraftInstallerTests.cs`, temp `ServerBasePath`, stub handler serving known bytes; compute the expected SHA-1 in the test):
- a valid payload downloads, writes the jar, `eula.txt` and `server.properties`, and returns `-Xmx` from `MemoryMb`;
- a second call with the jar present makes no HTTP request;
- a SHA-1 mismatch fails and leaves neither `server.jar` nor `.download`;
- an untrusted host fails with no request made (the stub records zero calls);
- a 302 fails;
- `EulaAccepted=false` fails and writes no `eula.txt`;
- an existing `server.properties` keeps its other lines and gets the new port;
- boundaries, each with zero HTTP calls on failure: `MinecraftVersion` `24w14a` (a snapshot id) passes validation, `1.21.11/..` and a 33-char version fail; an upper-case SHA-1 passes; `MemoryMb` 511/512 and 32768/32769; `Port` 1023/1024 and 65535/65536; a 2049-char URL fails.

Why they fail when reverted: nothing provisions a server, so `StartServer` (VS-20) has no jar to run.

**Checks (in order).**

```bash
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
```

**Docs owed.** `src/Agents/Dhadgar.Agent.Core/README.md` "Minecraft provisioning" (download host, EULA rule, directory layout; one line saying Java is provisioned separately, see "Java runtime", which VS-29 writes).

**Done when.** Checks pass.

### VS-29 Java runtime contract, catalog pins and resolver policy

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/29-java-runtime-contract` / `vertical-slice-vs29` |
| Commit subject | `feat(contracts,agent-core,servers): pinned Temurin JRE artifacts and Java runtime resolver policy (D-19) [VS-29]` |
| Security review | Yes (agent project; decides what the agent may download and run) |
| Depends on | wave 3 only (stage 1, parallel with VS-19 and VS-22; no shared files) |
| Size | about 35k tokens |

**Goal.** D-19 ruling 1, policy half. The contract carries pinned JRE artifacts. Servers holds the pins. The agent has one `IJavaRuntimeResolver` that applies the `JavaPath` override, picks the artifact for its platform and validates it before handing it to an installer. The real installer is VS-30; until then a stub installer answers `[Java.ManagedInstallUnavailable]`, so the override is the only working path.

**Rules.**
- Seams harness and test JWTs (D-22): mint and accept test user tokens only with `SeamJwt` (`tests/Dhadgar.Testing/Auth/SeamJwt.cs` after VS-32, which removes VS-04's duplicate minter in `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`); never add another minter or `PostConfigure<JwtBearerOptions>` test-key block. Client-certificate shims stay inside the test project that owns them. A seam that asserts a rejection proves the server answered: the status or code comes from the server's response, the forwarder or a backend recorder shows which server it reached, and a positive twin passes (dispatch rules).
- `Result<T>` (or `Result`) for every validation, IO and configuration failure, with the `[Category.Code] message` style. No exceptions for bad input.
- Trusted hosts: the agent checks an artifact URL (C-7 rule 4) **before** any request. Hosts compare exactly (ordinal, ignore case); no subdomain or wildcard matching.
- File paths: the override goes through `IPathValidator.IsSafePath` plus `Path.IsPathFullyQualified`, because it is operator configuration outside any agent root. Everything under `JavaRuntimePath` goes through `ValidatePath(path, [JavaRuntimePath])` (VS-30).
- Contracts: `Dhadgar.Contracts.csproj` gets no package references. Change no C-2 record except appending the one optional parameter to `StartServerPayload`. It **must** have the `= null` default: `AgentJson` requires every parameter without one (C-2 JSON rules). Agents built before this packet ignore `javaRuntimes` (D-40), which is acceptable because no agent ships before VS-27. Nodes must be built with this packet to keep the field: its pre-sign parse (VS-15) re-serializes the typed record, so a Nodes built before VS-29 would drop `javaRuntimes` from what it signs, and the count limit `MaxPerPayload` is the agent's check (C-7 rule 3). Before appending, `grep -rn "StartServerPayload" src tests` and check that nobody deconstructs it (`var (` on it); report any hit.
- No `Version=` in any `.csproj` (Central Package Management, `Directory.Packages.props`).
- Nullable enabled and warnings as errors (`CI=true`). XML docs on public types, as `Nodes/EnrollmentContracts.cs` does.
- xUnit in the style of each target test project; temp directories cleaned in `Dispose`.
- New `.cs` files are CRLF (dispatch rules).

**Contract excerpt.** C-7 in full: `JavaRuntimePlatforms`, `JavaRuntimeArtifact`, the `StartServerPayload` addition, the pin table and agent rules 1 to 4. C-6 rows for `Agent:Minecraft:*`.

**Changes.**
1. New `src/Shared/Dhadgar.Contracts/Agents/JavaRuntimeContracts.cs`: `JavaRuntimePlatforms` and `JavaRuntimeArtifact`, exactly as in C-7. `Agents/AgentCommandPayloads.cs`: append `IReadOnlyList<JavaRuntimeArtifact>? JavaRuntimes = null` to `StartServerPayload`, with an XML doc ("Pinned JRE archives, one per platform; ignored when the agent has `Agent:Minecraft:JavaPath`").
2. New `src/Dhadgar.Servers/Minecraft/JavaRuntimeCatalog.cs` (a separate file from VS-22's `MinecraftCatalog.cs`, so the two packets do not collide):

```csharp
public static class JavaRuntimeCatalog
{
    public const string TemurinVersion = "21.0.12.1+1";   // checked against api.adoptium.net and GitHub .sha256.txt on 2026-10-26
    public static IReadOnlyDictionary<int, IReadOnlyList<JavaRuntimeArtifact>> ByMajorVersion { get; } =
        new Dictionary<int, IReadOnlyList<JavaRuntimeArtifact>>
        {
            [21] =
            [
                new(JavaRuntimePlatforms.Windows, JavaRuntimePlatforms.X64, 21, TemurinVersion,
                    "https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.12.1%2B1/OpenJDK21U-jre_x64_windows_hotspot_21.0.12.1_1.zip",
                    "d35f31e712f0fcf6ac5a093edc90204fbff22f720ba3950bd09d331d5e621636", 48999141),
                new(JavaRuntimePlatforms.Windows, JavaRuntimePlatforms.Aarch64, 21, TemurinVersion,
                    "https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.12.1%2B1/OpenJDK21U-jre_aarch64_windows_hotspot_21.0.12.1_1.zip",
                    "e82cc17e0bf89a25b0b0ed106d072f2ea420587d0a6870534b71b1dce3ae28c3", 40078274),
            ],
        };

    public static IReadOnlyList<JavaRuntimeArtifact> ForMajorVersion(int majorVersion) =>
        ByMajorVersion.TryGetValue(majorVersion, out var list) ? list : [];
}
```

3. New `src/Agents/Dhadgar.Agent.Core/Minecraft/Java/JavaRuntimeOptions.cs` (`IValidatableObject`, `public const string SectionName = "Agent:Minecraft"`):

```csharp
public string JavaPath { get; set; } = "";
public bool JavaDownloadEnabled { get; set; } = true;
public string JavaRuntimePath { get; set; } = DefaultRuntimePath();
// Windows: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Meridian Console", "Agent", "runtimes")
// otherwise: "/var/lib/dhadgar-agent/runtimes"
public string[] JavaDownloadHosts { get; set; } = ["github.com"];
public string[] JavaRedirectHosts { get; set; } = ["release-assets.githubusercontent.com", "objects.githubusercontent.com"];
public long MaxJavaArchiveBytes { get; set; } = 256L * 1024 * 1024;
public long MaxJavaExtractedBytes { get; set; } = 768L * 1024 * 1024;
public int MaxJavaArchiveEntries { get; set; } = 5000;
public int JavaRuntimeRetentionDays { get; set; } = 7;
```

   Validation: `JavaRuntimePath` fully qualified and already normalized (same rule as `ProcessOptions.ServerBasePath`); `JavaDownloadHosts` not empty when `JavaDownloadEnabled`; no host contains `*`, `/`, `:` or whitespace; caps above 0; retention ≥ 0. (VS-12's `StatePath` is a sibling folder, `...\Agent\state`, and is not shared.)
4. New `Minecraft/Java/JavaPlatform.cs`: `public static (string Os, string Architecture)? Current { get; }`. `OperatingSystem.IsWindows()` maps to `windows` and `IsLinux()` to `linux`; `RuntimeInformation.OSArchitecture` `X64` maps to `x64` and `Arm64` to `aarch64`; anything else is null.
5. New `Minecraft/Java/JavaRuntimeArtifactValidator.cs` (static, so the seam test can call it): `Result Validate(JavaRuntimeArtifact artifact, JavaRuntimeOptions options)` (C-7 rule 4) and `Result<JavaRuntimeArtifact> Select(IReadOnlyList<JavaRuntimeArtifact>? artifacts, (string Os, string Architecture)? platform)` (rule 3). Compile the path regex once with `[GeneratedRegex]`.
6. New `Minecraft/Java/IJavaRuntimeResolver.cs`: `Task<Result<string>> ResolveAsync(StartServerPayload payload, CancellationToken ct);`, returning the full path of the `java` executable. New `Minecraft/Java/IManagedJavaRuntimeInstaller.cs`: `Task<Result<string>> EnsureInstalledAsync(JavaRuntimeArtifact artifact, CancellationToken ct);`, plus `UnavailableManagedJavaRuntimeInstaller` returning `Failure("[Java.ManagedInstallUnavailable] Managed Java download is not available in this build; set Agent:Minecraft:JavaPath")`.
7. New `Minecraft/Java/JavaRuntimeResolver.cs` (singleton; `IOptionsMonitor<JavaRuntimeOptions>`, `IPathValidator`, `IManagedJavaRuntimeInstaller`, logger): C-7 rules 1 to 4 in order, then `EnsureInstalledAsync`. Log the artifact `Os`, `Architecture` and `Version`, never the URL.
8. `Hosting/AgentServiceCollectionExtensions.cs` `AddAgentCore`: `services.AddOptions<JavaRuntimeOptions>().BindConfiguration(JavaRuntimeOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();`, `TryAddSingleton<IJavaRuntimeResolver, JavaRuntimeResolver>()`, `TryAddSingleton<IManagedJavaRuntimeInstaller, UnavailableManagedJavaRuntimeInstaller>()`.

**Tests.**
- `tests/Dhadgar.Contracts.Tests/Agents/JavaRuntimeContractsTests.cs`: JSON without `javaRuntimes` deserializes with `JavaRuntimes == null` (`AgentJson.Options`); a payload with two artifacts round-trips with camelCase names `javaRuntimes`, `archiveUrl`, `archiveSha256`, `archiveSizeBytes`.
- `tests/Dhadgar.Servers.Tests/Minecraft/JavaRuntimeCatalogTests.cs`: `ForMajorVersion(21)` holds exactly the two C-7 rows (values pinned in the test, so a bump updates both on purpose); `ForMajorVersion(17)` is empty.
- `tests/Dhadgar.Agent.Core.Tests/Minecraft/Java/JavaRuntimeArtifactValidatorTests.cs`: the two C-7 pins pass with default options (literal copies, not a reference to Servers). Each of these fails with `Java.InvalidArtifact`: `http://`; port 8443; user info; a query string; host `github.com.evil.example`; host `objects.githubusercontent.com` as the **initial** URL (redirect hosts are not download hosts); another owner (`/evil/temurin21-binaries/...`); a JDK file (`OpenJDK21U-jdk_...`); a `.msi`; an os token that differs from `Os`; an upper-case or 63-char hash; size 0 and size above the cap; `Version` `../x`. `Select`: none, two for the platform, nine entries, null platform.
- `.../Java/JavaRuntimeResolverTests.cs` (recording fake installer; temp dir): an override that exists and is named `java.exe`/`java` (by OS) returns it with **zero** installer calls even when `JavaRuntimes` is present; a missing override file, a relative override and a file named `notjava` each give `Java.ConfiguredPathInvalid` with zero installer calls (no fallback); `JavaDownloadEnabled=false` gives `Java.NotConfigured`; an invalid artifact means zero installer calls; a valid artifact for `JavaPlatform.Current` means exactly one call with that artifact; an installer failure propagates unchanged.
- Seam `tests/Dhadgar.Seams.Tests/Agents/JavaRuntimeCatalogSeamTests.cs`: every artifact in Servers' `JavaRuntimeCatalog.ByMajorVersion` passes the agent's `JavaRuntimeArtifactValidator.Validate(a, new JavaRuntimeOptions())`, and a `StartServerPayload` carrying them serializes with `AgentJson.Options` and deserializes to equal artifacts. If `Dhadgar.Seams.Tests` does not yet reference `Dhadgar.Servers` or `Dhadgar.Agent.Core`, add the reference with an extern alias in the pattern VS-05 set (read the csproj), then follow the lock-file rule. Prove it red once (D-13): change one catalog host to `api.adoptium.net` locally, run, and quote the failure line in the report. Do not commit that change.

Why they fail when reverted: the types do not exist (compile failure). The seam test fails as soon as Servers pins a URL the agent refuses, which is the seam this packet creates.

**Checks (in order).**

```bash
dotnet build src/Shared/Dhadgar.Contracts/Dhadgar.Contracts.csproj -c Release
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet build src/Dhadgar.Servers/Dhadgar.Servers.csproj -c Release
dotnet test tests/Dhadgar.Contracts.Tests/Dhadgar.Contracts.Tests.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
dotnet test tests/Dhadgar.Servers.Tests/Dhadgar.Servers.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Agents/Dhadgar.Agent.Core/README.md` "Java runtime": the override rule, managed download (Temurin from GitHub releases, the hosts to allow outbound), the install location, the error codes, and that VS-30/VS-31 complete it. `src/Dhadgar.Servers/README.md` "Java runtime catalog": how to bump a pin (query `https://api.adoptium.net/v3/assets/latest/21/hotspot?os=windows&architecture=x64&image_type=jre`, check `package.checksum` against the release's `.sha256.txt` on GitHub, update the catalog and its test together; Temurin ships quarterly security updates in January, April, July and October). `src/Shared/Dhadgar.Contracts/README.md` Agents section: one line for `JavaRuntimeContracts.cs`.

**Done when.** Checks pass; `JavaRuntimeCatalogSeamTests` green; red run quoted.

### VS-20 Minecraft command handlers and console forwarder (Core)

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/20-mc-handlers` / `vertical-slice-vs20` (from `origin/main` after VS-19 and VS-29) |
| Commit subject | `feat(agent-core): StartServer, StopServer, ServerStatus handlers and console forwarding (#118) [VS-20]` |
| Security review | Yes (agent command handlers) |
| Size | about 40k tokens |

**Goal.** #118's minimum handler set and D-15's agent half, written against a host interface so they are testable on Linux. The Windows host is VS-21.

**Rules.**
- Seams harness and test JWTs (D-22): mint and accept test user tokens only with `SeamJwt` (`tests/Dhadgar.Testing/Auth/SeamJwt.cs` after VS-32, which removes VS-04's duplicate minter in `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`); never add another minter or `PostConfigure<JwtBearerOptions>` test-key block. Client-certificate shims stay inside the test project that owns them. A seam that asserts a rejection proves the server answered: the status or code comes from the server's response, the forwarder or a backend recorder shows which server it reached, and a positive twin passes (dispatch rules).
- Handlers never start a process themselves; they call `IMinecraftInstaller`, `IJavaRuntimeResolver` (VS-29) and `IMinecraftServerHost`. They never read `Agent:Minecraft:JavaPath` themselves; the resolver owns the override rule (C-7 rule 1).
- Console text is truncated to `AgentMessageLimits.MaxConsoleLineLength`; a batch flushes at 200 lines, at `MaxConsoleBatchChars` or every 250 ms; at most 400 lines per second per server, and overflow is counted and reported as one line `[agent] N lines dropped`.
- The forwarder never blocks the host's output event (buffer in a `Channel` per server, flush on a timer).
- `Result<T>`; `using`; no exceptions out of event handlers (log and continue).

**Contract excerpt.** `StartServerPayload` (with C-7's `JavaRuntimes`), `StopServerPayload`, `ServerStatusPayload`, `ServerStatusResult`, `AgentServerState`, `AgentServerStatus`, `AgentConsoleBatch`, `AgentConsoleLine`, `AgentMessageLimits`, `AgentCommandTypes` (C-2). Agent-side: `IJavaRuntimeResolver.ResolveAsync(StartServerPayload, CancellationToken) → Task<Result<string>>` (VS-29).

**Changes.**
1. New `src/Agents/Dhadgar.Agent.Core/Minecraft/IMinecraftServerHost.cs`:

```csharp
public interface IMinecraftServerHost
{
    Task<Result<ServerStatusResult>> StartAsync(
        MinecraftInstallation installation, string javaExecutablePath, StartServerPayload payload, CancellationToken ct);
    Task<Result<ServerStatusResult>> StopAsync(Guid serverId, TimeSpan gracefulTimeout, CancellationToken ct);
    ServerStatusResult GetStatus(Guid serverId);                 // Stopped when unknown
    IReadOnlyList<ServerStatusResult> GetAll();
    event EventHandler<MinecraftOutputEventArgs>? OutputReceived;  // ServerId, Text, IsError, Timestamp
    event EventHandler<MinecraftStatusEventArgs>? StatusChanged;   // ServerId, State, ExitCode, Message, Timestamp
}
```

   The two event-args classes go in the same file.
2. New handlers in `src/Agents/Dhadgar.Agent.Core/Commands/Handlers/` (each `CommandHandlerBase<TPayload>`, registered as `ICommandHandler` only when an `IMinecraftServerHost` is registered; use `services.TryAddEnumerable` in a new `AddMinecraftHandlers()` that `AddAgentCore` calls):
   - `StartServerCommandHandler`: reject an empty `ServerId`; `EnsureInstalledAsync` → `IJavaRuntimeResolver.ResolveAsync(payload, ct)` → `StartAsync(installation, javaPath, payload, ct)` → success result JSON `ServerStatusResult` (`AgentJson.Options`). A resolver failure becomes a failure result whose `ErrorCode` is the `Java.*` code from the message (it matches `AgentMessageLimits.ErrorCodePattern`), and the host is not called. Nodes has already checked that the server is claimed on this node; the agent has nothing further to compare.
   - `StopServerCommandHandler`: `GracefulTimeoutSeconds` outside `AgentMessageLimits.MinGracefulTimeoutSeconds..MaxGracefulTimeoutSeconds` (1..300) → failure `[Minecraft.InvalidPayload] gracefulTimeoutSeconds`, host not called. It is **rejected, never clamped** (review-2 item 7, D-23); Servers sends 60 (VS-23). In range → `StopAsync`.
   - `ServerStatusCommandHandler`: `GetStatus`.
3. New `Minecraft/ConsoleOutputForwarder.cs` (`IHostedService`, registered in `AddMinecraftHandlers()`): subscribes to `OutputReceived` and `StatusChanged`; batches per D-15 and the rules; sends with `IControlPlaneClient.SendConsoleOutputAsync(new AgentConsoleBatch(nodeId, serverId, lines))` and `SendServerStatusAsync(new AgentServerStatus(...))` (`Message` truncated to `MaxErrorMessageLength`; a batch never exceeds `MaxConsoleLinesPerBatch` lines or `MaxConsoleBatchChars` chars, because Nodes rejects a violating batch whole, VS-33); `nodeId` from `IOptionsMonitor<AgentOptions>.CurrentValue.NodeId` (skip sending while null).
4. `Health/HealthReporter.cs`: when an `IMinecraftServerHost` is registered, add its `GetAll()` entries to `HeartbeatPayload.ActiveProcesses` (map `AgentServerState` to `ProcessState`: Running→Running, Starting/Installing→Starting, Stopping→Stopping, Crashed→Failed, Stopped→Stopped; `ProcessId` = `ServerId`). Inject it as `IEnumerable<IMinecraftServerHost>` and use the first.

**Tests** (`tests/Dhadgar.Agent.Core.Tests/Minecraft/`), with a `FakeMinecraftServerHost` and a fake installer:
- `StartServerCommandHandlerTests`: installer failure → failure result with its code, host not called; resolver failure (`[Java.NoRuntimeForPlatform] ...`) → failure result with `Java.NoRuntimeForPlatform`, host not called; success → the host receives the resolver's path, and the result is `ServerStatusResult` JSON with `Running`.
- `StopServerCommandHandlerTests`: 0 and 301 → `Minecraft.InvalidPayload` with the host not called; 1 and 300 → `StopAsync` with exactly that timeout; host failure → failure.
- `ConsoleOutputForwarderTests` (fake client, `FakeTimeProvider` if the timer accepts one): 3 lines → one batch after the flush interval; 250 lines → two batches (200 and 50); 1,000 lines in one second → at most 400 sent plus one "dropped" line; a 10,000-char line is truncated to 4,096; 20 lines of 4,096 chars → two batches, neither over `MaxConsoleBatchChars`; a status change → one `SendServerStatusAsync`, and a 2,000-char status message is sent as 1,024 chars.
- `HealthReporterMinecraftTests`: a running fake server shows up in the heartbeat payload.
- Seam `tests/Dhadgar.Seams.Tests/Console/AgentConsoleSeamTests.cs`: with the real agent host from `AgentBootstrapPingSeamTests` (move its setup into `Infrastructure/SeamAgentHost.cs`, now registering `FakeMinecraftServerHost`, the fake installer and a `FakeJavaRuntimeResolver` that returns a fixed path, with `services.Replace`, so seam tests never download anything), Nodes, Console and Gateway: seed a claimed reservation on the node for server S (Nodes DB) and server S in the Servers DB; a browser-style console client joins S through the Gateway; the fake host raises two output lines; the client receives both `output` events. The Nodes `CapturingPublishEndpoint` delivers to Console: in the test, forward captured `ServerConsoleOutput` messages to the Console harness bus (`harness.Bus.Publish`), which stands in for RabbitMQ.

Why they fail when reverted: no handler is registered for `StartServer`, so the dispatcher rejects it, and no batch ever reaches the hub.

**Checks (in order).**

```bash
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Agents/Dhadgar.Agent.Core/README.md` "Command handlers" (the four types, payloads, results) and "Console forwarding" (limits).

**Done when.** Checks pass; `AgentConsoleSeamTests` green.

### VS-30 Managed JRE install: download, verify, extract (Core)

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/30-java-runtime-install` / `vertical-slice-vs30` (from `origin/main` after VS-29) |
| Commit subject | `feat(agent-core): download, verify and install the pinned Temurin JRE (D-19) [VS-30]` |
| Security review | Yes (downloads and unpacks executable content onto customer hardware) |
| Size | about 40k tokens |

**Goal.** D-19 ruling 1, mechanics half. `ManagedJavaRuntimeInstaller` replaces VS-29's stub. It downloads the pinned archive through validated hops, checks size and SHA-256, unpacks it with byte, entry and path limits, and installs it atomically under `JavaRuntimePath`, so a second start reuses it with no network call. Platform-neutral and tested on Linux. On Windows it fails closed (`Java.DirectoryNotSecured`) until VS-31 registers the ACL securer.

**Rules.**
- `Result<T>` for every failure. Catch `IOException`, `UnauthorizedAccessException`, `HttpRequestException`, `InvalidDataException` and timeout `TaskCanceledException` and map them to `[Java.IoError]`, `[Java.DownloadFailed]` or `[Java.CorruptArchive]`. Real cancellation (`ct`) still propagates after cleanup.
- Paths: every path under the root goes through `IPathValidator.ValidatePath(path, [JavaRuntimePath])`. Every zip entry destination also goes through `ValidatePath(dest, [stagingDir])`.
- Trusted hosts: re-run `JavaRuntimeArtifactValidator.Validate` on entry (defence in depth). Check every redirect hop against `JavaDownloadHosts` ∪ `JavaRedirectHosts` before requesting it (C-7 rule 5).
- **Never log a redirect URL**: GitHub's `release-assets` URL carries a signed query. Log host and status only.
- No `ZipFile.ExtractToDirectory` (it has no byte limits). Never trust header sizes in the zip; count the bytes actually written. Reject symlink entries.
- `using` on `HttpResponseMessage`, every `Stream`, `ZipArchive` and `IncrementalHash`. Delete the partial archive and the staging directory in `finally`.
- No new packages (`System.IO.Compression` is in the BCL). No `Version=` in `.csproj`. Warnings are errors; guard Unix-only APIs with `OperatingSystem` checks (CA1416).

**Contract excerpt.** C-7 rules 5 to 7, `JavaRuntimeArtifact`, and VS-29's `IManagedJavaRuntimeInstaller`, `JavaRuntimeOptions`, `JavaRuntimeArtifactValidator` and `JavaPlatform`.

**Changes.**
1. New `src/Agents/Dhadgar.Agent.Core/Minecraft/Java/IJavaRuntimeDirectorySecurer.cs`: `Result Secure(string runtimeRoot);`. `DefaultJavaRuntimeDirectorySecurer`: on non-Windows, `File.SetUnixFileMode(root, 0755 as UnixFileMode flags)` → Success; on Windows, `Failure("[Java.DirectoryNotSecured] No Windows ACL securer is registered")`. VS-31 replaces it on Windows.
2. New `Minecraft/Java/IJavaRuntimeMaintenance.cs`: `Task RunAsync(string runtimeRoot, string keepKey, CancellationToken ct);` and `NoOpJavaRuntimeMaintenance`. VS-31 replaces it with the pruner.
3. New `Minecraft/Java/JavaRuntimeMarker.cs`: `public sealed record JavaRuntimeMarker(string Sha256, string Os, string Architecture, string Version, DateTimeOffset InstalledAt, DateTimeOffset LastUsedAt)`, `public const string FileName = ".meridian-runtime.json"`, `Result<JavaRuntimeMarker> Read(string dir)` and `Result Write(string dir, JavaRuntimeMarker marker)`. Write `.tmp` first, then `File.Move(..., overwrite: true)`. Use `AgentJson.Options`.
4. New `Minecraft/Java/ManagedJavaRuntimeInstaller.cs` (singleton; `IHttpClientFactory`, `IPathValidator`, `IOptionsMonitor<JavaRuntimeOptions>`, `IJavaRuntimeDirectorySecurer`, `IJavaRuntimeMaintenance`, `TimeProvider`, logger; one `SemaphoreSlim(1, 1)` around the whole method):

```csharp
var key = $"{artifact.Os}-{artifact.Architecture}-{artifact.ArchiveSha256[..16]}";   // hex only
// root = ValidatePath(o.JavaRuntimePath, [o.JavaRuntimePath]); Directory.CreateDirectory(root); _securer.Secure(root) must succeed
// installDir = root/key; javaExe = installDir/bin/(java.exe | java); both through ValidatePath(..., [root])
// reuse: marker reads, marker.Sha256 == artifact.ArchiveSha256 (ordinal), File.Exists(javaExe)
//        → rewrite marker with LastUsedAt = now; maintenance; return javaExe (no HTTP)
// otherwise: delete installDir if present (no valid marker = incomplete install)
// archive = root/.downloads/{key}.zip.partial ; staging = root/.staging/{key}-{Guid:N}
// download → extract → Directory.Move(staging/<single top dir>, installDir) → write marker (last) → maintenance → return javaExe
```

   - **Download:** named client `JavaRuntimeDownloads`, `HttpCompletionOption.ResponseHeadersRead`. Follow redirects by hand per C-7 rule 5 (at most 3). On the final 200: a `Content-Length` other than `ArchiveSizeBytes` → `[Java.SizeMismatch]` before reading. Stream to `archive` through `IncrementalHash` (SHA-256). More than `ArchiveSizeBytes` bytes → `[Java.ArchiveTooLarge]`; a short body → `[Java.SizeMismatch]`; a different hash (lower-case hex, ordinal) → `[Java.HashMismatch]`. Any other status → `[Java.DownloadFailed] HTTP {code} from {host}`.
   - **Extract:** `new ZipArchive(fileStream, ZipArchiveMode.Read)`. More than `MaxJavaArchiveEntries` entries → `[Java.ArchiveTooLarge]`. Per entry: reject names that are rooted or contain `\`, `:` or a `..` or `.` segment, and Unix symlinks (`((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000`), with `[Java.CorruptArchive] unsafe entry`. Then `dest = Path.GetFullPath(Path.Combine(staging, entry.FullName))` must pass `ValidatePath(dest, [staging])`. A name ending in `/` is a directory; otherwise create the parent and copy through a counting stream. A running total above `MaxJavaExtractedBytes` → `[Java.ExtractedTooLarge]`.
   - **Layout:** staging holds exactly one directory and no files, and that directory holds `bin/java.exe` (Windows) or `bin/java` plus a `release` file; otherwise `[Java.UnexpectedLayout]`. `release` must contain a line starting `JAVA_VERSION="{MajorVersion}.` or equal to `JAVA_VERSION="{MajorVersion}"`; otherwise `[Java.VersionMismatch]`.
   - **Maintenance:** call `_maintenance.RunAsync(root, key, ct)` inside the lock after success. Log its failures; they never fail the resolve.
5. `Hosting/AgentServiceCollectionExtensions.cs`: change the installer registration to `ManagedJavaRuntimeInstaller`. `TryAddSingleton<IJavaRuntimeDirectorySecurer, DefaultJavaRuntimeDirectorySecurer>()`, `TryAddSingleton<IJavaRuntimeMaintenance, NoOpJavaRuntimeMaintenance>()`, `TryAddSingleton(TimeProvider.System)`. Named client `JavaRuntimeDownloads`: primary handler with `AllowAutoRedirect = false`, `AutomaticDecompression = None` (same handler type as VS-19's `MinecraftDownloads`), timeout 15 min, `User-Agent: MeridianConsole-Agent/<assembly version>`.

**Tests** (`tests/Dhadgar.Agent.Core.Tests/Minecraft/Java/ManagedJavaRuntimeInstallerTests.cs`): a temp `JavaRuntimePath`; a stub `HttpMessageHandler` that records requests; a zip built in memory by the test (`jdk-21.0.0+1-jre/bin/java[.exe]` by OS, `jdk-21.0.0+1-jre/release` with `JAVA_VERSION="21.0.0"`); the artifact uses `JavaPlatform.Current`'s tokens in a valid Temurin-shaped github.com URL, with the SHA-256 and size computed in the test.
- First call: one request, returns `{root}/{key}/bin/java[.exe]`; the marker exists; `.downloads` and `.staging` are empty.
- Second call: **zero** requests, same path, marker `LastUsedAt` advanced (`FakeTimeProvider`).
- A 302 to `https://release-assets.githubusercontent.com/...` is followed and succeeds (two requests). A 302 to `https://evil.example/x` fails after **one** request; a 302 to `http://release-assets.githubusercontent.com/...` fails; four chained redirects fail.
- Hash mismatch → `Java.HashMismatch`; no `{key}` directory and no partial file remain.
- `Content-Length` different from the pin → `Java.SizeMismatch` with the body unread; a body one byte longer than the pin (no `Content-Length`) → `Java.ArchiveTooLarge`.
- Zip slip: entries `../evil.txt` and `jdk/../../evil.txt` → `Java.CorruptArchive`; no `evil.txt` anywhere under the temp parent.
- Two top-level directories → `Java.UnexpectedLayout`; `JAVA_VERSION="17.0.1"` → `Java.VersionMismatch`.
- `MaxJavaExtractedBytes = 1 MiB` with a 2 MiB entry → `Java.ExtractedTooLarge`.
- A securer that fails → that failure, **zero** requests.
- Two concurrent calls → exactly one download; both return the same path.
- A directory `{key}` without a marker is replaced by a fresh install.

Why they fail when reverted: the stub installer answers `Java.ManagedInstallUnavailable`, so with no override no server can start; each limit test fails if its check is removed.

**Checks (in order).**

```bash
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet build src/Agents/Dhadgar.Agent.Windows/Dhadgar.Agent.Windows.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Agents/Dhadgar.Agent.Core/README.md` "Java runtime": the directory layout (`runtimes/{os}-{arch}-{hash16}`, `.downloads`, `.staging`, marker), the limits, the error codes, and the note that on Windows the download path needs VS-31's ACL securer.

**Done when.** Checks pass.

### VS-22 Server placement: reservation contracts, Nodes client, Minecraft create

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/22-server-placement` / `vertical-slice-vs22` |
| Commit subject | `feat(servers): place Minecraft servers on a node through Nodes capacity reservations [VS-22]` |
| Security review | Yes (forwards user tokens service to service) |
| Size | about 40k tokens |

**Goal.** F-13 and F-19: creating a server reserves and claims capacity on the chosen node through Nodes, with the caller's token; Minecraft is the only game (D-14); the server records EULA acceptance (D-19, decided). Java runtime pins are **not** in this packet: they live in VS-29's `JavaRuntimeCatalog.cs`. Keep `MinecraftRelease.JavaMajorVersion`, which VS-23 uses to look them up, and do not create or edit `Minecraft/JavaRuntimeCatalog.cs`.

**Rules.**
- Seams harness and test JWTs (D-22): mint and accept test user tokens only with `SeamJwt` (`tests/Dhadgar.Testing/Auth/SeamJwt.cs` after VS-32, which removes VS-04's duplicate minter in `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`); never add another minter or `PostConfigure<JwtBearerOptions>` test-key block. Client-certificate shims stay inside the test project that owns them. A seam that asserts a rejection proves the server answered: the status or code comes from the server's response, the forwarder or a backend recorder shows which server it reached, and a positive twin passes (dispatch rules).
- Cross-service types only from `Dhadgar.Contracts` (move the reservation DTOs; delete the local copies).
- The bearer token is forwarded only to `Services:Nodes:BaseUrl`; check the outgoing request host equals that host before attaching it. Never log it.
- Servers returns ProblemDetails with `errorCode`; Nodes errors are mapped, never passed through raw.
- Compensate on failure: a claim failure deletes the new server and releases the reservation.
- No `Version=` in `.csproj`.

**Contract excerpt.** C-4 reservation DTOs and `ClaimReservationRequest.ServerId = serverId.ToString("D")`; C-5 `CreateServerRequest` additions and validation; D-14 catalog.

**Changes.**
1. New `src/Shared/Dhadgar.Contracts/Nodes/ReservationContracts.cs`: move `CreateReservationRequest`, `ClaimReservationRequest`, `ReservationResponse` from `src/Dhadgar.Nodes/Models/ReservationModels.cs` (unchanged, with their DataAnnotations) and `ReservationStatus` from `src/Dhadgar.Nodes/Data/Entities` (same members and values). Fix Nodes usings; `ReservationSummary` and `AvailableCapacityResponse` stay in Nodes.
2. `src/Shared/Dhadgar.Contracts/Servers/ServerRequests.cs`: append `Guid? NodeId = null, string? MinecraftVersion = null, bool EulaAccepted = false` to `CreateServerRequest`, with XML docs.
3. New `src/Dhadgar.Servers/Minecraft/MinecraftCatalog.cs`:

```csharp
public static class MinecraftCatalog
{
    public const string GameType = "minecraft";
    public const string DefaultVersion = "1.21.11";
    public static IReadOnlyDictionary<string, MinecraftRelease> Releases { get; } = new Dictionary<string, MinecraftRelease>
    {
        ["1.21.11"] = new("1.21.11",
            "https://piston-data.mojang.com/v1/objects/64bb6d763bed0a9f1d632ec347938594144943ed/server.jar",
            "64bb6d763bed0a9f1d632ec347938594144943ed", JavaMajorVersion: 21),
    };
}
public sealed record MinecraftRelease(string Version, string ServerJarUrl, string ServerJarSha1, int JavaMajorVersion);
```

4. `src/Dhadgar.Servers/Data/Entities/Server.cs`: add `string MinecraftVersion` (max 32), `int GamePort` (default 25565), `DateTime? EulaAcceptedAt`, `string? EulaAcceptedByUserId` (max 128); configure them in `ServerConfiguration.cs`; migration `dotnet ef migrations add AddMinecraftPlacement --project src/Dhadgar.Servers --startup-project src/Dhadgar.Servers --output-dir Data/Migrations`, then `has-pending-model-changes` reports none.
5. New `src/Dhadgar.Servers/Services/Nodes/INodesClient.cs`, `NodesClient.cs`, `BearerTokenForwardingHandler.cs`: typed HttpClient (base address `Services:Nodes:BaseUrl`, required at startup). Methods return `Result<T>` with failure strings equal to Nodes' `errorCode` (read from the ProblemDetails body) or `nodes_unavailable` on network errors: `ReserveAsync(Guid org, Guid node, CreateReservationRequest)`, `ClaimAsync(Guid token, Guid serverId)`, `ReleaseAsync(Guid token)`, and `DispatchCommandAsync(Guid org, Guid node, DispatchAgentCommandRequest)` (used by VS-23). The handler copies `Authorization` from `IHttpContextAccessor.HttpContext` (absent → no header; Nodes answers 401 → `unauthorized`).
6. `src/Dhadgar.Servers/Validators/CreateServerRequestValidator.cs`: C-5 MVP rules (keep #88's other rules); error codes `unsupported_game_type`, `node_required`, `eula_not_accepted`, `unsupported_minecraft_version`.
7. `src/Dhadgar.Servers/Services/ServerService.cs` create path: after validation, `ReserveAsync(org, nodeId, new CreateReservationRequest(MemoryMb: req.MemoryLimitMb, DiskMb: req.DiskLimitMb, CpuMillicores: req.CpuLimitMillicores, TtlMinutes: 15, RequestedBy: $"servers:{userId}"))`; map `node_not_found` → `invalid_node` (400 at the endpoint), capacity errors → `insufficient_capacity` (409); save the server with `Status = Ready`, `NodeId`, `ReservationToken`, `MinecraftVersion` (or default), `GamePort` (primary port or 25565), `EulaAcceptedAt = now`, `EulaAcceptedByUserId = userId`; `ClaimAsync(token, server.Id)`; on failure delete the server, `ReleaseAsync`, return `insufficient_capacity`. Delete path: release the reservation, best effort.
8. `src/Dhadgar.Servers/Program.cs`: register the typed client and handler; `AddHttpContextAccessor`. `appsettings.json`: `Services:Nodes:BaseUrl` = `http://localhost:5040`.
9. Seams: `Infrastructure/ServersSeamFactory.cs` points `Services:Nodes:BaseUrl` at `http://nodes.seams/` and swaps the typed client's primary handler for the Nodes `TestServer` handler.

**Tests.**
- `tests/Dhadgar.Servers.Tests/Services/ServerPlacementTests.cs` (fake `INodesClient`): create reserves then claims with `serverId.ToString("D")`; `insufficient_capacity` → 409 and no server row; claim failure → server deleted and reservation released; EULA false → 400 `eula_not_accepted`; game type `valheim` → 400.
- `tests/Dhadgar.Servers.Tests/Services/NodesClientTests.cs`: the token is forwarded; a request to another host does not get it; ProblemDetails `errorCode` is surfaced.
- Nodes tests still pass after the DTO move.
- Seam `tests/Dhadgar.Seams.Tests/Servers/ServerPlacementSeamTests.cs`: seed a node with capacity in the Nodes DB for org O (use whatever `NodeCapacity`/`NodeHardwareInventory` rows `CapacityReservationService` needs; read it); `POST /api/v1/organizations/{O}/servers` **through the Gateway** with a `SeamJwt` token for O and a valid Minecraft request; 201; the Nodes DB holds a **Claimed** reservation whose `ServerId` is the new server id. A second request with `EulaAccepted=false` → 400 whose ProblemDetails `errorCode` is `eula_not_accepted` (proof that Servers' validator answered, not the Gateway), the forwarder recorded a `servers` request for that path, and the Nodes DB has no new reservation (dispatch rule on rejection seams). `SeamJwt` from `tests/Dhadgar.Testing` (D-22); no new token helper.

Why they fail when reverted: Servers never calls Nodes, so `NodeId` stays null and no reservation exists.

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Servers/Dhadgar.Servers.csproj -c Release
dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release
dotnet ef migrations has-pending-model-changes --project src/Dhadgar.Servers --startup-project src/Dhadgar.Servers
dotnet test tests/Dhadgar.Servers.Tests/Dhadgar.Servers.Tests.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Dhadgar.Servers/README.md` "MVP: Minecraft placement" (request fields, validation codes, catalog, reservation flow).

**Done when.** Checks pass; `ServerPlacementSeamTests` green.

## Wave 4b: lifecycle through the agent, Windows host, JRE hardening

Base: `origin/main` after all of wave 4. VS-23, VS-21 and VS-31 are dispatched together. Merge order: VS-23, VS-21, VS-31 (VS-31 rebases onto VS-21's `Program.cs`). Wave review after all three reach `ship`.

### VS-23 Server lifecycle: start/stop dispatch and result consumers

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/23-server-lifecycle` / `vertical-slice-vs23` (wave 4b: from `origin/main` after wave 4, which holds VS-20, VS-22 and VS-29) |
| Commit subject | `feat(servers): start and stop Minecraft servers through Nodes and track agent results [VS-23]` |
| Security review | No (dispatch goes through VS-15's signed path; reviewer only) |
| Size | about 40k tokens |

**Goal.** D-16: start and stop go Servers → Nodes → agent and return 202; agent results and status changes move the server's state.

**Rules.**
- Seams harness and test JWTs (D-22): mint and accept test user tokens only with `SeamJwt` (`tests/Dhadgar.Testing/Auth/SeamJwt.cs` after VS-32, which removes VS-04's duplicate minter in `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`); never add another minter or `PostConfigure<JwtBearerOptions>` test-key block. Client-certificate shims stay inside the test project that owns them. A seam that asserts a rejection proves the server answered: the status or code comes from the server's response, the forwarder or a backend recorder shows which server it reached, and a positive twin passes (dispatch rules).
- State changes go through #88's transition table in `ServerLifecycleService`; do not bypass it.
- Consumers scope every lookup by `OrganizationId` from the event and `ServerId`.
- Event fields that came from the agent are untrusted (C-2 "Limit enforcement", `AgentCommandCompleted` row, D-23): check `Status`/`State` with `Enum.IsDefined` before switching on them (an undefined value: log ids only and skip). Never log `ResultJson`, `ErrorMessage` or `ErrorCode` values. If a consumer ever reads `ResultJson`, it parses with `AgentJson.Options` inside a `try` and ignores a body that fails.
- `Result<T>` in services, ProblemDetails at endpoints (C-1 codes).

**Contract excerpt.** `StartServerPayload` (including C-7's `JavaRuntimes`), `StopServerPayload`, `AgentCommandTypes`, `AgentCommandCompleted`, `AgentServerStatusChanged`, `AgentCommandStatus`, `AgentServerState`, `AgentJson` (C-2); `DispatchAgentCommandRequest` (C-4); start/stop rows (C-1, C-5).

**Changes.**
1. `src/Dhadgar.Servers/Services/ServerLifecycleService.cs`:
   - `StartServerAsync`: existing state checks and `server_not_placed`; set `Starting`/`PowerState.Starting`; save; payload `new StartServerPayload(server.Id, server.MinecraftVersion, release.ServerJarUrl, release.ServerJarSha1, server.MemoryLimitMb, server.GamePort, server.EulaAcceptedAt is not null, JavaRuntimeCatalog.ForMajorVersion(release.JavaMajorVersion))` from `MinecraftCatalog` and VS-29's `JavaRuntimeCatalog`. An empty runtime list is still sent (an agent with `Agent:Minecraft:JavaPath` needs none); `DispatchCommandAsync(org, node, new DispatchAgentCommandRequest(AgentCommandTypes.StartServer, JsonSerializer.Serialize(payload, AgentJson.Options), server.Id, correlationId))`. On `node_offline` (or any failure) restore the previous status and power state, save, and return `Failure("node_offline")` (or the code).
   - `StopServerAsync`: from `Running`/`Starting` → `Stopping`; dispatch `StopServer` with `GracefulTimeoutSeconds = 60`; same compensation.
   - `RestartServerAsync`, `KillServerAsync`: return `Failure("not_supported_in_mvp")`.
2. `src/Dhadgar.Servers/Endpoints/ServerLifecycleEndpoints.cs`: success → `Results.Accepted()`; `server_not_found` → 404; `node_offline`, `not_supported_in_mvp` and `cannot_*_from_status_*` → 409 (`invalid_state` for the last group, with the original code in `detail`); Nodes' `server_not_on_node` (C-1, VS-15) → 409 `invalid_state` with that code in `detail`; others → 400.
3. New `src/Dhadgar.Servers/Consumers/AgentCommandCompletedConsumer.cs`: ignore events without `ServerId` or for other command types. `StartServer`: `Succeeded` → `Running`, `PowerState.On`, `LastStartedAt`; `Failed`/`Rejected`/`TimedOut`/`Cancelled` → `Error`, `PowerState.Off`. `StopServer`: `Succeeded` → `Stopped`, `PowerState.Off`, `LastStoppedAt`, add uptime; otherwise `Error`. Use `TransitionStatusAsync`/`TransitionPowerStateAsync` where the table allows; log and skip a disallowed transition.
4. New `src/Dhadgar.Servers/Consumers/AgentServerStatusChangedConsumer.cs`: `Crashed` → `Crashed` (`CrashCount++`); `Stopped` while `Running` → `Stopped`; `Running` while `Starting` → `Running`.
5. `src/Dhadgar.Servers/Program.cs`: `x.AddConsumer<...>()` for both.

**Tests.**
- `tests/Dhadgar.Servers.Tests/Services/ServerLifecycleDispatchTests.cs` (fake `INodesClient`): start sends `StartServer` with the catalog SHA-1, the server's port and memory, and `JavaRuntimes` equal to `JavaRuntimeCatalog.ForMajorVersion(21)`; every `MinecraftCatalog` release's `JavaMajorVersion` has at least one runtime; `node_offline` → status back to `Ready`, endpoint 409; stop sends `StopServer`.
- `tests/Dhadgar.Servers.Tests/Consumers/AgentCommandCompletedConsumerTests.cs` (MassTransit test harness): `Starting` + `Succeeded` → `Running`; `Starting` + `TimedOut` → `Error`; an event for another org's server id changes nothing.
- `Consumers/AgentServerStatusChangedConsumerTests.cs`: `Running` + `Crashed` → `Crashed`, crash count 1.
- Undefined enum values: an `AgentCommandCompleted` with `Status = (AgentCommandStatus)42` and an `AgentServerStatusChanged` with `State = (AgentServerState)42` change nothing.
- Update #88's lifecycle endpoint tests for 202 and the MVP 409s.
- Seam `tests/Dhadgar.Seams.Tests/Servers/ServerLifecycleSeamTests.cs`: Gateway + Servers + Nodes + the real agent host (`SeamAgentHost` with `FakeMinecraftServerHost`, the fake installer and VS-20's `FakeJavaRuntimeResolver`; the fake records the payload, and the test asserts that it received the two catalog runtimes intact through the signed path). Create a server through the Gateway (as in VS-22); `POST .../start` through the Gateway → 202; wait for Nodes' `CapturingPublishEndpoint` to hold `AgentCommandCompleted` `Succeeded` for that server; publish it to the Servers harness bus; `GET` the server through the Gateway shows `Running`. Then stop → 202 → `Stopped`. A third test (rejection, dispatch rule): no agent connected → start → 409 whose `errorCode` is `node_offline`, status still `Ready`, **and** a recording `DelegatingHandler` around the Nodes handler that `ServersSeamFactory` injects saw the `POST /internal/organizations/{O}/nodes/{node}/commands` call, so the refusal came from Nodes' registry check and not from Servers short-circuiting.

Why they fail when reverted: start only flips a status locally and never reaches the agent; nothing consumes results.

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Servers/Dhadgar.Servers.csproj -c Release
dotnet test tests/Dhadgar.Servers.Tests/Dhadgar.Servers.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Dhadgar.Servers/README.md` "Lifecycle" (202 semantics, events consumed, MVP limits).

**Done when.** Checks pass; `ServerLifecycleSeamTests` green.

### VS-21 Windows Minecraft host (wrapper service, pipe, stop via stdin)

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/21-mc-windows-host` / `vertical-slice-vs21` (wave 4b: from `origin/main` after wave 4) |
| Commit subject | `feat(agent-windows): isolated Minecraft host via GameServerWrapper service and agent pipe [VS-21]` |
| Security review | Yes (`WindowsServiceManager`, process isolation) |
| Status | D-17 decided A (isolated service) and D-19 decided (ruling 1). The JRE is resolved before this host is called (VS-20, VS-29, VS-30); this packet only runs what it is given. |
| Size | about 40k tokens; behaviour verified only on Windows |

**Goal.** The Windows implementation of `IMinecraftServerHost`: each server runs as its own Windows service hosting GameServerWrapper under a virtual service account; output and stdin travel over `AgentPipeServer`; stop sends `stop` on stdin and waits.

**Rules.**
- `ArgumentList`, never a concatenated command line. GameServerWrapper's `ServerConfig.Arguments` is a string, so add `IReadOnlyList<string>? ArgumentList` to `ServerConfig` and make the wrapper prefer it (adding each item to `ProcessStartInfo.ArgumentList`); keep `Arguments` for compatibility.
- Paths through `IPathValidator` (or the existing validation in `ServerConfig.Validate` and `DirectoryAclManager`, which already restricts roots).
- Every `Process` and service handle in `using`; `Result<T>` for every failure.
- `AgentPipeServer` must not be created before the agent has a NodeId (F-12).
- The host never resolves Java and never reads `Agent:Minecraft:JavaPath`; it uses the `javaExecutablePath` argument (VS-20's signature). Add no ACE for the JRE directory: VS-31's DACL already gives Users and SERVICE read and execute, and the game-server accounts must never get write access there.
- Windows-only tests are `[SkippableFact]` with `Skip.IfNot(OperatingSystem.IsWindows())`.

**Changes.**
1. `src/Agents/Dhadgar.Agent.GameServerWrapper/ServerConfig.cs` and `GameServerHostedService.cs`: the `ArgumentList` addition above.
2. `src/Agents/Dhadgar.Agent.Windows/Program.cs`: replace the `IAgentPipeServer` singleton factory with a new `IPC/AgentPipeServerProvider.cs` singleton exposing `Task<Result<IAgentPipeServer>> GetOrStartAsync(CancellationToken ct)`. It returns `Failure("[Pipe.NotEnrolled] ...")` while `IOptionsMonitor<AgentOptions>.CurrentValue.NodeId` is null, and otherwise creates the `AgentPipeServer` with that NodeId, starts it once and caches it. Switch every consumer of `IAgentPipeServer` (grep) to the provider. Register `IMinecraftServerHost` → `WindowsMinecraftServerHost`.
3. New `src/Agents/Dhadgar.Agent.Windows/Minecraft/WindowsMinecraftServerHost.cs`:
   - `StartAsync(installation, javaExecutablePath, payload, ct)`: write `ServerConfig` (`ExecutablePath = javaExecutablePath` (must be fully qualified and exist, else `[Minecraft.JavaMissing]` before any service call), `ArgumentList = installation.JavaArguments`, `WorkingDirectory`, `RedirectStdin = true`, `CaptureStdout/Stderr = true`, `MemoryLimitMb = payload.MemoryMb + 512`, `GracefulShutdownTimeoutSeconds = 60`) with `ServerConfig.SaveToFile` into the server directory as `wrapper.json`; `pipe = provider.GetOrStart()`; create the service if `!ServiceExists(serverId)` with `CreateGameServerServiceAsync(new GameServerServiceConfig { ... wrapper path from ProcessOptions.GameServerWrapperPath, args --server-id, --pipe, --config })` (read `GameServerServiceConfig` for the exact properties); `pipe.RegisterServer(serverId, serviceAccountName)`; `StartServiceAsync`; wait up to 60 s for a pipe `StatusChanged` to `Running`; return `ServerStatusResult`.
   - `StopAsync`: `pipe.SendInputAsync(serverId, "stop")`; wait for status `Stopped` up to the timeout; then `StopServiceAsync(serverId, 30 s)`; return the final status.
   - `GetStatus`/`GetAll`: from the last pipe status per server.
   - Forward `AgentPipeServer.OutputReceived` and `StatusChanged` as `OutputReceived` and `StatusChanged` (map `GameServerState` to `AgentServerState`).
4. `src/Agents/Dhadgar.Agent.Windows/appsettings.json`: add an `Agent:Minecraft` block with `JavaPath` empty, `JavaDownloadEnabled` true and the default `AllowedDownloadHosts`, `JavaDownloadHosts` and `JavaRedirectHosts` (values equal to the option defaults in VS-19 and VS-29; leave `JavaRuntimePath` out so the default applies), and make sure `Agent:Process:GameServerWrapperPath` and `ServerBasePath` are present (empty values with comments are not possible in JSON, so document them in the README).

**Tests** (`tests/Dhadgar.Agent.Windows.Tests/Minecraft/WindowsMinecraftServerHostTests.cs`), with NSubstitute fakes for `IWindowsServiceManager` and the pipe provider so they run on Linux: start creates the service once, registers the pipe and returns Running when the fake pipe raises Running; stop sends `stop` before `StopServiceAsync`; a `javaExecutablePath` that does not exist fails with `Minecraft.JavaMissing` before any service call; the provider returns `Pipe.NotEnrolled` while NodeId is null. No real-service integration test is required here; the owner's manual run in VS-27 covers real behaviour. `tests/Dhadgar.Agent.GameServerWrapper.Tests`: `ArgumentList` reaches `ProcessStartInfo.ArgumentList` unchanged, including an item with spaces.

**Checks (in order).**

```bash
dotnet build src/Agents/Dhadgar.Agent.Windows/Dhadgar.Agent.Windows.csproj -c Release
dotnet build src/Agents/Dhadgar.Agent.GameServerWrapper/Dhadgar.Agent.GameServerWrapper.csproj -c Release
dotnet test tests/Dhadgar.Agent.Windows.Tests/Dhadgar.Agent.Windows.Tests.csproj -c Release
dotnet test tests/Dhadgar.Agent.GameServerWrapper.Tests/Dhadgar.Agent.GameServerWrapper.Tests.csproj -c Release
```

Verification on Windows: the report states that real service creation, the pipe and `stop` were **not** exercised (Linux), and VS-27's runbook carries the manual steps.

**Docs owed.** `src/Agents/Dhadgar.Agent.Windows/README.md` "Minecraft servers" (service per server, account, directories, wrapper path, how stop works, and Java: managed Temurin JRE under `%ProgramData%\Meridian Console\Agent\runtimes`, or the `Agent:Minecraft:JavaPath` override, which wins; outbound HTTPS to `github.com`, `release-assets.githubusercontent.com` and `piston-data.mojang.com`). `Program.cs` merge note: VS-31 also adds a registration there and rebases onto this packet.

**Done when.** Checks pass; status stays "code exists" until the owner records the manual run.

### VS-31 JRE hardening: Windows ACL securer and runtime pruning

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/31-java-runtime-hardening` / `vertical-slice-vs31` (wave 4b: from `origin/main` after wave 4; rebase onto VS-21 before merge) |
| Commit subject | `feat(agent): protected ACL on the JRE directory and pruning of unused runtimes (D-19) [VS-31]` |
| Security review | Yes (Windows ACLs; deletes directories under the agent data dir) |
| Size | about 25k tokens; ACL behaviour verified only on Windows |

**Goal.** C-7 rules 7 and 8. On Windows the JRE directory gets a protected DACL, so game-server accounts (and any local user, via the inherited `ProgramData` "create files" right) cannot plant DLLs or replace `java.exe`. Old runtimes are removed once nothing uses them.

**Rules.**
- `Result` for every failure. Catch `UnauthorizedAccessException`, `PrivilegeNotHeldException` and `IOException`; never throw out of `Secure` or `RunAsync`.
- Paths: everything the pruner touches goes through `IPathValidator.ValidatePath(path, [runtimeRoot])`. Only directories whose name matches `^(windows|linux)-(x64|aarch64)-[0-9a-f]{16}$`, plus `.trash`, `.staging` and `.downloads` entries, may be moved or deleted.
- Never delete a runtime in place: rename it into `.trash` first. On Windows a running `java.exe` makes the rename fail, which is the in-use test.
- Windows-only code is `[SupportedOSPlatform("windows")]`. Windows-only tests are `[SkippableFact]` with `Skip.IfNot(OperatingSystem.IsWindows())`, and the ACL-apply test also needs elevation (`Skip.IfNot(IsElevated())`), because setting the owner to SYSTEM needs SeRestorePrivilege.
- `using` on every disposable; no `Version=` in `.csproj`; warnings are errors.

**Contract excerpt.** C-7 rules 7 and 8; VS-30's `IJavaRuntimeDirectorySecurer`, `IJavaRuntimeMaintenance`, `JavaRuntimeMarker`; `JavaRuntimeOptions.JavaRuntimeRetentionDays` (VS-29).

**Changes.**
1. New `src/Agents/Dhadgar.Agent.Windows/Security/WindowsJavaRuntimeDirectorySecurer.cs`:

```csharp
[SupportedOSPlatform("windows")]
public sealed class WindowsJavaRuntimeDirectorySecurer : IJavaRuntimeDirectorySecurer
{
    // Owner SYSTEM; protected DACL; SYSTEM and Administrators full control; Users and SERVICE read and execute.
    public const string Sddl = "O:SYD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)(A;OICI;0x1200a9;;;SU)";

    public Result Secure(string runtimeRoot)
    {
        // var ds = new DirectorySecurity(); ds.SetSecurityDescriptorSddlForm(Sddl);
        // new DirectoryInfo(runtimeRoot).SetAccessControl(ds);
        // read back with GetAccessControl(): AreAccessRulesProtected must be true, and no Allow rule may grant
        // WriteData, AppendData, WriteAttributes, WriteExtendedAttributes, Delete, DeleteSubdirectoriesAndFiles,
        // ChangePermissions or TakeOwnership to any SID other than S-1-5-18 or S-1-5-32-544
        // → otherwise Failure("[Java.DirectoryNotSecured] ...")
    }
}
```

   `src/Agents/Dhadgar.Agent.Windows/Program.cs`, after `AddAgentCore()`: `builder.Services.Replace(ServiceDescriptor.Singleton<IJavaRuntimeDirectorySecurer, WindowsJavaRuntimeDirectorySecurer>());`.
2. New `src/Agents/Dhadgar.Agent.Core/Minecraft/Java/JavaRuntimePruner.cs : IJavaRuntimeMaintenance` (`IPathValidator`, `IOptionsMonitor<JavaRuntimeOptions>`, `TimeProvider`, logger). `RunAsync(root, keepKey, ct)` returns at once if it last ran less than 1 h ago or `JavaRuntimeRetentionDays == 0`. Otherwise, for each runtime directory other than `keepKey` whose marker `LastUsedAt` is older than the retention (or whose marker is missing or corrupt and whose directory is older than the retention), `Directory.Move(dir, root/.trash/{name}-{Guid:N})`; an `IOException` or `UnauthorizedAccessException` logs "runtime in use, skipped". Then delete everything in `.trash`, logging failures (they are retried on the next run), and delete `.staging` and `.downloads` entries older than 1 h. Register it in `AddAgentCore` in place of VS-30's no-op.

**Tests.**
- `tests/Dhadgar.Agent.Windows.Tests/Security/WindowsJavaRuntimeDirectorySecurerTests.cs`:
  - Runs on Linux: parse the `Sddl` constant's ACEs with a regex (`\((A|D);([A-Z]*);([^;]*);;;([A-Z]{2}|S-[0-9-]+)\)`). Assert that it starts with `O:SY`, that the DACL is protected (`D:P`), and that only `SY` and `BA` hold rights other than `0x1200a9`. Adding `(A;OICI;FA;;;BU)` must fail this test.
  - Windows and elevated: apply to a temp directory and read it back. Rules are protected, Users rights equal `ReadAndExecute | Synchronize`, and a child file created afterwards inherits no write ACE for Users.
- `tests/Dhadgar.Agent.Core.Tests/Minecraft/Java/JavaRuntimePrunerTests.cs` (`FakeTimeProvider`, temp root, markers written with `JavaRuntimeMarker.Write`):
  - a runtime last used 8 days ago is removed;
  - `keepKey` is kept even when old;
  - a runtime used 1 day ago is kept;
  - retention 0 removes nothing;
  - a directory `notes` and a file `readme.txt` are untouched;
  - a second call within 1 h does nothing (an old runtime created between the calls survives);
  - a `.staging/x` 2 h old is removed.
  - Windows-only `[SkippableFact]`: with a `FileStream` held open on `bin/java.exe` (`FileShare.Read`), the old runtime is skipped and still has its marker.

Why they fail when reverted: on Windows `DefaultJavaRuntimeDirectorySecurer` refuses (`Java.DirectoryNotSecured`), so the managed JRE cannot be used; without the pruner every Temurin update leaves about 150 MB behind.

**Checks (in order).**

```bash
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet build src/Agents/Dhadgar.Agent.Windows/Dhadgar.Agent.Windows.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
dotnet test tests/Dhadgar.Agent.Windows.Tests/Dhadgar.Agent.Windows.Tests.csproj -c Release
```

Verification on Windows: the report states that the ACL was **not** applied for real (Linux). VS-27's runbook carries the `icacls` check.

**Docs owed.** `src/Agents/Dhadgar.Agent.Windows/README.md` "Java runtime directory" (the DACL in words and SDDL, why `ProgramData`'s inherited ACL is not enough, the `icacls` check). `src/Agents/Dhadgar.Agent.Core/README.md` "Java runtime" (upgrade and cleanup behaviour, `JavaRuntimeRetentionDays`).

**Done when.** Checks pass; status stays "code exists" for the ACL until the owner records the runbook check.

---

## Wave 5: Panel, deployment, close-out

Base: `origin/main` after all of wave 4. **Ask D-18 before VS-27.** Stage 1: VS-24, VS-27. Stage 2: VS-25 (after VS-24). Stage 3: VS-26 (after VS-25). VS-28 runs after the campaign review loop, last. Merge order: VS-24, VS-27, VS-25, VS-26, then the campaign-review fix PR, then VS-28. The three Panel packets edit `src/lib/api/routes.ts` and `types.ts` in sequence, which is why they are staged.

Panel rules shared by VS-24..VS-26 (also repeated in each packet):
- TypeScript strict, React function components, Tailwind classes in the style of `src/components/dashboard/DashboardContent.tsx` and `src/components/ui/*`.
- Every API call goes through `apiClient` in `src/lib/auth/api.ts`; every path comes from `src/lib/api/routes.ts`; never build a URL elsewhere.
- Never store tokens yourself; `authClient.getAccessToken()` is the only source.
- No new npm dependency except `@microsoft/signalr` in VS-26. Keep `package-lock.json` consistent (`npm install` updates it; commit both).
- Keep each file's line endings (Panel `.ts`/`.tsx` files are CRLF).
- Checks: `cd src/Dhadgar.Panel && npm ci --no-audit --no-fund && npm run build`, then `dotnet test tests/Dhadgar.Gateway.Tests/Dhadgar.Gateway.Tests.csproj -c Release`.
- Verification of behaviour in a browser is part of the owner's VS-27 runbook; the report says the page was built but not clicked through.

### VS-24 Panel `/nodes` page and route contract test

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/24-panel-nodes` / `vertical-slice-vs24` |
| Commit subject | `feat(panel): nodes page with enrollment tokens; Panel-to-Gateway route contract test [VS-24]` |
| Security review | No |
| Size | about 35k tokens |

**Goal.** F-23: the `/nodes` page lists the org's nodes and creates enrollment tokens. A Gateway test pins every Panel path to a real Gateway route.

**Rules.**
- TypeScript strict, React function components, Tailwind classes in the style of `src/components/dashboard/DashboardContent.tsx` and `src/components/ui/*`.
- Every API call goes through `apiClient` in `src/lib/auth/api.ts`; every path comes from `src/lib/api/routes.ts`.
- Never store tokens yourself; `authClient.getAccessToken()` is the only source.
- No new npm dependency (except `@microsoft/signalr`, VS-26 only); commit `package-lock.json` with `package.json`.
- Keep each file's line endings (Panel `.ts`/`.tsx` files are CRLF).
- Behaviour in a browser is verified by the owner's VS-27 runbook; say in the report that the page was built, not clicked through.

**Contract excerpt.** Rows `nodes-org-route` and `enrollment-route` (C-1); `NodeListItemResponse`/`PagedNodesResponse` in `src/Shared/Dhadgar.Contracts/Nodes/NodeResponses.cs`; `CreateEnrollmentTokenRequest(string? Label, int? ExpiresInMinutes)` and `CreateEnrollmentTokenResponse(string TokenId, string Token, DateTimeOffset ExpiresAt)` (JSON camelCase).

**Changes.**
1. New `src/Dhadgar.Panel/src/lib/api/routes.ts`:

```typescript
export const routes = {
  nodes: (org: string) => `/api/v1/organizations/${org}/nodes`,
  node: (org: string, nodeId: string) => `/api/v1/organizations/${org}/nodes/${nodeId}`,
  enrollmentTokens: (org: string) => `/api/v1/organizations/${org}/enrollment/tokens`,
};
export const hubs = { console: '/hubs/console' };
```

2. New `src/lib/api/session.ts`: `export async function getOrganizationId(): Promise<string | null>` decodes the access token payload (base64url JSON) and returns `org_id`, or null.
3. New `src/lib/api/types.ts`: TypeScript interfaces mirroring the C# records above (field for field, camelCase).
4. New `src/pages/nodes/index.astro` (uses `DashboardLayout`, mounts the React island with `client:load`) and `src/components/nodes/NodesPage.tsx`: loading and error states; no `org_id` → a message "Your account has no organization yet"; a table with name, status, platform, last heartbeat (relative time); "Add node" → `POST enrollmentTokens(org)` with `{ label, expiresInMinutes: 60 }` → a dialog showing the token **once** with a copy button, its expiry, and the text "Install the Windows agent and give it this token; see docs/runbooks/vertical-slice.md". Refresh the list every 15 s.
5. `src/layouts/DashboardLayout.astro`: remove the `/settings` link (no such page in the MVP).
6. New `tests/Dhadgar.Gateway.Tests/PanelRouteContractTests.cs`: locate `src/Dhadgar.Panel/src/lib/api/routes.ts` the way `RouteConfigurationTests.GetGatewayAppSettingsPath` locates `appsettings.json`; extract every template literal starting with `` `/api/v1/ `` (regex), replace each `${...}` with a new GUID, and send `GET` through `GatewayWebApplicationFactory` with no auth. Every path must answer **401** (a route exists and is protected); a 404 fails the test naming the path. Also assert `hubs.console` (`/hubs/console/negotiate`) answers 401.

**Tests.** `PanelRouteContractTests`: it fails when a Panel path has no Gateway route (404), which is how the Panel and the Gateway drift.

**Checks.** The shared Panel checks.

**Docs owed.** `src/Dhadgar.Panel/README.md` "Pages" (nodes) and "API paths live in `src/lib/api/routes.ts`; `PanelRouteContractTests` pins them".

**Done when.** Build and Gateway tests pass.

### VS-25 Panel `/servers` page: list, create, start, stop

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/25-panel-servers` / `vertical-slice-vs25` (from `origin/main` after VS-24) |
| Commit subject | `feat(panel): servers page to create, start and stop Minecraft servers [VS-25]` |
| Security review | No |
| Size | about 35k tokens |

**Goal.** The `/servers` page: list servers, create a Minecraft server on a node, start and stop it (D-16 async states, D-19 EULA checkbox).

**Rules.**
- TypeScript strict, React function components, Tailwind classes in the style of `src/components/dashboard/DashboardContent.tsx` and `src/components/ui/*`.
- Every API call goes through `apiClient` in `src/lib/auth/api.ts`; every path comes from `src/lib/api/routes.ts`.
- Never store tokens yourself; `authClient.getAccessToken()` is the only source.
- No new npm dependency (except `@microsoft/signalr`, VS-26 only); commit `package-lock.json` with `package.json`.
- Keep each file's line endings (Panel `.ts`/`.tsx` files are CRLF).
- Behaviour in a browser is verified by the owner's VS-27 runbook; say in the report that the page was built, not clicked through.

**Contract excerpt.** `servers-org-route` rows and start/stop codes (C-1); C-5 (`CreateServerRequest` with `nodeId`, `minecraftVersion`, `eulaAccepted`; 202; 409 `node_offline`/`invalid_state`); `ServerResponse` and list types in `src/Shared/Dhadgar.Contracts/Servers/ServerResponses.cs`.

**Changes.**
1. `src/lib/api/routes.ts`: add `servers(org)`, `server(org, id)`, `serverStart(org, id)`, `serverStop(org, id)` (`/api/v1/organizations/${org}/servers[/${id}[/start|/stop]]`). `types.ts`: server types and the create request.
2. New `src/pages/servers/index.astro` and `src/components/servers/ServersPage.tsx`:
   - a table: name, status badge (`Ready`, `Starting`, `Running`, `Stopping`, `Stopped`, `Crashed`, `Error`), node name (from the nodes list), game port;
   - a "New server" form: name, node (dropdown of the org's nodes, online ones first), memory MB (default 2048, 1024..16384), a required checkbox "I accept the Minecraft EULA" linking to `https://aka.ms/MinecraftEULA`; submits `gameType: "minecraft"`, `diskLimitMb: 10240`, `cpuLimitMillicores: 0`, `autoStart: false`, `autoRestartOnCrash: false`, `eulaAccepted: true`; shows `errorCode` messages (`insufficient_capacity`, `invalid_node`, `eula_not_accepted`);
   - Start/Stop buttons by state; 202 → poll `server(org, id)` every 3 s until the status leaves `Starting`/`Stopping` (stop polling after 7 min, just past Nodes' command timeout of `ExpiresAt` + 5 min, so the final `Error` from a timed-out start is shown); 409 `node_offline` → "The node is offline". While a server is `Starting`, show "The first start on a node downloads Java and the Minecraft server (about 100 MB) and can take a few minutes." (D-19 ruling 1, F-28).
   - Each row links to `/servers/{id}` (VS-26).
3. `PanelRouteContractTests` covers the new paths automatically. `POST`-only paths (start, stop) are probed with `GET`; YARP still matches the route and auth still answers 401, which is what the test checks.

**Checks.** The shared Panel checks.

**Docs owed.** `src/Dhadgar.Panel/README.md` "Pages" (servers).

**Done when.** Build and Gateway tests pass.

### VS-26 Panel console tail

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/26-panel-console` / `vertical-slice-vs26` (from `origin/main` after VS-25) |
| Commit subject | `feat(panel): live console tail on the server page via the Console hub [VS-26]` |
| Security review | No |
| Size | about 30k tokens |

**Goal.** The last step of D-01: the server page shows recent history and live output.

**Rules.**
- TypeScript strict, React function components, Tailwind classes in the style of `src/components/dashboard/DashboardContent.tsx` and `src/components/ui/*`.
- Every API call goes through `apiClient` in `src/lib/auth/api.ts`; every path comes from `src/lib/api/routes.ts`.
- Never store tokens yourself; `authClient.getAccessToken()` is the only source.
- No new npm dependency (except `@microsoft/signalr`, VS-26 only); commit `package-lock.json` with `package.json`.
- Keep each file's line endings (Panel `.ts`/`.tsx` files are CRLF).
- Behaviour in a browser is verified by the owner's VS-27 runbook; say in the report that the page was built, not clicked through.

**Contract excerpt.** C-5 console hub (`JoinServer(JoinServerRequest { serverId, organizationId, historyLines })`, events `history`, `output`, `joined`, `error`); `ConsoleHistoryDto`, `ConsoleOutputDto`, `ConsoleLine` in `src/Shared/Dhadgar.Contracts/Console/ConsoleContracts.cs`; route `console-hub-route`; the Gateway reads the token from `access_token` on `/hubs/console` (VS-04).

**Changes.**
1. `package.json`: add `@microsoft/signalr` `^10.0.11` (`npm install @microsoft/signalr@^10.0.11`), commit `package-lock.json`.
2. New `src/pages/servers/[id].astro` (the Panel builds with `output: 'server'`, so dynamic routes work) and `src/components/servers/ServerDetail.tsx`: server summary with the Start/Stop controls reused from VS-25 (extract them into `src/components/servers/ServerControls.tsx`), and `src/components/console/ConsoleTail.tsx`:

```typescript
const connection = new HubConnectionBuilder()
  .withUrl(`${GATEWAY_URL}${hubs.console}`, { accessTokenFactory: async () => (await authClient.getAccessToken()) ?? '' })
  .withAutomaticReconnect()
  .build();
connection.on('history', (h: ConsoleHistoryDto) => setLines(h.lines.map(toLine)));
connection.on('output', (o: ConsoleOutputDto) => append(toLine(o)));
connection.on('error', (message: string) => setError(message));
await connection.start();
await connection.invoke('JoinServer', { serverId, organizationId, historyLines: 200 });
// on unmount: invoke('LeaveServer', { serverId }) then stop()
```

   Keep at most 1,000 lines; monospace; stderr lines tinted; auto-scroll unless the user scrolled up; show the connection state. Export `GATEWAY_URL` from `src/lib/auth/api.ts` instead of duplicating it.
   Console text is agent-originated and untrusted (C-2 `AgentConsoleLine` remarks; D-23): render each line only as a React text child. Never use `dangerouslySetInnerHTML`, `innerHTML` or a markdown or HTML renderer. Before display, strip ANSI and OSC escape sequences and other C0 control characters except tab (one exported `sanitizeConsoleText(text: string): string` in `src/components/console/sanitize.ts`, regex `/\x1b\[[0-9;?]*[ -\/]*[@-~]|\x1b\][^\x07\x1b]*(\x07|\x1b\\)|[\x00-\x08\x0b-\x1f\x7f]/g` → `''`). There is no JS test runner (F-23), so the report shows the function's output for one ANSI-coloured line and one `<script>` line, run once with `node --experimental-strip-types` (Node 22.6 or later) or by pasting the function body into `node -e`.

**Checks.** The shared Panel checks (`PanelRouteContractTests` already covers `/hubs/console`).

**Docs owed.** `src/Dhadgar.Panel/README.md` "Pages" (server detail and console).

**Done when.** Build and Gateway tests pass.

### VS-27 Compose end to end and the vertical-slice runbook

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/27-compose-runbook` / `vertical-slice-vs27` |
| Commit subject | `feat(deploy): compose configuration and runbook for the vertical slice [VS-27]` |
| Security review | Yes (secrets, exposed ports, TLS client-certificate listener) |
| Status | **BlockedDecision D-18.** Spec follows option A (database per service); option B means re-spec. |
| Size | about 35k tokens |

**Goal.** "Deployable via docker-compose.services.yml + Windows agent": compose carries every setting the campaign added, and one runbook takes an operator from a clean machine to a running, tailed Minecraft server with a recorded manual check.

**Rules.**
- No secrets in committed files: every secret is `${VAR:?message}` in compose and documented in `deploy/compose/.env.example` (create it if missing) with a placeholder.
- Nodes is not published on a host port (D-06). Other internal services may stay published for debugging, as today.
- Compose files are LF. `docker compose ... config -q` must pass with dummy values.
- No Docker daemon here: the packet cannot run the stack. The runbook's manual check table is how "confirmed working" gets recorded later, by the owner.

**Changes.**
1. New `deploy/compose/postgres-init/01-create-databases.sql`: `CREATE DATABASE dhadgar_nodes; CREATE DATABASE dhadgar_servers; CREATE DATABASE dhadgar_console;` (owner `dhadgar`). Mount it in `deploy/compose/docker-compose.dev.yml` on the `postgres` service at `/docker-entrypoint-initdb.d:ro`. Note in the runbook that it runs only on a fresh volume.
2. `deploy/compose/docker-compose.services.yml`:
   - `gateway`: replace `ASPNETCORE_URLS` with `Kestrel__Endpoints__Https__Url: https://+:443`, `Kestrel__Endpoints__Http__Url: http://+:8080`, `Kestrel__Endpoints__Agents__Url: https://+:8443`, `Kestrel__Endpoints__Agents__ClientCertificateMode: AllowCertificate` (keep the default certificate path/key env); add `AgentIngress__ForwardedCertificateSecret: ${AGENT_FORWARDING_SECRET:?AGENT_FORWARDING_SECRET must be set}`, `Auth__MetadataAddress: http://identity:8080/.well-known/openid-configuration`, `Auth__InternalBaseUrl: http://identity:8080`; publish `"8443:8443"`.
   - `nodes`: own connection string `Database=dhadgar_nodes`; `Auth__Issuer`, `Auth__Audience`, `Auth__MetadataAddress`, `Auth__InternalBaseUrl` (Gateway values); `Mtls__ForwardedCertificate__Enabled: "true"`, `Mtls__ForwardedCertificate__SharedSecret: ${AGENT_FORWARDING_SECRET:?...}`; `Nodes__CaStoragePath: /var/lib/dhadgar/nodes-ca` on a named volume `nodes_ca`; **remove** `ports`.
   - `servers`: `Database=dhadgar_servers`; `Auth__*`; `Services__Nodes__BaseUrl: http://nodes:8080`.
   - `console`: `Database=dhadgar_console`; `Auth__*`; `Services__Servers__BaseUrl: http://servers:8080`; `ConnectionStrings__Redis: redis:6379,password=dhadgar` (match the redis service; read `docker-compose.dev.yml`); depends on redis.
   - Mirror the same settings in `docker-compose.services.acr.yml`.
3. `deploy/compose/.env.example`: `POSTGRES_PASSWORD`, `RABBITMQ_PASSWORD`, `AGENT_FORWARDING_SECRET` (with "at least 32 random characters; `openssl rand -base64 48`"), `BETTERAUTH_EXCHANGE_PUBLIC_KEY`, and the others the compose file already requires.
4. New `docs/runbooks/vertical-slice.md`:
   1. Prerequisites: Docker with compose; a host entry `dev.meridianconsole.com` → the Docker host; TLS cert and key in `deploy/certs` (names the compose file expects); a trusted CA on the Windows machine for that cert; BetterAuth exchange key pair (point at the existing key script or docs); `.env` from `.env.example`.
   2. Bring-up: `docker compose -f deploy/compose/docker-compose.dev.yml -f deploy/compose/docker-compose.services.yml up -d --build`; what healthy looks like (`/healthz` through the Gateway); a fresh-volume note for the init script.
   3. Login in the Panel (`http://localhost:4321`), and what to do if the account has no organization.
   4. Nodes page → Add node → copy the token.
   5. Windows agent: `dotnet publish src/Agents/Dhadgar.Agent.Windows -c Release -r win-x64` and the same for `Dhadgar.Agent.GameServerWrapper`; copy to the Windows machine; `appsettings.json` keys `Agent:ControlPlane:Endpoint=https://dev.meridianconsole.com:8443`, `Agent:Process:ServerBasePath`, `Agent:Process:GameServerWrapperPath`, `Agent:Process:UseServiceIsolation=true` (D-17 A), `Agent:StatePath`; Java: nothing to install by default, since the agent downloads the pinned Temurin 21 JRE on the first start into `%ProgramData%\Meridian Console\Agent\runtimes` (D-19). Allow outbound HTTPS from the agent to `github.com`, `release-assets.githubusercontent.com` and `piston-data.mojang.com`. `Agent:Minecraft:JavaPath` is an optional override: when set, the agent uses that `java.exe` and downloads no JRE; enrollment token in `HKLM\SOFTWARE\Meridian Console\Agent\EnrollmentToken` or `Agent:EnrollmentToken`; install and start the service (use the existing installer docs in `src/Agents/Dhadgar.Agent.Windows/README.md`, or `sc.exe create DhadgarAgent binPath= "<path>\Dhadgar.Agent.Windows.exe" start= auto` as a fallback); open the game port in Windows Firewall if the installer does not.
   6. Servers page → New server (EULA checkbox) → Start → wait for Running (the first start downloads about 100 MB) → open the server → console shows the Minecraft startup log → Stop → Stopped and "Saving chunks" in the console. Then, on the Windows machine: `icacls "%ProgramData%\Meridian Console\Agent\runtimes"` shows no inherited entries, `NT AUTHORITY\SYSTEM:(OI)(CI)(F)`, `BUILTIN\Administrators:(OI)(CI)(F)`, and only `(OI)(CI)(RX)` for `BUILTIN\Users` and `NT AUTHORITY\SERVICE`; a second Start makes no new download (agent log).
   7. Troubleshooting: agent log (Event Log source "Meridian Console Agent"), `401` at `/hubs/agent` (certificate not forwarded: check the secret on both sides and that the agent uses port 8443), `node_offline` on start, EF migration errors. Start failing with `Java.*` codes: `Java.DownloadFailed`/`Java.IoError` (outbound HTTPS or proxy blocked; allow the three hosts above, or set `Agent:Minecraft:JavaPath`), `Java.DirectoryNotSecured` (agent not running as LocalSystem), access denied starting `java.exe` under the game-server account (record it; the DACL in `src/Agents/Dhadgar.Agent.Windows/README.md` would need a per-account ACE).
   8. **Manual check record** table: `| Step | Date | Operator | Result | Notes |` with one row per step 2-6, empty. The close-out may call the slice "confirmed working" only for rows the owner has filled with a pass.
5. `deploy/compose/README.md`: link to the runbook; list the new environment variables.

**Checks (in order).**

```bash
POSTGRES_PASSWORD=x RABBITMQ_PASSWORD=x AGENT_FORWARDING_SECRET=0123456789abcdef0123456789abcdef docker compose -f deploy/compose/docker-compose.dev.yml -f deploy/compose/docker-compose.services.yml config -q
POSTGRES_PASSWORD=x RABBITMQ_PASSWORD=x AGENT_FORWARDING_SECRET=0123456789abcdef0123456789abcdef docker compose -f deploy/compose/docker-compose.dev.yml -f deploy/compose/docker-compose.services.yml config --format json | python3 -I -c "import json,sys; d=json.load(sys.stdin); assert not d['services']['nodes'].get('ports'), 'nodes is published'; print('nodes unpublished')"
dotnet test tests/Dhadgar.Gateway.Tests/Dhadgar.Gateway.Tests.csproj -c Release
```

Add any further variables the compose files require to the commands rather than editing them out. The second line must print `nodes unpublished`.

**Docs owed.** `docs/runbooks/vertical-slice.md`, `deploy/compose/README.md`, `deploy/compose/.env.example`.

**Done when.** Checks pass. Ledger status becomes UATPending until the owner fills the manual check table.

### VS-28 Close-out: PROJECT-STATE, ADRs, issues

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/28-close-out` / `vertical-slice-vs28` (after the campaign review fix PR merges) |
| Commit subject | `docs: vertical-slice close-out, project state and ADR status [VS-28]` |
| Security review | No |
| Size | about 25k tokens |

**Goal.** `docs/PROJECT-STATE.md` changes once, honestly (status honesty rule), and the ADRs and indexes match what shipped.

**Rules.**
- "Confirmed working" only with a passing seam test named in the row, or a manual check the owner recorded in `docs/runbooks/vertical-slice.md`; everything else is "code exists".
- Do not touch code. Documentation only.
- Tables with spaces around pipes; dated entries.

**Changes.**
1. `docs/PROJECT-STATE.md`: a new section "Status update <date> (vertical-slice campaign)" with a table: one row per seam from the README's seam table (seam test name and status), plus rows for parking (D-03), Panel pages ("code exists" unless the runbook rows pass) and compose deployment (the runbook record). Update §3 (Servers and Console move out of "Stub"; parked services listed as parked), §4 dispositions for DV-1, DV-2, DV-5, DV-11, DV-13, DV-14 (link the packets), and §6 (Phases 2-4: done, partial or open, with packet ids). Do not rewrite history sections.
2. `docs/adr/0008-agent-transport-signalr.md`: status "Accepted (2026-07-26); implemented by the vertical-slice campaign (<date>)", and an "Implementation notes" paragraph: D-06 ingress, D-10 protocol header, single-instance registry.
3. `docs/adr/0007-*.md`: dated note that signing is implemented per D-09; rotation open.
4. `docs/analysis/README.md`: replace "No campaigns yet." with a table row for `vertical-slice` (status, link).
5. `CLAUDE.md`: "Open Issues" reflects #94 (done except rotation) and #101 (done); add "Nodes `AgentHub` and `AgentCommandDispatcher`" to the security-review list.
6. `docs/analysis/vertical-slice/README.md` and `RESUME.md`: final statuses; follow-ups list (below).
7. Report for ops: issues to comment or close: #94 (close; rotation follow-up), #101 (close), #118 (comment: Ping/StartServer/StopServer/ServerStatus done; Restart and P1 open), #116 (comment: protocol header only), #121 (comment: P0 #1 and enrollment→heartbeat covered by `tests/Dhadgar.Seams.Tests` without Aspire), #88 (closed after VS-17).

Follow-ups to record: signing-key rotation; Redis backplane for `/hubs/agent` and Console; `Asp.Versioning` (#116); Restart/Kill; Console `SendCommand`; dedupe `RabbitMqOptions`; BetterAuth `shop` client and cart origins; Gateway global rate-limit partition (F-26); Linux agent unpark; Cloudflare and mTLS for production agent ingress.

**Checks.** `git diff --stat` shows only documentation files.

**Docs owed.** The changes above.

**Done when.** The campaign reviewer's close-out pass is `ship`.

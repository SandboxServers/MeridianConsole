# vertical-slice: contract and work packets

Ledger: [README.md](README.md). Resume card: [RESUME.md](RESUME.md).

## Dispatch rules

- One packet, one worktree, one `packet-coder`. Worktree `/home/claude/meridianconsole/.claude/worktrees/vertical-slice-vsNN`, branch `vs/NN-<slug>`, created by ops from `origin/main` **after the previous wave has merged** (the wave 1 base is `2c4c19c`). A packet marked "stacks on VS-MM" is created from VS-MM's branch instead, and its PR uses `--base vs/MM-<slug>` until VS-MM merges.
- Report path: `<worktree>/.claude/reports/vertical-slice/VS-NN-coder.md`. Return line: `VS-NN <done|blocked|failed> <sha|-> <report-path> <reason>`.
- Shipping: **one PR per packet**, squash-merged in the merge order given per wave, CI green, `security-review` label plus the owner's yes where the packet says "Security review: yes".
- Within a wave, dispatch every packet whose dependencies have merged, in one message. Packets whose dependencies are still open stay BlockedDependency until those merge.
- Environment (Linux container, bash; `pwsh` is not assumed). Start every shell call with:

```bash
cd /home/claude/meridianconsole/.claude/worktrees/vertical-slice-vsNN && export DOTNET_ROOT=/home/claude/.dotnet PATH=/home/claude/.dotnet:$HOME/.dotnet/tools:$PATH CI=true DOTNET_NOLOGO=1
```

- `CI=true` matches CI: warnings are errors and the full analyzer set runs. There is no Docker daemon: never use Testcontainers. Tests use `WebApplicationFactory` with EF InMemory.
- **Lock files.** After any `PackageReference` or `ProjectReference` change, run `dotnet restore <changed csproj or Dhadgar.sln> --force-evaluate`. For every `packages.lock.json` that `git status` shows as changed, run `sed -i 's/\r\?$/\r/' <file>` (lock files are CRLF), then `dotnet restore <same target> --locked-mode` must pass.
- **Line endings.** Keep each existing file's line endings. New `.cs`, `.csproj` and `.json` files under `src/` and `tests/` are CRLF (finish with `sed -i 's/\r\?$/\r/' <file>`). New `.md`, `.yml` and `.ts`/`.tsx` files match their neighbours (check with `grep -c $'\r' <neighbour>`).
- Windows-only code builds on Linux; Windows-only tests use `[SkippableFact]` with `Skip.IfNot(OperatingSystem.IsWindows())` and are skipped here and in CI. A packet with Windows-only behaviour says how it is verified.
- Seam tests that cannot pass yet use `[Fact(Skip = "Pending VS-NN: <reason>")]` (D-13). The writing packet runs them once with the skip removed and quotes the failure line in its report; packet VS-NN removes the skip. The quoted failure must be the seam reason (no route, wrong status from the server, missing event), never an exception thrown inside the test's own client.
- **A seam test that asserts a rejection must prove the server side rejected it** (VS-05 review-1 and security-1 N1). Passing because a client-side guard threw proves nothing: `ControlPlaneClient` refused to send without a certificate, and the test stayed green while the Gateway and Nodes accepted anyone. Every rejection assertion therefore:
  1. reads the status from the server's response: `response.StatusCode`, `HttpRequestException.StatusCode`, a ProblemDetails `errorCode` or a `HubException` code. Never `Assert.ThrowsAnyAsync<Exception>`, and never an exception type the client can throw on its own;
  2. proves which server answered. If the backend rejected it, assert the backend received the call: `SeamForwarderFactory` recorded a request for that cluster and path, or a recording handler or middleware on the backend factory saw it. If the Gateway rejected it, assert the forwarder recorded **no** request for that path;
  3. has a positive twin in the same class that differs only in the variable under test (certificate present, right org, valid payload) and passes, so a 401 or 404 is about that variable and not a missing route or JWT.
- **Client-certificate test shims stay in the test project that owns them.** A startup filter or middleware that turns a test header (`X-Seam-Client-Cert`, `X-Test-Client-Cert`) into `Connection.ClientCertificate` lives in that test project, is registered only in `ConfigureTestServices`, and is never placed in `src/`, in the shared `tests/Dhadgar.Testing` library (VS-32), or behind `InternalsVisibleTo`. A test that needs one copies or reuses the shim inside its own project.
- **Test JWTs come from one helper (D-22).** Mint and accept test user tokens only with `SeamJwt` (`CreateUserToken(userId, organizationId)`, `Apply(services)`). It lives in `tests/Dhadgar.Seams.Tests/Infrastructure/SeamJwt.cs` until VS-32 moves it to `tests/Dhadgar.Testing/Auth/SeamJwt.cs` (namespace `Dhadgar.Testing.Auth`, global `<Using>` in the consuming test projects). Never add another token minter or another `PostConfigure<JwtBearerOptions>` test-key block. Service test factories that use a test authentication scheme (the `NodesWebApplicationFactory` pattern) are not JWT helpers and stay as they are.
- **Agent-originated data is bounded by C-2 "Limit enforcement" (D-23).** A packet that receives agent data (hub methods, events carrying agent text, agent HTTP bodies) enforces its rows with one test per bound. A packet that sends agent data truncates free text to the same constants.

## The contract

Everything below is the single source of truth for both sides of each seam. Types live only in `src/Shared/Dhadgar.Contracts`; never copy one into a service or the agent.

### C-1 Routes

| Public path (through the Gateway) | Gateway route id, policy, transform | Service and service path | Service auth | Success | Errors (`errorCode`) |
| --- | --- | --- | --- | --- | --- |
| `POST /api/v1/agents/enroll` | `agents-enroll-route`, Anonymous, strip `/api/v1` | Nodes `POST /agents/enroll` | token in body | 201 `EnrollNodeResponse` | 400 `invalid_platform`/`invalid_public_key`, 401 `invalid_token` |
| `GET /api/v1/agents/ca-certificate` | `agents-ca-route` (VS-07), Anonymous, strip `/api/v1` | Nodes `GET /agents/ca-certificate` | none | 200 `application/x-pem-file` | 400 `ca_not_initialized` |
| `POST /api/v1/agents/{nodeId}/certificates/renew` | `agents-route`, `AgentCertificate` (VS-07), strip `/api/v1` | Nodes `POST /agents/{nodeId}/certificates/renew` | mTLS, `AgentPolicy` | 200 `RenewCertificateResponse` | 400 `invalid_public_key` (also for a `PublicKey` over 4096 chars, checked before decoding), 401 `certificate_not_current`, 403, 404 `node_not_found` |
| `POST /api/v1/agents/{nodeId}/heartbeat` (legacy HTTP, kept per ADR-0008) | `agents-route` | Nodes `POST /agents/{nodeId}/heartbeat` | mTLS, `AgentPolicy` | 200 | 400, 403, 404 |
| `/hubs/agent` (negotiate and connection) | `agents-hub-route` (VS-07), `AgentCertificate`, no transform | Nodes `/hubs/agent` | mTLS, `AgentPolicy`, header `X-Agent-Protocol: 1` | connected | 401 missing/invalid certificate; connection aborted on wrong protocol or unknown/decommissioned node |
| `/api/v1/organizations/{organizationId}/nodes/{**}` | `nodes-org-route`, TenantScoped, strip `/api/v1` | Nodes `/organizations/{organizationId}/nodes/...` | JWT, `TenantScoped` | existing | existing |
| `/api/v1/organizations/{organizationId}/enrollment/{**}` | `enrollment-route`, TenantScoped, strip `/api/v1` | Nodes `/organizations/{organizationId}/enrollment/...` | JWT, `TenantScoped` | 201/200 | existing |
| `/api/v1/organizations/{organizationId}/servers/{**}` | `servers-org-route` (VS-07, replaces `servers-route`), TenantScoped, strip `/api/v1` | Servers `/organizations/{organizationId}/servers/...` | JWT, `TenantScoped` | 200/201/204 | 400 validation, 404 `server_not_found`, 409 `insufficient_capacity`/`invalid_node` |
| `POST /api/v1/organizations/{organizationId}/servers/{serverId}/start` and `/stop` | `servers-org-route` | Servers same path minus `/api/v1` | JWT | **202** (VS-23) | 404 `server_not_found`, 409 `node_offline`, 409 `invalid_state` |
| `/api/v1/organizations/{organizationId}/servers/{serverId}/console/{**}` | `console-org-route` (VS-07, replaces `console-api-route`), TenantScoped, strip `/api/v1`, `Order: -1` | Console `/organizations/{organizationId}/servers/{serverId}/console/...` | JWT | 200 | 404 |
| `/hubs/console` | `console-hub-route`, TenantScoped (Gateway reads the JWT from the `access_token` query on `/hubs/console`, VS-04) | Console `/hubs/console` | JWT (also from `access_token` query) | connected | 401 |
| not routed (Gateway must 404) | none | Nodes `POST /internal/organizations/{organizationId}/nodes/{nodeId}/commands` | JWT `TenantScoped`; Servers forwards the caller's token | **202** `DispatchAgentCommandResponse` | 400 `invalid_command_type`/`invalid_payload` (payload fails the typed strict parse or a C-2 limit, or `ServerId` disagrees with the payload), 404 `node_not_found`, 409 `node_offline`, 409 `server_not_on_node` (server command without a Claimed reservation for that server on that node) |
| not routed | none | Nodes `POST /organizations/{org}/nodes/{node}/reservations`, `POST /reservations/{token}/claim`, `DELETE /reservations/{token}` (existing) | JWT | 201/200/204 | existing |

Error shape everywhere: RFC 7807 from `Dhadgar.ServiceDefaults.Problems.ProblemDetailsHelper`, `{ "type", "title", "status", "detail", "errorCode", "traceId" }`. Clients branch on `errorCode`. Hubs raise `HubException("<error_code>")`.

### C-2 Agent wire types (`src/Shared/Dhadgar.Contracts/Agents/`, VS-03)

Synced on 2026-10-26 to branch `vs/03-agent-contracts` at `eabdbe5` (review-3 and security-2 `ship`). The code there is the reference for anything this block abbreviates (XML docs, remarks).

```csharp
namespace Dhadgar.Contracts.Agents;

public static class AgentProtocol
{
    public const int Version = 1;
    public const string VersionHeader = "X-Agent-Protocol";
    public const string HubPath = "/hubs/agent";            // same public and service path
    // Nodes HubOptions.MaximumReceiveMessageSize. Size of one SignalR JSON frame AFTER escaping: the limits
    // below count unescaped chars or UTF-8 bytes, and a control or non-ASCII char escapes to 6 bytes (\uXXXX),
    // so 64 KiB of content can be 384 KiB on the wire. 512 KiB covers the largest allowed message.
    public const int MaxHubMessageBytes = 512 * 1024;
}

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
    public const int MaxHeartbeatWarnings = 20;
    public const int MaxWarningLength = 256;
    public const int MaxAgentVersionLength = 64;            // AgentHeartbeat.AgentVersion, PingResult.AgentVersion
    public const int MaxServersPerHeartbeat = 64;
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
/// Strict and read-only: a missing required property or an unknown property throws JsonException.</summary>
public static class AgentJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 8,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>Implemented by Nodes' AgentHub. Called by the agent.</summary>
public interface IAgentHub
{
    Task Heartbeat(AgentHeartbeat heartbeat);
    Task CommandResult(AgentCommandResult result);
    Task Telemetry(AgentTelemetry telemetry);
    Task ServerStatusChanged(AgentServerStatus status);
    Task ConsoleOutput(AgentConsoleBatch batch);
}

/// <summary>Implemented by the agent. Called by Nodes through IHubContext&lt;AgentHub, IAgentHubClient&gt;.</summary>
public interface IAgentHubClient
{
    Task ReceiveCommand(AgentCommandEnvelope command);
    Task ReceivePing(string nonce);
}

public sealed record AgentCommandEnvelope(
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

public sealed record AgentCommandResult(
    Guid CommandId,
    Guid NodeId,
    AgentCommandStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? ResultJson,
    string? ErrorMessage,
    string? ErrorCode,
    string? CorrelationId);

public enum AgentServerState { Stopped = 0, Installing = 1, Starting = 2, Running = 3, Stopping = 4, Crashed = 5 }

public sealed record AgentServerProcess(
    Guid ServerId, AgentServerState State, int? OsPid, double CpuPercent, long MemoryBytes, long UptimeSeconds);

public sealed record AgentHeartbeat(
    Guid NodeId,
    string AgentVersion,
    DateTimeOffset Timestamp,
    double CpuUsagePercent,      // 0..100
    double MemoryUsagePercent,   // 0..100
    double DiskUsagePercent,     // 0..100, highest-used disk
    IReadOnlyList<AgentServerProcess> Servers,
    IReadOnlyList<string> Warnings);

public sealed record AgentServerStatus(
    Guid NodeId, Guid ServerId, AgentServerState State, int? ExitCode, string? Message, DateTimeOffset Timestamp);

public sealed record AgentConsoleLine(DateTimeOffset Timestamp, bool IsError, string Text);

public sealed record AgentConsoleBatch(Guid NodeId, Guid ServerId, IReadOnlyList<AgentConsoleLine> Lines);

public sealed record AgentTelemetry(Guid NodeId, DateTimeOffset Timestamp, IReadOnlyDictionary<string, double> Metrics);

// Command payloads (PayloadJson / ResultJson, serialized with AgentJson.Options)
public sealed record PingPayload(string? Nonce = null);   // default: "{}" is a valid Ping payload (review-2 N1)
public sealed record PingResult(string AgentVersion, DateTimeOffset AgentTime, string? Nonce);
public sealed record StartServerPayload(
    Guid ServerId, string MinecraftVersion, string ServerJarUrl, string ServerJarSha1,
    int MemoryMb, int Port, bool EulaAccepted);
public sealed record StopServerPayload(Guid ServerId, int GracefulTimeoutSeconds);
public sealed record ServerStatusPayload(Guid ServerId);
public sealed record ServerStatusResult(Guid ServerId, AgentServerState State, int? OsPid, long UptimeSeconds);

// MassTransit events published by Nodes
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

- Every positional record parameter is **required** in `AgentJson` unless it has a default value. So `PingPayload(string? Nonce = null)` accepts `{}`, VS-29's `JavaRuntimes = null` is optional, and any future optional field must get a default. Unknown properties throw (D-21 covers the upgrade order this implies).
- PayloadJson is untrusted on the agent: verify the signature over the **raw** `PayloadJson` string before parsing it. After parsing, check every enum with `Enum.IsDefined` (numbers are not range-checked by the serializer).
- SignalR binds hub-method arguments with its own JSON hub protocol options, **not** `AgentJson`. A non-nullable string or list can arrive null, unknown properties are ignored and enums are not range-checked. Hub receivers therefore null-check and `Enum.IsDefined`-check every field (table below). Do not swap the hub protocol options.

Command acceptance on the agent (the `AgentCommandEnvelope` remarks in the code; VS-13 filter, VS-14 validator). The agent answers `Rejected` unless all of these hold:

1. `AgentCommandTypes.IsKnown(CommandType)`.
2. `NodeId` equals the agent's own node id and `OrganizationId` equals the enrolled organization (neither `Guid.Empty`).
3. `0 < ExpiresAt - IssuedAt <= MaxCommandLifetimeSeconds`; `IssuedAt <= now + MaxClockSkewSeconds`; `now < ExpiresAt`.
4. `SigningKeyId` equals the id of the key pinned at enrollment (D-09), ordinal. The agent never selects or fetches a key by this value.
5. `Signature` decodes (`Convert.TryFromBase64String`) to exactly `AgentCommandSigning.SignatureBytes` (64) bytes and verifies with `DSASignatureFormat.IeeeP1363FixedFieldConcatenation` over `AgentCommandSigning.TryBuildSigningInput(envelope, out input)`. `TryBuildSigningInput` returning false is a rejection, not an exception.
6. The replay cache has not seen `CommandId`. The cache key is `CommandId`, never the signature (ECDSA signatures are malleable), and an entry is kept until `ExpiresAt + MaxClockSkewSeconds`. A command is registered only after every other check passed.

Hub rules (Nodes side, VS-08 and VS-33):

- The connection's node is the `node_id` claim from the certificate (set by `MtlsMiddleware`). Every method whose argument carries a `NodeId` that differs throws `HubException("node_mismatch")`.
- On connect: missing or wrong `X-Agent-Protocol` aborts the connection; an unknown or decommissioned node aborts it; otherwise the connection joins group `node:{nodeId:D}` and is recorded in `IAgentConnectionRegistry`.
- `ServerStatusChanged` and `ConsoleOutput` require a **Claimed** capacity reservation on that node whose `ServerId` equals `serverId.ToString("D")`; otherwise `HubException("server_not_on_node")`.
- `CommandResult` for an unknown command id, or one belonging to another node, throws `HubException("unknown_command")`; a second result for a finished command is ignored.
- `AgentCommandCompleted` takes `OrganizationId`, `CommandType`, `ServerId` and `CorrelationId` from Nodes' own `AgentCommand` row, never from the agent's result. `AgentServerStatusChanged` and `ServerConsoleOutput` take `OrganizationId` from the node record (`Context.Items["OrganizationId"]`, set on connect).

#### Limit enforcement (D-23)

Every receiver of agent-originated data enforces these limits; a conforming sender never trips them. **Receivers reject; they never truncate** (truncating on receipt hides sender bugs and lets a compromised agent store data that differs from what it sent). **Senders truncate** free text so they stay inside the limits. Every validator uses the constants above, never a local copy or a stricter or looser local rule. One test per row: at the limit passes, limit + 1 fails with the listed code.

| Message (direction) | Field rules | Receiver (packet) | Violation |
| --- | --- | --- | --- |
| Any hub frame (agent → Nodes) | escaped frame ≤ `AgentProtocol.MaxHubMessageBytes` | SignalR `MaximumReceiveMessageSize` (VS-08) | connection closed by SignalR |
| `AgentHeartbeat` | `AgentVersion` non-null, 1..`MaxAgentVersionLength`; three percentages finite and 0..100; `Servers` non-null, ≤ `MaxServersPerHeartbeat`, each `State` defined, `CpuPercent` finite ≥ 0, `MemoryBytes` ≥ 0, `UptimeSeconds` ≥ 0; `Warnings` non-null, ≤ `MaxHeartbeatWarnings`, each non-null and ≤ `MaxWarningLength` | `AgentHubMessageValidator` (VS-08) | `HubException("invalid_heartbeat")` |
| `AgentTelemetry` | `Metrics` non-null, ≤ `MaxTelemetryMetrics`; each name 1..`MaxMetricNameLength` with no control chars; each value finite | VS-08 | `invalid_telemetry` |
| `AgentCommandResult` | `Status` defined; `ResultJson` null or ≤ `MaxResultJsonBytes` UTF-8 bytes; `ErrorMessage` null or ≤ `MaxErrorMessageLength`; `ErrorCode` null or matches `ErrorCodePattern`; `CorrelationId` null or ≤ `MaxCorrelationIdLength` (bounded, then ignored: the event uses the row's value) | VS-33 | `invalid_result` (row unchanged) |
| `AgentServerStatus` | `State` defined; `Message` null or ≤ `MaxErrorMessageLength` | VS-33 | `invalid_status` |
| `AgentConsoleBatch` | `Lines` non-null, 1..`MaxConsoleLinesPerBatch`; each line non-null, `Text` non-null and ≤ `MaxConsoleLineLength`; sum of `Text` lengths ≤ `MaxConsoleBatchChars` | VS-33 | `invalid_console_batch` |
| `ServerConsoleOutput` event (Nodes → Console, agent-originated text) | `Lines.Count` ≤ `MaxConsoleLinesPerBatch`, each `Text` ≤ `MaxConsoleLineLength`; text never logged; rendered only as text | Console consumer (VS-17), Panel (VS-26) | event dropped with a warning that carries no text |
| `AgentCommandCompleted` event (agent-originated `ResultJson`, `ErrorCode`, `ErrorMessage`) | `Status` defined before use; `ResultJson` parsed only with `AgentJson.Options` inside a `try`, failure = ignore the result body | Servers consumers (VS-23) | event skipped with a warning |
| `AgentCommandEnvelope` (Nodes → agent) | `CommandType` non-null, ≤ `MaxCommandTypeLength`; `PayloadJson` non-null, ≤ `MaxPayloadJsonBytes`; `CorrelationId` null or ≤ `MaxCorrelationIdLength`; `Signature` null/empty or exactly `AgentCommandSigning.SignatureBase64Length` (88) chars; `SigningKeyId` null/empty or exactly `AgentCommandSigning.KeyIdLength` (64) chars; then the acceptance rules above | agent `InboundCommandFilter` (VS-13: sizes, identity, time) and `CommandValidator` (VS-14: type, signature, key id, replay) | dropped with a log (filter) or `Rejected` result (validator) |
| `ReceivePing(nonce)` (Nodes → agent) | non-null, ≤ `MaxNonceLength` | agent `ControlPlaneClient` (VS-13) | dropped with a log; the nonce is not logged |
| Typed payloads (Servers → Nodes dispatch, then agent) | parse with `AgentJson.Options` by `CommandType`; `PingPayload.Nonce` ≤ `MaxNonceLength`; `StartServerPayload`: `MinecraftVersion` matches `MinecraftVersionPattern`, `ServerJarUrl` absolute `https` and ≤ `MaxServerJarUrlLength`, `ServerJarSha1` exactly `ServerJarSha1HexLength` hex chars, `MemoryMb` and `Port` in range; `StopServerPayload.GracefulTimeoutSeconds` in `MinGracefulTimeoutSeconds..MaxGracefulTimeoutSeconds` (rejected, never clamped) | Nodes dispatcher (VS-15) before signing; agent handlers again (VS-16, VS-19, VS-20) | `invalid_payload` (Nodes); `Failed` result with a `[Category.Code]` error (agent) |
| `RenewCertificateRequest.PublicKey` (agent → Nodes, HTTP) | ≤ 4096 chars before base64 decoding | Nodes renew endpoint (VS-10) | 400 `invalid_public_key` |

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

// VS-10: replaces the existing renewal records
public record RenewCertificateRequest(string PublicKey);   // base64 DER SPKI of the agent's NEW key pair, at most 4096 chars
public record RenewCertificateResponse(
    string CertificateThumbprint, string Certificate /* PEM */, DateTimeOffset NotBefore, DateTimeOffset NotAfter);

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
| `X-Agent-Protocol: 1` | agent → hub | Protocol version (D-10) |
| `X-Client-Cert` | Gateway → Nodes | base64 DER of the TLS client certificate; stripped from client requests |
| `X-Dhadgar-Gateway-Secret` | Gateway → Nodes | shared secret proving the header came from the Gateway; stripped from client requests |
| `AgentIngress:ForwardedCertificateSecret` | Gateway | the secret (≥ 32 chars); empty disables forwarding |
| `Kestrel:Endpoints:Agents:Url`, `...:ClientCertificateMode=AllowCertificate` | Gateway | agent listener `https://+:8443` (compose, VS-27) |
| `Mtls:ForwardedCertificate:Enabled`, `Mtls:ForwardedCertificate:SharedSecret` | Nodes | accept forwarded certificates (same secret) |
| `Mtls:AdditionalProtectedPrefixes` | Nodes | default `["/hubs/agent"]` |
| `CommandSigning:KeyPath`, `CommandSigning:CommandTtlSeconds` (60, range 10..`AgentMessageLimits.MaxCommandLifetimeSeconds` = 60) | Nodes | signing key file (optional), command lifetime; a TTL above 60 would make every conforming agent reject every command |
| `Auth:Issuer`, `Auth:Audience`, `Auth:MetadataAddress`, `Auth:InternalBaseUrl`, `Auth:ClockSkewSeconds` | Gateway, Nodes, Servers, Console, Secrets | JWT validation (VS-04 helper) |
| `Services:Nodes:BaseUrl` | Servers | e.g. `http://nodes:8080` |
| `Services:Servers:BaseUrl` | Console | e.g. `http://servers:8080` (exists in #88) |
| `Agent:StatePath` | agent | directory for `agent-identity.json` |
| `Agent:EnrollmentToken` | agent | optional; Windows also reads `HKLM\SOFTWARE\Meridian Console\Agent\EnrollmentToken` |
| `Agent:Security:RequireSignedCommands` (true), `Agent:Security:CommandSigningPublicKey` | agent | D-09 |
| `Agent:Minecraft:AllowedDownloadHosts` (`["piston-data.mojang.com"]`), `Agent:Minecraft:MaxJarBytes` | agent (`MinecraftOptions`, VS-19) | D-19: server jar download |
| `Agent:Minecraft:JavaPath` (empty), `JavaDownloadEnabled` (true), `JavaRuntimePath`, `JavaDownloadHosts` (`["github.com"]`), `JavaRedirectHosts` (`["release-assets.githubusercontent.com", "objects.githubusercontent.com"]`), `MaxJavaArchiveBytes`, `MaxJavaExtractedBytes`, `MaxJavaArchiveEntries`, `JavaRuntimeRetentionDays` | agent (`JavaRuntimeOptions`, VS-29) | D-19: managed JRE; a non-empty `JavaPath` wins over everything (C-7) |

### C-7 Java runtime (D-19; VS-29 adds it on top of VS-03's files)

VS-03 built C-2. VS-29 then adds one record and one trailing optional parameter. Old JSON without `javaRuntimes` still deserializes (the parameter has a default, so `AgentJson` does not require it), and every `new StartServerPayload(...)` call with seven arguments still compiles. The reverse does not hold: an agent built before VS-29 rejects a payload that carries `javaRuntimes` (`UnmappedMemberHandling.Disallow`). That is acceptable only under D-21's upgrade order.

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

Five independent packets, dispatched together from `origin/main` `2c4c19c`. Merge order: VS-03, VS-04, VS-05, VS-01, VS-02 (VS-01 and VS-02 last because they touch the most files; neither conflicts with the others). Wave review after all five reach `ship`.

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

Base: `origin/main` after all of wave 1. Stage 1, dispatched together: VS-06, VS-07, VS-09, VS-11, VS-32. Stage 2, dispatched as their dependencies merge: VS-08 (after VS-06 and VS-07), VS-10 (after VS-06, VS-07, VS-09). Merge order: VS-11, VS-32, VS-06, VS-07, VS-09, VS-08, VS-10. VS-06 and VS-07 touch disjoint files (Nodes vs Gateway), except for the seams harness: VS-06 owns `NodesSeamFactory.cs`, VS-07 owns `GatewaySeamFactory.cs`, VS-32 owns `SeamJwt.cs` and the seams `.csproj` (it moves `SeamJwt` behind a global `<Using>`, so callers in the two factories need no edit). Whoever merges after VS-32 reruns the lock-file rule for `tests/Dhadgar.Seams.Tests` on rebase (ops).

Contract sync (2026-10-26, report `contract-sync-1.md`): C-2 and C-3 now match VS-03 at `eabdbe5`. Every wave-2+ packet reads C-2 "Limit enforcement" and the new dispatch rules on rejection seams, certificate shims and test JWTs.

### VS-06 Nodes auth: JWT, strict AgentPolicy, hub mTLS, forwarded certificates

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/06-nodes-auth` / `vertical-slice-vs06` |
| Commit subject | `fix(nodes): register JWT auth, restrict AgentPolicy to mTLS identities, accept Gateway-forwarded certificates [VS-06]` |
| Security review | Yes |
| Size | about 40k tokens |

**Goal.** Nodes starts outside tests (F-01), user tokens cannot pass `AgentPolicy` (F-02), `/hubs/agent` is covered by mTLS (F-03), and a certificate forwarded by the Gateway with the shared secret counts as the client certificate (D-06, F-05).

**Rules.**
- Errors at HTTP boundaries are ProblemDetails (`ProblemDetailsHelper`) or the existing `WriteUnauthorizedResponse`; no new exception types.
- Secrets are compared with `CryptographicOperations.FixedTimeEquals` on UTF-8 bytes. Never log the secret or the certificate body; log the thumbprint at most.
- Forwarded certificate bytes are capped at 16 KiB before decoding; parse with `X509CertificateLoader.LoadCertificate`; register the certificate with `context.Response.RegisterForDispose`.
- mTLS may only be disabled in Development (existing rule in `AddMtlsAuthentication`); do not weaken it.
- Contract excerpt (C-6): headers `X-Client-Cert` (base64 DER), `X-Dhadgar-Gateway-Secret`; keys `Mtls:ForwardedCertificate:Enabled`, `Mtls:ForwardedCertificate:SharedSecret`, `Mtls:AdditionalProtectedPrefixes` (default `["/hubs/agent"]`).

**Changes.**
1. `src/Dhadgar.Nodes/Program.cs`: before the authorization builder, `builder.Services.AddDhadgarJwtBearer(builder.Configuration, builder.Environment);` (from VS-04, namespace `Dhadgar.ServiceDefaults.Security`). Replace the `AgentPolicy` definition with `policy.AddRequirements(new AgentIdentityRequirement());` and register `builder.Services.AddSingleton<IAuthorizationHandler, AgentIdentityHandler>();`. In the pipeline: `app.UseAuthentication(); app.UseForwardedClientCertificate(); app.UseMtlsAuthentication(); app.UseAuthorization();`.
2. `src/Dhadgar.Nodes/appsettings.json`: add `"Auth": { "Issuer": "https://meridianconsole.com/api/v1/identity", "Audience": "meridian-api", "ClockSkewSeconds": 30 }` (same values as the Gateway's file) so the helper's required keys exist.
3. New `src/Dhadgar.Nodes/Auth/AgentIdentityRequirement.cs` (requirement and handler in one file):

```csharp
public sealed class AgentIdentityRequirement : IAuthorizationRequirement { }

public sealed class AgentIdentityHandler(IOptionsMonitor<MtlsOptions> mtls) : AuthorizationHandler<AgentIdentityRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AgentIdentityRequirement requirement)
    {
        var hasNodeId = Guid.TryParse(context.User.FindFirst("node_id")?.Value, out _);
        var viaMtls = context.User.Identities.Any(i => i.IsAuthenticated && i.AuthenticationType == MtlsMiddleware.AuthenticationType);
        // mTLS can only be disabled in Development (AddMtlsAuthentication enforces it); tests rely on this.
        if (hasNodeId && (viaMtls || !mtls.CurrentValue.Enabled)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
```

4. `src/Dhadgar.Nodes/Auth/MtlsMiddleware.cs`: add `public const string AuthenticationType = "mTLS";` and use it in `new ClaimsIdentity(claims, AuthenticationType)`. `IsAgentEndpoint` returns true for `AgentEndpointPrefix` **or** any `AdditionalProtectedPrefixes` entry (segment-aware: `PathString.StartsWithSegments`). Exempt paths apply only under `AgentEndpointPrefix`; nothing under an additional prefix is exempt.
5. `src/Dhadgar.Nodes/Auth/MtlsOptions.cs`: add `public string[] AdditionalProtectedPrefixes { get; set; } = ["/hubs/agent"];` and `public ForwardedCertificateOptions ForwardedCertificate { get; set; } = new();` with class `ForwardedCertificateOptions { bool Enabled; string HeaderName = "X-Client-Cert"; string SecretHeaderName = "X-Dhadgar-Gateway-Secret"; string SharedSecret = ""; }`. Validation (make `MtlsOptions` `IValidatableObject`, or add an `IValidateOptions<MtlsOptions>`): when `ForwardedCertificate.Enabled`, `SharedSecret.Length >= 32`.
6. New `src/Dhadgar.Nodes/Auth/ForwardedClientCertificateMiddleware.cs` plus `UseForwardedClientCertificate()` in `MtlsMiddlewareExtensions`:
   - Read both headers, then **always** remove them from `context.Request.Headers`.
   - If `ForwardedCertificate.Enabled`, `context.Connection.ClientCertificate` is null, the cert header is present, the secret matches (fixed time; different length means no match), and the decoded bytes are at most 16 KiB and parse, then set `context.Connection.ClientCertificate`. Otherwise log a warning (`"Ignoring forwarded client certificate: {Reason}"`, reason one of `disabled`, `secret_mismatch`, `too_large`, `malformed`) and continue without one; `MtlsMiddleware` then answers 401 on protected paths.
7. Seams harness `tests/Dhadgar.Seams.Tests/Infrastructure/NodesSeamFactory.cs`: delete the `AddAuthentication(...).AddJwtBearer()` line (keep `SeamJwt.Apply`), and add config `Mtls:ForwardedCertificate:Enabled=true`, `Mtls:ForwardedCertificate:SharedSecret=SeamSecrets.ForwardedCertificate`. The seams suite now starts Nodes with its real auth registration, which proves F-01 is fixed. Do not edit `SeamJwt.cs` or the seams `.csproj`: VS-32 moves `SeamJwt` in parallel, and `SeamJwt.Apply(services)` keeps compiling either way (D-22).
8. Agent identity is bound to the certificate, never to a header (VS-05 security-2 carry-forward). Today `MtlsMiddleware` sets `node_id` only from the certificate's SPIFFE SAN (`spiffe://<trust domain>/nodes/{nodeId}`, `CertificateValidationService`), and the agent's `X-Node-Id` header is advisory. Keep it that way: no Nodes code may read `X-Node-Id` (or any request header or body field) to decide which node a caller is. `ForwardedClientCertificateMiddleware` must not add claims; only `MtlsMiddleware` does, from the validated certificate. New seam `tests/Dhadgar.Seams.Tests/AgentHub/NodesAgentMtlsSeamTests.cs`, against the Nodes factory directly with the forwarded headers (`X-Client-Cert` plus the secret; the Gateway half is VS-07's). Enroll one agent through the Gateway (as the enrollment seam does) to get a real certificate for node A, and a second for node B.
   - `EnrolledCertificate_Heartbeat_Succeeds` (positive twin): `POST /agents/{A}/heartbeat` with A's certificate → 200.
   - `CertificateFromAnotherCa_IsRejected`: a self-signed P-256 certificate with the Client Authentication EKU and the SPIFFE SAN of node A (built with `CertificateRequest` and `SubjectAlternativeNameBuilder.AddUri`), forwarded with the right secret, to `POST /agents/{A}/heartbeat` and to `/hubs/agent/negotiate?negotiateVersion=1` → both `401` from Nodes, and the node's `LastHeartbeat` unchanged.
   - `NodeIdHeader_DoesNotOverrideCertificate`: A's certificate plus `X-Node-Id: {B}` to `POST /agents/{B}/heartbeat` → 403 (or the status the endpoint already returns for a route/claim mismatch; read `AgentEndpoints.cs` around line 102 and assert that exact status), and B's `LastHeartbeat` unchanged.
   - Also add `Connect_WithCertificateFromAnotherCa_IsRejected` to `AgentClientHubSeamTests` (through the Gateway, with the same `[Fact(Skip = "Pending VS-13: ...")]` text as its neighbours): a `SeamAgent` whose store holds the other-CA certificate and key, `CreateControlPlaneClient(nodeId: A, ...)`, `ConnectAsync` → `HttpRequestException` with `StatusCode == Unauthorized` **and** `Assert.Contains("nodes", host.Forwarder.ForwardedClusters)` (Nodes rejected it, not the Gateway). Run it once without the skip and quote the failure (D-13): today the Gateway rejects without forwarding, so the `ForwardedClusters` assertion fails. VS-13 removes the skip.

**Tests** (`tests/Dhadgar.Nodes.Tests`):
- `Auth/AgentIdentityHandlerTests.cs`: mTLS identity with `node_id` → succeeds; a JWT-style identity (`AuthenticationType = "Bearer"`) with `node_id` and mTLS enabled → fails; same with mTLS disabled → succeeds; no `node_id` → fails.
- `Auth/ForwardedClientCertificateMiddlewareTests.cs` (`DefaultHttpContext`): valid secret and cert → `Connection.ClientCertificate` set and both headers removed; wrong secret, disabled, oversize and garbage → not set and headers removed.
- `MtlsMiddlewareTests.cs`: add `HubPath_WithoutCertificate_Returns401_WhenMtlsEnabled` (`/hubs/agent/negotiate`) and `HubPath_IsNotExempt`.
- `ForwardedClientCertificateMiddlewareTests.cs`: also `XNodeIdHeader_AddsNoClaim` (after the middleware runs, `context.User` has no `node_id` claim, whatever headers came in).
- The seam in change 8 (`NodesAgentMtlsSeamTests`): fails when reverted because without `AdditionalProtectedPrefixes` the negotiate path is not checked, and without the forwarded-certificate middleware no certificate is seen at all.
- Existing Nodes tests stay green: `NodesWebApplicationFactory` keeps its default test scheme (`AddAuthentication(options => { DefaultAuthenticateScheme = ...; DefaultChallengeScheme = ...; })` overrides the JwtBearer defaults), and mTLS disabled there makes `AgentIdentityHandler` accept `node_id` claims.

Why they fail when reverted: without the handler a bearer identity passes `AgentPolicy`; without the middleware the forwarded certificate is never seen; and the seams suite cannot start Nodes without the JWT registration (the enrollment seam goes red).

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Dhadgar.Nodes/README.md` (auth section: JWT for users, mTLS for agents, forwarded certificates and the secret); `docs/runbooks/nodes-service-operations.md` (the new config keys).

**Done when.** Checks pass; the seams enrollment test still passes with the factory's auth line gone.

### VS-07 Gateway agent ingress and route alignment

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/07-gateway-agent-ingress` / `vertical-slice-vs07` |
| Commit subject | `feat(gateway): certificate-based agent ingress, client-cert forwarding, hub and org routes [VS-07]` |
| Security review | Yes |
| Size | about 40k tokens |

**Goal.** Agents authenticate at the Gateway by TLS client certificate instead of a JWT (F-04); the Gateway forwards the certificate to Nodes (D-06, F-05); routes for the hub, the CA certificate, Servers and Console match the services (F-15).

**Rules.**
- Strip `X-Client-Cert` and `X-Dhadgar-Gateway-Secret` from **every** incoming request before setting them; never take them from the client.
- Never log the secret or certificate bodies.
- The Gateway accepts any client certificate at the TLS layer (the handshake still proves key possession); Nodes validates chain and revocation. Say this in a code comment.
- Contract excerpt: route table C-1 (rows `agents-*`, `servers-org-route`, `console-org-route`, `console-hub-route`), headers and keys C-6.

**Changes.**
1. `src/Dhadgar.Gateway/Program.cs`:
   - In the existing `ConfigureKestrel` lambda add `options.ConfigureHttpsDefaults(https => https.ClientCertificateValidation = static (_, _, _) => true);`. Which endpoints ask for a certificate is set in configuration (`Kestrel:Endpoints:Agents:ClientCertificateMode=AllowCertificate`, VS-27).
   - Replace policy `Agent` with:

```csharp
options.AddPolicy("AgentCertificate", policy =>
    policy.RequireAssertion(ctx => ctx.Resource is HttpContext http && http.Connection.ClientCertificate is not null));
```

   - Bind `AgentIngressOptions` (section `AgentIngress`: `ForwardedCertificateSecret`, `HeaderName = "X-Client-Cert"`, `SecretHeaderName = "X-Dhadgar-Gateway-Secret"`) and add `app.UseMiddleware<ClientCertificateForwardingMiddleware>();` right after `RequestEnrichmentMiddleware`. When the secret is empty, log one warning at startup: "AgentIngress:ForwardedCertificateSecret is empty; agent certificates will not be forwarded".
2. New `src/Dhadgar.Gateway/Options/AgentIngressOptions.cs` and `src/Dhadgar.Gateway/Middleware/ClientCertificateForwardingMiddleware.cs`: remove both headers; if `Connection.ClientCertificate` is not null and the secret is configured, set `HeaderName = Convert.ToBase64String(cert.RawData)` and `SecretHeaderName = secret`.
3. `src/Dhadgar.Gateway/appsettings.json` (`ReverseProxy:Routes`):
   - `agents-route`: `"AuthorizationPolicy": "AgentCertificate"`.
   - New `agents-ca-route`: `Match.Path` `/api/v1/agents/ca-certificate`, cluster `nodes`, `Anonymous`, transform `PathRemovePrefix /api/v1`, same `RateLimiterPolicy` as `agents-enroll-route`.
   - New `agents-hub-route`: `Match.Path` `/hubs/agent/{**catch-all}`, cluster `nodes`, `AgentCertificate`, no transform, `RateLimiterPolicy` `PerAgent`.
   - Replace `servers-route` with `servers-org-route`: `/api/v1/organizations/{organizationId}/servers/{**catch-all}`, cluster `servers`, `TenantScoped`, `PathRemovePrefix /api/v1`, `PerTenant`.
   - Replace `console-api-route` with `console-org-route`: `/api/v1/organizations/{organizationId}/servers/{serverId}/console/{**catch-all}`, cluster `console`, `TenantScoped`, `PathRemovePrefix /api/v1`, `"Order": -1`, `PerTenant`.
   - Result: 14 routes, 8 clusters.
4. `tests/Dhadgar.Gateway.Tests/RouteConfigurationTests.cs` and `RateLimiterConfigurationTests.cs`: counts (14 routes) and rows for the new and renamed routes; new assertions: `agents-route` and `agents-hub-route` use `AgentCertificate`; `servers-org-route` and `console-org-route` strip `/api/v1`; `console-org-route` has `Order` -1.
5. Seams `tests/Dhadgar.Seams.Tests/Infrastructure/GatewaySeamFactory.cs`: config `AgentIngress:ForwardedCertificateSecret = SeamSecrets.ForwardedCertificate`. Remove the `Skip` from `AgentIngressThroughGatewaySeamTests.CaCertificate_IsFetchableAnonymously`, and add to that class `RenewEndpoint_WithoutCertificate_IsRejected` (`POST /api/v1/agents/{guid}/certificates/renew` without a cert → 401 or 403, never forwarded: assert the forwarder recorded no `nodes` request for that path) and `SpoofedClientCertHeader_IsStripped` (a request to `/api/v1/agents/enroll` carrying `X-Client-Cert: AAAA` and a wrong secret reaches Nodes without those headers; check with a test middleware on the Nodes factory that records the headers it receives, or with the forwarder's captured request). If `SeamForwarderFactory` records only cluster ids, extend it to record `(clusterId, method, path)` per forwarded request (`ForwardedRequests`), keeping `ForwardedClusters`; VS-08, VS-15 and VS-33 assert on paths (dispatch rule on rejection seams). Do not edit `SeamJwt.cs`, the seams `.csproj` or `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`: VS-32 owns them in this stage (D-22).

**Tests** (`tests/Dhadgar.Gateway.Tests/AgentIngressTests.cs`): a Gateway factory with a test `IStartupFilter` that sets `Connection.ClientCertificate` from header `X-Test-Client-Cert`, and an `IForwarderHttpClientFactory` whose handler captures the forwarded request and returns 200:
- with a certificate and the secret configured: the request to `/api/v1/agents/{id}/heartbeat` is forwarded, carries `X-Client-Cert` equal to the cert's base64 DER, and carries the secret;
- without a certificate: not forwarded, 401 (read from the Gateway's response; the capturing handler recorded nothing);
- a client-supplied `X-Client-Cert` without a TLS certificate is not forwarded on any route (check on `/api/v1/agents/enroll`);
- `/hubs/agent/negotiate` with a certificate is forwarded to cluster `nodes`.

Why they fail when reverted: the `Agent` policy demands a JWT claim, so certificate-only requests never forward, and nothing sets the forwarding headers.

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Gateway/Dhadgar.Gateway.csproj -c Release
dotnet test tests/Dhadgar.Gateway.Tests/Dhadgar.Gateway.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Dhadgar.Gateway/README.md`: "Agent ingress" (port 8443, `AllowCertificate`, forwarding headers, secret, why Cloudflare-proxied hostnames break mTLS) and the Kestrel environment example:

```bash
Kestrel__Endpoints__Https__Url=https://+:443
Kestrel__Endpoints__Http__Url=http://+:8080
Kestrel__Endpoints__Agents__Url=https://+:8443
Kestrel__Endpoints__Agents__ClientCertificateMode=AllowCertificate
```

Also `docs/gateway-routing-reference.md`: the route table rows from C-1.

**Done when.** Checks pass; the CA-certificate seam is green.

### VS-08 Nodes `/hubs/agent` hub, connection registry, heartbeat bridge

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/08-agent-hub` / `vertical-slice-vs08` (from `origin/main` after VS-06 and VS-07 merge) |
| Commit subject | `feat(nodes): /hubs/agent SignalR hub with mTLS identity, protocol check and heartbeat bridge [VS-08]` |
| Security review | Yes (new agent-facing surface) |
| Size | about 40k tokens (the contract sync added the validator and the frame-size tests) |

**Goal.** ADR-0008's hub exists on the Nodes side with the C-2 interfaces; heartbeats feed the existing `HeartbeatService`; Nodes can tell whether a node is connected.

**Rules.**
- Seams harness and test JWTs (D-22): mint and accept test user tokens only with `SeamJwt` (`tests/Dhadgar.Testing/Auth/SeamJwt.cs` after VS-32, which removes VS-04's duplicate minter in `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`); never add another minter or `PostConfigure<JwtBearerOptions>` test-key block. Client-certificate shims stay inside the test project that owns them. A seam that asserts a rejection proves the server answered: the status or code comes from the server's response, the forwarder or a backend recorder shows which server it reached, and a positive twin passes (dispatch rules).
- The node identity comes only from the `node_id` claim, never from message fields or headers (C-2 hub rules).
- Validate every inbound message with `AgentHubMessageValidator` against C-2 "Limit enforcement" (D-23): reject with `HubException("<code>")`, never truncate on receipt, never echo input in error text or logs. SignalR binds hub arguments with its own JSON options, so null-check every reference field and `Enum.IsDefined` every enum.
- `EnableDetailedErrors = false`. `MaximumReceiveMessageSize = AgentProtocol.MaxHubMessageBytes` (512 KiB, sized for escaped JSON; C-2).
- Hubs are transient; scoped services (DbContext, `IHeartbeatService`) are injected per invocation.
- Single-instance registry for the MVP; note in a comment that scale-out needs a Redis backplane (ADR-0008).

**Contract excerpt.** `IAgentHub`, `IAgentHubClient`, `AgentHeartbeat`, `AgentTelemetry`, `AgentServerState`, `AgentProtocol`, `AgentMessageLimits` (C-2); the hub rules and the `AgentHeartbeat`, `AgentTelemetry` and frame rows of "Limit enforcement" under C-2.

**Changes.**
0. New `src/Dhadgar.Nodes/Hubs/AgentHubMessageValidator.cs` (`internal static`; Nodes.Tests already sees internals, else add `InternalsVisibleTo`): `string? Validate(AgentHeartbeat h)` and `string? Validate(AgentTelemetry t)`, returning null when valid or the error code (`invalid_heartbeat`, `invalid_telemetry`). Implement exactly the two C-2 rows, using the `AgentMessageLimits` constants. VS-33 adds the overloads for `AgentCommandResult`, `AgentServerStatus` and `AgentConsoleBatch` to this file.
1. New `src/Dhadgar.Nodes/Hubs/AgentHubGroups.cs`: `public static string Node(Guid nodeId) => $"node:{nodeId:D}";`.
2. New `src/Dhadgar.Nodes/Hubs/IAgentConnectionRegistry.cs` and `AgentConnectionRegistry.cs` (singleton): `void Add(Guid nodeId, string connectionId)`, `void Remove(Guid nodeId, string connectionId)`, `bool IsConnected(Guid nodeId)`, `int ConnectedNodeCount`. A `ConcurrentDictionary<Guid, ImmutableHashSet<string>>` updated with `AddOrUpdate`.
3. New `src/Dhadgar.Nodes/Hubs/AgentHub.cs`:

```csharp
[Authorize(Policy = "AgentPolicy")]
public sealed class AgentHub(
    NodesDbContext db, IHeartbeatService heartbeats, IAgentConnectionRegistry registry, ILogger<AgentHub> logger)
    : Hub<IAgentHubClient>, IAgentHub
{
    public override async Task OnConnectedAsync()
    {
        // 1. X-Agent-Protocol header on Context.GetHttpContext() must equal AgentProtocol.Version; else log + Context.Abort(); return.
        // 2. nodeId = node_id claim; load (OrganizationId, Status) AsNoTracking; missing or Decommissioned -> Abort.
        // 3. Context.Items["OrganizationId"] = orgId; Groups.AddToGroupAsync(ConnectionId, AgentHubGroups.Node(nodeId)); registry.Add(...).
    }
    public override Task OnDisconnectedAsync(Exception? exception) { /* registry.Remove */ }

    public async Task Heartbeat(AgentHeartbeat heartbeat) { /* see 4 */ }
    public Task Telemetry(AgentTelemetry telemetry) { /* null check, node match, validator, log the metric COUNT at Debug, drop */ }
    public Task CommandResult(AgentCommandResult result) => throw new HubException("not_supported_yet");        // VS-33
    public Task ServerStatusChanged(AgentServerStatus status) => throw new HubException("not_supported_yet");   // VS-33
    public Task ConsoleOutput(AgentConsoleBatch batch) => throw new HubException("not_supported_yet");          // VS-33
}
```

4. `Heartbeat`: null argument → `invalid_heartbeat`; node mismatch → `node_mismatch`; `AgentHubMessageValidator.Validate(heartbeat)` non-null → `HubException(code)`; then map to `new HeartbeatRequest(cpu, mem, disk, heartbeat.Servers.Count(s => s.State == AgentServerState.Running), Truncate(heartbeat.AgentVersion, 50), heartbeat.Warnings.ToList())` (the 50-char truncation is the existing `HeartbeatRequest` storage limit, applied after the contract check passed, not instead of it); call `ProcessHeartbeatAsync(nodeId, request, Context.ConnectionAborted)`; failure codes `node_not_found`/`node_decommissioned` → `HubException(code)`.
5. `src/Dhadgar.Nodes/Program.cs`: `builder.Services.AddSignalR(o => { o.MaximumReceiveMessageSize = AgentProtocol.MaxHubMessageBytes; o.EnableDetailedErrors = false; });`, `builder.Services.AddSingleton<IAgentConnectionRegistry, AgentConnectionRegistry>();`, and after the endpoint maps `app.MapHub<AgentHub>(AgentProtocol.HubPath).RequireAuthorization("AgentPolicy");`.

**Tests.**
- `tests/Dhadgar.Nodes.Tests/Hubs/AgentHubTests.cs`, using `NodesWebApplicationFactory` (mTLS disabled; `TestNodesAuthHandler` turns `X-Test-User-Id` and `X-Test-Node-Id` into claims): a raw `HubConnection` to `factory.Server.BaseAddress + "hubs/agent"` with `Transports = LongPolling`, `HttpMessageHandlerFactory = _ => factory.Server.CreateHandler()`, headers `X-Test-User-Id`, `X-Test-Node-Id` = a seeded node, `X-Agent-Protocol: 1`. Cases: heartbeat with CPU 42 updates `LastHeartbeat` and health; heartbeat for another node id → `HubException` with `node_mismatch`; missing protocol header → connection closes (Closed event within 5 s or `StartAsync` throws); unknown node → closes; registry `IsConnected` true while connected and false after `DisposeAsync`.
- `tests/Dhadgar.Nodes.Tests/Hubs/AgentConnectionRegistryTests.cs`: two connections for one node, remove one, still connected; remove both, not connected.
- `tests/Dhadgar.Nodes.Tests/Hubs/AgentHubMessageValidatorTests.cs`, one `[Theory]` row per bound in the two C-2 rows, each at the limit (valid) and one past it (code): `AgentVersion` 64 and 65 chars and null; each percentage 100, 100.1, -0.1, `NaN`, `PositiveInfinity`; 64 and 65 servers; a server with `State = (AgentServerState)99`, negative `MemoryBytes`; 20 and 21 warnings; a 256- and 257-char warning; a null warning; null `Servers` and `Warnings`; 500 and 501 metrics; a 128- and 129-char metric name; a metric name with `\n`; a `NaN` metric value.
- Frame size, in `AgentHubTests` (raw `HubConnection`): `Telemetry_AtEscapedWorstCase_IsAccepted`, 500 metric names of 128 `'é'` each (escaped as `é`, about 400 KiB on the wire), completes without an error and the connection stays open. It fails with the default 32 KiB limit, so it proves `MaximumReceiveMessageSize` was raised. `Frame_OverLimit_ClosesConnection`: one `AgentHeartbeat` whose single warning is 100,000 `'é'` (about 600 KiB escaped); the connection's `Closed` event fires within 5 s. It must be the close, not `HubException("invalid_heartbeat")`, which proves the frame limit is not unlimited.
- `AgentHubTests` also: a heartbeat with 21 warnings → `HubException` whose message contains `invalid_heartbeat`, and `LastHeartbeat` unchanged.
- Seam `tests/Dhadgar.Seams.Tests/AgentHub/AgentHubThroughGatewaySeamTests.cs`: enroll through the Gateway with `SeamAgent`, then a raw Contracts-typed `HubConnection` to the **Gateway** `/hubs/agent` (long polling, `ClientCertHeaderHandler`, `X-Agent-Protocol: 1`), invoke `nameof(IAgentHub.Heartbeat)` with an `AgentHeartbeat` for the enrolled node, assert the Nodes DB updated. Rejection cases, under the dispatch rule on rejection seams:
  - `Connect_WithoutCertificate_IsRejectedByGateway`: no certificate → `StartAsync` throws `HttpRequestException` with `StatusCode == Unauthorized`, and the forwarder recorded **no** `nodes` request for `/hubs/agent`.
  - `Connect_WithCertificateFromAnotherCa_IsRejectedByNodes`: the other-CA certificate from VS-06's `NodesAgentMtlsSeamTests` → `Unauthorized`, and the forwarder **did** record a `nodes` request for `/hubs/agent/negotiate`.
  - `Connect_WithoutProtocolHeader_IsClosed`: a valid certificate without `X-Agent-Protocol` → the connection closes (`Closed` fires, or `StartAsync` throws), and the forwarder recorded the request.
  This proves Gateway forwarding plus the Nodes hub together. The agent's own client joins in VS-13.

Why they fail when reverted: there is no hub at `/hubs/agent` (404 at negotiate); without the raised receive size the worst-case telemetry closes the connection; without the validator the 21-warning heartbeat is stored.

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Dhadgar.Nodes/README.md` "Agent hub" (path, auth, protocol header, methods, error codes, single-instance note).

**Done when.** Checks pass; `AgentHubThroughGatewaySeamTests` is green.

### VS-09 Command signing on the control plane; key in the enrollment response

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/09-command-signing` / `vertical-slice-vs09` |
| Commit subject | `feat(nodes): ECDSA command signing key, signer, and signing key in the enrollment response (#94) [VS-09]` |
| Security review | Yes (key material; touches the agent's `EnrollmentService`) |
| Size | about 40k tokens |

**Goal.** Nodes owns a P-256 signing key, signs `AgentCommandEnvelope`s per C-3, and hands the public key to agents at enrollment (D-09).

**Rules.**
- Seams harness and test JWTs (D-22): mint and accept test user tokens only with `SeamJwt` (`tests/Dhadgar.Testing/Auth/SeamJwt.cs` after VS-32, which removes VS-04's duplicate minter in `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`); never add another minter or `PostConfigure<JwtBearerOptions>` test-key block. Client-certificate shims stay inside the test project that owns them. A seam that asserts a rejection proves the server answered: the status or code comes from the server's response, the forwarder or a backend recorder shows which server it reached, and a positive twin passes (dispatch rules).
- Private key bytes and PEM buffers are zeroed after use (`CryptographicOperations.ZeroMemory`); `ECDsa` instances are disposed or held by a singleton that disposes them.
- The key file is written with owner-only permissions on Unix (`File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite)` when not Windows).
- File paths come from configuration only: normalise with `Path.GetFullPath`, reject relative `CommandSigning:KeyPath` and any path containing `..` segments.
- The agent rejects a malformed signing key at enrollment with a `Result` failure; no exceptions escape.
- `Dhadgar.Contracts.csproj` gets no package references; new `.cs` content is CRLF.
- Contract excerpt: C-3 (`AgentCommandSigning`, including the three constants this packet adds), C-4 (`EnrollNodeResponse` with `CommandSigningPublicKey`, `CommandSigningKeyId`), C-6 `CommandSigning:*`, and `AgentMessageLimits.MaxCommandLifetimeSeconds` (C-2).

**Changes.**
1. `src/Shared/Dhadgar.Contracts/Nodes/EnrollmentContracts.cs`: append `string CommandSigningPublicKey, string CommandSigningKeyId` to `EnrollNodeResponse` with XML docs (C-4). Fix every constructor call the compiler then reports (Nodes and tests).
1a. Contract constants (C-2, C-3; security-2 found these fields unbounded). Append, with XML docs in the style of the neighbours, keeping CRLF:
   - `src/Shared/Dhadgar.Contracts/Agents/AgentProtocol.cs`, class `AgentMessageLimits`: `public const int MaxErrorCodeLength = 64;` and `public const string ErrorCodePattern = "^[A-Za-z0-9._-]{1,64}$";` (bounds `AgentCommandResult.ErrorCode` and `AgentCommandCompleted.ErrorCode`; the agent's `[Category.Code]` codes such as `Java.NoRuntimeForPlatform` match it).
   - `src/Shared/Dhadgar.Contracts/Agents/AgentCommandSigning.cs`: `public const int SignatureBytes = 64;`, `public const int SignatureBase64Length = 88;`, `public const int KeyIdLength = 64;`.
   - Do not change any existing member.
2. New `src/Dhadgar.Nodes/Services/CommandSigning/CommandSigningOptions.cs` (section `CommandSigning`: `string? KeyPath`, `int CommandTtlSeconds = 60` with `[Range(10, AgentMessageLimits.MaxCommandLifetimeSeconds)]`, i.e. 10..60; a TTL above 60 would make every conforming agent reject every command, C-2 acceptance rule 3).
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

   Key source: `CommandSigning:KeyPath` if set (PEM, `-----BEGIN PRIVATE KEY-----` or `EC PRIVATE KEY`, must be a P-256 key); otherwise `command-signing-key.pem` in the same directory `LocalFileCaStorageProvider` uses (read it to reuse its directory resolution), created with `ECDsa.Create(ECCurve.NamedCurves.nistP256)` and `ExportPkcs8PrivateKeyPem()` on first start. `Sign` uses `AgentCommandSigning.BuildSigningInput(unsigned)` and `SignData(..., HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)`.
4. `src/Dhadgar.Nodes/Program.cs`: bind `CommandSigningOptions` with validation; register `ICommandSigner`; call `InitializeAsync` at startup right after the CA initialization, with the same try/log pattern.
5. `src/Dhadgar.Nodes/Endpoints/AgentEndpoints.cs` `Enroll`: inject `ICommandSigner` and fill the two new fields.
6. Agent: `src/Agents/Dhadgar.Agent.Core/Authentication/EnrollmentService.cs`: after deserializing the response, check `CommandSigningPublicKey` (base64, imports with `ECDsa.ImportSubjectPublicKeyInfo`, `KeySize == 256`) and that `AgentCommandSigning.ComputeKeyId(spki) == CommandSigningKeyId`; otherwise return `Result.Failure("[Enrollment.InvalidSigningKey] Control plane returned an invalid command signing key")` **before** storing the certificate. Add `CommandSigningPublicKey` and `CommandSigningKeyId` to `EnrollmentResult`. Persisting them is VS-12.
7. `tests/Dhadgar.Nodes.Tests/TestCertificateAuthorityService.cs` and `NodesWebApplicationFactory.cs`: the factory needs a signer. Register the real `CommandSigner` with `CommandSigning:KeyPath` pointing at a key generated into a per-factory temp file, or a `TestCommandSigner` that wraps a fresh in-memory key. Choose the smaller change and say which in the report.

**Tests.**
- `tests/Dhadgar.Nodes.Tests/CommandSigning/CommandSignerTests.cs`: a signature verifies with the public key over `AgentCommandSigning.BuildSigningInput`; the signature string is exactly `SignatureBase64Length` chars and decodes to `SignatureBytes`; `KeyId == ComputeKeyId(spki)` and has `KeyIdLength` chars; a second signer over the same directory loads the same key (same `KeyId`); `KeyPath` with a P-384 key fails initialization; a relative `KeyPath` fails validation; `CommandTtlSeconds` 61 and 9 fail options validation, 60 and 10 pass.
- `tests/Dhadgar.Contracts.Tests/Agents/AgentMessageLimitsTests.cs`: pin `MaxErrorCodeLength`, `ErrorCodePattern` (`Java.NoRuntimeForPlatform` and `AgentBusy` match; `a b`, `x\ny`, 65 chars and the empty string do not). `AgentCommandSigningTests.cs`: `Convert.ToBase64String(new byte[SignatureBytes]).Length == SignatureBase64Length` and `ComputeKeyId(...)` of the golden SPKI has `KeyIdLength` chars.
- `tests/Dhadgar.Nodes.Tests/Integration/AgentEnrollmentContractTests.cs`: extend `RealAgent_EnrollsAgainstRealNodes...` to assert `result.Value.CommandSigningKeyId` equals the factory signer's `KeyId`.
- `tests/Dhadgar.Agent.Core.Tests/Authentication/EnrollmentServiceSigningKeyTests.cs`: a stub `HttpMessageHandler` returns an enrollment response; a malformed key → failure with `InvalidSigningKey` and the cert store untouched; a key-id mismatch → failure; a valid key → success, with the fields on `EnrollmentResult`.
- Seams `AgentEnrollmentThroughGatewaySeamTests`: assert the agent received the Nodes signer's `KeyId`.

Why they fail when reverted: the response lacks the key fields (compile failure) and the agent has no key to verify commands with.

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet test tests/Dhadgar.Contracts.Tests/Dhadgar.Contracts.Tests.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `docs/runbooks/nodes-service-operations.md`: the key file, backup with the CA, `CommandSigning:*` keys, "rotation not supported in MVP". ADR-0007: a dated note under its divergence note that signing is implemented per D-09.

**Done when.** Checks pass.

### VS-10 Certificate renewal on the agent-held-key path

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/10-cert-renewal` / `vertical-slice-vs10` (from `origin/main` after VS-06, VS-07, VS-09) |
| Commit subject | `fix(agents): align certificate renewal on agent-generated keys; drop server-generated PKCS#12 [VS-10]` |
| Security review | Yes |
| Size | about 35k tokens |

**Goal.** F-07: renewal uses one contract (C-4); Nodes signs the agent's new public key and returns PEM; server-side private-key generation goes away.

**Rules.**
- Seams harness and test JWTs (D-22): mint and accept test user tokens only with `SeamJwt` (`tests/Dhadgar.Testing/Auth/SeamJwt.cs` after VS-32, which removes VS-04's duplicate minter in `tests/Dhadgar.Gateway.Tests/HubTokenTests.cs`); never add another minter or `PostConfigure<JwtBearerOptions>` test-key block. Client-certificate shims stay inside the test project that owns them. A seam that asserts a rejection proves the server answered: the status or code comes from the server's response, the forwarder or a backend recorder shows which server it reached, and a positive twin passes (dispatch rules).
- The current certificate comes from the mTLS identity (claim `certificate_thumbprint`), never from the request body.
- Private key bytes zeroed after storing (the existing pattern in `EnrollmentService` stays).
- `Result<T>` on the agent side; ProblemDetails on the Nodes side.
- Contract excerpt: `RenewCertificateRequest(string PublicKey)`, `RenewCertificateResponse(string CertificateThumbprint, string Certificate, DateTimeOffset NotBefore, DateTimeOffset NotAfter)`; route row `POST /api/v1/agents/{nodeId}/certificates/renew` (C-1).

**Changes.**
1. `src/Shared/Dhadgar.Contracts/Nodes/EnrollmentContracts.cs`: replace the two renewal records with the C-4 versions.
2. `src/Dhadgar.Nodes/Endpoints/AgentEndpoints.cs` `RenewCertificate`: bind `Dhadgar.Contracts.Nodes.RenewCertificateRequest`; a null `PublicKey` or one longer than 4096 chars → 400 `invalid_public_key` **before** base64 decoding (C-2 "Limit enforcement", last row); take the thumbprint from `context.User.FindFirst("certificate_thumbprint")` (missing → 400 `certificate_thumbprint_unavailable`); load the current non-revoked `AgentCertificate` for that node **with that thumbprint** (none → 401 `certificate_not_current`); call `caService.IssueCertificateForPublicKeyAsync(nodeId, request.PublicKey, ct)` (failure `invalid_public_key` → 400); keep the record insert, revocation and the three events; return the C-4 response with `Certificate = certResult.CertificatePem`. Delete the `Local*` renewal aliases and the local renewal records in `src/Dhadgar.Nodes/Models/EnrollmentModels.cs` if nothing else uses them.
3. `src/Dhadgar.Nodes/Services/ICertificateAuthorityService.cs`, `CertificateAuthorityService.cs`, `tests/Dhadgar.Nodes.Tests/TestCertificateAuthorityService.cs`: delete `RenewCertificateAsync`. If `git grep -n "IssueCertificateAsync(" src` shows no caller outside the CA service, delete `IssueCertificateAsync` and its private-key/PKCS#12 helpers too (no server-generated agent keys remain). Remove or adapt tests that covered them.
4. Agent `src/Agents/Dhadgar.Agent.Core/Authentication/EnrollmentService.cs` `RenewCertificateAsync`: send `new RenewCertificateRequest(Convert.ToBase64String(publicKeyBytes))`; deserialize `Dhadgar.Contracts.Nodes.RenewCertificateResponse`; decode with the existing `DecodeCertificate` (PEM); keep the key-match check and zeroing. Delete the agent-local `CertificateRenewalRequest` and `CertificateRenewalResponse` classes.

**Tests.**
- `tests/Dhadgar.Nodes.Tests/Integration/AgentApiIntegrationTests.cs` (or a new `CertificateRenewalEndpointTests.cs`): with the factory's test auth adding `certificate_thumbprint` (extend `TestNodesAuthHandler` with header `X-Test-Cert-Thumbprint`): a matching thumbprint and a valid key → 200 with PEM bound to that key, old cert revoked; a thumbprint that is not current → 401; a malformed key → 400; a 4097-char `PublicKey` → 400 `invalid_public_key` with no CA call (add a call counter to `TestCertificateAuthorityService` if it has none), and a 4096-char one reaches the CA (then fails as malformed).
- `tests/Dhadgar.Agent.Core.Tests/Authentication/EnrollmentServiceRenewalTests.cs`: the request body JSON has exactly `publicKey`; a PEM response is accepted and stored; a response for a different key is rejected with `[Renewal.KeyMismatch]`.
- Seam `tests/Dhadgar.Seams.Tests/Enrollment/CertificateRenewalSeamTests.cs`: enroll through the Gateway, call the real `EnrollmentService.RenewCertificateAsync` (agent `NodeId` set from the enrollment result through `Options.Create`; client `ControlPlaneMtls` carries the cert header), then assert the store's new cert key matches the stored private key, and in the Nodes DB the old certificate is revoked and a new one is active.

Why they fail when reverted: the agent and Nodes disagree on both request and response shapes, so renewal fails with 400 or deserializes nothing.

**Checks (in order).**

```bash
dotnet build src/Dhadgar.Nodes/Dhadgar.Nodes.csproj -c Release
dotnet build src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

**Docs owed.** `src/Dhadgar.Nodes/README.md` renewal section; `docs/runbooks/nodes-service-operations.md` if it describes PKCS#12 renewal.

**Done when.** Checks pass; `CertificateRenewalSeamTests` is green.

### VS-11 Import Servers from PR #88

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/11-servers-import` / `vertical-slice-vs11` |
| Commit subject | `feat(servers): import Servers service implementation from PR #88 (0761b97) [VS-11]` |
| Security review | Yes (service-level auth on a promoted service, DV-13) |
| Size | about 35k tokens |

**Goal.** D-02: Servers' implementation from #88 lands on current `main`, builds under CI rules, authenticates users itself (VS-04 helper), and its tests pass. No new behaviour; placement and lifecycle come in VS-22 and VS-23.

**Rules.**
- Import file contents with `git show 0761b97:<path> > <path>`; never `git checkout`, `git cherry-pick` or `git restore`. Ops has fetched `refs/pull/88/head` as `origin/pr-88`; check `git rev-parse origin/pr-88` prints `0761b97fba08f62b75925b0772c3b3364b699c77`, else return `blocked` with reason `tool`.
- Do **not** import `src/Shared/Dhadgar.ServiceDefaults/MultiTenancy/TenantScopedAuthorization.cs` (VS-04 rebuilt it) or `src/Shared/Dhadgar.ServiceDefaults/RabbitMqOptions.cs` (F-17).
- Do not import anything for Mods, Console or the agents.
- No `Version=` in `.csproj`; every package #88's csproj references must already be pinned in `Directory.Packages.props` (FluentValidation 12.1.1 is); if one is not, return `blocked` with reason `spec`.

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

   Files under `src/Dhadgar.Servers` that exist on `main` but not in #88 stay as they are (check with `git status`; there should be none besides `packages.lock.json`, which you regenerate).
2. New `src/Shared/Dhadgar.ServiceDefaults/Messaging/RabbitMqOptions.cs`, namespace `Dhadgar.ServiceDefaults.Messaging`: a copy of `src/Dhadgar.Nodes/RabbitMqOptions.cs` (empty `Username`/`Password` defaults, `[Required]`). In `src/Dhadgar.Servers/Program.cs` add `using Dhadgar.ServiceDefaults.Messaging;`. Do not touch Nodes' copy (a follow-up can dedupe).
3. `src/Dhadgar.Servers/Program.cs`: it already calls `AddTenantScopedAuthorization(builder.Configuration, builder.Environment)`, which now resolves to VS-04's helper. Add `Auth:Issuer`, `Auth:Audience` and `Auth:ClockSkewSeconds` to `src/Dhadgar.Servers/appsettings.json` with the Gateway's values.
4. Lock files for `src/Dhadgar.Servers` and `tests/Dhadgar.Servers.Tests`, plus any project that references `Dhadgar.Shared` or `Dhadgar.Contracts` whose lock file changes (dispatch rules).
5. Make it build under `CI=true`: #88's last commits fixed CI analyzer errors, but `main` has moved since. Fix only what the compiler and analyzers report, in the imported Servers files. If a fix would need a change outside `src/Dhadgar.Servers`, `tests/Dhadgar.Servers.Tests` or the files listed above, stop and report it.
6. `tests/Dhadgar.Servers.Tests/ServersWebApplicationFactory.cs`: if its tests relied on #88's own JwtBearer registration, switch to a test scheme as `NodesWebApplicationFactory` does (default authenticate and challenge schemes overridden). If #88 brought its own test-token minter (a signing key plus `PostConfigure<JwtBearerOptions>`), delete it and use the test scheme; do not keep a third JWT helper (D-22). `DhadgarDbContext` changes are additive; other services' tests must stay green.

**Tests.** #88's Servers tests (endpoints, lifecycle, templates, validators) come with the import and must pass. Add `tests/Dhadgar.Servers.Tests/Auth/ServersAuthTests.cs`: `GET /organizations/{org}/servers` without credentials → 401; with a test identity for another org → 403. This fails when reverted because #88's endpoints would no longer be protected by the shared helper.

**Checks (in order).**

```bash
git rev-parse origin/pr-88
dotnet restore Dhadgar.sln --locked-mode
dotnet build src/Dhadgar.Servers/Dhadgar.Servers.csproj -c Release
dotnet test tests/Dhadgar.Servers.Tests/Dhadgar.Servers.Tests.csproj -c Release
dotnet test tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release
dotnet test tests/Dhadgar.Identity.Tests/Dhadgar.Identity.Tests.csproj -c Release
```

(Identity and Nodes guard the shared `DhadgarDbContext` change.)

**Docs owed.** `src/Dhadgar.Servers/README.md`: keep #88's text, add a status line "Imported from PR #88 (D-02); placement and agent dispatch arrive in VS-22/VS-23".

**Done when.** Checks pass.

### VS-32 One test JWT helper; access tokens never reach traces

| Field | Value |
| --- | --- |
| Branch / worktree | `vs/32-test-jwt` / `vertical-slice-vs32` |
| Commit subject | `test(shared): single test JWT helper in Dhadgar.Testing; prove access_token never reaches traces [VS-32]` |
| Security review | Yes (token hygiene in shared telemetry) |
| Depends on | wave 1 (VS-04, VS-05); wave 2 stage 1 |
| Size | about 30k tokens |

**Goal.** D-22: VS-04's private test-token minter (`HubTokenWebApplicationFactory`, a fixed key plus its own `PostConfigure<JwtBearerOptions>`) and VS-05's `SeamJwt` converge on one helper, `SeamJwt`, in a shared test-support library. This packet removes the duplicate. It also closes VS-04 review-1's open item: hub requests carry the user JWT in `?access_token=`, and nothing proves that the OpenTelemetry ASP.NET Core and HttpClient instrumentation keep it out of exported traces. It also adds the first seam that sends a `SeamJwt` user token through the Gateway into Nodes.

**Rules.**
- No `Version=` in any `.csproj` (Central Package Management, `Directory.Packages.props`); every package referenced must already be pinned, else return `blocked` with reason `spec`.
- `tests/Dhadgar.Testing` is a library, not a test project: `IsPackable` false, `IsTestProject` false, no xUnit reference, and **no client-certificate shims** (dispatch rule). It holds `SeamJwt` only.
- Never log or assert-print a token; tests compare with `Assert.DoesNotContain(token, ...)` and never write the token to output.
- Change `src/Shared/Dhadgar.ServiceDefaults` only if the redaction test (change 5) fails without the change. If it passes as is, the packet ships the test as a guard and changes no production code.
- Lock files and line endings per the dispatch rules (new `.cs`, `.csproj`, `.json` CRLF; `Dhadgar.sln` stays LF).
- Nullable enabled, warnings as errors (`CI=true`).

**Changes.**
1. New `tests/Dhadgar.Testing/Dhadgar.Testing.csproj` (target framework and common properties copied from `tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj`): `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, `<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />`, `<PackageReference Include="System.IdentityModel.Tokens.Jwt" />`. `dotnet sln Dhadgar.sln add` it in the test projects' solution folder. Create its lock file.
2. `git mv tests/Dhadgar.Seams.Tests/Infrastructure/SeamJwt.cs tests/Dhadgar.Testing/Auth/SeamJwt.cs`, then change only the namespace to `Dhadgar.Testing.Auth`. The class keeps its name, members, per-process random key, issuer `https://seams.test/identity` and audience `meridian-api`.
3. `tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj`: `<ProjectReference Include="..\Dhadgar.Testing\Dhadgar.Testing.csproj" />` and `<Using Include="Dhadgar.Testing.Auth" />`. No `.cs` caller changes: `NodesSeamFactory` and `GatewaySeamFactory` (edited in parallel by VS-06 and VS-07) keep calling `SeamJwt.Apply(services)`.
4. `tests/Dhadgar.Gateway.Tests`: the same `ProjectReference` and `<Using>`. In `HubTokenTests.cs`, `HubTokenWebApplicationFactory` replaces its `PostConfigure<JwtBearerOptions>` block with `SeamJwt.Apply(services);` and deletes `KeyBytes`, `CreateUserToken()` and the `Issuer`/`Audience` constants once nothing uses them. Callers use `SeamJwt.CreateUserToken(Guid.NewGuid(), Guid.NewGuid())`. Keep `CapturingLoggerProvider` and the log assertions (VS-04 security N1).
5. Trace redaction test, `tests/Dhadgar.Gateway.Tests/AccessTokenTraceRedactionTests.cs` (uses `HubTokenWebApplicationFactory`): register an `ActivityListener` before the request (`ShouldListenTo = _ => true`, `Sample = AllDataAndRecorded`, `ActivityStopped` adds each activity to a `ConcurrentBag`; dispose it after). Send `GET /hubs/console/negotiate?negotiateVersion=1&access_token={token}` with a fresh `SeamJwt` token.
   - Positive twin `HubRequest_IsTraced`: at least one stopped activity from source `Microsoft.AspNetCore` has a `url.path` (or `http.target`) tag containing `/hubs/console/negotiate`. This proves the instrumentation ran in the test host. If no such activity exists because tracing is not registered under the `Testing` environment, return `blocked` with reason `spec` and quote what you found in `ServiceDefaultsExtensions.cs` around lines 104 and 435.
   - `AccessToken_NeverAppearsInTraceTags`: no tag value and no `DisplayName` of any captured activity (ASP.NET Core and the forwarded `System.Net.Http` request) contains the token string.
   - Prove it red once (D-13 style, quote the failing line in the report): set the instrumentation's documented switch that disables URL query redaction for the run (find the exact environment variable name in the README or CHANGELOG of `opentelemetry.instrumentation.aspnetcore` 1.14.0 and `opentelemetry.instrumentation.http` 1.14.0 under `~/.nuget/packages`). If the test fails **without** that switch, fix it: in `src/Shared/Dhadgar.ServiceDefaults/ServiceDefaultsExtensions.cs`, at both `AddAspNetCoreInstrumentation()` calls, set `EnrichWithHttpRequest` so that when the query contains `access_token`, the `url.query` tag is rewritten with that value replaced by `Redacted`. Do the same for `url.full` in the HttpClient instrumentation options if the forwarded request leaks. Put the helper in one internal static method with its own unit test in `tests/Dhadgar.ServiceDefaults.Tests`.
6. New seam `tests/Dhadgar.Seams.Tests/Auth/UserJwtThroughGatewaySeamTests.cs` (first real use of `SeamJwt` through the Gateway into Nodes; VS-05 review-1):
   - `UserToken_ThroughGateway_ReachesNodes`: `GET /api/v1/organizations/{O}/nodes` through the Gateway with `SeamJwt.CreateUserToken(user, O)` → 200, the body deserializes as `PagedNodesResponse`, and `host.Forwarder.ForwardedClusters` contains `nodes`.
   - `TokenForOtherOrganization_IsRejectedByNodes` (rejection twin): the same request with a token for org P → 403. The Gateway's `TenantScoped` policy only requires an authenticated user (`src/Dhadgar.Gateway/Program.cs`), so the Gateway forwards, and the 403 must come from Nodes' tenant check: assert the status **and** that `ForwardedClusters` contains `nodes` (a fresh `SeamHost` per test). If Nodes answers something other than 403, assert that exact status and say so in the report.
7. Docs: `tests/Dhadgar.Seams.Tests/README.md` gets one line saying where `SeamJwt` lives now. New `tests/Dhadgar.Testing/README.md` (5-10 lines): what belongs here (shared test helpers with no service references), what never does (certificate shims, service factories). If change 5 touched ServiceDefaults: `src/Shared/Dhadgar.ServiceDefaults/README.md` gets a "Telemetry: query redaction" note.

**Tests.** Changes 5 and 6. The Gateway hub-token tests and the seams enrollment test must stay green after the move. They fail when reverted: without the `<Using>` and the reference, nothing compiles; without redaction (if it was needed), `AccessToken_NeverAppearsInTraceTags` fails.

**Checks (in order).**

```bash
dotnet restore Dhadgar.sln --locked-mode
dotnet build tests/Dhadgar.Testing/Dhadgar.Testing.csproj -c Release
dotnet test tests/Dhadgar.Gateway.Tests/Dhadgar.Gateway.Tests.csproj -c Release
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
dotnet test tests/Dhadgar.ServiceDefaults.Tests/Dhadgar.ServiceDefaults.Tests.csproj -c Release   # only if ServiceDefaults changed
```

**Docs owed.** Change 7.

**Done when.** Checks pass; the report states whether redaction was already on (test green without a code change) or needed the `EnrichWithHttpRequest` change, and quotes the red run.

---

## Wave 3: agent runtime, command pipeline, Console import

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
3. New `Authentication/IAgentIdentityStore.cs` and `FileAgentIdentityStore.cs`: `Result<AgentIdentity?> Load()` (missing file → `Success(null)`; unreadable or invalid JSON → `Failure("[Identity.Corrupt] ...")`), `Task<Result> SaveAsync(AgentIdentity identity, CancellationToken ct)` (creates the directory), `Task<Result> ClearAsync(CancellationToken ct)`. File `Path.Combine(options.StatePath, "agent-identity.json")`, `System.Text.Json` with `AgentJson.Options`. Those options are strict (C-2 JSON rules: every positional parameter without a default is required, unknown properties throw), so a field added to `AgentIdentity` later must have a default value, or every agent with an older file would report `[Identity.Corrupt]`. Say so in the record's XML doc.
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
- Only `AgentCommandTypes.IsKnown` types. The payload is validated as the typed record for its command type with `AgentJson.Options` (strict) and against the C-2 limits **before** it is signed (C-2 "Limit enforcement", typed-payloads row). Nodes signs exactly the `PayloadJson` string it validated.
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
- `AgentCommands/AgentCommandPayloadValidatorTests.cs`, one row per bound, at the limit and one past it: payload of exactly 64 KiB (padded inside a valid `Nonce`-free JSON, e.g. whitespace) accepted and 64 KiB + 1 rejected; `{}` is a valid Ping; `{"nonce":"<64 chars>"}` accepted, 65 rejected; `StartServer` with an unknown property, a missing property, `"port":1023`, `"port":65536`, `"memoryMb":511`, `"memoryMb":32769`, a 39-char SHA-1, `http://` jar URL, a URL with user info, version `1.21.11/../x`; `StopServer` with `gracefulTimeoutSeconds` 0 and 301 rejected, 1 and 300 accepted; `ServerId` mismatch with the request; `Ping` with a request `ServerId`.
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
- Contracts: `Dhadgar.Contracts.csproj` gets no package references. Change no C-2 record except appending the one optional parameter to `StartServerPayload`. It **must** have the `= null` default: `AgentJson` requires every parameter without one (C-2 JSON rules). Agents built before this packet reject a payload carrying `javaRuntimes`, which is acceptable only under D-21's upgrade order. Nodes' strict pre-sign parse (VS-15) accepts the new parameter with no Nodes change, and the count limit `MaxPerPayload` is the agent's check (C-7 rule 3). Before appending, `grep -rn "StartServerPayload" src tests` and check that nobody deconstructs it (`var (` on it); report any hit.
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

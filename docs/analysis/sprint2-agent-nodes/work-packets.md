# sprint2-agent-nodes: work packets

Ledger and decisions: [README.md](README.md). Resume card: [RESUME.md](RESUME.md).

## Dispatch rules

- **One PR for the whole campaign (owner rule, D-S215).** Every packet, in every wave, P15 included, lands on the single local campaign branch `campaign/sprint2-agent-nodes-w1` (worktree `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-w1`). Nothing is pushed and no PR is opened until the campaign review is at `ship` and P15 has merged into w1. There is no `-w2` branch and no per-packet or per-wave PR.
- Each packet runs in its own worktree, `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-pNN`, branched from the **current tip of `campaign/sprint2-agent-nodes-w1`** at dispatch time.
- `<wt>` below always means that packet's worktree root.
- Branch names are `s2/pNN-<slug>`. The packet-coder commits locally in its worktree only; it never pushes.
- Do not start a packet until every packet in its **Depends on** list has been merged locally into `campaign/sprint2-agent-nodes-w1`. "Merged" in a **Depends on** line always means that.
- After a packet's review loop (and `security-advisor` pass) ends at `ship`, a Sonnet `general-purpose` agent merges its `s2/pNN-*` branch locally into `campaign/sprint2-agent-nodes-w1`, in the wave's merge order, and records the new w1 tip. ops does not do local merges.
- Every packet except P15 is security-review: a `security-advisor` pass runs beside the reviewer, and the one campaign PR gets the `security-review` label.
- Checks run in the order given, scoped to the projects named. A check that fails after 5 fix rounds ends the session as `blocked`.
- No Docker is needed in waves 1 and 2. Every test there uses `WebApplicationFactory` with EF InMemory. Wave 2U (the Windows UAT) needs Docker Desktop and Windows; its own rules are at the top of that wave.
- A packet that writes a **red** seam test commits it red. Record the failing test names and the first line of each failure message in the report. Every other test in the project must pass.
- Do not edit `docs/PROJECT-STATE.md`; P15 owns it.
- End every commit message with:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Wz3WiSWFFL9NvwfvRqhrt
```

### Repo rules (each packet repeats the ones it needs)

| Key | Rule |
| --- | --- |
| R-RESULT | Return `Result` / `Result<T>` (`Dhadgar.Shared.Results`) for validation, IO and network failures; never throw for them. Error strings use the existing `"[Area.Code] message"` style. |
| R-PATH | Validate every file path with `IPathValidator.ValidatePath(path, allowedBasePaths)` (`Dhadgar.Agent.Core.Files`) before use. |
| R-ZERO | Zero key material with `CryptographicOperations.ZeroMemory` straight after use, in a `finally`. |
| R-USING | Use `using` on every `IDisposable`: `X509Certificate2`, `ECDsa`, `HttpResponseMessage`, `X509Store`, `HubConnection` (await `DisposeAsync`). |
| R-CPM | No `Version=` in any `.csproj`. Versions live only in `Directory.Packages.props`, and this campaign needs no new package versions. |
| R-NULL | Nullable is enabled and CI builds with warnings as errors. Do not suppress analyzers unless the reason is written in a comment. |
| R-CONTRACT | Cross-boundary types come only from `Dhadgar.Contracts.Nodes`. Never make a local copy on either side. |
| R-XUNIT | xUnit v2 `[Fact]` / `[Theory]` and plain `Assert`. Nodes integration tests use `[Collection("Nodes Integration")]` and `NodesWebApplicationFactory`. Test names follow `Subject_Condition_Expectation`. |
| R-HTTPS | The control-plane endpoint must be HTTPS (the existing check in `ControlPlaneClient`). The agent never follows redirects (`AllowAutoRedirect = false` stays). |
| R-LOG | Never log key material, tokens or the whole certificate. Thumbprints and node IDs are fine. |

## Contract

Everything in C1, C2, C3 and C3a lives in `src/Shared/Dhadgar.Contracts/Nodes/`. P01 creates C1 and C2 exactly as written. P09 replaces the renewal pair (C3). P16 creates C3a.

### C1. Agent hub: `AgentHubContracts.cs`

```csharp
namespace Dhadgar.Contracts.Nodes;

/// <summary>Names shared by the agent and Nodes for the agent hub (ADR-0008).</summary>
public static class AgentHubProtocol
{
    /// <summary>Hub path. Identical at the Gateway and in Nodes (no prefix transform).</summary>
    public const string Path = "/hubs/agent";

    /// <summary>Request header carrying the hub protocol version (#116).</summary>
    public const string VersionHeader = "X-Agent-Protocol";

    /// <summary>The only version Nodes accepts. A missing header is treated as this value.</summary>
    public const string CurrentVersion = "1";

    /// <summary>Claim that carries the node ID taken from the client certificate's SPIFFE ID.</summary>
    public const string NodeIdClaim = "node_id";

    /// <summary>SignalR group holding every connection of one node.</summary>
    public static string NodeGroup(Guid nodeId) => $"node:{nodeId:D}";
}

/// <summary>Values of <see cref="AgentHeartbeatAck.ErrorCode"/>.</summary>
public static class AgentHubErrorCodes
{
    public const string NodeNotFound = "node_not_found";
    public const string NodeDecommissioned = "node_decommissioned";
    public const string InvalidHeartbeat = "invalid_heartbeat";
    public const string HeartbeatFailed = "heartbeat_failed";
}

/// <summary>Limits Nodes enforces on <see cref="AgentHeartbeat"/>. The agent clamps to them before sending.</summary>
public static class AgentHeartbeatLimits
{
    public const int MaxAgentVersionLength = 50;
    public const int MaxHealthIssues = 20;
    public const int MaxHealthIssueLength = 256;
    public const int MaxActiveGameServers = 10_000;
}

/// <summary>Server methods the agent invokes. Implemented by Nodes' AgentHub.</summary>
/// <remarks>CommandResult and Telemetry are deferred to the commands campaign (D-S29).</remarks>
public interface IAgentHub
{
    /// <summary>Reports node health. The node is identified by the connection's certificate, never by the payload.</summary>
    Task<AgentHeartbeatAck> Heartbeat(AgentHeartbeat heartbeat);
}

/// <summary>Client methods Nodes invokes on the agent.</summary>
public interface IAgentHubClient
{
    /// <summary>A serialized CommandEnvelope. The shape and signing are owned by the commands campaign (#94).</summary>
    Task ReceiveCommand(string commandEnvelopeJson);

    Task ReceivePing(string message);
}

/// <summary>One heartbeat. Percentages are 0-100.</summary>
public sealed record AgentHeartbeat(
    double CpuUsagePercent,
    double MemoryUsagePercent,
    double DiskUsagePercent,
    int ActiveGameServers,
    string? AgentVersion,
    IReadOnlyList<string>? HealthIssues);

/// <summary>Reply to <see cref="IAgentHub.Heartbeat"/>. On failure, Acknowledged is false and ErrorCode is one of <see cref="AgentHubErrorCodes"/>.</summary>
public sealed record AgentHeartbeatAck(
    bool Acknowledged,
    DateTimeOffset ServerTime,
    string? ErrorCode = null);
```

Wire format is the SignalR JSON hub protocol with System.Text.Json web defaults (camelCase) on both sides. The method names on the wire are `nameof(IAgentHub.Heartbeat)`, `nameof(IAgentHubClient.ReceiveCommand)` and `nameof(IAgentHubClient.ReceivePing)`. Neither side writes a method name as a string literal.

### C2. Certificate profile: `AgentCertificateProfile.cs`

```csharp
namespace Dhadgar.Contracts.Nodes;

/// <summary>
/// What an agent client certificate looks like. Nodes' CA issues to this profile, Nodes' mTLS
/// validates against it, the Gateway checks the issuer name, and the Windows store finds certs by it.
/// </summary>
public static class AgentCertificateProfile
{
    public const string SpiffeTrustDomain = "meridianconsole.com";
    public const string SpiffeNodeIdPrefix = "spiffe://meridianconsole.com/nodes/";
    public static string SpiffeId(Guid nodeId) => $"{SpiffeNodeIdPrefix}{nodeId:D}";

    /// <summary>O= of the subject; the subject is CN={nodeId:D}, O=MeridianConsole.</summary>
    public const string Organization = "MeridianConsole";

    /// <summary>CN of the agent CA (issuer of every agent client certificate).</summary>
    public const string CaCommonName = "Meridian Console Agent CA";

    public const int ValidityDays = 90;

    /// <summary>The agent renews when fewer than this many days remain.</summary>
    public const int RenewalThresholdDays = 30;

    /// <summary>Gateway → Nodes header carrying the TLS client certificate as base64 DER (D-S25).</summary>
    public const string ForwardedCertificateHeader = "X-Client-Cert";
}
```

### C3. Renewal pair, replacing the existing records in `EnrollmentContracts.cs` (P09)

```csharp
/// <summary>
/// Request from an agent to renew its client certificate (<c>POST /agents/{nodeId}/certificates/renew</c>,
/// public path <c>/api/v1/agents/{nodeId}/certificates/renew</c>). Sent over mTLS with the current
/// certificate; the current thumbprint is taken from that certificate, not from the body.
/// </summary>
/// <param name="PublicKey">Base64 DER SubjectPublicKeyInfo of a NEW key pair generated on the agent.</param>
public record RenewCertificateRequest(string PublicKey);

/// <summary>Response to a successful renewal. No private key or PKCS#12 is ever returned.</summary>
/// <param name="CertificateThumbprint">SHA-256 thumbprint of the new certificate.</param>
/// <param name="Certificate">PEM-encoded new certificate, bound to the request's public key.</param>
/// <param name="CaCertificate">PEM-encoded agent CA certificate.</param>
/// <param name="NotBefore">Validity start.</param>
/// <param name="NotAfter">Validity end.</param>
public record RenewCertificateResponse(
    string CertificateThumbprint,
    string Certificate,
    string CaCertificate,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter);
```

### C3a. Renewal grace policy: `AgentCertificateRenewalPolicy.cs` (P16, D-S212)

```csharp
namespace Dhadgar.Contracts.Nodes;

/// <summary>
/// Timing both sides of certificate renewal rely on (D-S212). After a renewal, Nodes keeps the
/// presented certificate usable for a short grace window, so an agent whose renewal response was
/// lost can renew again with it. The agent retries a failed renewal well inside that window.
/// </summary>
public static class AgentCertificateRenewalPolicy
{
    /// <summary>Default of Nodes' Mtls:RenewalGraceMinutes. 0 there means immediate revoke.</summary>
    public const int DefaultGraceMinutes = 60;

    /// <summary>Upper bound Nodes accepts for Mtls:RenewalGraceMinutes.</summary>
    public const int MaxGraceMinutes = 1440;

    /// <summary>Default delay before the agent retries a failed renewal. Must stay well below DefaultGraceMinutes.</summary>
    public const int AgentRetryMinutes = 5;
}
```

Grace rules (Nodes side, P16):

- A certificate row is **usable** when `!IsRevoked || (GraceUntil is { } g && now < g)`. `AgentCertificateState.IsUsable(AgentCertificate, DateTime utcNow)` in `src/Dhadgar.Nodes/Services/` is the only implementation; mTLS validation (`CertificateAuthorityService.ValidateCertificateAsync`), the hub's connect and per-heartbeat check (`AgentHub.IsCertificateCurrentAsync`) and the renew endpoint all call it (wave1-ruling-1).
- Renewal sets, on the presented certificate: `IsRevoked = true`, `RevokedAt ??= now`, `RevocationReason = "Renewed - superseded by new certificate"`, and `GraceUntil = presented.IsRevoked ? presented.GraceUntil : (grace > 0 ? now + grace : null)`. Grace is never extended.
- On every **other** row of the node that is not revoked, or has a `GraceUntil`: `IsRevoked = true`, `RevokedAt ??= now`, `GraceUntil = null`, `RevocationReason = "Superseded by a later renewal"` when it was not revoked.
- Decommission and any other revoke set `GraceUntil = null`.
- Events are unchanged: `AgentCertificateRenewed` and `AgentCertificateRevoked` for the presented certificate.

### C4. Routes: what the Gateway exposes and what Nodes maps

| Call | Agent calls (Gateway) | Gateway route, policy, rate limit, transform | Nodes maps | Nodes auth | Success | Errors |
| --- | --- | --- | --- | --- | --- | --- |
| Enroll | `POST /api/v1/agents/enroll` | `agents-enroll-route`, `Anonymous`, `Auth`, `PathRemovePrefix /api/v1`, no cert forwarding | `POST /agents/enroll` | anonymous (`ExemptPaths`) | 201 `EnrollNodeResponse` | 400 `invalid_platform` / `invalid_public_key` / `certificate_generation_failed`; 401 `invalid_token` (unchanged) |
| Hub negotiate | `POST /hubs/agent/negotiate?negotiateVersion=1` | `agent-hub-route` (new), `AgentCertificate`, `PerAgent`, no path transform, metadata `ForwardClientCertificate=true` | `MapHub<AgentHub>("/hubs/agent").RequireAuthorization("AgentPolicy")` | `MtlsMiddleware` (hub prefix) + `AgentPolicy` | 200 negotiate | Gateway 401 (no cert or wrong issuer, empty body with a Bearer challenge); Nodes 401 problem+json (C5) |
| Hub connect | WebSocket `GET /hubs/agent?id=…`, or long-polling `GET`/`POST /hubs/agent?id=…` | same | same | same | 101 / 200 | same. Nodes aborts the connection if `X-Agent-Protocol` is present and not `1`, or if there is no `node_id` claim |
| Heartbeat | hub invocation `Heartbeat(AgentHeartbeat)` | - | `AgentHub.Heartbeat` | node ID from the `node_id` claim | `AgentHeartbeatAck(true, now)` | `AgentHeartbeatAck(false, now, code)` with codes from C1. Never a `HubException` for business errors |
| Renew | `POST /api/v1/agents/{nodeId}/certificates/renew` | `agents-route`, `AgentCertificate` (was `Agent`), `PerAgent`, `PathRemovePrefix /api/v1`, `ForwardClientCertificate=true` | `POST /agents/{nodeId:guid}/certificates/renew` | `MtlsMiddleware` + `AgentPolicy` | 200 `RenewCertificateResponse` (C3) | 400 `invalid_public_key`, `certificate_renewal_failed`; 401 `certificate_required`, `thumbprint_mismatch` (the presented certificate is not a usable row of this node, C3a); 403 `forbidden` (route node ≠ certificate node); 404 `node_not_found`, `certificate_not_found` |
| HTTP heartbeat (legacy, D-S28; the agent does not call it) | `POST /api/v1/agents/{nodeId}/heartbeat` | `agents-route` | `POST /agents/{nodeId:guid}/heartbeat` | same | 200 `HeartbeatResponse` (Nodes-local, unchanged) | 403 `forbidden`, 404 `node_not_found`, 400 `node_decommissioned` |
| CA certificate | `GET /api/v1/agents/ca-certificate` | `agents-route`: the Gateway now requires a client certificate. Same as before, when it required a JWT. Agents receive the CA certificate at enrollment and renewal. | `GET /agents/ca-certificate` | anonymous | 200 PEM | - |

**Rate limits (P25, D-S220, D-S228).** Every proxied route is limited by its own `RateLimiterPolicy`; `Global` applies only to a route with no policy (today only the `DenyAll` `identity-internal-block`). The limiter runs **after authentication and before authorization**, so it also counts requests that end in 401/403. On the routes above: enroll uses `Auth` (per client IP, IPv6 /64, 30 per 60 s by default, shared with `betterauth-route` and `identity-route`); the hub and `agents-route` use `PerAgent` (per client IP, 500 per 60 s). An agent that exceeds either gets 429 (C5) **before** any 401, and must treat it like any other connect or enroll failure: back off and retry. No agent code changes for this.

### C5. Error shapes

- **Nodes HTTP errors** are `application/problem+json`: `{ "type", "title", "status", "detail", "instance", "errorCode" }`. That is the existing `MtlsMiddleware` 401 writer and `ProblemDetailsHelper`. `errorCode` is the machine-readable value from C4.
  - `MtlsMiddleware` 401 codes are the existing validation codes: `certificate_required`, `invalid_issuer`, `certificate_expired`, `certificate_not_yet_valid`, `missing_client_auth_eku`, `missing_spiffe_id`, `invalid_spiffe_id`, `trust_domain_mismatch`.
  - The `AgentMtls` scheme (P03) writes 401 `certificate_required` on challenge and 403 `forbidden` on forbid, with the same writer.
  - `MtlsMiddleware` returns **503** problem+json, `errorCode = "certificate_validation_unavailable"`, when certificate trust cannot be determined (CA not loaded, or the revocation lookup failed). It never returns 401 for an infrastructure fault. The agent treats it like any other connect failure (retry with backoff). (P17f, D-S226.)
- **Gateway authorization failures** come from ASP.NET Core: 401 with an empty body and the `WWW-Authenticate: Bearer` challenge. Agents treat any 401 or 403 as "certificate not accepted".
- **Gateway rate-limit rejections (P25)** are 429 with a `Retry-After` header (whole seconds) and a ProblemDetails JSON body (`title` "Too Many Requests", `status` 429), written by the existing `OnRejected` handler in `src/Dhadgar.Gateway/Program.cs`. Because the limiter runs before authorization, a client over its limit gets 429 even when it would have got 401. Agents treat 429 as a retryable failure.
- **Hub business errors** come back as `AgentHeartbeatAck.ErrorCode` (D-S24).

### C6. Gateway → Nodes certificate forwarding (D-S25)

- The Gateway removes `X-Client-Cert` from **every** proxied request. On routes with metadata `ForwardClientCertificate=true`, when `HttpContext.Connection.ClientCertificate` is not null, it then sets `X-Client-Cert: <Convert.ToBase64String(cert.RawData)>`.
- Nodes' `ForwardedClientCertificateMiddleware` always removes the header. It uses the header as `Connection.ClientCertificate` only when all of these hold:
  - `Mtls:ForwardedCertificate:Enabled` is true;
  - the remote IP is inside one of `Mtls:ForwardedCertificate:TrustedProxies` (CIDR);
  - there is exactly one header value of at most `MaxHeaderLength` chars (default 16384);
  - the connection has no TLS client certificate of its own;
  - the value parses as base64 DER.
  
  Otherwise the header is ignored.
- Nodes refuses to start when `ForwardedCertificate.Enabled` is true and `TrustedProxies` is empty or holds an unparsable CIDR.

### C7. Agent-internal interfaces

These are shared between packets inside one process, so they are not Contracts. Their home is `src/Agents/Dhadgar.Agent.Core`.

```csharp
// Communication/ControlPlaneHubTransport.cs (P02)
public sealed class ControlPlaneHubTransport
{
    /// <summary>Optional wrapper around the SignalR client's HttpClientHandler. Null = default. Tests only.</summary>
    public Func<HttpMessageHandler, HttpMessageHandler>? HttpMessageHandlerFactory { get; init; }
    public HttpTransportType Transports { get; init; } = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
}

// Communication/IControlPlaneClient.cs (P05: changed members only)
Task<Result> ConnectAsync(CancellationToken cancellationToken = default);
Task<Result<AgentHeartbeatAck>> SendHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken = default);

// Authentication/IClientCertificateProvider.cs (P10)
public interface IClientCertificateProvider
{
    /// <summary>The current client certificate with its private key, or null. Owned by the provider; callers must not dispose it.</summary>
    X509Certificate2? Current { get; }
    /// <summary>Drop the cached certificate (and dispose it) so the next read reloads from ICertificateStore.</summary>
    void Invalidate();
}

// Identity/AgentIdentity.cs, StoredAgentState.cs, IAgentStateStore.cs, IStateFileSecurity.cs, IAgentIdentityProvider.cs (P11, P11-ruling-1)
public sealed record AgentIdentity(Guid NodeId, Guid OrganizationId, DateTimeOffset EnrolledAt);

/// <summary>Identity is null when no state file exists (first run). Result<T> cannot carry a successful null (D-S232).</summary>
public sealed record StoredAgentState(AgentIdentity? Identity)
{
    public static StoredAgentState None { get; } = new(Identity: null);
}

public interface IAgentStateStore
{
    /// <summary>Success(None) only when the state file does not exist; Success with an identity when it is valid and trusted;
    /// Failure ([AgentState.Corrupt] / [AgentState.Io] / [AgentState.Config] / [AgentState.Untrusted]) otherwise.
    /// Callers enroll only on None, never on a Failure (D-S232).</summary>
    Task<Result<StoredAgentState>> LoadAsync(CancellationToken cancellationToken = default);
    Task<Result> SaveAsync(AgentIdentity identity, CancellationToken cancellationToken = default);
}

/// <summary>Creates and checks the state file (D-S233). Core default: PortableStateFileSecurity;
/// the Windows host registers WindowsStateFileSecurity (P27f) before AddAgentCore.</summary>
public interface IStateFileSecurity
{
    /// <summary>FileMode.CreateNew, write-through, accessible only to the agent's account (plus SYSTEM and Administrators on Windows). Caller disposes.</summary>
    Result<FileStream> CreateNew(string path);
    /// <summary>Failure("[AgentState.Untrusted] …") when someone other than the agent could have written the file.</summary>
    Result VerifyTrusted(string path);
}

public interface IAgentIdentityProvider
{
    AgentIdentity? Current { get; }
    /// <summary>Completes when an identity is set.</summary>
    Task<AgentIdentity> WaitForIdentityAsync(CancellationToken cancellationToken);
    /// <summary>Sets the identity once. The same NodeId and OrganizationId again succeeds (EnrolledAt ignored);
    /// a different NodeId or OrganizationId fails with [AgentIdentity.Changed] (an identity change needs a restart).</summary>
    Result Set(AgentIdentity identity);
}

// Authentication/IEnrollmentTokenSource.cs (P12)
public interface IEnrollmentTokenSource
{
    /// <summary>Success(string.Empty) when no token is configured (D-S232); Failure only when the source cannot be read.</summary>
    Result<string> GetToken();
}
```

---

## Wave 1: contract, Nodes hub, Gateway (local campaign branch `campaign/sprint2-agent-nodes-w1`)

Local merge order into w1: P01 → P02 → P03 → P06 → P04 → P05 → P17f → P07 → P24f. Nothing is pushed. **Wave 1 gate** (before any wave 2 packet starts): P01-P07, P17f and P24f merged into w1, S1 and S2 green on the w1 tip, and the wave 1 review at `ship` (after `wave1-review-1` = `decision`, that means the ruling check of [wave1-ruling-1](worknotes/wave1-ruling-1.md) at `ship`).

P17f (fix packet, [P07-ruling-1](worknotes/P07-ruling-1.md)) and P24f (fix packet, [wave1-ruling-1](worknotes/wave1-ruling-1.md)) are specified in full in their rulings; the sections at the end of this wave only point there.

### P01: Contracts for the hub, heartbeat and certificate profile

- **Branch** `s2/p01-contracts`. **Worktree** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p01`. **Depends on** nothing. **Security review** yes (certificate profile used by the CA).
- **Commit subject** `feat(contracts): agent hub interfaces, heartbeat DTOs and certificate profile`

**Rules:** R-CONTRACT, R-NULL, R-CPM, R-XUNIT. Contracts holds records and constants only, with no logic and no SignalR package (`src/Shared/Dhadgar.Contracts/CLAUDE.md`).

**Change**

1. Create `src/Shared/Dhadgar.Contracts/Nodes/AgentHubContracts.cs` with exactly the C1 code.
2. Create `src/Shared/Dhadgar.Contracts/Nodes/AgentCertificateProfile.cs` with exactly the C2 code.
3. `src/Dhadgar.Nodes/Services/CertificateAuthorityService.cs`: replace the private SPIFFE format string, the organisation name, the CA common name and the 90-day validity constant with references to `AgentCertificateProfile` (`SpiffeId(nodeId)`, `Organization`, `CaCommonName`, `ValidityDays`). The values are identical, so behaviour does not change.
4. `src/Dhadgar.Nodes/Auth/MtlsOptions.cs`: the default of `SpiffeTrustDomain` becomes `AgentCertificateProfile.SpiffeTrustDomain`.

**Tests**

- New `tests/Dhadgar.Contracts.Tests/Nodes/AgentHubContractsTests.cs`:
  - `AgentHeartbeat_SerializesWithWebDefaults_ToPinnedWireNames`: serialize with `new JsonSerializerOptions(JsonSerializerDefaults.Web)` and assert the exact JSON `{"cpuUsagePercent":12.5,"memoryUsagePercent":40,"diskUsagePercent":70,"activeGameServers":2,"agentVersion":"1.2.3","healthIssues":["x"]}`, then round-trip it.
  - The same for `AgentHeartbeatAck` (`acknowledged`, `serverTime`, `errorCode`).
  - `SpiffeId_FormatsNodeIdWithHyphens`.
  - `NodeGroup_IsStable`.
  - These fail if a property is renamed: that would be a silent wire break.
- Extend `tests/Dhadgar.Nodes.Tests/Services/CertificateAuthorityPublicKeyIssuanceTests.cs` (or its folder, wherever it lives) with `IssuedCertificate_MatchesSharedProfile`. Issue for a test key, then assert:
  - the issuer simple name equals `AgentCertificateProfile.CaCommonName`;
  - the SAN URI equals `AgentCertificateProfile.SpiffeId(nodeId)`;
  - the subject contains `O=` + `Organization`;
  - `NotAfter - NotBefore` is `ValidityDays` days.
  
  It fails if the CA drifts from the profile the Windows store and the Gateway rely on (F7).

**Checks (in order)**

```powershell
dotnet build <wt>\src\Shared\Dhadgar.Contracts\Dhadgar.Contracts.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <wt>\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Contracts.Tests\Dhadgar.Contracts.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj
```

**Docs owed:** none. The types are documented in XML comments.

### P02: S1 red, the direct hub seam test (with a transport hook in the agent)

- **Branch** `s2/p02-hub-seam-red`. **Worktree** `…\sprint2-agent-nodes-p02`. **Depends on** P01. **Security review** yes (`ControlPlaneClient`, agent project).
- **Commit subject** `test(nodes): red agent hub seam test (S1) and hub transport hook`

**Rules:** R-USING, R-NULL, R-CPM, R-XUNIT, R-HTTPS, R-LOG. The transport hook is set only through DI or the constructor, never from configuration: it can bypass TLS, so it must not be reachable from settings.

**Change: agent (minimal, needed so the test can reach a TestServer)**

1. Create `src/Agents/Dhadgar.Agent.Core/Communication/ControlPlaneHubTransport.cs` exactly as in C7.
2. In `ControlPlaneClient`, add a last constructor parameter `ControlPlaneHubTransport? transport = null`, stored as `_transport = transport ?? new ControlPlaneHubTransport()`. Inside `.WithUrl(endpoint, options => { … })`, add:

   ```csharp
   options.Transports = _transport.Transports;
   if (_transport.HttpMessageHandlerFactory is { } factory)
   {
       options.HttpMessageHandlerFactory = factory;
   }
   ```

3. In `AgentServiceCollectionExtensions.AddAgentCore`, add `services.TryAddSingleton(new ControlPlaneHubTransport());`.

**Change: test harness (extract and extend; `AgentEnrollmentContractTests` must stay green)**

Create the folder `tests/Dhadgar.Nodes.Tests/Integration/AgentSeam/` and put these public types in namespace `Dhadgar.Nodes.Tests.Integration.AgentSeam`. They are public because P07's project reuses them.

- `AgentSeamHarness` (static):
  - `CreateFactoryWithRealCa(NodesWebApplicationFactory baseFactory, string caDirectory, bool enableMtls, Action<MtlsOptions>? configureMtls = null)`. This is the existing `CreateFactoryWithRealCa` body plus, when `enableMtls` is set, `services.Configure<MtlsOptions>(o => { o.Enabled = true; configureMtls?.Invoke(o); })`.
  - `CreateEnrollmentTokenAsync(factory, orgId)`, moved.
  - `CreateEnrollmentService(IHttpClientFactory, ICertificateStore)`, moved.
  - `EnrollAgentAsync(factory, orgId)`, which returns `(EnrollmentResult Result, InMemoryCertificateStore Store)` through a `TestServerClientFactory`.
  - `CertificateInjectingHandlerFactory(TestServer server, IPAddress? remoteIp = null)`, which returns a `Func<HttpMessageHandler, HttpMessageHandler>`:

    ```csharp
    return inner =>
    {
        // The SignalR client builds an HttpClientHandler and copies HttpConnectionOptions.ClientCertificates
        // into it. Read the cert the agent actually chose, then put it on the server-side connection,
        // which is what Kestrel does after a TLS handshake.
        var cert = (inner as HttpClientHandler)?.ClientCertificates.OfType<X509Certificate2>().FirstOrDefault();
        inner.Dispose();
        return server.CreateHandler(ctx =>
        {
            ctx.Connection.ClientCertificate = cert;
            if (remoteIp is not null) ctx.Connection.RemoteIpAddress = remoteIp;
        });
    };
    ```

    If `TestServer.CreateHandler(Action<HttpContext>)` does not exist in the referenced version, add a test-only `IStartupFilter` middleware to the factory that sets `Connection.ClientCertificate` from a per-test `AsyncLocal`. Report which approach you used.
- `TestServerClientFactory(TestServer server, bool stripApiPrefix = true)` and `StripApiPrefixHandler`, moved.
- `InMemoryCertificateStore`, moved and extended:
  - keep the raw key bytes;
  - `GetClientCertificate()` returns a certificate **with** its private key: create an `ECDsa`, try `ImportPkcs8PrivateKey` and fall back to `ImportECPrivateKey` (P10 switches the agent to PKCS#8), then return `cert.CopyWithPrivateKey(key)`;
  - keep the existing `public byte[]? PrivateKey` property name, so `AgentEnrollmentContractTests` compiles unchanged.
- `ForeignCa` (static): `CreateClientCertificate(Guid nodeId)` builds a **self-signed** CA whose CN is `AgentCertificateProfile.CaCommonName` (the same name as the real CA), then issues a client certificate with the correct SPIFFE SAN, `CN={nodeId}` and the client-auth EKU. This proves Nodes checks the signature and not the name.
- Update `AgentEnrollmentContractTests` to use the harness. Its assertions do not change.

**Change: the red test**

`tests/Dhadgar.Nodes.Tests/Integration/AgentHubSeamTests.cs` uses `[Collection("Nodes Integration")]`, a temp CA directory with best-effort cleanup (copy the existing pattern) and `AgentSeamHarness.CreateFactoryWithRealCa(..., enableMtls: true)`.

1. `RealAgent_ConnectsWithEnrolledCertificate_AndHeartbeatUpdatesNodeHealth`:
   1. Enroll. Advance `factory.TimeProvider` by 2 minutes.
   2. Build `new ControlPlaneClient(Options.Create(new AgentOptions { NodeId = r.NodeId, OrganizationId = r.OrganizationId, ControlPlane = { Endpoint = "https://localhost" } }), store, new AgentMeter(), new AgentActivitySource(), NullLogger<ControlPlaneClient>.Instance, new ControlPlaneHubTransport { HttpMessageHandlerFactory = AgentSeamHarness.CertificateInjectingHandlerFactory(factory.Server), Transports = HttpTransportType.LongPolling })`. Use `await using` or `DisposeAsync` in a `finally`.
   3. Call `ConnectAsync`, then `SendHeartbeatAsync` with a `HeartbeatPayload` whose `AgentVersion` is `"2.0.0-seam"` and whose metrics have CPU 42.5 and memory/disk values that give known percentages.
   4. Poll the DB for up to 5 s. Assert that `node.LastHeartbeat` equals the advanced time, `node.AgentVersion == "2.0.0-seam"`, the node's `NodeHealth` CPU is 42.5 and `node.Status == NodeStatus.Online`.
   5. Write the connect and send calls through two small private helpers, so that P05's signature change (returning `Result`) touches only the helpers.
2. `Negotiate_WithoutCertificate_Returns401ProblemJson`: send a raw `HttpClient` request through `CertificateInjectingHandlerFactory(factory.Server)(new HttpClientHandler())` (no certificate) to `POST https://localhost/hubs/agent/negotiate?negotiateVersion=1`. Expect 401, `application/problem+json` and `errorCode == "certificate_required"`.
3. `Negotiate_WithForeignCaCertificate_Returns401`: the same request, but with the handler's `ClientCertificates` set to `ForeignCa.CreateClientCertificate(enrolledNodeId)`. Expect 401 with `errorCode == "invalid_issuer"`.
4. `Heartbeat_UpdatesCertificateNode_NotPayloadNode`: seed a second node B with `factory.SeedNodeAsync`. The enrolled agent A sends a heartbeat whose payload `NodeId` is B. Assert that A is updated and B's `LastHeartbeat` is unchanged.

**Expected red now:** tests 1 and 4 fail at connect (negotiate 404). Tests 2 and 3 fail with 404 where 401 is expected. Record this.

**Checks (in order)**

```powershell
dotnet build <wt>\src\Agents\Dhadgar.Agent.Core\Dhadgar.Agent.Core.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName!~AgentHubSeamTests"
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName~AgentHubSeamTests"
```

The third command must pass. The fourth must fail with the expected reasons; record them in the report.

**Docs owed:** none.

### P03: Nodes mTLS for the hub, the forwarded certificate and the `AgentMtls` scheme

- **Branch** `s2/p03-nodes-mtls`. **Worktree** `…\sprint2-agent-nodes-p03`. **Depends on** P01. **Security review** yes (auth).
- **Commit subject** `feat(nodes): mTLS on /hubs/agent, trusted forwarded client cert, AgentMtls scheme`

**Rules:** R-CONTRACT (header name from `AgentCertificateProfile.ForwardedCertificateHeader`, hub path from `AgentHubProtocol.Path`), R-USING, R-NULL, R-XUNIT, R-LOG.

- Every 401 or 403 is problem+json in the C5 shape.
- Never trust the forwarded header from an untrusted source (C6).
- Do **not** edit `src/Dhadgar.Nodes/Program.cs` (P04 owns it). Everything goes in through the existing `AddMtlsAuthentication` / `UseMtlsAuthentication` extensions in `MtlsMiddleware.cs`.

**Change**

1. `src/Dhadgar.Nodes/Auth/MtlsOptions.cs`:
   - Add `public string HubPathPrefix { get; set; } = AgentHubProtocol.Path;`.
   - Add `public ForwardedCertificateOptions ForwardedCertificate { get; set; } = new();`.
   - Add the class `ForwardedCertificateOptions { bool Enabled; List<string> TrustedProxies = []; int MaxHeaderLength = 16384; }`, with XML docs that point at D-S25 and D-S210.
2. `src/Dhadgar.Nodes/Auth/MtlsMiddleware.cs`:
   - The path predicate (around lines 147-164) protects a request when `Path.StartsWithSegments(AgentEndpointPrefix)` (minus `ExemptPaths`, unchanged) **or** `Path.StartsWithSegments(HubPathPrefix)`. `StartsWithSegments` means `/hubs/agentx` is not protected and `/hubs/agent/negotiate` is.
   - Move the 401 problem+json writer into `internal static class MtlsProblemWriter` (same file or a new `Auth/MtlsProblemWriter.cs`), so the handler below can reuse it.
3. New `src/Dhadgar.Nodes/Auth/ForwardedClientCertificateMiddleware.cs`, following C6 exactly:
   - Parse `TrustedProxies` once in the constructor with `System.Net.IPNetwork.TryParse`.
   - Normalise `RemoteIpAddress` with `IsIPv4MappedToIPv6 ? MapToIPv4() : ip`.
   - Remove the header from `context.Request.Headers` in every case.
   - On acceptance, call `X509CertificateLoader.LoadCertificate(Convert.FromBase64String(value))`, then `context.Response.RegisterForDispose(cert)` and `context.Connection.ClientCertificate = cert`.
   - Catch only `FormatException` and `CryptographicException`; log at Warning without the value, and continue with no certificate.
4. New `src/Dhadgar.Nodes/Auth/AgentMtlsAuthenticationHandler.cs` (`AuthenticationHandler<AuthenticationSchemeOptions>`, `public const string SchemeName = "AgentMtls"`):
   - `HandleAuthenticateAsync` returns `NoResult()`. `MtlsMiddleware` sets `context.User` after `UseAuthentication`, and policies without schemes evaluate `context.User`.
   - `HandleChallengeAsync` writes 401 `certificate_required`.
   - `HandleForbiddenAsync` writes 403 `forbidden`. Both use `MtlsProblemWriter`.
5. `AddMtlsAuthentication(...)` (`MtlsMiddleware.cs:~240`):
   - Add `services.AddAuthentication(AgentMtlsAuthenticationHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, AgentMtlsAuthenticationHandler>(AgentMtlsAuthenticationHandler.SchemeName, null);`.
   - Add startup validation: `ForwardedCertificate.Enabled` with an empty or unparsable `TrustedProxies` throws `InvalidOperationException` at startup. Follow the existing `Enabled=false` guard's pattern.
   - `UseMtlsAuthentication(...)` registers `ForwardedClientCertificateMiddleware` **before** `MtlsMiddleware`.
6. `src/Dhadgar.Nodes/appsettings.json`: under `Mtls`, add `"ForwardedCertificate": { "Enabled": false, "TrustedProxies": [] }`.

**Tests**

- New `tests/Dhadgar.Nodes.Tests/Auth/ForwardedClientCertificateMiddlewareTests.cs` (unit, `DefaultHttpContext`):
  - a trusted IP with a valid header sets the certificate and removes the header;
  - an untrusted IP leaves no certificate, removes the header and still calls `next`;
  - `Enabled=false` ignores the header;
  - malformed base64 leaves no certificate and throws nothing;
  - a header over `MaxHeaderLength` is ignored;
  - two header values are ignored;
  - an existing connection certificate is kept;
  - an IPv4-mapped IPv6 address inside an IPv4 CIDR is trusted.
- `tests/Dhadgar.Nodes.Tests/MtlsMiddlewareTests.cs`, or wherever the class lives, gets these cases: `/hubs/agent/negotiate` without a certificate gives 401 problem+json; `/hubs/agentx` passes through; `/agents/enroll` stays exempt.
- New `tests/Dhadgar.Nodes.Tests/Auth/AgentMtlsSchemeTests.cs`:
  - Build a minimal host: `WebApplication.CreateBuilder()` plus `builder.WebHost.UseTestServer()`.
  - Configure `Mtls:Enabled=true` and call `services.AddMtlsAuthentication(config)` (or the real extension signature), plus `AddAuthorization`.
  - Map an endpoint that returns `Results.Forbid()` and one that has `.RequireAuthorization(p => p.RequireAuthenticatedUser())` under a path that is **not** mTLS-protected.
  - Assert 403 problem+json `forbidden` and 401 problem+json `certificate_required`.
  - Revert check: without the scheme, both throw `InvalidOperationException` (no default scheme), which is F3.
- Add a startup test: `ForwardedCertificate.Enabled=true` with no `TrustedProxies` fails host start.

**Checks (in order)**

```powershell
dotnet build <wt>\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName!~AgentHubSeamTests"
```

**Docs owed:** in `src/Dhadgar.Nodes/README.md`, the mTLS section gets the protected paths (`/agents/*` minus the exempt paths, and `/hubs/agent`), `Mtls:ForwardedCertificate` with its trust rule and a pointer to D-S210, and the `AgentMtls` scheme.

### P04: Nodes `AgentHub` and the heartbeat bridge

- **Branch** `s2/p04-nodes-agent-hub`. **Worktree** `…\sprint2-agent-nodes-p04`. **Depends on** P01, P03. **Security review** yes (auth on the hub).
- **Commit subject** `feat(nodes): AgentHub at /hubs/agent feeding heartbeat health tracking`

**Rules:** R-CONTRACT (implement `IAgentHub`, type the hub as `Hub<IAgentHubClient>`, use the C1 constants), R-RESULT (hub business errors go in `AgentHeartbeatAck.ErrorCode`; never throw `HubException` for them), R-NULL, R-XUNIT, R-LOG.

- The node ID comes **only** from `Context.User`'s `node_id` claim.

**Change**

1. New `src/Dhadgar.Nodes/Hubs/AgentHub.cs`:

   ```csharp
   public sealed class AgentHub(
       IHeartbeatService heartbeatService,
       TimeProvider timeProvider,
       ILogger<AgentHub> logger) : Hub<IAgentHubClient>, IAgentHub
   {
       public override async Task OnConnectedAsync()
       {
           var version = Context.GetHttpContext()?.Request.Headers[AgentHubProtocol.VersionHeader].ToString();
           if (!string.IsNullOrEmpty(version) && version != AgentHubProtocol.CurrentVersion)
           {
               logger.LogWarning("Agent connection refused: unsupported protocol {Version}", version);
               Context.Abort();
               return;
           }

           if (!TryGetNodeId(out var nodeId))
           {
               logger.LogWarning("Agent connection refused: no node_id claim");
               Context.Abort();
               return;
           }

           await Groups.AddToGroupAsync(Context.ConnectionId, AgentHubProtocol.NodeGroup(nodeId));
           await base.OnConnectedAsync();
       }

       public async Task<AgentHeartbeatAck> Heartbeat(AgentHeartbeat heartbeat)
       {
           var now = timeProvider.GetUtcNow();
           if (!TryGetNodeId(out var nodeId))
           {
               return new AgentHeartbeatAck(false, now, AgentHubErrorCodes.HeartbeatFailed);
           }

           if (!AgentHeartbeatValidator.IsValid(heartbeat))
           {
               return new AgentHeartbeatAck(false, now, AgentHubErrorCodes.InvalidHeartbeat);
           }

           var result = await heartbeatService.ProcessHeartbeatAsync(
               nodeId, AgentHeartbeatMapper.ToHeartbeatRequest(heartbeat), Context.ConnectionAborted);

           return result.Success
               ? new AgentHeartbeatAck(true, now)
               : new AgentHeartbeatAck(false, now, result.Error switch
               {
                   "node_not_found" => AgentHubErrorCodes.NodeNotFound,
                   "node_decommissioned" => AgentHubErrorCodes.NodeDecommissioned,
                   _ => AgentHubErrorCodes.HeartbeatFailed
               });
       }

       private bool TryGetNodeId(out Guid nodeId) =>
           Guid.TryParse(Context.User?.FindFirst(AgentHubProtocol.NodeIdClaim)?.Value, out nodeId);
   }
   ```

2. New `src/Dhadgar.Nodes/Hubs/AgentHeartbeatValidator.cs` (static `bool IsValid(AgentHeartbeat? h)`). It is valid when:
   - `h` is not null;
   - all three percentages are finite and within 0-100;
   - `0 <= ActiveGameServers <= AgentHeartbeatLimits.MaxActiveGameServers`;
   - `AgentVersion` is null or at most `MaxAgentVersionLength` long;
   - `HealthIssues` is null or has at most `MaxHealthIssues` entries, each non-null and at most `MaxHealthIssueLength` long.
3. New `src/Dhadgar.Nodes/Hubs/AgentHeartbeatMapper.cs` (static `HeartbeatRequest ToHeartbeatRequest(AgentHeartbeat h)`): a one-to-one field copy into the Nodes-local `Models.HeartbeatRequest`.
4. `src/Dhadgar.Nodes/Program.cs`:
   - Add `builder.Services.AddSignalR(o => { o.MaximumReceiveMessageSize = 64 * 1024; o.EnableDetailedErrors = false; });` next to the other service registrations.
   - Add `app.MapHub<AgentHub>(AgentHubProtocol.Path).RequireAuthorization("AgentPolicy");` after `AgentEndpoints.Map(app);`.
   - The SignalR server is in the shared framework, so no package is needed.

**Tests**

New `tests/Dhadgar.Nodes.Tests/Hubs/AgentHubTests.cs`:

- Use `AgentSeamHarness` from P02, mTLS enabled, an enrolled agent, a raw `HubConnectionBuilder` with `.WithUrl("https://localhost" + AgentHubProtocol.Path, o => { o.Transports = HttpTransportType.LongPolling; o.ClientCertificates.Add(storeCert); o.HttpMessageHandlerFactory = AgentSeamHarness.CertificateInjectingHandlerFactory(factory.Server); })`.
- If the SignalR client type is not visible, add `<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />` with no version.
- Cases:
  - `Heartbeat_Valid_AcknowledgesAndUpdatesHealth`: `InvokeAsync<AgentHeartbeatAck>(nameof(IAgentHub.Heartbeat), new AgentHeartbeat(...))`, then check the ack and the DB.
  - `Heartbeat_CpuOver100_ReturnsInvalidHeartbeat_AndLeavesDbUnchanged`.
  - `Heartbeat_DecommissionedNode_ReturnsNodeDecommissioned`: set the node's status in the DB first.
  - `Connect_WithUnsupportedProtocolHeader_IsClosedByServer`: set `o.Headers[AgentHubProtocol.VersionHeader] = "2"`. Assert that the connection closes, or that the first invoke throws.
  - `AgentHub_ImplementsContractInterface`: `Assert.True(typeof(IAgentHub).IsAssignableFrom(typeof(AgentHub)))`.
- Revert check: without the hub, negotiate returns 404, and without the validator the out-of-range case writes to the DB.
- Also run S1. It stays red until P05; record its current failure, which should now be the agent's string payload.

**Checks (in order)**

```powershell
dotnet build <wt>\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName!~AgentHubSeamTests"
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName~AgentHubSeamTests"
```

The third command is informational: tests 2 and 3 must now pass, and 1 and 4 may still fail.

**Docs owed:** add an "Agent hub" section to `src/Dhadgar.Nodes/README.md` covering the path, auth, protocol header, `Heartbeat` and the error codes. Link it from ADR-0008 in P15.

### P05: Agent hub client with typed heartbeat and `Result` returns

- **Branch** `s2/p05-agent-hub-client`. **Worktree** `…\sprint2-agent-nodes-p05`. **Depends on** P02, P04 (merged). **Security review** yes (`IControlPlaneClient`, agent project).
- **Commit subject** `feat(agent-core): typed hub heartbeat over Contracts, Result-returning ConnectAsync`

**Rules:** R-RESULT, R-CONTRACT (`AgentHeartbeat`, `AgentHeartbeatAck`, `AgentHubProtocol`, `nameof(IAgentHub.Heartbeat)`), R-HTTPS, R-USING, R-NULL, R-XUNIT, R-LOG. The agent must not send `X-Node-Id` (D-S23).

**Change**

1. `src/Agents/Dhadgar.Agent.Core/Communication/IControlPlaneClient.cs`:
   - Change `ConnectAsync` and `SendHeartbeatAsync` to the C7 signatures.
   - Leave `SendCommandResultAsync` and `SendTelemetryAsync` unchanged, but add `/// <remarks>Not served by Nodes yet (D-S29).</remarks>` to both.
2. `ControlPlaneClient.ConnectAsync`:
   - The non-HTTPS case returns `Result.Failure("[ControlPlane.InsecureEndpoint] …")`.
   - The endpoint is `new Uri(baseUri, AgentHubProtocol.Path)`.
   - Load the certificate from the store. If it is null, return `Failure("[ControlPlane.NoCertificate] …")` whether or not `NodeId` is set.
   - Set `options.ClientCertificates = [clientCert]` and `options.Headers[AgentHubProtocol.VersionHeader] = AgentHubProtocol.CurrentVersion`. Delete the `X-Node-Id` header.
   - Register handlers with `On<string>(nameof(IAgentHubClient.ReceiveCommand), …)` and `On<string>(nameof(IAgentHubClient.ReceivePing), …)`.
   - Catch `HttpRequestException` (include the status code when present), `OperationCanceledException` when the caller's token is **not** cancelled (timeout), `WebSocketException` and `InvalidOperationException` from `StartAsync`. Each sets state `Failed` and returns `Failure("[ControlPlane.ConnectFailed] …")`. Caller cancellation still throws.
3. `ControlPlaneClient.SendHeartbeatAsync(AgentHeartbeat heartbeat, …)`:
   - Not connected returns `Failure("[ControlPlane.NotConnected]")`.
   - Otherwise call `var ack = await _hubConnection.InvokeAsync<AgentHeartbeatAck>(nameof(IAgentHub.Heartbeat), heartbeat, cts.Token);`, keeping the invocation timeout, and return `Success(ack)` even when `ack.Acknowledged` is false; the caller logs `ErrorCode`.
   - A timeout returns `Failure("[ControlPlane.Timeout]")` and a `HubException` returns `Failure("[ControlPlane.HubError]")`.
   - Activity and logging use the node ID from `_options.NodeId` (P11 swaps the source). Do not serialize to a string.
4. New `src/Agents/Dhadgar.Agent.Core/Health/HeartbeatPayloadMapper.cs` (static `AgentHeartbeat ToContract(HeartbeatPayload p)`):
   - CPU is `Clamp(p.Metrics?.CpuUsagePercent)`; memory is `Clamp(p.Metrics?.MemoryUsagePercent)`; disk is `Clamp(max of p.Metrics.Disks[].UsagePercent, or 0)`.
   - `ActiveGameServers` is `Math.Min(p.ActiveProcesses.Count(x => x.State == ProcessState.Running), MaxActiveGameServers)`; use the actual running enum member.
   - `AgentVersion` is truncated to `MaxAgentVersionLength`.
   - `HealthIssues` is `p.Warnings` with nulls removed, the first `MaxHealthIssues` taken and each truncated to `MaxHealthIssueLength`.
   - `Clamp` maps null and NaN to 0, then clamps to 0-100.
5. `src/Agents/Dhadgar.Agent.Core/Health/HeartbeatService.cs`: after `GetHeartbeatPayloadAsync` succeeds, map with `HeartbeatPayloadMapper.ToContract` and call `SendHeartbeatAsync`.
   - A failed `Result` is logged at Warning.
   - `ack.Acknowledged == false` is logged at Warning with `ErrorCode`.
   - Keep the `NodeId` wait loop; P11 replaces it.
6. Update `AgentHubSeamTests`' two helpers to the new signatures. S1 must now be **green**.

**Tests**

- New `tests/Dhadgar.Agent.Core.Tests/Health/HeartbeatPayloadMapperTests.cs`:
  - null metrics give zeros;
  - NaN and 150 clamp to 0 and 100;
  - disk is the maximum over disks;
  - only running processes are counted;
  - version and issues are truncated;
  - null warnings are dropped.
  
  Revert check: removing the clamp makes Nodes reject real heartbeats with `invalid_heartbeat`.
- New `tests/Dhadgar.Agent.Core.Tests/Communication/ControlPlaneClientTests.cs`:
  - `ConnectAsync_HttpEndpoint_ReturnsFailure_NoThrow`;
  - `ConnectAsync_NoCertificate_ReturnsFailure`, using a stub `ICertificateStore`.
- S1 (`AgentHubSeamTests`): all four tests pass.

**Checks (in order)**

```powershell
dotnet build <wt>\src\Agents\Dhadgar.Agent.Core\Dhadgar.Agent.Core.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <wt>\src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Core.Tests\Dhadgar.Agent.Core.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj
```

**Docs owed:** update `src/Agents/Dhadgar.Agent.Core/README.md` §IControlPlaneClient with the new signatures, the typed hub, the protocol header and "no X-Node-Id".

### P06: Gateway client certificate, header transform, hub route and `AgentCertificate` policy

- **Branch** `s2/p06-gateway-agent-ingress`. **Worktree** `…\sprint2-agent-nodes-p06`. **Depends on** P01. **Security review** yes (auth, certificate forwarding).
- **Commit subject** `feat(gateway): agent hub route, client-cert forwarding and AgentCertificate policy`

**Rules:** R-CONTRACT (header and CA name from `AgentCertificateProfile`, hub path from C1), R-NULL, R-XUNIT, R-CPM, R-LOG.

- Strip the forwarded header on **every** route (C6).
- Kestrel accepts any client certificate at TLS level because Nodes is the validator (D-S25). Say so in a code comment.
- Do not change `agents-enroll-route`'s policy.

**Change**

1. `src/Dhadgar.Gateway/Program.cs`:
   - Inside `ConfigureKestrel` (:30):

     ```csharp
     var agentCertMode = builder.Configuration.GetValue("AgentIngress:ClientCertificateMode", ClientCertificateMode.NoCertificate);
     options.ConfigureHttpsDefaults(https =>
     {
         https.ClientCertificateMode = agentCertMode;
         if (agentCertMode != ClientCertificateMode.NoCertificate)
         {
             // Chain, SPIFFE and revocation are validated by Nodes (D-S25); the Gateway only forwards.
             https.AllowAnyClientCertificate();
         }
     });
     ```

   - In the authorization options (near :108), add the policy `"AgentCertificate"` as `policy.RequireAssertion(ctx => ctx.Resource is HttpContext h && h.Connection.ClientCertificate is { } c && AgentCertificateForwarding.IsAgentIssued(c))`. Keep the `Agent` policy.
   - After `.LoadFromConfig(...)` (:122), chain `.AddTransforms(AgentCertificateForwarding.Apply)`.
   - `PerAgent` limiter (:243): the partition key becomes `agent-cert:{c.GetCertHashString(HashAlgorithmName.SHA256)}` when a client certificate is present, and otherwise the existing `sub`-or-IP key.
2. New `src/Dhadgar.Gateway/Agents/AgentCertificateForwarding.cs`:

   ```csharp
   public static class AgentCertificateForwarding
   {
       public const string MetadataKey = "ForwardClientCertificate";

       public static bool IsAgentIssued(X509Certificate2 certificate) =>
           string.Equals(certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true),
               AgentCertificateProfile.CaCommonName, StringComparison.Ordinal);

       public static void Apply(TransformBuilderContext context)
       {
           context.AddRequestHeaderRemove(AgentCertificateProfile.ForwardedCertificateHeader);
           if (context.Route.Metadata?.TryGetValue(MetadataKey, out var value) == true
               && bool.TryParse(value, out var enabled) && enabled)
           {
               context.AddRequestTransform(transform =>
               {
                   var cert = transform.HttpContext.Connection.ClientCertificate;
                   if (cert is not null)
                   {
                       transform.ProxyRequest.Headers.TryAddWithoutValidation(
                           AgentCertificateProfile.ForwardedCertificateHeader,
                           Convert.ToBase64String(cert.RawData));
                   }
                   return ValueTask.CompletedTask;
               });
           }
       }
   }
   ```

3. `src/Dhadgar.Gateway/appsettings.json`:
   - `agents-route` (:228): `"AuthorizationPolicy": "AgentCertificate"`, plus `"Metadata": { "ForwardClientCertificate": "true" }`.
   - New `agent-hub-route` beside `console-hub-route`: `{ "ClusterId": "nodes", "Order": 20, "Match": { "Path": "/hubs/agent/{**catch-all}" }, "AuthorizationPolicy": "AgentCertificate", "RateLimiterPolicy": "PerAgent", "Metadata": { "ForwardClientCertificate": "true" } }`, with no `Transforms`.
   - `nodes` cluster (:328): add `SessionAffinity`, copied from the `console` cluster (:399) with a nodes-specific cookie name (D-S217).
   - New top-level `"AgentIngress": { "ClientCertificateMode": "NoCertificate" }`.
4. `deploy/compose/docker-compose.services.yml`:
   - gateway env `AgentIngress__ClientCertificateMode: "AllowCertificate"`;
   - nodes env `Mtls__ForwardedCertificate__Enabled: "true"` and `Mtls__ForwardedCertificate__TrustedProxies__0: "172.16.0.0/12"` (Docker's default bridge pool);
   - a comment on both: `# dev only; production ingress and trust: D-S210 (docs/analysis/sprint2-agent-nodes)`.
   
   Follow the file's existing env style.

**Tests**

- `tests/Dhadgar.Gateway.Tests/RouteConfigurationTests.cs`:
  - the route count goes from 17 to 18 (:30);
  - new asserts: `agent-hub-route` has path `/hubs/agent/{**catch-all}`, policy `AgentCertificate`, no transforms, metadata `ForwardClientCertificate=true` and cluster `nodes`;
  - `agents-route` has policy `AgentCertificate` and the metadata;
  - `agents-enroll-route` has no metadata and stays `Anonymous`;
  - the `nodes` cluster has affinity.
- New `tests/Dhadgar.Gateway.Tests/AgentCertificateForwardingTests.cs`:
  - Use `GatewayWebApplicationFactory` (or a `WithWebHostBuilder` derivative). Replace `IForwarderHttpClientFactory` with one that returns `new HttpMessageInvoker(captureHandler)`, where `captureHandler` records the proxied `HttpRequestMessage` and returns 200.
  - Set the nodes destination to `http://nodes.test/` and disable the nodes active health check in test config.
  - Send client requests through `factory.Server.CreateHandler(ctx => ctx.Connection.ClientCertificate = cert)`.
  - Build test certificates with `CertificateRequest`: one whose issuer CN is `AgentCertificateProfile.CaCommonName` (CA CN set accordingly) and one with another issuer.
  - Cases:
    - `HubRoute_WithAgentCertificate_ForwardsBase64Der`: request `/hubs/agent/negotiate` and check that the captured header equals `Convert.ToBase64String(cert.RawData)`;
    - `AgentsRoute_WithoutCertificate_Returns401_AndDoesNotProxy`;
    - `AgentsRoute_WithForeignIssuer_IsRejected` (401 or 403) and is not proxied;
    - `EnrollRoute_SpoofedHeader_IsStripped`: an anonymous enroll request carrying `X-Client-Cert: abc` produces a captured request without the header;
    - `TenantRoute_SpoofedHeader_IsStripped`, on any JWT route if the test auth makes that easy; otherwise skip and say so in the report.
  - Revert check: without the transform the header is missing or spoofable, and without the route you get 404.

**Checks (in order)**

```powershell
dotnet build <wt>\src\Dhadgar.Gateway\Dhadgar.Gateway.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Gateway.Tests\Dhadgar.Gateway.Tests.csproj
```

**Docs owed:** in `src/Dhadgar.Gateway/README.md` (if absent, the Gateway section of `docs/`), document the agent routes, the `AgentCertificate` policy, `AgentIngress:ClientCertificateMode`, certificate forwarding and the strip-everywhere rule, with a pointer to D-S210.

### P07: S2, the Gateway-path seam project (agent → Gateway → Nodes)

- **Branch** `s2/p07-gateway-seam`. **Worktree** `…\sprint2-agent-nodes-p07`. **Depends on** P05, P06 (merged). **Security review** yes (auth path).
- **Commit subject** `test(seam): agent to Gateway to Nodes hub seam project (S2)`

**Rules:** R-CPM (no versions; reuse the package names already used by `Dhadgar.Nodes.Tests`), R-NULL, R-XUNIT, R-USING. Production code is not changed; this packet only adds tests.

**Change**

1. New `tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj`. Copy `Dhadgar.Nodes.Tests.csproj` (net10.0, nullable, the same test packages, `xunit.runner.json`), then set the project references:

   ```xml
   <ProjectReference Include="..\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj" />
   <!-- Both web apps define a global Program; the Gateway's is reached through the alias (D-S216). -->
   <ProjectReference Include="..\..\src\Dhadgar.Gateway\Dhadgar.Gateway.csproj" Aliases="gateway" />
   ```

   - If the build reports CS0436 or a duplicate `AutoGeneratedProgram`, add `<GenerateProgramFile>false</GenerateProgramFile>`.
   - If `appsettings.json` copy conflicts break the build, set `<ProjectReference … Private="false" />` on the Gateway reference only if the factory still starts; otherwise report `blocked spec` with the exact error.
   - Add the project to `Dhadgar.sln` with `dotnet sln <wt>\Dhadgar.sln add <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj`. CI runs `dotnet test Dhadgar.sln`, so it is then covered.
2. New `tests/Dhadgar.AgentSeam.Tests/GatewayNodesHarness.cs` (`extern alias gateway;` at the top). It builds:
   - **Nodes**: `AgentSeamHarness.CreateFactoryWithRealCa(new NodesWebApplicationFactory(), caDir, enableMtls: true, o => { o.ForwardedCertificate.Enabled = true; o.ForwardedCertificate.TrustedProxies = ["10.99.0.0/16"]; })`. Dispose it.
   - **Gateway**: `WebApplicationFactory<gateway::Program>`.
     - Copy the configuration `tests/Dhadgar.Gateway.Tests/GatewayIntegrationTests.cs`' `GatewayWebApplicationFactory` uses to start in tests.
     - Set `ReverseProxy:Clusters:nodes:Destinations:d1:Address = http://nodes.seam/` and disable the nodes active health check.
     - Replace `IForwarderHttpClientFactory` with a factory whose `CreateClient` returns `new HttpMessageInvoker(nodesServer.CreateHandler(ctx => ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.99.0.10")), disposeHandler: true)`. Nodes sees the Gateway as a trusted proxy, and **no** TLS certificate on its own connection.
   - **Agent**: `ControlPlaneHubTransport { Transports = LongPolling, HttpMessageHandlerFactory = AgentSeamHarness.CertificateInjectingHandlerFactory(gatewayServer) }`, plus `new TestServerClientFactory(gatewayServer, stripApiPrefix: false)` for enrollment. The Gateway strips `/api/v1` now.
3. New `tests/Dhadgar.AgentSeam.Tests/GatewayAgentHubSeamTests.cs`:
   - `RealAgent_EnrollsAndHeartbeatsThroughGateway`: enroll via `/api/v1/agents/enroll` through the Gateway, connect to the hub through the Gateway, send a heartbeat, get `ack.Acknowledged`, and see the Nodes DB updated.
   - `Hub_WithoutClientCertificate_IsRejectedAtGateway`: raw negotiate through the Gateway with no certificate gives 401.
   - `Hub_SpoofedForwardedHeaderWithoutTlsCert_IsRejected`: no connection certificate, but an `X-Client-Cert` header holding a *valid enrolled* certificate's base64. Expect 401: the Gateway refuses it, and even with the policy relaxed the header would be stripped.
   - `Hub_SpoofedHeaderAlongsideRealCert_UsesTlsCert`: agent A connects with its own TLS certificate and also sends a header with node B's certificate. The heartbeat updates A only.
   - Revert check: without P06's route the hub returns 404; without the transform Nodes returns 401; without P03's forwarded-certificate middleware Nodes returns 401.

**Checks (in order)**

```powershell
dotnet build <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Gateway.Tests\Dhadgar.Gateway.Tests.csproj
```

**Docs owed:** none. P15 references the project.

### P17f: Nodes CA uses a DbContext scope per lookup; trust lookup failures are 503 (fix packet)

Specified in [worknotes/P07-ruling-1.md](worknotes/P07-ruling-1.md) § P17f. Integrated (merged into w1 at `77172a7`, before P07). Decision D-S226.

### P24f: `ControlPlaneClient.ConnectAsync` returns `Result` for unexpected faults (fix packet)

Specified in [worknotes/wave1-ruling-1.md](worknotes/wave1-ruling-1.md) § P24f. Branch `s2/p24f-connect-result`, from the w1 tip `c1a1497`; merges into w1 last in wave 1. Security review yes (agent project, `IControlPlaneClient` implementation). **Integrated: merged into w1 at `452b0ff`, which closed the wave 1 gate.**

---

## Wave 2: renewal, identity, bootstrap, Windows (same local campaign branch `campaign/sprint2-agent-nodes-w1`)

Wave 2 packets branch from the w1 tip once the wave 1 gate holds (it did, at `452b0ff`); there is no separate wave 2 branch. Local merge order into w1: P08 → P09 → P16 → P10 → P11 → P12 → {P13, P14} → P27f.

- P16 (D-S212 grace) is written after P14 below, but it merges after P09.
- P12 merges directly after P11 with nothing in between (P11-ruling-1 D3).
- P27f (fix packet, [P11-ruling-1](worknotes/P11-ruling-1.md)) is written after P14 and merges after it. **P25** (Gateway limiters, D-S220) is Gateway-only and shares no file with any other wave 2 or 2U packet, so it runs in parallel with all of them from the current w1 tip; its default merge slot is right after P09, but it may merge whenever its review is at `ship` (it never waits on, or blocks, P10-P14). Nothing is pushed. **Wave 2 gate** (before P15 and the campaign review): P08-P16, P25 and P27f merged into w1, S1-S4 green on the w1 tip, `Dhadgar.Gateway.Tests` green on the w1 tip, and the wave 2 review at `ship`.

### P08: S3 red, the renewal seam tests (direct and through the Gateway)

- **Branch** `s2/p08-renewal-seam-red`. **Worktree** `…\sprint2-agent-nodes-p08`. **Depends on** the wave 1 gate (P01-P07 merged into w1). **Security review** yes (certificates).
- **Commit subject** `test(seam): red agent certificate renewal seam tests (S3)`

**Rules:** R-USING, R-NULL, R-XUNIT, R-ZERO in test helpers that handle key bytes. Tests only.

**Change**

1. `AgentSeamHarness.TestServerClientFactory`: when `CreateClient(name)` is called with `"ControlPlaneMtls"`, wrap `server.CreateHandler(ctx => ctx.Connection.ClientCertificate = <store's current cert>)`. Take an optional `Func<X509Certificate2?>` constructor argument for the certificate. This mirrors what the agent's mTLS handler presents during the TLS handshake.
   - Also take an optional `Func<HttpMessageHandler, HttpMessageHandler>? wrapMtlsHandler`, applied around that handler. The lost-response test uses it.
   - New `tests/Dhadgar.Nodes.Tests/Integration/AgentSeam/ResponseDroppingHandler.cs` (`DelegatingHandler`, public): for the first request whose path ends in `/certificates/renew`, it awaits `base.SendAsync` (so Nodes commits), disposes the response and throws `new HttpRequestException("simulated lost renewal response")`. Later requests pass through. It exposes `int DroppedCount`.
   - `AgentSeamHarness.CreateFactoryWithRealCa` gets an optional `IReadOnlyDictionary<string, string?>? settings` argument, added through `ConfigureAppConfiguration(c => c.AddInMemoryCollection(settings))`. The grace tests pass `["Mtls:RenewalGraceMinutes"] = "60"`. Until P16 adds the option the key binds to nothing, so this compiles now and pins the window once P16 lands.
2. New `tests/Dhadgar.Nodes.Tests/Integration/AgentRenewalSeamTests.cs` (mTLS enabled, real CA, settings `Mtls:RenewalGraceMinutes=60`):
   - `RealAgent_RenewsWithFreshAgentHeldKey`:
     1. Enroll. Capture the old thumbprint, the old public key (SPKI) and the old private key bytes. **(wave1-ruling-1)** Also fill a second `InMemoryCertificateStore` `oldStore` with the enrolled certificate and key, the same way the enrollment helper fills the first store, before any renewal.
     2. Build the agent `EnrollmentService` with `AgentOptions { NodeId, OrganizationId }` (P11 changes this to the identity provider).
     3. Call `RenewCertificateAsync()`.
     4. Assert:
        - the result succeeded;
        - the store's certificate thumbprint differs from the old one;
        - the new certificate's SPKI equals the public key of the store's new private key **and** differs from the old SPKI;
        - the new certificate chains to the stored CA (copy the existing chain assertion);
        - in the DB, the old `AgentCertificate` row has `IsRevoked` and a new non-revoked row exists for the node;
        - the hub negotiate with the **new** certificate returns 200;
        - the hub negotiate with the **old** certificate still returns 200 (inside the grace window, D-S212);
        - **(wave1-ruling-1)** a real `ControlPlaneClient` over `oldStore` (the **old** certificate, now in grace) connects (`ConnectAsync` succeeds) and its `SendHeartbeatAsync` returns `Success` with `Acknowledged == true`. Copy `CreateAgentClient`, `ConnectAgentAsync` and `SendAgentHeartbeatAsync` from `AgentHubSeamTests` (`:216-256`). This is the only test that proves the hub itself, not just `MtlsMiddleware`, accepts a certificate in grace;
        - after `factory.TimeProvider.Advance(TimeSpan.FromMinutes(61))`, the hub negotiate with the **old** certificate returns 401 and with the **new** one returns 200, and a further `SendHeartbeatAsync` on the old-certificate client does **not** return `Success` with `Acknowledged == true` (either a failure `Result` or `Acknowledged == false` passes: the hub aborts the connection).
   - `RealAgent_RenewalResponseLost_RenewsAgainWithOldCertificateWithinGrace` (D-S212, F12):
     1. Enroll. Build the agent `EnrollmentService` over a `TestServerClientFactory` whose mTLS handler is wrapped in `ResponseDroppingHandler`.
     2. The first `RenewCertificateAsync()` fails (`IsSuccess == false`), and the store still holds the old certificate. Assert `DroppedCount == 1`, and that the DB has a non-revoked orphan row besides the old row.
     3. Advance time by 5 minutes (`AgentCertificateRenewalPolicy.AgentRetryMinutes` once P16 exists; write the literal `5` with a comment now). Call `RenewCertificateAsync()` again: it succeeds.
     4. Assert: the store's certificate is a third certificate; the orphan row is `IsRevoked` with `GraceUntil` null (read it with the helper below, so the test compiles before P16); the old row is still usable (hub negotiate with the old certificate returns 200), and returns 401 once time passes 61 minutes after the **first** renewal (grace is not extended).
   - `Renew_WithoutCertificate_Fails`: an empty store gives a failure result and no DB change.
3. New `tests/Dhadgar.AgentSeam.Tests/GatewayAgentRenewalSeamTests.cs`: the same main flow through the Gateway (`/api/v1/agents/{nodeId}/certificates/renew`), up to and including "old certificate still returns 200 inside the window" **and** the old-certificate `ControlPlaneClient` connecting through the Gateway with an acknowledged heartbeat (wave1-ruling-1; reuse the client helper of `GatewayAgentHubSeamTests`). The time-advance and lost-response cases run only in the direct test.

**Expected red now:** the agent sends `{NodeId, PublicKey}` in PascalCase and Nodes binds `{CurrentThumbprint}`, giving 400 or `thumbprint_mismatch`; the agent also decodes the PEM as bare base64. Record the actual first failure. After P09 and P10 alone, the grace assertions still fail (old certificate 401 at once, `GraceUntil` missing, and `AgentHub` aborts the old-certificate client because it checks `IsRevoked` inline); P16 (steps 6 and 6b) turns them green.

Read `GraceUntil` through this private helper in `AgentRenewalSeamTests`; P16 replaces it with the real property:

```csharp
// Reads AgentCertificate.GraceUntil by name, so the test compiles before P16 adds the property.
private static DateTime? ReadGraceUntil(NodesDbContext db, AgentCertificate row) =>
    db.Model.FindEntityType(typeof(AgentCertificate))?.FindProperty("GraceUntil") is null
        ? null
        : db.Entry(row).Property<DateTime?>("GraceUntil").CurrentValue;
```

**Checks (in order)**

```powershell
dotnet build <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName!~AgentRenewalSeamTests"
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName~AgentRenewalSeamTests"
dotnet test <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj
```

The second command must pass. The third must fail as expected. In the fourth, only the renewal tests fail.

**Docs owed:** none.

### P09: Contracts renewal DTOs, and the Nodes renew endpoint and CA on the public key

- **Branch** `s2/p09-nodes-renewal`. **Worktree** `…\sprint2-agent-nodes-p09`. **Depends on** the wave 1 gate (P01-P07 merged into w1; it can run in parallel with P08). **Security review** yes (certificates, auth).
- **Commit subject** `feat(nodes)!: renew agent certificates from an agent-held public key`

**Rules:** R-CONTRACT (C3 exactly), R-RESULT (endpoint errors through `ProblemDetailsHelper` with the C4 codes), R-USING, R-NULL, R-XUNIT, R-LOG.

- No server-generated private key and no PKCS#12 anywhere on the renewal path (D-S211).
- The current thumbprint comes only from the `certificate_thumbprint` claim.

**Change**

1. `src/Shared/Dhadgar.Contracts/Nodes/EnrollmentContracts.cs`: replace `RenewCertificateRequest` and `RenewCertificateResponse` with C3.
2. `src/Dhadgar.Nodes/Endpoints/AgentEndpoints.cs`, `RenewCertificate` (:143-…):
   - Bind `Dhadgar.Contracts.Nodes.RenewCertificateRequest`. Drop the `LocalRenewCertificateRequest` alias.
   - Keep the `node_id` checks.
   - `var presented = context.User.FindFirst("certificate_thumbprint")?.Value`. If it is missing, return `ProblemDetailsHelper.Unauthorized("certificate_required")`.
   - If `PublicKey` is blank or longer than 4096 characters, return `BadRequest("invalid_public_key")`.
   - Compare `presented` with the latest non-revoked `AgentCertificate`'s thumbprint (case-insensitive). On mismatch, return 401 `thumbprint_mismatch`; the logging stays as is.
   - `var certResult = await caService.IssueCertificateForPublicKeyAsync(nodeId, request.PublicKey, ct)`. `invalid_public_key` gives 400 `invalid_public_key`, and any other failure gives 400 `certificate_renewal_failed`.
   - Keep the rest as it is: new row, revoke the old row, publish the three events before `SaveChangesAsync`. Do **not** add grace here: P16 replaces the thumbprint lookup and the revoke step with the C3a rules (D-S212). Put the lookup and the revoke in two clearly separated blocks so P16's diff stays small.
   - Respond `200 new RenewCertificateResponse(certResult.Thumbprint!, certResult.CertificatePem!, await caService.GetCaCertificatePemAsync(ct), new DateTimeOffset(certResult.NotBefore!.Value, TimeSpan.Zero), new DateTimeOffset(certResult.NotAfter!.Value, TimeSpan.Zero))`.
   - Update `.Produces<…>` metadata if present.
3. `src/Dhadgar.Nodes/Services/ICertificateAuthorityService.cs` and `CertificateAuthorityService.cs`:
   - Delete `RenewCertificateAsync` (the server-key path).
   - Grep the solution for other callers and implementers, including `tests/**/TestCertificateAuthorityService.cs`, and remove them.
   - Leave `IssueCertificateAsync` alone. Its removal is a follow-up; mention it in the report.
   - Keep the `CertificateTrustStatus` enum (P17f) that sits beside `CertificateIssuanceResult` in `ICertificateAuthorityService.cs`, and the `Task<CertificateTrustStatus> ValidateCertificateAsync` signature.
4. `tests/Dhadgar.Nodes.Tests/NodesWebApplicationFactory.cs`, `TestNodesAuthHandler`: add `public const string CertThumbprintHeader = "X-Test-Cert-Thumbprint";`. When the header is present, add the claim `certificate_thumbprint`.

**Tests**

Rewrite `tests/Dhadgar.Nodes.Tests/**/CertificateRenewalIntegrationTests.cs` against C3. Use the test auth headers and seed the node and certificate as the file does today:

- `Renew_ValidKey_IssuesCertBoundToRequestKey`: the response certificate's SPKI equals the request key.
- `Renew_ResponseHasNoPkcs12`: the raw JSON contains no `pkcs12`.
- `Renew_RevokesPreviousCertificate`.
- `Renew_MissingThumbprintClaim_Returns401CertificateRequired`.
- `Renew_ThumbprintMismatch_Returns401`.
- `Renew_InvalidPublicKey_Returns400`: garbage, and RSA-1024.
- `Renew_OtherNode_Returns403ProblemJson`.

Revert check: the old contract has no `PublicKey`, and the server-key path returns PKCS#12.

**Checks (in order)**

```powershell
dotnet build <wt>\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <wt>\src\Agents\Dhadgar.Agent.Core\Dhadgar.Agent.Core.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Contracts.Tests\Dhadgar.Contracts.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName!~AgentRenewalSeamTests"
```

**Docs owed:** update the renewal section of `src/Dhadgar.Nodes/README.md`: agent-held key, thumbprint from mTLS, no PKCS#12.

### P10: Agent renewal on Contracts, PKCS#8 export, per-handshake mTLS certificate

- **Branch** `s2/p10-agent-renewal`. **Worktree** `…\sprint2-agent-nodes-p10`. **Depends on** P09 and P16 (merged into w1). **Security review** yes (agent, certificates).
- **Commit subject** `feat(agent-core): renew with a fresh agent-held key over the Contracts DTOs; PKCS#8 key handoff`

**Rules:** R-RESULT, R-ZERO (every exported key buffer, in `finally`), R-USING, R-CONTRACT (C3, `AgentCertificateProfile`), R-HTTPS, R-NULL, R-XUNIT, R-LOG.

- Response size caps stay: `SizeLimitedStream` and `MaxCertificateBase64Length`, which must allow PEM, i.e. at least 8192.

**Change**

1. `src/Agents/Dhadgar.Agent.Core/Authentication/EnrollmentService.cs`:
   - **Enrollment (:230):** `ExportECPrivateKey()` becomes `ExportPkcs8PrivateKey()`. Zeroing is unchanged. After storing, call `_clientCertificateProvider.Invalidate()`; the provider is a new constructor dependency.
   - **`RenewCertificateAsync` (from :321), rewritten:**

     ```csharp
     // 1. NodeId from _options (P11 swaps to identity provider); missing → Failure("[Renewal.NotEnrolled] …")
     // 2. using var current = _certificateStore.GetClientCertificate(); null → Failure("[Renewal.NoCertificate] …"); oldExpiry = current.NotAfter
     // 3. using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
     //    var request = new RenewCertificateRequest(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
     // 4. using var client = _httpClientFactory.CreateClient("ControlPlaneMtls");
     //    POST $"api/v1/agents/{nodeId}/certificates/renew" with JsonContent.Create(request, options: WireJsonOptions)
     // 5. non-success → Failure("[Renewal.Rejected] {status} {errorCode}") (parse problem+json errorCode like enrollment does, size-limited)
     // 6. deserialize RenewCertificateResponse with WireJsonOptions via SizeLimitedStream; null/oversize → Failure
     // 7. using var cert = DecodeCertificate(response.Certificate);
     //    SPKI(cert) must equal ecdsa.ExportSubjectPublicKeyInfo() → else Failure("[Renewal.KeyMismatch]")
     //    SAN URI must equal AgentCertificateProfile.SpiffeId(nodeId) → else Failure("[Renewal.WrongIdentity]")
     // 8. var key = ecdsa.ExportPkcs8PrivateKey(); try { await _certificateStore.StoreCertificateAsync(cert, key, ct); } finally { ZeroMemory(key); }
     //    _clientCertificateProvider.Invalidate();
     // 9. if CaCertificate present: using var ca = DecodeCertificate(...); await StoreCaCertificateAsync(ca, ct)
     // 10. Success(new CertificateRenewalResult { NewExpiry = cert.NotAfter, OldExpiry = oldExpiry })
     ```

     Catch the same exception types as `EnrollAsync` and turn each into a `Failure`.
   - Delete the local classes `CertificateRenewalRequest` and `CertificateRenewalResponse` (:564…).
2. `src/Agents/Dhadgar.Agent.Core/Authentication/ICertificateStore.cs`: the doc of `StoreCertificateAsync`'s `privateKey` becomes "PKCS#8 DER (PrivateKeyInfo) on every platform; the caller zeroes it after the call".
3. New `src/Agents/Dhadgar.Agent.Core/Authentication/IClientCertificateProvider.cs` and `ClientCertificateProvider.cs` (C7):
   - Lazily load from `ICertificateStore` under a lock.
   - `Invalidate()` disposes the cached certificate and clears it.
   - Register it as a singleton in `AddAgentCore`.
4. `src/Agents/Dhadgar.Agent.Core/Hosting/AgentServiceCollectionExtensions.cs`, the `ControlPlaneMtls` handler (:~110-125):
   - Replace the read-once certificate with a `SocketsHttpHandler`:
     - `AllowAutoRedirect = false`;
     - `PooledConnectionLifetime = TimeSpan.FromMinutes(1)`;
     - `SslOptions.LocalCertificateSelectionCallback = (_, _, _, _, _) => provider.Current!` (with a comment: null means no certificate; the server answers 401);
     - keep the existing server-certificate validation callback.
   - Delete the throw on "NodeId set but no cert".
5. The test harness `InMemoryCertificateStore`: keep the PKCS#8-first import.

**Tests**

- New `tests/Dhadgar.Agent.Core.Tests/Authentication/EnrollmentServiceRenewalTests.cs`, using a stub `IHttpClientFactory` and handler plus a stub store. Build the response certificate in the test from a test CA, signing the SPKI the agent posted. Cases:
  - the request body is exactly the camelCase `{"publicKey":"…"}`;
  - a PEM response is decoded;
  - the stored key bytes load with `ImportPkcs8PrivateKey`;
  - a key-mismatch response fails without storing;
  - a wrong SPIFFE fails without storing;
  - a 401 problem response gives a failure containing `thumbprint_mismatch`;
  - an oversized response fails;
  - after success, the provider is invalidated.
- `tests/Dhadgar.Agent.Core.Tests/Authentication/EnrollmentServicePkcs8Tests.cs`: the enrollment stores PKCS#8.
- `ClientCertificateProviderTests`: `Invalidate` makes the next `Current` come from the store.
- `tests/Dhadgar.Nodes.Tests/Integration/AgentEnrollmentContractTests.cs:78`: `agentKey.ImportECPrivateKey(...)` becomes `agentKey.ImportPkcs8PrivateKey(...)`. This is the enrollment seam's pin on the key format; it fails if enrollment reverts to SEC1.
- S3 must now be **green**, both direct and through the Gateway, including the grace and lost-response cases (P16 has merged).

**Checks (in order)**

```powershell
dotnet build <wt>\src\Agents\Dhadgar.Agent.Core\Dhadgar.Agent.Core.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <wt>\src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Core.Tests\Dhadgar.Agent.Core.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj
dotnet test <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj
```

**Docs owed:** in `src/Agents/Dhadgar.Agent.Core/README.md`, add a renewal flow section and the PKCS#8 handoff note for `ICertificateStore` implementers.

### P11: Agent identity, `IAgentStateStore` and `IAgentIdentityProvider` (#101)

- **Branch** `s2/p11-agent-identity`. **Worktree** `…\sprint2-agent-nodes-p11`. **Depends on** P10. **Security review** yes (agent).
- **Commit subject** `feat(agent-core): persist and provide agent identity; heartbeat waits on it (#101)`

**Rules:** R-RESULT, R-PATH (state file path validated against `[StateDirectory]`), R-USING, R-NULL, R-XUNIT, R-LOG.

- The file holds no secrets.
- A corrupt state file is a `Failure`, never a silent re-enroll.

**Change**

1. New in `src/Agents/Dhadgar.Agent.Core/Identity/`: `AgentIdentity.cs`, `IAgentStateStore.cs` and `IAgentIdentityProvider.cs` (C7), plus the implementations:
   - `FileAgentStateStore`:
     - The file is `Path.Combine(StateDirectory, "agent-state.json")`, validated with `IPathValidator.ValidatePath(path, [StateDirectory])`.
     - Use System.Text.Json web defaults and a 64 KB read cap.
     - Save writes `agent-state.json.tmp`, flushes, then `File.Move(tmp, final, overwrite: true)`, and creates the directory if missing.
     - A missing file gives `Success(StoredAgentState.None)` (D-S232; `Success(null)` throws in the shared `Result<T>`). Bad JSON, `Guid.Empty` or oversize gives `Failure("[AgentState.Corrupt] …")`. An IO error gives `Failure("[AgentState.Io] …")`. File creation, durability, trust and the path rule follow D-S233 ([P11-ruling-1](worknotes/P11-ruling-1.md), steps 3-6).
   - `AgentIdentityProvider`:
     - Use a `TaskCompletionSource<AgentIdentity>(TaskCreationOptions.RunContinuationsAsynchronously)` and a lock.
     - `Set` with the same `NodeId` succeeds idempotently; a different `NodeId` gives `Failure("[AgentIdentity.Changed] restart required")`.
2. `src/Agents/Dhadgar.Agent.Core/Configuration/AgentOptions.cs`:
   - Add `public string StateDirectory { get; set; } = string.Empty;`.
   - In `AddAgentCore`, add `services.PostConfigure<AgentOptions>(o => { if (string.IsNullOrWhiteSpace(o.StateDirectory)) o.StateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Meridian Console", "Agent"); })`.
   - `Validate`: `StateDirectory`, when set, must be rooted.
   - Replace the rule "NodeId set ⇒ Security certificate config required" with "NodeId and OrganizationId are both set or both null". The certificate now comes from the store (F6, F7).
3. Switch every runtime read of `_options.NodeId` / `_options.OrganizationId` to `IAgentIdentityProvider.Current`. Inject the provider through the constructor and keep each existing branch's meaning (F10 stays a follow-up):
   - `Authentication/EnrollmentService.cs`: `IsEnrolled` and `RenewCertificateAsync`.
   - `Communication/ControlPlaneClient.cs`: the activity tag and `OnCommandReceived` (:353, :363).
   - `Commands/CommandValidator.cs` (:49, :60).
   - `Commands/CommandDispatcher.cs` (:124-167).
   - `Health/HealthReporter.cs` (:113, :149).
   - `Health/HeartbeatService.cs`: replace the polling loop (:36-39) with `await _identity.WaitForIdentityAsync(stoppingToken);`.
4. DI in `AddAgentCore`:
   - `TryAddSingleton<IAgentIdentityProvider, AgentIdentityProvider>()`;
   - `TryAddSingleton<IAgentStateStore, FileAgentStateStore>()`;
   - register `IPathValidator` with `TryAddSingleton` if it is not registered yet.
5. Update every seam-test construction site (`AgentSeamHarness`, `AgentHubSeamTests`, `AgentRenewalSeamTests`, the Gateway seam tests and `AgentHubTests` if affected) to pass an `AgentIdentityProvider` with `Set(...)` called after enrollment.

**Tests** (in `tests/Dhadgar.Agent.Core.Tests/Identity/`)

- `FileAgentStateStoreTests` (temp dir):
  - round trip;
  - a missing file or directory gives `StoredAgentState.None`, and a path that cannot be read gives a failure;
  - corrupt JSON gives a failure;
  - `Guid.Empty` gives a failure;
  - no `.tmp` is left after save;
  - a `StateDirectory` containing `..` segments, or a validator rejection, gives a failure.
- `AgentIdentityProviderTests`:
  - a wait completes on `Set`;
  - setting the same node twice succeeds;
  - setting a different node fails;
  - a wait honours cancellation.
- `HeartbeatServiceTests.Heartbeat_StartsAfterIdentityIsSet_WithoutOptionsNodeId`: use a fake `IControlPlaneClient` (connected) and a fake `IHealthReporter`; set the identity after `StartAsync`; assert a heartbeat is sent within 10 s (`HeartbeatService` has a fixed 2 s startup delay, `HeartbeatService.cs:54`). This is the #101 deadlock regression test: on revert the service waits on `AgentOptions.NodeId` forever.
- All the seam tests (S1-S3) stay green.

**Checks (in order)**

```powershell
dotnet build <wt>\src\Agents\Dhadgar.Agent.Core\Dhadgar.Agent.Core.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <wt>\src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Core.Tests\Dhadgar.Agent.Core.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj
dotnet test <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj
```

**Docs owed:** add an "Identity and state" section to `src/Agents/Dhadgar.Agent.Core/README.md` covering the file location, format and the corrupt-file behaviour.

**Fix pass 1 ([P11-ruling-1](worknotes/P11-ruling-1.md), D-S232, D-S233):**

- `LoadAsync` returns `Result<StoredAgentState>`.
- New `IStateFileSecurity` with the Core default `PortableStateFileSecurity`.
- `CreateNew`, write-through and `Flush(true)`; a stale temp file is deleted first.
- `AgentOptions.CheckStateDirectory`, a fully qualified local path, shared by `Validate` and the store (`[AgentState.Config]`).
- `Set` compares `OrganizationId`.
- New `AgentOptionsValidationTests` and `PortableStateFileSecurityTests`.

Commit subject `fix(agent-core): typed state load, hardened state file, org-change check (P11-ruling-1)`.

### P12: `AgentBootstrapService` and the enrollment token source

- **Branch** `s2/p12-agent-bootstrap`. **Worktree** `…\sprint2-agent-nodes-p12`. **Depends on** P11. **Security review** yes (agent, enrollment).
- **Commit subject** `feat(agent-core): startup service enrolls if needed, persists identity, connects and renews (#101)`

**Rules:** R-RESULT, R-USING, R-NULL, R-XUNIT, R-LOG (never log the token), R-HTTPS.

- All delays go through `TimeProvider` so they can be tested.
- Never retry enrollment in a tight loop.

**Change**

1. New `src/Agents/Dhadgar.Agent.Core/Authentication/IEnrollmentTokenSource.cs` (C7) and `ConfigurationEnrollmentTokenSource.cs`:
   - It reads `IConfiguration["Agent:EnrollmentToken"]` directly, **not** bound into `AgentOptions`, so options dumps never carry it.
   - A blank value gives `Success(string.Empty)` (D-S232). The enrollment loop treats `string.IsNullOrWhiteSpace(token.Value)` as "no token".
2. `ControlPlaneOptions`: add `RenewalCheckIntervalMinutes` (default 720), `RenewalRetryMinutes` (default `AgentCertificateRenewalPolicy.AgentRetryMinutes`, i.e. 5; valid range 1-30, so retries stay inside Nodes' default 60-minute grace window, D-S212), `ConnectionCheckIntervalSeconds` (default 60) and `EnrollmentRetrySeconds` (default 60), with range validation in the existing `Validate`.
3. New `src/Agents/Dhadgar.Agent.Core/Hosting/AgentBootstrapService.cs` (`BackgroundService`). Constructor dependencies: `IAgentStateStore`, `IAgentIdentityProvider`, `IEnrollmentService`, `IEnrollmentTokenSource`, `ICertificateStore`, `IControlPlaneClient`, `IOptions<AgentOptions>`, `TimeProvider`, `ILogger`.

   ```csharp
   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
       var identity = await ResolveIdentityAsync(stoppingToken);
       if (identity is null) return;                       // stopping, or corrupt state (logged Critical)

       var set = _identity.Set(identity);
       if (!set.IsSuccess) { _logger.LogCritical(...); return; }

       var nextRenewalCheck = DateTimeOffset.MinValue;     // check at once on start: a crash after a renewal commit is recovered inside the grace window
       var connectDelay = TimeSpan.FromSeconds(_cp.ReconnectDelaySeconds);
       do
       {
           if (_client.State != ConnectionState.Connected)
           {
               var connected = await _client.ConnectAsync(stoppingToken);
               connectDelay = connected.IsSuccess ? TimeSpan.FromSeconds(_cp.ReconnectDelaySeconds)
                                                  : Min(connectDelay * 2, TimeSpan.FromSeconds(_cp.MaxReconnectDelaySeconds));
               if (!connected.IsSuccess) { log Warning; await Task.Delay(connectDelay, _time, stoppingToken); continue; }
           }

           if (_time.GetUtcNow() >= nextRenewalCheck)
           {
               nextRenewalCheck = _time.GetUtcNow() + TimeSpan.FromMinutes(_cp.RenewalCheckIntervalMinutes);
               if (_certificateStore.NeedsRenewal(AgentCertificateProfile.RenewalThresholdDays))
               {
                   var renewed = await _enrollment.RenewCertificateAsync(stoppingToken);
                   // D-S231: the hub pins its certificate per HubConnection; only a disconnect plus ConnectAsync presents the renewed one.
                   if (renewed.IsSuccess) { await _client.DisconnectAsync(stoppingToken); /* next pass reconnects with the new cert */ }
                   else
                   {
                       // D-S212: the response may have been lost after Nodes committed. The old certificate
                       // can still renew only inside the grace window, so retry soon, not after the full interval.
                       log Error;
                       nextRenewalCheck = _time.GetUtcNow() + TimeSpan.FromMinutes(_cp.RenewalRetryMinutes);
                   }
               }
           }
       }
       while (await WaitAsync(TimeSpan.FromSeconds(_cp.ConnectionCheckIntervalSeconds), stoppingToken));
   }
   ```

   `ResolveIdentityAsync` runs in order:
   1. `store.LoadAsync()`. **Any** failure is logged Critical with its code and returns null; do **not** enroll over corrupt, unreadable or untrusted state. `Success` with `Identity == null` (`StoredAgentState.None`) is a first run: go to step 4. (Amendment A2.)
   2. If the load succeeded with an identity and `GetClientCertificate()` is not null **and its SPIFFE SAN matches the identity's `NodeId`** (amendment A2), return it.
   3. If it succeeded with an identity but there is no certificate, log Error ("state present but certificate missing; a new enrollment token is required") and fall through to enrollment.
   4. If `AgentOptions.NodeId` and `OrganizationId` are both set and a certificate exists **whose SPIFFE SAN matches that `NodeId`** (amendment A2), save that identity and return it (the D-S213 seed).
   5. Enrollment loop:
      - Read the token. If there is none, log Warning once per hour at most, wait `EnrollmentRetrySeconds` and loop.
      - Call `EnrollAsync(token)`. On failure, log Error with the error code (never the token), wait with backoff (`EnrollmentRetrySeconds`, doubling, capped at 15 min) and loop. The token source is re-read every loop, so an operator can fix a bad token.
      - On success, build `identity = new(r.NodeId, r.OrganizationId, now)` and call `SaveAsync`. Retry the save 3 times, 2 s apart. If it still fails, log Critical ("identity not persisted; the next restart will need a new token") and continue in memory.
      - Return the identity.
4. `AddAgentCore`:
   - `TryAddSingleton<IEnrollmentTokenSource, ConfigurationEnrollmentTokenSource>()`;
   - `TryAddSingleton(TimeProvider.System)`;
   - `services.AddHostedService<AgentBootstrapService>()`, registered **before** `HeartbeatService`.
5. `tests/Dhadgar.Agent.Core.Tests/Dhadgar.Agent.Core.Tests.csproj`: add `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />` and `<PackageReference Include="NSubstitute" />` with no versions; both are already in `Directory.Packages.props`.

**Tests** (`tests/Dhadgar.Agent.Core.Tests/Hosting/AgentBootstrapServiceTests.cs`, using `FakeTimeProvider` and NSubstitute fakes)

- `StoredIdentityWithCertificate_ConnectsWithoutEnrolling`.
- `NoIdentity_WithToken_EnrollsSavesSetsIdentityAndConnects`: verify the order is enroll, then save, then set, then connect.
- `NoIdentity_NoToken_DoesNotEnroll_AndRetriesAfterInterval`.
- `CorruptState_DoesNotEnroll`.
- `IdentityWithoutCertificate_EnrollsAgain`.
- `ConnectFailure_BacksOffAndRetries`.
- `RenewalDue_RenewsThenReconnects`: assert with `Received.InOrder` that `RenewCertificateAsync`, then `DisconnectAsync`, then `ConnectAsync` are called (the fake's `State` reports `Disconnected` after the disconnect). Revert check: without the disconnect the hub stays on the pre-renewal certificate pinned in its `HubConnection`, which Nodes refuses once the grace window closes (D-S231).
- `ClientReconnecting_IsRebuiltByConnectAsync` (D-S231): the fake client's `State` is `ConnectionState.Reconnecting`; after one `ConnectionCheckIntervalSeconds`, `ConnectAsync` has been called. Revert check: a guard such as `State is Connected or Reconnecting` leaves SignalR's automatic reconnect (whose `RetryPolicy` never gives up) retrying with the pinned certificate, so a node whose old certificate has left grace never recovers.
- `RenewalFailure_RetriesAfterRetryInterval_NotBefore`: the first renewal fails; advancing time by `RenewalRetryMinutes - 1` gives no second call, and advancing past `RenewalRetryMinutes` gives exactly one. Revert check: with the full-interval wait (720 min) the retry lands after Nodes' 60-minute grace has closed, and the node is stranded (F12).
- `RenewalSuccess_NextCheckAfterFullInterval`: after a successful renewal no further `RenewCertificateAsync` call happens before `RenewalCheckIntervalMinutes`.
- `ControlPlaneOptions_DefaultRenewalRetry_IsInsideGraceWindow`: `new ControlPlaneOptions().RenewalRetryMinutes * 4 <= AgentCertificateRenewalPolicy.DefaultGraceMinutes`.
- `SaveFailure_StillConnects_LogsCritical`.

Revert check: without the service nothing enrolls or connects, which is #101.

**Checks (in order)**

```powershell
dotnet build <wt>\src\Agents\Dhadgar.Agent.Core\Dhadgar.Agent.Core.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <wt>\src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Core.Tests\Dhadgar.Agent.Core.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj
dotnet test <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj
```

**Ruling 1 (D-S234, [P12-ruling-1](worknotes/P12-ruling-1.md)).** The `Dhadgar.Nodes.Tests` line above was added by the ruling. The enrollment validity check stays; the seam harness gives the agent the Nodes host's clock (`CreateEnrollmentService` takes a required `TimeProvider`), and `RealAgent_EnrollsWhileNodesClockRunsAhead` pins it. The throwing-`StopAsync` test is an accepted gap.

**Amendment A1 (D-S221, 2026-10-27; it overrides step 3 where they differ). The renewal threshold and switch come from `SecurityOptions`:**

1. `src/Agents/Dhadgar.Agent.Core/Configuration/SecurityOptions.cs`:
   - `CertificateRenewalThresholdDays` becomes `[Range(1, AgentCertificateProfile.ValidityDays)] public int CertificateRenewalThresholdDays { get; set; } = AgentCertificateProfile.RenewalThresholdDays;`. Add `using Dhadgar.Contracts.Nodes;` if Core does not already import it. Update the XML doc: "Days before expiry at which the agent renews. Default and upper bound come from `AgentCertificateProfile`."
   - Any range check for this property inside `Validate` uses the same two constants.
   - `EnableCertificateAutoRenewal` keeps its default `true`.
2. `src/Agents/Dhadgar.Agent.Windows/appsettings.json`: delete the `"CertificateRenewalThresholdDays": 14` line (fix the trailing comma). Grep `src/` and `deploy/` for other `CertificateRenewalThresholdDays` settings and list them in the report; do not change them.
3. `AgentBootstrapService`: keep `_security = options.Value.Security` beside `_cp`. The renewal block in step 3 becomes:

   ```csharp
   if (_time.GetUtcNow() >= nextRenewalCheck)
   {
       nextRenewalCheck = _time.GetUtcNow() + TimeSpan.FromMinutes(_cp.RenewalCheckIntervalMinutes);
       if (!_security.EnableCertificateAutoRenewal)
       {
           if (!_autoRenewalOffLogged) { _logger.LogWarning("Certificate auto-renewal is disabled (Agent:Security:EnableCertificateAutoRenewal=false)"); _autoRenewalOffLogged = true; }
       }
       else if (_certificateStore.NeedsRenewal(_security.CertificateRenewalThresholdDays))
       {
           // unchanged: renew, disconnect on success, retry after RenewalRetryMinutes on failure
       }
   }
   ```

4. Extra tests in `AgentBootstrapServiceTests`:
   - `RenewalThreshold_ComesFromSecurityOptions`: with `CertificateRenewalThresholdDays = 90`, the store fake receives `NeedsRenewal(90)`. Revert check: with the constant it receives 30.
   - `AutoRenewalDisabled_NeverRenews`: with `EnableCertificateAutoRenewal = false` and `NeedsRenewal` returning true, `RenewCertificateAsync` is never called across two check intervals.
   - In `tests/Dhadgar.Agent.Core.Tests/Configuration/SecurityOptionsTests.cs` (create it if missing): `Default_RenewalThreshold_IsProfileValue` (`new SecurityOptions().CertificateRenewalThresholdDays == AgentCertificateProfile.RenewalThresholdDays`) and `RenewalThreshold_AboveValidity_FailsValidation` (91 fails `Validator.TryValidateObject` with `validateAllProperties: true`; 90 passes).
5. Why: the Windows UAT (P21) forces a real renewal by setting `Agent:Security:CertificateRenewalThresholdDays=90`. Without this amendment the setting is ignored and U6 cannot pass.

**Amendment A2 (D-S232, D-S233, [P11-ruling-1](worknotes/P11-ruling-1.md); it overrides steps 1 and 3 where they differ). Typed state load, fail closed, identity bound to the certificate:**

1. **Load.**
   - `var loaded = await _store.LoadAsync(ct)`. If `loaded.IsFailure`, log Critical `"Agent state unusable ({Error}); not enrolling. Remove agent-state.json and supply a new token to recover"` and return null.
   - Never inspect the error text to decide whether to enroll.
   - `loaded.Value!.Identity is null` means a first run.
2. **Certificate binding.**
   - New `src/Agents/Dhadgar.Agent.Core/Authentication/AgentCertificateIdentity.cs`: `internal static class AgentCertificateIdentity { internal static bool HasOnlySpiffeUri(X509Certificate2 certificate, string expectedUri) }`. Move the body of `EnrollmentService.HasOnlySpiffeUri` (`EnrollmentService.cs:616`) there unchanged, and point `EnrollmentService` (`:483`) at it.
   - In `ResolveIdentityAsync`, read the certificate with `using var cert = _certificateStore.GetClientCertificate();`, because the caller owns it.
   - Step 2 returns the stored identity only if `cert is not null && AgentCertificateIdentity.HasOnlySpiffeUri(cert, AgentCertificateProfile.SpiffeId(identity.NodeId))`.
   - When a certificate exists but does not match, log Critical `[AgentState.CertificateMismatch]` with both node IDs (the stored one and the one parsed from the SAN, if any) and return null. Do not enroll and do not connect.
   - The step 4 config seed uses the same check. On a mismatch it logs Critical and returns null.
3. **Token.** `ConfigurationEnrollmentTokenSource.GetToken()` returns `Result<string>`: `Success(string.Empty)` when blank, `Success(value.Trim())` otherwise. The loop's "no token" branch is `token.IsSuccess && string.IsNullOrWhiteSpace(token.Value)`. A `Failure` is logged Warning (code only) and handled like "no token".
4. **Extra tests** in `AgentBootstrapServiceTests`:
   - `LoadFailure_Io_DoesNotEnroll` and `LoadFailure_Untrusted_DoesNotEnroll`: the store fake returns those failures; `EnrollAsync` and `ConnectAsync` are never called.
   - `StoredIdentity_CertificateForOtherNode_DoesNotEnrollOrConnect`: the certificate's SAN is SPIFFE(other node); neither `EnrollAsync`, `Set` nor `ConnectAsync` is called. Revert check: without the binding the agent connects as a node its certificate does not name.
   - `ConfigSeed_CertificateForOtherNode_DoesNotSeed`: `SaveAsync` is never called.
   - Existing `NoIdentity_*` tests use `Result<StoredAgentState>.Success(StoredAgentState.None)`.
   - In `tests/Dhadgar.Agent.Core.Tests/Authentication/ConfigurationEnrollmentTokenSourceTests.cs`: `Blank_ReturnsEmptyNotFailure` and `Value_IsTrimmed`.
   - Build certificates with the real profile (`AgentCertificateProfile.SpiffeId`) through `CertificateRequest` with a SAN URI.
5. **Docs** (adds to "Docs owed"): the "Startup sequence" section names the fail-closed load and the certificate binding, and gives the recovery step.

**Docs owed:** add a "Startup sequence" section to `src/Agents/Dhadgar.Agent.Core/README.md`, with the states, the retry and backoff rules, the token source, the renewal cadence and the renewal retry inside Nodes' grace window (D-S212). Name the two settings `Agent:Security:EnableCertificateAutoRenewal` and `Agent:Security:CertificateRenewalThresholdDays` (default 30, range 1-90) in it.

### P13: S4, the bootstrap seam through the Gateway

- **Branch** `s2/p13-bootstrap-seam`. **Worktree** `…\sprint2-agent-nodes-p13`. **Depends on** P12. **Security review** yes (enrollment path end to end).
- **Commit subject** `test(seam): fresh agent enrolls, persists and reconnects without re-enrolling (S4)`

**Rules:** R-USING, R-NULL, R-XUNIT, R-CPM. Tests only. If a production defect shows up, stop and report `blocked spec`; do not fix production code in this packet.

**Change**

New `tests/Dhadgar.AgentSeam.Tests/AgentBootstrapSeamTests.cs` using `GatewayNodesHarness`.

1. `FreshAgent_EnrollsPersistsConnectsAndHeartbeats_ThenRestartReusesIdentity`:
   1. Create a temp state dir, one shared `InMemoryCertificateStore` and a Nodes enrollment token.
   2. **Run 1:** build `Host.CreateApplicationBuilder()` and set `Agent:ControlPlane:Endpoint = https://localhost`, `Agent:StateDirectory = <temp>` and `Agent:ControlPlane:HeartbeatIntervalSeconds = 1`. Call `services.AddAgentCore(configuration)` (the real extension). Then replace:
      - `ICertificateStore` with the shared store;
      - `IHttpClientFactory` with the harness's Gateway client factory;
      - `ControlPlaneHubTransport` with the Gateway certificate-injecting transport (LongPolling);
      - `IEnrollmentTokenSource` with a fixed token.
      
      Remove any hosted service that needs Windows or real processes, and list which ones in the report.
   3. Start the host. Wait up to 15 s until the Nodes DB has a node in the org with a `LastHeartbeat`. Stop the host.
   4. Assert that `agent-state.json` exists and holds that `NodeId`.
   5. **Run 2:** a new host over the same state dir and store, with a token source that returns `Success(string.Empty)` (D-S232). Start it, wait until `LastHeartbeat` advances (advance the Nodes `FakeTimeProvider` between runs so the change is visible), then stop.
   6. Assert that the org still has exactly one node and that `EnrollAsync` was never called in run 2. Wrap `IEnrollmentService` in a counting decorator.

Revert check: without P12 nothing enrolls, and without P11 the second run has no identity and waits forever.

**Amendment A1 (D-S234, [P12-ruling-1](worknotes/P12-ruling-1.md)). The clock budget.** The agent host keeps `TimeProvider.System`: its delay loops would never fire on a fake clock. The agent rejects a certificate whose `NotBefore` is more than 5 minutes ahead of its clock, and the Nodes CA stamps `NotBefore` from `harness.NodesBase.TimeProvider`. So:

- Run 1 enrolls before any `NodesBase.TimeProvider.Advance`.
- Between runs, advance it by **2 minutes** in total, and by no more than 4 minutes over the whole test. Put a comment on the advance that cites D-S234.
- Use a fresh `GatewayNodesHarness` per test, as the existing Gateway tests do, so no other test's advance carries over.

**Checks (in order)**

```powershell
dotnet build <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj
```

**Docs owed:** none.

### P14: Windows certificate store profile matching, registry token source and `Program` wiring

- **Branch** `s2/p14-windows-agent`. **Worktree** `…\sprint2-agent-nodes-p14`. **Depends on** P10, P12. **Security review** yes (agent, certificates, Windows).
- **Commit subject** `fix(agent-windows): find Nodes-issued certs by profile, import PKCS#8, keep the agent CA out of Root`

**Rules:** R-RESULT, R-ZERO (PFX bytes and password), R-USING (every `X509Store`, `X509Certificate2`, `RegistryKey`, `ECDsa`), R-CONTRACT (`AgentCertificateProfile`), R-NULL, R-XUNIT, R-LOG (never log the token).

- Add the new certificate **before** removing the old one, so a crash leaves two certificates rather than none.
- Decision D-S219 (made in this design): the agent CA is stored in the custom machine store `MeridianConsoleAgent`, **never** in `LocalMachine\Root`. Root would make Windows trust anything the agent CA signs, machine-wide.

**Change**

1. New `src/Agents/Dhadgar.Agent.Core/Authentication/AgentCertificateMatcher.cs` (static; it lives in Core so Linux CI tests it):
   - `IsAgentClientCertificate(X509Certificate2 c, DateTimeOffset now)` is true when the issuer simple name equals `CaCommonName`, `NotBefore <= now < NotAfter` and some SAN URI starts with `SpiffeNodeIdPrefix`.
   - `IsAgentCa(X509Certificate2 c)` is true when the subject simple name equals `CaCommonName`.
2. `src/Agents/Dhadgar.Agent.Windows/Windows/WindowsCertificateStore.cs`:
   - Delete `ClientSubjectName` (:46) and `CaSubjectName` (:51).
   - Add a constructor parameter `StoreLocation location = StoreLocation.LocalMachine`, used for tests.
   - `GetClientCertificate`: in the `My` store, take certificates with `HasPrivateKey` where `AgentCertificateMatcher.IsAgentClientCertificate` holds, and return the one with the latest `NotAfter`, as a new instance. Dispose the rest.
   - `StoreCertificateAsync(cert, pkcs8)`:
     - `using var key = ECDsa.Create(); key.ImportPkcs8PrivateKey(pkcs8, out _)`. Fall back to RSA `ImportPkcs8PrivateKey` only if the existing code supports RSA; delete the PEM branch if nothing passes PEM any more.
     - `using var withKey = cert.CopyWithPrivateKey(key)`.
     - Export a PFX with a random 32-byte password and re-load it with `X509CertificateLoader.LoadPkcs12(pfx, pwd, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet)` (`UserKeySet` when the location is `CurrentUser`). Zero the PFX and the password chars.
     - Add it to `My`, then remove the other certificates that match `IsAgentClientCertificate` (ignoring validity time) and have a different thumbprint.
   - `GetCaCertificate` / `StoreCaCertificateAsync`: use `new X509Store("MeridianConsoleAgent", location)` with `IsAgentCa`. The store must remove any earlier agent CA it put in `Root`; match by `IsAgentCa` in `Root` and remove it, once, logged.
3. New `src/Agents/Dhadgar.Agent.Windows/Installation/RegistryEnrollmentTokenSource.cs` (`IEnrollmentTokenSource`):
   - Read `HKLM\SOFTWARE\Meridian Console\Agent\EnrollmentToken`.
   - Move the key path and value name constants from `EnrollmentTokenCleanup` into `internal static class AgentRegistry` and use them from both classes.
   - A missing key or value gives `Success(string.Empty)` (D-S232). `SecurityException` and `IOException` give `Failure`.
4. `src/Agents/Dhadgar.Agent.Windows/Program.cs`:
   - Register `IEnrollmentTokenSource` as `RegistryEnrollmentTokenSource` **before** `AddAgentCore`, so Core's `TryAdd` keeps it.
   - The `AgentPipeServer` registration (:97) gets the node ID from `IAgentIdentityProvider` at use time instead of `AgentOptions.NodeId ?? Guid.Empty`. Change its constructor to take the provider if needed.
   - The log at :158 reads the provider.
   - `StateDirectory` defaults through Core's `PostConfigure`. Nothing else changes.

**Tests**

- `tests/Dhadgar.Agent.Core.Tests/Authentication/AgentCertificateMatcherTests.cs`, which runs on Linux CI:
  - a Nodes-shaped certificate matches (build it with the real profile values);
  - `CN=dhadgar-agent` from another issuer does not;
  - an expired certificate does not;
  - a certificate without a SPIFFE SAN does not;
  - the CA matches `IsAgentCa`.
- `tests/Dhadgar.Agent.Windows.Tests/WindowsCertificateStoreProfileTests.cs`:
  - Windows only, using `StoreLocation.CurrentUser` and cleaning up after itself. Use the project's existing skip mechanism; if there is none, return early with `if (!OperatingSystem.IsWindows()) return;` and a comment.
  - Cases:
    - storing a PKCS#8 key with a Nodes-shaped certificate means `GetClientCertificate()` returns it with a usable private key (sign and verify);
    - after a renewal store, only the new certificate is left;
    - the CA lands in `MeridianConsoleAgent` and not in `Root`;
    - a non-agent certificate in `My` is ignored.
- `RegistryEnrollmentTokenSource` has no unit test because it needs HKLM. The Windows UAT (P20, steps U2-U3) exercises it.

**Amendment A1 (2026-10-27, for the Windows UAT):**

- In `WindowsCertificateStore`, the custom CA store name is a constant: `internal const string CaStoreName = "MeridianConsoleAgent";`. Every `new X509Store(...)` for the CA store uses it.
- `AgentRegistry` (step 3) is `internal static class AgentRegistry { internal const string KeyPath = @"SOFTWARE\Meridian Console\Agent"; internal const string EnrollmentTokenValueName = "EnrollmentToken"; }` in `src/Agents/Dhadgar.Agent.Windows/Installation/AgentRegistry.cs`.
- The UAT driver keeps its own copies of these values; `Dhadgar.Agent.Windows.Uat.Tests` pins them to the agent's through `InternalsVisibleTo` (added by P18). Do not make them public.

**Amendment A2 (2026-10-27, F18):** when `StoreCertificateAsync` removes an older agent certificate from `My`, it first deletes that certificate's persisted private key: `using (var old = cert.GetECDsaPrivateKey()) { if (old is ECDsaCng cng) cng.Key.Delete(); }` (RSA likewise if the RSA branch stays), then `store.Remove(cert)`. A failure to delete the key is logged at Warning with the thumbprint and does not fail the store. Without this, every renewal leaves the private key of a revoked certificate in `%ProgramData%\Microsoft\Crypto\Keys`. Test, in `WindowsCertificateStoreProfileTests` (CurrentUser): after a renewal store, `CngKey.Exists(oldKeyName, oldProvider, CngKeyOpenOptions.UserKey)` is false; it fails when the delete is removed. The UAT checks the same on the machine store (U6).

**Amendment A3 (D-S235, [P12-ruling-1](worknotes/P12-ruling-1.md); it overrides step 1 where they differ). One validity predicate:**

1. In `src/Agents/Dhadgar.Agent.Core/Authentication/AgentCertificateIdentity.cs` add:

   ```csharp
   /// <summary>How far a certificate's NotBefore may sit ahead of this machine's clock (Nodes stamps it on its own clock).</summary>
   internal const int MaxClockSkewMinutes = 5;

   /// <summary>
   /// True when <paramref name="certificate"/> is usable at <paramref name="now"/>, allowing NotBefore up to
   /// <see cref="MaxClockSkewMinutes"/> ahead. The one validity rule for enrollment, renewal and the stores (D-S235).
   /// </summary>
   internal static bool IsWithinValidity(X509Certificate2 certificate, DateTimeOffset now) =>
       new DateTimeOffset(certificate.NotBefore) <= now.AddMinutes(MaxClockSkewMinutes)
       && now < new DateTimeOffset(certificate.NotAfter);
   ```

2. `EnrollmentService.cs`: delete the private `MaxClockSkewMinutes` (`:43-46`). The enrollment check (`:262-268`) becomes `if (!AgentCertificateIdentity.IsWithinValidity(cert, _timeProvider.GetUtcNow()))`, and the renewal check (`:508-515`) becomes `if (!AgentCertificateIdentity.IsWithinValidity(newCert, _timeProvider.GetUtcNow()))`. Keep both error codes, messages and the renewal log line unchanged.
3. `AgentCertificateMatcher.IsAgentClientCertificate` uses `AgentCertificateIdentity.IsWithinValidity(c, now)` in place of `NotBefore <= now < NotAfter`.
4. Extra cases in `AgentCertificateMatcherTests`: `NotBefore = now + 3 min` matches; `NotBefore = now + 6 min` does not. Revert check: with `NotBefore <= now` the +3 min certificate does not match, so a just-renewed certificate from a Nodes clock that runs ahead is hidden by the store.
5. `EnrollmentServicePkcs8Tests` and `EnrollmentServiceRenewalTests` must still pass unchanged; if an existing test pins the boundary, keep its expectation.

**No manual check is owed any more.** The checklist that used to sit here (MSI install, enroll, token gone, Online, restart, no CA in `Root`) is now the automated Windows UAT in wave 2U (P18-P22; U3, U4 and U10 cover it). P14 ends at Integrated. The MSI install itself is D-S223.

**Checks (in order)**

```powershell
dotnet build <wt>\src\Agents\Dhadgar.Agent.Core\Dhadgar.Agent.Core.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <wt>\src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Core.Tests\Dhadgar.Agent.Core.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Agent.Windows.Tests\Dhadgar.Agent.Windows.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj
dotnet test <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj
```

The last two lines are D-S234: A3 changes the agent's enrollment and renewal checks, which the direct and Gateway seam tests exercise.

**Docs owed:** update `src/Agents/Dhadgar.Agent.Windows/README.md` with the certificate store layout (`My` plus the `MeridianConsoleAgent` CA store), the token source and the state file. P20 adds the UAT pointer (moved from P18 by P18-ruling-1).

### P27f: Windows state-file protection (fix packet)

Specified in full in [worknotes/P11-ruling-1.md](worknotes/P11-ruling-1.md) § P27f (decision D-S233).

- **Branch** `s2/p27f-windows-state-acl`. **Worktree** `…\sprint2-agent-nodes-p27f`, from the w1 tip once P14 has merged.
- **Depends on** P11 (fix pass 1) and P14 (both edit `Program.cs`). It merges last in wave 2.
- **Security review** yes (agent project, file ACLs).
- **What it adds:**
  - `WindowsStateFileSecurity : IStateFileSecurity` in `Agent.Windows/Security/`: the `agent-state.json` temp file gets a protected DACL (SYSTEM, Administrators and the agent's account), and a load trusts the file only for a trusted owner with no untrusted write ACE.
  - `Program.cs` registers the class before `AddAgentCore`.
  - Five Windows-only tests.
  - The published-agent check is P20 amendment A1 (U3).

### P16: Nodes renewal grace window (D-S212)

- **Branch** `s2/p16-renewal-grace`. **Worktree** `…\sprint2-agent-nodes-p16`. **Depends on** P09 (merged into w1). **Merges** after P09 and before P10. **Security review** yes (certificates, revocation, auth).
- **Commit subject** `feat(nodes): short revocation grace for the renewed agent certificate (D-S212)`

**Rules:** R-CONTRACT (C3a exactly; the grace default comes from `AgentCertificateRenewalPolicy`, never a local literal), R-RESULT (the endpoint answers through `ProblemDetailsHelper` with the C4 codes), R-USING, R-NULL, R-XUNIT, R-LOG (thumbprints and node IDs only), R-CPM.

- `AgentCertificateState.IsUsable` is the **only** place that decides whether a revoked row still authenticates. Do not inline the predicate anywhere else. Its callers are exactly: `CertificateAuthorityService.ValidateCertificateAsync` (step 6), `AgentHub.IsCertificateCurrentAsync` (step 6b) and the renew endpoint (step 7). Step 8a checks that no other authentication read of `IsRevoked` is left.
- Every time comes from the injected `TimeProvider`, as `GetUtcNow().UtcDateTime` (the entity uses `DateTime` UTC).
- Grace is never extended, and every revoke path other than renewal clears it.

**Change**

1. Create `src/Shared/Dhadgar.Contracts/Nodes/AgentCertificateRenewalPolicy.cs` with exactly the C3a code.
2. `src/Dhadgar.Nodes/Data/Entities/AgentCertificate.cs`: add after `RevocationReason`:

   ```csharp
   /// <summary>
   /// Set only when this certificate was superseded by a renewal (D-S212). Until this UTC time the
   /// certificate still authenticates and may renew, although IsRevoked is true. Null means no grace.
   /// </summary>
   public DateTime? GraceUntil { get; set; }
   ```

3. Migration: from `<wt>` run `dotnet ef migrations add AddAgentCertificateGraceUntil --project src\Dhadgar.Nodes\Dhadgar.Nodes.csproj --startup-project src\Dhadgar.Nodes\Dhadgar.Nodes.csproj --output-dir Data\Migrations`. The tool is installed globally (EF Core tools 10.0.1). Keep the generated migration, designer and snapshot changes as generated. `Up` must add exactly one nullable `timestamp with time zone` column `GraceUntil` to `agent_certificates`, and `Down` must drop it; if the diff holds anything else, stop and report `blocked spec` with the extra operations. No index is needed.
4. New `src/Dhadgar.Nodes/Services/AgentCertificateState.cs`:

   ```csharp
   namespace Dhadgar.Nodes.Services;

   /// <summary>Whether an agent certificate row may still authenticate or renew (D-S212).</summary>
   public static class AgentCertificateState
   {
       public static bool IsUsable(AgentCertificate certificate, DateTime utcNow) =>
           !certificate.IsRevoked || (certificate.GraceUntil is { } graceUntil && utcNow < graceUntil);

       /// <summary>Hard revoke: no grace. Keeps the first RevokedAt and reason if already revoked.</summary>
       public static void RevokeWithoutGrace(AgentCertificate certificate, DateTime utcNow, string reason)
       {
           if (!certificate.IsRevoked)
           {
               certificate.IsRevoked = true;
               certificate.RevokedAt = utcNow;
               certificate.RevocationReason = reason;
           }
           certificate.GraceUntil = null;
       }
   }
   ```

5. `src/Dhadgar.Nodes/Auth/MtlsOptions.cs`: add `public int RenewalGraceMinutes { get; set; } = AgentCertificateRenewalPolicy.DefaultGraceMinutes;` with an XML doc pointing at D-S212 ("0 = immediate revoke"). In `AddMtlsAuthentication`'s startup validation (the guard P03 added for `ForwardedCertificate`), throw `InvalidOperationException` when it is outside `0..AgentCertificateRenewalPolicy.MaxGraceMinutes`. Add `"RenewalGraceMinutes": 60` under `Mtls` in `src/Dhadgar.Nodes/appsettings.json`.
6. `src/Dhadgar.Nodes/Services/CertificateAuthorityService.cs`, `ValidateCertificateAsync`: after P17f the revocation check sits after `FindAgentCertificateAsync` (about :359 on w1 `c1a1497`). Replace `agentCert is not null && agentCert.IsRevoked` with `agentCert is not null && !AgentCertificateState.IsUsable(agentCert, _timeProvider.GetUtcNow().UtcDateTime)`; it still returns `CertificateTrustStatus.Untrusted`. The warning log adds `GraceUntil`. If `tests/Dhadgar.Nodes.Tests/TestCertificateAuthorityService.cs` checks `IsRevoked` in its `ValidateCertificateAsync`, give it the same predicate; otherwise leave it.
6b. **(wave1-ruling-1)** `src/Dhadgar.Nodes/Hubs/AgentHub.cs`, `IsCertificateCurrentAsync` (:128-132 on w1 `c1a1497`). Today it rejects any row with `IsRevoked`, on `OnConnectedAsync` and on every `Heartbeat`, so a certificate in grace passes `MtlsMiddleware` and is then aborted by the hub. Replace the query and the return with:

   ```csharp
   var row = await dbContext.AgentCertificates
       .AsNoTracking()
       .FirstOrDefaultAsync(c => c.Thumbprint == thumbprint, ct);

   // No row: same as mTLS validation (Trusted). A row: the one grace-aware predicate (D-S212).
   return row is null || AgentCertificateState.IsUsable(row, timeProvider.GetUtcNow().UtcDateTime);
   ```

   Update the method's `<summary>` to "False when the connection's certificate is no longer usable (revoked and outside its renewal grace, D-S212), or has passed its NotAfter time and expired certificates are not allowed." and the class `<remarks>` "re-checks revocation" to "re-checks usability (revocation with grace)". `AgentHub` already injects `TimeProvider` and imports `Dhadgar.Nodes.Services`.
7. `src/Dhadgar.Nodes/Endpoints/AgentEndpoints.cs`, `RenewCertificate` (the blocks P09 separated). Inject `IOptions<MtlsOptions>`.
   - **Lookup:** load every row of the node that is not revoked **or** has a non-null `GraceUntil` (`c.NodeId == nodeId && (!c.IsRevoked || c.GraceUntil != null)`). `presentedCert` is the row whose thumbprint equals the claim (case-insensitive) **and** for which `AgentCertificateState.IsUsable(row, now)` holds. None found: keep P09's 401 `thumbprint_mismatch` (when rows exist) or 404 `certificate_not_found` (when none exist).
   - **Revoke step**, replacing P09's single-row revoke:

     ```csharp
     var grace = TimeSpan.FromMinutes(mtlsOptions.Value.RenewalGraceMinutes);
     var presentedWasInGrace = presentedCert.IsRevoked;
     if (!presentedWasInGrace)
     {
         presentedCert.IsRevoked = true;
         presentedCert.RevokedAt = now;
         presentedCert.RevocationReason = "Renewed - superseded by new certificate";
         presentedCert.GraceUntil = grace > TimeSpan.Zero ? now + grace : null;
     }
     // A certificate already in grace keeps its GraceUntil: grace is never extended (D-S212).

     foreach (var other in nodeRows.Where(r => r.Id != presentedCert.Id))
     {
         AgentCertificateState.RevokeWithoutGrace(other, now, "Superseded by a later renewal");
     }
     ```

   - Events stay as P09 left them (`AgentCertificateRenewed` and `AgentCertificateRevoked` for the presented thumbprint), published before `SaveChangesAsync`.
   - Log at Information: node ID, old thumbprint, new thumbprint, `presentedWasInGrace`, and the number of rows hard-revoked.
8. `src/Dhadgar.Nodes/Services/NodeService.cs`, decommission (:261-270): the query becomes `c.NodeId == nodeId && (!c.IsRevoked || c.GraceUntil != null)`, and the loop calls `AgentCertificateState.RevokeWithoutGrace(cert, now, "Node decommissioned")`. Then grep `src/Dhadgar.Nodes` for `IsRevoked = true` on `AgentCertificate`; any other revoke path must also clear `GraceUntil`. List them in the report.
8a. **(wave1-ruling-1) Predicate guard.** Grep `src/Dhadgar.Nodes` (`*.cs`, excluding `Data/Migrations/`) for `IsRevoked`. The only allowed `AgentCertificate` hits are: the property in `Data/Entities/AgentCertificate.cs`; `AgentCertificateState.cs`; the lookup filter and the revoke block in `AgentEndpoints.cs`; the decommission filter in `NodeService.cs`; `IsRevoked = false` initialisers in `EnrollmentService.cs` and `AgentEndpoints.cs`; the index in `AgentCertificateConfiguration.cs`. `EnrollmentToken` hits (`EnrollmentTokenService.cs`, `EnrollmentToken.cs`) are unrelated. A hit in `AgentHub.cs` or `CertificateAuthorityService.cs` means an inline predicate survived: fix it with `IsUsable`. Paste the grep output into the report.

**Tests**

- `tests/Dhadgar.Contracts.Tests/Nodes/AgentCertificateRenewalPolicyTests.cs`:
  - `AgentRetry_IsWellInsideDefaultGrace`: `AgentRetryMinutes * 4 <= DefaultGraceMinutes`;
  - `DefaultGrace_IsWithinMax`.
  These fail if someone raises the retry or cuts the grace below the agent's recovery window.
- New `tests/Dhadgar.Nodes.Tests/Services/AgentCertificateStateTests.cs`: active row is usable; revoked without grace is not; revoked with `GraceUntil` in the future is; at exactly `GraceUntil` it is not; `RevokeWithoutGrace` clears grace and keeps the first `RevokedAt`.
- `tests/Dhadgar.Nodes.Tests/Integration/CertificateRenewalIntegrationTests.cs` (P09's rewrite), add:
  - `Renew_SetsGraceOnPresentedCertificate`: the old row has `IsRevoked` and `GraceUntil == now + 60 min`.
  - `Renew_FromCertificateInGrace_Succeeds_AndHardRevokesOrphan`: renew once, then renew again presenting the **old** thumbprint. 200; the first new row is revoked with `GraceUntil` null; the old row's `GraceUntil` is unchanged.
  - `Renew_FromCertificatePastGrace_Returns401ThumbprintMismatch`: advance the factory's `FakeTimeProvider` 61 minutes, then present the old thumbprint.
  - `Renew_GraceZero_RevokesImmediately`: with `Mtls:RenewalGraceMinutes=0`, the old row has `GraceUntil` null.
- `tests/Dhadgar.Nodes.Tests/CertificateAuthorityServiceTests.cs`: `ValidateCertificate_RevokedInGrace_IsTrusted` and `ValidateCertificate_RevokedPastGrace_IsUntrusted` (asserting `CertificateTrustStatus.Trusted` / `Untrusted`), using a `FakeTimeProvider`.
- **(wave1-ruling-1)** `tests/Dhadgar.Nodes.Tests/Hubs/AgentHubCertificateExpiryTests.cs`, using that file's `CreateContext`, `CreateHubContext` (thumbprint claim `"abc"`) and `CreateHub` helpers. Seed one row and `SaveChangesAsync` before calling the hub:

  ```csharp
  db.AgentCertificates.Add(new AgentCertificate
  {
      Id = Guid.NewGuid(),
      NodeId = nodeId,
      Thumbprint = "abc",
      SerialNumber = "01",
      NotBefore = Now.UtcDateTime.AddDays(-1),
      NotAfter = Now.UtcDateTime.AddDays(89),
      IssuedAt = Now.UtcDateTime.AddDays(-1),
      IsRevoked = true,
      RevokedAt = Now.UtcDateTime.AddMinutes(-10),
      GraceUntil = graceUntil,
  });
  await db.SaveChangesAsync();
  ```

  - `Heartbeat_CertificateInGrace_IsAcknowledged`: `graceUntil = Now.UtcDateTime.AddMinutes(50)`. The ack is `Acknowledged`, `context.DidNotReceive().Abort()`, and `ProcessHeartbeatAsync` was received once.
  - `Heartbeat_CertificatePastGrace_IsRefusedAndAborted`: `graceUntil = Now.UtcDateTime.AddMinutes(-1)`. The ack is not acknowledged with `AgentHubErrorCodes.HeartbeatFailed`, `context.Received(1).Abort()`, and `ProcessHeartbeatAsync` was not received.
  - Revert check: put back the `.AnyAsync(c => c.Thumbprint == thumbprint && c.IsRevoked, ct)` query and `Heartbeat_CertificateInGrace_IsAcknowledged` fails. The existing `AgentHubTests.Heartbeat_AfterCertificateRevoked_IsRefusedAndConnectionClosed` and `Connect_WithRevokedCertificate_IsRefused` (revoked, no grace) must stay green.
- `tests/Dhadgar.Nodes.Tests/NodeServiceTests.cs`: `Decommission_EndsRenewalGrace`: a row in grace ends `IsRevoked` with `GraceUntil` null and `ValidateCertificateAsync` returns `CertificateTrustStatus.Untrusted`.
- In `AgentRenewalSeamTests`, replace P08's `ReadGraceUntil` helper with the real `GraceUntil` property and delete the helper.
- Startup test, next to P03's: `Mtls:RenewalGraceMinutes=-1` and `=1441` each fail host start.
- Revert check: without the predicate in `ValidateCertificateAsync` the old certificate gets 401 at once; without step 6b the hub aborts a certificate in grace (`Heartbeat_CertificateInGrace_IsAcknowledged` fails); without the in-grace lookup the lost-response retry gets `thumbprint_mismatch`; without the decommission change a decommissioned node keeps authenticating for up to an hour.
- S3's direct grace cases (`AgentRenewalSeamTests`) still need P10's agent changes; run them for information only and record the result.

**Checks (in order)**

```powershell
dotnet build <wt>\src\Shared\Dhadgar.Contracts\Dhadgar.Contracts.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <wt>\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet ef migrations has-pending-model-changes --project <wt>\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj
dotnet test <wt>\tests\Dhadgar.Contracts.Tests\Dhadgar.Contracts.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName!~AgentRenewalSeamTests"
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj --filter "FullyQualifiedName~AgentRenewalSeamTests"
```

The third command must report no pending changes. If it, or step 3's `migrations add`, fails because the design-time `DbContext` cannot be built (not because of the model), record the exact error, hand-write the migration on the pattern of `20260123085708_AddAuditLogging.cs` plus its designer and the snapshot property, and say so in the report. The last command is informational.

**Docs owed:** the renewal section of `src/Dhadgar.Nodes/README.md` gets the grace window: `Mtls:RenewalGraceMinutes`, the usable rule, renew-from-grace, no extension, and decommission ending it. Note the new column under the README's database or migrations notes if it has them. **(wave1-ruling-1)** The README's agent hub section says the hub applies the same usable rule on connect and on every heartbeat, so a certificate in grace keeps its hub connection until `GraceUntil`.

### P25: Gateway enforces the `Auth` and `PerTenant` limiters and counts unauthenticated requests (D-S220, D-S228)

- **Branch** `s2/p25-gateway-limiters`. **Worktree** `…\sprint2-agent-nodes-p25`, from the **current** w1 tip (`452b0ff` or later). **Depends on** the wave 1 gate only. Runs in parallel with every other wave 2 and 2U packet: no other packet touches `src/Dhadgar.Gateway/` or `tests/Dhadgar.Gateway.Tests/`. If a merge conflict appears anyway, stop with `blocked spec` and name the file.
- **Security review** yes (Gateway authentication pipeline order, rate limiting as a DoS control, F23).
- **Commit subject** `fix(gateway): enforce Auth and PerTenant limiters and count unauthenticated requests (D-S220)`

**Why (F23).** ASP.NET Core enforces only the **last** `EnableRateLimiting` metadata on an endpoint. `MapReverseProxy().ConfigureEndpoints` adds `Global` after YARP has added the route's policy, so on every route whose policy is not `PerAgent`, `Global` silently replaces it (`RouteRateLimiting.UsesGlobal`). `Global` is a single partition (`"global"`) for the whole Gateway: one client can use up 1000/min for everybody. And `UseRateLimiter` runs after `UseAuthorization` (`Program.cs:319-323`), so requests that fail authorization (no JWT on a tenant route, no or wrong-issuer certificate on an agent route) are never counted by any limiter.

**Rules (inline; this packet does not read CLAUDE.md):**

- R-NULL: nullable on; build with `-p:TreatWarningsAsErrors=true`. Do not suppress analyzers without a comment giving the reason.
- R-CPM: no `Version=` in any `.csproj`; no new packages are needed (if one is, stop with `blocked spec`).
- R-USING: `using` on every `HttpResponseMessage`, `HttpRequestMessage`, `HttpClient`, `X509Certificate2`, `ECDsa` and `WebApplicationFactory` in tests.
- R-XUNIT: xUnit v2 `[Fact]`/`[Theory]`, plain `Assert`. Gateway integration tests use `[Collection("Gateway Integration")]` and `GatewayWebApplicationFactory`. Names `Subject_Condition_Expectation`.
- R-LOG: never log a token, a certificate or a claim value other than IDs.
- Partition keys never come from a client-supplied header or an unvalidated certificate; only from the connection's remote IP and claims of the **validated** principal (`HttpContext.User` after `UseAuthentication`).
- Edit only the files named below.

**Contract (C4 rate-limit note, C5 429 bullet).** 429 + `Retry-After` + ProblemDetails JSON from the existing `OnRejected`, counted before authorization. No route, policy name or config key is added or renamed.

**Change**

1. `src/Dhadgar.Gateway/Services/RouteRateLimiting.cs`: a route keeps its own policy unless it has none.

   ```csharp
   public static class RouteRateLimiting
   {
       public const string GlobalPolicy = "Global";

       // ASP.NET Core enforces only the last EnableRateLimiting metadata on an endpoint, so attaching Global to a
       // route that names a policy would silently replace that policy. Global is only the fallback for routes that
       // name none, or name YARP's "Default" (D-S228).
       public static bool UsesGlobal(string? routePolicy) =>
           string.IsNullOrWhiteSpace(routePolicy)
           || string.Equals(routePolicy, "Default", StringComparison.OrdinalIgnoreCase);
   }
   ```

   Delete `PoliciesThatReplaceGlobal`. `Program.cs:388-395` (`ConfigureEndpoints`) stays as it is; it now attaches `Global` only to routes without a policy.
2. `src/Dhadgar.Gateway/Services/RateLimitPartitionKeys.cs`: add

   ```csharp
   /// <summary>
   /// PerTenant key: the validated principal's org_id when it has one, else the client IP (IPv6 /64).
   /// The prefixes keep an org ID from ever colliding with an IP bucket.
   /// </summary>
   public static string Tenant(ClaimsPrincipal? user, IPAddress? ip)
   {
       var orgId = user?.Identity?.IsAuthenticated == true ? user.FindFirst("org_id")?.Value : null;
       return string.IsNullOrWhiteSpace(orgId) ? $"tenant-ip:{ClientIp(ip)}" : $"tenant-org:{orgId}";
   }
   ```

   Update the class summary ("Partition keys shared by the Auth, PerTenant and PerAgent rate-limit policies").
3. `src/Dhadgar.Gateway/Program.cs`, `PerTenant` policy (lines 224-240): replace the body with a token bucket keyed by `RateLimitPartitionKeys.Tenant(httpContext.User, httpContext.Connection.RemoteIpAddress)`. The values come from the keys `appsettings.json` already has (`PermitLimit` 100, `ReplenishPerSecond` 50, `QueueLimit` 0):

   ```csharp
   options.AddPolicy("PerTenant", httpContext =>
   {
       // Keyed by the validated JWT's org_id (UseAuthentication has run), else by client IP. Never a header.
       var partitionKey = RateLimitPartitionKeys.Tenant(httpContext.User, httpContext.Connection.RemoteIpAddress);

       return RateLimitPartition.GetTokenBucketLimiter(
           partitionKey: partitionKey,
           factory: _ => new TokenBucketRateLimiterOptions
           {
               TokenLimit = builder.Configuration.GetValue<int?>("RateLimiting:Policies:PerTenant:PermitLimit") ?? 100,
               TokensPerPeriod = builder.Configuration.GetValue<int?>("RateLimiting:Policies:PerTenant:ReplenishPerSecond") ?? 50,
               ReplenishmentPeriod = TimeSpan.FromSeconds(1),
               QueueLimit = builder.Configuration.GetValue<int?>("RateLimiting:Policies:PerTenant:QueueLimit") ?? 0,
               AutoReplenishment = true
           });
   });
   ```

   `Auth`, `PerAgent` and `Global` keep their fixed windows and their keys unchanged.
4. `src/Dhadgar.Gateway/Program.cs`, pipeline (lines 318-323): move `app.UseRateLimiter()` between `UseAuthentication` and `UseAuthorization`, and fix the comments:

   ```csharp
   // 6. Authentication, then rate limiting, then authorization.
   //    Rate limiting runs after authentication so PerTenant can key on the validated org_id, and before
   //    authorization so requests that end in 401/403 (no JWT, no or wrong-issuer agent certificate) are counted
   //    too (D-S220, D-S228).
   app.UseAuthentication();
   app.UseRateLimiter();
   app.UseAuthorization();
   ```

   Renumber the following comments if they carry numbers. Change nothing else in the pipeline.
5. `src/Dhadgar.Gateway/appsettings.json`: no change. (If the coder finds a route without `RateLimiterPolicy` other than `identity-internal-block`, report it; do not add one.)

**Tests** (`tests/Dhadgar.Gateway.Tests`; each fails when its change is reverted)

- `RateLimitingBehaviorTests`: replace `RouteRateLimiting_UsesGlobal_OnlyPerAgentReplacesGlobal` with `RouteRateLimiting_UsesGlobal_OnlyForRoutesWithoutPolicy`: `null`, `""`, `"  "`, `"Default"`, `"default"` → true; `"Auth"`, `"PerTenant"`, `"PerAgent"` → false. Reverting step 1 fails the `Auth` and `PerTenant` rows.
- `AgentCertificateForwardingTests`:
  - **Invert** `NonAgentRoute_StillLimitedByGlobal_NotByRoutePolicy` into `NonAgentRoute_LimitedByRoutePolicy_NotByGlobal`: settings `Auth:PermitLimit=2`, `Auth:WindowSeconds=60`, `Global:PermitLimit=100`; three GETs to `/api/v1/identity/.well-known/openid-configuration` from `203.0.113.20` → `[OK, OK, 429]`, two requests captured. Add a second case in the same test or a sibling, `NonAgentRoute_GlobalExhausted_RoutePolicyStillAllows`: `Global:PermitLimit=1`, `Auth:PermitLimit=100`, three GETs → all `OK`. That one fails on revert of step 1.
  - `AgentRoute_WithoutCertificate_FloodIsRateLimited`: `PerAgent:PermitLimit=2`, `PerAgent:WindowSeconds=60`; three `POST /hubs/agent/negotiate` from `203.0.113.40` with **no** certificate → `[401, 401, 429]`, and `capture.Requests` is empty. Assert the 429 has a `Retry-After` header. Fails when step 4 is reverted (it gets `[401, 401, 401]`).
  - `AgentRoute_WrongIssuerCertificate_FloodIsRateLimited`: the same with a certificate from `CreateCertificate("Some Other CA")` → `[401, 401, 429]`.
- New `PerTenantRateLimitingTests` (`[Collection("Gateway Integration")]`, `GatewayWebApplicationFactory`, copy `CreateApp`/`SendAsync`/`CaptureHandler`/`CaptureForwarderHttpClientFactory` from `AgentCertificateForwardingTests` as private members, or move them to a shared internal `GatewayTestHost` helper in the test project; your choice, but do not change the other tests' behaviour). Route: `servers-route` (`/api/v1/servers/{**catch-all}`, cluster `servers`); set `ReverseProxy:Clusters:servers:Destinations:d1:Address=http://servers.test/` and `ReverseProxy:Clusters:servers:HealthCheck:Active:Enabled=false` (check the real destination name in `appsettings.json` and use it). Settings `PerTenant:PermitLimit=2`, `PerTenant:ReplenishPerSecond=1`.
  - Authentication in these tests: a private `TestOrgAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>` with scheme `"Test"`. It succeeds when the request has header `X-Test-Org-Id`, with claims `sub` = `"test-user"` and `org_id` = the header value; otherwise `AuthenticateResult.NoResult()`. Register it in `ConfigureTestServices`: `services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestOrgAuthHandler>("Test", _ => { });` and `services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = "Test"; o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; });`. The handler exists only in the test project.
  - `PerTenantRoute_Unauthenticated_FloodIsRateLimited`: no header, three GETs `/api/v1/servers/x` from `203.0.113.50` → `[401, 401, 429]`, nothing captured. Fails when step 4 is reverted.
  - `PerTenant_SameOrgFromTwoIps_SharesBucket`: org `A` from `203.0.113.51`, then `203.0.113.52`, then `203.0.113.53` (header set each time) → `[OK, OK, 429]`. Fails if the key falls back to IP for an authenticated user.
  - `PerTenant_TwoOrgsFromOneIp_SeparateBuckets`: from `203.0.113.54`, org `A` twice, then org `B` once → `[OK, OK, OK]`; a third org `A` request → 429. Fails if the key ignores `org_id`.
  - The bucket refills at 1 token per second: send each sequence without delay, and keep each test under one second of requests (no `Task.Delay`).
- `RateLimitPartitionKeysTests` (new or existing file): `Tenant` with an authenticated principal carrying `org_id` → `tenant-org:<id>`; with an unauthenticated principal carrying `org_id` → `tenant-ip:…` (claims of an unauthenticated identity are not trusted); with `null` user and IPv6 `2001:db8:1:2:3:4:5:6` → `tenant-ip:2001:db8:1:2::`; with whitespace `org_id` → IP key.
- `RouteRateLimitingGuardTests`: load `src/Dhadgar.Gateway/appsettings.json` through the test host's `IConfiguration` (`_factory.Services.GetRequiredService<IConfiguration>()`), enumerate `ReverseProxy:Routes`, and assert every route whose `AuthorizationPolicy` is not `DenyAll` has a `RateLimiterPolicy` in `{ "Auth", "PerTenant", "PerAgent" }`. Fails when someone adds a route that would silently fall back to the single `Global` bucket.
- **Shared-factory regressions.** `Auth` is now enforced at 30/min on the `"unknown"` bucket, which every test without a remote IP shares inside the one `GatewayWebApplicationFactory`. If existing tests in the collection start getting 429, raise the limits **in `GatewayWebApplicationFactory` only** (add `RateLimiting:Policies:Auth:PermitLimit=10000`, `PerTenant:PermitLimit=10000`, `PerTenant:ReplenishPerSecond=10000`, `PerAgent:PermitLimit=10000` to its in-memory settings), and confirm the new tests above still pass, i.e. their own settings (added later through `WithWebHostBuilder`) win. Do not weaken any test's assertions.

**Checks (in order)**

```powershell
dotnet build <wt>\src\Dhadgar.Gateway\Dhadgar.Gateway.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Gateway.Tests\Dhadgar.Gateway.Tests.csproj
dotnet test <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj
```

The last command is the S2 (and, once merged, S3/S4 Gateway-path) regression: each harness builds its own Gateway, so its limiter buckets are fresh, and one or two enrollments per test is far below `Auth`'s 30/min. If it fails with 429, report the test and stop with `blocked spec`; do not raise limits in `GatewayNodesHarness`. No Docker.

**Docs owed**

- `src/Dhadgar.Gateway/README.md`: the middleware-order list (around lines 371-377: items 8-10 become authentication, rate limiting, authorization, with "before authorization, so rejected requests are counted" replacing "after auth for tenant context"), the rate-limiting section (around line 541) and the config example (around lines 236-253): each route is limited by its own policy, `Global` only for routes without one, the pipeline order (authentication → rate limiting → authorization) and why, the PerTenant token bucket (`PermitLimit` = burst, `ReplenishPerSecond`), and the partition keys.
- `docs/gateway-routing-reference.md` (around line 164-180): the same facts; the PerTenant example keys must match `appsettings.json`.
- `docs/gateway-authentication.md` (around line 103): replace the "600/min" figure with the real PerTenant numbers.
- `src/Dhadgar.Nodes/README.md` "Rate Limiting" (around lines 533-540) also states a wrong figure; **do not edit it here** (P09 and P16 edit that README in the same wave). P15 step 4b fixes it.

### Shared-file note for wave 2

P25 is the only wave 2 packet in `src/Dhadgar.Gateway/` and `tests/Dhadgar.Gateway.Tests/`. P08 and P13 add Gateway-path tests in `tests/Dhadgar.AgentSeam.Tests/`, which P25 only runs.

---

## Wave 2U: Windows UAT (D-S224, D-S225, D-S239)

The UAT is the Windows seam test: the published Windows agent against a real Gateway and Nodes, on the real certificate stores and registry. It is not in CI (F9). The UAT runner agent runs it, in a Claude session the owner launched elevated (D-S239); its `results.json` is the record behind "confirmed working on Windows" (D-S224).

### Wave rules

- Serial: P18 → P19 → P20 → P28f → P21 → P22 → P23 (D-S222 (A) and D-S223 (B), owner 2026-10-27; P28f from D-S239). Each packet branches from the w1 tip after the one before it merged. **P26** (installer endpoint property) is the exception: it edits only `src/Agents/Dhadgar.Agent.Windows/` and `tests/Dhadgar.Agent.Windows.Tests/`, needs only P14, and runs in parallel with P18-P22; it must merge before P23 starts. All are security-review.
- Run modes: `Process` (P20-P21), `Service` (P22), `Msi` (P23). The UAT runner runs all three after the wave 2U review, with the brief in [D-S239-ruling](worknotes/D-S239-ruling.md) § (2).
- Windows 11, PowerShell 7.4 or later, .NET 10 SDK. From P19 on, Docker Desktop must be running; a coder without it returns `blocked docker`.
- **Only the UAT runner makes a machine-changing run (D-S239).** Coders, reviewers, the security-advisor, ops and the documentation-writer may run only `-Preflight` (P18 on) and `-StackOnly` (P19 on). Never pass `-AcceptMachineChanges` or `-CleanupOnly`; never write HKLM; never open a `LocalMachine` store for writing; never create a service or an event source; never unset `CLAUDECODE`. The driver cannot tell agents apart, so this rule is yours to keep even in an elevated session. From P28f on, the driver lets an agent `run` only when the shell is elevated, the tree is clean and `-NoBuild` is absent. Anything only a real run can prove goes into the report under **Owed to the UAT run** (older packet text that says "Owed to the user run" means the same).
- **No bare RID build in checks (D-S241, from P32).** A `dotnet publish -r win-x64` (or `build -r`) of the agent that restores for itself rewrites the lock files of `Agent.Core`, `Contracts` and `Shared`, and the dirty tree then makes the driver refuse an agent run (D-S239). Packet checks build the agent and installer only through `scripts\uat\Build-AgentWindowsUat.ps1` or the wrapper's `-Preflight`/`-StackOnly`. This replaces the bare publish and installer lines in older check lists, such as P23's. If `git status` shows a lock file dirty after a check, restore that file with `git checkout -- <lock file>` and report it.
- Every machine action sits behind an interface, so `tests/Dhadgar.Agent.Windows.Uat.Tests` tests the logic with fakes. That project runs in Linux CI (`dotnet test Dhadgar.sln`): Windows-only tests use `[SkippableFact]` with `Skip.IfNot(OperatingSystem.IsWindows())` (the package is already in `Directory.Packages.props`).

### Rules for every wave 2U packet

| Key | Rule |
| --- | --- |
| R-RESULT | Every step and every machine action returns `Result` / `Result<T>` (`Dhadgar.Shared.Results`). Exceptions are caught only at the step boundary and turned into a `fail` with the exception type and message. |
| R-NONULL | **Never `Result<T>.Success(null)`**: the shared type throws on it (`Result.cs:150-155`). A "nothing there" outcome is a typed value (for example `ProcessImage.NotRunning`). (D-S237, P18-ruling-1) |
| R-CLEANUP | Cleanup kills a `process` only when its image is under the run folder or is one of `RunPaths.StackImages`, and never the driver's own PID. It deletes a `cngKey` only when it is named `meridian-uat-*` under `Microsoft Software Key Storage Provider` or `Microsoft Platform Crypto Provider`. The agent's own key (Windows-assigned name) is never a `cngKey` entry: `RemoveCertificate` deletes it through the agent client certificate in `My`, key first, then the certificate (D-S238). `HKLM\SOFTWARE\Meridian Console` is allowed by exact path only and is deleted only when empty; every other registry key must be `...\Agent` or below it. A packet that needs a child outside the run folder, or another key name or provider, adds it to the rule in `CleanupEngine`/`RunPaths` together with a refusal test and an allowed test, or stops with `blocked spec`. Every entry a packet appends comes from an `internal static` producer list that a test pushes through the real `CleanupEngine`. The driver's threat model is D-S237 (3); do not add PATH pinning or owner-SID checks. |
| R-USING | `using` on every `Process`, `X509Store`, `X509Certificate2`, `ECDsa`, `RegistryKey`, `HttpClient`, `HttpResponseMessage`, `NpgsqlConnection` and `DbContext`. |
| R-ZERO | PFX bytes, PFX passwords and generated passwords as `char[]`/`byte[]` are zeroed in `finally` (`CryptographicOperations.ZeroMemory`, `Array.Clear`). Enrollment tokens are managed strings and cannot be zeroed: keep them in locals only, never in a field that outlives the step that uses them, except the `Redactor`'s secret list. |
| R-LOG | **No enrollment token, password or private key is ever written** to the console, a log, `results.*` or the manifest. Thumbprints, node IDs and run IDs are fine. Child process output is redacted on capture (see `Redactor`). |
| R-ARGS | Every child process uses `ProcessStartInfo.ArgumentList`, never `Arguments`, and `UseShellExecute = false`. Secrets for a child go in `ProcessStartInfo.Environment`, never in arguments (`docker run -e NAME` without a value reads it from the child's environment). **One exception (D-S229, P23 only):** `msiexec` has no way to read a property from the environment, so the MSI leg passes `ENROLLMENT_TOKEN=<T>` as a single `ArgumentList` item. The token is single-use with a 10-minute lifetime, is in the `Redactor` before the call, and `CommandRunner` logs only the redacted argument list. |
| R-LOCALURL | The driver sends HTTP only to `LocalUrl` values: `https://localhost:<port>` or `http://127.0.0.1:<port>` for a port the driver itself allocated in this run. `LocalUrl.Create` refuses anything else. This is the trusted-host check for the driver. |
| R-RUNDIR | Every path the driver writes or deletes is validated by `RunPaths.Validate(path)`: full path, no `..` segment, no null or control characters, and under `<repo>\.uat\agent-windows\<runId>\`. The only exceptions are the machine paths named in the manifest kinds (registry, stores, service, event source, `%ProgramData%\Meridian Console`, and from P23 `%ProgramFiles%\Meridian Console` and the installed MSI product), which the cleanup engine handles by kind, never by a free path. |
| R-CONTRACT | Wire types come from `Dhadgar.Contracts.Nodes` (`EnrollNodeRequest`, `EnrollNodeResponse`, `RenewCertificateRequest`, `RenewCertificateResponse`, `AgentHubProtocol`, `AgentCertificateProfile`). No local copies. JSON uses `JsonSerializerDefaults.Web`. |
| R-CPM | No `Version=` in a `.csproj`. No new packages are needed; if one is, stop with `blocked spec`. |
| R-NULL | Nullable on; build with `-p:TreatWarningsAsErrors=true`. |
| R-XUNIT | xUnit v2, `[Fact]`/`[Theory]`/`[SkippableFact]`, plain `Assert`, NSubstitute for fakes, names `Subject_Condition_Expectation`. |

### UAT contract (every wave 2U packet builds against this)

**Script.** `scripts/uat/Invoke-AgentWindowsUat.ps1`:

```powershell
#Requires -Version 7.4
[CmdletBinding(DefaultParameterSetName = 'Preflight')]
param(
    [Parameter(ParameterSetName = 'Run', Mandatory)][ValidateSet('Process', 'Service', 'Msi')][string] $Mode,   # 'Service' from P22, 'Msi' from P23
    [Parameter(ParameterSetName = 'Run', Mandatory)][switch] $AcceptMachineChanges,
    [Parameter(ParameterSetName = 'Preflight')][switch] $Preflight,
    [Parameter(ParameterSetName = 'StackOnly', Mandatory)][switch] $StackOnly,
    [Parameter(ParameterSetName = 'Cleanup', Mandatory)][string] $CleanupOnly,   # a run folder
    [switch] $NoBuild
)
```

It resolves `$repo` from `$PSScriptRoot\..\..`, creates `runId` as UTC `yyyyMMdd-HHmmss-xxxx` (`xxxx` = 4 lowercase hex characters from `[System.Security.Cryptography.RandomNumberGenerator]`), creates `$repo\.uat\agent-windows\<runId>\`, builds (unless `-NoBuild`), and then calls the driver exe with `&` and an argument array, never a built string. It exits with the driver's exit code.

| Parameter set | Builds | Driver verb |
| --- | --- | --- |
| `Preflight` (default) | driver | `preflight` |
| `StackOnly` | driver, Nodes, Gateway | `stack-only` |
| `Run` | driver, Nodes, Gateway, then `dotnet publish src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -r win-x64 -o <runDir>\agent`. **`-Mode Msi` (P23)** instead publishes **without** `-o` (the wixproj binds `src\Agents\Dhadgar.Agent.Windows\bin\Release\net10.0\win-x64\publish`), builds `src\Agents\Dhadgar.Agent.Windows\Installer\Dhadgar.Agent.Windows.Installer.wixproj -c Release`, and copies `Installer\bin\Release\MeridianConsoleAgent.msi` to `<runDir>\msi\MeridianConsoleAgent.msi` | `run --mode process\|service\|msi --accept-machine-changes` |
| `Cleanup` | nothing | `cleanup --run-dir <path>` |

Builds are `dotnet build <csproj> -c Release`. Driver exe: `tests\Dhadgar.Agent.Windows.Uat\bin\Release\net10.0\Dhadgar.Agent.Windows.Uat.exe`. Common driver arguments: `--repo-root <repo> --run-id <runId>`.

**Exit codes.**

| Code | Meaning |
| --- | --- |
| 0 | Every step `pass` and cleanup `clean` |
| 1 | A step `fail` or `inconclusive` (cleanup `clean`) |
| 2 | Refused before any machine change: not elevated, switch missing, footprint found, port 5672 busy, Docker down; from P28f (D-S239) also an agent run on a dirty tree or with `-NoBuild`, a run folder already used (`launch.json` or manifest present), agent cleanup of a non-agent run folder. Before P28f: `CLAUDECODE` set for `run` or `cleanup` |
| 3 | Footprint left after cleanup (overrides 1) |
| 4 | Internal driver error (cleanup still ran) |

**`results.json` (schemaVersion 1).** Written to `<runDir>\results.json`, with `results.md` beside it (the same data as a header and one table: `| Step | Name | Status | Evidence | Duration |`).

```json
{
  "schemaVersion": 1,
  "runId": "20261027-142233-9f3a",
  "verb": "run",
  "mode": "process",
  "startedUtc": "2026-10-27T14:22:33Z",
  "finishedUtc": "2026-10-27T14:34:02Z",
  "gitSha": "<git rev-parse HEAD>",
  "gitDirty": false,
  "windowsBuild": 26300,
  "dotnetVersion": "10.0.x",
  "launchedBy": "user",
  "elevated": true,
  "overall": "pass",
  "exitCode": 0,
  "steps": [
    { "id": "U3", "name": "First start", "status": "pass", "evidence": { "nodeId": "…", "thumbprintSha256": "…" }, "message": null, "durationMs": 21345 }
  ],
  "cleanup": { "status": "clean", "undone": 17, "failed": [], "remaining": [], "rootAdded": [], "rootRemoved": [] },
  "warnings": []
}
```

- `status` per step: `pass`, `fail`, `inconclusive`, `skipped`. `overall` is `pass` only when every non-skipped step is `pass` and `cleanup.status` is `clean`; `fail` when any step failed or cleanup left a footprint; else `inconclusive`.
- `cleanup.status`: `clean`, `footprint-left`, `not-run`.
- `launchedBy` is `"agent"` when `CLAUDECODE` is set, else `"user"`. From P28f (D-S239) any verb may have `"agent"`, and agent results add `"agentRun": { "authority": "D-S239", "detectedBy": "CLAUDECODE", "treeClean": <bool> }` (omitted for user runs; schemaVersion stays 1).
- **`launch.json` (P28f, D-S239).** `run` and `stack-only` write `<runDir>\launch.json` (`schemaVersion`, `runId`, `verb`, `mode`, `launchedBy`, `elevated`, `gitSha`, `startedUtc`) after every refusal and before the first manifest entry, never overwriting. Agent `cleanup` replays a manifest only when this file loads, names the same `runId` and says `launchedBy = agent`.
- `evidence` values are strings, numbers or booleans: IDs, thumbprints, counts, timestamps, HTTP status codes. Never a token, password, PEM or key.
- `gitSha` and `gitDirty` come from `git rev-parse HEAD` and `git status --porcelain -- src tests scripts` (non-empty means dirty), both through `ArgumentList`.

**Cleanup manifest.** `<runDir>\cleanup-manifest.jsonl`, one JSON object per line, appended and flushed (`FileStream.Flush(true)`) **before** the change it describes. Undo walks it in reverse; every undo is idempotent (already gone counts as success).

| Kind | Fields | Undo |
| --- | --- | --- |
| `container` | `name` | `docker rm -f <name>` |
| `process` | `pid`, `startTimeUtc`, `name` | kill the tree if a process with that PID and start time still runs, it is not the driver, and its image is under the run folder or in `RunPaths.StackImages` (R-CLEANUP); an unreadable image is refused and reported, never killed |
| `registryKey` | `hive` (`HKLM`), `path`, `existedBefore` | delete the key tree when `existedBefore` is false; `SOFTWARE\Meridian Console` only when empty (D-S238) |
| `registryValue` | `path`, `name`, `existedBefore` | delete the value when `existedBefore` is false |
| `certificate` | `storeLocation`, `storeName`, `thumbprintSha1` | remove from that store; for an agent client certificate in `My`, delete its machine CNG key first and keep the certificate if that fails (D-S238) |
| `cngKey` | `keyName`, `provider`, `machineKey` | `CngKey.Open(...)` then `Delete()`; driver-named `meridian-uat-*` keys only |
| `certStore` | `storeName` (`MeridianConsoleAgent`), `existedBefore` | `CertUnregisterSystemStore(name, CERT_SYSTEM_STORE_LOCAL_MACHINE \| CERT_STORE_DELETE_FLAG)` when `existedBefore` is false |
| `directory` | `path`, `existedBefore` | delete recursively when `existedBefore` is false (run-dir paths through `RunPaths`; `%ProgramData%\Meridian Console` by kind) |
| `file` | `path` | delete (through `RunPaths`) |
| `eventSource` | `source`, `existedBefore` | `EventLog.DeleteEventSource(source)` when `existedBefore` is false |
| `service` | `name` (`DhadgarAgent`) | `sc.exe stop`, wait for `STOPPED` up to 30 s, `sc.exe delete` |
| `msiProduct` (P23) | `upgradeCode`, `msiPath` | for each product code `MsiEnumRelatedProductsW(upgradeCode)` returns: `msiexec /x <productCode> /qn /norestart /l*v <runDir>\logs\msi-cleanup.log`; exit 0, 3010 or 1605 (not installed) is success |

**Undo order (P22).** `CleanupEngine` first runs a **quiesce pass**: every `service` entry is stopped (not deleted) and every `process` entry is killed, newest first. Then it undoes all entries in reverse as above. A running service or agent process would otherwise hold its key, store or folder while those are deleted.

**Footprint (the probe, D-S225).** Present means any of: service `DhadgarAgent`; key `HKLM\SOFTWARE\Meridian Console\Agent`; system store `MeridianConsoleAgent` (opened with `OpenFlags.OpenExistingOnly`); a certificate in `LocalMachine\My` matching `AgentCertificateMatcher.IsAgentClientCertificate` (validity ignored: pass `cert.NotBefore`) or `IsAgentCa`; an `IsAgentCa` certificate in `LocalMachine\Root`; directory `%ProgramData%\Meridian Console\Agent`; a container whose name starts with `meridian-uat-`; from P23, an installed product with the agent's UpgradeCode (`MsiEnumRelatedProductsW`) and directory `%ProgramFiles%\Meridian Console\Agent`. The event source `Meridian Console Agent` existing is a **warning**, not a footprint (a developer machine may have it); the manifest keeps it.

**`Root` snapshot.** Before the first change, the driver stores the SHA-1 thumbprints of `LocalMachine\Root` in `<runDir>\root-before.txt`. After cleanup: a thumbprint missing from the snapshot (removed during the run) is `rootRemoved` and makes cleanup `footprint-left`; an added thumbprint whose certificate is `IsAgentCa` or has subject `O=Meridian Console UAT*` is `rootAdded` and makes it `footprint-left`; any other addition (Windows updates roots itself) is a warning.

**Stack settings (P19 sets these; later packets rely on them).** Ports are free ports from `TcpListener(IPAddress.Loopback, 0)`, except RabbitMQ, which must be 5672 because `RabbitMqOptions` has no port (`Program.cs:128`). Passwords are 24 random bytes as hex.

| Process | Setting | Value |
| --- | --- | --- |
| Postgres container `meridian-uat-pg-<runId>` | image, port, env | `postgres:16`, `-p 127.0.0.1:<pgPort>:5432`, `POSTGRES_PASSWORD` (from env), `POSTGRES_DB=dhadgar_nodes_uat`, `--label meridian-uat=<runId>` |
| RabbitMQ container `meridian-uat-mq-<runId>` | image, port, env | `rabbitmq:3-management`, `-p 127.0.0.1:5672:5672`, `RABBITMQ_DEFAULT_USER=uat`, `RABBITMQ_DEFAULT_PASS` (from env), same label |
| Nodes `src\Dhadgar.Nodes\bin\Release\net10.0\Dhadgar.Nodes.exe` | working directory | its bin folder |
| | `ASPNETCORE_ENVIRONMENT` | `Development` (auto-migrates) |
| | `ASPNETCORE_URLS` | `http://127.0.0.1:<nodesPort>` |
| | `ConnectionStrings__Postgres` | `Host=127.0.0.1;Port=<pgPort>;Database=dhadgar_nodes_uat;Username=postgres;Password=<pgPassword>` |
| | `RabbitMq__Host` / `__Username` / `__Password` / `__VirtualHost` | `127.0.0.1` / `uat` / `<mqPassword>` / `/` |
| | `Mtls__ForwardedCertificate__Enabled` | `true` |
| | `Mtls__ForwardedCertificate__TrustedProxies__0` / `__1` | `127.0.0.1/32` / `::1/128` |
| | `Mtls__RenewalGraceMinutes` | `5` |
| | `Nodes__CaStoragePath` / `Nodes__CaKeyPassword` | `<runDir>\ca` / `<caPassword>` |
| Gateway `src\Dhadgar.Gateway\bin\Release\net10.0\Dhadgar.Gateway.exe` | working directory | its bin folder |
| | `ASPNETCORE_ENVIRONMENT` | `Development` |
| | `ASPNETCORE_URLS` | `https://localhost:<gatewayPort>` (process mode and `-StackOnly`: the user's trusted dev certificate) |
| | `Kestrel__Certificates__Default__Path` / `__Password` | **service and MSI modes only (P22):** `<runDir>\tls\gateway.pfx` / the run's TLS PFX password. Never set in process mode or `-StackOnly`. |
| | `AgentIngress__ClientCertificateMode` | `AllowCertificate` |
| | `ReverseProxy__Clusters__nodes__Destinations__d1__Address` | `http://127.0.0.1:<nodesPort>/` (`d1` is the existing destination name; a new name would load-balance onto port 5040) |
| | `Redis__ConnectionString` | not set |

Every child environment removes `Agent__EnrollmentToken`, `DHADGAR_Agent__EnrollmentToken` and every variable starting `Agent__`, before adding its own.

**Agent settings (P20).** The driver edits `<runDir>\agent\appsettings.json` (the published copy) with `System.Text.Json.Nodes`, keeping every other key:

| Key | Value |
| --- | --- |
| `Agent:ControlPlane:Endpoint` | `https://localhost:<gatewayPort>` |
| `Agent:ControlPlane:HeartbeatIntervalSeconds` | `10` (the `[Range(10, 300)]` minimum) |
| `Agent:ControlPlane:ConnectionCheckIntervalSeconds` | `10`, or the minimum `ControlPlaneOptions.Validate` allows if that is higher |
| `Agent:StateDirectory` | `<runDir>\agent-state` |
| `Agent:Process:ServerBasePath` | `<runDir>\agent-data\Servers` |
| `Agent:Files:TempDirectory` | `<runDir>\agent-data\Temp` |
| `Agent:Security:CertificateRenewalThresholdDays` | absent, except during U6 (`90`, D-S221) |

**Constants the driver copies** (in `AgentWindowsFacts`, pinned by a test against the agent through `InternalsVisibleTo`): service name `DhadgarAgent` (`Program.ServiceName`), event source `Meridian Console Agent` (`Program.EventLogSourceName`), CA store `MeridianConsoleAgent` (`WindowsCertificateStore.CaStoreName`), registry key `SOFTWARE\Meridian Console\Agent` and value `EnrollmentToken` (`AgentRegistry`); from P23 also the value `ControlPlaneEndpoint` (`AgentRegistry.ControlPlaneEndpointValueName`, P26), the MSI UpgradeCode `8A3B4E6F-1D2C-4A5B-9E0F-7C8D6B5A4E3F` and the MSI property names `ENROLLMENT_TOKEN` and `CONTROL_PLANE_ENDPOINT` (pinned against `Installer/Package.wxs` by P23's `MsiFactsTests`). The driver does **not** reference `Dhadgar.Agent.Windows` (it is a self-contained exe in Release, and only its constants are needed).

### P18: UAT driver skeleton, guards and cleanup engine

- **Branch** `s2/p18-windows-uat-driver`. **Worktree** `…\sprint2-agent-nodes-p18`. **Depends on** P14 (and so all of wave 2) merged into w1. **Security review** yes (a tool that changes HKLM, machine certificate stores and services; its guards are the safety case).
- **Commit subject** `test(uat): Windows UAT driver skeleton with preflight, cleanup manifest and machine probe`

**Rules:** all wave 2U rules above. Do not edit any file under `src/` except the one `InternalsVisibleTo` line in step 9.

**Change**

1. `.gitignore`: add `.uat/` (one line, in the section for local tool output if there is one).
2. `scripts/uat/Invoke-AgentWindowsUat.ps1`, exactly per the contract. Use `Set-StrictMode -Version Latest` and `$ErrorActionPreference = 'Stop'`. The `Run` set refuses with exit 2 before building when `-AcceptMachineChanges` is missing (PowerShell makes it mandatory; keep a defensive check). Print the run folder path first.
3. New project `tests/Dhadgar.Agent.Windows.Uat/Dhadgar.Agent.Windows.Uat.csproj`, added to `Dhadgar.sln` (`dotnet sln add`):

   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <OutputType>Exe</OutputType>
       <TargetFramework>net10.0</TargetFramework>
       <Nullable>enable</Nullable>
       <IsPackable>false</IsPackable>
       <IsTestProject>false</IsTestProject>
       <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
       <RootNamespace>Dhadgar.Agent.Windows.Uat</RootNamespace>
       <!-- CA1416: Windows-only tool; every Windows API use is intentional. CA1303: console text is not localized. -->
       <NoWarn>$(NoWarn);CA1416;CA1303</NoWarn>
     </PropertyGroup>
     <ItemGroup>
       <ProjectReference Include="..\..\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj" />
       <ProjectReference Include="..\..\src\Agents\Dhadgar.Agent.Core\Dhadgar.Agent.Core.csproj" />
       <ProjectReference Include="..\..\src\Shared\Dhadgar.Contracts\Dhadgar.Contracts.csproj" />
       <ProjectReference Include="..\..\src\Shared\Dhadgar.Shared\Dhadgar.Shared.csproj" />
     </ItemGroup>
     <ItemGroup>
       <InternalsVisibleTo Include="Dhadgar.Agent.Windows.Uat.Tests" />
     </ItemGroup>
   </Project>
   ```

   Add `[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]` in `Properties/AssemblyInfo.cs`. Make every type `internal`. If the build reports a duplicate output file or a conflict caused by the Nodes reference (for example `appsettings.json`), report the exact message and stop with `blocked spec`; do not suppress it.

4. `UatProgram.cs`: no top-level statements (Nodes' `Program` would give CS0436).

   ```csharp
   namespace Dhadgar.Agent.Windows.Uat;

   internal static class UatProgram
   {
       public static async Task<int> Main(string[] args)
       {
           var parsed = UatArgs.Parse(args);
           if (!parsed.IsSuccess) { Console.Error.WriteLine(parsed.Error); return UatExitCodes.Refused; }
           using var cts = new CancellationTokenSource();
           Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
           return await UatRunner.RunAsync(parsed.Value!, cts.Token);
       }
   }
   ```

   `UatRunner.RunAsync` builds the `RunContext`, dispatches the verb, and always runs cleanup and writes results in `finally` (also after Ctrl+C or an internal error).
5. Files and members (namespaces follow folders):
   - `UatArgs` (`Verb`, `RepoRoot`, `RunId`, `Mode`, `AcceptMachineChanges`, `RunDir` for `cleanup`), `static Result<UatArgs> Parse(string[] args)`. `RunId` must match `^\d{8}-\d{6}-[0-9a-f]{4}$`. Unknown arguments are a failure.
   - `UatExitCodes` constants per the contract.
   - `Infra/RunPaths.cs`: `RunPaths(string repoRoot, string runId)`, `RunDir`, `Result<string> Validate(string path)` per R-RUNDIR, and named children `Ca`, `Tls`, `AgentPublish`, `AgentState`, `AgentData`, `Logs`.
   - `Infra/LocalUrl.cs`: `static Result<Uri> Create(string scheme, string host, int port, IReadOnlySet<int> allocatedPorts)` per R-LOCALURL; `https` only with `localhost`, `http` only with `127.0.0.1`.
   - `Infra/Redactor.cs`: `AddSecret(string value, SecretKind kind)` (`EnrollmentToken`, `Password`), `string Redact(string line)` replacing each secret with `***<kind>***`, and `IReadOnlyList<SecretHit> Scan(string filePath)` for P20's scan.
   - `Infra/JobObject.cs`: `CreateJobObjectW`, `SetInformationJobObject` (`JobObjectExtendedLimitInformation` with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000`), `AssignProcessToJobObject`, through `[LibraryImport]` on `kernel32.dll`; `SafeHandle`-based and `IDisposable`.
   - `Infra/ChildProcess.cs`: `static Result<ChildProcess> Start(ChildSpec spec, JobObject job, Redactor redactor, CleanupManifest manifest)`. It writes the `process` manifest entry right after `Start()` (PID and start time are only known then), assigns the job, and pumps stdout and stderr through `Redactor.Redact` into `<runDir>\logs\<name>.log`. `StopAsync` kills the tree (`Kill(entireProcessTree: true)`) and waits up to 15 s. `ChildSpec` holds `FileName`, `Arguments` (list), `WorkingDirectory`, `Environment` (dictionary applied after the removals in the contract), `Name`.
   - `Infra/CommandRunner.cs`: run a short-lived tool (`docker`, `git`, `sc.exe`, `wevtutil`) with `ArgumentList` and a timeout; returns `Result<CommandOutput>` (exit code, redacted stdout, stderr). `using` on the `Process`.
   - `Cleanup/CleanupEntry.cs`: a record per kind (polymorphic JSON with a `kind` discriminator, `[JsonPolymorphic]` and `[JsonDerivedType]`).
   - `Cleanup/CleanupManifest.cs`: `Append(CleanupEntry)` (append and `Flush(true)`), `static Result<IReadOnlyList<CleanupEntry>> Load(string path)`. A corrupt last line (crash mid-write) is skipped with a warning.
   - `Cleanup/IMachineActions.cs`: one method per undo kind, each returning `Result`, plus `bool IsElevated()`. `Cleanup/WindowsMachineActions.cs` implements them (`CertUnregisterSystemStore` through `[LibraryImport("crypt32.dll")]`, flags `CERT_SYSTEM_STORE_LOCAL_MACHINE = 0x00020000` and `CERT_STORE_DELETE_FLAG = 0x10`).
   - `Cleanup/CleanupEngine.cs`: `Task<CleanupReport> UndoAsync(IReadOnlyList<CleanupEntry> entries, CancellationToken ct)`: reverse order; one failure does not stop the rest; each failure goes to `CleanupReport.Failed` with kind and identity. Ctrl+C does not cancel cleanup (pass `CancellationToken.None` from the runner).
   - `Probe/MachineProbe.cs` (behind `IMachineProbe`): `Footprint Scan()` per the contract; `Footprint` lists items as `kind:identity` strings. Uses `AgentCertificateMatcher` from Agent.Core.
   - `Probe/RootSnapshot.cs`: `Take()`, `Save(path)`, `Load(path)`, `Diff(RootSnapshot before, RootSnapshot after, Func<string, X509Certificate2?> lookup)` per the contract.
   - `Results/UatResults.cs` (records per the schema) and `Results/ResultsWriter.cs` (`results.json` with `JsonSerializerOptions { WriteIndented = true }` and camelCase; `results.md`). Writes are to a temp file and then `File.Move(..., overwrite: true)`.
   - `Results/StepRecorder.cs`: `Task<StepResult> RunAsync(string id, string name, Func<CancellationToken, Task<Result<Evidence>>> body, CancellationToken ct)`: times the step, maps `Result` and caught exceptions to a status, and **skips every later step after the first `fail`** (they record `skipped`), except cleanup.
   - `AgentWindowsFacts.cs`: the constants in the contract.
   - `Preflight/Preflight.cs`: `Task<PreflightReport> RunAsync(PreflightMode mode, CancellationToken ct)` with checks: Windows; build ≥ 10240 (`Environment.OSVersion.Version.Build`); elevation (reported; required only for `run`); `CLAUDECODE` (refuses `run`); Docker (`docker info --format {{.ServerVersion}}` exits 0; required from `stack-only` on); `dotnet dev-certs https --check --trust` exits 0 (required from `stack-only` on); port 5672 free (try `TcpListener` on 127.0.0.1:5672; required from `stack-only` on, and the message says to stop a dev-compose RabbitMQ); footprint empty (required for `run`; reported for the others); the run folder path has no spaces (required for `--mode service`); the `Root` snapshot can be read.
6. Verbs in this packet:
   - `preflight`: run every check, write `results.json` with one step `U0` (`pass` when every check required for `run` would pass, else `fail`, with each check in `evidence`), exit 0 or 1. It changes nothing; cleanup is `not-run`.
   - `cleanup --run-dir <path>`: validate the folder through `RunPaths`, load the manifest, undo, probe, diff `Root` against `root-before.txt` if present, write `cleanup-results.json` (the `cleanup` object of the schema) and exit 0 or 3.
   - `run`: implement only the guard chain and the skeleton: refuse (exit 2) unless elevated, `--accept-machine-changes`, no `CLAUDECODE`, preflight `U0` passes for `run`; then take the `Root` snapshot, and call `IUatScenario.RunAsync` for the mode. In this packet the scenario registry is empty, so `run` records `U1` as `skipped` with "scenario not built yet (P19)" and goes to cleanup. Coders never execute `run`; the unit tests cover it with fakes.
   - `stack-only`: refuse with exit 2 and "not built yet (P19)".
7. `tests/Dhadgar.Agent.Windows.Uat.Tests/Dhadgar.Agent.Windows.Uat.Tests.csproj` (copy the shape of `tests/Dhadgar.Agent.Windows.Tests/Dhadgar.Agent.Windows.Tests.csproj`), in the sln, referencing the driver and `src/Agents/Dhadgar.Agent.Windows`.
8. Tests (each fails when the guarded behaviour is reverted):
   - `UatArgsTests`: valid `run` parses; missing `--accept-machine-changes` on `run` fails; bad run ID fails; unknown argument fails.
   - `LocalUrlTests`: `https://localhost:<allocated>` passes; `http://localhost`, `https://127.0.0.1`, `https://example.com`, an unallocated port each fail.
   - `RunPathsTests`: a child of the run folder passes; `..` escape, a sibling run folder, a null character and a relative path fail.
   - `RedactorTests`: a token inside a longer line is replaced; two secrets in one line; `Scan` reports the line number and kind without the secret text.
   - `CleanupManifestTests`: round-trip of every kind; a truncated last line is skipped.
   - `CleanupEngineTests` with a fake `IMachineActions`: undo order is the reverse of append order; a failure in one entry still undoes the others and is reported; running undo twice gives the same calls and success (idempotent); `existedBefore: true` entries are never deleted.
   - `RootSnapshotTests`: removed thumbprint → `rootRemoved`; added `IsAgentCa` → `rootAdded`; other addition → warning only.
   - `UatRunnerGuardTests` with fakes: `run` with `CLAUDECODE` set → exit 2 and no manifest entry; not elevated → exit 2; footprint present → exit 2 and no manifest entry.
   - `AgentWindowsFactsTests` (plain `[Fact]`; it compares constants, so it also runs on Linux CI): each constant equals the agent's (`Dhadgar.Agent.Windows.Program.ServiceName`, `Program.EventLogSourceName`, `WindowsCertificateStore.CaStoreName`, `AgentRegistry.KeyPath`, `AgentRegistry.EnrollmentTokenValueName`). It fails when either side renames.
9. `src/Agents/Dhadgar.Agent.Windows/Properties/AssemblyInfo.cs`: add `[assembly: InternalsVisibleTo("Dhadgar.Agent.Windows.Uat.Tests")]` next to the existing one. No other `src/` change.

**Checks (in order)**

```powershell
dotnet build <wt>\tests\Dhadgar.Agent.Windows.Uat\Dhadgar.Agent.Windows.Uat.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <wt>\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Agent.Windows.Tests\Dhadgar.Agent.Windows.Tests.csproj
pwsh -NoProfile -File <wt>\scripts\uat\Invoke-AgentWindowsUat.ps1 -Preflight
```

The last command must exit 0 or 1 (not 2 or 4), create `<wt>\.uat\agent-windows\<runId>\results.json` with `verb = preflight`, `launchedBy = agent` and `cleanup.status = not-run`, and leave no manifest. Paste the `U0` evidence into the report. Also confirm `git status` shows nothing under `.uat/`.

**Docs owed:** `scripts/uat/README.md` (new): what the UAT is, D-S225 (who may run what), the threat model (D-S237 (3)), parameters, exit codes, where results go, how to run `-CleanupOnly` after a crash. **(P18-ruling-1 R4)** The agent README's UAT pointer moved to P20; P18 does not edit `src/Agents/Dhadgar.Agent.Windows/README.md`.

**Amended by [P18-ruling-1](worknotes/P18-ruling-1.md) (D-S237):** `IMachineActions.GetProcessImage` returns `Result<ProcessImage>` (no `Success(null)`); `RunPaths` gains `RepoRoot` and `StackImages`; the process rule drops the dotnet-host arm and refuses the driver's own PID; the quiesce pass runs `Reject` inside its guard. The ruling note is the spec for the fix.

### P19: UAT stack, token mint and `-StackOnly` smoke

- **Branch** `s2/p19-windows-uat-stack`. **Worktree** `…\sprint2-agent-nodes-p19`. **Depends on** P18. **Security review** yes (enrollment tokens, client certificates, secrets for local containers).
- **Commit subject** `test(uat): local Docker, Nodes and Gateway stack with token mint and smoke enrollment`

**Rules:** all wave 2U rules. No `src/` change. Stack settings exactly as in the contract table.

**Change**

1. `Stack/FreePorts.cs`: `AllocateLoopbackPort()` (bind `TcpListener(IPAddress.Loopback, 0)`, read the port, stop) and the run's `IReadOnlySet<int>` of allocated ports for `LocalUrl`. 5672 is added to the set only after the preflight check that it is free.
2. `Stack/Secrets.cs`: `GeneratePassword()` returns 24 random bytes as lowercase hex (string, because it must reach a child environment); every one is passed to `Redactor.AddSecret(..., Password)` at once.
3. `Stack/DockerStack.cs`: `StartAsync` writes the `container` manifest entry, then `docker run -d --name <name> --label meridian-uat=<runId> -p ... -e POSTGRES_PASSWORD -e POSTGRES_DB=dhadgar_nodes_uat postgres:16` (values from the child environment), same shape for RabbitMQ. Ready checks: `docker exec <pg> pg_isready -U postgres -d dhadgar_nodes_uat` until exit 0 (120 s, poll 2 s; image pulls count against the budget), `docker exec <mq> rabbitmq-diagnostics -q ping` until exit 0 (120 s). A timeout is `fail` with the last 20 lines of `docker logs <name>` (redacted).
4. `Stack/ServiceHost.cs`: starts Nodes, then Gateway, as `ChildProcess` with the contract's environment and `FileName = RunPaths.StackImages[0]` (Nodes) and `[1]` (Gateway), so cleanup may kill them (R-CLEANUP, D-S237); `ServiceEnvironmentTests` also asserts both `FileName`s; waits for `GET <base>/healthz` 200 (Nodes 90 s, Gateway 60 s, poll 1 s, `HttpClient` timeout 5 s). If the child exits early, `fail` with its exit code and the last 30 lines of its log. The Gateway health check uses the default `HttpClientHandler` certificate validation (the dev certificate is trusted for the user; `dotnet dev-certs https --check --trust` was a preflight check). **Never** set a validation callback that returns `true`.
5. `Stack/NodesDb.cs`: `NodesDbContext Create()` with `new DbContextOptionsBuilder<NodesDbContext>().UseNpgsql(connectionString).Options`. Callers `await using` it and use `AsNoTracking()` for reads.
6. `Stack/TokenMinter.cs`: `Task<Result<MintedToken>> MintAsync(Guid organizationId, string label, CancellationToken ct)` with the real service:

   ```csharp
   await using var db = _nodesDb.Create();
   var service = new EnrollmentTokenService(
       db, new NoopAuditService(), TimeProvider.System,
       Options.Create(new NodesOptions()), NullLogger<EnrollmentTokenService>.Instance);
   var (token, plainText) = await service.CreateTokenAsync(
       organizationId, createdByUserId: $"uat-{_runId}", label, TimeSpan.FromMinutes(30), ct);
   _redactor.AddSecret(plainText, SecretKind.EnrollmentToken);
   return Result<MintedToken>.Success(new MintedToken(token.Id, plainText));
   ```

   `MintedToken` overrides `ToString()` to print only the ID. `NoopAuditService : IAuditService` returns completed tasks for every member. The organization ID is one `Guid.NewGuid()` per run, recorded as evidence. If `NodesOptions` validation needs values to construct, set the minimum and say so in the report.
7. `Stack/GatewayClients.cs`: `HttpClient` factory over `SocketsHttpHandler` with `AllowAutoRedirect = false`; `WithClientCertificate(X509Certificate2)` sets `SslOptions.ClientCertificates` and `LocalCertificateSelectionCallback` returning that certificate. A new handler per probe (TLS sessions are cached per handler, so reusing one would hide a certificate change).
8. `Stack/TestKeyCertificate.cs`: given an `ECDsa` (P-384) and the PEM from Nodes, build `X509Certificate2.CreateFromPem(pem).CopyWithPrivateKey(key)`, export PKCS#12 with a random 32-byte password, reload with `X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.UserKeySet)` (SChannel cannot use an ephemeral key for a client certificate), and zero the PFX and password in `finally`. No `PersistKeySet`, so the key goes away on dispose; dispose it in the step's `finally`.
9. `Scenarios/StackScenario.cs` implementing U1 and the smoke. Verb `stack-only`: elevation is not required and `CLAUDECODE` is allowed; it refuses (exit 2) only when Docker is down, port 5672 is busy, the dev certificate is not trusted, or a `meridian-uat-*` container exists. It changes nothing on the machine outside Docker and the run folder:
   - U1: containers, Nodes, Gateway healthy. Evidence: ports, image IDs (`docker inspect --format {{.Image}}`), Nodes and Gateway PIDs.
   - S-mint: mint a token for a new org ID.
   - S-enroll: `POST https://localhost:<gw>/api/v1/agents/enroll` with `EnrollNodeRequest(token, "windows", new HardwareInventoryRequest(Environment.MachineName, RuntimeInformation.OSDescription, Environment.ProcessorCount, <memory>, <disk>, <nics or empty>), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), "uat")` (adjust to the record's exact constructor). Expect 200 (or the status `EnrollmentEndpoints` documents for success) and an `EnrollNodeResponse` whose PEM's SAN equals `AgentCertificateProfile.SpiffeId(nodeId)` and whose public key equals the key's.
   - S-negotiate: `POST https://localhost:<gw>/hubs/agent/negotiate?negotiateVersion=1` with header `AgentHubProtocol.VersionHeader: AgentHubProtocol.CurrentVersion`: with the certificate → 200; without → 401. Evidence: both status codes.
   - **S-hub-ws (wave1-ruling-1).** Every in-process hub test forces long polling, so this is the first run of the WebSocket upgrade through YARP (the `X-Client-Cert` header on the upgrade request, Nodes' forwarded certificate on an upgrade). With the S-enroll key and certificate (`TestKeyCertificate`), build `new HubConnectionBuilder().WithUrl(new Uri($"https://localhost:{gw}{AgentHubProtocol.Path}"), o => { o.Transports = HttpTransportType.WebSockets; o.ClientCertificates = [cert]; o.Headers[AgentHubProtocol.VersionHeader] = AgentHubProtocol.CurrentVersion; }).Build()`, `await using` it, `StartAsync` (30 s timeout), then `InvokeAsync<AgentHeartbeatAck>(nameof(IAgentHub.Heartbeat), new AgentHeartbeat(1, 1, 1, 0, "uat-smoke", null))`. Pass when the start succeeds with WebSockets as the only allowed transport, the ack is `Acknowledged`, and the node's `LastHeartbeat` in the DB is set. Do not set `SkipNegotiation`, and never set a server-certificate callback. If the UAT project does not reference the SignalR client yet, add `<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />` with no version (R-CPM; it is already in `Directory.Packages.props`). Evidence: transport `WebSockets`, the ack's `ServerTime`, `LastHeartbeat`. A failure here with S-negotiate green is a product defect in the upgrade path: `blocked spec` with the evidence.
   - S-db: the node row exists with the org ID; the token's `UsedAt` is set and `UsedByNodeId` equals the node ID.
   - Then cleanup (containers and processes; nothing else was created) and results.
10. Register `StackScenario` as the first part of the `run` scenario for both modes (`run` = stack, then P20's legs).

**Tests**

- `DockerArgsTests`: the `docker run` argument list for each container contains `-e POSTGRES_PASSWORD` / `-e RABBITMQ_DEFAULT_PASS` **without** a value and binds only to `127.0.0.1`. Fails if a password is put on the command line or a port is bound to all interfaces.
- `ServiceEnvironmentTests`: the Nodes and Gateway environments equal the contract table (keys and value shapes), contain no `Agent__*` key, and the Gateway destination key uses `d1`.
- `TokenMinterTests` (EF InMemory `NodesDbContext`, which the Nodes tests already use): the minted token row exists, has the label and validity, and the plain text was added to the redactor.
- `GatewayClientsTests`: no handler has `ServerCertificateCustomValidationCallback` or `SslOptions.RemoteCertificateValidationCallback` set.

**Checks (in order)**

```powershell
dotnet build <wt>\tests\Dhadgar.Agent.Windows.Uat\Dhadgar.Agent.Windows.Uat.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj
pwsh -NoProfile -File <wt>\scripts\uat\Invoke-AgentWindowsUat.ps1 -Preflight
pwsh -NoProfile -File <wt>\scripts\uat\Invoke-AgentWindowsUat.ps1 -StackOnly
docker ps -a --filter label=meridian-uat --format "{{.Names}}"
```

`-StackOnly` must exit 0 with `cleanup.status = clean`, and the last command must print nothing. If it fails, attach the failing step's message and the last 30 lines of the named log (they are redacted) to the report. A failure that is a product defect (for example enroll through the Gateway returns 4xx/5xx) is reported as `blocked spec` with the evidence; do not change `src/`.

**Docs owed:** `scripts/uat/README.md`: the stack, the ports, the RabbitMQ 5672 requirement, and the `-StackOnly` smoke.

### P20: UAT agent legs U2-U4, U9 wiring and secret scan (process mode)

- **Branch** `s2/p20-windows-uat-agent`. **Worktree** `…\sprint2-agent-nodes-p20`. **Depends on** P19 and P26 (D-S236: P26 makes `dotnet publish` emit the `appsettings.json` that `AgentPublishConfig` edits). **Security review** yes (writes the enrollment token to HKLM, drives the agent's machine certificate store).
- **Commit subject** `test(uat): enroll, heartbeat and restart the real Windows agent in process mode (#101)`
- After merge the packet is **UATPending**: the user runs `-Mode Process -AcceptMachineChanges`.

**Rules:** all wave 2U rules. No `src/` change.

**Change**

1. `Agent/AgentPublishConfig.cs`: `Result Apply(string appsettingsPath, AgentConfig config)` and `Result SetRenewalThreshold(string appsettingsPath, int? days)` per the contract table (`null` removes the key). Paths through `RunPaths`.
2. `Agent/RegistryToken.cs` (behind `IRegistryToken`): `Write(string token)` writes manifest entries first: `registryKey` for `SOFTWARE\Meridian Console` and for `SOFTWARE\Meridian Console\Agent`, each with `existedBefore` from `OpenSubKey`, then creates the key and sets the `REG_SZ` value. `bool ValueExists()`. Use `RegistryView.Registry64`.
3. `Agent/AgentProcess.cs`: starts `<runDir>\agent\Dhadgar.Agent.Windows.exe` as a `ChildProcess` named `agent` (log `agent.log`), working directory `<runDir>\agent`, environment `DOTNET_ENVIRONMENT=Production`. Before the **first** start, write the manifest entries the agent may cause: `eventSource` (`Meridian Console Agent`, `existedBefore` from `EventLog.SourceExists`), `certStore` (`MeridianConsoleAgent`, `existedBefore: false`, guaranteed by preflight), `directory` for `%ProgramData%\Meridian Console\Agent` (`existedBefore` from `Directory.Exists`; it holds the agent's machine `Temp` and `Servers`, so there is no separate Temp entry, D-S238). `StopAsync` kills the tree (a crash-style stop; a graceful stop of a console process from outside is not reliable). Expected on every start: a warning that service recovery could not be configured (no `DhadgarAgent` service in process mode); record it under `warnings` if seen in `agent.log`.
4. `Probe/StoreInspector.cs`: `IReadOnlyList<AgentCertInfo> AgentCertificatesInMy()` (every `LocalMachine\My` certificate matching `IsAgentClientCertificate` with validity ignored): SHA-1 and SHA-256 thumbprints, SAN URIs, `NotBefore`/`NotAfter`, `HasPrivateKey`, and for the key `ECDsaCng` → `Key.KeyName`, `Key.Provider.Provider`, `Key.IsMachineKey`, and whether it can sign (sign and verify 32 random bytes). `CaInAgentStore()`; `AgentCasInRoot()`. Each agent certificate seen is appended to the manifest (`certificate`) at once, so cleanup removes it and its key even if a later step fails. No `cngKey` entry: the key is deleted through the certificate (D-S238).
5. `Scenarios/AgentLegs.cs`, after the stack:
   - **U2 Token.** Mint token T1; write it to the registry. Pass when the value exists. Evidence: token ID, org ID.
   - **U3 First start.** Apply the agent config; start the agent. Poll (2 s, up to 90 s) until all hold, then pass; else `fail` naming the first condition that never held:
     - `<runDir>\agent-state\agent-state.json` exists and its `NodeId` (case-insensitive property) parses;
     - DB: exactly one `Node` with the run's org ID, that `Id`, `Status` `Online` or `Degraded`, `LastHeartbeat` not null;
     - T1 has `UsedAt` set and `UsedByNodeId` = node ID;
     - the registry value is gone;
     - exactly one agent certificate in `My`, with a private key that is a machine CNG key and can sign; its SAN URI equals `AgentCertificateProfile.SpiffeId(nodeId)`;
     - DB: exactly one `AgentCertificate` for the node with `IsRevoked == false`, and its `Thumbprint` equals the store certificate's SHA-256 hex (compare case-insensitively; if Nodes stores SHA-1, compare that and note it);
     - the CA is in `MeridianConsoleAgent`, and the client certificate chains to it (`X509Chain`, `TrustMode = CustomRootTrust`, `CustomTrustStore` = that CA, `RevocationMode = NoCheck`);
     - no `IsAgentCa` certificate in `LocalMachine\Root`.
     Evidence: node ID, SHA-256 thumbprint, key name, `LastHeartbeat`, `NotAfter`.
   - **U4 Restart (#101).** Mint token T2 and write it to the registry; record the `LastHeartbeat` baseline and the certificate row count; stop the agent; start it. Pass when, within 60 s: `LastHeartbeat` is later than the baseline; still one node for the org; T2 `UsedAt` is null; the certificate row count is unchanged; `agent-state.json` holds the same node ID; the same thumbprint is in `My`. Evidence: both heartbeats, and whether the T2 value is still in the registry (informational, not a pass condition).
   - Stop the agent at the end of the legs (P21 restarts it).
6. **U9 Cleanup and scan**, the last step for every `run`, in `UatRunner`'s `finally` after the manifest undo:
   - export the agent's events: `wevtutil qe Application /q:"*[System[Provider[@Name='Meridian Console Agent'] and TimeCreated[@SystemTime>='<startedUtc ISO>']]]" /f:text` to `<runDir>\logs\eventlog.txt` (through `CommandRunner`; redacted on write), **before** the event source is deleted;
   - scan every file in `<runDir>\logs\`, `results.md`, `results.json` (as written so far) and `cleanup-manifest.jsonl` with `Redactor.Scan`. An `EnrollmentToken` hit: rewrite that file redacted, and U9 `fail` with file and line (no text). A `Password` hit: rewrite redacted and add a warning only (child logs were already redacted on capture; a hit means a path missed it);
   - probe and `Root` diff per the contract; U9 passes when the manifest undo had no failures, the probe is empty and the diff has no `rootAdded`/`rootRemoved`;
   - delete `<runDir>\ca`, `<runDir>\agent-state`, `<runDir>\agent-data` (their `directory` entries were appended when created; `<runDir>\agent` and `logs` stay).

**Tests**

- `AgentPublishConfigTests`: applying keeps unrelated keys, sets every contract key, and `SetRenewalThreshold(null)` removes the key.
- `AgentLegsTests` with fakes for the registry, the probe, the DB and the agent process: U3 fails with the right message when the registry value remains, when two certificates are in `My`, and when an `IsAgentCa` is in `Root`; U4 fails when the node count becomes 2 and when T2's `UsedAt` is set. Each fails if its check is deleted.
- `SecretScanTests`: a planted token in `agent.log` fails U9 and the file is rewritten without it; a password hit only warns.
- `ManifestOrderTests`: in `AgentLegs`, every manifest append happens before the matching fake machine call (record call order).

**Checks (in order)**

```powershell
dotnet build <wt>\tests\Dhadgar.Agent.Windows.Uat\Dhadgar.Agent.Windows.Uat.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj
pwsh -NoProfile -File <wt>\scripts\uat\Invoke-AgentWindowsUat.ps1 -StackOnly
```

No `run`. Under **Owed to the user run**, list U2-U4 and U9.

**Amendment A1 ([P11-ruling-1](worknotes/P11-ruling-1.md), D-S233). U3 checks the state-file ACL:**

- U3 gains one condition: the DACL of `<runDir>\agent-state\agent-state.json` is protected (`AreAccessRulesProtected`), and every Allow ACE's SID is SYSTEM (S-1-5-18), Administrators (S-1-5-32-544) or the agent's account (the elevated user in process mode, SYSTEM in service and MSI modes).
- Read it through a new `StoreInspector`-style probe method, `StateFileAcl(string path)`, that returns the protected flag and the SIDs of the Allow ACEs. Its evidence lists the SIDs.
- `AgentLegsTests` gets one more case: a fake ACL with a Users write ACE fails U3 with that message, and the case fails if the check is deleted.
- This is the published-agent proof that `Program` registers `WindowsStateFileSecurity` (P27f).

**Amendment A2 ([P20-ruling-1](worknotes/P20-ruling-1.md), D-S238). Manifest entries match the cleanup allowlist:**

- The agent's key is deleted through its `certificate` entry; `AgentLegs` appends no `cngKey`. The parent registry key is allowed by exact path and undone only when empty (`DeleteEmptyRegistryKey`). The Temp `directory` entry is dropped.
- Producers expose `RegistryToken.ManifestEntries`, `AgentProcess.FirstStartEntries` and `AgentLegs.CertificateEntries`; `AgentCleanupEntriesTests` runs them through the real `CleanupEngine`.
- The ruling also absorbs the review and security fix notes: dispose on stop, appended and shared-read `agent.log`, `using` on `AgentProcess`, record certificates first, `Result<TokenRow>`, `https://localhost` endpoint only, no chain downloads.

**Docs owed:** `scripts/uat/README.md`: the step table U0-U4 and U9, what each checks, how to read `results.md`. `src/Agents/Dhadgar.Agent.Windows/README.md`: a "Verifying on Windows" paragraph pointing at the UAT and at `scripts/uat/README.md` (this is also the UAT pointer P14 left, moved here from P18 by P18-ruling-1 R4).

### P28f: agent-run guard for the UAT driver (fix packet)

Specified in full in [worknotes/D-S239-ruling.md](worknotes/D-S239-ruling.md) § P28f (decision D-S239, owner).

- **Branch** `s2/p28f-uat-agent-run-guard`. **Worktree** `…\sprint2-agent-nodes-p28f`, from the w1 tip after P20 merges.
- **Depends on** P20. **Blocks** P21, which builds on the new `RunWithAsync(..., bool launchedByAgent, IWorkTree workTree, ...)` signature.
- **Security review** yes (machine-change guards, cleanup scope).
- **Commit subject** `fix(uat): let an elevated agent run the Windows UAT from a clean tree (D-S239)`
- **What it changes:**
  - It drops the `CLAUDECODE` refusal of `run` in `UatRunner` and the wrapper.
  - An agent run needs a clean tree: `IWorkTree`/`GitWorkTree`, fail closed. The wrapper checks the same tree and refuses `-NoBuild` for agents.
  - `Results/LaunchRecord.cs` writes `launch.json` before the first manifest entry. `run` refuses a used run folder.
  - Agent `cleanup` replays only a folder whose `launch.json` says `launchedBy = agent` for that runId.
  - The preflight check `claudeCodeUnset` becomes `agentRunTree`. `results.json` gains `agentRun`.
  - `scripts/uat/README.md` and the wrapper help are rewritten for D-S239.
  - No edit to `CleanupEngine`, `RunPaths`, `WindowsMachineActions` or any producer list.

### P21: UAT renewal legs U5-U8

- **Branch** `s2/p21-windows-uat-renewal`. **Worktree** `…\sprint2-agent-nodes-p21`. **Depends on** P20, P28f (D-S239), and P12 (with amendment A1, D-S221) and P16 (grace) — both already in w1 by the wave 2 gate. **Security review** yes (renews with the agent's machine key; handles a second client key).
- **Commit subject** `test(uat): renewal, lost-response grace and restart on the real Windows store (D-S212, D-S221)`
- After merge: **UATPending** (the user's `-Mode Process` run now covers U2-U9).

**Rules:** all wave 2U rules. No `src/` change. C1 is the agent's certificate after U4; C2 the orphan the driver creates; C3 the agent's renewal.

**Change** (`Scenarios/RenewalLegs.cs`, after `AgentLegs`, agent stopped)

1. **U5 Lost renewal response.** Open C1 from `LocalMachine\My` (with its machine key). Generate a P-384 `ECDsa` K2. `POST https://localhost:<gw>/api/v1/agents/<nodeId>/certificates/renew` with `RenewCertificateRequest(Convert.ToBase64String(K2.ExportSubjectPublicKeyInfo()))`, presenting C1. Expect 200 and a `RenewCertificateResponse`; build C2 with `TestKeyCertificate` (step 8 of P19). Then, in the DB: C1's row `IsRevoked`, `GraceUntil` ≈ now + 5 min (±60 s); C2's row active. Probes through the Gateway, each with a new handler: negotiate with C1 → 200; with C2 → 200. Record `graceUntil` as evidence. C1 is no longer in the agent's hands after U6, so this is where "C1 inside grace" is proved.
2. **U6 Agent renews from grace.** `SetRenewalThreshold(90)`; start the agent. Pass when, within 90 s: exactly one agent certificate in `My`, C3, with a thumbprint different from C1; DB C3 row active; C2's row `IsRevoked`, `GraceUntil` null, `RevocationReason` "Superseded by a later renewal"; C1's `GraceUntil` unchanged from U5; `LastHeartbeat` advanced. C1's CNG key no longer exists (`CngKey.Exists(keyName, provider, CngKeyOpenOptions.MachineKey)` is false; P14 amendment A2, F18). Append C3's `certificate` entry through `AgentLegs.CertificateEntries` (its key is deleted with it, D-S238). Stop the agent; `SetRenewalThreshold(null)`.
3. **U7 Grace over the wire.** New handler per probe:
   - C2 through the Gateway → 401 (hard revoked);
   - C3 through the Gateway (from `My`) → 200;
   - wait until `GraceUntil + 10 s` (from U5's DB value; poll the clock, honour cancellation), then C1 → 401. The agent deleted C1's key in U6 (P14 A2), so nobody can present C1 over TLS any more; this probe goes **straight to Nodes**: `POST http://127.0.0.1:<nodesPort>/hubs/agent/negotiate?negotiateVersion=1` with header `AgentCertificateProfile.ForwardedCertificateHeader` = base64 DER of C1, from loopback (a trusted proxy in this stack). It proves Nodes' decision, which is what grace is; the Gateway path was proved for C1 in U5. Evidence: each status code and the path used.
   - A TLS failure (not an HTTP status) on a Gateway probe is `inconclusive` with the exception type, never `pass`.
4. **U8 Restart after renewal.** Record the certificate row count; start the agent (threshold back at default). Pass when, within 60 s: `LastHeartbeat` advanced; row count unchanged; only C3 in `My`. Stop the agent.
5. Dispose C1, C2 and K2 in `finally`; C2's key goes with it (no `PersistKeySet`).

**Tests** (fakes for HTTP, DB and stores)

- `RenewalLegsTests`: U5 fails when `GraceUntil` is null or off by more than 60 s; U6 fails when two certificates remain in `My`, when C2 is not hard-revoked, when C1's `GraceUntil` moved, and when C1's key still exists; U7 maps a TLS exception to `inconclusive`; U7 fails when C1 gets 200 after grace; U8 fails when a new row appears.
- `RenewalThresholdOverrideTests`: after U6, the agent `appsettings.json` has no threshold key, also when U6 fails (the reset is in `finally`).

**Checks (in order)**

```powershell
dotnet build <wt>\tests\Dhadgar.Agent.Windows.Uat\Dhadgar.Agent.Windows.Uat.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj
pwsh -NoProfile -File <wt>\scripts\uat\Invoke-AgentWindowsUat.ps1 -StackOnly
```

Under **Owed to the user run**, list U5-U8. A full process-mode run takes about 12 minutes (U7 waits out the 5-minute grace).

**Docs owed:** `scripts/uat/README.md`: U5-U8, the grace timing, and why C1's expiry is checked at Nodes.

### P22: UAT service mode: `DhadgarAgent` as LocalSystem, run-scoped localhost TLS root, decoy CA (D-S222 (A))

- **Branch** `s2/p22-windows-uat-service`. **Worktree** `…\sprint2-agent-nodes-p22`. **Depends on** P21 merged into w1. D-S222 = (A), owner ruling 2026-10-27. **Security review** yes (`LocalMachine\Root`, a LocalSystem service, a PFX with a private key on disk).
- **Commit subject** `test(uat): run the agent as the DhadgarAgent LocalSystem service with a run-scoped TLS root (D-S222)`
- After merge: **UATPending** (the user runs `-Mode Service -AcceptMachineChanges`).

**Rules:** all wave 2U rules above (R-RESULT, R-USING, R-ZERO, R-LOG, R-ARGS, R-LOCALURL, R-RUNDIR, R-CONTRACT, R-CPM, R-NULL, R-XUNIT). No `src/` change. The two `Root` additions in this packet (the TLS leaf and the decoy) are the only `Root` changes the whole UAT makes; each gets its `certificate` manifest entry, flushed, **before** the add. Never add anything with a private key to `Root`. Never set a server-certificate validation callback anywhere.

**Why (F14).** A LocalSystem service trusts only `LocalMachine` roots, and the user's ASP.NET dev certificate is trusted only in `CurrentUser\Root`. So in service mode the Gateway serves a run-scoped `CN=localhost` leaf that the run adds to `LocalMachine\Root` and removes at cleanup.

**Step order in service mode:** U0 → **U10** → U1 → U2 → U3 → U4 → U5 → U6 → U7 → U8 → U9. U10 runs before U1 because the Gateway needs the TLS certificate at start.

**Change**

1. `Service/UatTlsCertificate.cs`: `static Result<UatTlsMaterial> Create(RunPaths paths, string runId, CleanupManifest manifest, Redactor redactor, IMachineActions machine)`.
   - `using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256)`; `CertificateRequest` with subject `CN=localhost, O=Meridian Console UAT <runId>`, SAN DNS `localhost` and IP `127.0.0.1` (`SubjectAlternativeNameBuilder`), EKU serverAuth `1.3.6.1.5.5.7.3.1` only, `BasicConstraints` CA=false (critical), `KeyUsage` DigitalSignature; `CreateSelfSigned(now - 5 min, now + 2 h)`.
   - Password: 32 random bytes as hex in a `char[]`, then a `string` only for the Gateway environment; `redactor.AddSecret(password, SecretKind.Password)` at once.
   - Manifest `directory` for `<runDir>\tls` and `file` for `<runDir>\tls\gateway.pfx`, then write the PFX (`cert.Export(X509ContentType.Pkcs12, password)`). Set the file ACL with `FileSecurity`: protected (`SetAccessRuleProtection(true, false)`), FullControl for the current user's SID and `S-1-5-18` (SYSTEM) only. Zero the PFX bytes and the password `char[]` in `finally`.
   - Manifest `certificate` (`LocalMachine`, `Root`, SHA-1 thumbprint), flush, then add `X509CertificateLoader.LoadCertificate(cert.RawData)` (public only) to `LocalMachine\Root`. `using` on every store and certificate.
   - Returns `UatTlsMaterial(string PfxPath, string Password, string ThumbprintSha1)`; `ToString()` prints only the path and thumbprint.
2. `Service/DecoyCa.cs`: `static Result<string> Plant(string runId, CleanupManifest manifest, IMachineActions machine)`: self-signed `CN=Meridian Console Agent CA, O=UAT decoy <runId>` (P-256, `BasicConstraints` CA=true, valid now−5 min to now+2 h). Manifest `certificate` (`LocalMachine`, `Root`) first, then add only `X509CertificateLoader.LoadCertificate(cert.RawData)`; dispose the key and the certificate at once. Returns the SHA-1 thumbprint.
3. `Service/ServiceControl.cs` (behind `IServiceControl`; `sc.exe` through `CommandRunner`, each token its own `ArgumentList` item):
   - `Create(string exePath)`: refuse with `Failure` when `exePath` contains a space or is not under `RunPaths.AgentPublish`. Manifest `service` entry first. Then `sc.exe create DhadgarAgent binPath= <exePath> start= demand obj= LocalSystem` (items: `create`, `DhadgarAgent`, `binPath=`, `<exePath>`, `start=`, `demand`, `obj=`, `LocalSystem`).
   - `Query()` parses `STATE` from `sc.exe query DhadgarAgent`; `QueryConfig()` parses `START_TYPE`, `SERVICE_START_NAME` and `BINARY_PATH_NAME` from `sc.exe qc DhadgarAgent`.
   - `StartAsync`/`StopAsync`: `sc.exe start|stop DhadgarAgent`, then poll `Query()` every 1 s until `RUNNING`/`STOPPED` (30 s). `StopAsync` on an already stopped service is success.
   - Service name comes from `AgentWindowsFacts.ServiceName`.
4. `Agent/IAgentHost.cs`, the seam that lets the same legs run in every mode:

   ```csharp
   internal interface IAgentHost
   {
       string Mode { get; }                                      // "process", "service" or "msi"
       Task<Result> PrepareAsync(CancellationToken ct);          // manifest entries the agent may cause, before the first start
       Task<Result> StartAsync(CancellationToken ct);
       Task<Result> StopAsync(CancellationToken ct);
       string? ConsoleLogPath { get; }                           // <runDir>\logs\agent.log in process mode; null otherwise
   }
   ```

   - `Agent/ProcessAgentHost.cs` wraps P20's `AgentProcess` with no behaviour change (its "before the first start" manifest entries move into `PrepareAsync`).
   - `Agent/ServiceAgentHost.cs` wraps `IServiceControl`; `PrepareAsync` writes the same `eventSource`, `certStore` and `directory` entries as `ProcessAgentHost`.
   - `AgentLegs` and `RenewalLegs` take an `IAgentHost` instead of `AgentProcess`. Every assertion stays the same. Where P20 reads `agent.log` (the service-recovery warning), read it only when `ConsoleLogPath` is not null.
5. `Scenarios/ServiceSetup.cs`, step **U10 "Service setup"**: `UatTlsCertificate.Create`, `DecoyCa.Plant`, `ServiceControl.Create(<runDir>\agent\Dhadgar.Agent.Windows.exe)`. Pass when the TLS leaf and the decoy are both in `LocalMachine\Root` (by thumbprint) and `QueryConfig()` shows `START_TYPE` demand, `SERVICE_START_NAME` `LocalSystem` and `STATE` stopped. Evidence: both thumbprints, the binary path. The agent's `appsettings.json` is edited exactly as in process mode (`AgentPublishConfig.Apply`), before U10.
6. Gateway environment (P19's `ServiceHost`): in service and MSI modes add `Kestrel__Certificates__Default__Path` = `<runDir>\tls\gateway.pfx` and `Kestrel__Certificates__Default__Password` = the password. `ASPNETCORE_URLS` stays `https://localhost:<gatewayPort>`. Process mode and `-StackOnly` are unchanged (dev certificate). The driver's own Gateway health check keeps the default validation; the leaf is in `LocalMachine\Root`, which the elevated user also trusts.
7. U3 in service mode has one extra condition: the decoy thumbprint is no longer in `LocalMachine\Root` (the agent removes any `IsAgentCa` certificate from `Root`, P14, D-S219). It is a pass condition, not a warning.
8. `Cleanup/CleanupEngine.cs`: add the **quiesce pass** from the contract: before the reverse undo, stop every `service` entry (`IMachineActions.StopService`, not delete) and kill every `process` entry, newest first; failures go to the report like any other and do not stop the undo.
9. `Results/UatResults.cs`: add an optional `service` object, written only in service mode: `{ "name": "DhadgarAgent", "account": "LocalSystem", "decoyRemoved": true, "tlsRootRemovedAtCleanup": true }`. `decoyRemoved` comes from U3; `tlsRootRemovedAtCleanup` from the post-cleanup `Root` check by thumbprint.
10. Scenario registry: `--mode service` = stack scenario with U10 first, then `AgentLegs` and `RenewalLegs` with `ServiceAgentHost`. U9 is unchanged: in service mode the event-log export (`eventlog.txt`) is the agent's log, and it is scanned like every other log.
11. Preflight: "the run folder path has no spaces" stays required for `--mode service` (P18). Add nothing else.

**Tests** (each fails when its change is reverted)

- `UatTlsCertificateTests`: CA=false, EKU serverAuth only, SANs exactly `localhost` and `127.0.0.1`, lifetime ≤ 2 h 5 min, subject `O` starts with `Meridian Console UAT`; with a fake `IMachineActions`, the object passed to the `Root` add has `HasPrivateKey == false`; the `file` and `certificate` manifest entries precede the write and the add; the password reached the `Redactor`.
- `DecoyCaTests`: matches `AgentCertificateMatcher.IsAgentCa`; the object added has no private key; the manifest entry precedes the add.
- `ServiceControlTests` with a fake `CommandRunner`: the exact `sc.exe create` argument list; a path with a space is refused before any call; a path outside the publish folder is refused; `qc` output parsing (start type, account) from a captured sample.
- `AgentHostParityTests`: one `AgentLegs` run with a fake `ProcessAgentHost` and one with a fake `ServiceAgentHost` make the same assertion calls and produce the same step results for the same fake machine state.
- `ServiceLegsTests`: U3 in service mode fails when the decoy is still in `Root`; U10 fails when the start type is `auto` or the account is not LocalSystem.
- `CleanupEngineTests`: add `Undo_ServiceAndProcessEntries_AreQuiescedBeforeAnyOtherUndo` (call order on the fake: `StopService`, `KillProcess`, then the reverse undo, and `DeleteService` after `StopService`).
- `ServiceEnvironmentTests` (P19's): the `Kestrel__Certificates__Default__*` keys appear in service and MSI modes and never in process mode or `stack-only`.

**Checks (in order)**

```powershell
dotnet build <wt>\tests\Dhadgar.Agent.Windows.Uat\Dhadgar.Agent.Windows.Uat.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj
pwsh -NoProfile -File <wt>\scripts\uat\Invoke-AgentWindowsUat.ps1 -Preflight
pwsh -NoProfile -File <wt>\scripts\uat\Invoke-AgentWindowsUat.ps1 -StackOnly
```

`-StackOnly` must exit 0 with `cleanup.status = clean` and must not touch `Root` (it uses the dev certificate): compare `Get-ChildItem Cert:\LocalMachine\Root | Measure-Object` before and after and paste both counts. No `run`. Under **Owed to the user run**, list U10 and service-mode U2-U9.

**Docs owed:** `scripts/uat/README.md`: service mode, the step order with U10, exactly which `Root` changes it makes (the TLS leaf and the keyless decoy), for how long (at most the run, and both expire after 2 hours anyway), how `-CleanupOnly` undoes them, and the quiesce pass.

### P26: Installer endpoint property and registry-backed endpoint (D-S230, F17, F24)

- **Branch** `s2/p26-installer-endpoint`. **Worktree** `…\sprint2-agent-nodes-p26`, from the w1 tip. **Depends on** P14 merged into w1 (it adds `AgentRegistry` and the `Program` wiring). Runs in parallel with P18 and P19 (no shared file); **merges before P20 starts** (D-S236: its csproj change puts `appsettings.json` in the publish output that P20 edits) and so before P23. **Security review** yes (agent project, installer that runs elevated, a registry value that redirects the agent's control plane, folder and token-key ACLs).
- **Commit subject** `feat(agent-windows): CONTROL_PLANE_ENDPOINT installer property read from HKLM (F17)`
- No Docker. Windows is needed for the wixproj build and the publish.

**Rules (inline):**

- R-RESULT: return `Result`/`Result<T>` for IO and registry failures; never throw for them. Error strings use `"[Area.Code] message"`.
- R-USING: `using` on every `RegistryKey`.
- R-LOG: never log the enrollment token. The endpoint URL may be logged.
- R-HTTPS: the endpoint must still pass `ControlPlaneOptions` validation (absolute HTTPS URL); do not loosen it.
- R-CPM: no `Version=` in any `.csproj` or `.wixproj`; no new packages.
- R-NULL: nullable on; build with `-p:TreatWarningsAsErrors=true`.
- R-XUNIT: xUnit v2, plain `Assert`, names `Subject_Condition_Expectation`; follow `tests/Dhadgar.Agent.Windows.Tests` conventions.
- WiX: use `WindowsBuild >= 10240` for "Windows 10 or later", never `VersionNT`. Mark any property that can carry a secret `Hidden="yes"`. Every property passed on the command line that the execute sequence needs is `Secure="yes"`.
- Never install, repair or uninstall the MSI on this machine. Building it is fine.

**Why (F17, F24).** The MSI starts the service during install (`ServiceControl Start="install"`), and the installed `appsettings.json` points at `https://localhost:5000`. There is no installer input for the control plane, so a real install either talks to the wrong host or needs a hand-edited file under Program Files. Separately, the shipped `Files:TempDirectory` (`C:\ProgramData\Meridian Console\Temp`) is not the folder the MSI creates and ACLs (`…\Meridian Console\Agent\Temp`), and the "residual artifacts" comment in `Package.wxs` describes the pre-P14 agent (CA in `Root`, `CN=dhadgar-agent`).

**Contract (installer ↔ agent seam):**

| Item | Installer (`Package.wxs`) | Agent |
| --- | --- | --- |
| Registry key | `HKLM\SOFTWARE\Meridian Console\Agent` (64-bit view) | `AgentRegistry.KeyPath` |
| Token value | `EnrollmentToken` (REG_SZ) from `ENROLLMENT_TOKEN` (`Secure`, `Hidden`) | `AgentRegistry.EnrollmentTokenValueName` (P14) |
| Endpoint value | `ControlPlaneEndpoint` (REG_SZ) from `CONTROL_PLANE_ENDPOINT` (`Secure`), kept across major upgrades | `AgentRegistry.ControlPlaneEndpointValueName` → configuration key `Agent:ControlPlane:Endpoint` |
| Precedence | - | command line > environment variables > **registry value** > `appsettings.{Environment}.json` > `appsettings.json` |
| Service name | `ServiceInstall Name="DhadgarAgent"` | `Program.ServiceName` |

**Change**

1. **Baseline (D-S236, [P26-ruling-1](worknotes/P26-ruling-1.md)).** The MSI has never built (F25). Keep the five WiX v3-to-v5 syntax fixes already in the worktree (`Platform` removed from `<Package>`, `Condition` attribute, `RegistryKey Id="EnrollmentTokenKey" ForceCreateOnInstall="yes"`), but **not** the `util:PermissionEx User=` lines. Then apply the ruling's B1-B3 exactly: B1 adds `<None Update="appsettings.json" CopyToPublishDirectory="PreserveNewest" ExcludeFromSingleFile="true" />` to the agent csproj; B2 adds `<InstallerPlatform>x64</InstallerPlatform>` to the wixproj and removes the two non-publish `win-x64` BindPaths; B3 uses core `<PermissionEx Sddl="D:P(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)" />` on the token key and `<PermissionEx Sddl="D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)" />` in the three `CreateFolder` elements (no `<Permission>`, no `util:PermissionEx`; ICE104). Any other baseline failure: `blocked spec` with the exact error.
2. `src/Agents/Dhadgar.Agent.Windows/Installation/AgentRegistry.cs`: add `internal const string ControlPlaneEndpointValueName = "ControlPlaneEndpoint";`.
3. New `src/Agents/Dhadgar.Agent.Windows/Installation/RegistryEndpointConfiguration.cs`:

   ```csharp
   internal static class RegistryEndpointConfiguration
   {
       internal const string ConfigurationKey = "Agent:ControlPlane:Endpoint";

       /// <summary>
       /// Inserts the installer's endpoint as a configuration source right after the last JSON file source, so it
       /// overrides appsettings*.json while environment variables and the command line still win. A missing or
       /// blank value adds nothing.
       /// </summary>
       internal static void Apply(IConfigurationBuilder configuration, Func<string?> readEndpoint)
       {
           var endpoint = readEndpoint();
           if (string.IsNullOrWhiteSpace(endpoint)) return;

           var source = new MemoryConfigurationSource
           {
               InitialData = [new KeyValuePair<string, string?>(ConfigurationKey, endpoint.Trim())]
           };
           var lastJson = -1;
           for (var i = 0; i < configuration.Sources.Count; i++)
           {
               if (configuration.Sources[i] is JsonConfigurationSource) lastJson = i;
           }
           configuration.Sources.Insert(lastJson + 1, source);
       }

       /// <summary>Reads HKLM (64-bit view). Missing key or value, or a non-string value, gives null.</summary>
       [SupportedOSPlatform("windows")]
       internal static string? ReadFromRegistry()
       {
           try
           {
               using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
               using var key = hklm.OpenSubKey(AgentRegistry.KeyPath, writable: false);
               return key?.GetValue(AgentRegistry.ControlPlaneEndpointValueName) as string;
           }
           catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
           {
               return null;
           }
       }
   }
   ```

   `lastJson + 1` is 0 when there is no JSON source, which still leaves environment variables and the command line after it. A read failure is not fatal: the agent falls back to `appsettings.json`, and the existing options validation reports a bad endpoint.
4. `src/Agents/Dhadgar.Agent.Windows/Program.cs`: right after `Host.CreateApplicationBuilder(args)` (line 37), call `RegistryEndpointConfiguration.Apply(builder.Configuration, RegistryEndpointConfiguration.ReadFromRegistry);`, before any options binding. Log once at Information, after the host is built, which source the endpoint came from only if this is easy with the existing logger; otherwise skip the log.
5. `src/Agents/Dhadgar.Agent.Windows/Installer/Package.wxs`:
   - Properties, next to `ENROLLMENT_TOKEN` (line 59), using the "remember property" pattern so a major upgrade keeps the endpoint and a command-line value still wins:

     ```xml
     <Property Id="CONTROL_PLANE_ENDPOINT" Secure="yes">
       <RegistrySearch Id="ControlPlaneEndpointSearch" Root="HKLM" Key="SOFTWARE\Meridian Console\Agent"
                       Name="ControlPlaneEndpoint" Type="raw" Bitness="always64" />
     </Property>
     <SetProperty Id="CMDLINE_CONTROL_PLANE_ENDPOINT" Value="[CONTROL_PLANE_ENDPOINT]" Before="AppSearch"
                  Sequence="first" Condition="CONTROL_PLANE_ENDPOINT" />
     <SetProperty Id="CONTROL_PLANE_ENDPOINT" Value="[CMDLINE_CONTROL_PLANE_ENDPOINT]" After="AppSearch"
                  Sequence="first" Condition="CMDLINE_CONTROL_PLANE_ENDPOINT" />
     ```

     Declare `<Property Id="CMDLINE_CONTROL_PLANE_ENDPOINT" Secure="yes" />` too.
   - A launch condition that refuses a non-HTTPS endpoint before anything is written. MSI's case-insensitive "starts with" operator is `~<<` (`&gt;&lt;` is "contains", not a prefix test):

     ```xml
     <Launch Condition="Installed OR NOT CONTROL_PLANE_ENDPOINT OR (CONTROL_PLANE_ENDPOINT ~&lt;&lt; &quot;https://&quot;)"
             Message="CONTROL_PLANE_ENDPOINT must be an https:// URL." />
     ```
   - New component in the `ConfigComponents` group (fresh GUID from `[guid]::NewGuid()`; never reuse one):

     ```xml
     <Component Id="ControlPlaneEndpointComponent" Guid="<new GUID>" Directory="INSTALLFOLDER"
                Condition="CONTROL_PLANE_ENDPOINT">
       <RegistryKey Root="HKLM" Key="SOFTWARE\Meridian Console\Agent">
         <RegistryValue Name="ControlPlaneEndpoint" Type="string" Value="[CONTROL_PLANE_ENDPOINT]" KeyPath="yes" />
       </RegistryKey>
     </Component>
     ```

     and `<ComponentRef>` it from `CoreFeature` if the group is not already referenced there (it is: `ConfigComponents`). `StartServices` runs after `WriteRegistryValues`, so the service's first start already sees the value.
   - Replace the launch condition at lines 68-70 with `<Launch Condition="Installed OR WindowsBuild &gt;= 10240" Message="[ProductName] requires Windows 10 or later." />`.
   - Rewrite the residual-artifacts comment (lines 283-315) to match the agent after P14: agent client certificates in `LocalMachine\My` are those issued by `CN=Meridian Console Agent CA` (`Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Issuer -like '*CN=Meridian Console Agent CA*' }`), each with a persisted machine key; the CA lives in the custom store `MeridianConsoleAgent`, never in `Root`; the agent state is `C:\ProgramData\Meridian Console\Agent\agent-state.json`; the event source `Meridian Console Agent`. Keep the firewall and `MeridianGS_*` items. Keep the text a comment; add no custom action.
   - Update the install commands at the top: `msiexec /i MeridianConsoleAgent.msi /qn ENROLLMENT_TOKEN=<token> CONTROL_PLANE_ENDPOINT=https://<host>`.
6. `src/Agents/Dhadgar.Agent.Windows/appsettings.json`: `Agent:Files:TempDirectory` becomes `C:\\ProgramData\\Meridian Console\\Agent\\Temp` (the folder the MSI creates). Change nothing else in the file; in particular leave `ControlPlane:Endpoint` (the dev default).

**Tests** (`tests/Dhadgar.Agent.Windows.Tests`; plain `[Fact]`, they need no registry and run on Linux CI)

- `RegistryEndpointConfigurationTests`:
  - `Apply_ValuePresent_OverridesAppSettings`: a `ConfigurationBuilder` with a JSON source (`AddJsonStream` of `{"Agent":{"ControlPlane":{"Endpoint":"https://from-json"}}}`) then `AddInMemoryCollection` standing in for environment variables (empty). After `Apply(builder, () => "https://from-registry")`, `Build()["Agent:ControlPlane:Endpoint"]` is `https://from-registry`.
  - `Apply_EnvironmentVariableSet_EnvironmentWins`: as above, but the later in-memory source sets the key to `https://from-env`; the result is `https://from-env`. Fails if the source is appended at the end instead of inserted.
  - `Apply_ValueMissingOrBlank_AddsNoSource` (theory: `null`, `""`, `"  "`): `Sources.Count` is unchanged.
  - `Apply_NoJsonSource_InsertsFirst`.
- `InstallerPackageTests`, the installer ↔ agent seam test: find the repo root by walking up from `AppContext.BaseDirectory` to the folder with `Dhadgar.sln`, load `src/Agents/Dhadgar.Agent.Windows/Installer/Package.wxs` with `XDocument` (namespace `http://wixtoolset.org/schemas/v4/wxs`), and assert:
  - every `RegistryKey`/`RegistrySearch` under HKLM uses `Key` = `AgentRegistry.KeyPath`;
  - the `RegistryValue` written from `[ENROLLMENT_TOKEN]` has `Name` = `AgentRegistry.EnrollmentTokenValueName`, and the one from `[CONTROL_PLANE_ENDPOINT]` has `Name` = `AgentRegistry.ControlPlaneEndpointValueName`; the `RegistrySearch` reads that same name;
  - property `ENROLLMENT_TOKEN` has `Secure="yes"` and `Hidden="yes"`; `CONTROL_PLANE_ENDPOINT` and `CMDLINE_CONTROL_PLANE_ENDPOINT` have `Secure="yes"`;
  - no `Launch` condition contains `VersionNT >=` and one contains `WindowsBuild >= 10240`;
  - `ServiceInstall/@Name` and `ServiceControl/@Name` equal `Program.ServiceName`;
  - `util:EventSource/@Name` equals `Program.EventLogSourceName`.
  Each assertion fails when either side renames (revert step 2 or 5 and the test fails).
- `InstallerPackageTests`, baseline guards (D-S236; exact shapes in [P26-ruling-1](worknotes/P26-ruling-1.md)): `PackageWxs_TokenKey_HasProtectedSddl`, `PackageWxs_UsesOnlySddlPermissions`, `Wixproj_InstallerPlatform_IsX64`, `AgentCsproj_PublishesAppSettings`. CI never builds the MSI, so these are its only CI guard.

**Checks (in order)**

```powershell
dotnet build <wt>\src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Windows.Tests\Dhadgar.Agent.Windows.Tests.csproj
dotnet publish <wt>\src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -r win-x64
dotnet build <wt>\src\Agents\Dhadgar.Agent.Windows\Installer\Dhadgar.Agent.Windows.Installer.wixproj -c Release
```

The publish must leave `appsettings.json` in `bin\Release\net10.0\win-x64\publish\`. The last command must produce `Installer\bin\Release\MeridianConsoleAgent.msi` (about 36 MB; no `en-US` folder); paste its size and the WiX warning codes, which must be exactly ICE61 and ICE69 (anything else: `blocked spec`). The ruling's read-only `OpenDatabase(…, 0)` evidence step is optional. If `Dhadgar.Agent.Windows.Uat.Tests` exists on the w1 tip (P18 merged), also run `dotnet test <wt>\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj` (its `AgentWindowsFactsTests` must stay green). Do not install the MSI. Under **Owed to the user run**: the MSI install with `CONTROL_PLANE_ENDPOINT` and the launch-condition change are proved by P23's U11a.

**Docs owed:** `src/Agents/Dhadgar.Agent.Windows/README.md` (install parameters: `ENROLLMENT_TOKEN`, `CONTROL_PLANE_ENDPOINT`, precedence, upgrade keeps the endpoint) and `src/Agents/Dhadgar.Agent.Windows/Installer/README.md` (the same parameters; the build sequence publish then `dotnet build -c Release`; the output path `bin\Release\MeridianConsoleAgent.msi`; the protected-DACL SDDL on the folders and the token key).

### P23: UAT MSI leg: install, enroll, restart, uninstall (D-S223 (B), U11)

- **Branch** `s2/p23-windows-uat-msi`. **Worktree** `…\sprint2-agent-nodes-p23`. **Depends on** P22 and P26 merged into w1. D-S223 = (B), owner ruling 2026-10-27. **Security review** yes (runs the product installer elevated, an enrollment token on the `msiexec` command line (D-S229), uninstall residue).
- **Commit subject** `test(uat): install, enroll, restart and uninstall the agent through the MSI (D-S223)`
- After merge: **UATPending** (the user runs `-Mode Msi -AcceptMachineChanges`).

**Rules:** all wave 2U rules, including the D-S229 exception to R-ARGS (the token is one `ArgumentList` item for `msiexec` only, and nowhere else). No `src/` change; if the MSI itself is wrong, stop with `blocked spec` and the evidence. Never run `msiexec` in a coder session (it is a machine change; D-S225).

**Step order in MSI mode:** U0 → U10 (TLS leaf and decoy only; no `sc.exe create`, the MSI creates the service) → U1 → **U11a** → U3 → U4 → U5-U8 `skipped` ("covered by process and service modes") → **U11b** → U9.

**Change**

1. `scripts/uat/Invoke-AgentWindowsUat.ps1`: `Msi` in the `ValidateSet` and the `Run`-set build per the contract table (publish without `-o`, build the wixproj, copy the MSI to `<runDir>\msi\`). Driver `UatArgs` accepts `--mode msi`.
2. `Msi/MsiProducts.cs` (behind `IMsiProducts`): `IReadOnlyList<string> RelatedProducts(string upgradeCode)` through `[LibraryImport("msi.dll", StringMarshalling = StringMarshalling.Utf16)] MsiEnumRelatedProductsW(string upgradeCode, uint reserved, uint index, char[] productCode)` (39-char buffer; `ERROR_NO_MORE_ITEMS = 259` ends the loop), and `string? ProductVersion(string productCode)` through `MsiGetProductInfoW(productCode, "VersionString", …)`.
3. `Msi/MsiInstaller.cs` (behind `IMsiInstaller`, `msiexec.exe` through `CommandRunner`, timeout 5 min):
   - `Install(string msiPath, MintedToken token, Uri endpoint)`: items `/i`, `<msiPath>`, `/qn`, `/norestart`, `/l*v`, `<runDir>\logs\msi-install.log`, `ENROLLMENT_TOKEN=<token>`, `CONTROL_PLANE_ENDPOINT=<endpoint>`. The token was added to the `Redactor` at mint time (P19); `CommandRunner`'s log line and any exception message show only the redacted list. `endpoint` is a `LocalUrl`.
   - `Uninstall(string msiPath)`: `/x`, `<msiPath>`, `/qn`, `/norestart`, `/l*v`, `<runDir>\logs\msi-uninstall.log`.
   - Exit codes 0 and 3010 are success (3010 adds a warning "reboot requested"); 1603 and anything else are `Failure` with the code and the last 40 lines of the log that contain `Return value 3` or `Error` (redacted).
4. Cleanup manifest kind `msiProduct` (`upgradeCode`, `msiPath`) with the undo in the contract; `IMachineActions.UninstallMsiProduct(upgradeCode)` implements it with `IMsiProducts` and `msiexec /x <productCode>`.
5. Before U11a, append in this order (each before the install): `msiProduct` (`AgentWindowsFacts.MsiUpgradeCode`, msi path); `service` (`DhadgarAgent`); `registryKey` `SOFTWARE\Meridian Console` and `SOFTWARE\Meridian Console\Agent` (`existedBefore` from `OpenSubKey`); `eventSource`; `certStore`; `directory` `%ProgramData%\Meridian Console\Agent` and `%ProgramFiles%\Meridian Console` (`existedBefore` from `Directory.Exists`). The undo of `%ProgramFiles%\Meridian Console` is by kind, like `%ProgramData%`.
6. Probe (`MachineProbe`): add the two footprint items from the contract (an installed product with the agent's UpgradeCode; `%ProgramFiles%\Meridian Console\Agent`).
7. `Scenarios/MsiLegs.cs`:
   - **U11a MSI install.** Mint T1 with a **10-minute** lifetime (add an optional `TimeSpan? lifetime` to `TokenMinter.MintAsync`; default stays 30 min). `Install(msi, T1, https://localhost:<gw>)`. Pass when, within 60 s of `msiexec` returning: exit 0 or 3010; `sc.exe qc DhadgarAgent` shows `START_TYPE` auto and `SERVICE_START_NAME` LocalSystem; `sc.exe query` shows `RUNNING`; HKLM `SOFTWARE\Meridian Console\Agent` has `InstallPath`, `Version`, `DataPath`, `ServiceInstalled` = 1 and `ControlPlaneEndpoint` = the Gateway URL; `<InstallPath>\Dhadgar.Agent.Windows.exe` exists; `RelatedProducts(upgradeCode)` has exactly one product. Evidence: product code, product version, exit code, endpoint value.
   - **U3** uses the shared U3 assertions with an `IAgentHost` for the installed service (`ServiceAgentHost` over the existing service; its `PrepareAsync` writes nothing, step 5 did) and the state path `%ProgramData%\Meridian Console\Agent\agent-state.json` (the agent's default; the MSI mode does not edit the installed `appsettings.json`). Heartbeats run at the shipped 30 s interval, so U3's window is 120 s. If P20 did not factor the U3 and U4 assertions out of `AgentLegs`, extract them into `Scenarios/AgentAssertions.cs` with no behaviour change (`AgentLegsTests` must stay green) and use them from both.
   - **U4** as in process mode (T2 written to the registry by the driver, then restart through `sc.exe stop`/`start`), with a 90 s window.
   - **U11b MSI uninstall.** Stop nothing by hand; `Uninstall(msi)`. Pass when: exit 0 or 3010; `sc.exe query DhadgarAgent` reports the service does not exist (1060); `RelatedProducts` is empty; `%ProgramFiles%\Meridian Console\Agent` does not exist; the HKLM values `InstallPath`, `Version`, `DataPath`, `ServiceInstalled`, `ControlPlaneEndpoint` and `EnrollmentToken` are gone. **Residue** is recorded as evidence and as warnings, not as a failure: agent certificates in `LocalMachine\My`, the `MeridianConsoleAgent` store, `%ProgramData%\Meridian Console\Agent` (the state file), the event source. These are the documented residual artifacts (P26's comment); U9's cleanup removes them through the manifest.
8. U9 additionally scans `msi-install.log`, `msi-uninstall.log` and `msi-cleanup.log` (if present). `msiexec` writes UTF-16 LE logs with a BOM: `Redactor.Scan` must detect the BOM (`new StreamReader(path, detectEncodingFromByteOrderMarks: true)`) and keep UTF-8 as the default. An `EnrollmentToken` hit in an MSI log fails U9 like any other (it would mean `Hidden="yes"` did not mask it).
9. `results.json`: an optional `msi` object, written only in MSI mode: `{ "productCode": "{…}", "productVersion": "…", "installExitCode": 0, "uninstallExitCode": 0, "residue": ["certificate:…", "certStore:MeridianConsoleAgent", "directory:%ProgramData%\\Meridian Console\\Agent"] }`. `results.md` gets the same as a short list under the table.
10. `AgentWindowsFacts`: add `MsiUpgradeCode = "8A3B4E6F-1D2C-4A5B-9E0F-7C8D6B5A4E3F"`, `MsiTokenProperty = "ENROLLMENT_TOKEN"`, `MsiEndpointProperty = "CONTROL_PLANE_ENDPOINT"`, `ControlPlaneEndpointValueName = "ControlPlaneEndpoint"`.

**Tests** (fakes for `CommandRunner`, `IMsiProducts`, registry, probe)

- `MsiFactsTests` (plain `[Fact]`, Linux CI too): parse `Package.wxs` as P26's `InstallerPackageTests` does and assert the `Package/@UpgradeCode` equals `MsiUpgradeCode`, the two property names exist, and `ControlPlaneEndpointValueName` equals `AgentRegistry.ControlPlaneEndpointValueName`. Fails when either side renames.
- `MsiInstallerTests`: the exact `msiexec` argument list for install and uninstall; the token appears only as the single item `ENROLLMENT_TOKEN=<token>`; the logged command line (captured from the fake) contains `***EnrollmentToken***` and not the token; exit 3010 → success with a warning; 1603 → failure carrying the code.
- `MsiLegsTests`: U11a fails when the start type is demand, when `ControlPlaneEndpoint` is missing, when two related products exist; U11b fails when the service still exists or `ControlPlaneEndpoint` remains, and only warns for agent certificates left in `My`; the `msiProduct` manifest entry precedes the install call (call order).
- `CleanupEngineTests`: `msiProduct` undo calls uninstall once per related product and treats 1605 as success; running it twice is idempotent.
- `RedactorTests`: `Scan` finds a token in a UTF-16 LE file with a BOM and in a UTF-8 file without one.

**Checks (in order)**

```powershell
dotnet build <wt>\tests\Dhadgar.Agent.Windows.Uat\Dhadgar.Agent.Windows.Uat.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Windows.Uat.Tests\Dhadgar.Agent.Windows.Uat.Tests.csproj
dotnet publish <wt>\src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -r win-x64
dotnet build <wt>\src\Agents\Dhadgar.Agent.Windows\Installer\Dhadgar.Agent.Windows.Installer.wixproj -c Release
pwsh -NoProfile -File <wt>\scripts\uat\Invoke-AgentWindowsUat.ps1 -Preflight
pwsh -NoProfile -File <wt>\scripts\uat\Invoke-AgentWindowsUat.ps1 -StackOnly
```

No `run`, no `msiexec`. Under **Owed to the user run**, list U10, U11a, U3, U4, U11b and U9 in MSI mode. An MSI run takes about 6 minutes.

**Docs owed:** `scripts/uat/README.md`: MSI mode, its step order, why U5-U8 are skipped, the D-S229 note (the token is on the `msiexec` command line for the install's duration, visible to administrators only; single-use; 10-minute lifetime; masked in the MSI log by `Hidden="yes"`, which U9 proves), and the uninstall residue list.

### P32: the UAT build keeps the tree clean (fix packet)

Specified in full in [worknotes/P32-ruling.md](worknotes/P32-ruling.md) (decision D-S241). That note holds the exact script, the wrapper edit, the tests and the executed checks.

- **Branch** `s2/p32-uat-lockfiles`. **Worktree** `…\sprint2-agent-nodes-p32`, from w1 `0dc3dd6` (after P30f).
- **Depends on** P30f. **Blocks** the UAT runner's next dispatch (Process, then Service, then Msi).
- **Security review** yes: the elevated wrapper, and a build script that runs elevated in every agent run.
- **Commit subject** `fix(uat): restore locked and RID-less, then build with --no-restore, so the UAT build keeps the tree clean (P32)`
- **Why:** the owner's elevated `-Mode Process` at `0dc3dd6` exited 2 "agent run needs a clean tree". The wrapper's self-restoring `publish -r win-x64` added a `net10.0/win-x64` section to the lock files of `Agent.Core`, `Contracts` and `Shared`. No machine change was made.
- **What it changes:**
  - The new `scripts/uat/Build-AgentWindowsUat.ps1` restores every project RID-less with `--locked-mode`, then builds or publishes it with `--no-restore`. It has `-ListSteps`, never calls `exit` and keeps the stale-delete containment.
  - The wrapper's build block becomes one call to that script, and `Invoke-DotNet` is removed. The guards, the tree check, the MSI copy and the driver call are unchanged.
  - Tests: `UatBuildPlanTests` checks the plan for every verb and mode (Windows). `UatScriptTests` is retargeted, and adds a "wrapper builds only through the script" pin and a "no exit" pin (Linux CI). A shared `PwshScript` helper is added.
  - Executed checks: the Process and Msi builds leave `git status` empty. The executed mutation dirties exactly the three lock files.
  - Not changed: any lock file, csproj, props file, workflow or C# file in the driver. D-S239's guard is unchanged, with no allowlist.

### P34: the MSI reads the Windows build from the registry (fix packet)

Specified in full in [worknotes/P34-ruling.md](worknotes/P34-ruling.md) § P34 (decision D-S242). That note holds the exact WiX, the tests, the mutation and the executed checks with [P34-msi-conditions.ps1](worknotes/P34-msi-conditions.ps1).

- **Branch** `s2/p34-msi-build-condition`. **Worktree** `…\sprint2-agent-nodes-p34`, from w1 `6530253`.
- **Depends on** P32 and P33 (w1 `6530253`). **Blocks** the UAT runner's Msi rerun. It is disjoint from P35, so the two run in parallel.
- **Security review** yes: the installer.
- **Commit subject** `fix(agent-windows): read the Windows build from the registry so the MSI installs on Windows 10 and 11 (P34)`
- **Why:** the runner's Msi run `20261027-183915-637c` failed at U11a with `msiexec` exit 1603 in `LaunchConditions`. msiexec's server session reports `WindowsBuild = 9600` and `VersionNT = 603` on Windows 11 (build 26300), so `Installed OR WindowsBuild >= 10240` refuses every supported machine.
- **What it changes:**
  - In `Package.wxs`, AppSearch reads `CurrentBuildNumber` (raw, 64-bit view) into `WINDOWS_BUILD_NUMBER`, and the row becomes `Installed OR WINDOWS_BUILD_NUMBER >= 10240`.
  - `InstallerPackageTests` replaces the `WindowsBuild` pin, pins the search, and scopes the agent-key test by search `Id`.
  - `Installer/README.md` gets a requirements note and the D-S241 build line.
- **Reviewer note:** D-S242 supersedes CLAUDE.md's "`WindowsBuild` not `VersionNT`" rule (owner action O1 in the ruling).

### P35: a second cleanup treats a file under a removed folder as undone (fix packet)

Specified in full in [worknotes/P34-ruling.md](worknotes/P34-ruling.md) § P35 (decision D-S243).

- **Branch** `s2/p35-cleanup-idempotent-file`. **Worktree** `…\sprint2-agent-nodes-p35`, from w1 `6530253`.
- **Depends on** P32 and P33 (w1 `6530253`). **Blocks** the UAT runner's Msi rerun. It is disjoint from P34.
- **Security review** yes: a machine action in the elevated UAT driver. The reviewer confirms that the allowlist did not move.
- **Commit subject** `fix(uat): treat a file whose folder is already gone as undone, so a second cleanup is clean (P35)`
- **Why:** the runner's extra `-CleanupOnly` on the cleaned run `20261027-183915-637c` reported `footprint-left` (exit 3). `File.Delete` throws `DirectoryNotFoundException` for `tls\gateway.pfx` because the first pass had already removed `tls\`.
- **What it changes:**
  - `WindowsMachineActions.DeleteFile` returns success on `DirectoryNotFoundException`.
  - Real-filesystem tests cover the missing parent, the missing file, the locked file (still a failure), and the real engine run twice (second pass clean) with a missing file outside the run folder still refused.
  - One paragraph in `scripts/uat/README.md`.
- **Not changed:** `CleanupEngine.cs` (`Reject`, `DeleteGuarded`), `RunPaths.cs`, `CleanupEntry.cs`, producers, `UatRunner.cs` (D-S238).

### P36: install the service from the agent exe's component (fix packet)

Specified in full in [worknotes/P36-ruling.md](worknotes/P36-ruling.md) § P36 (decision D-S244). That note holds the exact WiX, the test, the mutation and the executed checks with [P36-msi-tables.ps1](worknotes/P36-msi-tables.ps1) (`-Checks P36`).

- **Branch** `s2/p36-msi-service-keypath`. **Worktree** `…\sprint2-agent-nodes-p36`, from the w1 tip (`1159f36`).
- **Depends on** P34 and P35 (merged). **Blocks** P37 and the UAT runner's Msi rerun.
- **Security review** yes: the installer. The reviewer confirms that the service account, start type and recovery settings did not change.
- **Commit subject** `fix(agent-windows): install the service from the agent exe's component so ExecServiceConfig finds it (P36)`
- **Why:** the runner's Msi run `20261027-192738-7347` failed at U11a with exit 1603: `Wix4ExecServiceConfig` got 0x80070424 because `InstallServices` had skipped the `ServiceInstall` row of the registry-keyed `ServiceComponent` (F28).
- **What it changes:**
  - In `Package.wxs`, `ServiceInstall` (with `util:ServiceConfig`) and `ServiceControl` move verbatim into `MainExecutableComponent`. The `ServiceInstalled` value moves there without `KeyPath`. `ServiceComponent` and its `ComponentRef` are deleted.
  - `InstallerPackageTests` gains `PackageWxs_ServiceInstall_IsInTheComponentWhoseKeyPathIsTheAgentExe`.
  - One sentence in `Installer/README.md`.
- **Do not** rerun the Msi leg after P36 alone: that MSI installs and still writes the token into the log (F29).

### P37: write the enrollment token through a hidden custom action (fix packet)

Specified in full in [worknotes/P36-ruling.md](worknotes/P36-ruling.md) § P37 (decision D-S245, amends D-S229). It has the same executed check, with `-Checks All`.

- **Branch** `s2/p37-msi-token-hidden-ca`. **Worktree** `…\sprint2-agent-nodes-p37`, from the w1 tip **after P36 merges** (the two share `Package.wxs`, `InstallerPackageTests.cs` and `Installer/README.md`).
- **Depends on** P36. **Blocks** the UAT runner's Msi rerun.
- **Security review** yes: a new elevated custom action that handles the enrollment token, and the agent key's ACL.
- **Commit subject** `fix(agent-windows): write the enrollment token through a hidden custom action so no MSI log records it (P37)`
- **Why:** the same run's U9 scan found the token in plain text in `logs\msi-install.log`. `Hidden="yes"` masks the property but not the formatted Registry-table value in the `RegAddValue` and `RegRemoveValue` script ops (F29). Every customer's `/l*v` log would hold it.
- **What it changes:**
  - A new project, `src/Agents/Dhadgar.Agent.Windows.InstallerActions` (DTF, net462, `Platform=x64` for the x64 SfxCA, AnyCPU assembly, not in `Dhadgar.sln`), with `EnrollmentTokenActions` (write, delete) and the dependency-free `EnrollmentTokenData`, plus its committed lock file.
  - Two `PackageVersion` entries in `Directory.Packages.props`.
  - A wixproj `ProjectReference`.
  - In `Package.wxs`: the `Binary`, a `HideTarget` setter, a `HideTarget` deferred no-impersonate writer, and rollback and uninstall deletes. The token Registry row and its component are removed. The protected SDDL moves to the unconditional `InstallPathComponent`.
  - Tests:
    - `EnrollmentTokenDataTests`, the seam pin against `AgentRegistry`, through a linked file;
    - six new `InstallerPackageTests`;
    - two obsolete ones deleted.
  - Docs: `Installer/README.md` and `scripts/uat/README.md` (the D-S229 note).
- **Reviewer note:** D-S245 supersedes CLAUDE.md's "`Hidden="yes"` to prevent logging" rule (owner action O2 in the ruling). The live seam proof is the Msi rerun's U3 and U9.

### P39: consume the enrollment token atomically; compare certificate validity as instants (fix packet)

Specified in full in [worknotes/P39-ruling.md](worknotes/P39-ruling.md) § 3-4 (decisions D-S246 and D-S247, findings F30 and F31). That note holds the exact code, the new race test, the three validity tests, the mutation runs and the checks. It replaces the first pass's brief, which blocked with `spec` because the review's `ExecuteUpdateAsync` fix cannot run on the InMemory provider.

- **Branch** `s2/p39-campaign-fix-1`. **Worktree** `…\sprint2-agent-nodes-p39` (exists at `c0a641e`, clean; reuse it).
- **Depends on** the w1 tip `c0a641e`. **Blocks** campaign security pass 2 and P15.
- **Security review** yes: enrollment and certificate validation. Campaign security pass 2 covers it.
- **Commit subject** `fix(nodes): consume the enrollment token atomically and compare certificate validity as instants (P39, D-S246, D-S247)`
- **Why:**
  - two enrollments that validate the same one-time token before either saves both enroll (F30, campaign-security-1 N1);
  - certificate validity is compared in local wall-clock numbers, which is fail-open east of UTC (F31, N2).
- **What it changes** (seven files):
  - `UsedAt` becomes a concurrency token, with the matching snapshot line and no migration.
  - `EnrollmentService.EnrollAsync` catches `DbUpdateConcurrencyException` around its save, clears the change tracker, audits and returns `invalid_token`.
  - `CertificateValidationService` compares `DateTimeOffset` instants.
  - New `EnrollmentTokenRaceTests`: SQLite in memory, two contexts, a gated CA and the real `AuditService`.
  - Three tests in `CertificateValidationServiceTests`.
  - One sentence in `src/Dhadgar.Nodes/README.md`.
- **Do not** add Testcontainers, a migration or `ExecuteUpdateAsync`. Those are follow-up FU-P39-1, which P15 lists.

---

## Wave 3: close-out, then the one campaign PR

### P15: Close-out

- **Branch** `s2/p15-close-out`. **Worktree** `…\sprint2-agent-nodes-p15`, from the w1 tip. **Depends on** the wave 2 gate (P01-P14, P16, P25 and P27f merged into w1), the wave 2U gate (P18-P23 and P26 merged; UAT runs, launched by the user or by the UAT runner under D-S239, passed in all three modes, Process, Service and Msi, D-S224, D-S222, D-S223) and the campaign review loop at `ship`. **Security review** no (docs only). Its branch merges locally into w1 like every other packet; it ships inside the one campaign PR, not as a PR of its own.
- **After P15 merges into w1 (orchestrator, not this packet):** ops pushes `campaign/sprint2-agent-nodes-w1` once and opens the one PR to `main`. Its body says `Fixes #101`, it carries the `security-review` label, and it merges only on the user's yes. ops files the follow-up issues listed in step 5 at the same time.
- **Commit subject** `docs: close sprint2-agent-nodes (hub, heartbeat, bootstrap, renewal)`

**Rules:** tables have spaces around pipes, and code blocks name their language. "Confirmed working" needs a passing seam test (S1-S4) or a manual check the user recorded; otherwise write "code exists". Edit docs only.

**Change**

1. `docs/PROJECT-STATE.md`, the **only** edit to it in this campaign:
   - status table (around lines 32-41): `DV-1 hub / heartbeat / commands` becomes hub and heartbeat **confirmed working (seam S1, S2; long-polling transport)**, with commands still open (#94, #118). Add "over WebSockets through the Gateway: confirmed working on Windows (UAT `<runId>`, S-hub-ws)" only when the recorded run's `results.json` has S-hub-ws `pass`; otherwise "WebSockets: code exists, untested" (wave1-ruling-1, D2).
   - Nodes mTLS may say "confirmed working under concurrent agent requests" only with P17f's `ServiceLifetimeTests` and `ValidateCertificateAsync_ConcurrentCallsOnOneInstance_AllTrusted` and P07's seam test green (P07-ruling-1).
   - Add a renewal row: **confirmed working (seam S3)**.
   - Bootstrap / #101: **confirmed working (seam S4)**.
   - Windows agent (certificate store, registry token, `Program` wiring, restart without re-enrolling, renewal on the real store): **confirmed working on Windows (UAT `<runId>`, `<gitSha>`)** only when the D-S224 evidence holds: the orchestrator's brief names a run folder `.uat/agent-windows/<runId>/` whose `results.json` has `overall = pass`, `cleanup.status = clean`, either `launchedBy = user` or (D-S239) `launchedBy = agent` with `elevated = true` and `agentRun.treeClean = true`, and a `gitSha` equal to the approved w1 tip (or every later commit touches only `docs/`, which you check with `git diff --stat <gitSha>..HEAD -- src tests scripts` being empty). Otherwise write **code exists**. The orchestrator's brief names one run folder per mode; apply the evidence test to each. Say "as a LocalSystem service" only if the Service run holds (and its `service.decoyRemoved` and `service.tlsRootRemovedAtCleanup` are `true`), and "installed and uninstalled by MSI" only if the Msi run holds (list its `msi.residue` beside the row). A mode whose run does not hold is "code exists" for that mode. Never "in deployment" (D-S210).
   - Gateway rate limiting: `Auth` and `PerTenant` are enforced and unauthenticated agent-route requests are counted (P25, D-S220, D-S228): **confirmed working (Gateway.Tests `PerTenantRateLimitingTests`, `AgentRoute_WithoutCertificate_FloodIsRateLimited`)**. Write this only if those tests are on the w1 tip.
   - DV-2 (:79): update the "Agent has no runtime" text.
   - DV-13 (:100): note the `AgentMtls` scheme.
   - Phase 2 (around 161-178): tick the items done.
   - Note the Gateway-path seam project under #121.
2. `docs/adr/0008-agent-transport-signalr.md`:
   - status becomes Accepted / Implemented (Sprint 2);
   - add an "As built" section covering the Contracts interfaces, the protocol header (#116), certificate forwarding (D-S25) with the chosen trust model (D-S210 (A): Gateway Kestrel on a DNS-only hostname, Nodes trusts the Gateway's CIDR), renewal with its grace window (D-S211, D-S212), the HTTP heartbeat kept (D-S28) and the Redis backplane still deferred.
3. `docs/analysis/README.md`: replace "No campaigns yet." with a campaign table row for `sprint2-agent-nodes` (folder link, Done, close date).
4. `docs/analysis/sprint2-agent-nodes/README.md`: set every packet status, P16-P26 included. These docs land in the same campaign PR as the code, so "Done" means "Done when the campaign PR merges"; say that once above the packet table. The owner rulings (D-S210, D-S212, D-S215, D-S220, D-S222, D-S223, D-S239) are already recorded; change none of them.
4a. **UAT record (D-S224).** For **each** of the three runs (process, service, msi), copy `.uat/agent-windows/<runId>/results.md` to `docs/analysis/sprint2-agent-nodes/uat/windows-uat-<mode>-<runId>.md` unchanged, except a first line `Recorded by P15 from run <runId> (<mode> mode); launched by <user|agent (D-S239)>; source .uat/agent-windows/<runId>/results.json.` (the launcher comes from `launchedBy`). Before copying each, Grep the file for the regex `[A-Za-z0-9+/]{43}=` (a base64 32-byte token): any hit means stop with `blocked spec`. Do not copy `results.json` or the logs. Link the three copied files from the README's "Windows UAT" section and from the PROJECT-STATE row.
4b. `src/Dhadgar.Nodes/README.md` "Rate Limiting" table (around lines 533-540): `PerTenant` becomes `100-token bucket, refills 50/sec` scoped to "Organization (authenticated) or client IP (unauthenticated)", `PerAgent` stays `500 req/60sec` with scope "Client IP (IPv6 /64), never the certificate (D-S26)", and add a row `Auth` `30 req/60sec` "Client IP, shared by login, Identity and `/api/v1/agents/enroll`" (P25, D-S228). Keep the table's existing column style.
5. List the follow-up issues to open in the report; ops files them:
   - retire the HTTP heartbeat endpoint (D-S28);
   - make command NodeId/OrgId checks fail-closed (F10);
   - delete the server-key `IssueCertificateAsync` if unused;
   - the Redis backplane for Nodes scale-out;
   - D-S210 (A) production config in `deploy/kubernetes/helm`: a TLS-passthrough or LoadBalancer path to the Gateway for a DNS-only agent hostname, `AgentIngress__ClientCertificateMode=AllowCertificate`, Nodes `Mtls__ForwardedCertificate__*` with the Gateway pod CIDR, and a check that `AllowCertificate` does not show a certificate picker to browser users on that hostname;
   - F14: implement `Agent:Security:CaCertificatePath` as a control-plane TLS trust pin (or delete the unused setting), so a LocalSystem agent can trust a private control-plane CA without `LocalMachine\Root`;
   - D-S228 watch: the `Auth` limiter (30 per minute per client IP) is one bucket shared by `betterauth-route`, `identity-route` (the whole Identity API, `/exchange` included) and `agents-enroll-route`. Watch its rejections (`aspnetcore.rate_limiting` metrics, the Gateway's `Rate limit exceeded` warning) after release; if panel users behind one NAT hit 429, split identity's API routes onto `PerTenant` and keep `Auth` for login and enroll, or re-key `Auth`;
   - Windows UAT in CI: a `windows-latest` job (or self-hosted runner) running `Invoke-AgentWindowsUat.ps1 -Mode Process`, so the record does not depend on one machine;
   - the P39 follow-ups ([P39-ruling](worknotes/P39-ruling.md) § 6):
     - FU-P39-1: a Nodes PostgreSQL integration fixture (Testcontainers) running the D-S246 race test;
     - R-P39-1: `RevokeTokenAsync` returns 500 when it races a consume; catch the conflict and return `false`;
     - R-P39-2: the loser of a race has already published its events; this closes with `UseBusOutbox` (the notifications outbox);
     - the campaign security residuals: `AgentHub` treats a missing certificate row as usable, and `Hardware` strings are not checked for control characters.

**Checks:** `git diff --stat` touches only `docs/`. Check every relative link in the changed docs resolves with Glob.

**Docs owed:** this packet is the docs.

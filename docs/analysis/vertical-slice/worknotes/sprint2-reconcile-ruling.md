# Sprint 2 reconcile: what happens to wave 1 and wave 2 now that #235 is on main

Date: 2026-10-27. Author: architect. Trigger: Sprint 2 (PR #235, squash `6df3338`) merged into `main` after wave 1's PRs #229-#233 had passed review but before any of them merged. Sprint 2 built the agent hub, gateway agent ingress, forwarded certificates, renewal, agent state and a Gateway + Nodes + agent seam project. Those overlap with VS-03, VS-05, VS-06, VS-07, VS-08, VS-10 and VS-12.

**Rule for this ruling:** what Sprint 2 shipped wins wherever it overlaps, because it is merged, reviewed, UAT-tested on a real Windows host (Process, Service and Msi runs) and owner-approved. This campaign keeps only what is new and extends Sprint 2's types; it never adds a second copy. Every overrule below gives its reason.

Evidence base: `main` at `6df3338` (read in the main checkout), `gh pr view`/`gh pr diff` for #229-#233 (read-only), Sprint 2's ledger `docs/analysis/sprint2-agent-nodes/README.md` (decisions D-S21 to D-S247).

Decisions recorded in the ledger README: D-25 to D-39. Owner decisions: section 5.

## 1. Verdicts

**Overtaken in part (section 9).** #230, #231 and #233 were merged to `main` on 2026-10-27 by another session, without the owner's go-ahead; `main` is now `5eead35`. The rows for those three PRs are history. Section 9 holds the current verdicts, the D-41 merge rule and the VS-34 fold.

| PR | Packet | Verdict | Security re-review |
| --- | --- | --- | --- |
| #229 | VS-03 | **Rework** (VS-03R, a fix commit on the same branch): the hub surface moves to Sprint 2's `Contracts/Nodes/AgentHubContracts.cs`, `NodeId` leaves agent payloads, heartbeat duplicates are deleted. | Yes |
| #230 | VS-04 | **Merge main only**: conflicts in Gateway `Program.cs` and `appsettings.json` plus 26 lock files; resolutions in section 2.2. | Yes (the merged Gateway auth block) |
| #231 | VS-05 | **Drop and supersede** (OD-1): `IHubConnectionConfigurator` is replaced by Sprint 2's `ControlPlaneHubTransport`, and `tests/Dhadgar.Seams.Tests` by `tests/Dhadgar.AgentSeam.Tests` (D-S216). The residuals move to VS-07R and VS-32R. Close without merging; keep the branch. | No (closed) |
| #232 | VS-01 | **Merge main only**: conflicts in `Dhadgar.sln` and `.github/workflows/_dotnet-build-test.yml`; then re-run the leftover-name grep. | No (the owner's label stays) |
| #233 | VS-02 | **Merge main only**: conflicts in Gateway `appsettings.json`, compose and two route tests; route count is now 14, not 13. | No |
| #234 | ledger | **Keep**: ops merges `main` in, then commits this ruling and the ledger edits. | No |

| Wave 2 packet | Verdict | Replaced by or changed to |
| --- | --- | --- |
| VS-06 | Rework as **VS-06R** | Nodes user JWT next to Sprint 2's `AgentMtls` scheme, a path-selected default scheme, strict `AgentPolicy`, the user-JWT seam. No shared secret, no `AdditionalProtectedPrefixes`. |
| VS-07 | Rework as **VS-07R** | `agents-ca-route`, the Servers and Console org routes, delete the dead `Agent` policy, a recording forwarder in the AgentSeam harness, the CA seam. No forwarding middleware. |
| VS-08 | Rework as **VS-08R** | Connection registry, connect-time node check, 512 KiB frame limit, typed `Telemetry` with an ack. Heartbeat bridge and hub skeleton dropped (Sprint 2 has them). |
| VS-09 | **Keep, amended** | Tests move to AgentSeam.Tests; the agent also persists the signing key in `agent-state.json`; contract constants land in Sprint 2's files where they belong. |
| VS-10 | **Superseded** | Sprint 2's renewal (D-S211, D-S212) and `GatewayAgentRenewalSeamTests`. No packet. |
| VS-11 | **Keep** | Adds the AgentSeam and Nodes suites as guards. |
| VS-32 | Rework as **VS-32R** | `tests/Dhadgar.Testing` with `SeamJwt` taken from #231's branch; HubTokenTests de-duplicated; trace redaction test. The user-JWT seam moves to VS-06R. |

## 2. Wave 1

### 2.1 #229 VS-03: rework into VS-03R

**Where the hub contract lives (asked by the brief).** One home: `src/Shared/Dhadgar.Contracts/Nodes/AgentHubContracts.cs`, which Sprint 2 shipped (D-S21). It holds `AgentHubProtocol`, `AgentHubErrorCodes`, `AgentHeartbeatLimits`, `IAgentHub`, `IAgentHubClient`, `AgentHeartbeat`, `AgentHeartbeatAck`. VS-03R **extends** it. Message records, command payloads, events, `AgentMessageLimits`, `AgentCommandTypes`, `AgentJson` and `AgentCommandSigning` stay in `Dhadgar.Contracts.Agents` (they are new, and Sprint 2 has no equivalent). I do not overrule the default.

**Wire compatibility with the agent Sprint 2 shipped.** That agent calls `Heartbeat(AgentHeartbeat)` typed, listens with `On<string>` for `ReceiveCommand`/`ReceivePing`, and invokes `"CommandResult"` and `"Telemetry"` with JSON strings (`ControlPlaneClient.cs` lines 142-143, 328, 360).
- Heartbeat: unchanged, so the shipped agent keeps working.
- New hub methods are additive. The shipped agent's string invokes fail on today's hub (no such method) and will still fail; VS-13 moves the agent to the typed calls.
- `ReceiveCommand` becomes typed (D-29). No Nodes code sends a command today (`git grep ReceiveCommand src` finds only the interface and the agent's `On<string>`), so nothing on the wire changes until VS-15. VS-13 merges before any seam sends one. The only agents built from Sprint 2 are UAT installs, and D-21 already says no agent ships before VS-27.

**Changes VS-03R makes** (contract text: work-packets C-2, rewritten in this edit):

1. `src/Shared/Dhadgar.Contracts/Agents/AgentHubContracts.cs`: delete the file. Its interfaces move, extended, into `Contracts/Nodes/AgentHubContracts.cs`:

```csharp
namespace Dhadgar.Contracts.Nodes;

using Dhadgar.Contracts.Agents;

public static class AgentHubProtocol
{
    // existing: Path, VersionHeader, CurrentVersion, NodeIdClaim, NodeGroup(Guid)
    /// Nodes HubOptions.MaximumReceiveMessageSize (D-30). Escaped JSON: a 64 KiB console batch can be 384 KiB on the wire.
    public const int MaxReceiveMessageBytes = 512 * 1024;
}

public static class AgentHubErrorCodes
{
    // existing: NodeNotFound, NodeDecommissioned, InvalidHeartbeat, HeartbeatFailed
    public const string InvalidTelemetry = "invalid_telemetry";
    public const string InvalidResult = "invalid_result";
    public const string InvalidStatus = "invalid_status";
    public const string InvalidConsoleBatch = "invalid_console_batch";
    public const string UnknownCommand = "unknown_command";
    public const string ServerNotOnNode = "server_not_on_node";
    public const string NotSupported = "not_supported";      // stub answer until VS-08R / VS-33 implement the method
}

public interface IAgentHub
{
    Task<AgentHeartbeatAck> Heartbeat(AgentHeartbeat heartbeat);        // unchanged (Sprint 2)
    Task<AgentHubAck> Telemetry(AgentTelemetry telemetry);              // VS-08R
    Task<AgentHubAck> CommandResult(AgentCommandResult result);         // VS-33
    Task<AgentHubAck> ServerStatusChanged(AgentServerStatus status);    // VS-33
    Task<AgentHubAck> ConsoleOutput(AgentConsoleBatch batch);           // VS-33
}

public interface IAgentHubClient
{
    Task ReceiveCommand(AgentCommandEnvelope command);   // was string (D-S29 deferred this to the commands campaign; D-29)
    Task ReceivePing(string message);                    // unchanged
}

/// Business result of an agent → Nodes hub call (D-S24, D-28). Never thrown as HubException.
public sealed record AgentHubAck(bool Accepted, string? ErrorCode = null);
```

2. `Contracts/Agents/AgentMessages.cs`: delete `AgentHeartbeat` and `AgentServerProcess` (Sprint 2's `AgentHeartbeat` is the one heartbeat; per-server state travels in `AgentServerStatus`). Remove the `NodeId` parameter from `AgentCommandResult`, `AgentServerStatus`, `AgentConsoleBatch` and `AgentTelemetry` (D-S23, D-27). `AgentCommandEnvelope` keeps `NodeId` (Nodes → agent; acceptance rule 2). The three events keep `NodeId` (Nodes sets it from the certificate).
3. `Contracts/Agents/AgentProtocol.cs`: delete class `AgentProtocol` (`Version`, `VersionHeader` and `HubPath` duplicate `AgentHubProtocol`; `MaxHubMessageBytes` moved to `AgentHubProtocol.MaxReceiveMessageBytes`). In `AgentMessageLimits` delete `MaxHeartbeatWarnings`, `MaxWarningLength`, `MaxServersPerHeartbeat` and `MaxAgentVersionLength`. `PingResult.AgentVersion` is bounded by `AgentHeartbeatLimits.MaxAgentVersionLength` (50) instead, so one version bound exists.
4. `src/Dhadgar.Nodes/Hubs/AgentHub.cs`: it implements `IAgentHub`, so add the four methods as stubs that return `new AgentHubAck(false, AgentHubErrorCodes.NotSupported)` and do nothing else. No other Nodes change.
5. Tests in `tests/Dhadgar.Contracts.Tests/Agents/`: drop the pins and serialization cases for deleted members and for `NodeId` on the four records. Add `AgentHubContractTests.cs`:
   - `IAgentHub` has exactly the five methods above with those parameter and return types; `IAgentHubClient.ReceiveCommand` takes `AgentCommandEnvelope`.
   - D-27 guard: no public record in namespace `Dhadgar.Contracts.Agents` whose name starts with `Agent` and that the agent sends (`AgentCommandResult`, `AgentServerStatus`, `AgentConsoleBatch`, `AgentTelemetry`) has a `NodeId` property.
   - Both fail when the change is reverted.
6. `src/Shared/Dhadgar.Contracts/README.md`: one paragraph saying the hub surface is in `Contracts.Nodes` and the messages in `Contracts.Agents`.
7. Before starting: ops merges `origin/main` into `vs/03-agent-contracts` (no conflicts expected; GitHub reports MERGEABLE).

Checks: `dotnet build src/Shared/Dhadgar.Contracts/Dhadgar.Contracts.csproj -c Release`, `dotnet test tests/Dhadgar.Contracts.Tests`, `dotnet build src/Dhadgar.Nodes -c Release`, `dotnet test tests/Dhadgar.Nodes.Tests`, `dotnet test tests/Dhadgar.AgentSeam.Tests` (Sprint 2's hub and renewal seams must stay green; this is the seam for the contract), then the `CI=true` solution build.

### 2.2 #230 VS-04: merge main only

Ops merges `origin/main` into `vs/04-service-jwt` and a `packet-coder` resolves:

- `src/Dhadgar.Gateway/Program.cs`: keep everything Sprint 2 added (the Kestrel `AgentIngress:ClientCertificateMode` block, the `AgentCertificate` policy, `.AddTransforms(AgentCertificateForwarding.Apply)`, the `PerAgent` limiter). Replace only the old `AddAuthentication(JwtBearerDefaults...).AddJwtBearer(...)` block (main lines 96-113) with VS-04's `AddDhadgarJwtBearer(..., "/hubs/console")` call. Leave policy `Agent` for VS-07R to delete.
- `src/Dhadgar.Gateway/appsettings.json`: keep both sides (Sprint 2's `AgentIngress` section and `agents-route` policy `AgentCertificate`; VS-04's `Auth` keys).
- Every conflicted `packages.lock.json`: take main's version, then apply the lock-file rule (work-packets dispatch rules). Never hand-merge lock-file JSON.
- Checks: `dotnet restore Dhadgar.sln --locked-mode`, the `CI=true` solution build, `Dhadgar.Gateway.Tests`, `Dhadgar.ServiceDefaults.Tests`, `Dhadgar.Secrets.Tests` and `Dhadgar.AgentSeam.Tests` (its Gateway factory now runs VS-04's helper; `GatewayAgentHubSeamTests` must stay green).

Security re-review: the merged `Program.cs`. Questions for the advisor: the JWT default scheme does not change how `AgentCertificate` is evaluated (it asserts on `Connection.ClientCertificate`, not on a principal); `access_token` query fallback stays limited to `/hubs/console` and never applies to `/hubs/agent`.

Note for VS-06R: `AddDhadgarJwtBearer` calls `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)`, which sets the default scheme. In Nodes that would fight Sprint 2's `AgentMtls` default (D-S27). VS-06R handles it; #230 does not change.

### 2.3 #231 VS-05: drop and supersede

- `IHubConnectionConfigurator` is the second hook for the same job as Sprint 2's `ControlPlaneHubTransport`. Merging both would leave two ways to change the agent's transport, which is the seam defect class this campaign exists to stop. Closed; D-24 is moot (D-34).
- **Fold or keep separate (asked by the brief): fold into `tests/Dhadgar.AgentSeam.Tests`.** Reason: it already composes the real Gateway and Nodes (`GatewayNodesHarness`, extern alias `gateway`), enrolls a real agent through the Gateway and holds the hub, bootstrap and renewal seams. A second harness (`SeamHost`, `NodesSeamFactory`, `GatewaySeamFactory`) would build the same two hosts differently, and the two would drift. Later packets that name `tests/Dhadgar.Seams.Tests` write into AgentSeam.Tests (D-33), adding extern aliases for Servers and Console when they need them.
- What #231 covered and where it is now:

| #231 piece | Now |
| --- | --- |
| `AgentEnrollmentThroughGatewaySeamTests` | Sprint 2's `GatewayNodesHarness.EnrollThroughGatewayAsync` and the AgentSeam suites |
| `AgentClientHubSeamTests` (heartbeat, rejections) | `GatewayAgentHubSeamTests`; `Nodes.Tests/Integration/AgentHubSeamTests` (foreign CA, payload node ignored) |
| `AgentIngressThroughGatewaySeamTests` (CA fetch, renew without cert, spoofed header) | VS-07R |
| `SeamJwt` | VS-32R (`git show 760af6a:tests/Dhadgar.Seams.Tests/Infrastructure/SeamJwt.cs`) |
| `SeamForwarderFactory` request recording | VS-07R, in `GatewayTestFactory` |
| `CapturingPublishEndpoint`, `InMemoryCertificateStore` | Nodes.Tests' `TestNodesEventPublisher`; Sprint 2's `InMemoryCertificateStore` |

- Ops keeps branch `vs/05-seams-harness` (VS-32R reads a file from `760af6a`).

### 2.4 #232 VS-01: merge main only

Expected conflicts and resolutions:

- `Dhadgar.sln`: Sprint 2 added `Dhadgar.AgentSeam.Tests`, `Dhadgar.Agent.Windows.Uat` and `Dhadgar.Agent.Windows.Uat.Tests` (3 project blocks, 36 configuration lines, 3 nesting lines). Keep all of them and keep VS-01's 12 removals. Keep LF.
- `.github/workflows/_dotnet-build-test.yml`: Sprint 2 added the PSScriptAnalyzer step; VS-01 dropped `src/Dhadgar.ShoppingCart` from the npm list. Keep both.
- Any other conflict: keep both sides' intent; report it.
- After the merge, re-run VS-01's `git grep` check (work-packets VS-01 checks, last command). On `6df3338` the parked names appear only in files VS-01 already edits (`Dhadgar.sln`, `_dotnet-build-test.yml`, `dependabot.yml`, `static-web-apps.yml`, AppHost, compose), so it must still print nothing. Also run `dotnet test tests/Dhadgar.AgentSeam.Tests` and the Agent.Windows UAT unit tests (`tests/Dhadgar.Agent.Windows.Uat.Tests`) once, since they are new projects in the solution.
- `gh pr view --json files` stops at 100 files; use `gh api repos/{owner}/{repo}/pulls/232/files --paginate` when listing this PR.

### 2.5 #233 VS-02: merge main only

- `src/Dhadgar.Gateway/appsettings.json`: keep Sprint 2's `agent-hub-route`, `agents-route` with `AgentCertificate` and the `AgentIngress` section; delete `billing-route`, `tasks-route`, `files-route`, `mods-route` and their clusters. Result: **14 routes, 8 clusters** (main has 18 and 12).
- `tests/Dhadgar.Gateway.Tests/RouteConfigurationTests.cs`: route count 14 (test name `ReverseProxyShouldHave14Routes`), clusters 8, keep Sprint 2's rows. `RateLimiterConfigurationTests.cs`: keep Sprint 2's rows (`agent-hub-route`), delete the four parked rows.
- `deploy/compose/docker-compose.services.yml`: Sprint 2 changed the gateway env (`AgentIngress__ClientCertificateMode`) and the nodes service (`Mtls__ForwardedCertificate__*`, port `127.0.0.1:5040:8080`). Keep both; delete only the parked services and their gateway cluster env lines.
- Checks: VS-02's checks unchanged.

## 3. Wave 2

Base: `main` after #230, #229, #232 and #233 merge. Six packets. Full text in work-packets "Wave 2".

| Packet | What it does now | Depends on | Stage |
| --- | --- | --- | --- |
| VS-11 | Servers import, unchanged except the guard suites | W1 | 1 |
| VS-32R | `tests/Dhadgar.Testing` + `SeamJwt`; HubTokenTests uses it; trace redaction | W1 | 1 |
| VS-07R | `agents-ca-route`; `servers-org-route`, `console-org-route`; delete policy `Agent`; recording forwarder; CA seam | W1 | 1 |
| VS-09 | Signing key, signer, `EnrollNodeResponse` fields, agent validates and persists the key | W1 | 1 |
| VS-08R | Registry, connect-time node check, frame limit, `Telemetry` | W1 | 1 |
| VS-06R | Nodes user JWT, default scheme selector, strict `AgentPolicy`, user-JWT seam | VS-32R, VS-07R | 2 |

Merge order: VS-11, VS-32R, VS-07R, VS-09, VS-08R, VS-06R. Every packet except VS-11 touches a security-review area (auth, enrollment, certificates or the agent hub); VS-11 is also Yes (service auth on a promoted service). One PR per packet.

### Key points of the re-specs

- **VS-06R default scheme (D-35).** Nodes keeps `AgentMtls` for agent paths and adds JwtBearer for users. A policy scheme `NodesAuth` becomes the default; its `ForwardDefaultSelector` returns `AgentMtls` for paths under `MtlsOptions.AgentEndpointPrefix` (`/agents`) or `AgentHubProtocol.Path`, and `JwtBearer` otherwise. So a bearer token is never even evaluated on agent paths and a certificate never on user paths. Policies do not name schemes, which keeps `NodesWebApplicationFactory`'s test-scheme override working for the existing tests. `AgentPolicy` adds VS-06's `AgentIdentityRequirement` (mTLS identity with `node_id`, or mTLS disabled in Development). A test asserts the resolved default scheme, because `AddDhadgarJwtBearer` and `AddMtlsAuthentication` both set a default and the last registration wins.
- **Dropped from VS-06:** the forwarded-certificate middleware and shared secret (Sprint 2 shipped `ForwardedClientCertificateMiddleware` with a TrustedProxies CIDR, D-S25 and D-S210 A), `AdditionalProtectedPrefixes` (Sprint 2's hub is mTLS-authorized by `AgentPolicy` and the `AgentMtls` scheme), the other-CA and `X-Node-Id` seams (Sprint 2's `AgentHubSeamTests` covers both).
- **VS-07R** keeps only the route work and the CA seam. The forwarding middleware and secret are dropped (D-31).
- **VS-08R** answers Telemetry with an ack (D-28), joins the existing `AgentHubProtocol.NodeGroup`, and adds what Sprint 2 lacks: `IAgentConnectionRegistry` (VS-15 needs `IsConnected`), aborting on connect for an unknown or decommissioned node, and the 512 KiB frame limit with the two frame tests.
- **VS-09** persists the key in Sprint 2's `agent-state.json` as two optional fields. Before, persistence was VS-12's job; VS-12 is now mostly superseded by D-S213, so the field moves to the packet that receives it.

## 4. Wave 3 and later: contract sync 2 before dispatch

Do not dispatch wave 3 from the current text. Before it, the orchestrator runs an architect contract sync 2 that rewrites these packets against D-25 to D-39:

| Packet | Change needed |
| --- | --- |
| VS-12 | Mostly superseded by D-S213 (`FileAgentStateStore`, `AgentIdentity`, `StateDirectory`). Keep only what remains: live options (`IOptionsMonitor`) for anything still captured once, and `SecurityOptions` reading the persisted signing key. |
| VS-13 | Build on `ControlPlaneHubTransport` and `AgentHubProtocol`, not `IHubConnectionConfigurator`/`AgentProtocol`. Typed `On<AgentCommandEnvelope>`; typed `Telemetry`/`CommandResult`/... invokes that read `AgentHubAck`. `IControlPlaneClient` keeps D-S227's `Result` contract. |
| VS-15 | `registry.IsConnected` from VS-08R; `AgentHubProtocol.NodeGroup` instead of `AgentHubGroups.Node`. |
| VS-33 | Ack codes instead of `HubException`; no `node_mismatch` (payloads carry no node id). |
| VS-14, VS-16 | Seams in AgentSeam.Tests; VS-16 reuses Sprint 2's bootstrap (`AgentBootstrapSeamTests`) and adds only the Ping handler path. |
| All | `tests/Dhadgar.Seams.Tests` → `tests/Dhadgar.AgentSeam.Tests`; C-6 keys per this edit (`Agent:StateDirectory`, no `AgentIngress:ForwardedCertificateSecret`); VS-27 compose uses Sprint 2's ingress keys. |

## 5. Owner decisions

OD-1 is overtaken: #231 merged, so it cannot be closed unmerged. The owner's intent (seams live in AgentSeam.Tests) is carried out by VS-34 under D-42 (section 9).

| ID | Question | Recommendation | Blocks |
| --- | --- | --- | --- |
| OD-1 (BlockedDecision) | Close #231 (VS-05, reviewed and waiting for your review) without merging it, and put its remaining seams in Sprint 2's `tests/Dhadgar.AgentSeam.Tests`? | **Yes, close it.** Sprint 2 shipped the same harness and transport hook; keeping both means two harnesses and two hooks that drift (section 2.3). | Dispatch of VS-07R, VS-32R and VS-06R (they write into AgentSeam.Tests and take `SeamJwt` from #231's branch). |
| OD-2 (decided 2026-10-27) | Confirm D-21 (strict agent JSON)? | The owner chose **lenient** JSON. That is D-40, which supersedes D-21 (section 8). | Nothing. |

Not owner decisions (architect-decided, listed so the owner sees them at the security approvals): D-29 (typed `ReceiveCommand`), D-30 (frame limit 64 KiB → 512 KiB in VS-08R), D-31 (no shared secret on agent ingress, following the owner's D-S210 A).

## 6. Merge order and security re-review

Overtaken: steps 2, 5 and 6 happened out of order (section 9). The current order is in section 9.6.

1. #234 ledger: ops merges `origin/main` into `campaign/vertical-slice-ledger` (merge, never rebase; D-38), commits these edits, ships it.
2. #230 VS-04 after the merge resolution, review pass and security re-review; owner's yes.
3. #229 VS-03 after VS-03R, review pass and security re-review; owner's yes.
4. #232 VS-01 after the merge and the grep.
5. #233 VS-02 after the merge resolution.
6. #231: closed (OD-1).

Between steps, ops merges the new `main` into the next PR branch and the packet's checks run again (the lock-file rule applies to every conflicted lock file).

Security re-review needed: **#229** (agent contracts, `CommandEnvelope` shape, a Nodes hub edit) and **#230** (Gateway auth merged with agent ingress). Not needed: #231 (closed), #232 and #233 (no security area; #232 keeps the owner's label).

## 7. Things I could not see

The brief asks to include jorsh's three missing ledger edits (today's resume notes, the corrected lock-file rule, the no-rebase rule). They are not in the ledger worktree (clean at `5d9e4f2`), in `main`, or in any PR I read. I wrote my own versions: RESUME.md (today's state), work-packets dispatch rule "Lock files" (D-39) and dispatch rule "Merging main" (D-38). If jorsh's text differs, his wins where it is stricter.

## 8. D-40: lenient agent JSON (owner, OD-2, 2026-10-27)

**Decision.** `AgentJson.Options` skips unknown members (`UnmappedMemberHandling.Skip`) instead of rejecting them, and rejects duplicate properties (`AllowDuplicateProperties = false`). Everything else stays:
- `RespectRequiredConstructorParameters` and `RespectNullableAnnotations`;
- `MaxDepth = 8`, which still applies inside skipped members;
- read-only options;
- every C-2 limit, required-field, null and `Enum.IsDefined` check (D-23).

D-21's text is kept in the README and marked superseded.

**Ledger changes.**
- C-1: the dispatch route row.
- C-2: the `AgentJson` block, the JSON rules and a new signing rule.
- C-7: the compatibility note.
- VS-03R: change 4a, the serialization tests and the revert reasons.
- VS-08R: a rule, plus `Telemetry_WithUnknownMember_IsAccepted` and `Telemetry_WithoutMetrics_IsRejected`.
- Wave-3 and wave-4 lines amended ahead of contract sync 2: VS-12 (identity file note), VS-15 (signs its own re-serialization; the unknown-property row is now accepted and stripped, and a duplicate-property row is added) and VS-29 (compatibility note).

**Wire compatibility.**
- The hub was already lenient. SignalR binds arguments with its own options, so nothing changes for Sprint 2 agents or heartbeats.
- `PayloadJson`/`ResultJson` become forward-compatible both ways. An older agent ignores a new optional field instead of rejecting the command, and an older Nodes or Servers ignores a new `ResultJson` field.
- The cost is silent loss. An older receiver drops a field it does not know. D-40 therefore keeps one part of D-21: a field whose meaning must not be ignored (a new restriction, limit or safety setting) bumps `AgentHubProtocol.CurrentVersion`, and Nodes sends it only to connections that announced that version.
- VS-29's `javaRuntimes` needs no bump. No agent ships before VS-27, and every UAT agent includes VS-29.

**Security notes.**
1. **Unvalidated content under a signature.** If Nodes signed the caller's string, Servers (or anything that can call dispatch) could add a member that Nodes does not know but a newer agent does. Nodes would then sign data it never validated. Mitigation, now in C-2 and VS-15: Nodes signs `JsonSerializer.Serialize(typed, AgentJson.Options)` of the record it validated, so only understood members reach a signature. The agent still verifies over the raw string it received.
2. **Duplicate keys.** With unknown members tolerated, a duplicate key is the remaining way to make two parsers disagree. `AllowDuplicateProperties = false` rejects it on both sides. The re-serialization in note 1 also removes duplicates from anything signed.
3. **Resource use.** Skipped members still count against `MaxPayloadJsonBytes`, `MaxDepth` and the 512 KiB hub frame, so tolerance adds no unbounded input.
4. **Security review.** D-40 changes `AgentCommandEnvelope` parsing and the signed-payload rule, so VS-03R's security re-review and VS-15's security pass must check notes 1 and 2.

## 9. Re-brief: main at `5eead35` (2026-10-27)

### 9.1 What is on main

| Commit | PR | Packet | What it did |
| --- | --- | --- | --- |
| `32779b5` | #233 | VS-02 | Parked Gateway routes, compose services, Discord map |
| `2440ada` | #230 | VS-04 | `AddDhadgarJwtBearer` in Gateway and Secrets |
| `6895cd1` | #231 | VS-05 | `tests/Dhadgar.Seams.Tests` and `IHubConnectionConfigurator` in `ControlPlaneClient` |
| `5eead35` | #236 | - | JwtBearer added to the Seams.Tests lock file (#231 merged with a lock file that failed `--locked-mode`) |

Another session merged #233, #230 and #231 between 23:03Z and 23:10Z without the owner's go-ahead. #229, #232 and #234 are still open.

### 9.2 D-41: merge authority (owner rule, 2026-10-27)

Only the owner's main session merges a PR, and only after the owner says yes to that PR. A green CI run, a `ship` verdict, or a yes given for a different PR or an earlier head does not count. The orchestrator asks; ops runs `gh pr merge` only from the owner's main session, citing the owner's yes. Every other session, including a campaign session that is not the owner's main session, stops at "ready to merge" and reports.

### 9.3 D-42: fold Seams.Tests into AgentSeam.Tests and remove the hook (supersedes D-34)

**Decision.** VS-34 deletes `tests/Dhadgar.Seams.Tests`, moves its single live test into `tests/Dhadgar.AgentSeam.Tests`, and removes `IHubConnectionConfigurator` from the agent. `ControlPlaneHubTransport` stays the only way to change the agent's hub transport.

**Why the hook goes.**
1. Nothing in production uses it. The agent's DI registers no configurator. Its only implementation is `SeamHubConfigurator` in Seams.Tests (`SeamAgent.cs:136`), and that file is deleted with the project.
2. `ControlPlaneHubTransport` (`HttpMessageHandlerFactory`, `Transports`; registered at `AgentServiceCollectionExtensions.cs:65`, never bound from configuration) already carries every agent seam in AgentSeam.Tests and Nodes.Tests through the Gateway. Two hooks for one job is the drift D-34 was meant to prevent.
3. The hook adds a per-connect code path in a security-review area. It runs caller code over `HttpConnectionOptions` after the agent has set the certificate and the version header. Its own XML doc says that replacing `HttpMessageHandlerFactory` is not checked, so a configurator can swap the TLS handler and the fail-closed check cannot see it.
4. That fail-closed check throws `InvalidOperationException` from inside `WithUrl`. That breaks the `Result` contract on `IControlPlaneClient.ConnectAsync` (D-S227).
5. Removing it restores `ControlPlaneClient.cs` to its `6df3338` content: the field, constructor parameter, warning log, configurator loop and the two throws. The HTTPS check (`ControlPlane.InsecureEndpoint`) and the certificate check (`ControlPlane.NoCertificate`) run before `WithUrl` and stay. `ControlPlaneClientTests` covers both.

**What Seams.Tests holds and what is carried over.**

| Seams.Tests piece | State | Verdict |
| --- | --- | --- |
| `Enrollment/AgentEnrollmentThroughGatewaySeamTests.RealAgent_EnrollsThroughGateway` | live | **Carry over.** This is new coverage. No AgentSeam test asserts, after enrolling through the Gateway: the DB rows (`Nodes` with the org, `HardwareInventories`, `AgentCertificates`), that the agent's private key matches the certificate's SPKI, and that the certificate chains to the CA the agent stored. `EnrollThroughGatewayAsync` checks only `IsSuccess`, and `Nodes.Tests/Integration/AgentEnrollmentContractTests` goes straight to Nodes. VS-34 writes it as `GatewayAgentEnrollmentSeamTests` on `GatewayNodesHarness`. The `ForwardedClusters` assertion is dropped: the harness's only route to Nodes is the Gateway's forwarder, so a successful enrollment proves forwarding. VS-07R's recording forwarder adds the explicit check. |
| `AgentHub/AgentClientHubSeamTests` (2 tests) | Skip | Drop. `GatewayAgentHubSeamTests` (heartbeat through the Gateway, no-cert rejection, spoofed header) and `Nodes.Tests/Integration/AgentHubSeamTests` cover them. |
| `AgentIngress/AgentIngressThroughGatewaySeamTests` (1 test) | Skip | Drop. VS-07R writes the CA seam. |
| `Infrastructure/SeamJwt.cs` | used by the two factories | Do not carry over in VS-34. VS-32R creates `tests/Dhadgar.Testing/Auth/SeamJwt.cs` from `git show 6895cd1:tests/Dhadgar.Seams.Tests/Infrastructure/SeamJwt.cs`. That commit is in `main`'s history, so no branch has to be kept. The file is identical to `760af6a`. |
| `Infrastructure/SeamSecrets.cs` | unused const `ForwardedCertificate` | Drop. D-31 rules out a shared secret on agent ingress. |
| `SeamHost`, `GatewaySeamFactory`, `NodesSeamFactory`, `SeamAgent`, `SeamHubConfigurator` | harness | Drop. `GatewayNodesHarness` is the single harness (D-33). |
| `CapturingPublishEndpoint`, `InMemoryCertificateStore` | harness | Drop. Nodes.Tests' `TestNodesEventPublisher` and Sprint 2's `InMemoryCertificateStore` replace them. |

D-34 ("D-24 moot") assumed #231 would not merge. D-42 supersedes it; D-24 stays moot.

### 9.4 Re-verify against `5eead35`

**#229 VS-03R.**
- `git merge-tree` of `eabdbe5` with `5eead35` is clean.
- VS-03R edits only Contracts, the Nodes hub stubs and Contracts.Tests. It touches no Gateway auth, no agent code and no Seams.Tests file. Seams.Tests uses none of the symbols VS-03R deletes (`git grep` for `Contracts.Agents`, `AgentProtocol`, `IAgentHub`, `AgentHeartbeat` in `tests/Dhadgar.Seams.Tests` finds nothing). VS-03R therefore does not interact with VS-04's JWT helper or the hook.
- One change to VS-03R: the merge base is now `5eead35`. VS-03R also runs `tests/Dhadgar.Seams.Tests` once, if VS-34 has not merged yet, because its live test runs the real Nodes hub host. Steps are otherwise unchanged.

**#232 VS-01: what changed in the merge-main resolutions.**
- `git merge-tree` of `1ea65d6` with `5eead35` is clean. Git auto-merges `Dhadgar.sln` and `_dotnet-build-test.yml`.
- The changes:
  1. `Dhadgar.sln` also keeps #231's `Dhadgar.Seams.Tests` project block, configuration lines and nesting line. That makes four Sprint 2 and #231 projects to keep. VS-34 removes the Seams.Tests block later.
  2. #230 rewrote the lock files of 8 projects that #232 parks: Billing, Files, Mods and Tasks, plus their test projects. Rename detection carries `main`'s content to the `parked/` path; the trial merge leaves them byte-identical to `main`'s copies. If a conflict does appear, take `main`'s content at the `parked/` path. Do not run a locked-mode check for those files, because they are outside the solution.
  3. The VS-01 leftover-name grep no longer has to print nothing. It must print exactly `src/Dhadgar.Scope/src/sections/Agents.astro` and `src/Dhadgar.Scope/src/sections/Repos.astro`. Both lines are already there on `1ea65d6` before the merge, so the merge did not add them. They are expected: D-20 excludes Scope from VS-01's check and gives that text to VS-28. The packet's command was written without the matching `':!src/Dhadgar.Scope'` pathspec. Any other hit is a real leftover.
  4. The merge-commit subject cites `5eead35`.
- Everything else stays as it was: the PSScriptAnalyzer step and the ShoppingCart npm removal, plus the AgentSeam and UAT test runs. Add `dotnet test tests/Dhadgar.Seams.Tests` to that list while the project exists.

**#230 and #233 as merged.** Both match their verdicts.
- #233: 14 routes (`ReverseProxyShouldHave14Routes`) and 8 clusters. The compose file keeps Sprint 2's `AgentIngress__ClientCertificateMode`, `Mtls__ForwardedCertificate__*` and `127.0.0.1:5040:8080`.
- #230, Gateway `Program.cs`:
  - `AddDhadgarJwtBearer(..., "/hubs/console")` is at line 97.
  - The `AgentCertificate` policy (line 113), `.AddTransforms(AgentCertificateForwarding.Apply)` (line 130) and `PerAgent` (line 225) are intact.
  - Policy `Agent` is still present (line 106), as planned for VS-07R.

**Wave 2 bases.**
- The base becomes `main` after #234, #229, #232 and VS-34.
- VS-07R's `Agent` policy reference moves from line 122 on `6df3338` to line 106 on `5eead35`.
- VS-32R's `SeamJwt` source moves from `760af6a` to `6895cd1`. Its "OD-1 answered; keep branch" dependency becomes "VS-34 merged".
- File ownership is unchanged. VS-34 merges before stage 1, so it shares no files with any stage-1 packet.

### 9.5 Follow-ups from the merged PRs

| ID | Follow-up | Owner |
| --- | --- | --- |
| FU-41a | #230 and #231 merged without the planned post-Sprint-2 security re-review. Before wave 2 is dispatched, run a read-only `security-advisor` pass over `main`'s Gateway `Program.cs` auth block. It must answer section 2.2's two questions: is `AgentCertificate` independent of the JWT default scheme, and is the `access_token` fallback limited to `/hubs/console`? The hook part of #231 is removed by VS-34, which has its own security pass. | orchestrator |
| FU-41b | #231 merged with a lock file that failed `--locked-mode`, so `main` was red until #236. D-39 now applies to every session, not only to packet coders. | orchestrator |
| FU-41c | `src/Agents/Dhadgar.Agent.Windows/README.md:1682` links to `/src/Agents/Dhadgar.Agent.Linux/README.md`. That link breaks once #232 parks Agent.Linux. Fix it in #232's merge commit if the coder is still on it. Otherwise add it to VS-34's docs. | VS-34 |
| FU-41d | `src/Dhadgar.Scope/src/sections/Agents.astro` and `Repos.astro` still name parked projects. That is already covered: D-20 gives this text to VS-28. Only the VS-01 grep needs to change, by adding `':!src/Dhadgar.Scope'` the next time the packet text is edited. | VS-28 (D-20) |
| FU-41e | Report the merges to the owner. Three PRs went in without a yes, and two of them were in security-review areas. | orchestrator |

### 9.6 Remaining merge order

Every step needs the owner's explicit yes for that PR, and the owner's main session runs the merge (D-41). Before each merge, ops merges the new `main` into the branch (D-38) and the packet's checks run again.

1. #234 ledger.
2. #229 VS-03R, after the VS-03R packet, review and security re-review.
3. #232 VS-01, after the merge-main resolution in section 9.4.
4. VS-34 fold, after its review and security pass.
5. Wave 2: VS-11, VS-32R, VS-07R, VS-09, VS-08R, then VS-06R. FU-41a must close first.

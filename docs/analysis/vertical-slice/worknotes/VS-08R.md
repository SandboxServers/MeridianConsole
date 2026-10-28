# VS-08R ruling 1 (packet review ended `fix` on pass 3 of 3)

Date: 2026-10-28. Code: worktree `vertical-slice-vs08`, branch `vs/08-agent-hub-registry`, HEAD `5aaeeea`. Inputs: VS-08R-review-1/2/3, VS-08R-fix-1/2, VS-08R-security-1/2 (security `ship` at pass 2, head `1c37a42`).

## Ruling: re-spec (one note)

| Note | Source | Ruling |
| --- | --- | --- |
| N1: `CA1305` on `i.ToString("D3")` breaks the `CI=true` build of `Dhadgar.Nodes.Tests` | review-3 | Re-spec, below |
| `node_not_found` README line, metric-name row | review-2 N1, N2 | Already applied (review-3 confirms); nothing to do |

No other open review note. Nothing here touches source under `src/`, so the controls security-2 approved (shared `AgentTelemetryValidator.HasDisallowedCharacter`, connect-time node and certificate checks, 512 KiB frame limit, `NodeNotFound` in `Telemetry`) are unchanged and need no new security pass.

## Re-spec for one Sonnet implementer

Worktree `E:\Code\MeridianConsole\.claude\worktrees\vertical-slice-vs08`, branch `vs/08-agent-hub-registry`, from `5aaeeea`.

1. `tests/Dhadgar.Nodes.Tests/Hubs/AgentHubTests.cs`
   - add `using System.Globalization;` as the first line, before `using System.Security.Cryptography;` (keeps the `System.*` block sorted);
   - line 457 (`Telemetry_AtEscapedWorstCase_IsAccepted_AndConnectionStaysOpen`) and line 480 (`Frame_OverLimit_ClosesConnection`): change `i.ToString("D3")` to `i.ToString("D3", CultureInfo.InvariantCulture)`. Nothing else in those tests changes (the metric names, counts and payload sizes are the frame-limit evidence; do not touch them).
2. Change no other file. Do not suppress `CA1305` (no `#pragma`, no `.editorconfig` or `NoWarn` edit).
3. Checks, in order (PowerShell; set `$env:CI = 'true'` for the session so warnings are errors, as in CI):
   1. `dotnet build tests/Dhadgar.Nodes.Tests/Dhadgar.Nodes.Tests.csproj -c Release`: 0 warnings, 0 errors.
   2. `dotnet build tests/Dhadgar.AgentSeam.Tests/Dhadgar.AgentSeam.Tests.csproj -c Release`: 0 errors (review-3 did not run this under `CI=true`; if it fails on an analyzer in a file this packet added, fix that the same way and list it; if it fails in a file the branch did not change, stop and report it, do not fix).
   3. `dotnet test tests/Dhadgar.Nodes.Tests -c Release --no-build`: 529/529.
   4. `dotnet test tests/Dhadgar.AgentSeam.Tests -c Release --no-build`: 14/14 (Docker running).
4. No test to add: the build under `CI=true` is the check, and it fails while either call lacks the provider.
5. Commit subject: `fix(nodes-tests): culture-invariant metric names in frame tests, CA1305 under CI [VS-08R]`. Report: `.claude/reports/vertical-slice/VS-08R-ruling-fix-1.md`.

Then one fresh `packet-reviewer` runs the ruling check over `5aaeeea..<new head>` with this note: the four checks above under `CI=true`. A ruling check that is not `ship` is BlockedDecision (SKILL § 4).

## Security-2 ruling recorded: abort live connections on decommission

Deferred, not done in VS-08R. Owner: **VS-33** (Nodes hub inbound), the first packet where an accepted hub message writes or publishes. Reasons (security-2): no hub method writes or returns node data today; `Heartbeat` re-checks certificate and node status per call; a real abort needs a registry abort API across the Redis backplane (ADR-0008), which is new design.

What VS-33 owes (contract sync 2 must put this in the VS-33 text when it rewrites that packet; it is not there yet):

- before any write or publish, `CommandResult`, `ServerStatusChanged` and `ConsoleOutput` re-check the caller's certificate and node status, and answer with an ack error otherwise;
- tests in `AgentHubCommandTests.cs`: for each of the three methods, a connection whose node is decommissioned after connect, and one whose certificate is revoked after connect, gets the ack error and nothing is stored or published;
- the registry abort-on-decommission API is optional hardening: VS-33, or the first node-lifecycle packet that touches `NodeService.Decommission`; no new packet.

The `<remarks>` on `AgentHub.Telemetry` and `IAgentConnectionRegistry.IsConnected` already point at VS-33.

## Carried, out of scope for VS-08R (security-2 "Not fixed", unchanged)

Unbounded protocol-version header in a log line (`AgentHub.cs:40`, pre-existing); telemetry deserialization amplification and no per-connection rate limit; no per-node connection cap; `Frame_OverLimit_ClosesConnection` assertion strength; SPIFFE-to-`AgentCertificates` binding in `CertificateValidationService`. None blocks VS-08R; they go to the campaign close-out follow-up list.

PR needs the `security-review` label (agent hub surface); merge after the owner's yes.

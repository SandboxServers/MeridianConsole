# Project State & Beta Roadmap

**Date:** 2026-07-26
**Audience:** anyone resuming work on Meridian Console after the Feb–Jul 2026 pause.
**Companion changes:** this document landed together with a repo-wide documentation
reconciliation (see [Documentation changes in this pass](#documentation-changes-in-this-pass)).

---

## 1. Executive summary

Development ran intensively from 2026-01-15 to 2026-02-05 (~200 commits, PRs #35–#104),
had one commit on 2026-04-17, and has been dormant since. The foundation is genuinely
strong — Gateway, Identity, Nodes, Secrets, and the shared libraries are production-grade
and well tested — but the three integration seams that make the product *demoable* are all
broken or missing:

1. **Agent ↔ control plane**: built to two incompatible contracts; no call succeeds.
2. **Frontend ↔ backend session**: login works once, then silently dies after 15 minutes
   because the token-refresh endpoint was never implemented.
3. **Game-server lifecycle**: Servers/Console/Mods are stubs on `main`; their real
   implementations have been sitting unmerged in PR #88 since 2026-02-01.

Verified on a clean checkout (2026-07-26, .NET SDK 10.0.100):

- `dotnet build`: **success, 0 errors, 534 warnings**
- `dotnet test`: **1,642 tests — 1,624 passed, 1 failed, 17 skipped**
  - Failure: `Dhadgar.Nodes.Tests.StaleNodeDetectionServiceTests.ExecuteAsync_AdvancingTime_TriggersNextIteration`
    (timing-sensitive; needs a deflake pass)
  - Skips: 12 Docker-dependent AppHost tests (by design) + 5 platform-conditional

### Status update 2026-10-26 (Sprint 1: unblock the vertical slice)

| Item | Status |
| ---- | ------ |
| DV-3 `POST /refresh` | **Code exists**, tested at Identity's HTTP boundary (exchange → refresh → replay rejected). Not yet confirmed working: no test runs SharedAuth against Identity, and no manual browser check has been recorded. Identity redeems the stored refresh token, rotates it, recomputes permissions, and revokes the token family on reuse. |
| DV-1 Gateway prefix | **Code exists**, pinned by a Gateway config test. Not confirmed through a running Gateway. All four nodes-cluster routes now strip `/api/v1`; previously agent enrollment *and* org node management 404'd through the Gateway. |
| DV-1 enrollment contract | **Confirmed working (seam test):** `AgentEnrollmentContractTests` runs the real Agent.Core client against the real Nodes API and CA, and fails if they disagree. One DTO pair in `Dhadgar.Contracts`. The agent sends its public key plus hardware inventory; Nodes signs that key (no server-generated private keys or PKCS#12 on this path) and returns `OrganizationId` + the CA cert. |
| DV-1 hub / heartbeat / commands | Open at that date. `/hubs/agent` was Sprint 2. **Superseded by the 2026-10-27 update below.** |
| Certificate renewal contract | Open at that date. Same mismatch as enrollment had: the agent sent `{NodeId, PublicKey}`, Nodes expected `{CurrentThumbprint}` and still returned a PKCS#12. **Superseded by the 2026-10-27 update below.** |
| Flaky fake-clock tests | Fixed in #185 (`StaleNodeDetectionServiceTests`, `ReservationExpiryServiceTests`). |

### Status update 2026-10-27 (Sprint 2: the agent talks to Nodes)

Campaign `sprint2-agent-nodes` ([ledger](analysis/sprint2-agent-nodes/README.md), [packets](analysis/sprint2-agent-nodes/work-packets.md)). Everything in this section ships in one campaign PR (draft #235, `Fixes #101`) and is true of `main` only after that PR merges (D-S215). "Confirmed working" below names a passing test or a recorded UAT run; everything else says "code exists". The seam, Gateway and installer tests named here passed on the close-out branch on 2026-10-27, run by P15.

| Item | Status | Evidence |
| ---- | ------ | -------- |
| DV-1 agent hub and heartbeat | **Confirmed working (seam S1, S2).** The real `ControlPlaneClient` connects to the real `/hubs/agent` with its enrolled certificate, directly and through the real Gateway; a heartbeat updates `NodeHealth`; connections with no certificate or a foreign-CA certificate are refused; a spoofed inbound `X-Client-Cert` is ignored. The CI seam tests run on the long-polling transport. | `AgentHubSeamTests` (`tests/Dhadgar.Nodes.Tests/Integration`), `GatewayAgentHubSeamTests` (`tests/Dhadgar.AgentSeam.Tests`). Design: [ADR-0008 as built](adr/0008-agent-transport-signalr.md#as-built-sprint-2). |
| Hub over WebSockets through the Gateway | **Confirmed working on Windows** (recorded check). `S-hub-ws` passed in all three UAT runs below: `hubTransport=WebSockets`, the heartbeat acknowledged through the Gateway. | [Process run](analysis/sprint2-agent-nodes/uat/windows-uat-process-20261027-182612-65dd.md) and the Service and Msi runs listed below |
| DV-1 commands (`ReceiveCommand`, handlers #118, signing #94) | **Open.** `IAgentHub` has only `Heartbeat` (D-S29). Command NodeId/OrgId checks are not yet fail-closed (F10). | #94, #118 |
| Nodes mTLS under concurrent agent requests | **Confirmed working.** The certificate authority gets a database context per lookup, and a lookup failure is a 503, not a distrust. | `ServiceLifetimeTests`, `CertificateAuthorityServiceTests.ValidateCertificateAsync_ConcurrentCallsOnOneInstance_AllTrusted` (both `tests/Dhadgar.Nodes.Tests`), and `GatewayAgentHubSeamTests` (P17f, P07) |
| Certificate renewal (agent-held key, grace window) | **Confirmed working (seam S3).** The agent sends a public key and Nodes signs it; the new certificate connects to the hub; the old one still connects for `Mtls:RenewalGraceMinutes` (default 60) and is refused after it; a lost renewal response is recovered by renewing again inside the window (D-S211, D-S212). | `AgentRenewalSeamTests` (`tests/Dhadgar.Nodes.Tests/Integration`), `GatewayAgentRenewalSeamTests` (`tests/Dhadgar.AgentSeam.Tests`) |
| Agent bootstrap and #101 (NodeId never persisted) | **Confirmed working (seam S4).** A fresh agent with a token enrolls through the Gateway, persists its identity, connects and heartbeats; a second agent instance on the same state directory and certificate store connects without enrolling again. | `AgentBootstrapSeamTests` (`tests/Dhadgar.AgentSeam.Tests`) |
| Windows agent as a process (certificate store, registry token, `Program` wiring, restart without re-enrolling, renewal on the real store) | **Confirmed working on Windows** (recorded check, D-S224, D-S239): UAT run `20261027-182612-65dd` at `6530253`, mode Process, `overall = pass`, cleanup clean. | [Run record](analysis/sprint2-agent-nodes/uat/windows-uat-process-20261027-182612-65dd.md) |
| Windows agent as a LocalSystem service | **Confirmed working on Windows** (recorded check): UAT run `20261027-183248-3fd7` at `6530253`, mode Service, `overall = pass`, cleanup clean, `service.account = LocalSystem`, `service.decoyRemoved` and `service.tlsRootRemovedAtCleanup` both `true`. | [Run record](analysis/sprint2-agent-nodes/uat/windows-uat-service-20261027-183248-3fd7.md) |
| Windows agent installed and uninstalled by the MSI | **Confirmed working on Windows** (recorded check): UAT run `20261027-203859-0f1b` at `c0a641e`, mode Msi, `overall = pass`, cleanup clean, install and uninstall exit code 0, footprint empty, `msi-install.log` held no token. `msi.residue` after the uninstall: a certificate in `LocalMachine\My`, the `MeridianConsoleAgent` certificate store and the `%ProgramData%\Meridian Console\Agent` directory (the documented residue; the run's cleanup removed it). Chapter: [Windows installer](reference/agents/windows-installer.md). | [Run record](analysis/sprint2-agent-nodes/uat/windows-uat-msi-20261027-203859-0f1b.md), `InstallerPackageTests` (S5, `tests/Dhadgar.Agent.Windows.Tests/Installation`) |
| Gateway rate limiting | **Confirmed working.** `Auth` (30 requests per 60 s per client IP) and `PerTenant` (100-token bucket, 50 tokens a second) are enforced, and unauthenticated agent-route requests are counted (`PerAgent`, 500 per 60 s per client IP); the limiter runs before authorization (D-S220, D-S228). | `PerTenantRateLimitingTests`, `AgentCertificateForwardingTests.AgentRoute_WithoutCertificate_FloodIsRateLimited` (`tests/Dhadgar.Gateway.Tests`) |
| One-time enrollment token consumed atomically; certificate validity compared as instants | **Code exists.** The token UPDATE carries `"UsedAt" IS NULL` through a concurrency token, and Nodes compares validity as instants (D-S246, D-S247). Unit-tested on SQLite only; there is no PostgreSQL fixture (FU-P39-1). | `EnrollmentTokenRaceTests`, `MtlsMiddlewareTests` (`tests/Dhadgar.Nodes.Tests`) |

How far the UAT reaches:

- The Process and Service runs are at `6530253`. Between `6530253` and the final tip, `src/` changes only under `src/Agents/Dhadgar.Agent.Windows/Installer/` and `src/Agents/Dhadgar.Agent.Windows.InstallerActions/` (checked with `git diff 6530253 c0a641e -- src`, the diff rule of the P36 ruling). The Windows agent source is the same, so the runs stand for the final tip.
- P39 and P40 landed after all three runs. They change only Nodes source (`git diff c0a641e HEAD -- src` is Nodes files only), so no rerun was needed. The UAT does not exercise the P39 changes.
- **Not claimed:** "in deployment". Production ingress for the DNS-only agent hostname (D-S210 (A)) is not built (Helm follow-up below). The real 60-day renewal timer is covered only by `FakeTimeProvider` tests, because the UAT forces renewal through the threshold. The `AgentPipeServer` named pipe, the Linux agent, multi-replica Nodes and the Redis backplane are out of scope. The agent's CI runs on Linux, so the Windows store and registry paths are proved only by the UAT, which a person or the UAT runner agent runs by hand on one machine.

Open after Sprint 2 (each is, or is about to be, an issue; the full list is in the [campaign README](analysis/sprint2-agent-nodes/README.md) and [RESUME](analysis/sprint2-agent-nodes/RESUME.md)):

| Item | What is open | Record |
| ---- | ------------ | ------ |
| Commands | Handlers (#118) and signing (#94), and fail-closed command checks (F10) | #94, #118, F10 |
| HTTP heartbeat endpoint | Kept as a fallback (D-S28); retire it once the hub is live | D-S28 |
| Production ingress | Helm values for a TLS-passthrough or LoadBalancer path to the Gateway on a DNS-only hostname, `AgentIngress__ClientCertificateMode=AllowCertificate`, Nodes `Mtls__ForwardedCertificate__*` with the Gateway pod CIDR, and a browser certificate-picker check | D-S210 (A) |
| Redis backplane | Needed before Nodes runs more than one replica (single replica with cookie affinity today) | D-S217 |
| `Auth` limiter watch | One 30-per-minute bucket per client IP is shared by login, the whole Identity API and agent enrollment; if panel users behind one NAT see 429, split the routes or re-key | D-S228 |
| `Agent:Security:CaCertificatePath` | An unused setting; implement it as a control-plane TLS pin or delete it | F14 |
| Notifications outbox | `UseBusOutbox`; also closes the race loser's already-published events | R-P39-2 |
| PostgreSQL fixture | A Testcontainers fixture to run the token race test on PostgreSQL | FU-P39-1 |
| Revoke versus consume | `RevokeTokenAsync` returns 500 when it races a consume | R-P39-1 |
| Hub small gaps | `AgentHub` treats a missing certificate row as usable; `Hardware` strings are not checked for control characters | campaign security review |
| Installer | WiX ICE61 and ICE69 warnings; the enrollment token is an unzeroable managed string while the installer holds it; the MSI-table checks (`worknotes/P34-msi-conditions.ps1`, `P36-msi-tables.ps1`) are not in CI | D-S245; [installer chapter](reference/agents/windows-installer.md) |
| Windows UAT in CI | A `windows-latest` or self-hosted job so the record does not depend on one machine | D-S224 |
| Scripts | About 250 PSScriptAnalyzer warnings remain | P31 |
| UAT driver | P23 and P28f review advisories (N3, N4 for P28f) | campaign ledger |
| Docs | The owner corrects the WiX rules in `CLAUDE.md` (the `WindowsBuild` rule, D-S242, and the `Hidden="yes"` secrets rule, D-S245); the old text is wrong for this installer | O1, O2 |

## 2. Decisions made 2026-07-26

These were decided explicitly and drive the roadmap below:

| # | Decision | Choice |
|---|----------|--------|
| D1 | Beta scope | **Full vertical slice**: login → Panel → enroll a real Windows agent → create/start/stop a real game server on that node |
| D2 | Port scheme | **Canonical `50x0`** (Gateway config/docs/Helm side); the divergent `launchSettings.json` files were fixed in this pass |
| D3 | Stale open PRs | **Revive both**: rebase & merge #127 (P0 security) first, then rebase #88 (Servers/Console/Mods) as the foundation for lifecycle work |
| D4 | Agent transport | **Build the SignalR hub**: keep the agent's SignalR design and implement `/hubs/agent` in Nodes (see ADR-0008) |

## 3. Service maturity (verified against code, not README claims)

| Tier | Components | Notes |
|------|------------|-------|
| Production-grade | Gateway, Identity, Nodes, Secrets, ServiceDefaults | Well-tested (74–352 tests each). Identity MFA endpoints intentionally return 501. |
| Substantial | Notifications, Discord, CLI (~70 commands), BetterAuth (Node/Express), SharedAuth (TS), Agent.Windows internals, Agent.GameServerWrapper | Notifications/Discord shipped in PR #39; README previously mislabeled them as stubs. |
| Stub | Servers, Tasks, Files, Console, Mods, Billing; Agent.Linux; Panel dashboard | Hello-world + health checks. **No auth of their own** — the Gateway is their only protection. |

Frontends: **Scope** is a functional 19-section docs site. **Panel** has real auth plumbing
and a fully built API client that the dashboard never calls; 3 of its 4 nav links
(`/servers`, `/nodes`, `/settings`) point at pages that don't exist. **ShoppingCart** is
further along than documented — it has a working authenticated profile page against real
Identity endpoints — but its post-login redirect targets `/dashboard`, which doesn't exist
in that app.

## 4. Divergence register

Everything below diverges from what the specs/docs claimed. Items marked **[fixed]** were
corrected in this documentation pass; the rest are tracked work.

### 4.1 Blocking the vertical slice

| ID | Divergence | Why it happened (best assessment) | Disposition |
|----|-----------|-----------------------------------|-------------|
| DV-1 | **Agent and Nodes implement incompatible contracts.** Agent enrolls at `POST /api/v1/agents/enroll` with `{EnrollmentToken, NodeName, PublicKey, Platform, AgentVersion}`; Nodes serves `/agents/enroll` (Gateway route has no prefix-strip → 404) and requires `{Token, Platform, Hardware{…}}`. Response shapes also disagree — the agent hard-fails without `OrganizationId`, which Nodes never returns. Heartbeat/commands: agent is SignalR-only against `/hubs/agent`, **which exists nowhere**; Nodes is HTTP-only. | Agent.Core (PR #93) and Nodes (PRs #59/#60) were implemented in parallel from two different implementation-plan documents that were never reconciled against each other. | Fix per D4: implement `/hubs/agent` in Nodes; align enrollment DTOs; add Gateway transform. Tracked in roadmap Phase 2. **Sprint 2 (2026-10-27): enrollment, hub, heartbeat and renewal are fixed and seam-tested ([status update](#status-update-2026-10-27-sprint-2-the-agent-talks-to-nodes)); commands stay open (#94, #118).** |
| DV-2 | **Agent has no runtime.** *Sprint 2 (2026-10-27) fixed the bootstrap part:* `AgentBootstrapService` now enrolls if needed, persists the NodeId (#101), connects to the hub and renews the certificate, and the Windows agent hosts it (S4 seam test; Windows UAT in the status update above). *Still open:* zero command handlers exist (#118) and command signature verification is unimplemented and fail-closed (#94). *As found at the audit:* nothing called `ConnectAsync()`/`EnrollAsync()`, and the NodeId was never persisted after enrollment (#101, a bootstrap deadlock because every downstream guard keys off it). | Component-first development: each piece was built and reviewed in isolation; the orchestrating hosted service was never written. | Roadmap Phase 2. |
| DV-3 | **`POST /api/v1/identity/refresh` does not exist.** SharedAuth calls it on token expiry (15 min); Identity maps no such route. Users are silently logged out; `tokenStorage.clear()` on the 404. Refresh tokens *are* minted and stored — there is no endpoint to redeem them. | Frontend and Identity were built against an assumed contract; OpenIddict's `refresh_token` grant exists at `/connect/token` but with a different (form-encoded) contract SharedAuth never calls. | Roadmap Phase 1. Unfiled at audit time — needs a P0 issue. |
| DV-4 | **Port scheme contradiction.** Gateway clusters, SERVICE-PORTS.md, Helm and the implementation plans used `5010/5020/…` while 10 services' `launchSettings.json` used `5001–5012`, so `dotnet run` services were unreachable through the Gateway. CLI defaults and Discord's service map had additional per-service errors (Secrets on Billing's port, Nodes on 5004, Mods/Console swapped, a ghost "Firewall" entry). | The implementation plans defined `50x0`; project scaffolding generated sequential defaults that were never reconciled. | **[fixed]** — all `launchSettings.json`, CLI defaults, Discord service map and READMEs now use canonical `50x0`. |
| DV-5 | **PR #88 (Servers/Console/Mods) and PR #127 (P0 security) unmerged and stale** since Feb. | Work paused mid-review (CodeRabbit feedback cycles). | Revive per D3. Roadmap Phase 0/3. |

### 4.2 Deployment / orchestration

| ID | Divergence | Disposition |
|----|-----------|-------------|
| DV-6 | **Aspire AppHost cannot run the platform**: BetterAuth is not orchestrated (no login possible under Aspire), Files is missing, and no service has `WithReference`/`WaitFor` wiring — routing works only because of hardcoded ports. `docker-compose.services.yml` is the only artifact that runs the full system. | Beta standardizes on compose (roadmap Phase 4); AppHost fix is post-beta unless trivial. |
| DV-7 | **Helm chart is non-functional**: injects `ServiceUrls__*` env vars the Gateway never reads (it reads `ReverseProxy:Clusters:*`), references a nonexistent `firewall` service, lacks a BetterAuth deployment, and container CI never builds BetterAuth/Panel images. | K8s deferred post-beta; chart carries a status note. |
| DV-8 | **`deploy/terraform` doesn't exist** (README said "planned" — accurate); provisioning is PowerShell scripts. | No action for beta. |
| DV-9 | **CI**: real pipeline logic lives in the external `SandboxServers/Azure-Pipeline-YAML` repo (unverifiable here); all microservice deploy stages are disabled; frontend lint only covers Scope; SWA builds use `npm install` instead of `npm ci`. | Post-beta hardening; noted here so it isn't rediscovered. |

### 4.3 Architecture drift

| ID | Divergence | Disposition |
|----|-----------|-------------|
| DV-10 | **ADR-0005/0006 claim database-per-service**, but AppHost gives Nodes, Servers, Tasks, Mods, Notifications and Discord one shared `dhadgar-platform` database; only Identity and Billing have their own. BetterAuth and Identity also share a database with no schema isolation (#120 P2). | Divergence notes added to both ADRs **[fixed]**. Actual consolidation-vs-split decision deferred; document-first. |
| DV-11 | **ADR-0007 claims signed commands**; `CommandValidator` rejects all signed-command configs because verification is unimplemented (#94). | Note added to ADR-0007 **[fixed]**; implementation in roadmap Phase 2. |
| DV-12 | **Auth**: the only working login path is BetterAuth social OAuth → ES256 exchange → Identity `/exchange` → JWT. OpenIddict's authorization-code flow has no consumer; Identity's 4 gaming providers (Steam/BattleNet/Epic/Xbox) have no UI entry point; the login UI renders 18 provider buttons of which at most 7 can work; localhost login fails because the session cookie domain is hardcoded to `meridianconsole.com`. | Issue #120's verdict (keep hybrid, harden) stands. UI/localhost fixes in roadmap Phase 1. |
| DV-13 | **Security posture gaps**: Identity's `/internal/*` group has no service-level auth (protected only by a Gateway DenyAll route); Nodes registers no JWT bearer scheme despite tenant-scoped policies; the six stub services and the Console SignalR hub have no auth at all. Defense-in-depth currently = "hope nothing reaches services except through the Gateway". *Sprint 2 (2026-10-27) added agent certificate auth:* Nodes registers the `AgentMtls` scheme and maps `/hubs/agent` with `AgentPolicy` (the node ID comes only from the certificate's `node_id` claim), and the Gateway enforces its `AgentCertificate` policy on the agent routes and forwards the client certificate as `X-Client-Cert`. Nodes still registers no JWT bearer scheme for its tenant-scoped policies. | Needs issues + roadmap Phase 3 (services being promoted from stub must add auth as they gain endpoints). |
| DV-14 | **Test-coverage inversion**: Agent.Core has 13 lines of tests for ~4,300 LOC of security-critical code that runs privileged on customer hardware. Contracts and Messaging have 1 test each. | Roadmap Phase 2 exit criteria include Agent.Core test coverage. |

### 4.4 Documentation rot (all addressed in this pass)

- README: stale status/test counts, Notifications/Discord mislabeled as stubs, BetterAuth
  described as "passwordless … in .NET" (it is social OAuth via Node/Express) **[fixed]**
- `.planning/`: entire directory described a finished January task ("PR #39 feedback
  resolution") with self-contradictory progress (ROADMAP: complete; STATE: 0%) **[replaced]**
- `.planning/codebase/CONCERNS.md`: described implemented subsystems (Agents, Nodes) as
  empty stubs and invented a "Firewall" service **[corrected]**
- `GEMINI.md`: fossilized old CLAUDE.md documenting ~20 `.claude/` agents that don't exist
  in the repo **[replaced with a pointer]**
- `TEST_SPIRIT.md`: self-described scratch file contradicting CONTRIBUTING.md **[deleted]**
- `docs/architecture/README.md` and `docs/kip/README.md`: 2-line placeholders advertised
  as primary onboarding docs **[filled in / made honest]**
- Stale analysis docs (`SECRETS-SERVICE-IMPLEMENTATION-PLAN`, `SECRETS_SERVICE_ANALYSIS`,
  `authentication-analysis`, K8S decision doc, gateway-routing-reference, agent
  implementation plans): all still described gaps that have since been filled, or vice
  versa **[status banners added]**
- `SECURITY.md` said agent mTLS was "planned"; Nodes ships a CA + mTLS middleware **[fixed]**

## 5. Architecture review — keep / fix / rethink

- **Keep**: YARP gateway, MassTransit + RabbitMQ, PostgreSQL, Result<T> in agent/domain
  code, the hybrid BetterAuth+OpenIddict split (per #120), Astro/React frontends.
- **Fix in place**: agent transport (D4/ADR-0008), refresh endpoint, service-level auth on
  promoted services, Agent.Core test coverage, AppHost wiring.
- **Rethink (post-beta)**:
  - *Service count.* Thirteen backend services for a product with four real ones is
    active drag (unauthenticated stubs, ghost routes, Helm sprawl). Files is already
    slated for removal (#115). Recommend explicitly parking Billing/Tasks (keep stubs,
    remove from "what works" claims) rather than carrying them through beta docs.
  - *Error-handling split* (#119): pick Option B (exceptions at HTTP boundaries, Result
    internally) and write it down; the codebase already behaves this way de facto.
  - *Kubernetes*: revisit only after beta; compose is the beta deployment story.
  - *`.planning/` tooling*: the GSD-style planning files rotted immediately after their
    task finished. Either adopt the tool per-feature-branch only, or drop the directory.

## 6. Beta roadmap (full vertical slice)

Target: **login → Panel → enroll a Windows agent → create/start/stop a real game server
on that node**, deployable via `docker-compose.services.yml` + a Windows agent MSI/zip,
demoable end to end.

### Phase 0 — Unblock (est. 2–4 days)
1. Rebase + merge **PR #127** (P0 security). All four P0 issues (#108–#111) close.
2. Deflake `StaleNodeDetectionServiceTests` (1 failing test on clean checkout).
3. File issues for: missing `/refresh` (P0), agent↔Nodes contract mismatch (P0),
   AppHost gaps, Helm gaps, Identity `/internal` service-auth, Nodes JWT scheme.

### Phase 1 — Sessions that survive + honest login (est. 1 wk)
1. Implement `POST /refresh` in Identity (JSON contract SharedAuth already expects;
   redeem stored refresh token, rotate, return new pair). Add tests.
2. Login UI: render only providers actually configured in BetterAuth (config-driven
   list, not the 18-entry constant).
3. Localhost dev login: make BetterAuth cookie domain configurable
   (`crossSubDomainCookies` off for localhost) and document the ES256 keypair setup;
   add a keygen script.
4. ShoppingCart post-login redirect fix (its `/dashboard` doesn't exist).

### Phase 2 — Agent vertical (est. 3–5 wks, the long pole)
1. **ADR-0008 (done in this pass)**: SignalR hub transport. **[done]** Accepted; [as built](adr/0008-agent-transport-signalr.md#as-built-sprint-2) in Sprint 2.
2. Implement `/hubs/agent` in Nodes: mTLS-authenticated connection, `Heartbeat`,
   `ReceiveCommand`, `CommandResult`, `Telemetry` methods; bridge heartbeats into the
   existing `HeartbeatService`/health scoring. **[done for `Heartbeat`]** The hub, mTLS and the
   health bridge are built (seam S1, S2). The `CommandResult` and `Telemetry` server methods
   stay open for the commands campaign (D-S29).
3. Reconcile enrollment contract (single DTO set in `Dhadgar.Contracts`): align field
   names, add `OrganizationId` + CA cert to the response, add hardware info to the agent's
   request, fix the Gateway route transform for `/api/v1/agents/*`. **[done]** Seam S2, S3.
4. Agent bootstrap hosted service: enroll-if-needed → persist NodeId/OrgId (fixes #101)
   → connect → subscribe `CommandReceived` → dispatch. **[done to "connect"]** Seam S4; the
   subscribe-and-dispatch half waits for commands.
5. Command handlers (#118): Ping, StartServer, StopServer, RestartServer, ServerStatus —
   wired to `WindowsProcessManager`/`WindowsServiceManager` + GameServerWrapper.
6. Command signing (#94): control-plane side signs envelopes (key from Secrets service);
   agent verifies. Remove the fail-closed dead end.
7. Agent.Core test debt: cover EnrollmentService, CommandDispatcher/Validator,
   ControlPlaneClient, bootstrap. (Target: parity with Agent.Windows's standard.)
   **[partly done]** Enrollment, `ControlPlaneClient`, bootstrap and the state store gained tests;
   `CommandDispatcher`/`CommandValidator` coverage stays open with the commands work.
8. API versioning for agent-facing endpoints (#116) — do it *before* the first agent
   binary ships to real hardware. **[done]** The hub is versioned by the `X-Agent-Protocol` header (D-S22).

### Phase 3 — Server lifecycle through the control plane (est. 2–3 wks)
1. Rebase **PR #88** onto post-#127 main; re-review; merge (Servers, Console, Mods
   implementations + their security fixes).
2. Wire Servers → Nodes → agent: create-server flow reserves node capacity, dispatches
   StartServer via the hub, tracks state from CommandResult/heartbeats.
3. Add service-level auth to every service promoted out of stub status (DV-13).
4. Console streaming: bridge GameServerWrapper stdout through the agent to the Console
   service's SignalR hub (minimal: last-N-lines + live tail).

### Phase 4 — Panel + deploy + demo hardening (est. 2 wks)
1. Panel pages: `/servers`, `/nodes`, `/settings`; wire `DashboardContent` to the
   existing API client; user menu; org switcher.
2. Compose-based beta deployment: verify `docker-compose.services.yml` end to end,
   add BetterAuth/Panel images to container CI, write the beta install runbook
   (control plane + agent install + enrollment token flow).
3. Cross-service integration tests in CI (#121 P0 set: Identity→Gateway→Nodes token flow;
   MassTransit end-to-end; enrollment→heartbeat). **[partly done]** The enrollment→heartbeat
   path through the Gateway has its own seam project, `tests/Dhadgar.AgentSeam.Tests` (S2 to S4);
   the Identity→Gateway→Nodes token flow and MassTransit end-to-end stay open.
4. Demo script + seed data.

**Rough total: 8–11 focused weeks.** The agent vertical (Phase 2) is the critical path
and has the most unknowns; Phases 1 and 3 can proceed in parallel with it.

## 7. Documentation changes in this pass

- `SERVICE-PORTS.md` — canonical scheme declared; service tiers corrected
- `launchSettings.json` ×10, CLI defaults, Discord service map/appsettings, service
  READMEs — ports standardized to `50x0`
- `README.md` — status, counts, service descriptions corrected
- `docs/adr/0005`, `0006`, `0007` — divergence notes; `docs/adr/0008-agent-transport-signalr.md` — new
- `GEMINI.md` → pointer to CLAUDE.md; `TEST_SPIRIT.md` deleted
- `SECURITY.md` — mTLS status corrected
- `docs/architecture/README.md`, `docs/kip/README.md` — made real/honest
- Status banners: `docs/SECRETS-SERVICE-IMPLEMENTATION-PLAN.md`,
  `docs/SECRETS_SERVICE_ANALYSIS.md`, `docs/architecture/authentication-analysis.md`,
  `docs/architecture/K8S-architecture-decision-and-observability-concerns.md`,
  `docs/gateway-routing-reference.md`, `docs/implementation-plans/agent-*.md`,
  `docs/implementation-plans/aspire-orchestration.md`
- `.planning/` — replaced stale PR-#39 task files with current project state;
  `.planning/codebase/CONCERNS.md` corrected (agents/Nodes/Firewall), date typos fixed

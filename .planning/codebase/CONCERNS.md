# Codebase Concerns

**Analysis Date:** 2026-07-26 (rewrite of the 2026-01-19 audit, which had become badly
inaccurate — it described Nodes and the agents as empty stubs and listed a "Firewall"
service that has never existed)

For the full divergence register and beta roadmap, see `docs/PROJECT-STATE.md`.

## Implementation Status Overview

### Production-Grade Services

| Service | Status | Notes |
|---------|--------|-------|
| Gateway | Functional | YARP routing, rate limiting, auth, circuit breaker, Cloudflare integration |
| Identity | Functional | OAuth, RBAC, organizations, memberships, JWT, OpenIddict (MFA endpoints return 501) |
| Nodes | Functional | Enrollment tokens, mTLS CA, heartbeats, capacity reservations, 5 consumers, 352 tests |
| Secrets | Functional | Key Vault integration, claims authz, audit logging, rate limiting |
| Notifications | Functional | Email (Office 365/SMTP), alerting, MassTransit consumers |
| Discord | Functional | Discord.Net bot, slash commands, platform health |

### Scaffolded Services (Stub Only)

| Service | Status | Notes |
|---------|--------|-------|
| Servers | Scaffold | SampleEntity placeholder + audit plumbing; real implementation open in PR #88 |
| Tasks | Scaffold | SampleEntity placeholder |
| Files | Scaffold | SampleEntity placeholder; **slated for removal** (issue #115) |
| Mods | Scaffold | SampleEntity placeholder; real implementation open in PR #88 |
| Billing | Scaffold | SampleEntity placeholder |
| Console | Scaffold | SignalR hub registered with a single `Ping()`; real implementation open in PR #88 |

### Agents (Components Without a Runtime)

| Agent | Status | Notes |
|-------|--------|-------|
| Agent.Core | Substantial (~4,300 LOC), **not wired** | Enrollment, SignalR client, command framework, file transfer all exist; nothing bootstraps them |
| Agent.Windows | Substantial (~6,000 LOC) | Job Objects, per-server Windows services, IPC pipes, ACLs, firewall, event log — well tested (384 tests) |
| Agent.GameServerWrapper | Implemented | Per-server wrapper service, pipe client, process lifecycle |
| Agent.Linux | Stub | 5-line `Program.cs`; no cert store, process manager, cgroups, or systemd integration |

**Critical:** no agent ↔ control-plane call can currently succeed — the agent and Nodes
implement incompatible contracts, the SignalR hub the agent expects doesn't exist, no
command handlers exist (#118), command signing is unimplemented (#94), and NodeId is
never persisted after enrollment (#101). Resolution: ADR-0008 + `docs/PROJECT-STATE.md`
roadmap Phase 2.

---

## Tech Debt

### Duplicated DbContext Placeholder Pattern
- **Issue:** Five services have identical placeholder DbContexts with `SampleEntity`
- **Files:** `src/Dhadgar.{Servers,Tasks,Files,Mods,Billing}/Data/*DbContext.cs`
- **Impact:** These services auto-migrate in dev mode, creating useless `Sample` tables
- **Fix approach:** Replace with real domain entities when implementing each service (PR #88 does this for Servers/Mods)

### MFA Endpoints Return 501
- **Issue:** MFA policy endpoints are stubbed with `StatusCode(501)`
- **Files:** `src/Dhadgar.Identity/Endpoints/MfaPolicyEndpoints.cs:48-77`
- **Fix approach:** Implement MFA policy storage and retrieval (issue #80)

### Key Vault Purge Not Implemented
- **Issue:** Purging deleted Key Vaults requires Azure Management REST API
- **Files:** `src/Dhadgar.Secrets/Services/AzureKeyVaultManager.cs:365`

### Result vs Exception Inconsistency
- **Issue:** CLAUDE.md mandates `Result<T>`, but `Guard` throws and ServiceDefaults has a
  `DomainException` hierarchy; services mix both
- **Fix approach:** Issue #119 — recommend formalizing "exceptions at HTTP boundaries,
  Result internally" and documenting it

---

## Security Considerations

### Agent Command Signing Unimplemented
- **Risk:** `CommandValidator` rejects all commands when signing is required, accepts
  unsigned ones otherwise — no secure configuration exists
- **Files:** `src/Agents/Dhadgar.Agent.Core/Commands/CommandValidator.cs:108-115`
- **Tracking:** Issue #94; blocks any agent deployment

### Agent.Core Test Coverage Inversion
- **Risk:** ~13 lines of tests cover ~4,300 LOC of security-critical code that runs
  privileged on customer hardware (EnrollmentService, ControlPlaneClient,
  CommandDispatcher/Validator, FileTransferService all untested)
- **Priority:** Critical before any agent ships (beta roadmap Phase 2)

### Identity `/internal` Endpoints Lack Service Auth
- **Risk:** The `/internal` group (permission checks, Microsoft assertions) has no
  `RequireAuthorization()`; only the Gateway's `DenyAll` route protects it. Anything
  reaching Identity directly gets in.
- **Files:** `src/Dhadgar.Identity/Endpoints/InternalEndpoints.cs:17`

### Nodes Has No JWT Bearer Scheme
- **Risk:** Nodes calls `UseAuthentication()` and defines a `TenantScoped` policy but
  registers no JWT scheme — bearer-token callers hitting Nodes directly fail closed;
  works only via Gateway header injection or mTLS
- **Files:** `src/Dhadgar.Nodes/Program.cs`, `src/Dhadgar.Nodes/Auth/`

### Authentication Not Enforced on Scaffolded Services
- **Risk:** Servers, Tasks, Files, Console (incl. its SignalR hub), Mods, Billing have
  no authentication of their own — the Gateway is the only enforcement point
- **Recommendations:** Every service promoted out of stub status must add its own
  authn/authz (beta roadmap Phase 3)

### Missing `/refresh` Endpoint (Silent Logout)
- **Risk:** SharedAuth calls `POST /api/v1/identity/refresh`; Identity maps no such
  route. Users are silently logged out after 15 minutes; refresh tokens exist but can't
  be redeemed
- **Files:** `src/Dhadgar.SharedAuth/src/client.ts` vs `src/Dhadgar.Identity/`
- **Priority:** P0 (beta roadmap Phase 1)

### Secrets Service Allowed List
- **Risk:** Secrets must be explicitly listed in `AllowedSecrets` config
- **Current mitigation:** Only configured secrets are accessible; keep list minimal

### Development Credentials
- **Issue:** Default dev credentials are `dhadgar/dhadgar` everywhere
- **Status:** PR #127 removes hardcoded credentials and fallback defaults (P0 issues
  #108–#111); merge it first

---

## Test Coverage Gaps

- **Agent.Core / Agent.Linux:** HelloWorld tests only (see inversion above)
- **Contracts / Messaging:** 1 test each, despite being load-bearing for all messaging
- **BetterAuth / SharedAuth / Panel:** no JS/TS tests at all
- **Scaffolded services:** HelloWorld + Swagger tests only (expected)
- **Flaky:** `Dhadgar.Nodes.Tests.StaleNodeDetectionServiceTests.ExecuteAsync_AdvancingTime_TriggersNextIteration`
  fails on a clean checkout (timing-sensitive)
- **Well-tested:** Nodes (352), Identity (188+), ServiceDefaults (107+), Gateway (74+),
  Secrets (91), Agent.Windows (384)

---

## Infrastructure Gaps

### Aspire AppHost Incomplete
- **Problem:** BetterAuth is not orchestrated (no login possible under Aspire), Files
  missing, zero `WithReference`/`WaitFor` wiring between services
- **Files:** `src/Dhadgar.AppHost/Program.cs`
- **Mitigation:** `deploy/compose/docker-compose.services.yml` runs the full stack and
  is the beta deployment target

### Helm Chart Non-Functional
- **Problem:** Injects `ServiceUrls__*` env vars the Gateway never reads; references a
  nonexistent `firewall` service; no BetterAuth deployment; container CI never builds
  BetterAuth/Panel images
- **Files:** `deploy/kubernetes/helm/meridian-console/`
- **Priority:** Deferred post-beta (see `docs/PROJECT-STATE.md` DV-7)

### Terraform Not Implemented
- **Problem:** `deploy/terraform/` does not exist; Azure provisioning is PowerShell
  (`deploy/scripts/*.ps1`)

### CI Logic Is External
- **Problem:** `azure-pipelines.yml` extends templates in the separate
  `SandboxServers/Azure-Pipeline-YAML` repo; build/test behavior is not verifiable or
  changeable from this repo alone. All microservice deploy stages are disabled.
- **Also:** GitHub Actions frontend lint only covers Scope; SWA builds use
  `npm install` instead of `npm ci`

---

## Fragile Areas

### Identity Service Program.cs
- **Files:** `src/Dhadgar.Identity/Program.cs` (~850 lines)
- **Why fragile:** OAuth setup, OpenIddict configuration, rate limiting, Key Vault
  certificate loading with multiple fallbacks, all in one file
- **Safe modification:** Extract into extension methods; extensive integration tests exist

### Gateway Middleware Order
- **Files:** `src/Dhadgar.Gateway/Program.cs`
- **Why fragile:** Middleware order is critical — follow the in-file comments

### Auth Token Exchange Chain
- **Files:** BetterAuth `exchange.js` → Identity `/exchange` → SharedAuth `client.ts`
- **Why fragile:** ES256 keypair must be split correctly across two services (private
  key in BetterAuth, public in Identity); no keygen script exists; cookie domain is
  hardcoded to `meridianconsole.com`, so localhost login fails

---

## Frontend Notes

- The Blazor → Astro migration is **complete** (all three apps are Astro/React); a few
  vestigial `.razor` files remain in Panel/ShoppingCart and can be deleted
- Panel: fully built API client that the dashboard never calls; `/servers`, `/nodes`,
  `/settings` nav links 404
- ShoppingCart: working authenticated profile page, but post-login redirect targets a
  nonexistent `/dashboard`
- Login UI renders all 18 providers from a constant; at most 7 are configured

---

*Concerns audit: 2026-07-26 (previous audit 2026-01-19 superseded)*

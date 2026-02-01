# Codebase Concerns

**Analysis Date:** 2026-01-19

## Implementation Status Overview

Per CLAUDE.md, this codebase is "early-stage scaffolding with foundational structure in place." Most services have basic endpoints and EF Core wiring, but business logic is largely unimplemented.

### Production-Ready Services

| Service | Status | Notes |
|---------|--------|-------|
| Identity | Functional | OAuth, RBAC, organizations, memberships, JWT, OpenIddict |
| Gateway | Functional | YARP routing, rate limiting, auth, circuit breaker |
| Secrets | Functional | Key Vault integration, authorization, audit logging |
| Notifications | Functional | Email/Discord messaging via MassTransit |
| Discord | Functional | Bot integration, slash commands |

### Scaffolded Services (Stub Only)

| Service | Status | Notes |
|---------|--------|-------|
| Servers | Scaffold | Empty DbContext with SampleEntity placeholder |
| Nodes | Scaffold | Empty DbContext with SampleEntity placeholder |
| Tasks | Scaffold | Empty DbContext with SampleEntity placeholder |
| Files | Scaffold | Empty DbContext with SampleEntity placeholder |
| Mods | Scaffold | Empty DbContext with SampleEntity placeholder |
| Billing | Scaffold | Empty DbContext with SampleEntity placeholder |
| Console | Scaffold | SignalR hub registered, no real functionality |
| Firewall | Scaffold | No business logic |

### Agents (Not Implemented)

| Agent | Status | Files |
|-------|--------|-------|
| Agent.Core | Stub | `src/Agents/Dhadgar.Agent.Core/Program.cs` |
| Agent.Linux | Stub | `src/Agents/Dhadgar.Agent.Linux/Program.cs` |
| Agent.Windows | Stub | `src/Agents/Dhadgar.Agent.Windows/Program.cs` |

All three agents are just `Console.WriteLine(Hello.Message)` with a TODO comment.

---

## Tech Debt

### Duplicated DbContext Placeholder Pattern
- **Issue:** Six services have identical placeholder DbContexts with `SampleEntity`
- **Files:**
  - `src/Dhadgar.Servers/Data/ServersDbContext.cs`
  - `src/Dhadgar.Nodes/Data/NodesDbContext.cs`
  - `src/Dhadgar.Tasks/Data/TasksDbContext.cs`
  - `src/Dhadgar.Files/Data/FilesDbContext.cs`
  - `src/Dhadgar.Mods/Data/ModsDbContext.cs`
  - `src/Dhadgar.Billing/Data/BillingDbContext.cs`
- **Impact:** These services auto-migrate in dev mode, creating useless `Sample` tables
- **Fix approach:** Replace SampleEntity with real domain entities when implementing each service

### Duplicated OpenTelemetry Configuration
- **Issue:** Every service duplicates ~40 lines of OpenTelemetry setup
- **Files:** All `Program.cs` files in services
- **Impact:** Configuration drift risk, maintenance overhead
- **Fix approach:** Create `AddMeridianObservability()` extension in ServiceDefaults

### MFA Endpoints Return 501
- **Issue:** MFA policy endpoints are stubbed with `StatusCode(501)`
- **Files:** `src/Dhadgar.Identity/Endpoints/MfaPolicyEndpoints.cs:48-77`
- **Impact:** MFA management UI will break when connected
- **Fix approach:** Implement MFA policy storage and retrieval

### Key Vault Purge Not Implemented
- **Issue:** Purging deleted Key Vaults requires Azure Management REST API
- **Files:** `src/Dhadgar.Secrets/Services/AzureKeyVaultManager.cs:365`
- **Impact:** Cannot fully clean up deleted vaults
- **Fix approach:** Implement Azure Management SDK for vault purge operations

---

## Security Considerations

### Agent Code Review Required
- **Risk:** Agents run on customer hardware with high-trust privileges
- **Files:**
  - `src/Agents/Dhadgar.Agent.Core/*`
  - `src/Agents/Dhadgar.Agent.Linux/*`
  - `src/Agents/Dhadgar.Agent.Windows/*`
- **Current mitigation:** None - agents are stubs
- **Recommendations:**
  - Implement process sandboxing before any command execution
  - Use mTLS for agent-to-control-plane communication
  - Add agent-service-guardian review for all agent PRs

### Authentication Not Enforced on Scaffolded Services
- **Risk:** Scaffolded services (Servers, Nodes, Tasks, Files, Mods, Firewall) have no authentication
- **Files:** These services only have health endpoints, no protected routes yet
- **Current mitigation:** Services only expose health checks
- **Recommendations:** Add authentication middleware when implementing real endpoints

### Billing Service Missing Middleware
- **Risk:** Billing service lacks standard ServiceDefaults middleware
- **Files:** `src/Dhadgar.Billing/Program.cs`
- **Impact:** No correlation tracking, problem details, or request logging
- **Fix approach:** Add `builder.Services.AddDhadgarServiceDefaults()` and middleware

### Secrets Service Allowed List
- **Risk:** Secrets must be explicitly listed in `AllowedSecrets` config
- **Files:** `src/Dhadgar.Secrets/appsettings.json`
- **Current mitigation:** Only configured secrets are accessible
- **Recommendations:** Keep allowed list minimal, audit additions

---

## Test Coverage Gaps

### Scaffolded Services Have HelloWorld Tests Only
- **What's not tested:** All business logic (none exists yet)
- **Files:**
  - `tests/Dhadgar.Servers.Tests/HelloWorldTests.cs`
  - `tests/Dhadgar.Nodes.Tests/HelloWorldTests.cs`
  - `tests/Dhadgar.Tasks.Tests/HelloWorldTests.cs`
  - `tests/Dhadgar.Files.Tests/HelloWorldTests.cs`
  - `tests/Dhadgar.Mods.Tests/HelloWorldTests.cs`
  - `tests/Dhadgar.Billing.Tests/HelloWorldTests.cs`
  - `tests/Dhadgar.Console.Tests/HelloWorldTests.cs`
  - `tests/Dhadgar.Firewall.Tests/HelloWorldTests.cs`
- **Risk:** Tests pass but provide no coverage
- **Priority:** Low (no logic to test yet)

### Agent Tests Are HelloWorld Only
- **What's not tested:** Agent functionality (none exists yet)
- **Files:**
  - `tests/Dhadgar.Agent.Core.Tests/HelloWorldTests.cs`
  - `tests/Dhadgar.Agent.Linux.Tests/HelloWorldTests.cs`
  - `tests/Dhadgar.Agent.Windows.Tests/HelloWorldTests.cs`
- **Risk:** When agent code is added, it will lack test coverage
- **Priority:** Critical when agent development begins

### Well-Tested Services
- **Identity:** 27+ test files covering services, endpoints, OAuth, integration
- **Gateway:** 11 test files covering routing, rate limiting, security, circuit breaker
- **Secrets:** 6 test files covering authorization, validation, security

---

## Infrastructure Gaps

### Terraform Not Implemented
- **Problem:** `deploy/terraform/` directory does not exist
- **Impact:** No IaC for production infrastructure
- **Priority:** High - needed before production deployment

### Kubernetes Manifests via Helm Only
- **Problem:** K8s deployment relies entirely on Helm charts
- **Files:** `deploy/kubernetes/helm/meridian-console/*`
- **Impact:** No raw manifests for debugging or GitOps without Helm
- **Priority:** Low - Helm is standard practice

### No Service Mesh Configuration
- **Problem:** mTLS planned but not implemented
- **Files:** None
- **Impact:** Internal traffic is not encrypted
- **Recommendations:** Add Istio or Linkerd configuration when deploying to production

---

## Frontend Migration Debt

### Blazor to Astro Migration Incomplete
- **Issue:** Panel and ShoppingCart still use Blazor WebAssembly
- **Files:**
  - `src/Dhadgar.Panel/*` (Blazor)
  - `src/Dhadgar.ShoppingCart/*` (Blazor)
- **Migrated:** `src/Dhadgar.Scope/*` (Astro/React/Tailwind)
- **Impact:** Two different frontend stacks to maintain
- **Fix approach:** Migrate Panel and ShoppingCart per Scope pattern

---

## Performance Concerns

### Auto-Migration in Development
- **Problem:** Services auto-migrate databases on startup in dev mode
- **Files:** All service `Program.cs` files with `app.Environment.IsDevelopment()` checks
- **Impact:** Slow startup, potential race conditions with concurrent services
- **Recommendations:** Use explicit migration commands instead of auto-migration

### No Connection Pooling Configuration
- **Problem:** PostgreSQL connections use default EF Core settings
- **Files:** All DbContext registrations
- **Impact:** May hit connection limits under load
- **Recommendations:** Configure connection pooling in production appsettings

---

## Fragile Areas

### Identity Service Complexity
- **Files:** `src/Dhadgar.Identity/Program.cs` (966 lines)
- **Why fragile:** Contains OAuth setup, OpenIddict configuration, rate limiting, all in one file
- **Safe modification:** Extract configuration into separate extension methods
- **Test coverage:** Good - many integration tests exist

### Gateway Middleware Order
- **Files:** `src/Dhadgar.Gateway/Program.cs:320-360`
- **Why fragile:** Middleware order is critical - comments warn about dependencies
- **Safe modification:** Follow existing comments, test extensively
- **Test coverage:** Good - middleware tests exist

### Key Vault Certificate Loading
- **Files:** `src/Dhadgar.Identity/Program.cs:875-963`
- **Why fragile:** Complex certificate loading with multiple fallback strategies
- **Safe modification:** Add extensive logging, test locally with Key Vault access
- **Test coverage:** Limited - hard to test without Key Vault

---

## Dependencies at Risk

### .NET 10 Preview
- **Risk:** Using .NET 10.0.100 which may have breaking changes before GA
- **Files:** `global.json`
- **Impact:** Must update when .NET 10 releases
- **Migration plan:** Track .NET 10 release notes, update SDK version

### MudBlazor (Blazor Only)
- **Risk:** Only used in Panel/ShoppingCart which will be migrated
- **Files:** `src/Dhadgar.Panel/*`, `src/Dhadgar.ShoppingCart/*`
- **Impact:** Dependency will be removed after migration
- **Migration plan:** Complete Astro migration

---

## Missing Critical Features

### Agent Implementation
- **Problem:** No agent functionality exists
- **Blocks:** Node enrollment, server provisioning, command execution
- **Priority:** Critical for MVP

### Server Lifecycle Management
- **Problem:** Servers service is a stub
- **Blocks:** Creating, starting, stopping game servers
- **Priority:** Critical for MVP

### Node Inventory
- **Problem:** Nodes service is a stub
- **Blocks:** Node health monitoring, capacity tracking
- **Priority:** Critical for MVP

### Task Orchestration
- **Problem:** Tasks service is a stub
- **Blocks:** Scheduled jobs, background operations
- **Priority:** High for MVP

### File Transfer
- **Problem:** Files service is a stub
- **Blocks:** Game server file uploads/downloads
- **Priority:** High for MVP

---

## Configuration Concerns

### Development Secrets in Config
- **Issue:** Default dev credentials are `dhadgar/dhadgar` everywhere
- **Files:**
  - `deploy/compose/docker-compose.dev.yml`
  - `appsettings.Development.json` files
- **Risk:** Accidental use in production
- **Recommendations:** Use distinct credentials, enforce via CI/CD checks

### Environment Variable Overlap
- **Issue:** Some config can come from appsettings OR environment variables
- **Impact:** Debugging configuration issues is difficult
- **Recommendations:** Document canonical configuration source for each setting

---

*Concerns audit: 2026-01-19*

# CLAUDE.md

Meridian Console (codebase: **Dhadgar**) - A multi-tenant SaaS control plane for game servers on customer-owned hardware.

See @README.md for full project overview.

## Build Commands

```bash
# Enable MSBuild Server for faster repeat builds (set this BEFORE running dotnet commands)
# Add to .bashrc/.zshrc for persistence, or run in your shell before building
export DOTNET_CLI_USE_MSBUILD_SERVER=1

# Build & test
dotnet build
dotnet test

# Run with Aspire (RECOMMENDED for local dev - starts all services + infrastructure)
dotnet run --project src/Dhadgar.AppHost
# Opens Aspire Dashboard at https://localhost:17178 with traces, metrics, logs

# Run individual service (standalone mode, needs manual infrastructure)
dotnet run --project src/Dhadgar.Gateway
dotnet watch --project src/Dhadgar.Gateway  # with hot reload

# EF Core migrations (example: Identity service)
dotnet ef migrations add MigrationName --project src/Dhadgar.Identity --startup-project src/Dhadgar.Identity --output-dir Data/Migrations
dotnet ef database update --project src/Dhadgar.Identity --startup-project src/Dhadgar.Identity

# Local infrastructure (alternative to Aspire, or for CI/CD)
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

## Code Rules

- **.NET 10** with C# nullable enabled
- **Microservices**: Services MUST NOT have `ProjectReference` to each other. Only reference shared libraries (Contracts, Shared, Messaging, ServiceDefaults).
- **Central Package Management**: All package versions in `Directory.Packages.props` - never specify versions in `.csproj` files.
- Don't create documentation files unless explicitly requested.

## Shared Libraries

What goes where (services can only reference these, not each other):

| Library           | Purpose                                                           | Can Depend On     |
| ----------------- | ----------------------------------------------------------------- | ----------------- |
| `Contracts`       | DTOs, message contracts, API models, events                       | Nothing           |
| `Shared`          | Result<T>, Guard, EntityId<T>, BaseEntity, DhadgarDbContext       | EF Core           |
| `Messaging`       | MassTransit config, StaticEntityNameFormatter (meridian.{type})   | Contracts         |
| `ServiceDefaults` | Middleware, observability, exceptions, caching, audit, tracing    | Contracts, Shared |

**Key Shared patterns:**
- `Result<T>` - Railway-oriented programming for error handling
- `BaseEntity` - Audit fields (CreatedAt, UpdatedAt, DeletedAt), RowVersion (xmin)
- `DhadgarDbContext` - Soft-delete filters, auto-timestamps, provider-specific handling
- `ITenantScoped` - Multi-tenant entity marker with OrganizationId

## Service Categories

**Core Services** (substantial implementation, TODOs remain):

- `Gateway` - YARP reverse proxy, rate limiting, circuit breaker, Cloudflare integration (production-ready)
- `Identity` - Users, orgs, roles, OAuth, sessions, search; MFA endpoints return 501 (PostgreSQL)
- `Nodes` - Agent enrollment, mTLS CA, heartbeats, capacity reservations; notification TODOs (PostgreSQL)
- `Secrets` - Azure Key Vault integration, claims-based auth, audit logging; Azure REST API TODOs
- `CLI` - Global .NET tool (`dhadgar`) for platform management (functional)

Note: Core services have working foundations but contain scaffolded endpoints and incomplete features.

**Stubs** (scaffolding only, functionality planned):

- With Database: Billing, Servers, Tasks, Files, Mods, Notifications
- Stateless: Console (SignalR hub), Discord, BetterAuth

**Frontend Apps** (Astro/React/Tailwind):

- `Scope` - Documentation site with 19 sections (functional)
- `Panel` - Control plane UI with OAuth (scaffolding)
- `ShoppingCart` - Marketing/checkout (wireframe, OAuth flow only)

## Git Workflow

- Create feature branches from `main`
- Conventional commits: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`
- **PR Size Limit**: Keep PRs under **85 files** (hard limit: 100). CodeRabbit cannot process PRs with 100+ files. If a feature requires more, split into multiple PRs.
- NEVER force push to main/master
- NEVER skip pre-commit hooks unless user explicitly requests

## PR Review Loop (CodeRabbit + qodo)

When asked to "open a PR" or "create a PR", run this autonomous review loop:

### The Loop

```text
1. Create PR
2. Wait 5 min (CodeRabbit + qodo need time to review)
3. Loop (max 10 iterations):
   a. Check for CodeRabbit and qodo-code-review feedback
   b. Implement fixes that align with design philosophy
   c. Reply to disagreements explaining reasoning (leave unresolved for human override)
   d. Wait until 15 min since last push (CodeRabbit rate limit)
   e. Push fixes (first push combines CodeRabbit + qodo feedback)
   f. Sleep 5 min, then check for new feedback
   g. Exit when: all CI checks green + no new actionable feedback
4. Leave comment on PR: "All checks passing, bot feedback addressed. Ready for review."
5. Ping PR author with @mention
```

### Timing Rules

- **15 min minimum between pushes** - CodeRabbit rate limits; use timestamp of most recent `git push` to PR branch
- **Check every 5 min** - Use `sleep 300` then poll for new comments
- **Max 10 iterations** - If still failing, stop and summarize blockers in PR comment

### Handling Feedback

| Response | Action |
| -------- | ------ |
| Agree | Fix and include in next push |
| Disagree | Reply explaining why, leave unresolved |
| False positive | Reply `@coderabbitai ignore` |

### What to Fix

- **CA analyzer rules** (CA1054, CA2208, etc.) - Fix, these block CI
- **Security (SCS)** - Fix immediately
- **Nullability warnings** - Fix, nullable enabled project-wide
- **Style/docs suggestions** - Use judgment, follow existing patterns

## Testing

- 1:1 test project mapping (e.g., `Dhadgar.Gateway` → `Dhadgar.Gateway.Tests`)
- xUnit framework, 947 tests across 23 projects
- Well-tested: Nodes (352), Identity (173), Gateway (74)
- Run specific tests: `dotnet test --filter "FullyQualifiedName~TestName"`

## Specialized Agents

15 domain-expert agents in `.claude/agents/` for architecture review, security analysis, and technical decisions. Use 2-6 as a "tiger team" when needed. Names are self-descriptive (e.g., `security-architect`, `database-schema-architect`, `agent-service-guardian`).

**IMPORTANT**: Always use `agent-service-guardian` after modifying code in `src/Agents/` - these run on customer hardware with elevated privileges.

## Common Gotchas

- Default credentials for local services: `dhadgar` / `dhadgar`
- Services auto-apply migrations in Development mode
- Gateway runs on port 5000, routes to backend services
- **YARP routes**: `src/Dhadgar.Gateway/appsettings.json` under `ReverseProxy.Routes`
- Agent code (`src/Agents/`) is security-critical - runs on customer hardware

## Pre-PR Checklist (Common Review Feedback)

Based on analysis of CodeRabbit/qodo feedback across 20 PRs, these items are frequently missed during initial development:

### HTTP Status Codes

- `NotFound` (404) for missing resources (UserNotFound, MemberNotFound, NodeNotFound)
- `Forbidden` (403) for permission denied, system role restrictions
- `Conflict` (409) for business rule violations (role has active members, duplicate name)
- `BadRequest` (400) for input validation failures only - NOT for "not found" scenarios

### Security

- No credentials in `appsettings.json` - use user-secrets or environment variables
- No full tokens/secrets in log messages - redact (`token[..8]..`) or hash
- Always pass `organizationId` to service layer for tenant isolation validation
- Path validation for file operations in Agent code

### Validation

- Create FluentValidation validator for each request DTO (`{RequestName}Validator`)
- Pagination: validate `page >= 1`, `limit >= 1 and <= 100`
- `NotEmpty()` allows whitespace - add `.Must(v => !string.IsNullOrWhiteSpace(v))` for required strings

### Resource Management

- Use `using` for HttpClient, ServiceProvider, IDisposable resources
- Use `TimeProvider` instead of `DateTime.UtcNow` for testability
- Dispose `ServiceProvider` in tests

### Code Quality

- Specify `StringComparison` on string operations (CA1307)
- Use `CultureInfo.InvariantCulture` for numeric formatting (CA1305)
- Seal private/internal classes without inheritors (CA1852)
- Null guards on collections before accessing `.Count`: `items is { Count: > 0 }`

### Tests

- Test method name must match assertion (don't name it `ReturnsForbidden` if asserting `NotFound`)
- Use `TimeProvider`/`FakeTimeProvider` consistently, not `DateTime.UtcNow`
- Extract duplicate test helpers to shared utilities

### Event Publishing

- Use MassTransit outbox pattern for reliable event publishing after database writes
- Don't publish events after `SaveChangesAsync` without outbox (creates reliability gap)

## Parallel Claude Sessions

When running multiple Claude sessions simultaneously, each MUST use a separate git worktree:

```bash
git worktree add ../MeridianConsole-feature -b feature/name
git worktree list
git worktree remove ../MeridianConsole-feature
```

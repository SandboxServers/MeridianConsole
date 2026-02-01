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

## CodeRabbit PR Reviews

CodeRabbit automatically reviews all PRs. Here's how to handle its feedback effectively:

### Addressing Feedback

1. **Read the full review first** - CodeRabbit posts a summary comment plus inline suggestions. Read everything before acting.
2. **Use agents for bulk fixes** - When CodeRabbit identifies patterns across multiple files, use specialized agents to fix them consistently.
3. **Push fixes in batches** - CodeRabbit re-reviews on each push. Group related fixes into single commits to reduce noise.

### Common CodeRabbit Findings

| Category | Example | Action |
| -------- | ------- | ------ |
| **CA analyzer rules** | CA1054 (Uri vs string), CA2208 (ArgumentException params) | Fix the code - these are real issues |
| **Security (SCS)** | SQL injection, hardcoded secrets | Fix immediately - blocking issues |
| **Nullability** | Missing null checks, nullable warnings | Fix - nullable is enabled project-wide |
| **Documentation** | Missing XML docs on public APIs | Add if public API, skip for internal |
| **Style** | Naming conventions, formatting | Follow existing patterns in codebase |

### Responding to CodeRabbit

- **Agree**: Just fix it and push. No comment needed.
- **Disagree**: Reply to the comment explaining why. CodeRabbit learns from feedback.
- **False positive**: Comment `@coderabbitai ignore` on the specific suggestion.
- **Need clarification**: Ask CodeRabbit directly in the comment thread - it responds.

### CI Integration

CodeRabbit findings flow into CI checks:
- **Lint .NET (Security SAST)** - Fails on CA/SCS analyzer errors
- **Code Quality Summary** - Aggregates all quality check results

If CI fails after CodeRabbit review, check the workflow logs for specific analyzer errors.

### Tips

- CodeRabbit respects `.editorconfig` and analyzer settings in the repo
- Large PRs (85+ files) may get incomplete reviews - split them
- CodeRabbit can miss context across files - use your judgment on architectural feedback

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

## Parallel Claude Sessions

When running multiple Claude sessions simultaneously, each MUST use a separate git worktree:

```bash
git worktree add ../MeridianConsole-feature -b feature/name
git worktree list
git worktree remove ../MeridianConsole-feature
```

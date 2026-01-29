# CLAUDE.md

Meridian Console (codebase: **Dhadgar**) - A multi-tenant SaaS control plane for game servers on customer-owned hardware.

See @README.md for full project overview.

## Build Commands

```bash
# Build & test
dotnet build
dotnet test

# Run a service
dotnet run --project src/Dhadgar.Gateway
dotnet watch --project src/Dhadgar.Gateway  # with hot reload

# EF Core migrations (example: Identity service)
dotnet ef migrations add MigrationName --project src/Dhadgar.Identity --startup-project src/Dhadgar.Identity --output-dir Data/Migrations
dotnet ef database update --project src/Dhadgar.Identity --startup-project src/Dhadgar.Identity

# Local infrastructure
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

## Code Rules

- **.NET 10** with C# nullable enabled
- **Microservices**: Services MUST NOT have `ProjectReference` to each other. Only reference shared libraries (Contracts, Shared, Messaging, ServiceDefaults).
- **Central Package Management**: All package versions in `Directory.Packages.props` - never specify versions in `.csproj` files.
- Don't create documentation files unless explicitly requested.

## Shared Libraries

What goes where (services can only reference these, not each other):

| Library           | Purpose                                              | Can Depend On     |
| ----------------- | ---------------------------------------------------- | ----------------- |
| `Contracts`       | DTOs, message contracts, API models                  | Nothing           |
| `Shared`          | Utilities, primitives, helpers                       | Nothing           |
| `Messaging`       | MassTransit consumers, publishers, saga base classes | Contracts         |
| `ServiceDefaults` | ASP.NET Core setup, middleware, observability        | Contracts, Shared |

## Service Categories

**With Database** (have EF Core, need migrations): Identity, Billing, Servers, Nodes, Tasks, Files, Mods, Notifications

**Stateless**: Gateway, Console, Secrets, Discord, BetterAuth

**Frontend Apps** (Astro/React/Tailwind):

- `Scope` - Documentation site (functional)
- `Panel` - Control plane UI (scaffolding)
- `ShoppingCart` - Marketing/checkout (wireframe, OAuth flow only)

## Git Workflow

- Create feature branches from `main`
- Conventional commits: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`
- **PR Size Limit**: Keep PRs under **85 files** (hard limit: 100). CodeRabbit cannot process PRs with 100+ files. If a feature requires more, split into multiple PRs.
- NEVER force push to main/master
- NEVER skip pre-commit hooks unless user explicitly requests

## Testing

- 1:1 test project mapping (e.g., `Dhadgar.Gateway` → `Dhadgar.Gateway.Tests`)
- xUnit framework
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

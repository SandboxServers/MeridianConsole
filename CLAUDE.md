# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

**Meridian Console** (solution name: **Dhadgar**) is a modern, security-first game server control plane—a multi-tenant SaaS platform that orchestrates game servers on customer-owned hardware via customer-hosted agents.

**Current Status**: Early-stage scaffolding with foundational structure in place. All services have basic endpoints and EF Core wiring, but features are largely unimplemented. This is intentional—the codebase provides the "shape" for incremental development.

**Important**: This is NOT a hosted game server provider. It's a control plane that orchestrates nodes customers control.

## Build & Development Commands

### Prerequisites
- .NET SDK 10.0.100 (pinned in `global.json`)
- Docker (for local PostgreSQL/RabbitMQ/Redis)

### Start Local Infrastructure
```bash
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

Default credentials for all services: `dhadgar` / `dhadgar`

### Build & Test
```bash
# Full solution
dotnet restore
dotnet build
dotnet test

# Specific service
dotnet build src/Dhadgar.Gateway
dotnet test tests/Dhadgar.Gateway.Tests

# Run a specific test
dotnet test tests/Dhadgar.Gateway.Tests --filter "FullyQualifiedName~HelloWorldTests"
```

### Run Services
```bash
# Run a service directly
dotnet run --project src/Dhadgar.Gateway

# Run with watch (auto-reload)
dotnet watch --project src/Dhadgar.Gateway
```

### EF Core Migrations

Services with databases: Identity, Billing, Servers, Nodes, Tasks, Files, Mods, Notifications

```bash
# Add migration (example: Identity)
dotnet ef migrations add MigrationName \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity \
  --output-dir Data/Migrations

# Apply migrations
dotnet ef database update \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity

# Remove last migration (if not applied)
dotnet ef migrations remove \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity
```

Note: Some services auto-apply migrations in Development mode (see `Program.cs`).

### Configuration Management

Use `dotnet user-secrets` for sensitive local configuration:

```bash
# Initialize user secrets
dotnet user-secrets init --project src/Dhadgar.Identity

# Set secrets
dotnet user-secrets set "Auth:SigningKey" "dev-only-key" --project src/Dhadgar.Identity
dotnet user-secrets set "Discord:BotToken" "your-token" --project src/Dhadgar.Discord

# List secrets
dotnet user-secrets list --project src/Dhadgar.Identity
```

## Architecture

### Microservices Pattern

**Critical Rule**: Services MUST NOT reference each other via `ProjectReference`. This prevents distributed monolith anti-patterns.

**Allowed Dependencies**:
- `Dhadgar.Contracts` (DTOs, message contracts)
- `Dhadgar.Shared` (utilities, primitives)
- `Dhadgar.Messaging` (MassTransit/RabbitMQ conventions)
- `Dhadgar.ServiceDefaults` (common service wiring)

**Runtime Communication**:
- HTTP: Typed clients configured with base URLs
- Async: MassTransit publish/subscribe via RabbitMQ

### Service Structure

Each service follows this pattern:
```
src/Dhadgar.{Service}/
├── Program.cs              # Minimal API setup
├── appsettings.json        # Service configuration
├── Data/                   # EF Core (if DB-backed)
│   └── Migrations/
└── {Service}.csproj
```

Common service endpoints (scaffolding):
- `GET /` - Service banner
- `GET /hello` - Hello world
- `GET /healthz` - Health probe

### Gateway (YARP)

The Gateway is the single public entry point:
- YARP-based reverse proxy
- Routes configured in `src/Dhadgar.Gateway/appsettings.json` under `ReverseProxy` section
- Intended for: authentication enforcement, rate limiting, routing

### Core Services

| Service | Responsibility |
|---------|----------------|
| Gateway | Single entry point; YARP routing and policy |
| Identity | AuthN/AuthZ, JWT, RBAC (planned) |
| Servers | Game server lifecycle management |
| Nodes | Node inventory, health, capacity |
| Tasks | Orchestration and background jobs |
| Files | File metadata and transfer orchestration |
| Mods | Mod registry and versioning |
| Console | Real-time console streaming (SignalR) |
| Billing | SaaS subscriptions (excluded in KiP edition) |
| Notifications | Email/Discord/webhook notifications |
| Firewall | Port/policy management |
| Secrets | Secret storage and rotation |
| Discord | Discord integration |

### Agents

Three agent projects for customer hardware:
- `Dhadgar.Agent.Core` - Shared logic
- `Dhadgar.Agent.Linux` - Linux-specific agent
- `Dhadgar.Agent.Windows` - Windows-specific agent

Agents are designed to make outbound-only connections to the control plane (no inbound holes required).

### Frontend (Blazor WebAssembly)

Three separate WASM apps:
- `Dhadgar.Panel` - Main control plane UI
- `Dhadgar.ShoppingCart` - Marketing/checkout (with Azure Functions `/api`)
- `Dhadgar.Scope.Client` - Documentation site

All use **MudBlazor** for UI components.

Azure Static Web Apps deployment: Custom MSBuild targets copy output to `_swa_publish/` directory.

## Configuration Conventions

Standard ASP.NET Core configuration hierarchy:
1. `appsettings.json`
2. `appsettings.Development.json`
3. Environment variables
4. User secrets (local dev)
5. Kubernetes secrets/ConfigMaps (planned)

### Common Keys

**PostgreSQL** (used by DB-backed services):
```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=dhadgar;Username=dhadgar;Password=dhadgar"
  }
}
```

**RabbitMQ**:
```json
{
  "RabbitMq": {
    "Host": "localhost",
    "Username": "dhadgar",
    "Password": "dhadgar"
  }
}
```

**Redis**:
```json
{
  "Redis": {
    "ConnectionString": "localhost:6379,password=dhadgar"
  }
}
```

**JWT (planned)**:
```json
{
  "Auth": {
    "Issuer": "https://meridian.local",
    "Audience": "meridian-api",
    "SigningKey": "use-user-secrets-for-this"
  }
}
```

## Testing Strategy

- 1:1 project-to-test mapping (23 test projects)
- xUnit framework
- WebApplicationFactory integration ready (services have `public partial class Program`)
- Currently scaffolding with basic assertions

## Security Architecture (Design Intent)

**Trust Boundaries**:
- Edge: Cloudflare (WAF/CDN/DDoS)
- Gateway: Single public entry point
- Internal: Kubernetes cluster (mTLS via service mesh planned)
- Agents: Customer hardware (high-trust components)

**TLS Everywhere**:
- User traffic: TLS at edge
- Internal traffic: mTLS (planned)
- Agent traffic: mTLS with cert rotation (planned)

**Data Collection**:
- Collect minimum required: node health, resource utilization, job status
- Avoid customer content unless explicitly requested (user-requested logs, uploaded files)

## Package Management

Uses **Central Package Management**:
- All versions in `Directory.Packages.props`
- Projects reference packages WITHOUT version attributes
- To update: modify `Directory.Packages.props` only

## CI/CD

**Azure Pipelines** (`azure-pipelines.yml`):
- Extends templates from `SandboxServers/Azure-Pipeline-YAML` repo
- Per-service build/test/deploy
- Selective builds via `servicesCsv` parameter
- Azure Static Web Apps deployment for Blazor WASM apps

## Important Development Patterns

### Database-per-Service
- Each service owns its schema
- Migrations in service's `Data/Migrations/` folder
- No shared database access between services
- Communication via APIs and messaging only

### Message-Driven Architecture
- MassTransit abstracts RabbitMQ
- Contract-based messaging (DTOs in `Dhadgar.Contracts`)
- Designed for commands, events, sagas (planned)

### Service Independence
- Services can be built, tested, deployed independently
- Runtime dependencies only (no compile-time coupling)
- Configuration-driven service discovery

## Codenames

- **Dhadgar**: Codebase/solution name (used in this repo)
- **Dadgar**: Pre-release codename from scope doc
- **KiP**: "Knowledge is Power" self-host edition (planned post-MVP)

Treat these as code names vs product name ("Meridian Console").

## What's Implemented vs Planned

### ✅ Working Today
- Solution builds and tests pass
- Basic service endpoints (/, /hello, /healthz)
- Swagger in Development mode
- YARP Gateway routing structure
- EF Core DbContexts with migrations
- Docker Compose local infrastructure
- One test project per service

### 🚧 Planned (Not Yet Implemented)
- Real authentication, RBAC, user/org lifecycle
- Billing integration, usage metering
- Actual provisioning workflows and scheduling
- Agent enrollment with mTLS
- MassTransit message consumers, retries, DLQs
- Production-ready UI features
- Observability stack (structured logging, tracing, dashboards)
- Kubernetes manifests and Helm charts

## Repository Structure

```
MeridianConsole/
├── Dhadgar.sln                 # Main solution (46 projects)
├── global.json                 # .NET SDK pinning
├── Directory.Build.props       # Shared build properties
├── Directory.Packages.props    # Central package versions
├── azure-pipelines.yml         # CI/CD
├── src/
│   ├── Agents/                 # Customer-side agents (Core, Linux, Windows)
│   ├── Shared/                 # Cross-cutting libraries (Contracts, Messaging, etc.)
│   └── [Services]              # 13 microservices
├── tests/                      # 1:1 test projects (23 total)
├── deploy/
│   ├── compose/                # Docker Compose for local dev
│   ├── kubernetes/             # K8s manifests (planned)
│   └── terraform/              # IaC (planned)
└── docs/                       # Architecture and runbooks
```

## Key Technologies

- **.NET 10.0** with latest C# (nullable enabled)
- **ASP.NET Core** for services
- **YARP 2.3.0** for API Gateway
- **Entity Framework Core 10** with PostgreSQL
- **MassTransit 8.3.6** + RabbitMQ
- **Blazor WebAssembly** for frontend
- **MudBlazor 7.15.0** for UI components
- **SignalR** for real-time features
- **xUnit 2.9.2** for testing

## Documentation Lookup with Context7

This repository is configured with the **context7 MCP server** for retrieving up-to-date library documentation. When working with any of the key technologies above, use context7 to look up best practices, API references, and code examples.

### When to Use Context7

Use context7 MCP tools proactively when:
- Implementing features with MudBlazor components (theming, layouts, forms, etc.)
- Working with Entity Framework Core patterns (migrations, relationships, queries)
- Setting up MassTransit consumers, sagas, or messaging patterns
- Configuring YARP routing, authentication, or middleware
- Using .NET 10 features or ASP.NET Core patterns

### How to Use Context7

1. **Resolve library ID** first using `mcp__context7__resolve-library-id`:
   ```
   libraryName: "MudBlazor"
   ```

2. **Fetch documentation** using `mcp__context7__get-library-docs`:
   ```
   context7CompatibleLibraryID: "/mudblazor/mudblazor"
   topic: "dark theme customization"
   mode: "code"  (for API references) or "info" (for conceptual guides)
   ```

### Examples

**MudBlazor theming:**
```
resolve-library-id: "MudBlazor"
get-library-docs: "/mudblazor/mudblazor", topic: "custom dark theme", mode: "code"
```

**Entity Framework Core:**
```
resolve-library-id: "Entity Framework Core"
get-library-docs: "/dotnet/efcore", topic: "migrations", mode: "code"
```

**MassTransit:**
```
resolve-library-id: "MassTransit"
get-library-docs: "/masstransit/masstransit", topic: "RabbitMQ consumer", mode: "code"
```

This ensures you're always working with current, accurate documentation rather than relying solely on training data.

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

**Meridian Console** (solution name: **Dhadgar**) is a modern, security-first game server control plane—a multi-tenant SaaS platform that orchestrates game servers on customer-owned hardware via customer-hosted agents.

**Current Status**: Early-stage scaffolding with foundational structure in place. All services have basic endpoints and EF Core wiring, but features are largely unimplemented. This is intentional—the codebase provides the "shape" for incremental development.

**Important**: This is NOT a hosted game server provider. It's a control plane that orchestrates nodes customers control.

## Build & Development Commands

### Prerequisites
- .NET SDK 10.0.100 (pinned in `global.json`)
- Node.js 20+ (for Scope project)
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

# Run Scope project (Astro/React/Tailwind)
cd src/Dhadgar.Scope
npm install
npm run dev
```

**Note**: Both `dotnet watch` and Astro's `npm run dev` support hot reload for rapid development cycles. Scope dev server runs on port 4321 by default (configurable in astro.config.mjs).

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

**.NET Services**: Use `dotnet user-secrets` for sensitive local configuration:

```bash
# Initialize user secrets
dotnet user-secrets init --project src/Dhadgar.Identity

# Set secrets
dotnet user-secrets set "Auth:SigningKey" "dev-only-key" --project src/Dhadgar.Identity
dotnet user-secrets set "Discord:BotToken" "your-token" --project src/Dhadgar.Discord

# List secrets
dotnet user-secrets list --project src/Dhadgar.Identity
```

**Node.js Projects** (e.g., Dhadgar.Scope): Use environment variables or `.env` files. Create a `.env` file in the project root (add to `.gitignore`) and use `process.env.VARIABLE_NAME` in Node.js code. Never commit actual secrets to the repository.

## Parallel Claude Sessions

**Rule**: When running multiple Claude Code sessions simultaneously, each session MUST use its own **git worktree**. This prevents one session from accidentally committing another session's uncommitted work.

### What is a Git Worktree?

Normal git: One folder, one branch at a time. Switching branches changes all files.

Git worktree: Same repo, multiple folders, each on a different branch simultaneously.

```
C:\projects\
├── MeridianConsole/           ← main branch (Claude A)
├── MeridianConsole-feature1/  ← feature/auth branch (Claude B)
└── MeridianConsole-feature2/  ← feature/billing branch (Claude C)
```

All folders share the same `.git` history. Commits in any folder show up in all of them, but uncommitted changes stay isolated to their respective folder.

### Commands

```bash
# Create a worktree for a new feature branch
git worktree add ../MeridianConsole-auth -b feature/auth

# Create a worktree for an existing branch
git worktree add ../MeridianConsole-hotfix hotfix/urgent-fix

# List all worktrees
git worktree list

# Remove a worktree when done
git worktree remove ../MeridianConsole-auth
```

### Why This is Required

Without separate worktrees, parallel Claude sessions share the same working directory. When one session commits, it may inadvertently include uncommitted changes from another session—exactly the problem worktrees solve.

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

**Special Case - Dhadgar.Scope**: The Scope project is a Node.js/Astro application wrapped in a .NET shim. Its `.csproj` file invokes `npm install` and `npm build` during `dotnet build`, allowing it to integrate with the .NET solution while using a different build toolchain. This enables running `dotnet build` to build the entire solution including the Scope project.

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

### Frontend Architecture

**Important**: The project is in transition from Blazor to modern web stack. Current state:

- `Dhadgar.Scope` - Documentation site using **Astro 5.1.1 + React + Tailwind CSS** (✅ Migrated)
- `Dhadgar.Panel` - Main control plane UI using Blazor WebAssembly (🚧 TODO: Migrate to Astro/React/Tailwind)
- `Dhadgar.ShoppingCart` - Marketing/checkout using Blazor WebAssembly (🚧 TODO: Migrate to Astro/React/Tailwind)

**Note**: The POC Scope project proves to architectural pattern going forward. Panel and ShoppingCart will be migrated to match this stack.

All frontend projects deploy to Azure Static Web Apps via `_swa_publish/wwwroot/` directory.

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
- xUnit framework for .NET services
- WebApplicationFactory integration ready (services have `public partial class Program`)
- Node.js projects (e.g., Dhadgar.Scope.Tests) use Jest or Vitest for unit/integration testing
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
- Azure Static Web Apps deployment for frontend apps (Astro and Blazor)

## PR Review Automation

### spirit-of-the-diff Bot

This repository includes **spirit-of-the-diff**, a free AI-powered code review bot using PR-Agent + OpenRouter.

**Usage:**
```
Comment /spirit on any pull request to trigger a detailed code review
```

**What it provides:**
- Code quality assessment with estimated review effort (1-5 scale)
- Security concern identification
- Recommended focus areas for reviewers
- Specific improvement suggestions with code examples
- Persistent review that updates with new commits

**Why `/spirit` instead of `/review`?**
- Avoids conflicts with other PR review bots (Qodo SaaS uses `/review`)
- Allows running multiple review bots simultaneously
- Custom command implemented via PR-Agent CLI + GitHub Actions

**Technical implementation:**
- GitHub Actions workflow: `.github/workflows/pr-agent-review.yml`
- Triggers on `/spirit` comments (manual only, no automatic spam)
- Runs PR-Agent CLI with explicit review command
- Uses GitHub App authentication (spirit-of-the-diff[bot])
- Free tier: OpenRouter + Mistral Devstral 2512 (262k context window)

**Setup documentation:** [`docs/SPIRIT_OF_THE_DIFF_SETUP.md`](docs/SPIRIT_OF_THE_DIFF_SETUP.md)

**Multiple bots:** This repo may run multiple review bots in parallel:
- **spirit-of-the-diff** (`/spirit`) - Manual deep-dive reviews (free)
- **CodeRabbit** - Automatic reviews on commits
- **Qodo/PR-Agent SaaS** (`/review`) - If configured

All bots can coexist on the same PRs without conflicts.

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
- Dhadgar.Scope site migrated to Astro/React/Tailwind

### 🚧 Planned (Not Yet Implemented)
- Real authentication, RBAC, user/org lifecycle
- Billing integration, usage metering
- Actual provisioning workflows and scheduling
- Agent enrollment with mTLS
- MassTransit message consumers, retries, DLQs
- Production-ready UI features
- Observability stack (structured logging, tracing, dashboards)
- Kubernetes manifests and Helm charts
- Migrate Dhadgar.Panel to Astro/React/Tailwind
- Migrate Dhadgar.ShoppingCart to Astro/React/Tailwind

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
- **Astro 5.1.1 + React + Tailwind CSS** for frontend (Scope - migrated)
- **Blazor WebAssembly** for frontend (Panel, ShoppingCart - TODO: migrate)
- **MudBlazor 7.15.0** for UI components (Blazor projects only)
- **SignalR** for real-time features
- **xUnit 2.9.2** for testing

## Documentation Lookup with Context7

This repository is configured with the **context7 MCP server** for retrieving up-to-date library documentation. When working with any of the key technologies above, use context7 to look up best practices, API references, and code examples.

### When to Use Context7

Use context7 MCP tools proactively when:
- Implementing features with Astro/React components (layouts, routing, etc.)
- Working with Tailwind CSS utilities and configuration
- Working with Entity Framework Core patterns (migrations, relationships, queries)
- Setting up MassTransit consumers, sagas, or messaging patterns
- Configuring YARP routing, authentication, or middleware
- Using .NET 10 features or ASP.NET Core patterns

### How to Use Context7

1. **Resolve library ID** first using `mcp__context7__resolve-library-id`:
   ```
   libraryName: "Astro"
   ```

2. **Fetch documentation** using `mcp__context7__get-library-docs`:
   ```
   context7CompatibleLibraryID: "/withastro/astro"
   topic: "react integration"
   mode: "code"  (for API references) or "info" (for conceptual guides)
   ```

### Examples

**Astro React integration:**
```
resolve-library-id: "Astro"
get-library-docs: "/withastro/astro", topic: "react integration", mode: "code"
```

**Tailwind CSS:**
```
resolve-library-id: "Tailwind CSS"
get-library-docs: "/tailwindlabs/tailwindcss", topic: "responsive utilities", mode: "code"
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

## Specialized Agents

This repository has **15 specialized agents** configured in `.claude/agents/` to provide expert guidance across all aspects of the platform. These agents should be used proactively when working in their respective domains.

### Frontend & UI

#### frontend-expert
**Use for**: Astro/React/Tailwind development, component architecture, responsive design, forms, layouts, navigation, CSS styling, client-side state management, TypeScript patterns.

**When to invoke**:
- Creating new Astro pages or React components
- Implementing forms with validation
- Styling with Tailwind CSS
- Fixing responsive layout issues
- Building reusable UI components
- Setting up Astro routing and layouts
- Working with React integration in Astro
- TypeScript type safety improvements

### Architecture & Design

#### microservices-architect
**Use for**: Service boundary decisions, inter-service communication patterns, distributed system concerns, evaluating service decomposition, ensuring proper service isolation.

**When to invoke**:
- Designing features that span multiple services
- Evaluating whether to create a new service
- Implementing cross-service data access
- Reviewing architecture for anti-patterns
- Planning distributed transactions or sagas

#### iam-architect
**Use for**: Identity and access management, authentication flows, authorization strategies, RBAC design, JWT tokens, OAuth/OIDC, passwordless authentication.

**When to invoke**:
- Implementing authentication or authorization
- Designing role hierarchies and permissions
- Evaluating passwordless options (WebAuthn, passkeys)
- Reviewing security of authentication code
- Planning multi-tenant identity isolation

#### security-architect
**Use for**: Security architecture, threat modeling, secure coding practices, secrets management, network security, API security, container security, compliance.

**When to invoke**:
- Designing security-critical features
- Reviewing code for security vulnerabilities
- Planning secrets management approach
- Implementing authentication/authorization enforcement
- Hardening Kubernetes or container configurations

#### agent-service-guardian
**Use for**: **CRITICAL** - Security reviews of customer-hosted agent code (Dhadgar.Agent.Core, Dhadgar.Agent.Linux, Dhadgar.Agent.Windows).

**When to invoke** (ALWAYS):
- After ANY code changes to agent projects
- When adding new agent endpoints or capabilities
- Implementing process isolation or sandboxing
- Adding command execution or file handling
- Modifying agent authentication or communication

**Special note**: This agent must review all agent code due to the high-trust nature of customer-hosted components.

### Data & Persistence

#### database-schema-architect
**Use for**: Database schema design, EF Core migrations, table relationships, indexing strategies, normalization, schema evolution patterns.

**When to invoke**:
- Creating new entities or tables
- Adding relationships between entities
- Designing migrations for schema changes
- Planning data models for new features
- Reviewing migrations for safety and performance

#### database-admin
**Use for**: PostgreSQL administration, performance tuning, connection pooling, migration strategies, backup/recovery, query optimization, database security.

**When to invoke**:
- Configuring PostgreSQL connections
- Troubleshooting slow queries
- Planning database scaling (replication, sharding)
- Implementing backup strategies
- Optimizing connection pool settings

### Integration & Communication

#### messaging-engineer
**Use for**: RabbitMQ, MassTransit, message consumers, publishers, sagas, message contracts, retry policies, dead letter queues.

**When to invoke**:
- Implementing message consumers or publishers
- Designing message contracts (commands/events)
- Configuring retry policies or error handling
- Implementing sagas for orchestration
- Troubleshooting message flow issues

#### rest-api-engineer
**Use for**: REST API design, endpoint structure, HTTP methods, status codes, request/response design, versioning, error handling, pagination.

**When to invoke**:
- Designing new API endpoints
- Reviewing API consistency across services
- Implementing pagination or filtering
- Choosing appropriate HTTP status codes
- Designing error response formats

### Development & Quality

#### dotnet-10-researcher
**Use for**: .NET 10 features, security patterns, performance optimization, advanced APIs, researching best practices.

**When to invoke**:
- Exploring new .NET 10 features for a use case
- Implementing security-sensitive functionality
- Optimizing performance-critical code
- Researching streaming or advanced patterns
- Evaluating cryptographic or security APIs

#### dotnet-test-engineer
**Use for**: Writing tests, debugging test failures, test strategies, xUnit, WebApplicationFactory, mocking, integration testing.

**When to invoke**:
- Writing tests for new features
- Debugging failing or flaky tests
- Designing test strategies for complex scenarios
- Setting up integration tests with database
- Mocking dependencies (HttpClient, databases)

### Infrastructure & Operations

#### azure-pipelines-architect
**Use for**: Azure Pipelines YAML, CI/CD configuration, pipeline templates, build/deploy workflows, pipeline versioning, troubleshooting pipeline failures.

**When to invoke**:
- Adding new services to CI/CD pipeline
- Modifying azure-pipelines.yml
- Debugging pipeline template errors
- Implementing pipeline versioning strategies
- Optimizing build performance

#### azure-infra-advisor
**Use for**: Cloud vs on-premises placement decisions, cost analysis, infrastructure sizing, evaluating Azure services, hybrid architecture planning.

**When to invoke**:
- Deciding where to host a database or service
- Evaluating Azure managed services vs self-hosted
- Analyzing cloud costs vs on-prem TCO
- Planning infrastructure for new features
- Reviewing monthly cloud spend

#### talos-os-expert
**Use for**: Talos OS configuration, cluster bootstrapping, upgrades, networking (CNI), storage, etcd management, Kubernetes-on-Talos patterns.

**When to invoke**:
- Configuring Talos machine configs
- Adding nodes to Talos cluster
- Troubleshooting etcd or cluster issues
- Planning Talos cluster upgrades
- Configuring storage or networking on Talos

**Warning**: Talos mistakes can brick nodes. Always use this agent before applying machine config changes.

#### observability-architect
**Use for**: Distributed tracing, metrics collection, log aggregation, alerting, dashboards, OpenTelemetry instrumentation, New Relic vs open-source tooling.

**When to invoke**:
- Adding observability to new services
- Implementing distributed tracing
- Setting up metrics or custom instrumentation
- Designing dashboards or alerts
- Evaluating observability tools (New Relic, Jaeger, Grafana)

## Agent Usage Patterns

### Proactive Agent Use

Some agents should be invoked **automatically** after certain actions:

1. **agent-service-guardian**: ALWAYS after modifying agent code
2. **dotnet-test-engineer**: After implementing complex features (should you write tests?)
3. **observability-architect**: After creating new services or endpoints
4. **security-architect**: After implementing authentication/authorization features

### Multi-Agent Collaboration

Some tasks benefit from multiple agents:

- **New microservice**: microservices-architect → dotnet-10-researcher → observability-architect → dotnet-test-engineer
- **Database feature**: database-schema-architect → database-admin → dotnet-test-engineer
- **API endpoint**: rest-api-engineer → security-architect → dotnet-test-engineer
- **Agent changes**: (implement code) → agent-service-guardian → dotnet-test-engineer
- **Infrastructure decision**: azure-infra-advisor → talos-os-expert (if Kubernetes) OR azure-pipelines-architect (if deployment)

### When NOT to Use Agents

- Simple, obvious code changes (typo fixes, minor refactoring)
- Reading/exploring code without modifications
- Quick debugging or investigation
- Tasks already well-understood and straightforward

Agents add value for complex, security-sensitive, or architecturally significant work. Use your judgment.

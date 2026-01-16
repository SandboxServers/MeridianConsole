# Meridian Console

**A modern game server control plane for customer-owned hardware**

Meridian Console (codebase: **Dhadgar**) is a multi-tenant SaaS platform that orchestrates game servers on hardware **you** control. Think of it as a mission control center that talks to agents running on your servers—whether they're in your basement, a colo facility, or spread across multiple clouds.

**What makes it different:** We don't host your servers. You do. We just give you the tools to manage them at scale.
test
---

## 🚀 Quick Start (5 Minutes)

**Prerequisites:**
- Windows 10/11 (or Linux/macOS—scripts work everywhere)
- 16GB RAM recommended
- PowerShell 7+ (Windows) or bash (Linux/macOS)

```powershell
# 1. Clone the repo
git clone https://github.com/SandboxServers/MeridianConsole.git
cd MeridianConsole

# 2. Run the bootstrap script (installs .NET, Docker, etc.)
.\scripts\bootstrap-dev.ps1

# 3. Start local infrastructure (PostgreSQL, RabbitMQ, Redis, Grafana, Prometheus, Loki)
docker compose -f deploy/compose/docker-compose.dev.yml up -d

# 4. Build everything
dotnet build

# 5. Run the Gateway (API entry point)
dotnet run --project src/Dhadgar.Gateway

# 6. Try it out!
curl http://localhost:5000/
curl http://localhost:5000/healthz
open http://localhost:5000/swagger  # API docs
```

**That's it!** You now have the entire platform running locally.

**Observability dashboards:**
- Grafana: http://localhost:3000 (admin/admin)
- Prometheus: http://localhost:9090
- RabbitMQ Management: http://localhost:15672 (dhadgar/dhadgar)

---

## 📖 Table of Contents

- [What is Meridian Console?](#what-is-meridian-console)
- [Current Status](#current-status)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Environment Setup](#environment-setup)
  - [Running Services](#running-services)
  - [Optional: Azure Integration](#optional-azure-integration)
- [Architecture Overview](#architecture-overview)
- [Services](#services)
  - [Implemented Services](#implemented-services)
  - [Stub Services](#stub-services)
- [Development Guide](#development-guide)
- [Testing](#testing)
- [Contributing](#contributing)

---

## What is Meridian Console?

### The Elevator Pitch

You have game servers. Maybe you're running Minecraft for friends, hosting Valheim for a guild, or managing a fleet of ARK servers for a community. You want:

- **Centralized control**: See all servers in one dashboard
- **Automated management**: Start, stop, update servers without SSH
- **Multi-tenancy**: Let others manage their servers through your instance
- **Your hardware**: Run on your gaming PC, dedicated server, or cloud VMs

That's Meridian Console. It's the control plane—the brain that coordinates everything—while your hardware does the actual work.

### What It Is (and Isn't)

**It IS:**
- A web UI + API for managing game servers
- A platform for running on **your** hardware (SaaS or self-hosted)
- Multi-tenant (SaaS edition) or single-tenant (KiP edition)
- Microservices architecture with modern observability

**It IS NOT:**
- A game server hosting provider (we don't run your servers for you)
- A finished product (it's actively being built)
- A monolithic app (services are independent and communicate via APIs)

### Trust Model

The design philosophy: **Agents run on customer hardware** and are high-trust components. The control plane issues commands and receives telemetry, but never reaches beyond what the customer installed.

- **Network**: Agents make outbound connections (no inbound firewall holes needed)
- **Security**: mTLS everywhere (in progress), certificate rotation, audit trails
- **Data**: Collect only what's needed (health, metrics, events)—not game content

---

## Current Status

### ✅ What Works Today

**Core Platform:**
- ✅ Full solution builds with .NET 10 (`dotnet build`)
- ✅ All 59+ tests pass (`dotnet test`)
- ✅ Local infrastructure with Docker Compose
- ✅ API Gateway with YARP reverse proxy
- ✅ OpenTelemetry distributed tracing + metrics
- ✅ Grafana/Prometheus/Loki observability stack
- ✅ Centralized middleware (correlation IDs, RFC 7807 errors, request logging)

**Implemented Services** (with real functionality):
- **Gateway**: YARP reverse proxy with rate limiting, CORS, correlation tracking
- **Identity**: User/org management, roles, OAuth providers, search (PostgreSQL + EF Core)
- **BetterAuth**: Passwordless authentication via Better Auth SDK

**Development Experience:**
- ✅ Hot reload with `dotnet watch`
- ✅ Swagger UI for all services
- ✅ EF Core migrations for database services
- ✅ User secrets for local config
- ✅ Bootstrap script for environment setup

### 🚧 What's Being Built

- Real authentication flows (JWT, RBAC policy enforcement)
- Billing integration (SaaS edition)
- Game server provisioning workflows
- Agent enrollment with mTLS
- MassTransit message topology (commands, events, sagas)
- Production UI features (currently Blazor, migrating to Astro/React/Tailwind)
- Kubernetes manifests and Helm charts

**Bottom line:** The foundation is solid. Features are landing incrementally.

---

## Getting Started

### Prerequisites

**Required:**
- **OS**: Windows 10/11, Linux, or macOS
- **RAM**: 16GB recommended (8GB minimum)
- **.NET SDK**: 10.0.100 (pinned in `global.json`)
- **Docker**: For local infrastructure
- **Git**: For cloning the repo

**Optional:**
- **Node.js 20+**: If you want to work on the Scope documentation site
- **Azure CLI**: If you're setting up Azure resources
- **Visual Studio 2022** or **VS Code**: For development

### Environment Setup

#### Option 1: Automated Bootstrap (Windows)

The bootstrap script installs everything you need:

```powershell
# Run from the repo root
.\scripts\bootstrap-dev.ps1

# Options:
# -SkipDocker          # Skip Docker Desktop installation
# -SkipMinikube        # Skip minikube installation
# -SkipOptional        # Skip VS Code, psql client tools
# -Status              # Show what's already installed
```

**What it does:**
1. Checks for required tools (.NET, Docker, Git)
2. Installs missing tools via Chocolatey (Windows) or package managers (Linux/macOS)
3. Configures Docker Desktop
4. Sets up minikube (Kubernetes for local testing)
5. Starts local infrastructure
6. Verifies everything works

**Checkpoint system:** If it needs a reboot (like after Docker install), it saves progress and resumes after restart.

#### Option 2: Manual Setup

**1. Install .NET SDK 10.0.100**

```bash
# Windows (winget)
winget install Microsoft.DotNet.SDK.10

# macOS (Homebrew)
brew install --cask dotnet-sdk

# Linux (see https://dotnet.microsoft.com/download)
```

**2. Install Docker**

```bash
# Windows
winget install Docker.DockerDesktop

# macOS
brew install --cask docker

# Linux
curl -fsSL https://get.docker.com | sh
```

**3. Verify Installation**

```bash
dotnet --version  # Should show 10.0.100
docker --version  # Any recent version
git --version     # Any recent version
```

#### Starting Local Infrastructure

The platform needs PostgreSQL, RabbitMQ, Redis, and observability tools:

```bash
# Start everything
docker compose -f deploy/compose/docker-compose.dev.yml up -d

# Verify it's running
docker ps

# Stop everything
docker compose -f deploy/compose/docker-compose.dev.yml down
```

**What you get:**
- **PostgreSQL** (port 5432): Database for services
- **RabbitMQ** (ports 5672, 15672): Message bus + management UI
- **Redis** (port 6379): Caching and sessions
- **Grafana** (port 3000): Metrics dashboards (admin/admin)
- **Prometheus** (port 9090): Metrics collection
- **Loki** (port 3100): Log aggregation
- **OpenTelemetry Collector** (ports 4317, 4318): Telemetry pipeline

**Default credentials:** `dhadgar` / `dhadgar` for everything

**Troubleshooting:** See `deploy/compose/README.md` for common issues and solutions.

### Running Services

#### Running the Gateway (API Entry Point)

```bash
# Standard run
dotnet run --project src/Dhadgar.Gateway

# With hot reload
dotnet watch --project src/Dhadgar.Gateway

# Gateway runs on http://localhost:5000
```

#### Running Other Services

```bash
# Identity service (user/org management)
dotnet run --project src/Dhadgar.Identity

# BetterAuth service (authentication)
dotnet run --project src/Dhadgar.BetterAuth

# Run any service with hot reload
dotnet watch --project src/Dhadgar.{ServiceName}
```

#### Running the Frontend (Scope Documentation Site)

```bash
cd src/Dhadgar.Scope
npm install
npm run dev

# Scope runs on http://localhost:4321
```

#### Building Everything

```bash
# Full solution
dotnet restore
dotnet build

# Specific service
dotnet build src/Dhadgar.Gateway

# Run all tests
dotnet test

# Run specific tests
dotnet test tests/Dhadgar.Gateway.Tests
dotnet test tests/Dhadgar.Gateway.Tests --filter "FullyQualifiedName~HealthCheckTests"
```

### Optional: Azure Integration

If you're deploying to Azure or using Azure services (Key Vault, Container Registry, etc.), you'll need to set up Azure resources.

#### Creating Azure Resources (PowerShell)

The repo includes scripts to create the necessary Azure resources:

```powershell
# Create Key Vault for secrets
.\scripts\Create-KeyVault.ps1 -VaultName "meridian-keyvault" -ResourceGroup "meridian-rg"

# Create App Registration for authentication
.\scripts\Create-AppRegistration.ps1 -AppName "MeridianConsole"

# Test Azure authentication
.\scripts\Test-WifCredential.ps1
```

**What these do:**
- **Key Vault**: Stores secrets (connection strings, API keys, certificates)
- **App Registration**: Azure AD app for authentication (OAuth/OIDC)
- **Service Principal**: Identity for CI/CD and service-to-service auth

**Azure Container Registry** (already set up):
- **Name**: `meridianconsoleacr`
- **Login Server**: `meridianconsoleacr-etdvg4cthscffqdf.azurecr.io`
- See `CLAUDE.md` for authentication details

#### Configuring Services for Azure

Use **user secrets** for local development (keeps secrets out of source control):

```bash
# Initialize user secrets for a service
dotnet user-secrets init --project src/Dhadgar.Identity

# Add Azure Key Vault URL
dotnet user-secrets set "Azure:KeyVaultUrl" "https://your-vault.vault.azure.net/" --project src/Dhadgar.Identity

# Add connection strings
dotnet user-secrets set "ConnectionStrings:Postgres" "your-connection-string" --project src/Dhadgar.Identity

# Optional: Enable OpenTelemetry export to observability stack
dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317" --project src/Dhadgar.Gateway

# List all secrets
dotnet user-secrets list --project src/Dhadgar.Identity
```

**Why user secrets?**
- They're stored in your user profile (not the repo)
- Different developers can have different values
- They override `appsettings.json` automatically

---

## Architecture Overview

### The Big Picture

```
Internet
   ↓
Cloudflare (WAF, CDN, DDoS protection)
   ↓
Gateway (YARP reverse proxy)
   ↓
┌─────────────────────────────────────────────┐
│ Microservices (running in Kubernetes)      │
│  ├─ Identity (users, orgs, roles)          │
│  ├─ Servers (game server lifecycle)        │
│  ├─ Nodes (hardware inventory)             │
│  ├─ Tasks (orchestration)                  │
│  └─ ... (see Services section)             │
└─────────────────────────────────────────────┘
   ↕
RabbitMQ (async messaging)
   ↕
Customer Agents (Windows/Linux)
   ↓
Game Servers (running on customer hardware)
```

### Key Design Principles

**1. Microservices (No Monolith)**
- Each service is independent
- Services communicate via HTTP APIs or message bus
- No compile-time dependencies between services
- Shared libraries only for contracts, utilities, and middleware

**2. Database-per-Service**
- Each service owns its data schema
- No shared database access
- Communication via APIs ensures proper boundaries

**3. API Gateway Pattern**
- Gateway is the single public entry point
- Handles: routing, rate limiting, CORS, authentication enforcement
- Uses YARP (Yet Another Reverse Proxy) for performance

**4. Centralized Middleware**
- Correlation IDs for distributed tracing (every request gets tracked)
- RFC 7807 Problem Details for errors (standard error format)
- Request logging with OpenTelemetry integration

**5. Observability-First**
- OpenTelemetry traces, metrics, and logs
- Grafana dashboards for visualization
- Prometheus for metrics, Loki for logs
- Correlation IDs connect everything

---

## Services

### Implemented Services

These services have real functionality beyond basic scaffolding:

#### 🌐 Gateway (`src/Dhadgar.Gateway`)

**What it does:** Single entry point for all API traffic. Routes requests to backend services.

**Tech stack:** YARP reverse proxy, rate limiting, CORS, OpenTelemetry

**Key features:**
- Routes all 14 microservices
- Rate limiting (global, per-tenant, per-agent, auth endpoints)
- Active health checks for backend services
- Session affinity for SignalR connections
- Security headers, correlation tracking, problem details middleware

**Endpoints:**
- `GET /` - Service banner
- `GET /healthz` - Health check
- `GET /swagger` - API documentation
- `/api/v1/{service}/*` - Proxies to backend services

**Runs on:** Port 5000 (configurable)

**Database:** None (stateless proxy)

#### 👤 Identity (`src/Dhadgar.Identity`)

**What it does:** User and organization management, role-based access control.

**Tech stack:** ASP.NET Core, PostgreSQL, Entity Framework Core

**Key features:**
- User CRUD operations
- Organization (tenant) management
- Role system (org-scoped and custom roles)
- Membership management (add/remove users from orgs)
- Search API (users, orgs, roles)
- OAuth provider integration (Steam, Battle.net, Discord, Microsoft)

**Endpoints:**
- `POST /users` - Create user
- `GET /users/:id` - Get user by ID
- `PATCH /users/:id` - Update user
- `DELETE /users/:id` - Delete user
- `POST /organizations` - Create organization
- `POST /organizations/:id/members` - Add member to org
- `POST /roles` - Create custom role
- `GET /search/users` - Search users
- `GET /search/organizations` - Search organizations
- `GET /webhooks` - Webhook endpoint for auth providers

**Runs on:** Port 5010

**Database:** PostgreSQL (`dhadgar_identity`)

#### 🔐 BetterAuth (`src/Dhadgar.BetterAuth`)

**What it does:** Passwordless authentication using Better Auth SDK.

**Tech stack:** Better Auth, Node.js-like integration in .NET

**Key features:**
- Passwordless authentication (email magic links, OAuth)
- Session management
- Multiple OAuth providers (Google, GitHub, etc.)
- Integration with Identity service

**Endpoints:**
- Better Auth standard endpoints (handled by SDK)
- Proxied through Gateway at `/api/v1/betterauth/*`

**Runs on:** Port 5130

**Database:** PostgreSQL (shared with Identity)

### Stub Services

These services have basic scaffolding (hello world, health checks) but core functionality is planned:

#### 💰 Billing (`src/Dhadgar.Billing`) - Port 5020
**Planned:** Subscription management, usage metering, invoicing

#### 🖥️ Servers (`src/Dhadgar.Servers`) - Port 5030
**Planned:** Game server lifecycle management, configuration, start/stop/restart

#### 🔌 Nodes (`src/Dhadgar.Nodes`) - Port 5040
**Planned:** Hardware inventory, health monitoring, capacity management, agent enrollment

#### 📋 Tasks (`src/Dhadgar.Tasks`) - Port 5050
**Planned:** Background job orchestration, scheduling, status tracking

#### 📁 Files (`src/Dhadgar.Files`) - Port 5060
**Planned:** File upload/download, transfer orchestration, mod distribution

#### 🧩 Mods (`src/Dhadgar.Mods`) - Port 5080
**Planned:** Mod registry, versioning, compatibility tracking

#### 🖥️ Console (`src/Dhadgar.Console`) - Port 5070
**Planned:** Real-time server console via SignalR, command execution

#### 📧 Notifications (`src/Dhadgar.Notifications`) - Port 5090
**Planned:** Email, Discord, webhook notifications

#### 🔥 Firewall (`src/Dhadgar.Firewall`) - Port 5100
**Planned:** Port management, firewall rule automation

#### 🔑 Secrets (`src/Dhadgar.Secrets`) - Port 5110
**Planned:** Secret storage, rotation, Azure Key Vault integration

#### 💬 Discord (`src/Dhadgar.Discord`) - Port 5120
**Planned:** Discord bot integration, server management commands

---

## Development Guide

### Project Structure

```
MeridianConsole/
├── src/
│   ├── Dhadgar.Gateway/              # API Gateway (YARP)
│   ├── Dhadgar.Identity/             # Users, orgs, roles ✅
│   ├── Dhadgar.BetterAuth/           # Passwordless auth ✅
│   ├── Dhadgar.{Service}/            # Other services (stubs)
│   ├── Shared/
│   │   ├── Dhadgar.Contracts/        # DTOs, message contracts
│   │   ├── Dhadgar.Shared/           # Utilities, primitives
│   │   ├── Dhadgar.Messaging/        # MassTransit conventions
│   │   └── Dhadgar.ServiceDefaults/  # Middleware, observability
│   ├── Agents/
│   │   ├── Dhadgar.Agent.Core/       # Shared agent logic
│   │   ├── Dhadgar.Agent.Linux/      # Linux-specific agent
│   │   └── Dhadgar.Agent.Windows/    # Windows-specific agent
│   ├── Dhadgar.Scope/                # Documentation site (Astro)
│   ├── Dhadgar.Panel/                # Main UI (Blazor → Astro migration)
│   └── Dhadgar.ShoppingCart/         # Marketing site (Blazor)
├── tests/                             # 1:1 test projects (23 total)
├── deploy/
│   ├── compose/                       # Docker Compose for local dev
│   ├── kubernetes/                    # K8s manifests (planned)
│   └── terraform/                     # Infrastructure as Code (planned)
├── scripts/                           # PowerShell/bash automation
└── docs/                              # Architecture and runbooks
```

### Adding a New Service

See the existing services for patterns. Key steps:

1. **Create the project**
   ```bash
   dotnet new webapi -n Dhadgar.YourService
   ```

2. **Add to solution**
   ```bash
   dotnet sln add src/Dhadgar.YourService/Dhadgar.YourService.csproj
   ```

3. **Add dependencies** (in `.csproj`)
   ```xml
   <ItemGroup>
     <ProjectReference Include="../Shared/Dhadgar.Contracts/Dhadgar.Contracts.csproj" />
     <ProjectReference Include="../Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj" />
   </ItemGroup>
   ```

4. **Add to Gateway routing** (`src/Dhadgar.Gateway/appsettings.json`)

5. **Create test project**
   ```bash
   dotnet new xunit -n Dhadgar.YourService.Tests
   ```

### Database Migrations (EF Core)

For services that use databases (Identity, Billing, etc.):

```bash
# Add a migration
dotnet ef migrations add YourMigrationName \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity \
  --output-dir Data/Migrations

# Apply migrations
dotnet ef database update \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity

# Remove last migration (if not applied yet)
dotnet ef migrations remove \
  --project src/Dhadgar.Identity \
  --startup-project src/Dhadgar.Identity
```

**Note:** Some services auto-apply migrations in Development mode (see `Program.cs`).

### Centralized Middleware

All services inherit these middleware components from `Dhadgar.ServiceDefaults`:

- **CorrelationMiddleware**: Adds `X-Correlation-Id`, `X-Request-Id`, `X-Trace-Id` headers
- **ProblemDetailsMiddleware**: Converts exceptions to RFC 7807 Problem Details responses
- **RequestLoggingMiddleware**: Logs HTTP requests/responses with correlation context

**To use in your service:**
```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDefaults(); // Adds middleware automatically
```

### Configuration Hierarchy

ASP.NET Core loads configuration in this order (later overrides earlier):

1. `appsettings.json` - Defaults for all environments
2. `appsettings.Development.json` - Development overrides
3. Environment variables - Server/container config
4. User secrets - Local development secrets
5. Kubernetes ConfigMaps/Secrets - Production secrets

**Example:**
```json
// appsettings.json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Database=dhadgar"
  }
}

// appsettings.Development.json (overrides)
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}

// User secrets (overrides)
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Database=dhadgar;Username=dev;Password=secret"
```

---

## Testing

### Running Tests

```bash
# All tests
dotnet test

# Specific project
dotnet test tests/Dhadgar.Gateway.Tests

# Specific test
dotnet test --filter "FullyQualifiedName~CorrelationMiddlewareTests"

# With detailed output
dotnet test --verbosity detailed
```

### Test Structure

- **1:1 mapping**: Every project has a corresponding test project
- **xUnit framework**: All .NET tests use xUnit
- **Integration tests ready**: Services expose `public partial class Program` for `WebApplicationFactory`

Example integration test:

```csharp
public class GatewayIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GatewayIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthCheck_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

---

## Contributing

### Before You Start

1. **Read the scope document** (`docs/scope/`) to understand the vision
2. **Check CLAUDE.md** for AI-specific guidance (if you're using Claude Code)
3. **Run the bootstrap script** to set up your environment
4. **Build and test** to ensure everything works

### Workflow

1. **Create a feature branch**
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **Make changes** (write code, tests, docs)

3. **Ensure tests pass**
   ```bash
   dotnet test
   ```

4. **Commit with conventional commits**
   ```bash
   git commit -m "feat: add user search endpoint"
   git commit -m "fix: correct correlation ID propagation"
   git commit -m "docs: update README with new service info"
   ```

5. **Push and create PR**
   ```bash
   git push -u origin feature/your-feature-name
   # Then create PR on GitHub
   ```

### PR Review Bots

This repo has multiple code review bots:

- **CodeRabbit**: Automatic reviews on every commit
- **spirit-of-the-diff**: Comment `/spirit` on a PR for deep-dive review
- **GitHub Actions**: CI/CD pipeline runs tests, builds, linting

### Coding Standards

- **C# 12 with nullable enabled**: All new code must handle nullability
- **Microservices pattern**: No `ProjectReference` between services (only to shared libraries)
- **OpenAPI/Swagger**: All HTTP endpoints documented
- **Tests required**: New features need tests
- **Security-first**: Review CLAUDE.md security guidelines

---

## Documentation

- **CLAUDE.md**: AI-assisted development guide (for Claude Code users)
- **GEMINI.md**: AI-assisted development guide (for Gemini users)
- **docs/scope/**: Original scope and architecture documents
- **docs/implementation-plans/**: Service implementation plans
- **deploy/compose/README.md**: Local infrastructure troubleshooting
- **API docs**: Run any service and visit `/swagger`

---

## FAQ

### Why "Dhadgar" as the solution name?

It's a code name (like "Dadgar" in the scope doc) to distinguish the codebase from the product name "Meridian Console." Think "Android" (code name) vs "Android OS" (product name).

### Can I run this without Docker?

Technically yes, but you'd need to install and configure PostgreSQL, RabbitMQ, Redis, Grafana, Prometheus, and Loki manually. Docker Compose is **much** easier.

### Do I need Azure to develop locally?

**No.** Everything runs locally via Docker. Azure resources are only needed if you're:
- Deploying to Azure
- Using Azure Key Vault for secrets
- Pushing container images to Azure Container Registry

### Why are some services just stubs?

This is intentional. The codebase provides the **shape** (architecture, structure, patterns) while features land incrementally. It's easier to maintain a consistent architecture if the structure exists first.

### How do I add a new service?

See the [Adding a New Service](#adding-a-new-service) section. Follow existing patterns (Gateway, Identity) for consistency.

### What's the difference between SaaS and KiP edition?

- **SaaS**: Multi-tenant, hosted by us, subscription billing
- **KiP (Knowledge is Power)**: Self-hosted, single-tenant, open-source

The codebase supports both—configuration and deployment differ.

---

## License

[To be determined]

---

## Need Help?

- **Issues**: https://github.com/SandboxServers/MeridianConsole/issues
- **Discussions**: https://github.com/SandboxServers/MeridianConsole/discussions
- **Discord**: [Coming soon]

---

**Built with ❤️ by the Meridian Console team**

🤖 This README was crafted with assistance from [Claude Code](https://claude.com/claude-code)

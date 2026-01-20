# Codebase Structure

**Analysis Date:** 2025-01-19

## Directory Layout

```
MeridianConsole/
├── .claude/                    # Claude Code configuration and agents
├── .github/                    # GitHub Actions workflows
├── .planning/                  # GSD planning documents
│   └── codebase/               # Codebase analysis documents
├── .vscode/                    # VS Code workspace settings
├── deploy/                     # Deployment configuration
│   ├── azure-pipelines/        # Pipeline templates
│   ├── compose/                # Docker Compose for local dev
│   ├── kubernetes/             # K8s manifests and Helm charts
│   └── scripts/                # Deployment scripts
├── docs/                       # Documentation
├── src/                        # Source code
│   ├── Agents/                 # Customer-hosted agent projects
│   ├── Dhadgar.{Service}/      # Backend microservices (13 services)
│   └── Shared/                 # Shared libraries (4 projects)
├── tests/                      # Test projects (24 projects)
├── Dhadgar.sln                 # Solution file
├── Directory.Build.props       # Shared MSBuild properties
├── Directory.Packages.props    # Central package management
├── global.json                 # .NET SDK pinning
├── azure-pipelines.yml         # Main CI/CD pipeline
└── CLAUDE.md                   # AI assistant instructions
```

## Directory Purposes

**`src/Agents/`:**
- Purpose: Customer-hosted node management agents
- Contains: Platform-specific executables
- Key files:
  - `Dhadgar.Agent.Core/` - Shared agent logic
  - `Dhadgar.Agent.Linux/` - Linux-specific agent
  - `Dhadgar.Agent.Windows/` - Windows-specific agent

**`src/Shared/`:**
- Purpose: Cross-cutting libraries shared by all services
- Contains: Contracts, messaging, middleware, utilities
- Key files:
  - `Dhadgar.Contracts/` - DTOs, message contracts, events
  - `Dhadgar.Messaging/` - MassTransit configuration
  - `Dhadgar.ServiceDefaults/` - Common middleware, health checks
  - `Dhadgar.Shared/` - Utilities and primitives

**`src/Dhadgar.Gateway/`:**
- Purpose: API Gateway (single entry point)
- Contains: YARP configuration, rate limiting, security middleware
- Key files:
  - `Program.cs` - Gateway initialization
  - `appsettings.json` - YARP routes and clusters config
  - `Middleware/` - Security headers, CORS, request enrichment
  - `Services/` - Cloudflare IP service, OpenAPI aggregation

**`src/Dhadgar.Identity/`:**
- Purpose: Authentication and authorization service
- Contains: OpenIddict, OAuth, user/org management, RBAC
- Key files:
  - `Program.cs` - OpenIddict configuration
  - `Data/` - EF Core DbContext and migrations
  - `Endpoints/` - API endpoint groups
  - `OAuth/` - Provider configuration (Steam, Discord, Epic, etc.)
  - `Authorization/` - Role definitions and permission service

**`src/Dhadgar.Secrets/`:**
- Purpose: Secret and certificate management (production-ready)
- Contains: Azure Key Vault integration, authorization, audit logging
- Key files:
  - `Program.cs` - Service configuration
  - `Authorization/` - Claims-based authorization
  - `Audit/` - Security audit logging
  - `Endpoints/` - Read and write API endpoints
  - `Validation/` - Secret name validation

**`src/Dhadgar.{Service}/` (typical service):**
- Purpose: Domain-specific business logic
- Contains: API endpoints, data access, domain logic
- Key files:
  - `Program.cs` - Service initialization
  - `appsettings.json` - Configuration
  - `Data/` - DbContext and entities (if DB-backed)
  - `Migrations/` or `Data/Migrations/` - EF Core migrations
  - `Dockerfile` - Container build
  - `Hello.cs` - Service identifier constant

**`src/Dhadgar.Scope/`:**
- Purpose: Documentation site (Astro/React/Tailwind)
- Contains: Static site with React components
- Key files:
  - `package.json` - Node.js dependencies
  - `astro.config.mjs` - Astro configuration
  - `tailwind.config.mjs` - Tailwind CSS config
  - `src/` - Pages and components
  - `Dhadgar.Scope.csproj` - .NET wrapper for build integration

**`src/Dhadgar.Panel/`:**
- Purpose: Main control plane UI (Astro/React - migrated from Blazor)
- Contains: Dashboard, server management UI
- Key files: Same pattern as Scope

**`src/Dhadgar.Cli/`:**
- Purpose: Command-line interface tool
- Contains: System.CommandLine-based CLI
- Key files:
  - `Program.cs` - Command registration (786 lines)
  - `Commands/` - Individual command implementations
  - `Configuration/` - CLI configuration
  - `Utilities/` - Helper functions

**`tests/`:**
- Purpose: Test projects (1:1 mapping with source projects)
- Contains: xUnit test projects
- Key files: `{ProjectName}Tests.csproj`, test classes

**`deploy/compose/`:**
- Purpose: Local development infrastructure
- Contains: Docker Compose for PostgreSQL, RabbitMQ, Redis, observability
- Key files:
  - `docker-compose.dev.yml` - Full local stack
  - `grafana/` - Grafana provisioning

**`deploy/kubernetes/`:**
- Purpose: Kubernetes deployment manifests
- Contains: Helm charts for all services
- Key files:
  - `helm/meridian-console/` - Main Helm chart
  - `helm/meridian-console/templates/{service}/` - Per-service templates

## Key File Locations

**Entry Points:**
- `src/Dhadgar.Gateway/Program.cs`: API Gateway
- `src/Dhadgar.Identity/Program.cs`: Identity service
- `src/Dhadgar.Cli/Program.cs`: CLI tool
- `src/Dhadgar.{Service}/Program.cs`: Each backend service

**Configuration:**
- `global.json`: .NET SDK version (10.0.100)
- `Directory.Build.props`: Shared MSBuild properties
- `Directory.Packages.props`: Central package versions
- `src/Dhadgar.{Service}/appsettings.json`: Service-specific config

**Database:**
- `src/Dhadgar.{Service}/Data/{Service}DbContext.cs`: EF Core context
- `src/Dhadgar.{Service}/Data/Migrations/`: EF Core migrations
- `src/Dhadgar.{Service}/Data/Entities/`: Entity classes

**API Endpoints:**
- `src/Dhadgar.{Service}/Endpoints/`: Endpoint groups
- `src/Dhadgar.Identity/Endpoints/`: Organization, Member, User, Role endpoints

**Messaging:**
- `src/Shared/Dhadgar.Contracts/{Domain}/`: Message contracts
- `src/Dhadgar.{Service}/Consumers/`: MassTransit consumers

**Testing:**
- `tests/Dhadgar.{Service}.Tests/`: Test project for each service

## Naming Conventions

**Files:**
- Services: `Dhadgar.{ServiceName}` (PascalCase)
- Endpoints: `{Domain}Endpoints.cs`
- Consumers: `{MessageName}Consumer.cs`
- Tests: `{ClassName}Tests.cs`

**Directories:**
- Agents: `Dhadgar.Agent.{Platform}`
- Services: `Dhadgar.{ServiceName}`
- Shared: `Dhadgar.{LibraryName}`
- Tests: `Dhadgar.{ServiceName}.Tests`

**Namespaces:**
- Root: `Dhadgar.{ServiceName}`
- Subdirectories: `Dhadgar.{ServiceName}.{Subdirectory}`
- Example: `Dhadgar.Identity.Endpoints`, `Dhadgar.Secrets.Authorization`

**Message Contracts:**
- Commands: `Send{Action}` (e.g., `SendEmailNotification`)
- Events: `{Subject}{Action}` (e.g., `UserAuthenticated`, `ServerStarted`)

## Where to Add New Code

**New Backend Service:**
1. Create `src/Dhadgar.{ServiceName}/` with standard structure:
   - `Program.cs`, `appsettings.json`, `Hello.cs`, `Dockerfile`
   - `Data/` if database-backed
   - `{ServiceName}.csproj` referencing ServiceDefaults
2. Add project to `Dhadgar.sln`
3. Create corresponding test project in `tests/`
4. Add route to Gateway `appsettings.json`
5. Add Helm templates in `deploy/kubernetes/helm/meridian-console/templates/{service}/`

**New API Endpoint Group:**
- Implementation: `src/Dhadgar.{Service}/Endpoints/{Domain}Endpoints.cs`
- Pattern: Static class with `Map(WebApplication app)` method
- Call from: `Program.cs` after middleware pipeline

**New Message Consumer:**
- Implementation: `src/Dhadgar.{Service}/Consumers/{MessageName}Consumer.cs`
- Register: In `Program.cs` via `x.AddConsumer<{ConsumerType}>()`
- Contract: Add message record to `src/Shared/Dhadgar.Contracts/{Domain}/`

**New EF Core Entity:**
- Entity: `src/Dhadgar.{Service}/Data/Entities/{EntityName}.cs`
- DbSet: Add to `{Service}DbContext.cs`
- Migration: `dotnet ef migrations add {MigrationName}`

**New Shared Utility:**
- Location: `src/Shared/Dhadgar.Shared/`
- Or domain-specific: `src/Shared/Dhadgar.Contracts/{Domain}/`

**New CLI Command:**
- Command class: `src/Dhadgar.Cli/Commands/{Group}/{CommandName}Command.cs`
- Registration: Add to `Program.cs` command hierarchy

**New Frontend Component (Astro/React):**
- Scope: `src/Dhadgar.Scope/src/components/`
- Panel: `src/Dhadgar.Panel/src/components/`
- Pages: `src/{Project}/src/pages/`

**New Test:**
- Location: `tests/Dhadgar.{Service}.Tests/`
- Pattern: `{ClassName}Tests.cs` with xUnit

## Special Directories

**`.claude/`:**
- Purpose: Claude Code configuration and specialized agents
- Generated: No
- Committed: Yes

**`_swa_publish/`:**
- Purpose: Azure Static Web Apps deployment output
- Generated: Yes (during build)
- Committed: No (in .gitignore)

**`bin/`, `obj/`:**
- Purpose: Build output
- Generated: Yes
- Committed: No (in .gitignore)

**`node_modules/`:**
- Purpose: Node.js dependencies
- Generated: Yes (npm install)
- Committed: No (in .gitignore)

**`.planning/`:**
- Purpose: GSD workflow planning documents
- Generated: By GSD commands
- Committed: Optional (project preference)

## Project Dependencies

**Allowed Dependencies (via ProjectReference):**
- All services may reference: Contracts, Messaging, ServiceDefaults, Shared
- Agents may reference: Contracts, Shared
- Tests reference: Corresponding source project

**Forbidden Dependencies:**
- Services must NOT reference other services directly
- No compile-time coupling between services

**Package Management:**
- Versions defined in: `Directory.Packages.props`
- Projects reference packages without version attribute
- Update all versions centrally

---

*Structure analysis: 2025-01-19*

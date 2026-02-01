# Technology Stack

**Analysis Date:** 2025-01-19

## Languages

**Primary:**
- C# (latest) - .NET microservices, agents, CLI (`src/Dhadgar.*/`)
- TypeScript 5.7.2 - Frontend applications (`src/Dhadgar.Scope/`, `src/Dhadgar.Panel/`, `src/Dhadgar.ShoppingCart/`)
- JavaScript (ES Modules) - BetterAuth service (`src/Dhadgar.BetterAuth/`)

**Secondary:**
- YAML - CI/CD pipelines, Docker Compose, OpenTelemetry config
- JSON - Configuration files, package manifests

## Runtime

**Environment:**
- .NET 10.0.100 (pinned in `global.json`)
- Node.js 20+ (for frontend builds)

**Package Manager:**
- NuGet (Central Package Management via `Directory.Packages.props`)
- npm (for Node.js projects)
- Lockfiles: `package-lock.json` present in Node.js projects

## Frameworks

**Core:**
- ASP.NET Core 10.0 - All .NET microservices
- Astro 5.1.1 - Frontend framework (`src/Dhadgar.Scope/`, `src/Dhadgar.Panel/`, `src/Dhadgar.ShoppingCart/`)
- React 18.3.1 - UI components within Astro
- Express 4.19.2 - BetterAuth service (`src/Dhadgar.BetterAuth/`)

**Testing:**
- xUnit 2.9.2 - .NET test framework
- NSubstitute 5.3.0 - .NET mocking
- FluentAssertions 8.3.0 - Assertion library
- Microsoft.NET.Test.Sdk 17.12.0 - Test runner
- coverlet.collector 6.0.4 - Code coverage

**Build/Dev:**
- MSBuild - .NET build system
- Docker - Containerization (16 Dockerfiles in `src/`)
- Tailwind CSS 3.4.16 - Styling
- ESLint 9.39.2 - JavaScript/TypeScript linting (`src/Dhadgar.Scope/`)
- Prettier 3.7.4 - Code formatting

## Key Dependencies

**Critical:**
- YARP.ReverseProxy 2.3.0 - API Gateway routing (`src/Dhadgar.Gateway/`)
- MassTransit 8.3.6 - Message bus abstraction (`src/Shared/Dhadgar.Messaging/`)
- Entity Framework Core 10.0 - ORM for PostgreSQL
- OpenIddict 7.0.0 - OpenID Connect server (`src/Dhadgar.Identity/`)
- better-auth 1.3.1 - Authentication (`src/Dhadgar.BetterAuth/`)

**Infrastructure:**
- Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0 - PostgreSQL driver
- MassTransit.RabbitMQ 8.3.6 - RabbitMQ transport
- StackExchange.Redis 2.7.27 - Redis client
- Discord.Net 3.18.0 - Discord bot (`src/Dhadgar.Discord/`)
- SendGrid 9.29.3 - Email delivery (`src/Dhadgar.Notifications/`)
- Microsoft.Graph 5.78.0 - Office 365 integration (`src/Dhadgar.Notifications/`)

**Azure:**
- Azure.Identity 1.11.4 - Azure authentication
- Azure.Security.KeyVault.Secrets 4.6.0 - Secret management
- Azure.Security.KeyVault.Keys 4.7.0 - Key management
- Azure.Security.KeyVault.Certificates 4.6.0 - Certificate management
- Azure.ResourceManager.KeyVault 1.3.3 - Key Vault management

**Observability:**
- OpenTelemetry 1.14.0 - Distributed tracing and metrics
- OpenTelemetry.Instrumentation.AspNetCore 1.14.0
- OpenTelemetry.Exporter.OpenTelemetryProtocol 1.14.0

**CLI:**
- Spectre.Console 0.49.1 - Rich console output (`src/Dhadgar.Cli/`)
- Spectre.Console.Cli 0.49.1 - CLI framework
- System.CommandLine 2.0.0-beta4 - Command parsing
- Refit 9.0.2 - HTTP client generation

**Auth Providers:**
- AspNet.Security.OpenId.Steam 10.0.0 - Steam authentication
- AspNet.Security.OAuth.BattleNet 10.0.0 - Battle.net authentication
- Microsoft.AspNetCore.Authentication.MicrosoftAccount 10.0.0 - Microsoft auth

**API Documentation:**
- Swashbuckle.AspNetCore 10.1.0 - Swagger/OpenAPI
- Microsoft.OpenApi 2.3.0 - OpenAPI spec

**Security:**
- SecurityCodeScan.VS2019 5.6.7 - SAST analyzer (enabled for Identity and Agent projects)
- System.IdentityModel.Tokens.Jwt 8.14.0 - JWT handling

## Configuration

**Environment:**
- Standard ASP.NET Core configuration hierarchy
- User secrets for local development (`dotnet user-secrets`)
- Environment variables for production
- `appsettings.json` and `appsettings.Development.json` per service

**Key configs required:**
- `ConnectionStrings:Postgres` - PostgreSQL connection
- `RabbitMq:Host/Username/Password` - Message bus
- `Redis:ConnectionString` - Cache/session
- `Auth:Issuer/Audience/SigningKey` - JWT configuration
- `Secrets:KeyVaultUri` - Azure Key Vault URI
- `Discord:BotToken` - Discord bot credentials
- `OpenTelemetry:OtlpEndpoint` - Telemetry collector

**Build:**
- `Directory.Build.props` - Shared build properties (net10.0, nullable enabled, analyzers)
- `Directory.Packages.props` - Central package versions
- `global.json` - SDK version pinning

## Platform Requirements

**Development:**
- .NET SDK 10.0.100
- Node.js 20+
- Docker Desktop (for local infrastructure)
- PostgreSQL 16, RabbitMQ 3, Redis 7 (via Docker Compose)

**Production:**
- Docker containers (Alpine-based .NET runtime)
- Azure Container Registry (`meridianconsoleacr-etdvg4cthscffqdf.azurecr.io`)
- Azure Static Web Apps (frontend hosting)
- Azure Key Vault (secret management)
- Cloudflare (WAF/CDN/DDoS)

**Container Images:**
- Base: `mcr.microsoft.com/dotnet/sdk:10.0` (build)
- Runtime: `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` (production)
- Non-root user (`appuser`) enforced in all Dockerfiles
- Health checks enabled (`/healthz` endpoint)

---

*Stack analysis: 2025-01-19*

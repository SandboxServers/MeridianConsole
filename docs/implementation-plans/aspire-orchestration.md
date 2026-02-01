# .NET Aspire Migration Implementation Plan

> **Status**: Ready for implementation
> **Last Updated**: 2026-01-31 (Phase 3 expanded with per-service configuration)
> **Issue**: #48 - Adopt .NET Aspire as orchestration foundation
> **Current State**: Investigation complete, ready for phased implementation

## Executive Summary

This plan migrates Meridian Console from Docker Compose-based local development to .NET Aspire orchestration. The migration adds an **AppHost** project for infrastructure orchestration while **layering** the existing `Dhadgar.ServiceDefaults` on top of Aspire's ServiceDefaults pattern.

**Key Decisions:**
1. **Layer, don't replace**: Keep Dhadgar.ServiceDefaults but layer it on Aspire's patterns
2. **Gradual migration**: Start with infrastructure, then add services incrementally
3. **Preserve Docker Compose**: Keep it as an alternative for CI/CD and production

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Current vs Target State](#current-vs-target-state)
3. [Component Analysis](#component-analysis)
4. [Phase 1: AppHost Project Creation](#phase-1-apphost-project-creation)
5. [Phase 2: ServiceDefaults Refactoring](#phase-2-servicedefaults-refactoring)
6. [Phase 3: Service Integration](#phase-3-service-integration)
7. [Phase 4: Developer Experience](#phase-4-developer-experience)
8. [Phase 5: Testing & Validation](#phase-5-testing--validation)
9. [Pre-Incorporated PR Feedback Patterns](#pre-incorporated-pr-feedback-patterns)
10. [Success Criteria](#success-criteria)

---

## Architecture Overview

### Target Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Dhadgar.AppHost                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Infrastructure Resources                                            │   │
│  │  ├─ PostgreSQL (builder.AddPostgres)                                │   │
│  │  ├─ Redis (builder.AddRedis)                                        │   │
│  │  ├─ RabbitMQ (builder.AddRabbitMQ)                                  │   │
│  │  └─ OpenTelemetry Collector (optional, Aspire Dashboard provides)   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Service Projects                                                    │   │
│  │  ├─ Gateway (.WithReference(redis))                                 │   │
│  │  ├─ Identity (.WithReference(postgres), .WaitFor(postgres))         │   │
│  │  ├─ Nodes (.WithReference(postgres), .WithReference(rabbitmq))      │   │
│  │  ├─ Secrets                                                         │   │
│  │  └─ ... (other services)                                            │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Aspire Dashboard                                    │
│  ├─ Traces (distributed tracing visualization)                             │
│  ├─ Metrics (runtime, HTTP, custom)                                        │
│  ├─ Logs (structured log aggregation)                                      │
│  └─ Resources (service health, connection strings)                         │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Project Dependencies

```
Dhadgar.AppHost
├─ References all service projects (Projects.Dhadgar_Gateway, etc.)
├─ Aspire.Hosting.PostgreSQL
├─ Aspire.Hosting.Redis
├─ Aspire.Hosting.RabbitMQ
└─ Aspire.Hosting.AppHost (implicit from SDK)

Dhadgar.ServiceDefaults (refactored)
├─ Aspire.ServiceDefaults (new dependency)
├─ Dhadgar.Contracts
└─ Existing packages (OpenTelemetry, health checks, etc.)

All Services
├─ Dhadgar.ServiceDefaults
├─ Aspire.{Integration} packages (e.g., Aspire.Npgsql.EntityFrameworkCore.PostgreSQL)
└─ Existing dependencies
```

---

## Current vs Target State

### Docker Compose Infrastructure (Current)

| Service | Image | Ports | Aspire Equivalent |
|---------|-------|-------|-------------------|
| `postgres` | `postgres:16` | 5432 | `builder.AddPostgres("db")` |
| `rabbitmq` | `rabbitmq:3-management` | 5672, 15672 | `builder.AddRabbitMQ("messaging")` |
| `redis` | `redis:7` | 6379 | `builder.AddRedis("cache")` |
| `otel-collector` | `otel/opentelemetry-collector-contrib:0.105.0` | 4317, 4318 | Built into Aspire Dashboard |
| `loki` | `grafana/loki:2.9.6` | 3100 | Optional, keep for production |
| `prometheus` | `prom/prometheus:v2.53.1` | 9090 | Aspire Dashboard for dev |
| `grafana` | `grafana/grafana:latest` | 3000 | Aspire Dashboard for dev |

### ServiceDefaults Component Analysis

| Component | Category | Action | Rationale |
|-----------|----------|--------|-----------|
| `AddDhadgarServiceDefaults()` | Core | **LAYER** | Calls Aspire's `AddServiceDefaults()` then adds Dhadgar-specific config |
| `ConfigureOpenTelemetry()` | Telemetry | **REPLACE** | Aspire configures OTLP automatically |
| `AddHealthChecks()` | Health | **LAYER** | Keep custom checks, use Aspire's endpoints |
| `MapDhadgarDefaultEndpoints()` | Health | **LAYER** | Aspire provides `/health`, `/alive`, add custom `/healthz`, `/livez`, `/readyz` |
| `UseDhadgarMiddleware()` | Middleware | **KEEP** | Correlation, tenant enrichment, request logging are Dhadgar-specific |
| `CorrelationMiddleware` | Middleware | **KEEP** | Custom correlation ID handling |
| `TenantEnrichmentMiddleware` | Middleware | **KEEP** | Multi-tenant logging scope |
| `RequestLoggingMiddleware` | Middleware | **KEEP** | Source-generated HTTP logging |
| `AddStrictJsonSerialization()` | Security | **KEEP** | Security hardening |
| `AddOrganizationContext()` | Multi-tenant | **KEEP** | Platform-specific |
| `AddDhadgarLogging()` | Logging | **LAYER** | Add PII redaction on top of Aspire logging |
| Health check helpers | Health | **LAYER** | Keep Postgres/Redis/RabbitMQ checks with Aspire wiring |

---

## Phase 1: AppHost Project Creation

**Goal**: Create the Aspire AppHost project with infrastructure orchestration.

### 1.1 Create AppHost Project

```bash
# From solution root
dotnet new aspire-apphost -n Dhadgar.AppHost -o src/Dhadgar.AppHost
dotnet sln add src/Dhadgar.AppHost/Dhadgar.AppHost.csproj
```

### 1.2 Configure AppHost Project File

Create `src/Dhadgar.AppHost/Dhadgar.AppHost.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <Sdk Name="Aspire.AppHost.Sdk" Version="9.2.0" />

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsAspireHost>true</IsAspireHost>
    <UserSecretsId>dhadgar-apphost</UserSecretsId>
  </PropertyGroup>

  <ItemGroup>
    <!-- Aspire Hosting Packages -->
    <PackageReference Include="Aspire.Hosting.PostgreSQL" />
    <PackageReference Include="Aspire.Hosting.Redis" />
    <PackageReference Include="Aspire.Hosting.RabbitMQ" />
  </ItemGroup>

  <ItemGroup>
    <!-- Service Project References -->
    <ProjectReference Include="..\Dhadgar.Gateway\Dhadgar.Gateway.csproj" />
    <ProjectReference Include="..\Dhadgar.Identity\Dhadgar.Identity.csproj" />
    <ProjectReference Include="..\Dhadgar.Nodes\Dhadgar.Nodes.csproj" />
    <ProjectReference Include="..\Dhadgar.Secrets\Dhadgar.Secrets.csproj" />
    <ProjectReference Include="..\Dhadgar.BetterAuth\Dhadgar.BetterAuth.csproj" />
    <ProjectReference Include="..\Dhadgar.Billing\Dhadgar.Billing.csproj" />
    <ProjectReference Include="..\Dhadgar.Servers\Dhadgar.Servers.csproj" />
    <ProjectReference Include="..\Dhadgar.Tasks\Dhadgar.Tasks.csproj" />
    <ProjectReference Include="..\Dhadgar.Console\Dhadgar.Console.csproj" />
    <ProjectReference Include="..\Dhadgar.Mods\Dhadgar.Mods.csproj" />
    <ProjectReference Include="..\Dhadgar.Notifications\Dhadgar.Notifications.csproj" />
    <ProjectReference Include="..\Dhadgar.Discord\Dhadgar.Discord.csproj" />
  </ItemGroup>

</Project>
```

### 1.3 Implement AppHost Program.cs

Create `src/Dhadgar.AppHost/Program.cs`:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════════════════════
// Infrastructure Resources
// ═══════════════════════════════════════════════════════════════════════════

// PostgreSQL - Primary database for all services
var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("dhadgar-postgres-data")
    .WithPgAdmin(); // Optional: pgAdmin for database management

// Create databases for each service (multi-tenant database-per-service pattern)
var platformDb = postgres.AddDatabase("dhadgar_platform");
var identityDb = postgres.AddDatabase("dhadgar_identity");
var billingDb = postgres.AddDatabase("dhadgar_billing");

// Redis - Caching and session storage
var redis = builder.AddRedis("cache")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("dhadgar-redis-data")
    .WithRedisCommander(); // Optional: Redis Commander for cache inspection

// RabbitMQ - Message bus for async communication
var rabbitmq = builder.AddRabbitMQ("messaging")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("dhadgar-rabbitmq-data")
    .WithManagementPlugin(); // RabbitMQ Management UI

// ═══════════════════════════════════════════════════════════════════════════
// Core Services
// ═══════════════════════════════════════════════════════════════════════════

// Gateway - API entry point (YARP reverse proxy)
var gateway = builder.AddProject<Projects.Dhadgar_Gateway>("gateway")
    .WithReference(redis)
    .WithExternalHttpEndpoints()
    .WaitFor(redis);

// Identity - User/org/role management
var identity = builder.AddProject<Projects.Dhadgar_Identity>("identity")
    .WithReference(identityDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

// Nodes - Agent enrollment, mTLS CA, heartbeats
var nodes = builder.AddProject<Projects.Dhadgar_Nodes>("nodes")
    .WithReference(platformDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

// Secrets - Azure Key Vault integration
var secrets = builder.AddProject<Projects.Dhadgar_Secrets>("secrets");

// BetterAuth - Passwordless authentication
var betterauth = builder.AddProject<Projects.Dhadgar_BetterAuth>("betterauth")
    .WithReference(identityDb)
    .WaitFor(postgres);

// ═══════════════════════════════════════════════════════════════════════════
// Stub Services (minimal dependencies until implemented)
// ═══════════════════════════════════════════════════════════════════════════

var billing = builder.AddProject<Projects.Dhadgar_Billing>("billing")
    .WithReference(billingDb)
    .WaitFor(postgres);

var servers = builder.AddProject<Projects.Dhadgar_Servers>("servers")
    .WithReference(platformDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

var tasks = builder.AddProject<Projects.Dhadgar_Tasks>("tasks")
    .WithReference(platformDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

var console = builder.AddProject<Projects.Dhadgar_Console>("console")
    .WithReference(redis)
    .WaitFor(redis);

var mods = builder.AddProject<Projects.Dhadgar_Mods>("mods")
    .WithReference(platformDb)
    .WaitFor(postgres);

var notifications = builder.AddProject<Projects.Dhadgar_Notifications>("notifications")
    .WithReference(platformDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

var discord = builder.AddProject<Projects.Dhadgar_Discord>("discord")
    .WithReference(platformDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

builder.Build().Run();
```

### 1.4 Add Package Versions to Directory.Packages.props

```xml
<!-- Aspire Hosting -->
<PackageVersion Include="Aspire.Hosting.PostgreSQL" Version="9.2.0" />
<PackageVersion Include="Aspire.Hosting.Redis" Version="9.2.0" />
<PackageVersion Include="Aspire.Hosting.RabbitMQ" Version="9.2.0" />

<!-- Aspire Service Integrations -->
<PackageVersion Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.2.0" />
<PackageVersion Include="Aspire.StackExchange.Redis" Version="9.2.0" />
<PackageVersion Include="Aspire.RabbitMQ.Client" Version="9.2.0" />
```

### Deliverables
- [ ] `src/Dhadgar.AppHost/Dhadgar.AppHost.csproj`
- [ ] `src/Dhadgar.AppHost/Program.cs`
- [ ] `src/Dhadgar.AppHost/appsettings.json` (if needed)
- [ ] Updated `Directory.Packages.props`
- [ ] Solution file updated

---

## Phase 2: ServiceDefaults Refactoring

**Goal**: Layer Dhadgar.ServiceDefaults on top of Aspire's ServiceDefaults pattern.

### 2.1 Add Aspire ServiceDefaults Reference

Update `src/Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj`:

```xml
<ItemGroup>
  <!-- Aspire Service Defaults (core pattern) -->
  <PackageReference Include="Aspire.ServiceDefaults" />

  <!-- Keep existing packages for Dhadgar-specific functionality -->
  <!-- ... existing packages ... -->
</ItemGroup>
```

### 2.2 Refactor ServiceDefaultsExtensions.cs

The key insight is that Aspire's `AddServiceDefaults()` configures:
- OpenTelemetry (tracing, metrics, logging with OTLP export)
- Health checks (`/health`, `/alive`)
- Service discovery
- HTTP client resilience

We **layer** Dhadgar-specific configuration on top:

```csharp
using Dhadgar.ServiceDefaults.Health;
using Dhadgar.ServiceDefaults.Logging;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.MultiTenancy;
using Dhadgar.ServiceDefaults.Serialization;
using Dhadgar.ServiceDefaults.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dhadgar.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Adds Aspire service defaults plus Dhadgar-specific configuration.
    /// </summary>
    /// <remarks>
    /// This method:
    /// <list type="bullet">
    ///   <item>Calls Aspire's AddServiceDefaults() for OTel, health checks, service discovery</item>
    ///   <item>Adds Dhadgar multi-tenant organization context</item>
    ///   <item>Configures strict JSON serialization</item>
    ///   <item>Adds PII redaction to logging</item>
    ///   <item>Registers source-generated request logging</item>
    ///   <item>Adds custom DhadgarActivitySource for business spans</item>
    /// </list>
    /// </remarks>
    public static IHostApplicationBuilder AddDhadgarServiceDefaults(
        this IHostApplicationBuilder builder)
    {
        // 1. Call Aspire's service defaults (OTel, health checks, service discovery, resilience)
        builder.AddServiceDefaults();

        // 2. Add Dhadgar-specific services
        builder.Services.AddOrganizationContext();
        builder.Services.AddSingleton<RequestLoggingMessages>();
        builder.Services.AddStrictJsonSerialization();

        // 3. Add PII redaction to logging (layers on Aspire's OTel logging)
        builder.Services.AddDhadgarLogging();

        // 4. Add custom ActivitySource for business-level tracing
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
        {
            tracing.AddSource(DhadgarActivitySource.Name);
        });

        return builder;
    }

    /// <summary>
    /// Adds Dhadgar service defaults with explicit health check dependencies.
    /// </summary>
    /// <remarks>
    /// Use this overload when you need to add service-specific health checks
    /// beyond what Aspire provides automatically.
    /// </remarks>
    public static IHostApplicationBuilder AddDhadgarServiceDefaults(
        this IHostApplicationBuilder builder,
        HealthCheckDependencies dependencies,
        Action<TracerProviderBuilder>? configureTracing = null)
    {
        // Call base configuration
        builder.AddDhadgarServiceDefaults();

        // Add additional health checks based on flags
        // Note: Aspire automatically adds health checks for resources wired via WithReference()
        // These are for services that need explicit health check configuration
        var healthChecks = builder.Services.AddHealthChecks();

        if (dependencies.HasFlag(HealthCheckDependencies.Postgres))
        {
            // Aspire handles this via Aspire.Npgsql.EntityFrameworkCore.PostgreSQL
            // Only add if not using Aspire integration packages
        }

        if (dependencies.HasFlag(HealthCheckDependencies.Redis))
        {
            // Aspire handles this via Aspire.StackExchange.Redis
        }

        if (dependencies.HasFlag(HealthCheckDependencies.RabbitMq))
        {
            // Aspire handles this via Aspire.RabbitMQ.Client
        }

        // Allow service-specific tracing configuration
        if (configureTracing is not null)
        {
            builder.Services.ConfigureOpenTelemetryTracerProvider(configureTracing);
        }

        return builder;
    }

    /// <summary>
    /// Maps Dhadgar default endpoints and middleware.
    /// </summary>
    /// <remarks>
    /// Call after Aspire's MapDefaultEndpoints() to add Dhadgar-specific endpoints
    /// and the middleware pipeline.
    /// </remarks>
    public static WebApplication MapDhadgarDefaults(this WebApplication app)
    {
        // Map Aspire's default endpoints (/health, /alive)
        app.MapDefaultEndpoints();

        // Map additional Dhadgar health check endpoints for Kubernetes
        app.MapDhadgarDefaultEndpoints();

        // Register Dhadgar middleware pipeline
        app.UseDhadgarMiddleware();

        return app;
    }

    /// <summary>
    /// Maps additional Kubernetes-style health check endpoints.
    /// </summary>
    public static WebApplication MapDhadgarDefaultEndpoints(this WebApplication app)
    {
        // Aspire provides /health and /alive
        // We add /healthz, /livez, /readyz for Kubernetes compatibility

        app.MapHealthChecks("/healthz", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = WriteHealthResponseAsync
        })
        .AllowAnonymous()
        .WithTags("Health");

        app.MapHealthChecks("/livez", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            ResponseWriter = WriteHealthResponseAsync
        })
        .AllowAnonymous()
        .WithTags("Health");

        app.MapHealthChecks("/readyz", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthResponseAsync
        })
        .AllowAnonymous()
        .WithTags("Health");

        return app;
    }

    /// <summary>
    /// Registers the Dhadgar middleware pipeline.
    /// </summary>
    public static WebApplication UseDhadgarMiddleware(this WebApplication app)
    {
        // 1. Correlation - Sets CorrelationId and RequestId
        app.UseMiddleware<CorrelationMiddleware>();

        // 2. Tenant Enrichment - Adds TenantId, ServiceName to logging scope
        app.UseMiddleware<TenantEnrichmentMiddleware>();

        // 3. Request Logging - Logs HTTP requests with full context
        app.UseMiddleware<RequestLoggingMiddleware>();

        return app;
    }

    private static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
    {
        // ... existing implementation ...
    }
}
```

### 2.3 Update Service Program.cs Files

Each service's `Program.cs` changes from:

```csharp
// Before (current)
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDhadgarServiceDefaults(
    builder.Configuration,
    HealthCheckDependencies.Postgres | HealthCheckDependencies.RabbitMq);

var app = builder.Build();
app.MapDhadgarDefaultEndpoints();
```

To:

```csharp
// After (with Aspire)
var builder = WebApplication.CreateBuilder(args);
builder.AddDhadgarServiceDefaults();

var app = builder.Build();
app.MapDhadgarDefaults();
```

### Deliverables
- [ ] Updated `Dhadgar.ServiceDefaults.csproj` with Aspire reference
- [ ] Refactored `ServiceDefaultsExtensions.cs`
- [ ] Updated `HealthCheckDependencies.cs` (may simplify)
- [ ] Unit tests for layered configuration

---

## Phase 3: Service Integration

**Goal**: Update each service to use Aspire integration packages with correct middleware configuration.

### 3.1 Service Requirements Matrix

Each service has specific infrastructure and middleware requirements. This matrix ensures accurate configuration:

| Service | Port | PostgreSQL | Redis | RabbitMQ | Multi-Tenant | Status |
|---------|------|------------|-------|----------|--------------|--------|
| **Gateway** | 5000 | - | ✅ Rate limiting | - | - | Production |
| **Identity** | 5010 | ✅ dhadgar_identity | ✅ Token replay | ✅ Events | ✅ | Production |
| **Nodes** | 5040 | ✅ dhadgar_platform | - | ✅ Events | ✅ | Production |
| **Secrets** | 5110 | - (Azure KV) | - | ✅ Registered | - | Production |
| **BetterAuth** | 5130 | ✅ Shared with Identity | - | - | - | Production |
| **Billing** | 5020 | ✅ dhadgar_billing | - | ✅ | - | Stub |
| **Servers** | 5030 | ✅ dhadgar_platform | - | ✅ | - | Stub |
| **Tasks** | 5050 | ✅ dhadgar_platform | - | ✅ | - | Stub |
| **Console** | 5070 | - | ✅ SignalR backplane | ✅ | - | Stub |
| **Mods** | 5080 | ✅ dhadgar_platform | - | ✅ | - | Stub |
| **Notifications** | 5090 | ✅ dhadgar_platform | - | ✅ Consumers | ✅ | Production |
| **Discord** | 5120 | ✅ dhadgar_platform | - | ✅ Consumers | ✅ | Production |

### 3.2 Middleware Requirements Matrix

Not all services need all middlewares. This matrix shows the complete requirements for all 12 microservices:

#### Core Middleware (Required by ALL services)

| Middleware | Purpose | Required By |
|------------|---------|-------------|
| `CorrelationMiddleware` | Distributed tracing (CorrelationId, RequestId) | All 12 services |
| `RequestLoggingMiddleware` | HTTP request/response logging | All 12 services |
| `ProblemDetailsMiddleware` | RFC 7807/9457 error responses | All 12 services |

#### Per-Service Middleware Configuration

**Production Services (7):**

| Middleware | Gateway | Identity | Nodes | Secrets | BetterAuth | Notifications | Discord |
|------------|:-------:|:--------:|:-----:|:-------:|:----------:|:-------------:|:-------:|
| `TenantEnrichmentMiddleware` | ✅ | ✅ | ✅ | - | - | ✅ | ✅ |
| `SecurityHeadersMiddleware` | ✅ | - | - | - | - | - | - |
| `AuditMiddleware` | - | ✅ | - | - | - | - | - |
| Authentication | ✅ | ✅ | ✅ | ✅ | - | ✅ | ✅ |
| Authorization | ✅ | ✅ | ✅ | ✅ | - | ✅ | ✅ |
| RateLimiter | ✅ | ✅ | - | ✅ | - | - | - |
| CORS | ✅ | - | - | - | - | - | - |

**Stub Services (5):**

| Middleware | Billing | Servers | Tasks | Console | Mods |
|------------|:-------:|:-------:|:-----:|:-------:|:----:|
| `TenantEnrichmentMiddleware` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `SecurityHeadersMiddleware` | - | - | - | - | - |
| `AuditMiddleware` | ✅ | ✅ | - | - | - |
| Authentication | ✅ | ✅ | ✅ | ✅ | ✅ |
| Authorization | ✅ | ✅ | ✅ | ✅ | ✅ |
| RateLimiter | - | - | - | - | - |
| CORS | - | - | - | - | - |

#### Middleware Rationale

| Service | TenantEnrichment | Audit | RateLimiter | Notes |
|---------|:----------------:|:-----:|:-----------:|-------|
| **Gateway** | ✅ | - | ✅ | Public entry point, rate limits all traffic |
| **Identity** | ✅ | ✅ | ✅ | User/role changes audited, auth endpoints rate limited |
| **Nodes** | ✅ | - | - | Node changes tracked via heartbeats, not audit middleware |
| **Secrets** | - | - | ✅ | Platform-level (not tenant-scoped), Key Vault has own audit |
| **BetterAuth** | - | - | - | Auth provider, handles own session management |
| **Notifications** | ✅ | - | - | Org-scoped notification delivery |
| **Discord** | ✅ | - | - | Org-scoped Discord webhooks |
| **Billing** | ✅ | ✅ | - | Org-scoped, payment changes require audit trail |
| **Servers** | ✅ | ✅ | - | Org-scoped, server lifecycle changes audited |
| **Tasks** | ✅ | - | - | Org-scoped background jobs |
| **Console** | ✅ | - | - | Org-scoped SignalR sessions |
| **Mods** | ✅ | - | - | Org-scoped or global mod catalog |

### 3.3 Aspire Integration Packages Per Service

| Service | Aspire Packages |
|---------|-----------------|
| **Gateway** | `Aspire.StackExchange.Redis` |
| **Identity** | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, `Aspire.RabbitMQ.Client`, `Aspire.StackExchange.Redis` |
| **Nodes** | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, `Aspire.RabbitMQ.Client` |
| **Secrets** | `Aspire.RabbitMQ.Client` (for future event publishing) |
| **BetterAuth** | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` |
| **Billing** | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, `Aspire.RabbitMQ.Client` |
| **Servers** | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, `Aspire.RabbitMQ.Client` |
| **Tasks** | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, `Aspire.RabbitMQ.Client` |
| **Console** | `Aspire.StackExchange.Redis`, `Aspire.RabbitMQ.Client` |
| **Mods** | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, `Aspire.RabbitMQ.Client` |
| **Notifications** | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, `Aspire.RabbitMQ.Client` |
| **Discord** | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, `Aspire.RabbitMQ.Client` |

---

### 3.4 ServiceDefaults Configuration Options

Create middleware configuration options to support per-service needs:

```csharp
// src/Shared/Dhadgar.ServiceDefaults/DhadgarServiceOptions.cs

namespace Dhadgar.ServiceDefaults;

/// <summary>
/// Options for configuring Dhadgar service defaults.
/// </summary>
public sealed class DhadgarServiceOptions
{
    /// <summary>
    /// Enable tenant enrichment middleware (for multi-tenant services).
    /// Default: true
    /// </summary>
    public bool EnableTenantEnrichment { get; set; } = true;

    /// <summary>
    /// Enable request logging middleware.
    /// Default: true
    /// </summary>
    public bool EnableRequestLogging { get; set; } = true;

    /// <summary>
    /// Enable audit middleware for tracking changes.
    /// Default: false
    /// </summary>
    public bool EnableAuditMiddleware { get; set; } = false;

    /// <summary>
    /// Enable security headers middleware (Gateway only).
    /// Default: false
    /// </summary>
    public bool EnableSecurityHeaders { get; set; } = false;
}
```

Update `UseDhadgarMiddleware`:

```csharp
public static WebApplication UseDhadgarMiddleware(
    this WebApplication app,
    DhadgarServiceOptions? options = null)
{
    options ??= new DhadgarServiceOptions();

    // 1. Correlation - Always enabled for distributed tracing
    app.UseMiddleware<CorrelationMiddleware>();

    // 2. Security Headers - Gateway only
    if (options.EnableSecurityHeaders)
    {
        app.UseMiddleware<SecurityHeadersMiddleware>();
    }

    // 3. Tenant Enrichment - Multi-tenant services only
    if (options.EnableTenantEnrichment)
    {
        app.UseMiddleware<TenantEnrichmentMiddleware>();
    }

    // 4. Request Logging - Most services
    if (options.EnableRequestLogging)
    {
        app.UseMiddleware<RequestLoggingMiddleware>();
    }

    // 5. Audit - Services that track changes
    if (options.EnableAuditMiddleware)
    {
        app.UseMiddleware<AuditMiddleware>();
    }

    return app;
}
```

---

### 3.5 Production Services Implementation

#### Gateway Service

```csharp
// src/Dhadgar.Gateway/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddDhadgarServiceDefaults();

// Aspire-integrated Redis for rate limiting and caching
builder.AddRedisClient("cache");

// YARP configuration
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Rate limiting, CORS, etc. (existing config)

var app = builder.Build();

app.MapDhadgarDefaults(new DhadgarServiceOptions
{
    EnableTenantEnrichment = true,
    EnableSecurityHeaders = true,
    EnableAuditMiddleware = false
});

// Gateway-specific middleware
app.UseCors();
app.UseRateLimiter();
app.MapReverseProxy();

app.Run();
```

#### Identity Service

```csharp
// src/Dhadgar.Identity/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddDhadgarServiceDefaults();

// Aspire-integrated database
builder.AddNpgsqlDbContext<IdentityDbContext>("dhadgar_identity");

// Aspire-integrated Redis for token replay store
builder.AddRedisClient("cache");

// Aspire-integrated RabbitMQ for MassTransit
builder.AddRabbitMQClient("messaging");

// MassTransit configuration (uses Aspire's RabbitMQ connection)
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        // Connection auto-configured by Aspire
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.MapDhadgarDefaults(new DhadgarServiceOptions
{
    EnableTenantEnrichment = true,
    EnableAuditMiddleware = true
});

app.UseAuthentication();
app.UseAuthorization();
app.MapEndpoints();

app.Run();
```

#### Nodes Service

```csharp
// src/Dhadgar.Nodes/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddDhadgarServiceDefaults();

// Aspire-integrated database
builder.AddNpgsqlDbContext<NodesDbContext>("dhadgar_platform");

// Aspire-integrated RabbitMQ
builder.AddRabbitMQClient("messaging");

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.MapDhadgarDefaults(new DhadgarServiceOptions
{
    EnableTenantEnrichment = true,
    EnableAuditMiddleware = false
});

app.UseAuthentication();
app.UseAuthorization();
app.MapEndpoints();

app.Run();
```

#### Secrets Service

```csharp
// src/Dhadgar.Secrets/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddDhadgarServiceDefaults();

// No database - uses Azure Key Vault
// No Redis
// RabbitMQ for future event publishing
builder.AddRabbitMQClient("messaging");

var app = builder.Build();

app.MapDhadgarDefaults(new DhadgarServiceOptions
{
    EnableTenantEnrichment = false, // Not multi-tenant
    EnableAuditMiddleware = false   // Audit via Key Vault logs
});

app.UseAuthentication();
app.UseAuthorization();
app.MapEndpoints();

app.Run();
```

#### Notifications Service

```csharp
// src/Dhadgar.Notifications/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddDhadgarServiceDefaults();

// Aspire-integrated database
builder.AddNpgsqlDbContext<NotificationsDbContext>("dhadgar_platform");

// Aspire-integrated RabbitMQ for MassTransit consumers
builder.AddRabbitMQClient("messaging");

builder.Services.AddMassTransit(x =>
{
    // Register notification consumers
    x.AddConsumer<SendEmailNotificationConsumer>();
    x.AddConsumer<SendWebhookNotificationConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.MapDhadgarDefaults(new DhadgarServiceOptions
{
    EnableTenantEnrichment = true // Notifications are org-scoped
});

app.UseAuthentication();
app.UseAuthorization();
app.MapEndpoints();

app.Run();
```

#### Discord Service

```csharp
// src/Dhadgar.Discord/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddDhadgarServiceDefaults();

// Aspire-integrated database
builder.AddNpgsqlDbContext<DiscordDbContext>("dhadgar_platform");

// Aspire-integrated RabbitMQ for MassTransit consumers
builder.AddRabbitMQClient("messaging");

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<SendDiscordNotificationConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

// Discord.NET bot client (for future bot commands)
// builder.Services.AddDiscordBot();

var app = builder.Build();

app.MapDhadgarDefaults(new DhadgarServiceOptions
{
    EnableTenantEnrichment = true // Discord notifications are org-scoped
});

app.UseAuthentication();
app.UseAuthorization();
app.MapEndpoints();

app.Run();
```

---

### 3.6 Stub Services Implementation

All stub services follow a consistent pattern. Here's the template:

#### Stub Service Template (with PostgreSQL + RabbitMQ)

```csharp
// src/Dhadgar.{Service}/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddDhadgarServiceDefaults();

// Aspire-integrated database
builder.AddNpgsqlDbContext<{Service}DbContext>("dhadgar_platform");

// Aspire-integrated RabbitMQ
builder.AddRabbitMQClient("messaging");

builder.Services.AddMassTransit(x =>
{
    // Add consumers when implemented
    // x.AddConsumer<SomeConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.MapDhadgarDefaults(new DhadgarServiceOptions
{
    EnableTenantEnrichment = false,
    EnableAuditMiddleware = false   // Set true for Servers
});

app.UseAuthentication();
app.UseAuthorization();

// Stub endpoint
app.MapGet("/", () => Results.Ok(new { service = "{Service}", status = "stub" }));

app.Run();
```

#### Per-Stub Configuration

| Service | TenantEnrichment | AuditMiddleware | Special Config |
|---------|------------------|-----------------|----------------|
| Billing | false | false | - |
| Servers | false | **true** | Audit for game server changes |
| Tasks | false | false | - |
| Console | false | false | SignalR hub, Redis backplane |
| Mods | false | false | - |

#### Console Service (Special: SignalR + Redis)

```csharp
// src/Dhadgar.Console/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddDhadgarServiceDefaults();

// Redis for SignalR backplane (sticky sessions across instances)
builder.AddRedisClient("cache");

// RabbitMQ for command dispatching
builder.AddRabbitMQClient("messaging");

// SignalR with Redis backplane
builder.Services.AddSignalR()
    .AddStackExchangeRedis(options =>
    {
        // Connection auto-configured by Aspire via "cache" reference
    });

var app = builder.Build();

app.MapDhadgarDefaults(new DhadgarServiceOptions
{
    EnableTenantEnrichment = false
});

app.MapHub<ServerConsoleHub>("/hubs/console");

app.Run();
```

---

### 3.7 Connection String Migration

Aspire automatically provides connection strings through environment variables:

| Before (Manual Configuration) | After (Aspire Auto-Wiring) |
|-------------------------------|----------------------------|
| `configuration.GetConnectionString("Postgres")` | Injected via `AddNpgsqlDbContext()` |
| `configuration["Redis:ConnectionString"]` | Injected via `AddRedisClient()` |
| `configuration["RabbitMq:Host"]` | Injected via `AddRabbitMQClient()` |

**Important**: Remove manual connection string configuration from `appsettings.json` in Development - Aspire handles this. Keep configuration for Production deployments outside Aspire.

---

### 3.8 Package Updates Summary

Add to `Directory.Packages.props`:

```xml
<!-- Aspire Service Integrations -->
<PackageVersion Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.2.0" />
<PackageVersion Include="Aspire.StackExchange.Redis" Version="9.2.0" />
<PackageVersion Include="Aspire.RabbitMQ.Client" Version="9.2.0" />
```

### Deliverables

**Core Services (7 production-ready):**
- [ ] Updated Gateway with Redis integration + security headers
- [ ] Updated Identity with PostgreSQL + Redis + RabbitMQ + audit middleware
- [ ] Updated Nodes with PostgreSQL + RabbitMQ
- [ ] Updated Secrets with RabbitMQ (no tenant enrichment)
- [ ] Updated BetterAuth with PostgreSQL
- [ ] Updated Notifications with PostgreSQL + RabbitMQ + tenant enrichment + consumers
- [ ] Updated Discord with PostgreSQL + RabbitMQ + tenant enrichment + consumers

**Stub Services (PostgreSQL + RabbitMQ):**
- [ ] Updated Billing
- [ ] Updated Servers (with audit middleware)
- [ ] Updated Tasks
- [ ] Updated Mods

**Stub Services (Special Configuration):**
- [ ] Updated Console with Redis + SignalR

**Configuration:**
- [ ] Created `DhadgarServiceOptions` for middleware configuration
- [ ] Updated `UseDhadgarMiddleware` to accept options
- [ ] Updated `Directory.Packages.props` with Aspire packages
- [ ] Verified all 12 services start via AppHost

---

## Phase 4: Developer Experience

**Goal**: Ensure smooth developer experience with Aspire.

### 4.1 Launch Configuration

Create `src/Dhadgar.AppHost/Properties/launchSettings.json`:

```json
{
  "profiles": {
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "https://localhost:17178;http://localhost:15178",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "DOTNET_ENVIRONMENT": "Development",
        "DOTNET_DASHBOARD_OTLP_ENDPOINT_URL": "https://localhost:21178"
      }
    }
  }
}
```

### 4.2 Update CLAUDE.md Build Commands

```markdown
## Build Commands

```bash
# Enable MSBuild Server for faster repeat builds
export DOTNET_CLI_USE_MSBUILD_SERVER=1

# Build & test
dotnet build
dotnet test

# Run with Aspire (recommended for local dev)
dotnet run --project src/Dhadgar.AppHost

# Run individual service (standalone mode, needs manual infra)
dotnet run --project src/Dhadgar.Gateway

# Alternative: Docker Compose (for CI/CD or if Aspire unavailable)
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

### 4.3 Update bootstrap-dev.ps1

Add Aspire workload check:

```powershell
# Check for Aspire workload
$aspireVersion = dotnet workload list | Select-String "aspire"
if (-not $aspireVersion) {
    Write-Host "Installing .NET Aspire workload..."
    dotnet workload install aspire
}
```

### 4.4 Keep Docker Compose as Fallback

The existing `docker-compose.dev.yml` remains for:
- CI/CD pipelines that don't support Aspire
- Developers who prefer Docker Compose
- Production-like local testing

Add a note to `deploy/compose/README.md`:

```markdown
## When to Use Docker Compose vs Aspire

| Use Case | Recommendation |
|----------|----------------|
| Local development | Aspire (better DX, Aspire Dashboard) |
| CI/CD pipelines | Docker Compose (portable, no .NET required) |
| Production simulation | Docker Compose (matches prod config) |
| Quick prototyping | Aspire (faster startup, auto-wiring) |
```

### Deliverables
- [ ] `launchSettings.json` for AppHost
- [ ] Updated `CLAUDE.md` with Aspire commands
- [ ] Updated `bootstrap-dev.ps1` with Aspire workload
- [ ] Updated Docker Compose README

---

## Phase 5: Testing & Validation

**Goal**: Ensure migration doesn't break existing functionality.

### 5.1 Integration Test Updates

Create `tests/Dhadgar.AppHost.Tests/Dhadgar.AppHost.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Dhadgar.AppHost\Dhadgar.AppHost.csproj" />
  </ItemGroup>
</Project>
```

### 5.2 AppHost Integration Tests

```csharp
public class AppHostIntegrationTests
{
    [Fact]
    public async Task AllServicesStartSuccessfully()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Dhadgar_AppHost>();

        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // Verify all resources are running
        var gateway = app.GetResource<ProjectResource>("gateway");
        Assert.Equal(KnownResourceStates.Running, gateway.State);

        var identity = app.GetResource<ProjectResource>("identity");
        Assert.Equal(KnownResourceStates.Running, identity.State);

        // ... verify other services ...
    }

    [Fact]
    public async Task GatewayHealthCheckPasses()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Dhadgar_AppHost>();

        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("gateway");
        var response = await httpClient.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

### 5.3 Existing Test Compatibility

Ensure existing tests still work:

```bash
# All tests should pass
dotnet test

# Specific service tests
dotnet test tests/Dhadgar.Identity.Tests
dotnet test tests/Dhadgar.Gateway.Tests
dotnet test tests/Dhadgar.Nodes.Tests
```

### Deliverables
- [ ] `tests/Dhadgar.AppHost.Tests/` project
- [ ] AppHost integration tests
- [ ] Verified all existing tests pass
- [ ] CI pipeline updated for Aspire tests

---

## Pre-Incorporated PR Feedback Patterns

Based on common CodeRabbit feedback from previous PRs, this implementation pre-incorporates:

### Error Handling
- [ ] Use `ErrorCodes` typed constants instead of string literals
- [ ] All error responses follow RFC 7807/9457 ProblemDetails format
- [ ] Error messages come from validators, not hardcoded

### Validation
- [ ] Auth check runs before validation (security-first)
- [ ] Use FluentValidation for complex validation
- [ ] Validation errors use `ErrorCodes.ValidationErrors.*`

### LINQ & Collections
- [ ] Use `.FirstOrDefault()` instead of `.Find()` for IEnumerable compatibility
- [ ] Use `IReadOnlyCollection<T>` for return types where appropriate

### Code Style
- [ ] XML documentation on public APIs
- [ ] No `#pragma warning disable` without justification
- [ ] Use `nameof()` for exception parameter names

### Database
- [ ] Use database unique constraints for idempotency (PostgreSQL 23505)
- [ ] Always use `async` suffix on async methods
- [ ] DbContext disposed properly in consumers

### Testing
- [ ] `<IsTestProject>true</IsTestProject>` for test projects
- [ ] `<IsTestProject>false</IsTestProject>` for shared test utilities
- [ ] Use `WebApplicationFactory<Program>` for integration tests

---

## Success Criteria

### Phase 1 Complete When
- [ ] AppHost project created and builds
- [ ] All services can be started via `dotnet run --project src/Dhadgar.AppHost`
- [ ] Aspire Dashboard shows all resources
- [ ] PostgreSQL, Redis, RabbitMQ containers run automatically

### Phase 2 Complete When
- [ ] ServiceDefaults layers on Aspire's defaults
- [ ] OpenTelemetry exports to Aspire Dashboard
- [ ] Health checks work at `/health`, `/alive`, `/healthz`, `/livez`, `/readyz`
- [ ] Dhadgar middleware still runs (correlation, tenant, logging)

### Phase 3 Complete When
- [ ] All services use Aspire integration packages
- [ ] Connection strings injected automatically
- [ ] No manual configuration needed for local dev

### Phase 4 Complete When
- [ ] `dotnet run --project src/Dhadgar.AppHost` is the default dev command
- [ ] Aspire workload installed by bootstrap script
- [ ] Documentation updated

### Phase 5 Complete When
- [ ] All 947 existing tests pass
- [ ] AppHost integration tests pass
- [ ] CI pipeline runs Aspire tests

---

## Estimated Total Effort

| Phase | Effort | Dependencies |
|-------|--------|--------------|
| Phase 1: AppHost | ~4-6 hours | None |
| Phase 2: ServiceDefaults | ~4-6 hours | Phase 1 |
| Phase 3: Service Integration | ~10-14 hours | Phase 2 |
| Phase 4: Developer Experience | ~2-3 hours | Phase 3 |
| Phase 5: Testing & Validation | ~4-6 hours | Phase 3 |
| **Total** | **~24-35 hours** | |

**Phase 3 Breakdown (12 services):**
- Core services (Gateway, Identity, Nodes, Secrets, BetterAuth, Notifications, Discord): ~8-10 hours
- Stub services with standard config (Billing, Servers, Tasks, Mods): ~2 hours
- Console (SignalR + Redis special config): ~1 hour

---

## Appendix: Configuration Mapping

### Environment Variables (Aspire vs Manual)

| Manual Config | Aspire Equivalent |
|---------------|-------------------|
| `ConnectionStrings:Postgres` | `ConnectionStrings__dhadgar_identity` (auto-set) |
| `Redis:ConnectionString` | `ConnectionStrings__cache` (auto-set) |
| `RabbitMq:Host` | `ConnectionStrings__messaging` (auto-set) |
| `OpenTelemetry:OtlpEndpoint` | `OTEL_EXPORTER_OTLP_ENDPOINT` (auto-set by Aspire) |

### Port Mapping

| Service | Manual Port | Aspire Port |
|---------|-------------|-------------|
| Gateway | 5000 | Dynamic (exposed via dashboard) |
| Identity | 5001 | Dynamic |
| Nodes | 5040 | Dynamic |
| Secrets | 5011 | Dynamic |
| Aspire Dashboard | N/A | 18888 (default) |

Aspire uses dynamic ports by default. Access services through the Aspire Dashboard or use `.WithExternalHttpEndpoints()` for fixed external access.

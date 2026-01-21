# Technology Stack: .NET Observability, Logging, and Auditing

**Project:** Meridian Console - Centralized Observability & Auditing
**Researched:** 2026-01-20
**Overall Confidence:** HIGH

## Executive Summary

The 2026 .NET observability stack builds on what Meridian Console already has (OpenTelemetry 1.14.0, Grafana/Prometheus/Loki) while adding structured logging providers, database-persisted audit trails, enhanced error handling, and comprehensive health checks. The key decision is whether to add Serilog for enhanced structured logging or stay with Microsoft.Extensions.Logging + OpenTelemetry. **Recommendation: Stay with Microsoft.Extensions.Logging** - your existing codebase already uses `[LoggerMessage]` source generators effectively, and adding Serilog introduces unnecessary complexity when OpenTelemetry handles log export.

---

## Recommended Stack

### Core Logging (Keep Current)

| Package | Version | Purpose | Confidence |
|---------|---------|---------|------------|
| Microsoft.Extensions.Logging | 10.0.2 | Base logging abstraction | HIGH |
| Microsoft.Extensions.Logging.Abstractions | 10.0.1 | ILogger interfaces | HIGH |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.14.0 | Log export to OTLP (Loki via collector) | HIGH |

**Rationale:** Your codebase already uses `[LoggerMessage]` source-generated logging (see `SecurityEventLogger.cs` with EventIds 5000-5999). This is the highest-performance logging approach in .NET and integrates natively with OpenTelemetry. Adding Serilog would require migration work with no material benefit.

**Source:** [Microsoft Learn - High-performance logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/high-performance-logging)

### OpenTelemetry (Upgrade from Current)

| Package | Current | Recommended | Purpose |
|---------|---------|-------------|---------|
| OpenTelemetry | 1.14.0 | 1.14.0 | Core OTEL SDK - **already current** |
| OpenTelemetry.Instrumentation.EntityFrameworkCore | Not installed | 1.14.0-beta.2 | Database query tracing |
| OpenTelemetry.Instrumentation.StackExchangeRedis | Not installed | 1.14.0-beta.2 | Redis operation tracing |

**Rationale:** Your MassTransit 8.3.6 has **native OpenTelemetry support** - no separate instrumentation package needed. EF Core and Redis instrumentation complete the distributed tracing picture.

**Source:** [MassTransit Observability](https://masstransit.io/documentation/configuration/observability) - MassTransit 8+ has built-in OTEL support.

### Audit Logging (New)

| Package | Version | Purpose | Confidence |
|---------|---------|---------|------------|
| Custom Implementation | N/A | Domain-specific audit tables | HIGH |

**Why NOT Audit.NET/Audit.EntityFramework.Core (32.0.0)?**

While Audit.NET is a popular library, your codebase already has:
1. `ISecretsAuditLogger` - A well-designed audit pattern in `Dhadgar.Secrets`
2. `ISecurityEventLogger` - Security event logging in `ServiceDefaults`

**Recommendation:** Extend your existing `ISecurityEventLogger` pattern into a database-backed audit system. Reasons:
- Consistent with current architecture
- No external dependency for compliance-critical code
- Full control over retention, immutability, and schema
- Your existing pattern already supports SIEM integration

**If you need change tracking specifically:** Audit.EntityFramework.Core 32.0.0 provides EF Core SaveChanges interception but your current use case (audit trails for compliance) is better served by explicit event logging.

**Source:** [Audit.NET GitHub](https://github.com/thepirat000/Audit.NET) - Consider for future if EF change tracking becomes a requirement.

### Error Handling (Enhance Current)

| Package | Version | Purpose | Confidence |
|---------|---------|---------|------------|
| Microsoft.AspNetCore.Http.Abstractions | 10.0.0 | ProblemDetails RFC 9457 support | HIGH |
| FluentValidation | 12.1.1 | Request validation | HIGH |
| SharpGrip.FluentValidation.AutoValidation | 2.x | Async auto-validation filter | MEDIUM |

**Current State:** You have `ProblemDetailsMiddleware.cs` in ServiceDefaults that returns RFC 7807 responses.

**Enhancement:**
1. Upgrade to RFC 9457 format (ASP.NET Core 10 supports this natively via `AddProblemDetails()`)
2. Add FluentValidation for request DTOs with the async filter pattern

**Why FluentValidation 12.1.1?**
- Targets .NET 8+
- FluentValidation.AspNetCore (auto-validation) is **deprecated** - use manual validation or SharpGrip filter instead
- Better async support with `ValidateAsync()`

**Source:** [FluentValidation 12.0 Upgrade Guide](https://docs.fluentvalidation.net/en/latest/upgrading-to-12.html)

### Health Checks (New)

| Package | Version | Purpose | Confidence |
|---------|---------|---------|------------|
| Microsoft.Extensions.Diagnostics.HealthChecks | 10.0.0 | Base health check framework | HIGH |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | 10.0.0 | DbContext health checks | HIGH |
| AspNetCore.HealthChecks.NpgSql | 9.0.0 | PostgreSQL connectivity | HIGH |
| AspNetCore.HealthChecks.Redis | 9.0.0 | Redis connectivity | HIGH |
| AspNetCore.HealthChecks.Rabbitmq | 9.0.0 | RabbitMQ connectivity | HIGH |
| AspNetCore.HealthChecks.UI | 9.0.0 | Health check dashboard (optional) | MEDIUM |

**Rationale:** Comprehensive health checks for all infrastructure dependencies. These integrate with your existing Grafana/Prometheus stack via the `/healthz` and `/ready` endpoints.

**Source:** [AspNetCore.Diagnostics.HealthChecks GitHub](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks)

---

## Alternatives Considered

### Serilog vs Microsoft.Extensions.Logging

| Criterion | Microsoft.Extensions.Logging | Serilog |
|-----------|------------------------------|---------|
| Performance | Highest (source generators) | High (but boxing overhead) |
| OTEL Integration | Native via AddOpenTelemetry() | Via Serilog.Sinks.OpenTelemetry |
| Structured Logging | Full support with scopes | First-class, slightly richer |
| Learning Curve | Already in use | New patterns to learn |
| Migration Effort | None | Significant |

**Decision:** Stay with Microsoft.Extensions.Logging. Your existing `[LoggerMessage]` pattern is best-in-class for performance and already supports structured logging with scopes (see `RequestLoggingMiddleware.cs`).

**When to reconsider:** If you need Serilog-specific sinks (Seq, Datadog, etc.) that don't have OTLP support.

### Seq vs Loki vs Elasticsearch

| Criterion | Loki (Current) | Seq | Elasticsearch |
|-----------|----------------|-----|---------------|
| Cost | Free/OSS | Free (single node), paid for HA | Free/OSS, expensive at scale |
| Query Power | Label-based, LogQL | SQL-like, excellent | Full-text, Query DSL |
| Scalability | Horizontal, label-indexed | Single-node only | Horizontal, resource-heavy |
| .NET Integration | Via OTLP | Native Serilog sink | Via OTLP or direct |
| Grafana Integration | Native | None | Good |

**Decision:** Keep Loki. It's already deployed, integrates with your Grafana stack, and label-based indexing is cost-effective for microservices logging.

**When to reconsider:** If you need full-text search across log bodies or enterprise HA requirements that Loki doesn't meet.

**Source:** [Seq vs Loki comparison](https://stackshare.io/stackups/loki-vs-seq)

### Audit.NET vs Custom Audit

| Criterion | Audit.NET | Custom Implementation |
|-----------|-----------|----------------------|
| Time to Implement | Fast | Medium |
| Flexibility | Good | Full control |
| EF Change Tracking | Built-in | Manual |
| Compliance Control | Limited | Full |
| Existing Code Alignment | Poor | Excellent |

**Decision:** Custom implementation extending your existing audit patterns. Your `SecretsAuditLogger` is already well-designed for SIEM integration.

---

## What NOT to Use

### Serilog (for this project)

**Why Not:**
- Adds dependency complexity without proportional benefit
- Requires migrating existing `[LoggerMessage]` code
- OpenTelemetry already handles log export to Loki
- Your source-generated logging is higher performance

### OpenTelemetry.Instrumentation.MassTransit

**Why Not:**
- **MassTransit 8+ has native OTEL support** - this package is deprecated
- Adding it would cause duplicate spans

**Source:** [GitHub Issue #326](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/issues/326) - "MassTransit instrumentation is not needed in MassTransit 8"

### FluentValidation.AspNetCore (Auto-Validation)

**Why Not:**
- Deprecated by FluentValidation team
- Does not support async validators properly
- Does not work with Minimal APIs (which you may adopt)
- Use manual validation or SharpGrip.FluentValidation.AutoValidation instead

**Source:** [FluentValidation ASP.NET Docs](https://docs.fluentvalidation.net/en/latest/aspnet.html)

### Audit.EntityFramework.Core (initially)

**Why Not:**
- Your use case is event-based auditing, not EF change tracking
- Your existing `ISecretsAuditLogger` pattern is more aligned with compliance needs
- Can be added later if EF change tracking becomes a requirement

---

## Installation

### New Packages to Add

```xml
<!-- Directory.Packages.props additions -->

<!-- Health Checks -->
<PackageVersion Include="AspNetCore.HealthChecks.NpgSql" Version="9.0.0" />
<PackageVersion Include="AspNetCore.HealthChecks.Redis" Version="9.0.0" />
<PackageVersion Include="AspNetCore.HealthChecks.Rabbitmq" Version="9.0.0" />

<!-- OpenTelemetry Instrumentation -->
<PackageVersion Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.14.0-beta.2" />
<PackageVersion Include="OpenTelemetry.Instrumentation.StackExchangeRedis" Version="1.14.0-beta.2" />

<!-- Validation -->
<PackageVersion Include="FluentValidation" Version="12.1.1" />
<PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
```

### Per-Service References

```xml
<!-- Services with database (Identity, Servers, etc.) -->
<PackageReference Include="AspNetCore.HealthChecks.NpgSql" />
<PackageReference Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" />

<!-- Services with Redis -->
<PackageReference Include="AspNetCore.HealthChecks.Redis" />
<PackageReference Include="OpenTelemetry.Instrumentation.StackExchangeRedis" />

<!-- Services with RabbitMQ -->
<PackageReference Include="AspNetCore.HealthChecks.Rabbitmq" />

<!-- Services with request validation -->
<PackageReference Include="FluentValidation" />
<PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
```

---

## Configuration Patterns

### OpenTelemetry with EF Core Instrumentation

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation(options =>
            {
                options.SetDbStatementForText = true; // Include SQL in spans
                options.SetDbStatementForStoredProcedure = true;
            })
            .AddSource("MassTransit"); // MassTransit native OTEL

        if (otlpUri is not null)
            tracing.AddOtlpExporter(o => o.Endpoint = otlpUri);
    });
```

### Health Checks Registration

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
    .AddNpgSql(connectionString, name: "postgresql", tags: ["ready"])
    .AddRedis(redisConnection, name: "redis", tags: ["ready"])
    .AddRabbitMQ(rabbitUri, name: "rabbitmq", tags: ["ready"]);
```

### FluentValidation (Manual Pattern)

```csharp
// Register validators
builder.Services.AddValidatorsFromAssemblyContaining<CreateServerRequestValidator>();

// In endpoint
app.MapPost("/servers", async (
    CreateServerRequest request,
    IValidator<CreateServerRequest> validator,
    CancellationToken ct) =>
{
    var result = await validator.ValidateAsync(request, ct);
    if (!result.IsValid)
        return Results.ValidationProblem(result.ToDictionary());

    // Continue processing
});
```

---

## Confidence Assessment

| Recommendation | Confidence | Reasoning |
|----------------|------------|-----------|
| Keep Microsoft.Extensions.Logging | HIGH | Existing codebase uses it well, source generators are optimal |
| OpenTelemetry 1.14.0 | HIGH | Already installed, verified current on NuGet |
| EF Core Instrumentation | HIGH | Standard package, verified 1.14.0-beta.2 available |
| Health Check packages | HIGH | Verified 9.0.0 versions, well-maintained |
| FluentValidation 12.1.1 | HIGH | Verified current, deprecation of AspNetCore package confirmed |
| Keep Loki (not Seq/Elastic) | HIGH | Already deployed, fits architecture |
| Custom Audit over Audit.NET | MEDIUM | Sound reasoning but untested at scale |

---

## Sources

### Official Documentation
- [Microsoft Learn - High-performance logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/high-performance-logging)
- [Microsoft Learn - .NET Observability with OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel)
- [OpenTelemetry .NET Releases](https://github.com/open-telemetry/opentelemetry-dotnet/releases)
- [MassTransit Observability](https://masstransit.io/documentation/configuration/observability)
- [FluentValidation 12 Upgrade Guide](https://docs.fluentvalidation.net/en/latest/upgrading-to-12.html)

### NuGet Package Verification
- [OpenTelemetry 1.14.0](https://www.nuget.org/packages/OpenTelemetry)
- [Microsoft.Extensions.Logging 10.0.2](https://www.nuget.org/packages/microsoft.extensions.logging/)
- [FluentValidation 12.1.1](https://www.nuget.org/packages/FluentValidation)
- [AspNetCore.HealthChecks.NpgSql 9.0.0](https://www.nuget.org/packages/AspNetCore.HealthChecks.NpgSql)
- [Audit.EntityFramework.Core 32.0.0](https://www.nuget.org/packages/Audit.EntityFramework.Core)

### Community Resources
- [AspNetCore.Diagnostics.HealthChecks GitHub](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks)
- [Serilog vs MEL comparison](https://www.milanjovanovic.tech/blog/structured-logging-in-asp-net-core-with-serilog)
- [Seq vs Loki comparison](https://stackshare.io/stackups/loki-vs-seq)

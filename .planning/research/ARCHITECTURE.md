# Architecture Patterns

**Domain:** Centralized logging, auditing, and error handling for .NET microservices
**Researched:** 2026-01-20
**Confidence:** HIGH (based on existing codebase patterns and .NET best practices)

## Executive Summary

The Meridian Console platform already has a solid foundation for observability with OpenTelemetry, structured logging, and a working audit pattern in the Secrets service. The recommended architecture extends these existing patterns consistently across all 13+ microservices while adding persistent audit storage and centralized error handling.

**Key insight:** The existing `ServiceDefaults` project is the natural home for centralized logging infrastructure. Services already reference it for middleware, and it follows the established pattern of sharing cross-cutting concerns without coupling services to each other.

## Current State Analysis

### What Already Exists

| Component | Location | Status |
|-----------|----------|--------|
| Correlation tracking | `ServiceDefaults/Middleware/CorrelationMiddleware.cs` | Production-ready |
| Request logging | `ServiceDefaults/Middleware/RequestLoggingMiddleware.cs` | Production-ready |
| Problem details | `ServiceDefaults/Middleware/ProblemDetailsMiddleware.cs` | Production-ready |
| Security event logger | `ServiceDefaults/Security/SecurityEventLogger.cs` | Production-ready, uses source generators |
| Secrets audit logger | `Dhadgar.Secrets/Audit/SecretsAuditLogger.cs` | Domain-specific, log-based |
| Identity audit service | `Dhadgar.Identity/Services/AuditService.cs` | Database-backed, with entity |
| OpenTelemetry | Per-service in Program.cs | Configured but duplicated |
| OTLP Collector | `deploy/compose/otel-collector-config.yml` | Exports to Loki/Prometheus/New Relic |

### Current Data Flow

```
Service Request
    |
    v
[CorrelationMiddleware] --> Sets X-Correlation-Id, X-Request-Id, X-Trace-Id
    |
    v
[ProblemDetailsMiddleware] --> Catches exceptions, returns RFC 7807
    |
    v
[RequestLoggingMiddleware] --> Logs HTTP method, path, status, duration
    |
    v
[Application Code] --> ILogger<T> calls
    |
    v
[OpenTelemetry LoggingProvider] --> Enriches with resource info
    |
    v
[OTLP Exporter] --> Sends to collector (port 4317)
    |
    v
[OTEL Collector] --> Routes to Loki (logs), Prometheus (metrics), New Relic (traces)
```

## Recommended Architecture

### Component Diagram

```
+------------------------------------------+
|             Service Layer                |
|  (Gateway, Identity, Servers, etc.)      |
+------------------------------------------+
         |              |              |
         v              v              v
+------------+  +---------------+  +----------+
| Request    |  | Domain Audit  |  | Error    |
| Logging    |  | (per-service) |  | Handling |
+------------+  +---------------+  +----------+
         |              |              |
         v              v              v
+------------------------------------------+
|           Dhadgar.ServiceDefaults        |
|  - IStructuredLogger (interface)         |
|  - LogEnrichmentMiddleware               |
|  - GlobalExceptionHandler                |
|  - AuditEventTypes (constants)           |
|  - OpenTelemetry configuration helpers   |
+------------------------------------------+
         |              |
         v              v
+----------------+  +-----------------+
| OpenTelemetry  |  | MassTransit     |
| (structured    |  | (audit events   |
|  logs/traces)  |  |  to Audit svc)  |
+----------------+  +-----------------+
         |              |
         v              v
+----------------+  +-----------------+
| Loki/New Relic |  | Audit Database  |
| (log storage)  |  | (PostgreSQL)    |
+----------------+  +-----------------+
```

### Component Boundaries

| Component | Responsibility | Communicates With | Owns |
|-----------|---------------|-------------------|------|
| `ServiceDefaults` | Shared logging interfaces, middleware, enrichment | All services (compile-time) | Interfaces, base implementations |
| `Dhadgar.Contracts` | Audit event message contracts | All services (compile-time) | DTOs for audit messages |
| Individual Services | Domain-specific audit events | ServiceDefaults, MassTransit | Their own audit logic |
| Audit Service (new) | Persistent audit storage, retention, queries | PostgreSQL, exposes API | Audit database, query endpoints |
| OTEL Collector | Log/metric/trace routing | All services (runtime) | Export configuration |

### Data Flow: Logging

```
[Service Code]
    |
    | ILogger<T>.LogInformation("User {UserId} created server {ServerId}", ...)
    v
[Serilog/OTel Sink]
    |
    | Auto-enrichment via BeginScope:
    | - CorrelationId (from middleware)
    | - ServiceName (from resource)
    | - Environment (from config)
    | - UserId (from ClaimsPrincipal if available)
    | - OrgId (from ClaimsPrincipal if available)
    v
[OpenTelemetry LoggingProvider]
    |
    | Structured log record with attributes
    v
[OTLP Exporter] --> [OTEL Collector] --> [Loki] --> [Grafana]
```

### Data Flow: Audit Events (Compliance)

```
[Service Code]
    |
    | _auditPublisher.PublishAsync(new ServerCreated(...));
    v
[MassTransit Publisher]
    |
    | Message to RabbitMQ
    v
[RabbitMQ Exchange: audit-events]
    |
    | Fanout to consumers
    v
[Audit Service Consumer]
    |
    | Persist to PostgreSQL
    v
[Audit Database]
    |
    | - 90-day retention (configurable)
    | - Indexed by OrgId, UserId, EventType, Timestamp
    | - Query API for compliance reports
```

### Data Flow: Error Handling

```
[Exception thrown in service]
    |
    v
[ProblemDetailsMiddleware] (existing, in ServiceDefaults)
    |
    | 1. Log error with correlation ID
    | 2. Capture exception details (dev only)
    | 3. Return RFC 7807 Problem Details
    v
[HTTP Response]
    {
      "type": "https://meridian.console/errors/...",
      "title": "Internal Server Error",
      "status": 500,
      "detail": "An error occurred...",
      "traceId": "abc123..."
    }

For known exceptions (validation, not found, etc.):

[Validation exception]
    |
    v
[Endpoint filter / Result pattern]
    |
    | Return Results.Problem(...) or Results.ValidationProblem(...)
    v
[HTTP Response with appropriate status]
```

## Architecture Patterns to Follow

### Pattern 1: Correlation ID Propagation

**What:** Every request gets a correlation ID that flows through all services and appears in all logs.

**When:** Already implemented in CorrelationMiddleware. Must be maintained for all new code.

**Example (current implementation):**
```csharp
// CorrelationMiddleware sets these on every request
context.Items["CorrelationId"] = correlationId;
Activity.Current?.SetTag("correlation.id", correlationId);
Activity.Current?.SetBaggage("correlation.id", correlationId);

// RequestLoggingMiddleware uses logging scope
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["CorrelationId"] = correlationId,
    ["RequestId"] = requestId,
    ...
}))
{
    await _next(context);
}
```

### Pattern 2: Source-Generated Logging

**What:** Use `[LoggerMessage]` attribute for high-performance, structured logging.

**When:** For all security events and high-frequency log messages.

**Example (from SecurityEventLogger.cs):**
```csharp
public sealed partial class SecurityEventLogger : ISecurityEventLogger
{
    private readonly ILogger<SecurityEventLogger> _logger;

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Information,
        Message = "Security: Authentication succeeded for user {UserId} ({Email}) from {ClientIp}")]
    private partial void AuthenticationSucceeded(Guid userId, string email, string clientIp, ...);
}
```

### Pattern 3: Domain-Specific Audit Interfaces

**What:** Each service defines its own audit interface that fits its domain.

**When:** For compliance-critical operations that need database persistence.

**Example (from Secrets service):**
```csharp
public interface ISecretsAuditLogger
{
    void LogAccess(SecretAuditEvent evt);
    void LogAccessDenied(SecretAccessDeniedEvent evt);
    void LogModification(SecretModificationEvent evt);
    void LogRotation(SecretRotationEvent evt);
}

// Implementation uses structured logging
_logger.LogInformation(
    "AUDIT:SECRETS:ACCESS Action={Action} Secret={SecretName} User={UserId} ...",
    evt.Action, evt.SecretName, evt.UserId, ...);
```

### Pattern 4: Centralized OpenTelemetry Configuration

**What:** Move duplicated OTLP configuration into ServiceDefaults extension method.

**When:** All services currently duplicate this code in Program.cs.

**Example (recommended consolidation):**
```csharp
// In ServiceDefaults
public static class OpenTelemetryExtensions
{
    public static IHostApplicationBuilder AddDhadgarOpenTelemetry(
        this IHostApplicationBuilder builder,
        string serviceName)
    {
        var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName);

        builder.Logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(resourceBuilder);
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;  // Critical for correlation IDs
            options.ParseStateValues = true;

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                options.AddOtlpExporter(e => e.Endpoint = new Uri(otlpEndpoint));
        });

        // ... tracing and metrics configuration
        return builder;
    }
}

// In service Program.cs
builder.AddDhadgarOpenTelemetry("Dhadgar.Servers");
```

### Pattern 5: Message-Based Audit Events

**What:** Publish audit events via MassTransit for database persistence.

**When:** For compliance requirements where logs are not sufficient (need queryable data, retention policies, user-facing audit reports).

**Example (recommended):**
```csharp
// In Dhadgar.Contracts
public record AuditEventPublished(
    Guid EventId,
    string EventType,
    string ServiceName,
    Guid? UserId,
    Guid? OrganizationId,
    Guid? ActorUserId,
    string? TargetType,
    Guid? TargetId,
    string? Details,  // JSON
    string? CorrelationId,
    DateTimeOffset OccurredAtUtc);

// In service code
await _publishEndpoint.Publish(new AuditEventPublished(
    EventId: Guid.NewGuid(),
    EventType: "server.created",
    ServiceName: "Dhadgar.Servers",
    ...
));

// Audit service consumes and persists
public class AuditEventConsumer : IConsumer<AuditEventPublished>
{
    public async Task Consume(ConsumeContext<AuditEventPublished> context)
    {
        var entity = MapToEntity(context.Message);
        await _dbContext.AuditEvents.AddAsync(entity);
        await _dbContext.SaveChangesAsync();
    }
}
```

## Anti-Patterns to Avoid

### Anti-Pattern 1: Service-to-Service Database Access

**What:** Having multiple services write to the same audit database directly.

**Why bad:** Violates service boundary isolation, creates coupling, makes schema changes risky.

**Instead:** Use message-based communication. Each service publishes audit events, a dedicated Audit service consumes and persists them.

### Anti-Pattern 2: Logging Sensitive Data

**What:** Logging secrets, tokens, passwords, or full request bodies.

**Why bad:** Security risk, compliance violations (GDPR, etc.).

**Instead:**
- Log only metadata: resource IDs, action types, user IDs
- Redact sensitive fields in log enrichment
- Use allowlists for what CAN be logged, not blocklists

### Anti-Pattern 3: Synchronous Audit Writes

**What:** Waiting for audit database write before returning HTTP response.

**Why bad:** Adds latency to every request, audit database becomes availability dependency.

**Instead:**
- Use fire-and-forget message publishing
- Accept eventual consistency for audit trail
- Use local logging as fallback if message broker unavailable

### Anti-Pattern 4: Inconsistent Error Response Formats

**What:** Some endpoints return `{ "error": "message" }`, others return Problem Details, others throw.

**Why bad:** Clients cannot reliably parse errors, debugging is harder.

**Instead:** Always return RFC 7807 Problem Details:
```json
{
  "type": "https://meridian.console/errors/validation-error",
  "title": "Validation Error",
  "status": 400,
  "detail": "Server name is required",
  "traceId": "abc123",
  "errors": {
    "name": ["Name is required"]
  }
}
```

### Anti-Pattern 5: Duplicating Configuration

**What:** Each service copies OpenTelemetry configuration code.

**Why bad:** Configuration drift, maintenance burden, missed updates.

**Instead:** Centralize in ServiceDefaults with extension methods. Current codebase has 50+ lines of OTLP config duplicated across services.

## Scalability Considerations

| Concern | At 100 users | At 10K users | At 1M users |
|---------|--------------|--------------|-------------|
| Log volume | Loki handles easily | Add log sampling, increase Loki resources | Consider log tiers (hot/warm/cold), aggressive sampling |
| Audit storage | Single PostgreSQL | Partitioning by month | Separate audit database cluster, archive old partitions to cold storage |
| Message throughput | RabbitMQ handles easily | Monitor queue depth, add consumers | Consider audit event batching, dedicated RabbitMQ vhost |
| Query latency | Direct queries | Add indexes, read replicas | Pre-aggregate audit summaries, caching layer |

## Build Order Implications

The following order ensures dependencies are available when needed:

### Phase 1: Foundation (ServiceDefaults Enhancements)

1. **OpenTelemetry consolidation** in ServiceDefaults
   - Move duplicated OTLP config to extension method
   - Add standard enrichment (service name, environment)
   - No breaking changes, services can adopt incrementally

2. **Log enrichment middleware**
   - Add user context (UserId, OrgId) from ClaimsPrincipal
   - Integrate with existing CorrelationMiddleware
   - All services get enrichment by using middleware

3. **Error handling improvements**
   - Enhance ProblemDetailsMiddleware with categorized error types
   - Add validation problem helper methods
   - Define standard error type URIs

**Rationale:** These changes provide immediate value and are low-risk. Services can adopt incrementally.

### Phase 2: Contracts and Messaging

4. **Audit event contracts** in Dhadgar.Contracts
   - Define `AuditEventPublished` record
   - Define standard event type constants
   - No runtime impact, just definitions

5. **Audit publisher interface** in ServiceDefaults
   - Define `IAuditEventPublisher` interface
   - Create MassTransit implementation
   - Services can start publishing without consumer

**Rationale:** Contracts must exist before consumers. Publisher without consumer allows incremental adoption.

### Phase 3: Audit Service

6. **Audit service database**
   - Create `Dhadgar.Audit` service (or extend existing service)
   - Add AuditEvent entity and DbContext
   - Add migrations with appropriate indexes

7. **Audit consumer**
   - Add MassTransit consumer for `AuditEventPublished`
   - Handle duplicates (idempotent by EventId)
   - Implement basic retention policy

8. **Audit query API**
   - Endpoints: GET /audit/user/{userId}, GET /audit/org/{orgId}
   - Pagination, filtering by event type and date range
   - Authorization: users see their own, admins see org

**Rationale:** Database before consumer (consumer needs to write). Consumer before API (need data to query).

### Phase 4: Service Integration

9. **Per-service audit integration**
   - Add audit publishing to each service
   - Start with high-value events (security, data changes)
   - Gradually expand coverage

**Rationale:** Integration phase is incremental and can be parallelized across services.

## Component Dependency Graph

```
                   [Dhadgar.Contracts]
                          ^
                          |
    [Dhadgar.ServiceDefaults] -----> [Dhadgar.Messaging]
           ^      ^
           |      |
    +------+------+------+------+------+
    |      |      |      |      |      |
Gateway Identity Servers Nodes Tasks  ... (all services)
    |
    | publishes AuditEventPublished
    v
[RabbitMQ]
    |
    | consumes
    v
[Dhadgar.Audit Service]
    |
    v
[Audit PostgreSQL DB]
```

## Key Files to Create/Modify

| Action | File | Purpose |
|--------|------|---------|
| Create | `ServiceDefaults/OpenTelemetry/OpenTelemetryExtensions.cs` | Centralized OTLP configuration |
| Create | `ServiceDefaults/Logging/LogEnrichmentMiddleware.cs` | Add user context to logs |
| Modify | `ServiceDefaults/Middleware/ProblemDetailsMiddleware.cs` | Categorized error types |
| Create | `Contracts/Audit/AuditContracts.cs` | Audit event records |
| Create | `Contracts/Audit/AuditEventTypes.cs` | Standard event type constants |
| Create | `ServiceDefaults/Audit/IAuditEventPublisher.cs` | Publisher interface |
| Create | `ServiceDefaults/Audit/MassTransitAuditPublisher.cs` | MassTransit implementation |
| Create | `src/Dhadgar.Audit/` | New audit service (or integrate into existing) |

## Integration with Existing Components

### With Existing CorrelationMiddleware

The log enrichment middleware should run AFTER CorrelationMiddleware:

```csharp
// Pipeline order in services
app.UseMiddleware<CorrelationMiddleware>();      // Sets correlation ID
app.UseMiddleware<LogEnrichmentMiddleware>();    // Adds user context
app.UseMiddleware<ProblemDetailsMiddleware>();   // Catches exceptions
app.UseMiddleware<RequestLoggingMiddleware>();   // Logs requests
```

### With Existing SecurityEventLogger

SecurityEventLogger continues to handle security-specific logging. For audit database persistence, security events can ALSO be published as audit events:

```csharp
// SecurityEventLogger handles structured logging
_securityEventLogger.LogAuthenticationSuccess(userId, email, clientIp, userAgent);

// Additionally publish for database persistence
await _auditPublisher.PublishAsync(new AuditEventPublished(
    EventType: "user.authenticated",
    ...
));
```

### With Existing Identity AuditService

The Identity service already has `IAuditService` for database-backed audit. This can:
1. Stay as-is for Identity-specific needs
2. Migrate to use the centralized audit service
3. Publish events to centralized audit AND keep local copy

Recommendation: Option 3 during transition, then migrate to centralized only.

## Sources

- Existing codebase patterns (HIGH confidence):
  - `/src/Shared/Dhadgar.ServiceDefaults/`
  - `/src/Dhadgar.Secrets/Audit/`
  - `/src/Dhadgar.Identity/Services/AuditService.cs`
- OpenTelemetry .NET documentation (HIGH confidence via Context7)
- MassTransit documentation (HIGH confidence via existing usage)
- RFC 7807 Problem Details specification (HIGH confidence, industry standard)

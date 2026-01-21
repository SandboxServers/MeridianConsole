# Phase 3: Audit System - Research

**Researched:** 2026-01-21
**Domain:** API Audit Logging, PostgreSQL Schema Design, Async Write Patterns, Background Cleanup
**Confidence:** HIGH

## Summary

This phase implements a centralized audit system that records all authenticated API calls to PostgreSQL for compliance queries. The codebase already has excellent patterns to follow: the Identity service has a working `AuditEvent` entity with database schema, `AuditService` for CRUD operations, and `TokenCleanupService` for background retention cleanup. The key architectural decision is **how** to collect audit events from all 13+ services and write them efficiently without impacting request latency.

The recommended approach is a **centralized audit middleware in ServiceDefaults** that captures HTTP request/response metadata, combined with **async writes using System.Threading.Channels** to buffer events for background processing. This avoids the overhead of MassTransit for high-frequency, low-value-per-message writes while keeping the hot path (request handling) fast.

**Primary recommendation:** Add `AuditMiddleware` to ServiceDefaults that runs after authentication, captures request metadata, and queues audit events to a `Channel<AuditEvent>`. A `BackgroundService` drains the channel and batch-inserts to PostgreSQL. Each service owns its audit table (following database-per-service pattern) but uses shared infrastructure.

## Architecture Decision: Centralized vs Distributed

### Option 1: Centralized Audit Service (NOT RECOMMENDED)

All services publish audit events via MassTransit to a dedicated Audit service that writes to a single database.

**Pros:**
- Single query location for "who did what when"
- Single retention policy enforcement

**Cons:**
- MassTransit overhead for high-frequency, simple writes (audit of every API call)
- Additional service to deploy and maintain
- Message broker becomes critical path for compliance
- Latency for cross-service queries (must wait for message processing)

### Option 2: Gateway-Only Audit (PARTIAL)

Only the Gateway captures audit events since all traffic flows through it.

**Pros:**
- Single capture point
- Simplest implementation

**Cons:**
- Loses internal service context (resource IDs, business outcomes)
- Cannot audit service-to-service calls
- Tenant ID may not be extracted yet at Gateway level

### Option 3: Per-Service Audit with Shared Infrastructure (RECOMMENDED)

Each service runs audit middleware and writes to its own audit table. Shared abstractions in ServiceDefaults provide consistent schema, middleware, and cleanup patterns.

**Pros:**
- No additional network hops (local database write)
- Each service can enrich audit with domain-specific resource IDs
- No single point of failure
- Works with existing database-per-service architecture
- Federation queries possible via PostgreSQL foreign data wrappers if needed

**Cons:**
- Cross-service audit queries require federation or ETL
- Must ensure schema consistency across services

**Decision: Option 3** - Aligns with existing microservices architecture and database-per-service pattern. The Identity service already demonstrates this pattern with `AuditEvent` entity.

## Write Strategy: Sync vs Async

### Synchronous Database Writes (NOT RECOMMENDED)

Each request waits for audit INSERT to complete before returning response.

**Impact:**
- Adds 1-5ms latency per request (PostgreSQL round-trip)
- Audit database issues block API responses
- Under load, connection pool exhaustion affects both audit and business queries

### Asynchronous via MassTransit (NOT RECOMMENDED FOR THIS USE CASE)

Publish `AuditRecorded` event to RabbitMQ, consume in background.

**Impact:**
- MassTransit adds serialization/deserialization overhead
- RabbitMQ becomes critical for compliance
- Overkill for simple writes that don't need reliable delivery guarantees beyond the request scope

### Asynchronous via Channel + BackgroundService (RECOMMENDED)

Buffer audit events in-memory using `System.Threading.Channels`, drain in background.

**Pattern from Microsoft docs ([Create a Queue Service](https://learn.microsoft.com/en-us/dotnet/core/extensions/queue-service)):**
```csharp
public interface IAuditQueue
{
    ValueTask QueueAsync(AuditRecord record, CancellationToken ct = default);
    ValueTask<AuditRecord> DequeueAsync(CancellationToken ct);
}

public sealed class AuditQueue : IAuditQueue
{
    private readonly Channel<AuditRecord> _channel;

    public AuditQueue(int capacity = 10_000)
    {
        _channel = Channel.CreateBounded<AuditRecord>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait // Backpressure if queue fills
        });
    }

    public ValueTask QueueAsync(AuditRecord record, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(record, ct);

    public ValueTask<AuditRecord> DequeueAsync(CancellationToken ct)
        => _channel.Reader.ReadAsync(ct);
}
```

**Impact:**
- Zero latency impact on request handling (non-blocking queue)
- Batch writes reduce database round-trips
- Graceful backpressure under extreme load
- Process shutdown waits for queue drain (configurable)

**Decision: Channel + BackgroundService** - Follows existing `TokenCleanupService` pattern, no external dependencies, optimal for high-frequency low-complexity writes.

## Schema Design

### Entity Model

Based on existing `AuditEvent` in Identity service, extended for API audit requirements:

```csharp
/// <summary>
/// Records authenticated API requests for compliance and analysis.
/// </summary>
public sealed class ApiAuditRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Timestamp of the request (UTC)</summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>User who made the request (from JWT sub claim)</summary>
    public Guid? UserId { get; set; }

    /// <summary>Organization/tenant context (from JWT org_id claim)</summary>
    public Guid? TenantId { get; set; }

    /// <summary>HTTP method (GET, POST, PUT, DELETE, PATCH)</summary>
    [Required]
    [MaxLength(10)]
    public string HttpMethod { get; set; } = null!;

    /// <summary>Request path (e.g., /api/v1/servers/123)</summary>
    [Required]
    [MaxLength(500)]
    public string Path { get; set; } = null!;

    /// <summary>Extracted resource ID from path (if applicable)</summary>
    public Guid? ResourceId { get; set; }

    /// <summary>Resource type (e.g., "server", "node", "user")</summary>
    [MaxLength(50)]
    public string? ResourceType { get; set; }

    /// <summary>HTTP status code returned</summary>
    public int StatusCode { get; set; }

    /// <summary>Request duration in milliseconds</summary>
    public long DurationMs { get; set; }

    /// <summary>Client IP address</summary>
    [MaxLength(45)]
    public string? ClientIp { get; set; }

    /// <summary>User agent string (truncated)</summary>
    [MaxLength(256)]
    public string? UserAgent { get; set; }

    /// <summary>Correlation ID for distributed tracing</summary>
    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    /// <summary>OpenTelemetry trace ID</summary>
    [MaxLength(32)]
    public string? TraceId { get; set; }

    /// <summary>Service that processed the request</summary>
    [MaxLength(50)]
    public string? ServiceName { get; set; }
}
```

### EF Core Configuration

```csharp
public sealed class ApiAuditRecordConfiguration : IEntityTypeConfiguration<ApiAuditRecord>
{
    public void Configure(EntityTypeBuilder<ApiAuditRecord> builder)
    {
        builder.ToTable("api_audit_records");

        builder.HasKey(e => e.Id);

        // Primary query patterns with composite indexes
        builder.HasIndex(e => new { e.UserId, e.TimestampUtc })
            .HasDatabaseName("ix_audit_user_time")
            .IsDescending(false, true);

        builder.HasIndex(e => new { e.TenantId, e.TimestampUtc })
            .HasDatabaseName("ix_audit_tenant_time")
            .IsDescending(false, true);

        // Cleanup index (90-day retention)
        builder.HasIndex(e => e.TimestampUtc)
            .HasDatabaseName("ix_audit_timestamp");

        // Resource lookup
        builder.HasIndex(e => new { e.ResourceType, e.ResourceId, e.TimestampUtc })
            .HasDatabaseName("ix_audit_resource_time")
            .IsDescending(false, false, true);

        // Service-level analysis
        builder.HasIndex(e => new { e.ServiceName, e.TimestampUtc })
            .HasDatabaseName("ix_audit_service_time")
            .IsDescending(false, true);
    }
}
```

### Partitioning Strategy (Future Enhancement)

For tables exceeding 10 million rows (roughly 6 months at 50k requests/day), consider PostgreSQL declarative partitioning by month:

```sql
-- Future migration when scale requires
CREATE TABLE api_audit_records (
    id UUID NOT NULL,
    timestamp_utc TIMESTAMPTZ NOT NULL,
    -- ... other columns
    PRIMARY KEY (id, timestamp_utc)
) PARTITION BY RANGE (timestamp_utc);

CREATE TABLE api_audit_records_2026_01
    PARTITION OF api_audit_records
    FOR VALUES FROM ('2026-01-01') TO ('2026-02-01');
```

**Note:** EF Core 8+ supports partitioned tables but doesn't auto-create partitions. Use pg_partman or scheduled scripts.

**Current Phase:** Start without partitioning. Add when retention cleanup shows performance issues (see [PostgreSQL Partitioning Best Practices](https://www.tigerdata.com/learn/when-to-consider-postgres-partitioning)).

## Implementation Pattern: Middleware

### Middleware Design

```csharp
/// <summary>
/// Middleware that captures API requests for audit logging.
/// Runs after authentication to have access to user claims.
/// </summary>
public sealed class AuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAuditQueue _auditQueue;
    private readonly ILogger<AuditMiddleware> _logger;

    public AuditMiddleware(
        RequestDelegate next,
        IAuditQueue auditQueue,
        ILogger<AuditMiddleware> logger)
    {
        _next = next;
        _auditQueue = auditQueue;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip unauthenticated requests (per AUDIT-01: "authenticated API calls")
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        // Skip health check endpoints
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/healthz") || path.StartsWith("/livez") || path.StartsWith("/readyz"))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var timestampUtc = DateTime.UtcNow;

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            var record = BuildAuditRecord(context, timestampUtc, stopwatch.ElapsedMilliseconds);

            // Fire-and-forget queue (non-blocking)
            _ = _auditQueue.QueueAsync(record);
        }
    }

    private ApiAuditRecord BuildAuditRecord(HttpContext context, DateTime timestamp, long durationMs)
    {
        return new ApiAuditRecord
        {
            TimestampUtc = timestamp,
            UserId = ExtractUserId(context),
            TenantId = ExtractTenantId(context),
            HttpMethod = context.Request.Method,
            Path = context.Request.Path.Value ?? "",
            ResourceId = ExtractResourceId(context),
            ResourceType = ExtractResourceType(context),
            StatusCode = context.Response.StatusCode,
            DurationMs = durationMs,
            ClientIp = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent = context.Request.Headers.UserAgent.ToString().Truncate(256),
            CorrelationId = context.Items["CorrelationId"]?.ToString(),
            TraceId = Activity.Current?.TraceId.ToString(),
            ServiceName = Assembly.GetEntryAssembly()?.GetName().Name
        };
    }
}
```

### Resource ID Extraction

Extract resource IDs from common path patterns:

```csharp
private static readonly Regex ResourceIdPattern = new(
    @"/(?:api/)?v?\d*/?(servers|nodes|users|organizations|tasks|files|mods)/([0-9a-fA-F-]{36})",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);

private static Guid? ExtractResourceId(HttpContext context)
{
    var path = context.Request.Path.Value ?? "";
    var match = ResourceIdPattern.Match(path);

    if (match.Success && Guid.TryParse(match.Groups[2].Value, out var id))
    {
        return id;
    }

    return null;
}

private static string? ExtractResourceType(HttpContext context)
{
    var path = context.Request.Path.Value ?? "";
    var match = ResourceIdPattern.Match(path);

    return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
}
```

### Middleware Registration Order

Add after authentication, before endpoint execution:

```csharp
// In ServiceDefaultsExtensions.UseDhadgarMiddleware()
public static WebApplication UseDhadgarMiddleware(this WebApplication app)
{
    app.UseMiddleware<CorrelationMiddleware>();
    app.UseMiddleware<TenantEnrichmentMiddleware>();
    app.UseMiddleware<ProblemDetailsMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();

    // NEW: Audit middleware runs after auth is complete
    // Registered separately after UseAuthentication/UseAuthorization
    return app;
}

// In each service's Program.cs
app.UseDhadgarMiddleware();
app.UseAuthentication();
app.UseAuthorization();
app.UseAuditMiddleware(); // Add audit after auth
```

## Cleanup Strategy: Background Job

### Pattern: BackgroundService with Batch Delete

Following `TokenCleanupService` pattern from Identity service:

```csharp
public sealed class AuditCleanupOptions
{
    /// <summary>How often to run cleanup (default: 24 hours)</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Retention period (default: 90 days per AUDIT-04)</summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(90);

    /// <summary>Batch size for deletion (default: 10,000)</summary>
    public int BatchSize { get; set; } = 10_000;

    /// <summary>Whether cleanup is enabled (default: true)</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class AuditCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditCleanupService> _logger;
    private readonly AuditCleanupOptions _options;
    private readonly TimeProvider _timeProvider;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;

        // Initial delay (don't run cleanup immediately on startup)
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOldRecordsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during audit cleanup");
            }

            await Task.Delay(_options.Interval, stoppingToken);
        }
    }

    private async Task CleanupOldRecordsAsync(CancellationToken ct)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - _options.RetentionPeriod;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<YourDbContext>();

        var totalDeleted = 0;
        int deleted;

        // Batch delete to avoid long-running transactions
        do
        {
            deleted = await db.ApiAuditRecords
                .Where(r => r.TimestampUtc < cutoff)
                .Take(_options.BatchSize)
                .ExecuteDeleteAsync(ct);

            totalDeleted += deleted;
        } while (deleted == _options.BatchSize && !ct.IsCancellationRequested);

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "Audit cleanup completed: {Count} records older than {Cutoff:yyyy-MM-dd} deleted",
                totalDeleted, cutoff);
        }
    }
}
```

### Why Not PostgreSQL Procedures?

- EF Core migrations handle schema; keep cleanup in application code
- Application-level logging of cleanup operations
- Configurable via IOptions pattern
- Testable with TimeProvider
- Consistent with `TokenCleanupService` pattern already in codebase

## Codebase Patterns to Follow

### Existing Audit Pattern (Identity Service)

| File | Pattern |
|------|---------|
| `Data/Entities/AuditEvent.cs` | Entity structure with standard fields |
| `Data/Configuration/AuditEventConfiguration.cs` | EF Core fluent configuration with indexes |
| `Services/AuditService.cs` | Interface + implementation with TimeProvider |
| `Services/AuditEventTypes.cs` | Constants for event types |

### Existing Background Service Pattern

| File | Pattern |
|------|---------|
| `Services/TokenCleanupService.cs` | Options class, IServiceScopeFactory, batch delete |
| `Services/TokenCleanupServiceExtensions.cs` | DI registration extension |

### Existing Middleware Pattern

| File | Pattern |
|------|---------|
| `Middleware/RequestLoggingMiddleware.cs` | Stopwatch timing, try/finally for response capture |
| `Middleware/CorrelationMiddleware.cs` | HttpContext.Items for passing data |
| `Middleware/TenantEnrichmentMiddleware.cs` | Claim extraction from context.User |

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| In-memory queue | Custom ConcurrentQueue wrapper | `System.Threading.Channels` | Async-friendly, backpressure, optimized |
| Batch inserts | Manual SQL batching | `EF Core AddRange + SaveChanges` | EF Core 8+ does batching automatically |
| Background processing | Custom ThreadPool scheduling | `BackgroundService` | Handles shutdown gracefully |
| Timestamp handling | `DateTime.UtcNow` directly | `TimeProvider.GetUtcNow()` | Testable with fake time |
| User ID extraction | Manual claim parsing | Helper extension method | Reuse pattern from `ClaimsPrincipalExtensions.cs` |

## Common Pitfalls

### Pitfall 1: Blocking Request Thread with Database Write

**What goes wrong:** Audit INSERT blocks request response, adding latency
**Why it happens:** Direct `await SaveChangesAsync()` in middleware
**How to avoid:** Use Channel + BackgroundService pattern (non-blocking queue)
**Warning signs:** P99 latency increases after audit is added

### Pitfall 2: Losing Audit Data on Service Restart

**What goes wrong:** Queued audit events lost when service restarts
**Why it happens:** In-memory channel not persisted
**How to avoid:**
- Accept this tradeoff (audit is not transactional with the request)
- Configure graceful shutdown with `HostOptions.ShutdownTimeout`
- Drain queue before shutdown (BackgroundService.StopAsync)
**Warning signs:** Audit gaps correlating with deployments

### Pitfall 3: Connection Pool Exhaustion from Batch Writes

**What goes wrong:** Background writer holds connections too long
**Why it happens:** Large batches with slow INSERT performance
**How to avoid:**
- Use separate connection pool for audit (configure in DbContext)
- Keep batch size reasonable (100-500 records)
- Use `ExecuteDeleteAsync` which doesn't load entities
**Warning signs:** "Connection pool exhausted" errors in logs

### Pitfall 4: Indexes Slowing Down Inserts

**What goes wrong:** Write throughput drops as table grows
**Why it happens:** Too many indexes or wrong index order
**How to avoid:**
- Only create indexes for actual query patterns
- Leading column should be high-cardinality filter (timestamp, tenant_id)
- Consider BRIN indexes for timestamp if purely chronological
**Warning signs:** INSERT latency correlating with table size

### Pitfall 5: Capturing Too Much Data

**What goes wrong:** Storage costs explode, privacy/compliance issues
**Why it happens:** Logging request/response bodies, all headers
**How to avoid:**
- Per AUDIT-02: Only timestamp, user ID, tenant ID, action, resource, outcome
- No request bodies, no response bodies
- Truncate user agent to 256 chars
**Warning signs:** Audit table growing faster than expected

## Code Examples

### Complete Audit Queue Implementation

```csharp
// Source: Based on Microsoft Queue Service pattern
// https://learn.microsoft.com/en-us/dotnet/core/extensions/queue-service

public interface IAuditQueue
{
    ValueTask QueueAsync(ApiAuditRecord record, CancellationToken ct = default);
    IAsyncEnumerable<ApiAuditRecord> ReadAllAsync(CancellationToken ct);
}

public sealed class AuditQueue : IAuditQueue
{
    private readonly Channel<ApiAuditRecord> _channel;

    public AuditQueue(IOptions<AuditQueueOptions> options)
    {
        _channel = Channel.CreateBounded<ApiAuditRecord>(
            new BoundedChannelOptions(options.Value.Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true, // Only one BackgroundService reads
                SingleWriter = false // Multiple middleware instances write
            });
    }

    public ValueTask QueueAsync(ApiAuditRecord record, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(record, ct);

    public IAsyncEnumerable<ApiAuditRecord> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}

public sealed class AuditQueueOptions
{
    public int Capacity { get; set; } = 10_000;
}
```

### Audit Writer Background Service

```csharp
public sealed class AuditWriterService : BackgroundService
{
    private readonly IAuditQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditWriterService> _logger;
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;

    public AuditWriterService(
        IAuditQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AuditWriterService> logger,
        IOptions<AuditWriterOptions> options)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _batchSize = options.Value.BatchSize;
        _flushInterval = options.Value.FlushInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ApiAuditRecord>(_batchSize);

        await foreach (var record in _queue.ReadAllAsync(stoppingToken))
        {
            batch.Add(record);

            if (batch.Count >= _batchSize)
            {
                await FlushBatchAsync(batch, stoppingToken);
                batch.Clear();
            }
        }

        // Drain remaining on shutdown
        if (batch.Count > 0)
        {
            await FlushBatchAsync(batch, CancellationToken.None);
        }
    }

    private async Task FlushBatchAsync(List<ApiAuditRecord> batch, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<YourDbContext>();

            db.ApiAuditRecords.AddRange(batch);
            await db.SaveChangesAsync(ct);

            _logger.LogDebug("Flushed {Count} audit records", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write {Count} audit records", batch.Count);
            // Records are lost - acceptable tradeoff per design
        }
    }
}

public sealed class AuditWriterOptions
{
    public int BatchSize { get; set; } = 100;
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);
}
```

### DI Registration Extension

```csharp
public static class AuditExtensions
{
    public static IServiceCollection AddAuditInfrastructure(
        this IServiceCollection services,
        Action<AuditOptions>? configure = null)
    {
        services.Configure<AuditQueueOptions>(o => { });
        services.Configure<AuditWriterOptions>(o => { });
        services.Configure<AuditCleanupOptions>(o => { });

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<IAuditQueue, AuditQueue>();
        services.AddHostedService<AuditWriterService>();
        services.AddHostedService<AuditCleanupService>();

        return services;
    }

    public static IApplicationBuilder UseAuditMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AuditMiddleware>();
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Sync audit writes | Async via Channels | .NET 5+ | Zero latency impact |
| ConcurrentQueue | System.Threading.Channels | .NET Core 3.0 | Async-native, backpressure |
| Custom background threads | BackgroundService | .NET Core 2.1 | Graceful shutdown |
| Manual SQL batching | EF Core batching | EF Core 7.0+ | Automatic optimization |
| DateTime.UtcNow | TimeProvider | .NET 8 | Testable time |

**Deprecated/outdated:**
- `OpenTelemetry.Instrumentation.MassTransit`: Native support in MassTransit 8+
- Audit.NET library: Good for complex scenarios, overkill for this use case
- EF Core SaveChanges interceptors: Better for entity-level audit, not HTTP request audit

## Open Questions

1. **Cross-service audit queries**
   - What we know: Each service has its own audit table
   - What's unclear: How to query "all actions by user X across all services"
   - Recommendation: v1 uses service-specific queries; v2 can add Grafana Loki queries on structured logs or PostgreSQL foreign data wrappers

2. **Audit during failures**
   - What we know: Queue is in-memory, lost on crash
   - What's unclear: Compliance requirement for durability
   - Recommendation: Document as known limitation; for true durability, would need to add MassTransit outbox pattern

3. **Rate of audit data growth**
   - What we know: Fields per record ~ 500 bytes
   - What's unclear: Actual request volume per service
   - Recommendation: Monitor table size, implement partitioning when > 10M rows

## Sources

### Primary (HIGH confidence)
- [Microsoft: Create a Queue Service](https://learn.microsoft.com/en-us/dotnet/core/extensions/queue-service) - Channel pattern for background processing
- [Microsoft: Background tasks with hosted services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services) - BackgroundService pattern
- [PostgreSQL: Table Partitioning](https://www.postgresql.org/docs/current/ddl-partitioning.html) - Partitioning strategy

### Secondary (MEDIUM confidence)
- [EF Core Interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors) - SaveChanges patterns
- [When to Consider Postgres Partitioning](https://www.tigerdata.com/learn/when-to-consider-postgres-partitioning) - Partitioning decision criteria

### Codebase (HIGH confidence)
- `src/Dhadgar.Identity/Data/Entities/AuditEvent.cs` - Existing audit entity pattern
- `src/Dhadgar.Identity/Services/AuditService.cs` - Existing audit CRUD pattern
- `src/Dhadgar.Identity/Services/TokenCleanupService.cs` - Background cleanup pattern
- `src/Shared/Dhadgar.ServiceDefaults/Middleware/RequestLoggingMiddleware.cs` - Request capture pattern

## Metadata

**Confidence breakdown:**
- Architecture decision: HIGH - Aligns with existing patterns, no external dependencies
- Write strategy: HIGH - Channel pattern is Microsoft-recommended
- Schema design: HIGH - Based on existing AuditEvent entity
- Middleware implementation: HIGH - Follows existing middleware patterns
- Cleanup strategy: HIGH - Based on existing TokenCleanupService

**Research date:** 2026-01-21
**Valid until:** 2026-02-21 (30 days - stable .NET patterns)

# Domain Pitfalls: Logging, Auditing & Error Handling in .NET Microservices

**Domain:** Observability infrastructure for multi-tenant SaaS (13+ microservices)
**Project:** Meridian Console / Dhadgar
**Researched:** 2026-01-20
**Confidence:** HIGH (verified with official docs, existing codebase analysis, and multiple authoritative sources)

---

## Critical Pitfalls

Mistakes that cause rewrites, security incidents, or major production issues.

---

### PITFALL-01: Synchronous Audit Database Writes Blocking Request Pipeline

**What goes wrong:** Every API call writes an audit record synchronously to the database before returning the response. Under load, database latency directly impacts response times. A slow database query or connection pool exhaustion cascades into timeout errors across all services.

**Why it happens:** The existing `AuditService.RecordAsync()` in `Dhadgar.Identity/Services/AuditService.cs` calls `SaveChangesAsync()` immediately for each audit event. While async, it still awaits the database round-trip before the request completes. Scaling this to "all API calls audited" means every request pays this cost.

**Consequences:**
- P99 latency increases by database write time (5-50ms per request)
- Database connection pool exhaustion under load (13 services x N instances x concurrent requests)
- Request timeouts when database is slow or briefly unavailable
- Audit failures causing request failures (if not handled)

**Warning signs:**
- Request latency increases proportionally with audit table size
- Connection pool exhaustion errors in logs
- Grafana shows database as bottleneck on request path

**Prevention:**
1. **Decouple audit writes from request pipeline** using a background channel:
   ```csharp
   // In middleware: enqueue, don't await
   _auditChannel.Writer.TryWrite(auditEvent);

   // Background service: batch and flush
   var batch = new List<AuditEvent>();
   await foreach (var evt in _channel.Reader.ReadAllAsync(ct))
   {
       batch.Add(evt);
       if (batch.Count >= 500 || /* timeout */)
       {
           await BulkInsertAsync(batch, ct);
           batch.Clear();
       }
   }
   ```
2. **Use MassTransit outbox** (already configured) to publish audit events as messages
3. **Accept eventual consistency** for audit records (they're not part of the business transaction)

**Which phase should address:** Phase 1 - Core audit infrastructure design. Do NOT implement synchronous per-request writes as the base pattern.

**Existing codebase reference:** `AuditService.cs` lines 72-112 show the current synchronous pattern that should be evolved.

---

### PITFALL-02: Sensitive Data Exposure in Logs (PII Leakage)

**What goes wrong:** Logs contain email addresses, passwords, API keys, JWT tokens, IP addresses, or other PII. This violates GDPR/CCPA, creates breach liability, and can lead to credential exposure if logs are compromised or shipped to third-party services.

**Why it happens:**
- Developers log entire request/response bodies for debugging
- Exception messages include user input (e.g., "Invalid password for user@example.com")
- Structured logging with `{@Request}` destructures objects containing secrets
- Authorization headers get logged in HTTP request logging

**Consequences:**
- GDPR/CCPA violations with fines up to 4% of global revenue
- Security incidents from exposed credentials in logs
- Compliance audit failures
- Customer trust damage

**Warning signs:**
- Grep for `@gmail.com`, `password`, `bearer`, `api_key` in log files
- Log aggregator shows email addresses in query results
- Audit finds PII in Loki/Elasticsearch indices

**Prevention:**
1. **Build scrubbing middleware** that intercepts log messages:
   ```csharp
   public static class SensitiveDataScrubber
   {
       private static readonly Regex EmailPattern = new(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
       private static readonly Regex JwtPattern = new(@"eyJ[A-Za-z0-9-_]+\.eyJ[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+");

       public static string Scrub(string message)
       {
           message = EmailPattern.Replace(message, "[EMAIL]");
           message = JwtPattern.Replace(message, "[JWT]");
           // Add more patterns...
           return message;
       }
   }
   ```
2. **Never log request/response bodies** - log only what you need (method, path, status, duration)
3. **Use structured logging with explicit fields** - avoid `{@Object}` destructuring
4. **Create allow-list, not deny-list** for loggable properties

**Which phase should address:** Phase 2 - Sensitive data scrubbing middleware. Must be in place before rolling out comprehensive logging.

**Existing codebase reference:** `RequestLoggingMiddleware.cs` already logs only method, path, and status - good pattern. Ensure this discipline is maintained as logging expands.

---

### PITFALL-03: Missing or Broken Correlation ID Propagation

**What goes wrong:** Distributed traces are fragmented - spans don't connect into complete traces. When debugging a failed request, logs from different services can't be correlated. "You see spans in your backend, but they aren't stitched together into a full trace."

**Why it happens:**
- Upstream service doesn't inject trace headers into outgoing HTTP calls
- Downstream service doesn't extract incoming headers
- Mixed propagation formats (W3C Trace Context vs B3)
- Background jobs/message consumers don't restore context
- HttpClient calls made without instrumentation

**Consequences:**
- Cannot trace a request across service boundaries
- Debugging distributed failures requires manual log timestamp matching
- MTTR (mean time to recovery) increases dramatically
- OpenTelemetry investment provides incomplete value

**Warning signs:**
- Jaeger/Tempo shows many single-span traces instead of multi-span traces
- Logs for same correlation ID appear in some services but not others
- MassTransit consumers have no trace context

**Prevention:**
1. **Standardize on W3C Trace Context** (OpenTelemetry default):
   ```csharp
   // Already configured in services - verify all HttpClients use instrumentation
   builder.Services.AddOpenTelemetry()
       .WithTracing(tracing => tracing
           .AddAspNetCoreInstrumentation()  // Extracts incoming context
           .AddHttpClientInstrumentation()); // Injects outgoing context
   ```
2. **Verify MassTransit propagation** - ensure OpenTelemetry integration is enabled:
   ```csharp
   cfg.UsePublishFilter(typeof(OpenTelemetryFilter<>), context);
   cfg.UseConsumeFilter(typeof(OpenTelemetryFilter<>), context);
   ```
3. **Test correlation end-to-end** before declaring success:
   ```bash
   # Make request, capture correlation ID
   # Query Loki: {correlationId="abc"} | count by service
   # Should show entries from all services in the call chain
   ```
4. **Handle message consumers explicitly** - create new scope from baggage

**Which phase should address:** Phase 1 - Verify existing OpenTelemetry setup. Phase 3 - Integration testing for end-to-end trace validation.

**Existing codebase reference:** OpenTelemetry is configured per-service (13 locations). Need centralization in ServiceDefaults and verification that all HTTP clients are instrumented.

---

## Moderate Pitfalls

Mistakes that cause delays, technical debt, or degraded observability.

---

### PITFALL-04: Log Level Misconfiguration (Debug in Production)

**What goes wrong:** Debug or Trace level logging accidentally enabled in production causes massive log volume, storage costs, and performance degradation. "In a test with millions of iterations, logging without checks allocated 3.6GB of memory and caused 2 seconds of garbage collection pauses."

**Why it happens:**
- `appsettings.Development.json` with Debug levels gets deployed
- Environment-specific config not properly overridden
- Category-specific levels not understood (e.g., EF Core at Debug logs every SQL query)
- No log level validation on deployment

**Consequences:**
- Log storage costs explode (Debug logs can be 100x volume of Info)
- Application performance degrades from serialization overhead
- Log aggregator becomes slow to query
- Signal-to-noise ratio makes real issues harder to find

**Warning signs:**
- Log volume spikes after deployment
- Seeing SQL queries in production logs
- Grafana Loki queries timing out
- Storage usage growing unexpectedly

**Prevention:**
1. **Set explicit production levels in appsettings.json**:
   ```json
   {
     "Logging": {
       "LogLevel": {
         "Default": "Information",
         "Microsoft.AspNetCore": "Warning",
         "Microsoft.EntityFrameworkCore": "Warning",
         "System.Net.Http.HttpClient": "Warning"
       }
     }
   }
   ```
2. **Use `LoggerMessage` source generators** for hot paths:
   ```csharp
   // Already used in SecurityEventLogger.cs - good pattern
   [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "...")]
   private partial void AuthenticationSucceeded(...);
   ```
3. **Wrap expensive log calls with IsEnabled**:
   ```csharp
   if (_logger.IsEnabled(LogLevel.Debug))
       _logger.LogDebug("Complex object: {Data}", expensiveSerialize(data));
   ```
4. **Add deployment validation** checking log levels match environment

**Which phase should address:** Phase 2 - Log configuration standardization. Review all `appsettings.*.json` files.

**Existing codebase reference:** `SecurityEventLogger.cs` uses source-generated logging correctly. Expand this pattern to all high-volume logging.

---

### PITFALL-05: Audit Log Table Growth Without Retention Strategy

**What goes wrong:** Audit tables grow unbounded. After months of operation, queries become slow, storage fills up, and there's no compliant way to delete old records without breaking referential integrity or losing required retention data.

**Why it happens:**
- Focus on capturing data, not managing it
- No index on `OccurredAtUtc` for time-range queries
- Retention requirements not defined upfront
- `DELETE` statements lock table and block writes

**Consequences:**
- Query performance degrades as table grows
- Storage costs increase linearly forever
- Compliance issues if data retained too long (GDPR) or deleted too soon (SOX)
- Table maintenance becomes emergency when disk fills

**Warning signs:**
- Audit queries getting slower over time
- Table size growing 1GB+ per month
- No automated cleanup jobs
- EXPLAIN shows table scans instead of index seeks

**Prevention:**
1. **Define retention requirements upfront**:
   - Security events: 90 days (SIEM), 1 year archive
   - API audit: 30 days hot, 90 days cold
   - Authentication failures: 7 days
2. **Implement table partitioning by time**:
   ```sql
   -- PostgreSQL partitioning
   CREATE TABLE audit_events (
       id UUID PRIMARY KEY,
       occurred_at_utc TIMESTAMPTZ NOT NULL
   ) PARTITION BY RANGE (occurred_at_utc);

   CREATE TABLE audit_events_2026_01 PARTITION OF audit_events
       FOR VALUES FROM ('2026-01-01') TO ('2026-02-01');
   ```
3. **Create cleanup job using existing pattern**:
   ```csharp
   // Already exists in AuditService.DeleteEventsBeforeAsync()
   // Need to schedule it as background job
   ```
4. **Add composite index on (occurred_at_utc, event_type)** for common queries

**Which phase should address:** Phase 1 - Design audit schema with partitioning. Phase 4 - Implement retention background service.

**Existing codebase reference:** `AuditService.cs` has `DeleteEventsBeforeAsync()` but no scheduling. `AuditEventConfiguration.cs` needs index additions.

---

### PITFALL-06: Problem Details Leaking Stack Traces in Production

**What goes wrong:** RFC 7807 Problem Details responses include stack traces or detailed exception messages that reveal internal architecture, file paths, or connection strings to attackers.

**Why it happens:**
- Development-friendly error details not disabled in production
- Generic exception handler logs exception but also returns it
- Developers want details for debugging and forget to remove

**Consequences:**
- Information disclosure vulnerability (OWASP Top 10)
- Attackers learn internal structure for targeted attacks
- Customer-facing errors show confusing technical details
- Professional appearance undermined

**Warning signs:**
- API responses contain `.cs` file paths or line numbers
- Stack traces visible in network tab
- Connection strings or secrets in error responses
- Pentest report flags information disclosure

**Prevention:**
1. **Verify environment check in ProblemDetailsMiddleware**:
   ```csharp
   // Already correct in existing code - verify it stays this way
   var includeDetails = _environment.IsDevelopment()
       || _environment.IsEnvironment("Testing");
   ```
2. **Classify exceptions by type** - return appropriate detail level:
   ```csharp
   var (status, detail) = exception switch
   {
       ValidationException ve => (400, ve.Message), // Safe to expose
       NotFoundException nf => (404, nf.Message),   // Safe to expose
       UnauthorizedException => (401, "Authentication required"),
       ForbiddenException => (403, "Access denied"),
       _ => (500, "An unexpected error occurred") // Generic for unknown
   };
   ```
3. **Log full exception server-side, return sanitized client response**
4. **Security test error handling** as part of each PR

**Which phase should address:** Phase 1 - Exception classification taxonomy. Phase 3 - Security review of all error responses.

**Existing codebase reference:** `ProblemDetailsMiddleware.cs` already has environment check on line 62. Need to expand exception classification beyond generic 500.

---

### PITFALL-07: Dual Logging Sinks Without Async/Batching

**What goes wrong:** Writing to both files and database synchronously doubles the I/O cost per log message. File locks and database round-trips compound, causing log calls to become expensive.

**Why it happens:**
- Plan calls for "dual output: files + database"
- Both sinks configured synchronously
- Not using Serilog.Sinks.Async wrapper
- Database sink writes one row per log message

**Consequences:**
- Logging latency adds to every request
- File I/O blocks when disk is slow
- Database becomes bottleneck for logging
- Under load, logging degrades performance significantly

**Warning signs:**
- High latency on log write operations
- Logging code shows up in profiler hot paths
- Database CPU elevated from log inserts
- Log files show gaps during high load (dropped messages)

**Prevention:**
1. **Use async wrapper for file sink**:
   ```csharp
   .WriteTo.Async(a => a.File("logs/app.log",
       rollingInterval: RollingInterval.Day))
   ```
2. **Batch database writes** instead of per-message:
   ```csharp
   // Use periodic batch writing
   var batch = await channel.Reader.ReadBatchAsync(500, TimeSpan.FromSeconds(1));
   await context.BulkInsertAsync(batch);
   ```
3. **Use different sinks for different purposes**:
   - Console/File: Real-time debugging (Serilog + Loki)
   - Database: Audit-only (structured queries needed)
4. **Set buffer limits** to drop logs under extreme load rather than block:
   ```csharp
   .WriteTo.Async(a => a.File(...), bufferSize: 10000)
   ```

**Which phase should address:** Phase 2 - Configure Serilog sinks with async wrappers. Phase 3 - Separate audit database writes from real-time logging.

---

### PITFALL-08: Console Logging Enabled in Production

**What goes wrong:** Console logging left enabled in containerized production deployments adds overhead and can cause issues with container log collection. "It is very important to turn off console logging in your production environment. Console logging can slow down your application significantly."

**Why it happens:**
- Default ASP.NET Core template includes console logging
- Kubernetes/Docker already capture stdout, so console seems redundant
- Developers forget to disable in production config

**Consequences:**
- Doubled logging (console + Loki scraping console)
- Performance overhead from console writes
- Log interleaving issues in high-concurrency scenarios
- Container log sizes bloat

**Warning signs:**
- Same logs appearing twice in aggregator
- High CPU from logging under load
- Container logs larger than expected

**Prevention:**
1. **Conditionally enable console** in ServiceDefaults:
   ```csharp
   if (builder.Environment.IsDevelopment())
   {
       builder.Logging.AddConsole();
   }
   else
   {
       // OTLP exporter to Loki directly
       builder.Logging.AddOpenTelemetry(options =>
           options.AddOtlpExporter());
   }
   ```
2. **Prefer OTLP export** over console+scraping in production
3. **Review logging configuration** in CI/CD pipeline

**Which phase should address:** Phase 2 - Standardize logging configuration in ServiceDefaults.

---

## Minor Pitfalls

Mistakes that cause annoyance but are fixable.

---

### PITFALL-09: Inconsistent Log Message Formats Across Services

**What goes wrong:** Each service uses different log message templates, making it hard to create consistent Grafana dashboards or write queries that work across all services.

**Why it happens:**
- No shared logging conventions
- Different developers, different styles
- No code review focus on log messages

**Consequences:**
- Queries need service-specific variations
- Dashboard maintenance burden
- Harder to train new team members
- Inconsistent correlation ID field names

**Prevention:**
1. **Define logging conventions in ServiceDefaults**
2. **Use shared message templates**:
   ```csharp
   public static class LogTemplates
   {
       public const string HttpRequest = "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms";
       public const string DatabaseQuery = "DB {Operation} on {Table} took {ElapsedMs}ms, {RowCount} rows";
   }
   ```
3. **Standardize structured property names**:
   - `CorrelationId` (not `correlationId`, `TraceId`, `RequestId`)
   - `ElapsedMs` (not `Duration`, `TimeMs`, `Elapsed`)
   - `UserId` (not `UserID`, `user_id`, `userId`)

**Which phase should address:** Phase 1 - Define conventions. Phase 2 - Implement shared templates.

**Existing codebase reference:** `CorrelationMiddleware` uses `CorrelationId` consistently - good. `RequestLoggingMiddleware` uses `ElapsedMs` - good. Document these as standards.

---

### PITFALL-10: Audit Event Types Not Standardized

**What goes wrong:** Different services use different strings for similar events (`user.login` vs `user.authenticated` vs `auth.success`), making aggregate queries difficult.

**Why it happens:**
- No shared event type registry
- Services developed independently
- No validation of event types

**Consequences:**
- Queries miss events due to typos or variations
- Reports show incomplete data
- Difficult to know what events exist

**Prevention:**
1. **Use centralized event type constants**:
   ```csharp
   // Already exists in AuditEventTypes - extend and share
   public static class AuditEventTypes
   {
       // Authentication
       public const string UserAuthenticated = "user.authenticated";
       // ... already comprehensive
   }
   ```
2. **Move to Dhadgar.Contracts** for cross-service sharing
3. **Validate event types** in audit service

**Which phase should address:** Phase 1 - Consolidate existing constants in Contracts.

**Existing codebase reference:** `AuditService.cs` has `AuditEventTypes` class with good coverage. Move to shared project.

---

### PITFALL-11: Over-Enrichment of Log Context

**What goes wrong:** Adding too many fields to every log message bloats log storage and makes queries slower. Every log message includes 20+ fields when 5 would suffice.

**Why it happens:**
- Enthusiasm for "full context"
- Copy-pasting enrichment from examples
- No review of what's actually needed

**Consequences:**
- Log storage costs 3-5x higher than necessary
- Query performance degradation
- Important fields buried in noise

**Prevention:**
1. **Start minimal, add when needed**:
   ```csharp
   // Essential: CorrelationId, Timestamp, Level, Message
   // Conditional: UserId (if authenticated), OrgId (if scoped)
   // Avoid: Full request headers, all claims, environment variables
   ```
2. **Different enrichment per sink** - database gets less, file gets more
3. **Review enrichment quarterly** - remove unused fields

**Which phase should address:** Phase 2 - Define enrichment levels per sink.

---

## Phase-Specific Warnings

| Phase | Likely Pitfall | Mitigation |
|-------|---------------|------------|
| Phase 1: Design | PITFALL-01 (sync writes) | Design async audit from start |
| Phase 1: Design | PITFALL-03 (correlation) | Verify OTEL propagation works E2E |
| Phase 2: Implementation | PITFALL-02 (PII exposure) | Build scrubber before expanding logging |
| Phase 2: Implementation | PITFALL-04 (log levels) | Standardize config in ServiceDefaults |
| Phase 2: Implementation | PITFALL-07 (sync sinks) | Use async wrappers from start |
| Phase 3: Integration | PITFALL-06 (stack traces) | Security review error responses |
| Phase 3: Integration | PITFALL-09 (inconsistency) | Audit log formats across services |
| Phase 4: Operations | PITFALL-05 (table growth) | Deploy retention jobs with rollout |

---

## Checklist Before Milestone Completion

### Design Phase
- [ ] Audit writes are decoupled from request pipeline
- [ ] Retention requirements defined per event category
- [ ] Event type taxonomy documented
- [ ] Correlation ID propagation verified end-to-end

### Implementation Phase
- [ ] Sensitive data scrubber implemented and tested
- [ ] Log levels standardized in all appsettings files
- [ ] Sinks use async/batching
- [ ] Console logging disabled in production config

### Integration Phase
- [ ] Problem Details never expose stack traces in production
- [ ] All services use same log field names
- [ ] Grafana dashboards work across all services
- [ ] E2E trace appears correctly in Jaeger/Tempo

### Operations Phase
- [ ] Retention job scheduled and tested
- [ ] Log volume monitoring alerts configured
- [ ] Database audit table has appropriate indexes
- [ ] Storage growth rate documented

---

## Sources

### Verified (HIGH confidence)
- Existing codebase analysis: `AuditService.cs`, `SecretsAuditLogger.cs`, `RequestLoggingMiddleware.cs`, `ProblemDetailsMiddleware.cs`, `SecurityEventLogger.cs`
- [OpenTelemetry Log Correlation - .NET](https://opentelemetry.io/docs/languages/dotnet/logs/correlation/)
- [Microsoft High-Performance Logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/high-performance-logging)
- [RFC 9457 (successor to RFC 7807)](https://blog.frankel.ch/problem-details-http-apis/)

### Cross-Referenced (MEDIUM confidence)
- [Serilog Async Sinks](https://github.com/serilog/serilog-sinks-async)
- [EF Core Audit Logging Guide](https://developersvoice.com/blog/database/efcore_audit_implementation_guide/)
- [OWASP Microservices Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Microservices_Security_Cheat_Sheet.html)
- [Better Stack - Logging Sensitive Data](https://betterstack.com/community/guides/logging/sensitive-data/)
- [Better Stack - Logging in Microservices](https://betterstack.com/community/guides/logging/logging-microservices/)
- [OpenTelemetry Context Propagation](https://opentelemetry.io/docs/concepts/context-propagation/)

### WebSearch-Only (LOW confidence - validate before implementing)
- [Audit Log Database Architecture Blueprint](https://www.myshyft.com/blog/audit-log-database-architecture/)
- [Splunk Audit Logs Guide](https://www.splunk.com/en_us/blog/learn/audit-logs.html)

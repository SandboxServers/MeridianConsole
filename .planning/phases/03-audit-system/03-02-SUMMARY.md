---
phase: 03-audit-system
plan: 03-02
subsystem: audit
tags: [audit, database, ef-core, migrations, servers, identity]
dependency-graph:
  requires: [03-01-audit-infrastructure]
  provides: [servers-audit, identity-audit, audit-tables, audit-indexes]
  affects: [03-03-audit-tests]
tech-stack:
  added: []
  patterns:
    - DbContext implements IAuditDbContext
    - EF Core configuration with composite indexes
    - Middleware registration after authentication
key-files:
  created:
    - src/Dhadgar.Servers/Data/Configuration/ApiAuditRecordConfiguration.cs
    - src/Dhadgar.Identity/Data/Configuration/ApiAuditRecordConfiguration.cs
    - src/Dhadgar.Servers/Data/Migrations/20260122151315_AddApiAuditRecords.cs
    - src/Dhadgar.Identity/Data/Migrations/20260122151340_AddApiAuditRecords.cs
  modified:
    - src/Dhadgar.Servers/Data/ServersDbContext.cs
    - src/Dhadgar.Servers/Program.cs
    - src/Dhadgar.Identity/Data/IdentityDbContext.cs
    - src/Dhadgar.Identity/Program.cs
decisions:
  - decision: "Both services use identical ApiAuditRecordConfiguration"
    rationale: "Same query patterns apply across services; consistent indexing strategy"
  - decision: "Middleware placed after UseAuthorization in Identity"
    rationale: "Identity has full auth stack; middleware captures authenticated requests immediately after auth"
  - decision: "Middleware placed after UseDhadgarMiddleware in Servers"
    rationale: "Servers has no auth yet; middleware will skip all requests (correct behavior per AUDIT-01)"
metrics:
  duration: 4 minutes
  completed: 2026-01-22
---

# Phase 3 Plan 2: Service Integration and Database Schema Summary

Servers and Identity services integrated with audit infrastructure. Database tables with optimized indexes ready for deployment.

## What Was Built

### 1. Servers Service Integration

**ServersDbContext.cs:**
- Implements `IAuditDbContext` interface
- Added `DbSet<ApiAuditRecord> ApiAuditRecords` property
- Applies `ApiAuditRecordConfiguration` in `OnModelCreating`

**Program.cs:**
- Added `using Dhadgar.ServiceDefaults.Audit;`
- Registered `AddAuditInfrastructure<ServersDbContext>()`
- Added `UseAuditMiddleware()` with comment about future auth

**ApiAuditRecordConfiguration.cs:**
- Table: `api_audit_records`
- Indexes:
  - `ix_audit_user_time` (UserId, TimestampUtc DESC)
  - `ix_audit_tenant_time` (TenantId, TimestampUtc DESC)
  - `ix_audit_timestamp` (TimestampUtc) - for cleanup
  - `ix_audit_resource_time` (ResourceType, ResourceId, TimestampUtc DESC)

### 2. Identity Service Integration

**IdentityDbContext.cs:**
- Implements `IAuditDbContext` interface (alongside existing IdentityDbContext inheritance)
- Added `DbSet<ApiAuditRecord> ApiAuditRecords` property with XML doc explaining distinction from domain `AuditEvents`
- Applies `ApiAuditRecordConfiguration` in `OnModelCreating`

**Program.cs:**
- Added `using Dhadgar.ServiceDefaults.Audit;`
- Registered `AddAuditInfrastructure<IdentityDbContext>()`
- Added `UseAuditMiddleware()` after `UseAuthorization()`

**ApiAuditRecordConfiguration.cs:**
- Identical index configuration to Servers
- XML documentation notes distinction from `AuditEventConfiguration`

### 3. Database Migrations

**Servers Migration (20260122151315_AddApiAuditRecords):**
- Creates `api_audit_records` table
- Creates all four indexes
- Also creates `Sample` table (first migration for this DbContext)

**Identity Migration (20260122151340_AddApiAuditRecords):**
- Creates `api_audit_records` table
- Creates all four indexes
- Also adds pending `AvatarUrl` column to users table

## Database Schema

```sql
CREATE TABLE api_audit_records (
    "Id" uuid PRIMARY KEY,
    "TimestampUtc" timestamp with time zone NOT NULL,
    "UserId" uuid,
    "TenantId" uuid,
    "HttpMethod" character varying(10) NOT NULL,
    "Path" character varying(500) NOT NULL,
    "ResourceId" uuid,
    "ResourceType" character varying(50),
    "StatusCode" integer NOT NULL,
    "DurationMs" bigint NOT NULL,
    "ClientIp" character varying(45),
    "UserAgent" character varying(256),
    "CorrelationId" character varying(64),
    "TraceId" character varying(32),
    "ServiceName" character varying(50)
);

CREATE INDEX ix_audit_user_time ON api_audit_records("UserId", "TimestampUtc" DESC);
CREATE INDEX ix_audit_tenant_time ON api_audit_records("TenantId", "TimestampUtc" DESC);
CREATE INDEX ix_audit_timestamp ON api_audit_records("TimestampUtc");
CREATE INDEX ix_audit_resource_time ON api_audit_records("ResourceType", "ResourceId", "TimestampUtc" DESC);
```

## Key Implementation Details

**Two Audit Tables in Identity:**
```
audit_events       - Domain events (user.created, role.assigned, etc.)
api_audit_records  - HTTP request audit (API compliance logging)
```

These serve different purposes and coexist. Domain events capture business actions; API audit captures all authenticated HTTP activity.

**Middleware Order:**

Servers (no auth yet):
```
Correlation -> TenantEnrichment -> RequestLogging -> ProblemDetails -> AuditMiddleware
```

Identity (full auth):
```
Correlation -> TenantEnrichment -> ProblemDetails -> RequestLogging -> Authentication -> Authorization -> AuditMiddleware -> RateLimiter
```

**Current Behavior:**
- Servers: AuditMiddleware skips all requests (no authenticated users)
- Identity: AuditMiddleware captures all authenticated API requests

## Deviations from Plan

None - plan executed exactly as written.

## Commits

| Hash | Type | Description |
|------|------|-------------|
| a0a0313 | feat | Integrate audit infrastructure into Servers service |
| b7ff632 | feat | Integrate audit infrastructure into Identity service |
| d2ec6e5 | feat | Create EF Core migrations for api_audit_records table |

## Verification Results

All must_haves confirmed:

- [x] ServersDbContext implements IAuditDbContext
- [x] IdentityDbContext implements IAuditDbContext
- [x] Both have ApiAuditRecordConfiguration with `ix_audit_user_time` index
- [x] `AddAuditInfrastructure<ServersDbContext>` registered
- [x] `AddAuditInfrastructure<IdentityDbContext>` registered
- [x] `UseAuditMiddleware` registered in both services
- [x] Migrations created with correct schema and indexes

Build verification: Both services compile successfully.

## Next Phase Readiness

### Ready for 03-03 (Testing)
- [ ] Unit tests for audit configurations
- [ ] Integration tests for middleware behavior
- [ ] Database migration verification tests

### Deployment Notes
1. Apply migrations: `dotnet ef database update` for each service
2. In dev mode, migrations auto-apply on startup
3. Docker compose will apply on first run

### Testing Pattern
To test the full flow without real auth:
1. Start services with docker-compose
2. Create test endpoint that sets a mock authenticated user
3. Call endpoint and verify audit record created
4. Or use Identity service which has real auth configured

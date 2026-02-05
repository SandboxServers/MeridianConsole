# Dhadgar.Mods Service

![Status: Alpha](https://img.shields.io/badge/Status-Alpha-yellow)

The Mods service is responsible for managing game modification (mod) registry, versioning, compatibility tracking, and distribution coordination within the Meridian Console platform. It serves as the central catalog for all mods that can be deployed to game servers managed by the control plane.

## Table of Contents

- [Overview](#overview)
- [Current Status](#current-status)
- [Technology Stack](#technology-stack)
- [Quick Start](#quick-start)
- [Architecture](#architecture)
- [Current Implementation](#current-implementation)
- [Planned Features](#planned-features)
- [Database Schema](#database-schema)
- [API Endpoints](#api-endpoints)
- [Integration Points](#integration-points)
- [Configuration](#configuration)
- [Observability](#observability)
- [Testing](#testing)
- [Development Guidelines](#development-guidelines)
- [Troubleshooting](#troubleshooting)

---

## Overview

### Purpose

The Mods service provides a centralized registry for game server modifications, enabling:

- **Mod Catalog Management**: Registration, metadata storage, and discovery of mods across all supported game types
- **Version Management**: Semantic versioning, release channels, and version history tracking
- **Compatibility Tracking**: Matrix of which mods work with which game versions, server configurations, and other mods
- **Dependency Resolution**: Tracking mod dependencies and conflicts for safe deployment
- **Distribution Coordination**: Working with the Files service to manage mod distribution to nodes

### Context within Meridian Console

Meridian Console is a multi-tenant SaaS control plane that orchestrates game servers on customer-owned hardware. The Mods service is one of 13 microservices that make up the platform:

```
                                    +-----------------+
                                    |     Gateway     |
                                    |   (port 5000)   |
                                    +--------+--------+
                                             |
                    +------------------------+------------------------+
                    |                        |                        |
             +------+------+          +------+------+          +------+------+
             |   Servers   |          |    Mods     |          |    Files    |
             | (port 5030) |          | (port 5080) |          | (port 5060) |
             +-------------+          +-------------+          +-------------+
                    |                        |                        |
                    +------------------------+------------------------+
                                             |
                                    +--------+--------+
                                    |      Nodes      |
                                    |   (port 5040)   |
                                    +--------+--------+
                                             |
                                    +--------+--------+
                                    |     Agents      |
                                    | (customer HW)   |
                                    +-----------------+
```

The Mods service interacts primarily with:
- **Servers Service**: To know which mods are installed on which servers
- **Files Service**: To coordinate mod file distribution to nodes
- **Nodes Service**: To understand node capabilities and storage
- **Tasks Service**: To orchestrate mod installation/update workflows

### Multi-Tenancy

Like all Meridian Console services, Mods operates in a multi-tenant environment:
- Each organization (tenant) has its own isolated view of the mod catalog
- System-wide mods can be shared across tenants (curated catalog)
- Tenant-specific custom mods are isolated to that organization

---

## Current Status

**Status: Alpha**

The Mods service has core functionality implemented including mod registry, semantic versioning with range parsing, dependency resolution, compatibility tracking, and download management.

### What Exists Today

| Component | Status | Description |
|-----------|--------|-------------|
| Project Structure | Complete | Standard ASP.NET Core Minimal API layout |
| Health Endpoints | Complete | `/healthz`, `/livez`, `/readyz` probes |
| Database Context | Scaffolded | `ModsDbContext` with placeholder `SampleEntity` |
| OpenTelemetry | Complete | Tracing, metrics, and logging instrumentation |
| Swagger/OpenAPI | Complete | API documentation at `/swagger` |
| Middleware | Complete | Correlation tracking, problem details, request logging |
| Test Project | Complete | `Dhadgar.Mods.Tests` with WebApplicationFactory setup |
| Gateway Routing | Complete | Route configured at `/api/v1/mods/{**catch-all}` |

### What is Planned (Not Yet Implemented)

| Feature | Priority | Description |
|---------|----------|-------------|
| Mod Entity & CRUD | High | Core mod management APIs |
| Version Management | High | Mod version tracking and release channels |
| Compatibility Matrix | High | Game version and mod compatibility tracking |
| Dependency Resolution | Medium | Mod dependency and conflict detection |
| Search & Discovery | Medium | Full-text search and filtering |
| Files Integration | Medium | Coordination with Files service for distribution |
| Messaging Consumers | Medium | MassTransit event handlers |
| Caching | Low | Redis caching for catalog queries |
| Audit Logging | Low | Track changes to mods and versions |

---

## Technology Stack

### Core Framework

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 10.0 | Runtime and SDK |
| ASP.NET Core | 10.0 | Web framework (Minimal APIs) |
| C# | 13.0 | Language (nullable enabled) |

### Data Layer

| Technology | Version | Purpose |
|------------|---------|---------|
| Entity Framework Core | 10.0.0 | ORM and database abstraction |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.0 | PostgreSQL provider |
| PostgreSQL | 16+ | Primary database |

### Messaging

| Technology | Version | Purpose |
|------------|---------|---------|
| MassTransit | 8.3.6 | Message bus abstraction |
| MassTransit.RabbitMQ | 8.3.6 | RabbitMQ transport |
| RabbitMQ | 3.x | Message broker |

### Observability

| Technology | Version | Purpose |
|------------|---------|---------|
| OpenTelemetry.Extensions.Hosting | 1.14.0 | Telemetry host integration |
| OpenTelemetry.Instrumentation.AspNetCore | 1.14.0 | HTTP request tracing |
| OpenTelemetry.Instrumentation.Http | 1.14.0 | Outbound HTTP tracing |
| OpenTelemetry.Instrumentation.Runtime | 1.14.0 | Runtime metrics |
| OpenTelemetry.Instrumentation.Process | 1.14.0 | Process metrics |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.14.0 | OTLP export |

### API Documentation

| Technology | Version | Purpose |
|------------|---------|---------|
| Swashbuckle.AspNetCore | 10.1.0 | Swagger/OpenAPI generation |
| Microsoft.AspNetCore.OpenApi | 10.0.0 | OpenAPI support |

### Shared Libraries

| Library | Purpose |
|---------|---------|
| Dhadgar.Contracts | DTOs, message contracts, API models |
| Dhadgar.Shared | Utilities, primitives, helpers |
| Dhadgar.Messaging | MassTransit configuration and conventions |
| Dhadgar.ServiceDefaults | Common middleware, health checks, observability |

---

## Quick Start

### Prerequisites

Before running the Mods service, ensure you have:

1. **.NET SDK 10.0.100** - Pinned in `global.json`
   ```bash
   dotnet --version
   # Should output: 10.0.100
   ```

2. **Docker** - For local infrastructure (PostgreSQL, RabbitMQ, etc.)
   ```bash
   docker --version
   docker compose version
   ```

3. **Local Infrastructure Running** - Start the development stack:
   ```bash
   # From repository root
   docker compose -f deploy/compose/docker-compose.dev.yml up -d
   ```

   This starts:
   - PostgreSQL on port 5432
   - RabbitMQ on ports 5672 (AMQP) and 15672 (management UI)
   - Redis on port 6379
   - Grafana on port 3000
   - Prometheus on port 9090
   - Loki on port 3100
   - OTLP Collector on ports 4317/4318

### Running the Service

#### Option 1: Direct Run

```bash
# From repository root
dotnet run --project src/Dhadgar.Mods

# Service will start on http://localhost:5080
```

#### Option 2: With Hot Reload (Development)

```bash
# From repository root
dotnet watch --project src/Dhadgar.Mods

# Changes to code will automatically rebuild and restart
```

#### Option 3: Via Gateway

```bash
# Start the Gateway (recommended for full platform testing)
dotnet run --project src/Dhadgar.Gateway

# Access Mods via Gateway at http://localhost:5000/api/v1/mods/
```

### Verifying the Service is Running

1. **Health Check**:
   ```bash
   curl http://localhost:5080/healthz
   # Expected: {"service":"Dhadgar.Mods","status":"ok","timestamp":"..."}
   ```

2. **Hello Endpoint**:
   ```bash
   curl http://localhost:5080/hello
   # Expected: Hello from Dhadgar.Mods
   ```

3. **Service Info**:
   ```bash
   curl http://localhost:5080/
   # Expected: {"service":"Dhadgar.Mods","message":"Hello from Dhadgar.Mods"}
   ```

4. **Swagger UI** (Development mode):
   ```
   http://localhost:5080/swagger
   ```

### Building and Testing

```bash
# Build the service
dotnet build src/Dhadgar.Mods

# Run all tests
dotnet test tests/Dhadgar.Mods.Tests

# Run specific test
dotnet test tests/Dhadgar.Mods.Tests --filter "FullyQualifiedName~HelloWorldTests"

# Run with verbose output
dotnet test tests/Dhadgar.Mods.Tests --verbosity detailed
```

---

## Architecture

### Project Structure

```
src/Dhadgar.Mods/
├── Program.cs                      # Application entry point, DI configuration
├── Hello.cs                        # Hello message constant for health checks
├── Dhadgar.Mods.csproj            # Project file with dependencies
├── appsettings.json               # Default configuration
├── appsettings.Development.json   # Development overrides (if exists)
├── Data/
│   ├── ModsDbContext.cs           # EF Core database context
│   ├── ModsDbContextFactory.cs    # Design-time factory for migrations
│   └── Migrations/                # EF Core migrations (empty - no migrations yet)
├── Entities/                      # (Planned) Domain entities
├── Services/                      # (Planned) Business logic services
├── Endpoints/                     # (Planned) Minimal API endpoint definitions
└── Consumers/                     # (Planned) MassTransit message consumers
```

### Dependency Graph

```
Dhadgar.Mods
├── Dhadgar.Contracts          # DTOs and message contracts
├── Dhadgar.Shared            # Utilities and primitives
├── Dhadgar.Messaging         # MassTransit configuration
└── Dhadgar.ServiceDefaults   # Common middleware and setup
    ├── CorrelationMiddleware     # Request/trace ID tracking
    ├── ProblemDetailsMiddleware  # RFC 7807 error responses
    └── RequestLoggingMiddleware  # HTTP request/response logging
```

### Middleware Pipeline

The service uses the following middleware (in order):

1. **CorrelationMiddleware**: Ensures every request has `X-Correlation-Id`, `X-Request-Id`, and trace IDs for distributed tracing
2. **ProblemDetailsMiddleware**: Transforms exceptions into RFC 7807 Problem Details JSON responses
3. **RequestLoggingMiddleware**: Logs HTTP requests and responses with correlation context

### Design Principles

1. **Microservices Isolation**: The Mods service has NO direct project references to other services. All inter-service communication is via:
   - HTTP APIs (typed clients)
   - Message bus (MassTransit/RabbitMQ)

2. **Database per Service**: Mods owns its schema exclusively. Other services cannot directly query the Mods database.

3. **Contract-Based Communication**: All DTOs and message contracts are defined in `Dhadgar.Contracts`, shared by all services.

4. **Configuration-Driven**: All settings are externalized to `appsettings.json`, environment variables, or user secrets.

---

## Current Implementation

### Program.cs Overview

The current implementation sets up:

```csharp
// Service defaults (health checks, etc.)
builder.Services.AddDhadgarServiceDefaults();

// Swagger/OpenAPI
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Mods API",
    description: "Mod registry and versioning for Meridian Console");

// PostgreSQL with EF Core
builder.Services.AddDbContext<ModsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// OpenTelemetry (tracing, metrics, logging)
// ... full instrumentation configured ...
```

### Current Endpoints

| Method | Path | Description | Tags |
|--------|------|-------------|------|
| GET | `/` | Service info (name and hello message) | Health |
| GET | `/hello` | Returns "Hello from Dhadgar.Mods" | Health |
| GET | `/healthz` | Full health check (all registered checks) | Health |
| GET | `/livez` | Liveness probe (is the process running?) | Health |
| GET | `/readyz` | Readiness probe (is the service ready for traffic?) | Health |

### Database Context

The current `ModsDbContext` contains only a placeholder entity:

```csharp
public sealed class ModsDbContext : DbContext
{
    public DbSet<SampleEntity> Sample => Set<SampleEntity>();

    // TODO: Replace with real entities (Mod, ModVersion, etc.)
}

public sealed class SampleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "hello";
}
```

### Auto-Migration (Development Only)

In Development mode, the service automatically applies pending migrations on startup:

```csharp
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ModsDbContext>();
    db.Database.Migrate();
}
```

This behavior is wrapped in a try-catch to keep startup resilient for first-run scenarios where the database might not exist.

---

## Planned Features

### 1. Mod Registry and Catalog

The core feature set for managing mods:

#### Mod Entity Structure (Planned)

```csharp
public class Mod
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }              // Multi-tenant isolation
    public string Name { get; set; }                 // e.g., "BetterSpawns"
    public string Slug { get; set; }                 // URL-friendly: "better-spawns"
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? Website { get; set; }
    public string? SourceRepository { get; set; }
    public string GameType { get; set; }             // e.g., "minecraft", "valheim"
    public ModVisibility Visibility { get; set; }    // Public, Private, Unlisted
    public bool IsVerified { get; set; }             // Curated/verified by platform
    public bool IsDeprecated { get; set; }
    public string? DeprecationMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? IconUrl { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<string> Categories { get; set; } = [];

    // Navigation
    public ICollection<ModVersion> Versions { get; set; }
}
```

#### Planned CRUD Operations

| Operation | Endpoint | Description |
|-----------|----------|-------------|
| List Mods | `GET /mods` | Paginated list with filtering |
| Get Mod | `GET /mods/{id}` | Single mod with versions |
| Create Mod | `POST /mods` | Register new mod |
| Update Mod | `PUT /mods/{id}` | Update mod metadata |
| Delete Mod | `DELETE /mods/{id}` | Soft delete (deprecate) |
| Search | `GET /mods/search?q={query}` | Full-text search |

### 2. Version Management

Track and manage mod versions:

#### ModVersion Entity (Planned)

```csharp
public class ModVersion
{
    public Guid Id { get; set; }
    public Guid ModId { get; set; }
    public string Version { get; set; }              // Semantic version: "1.2.3"
    public string? ReleaseNotes { get; set; }
    public string? Changelog { get; set; }
    public ReleaseChannel Channel { get; set; }      // Stable, Beta, Alpha, Dev
    public DateTime ReleasedAt { get; set; }
    public bool IsLatest { get; set; }               // Is this the latest stable?
    public bool IsPrerelease { get; set; }
    public long DownloadCount { get; set; }
    public string? FileId { get; set; }              // Reference to Files service
    public long FileSizeBytes { get; set; }
    public string? Checksum { get; set; }            // SHA256 hash

    // Navigation
    public Mod Mod { get; set; }
    public ICollection<ModVersionCompatibility> Compatibilities { get; set; }
    public ICollection<ModDependency> Dependencies { get; set; }
}

public enum ReleaseChannel
{
    Stable,
    Beta,
    Alpha,
    Development
}
```

#### Version Operations (Planned)

| Operation | Endpoint | Description |
|-----------|----------|-------------|
| List Versions | `GET /mods/{modId}/versions` | All versions of a mod |
| Get Version | `GET /mods/{modId}/versions/{version}` | Specific version details |
| Create Version | `POST /mods/{modId}/versions` | Publish new version |
| Update Version | `PUT /mods/{modId}/versions/{version}` | Update version metadata |
| Set Latest | `POST /mods/{modId}/versions/{version}/promote` | Promote to latest |

### 3. Compatibility Tracking

Track which mods work with which game versions:

#### ModVersionCompatibility Entity (Planned)

```csharp
public class ModVersionCompatibility
{
    public Guid Id { get; set; }
    public Guid ModVersionId { get; set; }
    public string GameType { get; set; }             // e.g., "minecraft"
    public string GameVersion { get; set; }          // e.g., "1.20.4"
    public string? MinGameVersion { get; set; }      // Minimum supported
    public string? MaxGameVersion { get; set; }      // Maximum supported
    public CompatibilityStatus Status { get; set; } // Verified, Compatible, Unknown, Incompatible
    public string? Notes { get; set; }

    // Navigation
    public ModVersion ModVersion { get; set; }
}

public enum CompatibilityStatus
{
    Verified,      // Tested and confirmed working
    Compatible,    // Reported working by users
    Unknown,       // Not yet tested
    Incompatible,  // Confirmed not working
    Partial        // Works with limitations
}
```

#### Compatibility Matrix API (Planned)

| Operation | Endpoint | Description |
|-----------|----------|-------------|
| Get Compatibility | `GET /mods/{modId}/compatibility` | Full compatibility matrix |
| Check Compatibility | `GET /mods/compatible?game={type}&version={ver}` | Find compatible mods |
| Report Compatibility | `POST /mods/{modId}/versions/{ver}/compatibility` | Add compatibility info |

### 4. Mod Dependencies

Track dependencies between mods:

#### ModDependency Entity (Planned)

```csharp
public class ModDependency
{
    public Guid Id { get; set; }
    public Guid ModVersionId { get; set; }           // The mod version with the dependency
    public Guid DependsOnModId { get; set; }         // The required mod
    public string? VersionConstraint { get; set; }   // e.g., ">=1.0.0 <2.0.0"
    public DependencyType Type { get; set; }
    public bool IsOptional { get; set; }

    // Navigation
    public ModVersion ModVersion { get; set; }
    public Mod DependsOnMod { get; set; }
}

public enum DependencyType
{
    Required,       // Must be installed
    Recommended,    // Should be installed
    Optional,       // Enhances functionality
    Incompatible,   // Cannot be installed together (conflict)
    Replaces        // This mod supersedes the other
}
```

#### Dependency Resolution API (Planned)

| Operation | Endpoint | Description |
|-----------|----------|-------------|
| Get Dependencies | `GET /mods/{modId}/versions/{ver}/dependencies` | List dependencies |
| Resolve Dependencies | `POST /mods/resolve` | Resolve dependency tree for mod list |
| Check Conflicts | `POST /mods/conflicts` | Check for conflicts in mod list |

### 5. Download Tracking

Track mod downloads for analytics and rate limiting:

#### ModDownload Entity (Planned)

```csharp
public class ModDownload
{
    public Guid Id { get; set; }
    public Guid ModVersionId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? ServerId { get; set; }              // Which server the mod was installed on
    public DateTime DownloadedAt { get; set; }
    public string? IpAddress { get; set; }           // For rate limiting (anonymized)
    public string? UserAgent { get; set; }
    public DownloadSource Source { get; set; }
}

public enum DownloadSource
{
    Api,            // Direct API download
    Panel,          // Downloaded via Panel UI
    Agent,          // Downloaded by agent
    Cli             // Downloaded via CLI tool
}
```

### 6. Files Service Integration

Coordinate with the Files service for actual mod file storage and distribution:

#### Integration Points (Planned)

1. **Upload Flow**:
   - User uploads mod file via Panel or API
   - Mods service validates metadata, creates mod/version records
   - Mods service requests Files service to store the file
   - Files service returns file ID and checksum
   - Mods service stores reference in `ModVersion.FileId`

2. **Download Flow**:
   - User/Agent requests mod download
   - Mods service validates permissions
   - Mods service requests signed URL from Files service
   - Files service returns time-limited download URL
   - Client downloads directly from Files service

3. **Distribution Flow**:
   - Server requests mod installation
   - Tasks service orchestrates the workflow
   - Mods service provides file references
   - Files service distributes to Node
   - Agent installs on server

#### Planned HTTP Client

```csharp
public interface IFilesServiceClient
{
    Task<FileUploadResult> RequestUploadAsync(FileUploadRequest request);
    Task<string> GetDownloadUrlAsync(Guid fileId, TimeSpan expiry);
    Task<FileMetadata> GetFileMetadataAsync(Guid fileId);
    Task DeleteFileAsync(Guid fileId);
}
```

### 7. Messaging Integration

MassTransit consumers for event-driven workflows:

#### Planned Consumers

| Consumer | Message | Purpose |
|----------|---------|---------|
| `ModInstalledConsumer` | `ModInstalledEvent` | Track installation for analytics |
| `ServerDeletedConsumer` | `ServerDeletedEvent` | Clean up mod associations |
| `FileUploadedConsumer` | `FileUploadedEvent` | Link uploaded file to mod version |
| `ModVersionRequestedConsumer` | `ModVersionRequestedCommand` | Handle mod distribution requests |

#### Planned Events Published

| Event | Trigger | Subscribers |
|-------|---------|-------------|
| `ModCreatedEvent` | New mod registered | Notifications, Search |
| `ModVersionPublishedEvent` | New version released | Notifications, Servers |
| `ModDeprecatedEvent` | Mod marked deprecated | Notifications, Servers |

---

## Database Schema

### Current Schema (Placeholder)

The current schema contains only a placeholder entity for scaffolding purposes:

```sql
-- Current placeholder (will be replaced)
CREATE TABLE "Sample" (
    "Id" uuid PRIMARY KEY,
    "Name" varchar(200) NOT NULL
);
```

### Planned Schema

The planned database schema for the full implementation:

```sql
-- Core mod entity
CREATE TABLE "Mods" (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" uuid NOT NULL,
    "Name" varchar(100) NOT NULL,
    "Slug" varchar(100) NOT NULL,
    "Description" text,
    "Author" varchar(100),
    "Website" varchar(500),
    "SourceRepository" varchar(500),
    "GameType" varchar(50) NOT NULL,
    "Visibility" int NOT NULL DEFAULT 0,
    "IsVerified" boolean NOT NULL DEFAULT false,
    "IsDeprecated" boolean NOT NULL DEFAULT false,
    "DeprecationMessage" text,
    "CreatedAt" timestamptz NOT NULL DEFAULT now(),
    "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
    "IconUrl" varchar(500),
    "Tags" text[] NOT NULL DEFAULT '{}',
    "Categories" text[] NOT NULL DEFAULT '{}',

    CONSTRAINT "UQ_Mods_TenantId_Slug" UNIQUE ("TenantId", "Slug")
);

CREATE INDEX "IX_Mods_TenantId" ON "Mods" ("TenantId");
CREATE INDEX "IX_Mods_GameType" ON "Mods" ("GameType");
CREATE INDEX "IX_Mods_Tags" ON "Mods" USING GIN ("Tags");

-- Mod versions
CREATE TABLE "ModVersions" (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "ModId" uuid NOT NULL REFERENCES "Mods"("Id") ON DELETE CASCADE,
    "Version" varchar(50) NOT NULL,
    "ReleaseNotes" text,
    "Changelog" text,
    "Channel" int NOT NULL DEFAULT 0,
    "ReleasedAt" timestamptz NOT NULL DEFAULT now(),
    "IsLatest" boolean NOT NULL DEFAULT false,
    "IsPrerelease" boolean NOT NULL DEFAULT false,
    "DownloadCount" bigint NOT NULL DEFAULT 0,
    "FileId" uuid,
    "FileSizeBytes" bigint NOT NULL DEFAULT 0,
    "Checksum" varchar(64),

    CONSTRAINT "UQ_ModVersions_ModId_Version" UNIQUE ("ModId", "Version")
);

CREATE INDEX "IX_ModVersions_ModId" ON "ModVersions" ("ModId");
CREATE INDEX "IX_ModVersions_ReleasedAt" ON "ModVersions" ("ReleasedAt" DESC);

-- Compatibility matrix
CREATE TABLE "ModVersionCompatibilities" (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "ModVersionId" uuid NOT NULL REFERENCES "ModVersions"("Id") ON DELETE CASCADE,
    "GameType" varchar(50) NOT NULL,
    "GameVersion" varchar(50) NOT NULL,
    "MinGameVersion" varchar(50),
    "MaxGameVersion" varchar(50),
    "Status" int NOT NULL DEFAULT 2,
    "Notes" text,

    CONSTRAINT "UQ_Compatibility" UNIQUE ("ModVersionId", "GameType", "GameVersion")
);

CREATE INDEX "IX_ModVersionCompatibilities_GameVersion" ON "ModVersionCompatibilities" ("GameType", "GameVersion");

-- Dependencies
CREATE TABLE "ModDependencies" (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "ModVersionId" uuid NOT NULL REFERENCES "ModVersions"("Id") ON DELETE CASCADE,
    "DependsOnModId" uuid NOT NULL REFERENCES "Mods"("Id") ON DELETE CASCADE,
    "VersionConstraint" varchar(100),
    "Type" int NOT NULL DEFAULT 0,
    "IsOptional" boolean NOT NULL DEFAULT false,

    CONSTRAINT "UQ_ModDependencies" UNIQUE ("ModVersionId", "DependsOnModId")
);

CREATE INDEX "IX_ModDependencies_DependsOnModId" ON "ModDependencies" ("DependsOnModId");

-- Download tracking
CREATE TABLE "ModDownloads" (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "ModVersionId" uuid NOT NULL REFERENCES "ModVersions"("Id") ON DELETE CASCADE,
    "TenantId" uuid NOT NULL,
    "UserId" uuid,
    "ServerId" uuid,
    "DownloadedAt" timestamptz NOT NULL DEFAULT now(),
    "IpAddressHash" varchar(64),
    "UserAgent" varchar(500),
    "Source" int NOT NULL DEFAULT 0
);

CREATE INDEX "IX_ModDownloads_ModVersionId" ON "ModDownloads" ("ModVersionId");
CREATE INDEX "IX_ModDownloads_DownloadedAt" ON "ModDownloads" ("DownloadedAt" DESC);
CREATE INDEX "IX_ModDownloads_TenantId" ON "ModDownloads" ("TenantId");
```

### Creating Migrations

When ready to implement the schema:

```bash
# Add migration for mod entities
dotnet ef migrations add AddModEntities \
  --project src/Dhadgar.Mods \
  --startup-project src/Dhadgar.Mods \
  --output-dir Data/Migrations

# Apply migrations
dotnet ef database update \
  --project src/Dhadgar.Mods \
  --startup-project src/Dhadgar.Mods

# Remove last migration (if not applied to database)
dotnet ef migrations remove \
  --project src/Dhadgar.Mods \
  --startup-project src/Dhadgar.Mods
```

---

## API Endpoints

### Current Endpoints

These endpoints are currently implemented:

| Method | Route | Description | Response |
|--------|-------|-------------|----------|
| `GET` | `/` | Service info | `{"service":"Dhadgar.Mods","message":"..."}` |
| `GET` | `/hello` | Hello message | `Hello from Dhadgar.Mods` |
| `GET` | `/healthz` | Full health check | Health report JSON |
| `GET` | `/livez` | Liveness probe | Health report JSON |
| `GET` | `/readyz` | Readiness probe | Health report JSON |

### Gateway Route

The Mods service is accessible via the API Gateway:

- **Gateway URL**: `http://localhost:5000/api/v1/mods/{path}`
- **Direct URL**: `http://localhost:5080/{path}`
- **Authorization**: `TenantScoped` policy (requires authentication)
- **Rate Limiting**: `PerTenant` policy

Gateway configuration (from `Dhadgar.Gateway/appsettings.json`):

```json
{
  "mods-route": {
    "ClusterId": "mods",
    "Order": 20,
    "Match": { "Path": "/api/v1/mods/{**catch-all}" },
    "AuthorizationPolicy": "TenantScoped",
    "RateLimiterPolicy": "PerTenant",
    "Transforms": [
      { "PathRemovePrefix": "/api/v1/mods" }
    ]
  }
}
```

### Planned Endpoints

The following endpoints are planned for implementation:

#### Mod Management

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/mods` | List mods (paginated, filterable) |
| `GET` | `/mods/{id}` | Get mod by ID with latest version |
| `GET` | `/mods/by-slug/{slug}` | Get mod by URL slug |
| `POST` | `/mods` | Create new mod |
| `PUT` | `/mods/{id}` | Update mod metadata |
| `DELETE` | `/mods/{id}` | Deprecate/delete mod |
| `GET` | `/mods/search` | Full-text search |

#### Version Management

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/mods/{modId}/versions` | List all versions |
| `GET` | `/mods/{modId}/versions/{version}` | Get specific version |
| `GET` | `/mods/{modId}/versions/latest` | Get latest stable version |
| `POST` | `/mods/{modId}/versions` | Publish new version |
| `PUT` | `/mods/{modId}/versions/{version}` | Update version metadata |
| `POST` | `/mods/{modId}/versions/{version}/promote` | Promote to latest |
| `DELETE` | `/mods/{modId}/versions/{version}` | Remove version |

#### Compatibility

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/mods/{modId}/compatibility` | Get compatibility matrix |
| `GET` | `/mods/compatible` | Find mods compatible with game version |
| `POST` | `/mods/{modId}/versions/{ver}/compatibility` | Add compatibility record |

#### Dependencies

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/mods/{modId}/versions/{ver}/dependencies` | Get dependencies |
| `POST` | `/mods/resolve` | Resolve dependency tree |
| `POST` | `/mods/conflicts` | Check for conflicts |

#### Downloads

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/mods/{modId}/versions/{ver}/download` | Get download URL |
| `GET` | `/mods/{modId}/stats` | Download statistics |

---

## Integration Points

### Files Service Integration

The Mods service coordinates with the Files service for actual file storage:

| Operation | Mods Service | Files Service |
|-----------|--------------|---------------|
| Upload | Receives metadata, validates | Stores file, returns ID |
| Download | Validates permissions | Provides signed URL |
| Delete | Removes mod record | Deletes file |

**Planned HTTP Client Configuration**:

```json
{
  "Services": {
    "Files": {
      "BaseUrl": "http://localhost:5060",
      "Timeout": "00:05:00"
    }
  }
}
```

### Servers Service Integration

The Mods service provides mod information to the Servers service:

| Operation | Flow |
|-----------|------|
| Mod Installation | Servers requests mod -> Mods validates -> Files distributes |
| Version Update | Servers checks updates -> Mods provides new version info |
| Compatibility Check | Servers requests check -> Mods validates against matrix |

### Tasks Service Integration

The Tasks service orchestrates mod-related workflows:

| Workflow | Tasks Involvement |
|----------|-------------------|
| Mod Installation | Creates task, tracks progress, handles retry |
| Bulk Update | Coordinates multiple server updates |
| Rollback | Orchestrates version rollback |

### Nodes Service Integration

Indirect integration via Tasks and Agents:

| Operation | Flow |
|-----------|------|
| Distribution | Mods -> Files -> Node storage |
| Capacity Check | Mods queries Node for available space |

### Message Bus Events

| Event Published | Subscribers |
|-----------------|-------------|
| `ModCreatedEvent` | Search indexer, Notifications |
| `ModVersionPublishedEvent` | Notifications, Auto-update service |
| `ModDeprecatedEvent` | Notifications, Servers |

| Event Consumed | Publisher |
|----------------|-----------|
| `ServerDeletedEvent` | Servers service |
| `FileUploadedEvent` | Files service |
| `TenantDeletedEvent` | Identity service |

---

## Configuration

### Configuration Files

#### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=dhadgar_platform;Username=dhadgar;Password=dhadgar",
    "RabbitMqHost": "localhost"
  },
  "RabbitMq": {
    "Username": "dhadgar",
    "Password": "dhadgar"
  }
}
```

### Configuration Options

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `ConnectionStrings:Postgres` | string | (local) | PostgreSQL connection string |
| `ConnectionStrings:RabbitMqHost` | string | localhost | RabbitMQ host |
| `RabbitMq:Username` | string | dhadgar | RabbitMQ username |
| `RabbitMq:Password` | string | dhadgar | RabbitMQ password |
| `Logging:LogLevel:Default` | string | Information | Default log level |
| `OpenTelemetry:OtlpEndpoint` | string | (empty) | OTLP exporter endpoint |

### Environment Variables

All configuration can be overridden via environment variables:

```bash
# Connection strings
ConnectionStrings__Postgres="Host=prod-db;..."
ConnectionStrings__RabbitMqHost="prod-rabbitmq"

# RabbitMQ credentials
RabbitMq__Username="prod-user"
RabbitMq__Password="prod-secret"

# OpenTelemetry
OpenTelemetry__OtlpEndpoint="http://otel-collector:4317"

# Logging
Logging__LogLevel__Default="Warning"
```

### User Secrets (Development)

For local development, use .NET user secrets:

```bash
# Initialize user secrets
dotnet user-secrets init --project src/Dhadgar.Mods

# Set connection string
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;..." --project src/Dhadgar.Mods

# Set RabbitMQ credentials
dotnet user-secrets set "RabbitMq:Password" "my-dev-password" --project src/Dhadgar.Mods

# Enable OpenTelemetry export
dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317" --project src/Dhadgar.Mods

# List all secrets
dotnet user-secrets list --project src/Dhadgar.Mods
```

### Planned Configuration (Future)

```json
{
  "Mods": {
    "MaxModSize": 524288000,
    "AllowedFileExtensions": [".zip", ".jar", ".pak"],
    "MaxVersionsPerMod": 100,
    "EnableAutoDeprecation": true,
    "DeprecationAfterDays": 365
  },
  "Services": {
    "Files": {
      "BaseUrl": "http://localhost:5060",
      "Timeout": "00:05:00"
    }
  },
  "Cache": {
    "Enabled": true,
    "DefaultTtlMinutes": 5,
    "CatalogTtlMinutes": 15
  }
}
```

---

## Observability

### OpenTelemetry Integration

The Mods service is fully instrumented with OpenTelemetry:

#### Tracing

- ASP.NET Core request tracing
- HTTP client outbound tracing
- EF Core database query tracing (when added)
- MassTransit message tracing (when added)

#### Metrics

- ASP.NET Core metrics (requests, response times)
- HTTP client metrics
- Runtime metrics (GC, thread pool)
- Process metrics (CPU, memory)
- Custom business metrics (planned):
  - `mods_total` - Total mods by tenant
  - `mod_versions_total` - Total versions
  - `mod_downloads_total` - Download count

#### Logging

- Structured logging with correlation IDs
- Request/response logging
- Exception logging with stack traces

### Enabling OTLP Export

To export telemetry to the local observability stack:

```bash
# Via user secrets
dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317" --project src/Dhadgar.Mods

# Or via environment variable
export OpenTelemetry__OtlpEndpoint="http://localhost:4317"
```

### Viewing Telemetry

With the local Docker Compose stack running:

1. **Grafana**: http://localhost:3000 (admin/admin)
   - Pre-configured datasources for Prometheus and Loki
   - Create dashboards for metrics visualization

2. **Prometheus**: http://localhost:9090
   - Direct metric queries
   - Target health monitoring

3. **Loki**: via Grafana
   - Log aggregation and search
   - Correlation ID filtering

### Health Check Responses

```json
// GET /healthz
{
  "service": "Dhadgar.Mods",
  "status": "ok",
  "timestamp": "2025-01-22T12:00:00Z",
  "checks": {
    "self": {
      "status": "Healthy",
      "duration_ms": 0.5
    }
  }
}

// Future: with database check
{
  "service": "Dhadgar.Mods",
  "status": "ok",
  "timestamp": "2025-01-22T12:00:00Z",
  "checks": {
    "self": {
      "status": "Healthy",
      "duration_ms": 0.5
    },
    "database": {
      "status": "Healthy",
      "duration_ms": 2.3,
      "data": {
        "connection": "open"
      }
    }
  }
}
```

---

## Testing

### Test Project Structure

```
tests/Dhadgar.Mods.Tests/
├── Dhadgar.Mods.Tests.csproj      # Test project file
├── ModsWebApplicationFactory.cs    # Custom WebApplicationFactory
├── HelloWorldTests.cs              # Basic smoke tests
└── SwaggerTests.cs                 # API documentation tests
```

### WebApplicationFactory

The test project includes a custom `WebApplicationFactory` that:
- Sets environment to "Testing"
- Replaces PostgreSQL with in-memory database
- Disables auto-migration

```csharp
public class ModsWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace PostgreSQL with in-memory database
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ModsDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ModsDbContext>(options =>
            {
                options.UseInMemoryDatabase("ModsTestDb");
            });
        });
    }
}
```

### Running Tests

```bash
# Run all Mods tests
dotnet test tests/Dhadgar.Mods.Tests

# Run with detailed output
dotnet test tests/Dhadgar.Mods.Tests --verbosity detailed

# Run specific test
dotnet test tests/Dhadgar.Mods.Tests --filter "FullyQualifiedName~HelloWorldTests"

# Run with coverage
dotnet test tests/Dhadgar.Mods.Tests --collect:"XPlat Code Coverage"
```

### Current Tests

#### HelloWorldTests

```csharp
public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Mods", Hello.Message);
    }
}
```

#### SwaggerTests

```csharp
public class SwaggerTests : IClassFixture<ModsWebApplicationFactory>
{
    [Fact]
    public async Task SwaggerEndpoint_ReturnsValidOpenApiSpec()
    {
        await SwaggerTestHelper.VerifySwaggerEndpointAsync(
            _factory,
            expectedTitle: "Dhadgar Mods API");
    }

    [Fact]
    public async Task SwaggerUi_ReturnsHtml()
    {
        await SwaggerTestHelper.VerifySwaggerUiAsync(_factory);
    }

    [Fact]
    public async Task SwaggerEndpoint_DocumentsHealthEndpoints()
    {
        await SwaggerTestHelper.VerifyHealthEndpointsDocumentedAsync(_factory);
    }
}
```

### Planned Tests

| Test Category | Description |
|---------------|-------------|
| Unit Tests | Service logic, validation, dependency resolution |
| Integration Tests | API endpoints with in-memory database |
| Repository Tests | EF Core queries with in-memory database |
| Contract Tests | DTO serialization/deserialization |
| Consumer Tests | MassTransit message handling |

---

## Development Guidelines

### Code Style

- Follow C# coding conventions
- Use nullable reference types (`#nullable enable`)
- Prefer records for DTOs
- Use Minimal APIs pattern for endpoints

### Adding New Endpoints

1. Define DTOs in `Dhadgar.Contracts` project
2. Create endpoint methods in `Endpoints/` folder
3. Register in `Program.cs` using `app.MapXxx()`
4. Add OpenAPI metadata with `.WithTags()` and `.WithName()`
5. Write tests in `Dhadgar.Mods.Tests`

### Adding New Entities

1. Define entity class in `Data/Entities/`
2. Add `DbSet<T>` to `ModsDbContext`
3. Configure in `OnModelCreating` if needed
4. Create migration: `dotnet ef migrations add ...`
5. Apply migration: `dotnet ef database update ...`

### Inter-Service Communication

- **Never** add `ProjectReference` to other services
- Use HTTP clients for synchronous calls
- Use MassTransit for asynchronous messaging
- Define contracts in `Dhadgar.Contracts`

### Error Handling

- Throw exceptions for errors; middleware converts to Problem Details
- Use standard HTTP status codes
- Include correlation IDs in error responses

---

## Troubleshooting

### Common Issues

#### Database Connection Failed

```
Npgsql.NpgsqlException: Failed to connect to host localhost
```

**Solution**: Ensure PostgreSQL is running:
```bash
docker compose -f deploy/compose/docker-compose.dev.yml up -d postgres
```

#### Migration Failed on Startup

```
DB migration failed (dev)
```

**Solution**: This is logged as a warning and is expected on first run. The database will be created when migrations are applied manually or on subsequent runs.

#### Port Already in Use

```
System.IO.IOException: Failed to bind to address http://localhost:5080
```

**Solution**: Another process is using port 5080. Find and stop it:
```bash
# Linux/macOS
lsof -i :5080
kill <PID>

# Windows
netstat -ano | findstr :5080
taskkill /PID <PID> /F
```

#### RabbitMQ Connection Failed

```
RabbitMQ.Client.Exceptions.BrokerUnreachableException
```

**Solution**: Ensure RabbitMQ is running:
```bash
docker compose -f deploy/compose/docker-compose.dev.yml up -d rabbitmq
```

### Checking Service Health

```bash
# Direct health check
curl http://localhost:5080/healthz | jq

# Via Gateway
curl http://localhost:5000/api/v1/mods/healthz | jq

# Liveness probe
curl http://localhost:5080/livez | jq

# Readiness probe
curl http://localhost:5080/readyz | jq
```

### Logs

```bash
# Run with debug logging
dotnet run --project src/Dhadgar.Mods -- --Logging:LogLevel:Default=Debug

# View logs in Grafana/Loki
# 1. Open http://localhost:3000
# 2. Go to Explore
# 3. Select Loki datasource
# 4. Query: {service="Dhadgar.Mods"}
```

---

## Related Documentation

- [Main Project README](/README.md)
- [Project CLAUDE.md](/CLAUDE.md)
- [Docker Compose Setup](/deploy/compose/README.md)
- [Files Service](/src/Dhadgar.Files/README.md)
- [Servers Service](/src/Dhadgar.Servers/README.md)
- [Gateway Configuration](/src/Dhadgar.Gateway/appsettings.json)

---

## Contributing

When contributing to the Mods service:

1. Create a feature branch from `main`
2. Follow the coding guidelines above
3. Write tests for new functionality
4. Ensure all tests pass: `dotnet test`
5. Use conventional commits: `feat:`, `fix:`, `docs:`, etc.
6. Submit a pull request

For complex changes, consider using the specialized agents:
- `database-schema-architect` for schema changes
- `rest-api-engineer` for API design
- `dotnet-test-engineer` for testing strategies

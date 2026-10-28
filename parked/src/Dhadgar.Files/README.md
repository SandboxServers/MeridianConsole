# Dhadgar.Files

![Status: Stub](https://img.shields.io/badge/Status-Stub-red)

File metadata storage and transfer orchestration service for Meridian Console. This service acts as the central coordinator for all file-related operations across the platform, managing metadata, coordinating transfers between the control plane and customer-hosted agents, and ensuring file integrity throughout the system.

## Table of Contents

- [Overview](#overview)
- [Current Status](#current-status)
- [Tech Stack](#tech-stack)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [API Endpoints](#api-endpoints)
- [Database Schema](#database-schema)
- [Architecture](#architecture)
- [Integration Points](#integration-points)
- [Planned Features](#planned-features)
- [Testing](#testing)
- [Observability](#observability)
- [Related Documentation](#related-documentation)

## Overview

The Files service is a critical component of Meridian Console's control plane architecture. Unlike traditional file hosting services, Dhadgar.Files does **not** store file content directly. Instead, it:

1. **Manages file metadata** - Tracks file information, checksums, versions, and relationships
2. **Orchestrates transfers** - Coordinates file movement between storage providers and customer-hosted agents
3. **Ensures integrity** - Verifies file checksums and manages content verification
4. **Abstracts storage** - Provides a unified API regardless of underlying storage provider (Azure Blob, S3, local)

### Why File Orchestration Matters

In Meridian Console's architecture:
- **Control Plane** (cloud-hosted) manages the "what" and "when" of file operations
- **Agents** (customer-hosted) execute the actual file operations on customer hardware
- **Storage Providers** (configurable) hold the actual file content

This separation ensures:
- Customer data sovereignty (files can stay on customer infrastructure)
- Flexible storage options (cloud, on-prem, hybrid)
- Efficient bandwidth usage (agents download directly from storage, not through control plane)
- Audit trail for all file operations

### Core Responsibilities

| Responsibility | Description |
|---------------|-------------|
| **Metadata Storage** | Track file names, sizes, checksums, MIME types, and custom metadata |
| **Transfer Orchestration** | Generate signed URLs, coordinate multi-part uploads, track progress |
| **Version Management** | Maintain file history, support rollbacks, manage retention |
| **Integrity Verification** | Compute and validate checksums (SHA-256, MD5) |
| **Access Control** | Enforce tenant isolation, verify permissions before generating URLs |
| **Audit Logging** | Record all file operations for compliance and debugging |

## Current Status

**Status: STUB** - Basic scaffolding exists, core functionality is planned.

### What Exists Today

The service currently has:

- **ASP.NET Core Minimal API** with proper project structure
- **Entity Framework Core** configured with PostgreSQL
- **Health check endpoints** (`/healthz`, `/livez`, `/readyz`)
- **Swagger/OpenAPI** documentation (Development mode)
- **OpenTelemetry** instrumentation for traces, metrics, and logs
- **Standard middleware** (correlation tracking, problem details, request logging)
- **Placeholder DbContext** with sample entity
- **Test project** with integration test infrastructure using `WebApplicationFactory`
- **Gateway routing** configured at `/api/v1/files/*`

### What Is Planned

See the [Planned Features](#planned-features) section for the comprehensive roadmap.

## Tech Stack

### Runtime

| Component | Technology | Version |
|-----------|------------|---------|
| Framework | ASP.NET Core | .NET 10 |
| Language | C# | Latest (nullable enabled) |
| Database | PostgreSQL | 16 |
| ORM | Entity Framework Core | 10.0.0 |
| API Style | Minimal APIs | - |
| Message Bus | MassTransit + RabbitMQ | 8.3.6 |
| Observability | OpenTelemetry | 1.14.0 |
| Documentation | Swagger/Swashbuckle | 10.1.0 |

### Key Dependencies

From the project file (`Dhadgar.Files.csproj`):

```xml
<!-- Shared libraries -->
<ProjectReference Include="..\Shared\Dhadgar.Contracts\Dhadgar.Contracts.csproj" />
<ProjectReference Include="..\Shared\Dhadgar.Shared\Dhadgar.Shared.csproj" />
<ProjectReference Include="..\Shared\Dhadgar.Messaging\Dhadgar.Messaging.csproj" />
<ProjectReference Include="..\Shared\Dhadgar.ServiceDefaults\Dhadgar.ServiceDefaults.csproj" />

<!-- Messaging -->
<PackageReference Include="MassTransit" />
<PackageReference Include="MassTransit.RabbitMQ" />

<!-- Data -->
<PackageReference Include="Microsoft.EntityFrameworkCore" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />

<!-- Observability -->
<PackageReference Include="OpenTelemetry.Extensions.Hosting" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
<PackageReference Include="OpenTelemetry.Instrumentation.Process" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
```

### Architecture Constraints

Per Meridian Console's microservices architecture:

- **No direct service references** - Services cannot reference each other via `ProjectReference`
- **Allowed dependencies**: Only shared libraries (`Contracts`, `Shared`, `Messaging`, `ServiceDefaults`)
- **Runtime communication**: HTTP (via typed clients) and messaging (via MassTransit)
- **Database isolation**: Each service owns its schema exclusively

## Quick Start

### Prerequisites

- .NET SDK 10.0.100 (pinned in `global.json`)
- Docker Desktop (for PostgreSQL, RabbitMQ, Redis, observability stack)
- Optional: Azure account (for Azure Blob Storage integration)
- Optional: AWS account (for S3 integration)

### 1. Start Local Infrastructure

From the repository root:

```bash
# Start all development infrastructure
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

This starts:
- **PostgreSQL** on `localhost:5432` (database: `dhadgar_platform`)
- **RabbitMQ** on `localhost:5672` (management UI: `localhost:15672`)
- **Redis** on `localhost:6379`
- **Grafana** on `localhost:3000` (observability dashboards)
- **Prometheus** on `localhost:9090` (metrics)
- **Loki** on `localhost:3100` (logs)
- **OTLP Collector** on `localhost:4317` (telemetry ingestion)

Default credentials for all services: `dhadgar` / `dhadgar`

### 2. Run the Service

```bash
# Run directly
dotnet run --project src/Dhadgar.Files

# Or with hot reload
dotnet watch --project src/Dhadgar.Files
```

The service runs on `http://localhost:5060` by default (configured in `launchSettings.json`).

### 3. Verify the Service

```bash
# Check service info
curl http://localhost:5060/

# Expected response:
# {"service":"Dhadgar.Files","message":"Hello from Dhadgar.Files"}

# Check health
curl http://localhost:5060/healthz

# Expected response:
# {"service":"Dhadgar.Files","status":"ok","timestamp":"2026-01-22T..."}

# Check hello endpoint
curl http://localhost:5060/hello

# Expected response:
# Hello from Dhadgar.Files
```

### 4. Access Swagger UI

Open `http://localhost:5060/swagger` in your browser to explore the API documentation.

Swagger is only enabled in Development and Testing environments.

### 5. Access via Gateway (Production-like)

If running the Gateway alongside:

```bash
# Start the gateway
dotnet run --project src/Dhadgar.Gateway &

# Access Files service through gateway
curl http://localhost:5000/api/v1/files/healthz
```

## Configuration

### Configuration Files

| File | Purpose |
|------|---------|
| `appsettings.json` | Base configuration (committed) |
| `appsettings.Development.json` | Development overrides (committed) |
| `appsettings.{Environment}.json` | Environment-specific overrides |
| User Secrets | Local sensitive values (not committed) |
| Environment Variables | Production/Kubernetes configuration |

### Configuration Hierarchy (Priority Order)

1. Command-line arguments
2. Environment variables
3. User secrets (Development only)
4. `appsettings.{Environment}.json`
5. `appsettings.json`

### Current Configuration (`appsettings.json`)

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

### Planned Configuration Options

When the service is fully implemented, these configuration sections will be available:

#### Database Configuration

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=dhadgar_files;Username=dhadgar;Password=dhadgar"
  },
  "Database": {
    "AutoMigrate": true,
    "CommandTimeout": 30,
    "EnableSensitiveDataLogging": false
  }
}
```

#### Storage Provider Configuration

```json
{
  "Storage": {
    "DefaultProvider": "AzureBlob",
    "Providers": {
      "AzureBlob": {
        "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...",
        "ContainerName": "files",
        "SignedUrlExpirationMinutes": 60
      },
      "S3": {
        "Region": "us-east-1",
        "BucketName": "meridian-files",
        "AccessKeyId": "...",
        "SecretAccessKey": "...",
        "SignedUrlExpirationMinutes": 60
      },
      "Local": {
        "BasePath": "/var/meridian/files",
        "MaxSizeBytes": 10737418240
      }
    }
  }
}
```

#### File Processing Configuration

```json
{
  "FileProcessing": {
    "MaxFileSizeBytes": 5368709120,
    "AllowedExtensions": [".zip", ".tar.gz", ".7z", ".jar", ".pak"],
    "ChunkSizeBytes": 10485760,
    "EnableVirusScanning": true,
    "VirusScannerEndpoint": "http://localhost:3310"
  }
}
```

#### Transfer Configuration

```json
{
  "Transfers": {
    "MaxConcurrentTransfers": 10,
    "TransferTimeoutMinutes": 30,
    "RetryCount": 3,
    "RetryDelaySeconds": 5,
    "EnableResumableUploads": true
  }
}
```

#### OpenTelemetry Configuration

```json
{
  "OpenTelemetry": {
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

### Using User Secrets

For local development with sensitive values:

```bash
# Initialize user secrets (one-time)
dotnet user-secrets init --project src/Dhadgar.Files

# Set secrets
dotnet user-secrets set "Storage:Providers:AzureBlob:ConnectionString" "your-connection-string" --project src/Dhadgar.Files
dotnet user-secrets set "Storage:Providers:S3:AccessKeyId" "your-access-key" --project src/Dhadgar.Files
dotnet user-secrets set "Storage:Providers:S3:SecretAccessKey" "your-secret-key" --project src/Dhadgar.Files

# List all secrets
dotnet user-secrets list --project src/Dhadgar.Files

# Clear all secrets
dotnet user-secrets clear --project src/Dhadgar.Files
```

### Environment Variables

For production/Kubernetes deployment, use environment variables:

```bash
# Connection strings
export ConnectionStrings__Postgres="Host=prod-db;Port=5432;Database=dhadgar_files;..."

# Storage configuration
export Storage__DefaultProvider="AzureBlob"
export Storage__Providers__AzureBlob__ConnectionString="..."

# OpenTelemetry
export OpenTelemetry__OtlpEndpoint="http://otel-collector:4317"
```

Note: Use double underscores (`__`) to represent nested JSON keys in environment variables.

## API Endpoints

### Current Endpoints (Scaffolding)

| Endpoint | Method | Description | Tags |
|----------|--------|-------------|------|
| `/` | GET | Service banner and status | Health |
| `/hello` | GET | Simple hello world response | Health |
| `/healthz` | GET | Comprehensive health check | Health |
| `/livez` | GET | Kubernetes liveness probe | Health |
| `/readyz` | GET | Kubernetes readiness probe | Health |
| `/swagger` | GET | Swagger UI (Development only) | Docs |
| `/swagger/v1/swagger.json` | GET | OpenAPI spec | Docs |

### Planned Endpoints

When fully implemented, the service will expose these endpoint groups:

#### File Metadata Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/files` | GET | List files (paginated, filtered) |
| `/files` | POST | Create file metadata record |
| `/files/{id}` | GET | Get file metadata by ID |
| `/files/{id}` | PATCH | Update file metadata |
| `/files/{id}` | DELETE | Delete file (soft delete) |
| `/files/{id}/versions` | GET | List file versions |
| `/files/{id}/versions/{version}` | GET | Get specific version |
| `/files/{id}/restore/{version}` | POST | Restore to previous version |

#### Transfer Orchestration Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/files/{id}/upload-url` | POST | Generate signed upload URL |
| `/files/{id}/download-url` | GET | Generate signed download URL |
| `/files/{id}/upload/complete` | POST | Complete multi-part upload |
| `/files/{id}/upload/abort` | POST | Abort multi-part upload |
| `/transfers` | GET | List active transfers |
| `/transfers/{id}` | GET | Get transfer status |
| `/transfers/{id}/cancel` | POST | Cancel transfer |

#### Distribution Endpoints (Agent Communication)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/distributions` | POST | Create distribution job |
| `/distributions/{id}` | GET | Get distribution status |
| `/distributions/{id}/nodes` | GET | List target nodes and their status |
| `/nodes/{nodeId}/files` | GET | List files on a specific node |
| `/nodes/{nodeId}/files/{fileId}/verify` | POST | Verify file integrity on node |

#### Backup Management Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/backups` | GET | List backups (paginated) |
| `/backups` | POST | Create backup job |
| `/backups/{id}` | GET | Get backup details |
| `/backups/{id}/restore` | POST | Restore from backup |
| `/backups/{id}/download-url` | GET | Generate backup download URL |
| `/servers/{serverId}/backups` | GET | List backups for a server |

#### Mod Distribution Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/mods/{modId}/files` | GET | List files for a mod |
| `/mods/{modId}/files` | POST | Upload mod file |
| `/mods/{modId}/distribute` | POST | Distribute mod to servers |
| `/servers/{serverId}/mods` | GET | List mods installed on server |
| `/servers/{serverId}/mods/{modId}` | POST | Install mod on server |
| `/servers/{serverId}/mods/{modId}` | DELETE | Remove mod from server |

#### Storage Provider Endpoints (Admin)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/admin/storage/providers` | GET | List configured providers |
| `/admin/storage/providers/{name}/test` | POST | Test provider connectivity |
| `/admin/storage/usage` | GET | Get storage usage statistics |
| `/admin/storage/cleanup` | POST | Trigger orphan file cleanup |

### API Response Formats

All endpoints return responses in a consistent format.

#### Success Response

```json
{
  "data": { ... },
  "meta": {
    "timestamp": "2026-01-22T15:30:00Z",
    "requestId": "abc123",
    "correlationId": "xyz789"
  }
}
```

#### Paginated Response

```json
{
  "data": [ ... ],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "totalItems": 150,
    "totalPages": 8
  },
  "meta": {
    "timestamp": "2026-01-22T15:30:00Z"
  }
}
```

#### Error Response (RFC 7807 Problem Details)

```json
{
  "type": "https://httpstatuses.io/404",
  "title": "Not Found",
  "status": 404,
  "detail": "File with ID 'abc123' was not found.",
  "instance": "/files/abc123",
  "traceId": "00-abc123def456-789012-00",
  "correlationId": "xyz789"
}
```

### Request Headers

| Header | Required | Description |
|--------|----------|-------------|
| `Authorization` | Yes* | Bearer JWT token |
| `X-Organization-Id` | Yes* | Current organization context |
| `X-Correlation-Id` | No | Client-provided correlation ID |
| `X-Request-Id` | No | Client-provided request ID |
| `Content-Type` | Yes** | `application/json` for request bodies |

*Required for authenticated endpoints (all except health checks)
**Required for POST/PATCH/PUT requests with body

### Response Headers

| Header | Description |
|--------|-------------|
| `X-Correlation-Id` | Correlation ID (echoed or generated) |
| `X-Request-Id` | Request ID (echoed or generated) |
| `X-Trace-Id` | OpenTelemetry trace ID |
| `traceparent` | W3C Trace Context header |
| `tracestate` | W3C Trace Context state |

## Database Schema

### Current Schema

The service currently uses a placeholder entity for EF Core validation:

```csharp
public sealed class SampleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "hello";
}
```

### Planned Schema

When fully implemented, the database will contain these entities:

#### Files Table

Stores core file metadata.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `uuid` | Primary key |
| `OrganizationId` | `uuid` | Owning organization (tenant) |
| `Name` | `varchar(255)` | Display name |
| `FileName` | `varchar(255)` | Original file name |
| `MimeType` | `varchar(128)` | MIME type |
| `SizeBytes` | `bigint` | File size in bytes |
| `StorageProvider` | `varchar(64)` | Storage provider name |
| `StoragePath` | `varchar(1024)` | Path/key in storage |
| `Sha256Checksum` | `char(64)` | SHA-256 hash |
| `Md5Checksum` | `char(32)` | MD5 hash (optional) |
| `Status` | `varchar(32)` | `Pending`, `Uploading`, `Ready`, `Error`, `Deleted` |
| `Category` | `varchar(64)` | `GameFiles`, `Mod`, `Backup`, `Config`, `Log` |
| `Metadata` | `jsonb` | Custom metadata |
| `IsPublic` | `boolean` | Public access flag |
| `ExpiresAt` | `timestamptz` | Auto-deletion time (nullable) |
| `CreatedAt` | `timestamptz` | Creation timestamp |
| `CreatedBy` | `uuid` | Creating user ID |
| `UpdatedAt` | `timestamptz` | Last update timestamp |
| `DeletedAt` | `timestamptz` | Soft delete timestamp (nullable) |

#### FileVersions Table

Tracks file version history.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `uuid` | Primary key |
| `FileId` | `uuid` | Parent file reference |
| `Version` | `int` | Version number |
| `StoragePath` | `varchar(1024)` | Path to this version |
| `SizeBytes` | `bigint` | Version size |
| `Sha256Checksum` | `char(64)` | Version checksum |
| `ChangeNote` | `text` | Optional change description |
| `CreatedAt` | `timestamptz` | Version creation time |
| `CreatedBy` | `uuid` | Creating user ID |

#### Transfers Table

Tracks ongoing file transfers.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `uuid` | Primary key |
| `FileId` | `uuid` | File being transferred |
| `Type` | `varchar(32)` | `Upload`, `Download`, `Distribution` |
| `Status` | `varchar(32)` | `Pending`, `InProgress`, `Completed`, `Failed`, `Cancelled` |
| `SourceProvider` | `varchar(64)` | Source storage provider |
| `DestinationProvider` | `varchar(64)` | Destination (nullable) |
| `BytesTransferred` | `bigint` | Progress tracking |
| `TotalBytes` | `bigint` | Total size |
| `UploadId` | `varchar(256)` | Multi-part upload ID |
| `Parts` | `jsonb` | Multi-part upload parts |
| `ErrorMessage` | `text` | Error details (nullable) |
| `StartedAt` | `timestamptz` | Transfer start time |
| `CompletedAt` | `timestamptz` | Completion time (nullable) |
| `ExpiresAt` | `timestamptz` | URL expiration |

#### Distributions Table

Tracks file distribution to nodes.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `uuid` | Primary key |
| `FileId` | `uuid` | File being distributed |
| `Status` | `varchar(32)` | `Pending`, `InProgress`, `Completed`, `PartialFail` |
| `TargetNodeCount` | `int` | Number of target nodes |
| `CompletedNodeCount` | `int` | Successfully completed |
| `FailedNodeCount` | `int` | Failed nodes |
| `CreatedAt` | `timestamptz` | Creation time |
| `CompletedAt` | `timestamptz` | Completion time |

#### DistributionNodes Table

Junction table for distribution to nodes.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `uuid` | Primary key |
| `DistributionId` | `uuid` | Parent distribution |
| `NodeId` | `uuid` | Target node |
| `Status` | `varchar(32)` | `Pending`, `Downloading`, `Verifying`, `Complete`, `Failed` |
| `BytesTransferred` | `bigint` | Progress |
| `ErrorMessage` | `text` | Error details |
| `StartedAt` | `timestamptz` | Start time |
| `CompletedAt` | `timestamptz` | Completion time |

#### Backups Table

Tracks backup files.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `uuid` | Primary key |
| `FileId` | `uuid` | Associated file record |
| `OrganizationId` | `uuid` | Owning organization |
| `ServerId` | `uuid` | Server backed up (nullable) |
| `BackupType` | `varchar(32)` | `Full`, `Incremental`, `Config` |
| `Description` | `text` | Backup description |
| `RetentionDays` | `int` | Days to retain |
| `CreatedAt` | `timestamptz` | Backup creation time |
| `ExpiresAt` | `timestamptz` | Calculated expiration |

### Entity Relationship Diagram

```
┌─────────────────┐       ┌─────────────────────┐
│     Files       │       │    FileVersions     │
├─────────────────┤       ├─────────────────────┤
│ Id (PK)         │──────<│ Id (PK)             │
│ OrganizationId  │       │ FileId (FK)         │
│ Name            │       │ Version             │
│ FileName        │       │ StoragePath         │
│ MimeType        │       │ SizeBytes           │
│ SizeBytes       │       │ Sha256Checksum      │
│ StorageProvider │       │ ChangeNote          │
│ StoragePath     │       │ CreatedAt           │
│ Sha256Checksum  │       │ CreatedBy           │
│ Status          │       └─────────────────────┘
│ Category        │
│ Metadata (JSONB)│       ┌─────────────────────┐
│ CreatedAt       │       │     Transfers       │
│ UpdatedAt       │       ├─────────────────────┤
│ DeletedAt       │──────<│ Id (PK)             │
└─────────────────┘       │ FileId (FK)         │
        │                 │ Type                │
        │                 │ Status              │
        │                 │ BytesTransferred    │
        │                 │ TotalBytes          │
        │                 │ UploadId            │
        │                 │ Parts (JSONB)       │
        │                 └─────────────────────┘
        │
        │                 ┌─────────────────────┐
        │                 │   Distributions     │
        │                 ├─────────────────────┤
        └────────────────<│ Id (PK)             │
                          │ FileId (FK)         │
                          │ Status              │
                          │ TargetNodeCount     │
                          │ CompletedNodeCount  │
                          └──────────┬──────────┘
                                     │
                          ┌──────────┴──────────┐
                          │ DistributionNodes   │
                          ├─────────────────────┤
                          │ Id (PK)             │
                          │ DistributionId (FK) │
                          │ NodeId              │
                          │ Status              │
                          │ BytesTransferred    │
                          └─────────────────────┘
```

### Indexes

Key indexes for query performance:

```sql
-- Primary lookups
CREATE UNIQUE INDEX ix_files_id ON files(id);
CREATE INDEX ix_files_org_id ON files(organization_id);
CREATE INDEX ix_files_org_category ON files(organization_id, category);
CREATE INDEX ix_files_status ON files(status) WHERE deleted_at IS NULL;
CREATE INDEX ix_files_checksum ON files(sha256_checksum);

-- Version lookups
CREATE INDEX ix_file_versions_file_id ON file_versions(file_id);
CREATE UNIQUE INDEX ix_file_versions_file_version ON file_versions(file_id, version);

-- Transfer lookups
CREATE INDEX ix_transfers_file_id ON transfers(file_id);
CREATE INDEX ix_transfers_status ON transfers(status) WHERE status NOT IN ('Completed', 'Cancelled');

-- Distribution lookups
CREATE INDEX ix_distributions_file_id ON distributions(file_id);
CREATE INDEX ix_distribution_nodes_dist_id ON distribution_nodes(distribution_id);
CREATE INDEX ix_distribution_nodes_node_id ON distribution_nodes(node_id);
```

### Migrations

Currently, no migrations exist. To create migrations when implementing features:

```bash
# Add a new migration
dotnet ef migrations add InitialCreate \
  --project src/Dhadgar.Files \
  --startup-project src/Dhadgar.Files \
  --output-dir Data/Migrations

# Apply migrations
dotnet ef database update \
  --project src/Dhadgar.Files \
  --startup-project src/Dhadgar.Files

# Remove last migration (if not applied)
dotnet ef migrations remove \
  --project src/Dhadgar.Files \
  --startup-project src/Dhadgar.Files
```

### Auto-Migration (Development)

In Development mode, migrations are automatically applied on startup:

```csharp
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FilesDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "DB migration failed (dev).");
    }
}
```

## Architecture

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                          Control Plane                               │
│  ┌───────────┐    ┌───────────────┐    ┌─────────────────────────┐  │
│  │  Gateway  │───>│ Files Service │<───│ Other Services          │  │
│  │  (YARP)   │    │ (This Service)│    │ (Servers, Mods, Tasks)  │  │
│  └───────────┘    └───────┬───────┘    └─────────────────────────┘  │
│                           │                                          │
│                    ┌──────┴──────┐                                   │
│                    │  PostgreSQL │                                   │
│                    │  (Metadata) │                                   │
│                    └─────────────┘                                   │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                    ┌────────┴────────┐
                    │  RabbitMQ       │
                    │  (Messages)     │
                    └────────┬────────┘
                             │
       ┌─────────────────────┼─────────────────────┐
       │                     │                     │
       ▼                     ▼                     ▼
┌────────────┐        ┌────────────┐        ┌────────────┐
│   Agent    │        │   Agent    │        │   Agent    │
│  (Node 1)  │        │  (Node 2)  │        │  (Node N)  │
│ Customer   │        │ Customer   │        │ Customer   │
│ Hardware   │        │ Hardware   │        │ Hardware   │
└──────┬─────┘        └──────┬─────┘        └──────┬─────┘
       │                     │                     │
       ▼                     ▼                     ▼
┌────────────┐        ┌────────────┐        ┌────────────┐
│ Local FS   │        │ Local FS   │        │ Local FS   │
│ Game Files │        │ Game Files │        │ Game Files │
└────────────┘        └────────────┘        └────────────┘
       │                     │                     │
       └─────────────────────┼─────────────────────┘
                             │
                    ┌────────┴────────┐
                    │ Storage Provider│
                    │ (Azure/S3/etc)  │
                    └─────────────────┘
```

### Component Responsibilities

| Component | Role |
|-----------|------|
| **Gateway** | Routes `/api/v1/files/*` to Files service; handles auth, rate limiting |
| **Files Service** | Metadata storage, URL generation, transfer orchestration |
| **PostgreSQL** | Persistent storage for file metadata and transfer state |
| **RabbitMQ** | Async communication for distribution jobs and notifications |
| **Agents** | Execute actual file operations on customer hardware |
| **Storage Provider** | Durable file content storage (Azure Blob, S3, etc.) |

### File Upload Flow

```
┌──────┐     ┌─────────┐     ┌───────┐     ┌─────────┐     ┌─────────┐
│Client│     │ Gateway │     │ Files │     │ Storage │     │ Agent   │
└──┬───┘     └────┬────┘     └───┬───┘     └────┬────┘     └────┬────┘
   │              │              │              │              │
   │ 1. Create    │              │              │              │
   │ file record  │              │              │              │
   │─────────────>│─────────────>│              │              │
   │              │              │              │              │
   │              │  2. Generate │              │              │
   │              │  signed URL  │              │              │
   │<─────────────│<─────────────│              │              │
   │              │              │              │              │
   │ 3. Upload    │              │              │              │
   │ directly to  │              │              │              │
   │ storage      │              │              │              │
   │───────────────────────────────────────────>│              │
   │              │              │              │              │
   │ 4. Complete  │              │              │              │
   │ upload       │              │              │              │
   │─────────────>│─────────────>│              │              │
   │              │              │              │              │
   │              │  5. Verify   │              │              │
   │              │  checksum    │              │              │
   │              │              │<────────────>│              │
   │              │              │              │              │
   │ 6. Success   │              │              │              │
   │<─────────────│<─────────────│              │              │
   │              │              │              │              │
   │              │  7. Publish  │              │              │
   │              │  FileUploaded│              │              │
   │              │  event       │              │              │
   │              │              │─────────────────────────────>│
   │              │              │              │              │
```

### File Distribution Flow

```
┌──────┐     ┌─────────┐     ┌───────┐     ┌─────────┐     ┌─────────┐
│Client│     │ Gateway │     │ Files │     │RabbitMQ │     │ Agent   │
└──┬───┘     └────┬────┘     └───┬───┘     └────┬────┘     └────┬────┘
   │              │              │              │              │
   │ 1. Request   │              │              │              │
   │ distribution │              │              │              │
   │─────────────>│─────────────>│              │              │
   │              │              │              │              │
   │              │  2. Create   │              │              │
   │              │  distribution│              │              │
   │              │  record      │              │              │
   │              │              │              │              │
   │              │  3. Publish  │              │              │
   │              │  commands    │              │              │
   │              │              │─────────────>│              │
   │              │              │              │              │
   │              │              │              │ 4. Deliver   │
   │              │              │              │ to agents    │
   │              │              │              │─────────────>│
   │              │              │              │              │
   │ 5. Return    │              │              │              │
   │ job ID       │              │              │              │
   │<─────────────│<─────────────│              │              │
   │              │              │              │              │
   │              │              │              │  6. Download │
   │              │              │              │  from storage│
   │              │              │              │<─────────────│
   │              │              │              │              │
   │              │              │              │  7. Report   │
   │              │              │              │  progress    │
   │              │              │<─────────────│<─────────────│
   │              │              │              │              │
   │ 8. Poll      │              │              │              │
   │ status       │              │              │              │
   │─────────────>│─────────────>│              │              │
   │<─────────────│<─────────────│              │              │
```

### Service Communication Patterns

#### Synchronous (HTTP)

Used for:
- Client API requests
- Metadata lookups
- Signed URL generation
- Status queries

#### Asynchronous (MassTransit/RabbitMQ)

Used for:
- File distribution commands
- Upload/download completion events
- Progress updates from agents
- Cross-service notifications

### Middleware Pipeline

Request processing order in `Program.cs`:

```csharp
// 1. Swagger (Development only)
app.UseMeridianSwagger();

// 2. Correlation tracking
app.UseMiddleware<CorrelationMiddleware>();

// 3. Exception handling
app.UseMiddleware<ProblemDetailsMiddleware>();

// 4. Request/response logging
app.UseMiddleware<RequestLoggingMiddleware>();

// 5. Endpoints
app.MapGet("/", ...);
app.MapGet("/hello", ...);
app.MapDhadgarDefaultEndpoints();
```

## Integration Points

### Gateway Integration

The Gateway routes traffic to the Files service:

**Route Configuration** (`Gateway/appsettings.json`):

```json
{
  "ReverseProxy": {
    "Routes": {
      "files-route": {
        "ClusterId": "files",
        "Order": 20,
        "Match": { "Path": "/api/v1/files/{**catch-all}" },
        "AuthorizationPolicy": "TenantScoped",
        "RateLimiterPolicy": "PerTenant",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/files" }
        ]
      }
    },
    "Clusters": {
      "files": {
        "LoadBalancingPolicy": "RoundRobin",
        "HttpRequest": {
          "ActivityTimeout": "00:05:00"
        },
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Path": "/healthz"
          }
        },
        "Destinations": {
          "d1": {
            "Address": "http://localhost:5060/"
          }
        }
      }
    }
  }
}
```

Notable configuration:
- **Extended timeout** (5 minutes) for large file operations
- **TenantScoped authorization** ensures organization isolation
- **Per-tenant rate limiting** prevents resource abuse

### Agent Integration (Planned)

Agents will communicate with the Files service for:

| Operation | Direction | Mechanism |
|-----------|-----------|-----------|
| Download commands | Control Plane -> Agent | RabbitMQ message |
| Progress updates | Agent -> Control Plane | RabbitMQ message |
| Completion reports | Agent -> Control Plane | RabbitMQ message |
| Signed URL requests | Agent -> Control Plane | HTTP (internal) |
| Checksum verification | Agent -> Control Plane | RabbitMQ message |

#### Message Contracts (Planned)

```csharp
// Command: Download file to node
public record DistributeFileToNode(
    Guid DistributionId,
    Guid FileId,
    Guid NodeId,
    string DownloadUrl,
    string DestinationPath,
    string ExpectedSha256,
    long ExpectedSizeBytes);

// Event: Download progress
public record FileDownloadProgress(
    Guid DistributionId,
    Guid NodeId,
    long BytesDownloaded,
    long TotalBytes);

// Event: Download completed
public record FileDownloadCompleted(
    Guid DistributionId,
    Guid NodeId,
    string ActualSha256,
    bool IntegrityVerified);

// Event: Download failed
public record FileDownloadFailed(
    Guid DistributionId,
    Guid NodeId,
    string ErrorMessage,
    string ErrorCode);
```

### Servers Service Integration (Planned)

The Servers service will interact with Files for:

| Use Case | Flow |
|----------|------|
| Game file installation | Servers -> Files: Request file distribution to server node |
| Config file management | Servers -> Files: Upload/download server configs |
| Log retrieval | Servers -> Files: Request log file upload from node |
| Crash dump collection | Servers -> Files: Request crash dump upload |

### Mods Service Integration (Planned)

The Mods service will interact with Files for:

| Use Case | Flow |
|----------|------|
| Mod upload | Mods -> Files: Store mod archive |
| Mod distribution | Mods -> Files: Distribute mod to servers |
| Version management | Mods -> Files: Track mod file versions |
| Checksum verification | Mods -> Files: Verify mod integrity before install |

### Notifications Service Integration (Planned)

The Notifications service will receive events from Files:

| Event | Notification |
|-------|--------------|
| `LargeUploadCompleted` | Email/Discord notification to user |
| `DistributionCompleted` | Notify when files reach all nodes |
| `DistributionFailed` | Alert on distribution failures |
| `BackupCompleted` | Confirm backup success |
| `StorageQuotaWarning` | Alert when approaching limits |

### Tasks Service Integration (Planned)

The Tasks service may orchestrate complex file workflows:

| Workflow | Coordination |
|----------|--------------|
| Server deployment | Tasks coordinates: Stop server -> Backup -> Update files -> Start server |
| Mod rollback | Tasks coordinates: Stop affected servers -> Restore mod version -> Restart |
| Mass distribution | Tasks tracks: Distribute to 100+ nodes with retry logic |

## Planned Features

### Phase 1: Core File Metadata

- [x] Project scaffolding
- [ ] Database schema implementation
- [ ] Basic CRUD operations for file metadata
- [ ] File category taxonomy
- [ ] Soft delete with retention
- [ ] Basic search and filtering
- [ ] Pagination support

### Phase 2: Storage Provider Abstraction

- [ ] Storage provider interface
- [ ] Azure Blob Storage implementation
- [ ] AWS S3 implementation
- [ ] Local filesystem implementation (for testing/dev)
- [ ] Provider configuration management
- [ ] Multi-provider support per tenant

### Phase 3: Upload/Download Orchestration

- [ ] Signed URL generation (time-limited)
- [ ] Multi-part upload support for large files
- [ ] Upload progress tracking
- [ ] Upload completion verification
- [ ] Download URL generation
- [ ] Bandwidth tracking

### Phase 4: File Integrity

- [ ] SHA-256 checksum computation
- [ ] MD5 checksum support (legacy compatibility)
- [ ] Checksum verification on upload completion
- [ ] Periodic integrity checks
- [ ] Corruption detection and alerting
- [ ] Automatic re-upload prompting

### Phase 5: Distribution to Nodes

- [ ] Distribution job creation
- [ ] Target node selection
- [ ] MassTransit message publishing
- [ ] Progress tracking per node
- [ ] Partial success handling
- [ ] Retry logic with exponential backoff
- [ ] Distribution cancellation

### Phase 6: Backup Management

- [ ] Backup metadata storage
- [ ] Automatic backup scheduling (via Tasks service)
- [ ] Retention policy enforcement
- [ ] Backup restoration
- [ ] Incremental backup support
- [ ] Backup integrity verification

### Phase 7: Mod Distribution

- [ ] Mod file type handling
- [ ] Compatibility matrix integration
- [ ] Version-aware distribution
- [ ] Dependency resolution
- [ ] Rollback support
- [ ] Mod integrity verification

### Phase 8: Advanced Features

- [ ] Content-addressable storage
- [ ] Deduplication
- [ ] Compression (for transfer)
- [ ] Virus scanning integration
- [ ] File preview generation (for supported types)
- [ ] Access audit logging
- [ ] Usage analytics and reporting
- [ ] Storage quota enforcement

### Phase 9: Performance & Scale

- [ ] Caching layer (Redis)
- [ ] CDN integration for public files
- [ ] Parallel distribution to multiple nodes
- [ ] Connection pooling optimization
- [ ] Query optimization
- [ ] Sharding strategy (if needed)

## Testing

### Current Test Coverage

The test project (`tests/Dhadgar.Files.Tests`) includes:

| Test Class | Description |
|------------|-------------|
| `HelloWorldTests` | Validates the Hello message constant |
| `SwaggerTests` | Integration tests for Swagger/OpenAPI endpoints |

### Running Tests

```bash
# Run all Files tests
dotnet test tests/Dhadgar.Files.Tests

# Run with verbose output
dotnet test tests/Dhadgar.Files.Tests --logger "console;verbosity=detailed"

# Run specific test
dotnet test tests/Dhadgar.Files.Tests --filter "FullyQualifiedName~HelloWorldTests"

# Run with code coverage
dotnet test tests/Dhadgar.Files.Tests --collect:"XPlat Code Coverage"
```

### Test Infrastructure

The test project uses:

- **xUnit** - Test framework
- **WebApplicationFactory** - Integration testing with in-memory server
- **InMemory EF Core** - Database testing without PostgreSQL dependency
- **ServiceDefaults.Tests** - Shared test utilities (Swagger validation helpers)

#### WebApplicationFactory Setup

```csharp
public class FilesWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove PostgreSQL registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<FilesDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Add in-memory database
            services.AddDbContext<FilesDbContext>(options =>
            {
                options.UseInMemoryDatabase("FilesTestDb");
            });
        });
    }
}
```

### Planned Test Categories

When features are implemented, tests will cover:

#### Unit Tests

- File metadata validation
- Checksum computation
- Storage path generation
- URL signing logic
- Category classification

#### Integration Tests

- API endpoint behavior
- Database operations
- Storage provider operations (with test doubles)
- Message publishing

#### End-to-End Tests

- Complete upload flow
- Complete distribution flow
- Backup and restore flow

### Writing New Tests

Example test structure:

```csharp
public class FileMetadataTests : IClassFixture<FilesWebApplicationFactory>
{
    private readonly FilesWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public FileMetadataTests(FilesWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateFile_WithValidData_ReturnsCreated()
    {
        // Arrange
        var request = new CreateFileRequest
        {
            Name = "test-file.zip",
            MimeType = "application/zip",
            Category = "GameFiles"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/files", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var file = await response.Content.ReadFromJsonAsync<FileResponse>();
        Assert.NotNull(file);
        Assert.Equal("test-file.zip", file.Name);
    }
}
```

## Observability

### Distributed Tracing

The service is instrumented with OpenTelemetry for distributed tracing:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Dhadgar.Files"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (otlpUri is not null)
        {
            tracing.AddOtlpExporter(options => options.Endpoint = otlpUri);
        }
    });
```

### Metrics

Collected metrics include:

| Metric | Description |
|--------|-------------|
| HTTP request duration | Request latency histogram |
| HTTP request count | Request count by status code |
| Active connections | Current active connections |
| Runtime metrics | GC, thread pool, etc. |
| Process metrics | CPU, memory usage |

### Logging

Structured logging with OpenTelemetry integration:

```csharp
builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(resourceBuilder);
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.ParseStateValues = true;
});
```

### Correlation IDs

Every request receives correlation tracking via `CorrelationMiddleware`:

- `X-Correlation-Id` - Session/user journey tracking
- `X-Request-Id` - Individual request tracking
- `X-Trace-Id` - OpenTelemetry trace ID

### Viewing Telemetry Data

With the local observability stack running:

| Tool | URL | Purpose |
|------|-----|---------|
| **Grafana** | http://localhost:3000 | Dashboards, visualization |
| **Prometheus** | http://localhost:9090 | Metrics queries |
| **Loki** | http://localhost:3100 | Log queries (via Grafana) |

### Enabling OTLP Export

To export telemetry to the local collector:

```bash
# Set via user secrets
dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317" --project src/Dhadgar.Files

# Or via environment variable
export OpenTelemetry__OtlpEndpoint="http://localhost:4317"
```

### Planned Observability Enhancements

- Custom metrics for file operations
- Upload/download throughput metrics
- Storage provider latency metrics
- Distribution success/failure rates
- Alerting rules for SLA violations

## Related Documentation

### Architecture & Design

- [CLAUDE.md (Root)](../../CLAUDE.md) - Project-wide development guide
- [Microservices Architecture](../../docs/architecture.md) - Overall system design (planned)
- [Database Per Service Pattern](../../docs/database-design.md) - Data isolation approach (planned)

### Related Services

| Service | Relationship |
|---------|--------------|
| [Gateway](../Dhadgar.Gateway/README.md) | Routes `/api/v1/files/*` traffic |
| [Identity](../Dhadgar.Identity/README.md) | Authentication, authorization context |
| [Servers](../Dhadgar.Servers/) | Game server file management |
| [Mods](../Dhadgar.Mods/) | Mod file storage and distribution |
| [Nodes](../Dhadgar.Nodes/) | Agent registration, file distribution targets |
| [Tasks](../Dhadgar.Tasks/) | Complex workflow orchestration |
| [Notifications](../Dhadgar.Notifications/) | Event notifications |

### Shared Libraries

| Library | Usage |
|---------|-------|
| [Dhadgar.Contracts](../Shared/Dhadgar.Contracts/) | DTOs, message contracts |
| [Dhadgar.Shared](../Shared/Dhadgar.Shared/) | Utilities, primitives |
| [Dhadgar.Messaging](../Shared/Dhadgar.Messaging/) | MassTransit configuration |
| [Dhadgar.ServiceDefaults](../Shared/Dhadgar.ServiceDefaults/) | Middleware, health checks, Swagger |

### Infrastructure

- [Docker Compose Setup](../../deploy/compose/README.md) - Local development infrastructure
- [Kubernetes Deployment](../../deploy/kubernetes/) - Production deployment (planned)

### Operations

- [Deployment Runbook](../../docs/runbooks/files-service-deployment.md) - Deployment procedures (planned)
- [Troubleshooting Guide](../../docs/runbooks/files-service-troubleshooting.md) - Common issues (planned)
- [Monitoring Guide](../../docs/runbooks/files-service-monitoring.md) - Observability setup (planned)

---

## Quick Reference

### Ports

| Environment | Port | Notes |
|-------------|------|-------|
| Development | 5060 | Direct access |
| Via Gateway | 5000 | Path: `/api/v1/files/*` |

### Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Application entry point, middleware configuration |
| `Hello.cs` | Hello message constant (smoke test surface) |
| `Data/FilesDbContext.cs` | EF Core database context |
| `Data/FilesDbContextFactory.cs` | Design-time DbContext factory |
| `appsettings.json` | Base configuration |
| `Properties/launchSettings.json` | Development run settings |

### Common Commands

```bash
# Run service
dotnet run --project src/Dhadgar.Files

# Run with hot reload
dotnet watch --project src/Dhadgar.Files

# Run tests
dotnet test tests/Dhadgar.Files.Tests

# Add migration
dotnet ef migrations add MigrationName \
  --project src/Dhadgar.Files \
  --startup-project src/Dhadgar.Files \
  --output-dir Data/Migrations

# Apply migrations
dotnet ef database update \
  --project src/Dhadgar.Files \
  --startup-project src/Dhadgar.Files

# Build only this service
dotnet build src/Dhadgar.Files
```

### Health Check Endpoints

| Endpoint | Purpose | Kubernetes Probe |
|----------|---------|------------------|
| `/healthz` | Full health status | - |
| `/livez` | Is the process alive? | livenessProbe |
| `/readyz` | Ready to serve traffic? | readinessProbe |

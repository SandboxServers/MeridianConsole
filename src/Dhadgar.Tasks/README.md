# Dhadgar.Tasks

The **Tasks service** is the orchestration and background job management engine for Meridian Console. It coordinates work distribution across the platform, dispatches commands to customer-hosted agents, tracks execution status, and provides scheduling capabilities for recurring operations.

## Table of Contents

- [Overview](#overview)
- [Current Status](#current-status)
- [Tech Stack](#tech-stack)
- [Quick Start](#quick-start)
  - [Prerequisites](#prerequisites)
  - [Running Locally](#running-locally)
  - [Verify It's Running](#verify-its-running)
  - [Access Swagger](#access-swagger)
- [Architecture](#architecture)
  - [Role in the Platform](#role-in-the-platform)
  - [Task Lifecycle](#task-lifecycle)
  - [Multi-Tenancy](#multi-tenancy)
- [Current Implementation](#current-implementation)
  - [Existing Code Structure](#existing-code-structure)
  - [Current Endpoints](#current-endpoints)
  - [Database Schema (Current)](#database-schema-current)
- [Planned Features](#planned-features)
  - [Task Creation and Management](#task-creation-and-management)
  - [Task Execution Tracking](#task-execution-tracking)
  - [Task Dependencies and Ordering](#task-dependencies-and-ordering)
  - [Recurring Tasks (Cron-like)](#recurring-tasks-cron-like)
  - [Task Templates](#task-templates)
  - [Agent Task Dispatch](#agent-task-dispatch)
- [Database Schema (Planned)](#database-schema-planned)
  - [Core Entities](#core-entities)
  - [Entity Relationships](#entity-relationships)
  - [Migration Strategy](#migration-strategy)
- [API Endpoints (Planned)](#api-endpoints-planned)
  - [Task Management](#task-management)
  - [Task Templates](#task-templates-1)
  - [Recurring Tasks](#recurring-tasks)
  - [Execution History](#execution-history)
- [Integration Points](#integration-points)
  - [Agent Integration](#agent-integration)
  - [Servers Service Integration](#servers-service-integration)
  - [Nodes Service Integration](#nodes-service-integration)
  - [Notifications Service Integration](#notifications-service-integration)
  - [Files Service Integration](#files-service-integration)
- [Message Contracts](#message-contracts)
  - [Commands (Planned)](#commands-planned)
  - [Events (Planned)](#events-planned)
  - [Message Flow Patterns](#message-flow-patterns)
- [Configuration](#configuration)
  - [Required Configuration](#required-configuration)
  - [RabbitMQ Configuration](#rabbitmq-configuration)
  - [OpenTelemetry Configuration](#opentelemetry-configuration)
  - [Task Execution Configuration (Planned)](#task-execution-configuration-planned)
- [Observability](#observability)
  - [Logging](#logging)
  - [Metrics (Planned)](#metrics-planned)
  - [Distributed Tracing](#distributed-tracing)
- [Testing](#testing)
  - [Running Tests](#running-tests)
  - [Test Factory](#test-factory)
  - [Writing New Tests](#writing-new-tests)
- [Troubleshooting](#troubleshooting)
- [Related Documentation](#related-documentation)

---

## Overview

The Tasks service is the **central nervous system** for work orchestration in Meridian Console. While individual services (Servers, Nodes, Files, etc.) manage their own domain data, the Tasks service coordinates complex operations that span multiple services and require execution on customer-hosted agents.

**Key Responsibilities:**

1. **Task Orchestration** - Coordinate multi-step operations across services
2. **Agent Dispatch** - Send commands to customer-hosted agents for execution
3. **Status Tracking** - Track task progress from creation to completion
4. **Scheduling** - Manage recurring tasks and scheduled operations
5. **Dependency Resolution** - Execute tasks in correct order based on dependencies
6. **Retry Management** - Handle failures with configurable retry policies
7. **Audit Trail** - Maintain complete history of all task executions

**Example Use Cases:**

- **Server Provisioning**: Create server record -> Download game files -> Configure server -> Start server -> Verify health
- **Scheduled Backups**: Cron-triggered backup tasks that archive server data
- **Mod Installation**: Download mod -> Stop server -> Install files -> Restart server
- **Node Maintenance**: Coordinate rolling updates across multiple nodes

---

## Current Status

> **Status: STUB SERVICE**
>
> The Tasks service currently has basic scaffolding in place. Core orchestration functionality is planned but not yet implemented.

### What Exists Today

| Component | Status | Notes |
|-----------|--------|-------|
| Service scaffold | Implemented | ASP.NET Core Minimal API with standard endpoints |
| Database context | Implemented | EF Core with placeholder `SampleEntity` |
| OpenTelemetry | Implemented | Full tracing, metrics, and logging instrumentation |
| Health checks | Implemented | `/healthz`, `/livez`, `/readyz` endpoints |
| Swagger/OpenAPI | Implemented | API documentation in Development mode |
| Gateway routing | Configured | `/api/v1/tasks/*` routes to this service |
| Test project | Implemented | Basic tests with WebApplicationFactory |

### What's Planned

| Feature | Priority | Dependencies |
|---------|----------|--------------|
| Task entity and CRUD | High | None |
| Task execution tracking | High | Task entity |
| Agent dispatch via MassTransit | High | Messaging infrastructure |
| Task status state machine | High | Task entity |
| Recurring task scheduler | Medium | Task entity, background jobs |
| Task templates | Medium | Task entity |
| Task dependencies/DAG | Medium | Task entity |
| Saga-based orchestration | Medium | MassTransit, multiple services |
| Retry policies | Medium | Task execution |
| Dead letter queue handling | Low | MassTransit |

---

## Tech Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 10.0 | Runtime framework |
| ASP.NET Core | 10.0 | Web framework (Minimal APIs) |
| Entity Framework Core | 10.0 | ORM and data access |
| PostgreSQL | 16 | Primary database |
| Npgsql | 10.0 | PostgreSQL provider for EF Core |
| MassTransit | 8.3.6 | Message bus abstraction |
| RabbitMQ | 3.x | Message broker |
| OpenTelemetry | 1.14.0 | Distributed tracing and metrics |
| Swashbuckle | 10.1.0 | OpenAPI/Swagger documentation |

**Project Dependencies:**

```xml
<ProjectReference Include="Dhadgar.Contracts" />      <!-- Shared DTOs -->
<ProjectReference Include="Dhadgar.Shared" />         <!-- Utilities -->
<ProjectReference Include="Dhadgar.Messaging" />      <!-- MassTransit config -->
<ProjectReference Include="Dhadgar.ServiceDefaults" /> <!-- Middleware, health -->
```

---

## Quick Start

### Prerequisites

- .NET SDK 10.0.100+ (pinned in `global.json`)
- Docker (for local infrastructure)
- PostgreSQL running on localhost:5432
- RabbitMQ running on localhost:5672 (optional, for messaging)

### Running Locally

```bash
# 1. Start infrastructure (from repository root)
docker compose -f deploy/compose/docker-compose.dev.yml up -d

# 2. Run the service
dotnet run --project src/Dhadgar.Tasks

# Or with hot reload
dotnet watch --project src/Dhadgar.Tasks
```

The service runs on `http://localhost:5050` by default.

### Verify It's Running

```bash
# Health check
curl http://localhost:5050/healthz

# Response:
{
  "service": "Dhadgar.Tasks",
  "status": "ok",
  "timestamp": "2026-01-22T12:00:00Z",
  "checks": {
    "self": {
      "status": "Healthy",
      "duration_ms": 0.5
    }
  }
}

# Hello endpoint
curl http://localhost:5050/hello
# Returns: Hello from Dhadgar.Tasks

# Root endpoint
curl http://localhost:5050/
# Returns: {"service":"Dhadgar.Tasks","message":"Hello from Dhadgar.Tasks"}
```

### Access Swagger

Open http://localhost:5050/swagger in your browser (Development mode only).

---

## Architecture

### Role in the Platform

The Tasks service occupies a critical position in the Meridian Console architecture:

```
                                    +-------------------+
                                    |      Panel        |
                                    |   (Frontend UI)   |
                                    +--------+----------+
                                             |
                                             v
+------------------+            +------------+------------+
|    Cloudflare    | ---------> |        Gateway         |
|   (Edge/WAF)     |            |   (YARP Reverse Proxy) |
+------------------+            +------------+------------+
                                             |
                    +------------------------+------------------------+
                    |                        |                        |
                    v                        v                        v
           +-------+-------+        +-------+-------+        +-------+-------+
           |    Identity   |        |    Servers    |        |     Nodes     |
           |   (Auth/IAM)  |        | (Game Servers)|        |  (Hardware)   |
           +---------------+        +-------+-------+        +-------+-------+
                                            |                        |
                                            v                        v
                                    +-------+------------------------+-------+
                                    |                                        |
                                    |          T A S K S   S E R V I C E     |
                                    |                                        |
                                    | - Receives orchestration requests      |
                                    | - Creates task execution plans         |
                                    | - Dispatches commands to agents        |
                                    | - Tracks progress and status           |
                                    | - Handles retries and failures         |
                                    |                                        |
                                    +-------------------+--------------------+
                                                        |
                                                        | (RabbitMQ)
                                                        v
                                    +-------------------+--------------------+
                                    |                                        |
                                    |     Customer-Hosted Agents             |
                                    |                                        |
                                    | - Execute tasks on actual hardware     |
                                    | - Report status back                   |
                                    | - Stream logs and metrics              |
                                    |                                        |
                                    +----------------------------------------+
```

### Task Lifecycle

A task progresses through the following states:

```
  [Created] -----> [Pending] -----> [Scheduled] -----> [Running] -----> [Completed]
      |               |                  |                 |                |
      |               v                  v                 v                v
      +----------> [Cancelled] <--- [Timeout] <------ [Failed] ----> [Retrying]
                                                          |               |
                                                          +---------------+
```

**State Definitions:**

| State | Description |
|-------|-------------|
| `Created` | Task record created, awaiting validation |
| `Pending` | Validated and ready for scheduling |
| `Scheduled` | Assigned to a node/agent, waiting to start |
| `Running` | Currently executing |
| `Completed` | Successfully finished |
| `Failed` | Execution failed (may retry) |
| `Retrying` | Retry attempt in progress |
| `Timeout` | Exceeded maximum execution time |
| `Cancelled` | Manually cancelled by user |

### Multi-Tenancy

The Tasks service is **organization-scoped**:

- All tasks belong to an organization (`org_id`)
- Tasks can only target resources (servers, nodes) within the same organization
- Task execution history is isolated per organization
- API requests are authorized via JWT with `org_id` claim

The Gateway enforces `TenantScoped` authorization policy on all `/api/v1/tasks/*` routes.

---

## Current Implementation

### Existing Code Structure

```
src/Dhadgar.Tasks/
|-- Program.cs                    # Application entry point, DI setup
|-- Hello.cs                      # Hello world constant for smoke tests
|-- appsettings.json              # Service configuration
|-- Dhadgar.Tasks.csproj          # Project file
|-- Data/
    |-- TasksDbContext.cs         # EF Core DbContext (placeholder entities)
    |-- TasksDbContextFactory.cs  # Design-time factory for migrations
```

### Current Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/` | Anonymous | Service info (name, message) |
| GET | `/hello` | Anonymous | Returns "Hello from Dhadgar.Tasks" |
| GET | `/healthz` | Anonymous | Overall health status |
| GET | `/livez` | Anonymous | Liveness probe (Kubernetes) |
| GET | `/readyz` | Anonymous | Readiness probe (Kubernetes) |

All endpoints are documented in Swagger when running in Development mode.

### Database Schema (Current)

The current implementation has a **placeholder entity** that will be replaced:

```csharp
public sealed class SampleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "hello";
}
```

**DbContext:**

```csharp
public sealed class TasksDbContext : DbContext
{
    public DbSet<SampleEntity> Sample => Set<SampleEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SampleEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200);
        });
    }
}
```

**Note:** No migrations have been created yet. The first real migration will replace `SampleEntity` with actual task entities.

---

## Planned Features

### Task Creation and Management

**Task Types** (planned):

| Type | Description | Example |
|------|-------------|---------|
| `ServerStart` | Start a game server | Start Minecraft server #123 |
| `ServerStop` | Stop a game server | Graceful shutdown |
| `ServerRestart` | Stop then start | Rolling restart |
| `ServerUpdate` | Update game server files | Download new version |
| `FileTransfer` | Transfer files to/from agent | Upload config |
| `ModInstall` | Install/update a mod | Install plugin |
| `Backup` | Create backup archive | Snapshot world data |
| `Restore` | Restore from backup | Restore to checkpoint |
| `Command` | Execute arbitrary command | Run console command |
| `HealthCheck` | Verify server health | Ping server |
| `Custom` | User-defined script | Custom maintenance |

**Task Creation Request** (planned):

```json
POST /api/v1/tasks
{
  "type": "ServerStart",
  "targetType": "Server",
  "targetId": "123e4567-e89b-12d3-a456-426614174000",
  "priority": "Normal",
  "scheduledAt": null,
  "payload": {
    "gracefulShutdown": true,
    "timeoutSeconds": 300
  },
  "metadata": {
    "initiatedBy": "user-action"
  }
}
```

### Task Execution Tracking

Each task execution creates an **execution record** with:

- Start/end timestamps
- Exit code
- Output logs (truncated)
- Error messages
- Agent that executed it
- Retry count

**Execution Query** (planned):

```json
GET /api/v1/tasks/{taskId}/executions

{
  "items": [
    {
      "executionId": "exec-001",
      "taskId": "task-123",
      "agentId": "agent-abc",
      "nodeId": "node-xyz",
      "status": "Completed",
      "startedAt": "2026-01-22T12:00:00Z",
      "completedAt": "2026-01-22T12:00:30Z",
      "durationMs": 30000,
      "exitCode": 0,
      "output": "[truncated]",
      "retryNumber": 0
    }
  ],
  "page": 1,
  "limit": 50,
  "total": 1
}
```

### Task Dependencies and Ordering

Tasks can depend on other tasks, forming a **Directed Acyclic Graph (DAG)**:

```json
POST /api/v1/tasks/batch
{
  "tasks": [
    {
      "name": "stop-server",
      "type": "ServerStop",
      "targetId": "server-123"
    },
    {
      "name": "backup-world",
      "type": "Backup",
      "targetId": "server-123",
      "dependsOn": ["stop-server"]
    },
    {
      "name": "update-files",
      "type": "ServerUpdate",
      "targetId": "server-123",
      "dependsOn": ["backup-world"]
    },
    {
      "name": "start-server",
      "type": "ServerStart",
      "targetId": "server-123",
      "dependsOn": ["update-files"]
    }
  ]
}
```

**Execution Order:** stop-server -> backup-world -> update-files -> start-server

### Recurring Tasks (Cron-like)

Scheduled tasks using cron expressions:

```json
POST /api/v1/tasks/recurring
{
  "name": "nightly-backup",
  "type": "Backup",
  "targetType": "Server",
  "targetId": "server-123",
  "schedule": "0 3 * * *",
  "timezone": "America/New_York",
  "enabled": true,
  "payload": {
    "retentionDays": 7
  }
}
```

**Supported Schedules:**

| Pattern | Description |
|---------|-------------|
| `0 * * * *` | Every hour at minute 0 |
| `0 3 * * *` | Daily at 3:00 AM |
| `0 0 * * 0` | Weekly on Sunday at midnight |
| `0 0 1 * *` | Monthly on the 1st at midnight |

### Task Templates

Reusable task definitions:

```json
POST /api/v1/tasks/templates
{
  "name": "rolling-restart",
  "description": "Graceful restart with backup",
  "tasks": [
    {
      "name": "backup",
      "type": "Backup",
      "order": 1
    },
    {
      "name": "stop",
      "type": "ServerStop",
      "order": 2,
      "payload": { "gracefulShutdown": true }
    },
    {
      "name": "start",
      "type": "ServerStart",
      "order": 3,
      "dependsOn": ["stop"]
    }
  ]
}
```

**Execute Template:**

```json
POST /api/v1/tasks/templates/{templateId}/execute
{
  "targetId": "server-123",
  "overrides": {
    "backup.payload.retentionDays": 14
  }
}
```

### Agent Task Dispatch

Tasks are dispatched to agents via MassTransit:

```
Tasks Service                    RabbitMQ                    Agent
     |                              |                           |
     | -- Publish TaskAssigned ---> |                           |
     |                              | -- Consume TaskAssigned ->|
     |                              |                           |
     |                              | <-- Publish TaskStarted --|
     | <-- Consume TaskStarted ---- |                           |
     |                              |                           |
     |                              | <-- Publish TaskProgress -|
     | <-- Consume TaskProgress --- |                           |
     |                              |                           |
     |                              | <-- Publish TaskCompleted-|
     | <-- Consume TaskCompleted -- |                           |
```

---

## Database Schema (Planned)

### Core Entities

**Task Entity:**

```csharp
public class Task
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }

    // Task definition
    public string Type { get; set; }           // ServerStart, Backup, etc.
    public string Name { get; set; }           // Human-readable name
    public string? Description { get; set; }
    public TaskPriority Priority { get; set; } // Low, Normal, High, Critical

    // Target
    public string TargetType { get; set; }     // Server, Node, etc.
    public Guid TargetId { get; set; }

    // Scheduling
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    // Status
    public TaskStatus Status { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }

    // Payload (JSON)
    public string? PayloadJson { get; set; }
    public string? MetadataJson { get; set; }

    // Audit
    public Guid CreatedByUserId { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }

    // Dependencies
    public Guid? ParentTaskId { get; set; }
    public Guid? BatchId { get; set; }

    // Navigation
    public ICollection<TaskExecution> Executions { get; set; }
    public ICollection<TaskDependency> Dependencies { get; set; }
}
```

**TaskExecution Entity:**

```csharp
public class TaskExecution
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }

    // Execution context
    public Guid AgentId { get; set; }
    public Guid NodeId { get; set; }
    public int AttemptNumber { get; set; }

    // Timing
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? DurationMs { get; set; }

    // Result
    public TaskExecutionStatus Status { get; set; }
    public int? ExitCode { get; set; }
    public string? Output { get; set; }        // Truncated stdout
    public string? ErrorOutput { get; set; }   // Truncated stderr
    public string? ErrorMessage { get; set; }  // Exception message

    // Navigation
    public Task Task { get; set; }
}
```

**RecurringTask Entity:**

```csharp
public class RecurringTask
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }

    // Definition
    public string Name { get; set; }
    public string Type { get; set; }
    public string TargetType { get; set; }
    public Guid TargetId { get; set; }

    // Schedule
    public string CronExpression { get; set; }
    public string Timezone { get; set; }
    public bool Enabled { get; set; }

    // Payload
    public string? PayloadJson { get; set; }

    // Tracking
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public Guid? LastTaskId { get; set; }

    // Audit
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
}
```

**TaskTemplate Entity:**

```csharp
public class TaskTemplate
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }

    // Definition
    public string Name { get; set; }
    public string? Description { get; set; }
    public bool IsGlobal { get; set; }  // Available to all orgs

    // Template data (JSON array of task definitions)
    public string TaskDefinitionsJson { get; set; }

    // Audit
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
}
```

**TaskDependency Entity:**

```csharp
public class TaskDependency
{
    public Guid TaskId { get; set; }
    public Guid DependsOnTaskId { get; set; }

    // Navigation
    public Task Task { get; set; }
    public Task DependsOnTask { get; set; }
}
```

### Entity Relationships

```
                          +---------------+
                          | Organization  |
                          +-------+-------+
                                  |
       +--------------------------+------------------------+
       |                          |                        |
       v                          v                        v
+------+------+          +--------+-------+       +--------+-------+
|    Task     |          | RecurringTask  |       | TaskTemplate   |
+------+------+          +----------------+       +----------------+
       |
       +-------------------+
       |                   |
       v                   v
+------+------+    +-------+--------+
|TaskExecution|    | TaskDependency |
+-------------+    +----------------+
```

### Migration Strategy

1. **First Migration**: Create `Tasks`, `TaskExecutions` tables
2. **Second Migration**: Add `RecurringTasks` table
3. **Third Migration**: Add `TaskTemplates` table
4. **Fourth Migration**: Add `TaskDependencies` junction table
5. **Fifth Migration**: Remove placeholder `SampleEntity`

**Commands:**

```bash
# Create first migration
dotnet ef migrations add CreateTaskEntities \
  --project src/Dhadgar.Tasks \
  --startup-project src/Dhadgar.Tasks \
  --output-dir Data/Migrations

# Apply migrations
dotnet ef database update \
  --project src/Dhadgar.Tasks \
  --startup-project src/Dhadgar.Tasks
```

---

## API Endpoints (Planned)

### Task Management

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/tasks` | Create a new task |
| `GET` | `/tasks` | List tasks (paginated, filtered) |
| `GET` | `/tasks/{id}` | Get task details |
| `DELETE` | `/tasks/{id}` | Cancel a task |
| `POST` | `/tasks/{id}/retry` | Retry a failed task |
| `POST` | `/tasks/batch` | Create batch of tasks with dependencies |

**Query Parameters for List:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `page` | int | Page number (1-based) |
| `limit` | int | Items per page (default 50, max 100) |
| `status` | string | Filter by status |
| `type` | string | Filter by task type |
| `targetId` | Guid | Filter by target |
| `sort` | string | Sort field (createdAt, status) |
| `order` | string | Sort order (asc, desc) |

### Task Templates

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/tasks/templates` | Create template |
| `GET` | `/tasks/templates` | List templates |
| `GET` | `/tasks/templates/{id}` | Get template |
| `PATCH` | `/tasks/templates/{id}` | Update template |
| `DELETE` | `/tasks/templates/{id}` | Delete template |
| `POST` | `/tasks/templates/{id}/execute` | Execute template |

### Recurring Tasks

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/tasks/recurring` | Create recurring task |
| `GET` | `/tasks/recurring` | List recurring tasks |
| `GET` | `/tasks/recurring/{id}` | Get recurring task |
| `PATCH` | `/tasks/recurring/{id}` | Update recurring task |
| `DELETE` | `/tasks/recurring/{id}` | Delete recurring task |
| `POST` | `/tasks/recurring/{id}/trigger` | Manually trigger |

### Execution History

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/tasks/{id}/executions` | Get execution history |
| `GET` | `/tasks/executions/{id}` | Get specific execution |
| `GET` | `/tasks/executions/{id}/logs` | Stream execution logs |

---

## Integration Points

### Agent Integration

Agents receive task assignments via RabbitMQ and report status back:

**Outbound (Tasks -> Agent):**

| Message | Purpose |
|---------|---------|
| `TaskAssigned` | Dispatch task to specific agent |
| `TaskCancelled` | Cancel running task |

**Inbound (Agent -> Tasks):**

| Message | Purpose |
|---------|---------|
| `TaskAccepted` | Agent acknowledged task |
| `TaskStarted` | Execution began |
| `TaskProgress` | Progress update (percentage, logs) |
| `TaskCompleted` | Successful completion |
| `TaskFailed` | Execution failed |

### Servers Service Integration

The Servers service creates tasks for server operations:

```
User Request -> Servers Service -> POST /tasks -> Tasks Service
                     ^                                  |
                     |                                  v
                     +------ Task Status Updates ------+
```

**Common Interactions:**

- Server start/stop/restart triggers task creation
- Server provisioning creates multi-step task batch
- Tasks service updates server status based on execution results

### Nodes Service Integration

Nodes service provides agent/node information:

```
Tasks Service ---> GET /api/v1/nodes/{nodeId} ---> Nodes Service
                                                        |
                                                        v
                                               Node health, capacity,
                                               agent connection status
```

**Node Selection:** Tasks service queries Nodes to find healthy agents for task dispatch.

### Notifications Service Integration

Task status changes trigger notifications:

```
Task Completed/Failed -> Tasks Service -> Publish TaskNotification -> Notifications Service
                                                                            |
                                                                            v
                                                                    Email, Discord, Webhook
```

### Files Service Integration

File transfer tasks coordinate with Files service:

```
FileTransfer Task -> Tasks Service -> Request presigned URL -> Files Service
                          |                                         |
                          v                                         v
                    Dispatch to Agent <----- Upload/Download ------ S3/Storage
```

---

## Message Contracts

### Commands (Planned)

```csharp
namespace Dhadgar.Contracts.Tasks;

/// <summary>
/// Command to create a new task.
/// </summary>
public record CreateTask(
    Guid TaskId,
    Guid OrgId,
    string Type,
    string TargetType,
    Guid TargetId,
    string? PayloadJson,
    TaskPriority Priority,
    DateTimeOffset? ScheduledAt);

/// <summary>
/// Command to cancel a task.
/// </summary>
public record CancelTask(
    Guid TaskId,
    Guid CancelledByUserId,
    string? Reason);

/// <summary>
/// Command to retry a failed task.
/// </summary>
public record RetryTask(
    Guid TaskId,
    Guid RetriedByUserId);

/// <summary>
/// Command dispatched to an agent to execute a task.
/// </summary>
public record ExecuteTask(
    Guid TaskId,
    Guid ExecutionId,
    string Type,
    string TargetType,
    Guid TargetId,
    string? PayloadJson,
    int TimeoutSeconds);
```

### Events (Planned)

```csharp
namespace Dhadgar.Contracts.Tasks;

/// <summary>
/// Event published when a task is created.
/// </summary>
public record TaskCreated(
    Guid TaskId,
    Guid OrgId,
    string Type,
    Guid TargetId,
    DateTimeOffset CreatedAt);

/// <summary>
/// Event published when task execution starts.
/// </summary>
public record TaskStarted(
    Guid TaskId,
    Guid ExecutionId,
    Guid AgentId,
    DateTimeOffset StartedAt);

/// <summary>
/// Event published for task progress updates.
/// </summary>
public record TaskProgressUpdated(
    Guid TaskId,
    Guid ExecutionId,
    int ProgressPercent,
    string? StatusMessage,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Event published when a task completes successfully.
/// </summary>
public record TaskCompleted(
    Guid TaskId,
    Guid ExecutionId,
    int ExitCode,
    int DurationMs,
    DateTimeOffset CompletedAt);

/// <summary>
/// Event published when a task fails.
/// </summary>
public record TaskFailed(
    Guid TaskId,
    Guid ExecutionId,
    string ErrorMessage,
    int? ExitCode,
    bool WillRetry,
    DateTimeOffset FailedAt);

/// <summary>
/// Event published when a task is cancelled.
/// </summary>
public record TaskCancelled(
    Guid TaskId,
    Guid CancelledByUserId,
    string? Reason,
    DateTimeOffset CancelledAt);
```

### Message Flow Patterns

**Simple Task Execution:**

```
1. Servers Service publishes CreateTask command
2. Tasks Service creates task record, publishes TaskCreated event
3. Tasks Service selects agent, publishes ExecuteTask command
4. Agent publishes TaskStarted event
5. Agent publishes TaskProgress events (optional)
6. Agent publishes TaskCompleted or TaskFailed event
7. Tasks Service updates task record
8. Tasks Service publishes notification if configured
```

**Task with Dependencies (Saga Pattern):**

```
1. User creates batch with dependencies
2. Tasks Service creates TaskBatch saga state
3. Saga publishes ExecuteTask for first task(s) with no dependencies
4. On TaskCompleted, saga checks if dependent tasks can start
5. Saga continues until all tasks complete or failure
6. On any failure, saga can trigger rollback tasks
```

---

## Configuration

### Required Configuration

**File: `appsettings.json`**

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

| Key | Description | Default |
|-----|-------------|---------|
| `ConnectionStrings:Postgres` | PostgreSQL connection string | localhost:5432 |
| `ConnectionStrings:RabbitMqHost` | RabbitMQ host | localhost |
| `RabbitMq:Username` | RabbitMQ username | dhadgar |
| `RabbitMq:Password` | RabbitMQ password | dhadgar |

### RabbitMQ Configuration

The Messaging library (`Dhadgar.Messaging`) provides MassTransit configuration:

```csharp
// In Program.cs (planned)
builder.Services.AddDhadgarMessaging(builder.Configuration, x =>
{
    // Register consumers
    x.AddConsumer<TaskCompletedConsumer>();
    x.AddConsumer<TaskFailedConsumer>();

    // Register sagas (planned)
    x.AddSagaStateMachine<TaskBatchStateMachine, TaskBatchSagaState>();
});
```

**Exchange Naming:** Messages use `meridian.{messagename}` format (e.g., `meridian.taskcreated`).

### OpenTelemetry Configuration

Enable OTLP export for tracing and metrics:

```bash
# Set via user secrets
dotnet user-secrets init --project src/Dhadgar.Tasks
dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317"
```

Or in `appsettings.Development.json`:

```json
{
  "OpenTelemetry": {
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

### Task Execution Configuration (Planned)

```json
{
  "Tasks": {
    "DefaultTimeoutSeconds": 300,
    "MaxRetries": 3,
    "RetryBackoffSeconds": [5, 30, 120],
    "MaxConcurrentTasksPerNode": 10,
    "ExecutionLogMaxBytes": 65536,
    "CleanupRetentionDays": 30
  }
}
```

| Key | Description | Default |
|-----|-------------|---------|
| `DefaultTimeoutSeconds` | Default task timeout | 300 |
| `MaxRetries` | Maximum retry attempts | 3 |
| `RetryBackoffSeconds` | Delay between retries | [5, 30, 120] |
| `MaxConcurrentTasksPerNode` | Concurrent task limit per node | 10 |
| `ExecutionLogMaxBytes` | Max output to store | 65536 |
| `CleanupRetentionDays` | Days to retain completed tasks | 30 |

---

## Observability

### Logging

The service uses structured logging with correlation context:

```csharp
logger.LogInformation(
    "Task {TaskId} dispatched to agent {AgentId} on node {NodeId}",
    taskId, agentId, nodeId);
```

**Log Levels:**

| Level | Usage |
|-------|-------|
| `Information` | Task lifecycle events |
| `Warning` | Retries, timeouts, recoverable errors |
| `Error` | Failed tasks, unhandled exceptions |
| `Debug` | Detailed execution flow |

### Metrics (Planned)

| Metric | Type | Description |
|--------|------|-------------|
| `tasks.created` | Counter | Tasks created |
| `tasks.completed` | Counter | Tasks completed successfully |
| `tasks.failed` | Counter | Tasks failed |
| `tasks.retried` | Counter | Tasks retried |
| `tasks.cancelled` | Counter | Tasks cancelled |
| `tasks.duration_ms` | Histogram | Task execution duration |
| `tasks.queue_depth` | Gauge | Tasks in pending state |
| `tasks.executing` | Gauge | Tasks currently running |

### Distributed Tracing

Traces include:

- HTTP request handling
- Database queries
- RabbitMQ message publish/consume
- External HTTP calls

Correlation IDs propagate via:
- `X-Correlation-Id` header
- `traceparent` header (W3C)
- MassTransit message headers

---

## Testing

### Running Tests

```bash
# Run all Tasks service tests
dotnet test tests/Dhadgar.Tasks.Tests

# Run specific test
dotnet test tests/Dhadgar.Tasks.Tests --filter "FullyQualifiedName~HelloWorldTests"

# With verbose output
dotnet test tests/Dhadgar.Tasks.Tests -v n
```

### Test Factory

The test project includes `TasksWebApplicationFactory` for integration testing:

```csharp
public class TasksWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace PostgreSQL with in-memory database
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<TasksDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<TasksDbContext>(options =>
            {
                options.UseInMemoryDatabase("TasksTestDb");
            });
        });
    }
}
```

### Writing New Tests

**Unit Test Example:**

```csharp
public class TaskServiceTests
{
    [Fact]
    public async Task CreateTask_WithValidInput_CreatesTaskRecord()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new TasksDbContext(options);
        var service = new TaskService(context, _loggerMock.Object);

        // Act
        var result = await service.CreateTaskAsync(new CreateTaskRequest
        {
            Type = "ServerStart",
            TargetId = Guid.NewGuid()
        });

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(TaskStatus.Pending);
    }
}
```

**Integration Test Example:**

```csharp
public class TasksApiTests : IClassFixture<TasksWebApplicationFactory>
{
    private readonly HttpClient _client;

    public TasksApiTests(TasksWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetTasks_ReturnsEmptyList_WhenNoTasks()
    {
        var response = await _client.GetAsync("/tasks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<PagedResponse<TaskDto>>();
        content.Items.Should().BeEmpty();
    }
}
```

---

## Troubleshooting

### Service Not Starting

**Symptom:** Service fails to start with database connection error.

**Solution:**
1. Verify PostgreSQL is running: `docker ps | grep postgres`
2. Check connection string in `appsettings.json`
3. Ensure database exists (auto-created in Development mode)

### Migrations Not Applying

**Symptom:** Schema changes not reflected in database.

**Solution:**
1. Check if `ASPNETCORE_ENVIRONMENT=Development` (auto-migrate only in dev)
2. Manually run migrations:
   ```bash
   dotnet ef database update \
     --project src/Dhadgar.Tasks \
     --startup-project src/Dhadgar.Tasks
   ```

### Tasks Stuck in Pending

**Symptom:** Tasks created but never executed.

**Solution (Planned):**
1. Check if target node has healthy agent
2. Verify RabbitMQ connection
3. Check agent logs for connection issues
4. Verify task type is supported

### OpenTelemetry Not Exporting

**Symptom:** No traces in Grafana/Jaeger.

**Solution:**
1. Set OTLP endpoint:
   ```bash
   dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317"
   ```
2. Verify OTLP collector is running: `docker ps | grep otel`
3. Check service logs for OTLP export warnings

### RabbitMQ Connection Failed

**Symptom:** Service can't connect to RabbitMQ.

**Solution:**
1. Verify RabbitMQ is running: `docker ps | grep rabbitmq`
2. Check credentials in `appsettings.json`
3. Access RabbitMQ management UI at http://localhost:15672 (dhadgar/dhadgar)
4. Check for `Connection refused` in logs

---

## Related Documentation

**Service Documentation:**

- [Root CLAUDE.md](/CLAUDE.md) - Overall project guidance
- [Gateway README](/src/Dhadgar.Gateway/README.md) - API Gateway documentation
- [Identity README](/src/Dhadgar.Identity/README.md) - Authentication service

**Shared Libraries:**

- [ServiceDefaults](/src/Shared/Dhadgar.ServiceDefaults/) - Middleware and observability
- [Contracts](/src/Shared/Dhadgar.Contracts/) - Shared DTOs and message contracts
- [Messaging](/src/Shared/Dhadgar.Messaging/) - MassTransit configuration

**Infrastructure:**

- [Docker Compose README](/deploy/compose/README.md) - Local development infrastructure
- [Container Build Setup](/deploy/kubernetes/CONTAINER-BUILD-SETUP.md) - CI/CD pipeline

**Architecture:**

- [Agent Core](/src/Agents/Dhadgar.Agent.Core/) - Customer-hosted agent (task executor)
- [Nodes Service](/src/Dhadgar.Nodes/) - Node and agent management
- [Servers Service](/src/Dhadgar.Servers/) - Game server management

**External Resources:**

- [MassTransit Documentation](https://masstransit-project.com/usage/messages.html) - Message patterns
- [EF Core Documentation](https://learn.microsoft.com/en-us/ef/core/) - Data access
- [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/) - Observability

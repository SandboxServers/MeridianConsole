# Dhadgar.Notifications Service

The **Notifications** service is the centralized notification delivery system for Meridian Console. It provides multi-channel notification capabilities including email, Discord webhooks, and custom webhook integrations. This service acts as the single point of notification dispatch, receiving notification requests from other services via MassTransit messaging and delivering them through configured channels based on user preferences.

## Table of Contents

- [Overview](#overview)
- [Current Status](#current-status)
- [Architecture](#architecture)
- [Technology Stack](#technology-stack)
- [Quick Start](#quick-start)
- [API Endpoints](#api-endpoints)
- [Database Schema](#database-schema)
- [Configuration](#configuration)
- [Integration Points](#integration-points)
- [Planned Features](#planned-features)
- [Testing](#testing)
- [Development Guidelines](#development-guidelines)
- [Related Documentation](#related-documentation)

---

## Overview

### Purpose

The Notifications service is responsible for:

1. **Multi-Channel Delivery**: Send notifications via email (SMTP/SendGrid), Discord (webhooks/bot messages), and custom webhooks
2. **User Preference Management**: Allow users to configure notification preferences per channel and per notification type
3. **Template Management**: Maintain notification templates for consistent, branded messaging
4. **Delivery Tracking**: Track notification delivery status, failures, and retry attempts
5. **Alert Rules**: Trigger notifications based on system events (server crashes, node health, billing alerts)
6. **Notification History**: Maintain a queryable history of all sent notifications for audit and troubleshooting

### Use Cases

The Notifications service handles notifications for events across the entire Meridian Console platform:

| Source Service | Example Events |
|----------------|----------------|
| **Identity** | User invited to organization, invitation accepted/rejected/expired, account linked/unlinked, password changed, email verified |
| **Servers** | Server started, stopped, crashed, backup completed, update available |
| **Nodes** | Node offline, node health degraded, capacity threshold reached, agent disconnected |
| **Billing** | Invoice generated, payment failed, subscription renewed, plan limits approaching |
| **Tasks** | Task completed, task failed, scheduled maintenance reminder |
| **Files** | Large file upload complete, backup completed, storage quota warning |
| **Mods** | Mod update available, mod installed, mod installation failed |
| **Firewall** | Suspicious traffic detected, rule triggered, port scan blocked |
| **Secrets** | Secret rotation reminder, secret accessed, secret rotation completed |

---

## Current Status

**Status**: STUB - Basic scaffolding with core functionality planned

### What Exists Today

The service currently has the foundational scaffolding in place:

| Component | Status | Description |
|-----------|--------|-------------|
| **ASP.NET Core Host** | Implemented | Minimal API with standard service structure |
| **Health Endpoints** | Implemented | `/healthz`, `/livez`, `/readyz` with structured JSON responses |
| **Swagger/OpenAPI** | Implemented | Interactive API documentation at `/swagger` |
| **EF Core DbContext** | Scaffolded | `NotificationsDbContext` with placeholder `SampleEntity` |
| **OpenTelemetry** | Implemented | Tracing, metrics, and logging with OTLP export support |
| **Middleware Stack** | Implemented | Correlation tracking, problem details, request logging |
| **MassTransit Reference** | Configured | Package references ready, consumers not yet implemented |
| **Gateway Integration** | Configured | Route at `/api/v1/notifications/{**catch-all}` with `TenantScoped` policy |

### Current Endpoints

```http
GET  /                    # Service info and status
GET  /hello               # Simple health check message
GET  /healthz             # Detailed health check with all checks
GET  /livez               # Kubernetes liveness probe
GET  /readyz              # Kubernetes readiness probe
GET  /swagger             # OpenAPI documentation (Development mode)
```

### What Is NOT Yet Implemented

- Real database entities (notifications, templates, preferences, channels)
- Notification delivery logic (email, Discord, webhook)
- MassTransit consumers for notification events
- User preference management
- Template rendering engine
- Delivery tracking and retry logic
- Alert rule evaluation
- Notification history API

---

## Architecture

### Service Position in Platform

```
                                    ┌─────────────────────────────────────────────────────────────┐
                                    │                      Control Plane                          │
                                    │                                                             │
┌──────────────┐                    │  ┌──────────┐   ┌──────────┐   ┌──────────┐               │
│   Identity   │────Events──────────┼─▶│          │   │          │   │          │               │
└──────────────┘                    │  │          │   │  Email   │   │          │               │
┌──────────────┐                    │  │          │──▶│  (SMTP/  │──▶│  User    │               │
│   Servers    │────Events──────────┼─▶│ Notifi-  │   │ SendGrid)│   │  Inbox   │               │
└──────────────┘                    │  │ cations  │   └──────────┘   │          │               │
┌──────────────┐                    │  │ Service  │   ┌──────────┐   │          │               │
│    Nodes     │────Events──────────┼─▶│          │──▶│ Discord  │──▶│ Discord  │               │
└──────────────┘                    │  │          │   │ Webhook  │   │ Channel  │               │
┌──────────────┐                    │  │          │   └──────────┘   │          │               │
│   Billing    │────Events──────────┼─▶│          │   ┌──────────┐   │          │               │
└──────────────┘                    │  │          │──▶│ Custom   │──▶│ External │               │
┌──────────────┐                    │  │          │   │ Webhook  │   │ Systems  │               │
│    Tasks     │────Events──────────┼─▶│          │   └──────────┘   │          │               │
└──────────────┘                    │  └──────────┘                   └──────────┘               │
                                    │       │                                                    │
                                    │       │                                                    │
                                    │       ▼                                                    │
                                    │  ┌──────────┐                                             │
                                    │  │PostgreSQL│  Notification history, templates, prefs     │
                                    │  └──────────┘                                             │
                                    └─────────────────────────────────────────────────────────────┘
```

### Communication Patterns

**Inbound (from other services)**:
- **MassTransit Events**: Services publish domain events (e.g., `UserAuthenticated`, `OrgMembershipChanged`) that the Notifications service consumes
- **HTTP API**: Direct API calls for managing templates, preferences, and querying notification history

**Outbound (to delivery channels)**:
- **SMTP/SendGrid**: Email delivery via configured email provider
- **Discord Webhooks**: Discord channel notifications via webhook URLs
- **Custom Webhooks**: HTTP POST to user-configured webhook endpoints

---

## Technology Stack

### Core Framework

| Technology | Version | Purpose |
|------------|---------|---------|
| **.NET** | 10.0 | Runtime and SDK |
| **ASP.NET Core** | 10.0 | Web framework with Minimal API |
| **C#** | Latest | Primary language with nullable reference types enabled |

### Data Layer

| Technology | Version | Purpose |
|------------|---------|---------|
| **Entity Framework Core** | 10.0 | ORM for PostgreSQL |
| **Npgsql** | 10.0 | PostgreSQL provider for EF Core |
| **PostgreSQL** | 15+ | Primary database |

### Messaging

| Technology | Version | Purpose |
|------------|---------|---------|
| **MassTransit** | 8.3.6 | Message bus abstraction |
| **RabbitMQ** | Latest | Message broker |

### Observability

| Technology | Version | Purpose |
|------------|---------|---------|
| **OpenTelemetry** | 1.14.0 | Distributed tracing and metrics |
| **OTLP Exporter** | 1.14.0 | Export to observability backends |
| **Swashbuckle** | 10.1.0 | OpenAPI/Swagger documentation |

### Project References

The service depends on these shared libraries (per microservices architecture rules):

```xml
<ProjectReference Include="..\Shared\Dhadgar.Contracts\Dhadgar.Contracts.csproj" />
<ProjectReference Include="..\Shared\Dhadgar.Shared\Dhadgar.Shared.csproj" />
<ProjectReference Include="..\Shared\Dhadgar.Messaging\Dhadgar.Messaging.csproj" />
<ProjectReference Include="..\Shared\Dhadgar.ServiceDefaults\Dhadgar.ServiceDefaults.csproj" />
```

---

## Quick Start

### Prerequisites

1. **.NET 10 SDK** - Pinned in `global.json`
2. **Docker** - For local infrastructure
3. **PostgreSQL** - Via Docker Compose or standalone

### 1. Start Local Infrastructure

```bash
# From repository root
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

This starts:
- **PostgreSQL** on port 5432 (credentials: `dhadgar`/`dhadgar`)
- **RabbitMQ** on ports 5672 (AMQP) and 15672 (Management UI)
- **Redis** on port 6379
- **Observability stack** (Grafana, Prometheus, Loki, OTLP Collector)

### 2. Build the Service

```bash
# Build just the Notifications service
dotnet build src/Dhadgar.Notifications

# Or build the entire solution
dotnet build
```

### 3. Run the Service

```bash
# Run directly
dotnet run --project src/Dhadgar.Notifications

# Run with hot reload (recommended for development)
dotnet watch --project src/Dhadgar.Notifications
```

The service starts on **port 5090** by default.

### 4. Verify the Service

```bash
# Check service info
curl http://localhost:5090/

# Expected response:
# {"service":"Dhadgar.Notifications","message":"Hello from Dhadgar.Notifications"}

# Check health
curl http://localhost:5090/healthz

# Expected response:
# {"service":"Dhadgar.Notifications","status":"ok","timestamp":"2026-01-22T...","checks":{...}}
```

### 5. Access Swagger UI

Open http://localhost:5090/swagger in your browser to explore the API documentation.

### Via Gateway

When running the full platform, access the Notifications service through the Gateway:

```bash
# Gateway route (requires Gateway running on port 5000)
curl http://localhost:5000/api/v1/notifications/healthz
```

---

## API Endpoints

### Currently Implemented

| Method | Path | Description | Auth Required |
|--------|------|-------------|---------------|
| `GET` | `/` | Service info banner | No |
| `GET` | `/hello` | Simple hello world | No |
| `GET` | `/healthz` | Full health check | No |
| `GET` | `/livez` | Kubernetes liveness | No |
| `GET` | `/readyz` | Kubernetes readiness | No |
| `GET` | `/swagger` | OpenAPI UI (dev only) | No |

### Planned API Endpoints

#### Notification Management

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/notifications` | Send a notification (internal use) |
| `GET` | `/notifications` | List notification history |
| `GET` | `/notifications/{id}` | Get notification details |
| `POST` | `/notifications/{id}/retry` | Retry failed notification |

#### Templates

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/templates` | List all templates |
| `GET` | `/templates/{id}` | Get template by ID |
| `POST` | `/templates` | Create template |
| `PUT` | `/templates/{id}` | Update template |
| `DELETE` | `/templates/{id}` | Delete template |
| `POST` | `/templates/{id}/preview` | Preview rendered template |

#### Preferences

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/preferences` | Get current user's preferences |
| `PUT` | `/preferences` | Update preferences |
| `GET` | `/preferences/channels` | List available channels |
| `GET` | `/preferences/notification-types` | List notification types |

#### Channels

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/channels` | List configured channels for org |
| `POST` | `/channels/discord` | Configure Discord webhook |
| `POST` | `/channels/webhook` | Configure custom webhook |
| `PUT` | `/channels/{id}` | Update channel configuration |
| `DELETE` | `/channels/{id}` | Remove channel |
| `POST` | `/channels/{id}/test` | Send test notification |

#### Alert Rules

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/rules` | List alert rules |
| `POST` | `/rules` | Create alert rule |
| `PUT` | `/rules/{id}` | Update alert rule |
| `DELETE` | `/rules/{id}` | Delete alert rule |
| `POST` | `/rules/{id}/enable` | Enable rule |
| `POST` | `/rules/{id}/disable` | Disable rule |

---

## Database Schema

### Current Schema (Placeholder)

The current `NotificationsDbContext` contains only a placeholder entity:

```csharp
public sealed class SampleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "hello";
}
```

### Planned Entities

The following entities are planned for implementation:

#### Notification

The core notification record:

```csharp
public class Notification
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? UserId { get; set; }  // Null for org-wide notifications

    // Notification content
    public string NotificationType { get; set; }  // e.g., "server.crashed", "billing.invoice"
    public string Subject { get; set; }
    public string Body { get; set; }
    public string? BodyHtml { get; set; }
    public string? TemplateId { get; set; }
    public string? TemplateData { get; set; }  // JSON

    // Source information
    public string SourceService { get; set; }
    public string? SourceEventId { get; set; }
    public string? CorrelationId { get; set; }

    // Priority and scheduling
    public NotificationPriority Priority { get; set; }
    public DateTime? ScheduledAt { get; set; }

    // Tracking
    public NotificationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? ReadAt { get; set; }

    // Navigation
    public ICollection<NotificationDelivery> Deliveries { get; set; }
}

public enum NotificationPriority { Low, Normal, High, Urgent }
public enum NotificationStatus { Pending, Sending, Sent, Failed, Cancelled }
```

#### NotificationDelivery

Track delivery attempts per channel:

```csharp
public class NotificationDelivery
{
    public Guid Id { get; set; }
    public Guid NotificationId { get; set; }
    public Guid ChannelId { get; set; }

    // Delivery tracking
    public DeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ExternalId { get; set; }  // Provider's message ID

    // Navigation
    public Notification Notification { get; set; }
    public NotificationChannel Channel { get; set; }
}

public enum DeliveryStatus { Pending, Sending, Delivered, Failed, Bounced }
```

#### NotificationChannel

User/org configured delivery channels:

```csharp
public class NotificationChannel
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? CreatedByUserId { get; set; }

    // Channel configuration
    public string Name { get; set; }
    public ChannelType Type { get; set; }
    public string Configuration { get; set; }  // JSON (encrypted for sensitive data)
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }

    // Audit
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastTestedAt { get; set; }
    public bool LastTestSuccessful { get; set; }
}

public enum ChannelType { Email, DiscordWebhook, DiscordBot, CustomWebhook, Slack }
```

#### NotificationTemplate

Reusable notification templates:

```csharp
public class NotificationTemplate
{
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; }  // Null for system templates

    // Template identification
    public string Name { get; set; }
    public string NotificationType { get; set; }
    public string? Description { get; set; }

    // Template content
    public string SubjectTemplate { get; set; }  // Liquid/Handlebars
    public string BodyTemplate { get; set; }
    public string? BodyHtmlTemplate { get; set; }

    // Metadata
    public bool IsSystemTemplate { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

#### NotificationPreference

User preferences for notification delivery:

```csharp
public class NotificationPreference
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }

    // Preference scope
    public string NotificationType { get; set; }  // Or "*" for all

    // Channel preferences
    public bool EmailEnabled { get; set; }
    public bool DiscordEnabled { get; set; }
    public bool WebhookEnabled { get; set; }
    public bool InAppEnabled { get; set; }

    // Delivery preferences
    public bool DigestEnabled { get; set; }
    public DigestFrequency DigestFrequency { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public enum DigestFrequency { Immediate, Hourly, Daily, Weekly }
```

#### AlertRule

Rules for triggering notifications on events:

```csharp
public class AlertRule
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CreatedByUserId { get; set; }

    // Rule definition
    public string Name { get; set; }
    public string? Description { get; set; }
    public string EventType { get; set; }  // e.g., "node.health.degraded"
    public string? Condition { get; set; }  // JSON - conditions to evaluate

    // Action
    public Guid TemplateId { get; set; }
    public string[] ChannelIds { get; set; }
    public NotificationPriority Priority { get; set; }

    // State
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public int TriggerCount { get; set; }
}
```

### Database Migrations

To add migrations when entities are implemented:

```bash
# Add a new migration
dotnet ef migrations add AddNotificationEntities \
  --project src/Dhadgar.Notifications \
  --startup-project src/Dhadgar.Notifications \
  --output-dir Data/Migrations

# Apply migrations
dotnet ef database update \
  --project src/Dhadgar.Notifications \
  --startup-project src/Dhadgar.Notifications
```

**Note**: In Development mode, migrations are auto-applied on startup.

---

## Configuration

### Application Settings

The service uses standard ASP.NET Core configuration with the following sources (in priority order):

1. `appsettings.json` - Base configuration
2. `appsettings.Development.json` - Development overrides
3. Environment variables
4. User secrets (local development)
5. Kubernetes ConfigMaps/Secrets (production)

### Current Configuration

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

#### Email (SMTP)

```json
{
  "Email": {
    "Provider": "Smtp",
    "Smtp": {
      "Host": "smtp.example.com",
      "Port": 587,
      "UseSsl": true,
      "Username": "notifications@meridianconsole.com",
      "Password": "use-user-secrets",
      "FromAddress": "notifications@meridianconsole.com",
      "FromName": "Meridian Console",
      "ReplyTo": "support@meridianconsole.com"
    }
  }
}
```

#### Email (SendGrid)

```json
{
  "Email": {
    "Provider": "SendGrid",
    "SendGrid": {
      "ApiKey": "use-user-secrets",
      "FromAddress": "notifications@meridianconsole.com",
      "FromName": "Meridian Console",
      "ReplyTo": "support@meridianconsole.com",
      "ClickTracking": false,
      "OpenTracking": false
    }
  }
}
```

#### Discord

```json
{
  "Discord": {
    "DefaultWebhookUrl": "use-user-secrets",
    "BotToken": "use-user-secrets",
    "RateLimitRetries": 3,
    "EmbedColor": "#5865F2"
  }
}
```

#### Webhooks

```json
{
  "Webhooks": {
    "DefaultTimeout": "00:00:30",
    "MaxRetries": 3,
    "RetryDelays": ["00:00:05", "00:00:30", "00:05:00"],
    "SignatureHeader": "X-Meridian-Signature",
    "SigningSecret": "use-user-secrets"
  }
}
```

#### Delivery Settings

```json
{
  "Delivery": {
    "MaxConcurrentDeliveries": 10,
    "BatchSize": 100,
    "DigestProcessingInterval": "00:15:00",
    "RetentionDays": 90,
    "HighPriorityQueue": true
  }
}
```

#### OpenTelemetry (Optional)

```json
{
  "OpenTelemetry": {
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

### Using User Secrets

For local development, store sensitive values using .NET user secrets:

```bash
# Initialize user secrets for the project
dotnet user-secrets init --project src/Dhadgar.Notifications

# Set email credentials
dotnet user-secrets set "Email:Smtp:Password" "your-smtp-password" --project src/Dhadgar.Notifications
dotnet user-secrets set "Email:SendGrid:ApiKey" "SG.xxxxx" --project src/Dhadgar.Notifications

# Set Discord credentials
dotnet user-secrets set "Discord:DefaultWebhookUrl" "https://discord.com/api/webhooks/..." --project src/Dhadgar.Notifications
dotnet user-secrets set "Discord:BotToken" "your-bot-token" --project src/Dhadgar.Notifications

# List all secrets
dotnet user-secrets list --project src/Dhadgar.Notifications
```

---

## Integration Points

### Consuming Events from Other Services

The Notifications service will consume domain events published by other services via MassTransit/RabbitMQ.

#### Identity Service Events

Already defined in `Dhadgar.Contracts.Identity.IdentityEvents`:

| Event | Trigger | Notification |
|-------|---------|--------------|
| `UserAuthenticated` | User logs in | Login notification (if enabled) |
| `OrgMembershipChanged` | Membership status change | Invite sent, accepted, rejected, etc. |
| `UserDeactivated` | Account deactivated | Account deactivation notice |
| `OrganizationCreated` | New org created | Welcome to your organization |
| `UserPermissionsChanged` | Role/permissions changed | Your permissions have been updated |
| `MemberLeftOrganization` | User leaves org | Member departure notification |
| `InvitationRejected` | Invite declined | Your invitation was declined |
| `InvitationExpired` | Invite expired | Pending invitation has expired |
| `UserDeletionRequested` | Deletion scheduled | Account deletion confirmation |
| `OrganizationOwnershipTransferred` | Ownership transfer | Ownership transfer notification |
| `OAuthAccountLinked` | Account linked | New login method added |
| `OAuthAccountUnlinked` | Account unlinked | Login method removed |

#### Planned Event Consumers

Events to be defined in `Dhadgar.Contracts`:

**Servers Service**:
- `ServerStarted`, `ServerStopped`, `ServerCrashed`
- `ServerBackupCompleted`, `ServerBackupFailed`
- `ServerUpdateAvailable`, `ServerUpdateInstalled`

**Nodes Service**:
- `NodeOnline`, `NodeOffline`
- `NodeHealthDegraded`, `NodeHealthRestored`
- `AgentDisconnected`, `AgentReconnected`

**Billing Service**:
- `InvoiceGenerated`, `PaymentReceived`, `PaymentFailed`
- `SubscriptionRenewed`, `SubscriptionCancelled`
- `UsageLimitApproaching`, `UsageLimitExceeded`

**Tasks Service**:
- `TaskCompleted`, `TaskFailed`
- `ScheduledMaintenanceReminder`

**Files Service**:
- `FileUploadCompleted`, `BackupCompleted`
- `StorageQuotaWarning`

**Firewall Service**:
- `SuspiciousTrafficDetected`, `RuleTriggered`
- `PortScanBlocked`

### Example Consumer Implementation

```csharp
public class OrgMembershipChangedConsumer : IConsumer<OrgMembershipChanged>
{
    private readonly INotificationService _notifications;
    private readonly ILogger<OrgMembershipChangedConsumer> _logger;

    public async Task Consume(ConsumeContext<OrgMembershipChanged> context)
    {
        var @event = context.Message;

        _logger.LogInformation(
            "Processing membership change: {ChangeType} for user {UserId} in org {OrgId}",
            @event.ChangeType, @event.UserId, @event.OrganizationId);

        var notificationType = @event.ChangeType switch
        {
            MembershipChangeTypes.Invited => "membership.invited",
            MembershipChangeTypes.Accepted => "membership.accepted",
            MembershipChangeTypes.Removed => "membership.removed",
            _ => null
        };

        if (notificationType != null)
        {
            await _notifications.SendAsync(new NotificationRequest
            {
                OrganizationId = @event.OrganizationId,
                UserId = @event.UserId,
                NotificationType = notificationType,
                TemplateData = new
                {
                    Role = @event.Role,
                    OccurredAt = @event.OccurredAtUtc
                }
            });
        }
    }
}
```

### Outbound Integration

#### Email Delivery

**SMTP Provider**:
```csharp
public interface IEmailSender
{
    Task<DeliveryResult> SendAsync(EmailMessage message, CancellationToken ct);
}
```

**SendGrid Provider**:
```csharp
public interface ISendGridClient
{
    Task<Response> SendEmailAsync(SendGridMessage msg, CancellationToken ct);
}
```

#### Discord Integration

**Webhook Delivery**:
```csharp
public interface IDiscordWebhookClient
{
    Task<DeliveryResult> SendAsync(string webhookUrl, DiscordEmbed embed, CancellationToken ct);
}
```

**Bot Messages** (via Dhadgar.Discord service):
```csharp
// Send via HTTP to Discord service
POST /api/v1/discord/messages
{
    "channelId": "123456789",
    "embed": { ... }
}
```

#### Custom Webhooks

```csharp
public interface IWebhookDelivery
{
    Task<DeliveryResult> SendAsync(WebhookRequest request, CancellationToken ct);
}

public class WebhookRequest
{
    public string Url { get; set; }
    public string Method { get; set; } = "POST";
    public Dictionary<string, string> Headers { get; set; }
    public object Payload { get; set; }
    public TimeSpan Timeout { get; set; }
    public string? SigningSecret { get; set; }
}
```

---

## Planned Features

### Phase 1: Core Notification Delivery

1. **Database Entities**: Implement the planned schema (notifications, channels, templates)
2. **Email Delivery**: SMTP and SendGrid providers with retry logic
3. **Basic Templates**: Simple variable substitution templates
4. **Notification History**: Store and query sent notifications
5. **MassTransit Consumers**: Consume Identity service events

### Phase 2: Multi-Channel Support

1. **Discord Webhooks**: Send rich embeds to Discord channels
2. **Custom Webhooks**: POST notifications to user-configured endpoints
3. **Webhook Signatures**: HMAC signing for webhook security
4. **Channel Management API**: CRUD for notification channels

### Phase 3: User Preferences

1. **Preference Management**: Per-user, per-type notification preferences
2. **Channel Selection**: Users choose which channels receive which notifications
3. **Quiet Hours**: Don't disturb settings
4. **Digest Mode**: Batch notifications into periodic digests

### Phase 4: Advanced Templates

1. **Template Engine**: Liquid/Handlebars template rendering
2. **Template Management UI**: Create and preview templates
3. **Localization**: Multi-language template support
4. **Rich Content**: HTML emails, Discord embeds, Slack blocks

### Phase 5: Alert Rules

1. **Rule Engine**: Define conditions for automatic notifications
2. **Threshold Alerts**: Trigger on metric thresholds
3. **Escalation**: Multi-level alerting with escalation paths
4. **Rate Limiting**: Prevent notification storms

### Phase 6: Analytics and Monitoring

1. **Delivery Analytics**: Track delivery rates, open rates, failures
2. **Dashboard**: Notification metrics in Grafana
3. **Alerting on Failures**: Notify admins of delivery issues
4. **Retention Policies**: Automatic cleanup of old notifications

---

## Testing

### Running Tests

```bash
# Run all Notifications tests
dotnet test tests/Dhadgar.Notifications.Tests

# Run specific test
dotnet test tests/Dhadgar.Notifications.Tests --filter "FullyQualifiedName~HelloWorldTests"

# Run with verbose output
dotnet test tests/Dhadgar.Notifications.Tests -v normal
```

### Current Test Coverage

The test project includes:

| Test Class | Tests | Description |
|------------|-------|-------------|
| `HelloWorldTests` | 1 | Verifies service hello message |
| `SwaggerTests` | 3 | Validates OpenAPI documentation |

### Test Infrastructure

**WebApplicationFactory**: `NotificationsWebApplicationFactory` provides:
- In-memory database for isolation
- Testing environment configuration
- Clean state per test

```csharp
public class NotificationsWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Replace PostgreSQL with in-memory database
            services.AddDbContext<NotificationsDbContext>(options =>
            {
                options.UseInMemoryDatabase("NotificationsTestDb");
            });
        });
    }
}
```

### Planned Tests

When core functionality is implemented, add tests for:

**Unit Tests**:
- Template rendering
- Notification priority sorting
- Preference matching logic
- Webhook signature generation

**Integration Tests**:
- Notification creation and storage
- Channel configuration CRUD
- Template CRUD
- Preference management

**Consumer Tests**:
- MassTransit consumer handling
- Event-to-notification mapping
- Error handling and retries

**Delivery Tests** (with mocked providers):
- Email delivery flow
- Discord webhook delivery
- Custom webhook delivery
- Retry logic on failures

---

## Development Guidelines

### Code Structure

Follow the established service pattern:

```
src/Dhadgar.Notifications/
├── Program.cs              # Minimal API setup
├── appsettings.json        # Configuration
├── Data/
│   ├── NotificationsDbContext.cs
│   ├── NotificationsDbContextFactory.cs
│   └── Migrations/         # EF Core migrations
├── Entities/               # Domain entities
├── Services/               # Business logic
│   ├── INotificationService.cs
│   ├── NotificationService.cs
│   ├── IEmailSender.cs
│   ├── SmtpEmailSender.cs
│   └── SendGridEmailSender.cs
├── Consumers/              # MassTransit consumers
├── Endpoints/              # API endpoint definitions
└── Options/                # Configuration options classes
```

### Microservices Rules

**Critical**: Do NOT add `ProjectReference` to other services. Communication with other services must be via:
- MassTransit events (async)
- HTTP API calls (sync)

### Adding New Notification Types

1. Define the event in `Dhadgar.Contracts`
2. Create a consumer in `Consumers/`
3. Create a default template in `Data/Seeds/`
4. Add to the notification type registry

### Error Handling

Use the standard Problem Details middleware for HTTP errors. For delivery failures:
- Log with correlation ID
- Store failure in `NotificationDelivery`
- Schedule retry with backoff
- After max retries, mark as permanently failed

---

## Related Documentation

- [Repository CLAUDE.md](/CLAUDE.md) - Overall project guidelines
- [Gateway Configuration](/src/Dhadgar.Gateway/appsettings.json) - Route configuration
- [Messaging Library](/src/Shared/Dhadgar.Messaging/README.md) - MassTransit patterns
- [Contracts Library](/src/Shared/Dhadgar.Contracts/README.md) - Event definitions
- [Docker Compose](/deploy/compose/README.md) - Local infrastructure
- [Identity Events](/src/Shared/Dhadgar.Contracts/Identity/IdentityEvents.cs) - Identity domain events

---

## Port Assignment

| Environment | Port |
|-------------|------|
| Local Development | 5090 |
| Docker Compose | 5090 |
| Kubernetes | Dynamic (via Service) |

**Gateway Route**: `/api/v1/notifications/{**catch-all}` with `TenantScoped` authorization policy.

---

## Contact

For questions about the Notifications service, consult the specialized agents:

- **messaging-engineer**: For MassTransit consumer implementation
- **rest-api-engineer**: For API design decisions
- **database-schema-architect**: For entity design
- **observability-architect**: For metrics and tracing

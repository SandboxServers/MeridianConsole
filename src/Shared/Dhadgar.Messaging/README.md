# Dhadgar.Messaging

A shared library providing MassTransit and RabbitMQ messaging infrastructure for the Meridian Console (Dhadgar) microservices platform. This library establishes consistent messaging patterns, exchange naming conventions, and configuration helpers used across all services that participate in asynchronous communication.

## Table of Contents

- [Overview](#overview)
- [Tech Stack](#tech-stack)
- [Purpose](#purpose)
- [Architecture](#architecture)
- [Configuration](#configuration)
- [Message Contracts](#message-contracts)
- [Publishing Messages](#publishing-messages)
- [Consuming Messages](#consuming-messages)
- [Error Handling and Reliability](#error-handling-and-reliability)
- [Testing](#testing)
- [Dependencies](#dependencies)
- [Related Documentation](#related-documentation)

---

## Overview

`Dhadgar.Messaging` is a foundational shared library that encapsulates all MassTransit and RabbitMQ configuration for the Dhadgar platform. It provides:

1. **Centralized Bus Configuration**: A single extension method (`AddDhadgarMessaging`) that configures MassTransit with RabbitMQ transport using consistent settings across all services.

2. **Stable Exchange Naming**: A custom `IEntityNameFormatter` implementation that ensures exchange names remain stable even if types or namespaces are refactored, using the `meridian.*` naming convention.

3. **Configuration-Driven Setup**: Reads connection details from standard ASP.NET Core configuration, supporting both `appsettings.json` and user secrets for local development.

The library is intentionally minimal and focused. It does NOT contain:
- Message contracts (those belong in `Dhadgar.Contracts`)
- Consumer implementations (those belong in individual services)
- Business logic of any kind

This separation ensures that the messaging infrastructure can evolve independently from the message contracts and business logic.

---

## Tech Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| **.NET** | 10.0 | Target framework |
| **MassTransit** | 8.3.6 | Message bus abstraction layer |
| **MassTransit.RabbitMQ** | 8.3.6 | RabbitMQ transport for MassTransit |
| **RabbitMQ** | 3.x (Management) | Message broker (via Docker Compose) |

### Version Management

Package versions are managed centrally in `Directory.Packages.props` at the repository root. Individual projects reference packages WITHOUT version attributes:

```xml
<!-- In Dhadgar.Messaging.csproj -->
<ItemGroup>
  <PackageReference Include="MassTransit" />
  <PackageReference Include="MassTransit.RabbitMQ" />
</ItemGroup>
```

To update MassTransit versions, modify ONLY `Directory.Packages.props`:

```xml
<!-- In Directory.Packages.props -->
<PackageVersion Include="MassTransit" Version="8.3.6" />
<PackageVersion Include="MassTransit.RabbitMQ" Version="8.3.6" />
```

---

## Purpose

### Why Messaging is Separated

The Dhadgar platform follows a strict microservices architecture where services MUST NOT reference each other via `ProjectReference`. This prevents the "distributed monolith" anti-pattern where compile-time coupling undermines the benefits of service separation.

Services communicate through two mechanisms:

1. **Synchronous HTTP**: For request/response patterns where immediate responses are needed
2. **Asynchronous Messaging**: For event-driven patterns, fire-and-forget operations, and decoupled workflows

The `Dhadgar.Messaging` library enables the second pattern by providing:

- **Consistent Configuration**: Every service uses the same MassTransit setup, ensuring compatibility
- **Stable Topology**: Exchange names are predictable and don't break when code is refactored
- **Environment Flexibility**: Works with both real RabbitMQ (production/development) and in-memory transport (testing)

### What Belongs Here

**DO include:**
- MassTransit bus configuration extensions
- RabbitMQ-specific configuration helpers
- Entity name formatters and conventions
- Retry policy configurations (when added)
- Saga state machine configurations (when added)
- Dead letter queue handlers (when added)

**DO NOT include:**
- Message contracts (DTOs) - use `Dhadgar.Contracts`
- Consumer implementations - implement in the consuming service
- Business logic - belongs in service layer
- Service-specific configurations - use service's own setup

---

## Architecture

### Message Flow

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  Service A      │     │    RabbitMQ      │     │  Service B      │
│                 │     │                  │     │                 │
│  Publisher      │────▶│  Exchange        │────▶│  Consumer       │
│  (IPublish      │     │  (meridian.*)    │     │  (IConsumer<T>) │
│   Endpoint)     │     │                  │     │                 │
└─────────────────┘     └──────────────────┘     └─────────────────┘
        │                                                 │
        │                                                 │
        ▼                                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│                     Dhadgar.Contracts                           │
│         (Shared message DTOs: Commands, Events)                 │
└─────────────────────────────────────────────────────────────────┘
```

### Exchange Naming Convention

The library uses a custom `StaticEntityNameFormatter` that transforms message type names into stable exchange names:

```csharp
// Input: Dhadgar.Contracts.Identity.UserAuthenticated
// Output: meridian.userauthenticated

// Input: Dhadgar.Contracts.Servers.ServerProvisioned
// Output: meridian.serverprovisioned
```

This convention:
- **Prefixes all exchanges with `meridian.`** for easy identification in RabbitMQ management
- **Uses lowercase** for consistency
- **Strips namespaces** so refactoring doesn't break message routing
- **Aligns with the scope document's conventions** for the platform

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          Dhadgar Platform                                │
│                                                                          │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                    Shared Libraries                               │   │
│  │                                                                   │   │
│  │  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐   │   │
│  │  │ Dhadgar.        │  │ Dhadgar.        │  │ Dhadgar.        │   │   │
│  │  │ Contracts       │  │ Messaging       │  │ ServiceDefaults │   │   │
│  │  │                 │  │                 │  │                 │   │   │
│  │  │ - DTOs          │  │ - MassTransit   │  │ - Middleware    │   │   │
│  │  │ - Events        │  │   Config        │  │ - OpenTelemetry │   │   │
│  │  │ - Commands      │  │ - RabbitMQ      │  │ - Health Checks │   │   │
│  │  │                 │  │   Transport     │  │                 │   │   │
│  │  └─────────────────┘  └─────────────────┘  └─────────────────┘   │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                           │                                              │
│                           ▼                                              │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                    Microservices                                  │   │
│  │                                                                   │   │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐    │   │
│  │  │Identity │ │ Servers │ │  Nodes  │ │  Tasks  │ │Notifica-│    │   │
│  │  │         │ │         │ │         │ │         │ │  tions  │    │   │
│  │  │Publisher│ │Consumer │ │Consumer │ │Consumer │ │Consumer │    │   │
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘ └─────────┘    │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Configuration

### Adding Messaging to a Service

In your service's `Program.cs`, add MassTransit messaging with a single extension method call:

```csharp
using Dhadgar.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Add messaging (reads configuration from appsettings.json)
builder.Services.AddDhadgarMessaging(builder.Configuration);

// Optional: Register consumers in the callback
builder.Services.AddDhadgarMessaging(builder.Configuration, x =>
{
    // Register consumers for this service
    x.AddConsumer<MyMessageConsumer>();
    x.AddConsumer<AnotherConsumer>();
});
```

### Configuration Settings

Add RabbitMQ settings to your service's `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "RabbitMqHost": "localhost"
  },
  "RabbitMq": {
    "Username": "dhadgar",
    "Password": "dhadgar"
  }
}
```

**Configuration Keys:**

| Key | Description | Default |
|-----|-------------|---------|
| `ConnectionStrings:RabbitMqHost` | RabbitMQ server hostname | `localhost` |
| `RabbitMq:Username` | Authentication username | `dhadgar` |
| `RabbitMq:Password` | Authentication password | `dhadgar` |

### Environment-Specific Configuration

**Development** (`appsettings.Development.json`):
```json
{
  "ConnectionStrings": {
    "RabbitMqHost": "localhost"
  },
  "RabbitMq": {
    "Username": "dhadgar",
    "Password": "dhadgar"
  }
}
```

**Production** (via environment variables or secrets):
```bash
# Environment variables (Kubernetes ConfigMaps/Secrets)
ConnectionStrings__RabbitMqHost=rabbitmq.production.svc.cluster.local
RabbitMq__Username=produser
RabbitMq__Password=<secret>
```

### Local Development with Docker Compose

The development infrastructure is defined in `deploy/compose/docker-compose.dev.yml`:

```bash
# Start RabbitMQ and other infrastructure
docker compose -f deploy/compose/docker-compose.dev.yml up -d

# Access RabbitMQ Management UI
# URL: http://localhost:15672
# Credentials: dhadgar / dhadgar
```

RabbitMQ ports:
- **5672**: AMQP protocol (for services)
- **15672**: Management UI (for debugging/monitoring)

---

## Message Contracts

Message contracts (DTOs) are defined in the `Dhadgar.Contracts` library, NOT in this library. This separation ensures:

1. **Clean Dependencies**: Services only need `Dhadgar.Contracts` to work with messages
2. **Type Safety**: Both publishers and consumers reference the same types
3. **Versioning**: Contract changes are explicit and versioned

### Contract Location

```
src/Shared/Dhadgar.Contracts/
├── Identity/
│   ├── IdentityEvents.cs        # User/org events
│   └── IdentityServiceContracts.cs
├── Servers/
│   └── Contracts.cs             # Server provisioning
└── Pagination.cs
```

### Contract Examples

**Events** (past tense - something happened):

```csharp
// From Dhadgar.Contracts.Identity.IdentityEvents
namespace Dhadgar.Contracts.Identity;

/// <summary>
/// Published when a user successfully authenticates.
/// </summary>
public record UserAuthenticated(
    Guid UserId,
    Guid OrganizationId,
    string ExternalAuthId,
    string Email,
    string? ClientApp,
    IReadOnlyCollection<string> Permissions,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when organization membership changes.
/// </summary>
public record OrgMembershipChanged(
    Guid OrganizationId,
    Guid UserId,
    Guid MembershipId,
    string ChangeType,
    string? Role,
    string? ClaimType,
    string? ClaimValue,
    string? ResourceType,
    Guid? ResourceId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when a user is deactivated.
/// </summary>
public record UserDeactivated(
    Guid UserId,
    string ExternalAuthId,
    string? Reason,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Published when a new organization is created.
/// </summary>
public record OrganizationCreated(
    Guid OrganizationId,
    Guid OwnerId,
    string Name,
    string Slug,
    DateTimeOffset OccurredAtUtc);
```

**Commands** (imperative - do something):

```csharp
// From Dhadgar.Contracts.Servers.Contracts
namespace Dhadgar.Contracts.Servers;

public record ServerProvisionRequested(
    Guid ServerId,
    Guid OrgId,
    string GameType,
    int CpuLimit,
    int MemoryMb);

public record ServerProvisioned(
    Guid ServerId,
    Guid OrgId,
    string NodeId,
    IReadOnlyDictionary<string, string> ConnectionInfo);
```

### Contract Design Guidelines

1. **Use Records**: Immutable by default, value equality, with-expressions
2. **Include Timestamps**: Always include `DateTimeOffset OccurredAtUtc` for events
3. **Include IDs**: Reference entities by ID, not full objects
4. **Keep Minimal**: Include only what consumers need; they can look up details
5. **Document**: Add XML comments explaining when events are published

---

## Publishing Messages

### Publisher Pattern

Services that need to publish messages should define a service interface and implementation that wraps `IPublishEndpoint`:

```csharp
// Interface (for testability)
public interface IIdentityEventPublisher
{
    Task PublishUserAuthenticatedAsync(UserAuthenticated message, CancellationToken ct = default);
    Task PublishOrgMembershipChangedAsync(OrgMembershipChanged message, CancellationToken ct = default);
    Task PublishUserDeactivatedAsync(UserDeactivated message, CancellationToken ct = default);
}

// Implementation
public sealed class IdentityEventPublisher : IIdentityEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public IdentityEventPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public Task PublishUserAuthenticatedAsync(UserAuthenticated message, CancellationToken ct = default)
        => _publishEndpoint.Publish(message, ct);

    public Task PublishOrgMembershipChangedAsync(OrgMembershipChanged message, CancellationToken ct = default)
        => _publishEndpoint.Publish(message, ct);

    public Task PublishUserDeactivatedAsync(UserDeactivated message, CancellationToken ct = default)
        => _publishEndpoint.Publish(message, ct);
}
```

### Registering the Publisher

In `Program.cs`:

```csharp
builder.Services.AddDhadgarMessaging(builder.Configuration);
builder.Services.AddScoped<IIdentityEventPublisher, IdentityEventPublisher>();
```

### Publishing from Endpoints

```csharp
// In an endpoint handler
app.MapPost("/users/{id}/deactivate", async (
    Guid id,
    IdentityDbContext db,
    IIdentityEventPublisher eventPublisher) =>
{
    var user = await db.Users.FindAsync(id);
    if (user is null) return Results.NotFound();

    user.IsActive = false;
    await db.SaveChangesAsync();

    // Publish the event
    await eventPublisher.PublishUserDeactivatedAsync(new UserDeactivated(
        user.Id,
        user.ExternalAuthId,
        "User requested deactivation",
        DateTimeOffset.UtcNow));

    return Results.Ok();
});
```

### Direct Publishing (Alternative)

For simpler cases, inject `IPublishEndpoint` directly:

```csharp
app.MapPost("/example", async (IPublishEndpoint publisher) =>
{
    await publisher.Publish(new SomeEvent(Guid.NewGuid(), DateTimeOffset.UtcNow));
    return Results.Accepted();
});
```

---

## Consuming Messages

### Creating a Consumer

Consumers implement `IConsumer<TMessage>` and are registered with MassTransit:

```csharp
using MassTransit;
using Dhadgar.Contracts.Identity;

public sealed class UserDeactivatedConsumer : IConsumer<UserDeactivated>
{
    private readonly ILogger<UserDeactivatedConsumer> _logger;
    private readonly MyDbContext _db;

    public UserDeactivatedConsumer(
        ILogger<UserDeactivatedConsumer> logger,
        MyDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    public async Task Consume(ConsumeContext<UserDeactivated> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Processing user deactivation for {UserId}",
            message.UserId);

        // Handle the event (e.g., clean up user data in this service)
        await _db.UserReferences
            .Where(r => r.UserId == message.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, false));

        _logger.LogInformation(
            "Completed processing user deactivation for {UserId}",
            message.UserId);
    }
}
```

### Registering Consumers

In `Program.cs`, use the configure callback:

```csharp
builder.Services.AddDhadgarMessaging(builder.Configuration, x =>
{
    // Register all consumers for this service
    x.AddConsumer<UserDeactivatedConsumer>();
    x.AddConsumer<OrganizationCreatedConsumer>();
    x.AddConsumer<ServerProvisionRequestedConsumer>();
});
```

### Consumer Guidelines

1. **Single Responsibility**: One consumer per message type
2. **Idempotency**: Design for at-least-once delivery; same message may arrive twice
3. **Error Handling**: Let exceptions propagate for MassTransit retry handling
4. **Logging**: Log at start and end of processing with correlation data
5. **Keep Fast**: Avoid long-running operations; offload to background jobs if needed

---

## Error Handling and Reliability

### Production-Ready Resilience

The messaging infrastructure includes comprehensive error handling and reliability features:

- ✅ **Retry policies** with exponential backoff (5 retries, 200ms-5s)
- ✅ **Delayed redelivery** for transient failures (5min, 15min, 1hr)
- ✅ **Circuit breaker** to prevent cascade failures (15% threshold)
- ✅ **In-memory outbox** to prevent duplicate sends on retry
- ✅ **Dead letter queue consumer pattern** for handling permanently failed messages
- ✅ **Publisher confirms** for guaranteed message delivery
- ✅ **Connection resilience** with heartbeat and timeout settings

### Retry Configuration

Messages are automatically retried with exponential backoff:

```csharp
// Configured in MessagingExtensions.cs
cfg.UseMessageRetry(r =>
{
    // Exponential backoff: 200ms, 400ms, 800ms, 1.6s, 3.2s (capped at 5s)
    r.Exponential(5,
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMilliseconds(200));

    // Don't retry validation errors - they won't succeed on retry
    r.Ignore<ArgumentNullException>();
    r.Ignore<ArgumentException>();
});
```

### Delayed Redelivery

After immediate retries fail, messages are scheduled for redelivery:

```csharp
cfg.UseDelayedRedelivery(r =>
{
    // Redelivery intervals: 5min, 15min, 1hr (3 attempts)
    r.Intervals(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1));
});
```

**Note:** Requires the `rabbitmq_delayed_message_exchange` plugin on RabbitMQ.

### Circuit Breaker

Prevents cascade failures when downstream services are unhealthy:

```csharp
cfg.UseCircuitBreaker(cb =>
{
    cb.TrackingPeriod = TimeSpan.FromMinutes(1);
    cb.TripThreshold = 15;       // Trip when failure rate exceeds 15%
    cb.ActiveThreshold = 10;     // Start tracking after 10 messages
    cb.ResetInterval = TimeSpan.FromMinutes(5);  // Try again after 5 minutes
});
```

### Dead Letter Queue Consumer Pattern

When messages exhaust all retries and redelivery attempts, they are sent to error queues. Use the `FaultConsumer<T>` base class to handle these permanently failed messages:

```csharp
// 1. Create a fault consumer for your message type
public sealed class SendEmailNotificationFaultConsumer
    : FaultConsumer<SendEmailNotification>
{
    private readonly IAlertService _alerts;

    public SendEmailNotificationFaultConsumer(
        ILogger<SendEmailNotificationFaultConsumer> logger,
        IAlertService alerts) : base(logger)
    {
        _alerts = alerts;
    }

    protected override async Task HandleFaultAsync(
        ConsumeContext<Fault<SendEmailNotification>> context,
        CancellationToken ct)
    {
        var fault = context.Message;

        // Alert operations team about the permanent failure
        await _alerts.SendAsync(
            $"Email notification {fault.Message.NotificationId} failed permanently",
            severity: AlertSeverity.High);
    }
}

// 2. Register the fault consumer
services.AddDhadgarMessaging(config, x =>
{
    x.AddConsumer<SendEmailNotificationConsumer>();
    x.AddFaultConsumer<SendEmailNotification, SendEmailNotificationFaultConsumer>();
});
```

The `FaultConsumer<T>` base class provides:
- Detailed exception logging with stack traces
- Correlation ID tracking
- Timing metrics
- Structured logging context

### Exception Handling in Consumers

The retry pipeline handles most exceptions automatically. Design your consumers accordingly:

```csharp
public async Task Consume(ConsumeContext<MyMessage> context)
{
    // Transient exceptions (network, timeout) are retried automatically
    // Just let them propagate

    // For validation/business rule violations that won't succeed on retry,
    // consider throwing a specific exception type and ignoring it in retry config:
    if (!IsValid(context.Message))
    {
        throw new ValidationException("Invalid message");
    }

    // Normal processing - exceptions will trigger retry
    await ProcessMessageAsync(context.Message);
}
```

---

## Testing

### Unit Testing Consumers

Test consumers in isolation by mocking dependencies:

```csharp
using MassTransit;
using MassTransit.Testing;
using NSubstitute;
using Xunit;

public class UserDeactivatedConsumerTests
{
    [Fact]
    public async Task Consume_DeactivatesUserReferences()
    {
        // Arrange
        var db = CreateTestDbContext();
        var logger = Substitute.For<ILogger<UserDeactivatedConsumer>>();
        var consumer = new UserDeactivatedConsumer(logger, db);

        var context = Substitute.For<ConsumeContext<UserDeactivated>>();
        context.Message.Returns(new UserDeactivated(
            Guid.NewGuid(),
            "external-id",
            "Test reason",
            DateTimeOffset.UtcNow));

        // Act
        await consumer.Consume(context);

        // Assert
        // Verify database changes
    }
}
```

### Integration Testing with In-Memory Transport

For integration tests, use MassTransit's in-memory transport to avoid RabbitMQ dependency:

```csharp
// In test WebApplicationFactory
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.UseEnvironment("Testing");

    builder.ConfigureServices(services =>
    {
        // Remove the real MassTransit registration
        services.RemoveAll<IBusControl>();
        services.RemoveAll<IBus>();

        // Use in-memory transport for testing
        services.AddMassTransit(x =>
        {
            x.UsingInMemory((ctx, cfg) =>
            {
                cfg.ConfigureEndpoints(ctx);
            });
        });
    });
}
```

### Testing Event Publishing

For services that publish events, create a test double of the publisher interface:

```csharp
// Test publisher that captures published events
public sealed class TestIdentityEventPublisher : IIdentityEventPublisher
{
    public ConcurrentQueue<UserAuthenticated> UserAuthenticatedEvents { get; } = new();
    public ConcurrentQueue<OrgMembershipChanged> OrgMembershipChangedEvents { get; } = new();
    public ConcurrentQueue<UserDeactivated> UserDeactivatedEvents { get; } = new();

    public Task PublishUserAuthenticatedAsync(UserAuthenticated message, CancellationToken ct = default)
    {
        UserAuthenticatedEvents.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task PublishOrgMembershipChangedAsync(OrgMembershipChanged message, CancellationToken ct = default)
    {
        OrgMembershipChangedEvents.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task PublishUserDeactivatedAsync(UserDeactivated message, CancellationToken ct = default)
    {
        UserDeactivatedEvents.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        while (UserAuthenticatedEvents.TryDequeue(out _)) { }
        while (OrgMembershipChangedEvents.TryDequeue(out _)) { }
        while (UserDeactivatedEvents.TryDequeue(out _)) { }
    }
}
```

### Using in Test Factory

```csharp
public sealed class IdentityWebApplicationFactory : WebApplicationFactory<Program>
{
    public TestIdentityEventPublisher EventPublisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace real publisher with test double
            services.RemoveAll<IIdentityEventPublisher>();
            services.AddSingleton<IIdentityEventPublisher>(EventPublisher);

            // Use in-memory MassTransit
            services.AddMassTransit(x =>
            {
                x.UsingInMemory((ctx, cfg) =>
                {
                    cfg.ConfigureEndpoints(ctx);
                });
            });
        });
    }
}
```

### Asserting Published Events

```csharp
[Fact]
public async Task DeactivateUser_PublishesUserDeactivatedEvent()
{
    // Arrange
    await using var factory = new IdentityWebApplicationFactory();
    var client = factory.CreateClient();
    var userId = await factory.SeedUserAsync();

    // Act
    var response = await client.PostAsync($"/users/{userId}/deactivate", null);

    // Assert
    response.EnsureSuccessStatusCode();

    Assert.Single(factory.EventPublisher.UserDeactivatedEvents);
    var publishedEvent = factory.EventPublisher.UserDeactivatedEvents.First();
    Assert.Equal(userId, publishedEvent.UserId);
}
```

---

## Dependencies

### Direct Dependencies

| Package | Purpose |
|---------|---------|
| `MassTransit` | Message bus abstraction and consumer infrastructure |
| `MassTransit.RabbitMQ` | RabbitMQ transport implementation |

### Transitive Dependencies

MassTransit brings in additional dependencies:
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `RabbitMQ.Client`

### Project References

This library intentionally has NO project references to maintain its position as a foundational building block:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>Dhadgar.Messaging</AssemblyName>
    <RootNamespace>Dhadgar.Messaging</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MassTransit" />
    <PackageReference Include="MassTransit.RabbitMQ" />
  </ItemGroup>
</Project>
```

### Services That Reference This Library

Any service that participates in messaging should reference this library:

- `Dhadgar.Identity` - Publishes authentication and organization events
- `Dhadgar.Servers` - Publishes/consumes server provisioning messages
- `Dhadgar.Nodes` - Publishes node health events
- `Dhadgar.Tasks` - Orchestrates background tasks via messaging
- `Dhadgar.Notifications` - Consumes events to send notifications
- `Dhadgar.Billing` - Consumes usage events for metering
- (And more as the platform grows)

---

## Related Documentation

### Repository Documentation

| Document | Location | Description |
|----------|----------|-------------|
| Main README | `/CLAUDE.md` | Repository-wide development guide |
| Contracts Library | `/src/Shared/Dhadgar.Contracts/` | Message DTOs and contracts |
| Docker Compose | `/deploy/compose/README.md` | Local infrastructure setup |
| Messaging Engineer Agent | `/.claude/agents/messaging-engineer.md` | AI agent for messaging guidance |

### External Documentation

| Resource | URL |
|----------|-----|
| MassTransit Documentation | https://masstransit.io/documentation |
| MassTransit GitHub | https://github.com/MassTransit/MassTransit |
| RabbitMQ Documentation | https://www.rabbitmq.com/documentation.html |
| RabbitMQ Tutorials | https://www.rabbitmq.com/tutorials |

### Key MassTransit Concepts

- [Consumers](https://masstransit.io/documentation/concepts/consumers) - Handling messages
- [Producers](https://masstransit.io/documentation/concepts/producers) - Publishing messages
- [Sagas](https://masstransit.io/documentation/patterns/saga) - Workflow orchestration
- [Testing](https://masstransit.io/documentation/concepts/testing) - Test harness usage
- [Transports](https://masstransit.io/documentation/transports) - RabbitMQ configuration

---

## Appendix: Complete Source Code

### MessagingExtensions.cs

```csharp
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddDhadgarMessaging(
        this IServiceCollection services,
        IConfiguration config,
        Action<IBusRegistrationConfigurator>? configure = null)
    {
        services.AddMassTransit(x =>
        {
            configure?.Invoke(x);

            x.UsingRabbitMq((ctx, cfg) =>
            {
                var host = config.GetConnectionString("RabbitMqHost") ?? "localhost";
                var user = config["RabbitMq:Username"] ?? "dhadgar";
                var pass = config["RabbitMq:Password"] ?? "dhadgar";

                cfg.Host(host, h =>
                {
                    h.Username(user);
                    h.Password(pass);
                });

                // Stable, explicit exchange names (aligns with the scope doc's meridian.* conventions)
                cfg.MessageTopology.SetEntityNameFormatter(new StaticEntityNameFormatter());

                cfg.ConfigureEndpoints(ctx);
            });
        });

        return services;
    }

    private sealed class StaticEntityNameFormatter : IEntityNameFormatter
    {
        public string FormatEntityName<T>()
        {
            // Keep topology stable even if namespaces/types refactor.
            // You can evolve this to a mapping table later.
            var name = typeof(T).Name;
            return name switch
            {
                _ => $"meridian.{name}".ToLowerInvariant()
            };
        }
    }
}
```

### Hello.cs

```csharp
namespace Dhadgar.Messaging;

/// <summary>
/// "Hello world" surface area used by tests and quick smoke-checks.
/// </summary>
public static class Hello
{
    public const string Message = "Hello from Dhadgar.Messaging";
}
```

---

## Changelog

| Date | Version | Changes |
|------|---------|---------|
| 2026-01 | Initial | Basic MassTransit + RabbitMQ configuration with stable exchange naming |

---

## Contributing

When modifying this library:

1. **Consider Backward Compatibility**: Changes to exchange naming affect ALL services
2. **Test Thoroughly**: Use both unit tests and integration tests
3. **Document Changes**: Update this README and CHANGELOG
4. **Consult Messaging Engineer Agent**: Use `/.claude/agents/messaging-engineer.md` for guidance
5. **Review with Team**: Messaging changes have platform-wide impact

# Dhadgar.Messaging Library

A shared library providing MassTransit and RabbitMQ messaging infrastructure for the Meridian Console (Dhadgar) microservices platform.

## Table of Contents

- [Purpose](#purpose)
- [Message Topology](#message-topology)
- [Consumer Patterns](#consumer-patterns)
- [Publisher Patterns](#publisher-patterns)
- [Saga Support](#saga-support)
- [RabbitMQ Configuration](#rabbitmq-configuration)
- [Error Handling and Retry Policies](#error-handling-and-retry-policies)
- [Usage Examples](#usage-examples)
- [Adding New Consumers and Publishers](#adding-new-consumers-and-publishers)
- [Testing](#testing)
- [Dependencies](#dependencies)

---

## Purpose

The `Dhadgar.Messaging` library provides centralized MassTransit configuration for asynchronous service-to-service communication. It solves several architectural challenges:

### Why Messaging?

The Dhadgar platform follows a strict microservices architecture where services **must not** reference each other via `ProjectReference`. Services communicate through:

1. **Synchronous HTTP**: Request/response patterns requiring immediate responses
2. **Asynchronous Messaging**: Event-driven patterns, fire-and-forget operations, and decoupled workflows

### What This Library Provides

| Feature                       | Description                                                             |
| ----------------------------- | ----------------------------------------------------------------------- |
| Centralized Bus Configuration | Single `AddDhadgarMessaging()` extension method for all services        |
| Stable Exchange Naming        | Custom `IEntityNameFormatter` using `meridian.*` convention             |
| Configuration-Driven Setup    | Reads connection details from standard ASP.NET Core configuration       |
| Environment Flexibility       | Works with real RabbitMQ (production) and in-memory transport (testing) |

### What Belongs Where

| Location            | Content                                                            |
| ------------------- | ------------------------------------------------------------------ |
| `Dhadgar.Messaging` | MassTransit bus configuration, RabbitMQ helpers, entity formatters |
| `Dhadgar.Contracts` | Message contracts (DTOs), events, commands                         |
| Individual Services | Consumer implementations, service-specific configurations          |

---

## Message Topology

### Exchange Naming Convention

The library uses a custom `StaticEntityNameFormatter` that creates stable, predictable exchange names:

```text
Message Type                                    Exchange Name
-----------                                     -------------
Dhadgar.Contracts.Identity.UserAuthenticated    meridian.userauthenticated
Dhadgar.Contracts.Servers.ServerProvisioned     meridian.serverprovisioned
Dhadgar.Contracts.Identity.OrgMembershipChanged meridian.orgmembershipchanged
```

This convention:

- Prefixes all exchanges with `meridian.` for easy identification in RabbitMQ management
- Uses lowercase for consistency
- Strips namespaces so refactoring does not break message routing
- Aligns with the platform naming conventions

### Message Flow Architecture

```
+------------------+     +------------------+     +------------------+
|   Service A      |     |    RabbitMQ      |     |   Service B      |
|                  |     |                  |     |                  |
|   Publisher      |---->|   Exchange       |---->|   Consumer       |
|   (IPublish      |     |   (meridian.*)   |     |   (IConsumer<T>) |
|    Endpoint)     |     |                  |     |                  |
+------------------+     +------------------+     +------------------+
        |                                                 |
        |                                                 |
        v                                                 v
+---------------------------------------------------------------+
|                     Dhadgar.Contracts                          |
|           (Shared message DTOs: Commands, Events)              |
+---------------------------------------------------------------+
```

### Exchange Types

MassTransit creates the following exchange types in RabbitMQ:

| Exchange Type | Purpose                                                     |
| ------------- | ----------------------------------------------------------- |
| Fanout        | Message type exchanges (e.g., `meridian.userauthenticated`) |
| Direct        | Service endpoint exchanges for receive endpoints            |

---

## Consumer Patterns

### Base Consumer Implementation

Consumers implement `IConsumer<TMessage>` from MassTransit:

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

        // Handle the event
        await _db.UserReferences
            .Where(r => r.UserId == message.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, false));

        _logger.LogInformation(
            "Completed processing user deactivation for {UserId}",
            message.UserId);
    }
}
```

### Consumer Guidelines

1. **Single Responsibility**: One consumer per message type
2. **Idempotency**: Design for at-least-once delivery; the same message may arrive twice
3. **Error Handling**: Let exceptions propagate for MassTransit retry handling
4. **Logging**: Log at start and end of processing with correlation data
5. **Keep Fast**: Avoid long-running operations; offload to background jobs if needed

### Registering Consumers

Register consumers in `Program.cs` using the configure callback:

```csharp
builder.Services.AddDhadgarMessaging(builder.Configuration, x =>
{
    x.AddConsumer<UserDeactivatedConsumer>();
    x.AddConsumer<OrganizationCreatedConsumer>();
    x.AddConsumer<ServerProvisionRequestedConsumer>();
});
```

---

## Publisher Patterns

### Recommended: Wrapper Service Pattern

Define a service interface and implementation that wraps `IPublishEndpoint`:

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

Register the publisher service:

```csharp
builder.Services.AddDhadgarMessaging(builder.Configuration);
builder.Services.AddScoped<IIdentityEventPublisher, IdentityEventPublisher>();
```

### Alternative: Direct IPublishEndpoint Injection

For simpler cases, inject `IPublishEndpoint` directly:

```csharp
app.MapPost("/example", async (IPublishEndpoint publisher) =>
{
    await publisher.Publish(new SomeEvent(Guid.NewGuid(), DateTimeOffset.UtcNow));
    return Results.Accepted();
});
```

### Publishing from Endpoints

```csharp
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

---

## Saga Support

### Current State

Saga state machines for complex workflows are planned but not yet implemented in the library.

### Planned Features

When implemented, sagas will support:

- State machine definitions for multi-step workflows
- Correlation by message properties
- Timeout and compensation handling
- Persistence to PostgreSQL

### Example Saga Pattern (Future)

```csharp
// Future implementation pattern
public class ServerProvisioningSaga : MassTransitStateMachine<ServerProvisioningState>
{
    public ServerProvisioningSaga()
    {
        InstanceState(x => x.CurrentState);

        Event(() => ProvisionRequested, x => x.CorrelateById(m => m.Message.ServerId));
        Event(() => NodeAssigned, x => x.CorrelateById(m => m.Message.ServerId));
        Event(() => ProvisioningComplete, x => x.CorrelateById(m => m.Message.ServerId));

        Initially(
            When(ProvisionRequested)
                .Then(context => context.Saga.RequestedAt = DateTime.UtcNow)
                .TransitionTo(Requested));

        During(Requested,
            When(NodeAssigned)
                .Then(context => context.Saga.NodeId = context.Message.NodeId)
                .TransitionTo(Assigned));

        During(Assigned,
            When(ProvisioningComplete)
                .TransitionTo(Completed)
                .Finalize());
    }
}
```

---

## RabbitMQ Configuration

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

### Configuration Keys

| Key                              | Description              | Default     |
| -------------------------------- | ------------------------ | ----------- |
| `ConnectionStrings:RabbitMqHost` | RabbitMQ server hostname | `localhost` |
| `RabbitMq:Username`              | Authentication username  | `dhadgar`   |
| `RabbitMq:Password`              | Authentication password  | `dhadgar`   |

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

**Production** (via environment variables):

```bash
ConnectionStrings__RabbitMqHost=rabbitmq.production.svc.cluster.local
RabbitMq__Username=produser
RabbitMq__Password=<secret>
```

### Local Development with Docker Compose

Start RabbitMQ and other infrastructure:

```bash
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

Access RabbitMQ Management UI:

- **URL**: http://localhost:15672
- **Credentials**: dhadgar / dhadgar

RabbitMQ Ports:

- **5672**: AMQP protocol (for services)
- **15672**: Management UI (for debugging/monitoring)

---

## Error Handling and Retry Policies

### Current Implementation

The current implementation provides basic MassTransit configuration. Advanced error handling is planned:

| Feature                    | Status              |
| -------------------------- | ------------------- |
| Basic message delivery     | Implemented         |
| Automatic error queues     | MassTransit default |
| Retry policies             | Planned             |
| Circuit breaker            | Planned             |
| Dead letter queue handling | Planned             |
| Outbox pattern             | Planned             |

### Automatic Error Queues

MassTransit automatically creates error queues (`_error` suffix) for messages that fail. Failed messages are moved to these queues after exhausting retries.

### Exception Handling in Consumers

Handle exceptions carefully to distinguish transient from permanent failures:

```csharp
public async Task Consume(ConsumeContext<MyMessage> context)
{
    try
    {
        // Business logic
    }
    catch (TransientException ex)
    {
        // Re-throw for MassTransit to retry
        _logger.LogWarning(ex, "Transient failure, will retry");
        throw;
    }
    catch (PermanentException ex)
    {
        // Log and do not re-throw (message goes to error queue)
        _logger.LogError(ex, "Permanent failure, moving to error queue");
        // Do not throw - message will be moved to error queue
    }
}
```

### Planned: Retry Policies

```csharp
// Future implementation in MessagingExtensions.cs
cfg.UseMessageRetry(retry =>
{
    retry.Exponential(
        retryLimit: 5,
        minInterval: TimeSpan.FromSeconds(1),
        maxInterval: TimeSpan.FromMinutes(1),
        intervalDelta: TimeSpan.FromSeconds(2));

    // Do not retry on validation errors
    retry.Ignore<ValidationException>();
});
```

### Planned: Circuit Breaker

```csharp
// Future implementation
cfg.UseCircuitBreaker(cb =>
{
    cb.TrackingPeriod = TimeSpan.FromMinutes(1);
    cb.TripThreshold = 15;
    cb.ActiveThreshold = 10;
    cb.ResetInterval = TimeSpan.FromMinutes(5);
});
```

---

## Usage Examples

### Complete Service Setup

```csharp
using Dhadgar.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Add messaging with consumers
builder.Services.AddDhadgarMessaging(builder.Configuration, x =>
{
    x.AddConsumer<UserDeactivatedConsumer>();
    x.AddConsumer<OrgMembershipChangedConsumer>();
});

// Register publisher service
builder.Services.AddScoped<IIdentityEventPublisher, IdentityEventPublisher>();

var app = builder.Build();
app.Run();
```

### Environment-Aware Configuration

The Identity service demonstrates environment-aware messaging setup:

```csharp
if (app.Environment.IsDevelopment() &&
    string.IsNullOrEmpty(builder.Configuration.GetConnectionString("RabbitMqHost")))
{
    // Use in-memory transport when RabbitMQ is not configured
    builder.Services.AddMassTransit(x =>
    {
        x.UsingInMemory((ctx, cfg) =>
        {
            cfg.ConfigureEndpoints(ctx);
        });
    });
}
else
{
    builder.Services.AddDhadgarMessaging(builder.Configuration);
}
```

### Multi-Consumer Service

```csharp
builder.Services.AddDhadgarMessaging(builder.Configuration, x =>
{
    // Register multiple consumers
    x.AddConsumer<ServerCreatedConsumer>();
    x.AddConsumer<ServerDeletedConsumer>();
    x.AddConsumer<UsageReportedConsumer>();

    // Consumer definitions for advanced configuration
    x.AddConsumerDefinition<ServerCreatedConsumerDefinition>();
});
```

---

## Adding New Consumers and Publishers

### Adding a New Consumer

1. **Define the message contract** in `Dhadgar.Contracts`:

```csharp
// src/Shared/Dhadgar.Contracts/YourDomain/Events.cs
namespace Dhadgar.Contracts.YourDomain;

public record SomethingHappened(
    Guid EntityId,
    string Details,
    DateTimeOffset OccurredAtUtc);
```

2. **Create the consumer class** in your service:

```csharp
// src/Dhadgar.YourService/Consumers/SomethingHappenedConsumer.cs
using MassTransit;
using Dhadgar.Contracts.YourDomain;

public sealed class SomethingHappenedConsumer : IConsumer<SomethingHappened>
{
    private readonly ILogger<SomethingHappenedConsumer> _logger;

    public SomethingHappenedConsumer(ILogger<SomethingHappenedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SomethingHappened> context)
    {
        var message = context.Message;
        _logger.LogInformation("Processing {EventType} for {EntityId}",
            nameof(SomethingHappened), message.EntityId);

        // Handle the event
        await Task.CompletedTask;
    }
}
```

3. **Register the consumer** in `Program.cs`:

```csharp
builder.Services.AddDhadgarMessaging(builder.Configuration, x =>
{
    x.AddConsumer<SomethingHappenedConsumer>();
});
```

### Adding a New Publisher

1. **Define the message contract** (if not already defined)

2. **Create the publisher interface and implementation**:

```csharp
// Interface
public interface IYourDomainEventPublisher
{
    Task PublishSomethingHappenedAsync(SomethingHappened message, CancellationToken ct = default);
}

// Implementation
public sealed class YourDomainEventPublisher : IYourDomainEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public YourDomainEventPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public Task PublishSomethingHappenedAsync(SomethingHappened message, CancellationToken ct = default)
        => _publishEndpoint.Publish(message, ct);
}
```

3. **Register the publisher** in `Program.cs`:

```csharp
builder.Services.AddScoped<IYourDomainEventPublisher, YourDomainEventPublisher>();
```

### Message Contract Guidelines

1. **Use Records**: Immutable by default, value equality, with-expressions
2. **Include Timestamps**: Always include `DateTimeOffset OccurredAtUtc` for events
3. **Include IDs**: Reference entities by ID, not full objects
4. **Keep Minimal**: Include only what consumers need
5. **Document**: Add XML comments explaining when events are published

```csharp
/// <summary>
/// Published when a server is successfully provisioned and ready for use.
/// </summary>
/// <param name="ServerId">The unique identifier of the server.</param>
/// <param name="OrgId">The organization that owns the server.</param>
/// <param name="NodeId">The node where the server was provisioned.</param>
/// <param name="ConnectionInfo">Connection details for the server.</param>
public record ServerProvisioned(
    Guid ServerId,
    Guid OrgId,
    string NodeId,
    IReadOnlyDictionary<string, string> ConnectionInfo);
```

---

## Testing

### Unit Testing Consumers

Test consumers in isolation by mocking dependencies:

```csharp
using MassTransit;
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

For integration tests, use MassTransit's in-memory transport:

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.UseEnvironment("Testing");

    builder.ConfigureServices(services =>
    {
        // Remove real MassTransit registration
        services.RemoveAll<IBusControl>();
        services.RemoveAll<IBus>();

        // Use in-memory transport
        services.AddMassTransit(x =>
        {
            x.AddConsumer<SomeConsumer>();
            x.UsingInMemory((ctx, cfg) =>
            {
                cfg.ConfigureEndpoints(ctx);
            });
        });
    });
}
```

### Testing Event Publishing

Create a test double to capture published events:

```csharp
public sealed class TestEventPublisher : IYourDomainEventPublisher
{
    public ConcurrentQueue<SomethingHappened> Events { get; } = new();

    public Task PublishSomethingHappenedAsync(SomethingHappened message, CancellationToken ct = default)
    {
        Events.Enqueue(message);
        return Task.CompletedTask;
    }
}
```

Use in tests:

```csharp
[Fact]
public async Task Action_PublishesExpectedEvent()
{
    // Arrange
    var testPublisher = new TestEventPublisher();
    // Configure service with testPublisher

    // Act
    // Perform action that should publish event

    // Assert
    Assert.Single(testPublisher.Events);
    var published = testPublisher.Events.First();
    Assert.Equal(expectedId, published.EntityId);
}
```

---

## Dependencies

### NuGet Packages

| Package                | Purpose                                             |
| ---------------------- | --------------------------------------------------- |
| `MassTransit`          | Message bus abstraction and consumer infrastructure |
| `MassTransit.RabbitMQ` | RabbitMQ transport implementation                   |

Package versions are managed centrally in `Directory.Packages.props`:

```xml
<PackageVersion Include="MassTransit" Version="8.3.6" />
<PackageVersion Include="MassTransit.RabbitMQ" Version="8.3.6" />
```

### Project References

This library has **no project references** to maintain its position as a foundational building block.

### Services Using This Library

| Service       | Usage                                            |
| ------------- | ------------------------------------------------ |
| Identity      | Publishes authentication and organization events |
| Servers       | Publishes/consumes server provisioning messages  |
| Nodes         | Publishes node lifecycle, certificate, and capacity events |
| Tasks         | Orchestrates background tasks via messaging      |
| Notifications | Consumes events to send notifications            |
| Billing       | Consumes usage events for metering               |

### Nodes Service Events

The Nodes service publishes 15 events across three categories:

**Node Lifecycle Events:**
| Event | Exchange Name | When Published |
|-------|---------------|----------------|
| `NodeEnrolled` | `meridian.nodeenrolled` | New agent completes enrollment |
| `NodeOnline` | `meridian.nodeonline` | Node transitions to online state |
| `NodeOffline` | `meridian.nodeoffline` | Node misses heartbeat threshold (5 min) |
| `NodeDegraded` | `meridian.nodedegraded` | Node reports health issues (CPU/memory/disk > 90%) |
| `NodeRecovered` | `meridian.noderecovered` | Node recovers from degraded state |
| `NodeDecommissioned` | `meridian.nodedecommissioned` | Node is permanently removed |
| `NodeMaintenanceStarted` | `meridian.nodemaintenancestarted` | Node enters maintenance mode |
| `NodeMaintenanceEnded` | `meridian.nodemaintenanceended` | Node exits maintenance mode |

**Certificate Events:**
| Event | Exchange Name | When Published |
|-------|---------------|----------------|
| `AgentCertificateIssued` | `meridian.agentcertificateissued` | mTLS cert issued during enrollment |
| `AgentCertificateRevoked` | `meridian.agentcertificaterevoked` | Certificate manually revoked |
| `AgentCertificateRenewed` | `meridian.agentcertificaterenewed` | Certificate renewed before expiry |

**Capacity Events:**
| Event | Exchange Name | When Published |
|-------|---------------|----------------|
| `CapacityReserved` | `meridian.capacityreserved` | Resource reservation created |
| `CapacityClaimed` | `meridian.capacityclaimed` | Reservation bound to server |
| `CapacityReleased` | `meridian.capacityreleased` | Reservation explicitly released |
| `CapacityReservationExpired` | `meridian.capacityreservationexpired` | Reservation timeout |

**Example Consumer:**
```csharp
public sealed class NodeOfflineConsumer : IConsumer<NodeOffline>
{
    private readonly IAlertService _alerts;

    public NodeOfflineConsumer(IAlertService alerts) => _alerts = alerts;

    public async Task Consume(ConsumeContext<NodeOffline> context)
    {
        var message = context.Message;
        await _alerts.SendNodeOfflineAlertAsync(
            message.NodeId,
            message.Timestamp,
            message.Reason);
    }
}
```

---

## Related Documentation

| Document                                  | Description                                              |
| ----------------------------------------- | -------------------------------------------------------- |
| `/src/Shared/Dhadgar.Messaging/README.md` | Library-specific README with full implementation details |
| `/src/Shared/Dhadgar.Contracts/`          | Message contracts and DTOs                               |
| `/deploy/compose/README.md`               | Local infrastructure setup including RabbitMQ            |
| `/.claude/agents/messaging-engineer.md`   | AI agent for messaging guidance                          |

### External Resources

| Resource                  | URL                                                     |
| ------------------------- | ------------------------------------------------------- |
| MassTransit Documentation | https://masstransit.io/documentation                    |
| MassTransit Consumers     | https://masstransit.io/documentation/concepts/consumers |
| MassTransit Producers     | https://masstransit.io/documentation/concepts/producers |
| MassTransit Testing       | https://masstransit.io/documentation/concepts/testing   |
| RabbitMQ Documentation    | https://www.rabbitmq.com/documentation.html             |

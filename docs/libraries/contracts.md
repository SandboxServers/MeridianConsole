# Dhadgar.Contracts Library

The shared contracts library providing DTOs, message contracts, and service interfaces for the Meridian Console platform.

**Location:** `src/Shared/Dhadgar.Contracts/`

---

## Table of Contents

1. [Purpose and Design Philosophy](#purpose-and-design-philosophy)
2. [Directory Structure](#directory-structure)
3. [DTO Naming Conventions](#dto-naming-conventions)
4. [Message Contract Patterns](#message-contract-patterns)
5. [API Model Structure](#api-model-structure)
6. [Versioning Strategy](#versioning-strategy)
7. [Usage Examples](#usage-examples)
8. [Guidelines for Adding New Contracts](#guidelines-for-adding-new-contracts)

---

## Purpose and Design Philosophy

### Why a Separate Contracts Library?

In a microservices architecture, services must communicate without creating compile-time coupling. `Dhadgar.Contracts` serves as the **boundary definition layer** that enables this separation:

1. **Decoupling Services**: Services reference `Dhadgar.Contracts` instead of each other. Service A never has a `ProjectReference` to Service B.

2. **Contract-First Design**: Contracts are defined first, then services implement producers and consumers independently.

3. **Versioning Control**: All breaking changes are visible in one place, making it easier to assess impact.

4. **Type Safety**: Unlike loosely-typed JSON schemas, C# records provide compile-time validation and IDE support.

5. **Message Serialization**: MassTransit uses these types for message serialization/deserialization on the bus.

### Core Principles

- **Zero External Dependencies**: The library depends only on the .NET Base Class Library. No NuGet packages.
- **Immutability**: All contracts are `record` types, ensuring immutability.
- **Pure Data**: No business logic beyond computed properties (like `TotalPages` in pagination).
- **Domain Organization**: Contracts are organized by the service that owns them.

### Architectural Constraint

From the project's CLAUDE.md:

> Services MUST NOT reference each other via `ProjectReference`. This prevents distributed monolith anti-patterns.

**Allowed dependencies for any service:**

- `Dhadgar.Contracts` - DTOs and message contracts (this library)
- `Dhadgar.Shared` - Utilities and primitives
- `Dhadgar.Messaging` - MassTransit/RabbitMQ conventions
- `Dhadgar.ServiceDefaults` - Common middleware and service wiring

---

## Directory Structure

```text
Dhadgar.Contracts/
├── Dhadgar.Contracts.csproj    # Project file (minimal, no package refs)
├── CLAUDE.md                   # AI assistant context file
├── README.md                   # Detailed reference documentation
├── Hello.cs                    # Smoke-test constant
├── Pagination.cs               # Generic pagination request/response
├── Identity/
│   ├── IdentityEvents.cs       # MassTransit message contracts (16 events)
│   └── IdentityServiceContracts.cs  # Service client interface and DTOs
└── Servers/
    └── Contracts.cs            # Server provisioning contracts
```

### Organization by Domain

Contracts are organized into folders by **domain** (the service that owns them):

| Folder       | Owner Service    | Contents                              |
| ------------ | ---------------- | ------------------------------------- |
| `/Identity/` | Identity Service | User, org, membership events and DTOs |
| `/Nodes/`    | Nodes Service    | Node lifecycle, certificate, and capacity events |
| `/Servers/`  | Servers Service  | Server provisioning contracts         |
| Root level   | Cross-cutting    | Pagination, utilities                 |

When adding contracts for a new domain (e.g., Billing), create a new folder: `/Billing/`.

---

## DTO Naming Conventions

### General Rules

| Type                   | Naming Pattern                        | Examples                                         |
| ---------------------- | ------------------------------------- | ------------------------------------------------ |
| **Data carriers**      | Suffix with `Info`                    | `UserInfo`, `OrganizationInfo`, `MembershipInfo` |
| **Request objects**    | Suffix with `Request`                 | `PaginationRequest`, `MemberInviteRequest`       |
| **Response wrappers**  | Suffix with `Response`                | `PagedResponse<T>`, `MemberClaimsResponse`       |
| **Service interfaces** | Prefix with `I`, suffix with `Client` | `IIdentityServiceClient`                         |
| **Constants classes**  | Descriptive plural nouns              | `MembershipChangeTypes`                          |
| **Value types**        | Use the entity name + `Id`            | `ServerId`                                       |

### Record vs Class

**Always use `record` types** for contracts:

```csharp
// Correct - immutable record
public sealed record UserInfo(
    Guid Id,
    string Email,
    string? DisplayName,
    bool IsActive);

// Avoid - mutable class
public class UserInfo { ... }
```

Records provide:

- Immutability by default
- Value-based equality
- Concise syntax for DTOs
- Built-in `ToString()` for debugging

### Nullability

Use nullable reference types (`string?`) for truly optional fields:

```csharp
public sealed record UserInfo(
    Guid Id,              // Required - never null
    string Email,         // Required - never null
    string? DisplayName,  // Optional - may be null
    bool IsActive);
```

---

## Message Contract Patterns

### Events vs Commands

The library distinguishes between two types of messages:

| Type         | Tense                | Purpose                | Naming Example                             |
| ------------ | -------------------- | ---------------------- | ------------------------------------------ |
| **Events**   | Past tense           | Describe what happened | `UserAuthenticated`, `OrganizationCreated` |
| **Commands** | Imperative/Requested | Request an action      | `ServerProvisionRequested`                 |

### Event Structure

All events should include:

1. **Entity identifiers**: The ID(s) of affected entities
2. **Timestamp**: When the event occurred (`DateTimeOffset OccurredAtUtc`)
3. **Actor**: Who triggered the event (when applicable)
4. **Relevant data**: Minimal data needed by consumers

```csharp
public record OrganizationCreated(
    Guid OrganizationId,        // Primary entity
    Guid OwnerId,               // Related entity
    string Name,                // Essential data
    string Slug,                // Essential data
    DateTimeOffset OccurredAtUtc);  // Timestamp
```

### Command Structure

Commands typically include:

1. **Target identifier**: What entity to act upon
2. **Required parameters**: Data needed to execute the action
3. **Optional parameters**: Nullable fields for optional behavior

```csharp
public record ServerProvisionRequested(
    Guid ServerId,      // Target
    Guid OrgId,         // Context
    string GameType,    // Required param
    int CpuLimit,       // Required param
    int MemoryMb);      // Required param
```

### Collection Types

For collections in contracts, use:

| Type                       | When to Use                       |
| -------------------------- | --------------------------------- |
| `IReadOnlyCollection<T>`   | Most cases - signals immutability |
| `IReadOnlyDictionary<K,V>` | Key-value pairs                   |

```csharp
public record UserAuthenticated(
    Guid UserId,
    IReadOnlyCollection<string> Permissions,  // Not List<string>
    DateTimeOffset OccurredAtUtc);

public record ServerProvisioned(
    Guid ServerId,
    IReadOnlyDictionary<string, string> ConnectionInfo);  // Key-value pairs
```

### Constants for String Values

Use constant classes instead of enums for message values:

```csharp
public static class MembershipChangeTypes
{
    public const string Invited = "invited";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Removed = "removed";
}
```

**Why constants over enums?**

- Easier serialization/deserialization across services
- More resilient to version mismatches
- Human-readable in logs and message queues
- Can be extended without breaking existing consumers

---

## API Model Structure

### Pagination

The library provides standard pagination types used across all list endpoints:

#### PaginationRequest

```csharp
public sealed record PaginationRequest
{
    public int Page { get; init; } = 1;           // 1-based page number
    public int Limit { get; init; } = 50;         // Items per page (max 100)
    public string? Sort { get; init; }            // Sort field
    public string Order { get; init; } = "asc";   // Sort direction

    // Computed properties for database queries:
    public int Skip => (NormalizedPage - 1) * NormalizedLimit;
    public int NormalizedPage => Math.Max(1, Page);
    public int NormalizedLimit => Math.Clamp(Limit, 1, 100);
    public bool IsAscending => !string.Equals(Order, "desc", StringComparison.OrdinalIgnoreCase);
}
```

#### PagedResponse<T>

```csharp
public sealed record PagedResponse<T>
{
    public required IReadOnlyCollection<T> Items { get; init; }
    public required int Page { get; init; }
    public required int Limit { get; init; }
    public required int Total { get; init; }

    // Computed properties:
    public int TotalPages => (int)Math.Ceiling((double)Total / Limit);
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;

    // Factory method:
    public static PagedResponse<T> Create(
        IReadOnlyCollection<T> items,
        int total,
        PaginationRequest pagination);
}
```

### Service Client Interfaces

For service-to-service communication, define interfaces in Contracts:

```csharp
public interface IIdentityServiceClient
{
    Task<UserInfo?> GetUserAsync(Guid userId, CancellationToken ct = default);
    Task<bool> UserHasPermissionAsync(Guid userId, Guid orgId, string permission, CancellationToken ct = default);
    // ... other methods
}
```

Services implement these interfaces using HTTP clients to call internal endpoints.

---

## Versioning Strategy

### Semantic Versioning Principles

| Change Type     | Version Bump | Example                                 |
| --------------- | ------------ | --------------------------------------- |
| **Breaking**    | MAJOR        | Removing a field, changing a type       |
| **New feature** | MINOR        | Adding a new contract or optional field |
| **Bug fix**     | PATCH        | Documentation, computed property fixes  |

### Backward-Compatible Changes

**Safe to add:**

- New contracts (events, commands, DTOs)
- New nullable fields to existing contracts
- New computed properties

```csharp
// Original
public record UserCreated(Guid UserId, string Email, DateTimeOffset OccurredAtUtc);

// Evolved (backward compatible)
public record UserCreated(
    Guid UserId,
    string Email,
    string? DisplayName,  // New optional field
    DateTimeOffset OccurredAtUtc);
```

### Breaking Changes

When unavoidable, follow this process:

1. **Create a versioned contract**: `UserCreatedV2` alongside `UserCreated`
2. **Deprecate the old**: Add `[Obsolete("Use UserCreatedV2")]`
3. **Migrate consumers gradually**: Coordinate with teams
4. **Remove after deprecation period**: Only after all consumers migrate

```csharp
[Obsolete("Use UserCreatedV2 - this version will be removed in v3.0")]
public record UserCreated(...);

public record UserCreatedV2(...);  // New version
```

### Exchange Name Implications

MassTransit generates exchange names from type names:

- `UserAuthenticated` -> `meridian.userauthenticated`

**Renaming a type changes the exchange name** and is a breaking change.

---

## Usage Examples

### Adding a Project Reference

```xml
<!-- In your service's .csproj -->
<ItemGroup>
  <ProjectReference Include="../Shared/Dhadgar.Contracts/Dhadgar.Contracts.csproj" />
</ItemGroup>
```

### Using Pagination in an Endpoint

From `Dhadgar.Identity/Endpoints/MembershipEndpoints.cs`:

```csharp
using Dhadgar.Contracts;

private static async Task<IResult> ListMembers(
    HttpContext context,
    Guid organizationId,
    MembershipService membershipService,
    int? page,
    int? limit,
    CancellationToken ct)
{
    // Create pagination request with defaults
    var pagination = new PaginationRequest { Page = page ?? 1, Limit = limit ?? 50 };

    var allMembers = await membershipService.ListMembersAsync(organizationId, ct);

    // Apply pagination
    var pagedMembers = allMembers
        .Skip(pagination.Skip)
        .Take(pagination.NormalizedLimit)
        .ToArray();

    // Return standardized paginated response
    return Results.Ok(PagedResponse<MemberSummary>.Create(pagedMembers, allMembers.Count, pagination));
}
```

### Publishing Events with MassTransit

From `Dhadgar.Identity/Services/IdentityEventPublisher.cs`:

```csharp
using Dhadgar.Contracts.Identity;
using MassTransit;

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
}
```

### Creating and Publishing an Event

```csharp
var evt = new UserAuthenticated(
    UserId: user.Id,
    OrganizationId: orgId,
    ExternalAuthId: user.ExternalAuthId,
    Email: user.Email,
    ClientApp: "web",
    Permissions: permissions.ToList(),
    OccurredAtUtc: DateTimeOffset.UtcNow
);

await _eventPublisher.PublishUserAuthenticatedAsync(evt, ct);
```

### Consuming Events

```csharp
using Dhadgar.Contracts.Identity;
using MassTransit;

public class UserAuthenticatedConsumer : IConsumer<UserAuthenticated>
{
    private readonly ILogger<UserAuthenticatedConsumer> _logger;

    public UserAuthenticatedConsumer(ILogger<UserAuthenticatedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<UserAuthenticated> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "User {UserId} authenticated at {OccurredAt}",
            message.UserId,
            message.OccurredAtUtc);

        // Your business logic here...
    }
}
```

### Using Constants

```csharp
using Dhadgar.Contracts.Identity;

var changeType = membership.Status switch
{
    MembershipStatus.Invited => MembershipChangeTypes.Invited,
    MembershipStatus.Active => MembershipChangeTypes.Accepted,
    MembershipStatus.Removed => MembershipChangeTypes.Removed,
    _ => throw new InvalidOperationException()
};

var evt = new OrgMembershipChanged(
    OrganizationId: orgId,
    UserId: userId,
    MembershipId: membershipId,
    ChangeType: changeType,  // Uses the constant
    Role: null,
    ClaimType: null,
    ClaimValue: null,
    ResourceType: null,
    ResourceId: null,
    ActorUserId: actorId,
    OccurredAtUtc: DateTimeOffset.UtcNow
);
```

---

## Guidelines for Adding New Contracts

### Checklist

Before adding a new contract:

- [ ] Determine the owning domain (Identity, Servers, Billing, etc.)
- [ ] Choose the appropriate type (event, command, DTO, interface)
- [ ] Follow naming conventions
- [ ] Use `record` types
- [ ] Include `OccurredAtUtc` for events
- [ ] Add XML documentation comments
- [ ] Consider backward compatibility

### Step-by-Step Process

1. **Determine the domain folder**

   Place the contract in the appropriate folder:
   - Existing domain: Use that folder (e.g., `/Identity/`)
   - New domain: Create a new folder (e.g., `/Billing/`)
   - Cross-cutting: Place at root level

2. **Choose the contract type**

   | If you need...                     | Create a...                     |
   | ---------------------------------- | ------------------------------- |
   | Notify services something happened | Event record                    |
   | Request an async action            | Command record                  |
   | Transfer data in HTTP APIs         | DTO record                      |
   | Define service-to-service contract | Interface + DTOs                |
   | Share string constants             | Static class with const strings |

3. **Define the contract**

   ```csharp
   namespace Dhadgar.Contracts.Billing;

   /// <summary>
   /// Published when a subscription is created for an organization.
   /// </summary>
   public record SubscriptionCreated(
       Guid SubscriptionId,
       Guid OrganizationId,
       string PlanId,
       decimal MonthlyAmount,
       DateTimeOffset StartsAt,
       DateTimeOffset OccurredAtUtc);
   ```

4. **Add XML documentation**

   Every public type should have a `<summary>` explaining its purpose.

5. **Consider consumers**
   - Who will consume this contract?
   - What data do they need?
   - Is all the data necessary, or is some redundant?

### Anti-Patterns to Avoid

| Anti-Pattern                  | Why It's Bad                    | Do This Instead                   |
| ----------------------------- | ------------------------------- | --------------------------------- |
| Adding business logic         | Contracts are pure data         | Put logic in services             |
| Referencing external packages | Creates transitive dependencies | Keep Contracts dependency-free    |
| Using `List<T>`               | Mutable, implies ownership      | Use `IReadOnlyCollection<T>`      |
| Using classes                 | Mutable, complex equality       | Use `record` types                |
| Embedding database entities   | Couples to storage              | Create separate DTOs              |
| Skipping timestamps           | Loses audit trail               | Include `OccurredAtUtc` on events |

### Testing Contracts

Add tests in `tests/Dhadgar.Contracts.Tests/`:

```csharp
public class PaginationTests
{
    [Fact]
    public void PaginationRequest_NormalizesNegativePage()
    {
        var request = new PaginationRequest { Page = -1 };
        Assert.Equal(1, request.NormalizedPage);
    }

    [Fact]
    public void PaginationRequest_ClampsTooHighLimit()
    {
        var request = new PaginationRequest { Limit = 500 };
        Assert.Equal(100, request.NormalizedLimit);
    }
}
```

---

## Quick Reference

### Current Contract Inventory

| Category            | Count   | Location                                |
| ------------------- | ------- | --------------------------------------- |
| Pagination types    | 2       | `/Pagination.cs`                        |
| Identity events     | 16      | `/Identity/IdentityEvents.cs`           |
| Identity DTOs       | 4       | `/Identity/IdentityServiceContracts.cs` |
| Identity interfaces | 1       | `/Identity/IdentityServiceContracts.cs` |
| Identity constants  | 1 class | `/Identity/IdentityEvents.cs`           |
| Nodes events        | 15      | `/Nodes/NodeEvents.cs`                  |
| Nodes commands      | 2       | `/Nodes/NodeCommands.cs`                |
| Server contracts    | 3       | `/Servers/Contracts.cs`                 |
| Utility types       | 1       | `/Hello.cs`                             |

### Nodes Contracts

**Node Lifecycle Events** (`/Nodes/NodeEvents.cs`):
- `NodeEnrolled` - New node completes enrollment
- `NodeOnline` - Node comes online (first heartbeat)
- `NodeOffline` - Node misses heartbeat threshold
- `NodeDegraded` - Node reports health issues
- `NodeRecovered` - Node recovers from degraded state
- `NodeDecommissioned` - Node permanently removed
- `NodeMaintenanceStarted` - Node enters maintenance mode
- `NodeMaintenanceEnded` - Node exits maintenance mode

**Certificate Events** (`/Nodes/NodeEvents.cs`):
- `AgentCertificateIssued` - Certificate issued during enrollment
- `AgentCertificateRevoked` - Certificate revoked
- `AgentCertificateRenewed` - Certificate renewed

**Capacity Events** (`/Nodes/NodeEvents.cs`):
- `CapacityReserved` - Capacity reservation created
- `CapacityClaimed` - Reservation claimed by server
- `CapacityReleased` - Reservation explicitly released
- `CapacityReservationExpired` - Reservation expired without claim

**Commands** (`/Nodes/NodeCommands.cs`):
- `CheckNodeHealth` - Request health check for a node
- `UpdateNodeCapacity` - Update node's capacity configuration

### Consuming Services

All services in the platform reference `Dhadgar.Contracts`:

- Gateway, Identity, BetterAuth, Billing, Servers, Nodes, Tasks
- Files, Mods, Console, Notifications, Secrets, Discord
- CLI tool, Agent.Core, Agent.Linux, Agent.Windows

---

## Related Documentation

- **[README in Contracts](/src/Shared/Dhadgar.Contracts/README.md)** - Complete API reference
- **[Dhadgar.Messaging](/src/Shared/Dhadgar.Messaging/README.md)** - MassTransit configuration
- **[Dhadgar.ServiceDefaults](/src/Shared/Dhadgar.ServiceDefaults/README.md)** - Common middleware
- **[MassTransit Message Contracts](https://masstransit.io/documentation/concepts/messages)** - External docs

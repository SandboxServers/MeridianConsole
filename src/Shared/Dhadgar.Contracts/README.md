# Dhadgar.Contracts

**The shared contracts library for the Meridian Console platform.**

This library serves as the canonical source for all Data Transfer Objects (DTOs), message contracts, service interfaces, and shared constants used across the Dhadgar microservices architecture. It is the **only** compile-time dependency allowed between services (other than `Dhadgar.Shared` for utilities and `Dhadgar.Messaging` for MassTransit configuration).

---

## Table of Contents

1. [Overview](#overview)
2. [Purpose and Architectural Role](#purpose-and-architectural-role)
3. [Contents Summary](#contents-summary)
4. [Project Organization](#project-organization)
5. [Usage Examples](#usage-examples)
6. [API Contracts Reference](#api-contracts-reference)
7. [Message Contracts Reference](#message-contracts-reference)
8. [Service Client Interfaces](#service-client-interfaces)
9. [Constants Reference](#constants-reference)
10. [Best Practices](#best-practices)
11. [Versioning Strategy](#versioning-strategy)
12. [Dependencies](#dependencies)
13. [Consuming Services](#consuming-services)
14. [Related Documentation](#related-documentation)

---

## Overview

`Dhadgar.Contracts` is a .NET class library containing shared types that define the "contract" between services in the Meridian Console platform. This includes:

- **Request/Response DTOs** for HTTP API endpoints
- **Message Contracts** for asynchronous communication via MassTransit/RabbitMQ
- **Service Client Interfaces** for service-to-service communication
- **Pagination Types** for consistent list/collection responses
- **Constants** for shared string values that must remain synchronized

The library intentionally has **zero external dependencies** beyond the .NET Base Class Library. This keeps it lightweight and ensures it can be referenced by any service without pulling in transitive dependencies.

**Target Framework:** .NET 10.0

**Namespace:** `Dhadgar.Contracts` (with domain-specific sub-namespaces like `Dhadgar.Contracts.Identity` and `Dhadgar.Contracts.Servers`)

---

## Purpose and Architectural Role

### Why Separate Contracts?

In a microservices architecture, services must communicate without creating compile-time coupling. The `Dhadgar.Contracts` library serves as the **boundary definition layer** that enables this:

1. **Decoupling Services**: Services reference `Dhadgar.Contracts` instead of each other. Service A never has a `ProjectReference` to Service B.

2. **Contract-First Design**: Contracts are defined first, then services implement producers and consumers independently.

3. **Versioning Control**: All breaking changes are visible in one place, making it easier to assess impact.

4. **Type Safety**: Unlike loosely-typed JSON schemas, C# records provide compile-time validation and IDE support.

5. **Message Serialization**: MassTransit uses these types for message serialization/deserialization on the bus.

### The Microservices Boundary Rule

**Critical architectural constraint from CLAUDE.md:**

> Services MUST NOT reference each other via `ProjectReference`. This prevents distributed monolith anti-patterns.

**Allowed dependencies for any service:**
- `Dhadgar.Contracts` - DTOs and message contracts (this library)
- `Dhadgar.Shared` - Utilities and primitives
- `Dhadgar.Messaging` - MassTransit/RabbitMQ conventions
- `Dhadgar.ServiceDefaults` - Common middleware and service wiring

**Runtime communication happens via:**
- HTTP APIs (typed clients calling endpoints)
- Async messaging (MassTransit publish/subscribe via RabbitMQ)

This library enables both communication styles by providing the shared types.

---

## Contents Summary

| Category | Count | Description |
|----------|-------|-------------|
| **Pagination Types** | 2 | Standard request/response wrappers for paginated endpoints |
| **Identity Events** | 16 | MassTransit message contracts for identity domain events |
| **Identity DTOs** | 4 | Service-to-service data transfer objects |
| **Identity Constants** | 1 class | Membership change type string constants |
| **Identity Interfaces** | 1 | Service client interface for Identity service |
| **Server Contracts** | 3 | Server provisioning message contracts and value types |
| **Utility Types** | 1 | Hello world smoke-test constant |

---

## Project Organization

```
Dhadgar.Contracts/
├── Dhadgar.Contracts.csproj    # Project file (minimal, no package refs)
├── CLAUDE.md                   # AI assistant context file
├── README.md                   # This file
├── Hello.cs                    # Smoke-test constant
├── Pagination.cs               # Generic pagination request/response
├── Identity/
│   ├── IdentityEvents.cs       # MassTransit message contracts
│   └── IdentityServiceContracts.cs  # Service client interface and DTOs
└── Servers/
    └── Contracts.cs            # Server provisioning contracts
```

### Naming Conventions

- **Events**: Past tense, describing what happened (e.g., `UserAuthenticated`, `OrganizationCreated`)
- **Commands**: Imperative, describing what to do (e.g., `ServerProvisionRequested`)
- **DTOs**: Suffixed with `Info`, `Request`, `Response`, or descriptive noun (e.g., `UserInfo`, `PaginationRequest`)
- **Interfaces**: Prefixed with `I`, suffixed with `Client` for service clients (e.g., `IIdentityServiceClient`)

### Folder Structure

Contracts are organized by **domain** (Identity, Servers, etc.) to make it clear which service owns which contracts:

- `/Identity/` - Contracts owned by the Identity service
- `/Servers/` - Contracts owned by the Servers service
- Root level - Cross-cutting contracts used by multiple services (pagination, utilities)

---

## Usage Examples

### Referencing the Library

Add a project reference in your service's `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\Shared\Dhadgar.Contracts\Dhadgar.Contracts.csproj" />
</ItemGroup>
```

### Using Pagination in an Endpoint

```csharp
using Dhadgar.Contracts;

// In your endpoint handler:
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

    // Get all items
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

### Publishing a Message with MassTransit

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

// Creating and publishing an event:
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

### Consuming a Message

```csharp
using Dhadgar.Contracts.Identity;
using MassTransit;

public class UserAuthenticatedConsumer : IConsumer<UserAuthenticated>
{
    public async Task Consume(ConsumeContext<UserAuthenticated> context)
    {
        var message = context.Message;

        // Handle the event
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

// Instead of magic strings:
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
    // ... other properties
);
```

---

## API Contracts Reference

### Pagination Types

Located in `/Pagination.cs`

#### `PaginationRequest`

Standard pagination request parameters for list endpoints.

```csharp
public sealed record PaginationRequest
{
    public int Page { get; init; } = 1;           // Page number (1-based)
    public int Limit { get; init; } = 50;         // Items per page (default 50, max 100)
    public string? Sort { get; init; }            // Sort field
    public string Order { get; init; } = "asc";   // Sort order (asc or desc)

    // Computed properties:
    public int Skip => (NormalizedPage - 1) * NormalizedLimit;  // For database queries
    public int NormalizedPage => Math.Max(1, Page);             // Ensures minimum 1
    public int NormalizedLimit => Math.Clamp(Limit, 1, 100);    // Clamps to valid range
    public bool IsAscending => !string.Equals(Order, "desc", StringComparison.OrdinalIgnoreCase);
}
```

**Use cases:**
- Accept pagination parameters from query strings
- Calculate skip count for EF Core `.Skip()` and `.Take()`
- Normalize invalid input (negative pages, excessive limits)

#### `PagedResponse<T>`

Standard paginated response wrapper for collections.

```csharp
public sealed record PagedResponse<T>
{
    public required IReadOnlyCollection<T> Items { get; init; }  // The page of items
    public required int Page { get; init; }                      // Current page number
    public required int Limit { get; init; }                     // Items per page
    public required int Total { get; init; }                     // Total items across all pages

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

**Example JSON response:**
```json
{
  "items": [...],
  "page": 1,
  "limit": 50,
  "total": 127,
  "totalPages": 3,
  "hasNext": true,
  "hasPrev": false
}
```

### Identity Service DTOs

Located in `/Identity/IdentityServiceContracts.cs`

#### `UserInfo`

Basic user information for service-to-service communication.

```csharp
public sealed record UserInfo(
    Guid Id,              // User's unique identifier
    string Email,         // User's email address
    string? DisplayName,  // User's display name (optional)
    bool IsActive);       // Whether the user account is active
```

#### `OrganizationInfo`

Basic organization information.

```csharp
public sealed record OrganizationInfo(
    Guid Id,          // Organization's unique identifier
    string Name,      // Organization display name
    string Slug,      // URL-safe identifier (e.g., "my-org")
    Guid OwnerId,     // User ID of the organization owner
    bool IsActive);   // Whether the organization is active
```

#### `OrganizationMemberInfo`

Member information within an organization.

```csharp
public sealed record OrganizationMemberInfo(
    Guid UserId,      // The member's user ID
    string Role,      // The member's role in the organization
    bool IsActive);   // Whether the membership is active
```

#### `MembershipInfo`

Detailed membership information for a user in an organization.

```csharp
public sealed record MembershipInfo(
    Guid UserId,           // The user's ID
    Guid OrganizationId,   // The organization's ID
    string? Role,          // The assigned role (null if no role)
    bool IsActive,         // Whether the membership is active
    DateTime JoinedAt);    // When the user joined
```

---

## Message Contracts Reference

Message contracts are used with MassTransit for publish/subscribe messaging via RabbitMQ. All messages are **events** (past tense) that describe something that has already happened.

### Identity Events

Located in `/Identity/IdentityEvents.cs`

#### Authentication Events

| Event | Description | Key Fields |
|-------|-------------|------------|
| `UserAuthenticated` | Fired when a user successfully authenticates | `UserId`, `OrganizationId`, `Email`, `Permissions`, `ClientApp` |
| `UserDeactivated` | Fired when a user account is deactivated | `UserId`, `ExternalAuthId`, `Reason` |

**`UserAuthenticated`**
```csharp
public record UserAuthenticated(
    Guid UserId,
    Guid OrganizationId,
    string ExternalAuthId,
    string Email,
    string? ClientApp,
    IReadOnlyCollection<string> Permissions,
    DateTimeOffset OccurredAtUtc);
```

**`UserDeactivated`**
```csharp
public record UserDeactivated(
    Guid UserId,
    string ExternalAuthId,
    string? Reason,
    DateTimeOffset OccurredAtUtc);
```

#### Organization Events

| Event | Description | Key Fields |
|-------|-------------|------------|
| `OrganizationCreated` | Fired when a new organization is created | `OrganizationId`, `OwnerId`, `Name`, `Slug` |
| `OrganizationUpdated` | Fired when organization details change | `OrganizationId`, `Name`, `UpdatedByUserId` |
| `OrganizationDeleted` | Fired when an organization is soft-deleted | `OrganizationId`, `DeletedByUserId`, `Reason` |
| `OrganizationOwnershipTransferred` | Fired when ownership changes | `OrganizationId`, `PreviousOwnerId`, `NewOwnerId` |

**`OrganizationCreated`**
```csharp
public record OrganizationCreated(
    Guid OrganizationId,
    Guid OwnerId,
    string Name,
    string Slug,
    DateTimeOffset OccurredAtUtc);
```

**`OrganizationUpdated`**
```csharp
public record OrganizationUpdated(
    Guid OrganizationId,
    string Name,
    Guid? UpdatedByUserId,
    DateTimeOffset OccurredAtUtc);
```

**`OrganizationDeleted`**
```csharp
public record OrganizationDeleted(
    Guid OrganizationId,
    Guid? DeletedByUserId,
    string? Reason,
    DateTimeOffset OccurredAtUtc);
```

**`OrganizationOwnershipTransferred`**
```csharp
public record OrganizationOwnershipTransferred(
    Guid OrganizationId,
    Guid PreviousOwnerId,
    Guid NewOwnerId,
    Guid TransferredByUserId,
    DateTimeOffset OccurredAtUtc);
```

#### Membership Events

| Event | Description | Key Fields |
|-------|-------------|------------|
| `OrgMembershipChanged` | Generic membership change event | `MembershipId`, `ChangeType`, `Role`, `ClaimType/Value` |
| `MemberLeftOrganization` | Fired when a member leaves | `UserId`, `OrganizationId`, `Reason` |
| `UserPermissionsChanged` | Fired when permissions change | `UserId`, `NewPermissions`, `ChangeReason` |

**`OrgMembershipChanged`**
```csharp
public record OrgMembershipChanged(
    Guid OrganizationId,
    Guid UserId,
    Guid MembershipId,
    string ChangeType,        // One of MembershipChangeTypes constants
    string? Role,
    string? ClaimType,
    string? ClaimValue,
    string? ResourceType,
    Guid? ResourceId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAtUtc);
```

**`MemberLeftOrganization`**
```csharp
public record MemberLeftOrganization(
    Guid UserId,
    Guid OrganizationId,
    string Reason,
    Guid? RemovedByUserId,
    DateTimeOffset OccurredAtUtc);
```

**`UserPermissionsChanged`**
```csharp
public record UserPermissionsChanged(
    Guid UserId,
    Guid OrganizationId,
    IReadOnlyCollection<string> NewPermissions,
    string ChangeReason,
    Guid? ChangedByUserId,
    DateTimeOffset OccurredAtUtc);
```

#### Invitation Events

| Event | Description | Key Fields |
|-------|-------------|------------|
| `InvitationRejected` | Fired when invitee rejects | `OrganizationId`, `UserId`, `MembershipId` |
| `InvitationWithdrawn` | Fired when inviter revokes | `OrganizationId`, `UserId`, `WithdrawnByUserId` |
| `InvitationExpired` | Fired when invitation expires | `OrganizationId`, `UserId`, `MembershipId` |

**`InvitationRejected`**
```csharp
public record InvitationRejected(
    Guid OrganizationId,
    Guid UserId,
    Guid MembershipId,
    DateTimeOffset OccurredAtUtc);
```

**`InvitationWithdrawn`**
```csharp
public record InvitationWithdrawn(
    Guid OrganizationId,
    Guid UserId,
    Guid MembershipId,
    Guid WithdrawnByUserId,
    DateTimeOffset OccurredAtUtc);
```

**`InvitationExpired`**
```csharp
public record InvitationExpired(
    Guid OrganizationId,
    Guid UserId,
    Guid MembershipId,
    DateTimeOffset OccurredAtUtc);
```

#### Role Events

| Event | Description | Key Fields |
|-------|-------------|------------|
| `CustomRoleCreated` | Fired when a custom role is created | `RoleId`, `OrganizationId`, `RoleName`, `Permissions` |
| `CustomRoleDeleted` | Fired when a custom role is deleted | `RoleId`, `OrganizationId`, `RoleName` |

**`CustomRoleCreated`**
```csharp
public record CustomRoleCreated(
    Guid RoleId,
    Guid OrganizationId,
    string RoleName,
    IReadOnlyCollection<string> Permissions,
    Guid CreatedByUserId,
    DateTimeOffset OccurredAtUtc);
```

**`CustomRoleDeleted`**
```csharp
public record CustomRoleDeleted(
    Guid RoleId,
    Guid OrganizationId,
    string RoleName,
    Guid DeletedByUserId,
    DateTimeOffset OccurredAtUtc);
```

#### OAuth Events

| Event | Description | Key Fields |
|-------|-------------|------------|
| `OAuthAccountLinked` | Fired when OAuth account is linked | `UserId`, `Provider`, `ProviderAccountId` |
| `OAuthAccountUnlinked` | Fired when OAuth account is unlinked | `UserId`, `Provider`, `ProviderAccountId` |

**`OAuthAccountLinked`**
```csharp
public record OAuthAccountLinked(
    Guid UserId,
    string Provider,
    string ProviderAccountId,
    DateTimeOffset OccurredAtUtc);
```

**`OAuthAccountUnlinked`**
```csharp
public record OAuthAccountUnlinked(
    Guid UserId,
    string Provider,
    string ProviderAccountId,
    DateTimeOffset OccurredAtUtc);
```

#### Account Lifecycle Events

| Event | Description | Key Fields |
|-------|-------------|------------|
| `UserDeletionRequested` | Fired when user requests account deletion | `UserId`, `Email`, `DeletionScheduledAt` |

**`UserDeletionRequested`**
```csharp
public record UserDeletionRequested(
    Guid UserId,
    string Email,
    DateTime DeletionScheduledAt,
    DateTimeOffset OccurredAtUtc);
```

### Server Contracts

Located in `/Servers/Contracts.cs`

#### Value Types

**`ServerId`**
```csharp
public record ServerId(Guid Value);
```
Strongly-typed identifier for servers. Prevents accidental mixing of GUIDs.

#### Server Provisioning Events

| Event | Description | Key Fields |
|-------|-------------|------------|
| `ServerProvisionRequested` | Command to provision a new server | `ServerId`, `OrgId`, `GameType`, `CpuLimit`, `MemoryMb` |
| `ServerProvisioned` | Event when server is successfully provisioned | `ServerId`, `NodeId`, `ConnectionInfo` |

**`ServerProvisionRequested`**
```csharp
public record ServerProvisionRequested(
    Guid ServerId,
    Guid OrgId,
    string GameType,
    int CpuLimit,
    int MemoryMb);
```

**`ServerProvisioned`**
```csharp
public record ServerProvisioned(
    Guid ServerId,
    Guid OrgId,
    string NodeId,
    IReadOnlyDictionary<string, string> ConnectionInfo);
```

---

## Service Client Interfaces

### `IIdentityServiceClient`

Located in `/Identity/IdentityServiceContracts.cs`

This interface defines the contract for service-to-service communication with the Identity service. Other microservices should implement this interface using an HTTP client to call Identity's `/internal/*` endpoints.

```csharp
public interface IIdentityServiceClient
{
    /// <summary>Get user information by ID.</summary>
    Task<UserInfo?> GetUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Get multiple users by their IDs (batch lookup).</summary>
    Task<Dictionary<Guid, UserInfo>> GetUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct = default);

    /// <summary>Get organization information by ID.</summary>
    Task<OrganizationInfo?> GetOrganizationAsync(
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>Check if an organization exists.</summary>
    Task<bool> OrganizationExistsAsync(
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>Get all active members of an organization.</summary>
    Task<IReadOnlyCollection<OrganizationMemberInfo>> GetOrganizationMembersAsync(
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>Check if a user has a specific permission in an organization.</summary>
    Task<bool> UserHasPermissionAsync(
        Guid userId,
        Guid organizationId,
        string permission,
        CancellationToken ct = default);

    /// <summary>Get all permissions for a user in an organization.</summary>
    Task<IReadOnlyCollection<string>> GetUserPermissionsAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>Get a user's membership info in an organization.</summary>
    Task<MembershipInfo?> GetMembershipAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default);
}
```

**Implementation pattern:**
```csharp
public class IdentityServiceClient : IIdentityServiceClient
{
    private readonly HttpClient _httpClient;

    public IdentityServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<UserInfo?> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/internal/users/{userId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserInfo>(ct);
    }

    // ... other methods
}
```

---

## Constants Reference

### `MembershipChangeTypes`

Located in `/Identity/IdentityEvents.cs`

String constants for the `ChangeType` field in `OrgMembershipChanged` events.

```csharp
public static class MembershipChangeTypes
{
    public const string Invited = "invited";          // User was invited to join
    public const string Accepted = "accepted";        // User accepted invitation
    public const string Rejected = "rejected";        // User rejected invitation
    public const string Withdrawn = "withdrawn";      // Inviter withdrew/revoked invitation
    public const string Expired = "expired";          // Invitation expired
    public const string Removed = "removed";          // Member was removed from organization
    public const string RoleAssigned = "role_assigned";    // Role was assigned to member
    public const string ClaimAdded = "claim_added";        // Custom claim was added
    public const string ClaimRemoved = "claim_removed";    // Custom claim was removed
}
```

**Why constants instead of enums?**

String constants are used instead of enums for message contracts because:
1. Easier serialization/deserialization across services
2. More resilient to version mismatches
3. Human-readable in logs and message queues
4. Can be extended without breaking existing consumers

### `Hello`

Located in `/Hello.cs`

Smoke-test constant for verifying the contracts library is properly referenced.

```csharp
public static class Hello
{
    public const string Message = "Hello from Dhadgar.Contracts";
}
```

---

## Best Practices

### Adding New Contracts

1. **Determine the domain**: Place the contract in the appropriate domain folder (`/Identity/`, `/Servers/`, etc.) or at the root if cross-cutting.

2. **Use records for immutability**: All contracts should be `record` types, not classes.

   ```csharp
   // Good
   public record MyEvent(Guid Id, string Name, DateTimeOffset OccurredAtUtc);

   // Avoid
   public class MyEvent { ... }
   ```

3. **Include timestamps**: All events should include `OccurredAtUtc` for auditing and ordering.

4. **Use `IReadOnlyCollection<T>`**: For collections, prefer `IReadOnlyCollection<T>` over `List<T>` or arrays.

5. **Make nullable explicit**: Use nullable reference types (`string?`) for truly optional fields.

6. **Document with XML comments**: Add `<summary>` comments explaining the contract's purpose.

### Naming Events

- **Use past tense**: Events describe something that happened (`UserCreated`, not `CreateUser`)
- **Be specific**: `InvitationExpired` is better than `MembershipChanged`
- **Include the aggregate**: `OrganizationCreated` makes the entity clear

### Naming Commands

- **Use imperative/requested form**: `ServerProvisionRequested`
- **Be specific about intent**: Describe what should happen

### Naming DTOs

- **Suffix with purpose**: `UserInfo`, `PaginationRequest`, `PagedResponse`
- **Match the API endpoint**: If the endpoint is `GET /users`, the response could be `UserInfo`

### Avoiding Breaking Changes

1. **Never remove fields** - mark as deprecated with `[Obsolete]` first
2. **Never rename fields** - add new field, deprecate old one
3. **Never change field types** - add new field with new type
4. **Add fields as nullable** - existing consumers won't break

### What NOT to Put in Contracts

- **Business logic**: Contracts are pure data, no methods beyond computed properties
- **Database entities**: Contracts are DTOs, not EF Core entities
- **Service implementations**: Only interfaces belong here
- **External package dependencies**: Keep this library dependency-free

---

## Versioning Strategy

### Semantic Versioning

The contracts library follows semantic versioning principles:

- **MAJOR**: Breaking changes (removing fields, changing types)
- **MINOR**: New contracts or fields (backward compatible)
- **PATCH**: Documentation, bug fixes in computed properties

### Handling Breaking Changes

When a breaking change is unavoidable:

1. **Create a new contract version**: `UserCreatedV2` alongside `UserCreated`
2. **Deprecate the old contract**: Add `[Obsolete("Use UserCreatedV2")]`
3. **Update consumers gradually**: Give teams time to migrate
4. **Remove after deprecation period**: Only remove after all consumers have migrated

### Message Contract Evolution

For MassTransit messages, use the envelope pattern for versioning:

```csharp
// Original
public record UserCreated(Guid UserId, string Email, DateTimeOffset OccurredAtUtc);

// Evolved - add new nullable field (backward compatible)
public record UserCreated(
    Guid UserId,
    string Email,
    string? DisplayName,  // New field, nullable for backward compat
    DateTimeOffset OccurredAtUtc);
```

### RabbitMQ Exchange Names

The `Dhadgar.Messaging` library formats exchange names using the pattern:

```
meridian.{typename}
```

For example, `UserAuthenticated` becomes `meridian.userauthenticated`.

This means renaming a type **changes the exchange name** and is a breaking change.

---

## Dependencies

**External packages:** None

**Project structure:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>Dhadgar.Contracts</AssemblyName>
    <RootNamespace>Dhadgar.Contracts</RootNamespace>
  </PropertyGroup>
</Project>
```

The library deliberately has no NuGet package references. It relies only on:
- .NET Base Class Library types (`Guid`, `DateTimeOffset`, `DateTime`)
- Collection interfaces (`IReadOnlyCollection<T>`, `IReadOnlyDictionary<TKey, TValue>`)

This ensures:
- No transitive dependency issues
- Fast build times
- No version conflicts across services
- Maximum compatibility

---

## Consuming Services

The following services reference `Dhadgar.Contracts`:

| Service | Purpose |
|---------|---------|
| `Dhadgar.Gateway` | API Gateway, routes requests |
| `Dhadgar.Identity` | Publishes identity events |
| `Dhadgar.Servers` | Server management |
| `Dhadgar.Nodes` | Node inventory |
| `Dhadgar.Tasks` | Task orchestration |
| `Dhadgar.Files` | File management |
| `Dhadgar.Mods` | Mod registry |
| `Dhadgar.Console` | Real-time console |
| `Dhadgar.Billing` | Subscription management |
| `Dhadgar.Notifications` | Email/webhook notifications |
| `Dhadgar.Secrets` | Secret storage |
| `Dhadgar.Discord` | Discord integration |
| `Dhadgar.Cli` | Command-line tool |
| `Dhadgar.Agent.Core` | Shared agent logic |
| `Dhadgar.Agent.Linux` | Linux agent |
| `Dhadgar.Agent.Windows` | Windows agent |

---

## Related Documentation

- **[CLAUDE.md](/CLAUDE.md)** - Main repository documentation with architecture overview
- **[Dhadgar.Messaging](/src/Shared/Dhadgar.Messaging/README.md)** - MassTransit configuration and conventions
- **[Dhadgar.ServiceDefaults](/src/Shared/Dhadgar.ServiceDefaults/README.md)** - Common middleware and service wiring
- **[Dhadgar.Shared](/src/Shared/Dhadgar.Shared/README.md)** - Utilities and primitives
- **[deploy/compose/README.md](/deploy/compose/README.md)** - Local development infrastructure including RabbitMQ

### MassTransit Documentation

- [MassTransit Message Contracts](https://masstransit.io/documentation/concepts/messages)
- [MassTransit Consumers](https://masstransit.io/documentation/concepts/consumers)

---

## Summary

`Dhadgar.Contracts` is the backbone of service-to-service communication in Meridian Console. By centralizing all shared types in one library:

- Services remain decoupled at compile time
- Message contracts are versioned consistently
- Breaking changes are visible and trackable
- Type safety is maintained across HTTP and messaging boundaries

When adding new features that require service communication, start by defining the contracts here first, then implement producers and consumers in the respective services.

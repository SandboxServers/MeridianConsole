# Dhadgar.Testing

Shared test infrastructure library for integration and unit tests across the Meridian Console platform.

## Overview

This library provides common testing utilities for:

- **Integration testing** with `WebApplicationFactory` and in-memory databases
- **Fake authentication** for testing authorization without real auth providers
- **Message capture** for testing MassTransit message publishing
- **HTTP client extensions** for simplified test setup

## Installation

Add a project reference to your test project:

```xml
<ItemGroup>
  <ProjectReference Include="../../src/Shared/Dhadgar.Testing/Dhadgar.Testing.csproj" />
</ItemGroup>
```

## Components

### 1. ServiceTestFixture

Base class for integration tests with Entity Framework Core.

**Features:**
- Creates unique in-memory database per test run
- Configures `WebApplicationFactory` with test services
- Implements `IAsyncLifetime` for xUnit test lifecycle
- Provides `ResetDatabaseAsync()` for test isolation

**Usage:**

```csharp
public class IdentityServiceFixture : ServiceTestFixture<Program, IdentityDbContext>
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // Add fake authentication
        services.AddFakeAuthentication();

        // Override any other services for testing
        services.Replace(ServiceDescriptor.Singleton<IEmailService, FakeEmailService>());
    }
}

public class UserControllerTests : IClassFixture<IdentityServiceFixture>
{
    private readonly IdentityServiceFixture _fixture;
    private readonly HttpClient _client;

    public UserControllerTests(IdentityServiceFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task GetUser_ReturnsUser_WhenAuthenticated()
    {
        // Arrange
        var userId = "test-user-123";
        _client.WithTestUser(userId);

        // Act
        var response = await _client.GetAsync($"/users/{userId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

### 2. Fake Authentication

Test authentication handler that reads user identity from HTTP headers.

**Headers:**
- `X-Test-User-Id` - Sets the authenticated user ID (required)
- `X-Test-Org-Id` - Sets the organization ID claim
- `X-Test-Role` - Sets the user role claim

**Claims created:**
- `ClaimTypes.NameIdentifier` - User ID
- `sub` - User ID (OIDC standard)
- `org_id` - Organization ID (if header present)
- `ClaimTypes.Role` - Role name (if header present)
- `role` - Role name (if header present)

**Setup:**

```csharp
protected override void ConfigureTestServices(IServiceCollection services)
{
    services.AddFakeAuthentication(); // Default scheme: "TestScheme"
    // or
    services.AddFakeAuthentication("CustomScheme");
}
```

**Usage with HttpClient extensions:**

```csharp
_client
    .WithTestUser("user-123")
    .WithTestOrg("org-456")
    .WithTestRole("Admin");

var response = await _client.GetAsync("/protected-resource");
```

### 3. InMemoryMessageCapture

Thread-safe utility for capturing and inspecting published messages in tests.

**Features:**
- Capture messages of any type
- Retrieve messages by type
- Check for message existence with predicates
- Async waiting for messages with timeout
- Thread-safe for concurrent test execution

**Usage:**

```csharp
public class MessagePublishingTests
{
    private readonly InMemoryMessageCapture _messageCapture = new();

    [Fact]
    public async Task CreateUser_PublishesUserCreatedEvent()
    {
        // Arrange
        var publisher = new FakePublisher(_messageCapture);
        var service = new UserService(publisher);

        // Act
        await service.CreateUserAsync("test@example.com");

        // Assert - wait for message with timeout
        var message = await _messageCapture.WaitForMessageAsync<UserCreatedEvent>(TimeSpan.FromSeconds(5));
        Assert.Equal("test@example.com", message.Email);

        // Or check synchronously
        Assert.True(_messageCapture.HasMessage<UserCreatedEvent>(m => m.Email == "test@example.com"));
    }

    [Fact]
    public void GetMessages_ReturnsAllCapturedMessages()
    {
        // Arrange
        _messageCapture.Capture(new UserCreatedEvent { Email = "user1@example.com" });
        _messageCapture.Capture(new UserCreatedEvent { Email = "user2@example.com" });
        _messageCapture.Capture(new OrgCreatedEvent { Name = "Org1" });

        // Act
        var userEvents = _messageCapture.GetMessages<UserCreatedEvent>();
        var orgEvents = _messageCapture.GetMessages<OrgCreatedEvent>();

        // Assert
        Assert.Equal(2, userEvents.Count);
        Assert.Single(orgEvents);
    }
}
```

### 4. HTTP Client Extensions

Fluent extension methods for setting test authentication headers.

**Methods:**
- `WithTestUser(string userId)` - Sets X-Test-User-Id header
- `WithTestOrg(string orgId)` - Sets X-Test-Org-Id header
- `WithTestRole(string role)` - Sets X-Test-Role header

**Usage:**

```csharp
// Chain multiple headers
var response = await _client
    .WithTestUser("user-123")
    .WithTestOrg("org-456")
    .WithTestRole("Admin")
    .GetAsync("/organizations/org-456/settings");

// Use individual headers
_client.WithTestUser("user-123");
var response = await _client.GetAsync("/users/user-123");
```

## Best Practices

### Test Isolation

Always reset the database between tests to ensure isolation:

```csharp
public async Task InitializeAsync()
{
    await _fixture.ResetDatabaseAsync();
}
```

Or use xUnit's `IAsyncLifetime`:

```csharp
public class UserControllerTests : IClassFixture<IdentityServiceFixture>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
```

### Organizing Test Fixtures

Create one fixture per service for reusability:

```csharp
// In tests/Dhadgar.Identity.Tests/Fixtures/IdentityServiceFixture.cs
public class IdentityServiceFixture : ServiceTestFixture<Program, IdentityDbContext>
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddFakeAuthentication();
    }
}
```

### Testing Authorization

```csharp
[Fact]
public async Task DeleteUser_Returns403_WhenNotAdmin()
{
    // Arrange - no admin role
    _client.WithTestUser("user-123");

    // Act
    var response = await _client.DeleteAsync("/users/other-user");

    // Assert
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}

[Fact]
public async Task DeleteUser_Returns204_WhenAdmin()
{
    // Arrange - with admin role
    _client.WithTestUser("admin-user").WithTestRole("Admin");

    // Act
    var response = await _client.DeleteAsync("/users/other-user");

    // Assert
    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
}
```

## Architecture Decisions

### Why In-Memory Database?

- **Fast**: No I/O overhead, tests run in milliseconds
- **Isolated**: Each test gets a fresh database
- **Simple**: No Docker or external dependencies needed
- **CI-friendly**: Works in any environment

For complex database features (stored procedures, triggers), consider using Testcontainers instead.

### Why Fake Authentication?

- **No dependencies**: No auth server or tokens needed
- **Flexible**: Set any user/org/role for testing
- **Clear**: Test setup shows exactly what identity is being used
- **Fast**: No cryptographic operations or network calls

### RemoveAll Pattern

The fixture uses `RemoveAll` instead of `SingleOrDefault` to handle both `AddDbContext` and `AddDbContextPool` patterns:

```csharp
services.RemoveAll(typeof(DbContextOptions<TDbContext>));
services.RemoveAll(typeof(TDbContext));
```

This ensures all DbContext registrations are removed before adding the test database.

## Examples

See the test projects for complete examples:

- `tests/Dhadgar.Identity.Tests/` - Integration tests with authentication
- `tests/Dhadgar.Nodes.Tests/` - Integration tests with message capture
- `tests/Dhadgar.Secrets.Tests/` - Authorization testing patterns

## Related Documentation

- [Testing Strategy](../../../docs/testing-strategy.md)
- [Integration Testing Guide](../../../docs/integration-testing.md)
- [xUnit Documentation](https://xunit.net/)
- [WebApplicationFactory Docs](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests)

# Testing Patterns

**Analysis Date:** 2026-01-19

## Test Framework

**Runner:**
- xUnit 2.9.2
- Config: Implicit via `.csproj` package references
- SDK: Microsoft.NET.Test.Sdk 17.12.0

**Assertion Libraries:**
- xUnit built-in `Assert.*` for most tests
- FluentAssertions 8.3.0 for more expressive assertions

**Mocking:**
- NSubstitute 5.3.0 for interface mocking

**Run Commands:**
```bash
dotnet test                                    # Run all tests
dotnet test --filter "FullyQualifiedName~Gateway"  # Run specific tests
dotnet test tests/Dhadgar.Gateway.Tests        # Run specific project
dotnet test /p:CollectCoverage=true            # Coverage via coverlet
```

## Test File Organization

**Location:**
- Separate `tests/` directory parallel to `src/`
- 1:1 mapping: `src/Dhadgar.{Service}` -> `tests/Dhadgar.{Service}.Tests`

**Naming:**
- `{ClassName}Tests.cs` for unit tests
- `{Feature}IntegrationTests.cs` for integration tests
- Subdirectories mirror source structure: `tests/Dhadgar.Secrets.Tests/Authorization/`, `tests/Dhadgar.Identity.Tests/Integration/`

**Structure:**
```
tests/
├── Dhadgar.Gateway.Tests/
│   ├── Dhadgar.Gateway.Tests.csproj
│   ├── HelloWorldTests.cs
│   ├── GatewayIntegrationTests.cs
│   ├── RateLimitingBehaviorTests.cs
│   └── SecurityTests.cs
├── Dhadgar.Identity.Tests/
│   ├── Dhadgar.Identity.Tests.csproj
│   ├── OrganizationServiceTests.cs
│   └── Integration/
│       ├── SecurityIntegrationTests.cs
│       └── AuthenticationFlowIntegrationTests.cs
└── Dhadgar.Secrets.Tests/
    ├── Authorization/
    │   └── SecretsAuthorizationServiceTests.cs
    ├── Validation/
    │   └── SecretNameValidatorTests.cs
    └── Security/
        └── SecretsSecurityIntegrationTests.cs
```

## Test Structure

**Suite Organization - Unit Tests:**
```csharp
// See: tests/Dhadgar.Secrets.Tests/Authorization/SecretsAuthorizationServiceTests.cs
public class SecretsAuthorizationServiceTests
{
    private readonly SecretsAuthorizationService _service;
    private readonly SecretsOptions _options;

    public SecretsAuthorizationServiceTests()
    {
        // Constructor-based setup (runs before each test)
        _options = new SecretsOptions { ... };
        _service = new SecretsAuthorizationService(
            OptionsFactory.Create(_options),
            NullLogger<SecretsAuthorizationService>.Instance);
    }

    #region Unauthenticated Access
    [Fact]
    public void Authorize_UnauthenticatedUser_ReturnsDenied()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());
        var result = _service.Authorize(user, "secret-name", SecretAction.Read);
        Assert.False(result.IsAuthorized);
    }
    #endregion

    #region Full Admin (secrets:*)
    // More tests...
    #endregion

    // Helper methods at bottom
    private static ClaimsPrincipal CreateUser(string userId, params string[] permissions) { ... }
}
```

**Suite Organization - Integration Tests:**
```csharp
// See: tests/Dhadgar.Gateway.Tests/GatewayIntegrationTests.cs
public class GatewayIntegrationTests : IClassFixture<GatewayWebApplicationFactory>
{
    private readonly HttpClient _client;

    public GatewayIntegrationTests(GatewayWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpointReturnsOk()
    {
        var response = await _client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

**Patterns:**
- `#region` blocks to organize related tests
- Constructor for per-test setup
- `IClassFixture<T>` for shared expensive setup (WebApplicationFactory)
- `IDisposable` for cleanup when needed
- Helper methods at bottom of test class

## Mocking

**Framework:** NSubstitute

**Interface Mocking:**
```csharp
// See: tests/Dhadgar.Discord.Tests/Consumers/SendDiscordNotificationConsumerTests.cs
private readonly IHttpClientFactory _httpClientFactory;
private readonly IConfiguration _configuration;
private readonly ILogger<SendDiscordNotificationConsumer> _logger;

public SendDiscordNotificationConsumerTests()
{
    _httpClientFactory = Substitute.For<IHttpClientFactory>();
    _configuration = Substitute.For<IConfiguration>();
    _logger = Substitute.For<ILogger<SendDiscordNotificationConsumer>>();
}

// Setup return values
_configuration["Discord:WebhookUrl"].Returns("https://discord.com/api/webhooks/test");
_httpClientFactory.CreateClient().Returns(mockHttpClient);
```

**MassTransit ConsumeContext Mocking:**
```csharp
var context = Substitute.For<ConsumeContext<SendDiscordNotification>>();
context.Message.Returns(notification);
context.CancellationToken.Returns(CancellationToken.None);
```

**HttpClient Mocking (custom handler):**
```csharp
private sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly System.Net.HttpStatusCode _statusCode;

    public MockHttpMessageHandler(System.Net.HttpStatusCode statusCode)
    {
        _statusCode = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent("")
        });
    }
}

// Usage
var mockHttpClient = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.NoContent));
```

**What to Mock:**
- External HTTP services (IHttpClientFactory)
- Configuration (IConfiguration)
- Loggers (ILogger<T>)
- Message bus contexts (ConsumeContext<T>)
- Time providers (TimeProvider.System vs fake)
- Third-party SDKs (Discord.Net, Azure clients)

**What NOT to Mock:**
- The system under test itself
- In-memory EF Core DbContext (use InMemory provider)
- Simple value objects and DTOs

## Fixtures and Factories

**WebApplicationFactory Pattern:**
```csharp
// See: tests/Dhadgar.Gateway.Tests/GatewayIntegrationTests.cs
public class GatewayWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var settings = new[]
            {
                new KeyValuePair<string, string?>("Cors:AllowedOrigins:0", "https://panel.meridianconsole.com")
            };
            config.AddInMemoryCollection(settings);
        });
    }
}
```

**WebApplicationFactory with JWT Auth:**
```csharp
// See: tests/Dhadgar.Secrets.Tests/Security/SecretsSecurityIntegrationTests.cs
public sealed class SecureSecretsWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSigningKey = "this-is-a-test-signing-key-for-jwt-tokens-minimum-256-bits";
    private const string TestIssuer = "https://test-issuer.local";
    private const string TestAudience = "test-api";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Auth:Issuer"] = TestIssuer,
                ["Auth:Audience"] = TestAudience,
                ["Auth:SigningKey"] = TestSigningKey,
                // ... other test settings
            };
            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            // Replace real providers with fakes
            services.RemoveAll<ISecretProvider>();
            services.AddSingleton<ISecretProvider>(new FakeSecretProvider());

            // Configure test JWT authentication
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestAudience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
                    ClockSkew = TimeSpan.Zero
                };
            });
        });
    }

    public HttpClient CreateAuthenticatedClient(string userId, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new("sub", userId),
            new("principal_type", "user")
        };
        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }
        return CreateClientWithToken(claims);
    }

    private string GenerateJwtToken(List<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

**In-Memory Database:**
```csharp
// See: tests/Dhadgar.Identity.Tests/OrganizationServiceTests.cs
private static IdentityDbContext CreateContext()
{
    var options = new DbContextOptionsBuilder<IdentityDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())  // Unique DB per test
        .Options;
    return new IdentityDbContext(options);
}

private static async Task<User> SeedUserAsync(IdentityDbContext context)
{
    await context.Database.EnsureCreatedAsync();
    var user = new User
    {
        Id = Guid.NewGuid(),
        ExternalAuthId = Guid.NewGuid().ToString("N"),
        Email = "owner@example.com",
        EmailVerified = true
    };
    context.Users.Add(user);
    await context.SaveChangesAsync();
    return user;
}
```

**Location:**
- Factory classes in same file as test class or dedicated file
- Fake implementations (FakeSecretProvider) as private nested classes in factory

## Coverage

**Requirements:** None formally enforced

**View Coverage:**
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

**Coverage Tool:** coverlet.collector 6.0.4

## Test Types

**Unit Tests:**
- Scope: Single class/method in isolation
- Database: In-memory EF Core
- Dependencies: Mocked via NSubstitute
- Examples: `OrganizationServiceTests.cs`, `SecretsAuthorizationServiceTests.cs`

**Integration Tests:**
- Scope: Full HTTP request/response cycle
- Database: In-memory EF Core (or real for specific tests)
- Setup: WebApplicationFactory with test configuration
- Examples: `GatewayIntegrationTests.cs`, `SecretsSecurityIntegrationTests.cs`

**Scaffolding Tests:**
- Every project has `HelloWorldTests.cs` with basic assertion
- Ensures test infrastructure works: `Assert.Equal("Hello from Dhadgar.Gateway", Hello.Message);`

**E2E Tests:**
- Framework: Not currently used
- Planned: Playwright or similar for frontend testing

## Common Patterns

**Async Testing:**
```csharp
[Fact]
public async Task CreateAsync_creates_org_and_owner_membership()
{
    using var context = CreateContext();
    var user = await SeedUserAsync(context);
    var service = new OrganizationService(context, TimeProvider.System);

    var result = await service.CreateAsync(user.Id, new OrganizationCreateRequest("Acme Corp", null));

    Assert.True(result.Success);
    Assert.NotNull(result.Value);
    Assert.Equal("acme-corp", result.Value?.Slug);
}
```

**Error Testing:**
```csharp
[Fact]
public async Task GetSecret_WithoutToken_Returns401()
{
    using var client = _factory.CreateClient();

    var response = await client.GetAsync("/api/v1/secrets/oauth-steam-api-key");

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task GetSecret_WithWrongCategoryPermission_Returns403()
{
    using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:read:infrastructure");

    var response = await client.GetAsync("/api/v1/secrets/oauth-steam-api-key");

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```

**Theory/InlineData for Parameterized Tests:**
```csharp
// See: tests/Dhadgar.Secrets.Tests/Validation/SecretNameValidatorTests.cs
[Theory]
[InlineData("../../../etc/passwd")]
[InlineData("secret';DROP TABLE--")]
[InlineData("<script>alert(1)</script>")]
public async Task GetSecret_WithMaliciousSecretName_Returns400(string maliciousName)
{
    using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:*");

    var response = await client.GetAsync($"/api/v1/secrets/{Uri.EscapeDataString(maliciousName)}");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Theory]
[InlineData("oauth-steam-api-key")]
[InlineData("oauth-discord-client-secret")]
[InlineData("betterauth-jwt-secret")]
public void Validate_RealWorldSecretNames_Succeeds(string name)
{
    var result = SecretNameValidator.Validate(name);
    Assert.True(result.IsValid);
}
```

**FluentAssertions Style:**
```csharp
// See: tests/Dhadgar.Discord.Tests/Consumers/SendDiscordNotificationConsumerTests.cs
using FluentAssertions;

var logs = await _dbContext.NotificationLogs.ToListAsync();
logs.Should().HaveCount(1);
logs[0].EventType.Should().Be(notification.EventType);
logs[0].Title.Should().Be(notification.Title);
logs[0].Status.Should().Be("sent");
```

**HTTP Response Header Testing:**
```csharp
[Fact]
public async Task ResponseIncludesSecurityHeaders()
{
    var response = await _client.GetAsync("/healthz");

    Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
}

[Fact]
public async Task ResponseIncludesCorrelationHeaders()
{
    var response = await _client.GetAsync("/healthz");

    Assert.True(response.Headers.Contains("X-Correlation-Id"));
    Assert.True(response.Headers.Contains("X-Request-Id"));
    Assert.True(response.Headers.Contains("X-Trace-Id"));
}
```

## Test Project Configuration

**Standard Test .csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
    <AssemblyName>Dhadgar.{Service}.Tests</AssemblyName>
    <RootNamespace>Dhadgar.{Service}.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" />
    <!-- Optional: -->
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Dhadgar.{Service}\Dhadgar.{Service}.csproj" />
  </ItemGroup>
</Project>
```

**Required for Integration Tests:**
- Service must expose `public partial class Program { }` at bottom of Program.cs

## Test Data Management

**Seeding Test Data:**
```csharp
private async Task<(Guid UserId, Guid OrgId)> SeedUserWithOrganizationAsync(
    string email,
    string role = "viewer")
{
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await db.Database.EnsureCreatedAsync();

    var user = new User
    {
        Id = Guid.NewGuid(),
        ExternalAuthId = Guid.NewGuid().ToString("N"),
        Email = email,
        EmailVerified = true
    };
    db.Users.Add(user);

    var org = new Organization
    {
        Id = Guid.NewGuid(),
        Name = "Test Org",
        Slug = $"test-org-{Guid.NewGuid():N}",  // Unique slug per test
        Settings = new OrganizationSettings()
    };
    db.Organizations.Add(org);

    db.UserOrganizations.Add(new UserOrganization
    {
        UserId = user.Id,
        OrganizationId = org.Id,
        Role = role,
        IsActive = true
    });

    await db.SaveChangesAsync();
    return (user.Id, org.Id);
}
```

**Test Claims Builder:**
```csharp
private static ClaimsPrincipal CreateUser(string userId, params string[] permissions)
{
    var claims = new List<Claim>
    {
        new("sub", userId),
        new("principal_type", "user")
    };

    foreach (var permission in permissions)
    {
        claims.Add(new Claim("permission", permission));
    }

    var identity = new ClaimsIdentity(claims, "TestAuth");
    return new ClaimsPrincipal(identity);
}
```

---

*Testing analysis: 2026-01-19*

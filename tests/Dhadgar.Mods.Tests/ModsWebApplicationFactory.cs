using System.Security.Claims;
using System.Text.Encodings.Web;
using Dhadgar.Mods.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Mods.Tests;

public class ModsWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"mods-tests-{Guid.NewGuid()}";

    /// <summary>
    /// Test-friendly TimeProvider that can be controlled in tests.
    /// </summary>
    public FakeTimeProvider FakeTimeProvider { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            services.RemoveAll<DbContextOptions<ModsDbContext>>();

            // Create isolated EF provider for InMemory
            var efProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            // Add in-memory database for testing
            services.AddDbContext<ModsDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName)
                    .UseInternalServiceProvider(efProvider));

            // Register FakeTimeProvider for testing time-dependent services
            services.AddSingleton<TimeProvider>(FakeTimeProvider);

            // Remove background services during testing
            services.RemoveAll<IHostedService>();

            // Replace real health checks with test-friendly ones
            var healthCheckDescriptors = services
                .Where(d => d.ServiceType == typeof(HealthCheckService) ||
                            d.ServiceType.FullName?.Contains("HealthCheck", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var descriptor in healthCheckDescriptors)
            {
                services.Remove(descriptor);
            }

            // Re-add basic health checks that don't require external dependencies
            services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
                .AddCheck("test-db", () => HealthCheckResult.Healthy(), tags: ["db", "ready"]);

            // Configure test authentication
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestModsAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestModsAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestModsAuthHandler>(
                TestModsAuthHandler.SchemeName, _ => { });

            // Add problem details and endpoint exploration
            services.AddProblemDetails();
            services.AddEndpointsApiExplorer();
        });
    }

    /// <summary>
    /// Creates an HTTP client with test authentication for a user.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(Guid userId, Guid? organizationId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestModsAuthHandler.UserIdHeader, userId.ToString());
        if (organizationId.HasValue)
        {
            client.DefaultRequestHeaders.Add(TestModsAuthHandler.OrgIdHeader, organizationId.Value.ToString());
        }
        return client;
    }
}

/// <summary>
/// Test authentication handler for integration tests.
/// SECURITY: This is ONLY for testing - never use in production.
/// </summary>
public sealed class TestModsAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";
    public const string UserIdHeader = "X-Test-User-Id";
    public const string OrgIdHeader = "X-Test-Org-Id";

    public TestModsAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check for test user ID header
        if (!Request.Headers.TryGetValue(UserIdHeader, out var userIdValues) ||
            !Guid.TryParse(userIdValues.FirstOrDefault(), out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("sub", userId.ToString())
        };

        // Add organization if provided
        if (Request.Headers.TryGetValue(OrgIdHeader, out var orgIdValues) &&
            Guid.TryParse(orgIdValues.FirstOrDefault(), out var orgId))
        {
            claims.Add(new Claim("org_id", orgId.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

using System.Security.Claims;
using System.Text.Encodings.Web;
using Dhadgar.Console.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using StackExchange.Redis;

namespace Dhadgar.Console.Tests;

/// <summary>
/// WebApplicationFactory for Console service integration tests.
/// Uses InMemory database, MemoryDistributedCache, and mocked Redis.
/// </summary>
public class ConsoleWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"console-tests-{Guid.NewGuid()}";

    /// <summary>
    /// Test-friendly TimeProvider that can be controlled in tests.
    /// </summary>
    public FakeTimeProvider FakeTimeProvider { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove all DbContext-related registrations to avoid provider conflicts
            services.RemoveAll<DbContextOptions<ConsoleDbContext>>();

            // Create isolated EF provider for InMemory
            var efProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            // Add InMemory database for testing with isolated provider
            services.AddDbContext<ConsoleDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName)
                    .UseInternalServiceProvider(efProvider));

            // Register FakeTimeProvider for testing time-dependent services
            services.AddSingleton<TimeProvider>(FakeTimeProvider);

            // Replace Redis IDistributedCache with MemoryDistributedCache
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache>(
                new MemoryDistributedCache(
                    Options.Create(new MemoryDistributedCacheOptions())));

            // Mock IConnectionMultiplexer (Redis) since ConsoleHistoryService needs it
            var mockRedis = Substitute.For<IConnectionMultiplexer>();
            var mockDb = Substitute.For<IDatabase>();
            mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(mockDb);
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton(mockRedis);

            // Configure ConsoleOptions with test defaults
            services.Configure<ConsoleOptions>(opts =>
            {
                opts.MaxCommandLength = 2000;
                opts.HotStorageTtlMinutes = 60;
                opts.RetentionDays = 30;
                opts.CommandRegexTimeoutMs = 1000;
            });

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
                options.DefaultAuthenticateScheme = TestConsoleAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestConsoleAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestConsoleAuthHandler>(
                TestConsoleAuthHandler.SchemeName, _ => { });

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
        client.DefaultRequestHeaders.Add(TestConsoleAuthHandler.UserIdHeader, userId.ToString());
        if (organizationId.HasValue)
        {
            client.DefaultRequestHeaders.Add(TestConsoleAuthHandler.OrgIdHeader, organizationId.Value.ToString());
        }
        return client;
    }
}

/// <summary>
/// Test authentication handler for Console integration tests.
/// </summary>
public sealed class TestConsoleAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";
    public const string UserIdHeader = "X-Test-User-Id";
    public const string OrgIdHeader = "X-Test-Org-Id";

    public TestConsoleAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
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

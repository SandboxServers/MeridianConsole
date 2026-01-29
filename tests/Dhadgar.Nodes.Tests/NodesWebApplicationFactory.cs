using System.Security.Claims;
using System.Text.Encodings.Web;
using Dhadgar.Nodes;
using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Auth;
using Dhadgar.Nodes.BackgroundServices;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Services;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Nodes.Tests;

public sealed class NodesWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"nodes-tests-{Guid.NewGuid()}";
    public TestNodesEventPublisher EventPublisher { get; } = new();
    public FakeTimeProvider TimeProvider { get; } = new(DateTimeOffset.UtcNow);
    public TestCertificateAuthorityService CaService { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registration
            services.RemoveAll<DbContextOptions<NodesDbContext>>();

            var efProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<NodesDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName)
                    .UseInternalServiceProvider(efProvider));

            // Remove all MassTransit services and replace with test publisher
            // This removes bus, health checks, and other MassTransit infrastructure
            var massTransitDescriptors = services
                .Where(d => d.ServiceType.FullName?.StartsWith("MassTransit", StringComparison.Ordinal) == true ||
                            d.ImplementationType?.FullName?.StartsWith("MassTransit", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var descriptor in massTransitDescriptors)
            {
                services.Remove(descriptor);
            }

            services.RemoveAll<IPublishEndpoint>();
            services.AddSingleton<IPublishEndpoint>(EventPublisher);

            // Remove real TimeProvider and use fake
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(TimeProvider);

            // Replace CA services with test implementations
            CaService = new TestCertificateAuthorityService(TimeProvider);
            CaService.InitializeAsync().GetAwaiter().GetResult();
            services.RemoveAll<ICaStorageProvider>();
            services.RemoveAll<ICertificateAuthorityService>();
            services.AddSingleton<ICertificateAuthorityService>(CaService);

            // Disable mTLS for testing (tests use TestNodesAuthHandler instead)
            services.Configure<MtlsOptions>(options =>
            {
                options.Enabled = false;
            });

            // Remove background services during testing
            services.RemoveAll<IHostedService>();

            // Replace real health checks with test-friendly ones
            // This removes PostgreSQL and other external dependency checks
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
                .AddCheck("test-db", () => HealthCheckResult.Healthy(), tags: ["db", "ready"])
                .AddCheck("test-messaging", () => HealthCheckResult.Healthy(), tags: ["messaging", "ready"]);

            // Configure test authentication - add the test scheme and handler
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestNodesAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestNodesAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestNodesAuthHandler>(
                TestNodesAuthHandler.SchemeName, _ => { });

            // Configure NodesOptions with defaults for testing
            services.Configure<NodesOptions>(options =>
            {
                options.HeartbeatThresholdMinutes = 5;
            });

            // Replace audit context accessor with test-friendly version
            // The real one depends on HttpContext which may not be fully available in tests
            services.RemoveAll<IAuditContextAccessor>();
            services.AddScoped<IAuditContextAccessor, TestNodesAuditContextAccessor>();

            // Add a startup filter to initialize the database
            services.AddSingleton<IStartupFilter>(new DbInitStartupFilter());

            // Add problem details and endpoint exploration for better error reporting
            // This ensures minimal API binding infrastructure is properly configured
            services.AddProblemDetails();
            services.AddEndpointsApiExplorer();
        });
    }

    private sealed class DbInitStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                using var scope = app.ApplicationServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
                db.Database.EnsureCreated();
                next(app);
            };
        }
    }

    /// <summary>
    /// Creates an HTTP client with test authentication for a user.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(Guid userId, Guid? organizationId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestNodesAuthHandler.UserIdHeader, userId.ToString());
        if (organizationId.HasValue)
        {
            client.DefaultRequestHeaders.Add(TestNodesAuthHandler.OrgIdHeader, organizationId.Value.ToString());
        }
        return client;
    }

    /// <summary>
    /// Creates an HTTP client with agent authentication (for agent endpoints).
    /// </summary>
    public HttpClient CreateAgentClient(Guid nodeId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestNodesAuthHandler.UserIdHeader, nodeId.ToString());
        client.DefaultRequestHeaders.Add(TestNodesAuthHandler.NodeIdHeader, nodeId.ToString());
        return client;
    }

    /// <summary>
    /// Seeds a test node in the database.
    /// </summary>
    public async Task<Node> SeedNodeAsync(
        Guid organizationId,
        string name = "test-node",
        NodeStatus status = NodeStatus.Online)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        await db.Database.EnsureCreatedAsync();

        var node = new Node
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            DisplayName = name,
            Status = status,
            Platform = "linux",
            AgentVersion = "1.0.0",
            LastHeartbeat = TimeProvider.GetUtcNow().UtcDateTime,
            CreatedAt = TimeProvider.GetUtcNow().UtcDateTime
        };

        db.Nodes.Add(node);
        await db.SaveChangesAsync();
        return node;
    }

    /// <summary>
    /// Ensures the test database is created (for tests that don't seed data).
    /// </summary>
    public async Task EnsureDatabaseCreatedAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// Seeds multiple test nodes in the database.
    /// </summary>
    public async Task<List<Node>> SeedNodesAsync(Guid organizationId, int count)
    {
        var nodes = new List<Node>();
        for (int i = 0; i < count; i++)
        {
            var node = await SeedNodeAsync(organizationId, $"test-node-{i + 1}");
            nodes.Add(node);
        }
        return nodes;
    }
}

/// <summary>
/// Test authentication handler for integration tests.
/// SECURITY: This is ONLY for testing - never use in production.
/// </summary>
public sealed class TestNodesAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";
    public const string UserIdHeader = "X-Test-User-Id";
    public const string OrgIdHeader = "X-Test-Org-Id";
    public const string NodeIdHeader = "X-Test-Node-Id";

    public TestNodesAuthHandler(
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

        // Add node ID if provided (for agent authentication)
        if (Request.Headers.TryGetValue(NodeIdHeader, out var nodeIdValues) &&
            Guid.TryParse(nodeIdValues.FirstOrDefault(), out var nodeId))
        {
            claims.Add(new Claim("node_id", nodeId.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Test implementation of IAuditContextAccessor for integration tests.
/// Returns sensible defaults without depending on HTTP context.
/// </summary>
public sealed class TestNodesAuditContextAccessor : IAuditContextAccessor
{
    public string GetActorId() => "test-user";
    public ActorType GetActorType() => ActorType.User;
    public string? GetCorrelationId() => Guid.NewGuid().ToString();
    public string? GetRequestId() => Guid.NewGuid().ToString();
    public string? GetIpAddress() => "127.0.0.1";
    public string? GetUserAgent() => "Test/1.0";
}

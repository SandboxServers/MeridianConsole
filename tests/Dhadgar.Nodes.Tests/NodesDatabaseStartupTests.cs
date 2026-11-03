using System.Net;
using System.Text.Json;
using Dhadgar.Nodes.Data;
using Dhadgar.Shared.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Nodes.Tests;

[Collection("Nodes Integration")]
public sealed class NodesDatabaseStartupTests
{
    // Never connected to by a passing run: port 1 is closed by design.
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=nodes;Username=u;Password=p;Timeout=1;Command Timeout=1";

    // Mirrors NodesDatabaseStartupService.StartupAttemptBound (internal, not visible to this project).
    private static readonly TimeSpan StartupAttemptBound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Host_DatabaseUnreachable_StartsLiveAndNotReady()
    {
        // Arrange - the production Npgsql registration, pointed at a closed port, in Development
        using var factory = new NodesWebApplicationFactory { UseProductionDbContext = true };
        factory.Settings["ConnectionStrings:Postgres"] = UnreachableConnectionString;
        using var host = factory.WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .ConfigureTestServices(s => s.AddNodesDatabaseStartup()));
        using var client = host.CreateClient();

        // Act + Assert - live answers at once
        using var live = await client.GetAsync("/livez");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        // Act - readiness turns 503 once the probe fails (polled, the background migration runs concurrently)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ready = await client.GetAsync("/readyz", cts.Token);
        while (ready.StatusCode != HttpStatusCode.ServiceUnavailable)
        {
            ready.Dispose();
            await Task.Delay(100, cts.Token);
            ready = await client.GetAsync("/readyz", cts.Token);
        }

        // Assert
        using (ready)
        {
            var body = await ready.Content.ReadAsStringAsync(cts.Token);
            using var json = JsonDocument.Parse(body);
            var status = json.RootElement.GetProperty("checks").GetProperty("nodes-database").GetProperty("status").GetString();
            Assert.Equal("Unhealthy", status);
            Assert.DoesNotContain("Npgsql", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Host=", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Password", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task StartupService_MigratorFailsThenSucceeds_StopsAfterThirdAttempt()
    {
        // Arrange
        var migrator = new FakeMigrator(failures: 2);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = BuildMigrationServices(migrator).BuildServiceProvider();
        using var service = new NodesDatabaseStartupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeHostEnvironment("Development"),
            time,
            NullLogger<NodesDatabaseStartupService>.Instance);

        // Act - first attempt fails and schedules a 1 s retry
        await service.StartAsync(CancellationToken.None);
        var run = service.ExecuteTask!;
        await WaitUntilAsync(() => migrator.Calls == 1);
        Assert.False(run.IsCompleted);

        // Second attempt fails and schedules a 2 s retry (fake time only; the real clock would take 1 s)
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => migrator.Calls == 2, TimeSpan.FromMilliseconds(750));
        Assert.False(run.IsCompleted);

        // The second delay is 2 s: 1 s must not be enough
        time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(100);
        Assert.Equal(2, migrator.Calls);

        // Third attempt succeeds and the service ends
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => run.IsCompleted, TimeSpan.FromMilliseconds(750));
        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(3, migrator.Calls);

        // No attempt after success
        time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(100);
        Assert.Equal(3, migrator.Calls);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartupService_NotDevelopment_NeverMigrates()
    {
        // Arrange
        var migrator = new FakeMigrator(failures: 0);
        using var provider = BuildMigrationServices(migrator).BuildServiceProvider();
        using var service = new NodesDatabaseStartupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeHostEnvironment("Production"),
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<NodesDatabaseStartupService>.Instance);

        // Act - the Production path returns at once, but poll (bounded) for the background task to finish rather than
        // assume it has already run when StartAsync returns
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => service.ExecuteTask!.IsCompleted);
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, migrator.Calls);
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Host_Development_MigratesBeforeOtherHostedServicesStart()
    {
        // Arrange - a migration that takes 200 ms, then a hosted service registered after the startup service
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });
        builder.Services.AddSingleton(TimeProvider.System);
        var migrator = new DelayedMigrator();
        builder.Services.AddSingleton<INodesDatabaseMigrator>(migrator);
        builder.Services.AddSingleton(migrator);
        builder.Services.AddHostedService<NodesDatabaseStartupService>();
        builder.Services.AddHostedService<ProbeHostedService>();
        using var host = builder.Build();

        // Act
        await host.StartAsync();
        var probe = host.Services.GetServices<IHostedService>().OfType<ProbeHostedService>().Single();
        await host.StopAsync();

        // Assert - the probe started after the migration completed
        Assert.True(probe.SawMigrated);
    }

    [Fact]
    public async Task StartingAsync_MigrationHangs_ReturnsAfterBoundAndExecuteRetries()
    {
        // Arrange - call 1 hangs, call 2 fails, call 3 succeeds
        var migrator = new HangingMigrator();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = BuildMigrationServices(migrator).BuildServiceProvider();
        using var service = new NodesDatabaseStartupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeHostEnvironment("Development"),
            time,
            NullLogger<NodesDatabaseStartupService>.Instance);

        // Act - StartingAsync returns once the bound elapses, without throwing
        var starting = service.StartingAsync(CancellationToken.None);
        await WaitUntilAsync(() => migrator.Calls == 1);
        time.Advance(StartupAttemptBound);
        await starting.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(starting.IsCompletedSuccessfully);
        Assert.Equal(1, migrator.Calls);

        // Act - StartAsync runs the background retry: the second attempt fails and schedules a 1 s retry
        await service.StartAsync(CancellationToken.None);
        var run = service.ExecuteTask!;
        await WaitUntilAsync(() => migrator.Calls == 2);
        Assert.False(run.IsCompleted);

        // Assert - the third attempt happens after the 1 s backoff and succeeds
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => migrator.Calls == 3, TimeSpan.FromMilliseconds(750));
        await WaitUntilAsync(() => run.IsCompleted, TimeSpan.FromMilliseconds(750));
        Assert.True(run.IsCompletedSuccessfully);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartingAsync_NotDevelopment_NeverMigrates()
    {
        // Arrange
        var migrator = new FakeMigrator(failures: 0);
        using var provider = BuildMigrationServices(migrator).BuildServiceProvider();
        using var service = new NodesDatabaseStartupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeHostEnvironment("Production"),
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<NodesDatabaseStartupService>.Instance);

        // Act
        await service.StartingAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => service.ExecuteTask!.IsCompleted);
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, migrator.Calls);
    }

    [Fact]
    public async Task HealthCheck_MigrationsPending_IsUnhealthy()
    {
        // Arrange - SQLite is relational, and nothing has been migrated, so every migration is pending
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NodesDbContext>(o => o.UseSqlite(connection));
        using var provider = services.BuildServiceProvider();
        var check = new NodesDatabaseHealthCheck(provider.GetRequiredService<IServiceScopeFactory>());

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("database migrations pending", result.Description);
    }

    [Fact]
    public async Task HealthCheck_Migrated_InMemory_IsHealthy()
    {
        // Arrange - InMemory is not relational (no migrations to check) and CanConnectAsync is true
        var services = new ServiceCollection();
        services.AddDbContext<NodesDbContext>(o => o.UseInMemoryDatabase($"nodes-health-{Guid.NewGuid()}"));
        using var provider = services.BuildServiceProvider();
        var check = new NodesDatabaseHealthCheck(provider.GetRequiredService<IServiceScopeFactory>());

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private static ServiceCollection BuildMigrationServices(INodesDatabaseMigrator migrator)
    {
        // Scoped, as the production registration is, so the startup service's per-attempt scope is exercised
        var services = new ServiceCollection();
        services.AddScoped(_ => migrator);
        return services;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, cts.Token);
        }
    }

    private sealed class DelayedMigrator : INodesDatabaseMigrator
    {
        private volatile bool _completed;

        public bool Completed => _completed;

        public async Task<Result> MigrateAsync(CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
            _completed = true;
            return Result.Success();
        }
    }

    private sealed class ProbeHostedService(DelayedMigrator migrator) : IHostedService
    {
        public bool SawMigrated { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            SawMigrated = migrator.Completed;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HangingMigrator : INodesDatabaseMigrator
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async Task<Result> MigrateAsync(CancellationToken ct)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            return call == 2
                ? Result.Failure("[Nodes.DatabaseUnavailable] test failure")
                : Result.Success();
        }
    }

    private sealed class FakeMigrator(int failures) : INodesDatabaseMigrator
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<Result> MigrateAsync(CancellationToken ct)
        {
            var call = Interlocked.Increment(ref _calls);
            return Task.FromResult(call <= failures
                ? Result.Failure("[Nodes.DatabaseUnavailable] test failure")
                : Result.Success());
        }
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Dhadgar.Nodes.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

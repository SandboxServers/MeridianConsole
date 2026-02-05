using System.Diagnostics;
using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using FluentAssertions;
using Xunit;

namespace Dhadgar.AppHost.Tests;

/// <summary>
/// Integration tests for the Aspire AppHost that verify all services can start
/// and respond to health checks.
/// </summary>
/// <remarks>
/// These tests require Docker to be running as they spin up containerized infrastructure.
/// They are marked with a custom trait to allow filtering in CI if needed.
/// Tests will be skipped automatically if Docker is not available or unhealthy.
/// </remarks>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class AppHostIntegrationTests : IAsyncLifetime
{
    private DistributedApplication? _app;
    private IDistributedApplicationTestingBuilder? _appHost;
    private string? _skipReason;

    public async Task InitializeAsync()
    {
        // Check if Docker is available and healthy before attempting to start the AppHost
        if (!await IsDockerHealthyAsync())
        {
            _skipReason = "Docker is not available or unhealthy. Skipping Aspire integration tests.";
            return;
        }

        // Create and build the AppHost for testing
        // This spins up all infrastructure (PostgreSQL, Redis, RabbitMQ) as containers
        _appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Dhadgar_AppHost>();

        _app = await _appHost.BuildAsync();

        // Start the application (this starts all services and waits for them to be ready)
        await _app.StartAsync();
    }

    private static async Task<bool> IsDockerHealthyAsync()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            // Docker command not found or failed
            return false;
        }
    }

    private void SkipIfDockerUnavailable()
    {
        Skip.If(_skipReason is not null, _skipReason ?? "Docker unavailable");
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (_appHost is not null)
        {
            await _appHost.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task Gateway_HealthCheck_ReturnsHealthy()
    {
        SkipIfDockerUnavailable();

        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("gateway");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Gateway_RootEndpoint_ReturnsOk()
    {
        SkipIfDockerUnavailable();

        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("gateway");
        var response = await httpClient.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Identity_HealthCheck_ReturnsHealthy()
    {
        SkipIfDockerUnavailable();

        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("identity");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Nodes_HealthCheck_ReturnsHealthy()
    {
        SkipIfDockerUnavailable();

        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("nodes");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Secrets_HealthCheck_ReturnsHealthy()
    {
        SkipIfDockerUnavailable();

        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("secrets");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Notifications_HealthCheck_ReturnsHealthy()
    {
        SkipIfDockerUnavailable();

        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("notifications");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Discord_HealthCheck_ReturnsHealthy()
    {
        SkipIfDockerUnavailable();

        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("discord");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Billing_HealthCheck_ReturnsHealthy()
    {
        SkipIfDockerUnavailable();

        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("billing");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Servers_HealthCheck_ReturnsHealthy()
    {
        SkipIfDockerUnavailable();

        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("servers");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Tasks_HealthCheck_ReturnsHealthy()
    {
        SkipIfDockerUnavailable();

        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("tasks");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Console_HealthCheck_ReturnsHealthy()
    {
        SkipIfDockerUnavailable();

        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("console");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Mods_HealthCheck_ReturnsHealthy()
    {
        SkipIfDockerUnavailable();

        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("mods");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

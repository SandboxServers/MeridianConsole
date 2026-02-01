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
/// </remarks>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class AppHostIntegrationTests : IAsyncLifetime
{
    private DistributedApplication? _app;
    private IDistributedApplicationTestingBuilder? _appHost;

    public async Task InitializeAsync()
    {
        // Create and build the AppHost for testing
        // This spins up all infrastructure (PostgreSQL, Redis, RabbitMQ) as containers
        _appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Dhadgar_AppHost>();

        _app = await _appHost.BuildAsync();

        // Start the application (this starts all services and waits for them to be ready)
        await _app.StartAsync();
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

    [Fact]
    public async Task Gateway_HealthCheck_ReturnsHealthy()
    {
        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("gateway");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Gateway_RootEndpoint_ReturnsOk()
    {
        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("gateway");
        var response = await httpClient.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Identity_HealthCheck_ReturnsHealthy()
    {
        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("identity");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nodes_HealthCheck_ReturnsHealthy()
    {
        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("nodes");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Secrets_HealthCheck_ReturnsHealthy()
    {
        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("secrets");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Notifications_HealthCheck_ReturnsHealthy()
    {
        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("notifications");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Discord_HealthCheck_ReturnsHealthy()
    {
        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("discord");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Billing_HealthCheck_ReturnsHealthy()
    {
        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("billing");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Servers_HealthCheck_ReturnsHealthy()
    {
        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("servers");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tasks_HealthCheck_ReturnsHealthy()
    {
        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("tasks");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Console_HealthCheck_ReturnsHealthy()
    {
        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("console");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Mods_HealthCheck_ReturnsHealthy()
    {
        // Arrange & Act
        using var httpClient = _app!.CreateHttpClient("mods");
        var response = await httpClient.GetAsync("/healthz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

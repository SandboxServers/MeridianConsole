using System.Net;
using System.Text.Json;
using Dhadgar.Nodes.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Dhadgar.Nodes.Tests.Auth;

public class AgentMtlsSchemeTests
{
    private const string ForbidPath = "/scheme-test/forbid";
    private const string ProtectedPath = "/scheme-test/protected";

    [Fact]
    public async Task Forbid_UnprotectedPath_Returns403ProblemJsonForbidden()
    {
        // Arrange
        await using var app = await BuildHostAsync(new Dictionary<string, string?>
        {
            ["Mtls:Enabled"] = "true"
        });
        using var client = app.GetTestClient();

        // Act
        using var response = await client.GetAsync(ForbidPath);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("forbidden", body.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Challenge_UnprotectedPathWithoutUser_Returns401ProblemJsonCertificateRequired()
    {
        // Arrange
        await using var app = await BuildHostAsync(new Dictionary<string, string?>
        {
            ["Mtls:Enabled"] = "true"
        });
        using var client = app.GetTestClient();

        // Act
        using var response = await client.GetAsync(ProtectedPath);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("certificate_required", body.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public void AddMtlsAuthentication_ForwardedEnabledWithoutTrustedProxies_Throws()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Mtls:ForwardedCertificate:Enabled"] = "true"
        });
        var services = new ServiceCollection();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            services.AddMtlsAuthentication(configuration);
        });

        // Assert
        Assert.Contains("TrustedProxies", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddMtlsAuthentication_ForwardedEnabledWithUnparsableProxy_Throws()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Mtls:ForwardedCertificate:Enabled"] = "true",
            ["Mtls:ForwardedCertificate:TrustedProxies:0"] = "not-a-cidr"
        });
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            services.AddMtlsAuthentication(configuration);
        });
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void AddMtlsAuthentication_ForwardedEnabledWithZeroPrefixProxy_Throws(string proxy)
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Mtls:ForwardedCertificate:Enabled"] = "true",
            ["Mtls:ForwardedCertificate:TrustedProxies:0"] = proxy
        });
        var services = new ServiceCollection();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            services.AddMtlsAuthentication(configuration);
        });

        // Assert
        Assert.Contains("prefix length of 0 is not allowed", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(proxy, exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static async Task<WebApplication> BuildHostAsync(Dictionary<string, string?> values)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(values);

        builder.Services.AddMtlsAuthentication(builder.Configuration);
        // MtlsMiddleware resolves ICertificateValidationService for every request, so the host needs one.
        // The scheme tests never reach validation (their paths are not mTLS-protected).
        builder.Services.AddSingleton<ICertificateValidationService>(
            Substitute.For<ICertificateValidationService>());
        builder.Services.AddAuthorization();

        var app = builder.Build();

        app.UseAuthentication();
        app.UseMtlsAuthentication();
        app.UseAuthorization();

        app.MapGet(ForbidPath, () => Results.Forbid());
        app.MapGet(ProtectedPath, () => Results.Ok("ok"))
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser());

        await app.StartAsync();
        return app;
    }
}

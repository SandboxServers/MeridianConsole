using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Dhadgar.Gateway.Middleware;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class MiddlewareUnitTests
{
    [Fact]
    public async Task CorrelationMiddlewareSetsHeadersAndItems()
    {
        using var activity = new System.Diagnostics.Activity("test");
        activity.Start();

        var context = CreateContext();
        var middleware = new CorrelationMiddleware(WriteResponseAsync);

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        Assert.NotNull(context.Items["CorrelationId"]);
        Assert.NotNull(context.Items["RequestId"]);
        Assert.True(context.Response.Headers.ContainsKey("X-Correlation-Id"));
        Assert.True(context.Response.Headers.ContainsKey("X-Request-Id"));
        Assert.True(context.Response.Headers.ContainsKey("X-Trace-Id"));
    }

    [Fact]
    public async Task CorrelationMiddlewareEchoesProvidedCorrelationId()
    {
        var context = CreateContext();
        var expectedCorrelationId = Guid.NewGuid().ToString("N");
        context.Request.Headers["X-Correlation-Id"] = expectedCorrelationId;

        var middleware = new CorrelationMiddleware(WriteResponseAsync);

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        Assert.Equal(expectedCorrelationId, context.Response.Headers["X-Correlation-Id"].ToString());
    }

    [Fact]
    public async Task RequestEnrichmentStripsHeadersAndInjectsClaims()
    {
        var context = CreateContext();
        context.Items["RequestId"] = "request-123";
        context.Request.Headers["X-Tenant-Id"] = "spoofed";
        context.Request.Headers["X-User-Id"] = "spoofed";
        context.Request.Headers["X-Client-Type"] = "spoofed";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.5, 10.0.0.1";

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", "tenant-1"),
            new Claim("sub", "user-1"),
            new Claim("client_type", "agent")
        }, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = new RequestEnrichmentMiddleware(WriteResponseAsync);

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        Assert.Equal("tenant-1", context.Request.Headers["X-Tenant-Id"].ToString());
        Assert.Equal("user-1", context.Request.Headers["X-User-Id"].ToString());
        Assert.Equal("agent", context.Request.Headers["X-Client-Type"].ToString());
        Assert.Equal("203.0.113.5", context.Request.Headers["X-Real-IP"].ToString());
        Assert.Equal("request-123", context.Request.Headers["X-Request-Id"].ToString());
        Assert.Equal("request-123", context.Response.Headers["X-Request-Id"].ToString());
    }

    [Fact]
    public async Task RequestEnrichmentPrefersCloudflareIp()
    {
        var context = CreateContext();
        context.Items["RequestId"] = "request-456";
        context.Request.Headers["CF-Connecting-IP"] = "198.51.100.7";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.5";

        var middleware = new RequestEnrichmentMiddleware(WriteResponseAsync);

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        Assert.Equal("198.51.100.7", context.Request.Headers["X-Real-IP"].ToString());
    }

    [Fact]
    public async Task RequestLoggingMiddlewareCompletesOnSuccess()
    {
        var context = CreateContext();
        context.Response.StatusCode = StatusCodes.Status204NoContent;

        var middleware = new RequestLoggingMiddleware(
            _ => Task.CompletedTask,
            NullLogger<RequestLoggingMiddleware>.Instance);

        await middleware.InvokeAsync(context);
    }

    [Fact]
    public async Task RequestLoggingMiddlewareRethrowsOnFailure()
    {
        var context = CreateContext();
        var middleware = new RequestLoggingMiddleware(
            _ => throw new InvalidOperationException("boom"),
            NullLogger<RequestLoggingMiddleware>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
    }

    [Fact]
    public async Task ProblemDetailsMiddlewareReturnsProblemJsonInDevelopment()
    {
        var context = CreateContext();
        context.Items["CorrelationId"] = "corr-123";
        context.Request.Path = "/api/v1/servers";

        var environment = new TestHostEnvironment { EnvironmentName = Environments.Development };
        var middleware = new ProblemDetailsMiddleware(
            _ => throw new InvalidOperationException("kaboom"),
            NullLogger<ProblemDetailsMiddleware>.Instance,
            environment);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);

        var body = await ReadResponseBodyAsync(context.Response);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("Internal Server Error", root.GetProperty("title").GetString());
        Assert.Equal("kaboom", root.GetProperty("detail").GetString());
        Assert.Equal("corr-123", root.GetProperty("traceId").GetString());
        Assert.True(root.TryGetProperty("extensions", out var extensions));
        Assert.True(extensions.TryGetProperty("stackTrace", out _));
    }

    [Fact]
    public async Task ProblemDetailsMiddlewareHidesStackTraceInProduction()
    {
        var context = CreateContext();
        context.Items["CorrelationId"] = "corr-999";
        context.Request.Path = "/api/v1/tasks";

        var environment = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var middleware = new ProblemDetailsMiddleware(
            _ => throw new InvalidOperationException("kaboom"),
            NullLogger<ProblemDetailsMiddleware>.Instance,
            environment);

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context.Response);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("An unexpected error occurred. Please contact support with the trace ID.",
            root.GetProperty("detail").GetString());
        Assert.False(root.TryGetProperty("extensions", out _));
    }

    [Fact]
    public async Task SecurityHeadersMiddlewareAddsHeadersInDevelopment()
    {
        var context = CreateContext();
        var environment = new TestHostEnvironment { EnvironmentName = Environments.Development };
        var middleware = new SecurityHeadersMiddleware(WriteResponseAsync, environment);

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("1; mode=block", context.Response.Headers["X-XSS-Protection"].ToString());
        Assert.False(context.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    [Fact]
    public async Task SecurityHeadersMiddlewareAddsHstsInProduction()
    {
        var context = CreateContext();
        var environment = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var middleware = new SecurityHeadersMiddleware(WriteResponseAsync, environment);

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        Assert.True(context.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    [Fact]
    public void CorsConfigurationAllowsAnyOriginWhenUnset()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddMeridianConsoleCors(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CorsOptions>>();
        var policy = options.Value.GetPolicy(CorsConfiguration.PolicyName);

        Assert.NotNull(policy);
        Assert.True(policy!.AllowAnyOrigin);
        Assert.False(policy.SupportsCredentials);
    }

    [Fact]
    public void CorsConfigurationUsesConfiguredOrigins()
    {
        var settings = new[]
        {
            new KeyValuePair<string, string?>("Cors:AllowedOrigins:0", "https://panel.meridianconsole.com"),
            new KeyValuePair<string, string?>("Cors:AllowedOrigins:1", "https://meridianconsole.com")
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddMeridianConsoleCors(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CorsOptions>>();
        var policy = options.Value.GetPolicy(CorsConfiguration.PolicyName);

        Assert.NotNull(policy);
        Assert.Contains("https://panel.meridianconsole.com", policy!.Origins);
        Assert.Contains("https://meridianconsole.com", policy.Origins);
        Assert.True(policy.SupportsCredentials);
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        return context;
    }

    private static async Task<string> ReadResponseBodyAsync(HttpResponse response)
    {
        response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static Task WriteResponseAsync(HttpContext context)
    {
        return context.Response.WriteAsync("ok");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Dhadgar.Gateway.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

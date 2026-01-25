using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Dhadgar.ServiceDefaults.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.Middleware;

public class ProblemDetailsMiddlewareTests
{
    [Fact]
    public async Task ErrorResponse_IncludesTraceId_InProblemDetails()
    {
        // Arrange
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        app.UseMiddleware<ProblemDetailsMiddleware>();
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/error", context =>
                                throw new InvalidOperationException("Test error"));
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("traceId", out var traceIdElement),
            "Problem Details should include traceId property");
        Assert.False(string.IsNullOrEmpty(traceIdElement.GetString()),
            "traceId should not be empty");
    }

    [Fact]
    public async Task ErrorResponse_TraceId_MatchesActiveActivity()
    {
        // Arrange
        string? capturedTraceId = null;

        // Register the test ActivitySource so activities are created
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Test.ProblemDetails",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var testActivitySource = new ActivitySource("Test.ProblemDetails");

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        // Middleware to start an activity and capture its TraceId
                        app.Use(async (context, next) =>
                        {
                            using var activity = testActivitySource.StartActivity("TestRequest");
                            capturedTraceId = activity?.TraceId.ToString();
                            await next();
                        });
                        app.UseMiddleware<ProblemDetailsMiddleware>();
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/error", context =>
                                throw new InvalidOperationException("Test error"));
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var responseTraceId = root.GetProperty("traceId").GetString();

        // When an activity is present, traceId should match
        Assert.NotNull(capturedTraceId);
        Assert.Equal(capturedTraceId, responseTraceId);
    }

    [Fact]
    public async Task ErrorResponse_FallsBackToCorrelationId_WhenNoActivity()
    {
        // Arrange
        const string correlationId = "test-correlation-123";

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        // Set CorrelationId without Activity
                        app.Use(async (context, next) =>
                        {
                            context.Items["CorrelationId"] = correlationId;
                            await next();
                        });
                        app.UseMiddleware<ProblemDetailsMiddleware>();
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/error", context =>
                                throw new InvalidOperationException("Test error"));
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var responseTraceId = root.GetProperty("traceId").GetString();
        Assert.Equal(correlationId, responseTraceId);
    }

    [Fact]
    public async Task ErrorResponse_FallsBackToTraceIdentifier_WhenNoActivityOrCorrelationId()
    {
        // Arrange
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        // No Activity and no CorrelationId
                        app.UseMiddleware<ProblemDetailsMiddleware>();
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/error", context =>
                                throw new InvalidOperationException("Test error"));
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var responseTraceId = root.GetProperty("traceId").GetString();

        // Should fall back to HttpContext.TraceIdentifier which is auto-generated
        Assert.NotNull(responseTraceId);
        Assert.NotEmpty(responseTraceId);
        Assert.NotEqual("unknown", responseTraceId);
    }

    [Fact]
    public async Task ErrorResponse_ActivityTraceId_TakesPrecedenceOverCorrelationId()
    {
        // Arrange
        string? activityTraceId = null;
        const string correlationId = "should-not-use-this";

        // Register the test ActivitySource so activities are created
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Test.Precedence",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var testActivitySource = new ActivitySource("Test.Precedence");

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        // Set both Activity and CorrelationId
                        app.Use(async (context, next) =>
                        {
                            using var activity = testActivitySource.StartActivity("TestRequest");
                            activityTraceId = activity?.TraceId.ToString();
                            context.Items["CorrelationId"] = correlationId;
                            await next();
                        });
                        app.UseMiddleware<ProblemDetailsMiddleware>();
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/error", context =>
                                throw new InvalidOperationException("Test error"));
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var responseTraceId = root.GetProperty("traceId").GetString();

        // Activity TraceId should take precedence over CorrelationId
        Assert.NotNull(activityTraceId);
        Assert.Equal(activityTraceId, responseTraceId);
        Assert.NotEqual(correlationId, responseTraceId);
    }
}

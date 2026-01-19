using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Dhadgar.ServiceDefaults.Resilience;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class CircuitBreakerTests
{
    // Use unique service IDs per test run to avoid state pollution
    private static string UniqueServiceId() => $"service-{Guid.NewGuid():N}";

    [Fact]
    public async Task ClosedCircuit_PassesRequestThrough()
    {
        var options = CreateOptions();
        var stateStore = new InMemoryCircuitBreakerStateStore();
        var middleware = new CircuitBreakerMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            NullLogger<CircuitBreakerMiddleware>.Instance,
            options,
            stateStore);

        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task CircuitOpens_AfterConsecutiveFailures()
    {
        var options = CreateOptions(failureThreshold: 3);
        var stateStore = new InMemoryCircuitBreakerStateStore();
        var callCount = 0;
        var serviceId = UniqueServiceId();

        var middleware = new CircuitBreakerMiddleware(
            ctx =>
            {
                callCount++;
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            },
            NullLogger<CircuitBreakerMiddleware>.Instance,
            options,
            stateStore);

        // First 3 failures should pass through to the backend
        for (int i = 0; i < 3; i++)
        {
            var context = CreateContext(serviceId: serviceId);
            await middleware.InvokeAsync(context);
            Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        }

        Assert.Equal(3, callCount);

        // After threshold, circuit should be open and return 503 without calling backend
        var openContext = CreateContext(serviceId: serviceId);
        await middleware.InvokeAsync(openContext);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, openContext.Response.StatusCode);
        Assert.Equal(3, callCount); // Backend should not be called
    }

    [Fact]
    public async Task OpenCircuit_Returns503WithProblemDetails()
    {
        var options = CreateOptions(failureThreshold: 1);
        var stateStore = new InMemoryCircuitBreakerStateStore();
        var serviceId = UniqueServiceId();

        var middleware = new CircuitBreakerMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            },
            NullLogger<CircuitBreakerMiddleware>.Instance,
            options,
            stateStore);

        // Trigger circuit open with one failure
        var failContext = CreateContext(serviceId: serviceId);
        await middleware.InvokeAsync(failContext);

        // Next request should get 503 with problem details
        var context = CreateContext(serviceId: serviceId);
        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.StartsWith("application/", context.Response.ContentType, StringComparison.Ordinal);

        var body = await ReadResponseBodyAsync(context.Response);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("Service Temporarily Unavailable", root.GetProperty("title").GetString());
        Assert.Equal(503, root.GetProperty("status").GetInt32());
        // With IncludeServiceNameInErrors = false (default), service name is NOT in detail
        Assert.Contains("temporarily unavailable", root.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        // Verify Retry-After header is set
        Assert.True(context.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task SuccessfulRequests_ResetFailureCount()
    {
        var options = CreateOptions(failureThreshold: 3);
        var stateStore = new InMemoryCircuitBreakerStateStore();
        var nextResponseStatus = StatusCodes.Status200OK;
        var serviceId = UniqueServiceId();

        var middleware = new CircuitBreakerMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = nextResponseStatus;
                return Task.CompletedTask;
            },
            NullLogger<CircuitBreakerMiddleware>.Instance,
            options,
            stateStore);

        // Two failures
        nextResponseStatus = StatusCodes.Status500InternalServerError;
        for (int i = 0; i < 2; i++)
        {
            var context = CreateContext(serviceId: serviceId);
            await middleware.InvokeAsync(context);
        }

        // One success should reset the counter
        nextResponseStatus = StatusCodes.Status200OK;
        var successContext = CreateContext(serviceId: serviceId);
        await middleware.InvokeAsync(successContext);

        // Two more failures should not open circuit (counter was reset)
        nextResponseStatus = StatusCodes.Status500InternalServerError;
        for (int i = 0; i < 2; i++)
        {
            var context = CreateContext(serviceId: serviceId);
            await middleware.InvokeAsync(context);
            Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        }
    }

    [Fact]
    public async Task DifferentServices_HaveIndependentCircuits()
    {
        var options = CreateOptions(failureThreshold: 2);
        var stateStore = new InMemoryCircuitBreakerStateStore();
        var serviceA = UniqueServiceId();
        var serviceB = UniqueServiceId();

        var middleware = new CircuitBreakerMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            },
            NullLogger<CircuitBreakerMiddleware>.Instance,
            options,
            stateStore);

        // Open circuit for service A
        for (int i = 0; i < 2; i++)
        {
            var context = CreateContext(serviceId: serviceA);
            await middleware.InvokeAsync(context);
        }

        // Service A circuit should be open
        var serviceAContext = CreateContext(serviceId: serviceA);
        await middleware.InvokeAsync(serviceAContext);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, serviceAContext.Response.StatusCode);

        // Service B circuit should still be closed
        var serviceBContext = CreateContext(serviceId: serviceB);
        await middleware.InvokeAsync(serviceBContext);
        Assert.Equal(StatusCodes.Status500InternalServerError, serviceBContext.Response.StatusCode);
    }

    [Fact]
    public async Task RequestsWithoutService_ArePassedThrough()
    {
        var options = CreateOptions(failureThreshold: 1);
        var stateStore = new InMemoryCircuitBreakerStateStore();

        var middleware = new CircuitBreakerMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            NullLogger<CircuitBreakerMiddleware>.Instance,
            options,
            stateStore);

        // Request without service ID should pass through
        var context = CreateContext(serviceId: null);
        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Only5xxStatusCodes_TriggerCircuitBreaker()
    {
        var options = CreateOptions(failureThreshold: 2);
        var stateStore = new InMemoryCircuitBreakerStateStore();
        var nextResponseStatus = StatusCodes.Status400BadRequest;
        var serviceId = UniqueServiceId();

        var middleware = new CircuitBreakerMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = nextResponseStatus;
                return Task.CompletedTask;
            },
            NullLogger<CircuitBreakerMiddleware>.Instance,
            options,
            stateStore);

        // 400 errors should not count toward circuit breaker
        for (int i = 0; i < 5; i++)
        {
            var context = CreateContext(serviceId: serviceId);
            await middleware.InvokeAsync(context);
            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        // Circuit should still be closed after multiple 4xx errors
        nextResponseStatus = StatusCodes.Status200OK;
        var successContext = CreateContext(serviceId: serviceId);
        await middleware.InvokeAsync(successContext);
        Assert.Equal(StatusCodes.Status200OK, successContext.Response.StatusCode);
    }

    private static IOptions<CircuitBreakerOptions> CreateOptions(
        int failureThreshold = 5,
        int successThreshold = 2,
        int openDurationSeconds = 30)
    {
        return Microsoft.Extensions.Options.Options.Create(new CircuitBreakerOptions
        {
            FailureThreshold = failureThreshold,
            SuccessThreshold = successThreshold,
            OpenDurationSeconds = openDurationSeconds,
            FailureStatusCodes = [500, 502, 503, 504],
            IncludeServiceNameInErrors = false // Default secure setting
        });
    }

    private static DefaultHttpContext CreateContext(string? serviceId = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        if (serviceId != null)
        {
            // Store service ID in Items for the middleware to use
            // In actual usage, this is set by YarpCircuitBreakerAdapter from IReverseProxyFeature
            context.Items["CircuitBreaker:ServiceId"] = serviceId;
        }

        return context;
    }

    private static async Task<string> ReadResponseBodyAsync(HttpResponse response)
    {
        response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}

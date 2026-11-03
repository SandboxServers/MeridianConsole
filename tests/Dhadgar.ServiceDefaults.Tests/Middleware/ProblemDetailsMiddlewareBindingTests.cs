using System.Text;
using System.Text.Json;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.Middleware;

public class ProblemDetailsMiddlewareBindingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BindingFailure_Returns400_ProblemJson_WithBadRequestType()
    {
        var logger = new RecordingLogger<ProblemDetailsMiddleware>();
        var middleware = CreateMiddleware(_ => throw new BadHttpRequestException("x", 400), logger, "Production");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
        context.Response.ContentType.Should().Be("application/problem+json");
        var body = await ReadBodyAsync(context);
        body.GetProperty("type").GetString().Should().Be("https://meridian.console/errors/bad-request");
        body.TryGetProperty("stackTrace", out _).Should().BeFalse();
    }

    [Fact]
    public async Task BindingFailure_LogsEvent9301Warning_AndNoUnhandledText()
    {
        var logger = new RecordingLogger<ProblemDetailsMiddleware>();
        var middleware = CreateMiddleware(_ => throw new BadHttpRequestException("x", 400), logger, "Production");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.EventId.Id.Should().Be(9301);
        entry.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        logger.Entries.Should().NotContain(e => e.Message.Contains("Unhandled exception", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidOperation_Returns500_WithFallbackLog()
    {
        var logger = new RecordingLogger<ProblemDetailsMiddleware>();
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("boom"), logger, "Production");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(500);
        logger.Entries.Should().ContainSingle(e =>
            e.Message.StartsWith("Unhandled exception (fallback handler)", StringComparison.Ordinal));
    }

    private static ProblemDetailsMiddleware CreateMiddleware(
        RequestDelegate next, RecordingLogger<ProblemDetailsMiddleware> logger, string environment) =>
        new(next, logger, BindingFailureProblemTests.Env(environment), new FakeTimeProvider(Now));

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

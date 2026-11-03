using System.Text;
using System.Text.Json;
using Dhadgar.ServiceDefaults.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.Errors;

public class GlobalExceptionHandlerBindingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BindingFailure_Returns400_ProblemJson_WithBadRequestType()
    {
        var logger = new RecordingLogger<GlobalExceptionHandler>();
        var handler = CreateHandler(logger, "Production");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(context, new BadHttpRequestException("x", 400), CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(400);
        context.Response.ContentType.Should().Be("application/problem+json");
        var body = await ReadBodyAsync(context);
        body.GetProperty("type").GetString().Should().Be("https://meridian.console/errors/bad-request");
        body.TryGetProperty("stackTrace", out _).Should().BeFalse();
    }

    [Fact]
    public async Task BindingFailure_LogsEvent9301Warning_AndNoUnhandledText()
    {
        var logger = new RecordingLogger<GlobalExceptionHandler>();
        var handler = CreateHandler(logger, "Production");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await handler.TryHandleAsync(context, new BadHttpRequestException("x", 400), CancellationToken.None);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.EventId.Id.Should().Be(9301);
        entry.Level.Should().Be(LogLevel.Warning);
        logger.Entries.Should().NotContain(e => e.Message.Contains("Unhandled exception", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidOperation_Returns500_WithUnhandledLog()
    {
        var logger = new RecordingLogger<GlobalExceptionHandler>();
        var handler = CreateHandler(logger, "Production");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(500);
        logger.Entries.Should().ContainSingle(e => e.Message.StartsWith("Unhandled exception.", StringComparison.Ordinal));
    }

    private static GlobalExceptionHandler CreateHandler(RecordingLogger<GlobalExceptionHandler> logger, string environment) =>
        new(logger, BindingFailureProblemTests.Env(environment), new FakeTimeProvider(Now));

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

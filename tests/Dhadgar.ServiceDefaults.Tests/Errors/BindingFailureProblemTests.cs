using Dhadgar.ServiceDefaults.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.Errors;

public class BindingFailureProblemTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(400, "https://meridian.console/errors/bad-request", "Bad Request")]
    [InlineData(408, "https://meridian.console/errors/request-timeout", "Request Timeout")]
    [InlineData(413, "https://meridian.console/errors/payload-too-large", "Payload Too Large")]
    [InlineData(415, "https://meridian.console/errors/unsupported-media-type", "Unsupported Media Type")]
    [InlineData(431, "https://meridian.console/errors/request-header-fields-too-large", "Request Header Fields Too Large")]
    [InlineData(422, "https://meridian.console/errors/bad-request", "Bad Request")]
    public void TryCreate_ClientErrorStatus_MapsTypeAndTitle(int statusCode, string type, string title)
    {
        var handled = BindingFailureProblem.TryCreate(
            new BadHttpRequestException("bad", statusCode), new DefaultHttpContext(), Env("Production"),
            "trace", "corr", Now, out var problem);

        handled.Should().BeTrue();
        problem!.Status.Should().Be(statusCode);
        problem.Type.Should().Be(type);
        problem.Title.Should().Be(title);
    }

    [Theory]
    [InlineData(400, "https://meridian.console/errors/bad-request")]
    [InlineData(405, "https://meridian.console/errors/method-not-allowed")]
    [InlineData(408, "https://meridian.console/errors/request-timeout")]
    [InlineData(413, "https://meridian.console/errors/payload-too-large")]
    [InlineData(415, "https://meridian.console/errors/unsupported-media-type")]
    [InlineData(431, "https://meridian.console/errors/request-header-fields-too-large")]
    [InlineData(418, "https://meridian.console/errors/bad-request")]
    public void ProblemTypeTable_KnownStatuses_KeepTheirTypes(int statusCode, string type)
    {
        ProblemTypeTable.Describe(statusCode).Type.Should().Be(type);
    }

    [Fact]
    public void TryCreate_Status500_IsNotHandled()
    {
        var handled = BindingFailureProblem.TryCreate(
            new BadHttpRequestException("bad", 500), new DefaultHttpContext(), Env("Production"),
            "trace", "corr", Now, out var problem);

        handled.Should().BeFalse();
        problem.Should().BeNull();
    }

    [Fact]
    public void TryCreate_NonBadHttpRequestException_IsNotHandled()
    {
        var handled = BindingFailureProblem.TryCreate(
            new InvalidOperationException("boom"), new DefaultHttpContext(), Env("Production"),
            "trace", "corr", Now, out var problem);

        handled.Should().BeFalse();
        problem.Should().BeNull();
    }

    [Fact]
    public void TryCreate_Production_DetailIsFixedText()
    {
        BindingFailureProblem.TryCreate(
            new BadHttpRequestException("secret parser detail", 400), new DefaultHttpContext(), Env("Production"),
            "trace", "corr", Now, out var problem);

        problem!.Detail.Should().Be("The request could not be read.");
        problem.Detail.Should().NotContain("secret parser detail");
    }

    [Fact]
    public void TryCreate_Testing_DetailIsExceptionMessage()
    {
        BindingFailureProblem.TryCreate(
            new BadHttpRequestException("parser detail", 400), new DefaultHttpContext(), Env("Testing"),
            "trace", "corr", Now, out var problem);

        problem!.Detail.Should().Be("parser detail");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public void TryCreate_NeverAddsStackTrace(string environment)
    {
        BindingFailureProblem.TryCreate(
            new BadHttpRequestException("bad", 400), new DefaultHttpContext(), Env(environment),
            "trace", "corr", Now, out var problem);

        problem!.Extensions.Should().NotContainKey("stackTrace");
        problem.Extensions["traceId"].Should().Be("trace");
        problem.Extensions["correlationId"].Should().Be("corr");
        problem.Extensions["timestamp"].Should().Be(Now);
    }

    [Fact]
    public void KestrelBadRequestException_DerivesFromHttpBadRequestException()
    {
        // D-ML233: matching Microsoft.AspNetCore.Http.BadHttpRequestException covers the Kestrel type too.
        // The Kestrel.Core type is marked obsolete (it forwards to the Http type); the test only reads its base type.
#pragma warning disable CS0618
        var kestrelType = typeof(Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException);
#pragma warning restore CS0618
        kestrelType.IsSubclassOf(typeof(BadHttpRequestException)).Should().BeTrue();
    }

    [Fact]
    public void Log_WritesWarningWithEvent9301_AndNoUnhandledText()
    {
        var logger = new RecordingLogger<BindingFailureProblemTests>();

        BindingFailureProblem.Log(logger, 400, "/agents/enroll", "trace");

        logger.Entries.Should().ContainSingle();
        var entry = logger.Entries[0];
        entry.Level.Should().Be(LogLevel.Warning);
        entry.EventId.Id.Should().Be(9301);
        entry.Message.Should().NotContain("Unhandled exception");
    }

    internal static IHostEnvironment Env(string name) => new TestHostEnvironment { EnvironmentName = name };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

/// <summary>Logger that records every entry, for asserting on log level, event id and text.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<RecordedLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new RecordedLogEntry(logLevel, eventId, formatter(state, exception), exception));
    }
}

internal sealed record RecordedLogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);

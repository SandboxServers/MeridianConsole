using System.Text.Json;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Tests.Processes;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Dhadgar.Lab.Tools.Logs;

namespace Dhadgar.Lab.Tests.Tools;

[Collection("LabProcesses")]
public sealed class LogsTailToolTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParseSince_RelativeMinutes_SubtractsFromNow()
    {
        Result<DateTimeOffset> parsed = LogsTailTool.ParseSince("-5m", Now);

        Assert.True(parsed.IsSuccess, parsed.Error);
        Assert.Equal(Now.AddMinutes(-5), parsed.Value);
    }

    [Fact]
    public void ParseSince_RelativeSeconds_SubtractsFromNow()
    {
        Result<DateTimeOffset> parsed = LogsTailTool.ParseSince("-30s", Now);

        Assert.True(parsed.IsSuccess, parsed.Error);
        Assert.Equal(Now.AddSeconds(-30), parsed.Value);
    }

    [Fact]
    public void ParseSince_IsoTimestamp_IsParsed()
    {
        Result<DateTimeOffset> parsed = LogsTailTool.ParseSince("2026-10-27T09:30:00+00:00", Now);

        Assert.True(parsed.IsSuccess, parsed.Error);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 9, 30, 0, TimeSpan.Zero), parsed.Value);
    }

    [Fact]
    public void ParseSince_Garbage_IsRefused()
    {
        Result<DateTimeOffset> parsed = LogsTailTool.ParseSince("yesterday-ish", Now);

        Assert.True(parsed.IsFailure);
    }

    [SkippableFact]
    public async Task Invoke_UnknownProcess_ReturnsNotFound()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        using var harness = new SupervisorHarness();
        var tool = new LogsTailTool(harness.Supervisor, harness.Time);

        Result<JsonElement> result = await tool.InvokeAsync(
            Arguments("{\"process\":\"nope\"}"),
            new LabCallContext("call-1", null, "test"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.NotFound]", result.Error);
    }

    [SkippableFact]
    public async Task Invoke_DaemonProcess_ReturnsEmptyList()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        using var harness = new SupervisorHarness();
        var tool = new LogsTailTool(harness.Supervisor, harness.Time);

        Result<JsonElement> result = await tool.InvokeAsync(
            Arguments("{\"process\":\"daemon\"}"),
            new LabCallContext("call-2", null, "test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value.GetProperty("lines").GetArrayLength());
    }

    [SkippableFact]
    public async Task Invoke_MaxLinesAboveCap_IsRefused()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        using var harness = new SupervisorHarness();
        var tool = new LogsTailTool(harness.Supervisor, harness.Time);

        Result<JsonElement> result = await tool.InvokeAsync(
            Arguments("{\"process\":\"daemon\",\"max_lines\":2001}"),
            new LabCallContext("call-3", null, "test"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.InvalidArgument]", result.Error);
    }

    private static JsonElement Arguments(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

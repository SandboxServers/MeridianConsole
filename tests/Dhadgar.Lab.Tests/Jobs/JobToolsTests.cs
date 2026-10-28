using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Tools.Jobs;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Jobs;

public sealed class JobToolsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-job-tools", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task JobStatus_NoJobs_ReturnsEmptyList()
    {
        using JobRunner runner = CreateRunner();
        var tool = new JobStatusTool(runner);

        Result<JsonElement> result = await tool.InvokeAsync(
            JsonSerializer.SerializeToElement(new { }), new LabCallContext("c1", null, "test"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value.GetProperty("jobs").GetArrayLength());
    }

    [Fact]
    public async Task JobStatus_UnknownId_IsNotFound()
    {
        using JobRunner runner = CreateRunner();
        var tool = new JobStatusTool(runner);
        var args = JsonSerializer.SerializeToElement(new { job_id = "0123456789ab" });

        Result<JsonElement> result = await tool.InvokeAsync(args, new LabCallContext("c1", null, "test"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.NotFound]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobCancel_UnknownId_IsNotFound()
    {
        using JobRunner runner = CreateRunner();
        var tool = new JobCancelTool(runner);
        var args = JsonSerializer.SerializeToElement(new { job_id = "0123456789ab" });

        Result<JsonElement> result = await tool.InvokeAsync(args, new LabCallContext("c1", null, "test"), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    private JobRunner CreateRunner() => new(LabPaths.FromRoot(_root), new LeaseBook(_time, new NoEvents(), new LeaseOptions()),
        new PathValidator(NullLogger<PathValidator>.Instance), _time, NullLogger<JobRunner>.Instance);

    private sealed class NoEvents : ILabEvents
    {
        public void Emit(LabEvent labEvent)
        {
        }
    }
}

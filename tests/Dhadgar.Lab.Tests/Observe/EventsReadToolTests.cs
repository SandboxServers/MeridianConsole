using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Tools.Observe;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Observe;

/// <summary>
/// <c>events_read</c> finds a run only through the run index, never by treating the id as a path.
/// </summary>
public sealed class EventsReadToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "l48-events-" + Guid.NewGuid().ToString("N"));
    private readonly IPathValidator _validator = new PathValidator(NullLogger<PathValidator>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void RunIdWithDotDot_NotFound()
    {
        string uatRuns = Path.Combine(_root, "uat-runs");
        Directory.CreateDirectory(uatRuns);

        Result<string> result = RunIndex.Find([uatRuns], "..", _validator);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.NotFound]", result.Error);
    }

    [Fact]
    public void RunIdWithSeparator_NotFound()
    {
        string uatRuns = Path.Combine(_root, "uat-runs");
        Directory.CreateDirectory(uatRuns);

        Assert.True(RunIndex.Find([uatRuns], "..\\..\\etc", _validator).IsFailure);
        Assert.True(RunIndex.Find([uatRuns], "a/b", _validator).IsFailure);
    }

    [Fact]
    public void KnownRunId_FoundByRunJson()
    {
        string uatRuns = Path.Combine(_root, "uat-runs");
        string folder = Path.Combine(uatRuns, "20260101-build-run-abc");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "run.json"), "{\"run_id\":\"run-abc\"}");

        Result<string> result = RunIndex.Find([uatRuns], "run-abc", _validator);

        Assert.True(result.IsSuccess);
        Assert.Equal(folder, result.Value);
    }

    [Fact]
    public void UnknownRunId_NotFound()
    {
        string uatRuns = Path.Combine(_root, "uat-runs");
        Directory.CreateDirectory(uatRuns);

        Assert.True(RunIndex.Find([uatRuns], "run-missing", _validator).IsFailure);
    }
}

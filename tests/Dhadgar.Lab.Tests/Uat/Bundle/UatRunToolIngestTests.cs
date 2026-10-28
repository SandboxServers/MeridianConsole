using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tests.Results;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Uat.Bundle;

/// <summary>
/// The run-end ingest of <c>uat_run</c> (D-ML91): no bundle gives no <c>ingest</c> field, and a written bundle is ingested
/// into the results index.
/// </summary>
public sealed class UatRunToolIngestTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-run-ingest-" + Path.GetRandomFileName());
    private readonly string _uatRuns;
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);

    public UatRunToolIngestTests()
    {
        _uatRuns = Path.Combine(_root, "uat-runs");
        Directory.CreateDirectory(_uatRuns);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A WAL file still open on a failing test; the temp folder is cleaned by the OS.
        }
    }

    [Fact]
    public async Task IngestAfterRun_NullFolder_IsNull()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        using var results = NewService(redactor);

        JsonObject? ingest = await UatRunTool.IngestAfterRunAsync(results, null, CancellationToken.None);

        Assert.Null(ingest);
    }

    [Fact]
    public async Task IngestAfterRun_WrittenBundle_IsIngested()
    {
        string folder = BundleFixtures.Write(_uatRuns, "run-after", "b1", 42, new RowSpec("nodes", "enroll", "PASS"));
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        using var results = NewService(redactor);

        JsonObject? ingest = await UatRunTool.IngestAfterRunAsync(results, folder, CancellationToken.None);

        Assert.NotNull(ingest);
        Assert.True(ingest["ok"]!.GetValue<bool>(), ingest["error"]?.GetValue<string>());
        Result<RunRecord> run = await results.ReadAsync(db => db.RunOf("run-after"), CancellationToken.None);
        Assert.True(run.IsSuccess, run.Error);
    }

    private ResultsService NewService(Redactor redactor) =>
        new(LabPaths.FromRoot(_root), _validator, redactor, new ServiceCollection().BuildServiceProvider(),
            NullLoggerFactory.Instance, new FakeTimeProvider(At));
}

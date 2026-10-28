using Dhadgar.Lab.Twins;
using Dhadgar.Lab.Uat.Spec;
using Xunit;

namespace Dhadgar.Lab.Tests.Twins;

/// <summary>
/// The shipped twin experiment (L53, C19's example) parses, its id matches its file name, and every base row it names exists in the
/// catalog spec it cites. A failure is a finding in the shipped file, never an expected value to edit.
/// </summary>
public sealed class ShippedTwinExperimentTests
{
    private const string ExperimentFile = "enroll-latency-invariance.toml";

    [Fact]
    public async Task ShippedExperiment_ParsesAndCitesExistingBaseRows()
    {
        string specs = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "docs", "guides", "uat-specs");
        string experimentText = await File.ReadAllTextAsync(Path.Combine(specs, "twins", ExperimentFile), CancellationToken.None);

        var parsed = TwinSpecLoader.Load(experimentText);
        Assert.True(parsed.IsSuccess, parsed.Error);
        TwinSpec experiment = parsed.Value!;
        Assert.Equal(Path.GetFileNameWithoutExtension(ExperimentFile), experiment.Id);
        Assert.Equal("catalog/twin-enroll.toml", experiment.Base.Spec);

        string catalogText = await File.ReadAllTextAsync(Path.Combine(specs, "catalog", "twin-enroll.toml"), CancellationToken.None);
        SpecLoadOutcome loaded = SpecLoader.LoadText(experiment.Base.Spec, catalogText);
        Assert.Empty(loaded.Problems);
        Assert.NotNull(loaded.Spec);

        IReadOnlyCollection<string> rowIds = loaded.Spec!.Rows.Select(row => row.Id).ToArray();
        Assert.True(TwinSpecLoader.CheckBaseRows(experiment, rowIds).IsSuccess);
    }
}

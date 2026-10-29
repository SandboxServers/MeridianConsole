using Dhadgar.Lab.Results;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// The coverage lists partition the C12 signature kinds (D-ML93). A packet that adds a kind to one list and not the other fails here.
/// </summary>
public sealed class EvidenceCoverageTests
{
    [Fact]
    public void EvidenceCoverage_KindsPartitionC12()
    {
        var union = EvidenceCoverage.BuiltKinds.Concat(EvidenceCoverage.NotBuiltKinds).ToList();

        Assert.Equal(union.Count, union.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(SignatureBuilder.Kinds.OrderBy(k => k, StringComparer.Ordinal), union.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void EvidenceCoverage_HistIsBuilt_AndNothingIsLeftNotIngested()
    {
        Assert.Contains("hist", EvidenceCoverage.BuiltKinds);
        Assert.DoesNotContain("hist", EvidenceCoverage.NotBuiltKinds);
        Assert.Empty(EvidenceCoverage.NotIngested);
    }

    [Fact]
    public void ToJson_NamesEveryListAndTheNote()
    {
        var json = EvidenceCoverage.ToJson();

        Assert.Equal(EvidenceCoverage.BuiltKinds.Count, json["built_kinds"]!.AsArray().Count);
        Assert.Equal(EvidenceCoverage.NotBuiltKinds.Count, json["not_built_kinds"]!.AsArray().Count);
        Assert.Equal(EvidenceCoverage.NotIngested.Count, json["not_ingested"]!.AsArray().Count);
        Assert.Equal(EvidenceCoverage.Note, json["note"]!.GetValue<string>());
    }
}

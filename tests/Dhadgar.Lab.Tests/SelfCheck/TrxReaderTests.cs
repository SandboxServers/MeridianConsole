using Dhadgar.Lab.SelfCheck;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.SelfCheck;

/// <summary>L55: the TRX reader that the self-check judge reads (<c>UnitTestResult</c> <c>testName</c> and <c>outcome</c>).</summary>
public sealed class TrxReaderTests
{
    private const string Trx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun id="00000000-0000-0000-0000-000000000001" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testName="Dhadgar.Lab.Tests.Faults.FaultBookTests.Failed_Case" outcome="Failed" />
            <UnitTestResult testName="Dhadgar.Lab.Tests.Faults.FaultBookTests.Passed_Case" outcome="Passed" />
            <UnitTestResult testName="Dhadgar.Lab.Tests.Faults.FaultBookTests.NotRun_Case" outcome="NotExecuted" />
          </Results>
        </TestRun>
        """;

    [Fact]
    public void Read_ReturnsEachOutcome()
    {
        Result<IReadOnlyList<TrxTestResult>> result = TrxReader.Read(Trx);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(3, result.Value!.Count);
        Assert.Equal(new TrxTestResult("Dhadgar.Lab.Tests.Faults.FaultBookTests.Failed_Case", "Failed"), result.Value[0]);
        Assert.Equal("Passed", result.Value[1].Outcome);
        Assert.Equal("NotExecuted", result.Value[2].Outcome);
    }

    [Fact]
    public void Read_NoResults_IsAnEmptyList_NotAFailure()
    {
        const string empty = """<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results /></TestRun>""";

        Result<IReadOnlyList<TrxTestResult>> result = TrxReader.Read(empty);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public void Read_DoctypeIsRefused()
    {
        const string hostile = """<!DOCTYPE TestRun [<!ENTITY x "y">]><TestRun><UnitTestResult testName="a" outcome="&x;" /></TestRun>""";

        Result<IReadOnlyList<TrxTestResult>> result = TrxReader.Read(hostile);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Read_MalformedXml_IsAFailure_NotAnEmptyList()
    {
        Result<IReadOnlyList<TrxTestResult>> result = TrxReader.Read("<TestRun><UnitTestResult testName=\"a\"");

        Assert.False(result.IsSuccess);
        Assert.Contains("[Lab.InvalidArgument]", result.Error);
    }

    [Fact]
    public void Read_ResultMissingOutcome_IsAFailure()
    {
        Result<IReadOnlyList<TrxTestResult>> result = TrxReader.Read("<TestRun><UnitTestResult testName=\"a\" /></TestRun>");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void SelfCheckOptions_ParsesFlags_AndRefusesUnknown()
    {
        var parsed = SelfCheckOptions.Parse(["selfcheck", "--only", "LM1-journal-after-inject", "--keep"]);
        Assert.True(parsed.IsSuccess, parsed.Error);
        Assert.Equal(new SelfCheckOptions("LM1-journal-after-inject", true, false), parsed.Value);

        Assert.False(SelfCheckOptions.Parse(["--only"]).IsSuccess);
        Assert.False(SelfCheckOptions.Parse(["--bogus"]).IsSuccess);
        Assert.False(SelfCheckOptions.Parse(["--recovery", "--only", "LM1"]).IsSuccess);
    }
}

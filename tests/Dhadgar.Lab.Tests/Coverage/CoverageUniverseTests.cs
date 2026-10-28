using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Coverage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Lab.Tests.Coverage;

/// <summary>
/// L52: the shipped <c>coverage-universe.json</c> loads and validates; the loader rejects a bad universe.
/// </summary>
public sealed class CoverageUniverseTests
{
    [Fact]
    public void ShippedFile_Loads()
    {
        string labRoot = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "src", "Tools", "Dhadgar.Lab");

        var result = CoverageUniverse.Load(labRoot, new PathValidator(NullLogger<PathValidator>.Instance));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Contains("nodes", result.Value.Services.Keys);
        Assert.Contains("gateway", result.Value.Services.Keys);
        Assert.NotEmpty(result.Value.Services["nodes"].Routes);
    }

    [Fact]
    public void ShippedFile_EveryTransition_NamesAKnownEntity()
    {
        string labRoot = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "src", "Tools", "Dhadgar.Lab");
        CoverageUniverse universe = CoverageUniverse.Load(labRoot, new PathValidator(NullLogger<PathValidator>.Instance)).Value!;

        foreach (CoverageService service in universe.Services.Values)
        {
            foreach (string transition in service.Transitions)
            {
                Assert.True(CoverageUniverse.IsKnownTransition(transition), "Unknown transition entry: " + transition);
            }
        }
    }

    [Fact]
    public void Parse_UnknownService_IsFailure()
    {
        string json = "{\"schema\":1,\"services\":{\"billing\":{\"routes\":[],\"error_codes\":[],\"transitions\":[]}}}";

        var result = CoverageUniverse.Parse(json);

        Assert.True(result.IsFailure);
        Assert.Contains("unknown service", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DuplicateRoute_IsFailure()
    {
        string json = """
            {"schema":1,"services":{"nodes":{"routes":[
              {"method":"GET","template":"/api/v1/agents/ca-certificate"},
              {"method":"GET","template":"/api/v1/agents/ca-certificate"}],
              "error_codes":[],"transitions":[]}}}
            """;

        var result = CoverageUniverse.Parse(json);

        Assert.True(result.IsFailure);
        Assert.Contains("duplicate route", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TransitionWithUnknownEntity_IsFailure()
    {
        string json = "{\"schema\":1,\"services\":{\"nodes\":{\"routes\":[],\"error_codes\":[],\"transitions\":[\"widget:A->B\"]}}}";

        var result = CoverageUniverse.Parse(json);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Parse_WrongSchema_IsFailure()
    {
        string json = "{\"schema\":2,\"services\":{}}";

        Assert.True(CoverageUniverse.Parse(json).IsFailure);
    }

    [Fact]
    public void Parse_NotJson_IsFailureAndDoesNotEchoTheText()
    {
        const string secretLookingText = "not-json-SECRET-VALUE";

        var result = CoverageUniverse.Parse(secretLookingText);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("SECRET-VALUE", result.Error, StringComparison.Ordinal);
    }
}

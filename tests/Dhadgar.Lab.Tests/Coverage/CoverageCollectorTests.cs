using Dhadgar.Lab.Coverage;
using Xunit;

namespace Dhadgar.Lab.Tests.Coverage;

/// <summary>
/// L52: a 5xx is counted outside the universe, a 4xx hits its route, and invariant violations are <c>invariant_check</c> hits.
/// </summary>
public sealed class CoverageCollectorTests
{
    private const string Json = """
        {
          "schema": 1,
          "services": {
            "nodes": {
              "routes": [ { "method": "POST", "template": "/api/v1/agents/enroll" } ],
              "error_codes": [ "invalid_token" ],
              "transitions": [ "node:Online->Degraded" ]
            },
            "gateway": { "routes": [], "error_codes": [], "transitions": [] }
          }
        }
        """;

    private static CoverageUniverse Universe()
    {
        Dhadgar.Shared.Results.Result<CoverageUniverse> parsed = CoverageUniverse.Parse(Json);
        Assert.True(parsed.IsSuccess);
        return parsed.Value;
    }

    [Fact]
    public void ServerError_IsKeyedAsFiveXx_NotAUniverseHit()
    {
        string? key = CoverageKeys.RouteKey(Universe(), "nodes", "POST", 503, "/api/v1/agents/enroll");

        Assert.Equal("nodes POST /api/v1/agents/enroll 5xx", key);
    }

    [Fact]
    public void Unauthorised_HitsTheRouteAs4xx()
    {
        string? key = CoverageKeys.RouteKey(Universe(), "nodes", "POST", 401, "/api/v1/agents/enroll");

        Assert.Equal("nodes POST /api/v1/agents/enroll 4xx", key);
    }

    [Fact]
    public void UnmatchedPath_IsKeyedWithoutThePath()
    {
        string? key = CoverageKeys.RouteKey(Universe(), "nodes", "GET", 404, "/api/v1/agents/secret-token-123");

        Assert.Equal("nodes GET ?unmatched 4xx", key);
    }

    [Fact]
    public void RedirectsAndUnknownServices_AreNotCounted()
    {
        Assert.Null(CoverageKeys.RouteKey(Universe(), "nodes", "POST", 302, "/api/v1/agents/enroll"));
        Assert.Null(CoverageKeys.RouteKey(Universe(), "unknown", "POST", 200, "/api/v1/agents/enroll"));
    }

    [Fact]
    public void Invariants_EachViolationIsAnInvariantCheckHit()
    {
        const string invariants = """
            {"schema":1,"violations":[
              {"invariant_id":"I1","service":"nodes","check_id":"unhandled","allowed":false},
              {"invariant_id":"I1","service":"nodes","check_id":"unhandled","allowed":false}],
             "errors":[],"skipped":[]}
            """;

        Dhadgar.Shared.Results.Result<IReadOnlyList<Dhadgar.Lab.Results.CoverageRecord>> hits =
            CoverageCollector.HitsOfInvariants("run-1", invariants);

        Assert.True(hits.IsSuccess);
        Dhadgar.Lab.Results.CoverageRecord hit = Assert.Single(hits.Value);
        Assert.Equal(CoverageKeys.InvariantCheck, hit.Kind);
        Assert.Equal("I1/unhandled", hit.Key);
        Assert.Equal(2, hit.Hits);
    }

    [Fact]
    public void MalformedInvariants_AreAFailureNotAThrow()
    {
        Dhadgar.Shared.Results.Result<IReadOnlyList<Dhadgar.Lab.Results.CoverageRecord>> hits =
            CoverageCollector.HitsOfInvariants("run-1", "{not json");

        Assert.True(hits.IsFailure);
    }

    [Fact]
    public void Count_SumsRepeatedHitsPerKindAndKey()
    {
        var records = CoverageKeys.Count("run-1",
        [
            (CoverageKeys.ErrorCode, "nodes:invalid_token"),
            (CoverageKeys.ErrorCode, "nodes:invalid_token"),
            (CoverageKeys.Transition, "node:Online->Degraded"),
        ]);

        Assert.Equal(2, records.Count);
        Assert.Equal(2, records.Single(r => r.Key == "nodes:invalid_token").Hits);
    }
}

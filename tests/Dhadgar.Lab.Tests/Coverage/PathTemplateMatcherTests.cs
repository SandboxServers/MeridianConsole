using Dhadgar.Lab.Coverage;
using Xunit;

namespace Dhadgar.Lab.Tests.Coverage;

/// <summary>
/// L52: a concrete path maps to its most specific template; a path that matches nothing is keyed as unmatched.
/// </summary>
public sealed class PathTemplateMatcherTests
{
    private static readonly CoverageRoute Heartbeat = new("POST", "/api/v1/agents/{nodeId}/heartbeat");
    private static readonly CoverageRoute Enroll = new("POST", "/api/v1/agents/enroll");
    private static readonly CoverageRoute AgentsCatchAll = new("*", "/api/v1/agents/{**catch-all}");

    [Fact]
    public void GuidSegment_MatchesTheNodeIdPlaceholder()
    {
        string path = "/api/v1/agents/" + Guid.NewGuid().ToString("D") + "/heartbeat";

        string? template = PathTemplateMatcher.MatchTemplate([Heartbeat], "POST", path);

        Assert.Equal("/api/v1/agents/{nodeId}/heartbeat", template);
    }

    [Fact]
    public void MostSpecificTemplate_Wins_OverACatchAll()
    {
        string? template = PathTemplateMatcher.MatchTemplate([AgentsCatchAll, Enroll], "POST", "/api/v1/agents/enroll");

        Assert.Equal("/api/v1/agents/enroll", template);
    }

    [Fact]
    public void LiteralSegment_BeatsAPlaceholder()
    {
        var routes = new[] { new CoverageRoute("GET", "/a/{x}"), new CoverageRoute("GET", "/a/b") };

        Assert.Equal("/a/b", PathTemplateMatcher.MatchTemplate(routes, "GET", "/a/b"));
        Assert.Equal("/a/{x}", PathTemplateMatcher.MatchTemplate(routes, "GET", "/a/c"));
    }

    [Fact]
    public void CatchAll_MatchesTheRestOfThePath()
    {
        string? template = PathTemplateMatcher.MatchTemplate([AgentsCatchAll], "GET", "/api/v1/agents/a/b/c");

        Assert.Equal("/api/v1/agents/{**catch-all}", template);
    }

    [Fact]
    public void UnmatchedPath_ReturnsNull()
    {
        string? template = PathTemplateMatcher.MatchTemplate([Enroll], "GET", "/api/v1/agents/enroll");

        Assert.Null(template);
    }

    [Fact]
    public void EmptySegment_DoesNotMatchAPlaceholder()
    {
        string? template = PathTemplateMatcher.MatchTemplate([Heartbeat], "POST", "/api/v1/agents//heartbeat");

        Assert.Null(template);
    }

    [Fact]
    public void QueryString_IsIgnored()
    {
        string? template = PathTemplateMatcher.MatchTemplate([Enroll], "POST", "/api/v1/agents/enroll?x=1");

        Assert.Equal("/api/v1/agents/enroll", template);
    }

    [Fact]
    public void WildcardMethod_MatchesAnyMethod()
    {
        string? template = PathTemplateMatcher.MatchTemplate([AgentsCatchAll], "DELETE", "/api/v1/agents/x");

        Assert.Equal("/api/v1/agents/{**catch-all}", template);
    }

    [Theory]
    [InlineData(200, "2xx")]
    [InlineData(401, "4xx")]
    [InlineData(503, "5xx")]
    public void StatusClass_GroupsByHundreds(int statusCode, string expected)
    {
        Assert.Equal(expected, PathTemplateMatcher.StatusClass(statusCode));
    }

    [Fact]
    public void UnmatchedKey_NamesTheServiceMethodAndClassOnly()
    {
        Assert.Equal("nodes GET ?unmatched 5xx", PathTemplateMatcher.UnmatchedKey("nodes", "get", "5xx"));
    }
}

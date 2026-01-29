using Dhadgar.Nodes.Models;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// Tests for NodeListQuery model validation and helper methods.
/// </summary>
public sealed class NodeListQueryTests
{
    #region Pagination Tests

    [Fact]
    public void NormalizedPage_ReturnsMinimum1_WhenPageIsZeroOrNegative()
    {
        var query = new NodeListQuery { Page = 0 };
        Assert.Equal(1, query.NormalizedPage);

        query.Page = -5;
        Assert.Equal(1, query.NormalizedPage);
    }

    [Fact]
    public void NormalizedPageSize_ClampsToValidRange()
    {
        var query = new NodeListQuery { PageSize = 0 };
        Assert.Equal(1, query.NormalizedPageSize);

        query.PageSize = 150;
        Assert.Equal(100, query.NormalizedPageSize);

        query.PageSize = 50;
        Assert.Equal(50, query.NormalizedPageSize);
    }

    [Fact]
    public void Skip_CalculatesCorrectOffset()
    {
        var query = new NodeListQuery { Page = 1, PageSize = 20 };
        Assert.Equal(0, query.Skip);

        query.Page = 2;
        Assert.Equal(20, query.Skip);

        query.Page = 3;
        Assert.Equal(40, query.Skip);
    }

    #endregion

    #region Sorting Tests

    [Theory]
    [InlineData("asc", true)]
    [InlineData("ASC", true)]
    [InlineData("Asc", true)]
    [InlineData("desc", false)]
    [InlineData("DESC", false)]
    [InlineData("Desc", false)]
    public void IsAscending_ParsesSortOrderCorrectly(string sortOrder, bool expected)
    {
        var query = new NodeListQuery { SortOrder = sortOrder };
        Assert.Equal(expected, query.IsAscending);
    }

    [Fact]
    public void Defaults_AreCorrect()
    {
        var query = new NodeListQuery();

        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
        Assert.Equal("name", query.SortBy);
        Assert.Equal("asc", query.SortOrder);
        Assert.Null(query.Status);
        Assert.Null(query.Platform);
        Assert.Null(query.MinHealthScore);
        Assert.Null(query.MaxHealthScore);
        Assert.Null(query.HasActiveServers);
        Assert.Null(query.Search);
        Assert.Null(query.Tags);
        Assert.False(query.IncludeDecommissioned);
    }

    #endregion

    #region Tags Parsing Tests

    [Fact]
    public void ParseTagsFilter_ReturnsEmptyList_WhenTagsIsNull()
    {
        var query = new NodeListQuery { Tags = null };
        var tags = query.ParseTagsFilter();
        Assert.Empty(tags);
    }

    [Fact]
    public void ParseTagsFilter_ReturnsEmptyList_WhenTagsIsEmpty()
    {
        var query = new NodeListQuery { Tags = "" };
        var tags = query.ParseTagsFilter();
        Assert.Empty(tags);
    }

    [Fact]
    public void ParseTagsFilter_ReturnsEmptyList_WhenTagsIsWhitespace()
    {
        var query = new NodeListQuery { Tags = "   " };
        var tags = query.ParseTagsFilter();
        Assert.Empty(tags);
    }

    [Fact]
    public void ParseTagsFilter_ParsesSingleTag()
    {
        var query = new NodeListQuery { Tags = "production" };
        var tags = query.ParseTagsFilter();

        Assert.Single(tags);
        Assert.Equal("production", tags[0]);
    }

    [Fact]
    public void ParseTagsFilter_ParsesMultipleTags()
    {
        var query = new NodeListQuery { Tags = "production,staging,test" };
        var tags = query.ParseTagsFilter();

        Assert.Equal(3, tags.Count);
        Assert.Contains("production", tags);
        Assert.Contains("staging", tags);
        Assert.Contains("test", tags);
    }

    [Fact]
    public void ParseTagsFilter_NormalizesToLowercase()
    {
        var query = new NodeListQuery { Tags = "Production,STAGING,Test" };
        var tags = query.ParseTagsFilter();

        Assert.All(tags, t => Assert.Equal(t.ToLowerInvariant(), t));
    }

    [Fact]
    public void ParseTagsFilter_TrimsWhitespace()
    {
        var query = new NodeListQuery { Tags = " production , staging , test " };
        var tags = query.ParseTagsFilter();

        Assert.Equal(3, tags.Count);
        Assert.Equal("production", tags[0]);
        Assert.Equal("staging", tags[1]);
        Assert.Equal("test", tags[2]);
    }

    [Fact]
    public void ParseTagsFilter_RemovesDuplicates()
    {
        var query = new NodeListQuery { Tags = "production,production,Production" };
        var tags = query.ParseTagsFilter();

        Assert.Single(tags);
        Assert.Equal("production", tags[0]);
    }

    [Fact]
    public void ParseTagsFilter_RemovesEmptyEntries()
    {
        var query = new NodeListQuery { Tags = "production,,staging,,,test" };
        var tags = query.ParseTagsFilter();

        Assert.Equal(3, tags.Count);
        Assert.DoesNotContain("", tags);
    }

    #endregion
}

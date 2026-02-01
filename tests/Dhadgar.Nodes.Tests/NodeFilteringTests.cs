using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// Tests for NodeService filtering, sorting, and search functionality.
/// </summary>
public sealed class NodeFilteringTests
{
    private static readonly Guid TestOrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();

    private static NodesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NodesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new NodesDbContext(options);
    }

    private static IOptions<NodesOptions> CreateOptions() =>
        Options.Create(new NodesOptions());

    private static (NodeService Service, TestNodesEventPublisher Publisher) CreateService(
        NodesDbContext context,
        FakeTimeProvider? timeProvider = null)
    {
        var publisher = new TestNodesEventPublisher();
        var auditService = Substitute.For<IAuditService>();
        var service = new NodeService(
            context,
            publisher,
            auditService,
            timeProvider ?? new FakeTimeProvider(DateTimeOffset.UtcNow),
            CreateOptions(),
            NullLogger<NodeService>.Instance);
        return (service, publisher);
    }

    private static async Task SeedTestDataAsync(NodesDbContext context)
    {
        var nodes = new List<Node>
        {
            // Online Linux nodes with varying health
            CreateNode("prod-linux-1", NodeStatus.Online, "Linux", tags: ["production", "critical"]),
            CreateNode("prod-linux-2", NodeStatus.Online, "Linux", tags: ["production"]),
            CreateNode("staging-linux-1", NodeStatus.Online, "Linux", tags: ["staging"]),

            // Online Windows nodes
            CreateNode("prod-windows-1", NodeStatus.Online, "Windows", tags: ["production"]),
            CreateNode("dev-windows-1", NodeStatus.Online, "Windows", tags: ["development"]),

            // Offline nodes
            CreateNode("offline-node-1", NodeStatus.Offline, "Linux", tags: ["production"]),
            CreateNode("offline-node-2", NodeStatus.Offline, "Windows"),

            // Degraded nodes
            CreateNode("degraded-node-1", NodeStatus.Degraded, "Linux", tags: ["critical"]),

            // Maintenance nodes
            CreateNode("maintenance-node-1", NodeStatus.Maintenance, "Linux"),

            // Node from different org (should not appear in results)
            CreateNode("other-org-node", NodeStatus.Online, "Linux", OtherOrgId),

            // Node with unique display name for display-name-specific search test
            CreateNode("special-node-1", NodeStatus.Online, "Linux", displayNameOverride: "UniqueDisplayOnly Server")
        };

        context.Nodes.AddRange(nodes);
        await context.SaveChangesAsync();

        // Add health data for some nodes
        var healthData = new List<NodeHealth>();
        var prodLinux1 = nodes.First(n => n.Name == "prod-linux-1");
        var prodLinux2 = nodes.First(n => n.Name == "prod-linux-2");
        var degradedNode = nodes.First(n => n.Name == "degraded-node-1");
        var offlineNode1 = nodes.First(n => n.Name == "offline-node-1");

        // High health score (90) - low usage
        healthData.Add(CreateNodeHealth(prodLinux1.Id, 10, 10, 10, activeServers: 3));

        // Medium health score (70) - moderate usage
        healthData.Add(CreateNodeHealth(prodLinux2.Id, 30, 30, 30, activeServers: 5));

        // Low health score (30) - high usage
        healthData.Add(CreateNodeHealth(degradedNode.Id, 70, 70, 70, activeServers: 1));

        // No active servers
        healthData.Add(CreateNodeHealth(offlineNode1.Id, 50, 50, 50, activeServers: 0));

        context.NodeHealths.AddRange(healthData);
        await context.SaveChangesAsync();
    }

    private static Node CreateNode(
        string name,
        NodeStatus status,
        string platform,
        Guid? orgId = null,
        List<string>? tags = null,
        string? displayNameOverride = null)
    {
        // CA5394 suppressed: Random is acceptable for test data generation - no security implications
#pragma warning disable CA5394 // Do not use insecure randomness
        return new Node
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId ?? TestOrgId,
            Name = name,
            DisplayName = displayNameOverride ?? $"Display: {name}",
            Status = status,
            Platform = platform,
            AgentVersion = "1.0.0",
            Tags = tags ?? [],
            LastHeartbeat = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 30))
        };
#pragma warning restore CA5394
    }

    private static NodeHealth CreateNodeHealth(
        Guid nodeId,
        double cpu,
        double memory,
        double disk,
        int activeServers = 0)
    {
        return new NodeHealth
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            CpuUsagePercent = cpu,
            MemoryUsagePercent = memory,
            DiskUsagePercent = disk,
            ActiveGameServers = activeServers,
            ReportedAt = DateTime.UtcNow
        };
    }

    #region Status Filter Tests

    [Fact]
    public async Task FilterByStatus_Online_ReturnsOnlyOnlineNodes()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Status = NodeStatus.Online });

        // Assert
        Assert.All(result.Items, n => Assert.Equal(NodeStatus.Online, n.Status));
        Assert.Equal(6, result.Total); // 6 online nodes in TestOrg (including special-node-1)
    }

    [Fact]
    public async Task FilterByStatus_Offline_ReturnsOnlyOfflineNodes()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Status = NodeStatus.Offline });

        // Assert
        Assert.All(result.Items, n => Assert.Equal(NodeStatus.Offline, n.Status));
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task FilterByStatus_Maintenance_ReturnsOnlyMaintenanceNodes()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Status = NodeStatus.Maintenance });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal(NodeStatus.Maintenance, result.Items.First().Status);
    }

    #endregion

    #region Platform Filter Tests

    [Fact]
    public async Task FilterByPlatform_Linux_ReturnsOnlyLinuxNodes()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Platform = "Linux" });

        // Assert
        Assert.All(result.Items, n => Assert.Equal("Linux", n.Platform, ignoreCase: true));
        Assert.Equal(7, result.Total); // 7 Linux nodes (including special-node-1)
    }

    [Fact]
    public async Task FilterByPlatform_Windows_ReturnsOnlyWindowsNodes()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Platform = "Windows" });

        // Assert
        Assert.All(result.Items, n => Assert.Equal("Windows", n.Platform, ignoreCase: true));
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public async Task FilterByPlatform_IsCaseInsensitive()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var lowerResult = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Platform = "linux" });
        var upperResult = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Platform = "LINUX" });
        var mixedResult = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Platform = "LiNuX" });

        // Assert
        Assert.Equal(lowerResult.Total, upperResult.Total);
        Assert.Equal(lowerResult.Total, mixedResult.Total);
    }

    #endregion

    #region Active Servers Filter Tests

    [Fact]
    public async Task FilterByHasActiveServers_True_ReturnsNodesWithServers()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery { HasActiveServers = true });

        // Assert
        Assert.All(result.Items, n => Assert.True(n.ActiveServers > 0));
    }

    [Fact]
    public async Task FilterByHasActiveServers_False_ReturnsNodesWithoutServers()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery { HasActiveServers = false });

        // Assert
        Assert.All(result.Items, n => Assert.Equal(0, n.ActiveServers));
    }

    #endregion

    #region Search Tests

    [Fact]
    public async Task Search_ByName_FindsMatchingNodes()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Search = "prod" });

        // Assert
        Assert.All(result.Items, n => Assert.Contains("prod", n.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_ByDisplayName_FindsMatchingNodes()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act - search for "UniqueDisplayOnly" which is only in the display name, not the node name
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Search = "UniqueDisplayOnly" });

        // Assert - should find exactly the node with the unique display name
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, n =>
        {
            Assert.Contains("UniqueDisplayOnly", n.DisplayName!, StringComparison.OrdinalIgnoreCase);
            // Verify the search term is NOT in the name (proving we matched on DisplayName)
            Assert.DoesNotContain("UniqueDisplayOnly", n.Name, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Search_IsCaseInsensitive()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var lowerResult = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Search = "prod" });
        var upperResult = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Search = "PROD" });

        // Assert
        Assert.Equal(lowerResult.Total, upperResult.Total);
    }

    [Fact]
    public async Task Search_NoMatches_ReturnsEmpty()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Search = "nonexistent" });

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    #endregion

    #region Tags Filter Tests

    [Fact]
    public async Task FilterByTags_SingleTag_ReturnsMatchingNodes()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Tags = "production" });

        // Assert
        Assert.All(result.Items, n => Assert.Contains("production", n.Tags));
    }

    [Fact]
    public async Task FilterByTags_MultipleTags_ReturnsNodesMatchingAny()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Tags = "staging,development" });

        // Assert
        Assert.All(result.Items, n =>
            Assert.True(n.Tags.Contains("staging") || n.Tags.Contains("development")));
    }

    [Fact]
    public async Task FilterByTags_NoMatches_ReturnsEmpty()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Tags = "nonexistent-tag" });

        // Assert
        Assert.Empty(result.Items);
    }

    #endregion

    #region Sorting Tests

    [Fact]
    public async Task SortByName_Ascending_SortsCorrectly()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery
        {
            SortBy = "name",
            SortOrder = "asc"
        });

        // Assert
        var names = result.Items.Select(n => n.Name).ToList();
        var sortedNames = names.OrderBy(n => n).ToList();
        Assert.Equal(sortedNames, names);
    }

    [Fact]
    public async Task SortByName_Descending_SortsCorrectly()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery
        {
            SortBy = "name",
            SortOrder = "desc"
        });

        // Assert
        var names = result.Items.Select(n => n.Name).ToList();
        var sortedNames = names.OrderByDescending(n => n).ToList();
        Assert.Equal(sortedNames, names);
    }

    [Fact]
    public async Task SortByStatus_Ascending_SortsCorrectly()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery
        {
            SortBy = "status",
            SortOrder = "asc"
        });

        // Assert
        var statuses = result.Items.Select(n => (int)n.Status).ToList();
        var sortedStatuses = statuses.OrderBy(s => s).ToList();
        Assert.Equal(sortedStatuses, statuses);
    }

    [Fact]
    public async Task SortByCreatedAt_Descending_SortsCorrectly()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery
        {
            SortBy = "createdAt",
            SortOrder = "desc"
        });

        // Assert
        var dates = result.Items.Select(n => n.CreatedAt).ToList();
        var sortedDates = dates.OrderByDescending(d => d).ToList();
        Assert.Equal(sortedDates, dates);
    }

    #endregion

    #region Combined Filters Tests

    [Fact]
    public async Task CombinedFilters_StatusAndPlatform_AppliesBoth()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery
        {
            Status = NodeStatus.Online,
            Platform = "Linux"
        });

        // Assert
        Assert.All(result.Items, n =>
        {
            Assert.Equal(NodeStatus.Online, n.Status);
            Assert.Equal("Linux", n.Platform, ignoreCase: true);
        });
    }

    [Fact]
    public async Task CombinedFilters_SearchAndTags_AppliesBoth()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery
        {
            Search = "prod",
            Tags = "production"
        });

        // Assert
        Assert.All(result.Items, n =>
        {
            Assert.Contains("prod", n.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("production", n.Tags);
        });
    }

    #endregion

    #region Pagination with Filters Tests

    [Fact]
    public async Task Pagination_WithFilters_ReturnsCorrectPage()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act - Get all online Linux nodes
        var allResults = await service.GetNodesAsync(TestOrgId, new NodeListQuery
        {
            Status = NodeStatus.Online,
            Platform = "Linux",
            PageSize = 100
        });

        // Get first page with smaller size
        var page1 = await service.GetNodesAsync(TestOrgId, new NodeListQuery
        {
            Status = NodeStatus.Online,
            Platform = "Linux",
            Page = 1,
            PageSize = 2
        });

        // Assert
        Assert.Equal(allResults.Total, page1.Total);
        Assert.Equal(2, page1.Items.Count);
    }

    #endregion

    #region Response Metadata Tests

    [Fact]
    public async Task Response_IncludesFilterMetadata()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery
        {
            Status = NodeStatus.Online,
            Platform = "Linux",
            Search = "prod",
            Tags = "production",
            SortBy = "name",
            SortOrder = "desc"
        });

        // Assert
        Assert.Equal(NodeStatus.Online, result.Filters.Status);
        Assert.Equal("Linux", result.Filters.Platform);
        Assert.Equal("prod", result.Filters.Search);
        Assert.NotNull(result.Filters.Tags);
        Assert.Contains("production", result.Filters.Tags);
        Assert.Equal("name", result.Filters.SortBy);
        Assert.Equal("desc", result.Filters.SortOrder);
    }

    [Fact]
    public async Task Response_CalculatesTotalPages()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery
        {
            PageSize = 3
        });

        // Assert
        var expectedTotalPages = (int)Math.Ceiling((double)result.Total / 3);
        Assert.Equal(expectedTotalPages, result.TotalPages);
    }

    [Fact]
    public async Task Response_HasNextAndHasPrev_CorrectForFirstPage()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery
        {
            Page = 1,
            PageSize = 3
        });

        // Assert
        Assert.False(result.HasPrev);
        Assert.True(result.HasNext || result.Total <= 3);
    }

    #endregion

    #region Tags CRUD Tests

    [Fact]
    public async Task UpdateNodeTags_ReplacesAllTags()
    {
        // Arrange
        using var context = CreateContext();
        var node = CreateNode("test-node", NodeStatus.Online, "Linux", tags: ["old-tag-1", "old-tag-2"]);
        context.Nodes.Add(node);
        await context.SaveChangesAsync();
        var (service, _) = CreateService(context);

        // Act
        var result = await service.UpdateNodeTagsAsync(
            TestOrgId,
            node.Id,
            new UpdateNodeTagsRequest(["new-tag-1", "new-tag-2", "new-tag-3"]));

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Value!.Tags.Count);
        Assert.Contains("new-tag-1", result.Value.Tags);
        Assert.Contains("new-tag-2", result.Value.Tags);
        Assert.Contains("new-tag-3", result.Value.Tags);
        Assert.DoesNotContain("old-tag-1", result.Value.Tags);
    }

    [Fact]
    public async Task UpdateNodeTags_NormalizesTags()
    {
        // Arrange
        using var context = CreateContext();
        var node = CreateNode("test-node", NodeStatus.Online, "Linux");
        context.Nodes.Add(node);
        await context.SaveChangesAsync();
        var (service, _) = CreateService(context);

        // Act
        var result = await service.UpdateNodeTagsAsync(
            TestOrgId,
            node.Id,
            new UpdateNodeTagsRequest([" PRODUCTION ", "Production", "  staging  "]));

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.Tags.Count); // Deduped
        Assert.Contains("production", result.Value.Tags);
        Assert.Contains("staging", result.Value.Tags);
    }

    [Fact]
    public async Task UpdateNodeTags_ClearsTags_WithEmptyList()
    {
        // Arrange
        using var context = CreateContext();
        var node = CreateNode("test-node", NodeStatus.Online, "Linux", tags: ["tag1", "tag2"]);
        context.Nodes.Add(node);
        await context.SaveChangesAsync();
        var (service, _) = CreateService(context);

        // Act
        var result = await service.UpdateNodeTagsAsync(TestOrgId, node.Id, new UpdateNodeTagsRequest([]));

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Value!.Tags);
    }

    [Fact]
    public async Task UpdateNodeTags_NonExistentNode_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);

        // Act
        var result = await service.UpdateNodeTagsAsync(
            TestOrgId,
            Guid.NewGuid(),
            new UpdateNodeTagsRequest(["tag1"]));

        // Assert
        Assert.False(result.Success);
        Assert.Equal("node_not_found", result.Error);
    }

    #endregion

    #region Organization Isolation Tests

    [Fact]
    public async Task GetNodes_OnlyReturnsNodesFromSpecifiedOrganization()
    {
        // Arrange
        using var context = CreateContext();
        await SeedTestDataAsync(context);
        var (service, _) = CreateService(context);

        // Act
        var testOrgResult = await service.GetNodesAsync(TestOrgId, new NodeListQuery());
        var otherOrgResult = await service.GetNodesAsync(OtherOrgId, new NodeListQuery());

        // Assert
        Assert.All(testOrgResult.Items, n => Assert.NotEqual("other-org-node", n.Name));
        Assert.Single(otherOrgResult.Items);
        Assert.Equal("other-org-node", otherOrgResult.Items.First().Name);
    }

    #endregion
}

using Dhadgar.Shared.Data;
using Xunit;

namespace Dhadgar.Shared.Tests.Data;

/// <summary>
/// Tests for <see cref="QueryableExtensions"/>.
/// </summary>
public sealed class QueryableExtensionsTests
{
    #region Test Entity

    /// <summary>
    /// A test entity implementing <see cref="IAuditableEntity"/> for use in tests.
    /// </summary>
    private sealed class TestEntity : IAuditableEntity
    {
        public required int Id { get; init; }
        public required string Name { get; init; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    #endregion

    #region Active() Tests

    [Fact]
    public void Active_FiltersOutDeletedEntities()
    {
        // Arrange
        var entities = new List<TestEntity>
        {
            new() { Id = 1, Name = "Active1", CreatedAt = DateTime.UtcNow, DeletedAt = null },
            new() { Id = 2, Name = "Deleted1", CreatedAt = DateTime.UtcNow, DeletedAt = DateTime.UtcNow },
            new() { Id = 3, Name = "Active2", CreatedAt = DateTime.UtcNow, DeletedAt = null },
            new() { Id = 4, Name = "Deleted2", CreatedAt = DateTime.UtcNow, DeletedAt = DateTime.UtcNow.AddDays(-1) }
        }.AsQueryable();

        // Act
        var result = entities.Active().ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Null(e.DeletedAt));
        Assert.Contains(result, e => e.Id == 1);
        Assert.Contains(result, e => e.Id == 3);
    }

    [Fact]
    public void Active_ReturnsAllEntities_WhenNoneDeleted()
    {
        // Arrange
        var entities = new List<TestEntity>
        {
            new() { Id = 1, Name = "Active1", CreatedAt = DateTime.UtcNow, DeletedAt = null },
            new() { Id = 2, Name = "Active2", CreatedAt = DateTime.UtcNow, DeletedAt = null },
            new() { Id = 3, Name = "Active3", CreatedAt = DateTime.UtcNow, DeletedAt = null }
        }.AsQueryable();

        // Act
        var result = entities.Active().ToList();

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Active_ReturnsEmpty_WhenAllDeleted()
    {
        // Arrange
        var deletedAt = DateTime.UtcNow;
        var entities = new List<TestEntity>
        {
            new() { Id = 1, Name = "Deleted1", CreatedAt = DateTime.UtcNow, DeletedAt = deletedAt },
            new() { Id = 2, Name = "Deleted2", CreatedAt = DateTime.UtcNow, DeletedAt = deletedAt }
        }.AsQueryable();

        // Act
        var result = entities.Active().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Active_ReturnsEmpty_WhenSourceIsEmpty()
    {
        // Arrange
        var entities = new List<TestEntity>().AsQueryable();

        // Act
        var result = entities.Active().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Active_ThrowsArgumentNullException_WhenQueryIsNull()
    {
        // Arrange
        IQueryable<TestEntity>? query = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => query!.Active());
    }

    #endregion

    #region Paginate() Tests

    [Fact]
    public void Paginate_ReturnsFirstPage_WithDefaultParameters()
    {
        // Arrange
        var entities = Enumerable.Range(1, 25)
            .Select(i => new TestEntity { Id = i, Name = $"Entity{i}", CreatedAt = DateTime.UtcNow })
            .AsQueryable();

        // Act
        var result = entities.Paginate().ToList();

        // Assert
        Assert.Equal(10, result.Count); // Default page size is 10
        Assert.Equal(1, result.First().Id);
        Assert.Equal(10, result.Last().Id);
    }

    [Fact]
    public void Paginate_ReturnsCorrectPage_ForPage2()
    {
        // Arrange
        var entities = Enumerable.Range(1, 50)
            .Select(i => new TestEntity { Id = i, Name = $"Entity{i}", CreatedAt = DateTime.UtcNow })
            .AsQueryable();

        // Act
        var result = entities.Paginate(page: 2, pageSize: 10).ToList();

        // Assert
        Assert.Equal(10, result.Count);
        Assert.Equal(11, result.First().Id);
        Assert.Equal(20, result.Last().Id);
    }

    [Fact]
    public void Paginate_ReturnsCorrectPage_ForPage3With15Items()
    {
        // Arrange
        var entities = Enumerable.Range(1, 100)
            .Select(i => new TestEntity { Id = i, Name = $"Entity{i}", CreatedAt = DateTime.UtcNow })
            .AsQueryable();

        // Act
        var result = entities.Paginate(page: 3, pageSize: 15).ToList();

        // Assert
        Assert.Equal(15, result.Count);
        Assert.Equal(31, result.First().Id);
        Assert.Equal(45, result.Last().Id);
    }

    [Fact]
    public void Paginate_ReturnsPartialPage_WhenNotEnoughItems()
    {
        // Arrange
        var entities = Enumerable.Range(1, 15)
            .Select(i => new TestEntity { Id = i, Name = $"Entity{i}", CreatedAt = DateTime.UtcNow })
            .AsQueryable();

        // Act
        var result = entities.Paginate(page: 2, pageSize: 10).ToList();

        // Assert
        Assert.Equal(5, result.Count);
        Assert.Equal(11, result.First().Id);
        Assert.Equal(15, result.Last().Id);
    }

    [Fact]
    public void Paginate_ReturnsEmpty_WhenPageExceedsData()
    {
        // Arrange
        var entities = Enumerable.Range(1, 10)
            .Select(i => new TestEntity { Id = i, Name = $"Entity{i}", CreatedAt = DateTime.UtcNow })
            .AsQueryable();

        // Act
        var result = entities.Paginate(page: 5, pageSize: 10).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void Paginate_ClampsPageToMinimum1_WhenPageIsLessThan1(int invalidPage)
    {
        // Arrange
        var entities = Enumerable.Range(1, 20)
            .Select(i => new TestEntity { Id = i, Name = $"Entity{i}", CreatedAt = DateTime.UtcNow })
            .AsQueryable();

        // Act
        var result = entities.Paginate(page: invalidPage, pageSize: 5).ToList();

        // Assert
        Assert.Equal(5, result.Count);
        Assert.Equal(1, result.First().Id); // Should be page 1
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    [InlineData(int.MinValue)]
    public void Paginate_ClampsPageSizeToMinimum1_WhenPageSizeIsLessThan1(int invalidPageSize)
    {
        // Arrange
        var entities = Enumerable.Range(1, 20)
            .Select(i => new TestEntity { Id = i, Name = $"Entity{i}", CreatedAt = DateTime.UtcNow })
            .AsQueryable();

        // Act
        var result = entities.Paginate(page: 1, pageSize: invalidPageSize).ToList();

        // Assert
        Assert.Single(result); // Should clamp to pageSize 1
        Assert.Equal(1, result.First().Id);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(200)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void Paginate_ClampsPageSizeToMaximum100_WhenPageSizeExceedsMax(int largePageSize)
    {
        // Arrange
        var entities = Enumerable.Range(1, 150)
            .Select(i => new TestEntity { Id = i, Name = $"Entity{i}", CreatedAt = DateTime.UtcNow })
            .AsQueryable();

        // Act
        var result = entities.Paginate(page: 1, pageSize: largePageSize).ToList();

        // Assert
        Assert.Equal(100, result.Count); // Should clamp to MaxPageSize (100)
    }

    [Fact]
    public void Paginate_AllowsExactlyMaxPageSize()
    {
        // Arrange
        var entities = Enumerable.Range(1, 150)
            .Select(i => new TestEntity { Id = i, Name = $"Entity{i}", CreatedAt = DateTime.UtcNow })
            .AsQueryable();

        // Act
        var result = entities.Paginate(page: 1, pageSize: QueryableExtensions.MaxPageSize).ToList();

        // Assert
        Assert.Equal(QueryableExtensions.MaxPageSize, result.Count);
    }

    [Fact]
    public void Paginate_ThrowsArgumentNullException_WhenQueryIsNull()
    {
        // Arrange
        IQueryable<TestEntity>? query = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => query!.Paginate());
    }

    [Fact]
    public void Paginate_ReturnsEmpty_WhenSourceIsEmpty()
    {
        // Arrange
        var entities = new List<TestEntity>().AsQueryable();

        // Act
        var result = entities.Paginate(page: 1, pageSize: 10).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region OrderByNewest() Tests

    [Fact]
    public void OrderByNewest_OrdersByCreatedAtDescending()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entities = new List<TestEntity>
        {
            new() { Id = 1, Name = "Oldest", CreatedAt = now.AddDays(-10) },
            new() { Id = 2, Name = "Middle", CreatedAt = now.AddDays(-5) },
            new() { Id = 3, Name = "Newest", CreatedAt = now }
        }.AsQueryable();

        // Act
        var result = entities.OrderByNewest().ToList();

        // Assert
        Assert.Equal(3, result[0].Id); // Newest first
        Assert.Equal(2, result[1].Id);
        Assert.Equal(1, result[2].Id); // Oldest last
    }

    [Fact]
    public void OrderByNewest_HandlesEntitiesWithSameCreatedAt()
    {
        // Arrange
        var sameTime = DateTime.UtcNow;
        var entities = new List<TestEntity>
        {
            new() { Id = 1, Name = "Entity1", CreatedAt = sameTime },
            new() { Id = 2, Name = "Entity2", CreatedAt = sameTime },
            new() { Id = 3, Name = "Entity3", CreatedAt = sameTime }
        }.AsQueryable();

        // Act
        var result = entities.OrderByNewest().ToList();

        // Assert
        Assert.Equal(3, result.Count);
        // All entities should be present (order among same timestamps is not guaranteed)
        Assert.Contains(result, e => e.Id == 1);
        Assert.Contains(result, e => e.Id == 2);
        Assert.Contains(result, e => e.Id == 3);
    }

    [Fact]
    public void OrderByNewest_ReturnsEmpty_WhenSourceIsEmpty()
    {
        // Arrange
        var entities = new List<TestEntity>().AsQueryable();

        // Act
        var result = entities.OrderByNewest().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void OrderByNewest_ReturnsSingleEntity_WhenOnlyOneExists()
    {
        // Arrange
        var entities = new List<TestEntity>
        {
            new() { Id = 1, Name = "Only", CreatedAt = DateTime.UtcNow }
        }.AsQueryable();

        // Act
        var result = entities.OrderByNewest().ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
    }

    [Fact]
    public void OrderByNewest_ThrowsArgumentNullException_WhenQueryIsNull()
    {
        // Arrange
        IQueryable<TestEntity>? query = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => query!.OrderByNewest());
    }

    #endregion

    #region OrderByOldest() Tests

    [Fact]
    public void OrderByOldest_OrdersByCreatedAtAscending()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entities = new List<TestEntity>
        {
            new() { Id = 3, Name = "Newest", CreatedAt = now },
            new() { Id = 2, Name = "Middle", CreatedAt = now.AddDays(-5) },
            new() { Id = 1, Name = "Oldest", CreatedAt = now.AddDays(-10) }
        }.AsQueryable();

        // Act
        var result = entities.OrderByOldest().ToList();

        // Assert
        Assert.Equal(1, result[0].Id); // Oldest first
        Assert.Equal(2, result[1].Id);
        Assert.Equal(3, result[2].Id); // Newest last
    }

    [Fact]
    public void OrderByOldest_ThrowsArgumentNullException_WhenQueryIsNull()
    {
        // Arrange
        IQueryable<TestEntity>? query = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => query!.OrderByOldest());
    }

    #endregion

    #region Combined Usage Tests

    [Fact]
    public void Active_AndOrderByNewest_CombineCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entities = new List<TestEntity>
        {
            new() { Id = 1, Name = "ActiveOld", CreatedAt = now.AddDays(-10), DeletedAt = null },
            new() { Id = 2, Name = "DeletedNew", CreatedAt = now, DeletedAt = now },
            new() { Id = 3, Name = "ActiveNew", CreatedAt = now.AddDays(-5), DeletedAt = null }
        }.AsQueryable();

        // Act
        var result = entities.Active().OrderByNewest().ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[0].Id); // ActiveNew (more recent)
        Assert.Equal(1, result[1].Id); // ActiveOld (older)
    }

    [Fact]
    public void Active_OrderByNewest_AndPaginate_CombineCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entities = Enumerable.Range(1, 30)
            .Select(i => new TestEntity
            {
                Id = i,
                Name = $"Entity{i}",
                CreatedAt = now.AddDays(-i), // Entity 1 is newest, Entity 30 is oldest
                DeletedAt = i % 3 == 0 ? now : null // Every 3rd entity is deleted
            })
            .AsQueryable();

        // Act
        var result = entities.Active().OrderByNewest().Paginate(page: 1, pageSize: 5).ToList();

        // Assert
        Assert.Equal(5, result.Count);
        Assert.All(result, e => Assert.Null(e.DeletedAt));
        // Should be the 5 newest active entities (ids 1, 2, 4, 5, 7 - skipping 3, 6)
        Assert.Equal(1, result[0].Id);
        Assert.Equal(2, result[1].Id);
        Assert.Equal(4, result[2].Id);
        Assert.Equal(5, result[3].Id);
        Assert.Equal(7, result[4].Id);
    }

    [Fact]
    public void Paginate_AfterOrderByNewest_MaintainsOrder()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entities = Enumerable.Range(1, 100)
            .Select(i => new TestEntity
            {
                Id = i,
                Name = $"Entity{i}",
                CreatedAt = now.AddMinutes(-i) // Entity 1 is newest
            })
            .AsQueryable();

        // Act
        var page2 = entities.OrderByNewest().Paginate(page: 2, pageSize: 10).ToList();

        // Assert
        Assert.Equal(10, page2.Count);
        Assert.Equal(11, page2[0].Id); // First item on page 2
        Assert.Equal(20, page2[9].Id); // Last item on page 2
        // Verify descending order (by created at, which correlates with id here)
        for (int i = 0; i < page2.Count - 1; i++)
        {
            Assert.True(page2[i].CreatedAt > page2[i + 1].CreatedAt);
        }
    }

    #endregion

    #region Constants Tests

    [Fact]
    public void MaxPageSize_IsCorrectValue()
    {
        Assert.Equal(100, QueryableExtensions.MaxPageSize);
    }

    [Fact]
    public void DefaultPageSize_IsCorrectValue()
    {
        Assert.Equal(10, QueryableExtensions.DefaultPageSize);
    }

    #endregion
}

using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Nodes.Tests;

public sealed class EnrollmentTokenServiceTests
{
    private static readonly Guid TestOrgId = Guid.NewGuid();
    private const string TestUserId = "user-123";

    private static NodesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NodesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new NodesDbContext(options);
    }

    private static IOptions<NodesOptions> CreateOptions() =>
        Options.Create(new NodesOptions());

    private static EnrollmentTokenService CreateService(
        NodesDbContext context,
        FakeTimeProvider? timeProvider = null)
    {
        return new EnrollmentTokenService(
            context,
            new TestAuditService(),
            timeProvider ?? new FakeTimeProvider(DateTimeOffset.UtcNow),
            CreateOptions(),
            NullLogger<EnrollmentTokenService>.Instance);
    }

    [Fact]
    public async Task CreateTokenAsync_GeneratesUniqueToken()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);

        // Act
        var (token1, plainText1) = await service.CreateTokenAsync(TestOrgId, TestUserId, "Token 1");
        var (token2, plainText2) = await service.CreateTokenAsync(TestOrgId, TestUserId, "Token 2");

        // Assert
        Assert.NotEqual(plainText1, plainText2);
        Assert.NotEqual(token1.Id, token2.Id);
        Assert.NotEqual(token1.TokenHash, token2.TokenHash);
    }

    [Fact]
    public async Task CreateTokenAsync_StoresHashNotPlaintext()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);

        // Act
        var (token, plainTextToken) = await service.CreateTokenAsync(TestOrgId, TestUserId, "Test Label");

        // Assert - hash should NOT contain the plaintext token
        Assert.DoesNotContain(plainTextToken, token.TokenHash);
        Assert.NotEmpty(token.TokenHash);
        Assert.Equal(64, token.TokenHash.Length); // SHA-256 produces 64 hex chars
    }

    [Fact]
    public async Task CreateTokenAsync_SetsCorrectExpiration()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var service = CreateService(context, timeProvider);

        // Act - default 1 hour validity
        var (token, _) = await service.CreateTokenAsync(TestOrgId, TestUserId, "Test");

        // Assert
        Assert.Equal(now.UtcDateTime.AddHours(1), token.ExpiresAt);
    }

    [Fact]
    public async Task CreateTokenAsync_RespectsCustomValidity()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var service = CreateService(context, timeProvider);
        var customValidity = TimeSpan.FromDays(7);

        // Act
        var (token, _) = await service.CreateTokenAsync(
            TestOrgId, TestUserId, "Week Token", customValidity);

        // Assert
        Assert.Equal(now.UtcDateTime.Add(customValidity), token.ExpiresAt);
    }

    [Fact]
    public async Task CreateTokenAsync_TrimsLabel()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);

        // Act
        var (token, _) = await service.CreateTokenAsync(TestOrgId, TestUserId, "  Padded Label  ");

        // Assert
        Assert.Equal("Padded Label", token.Label);
    }

    [Fact]
    public async Task CreateTokenAsync_NullLabelForWhitespace()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);

        // Act
        var (token, _) = await service.CreateTokenAsync(TestOrgId, TestUserId, "   ");

        // Assert
        Assert.Null(token.Label);
    }

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_ReturnsToken()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);
        var (_, plainTextToken) = await service.CreateTokenAsync(TestOrgId, TestUserId, "Valid Token");

        // Act
        var validatedToken = await service.ValidateTokenAsync(plainTextToken);

        // Assert
        Assert.NotNull(validatedToken);
        Assert.Equal(TestOrgId, validatedToken.OrganizationId);
    }

    [Fact]
    public async Task ValidateTokenAsync_ExpiredToken_ReturnsNull()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var service = CreateService(context, timeProvider);

        // Create token
        var (_, plainTextToken) = await service.CreateTokenAsync(TestOrgId, TestUserId, "Soon Expired");

        // Advance time past expiration (default 1 hour)
        timeProvider.Advance(TimeSpan.FromHours(2));

        // Act
        var validatedToken = await service.ValidateTokenAsync(plainTextToken);

        // Assert
        Assert.Null(validatedToken);
    }

    [Fact]
    public async Task ValidateTokenAsync_RevokedToken_ReturnsNull()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);
        var (token, plainTextToken) = await service.CreateTokenAsync(TestOrgId, TestUserId, "To Revoke");

        // Revoke
        await service.RevokeTokenAsync(token.Id);

        // Act
        var validatedToken = await service.ValidateTokenAsync(plainTextToken);

        // Assert
        Assert.Null(validatedToken);
    }

    [Fact]
    public async Task ValidateTokenAsync_UsedToken_ReturnsNull()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);
        var (token, plainTextToken) = await service.CreateTokenAsync(TestOrgId, TestUserId, "Single Use");
        var nodeId = Guid.NewGuid();

        // Mark as used
        await service.MarkTokenUsedAsync(token.Id, nodeId);

        // Act
        var validatedToken = await service.ValidateTokenAsync(plainTextToken);

        // Assert
        Assert.Null(validatedToken);
    }

    [Fact]
    public async Task ValidateTokenAsync_InvalidToken_ReturnsNull()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);

        // Act
        var validatedToken = await service.ValidateTokenAsync("completely-invalid-token");

        // Assert
        Assert.Null(validatedToken);
    }

    [Fact]
    public async Task ValidateTokenAsync_EmptyToken_ReturnsNull()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);

        // Act
        var validatedToken = await service.ValidateTokenAsync("");

        // Assert
        Assert.Null(validatedToken);
    }

    [Fact]
    public async Task ValidateTokenAsync_NullToken_ReturnsNull()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);

        // Act
        var validatedToken = await service.ValidateTokenAsync(null!);

        // Assert
        Assert.Null(validatedToken);
    }

    [Fact]
    public async Task MarkTokenUsedAsync_UpdatesTokenCorrectly()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var service = CreateService(context, timeProvider);
        var (token, _) = await service.CreateTokenAsync(TestOrgId, TestUserId, "To Use");
        var nodeId = Guid.NewGuid();

        // Act
        await service.MarkTokenUsedAsync(token.Id, nodeId);

        // Assert
        var updatedToken = await context.EnrollmentTokens.FindAsync(token.Id);
        Assert.NotNull(updatedToken);
        Assert.Equal(nodeId, updatedToken.UsedByNodeId);
        Assert.Equal(now.UtcDateTime, updatedToken.UsedAt);
    }

    [Fact]
    public async Task RevokeTokenAsync_SetsRevokedFlag()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);
        var (token, _) = await service.CreateTokenAsync(TestOrgId, TestUserId, "To Revoke");

        // Act
        await service.RevokeTokenAsync(token.Id);

        // Assert
        var revokedToken = await context.EnrollmentTokens.FindAsync(token.Id);
        Assert.NotNull(revokedToken);
        Assert.True(revokedToken.IsRevoked);
    }

    [Fact]
    public async Task GetActiveTokensAsync_ReturnsOnlyActiveTokens()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var service = CreateService(context, timeProvider);

        // Create various tokens
        var (activeToken, _) = await service.CreateTokenAsync(TestOrgId, TestUserId, "Active");
        var (revokedToken, _) = await service.CreateTokenAsync(TestOrgId, TestUserId, "Revoked");
        var (usedToken, _) = await service.CreateTokenAsync(TestOrgId, TestUserId, "Used");
        var (_, _) = await service.CreateTokenAsync(
            TestOrgId, TestUserId, "Expired", TimeSpan.FromMinutes(30));

        // Different org
        var otherOrgId = Guid.NewGuid();
        await service.CreateTokenAsync(otherOrgId, TestUserId, "Other Org");

        // Modify states
        await service.RevokeTokenAsync(revokedToken.Id);
        await service.MarkTokenUsedAsync(usedToken.Id, Guid.NewGuid());

        // Advance time to expire one token
        timeProvider.Advance(TimeSpan.FromMinutes(45));

        // Act
        var activeTokens = await service.GetActiveTokensAsync(TestOrgId);

        // Assert
        Assert.Single(activeTokens);
        Assert.Equal(activeToken.Id, activeTokens[0].Id);
        Assert.Equal("Active", activeTokens[0].Label);
    }

    [Fact]
    public async Task GetActiveTokensAsync_OrdersByCreatedAtDescending()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var service = CreateService(context, timeProvider);

        var (first, _) = await service.CreateTokenAsync(TestOrgId, TestUserId, "First");
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var (second, _) = await service.CreateTokenAsync(TestOrgId, TestUserId, "Second");
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var (third, _) = await service.CreateTokenAsync(TestOrgId, TestUserId, "Third");

        // Act
        var tokens = await service.GetActiveTokensAsync(TestOrgId);

        // Assert
        Assert.Equal(3, tokens.Count);
        Assert.Equal(third.Id, tokens[0].Id);
        Assert.Equal(second.Id, tokens[1].Id);
        Assert.Equal(first.Id, tokens[2].Id);
    }

    [Fact]
    public async Task CreateTokenAsync_PersistsToDatabase()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);

        // Act
        var (token, _) = await service.CreateTokenAsync(TestOrgId, TestUserId, "Persisted");

        // Assert - verify in database
        var dbToken = await context.EnrollmentTokens.FindAsync(token.Id);
        Assert.NotNull(dbToken);
        Assert.Equal(TestOrgId, dbToken.OrganizationId);
        Assert.Equal(TestUserId, dbToken.CreatedByUserId);
        Assert.Equal("Persisted", dbToken.Label);
        Assert.False(dbToken.IsRevoked);
        Assert.Null(dbToken.UsedAt);
    }
}

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Options;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Identity.Tests;

using IdentityContext = Dhadgar.Identity.Data.IdentityDbContext;

public sealed class TokenRefreshServiceTests
{
    private const string PresentedToken = "presented-refresh-token";

    [Fact]
    public async Task Refresh_with_valid_token_rotates_and_issues_new_pair()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = CreateContext(dbName);
        var (user, org) = await SeedUserAsync(context, role: "admin");
        var original = await SeedTokenAsync(context, user.Id, org.Id);
        var jwt = new SequenceJwtService();
        var service = CreateService(context, jwt);

        var outcome = await service.RefreshAsync(PresentedToken);

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal("access-1", outcome.AccessToken);
        Assert.Equal("refresh-1", outcome.RefreshToken);
        Assert.Equal(900, outcome.ExpiresIn);
        Assert.Equal(user.Id, outcome.UserId);
        Assert.Equal(org.Id, outcome.OrganizationId);

        var tokens = await context.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.NotNull(tokens.Single(t => t.Id == original.Id).RevokedAt);
        var replacement = tokens.Single(t => t.Id != original.Id);
        Assert.Null(replacement.RevokedAt);
        Assert.Equal(Hash("refresh-1"), replacement.TokenHash);
        Assert.Equal(org.Id, replacement.OrganizationId);
    }

    [Fact]
    public async Task Refresh_issues_same_claim_shape_as_exchange_with_current_role()
    {
        using var context = CreateContext(Guid.NewGuid().ToString());
        var (user, org) = await SeedUserAsync(context, role: "viewer");
        await SeedTokenAsync(context, user.Id, org.Id);

        // Role changed since the token was issued — refresh must pick up the new one.
        var membership = await context.UserOrganizations.SingleAsync();
        membership.Role = "admin";
        await context.SaveChangesAsync();

        var jwt = new SequenceJwtService();
        var outcome = await CreateService(context, jwt).RefreshAsync(PresentedToken);

        Assert.True(outcome.Success, outcome.Error);
        var claims = jwt.LastClaims!;
        Assert.Equal(user.Id.ToString(), claims.Single(c => c.Type == "sub").Value);
        Assert.Equal(org.Id.ToString(), claims.Single(c => c.Type == "org_id").Value);
        Assert.Equal(user.Email, claims.Single(c => c.Type == "email").Value);
        Assert.Equal("admin", claims.Single(c => c.Type == "role").Value);
        Assert.Equal("true", claims.Single(c => c.Type == "email_verified").Value);
        Assert.Equal("user", claims.Single(c => c.Type == "principal_type").Value);
    }

    [Fact]
    public async Task Refresh_with_unknown_token_fails()
    {
        using var context = CreateContext(Guid.NewGuid().ToString());
        var outcome = await CreateService(context, new SequenceJwtService()).RefreshAsync("nope");

        Assert.False(outcome.Success);
        Assert.Equal("invalid_refresh_token", outcome.Error);
    }

    [Fact]
    public async Task Refresh_with_expired_token_fails()
    {
        using var context = CreateContext(Guid.NewGuid().ToString());
        var (user, org) = await SeedUserAsync(context);
        await SeedTokenAsync(context, user.Id, org.Id, expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var outcome = await CreateService(context, new SequenceJwtService()).RefreshAsync(PresentedToken);

        Assert.False(outcome.Success);
        Assert.Equal("refresh_token_expired", outcome.Error);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_fails_and_revokes_the_whole_family()
    {
        using var context = CreateContext(Guid.NewGuid().ToString());
        var (user, org) = await SeedUserAsync(context);
        await SeedTokenAsync(context, user.Id, org.Id);
        var service = CreateService(context, new SequenceJwtService());

        var first = await service.RefreshAsync(PresentedToken);
        Assert.True(first.Success, first.Error);

        // Attacker (or a stale tab) replays the original token.
        var replay = await service.RefreshAsync(PresentedToken);

        Assert.False(replay.Success);
        Assert.Equal("refresh_token_reused", replay.Error);
        Assert.All(await context.RefreshTokens.AsNoTracking().ToListAsync(), t => Assert.NotNull(t.RevokedAt));

        // The legitimately rotated token is now dead too.
        var afterReuse = await service.RefreshAsync(first.RefreshToken!);
        Assert.False(afterReuse.Success);
    }

    [Fact]
    public async Task Refresh_after_user_leaves_org_fails_and_revokes_token()
    {
        using var context = CreateContext(Guid.NewGuid().ToString());
        var (user, org) = await SeedUserAsync(context);
        var token = await SeedTokenAsync(context, user.Id, org.Id);

        var membership = await context.UserOrganizations.SingleAsync();
        membership.LeftAt = DateTime.UtcNow;
        membership.IsActive = false;
        await context.SaveChangesAsync();

        var outcome = await CreateService(context, new SequenceJwtService()).RefreshAsync(PresentedToken);

        Assert.False(outcome.Success);
        Assert.Equal("user_inactive", outcome.Error);
        Assert.NotNull((await context.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == token.Id)).RevokedAt);
    }

    [Fact]
    public async Task Refresh_for_deleted_user_fails()
    {
        using var context = CreateContext(Guid.NewGuid().ToString());
        var (user, org) = await SeedUserAsync(context);
        await SeedTokenAsync(context, user.Id, org.Id);
        user.DeletedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var outcome = await CreateService(context, new SequenceJwtService()).RefreshAsync(PresentedToken);

        Assert.False(outcome.Success);
        Assert.Equal("user_inactive", outcome.Error);
    }

    [Fact]
    public async Task Concurrent_redemption_of_same_token_only_succeeds_once()
    {
        var dbName = Guid.NewGuid().ToString();
        using (var seed = CreateContext(dbName))
        {
            var (user, org) = await SeedUserAsync(seed);
            await SeedTokenAsync(seed, user.Id, org.Id);
        }

        // Two requests load the same un-revoked row before either saves.
        using var contextA = CreateContext(dbName);
        using var contextB = CreateContext(dbName);
        _ = await contextA.RefreshTokens.SingleAsync();
        _ = await contextB.RefreshTokens.SingleAsync();

        var a = await CreateService(contextA, new SequenceJwtService("a")).RefreshAsync(PresentedToken);
        var b = await CreateService(contextB, new SequenceJwtService("b")).RefreshAsync(PresentedToken);

        Assert.True(a.Success, a.Error);
        Assert.False(b.Success);
        Assert.Equal("refresh_token_reused", b.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Refresh_with_blank_token_fails(string token)
    {
        using var context = CreateContext(Guid.NewGuid().ToString());
        var outcome = await CreateService(context, new SequenceJwtService()).RefreshAsync(token);

        Assert.False(outcome.Success);
        Assert.Equal("missing_refresh_token", outcome.Error);
    }

    private static TokenRefreshService CreateService(IdentityContext context, IJwtService jwt)
    {
        var permissionService = new PermissionService(context, TimeProvider.System);
        var refreshTokenService = new RefreshTokenService(
            context, permissionService, TimeProvider.System, NullLogger<RefreshTokenService>.Instance);

        return new TokenRefreshService(
            context,
            refreshTokenService,
            jwt,
            TimeProvider.System,
            new OptionsWrapper<AuthOptions>(new AuthOptions { RefreshTokenLifetimeDays = 7 }),
            NullLogger<TokenRefreshService>.Instance);
    }

    private static async Task<(User User, Organization Org)> SeedUserAsync(
        IdentityContext context,
        string role = "owner")
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "refresh@example.com",
            ExternalAuthId = Guid.NewGuid().ToString(),
            EmailVerified = true
        };
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Refresh Org",
            Slug = "refresh-org",
            Settings = new OrganizationSettings()
        };
        user.PreferredOrganizationId = org.Id;

        context.Users.Add(user);
        context.Organizations.Add(org);
        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = user.Id,
            OrganizationId = org.Id,
            Role = role,
            IsActive = true
        });
        await context.SaveChangesAsync();
        return (user, org);
    }

    private static async Task<RefreshToken> SeedTokenAsync(
        IdentityContext context,
        Guid userId,
        Guid orgId,
        DateTime? expiresAt = null)
    {
        var token = new RefreshToken
        {
            UserId = userId,
            OrganizationId = orgId,
            TokenHash = Hash(PresentedToken),
            IssuedAt = DateTime.UtcNow.AddMinutes(-20),
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7)
        };
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync();
        return token;
    }

    private static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static IdentityContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .UseInMemoryDatabase(dbName)
            .Options;

        return new IdentityContext(options);
    }

    /// <summary>Issues distinct tokens per call so rotation can be observed.</summary>
    private sealed class SequenceJwtService : IJwtService
    {
        private readonly string _prefix;
        private int _counter;

        public SequenceJwtService(string prefix = "")
        {
            _prefix = prefix;
        }

        public IReadOnlyList<Claim>? LastClaims { get; private set; }

        public Task<(string AccessToken, string RefreshToken, int ExpiresIn)> GenerateTokenPairAsync(
            IEnumerable<Claim> claims,
            CancellationToken ct = default)
        {
            LastClaims = claims.ToList();
            var n = Interlocked.Increment(ref _counter);
            return Task.FromResult(($"{_prefix}access-{n}", $"{_prefix}refresh-{n}", 900));
        }
    }
}

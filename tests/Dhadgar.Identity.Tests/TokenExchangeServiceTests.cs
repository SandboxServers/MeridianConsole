using System.Security.Claims;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Identity.Tests;

using IdentityContext = Dhadgar.Identity.Data.IdentityDbContext;

public class TokenExchangeServiceTests
{
    [Fact]
    public async Task Exchange_creates_user_org_and_refresh_token()
    {
        using var context = CreateContext();
        var validator = new TestExchangeTokenValidator(CreatePrincipal());
        var replayStore = new InMemoryReplayStore();
        var jwtService = new TestJwtService();
        var permissionService = new PermissionService(context, TimeProvider.System);
        var eventPublisher = new TestIdentityEventPublisher();
        var options = new OptionsWrapper<Dhadgar.Identity.Options.AuthOptions>(
            new Dhadgar.Identity.Options.AuthOptions { RefreshTokenLifetimeDays = 7 });
        var lookupNormalizer = new UpperInvariantLookupNormalizer();

        var service = new TokenExchangeService(
            context,
            validator,
            replayStore,
            jwtService,
            permissionService,
            eventPublisher,
            TimeProvider.System,
            options,
            NullLogger<TokenExchangeService>.Instance,
            lookupNormalizer);

        var outcome = await service.ExchangeAsync("token");

        Assert.True(outcome.Success);
        Assert.Equal("access-token", outcome.AccessToken);
        Assert.Equal("refresh-token", outcome.RefreshToken);
        Assert.Single(context.Users);
        Assert.Single(context.Organizations);
        Assert.Single(context.UserOrganizations);
        Assert.Single(context.RefreshTokens);
        Assert.True(eventPublisher.UserAuthenticatedEvents.TryDequeue(out var authEvent));
        Assert.Equal(outcome.UserId, authEvent.UserId);
    }

    [Fact]
    public async Task Exchange_rejects_replayed_token()
    {
        using var context = CreateContext();
        var validator = new TestExchangeTokenValidator(CreatePrincipal());
        var replayStore = new RejectingReplayStore();
        var jwtService = new TestJwtService();
        var permissionService = new PermissionService(context, TimeProvider.System);
        var eventPublisher = new TestIdentityEventPublisher();
        var options = new OptionsWrapper<Dhadgar.Identity.Options.AuthOptions>(
            new Dhadgar.Identity.Options.AuthOptions { RefreshTokenLifetimeDays = 7 });
        var lookupNormalizer = new UpperInvariantLookupNormalizer();

        var service = new TokenExchangeService(
            context,
            validator,
            replayStore,
            jwtService,
            permissionService,
            eventPublisher,
            TimeProvider.System,
            options,
            NullLogger<TokenExchangeService>.Instance,
            lookupNormalizer);

        var outcome = await service.ExchangeAsync("token");

        Assert.False(outcome.Success);
        Assert.Equal("token_already_used", outcome.Error);
        Assert.Empty(eventPublisher.UserAuthenticatedEvents);
    }

    [Fact]
    public async Task Exchange_rejects_invalid_token()
    {
        using var context = CreateContext();
        var validator = new TestExchangeTokenValidator(null);
        var replayStore = new InMemoryReplayStore();
        var jwtService = new TestJwtService();
        var permissionService = new PermissionService(context, TimeProvider.System);
        var eventPublisher = new TestIdentityEventPublisher();
        var options = new OptionsWrapper<Dhadgar.Identity.Options.AuthOptions>(
            new Dhadgar.Identity.Options.AuthOptions { RefreshTokenLifetimeDays = 7 });
        var lookupNormalizer = new UpperInvariantLookupNormalizer();

        var service = new TokenExchangeService(
            context,
            validator,
            replayStore,
            jwtService,
            permissionService,
            eventPublisher,
            TimeProvider.System,
            options,
            NullLogger<TokenExchangeService>.Instance,
            lookupNormalizer);

        var outcome = await service.ExchangeAsync("token");

        Assert.False(outcome.Success);
        Assert.Equal("invalid_exchange_token", outcome.Error);
        Assert.Empty(eventPublisher.UserAuthenticatedEvents);
    }

    [Fact]
    public async Task Exchange_WithUnverifiedEmailAndEnforcementEnabled_RejectsToken()
    {
        // Arrange: Create context with email verification enforcement enabled
        using var context = CreateContext();
        var validator = new TestExchangeTokenValidator(CreatePrincipal());
        var replayStore = new InMemoryReplayStore();
        var jwtService = new ClaimCapturingJwtService();
        var permissionService = new PermissionService(context, TimeProvider.System);
        var eventPublisher = new TestIdentityEventPublisher();

        // Enable email verification requirement
        var authOptions = new Dhadgar.Identity.Options.AuthOptions
        {
            RefreshTokenLifetimeDays = 7,
            EmailVerification = new Dhadgar.Identity.Options.EmailVerificationOptions
            {
                RequireVerifiedEmail = true
            }
        };
        var options = new OptionsWrapper<Dhadgar.Identity.Options.AuthOptions>(authOptions);
        var lookupNormalizer = new UpperInvariantLookupNormalizer();

        var service = new TokenExchangeService(
            context,
            validator,
            replayStore,
            jwtService,
            permissionService,
            eventPublisher,
            TimeProvider.System,
            options,
            NullLogger<TokenExchangeService>.Instance,
            lookupNormalizer);

        // Act
        var outcome = await service.ExchangeAsync("token");

        // Assert: Should fail because email is not verified and enforcement is enabled
        Assert.False(outcome.Success);
        Assert.Equal("email_not_verified", outcome.Error);
    }

    [Fact]
    public async Task Exchange_WithUnverifiedEmailAndEnforcementDisabled_Succeeds()
    {
        // Arrange: Create context with email verification enforcement disabled (default)
        using var context = CreateContext();
        var validator = new TestExchangeTokenValidator(CreatePrincipal());
        var replayStore = new InMemoryReplayStore();
        var jwtService = new ClaimCapturingJwtService();
        var permissionService = new PermissionService(context, TimeProvider.System);
        var eventPublisher = new TestIdentityEventPublisher();

        // Email verification NOT required (default)
        var authOptions = new Dhadgar.Identity.Options.AuthOptions
        {
            RefreshTokenLifetimeDays = 7,
            EmailVerification = new Dhadgar.Identity.Options.EmailVerificationOptions
            {
                RequireVerifiedEmail = false
            }
        };
        var options = new OptionsWrapper<Dhadgar.Identity.Options.AuthOptions>(authOptions);
        var lookupNormalizer = new UpperInvariantLookupNormalizer();

        var service = new TokenExchangeService(
            context,
            validator,
            replayStore,
            jwtService,
            permissionService,
            eventPublisher,
            TimeProvider.System,
            options,
            NullLogger<TokenExchangeService>.Instance,
            lookupNormalizer);

        // Act
        var outcome = await service.ExchangeAsync("token");

        // Assert: Should succeed even with unverified email
        Assert.True(outcome.Success);
        Assert.NotNull(outcome.AccessToken);
    }

    [Fact]
    public async Task Exchange_TokenIncludesEmailVerifiedClaim()
    {
        // Arrange
        using var context = CreateContext();
        var validator = new TestExchangeTokenValidator(CreatePrincipal());
        var replayStore = new InMemoryReplayStore();
        var jwtService = new ClaimCapturingJwtService();
        var permissionService = new PermissionService(context, TimeProvider.System);
        var eventPublisher = new TestIdentityEventPublisher();
        var options = new OptionsWrapper<Dhadgar.Identity.Options.AuthOptions>(
            new Dhadgar.Identity.Options.AuthOptions { RefreshTokenLifetimeDays = 7 });
        var lookupNormalizer = new UpperInvariantLookupNormalizer();

        var service = new TokenExchangeService(
            context,
            validator,
            replayStore,
            jwtService,
            permissionService,
            eventPublisher,
            TimeProvider.System,
            options,
            NullLogger<TokenExchangeService>.Instance,
            lookupNormalizer);

        // Act
        var outcome = await service.ExchangeAsync("token");

        // Assert: Token should include email_verified claim
        Assert.True(outcome.Success);
        Assert.NotNull(jwtService.CapturedClaims);

        var emailVerifiedClaim = jwtService.CapturedClaims
            .FirstOrDefault(c => c.Type == "email_verified");
        Assert.NotNull(emailVerifiedClaim);
        Assert.Equal("false", emailVerifiedClaim.Value); // New users have unverified email by default
    }

    private static ClaimsPrincipal CreatePrincipal()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", "external-1"),
            new Claim("email", "user@example.com"),
            new Claim("purpose", "token_exchange"),
            new Claim("jti", Guid.NewGuid().ToString())
        }, "test");

        return new ClaimsPrincipal(identity);
    }

    private static IdentityContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new IdentityContext(options);
    }

    private sealed class TestExchangeTokenValidator : IExchangeTokenValidator
    {
        private readonly ClaimsPrincipal? _principal;

        public TestExchangeTokenValidator(ClaimsPrincipal? principal)
        {
            _principal = principal;
        }

        public Task<ClaimsPrincipal?> ValidateAsync(string exchangeToken, CancellationToken ct = default)
            => Task.FromResult(_principal);
    }

    private sealed class InMemoryReplayStore : IExchangeTokenReplayStore
    {
        private readonly HashSet<string> _seen = new();

        public Task<bool> MarkAsUsedAsync(string jti, TimeSpan ttl, CancellationToken ct = default)
        {
            return Task.FromResult(_seen.Add(jti));
        }
    }

    private sealed class RejectingReplayStore : IExchangeTokenReplayStore
    {
        public Task<bool> MarkAsUsedAsync(string jti, TimeSpan ttl, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class TestJwtService : IJwtService
    {
        public Task<(string AccessToken, string RefreshToken, int ExpiresIn)> GenerateTokenPairAsync(
            IEnumerable<Claim> claims,
            CancellationToken ct = default)
            => Task.FromResult(("access-token", "refresh-token", 900));
    }

    private sealed class ClaimCapturingJwtService : IJwtService
    {
        public IReadOnlyList<Claim>? CapturedClaims { get; private set; }

        public Task<(string AccessToken, string RefreshToken, int ExpiresIn)> GenerateTokenPairAsync(
            IEnumerable<Claim> claims,
            CancellationToken ct = default)
        {
            CapturedClaims = claims.ToList();
            return Task.FromResult(("access-token", "refresh-token", 900));
        }
    }
}

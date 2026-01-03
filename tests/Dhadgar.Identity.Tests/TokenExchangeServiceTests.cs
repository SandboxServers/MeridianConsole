using System.Security.Claims;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Identity.Tests;

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

        var service = new TokenExchangeService(
            context,
            validator,
            replayStore,
            jwtService,
            permissionService,
            eventPublisher,
            TimeProvider.System,
            options,
            NullLogger<TokenExchangeService>.Instance);

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

        var service = new TokenExchangeService(
            context,
            validator,
            replayStore,
            jwtService,
            permissionService,
            eventPublisher,
            TimeProvider.System,
            options,
            NullLogger<TokenExchangeService>.Instance);

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

        var service = new TokenExchangeService(
            context,
            validator,
            replayStore,
            jwtService,
            permissionService,
            eventPublisher,
            TimeProvider.System,
            options,
            NullLogger<TokenExchangeService>.Instance);

        var outcome = await service.ExchangeAsync("token");

        Assert.False(outcome.Success);
        Assert.Equal("invalid_exchange_token", outcome.Error);
        Assert.Empty(eventPublisher.UserAuthenticatedEvents);
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

    private static IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new IdentityDbContext(options);
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
}

using System.Net;
using Dhadgar.Identity.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Identity.Tests.OAuth;

[Collection("Identity Integration")]
public sealed class OAuthLinkingTests
{
    private readonly IdentityWebApplicationFactory _factory;

    public OAuthLinkingTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LinkFlow_creates_linked_account()
    {
        var userId = await _factory.SeedUserAsync();
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        using var linkRequest = new HttpRequestMessage(HttpMethod.Get, "/oauth/steam/link");
        IdentityWebApplicationFactory.AddTestAuth(linkRequest, userId);

        var linkResponse = await client.SendAsync(linkRequest);

        Assert.Equal(HttpStatusCode.Redirect, linkResponse.StatusCode);
        Assert.NotNull(linkResponse.Headers.Location);

        var state = ExtractState(linkResponse.Headers.Location!);
        Assert.False(string.IsNullOrWhiteSpace(state));

        using var callbackRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/oauth/steam/callback?code=mock&state={Uri.EscapeDataString(state)}");
        var cookieHeader = ExtractCookieHeader(linkResponse);
        if (!string.IsNullOrWhiteSpace(cookieHeader))
        {
            callbackRequest.Headers.Add("Cookie", cookieHeader);
        }

        var callbackResponse = await client.SendAsync(callbackRequest);

        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var linked = await db.LinkedAccounts.SingleAsync(la => la.UserId == userId);

        Assert.Equal("steam", linked.Provider);
        Assert.Equal("mock-steam-user", linked.ProviderAccountId);
    }

    [Fact]
    public async Task LinkFlow_rejects_unknown_provider()
    {
        var userId = await _factory.SeedUserAsync("other@example.com");
        var client = _factory.CreateClient();

        using var linkRequest = new HttpRequestMessage(HttpMethod.Get, "/oauth/unknown/link");
        IdentityWebApplicationFactory.AddTestAuth(linkRequest, userId);

        var response = await client.SendAsync(linkRequest);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string ExtractState(Uri location)
    {
        var query = QueryHelpers.ParseQuery(location.Query);
        return query.TryGetValue("state", out var state)
            ? state.ToString()
            : string.Empty;
    }

    private static string ExtractCookieHeader(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return string.Empty;
        }

        return string.Join("; ", values.Select(value => value.Split(';')[0]));
    }
}

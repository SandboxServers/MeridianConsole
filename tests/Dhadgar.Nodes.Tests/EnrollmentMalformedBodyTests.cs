using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// A malformed enrollment body is a client error: binding failures answer 400 problem+json, never 500 (D-ML107).
/// </summary>
/// <remarks>
/// <see cref="RouteHandlerOptions.ThrowOnBadRequest"/> defaults to true only in Development, so the Testing host
/// never throws a binding failure. The factory sets it, which makes these tests red on unfixed code.
/// </remarks>
[Collection("Nodes Integration")]
public sealed class EnrollmentMalformedBodyTests
{
    private const string EnrollRoute = "/agents/enroll";
    private const string BadRequestType = "https://meridian.console/errors/bad-request";
    private const string UnsupportedMediaTypeType = "https://meridian.console/errors/unsupported-media-type";

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"token\":")]
    [InlineData("[]")]
    [InlineData("{\"token\":123}")]
    public async Task Enroll_MalformedBody_Returns400ProblemJson_NotServerError(string body)
    {
        using var baseFactory = new NodesWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true)));
        using var client = factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(EnrollRoute, content);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.Equal(BadRequestType, document.RootElement.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task Enroll_WrongContentType_Returns415ProblemJson(string mediaType)
    {
        using var baseFactory = new NodesWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true)));
        using var client = factory.CreateClient();
        using var content = new StringContent("{\"token\":\"x\"}", Encoding.UTF8, mediaType);

        using var response = await client.PostAsync(EnrollRoute, content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.Equal(UnsupportedMediaTypeType, document.RootElement.GetProperty("type").GetString());
        Assert.Equal(415, document.RootElement.GetProperty("status").GetInt32());
    }
}

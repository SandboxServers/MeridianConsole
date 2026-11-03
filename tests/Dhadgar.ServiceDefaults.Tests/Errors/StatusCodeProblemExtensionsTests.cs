using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Dhadgar.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.Errors;

/// <summary>
/// F357: routing's 415 and 405 answer problem+json through <see cref="StatusCodeProblemExtensions"/>, and nothing
/// else is replaced.
/// </summary>
public sealed class StatusCodeProblemExtensionsTests
{
    private const string UnsupportedMediaTypeType = "https://meridian.console/errors/unsupported-media-type";
    private const string MethodNotAllowedType = "https://meridian.console/errors/method-not-allowed";

    public sealed record Payload(string Name);

    [Fact]
    public async Task WrongContentType_Returns415ProblemJson()
    {
        await using var app = await StartAsync(a => a.MapPost("/json", (Payload payload) => Results.Ok(payload)));
        using var client = app.GetTestClient();
        using var content = new StringContent("{\"name\":\"x\"}", Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync("/json", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(UnsupportedMediaTypeType, root.GetProperty("type").GetString());
        Assert.Equal("Unsupported Media Type", root.GetProperty("title").GetString());
        Assert.Equal(415, root.GetProperty("status").GetInt32());
        Assert.Equal("/json", root.GetProperty("instance").GetString());
        Assert.False(root.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task UnmappedMethod_Returns405ProblemJson()
    {
        await using var app = await StartAsync(a => a.MapGet("/only-get", () => Results.Ok("ok")));
        using var client = app.GetTestClient();

        using var response = await client.PostAsync("/only-get", null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(MethodNotAllowedType, document.RootElement.GetProperty("type").GetString());
        var allow = response.Headers.TryGetValues("Allow", out var headerValues)
            ? headerValues
            : response.Content.Headers.GetValues("Allow");
        Assert.Contains("GET", string.Join(",", allow), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotFound_IsLeftEmpty()
    {
        await using var app = await StartAsync(_ => { });
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EndpointWrittenBody_IsNotReplaced()
    {
        await using var app = await StartAsync(a =>
            a.MapPost("/teapot", () => Results.Text("mine", "text/plain", statusCode: 415)));
        using var client = app.GetTestClient();

        using var response = await client.PostAsync("/teapot", null);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("mine", await response.Content.ReadAsStringAsync());
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>Starts a Testing-environment host on TestServer with the middleware first and the given endpoints after it.</summary>
    private static async Task<WebApplication> StartAsync(Action<WebApplication> mapEndpoints)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.UseDhadgarStatusCodeProblems();
        mapEndpoints(app);
        await app.StartAsync();
        return app;
    }
}

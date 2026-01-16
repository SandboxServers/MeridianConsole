using Dhadgar.ServiceDefaults.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Dhadgar.Console.Tests;

public class SwaggerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SwaggerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
        });
    }

    [Fact]
    public async Task SwaggerEndpoint_ReturnsValidOpenApiSpec()
    {
        await SwaggerTestHelper.VerifySwaggerEndpointAsync(
            _factory,
            expectedTitle: "Dhadgar Console API");
    }

    [Fact]
    public async Task SwaggerUi_ReturnsHtml()
    {
        await SwaggerTestHelper.VerifySwaggerUiAsync(_factory);
    }

    [Fact]
    public async Task SwaggerEndpoint_DocumentsHealthEndpoints()
    {
        await SwaggerTestHelper.VerifyHealthEndpointsDocumentedAsync(_factory);
    }
}

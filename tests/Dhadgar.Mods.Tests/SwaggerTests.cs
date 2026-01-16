using Dhadgar.ServiceDefaults.Tests;
using Xunit;

namespace Dhadgar.Mods.Tests;

public class SwaggerTests : IClassFixture<ModsWebApplicationFactory>
{
    private readonly ModsWebApplicationFactory _factory;

    public SwaggerTests(ModsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SwaggerEndpoint_ReturnsValidOpenApiSpec()
    {
        await SwaggerTestHelper.VerifySwaggerEndpointAsync(
            _factory,
            expectedTitle: "Dhadgar Mods API");
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

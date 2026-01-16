using Dhadgar.ServiceDefaults.Tests;
using Xunit;

namespace Dhadgar.Nodes.Tests;

public class SwaggerTests : IClassFixture<NodesWebApplicationFactory>
{
    private readonly NodesWebApplicationFactory _factory;

    public SwaggerTests(NodesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SwaggerEndpoint_ReturnsValidOpenApiSpec()
    {
        await SwaggerTestHelper.VerifySwaggerEndpointAsync(
            _factory,
            expectedTitle: "Dhadgar Nodes API");
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

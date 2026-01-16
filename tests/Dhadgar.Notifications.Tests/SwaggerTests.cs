using Dhadgar.ServiceDefaults.Tests;
using Xunit;

namespace Dhadgar.Notifications.Tests;

public class SwaggerTests : IClassFixture<NotificationsWebApplicationFactory>
{
    private readonly NotificationsWebApplicationFactory _factory;

    public SwaggerTests(NotificationsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SwaggerEndpoint_ReturnsValidOpenApiSpec()
    {
        await SwaggerTestHelper.VerifySwaggerEndpointAsync(
            _factory,
            expectedTitle: "Dhadgar Notifications API");
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

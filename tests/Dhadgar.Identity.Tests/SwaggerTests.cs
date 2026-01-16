using Dhadgar.ServiceDefaults.Tests;
using Xunit;

namespace Dhadgar.Identity.Tests;

public class SwaggerTests : IClassFixture<IdentityWebApplicationFactory>
{
    private readonly IdentityWebApplicationFactory _factory;

    public SwaggerTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SwaggerEndpoint_ReturnsValidOpenApiSpec()
    {
        await SwaggerTestHelper.VerifySwaggerEndpointAsync(
            _factory,
            expectedTitle: "Dhadgar Identity API");
    }

    [Fact]
    public async Task SwaggerUi_ReturnsHtml()
    {
        await SwaggerTestHelper.VerifySwaggerUiAsync(_factory);
    }

    [Fact]
    public async Task SwaggerSpec_ContainsIdentityEndpoints()
    {
        // Identity uses /me for user endpoints and /organizations for org management
        await SwaggerTestHelper.VerifySwaggerContainsPathsAsync(
            _factory,
            ["/", "/hello", "/me", "/organizations"]);
    }
}

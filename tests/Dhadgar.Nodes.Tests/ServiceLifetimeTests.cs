using Dhadgar.Nodes.Services;
using Dhadgar.Nodes.Tests.Integration.AgentSeam;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// Guards against singletons capturing scoped services (P17f): the CA service is a singleton and
/// must never hold a scoped NodesDbContext.
/// </summary>
[Collection("Nodes Integration")]
public sealed class ServiceLifetimeTests : IDisposable
{
    private readonly NodesWebApplicationFactory _baseFactory;
    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"lifetime-ca-{Guid.NewGuid()}");

    public ServiceLifetimeTests(NodesWebApplicationFactory factory)
    {
        _baseFactory = factory;
        Directory.CreateDirectory(_caDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_caDirectory, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void NodesHost_WithScopeValidation_HasNoSingletonCapturingScopedServices()
    {
        using var factory = AgentSeamHarness.CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true)
            .WithWebHostBuilder(b => b.UseDefaultServiceProvider(o =>
            {
                o.ValidateScopes = true;
                o.ValidateOnBuild = true;
            }));

        // Building the host runs ValidateOnBuild; resolving from the root provider runs ValidateScopes.
        var ca = factory.Services.GetRequiredService<ICertificateAuthorityService>();
        Assert.IsType<CertificateAuthorityService>(ca);
    }
}

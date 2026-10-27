using System.Net;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Health;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Nodes.Auth;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using AgentEnrollmentService = Dhadgar.Agent.Core.Authentication.EnrollmentService;
using NodesEnrollmentTokenService = Dhadgar.Nodes.Services.IEnrollmentTokenService;

namespace Dhadgar.Nodes.Tests.Integration.AgentSeam;

/// <summary>
/// Shared setup for tests that drive the real agent against the real Nodes host over a <see cref="TestServer"/>.
/// Public so that other test projects can reuse it.
/// </summary>
public static class AgentSeamHarness
{
    /// <summary>
    /// Builds a Nodes factory that uses the real certificate authority, writing CA material to <paramref name="caDirectory"/>.
    /// </summary>
    /// <param name="baseFactory">The shared Nodes factory.</param>
    /// <param name="caDirectory">Directory for CA material. The caller owns cleanup.</param>
    /// <param name="enableMtls">When true, enables mTLS middleware options for the host.</param>
    /// <param name="configureMtls">Optional extra mTLS option changes, applied after <c>Enabled</c> is set.</param>
    /// <param name="settings">Optional configuration overrides, such as <c>Mtls:RenewalGraceMinutes</c>.</param>
    public static WebApplicationFactory<Program> CreateFactoryWithRealCa(
        NodesWebApplicationFactory baseFactory,
        string caDirectory,
        bool enableMtls,
        Action<MtlsOptions>? configureMtls = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        return baseFactory.WithWebHostBuilder(builder =>
        {
            if (settings is not null)
            {
                builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(settings));
            }

            builder.ConfigureTestServices(services =>
            {
                services.Configure<NodesOptions>(o =>
                {
                    o.CaStoragePath = caDirectory;
                    o.CaKeySize = 2048;
                });
                services.RemoveAll<ICaStorageProvider>();
                services.RemoveAll<ICertificateAuthorityService>();
                services.AddSingleton<ICaStorageProvider, LocalFileCaStorageProvider>();
                services.AddSingleton<ICertificateAuthorityService, CertificateAuthorityService>();

                if (enableMtls)
                {
                    services.Configure<MtlsOptions>(o =>
                    {
                        o.Enabled = true;
                        configureMtls?.Invoke(o);
                    });
                }
            });
        });
    }

    /// <summary>
    /// Creates a one-time enrollment token for <paramref name="orgId"/>.
    /// </summary>
    public static async Task<string> CreateEnrollmentTokenAsync(
        WebApplicationFactory<Program> factory,
        Guid orgId)
    {
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<NodesEnrollmentTokenService>();
        var (_, plainToken) = await tokens.CreateTokenAsync(orgId, "user-1", "contract-test");
        return plainToken;
    }

    /// <summary>
    /// Creates the real agent enrollment service over the given HTTP client factory and certificate store.
    /// </summary>
    /// <param name="httpClientFactory">Routes the agent's requests to the host under test.</param>
    /// <param name="store">The agent's certificate store.</param>
    /// <param name="timeProvider">
    /// The Nodes host's clock. The agent checks the issued certificate's validity on this clock, and the Nodes CA
    /// stamps NotBefore from the same one, so a seam test must pass the host's TimeProvider, never the system clock (D-S234).
    /// </param>
    public static AgentEnrollmentService CreateEnrollmentService(
        IHttpClientFactory httpClientFactory,
        ICertificateStore store,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new AgentEnrollmentService(
            store,
            httpClientFactory,
            Options.Create(new AgentOptions()),
            NullLogger<AgentEnrollmentService>.Instance,
            new HardwareInventoryCollector(NullLogger<HardwareInventoryCollector>.Instance),
            new ClientCertificateProvider(store),
            new AgentIdentityProvider(),
            enrollmentTokenCleanup: null,
            timeProvider: timeProvider);
    }

    /// <summary>
    /// Enrolls a new agent against <paramref name="factory"/> with a fresh token. Fails the test if enrollment fails.
    /// </summary>
    public static async Task<(EnrollmentResult Result, InMemoryCertificateStore Store)> EnrollAgentAsync(
        WebApplicationFactory<Program> factory,
        Guid orgId)
    {
        var token = await CreateEnrollmentTokenAsync(factory, orgId);
        var store = new InMemoryCertificateStore();
        var agent = CreateEnrollmentService(
            new TestServerClientFactory(factory.Server),
            store,
            factory.Services.GetRequiredService<TimeProvider>());

        var result = await agent.EnrollAsync(token);
        Xunit.Assert.True(result.IsSuccess, result.Error);
        return (result.Value!, store);
    }

    /// <summary>
    /// Builds a handler factory for <c>ControlPlaneHubTransport</c> that presents the client certificate the agent
    /// chose to the TestServer connection, as Kestrel does after a TLS handshake.
    /// </summary>
    /// <param name="server">The TestServer to route requests to.</param>
    /// <param name="remoteIp">Optional remote address to report for the connection.</param>
    public static Func<HttpMessageHandler, HttpMessageHandler> CertificateInjectingHandlerFactory(
        TestServer server,
        IPAddress? remoteIp = null)
    {
        return inner =>
        {
            // The SignalR client builds an HttpClientHandler and copies HttpConnectionOptions.ClientCertificates
            // into it. Read the cert the agent actually chose, then put it on the server-side connection,
            // which is what Kestrel does after a TLS handshake.
            var cert = (inner as HttpClientHandler)?.ClientCertificates.OfType<X509Certificate2>().FirstOrDefault();
            inner.Dispose();
            return server.CreateHandler(ctx =>
            {
                ctx.Connection.ClientCertificate = cert;
                if (remoteIp is not null)
                {
                    ctx.Connection.RemoteIpAddress = remoteIp;
                }
            });
        };
    }
}

using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Windows.Uat.Stack;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class GatewayClientsTests
{
    [Fact]
    public void Create_NoCertificate_HasNoValidationCallback()
    {
        using var client = GatewayClients.Create();

        AssertNoCallbacks(client);
    }

    [Fact]
    public void Create_WithCertificate_HasNoValidationCallback_AndOffersTheCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var request = new CertificateRequest("CN=uat", key, HashAlgorithmName.SHA384);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));

        using var client = GatewayClients.Create(cert);

        AssertNoCallbacks(client);
        var handler = GetHandler(client);
        Assert.Single(handler.SslOptions.ClientCertificates!);
        Assert.NotNull(handler.SslOptions.LocalCertificateSelectionCallback);
        Assert.False(handler.AllowAutoRedirect);
    }

    private static void AssertNoCallbacks(HttpClient client)
    {
        var handler = GetHandler(client);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
    }

    private static SocketsHttpHandler GetHandler(HttpClient client)
    {
        var field = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<SocketsHttpHandler>(field!.GetValue(client));
    }
}

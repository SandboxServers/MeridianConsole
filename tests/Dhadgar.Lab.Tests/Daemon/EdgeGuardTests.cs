using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Daemon;

/// <summary>
/// The edge refusal order (C2): Host, then Origin, then bearer token, for every route.
/// </summary>
[Collection("LabHome")]
public sealed class EdgeGuardTests
{
    [Fact]
    public async Task Edge_NoAuthorization_Returns401WithChallenge()
    {
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(EdgeTestHost.NewToken(), dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };

            using var response = await http.GetAsync("status");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Bearer !!not-base64url!!")]
    [InlineData("Bearer abc def")]
    public async Task Edge_MalformedAuthorization_Returns401(string authorization)
    {
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(EdgeTestHost.NewToken(), dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "status");
            request.Headers.TryAddWithoutValidation("Authorization", authorization);

            using var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Edge_TokenOfDifferentLength_Returns401()
    {
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(EdgeTestHost.NewToken(), dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Base64UrlOf(new byte[64]));

            using var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Edge_OriginHeader_Returns403()
    {
        var token = EdgeTestHost.NewToken();
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(token, dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("Origin", "http://evil");

            using var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Edge_EmptyOriginHeader_Returns403()
    {
        var token = EdgeTestHost.NewToken();
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(token, dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("Origin", string.Empty);

            using var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Edge_ForeignHost_Returns403()
    {
        var token = EdgeTestHost.NewToken();
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(token, dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Host = "evil.example:" + daemon.BaseAddress.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);

            using var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Edge_HostWithoutPort_Returns403()
    {
        var token = EdgeTestHost.NewToken();
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(token, dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Host = "127.0.0.1";

            using var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Edge_LocalhostHostWithBoundPort_IsAccepted()
    {
        var token = EdgeTestHost.NewToken();
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(token, dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Host = "localhost:" + daemon.BaseAddress.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);

            using var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Edge_ForeignHostWithoutToken_Returns403Not401()
    {
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(EdgeTestHost.NewToken(), dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "status");
            request.Headers.Host = "evil.example";

            using var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Edge_OriginWithoutToken_Returns403Not401()
    {
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(EdgeTestHost.NewToken(), dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "status");
            request.Headers.TryAddWithoutValidation("Origin", "http://evil");

            using var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Edge_UnknownRouteWithoutToken_Returns401NotFound()
    {
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(EdgeTestHost.NewToken(), dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };

            using var response = await http.GetAsync("nope");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Edge_UnknownRouteWithToken_Returns404()
    {
        var token = EdgeTestHost.NewToken();
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(token, dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "nope");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Status_ValidToken_ReturnsIdleStatusWithoutToken()
    {
        var token = EdgeTestHost.NewToken();
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(token, dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(token, body, StringComparison.Ordinal);
            using var json = JsonDocument.Parse(body);
            Assert.Equal("down", json.RootElement.GetProperty("stackState").GetString());
            Assert.False(json.RootElement.GetProperty("lease").GetProperty("held").GetBoolean());
            Assert.Equal(0, json.RootElement.GetProperty("processes").GetArrayLength());
            Assert.Equal(0, json.RootElement.GetProperty("activeFaults").GetInt32());
            Assert.Equal(0, json.RootElement.GetProperty("leasedMembers").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("runningJob").ValueKind);
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Status_LeasePurposeHoldsVaultSecret_ReturnsRedactedPurpose()
    {
        // GET /status runs the same redaction as lab_status: a lease purpose that equals a vault value is masked.
        var token = EdgeTestHost.NewToken();
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(token, dataRoot);
            var secret = "purpose-secret-" + Guid.NewGuid().ToString("N");
            var secretRef = daemon.Services.GetRequiredService<SecretVault>().Add(SecretKind.Password, secret.AsSpan());
            var lease = daemon.Services.GetRequiredService<LeaseBook>().Acquire("session-a", secret, null, false, null);
            Assert.True(lease.IsSuccess, lease.Error);

            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
            Assert.Contains("***" + secretRef, body, StringComparison.Ordinal);
            using var json = JsonDocument.Parse(body);
            var leaseJson = json.RootElement.GetProperty("lease");
            Assert.True(leaseJson.GetProperty("held").GetBoolean());
            Assert.Equal("session-a", leaseJson.GetProperty("owner").GetString());
            Assert.Equal("***" + secretRef, leaseJson.GetProperty("purpose").GetString());
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Edge_ForeignHostRefusal_ResponseCarriesNoToken()
    {
        var token = EdgeTestHost.NewToken();
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(token, dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Host = "evil.example";

            using var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain(token, response.Headers.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task Edge_WrongPortInHost_Returns403()
    {
        // The Host allowlist names only the bound port: a different port in Host is refused.
        var token = EdgeTestHost.NewToken();
        var dataRoot = EdgeTestHost.NewDataRoot();
        try
        {
            await using var daemon = await EdgeTestHost.StartAsync(token, dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, "status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var otherPort = daemon.BaseAddress.Port == 65535 ? 1 : daemon.BaseAddress.Port + 1;
            request.Headers.Host = "127.0.0.1:" + otherPort.ToString(System.Globalization.CultureInfo.InvariantCulture);

            using var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            EdgeTestHost.DeleteDataRoot(dataRoot);
        }
    }

    private static string Base64UrlOf(byte[] bytes) => System.Buffers.Text.Base64Url.EncodeToString(bytes);
}

/// <summary>
/// Shared fixture for the daemon tests: starts a daemon on an ephemeral loopback port and makes scratch data roots.
/// </summary>
internal static class EdgeTestHost
{
    /// <summary>
    /// Starts the daemon with the single-instance guard and elevation check off, so tests can run in parallel.
    /// </summary>
    public static async Task<LabDaemon> StartAsync(string token, string dataRoot)
    {
        var options = new LabDaemonOptions(
            Port: 0,
            Token: token,
            DataRoot: dataRoot,
            EnforceSingleInstance: false,
            RefuseElevated: false,
            Time: new FakeTimeProvider(DateTimeOffset.Parse("2026-11-02T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture)));

        var started = await LabDaemon.StartAsync(options, CancellationToken.None);
        Assert.True(started.IsSuccess, started.Error);
        return started.Value;
    }

    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
        try
        {
            return System.Buffers.Text.Base64Url.EncodeToString(bytes);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    public static string NewDataRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "lab-l02-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteDataRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

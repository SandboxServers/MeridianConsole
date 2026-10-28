using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Dhadgar.Lab.Daemon;
using ModelContextProtocol.Client;

namespace Dhadgar.Lab.Tests.Seams;

/// <summary>
/// S-L1: the daemon's loopback edge and MCP projection. The MCP facts go green in L05; the raw HTTP facts go green in L02.
/// </summary>
[Collection("LabHome")]
public sealed class McpEdgeSeamTests
{
    [Fact]
    [Trait("Seam", "S-L1")]
    public async Task Mcp_ValidToken_ListsLabStatusAndCallSucceeds()
    {
        var token = NewToken();
        var dataRoot = NewDataRoot();
        try
        {
            await using var daemon = await StartDaemonAsync(token, dataRoot);

            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(daemon.BaseAddress, LabDefaults.McpPath.TrimStart('/')),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token }
            });
            await using var client = await McpClient.CreateAsync(transport, null, null, CancellationToken.None);

            var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);
            Assert.Contains(tools, t => t.Name == "lab_status");

            var result = await client.CallToolAsync("lab_status", new Dictionary<string, object?>(), null, null, CancellationToken.None);
            Assert.NotEqual(true, result.IsError);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    [Trait("Seam", "S-L1")]
    public async Task Mcp_LeasedTool_TakesLeaseIdAsEnvelope()
    {
        var token = NewToken();
        var dataRoot = NewDataRoot();
        try
        {
            await using var daemon = await StartDaemonAsync(token, dataRoot);

            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(daemon.BaseAddress, LabDefaults.McpPath.TrimStart('/')),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token }
            });
            await using var client = await McpClient.CreateAsync(transport, null, null, CancellationToken.None);

            // C3, D-ML66: lab_build advertises lease_id as required; uat_run advertises it as optional.
            var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);
            System.Text.Json.JsonElement buildSchema = tools.First(t => t.Name == "lab_build").JsonSchema;
            Assert.Contains("lease_id", buildSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
            System.Text.Json.JsonElement runSchema = tools.First(t => t.Name == "uat_run").JsonSchema;
            Assert.True(runSchema.GetProperty("properties").TryGetProperty("lease_id", out _));
            Assert.DoesNotContain("lease_id", runSchema.TryGetProperty("required", out System.Text.Json.JsonElement runRequired)
                ? runRequired.EnumerateArray().Select(item => item.GetString())
                : Array.Empty<string?>());

            var acquired = await client.CallToolAsync(
                "lab_lease_acquire",
                new Dictionary<string, object?> { ["owner"] = "s-l1", ["purpose"] = "seam" },
                null,
                null,
                CancellationToken.None);
            Assert.NotEqual(true, acquired.IsError);
            string leaseId = acquired.StructuredContent!.Value.GetProperty("lease_id").GetString()!;

            try
            {
                var missing = await client.CallToolAsync("lab_build", new Dictionary<string, object?>(), null, null, CancellationToken.None);
                Assert.Equal(true, missing.IsError);
                Assert.Equal("Lab.LeaseRequired", missing.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());

                var bound = await client.CallToolAsync(
                    "lab_build",
                    new Dictionary<string, object?> { ["lease_id"] = leaseId },
                    null,
                    null,
                    CancellationToken.None);
                Assert.Equal(true, bound.IsError);
                System.Text.Json.JsonElement error = bound.StructuredContent!.Value.GetProperty("error");
                Assert.Equal("Lab.InvalidArgument", error.GetProperty("code").GetString());
                Assert.Contains("checkout is required", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                await client.CallToolAsync(
                    "lab_lease_release",
                    new Dictionary<string, object?> { ["lease_id"] = leaseId },
                    null,
                    null,
                    CancellationToken.None);
            }
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    [Trait("Seam", "S-L1")]
    public async Task Edge_NoToken_Returns401()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var daemon = await StartDaemonAsync(NewToken(), dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var response = await http.GetAsync(LabDefaults.StatusPath.TrimStart('/'));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    [Trait("Seam", "S-L1")]
    public async Task Edge_WrongToken_Returns401()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var daemon = await StartDaemonAsync(NewToken(), dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, LabDefaults.StatusPath.TrimStart('/'));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NewToken());
            using var response = await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    [Trait("Seam", "S-L1")]
    public async Task Edge_OriginHeader_Returns403()
    {
        var token = NewToken();
        var dataRoot = NewDataRoot();
        try
        {
            await using var daemon = await StartDaemonAsync(token, dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, LabDefaults.StatusPath.TrimStart('/'));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("Origin", "http://evil");
            using var response = await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    [Trait("Seam", "S-L1")]
    public async Task Edge_ForeignHostHeader_Returns403()
    {
        var token = NewToken();
        var dataRoot = NewDataRoot();
        try
        {
            await using var daemon = await StartDaemonAsync(token, dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Get, LabDefaults.StatusPath.TrimStart('/'));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Host = "evil:" + LabDefaults.DaemonPort;
            using var response = await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    [Trait("Seam", "S-L1")]
    public async Task Mcp_NoToken_Returns401()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var daemon = await StartDaemonAsync(NewToken(), dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Post, LabDefaults.McpPath.TrimStart('/'));
            using var response = await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    [Trait("Seam", "S-L1")]
    public async Task Mcp_ValidTokenWithOrigin_Returns403()
    {
        var token = NewToken();
        var dataRoot = NewDataRoot();
        try
        {
            await using var daemon = await StartDaemonAsync(token, dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Post, LabDefaults.McpPath.TrimStart('/'));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("Origin", "http://evil");
            using var response = await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    [Trait("Seam", "S-L1")]
    public async Task Mcp_ForeignHostHeader_Returns403()
    {
        var token = NewToken();
        var dataRoot = NewDataRoot();
        try
        {
            await using var daemon = await StartDaemonAsync(token, dataRoot);
            using var http = new HttpClient { BaseAddress = daemon.BaseAddress };
            using var request = new HttpRequestMessage(HttpMethod.Post, LabDefaults.McpPath.TrimStart('/'));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Host = "evil:" + LabDefaults.DaemonPort;
            using var response = await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    /// <summary>
    /// Starts the daemon on an ephemeral loopback port and asserts the start succeeded.
    /// </summary>
    private static async Task<LabDaemon> StartDaemonAsync(string token, string dataRoot)
    {
        var options = new LabDaemonOptions(
            Port: 0,
            Token: token,
            DataRoot: dataRoot,
            EnforceSingleInstance: false,
            RefuseElevated: false);

        var started = await LabDaemon.StartAsync(options, CancellationToken.None);
        Assert.True(started.IsSuccess, started.Error);
        return started.Value;
    }

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
        try
        {
            return Base64Url.EncodeToString(bytes);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private static string NewDataRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "lab-s1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDataRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

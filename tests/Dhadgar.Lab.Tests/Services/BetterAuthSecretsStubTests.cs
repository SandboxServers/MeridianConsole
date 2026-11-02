using System.Buffers.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Dhadgar.Lab.Services;

namespace Dhadgar.Lab.Tests.Services;

/// <summary>
/// The lab Secrets stub (D-ML144) and BetterAuth's real <c>loadSecrets</c> must agree on paths and secret names. The static half
/// needs nothing; the live half runs the product client against the stub under <c>node</c>, without Docker.
/// </summary>
public sealed class BetterAuthSecretsStubTests
{
    private const string StubRelative = "tools/lab/images/lab-secrets-stub.mjs";
    private const string ClientRelative = "src/Dhadgar.BetterAuth/src/secrets-client.js";

    [Fact]
    public void Stub_AndBetterAuthClient_AgreeOnPathsAndNames()
    {
        string root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string stub = File.ReadAllText(Path.Combine(root, StubRelative));
        string client = File.ReadAllText(Path.Combine(root, ClientRelative));

        foreach (string token in new[] { "\"betterauth-secret\"", "\"betterauth-exchange-private-key\"", "/api/v1/secrets/betterauth", "/api/v1/secrets/oauth", "/connect/token" })
        {
            Assert.Contains(token, stub, StringComparison.Ordinal);
            Assert.Contains(token, client, StringComparison.Ordinal);
        }

        Assert.Contains("\"127.0.0.1\"", stub, StringComparison.Ordinal);
        string dockerfile = File.ReadAllText(Path.Combine(root, "tools", "lab", "images", "node-app.Dockerfile"));
        Assert.Contains("COPY lab /lab", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY SharedAuth /src/Dhadgar.SharedAuth", dockerfile, StringComparison.Ordinal);
        string panel = File.ReadAllText(Path.Combine(root, "src", "Dhadgar.Panel", "package.json"));
        Assert.Contains("file:../Dhadgar.SharedAuth", panel, StringComparison.Ordinal);
        Assert.Equal("/lab/secrets-stub.mjs", BetterAuthLabAdapter.SecretsStubPath);
    }

    [SkippableFact(Timeout = 120_000)]
    public async Task Stub_FeedsTheRealLoadSecrets()
    {
        string root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        Skip.IfNot(NodeAvailable(), "node is not on PATH");

        int port = FreeLoopbackPort();
        string clientUrl = new Uri(Path.Combine(root, ClientRelative.Replace('/', Path.DirectorySeparatorChar))).AbsoluteUri;
        string stubUrl = new Uri(Path.Combine(root, StubRelative.Replace('/', Path.DirectorySeparatorChar))).AbsoluteUri;
        string secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string privateKey = LabEnvValue.SingleLinePem(key.ExportPkcs8PrivateKeyPem());

        var psi = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.Combine(root, "src", "Dhadgar.BetterAuth"),
        };
        psi.ArgumentList.Add("--input-type=module");
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(
            $"import {{ loadSecrets }} from \"{clientUrl}\";\n"
            + "await loadSecrets();\n"
            // The stub keeps its listener open (as it must in the container), so the script ends the process itself once stdout is flushed.
            + "const verdict = process.env.BETTER_AUTH_SECRET === process.env.EXPECT_SECRET && process.env.EXCHANGE_TOKEN_PRIVATE_KEY === process.env.EXPECT_KEY ? \"ok\" : \"mismatch\";\n"
            + "process.stdout.write(verdict + \"\\n\", () => process.exit(0));");
        foreach (string name in new[] { "IDENTITY_SERVICE_URL", "SERVICE_CLIENT_ID", "SERVICE_CLIENT_SECRET" })
        {
            _ = psi.Environment.Remove(name);
        }

        psi.Environment["NODE_OPTIONS"] = "--import=" + stubUrl;
        psi.Environment["LAB_SECRETS_STUB_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        psi.Environment["SECRETS_SERVICE_URL"] = $"http://127.0.0.1:{port}";
        psi.Environment["USE_ENV_DB_PASSWORD"] = "1";
        psi.Environment["NODE_ENV"] = "production";
        psi.Environment["BETTER_AUTH_SECRET"] = secret;
        psi.Environment["EXPECT_SECRET"] = secret;
        psi.Environment["EXCHANGE_TOKEN_PRIVATE_KEY"] = privateKey;
        psi.Environment["EXPECT_KEY"] = privateKey;

        using var process = new Process { StartInfo = psi };
        Assert.True(process.Start(), "node did not start");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        string output = await stdout;
        _ = await stderr;

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("Secrets loaded successfully", output, StringComparison.Ordinal);
        string last = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1];
        Assert.Equal("ok", last);
    }

    private static bool NodeAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            _ = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}

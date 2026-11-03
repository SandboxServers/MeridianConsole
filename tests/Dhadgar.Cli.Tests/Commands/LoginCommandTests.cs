using System.Net;
using System.Net.Sockets;
using System.Text;
using Dhadgar.Cli.Configuration;
using FluentAssertions;
using Xunit;

namespace Dhadgar.Cli.Tests.Commands;

/// <summary>
/// Verifies the exit code and the stored token of <c>auth login</c> when the Identity request fails.
/// Every request goes to a loopback listener owned by the test; no real Identity or Gateway is contacted.
/// </summary>
[Collection("CliConfig")]
public sealed class LoginCommandTests : IDisposable
{
    private const string DummyClientId = "x";
    private const string DummyClientSecret = "y";

    private readonly string _configDirectory;

    public LoginCommandTests()
    {
        _configDirectory = Path.Combine(Path.GetTempPath(), "dhadgar-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDirectory);
        CliConfig.ConfigDirectoryOverride = _configDirectory;
    }

    public void Dispose()
    {
        CliConfig.ConfigDirectoryOverride = null;
        if (Directory.Exists(_configDirectory))
        {
            Directory.Delete(_configDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Login_FailedRequestWithValidStoredToken_ReturnsOneAndKeepsToken()
    {
        new CliConfig
        {
            AccessToken = "stored",
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        }.Save();

        using var identity = new OneShotIdentityListener("HTTP/1.1 400 Bad Request", "{\"error\":\"invalid_client\"}");

        var exitCode = await CliApp.BuildRoot()
            .Parse(["auth", "login", "--client-id", DummyClientId, "--client-secret", DummyClientSecret,
                "--identity-url", $"http://127.0.0.1:{identity.Port}"])
            .InvokeAsync();

        exitCode.Should().Be(1);
        CliConfig.Load().AccessToken.Should().Be("stored");
    }

    [Fact]
    public async Task Login_ServerDown_ReturnsOne()
    {
        new CliConfig
        {
            AccessToken = "stored",
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        }.Save();

        var exitCode = await CliApp.BuildRoot()
            .Parse(["auth", "login", "--client-id", DummyClientId, "--client-secret", DummyClientSecret,
                "--identity-url", "http://127.0.0.1:1"])
            .InvokeAsync();

        exitCode.Should().Be(1);
        CliConfig.Load().AccessToken.Should().Be("stored");
    }

    /// <summary>
    /// Answers exactly one HTTP request on 127.0.0.1 with a fixed response, then stops listening.
    /// </summary>
    private sealed class OneShotIdentityListener : IDisposable
    {
        private readonly TcpListener _listener;

        public OneShotIdentityListener(string statusLine, string body)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            // ServeOnceAsync handles its own exceptions, so the discarded task cannot fault unobserved.
            _ = Task.Run(() => ServeOnceAsync(statusLine, body));
        }

        public int Port { get; }

        public void Dispose()
        {
            _listener.Stop();
        }

        private async Task ServeOnceAsync(string statusLine, string body)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

                var contentLength = 0;
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                {
                    const string header = "Content-Length:";
                    if (line.StartsWith(header, StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line[header.Length..].Trim(), out contentLength);
                    }
                }

                if (contentLength > 0)
                {
                    await reader.ReadBlockAsync(new char[contentLength], 0, contentLength);
                }

                var response = $"{statusLine}\r\nContent-Type: application/json\r\n" +
                               $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
                               $"Connection: close\r\n\r\n{body}";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
                await stream.FlushAsync();
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // The listener was disposed or the client went away; the test asserts on the CLI result.
            }
        }
    }
}

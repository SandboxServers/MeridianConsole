using Dhadgar.Cli.Configuration;
using FluentAssertions;
using Xunit;

namespace Dhadgar.Cli.Tests.Commands;

/// <summary>
/// Verifies that every command returns its exit code to the process. No test here contacts a network host.
/// </summary>
[Collection("CliConfig")]
public sealed class ExitCodeTests : IDisposable
{
    private readonly string _configDirectory;

    public ExitCodeTests()
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

    [Theory]
    [InlineData("nodes list")]
    [InlineData("nodes get 00000000-0000-0000-0000-000000000001")]
    [InlineData("enrollment tokens list")]
    [InlineData("identity orgs list")]
    [InlineData("me orgs")]
    [InlineData("member list")]
    public async Task Command_NotAuthenticated_ReturnsOne(string commandLine)
    {
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var exitCode = await CliApp.BuildRoot().Parse(args).InvokeAsync();

        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Ping_RefusedConnection_ReturnsOne()
    {
        var exitCode = await CliApp.BuildRoot().Parse(["ping", "--url", "http://127.0.0.1:1/healthz"]).InvokeAsync();

        exitCode.Should().Be(1);
    }
}

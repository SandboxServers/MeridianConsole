using System.Net;
using Dhadgar.Lab.Cli;
using Dhadgar.Lab.Daemon;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Tests.Daemon;

/// <summary>
/// Exit codes of <c>meridian-lab daemon run</c>: 2 for config refusals, 3 for a second instance. The refusal cases here never
/// start a daemon; the Kestrel case starts one on an ephemeral port and checks that it listens on loopback only.
/// </summary>
/// <remarks>
/// These tests set process environment variables, so they run one at a time within this class.
/// </remarks>
[Collection("LabHome")]
public sealed class LabCliExitCodeTests
{
    [Fact]
    public async Task DaemonRun_ShortToken_ExitsWithTwo()
    {
        var exit = await RunWithEnvironmentAsync(
            new Dictionary<string, string?> { [LabDefaults.TokenEnvVar] = "c2hvcnQ" },
            ["daemon", "run"]);

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task DaemonRun_MissingToken_ExitsWithTwo()
    {
        var exit = await RunWithEnvironmentAsync(
            new Dictionary<string, string?> { [LabDefaults.TokenEnvVar] = null },
            ["daemon", "run"]);

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task DaemonRun_NonLoopbackBindAddress_ExitsWithTwo()
    {
        var exit = await RunWithEnvironmentAsync(
            new Dictionary<string, string?>
            {
                [LabDefaults.TokenEnvVar] = ValidToken(),
                ["Lab__BindAddress"] = "0.0.0.0",
            },
            ["daemon", "run"]);

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task DaemonRun_IPv6AnyBindAddress_ExitsWithTwo()
    {
        var exit = await RunWithEnvironmentAsync(
            new Dictionary<string, string?>
            {
                [LabDefaults.TokenEnvVar] = ValidToken(),
                ["Lab__BindAddress"] = "::",
            },
            ["daemon", "run"]);

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task DaemonStart_KestrelEndpointOnAnyAddress_BindsLoopbackOnly()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "lab-l02-kestrel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        try
        {
            var options = new LabDaemonOptions(0, ValidToken(), dataRoot, EnforceSingleInstance: false, RefuseElevated: false);
            var started = await WithEnvironmentAsync(
                new Dictionary<string, string?> { ["Kestrel__Endpoints__Lan__Url"] = "http://0.0.0.0:0" },
                () => LabDaemon.StartAsync(options, CancellationToken.None));

            Assert.True(started.IsSuccess, started.Error);
            await using var daemon = started.Value;
            var feature = daemon.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            Assert.NotNull(feature);
            Assert.NotEmpty(feature.Addresses);
            foreach (var address in feature.Addresses)
            {
                var host = new Uri(address).Host.Trim('[', ']');
                Assert.True(IPAddress.IsLoopback(IPAddress.Parse(host)), address);
            }
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TokenNew_PrintsOneLineAndExitsWithZero()
    {
        var original = Console.Out;
        using var captured = new StringWriter();
        string output;
        int exit;
        try
        {
            Console.SetOut(captured);
            exit = await LabCli.RunAsync(["token", "new"]);
            output = captured.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal(0, exit);
        var line = output.TrimEnd('\r', '\n');
        Assert.Equal(LabDefaults.MinTokenBytes, System.Buffers.Text.Base64Url.DecodeFromChars(line).Length);
    }

    [Fact]
    public async Task TakeTokenFromEnvironment_ReturnsTokenAndClearsItFromEnvironment()
    {
        var token = ValidToken();
        var (read, after) = await WithEnvironmentAsync(
            new Dictionary<string, string?> { [LabDefaults.TokenEnvVar] = token },
            () =>
            {
                var taken = LabCli.TakeTokenFromEnvironment();
                return Task.FromResult((taken, Environment.GetEnvironmentVariable(LabDefaults.TokenEnvVar)));
            });

        Assert.Equal(token, read);
        Assert.Null(after);
    }

    [Theory]
    [InlineData("pass", 0)]
    [InlineData("fail", 1)]
    [InlineData("incomplete", 4)]
    [InlineData("pass_with_known", 5)]
    [InlineData("error", 2)]
    [InlineData("", 2)]
    [InlineData(null, 2)]
    public void UatRun_OverallMapsToCliExitCode(string? overall, int expected)
    {
        Assert.Equal(expected, LabCli.ExitCodeForOverall(overall));
    }

    private static string ValidToken() => System.Buffers.Text.Base64Url.EncodeToString(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes));

    private static Task<int> RunWithEnvironmentAsync(IReadOnlyDictionary<string, string?> values, string[] args) =>
        WithEnvironmentAsync(values, () => LabCli.RunAsync(args));

    private static async Task<T> WithEnvironmentAsync<T>(IReadOnlyDictionary<string, string?> values, Func<Task<T>> action)
    {
        var saved = new Dictionary<string, string?>();
        foreach (var key in values.Keys)
        {
            saved[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, values[key]);
        }

        try
        {
            return await action();
        }
        finally
        {
            foreach (var (key, value) in saved)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}

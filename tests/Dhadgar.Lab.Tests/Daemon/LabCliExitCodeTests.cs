using Dhadgar.Lab.Cli;

namespace Dhadgar.Lab.Tests.Daemon;

/// <summary>
/// Exit codes of <c>meridian-lab daemon run</c>: 2 for config refusals, 3 for a second instance. Every case here is a refusal,
/// so no daemon is ever started and no port is bound.
/// </summary>
/// <remarks>
/// These tests set process environment variables, so they run one at a time within this class.
/// </remarks>
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

    private static string ValidToken() => System.Buffers.Text.Base64Url.EncodeToString(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes));

    private static async Task<int> RunWithEnvironmentAsync(IReadOnlyDictionary<string, string?> values, string[] args)
    {
        var saved = new Dictionary<string, string?>();
        foreach (var key in values.Keys)
        {
            saved[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, values[key]);
        }

        try
        {
            return await LabCli.RunAsync(args);
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

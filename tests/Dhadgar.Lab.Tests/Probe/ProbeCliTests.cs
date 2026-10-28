using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;
using Dhadgar.Contracts.Agents;
using Dhadgar.Lab.Probe;

namespace Dhadgar.Lab.Tests.Probe;

/// <summary>
/// The probe's command line: usage and unknown-mode errors exit 2; <c>footprint --json</c> prints a
/// <see cref="ProbeFootprint"/> that deserialises back into the Contracts type.
/// </summary>
public sealed class ProbeCliTests
{
    private static (int Code, string Stdout, string Stderr) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int code = ProbeCli.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void NoArguments_ExitsTwoWithUsage()
    {
        var (code, stdout, stderr) = Run();

        Assert.Equal(2, code);
        Assert.Empty(stdout);
        Assert.Contains("usage:", stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("token-write")]
    [InlineData("corrupt")]
    public void UnknownMode_ExitsTwo(string mode)
    {
        var (code, stdout, stderr) = Run(mode);

        Assert.Equal(2, code);
        Assert.Empty(stdout);
        Assert.Equal("unknown mode", stderr.Trim());
    }

    [Theory]
    [InlineData("footprint")]
    [InlineData("footprint", "--text")]
    [InlineData("footprint", "--json", "extra")]
    public void FootprintWithWrongArguments_ExitsTwo(params string[] args)
    {
        var (code, stdout, stderr) = Run(args);

        Assert.Equal(2, code);
        Assert.Empty(stdout);
        Assert.Contains("usage:", stderr, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Footprint_OffWindows_ExitsTwoWithOneLine()
    {
        Skip.If(OperatingSystem.IsWindows(), "runs on non-Windows hosts only");

        var (code, stdout, stderr) = Run("footprint", "--json");

        Assert.Equal(2, code);
        Assert.Empty(stdout);
        Assert.Equal("footprint is Windows-only", stderr.Trim());
    }

    [SupportedOSPlatform("windows")]
    [SkippableFact]
    public void Footprint_Json_DeserialisesAsProbeFootprint()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "footprint reads the Windows host");

        var (code, stdout, stderr) = Run("footprint", "--json");

        Assert.True(code == 0, stderr);
        var footprint = JsonSerializer.Deserialize<ProbeFootprint>(stdout);
        Assert.NotNull(footprint);
        Assert.NotNull(footprint.Registry);
        Assert.NotNull(footprint.MachineMy);
    }
}

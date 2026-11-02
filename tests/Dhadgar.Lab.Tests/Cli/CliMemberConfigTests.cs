using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Tools.Cli;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Lab.Tests.Cli;

/// <summary>
/// L37 member install and config tests. No Docker: <see cref="FakeDocker"/> records each <c>docker</c> call and reads the
/// staged file at the <c>cp</c> step. Every credential here is a fake value.
/// </summary>
public sealed class CliMemberConfigTests
{
    private const string FakeAdminKey = "fake-admin-key-for-tests";
    private const string Container = "lab-member-linux-1";
    private static readonly PathValidator Validator = new(NullLogger<PathValidator>.Instance);

    [Fact]
    public void Launcher_ExecsTheDllUnderDotnet()
    {
        Assert.Equal("#!/bin/sh\nexec dotnet /opt/lab/cli/dhadgar.dll \"$@\"\n", LabBuilder.CliLauncherText);
    }

    [Fact]
    public void InstallArgs_AreOneItemPerArgument()
    {
        Assert.Equal(["exec", "-u", "root", Container, "mkdir", "-p", "/opt/lab/cli"], CliMemberInstall.MkdirArgs(Container));
        Assert.Equal(["cp", Path.Combine("C:\\builds\\cli", "."), Container + ":/opt/lab/cli/"], CliMemberInstall.CopyArgs(Container, "C:\\builds\\cli"));
        Assert.Equal(["exec", "-u", "root", Container, "chmod", "755", "/opt/lab/cli/dhadgar"], CliMemberInstall.ChmodArgs(Container));
    }

    [Fact]
    public async Task InstallAsync_RunsMkdirCopyChmod_InOrder()
    {
        var docker = new FakeDocker();

        Result result = await CliMemberInstall.InstallAsync(docker, Container, "C:\\builds\\cli", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, docker.Calls.Count);
        Assert.Equal("mkdir", docker.Calls[0][4]);
        Assert.Equal("cp", docker.Calls[1][0]);
        Assert.Equal("chmod", docker.Calls[2][4]);
    }

    [Fact]
    public void RenderMemberConfig_WithoutKey_HoldsOnlyTheGatewayUrl()
    {
        using JsonDocument doc = JsonDocument.Parse(CliMemberInstall.RenderMemberConfig(null));

        Assert.Equal("http://meridian-lab-gateway:8080", doc.RootElement.GetProperty("gateway_url").GetString());
        Assert.Equal("http://meridian-lab-notifications:8080", doc.RootElement.GetProperty("notifications_url").GetString());
        Assert.False(doc.RootElement.TryGetProperty("admin_api_key", out _));
    }

    [Fact]
    public void RenderMemberConfig_NeverWritesClientCredentials()
    {
        string json = CliMemberInstall.RenderMemberConfig(FakeAdminKey);

        Assert.DoesNotContain("client_secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("client_id", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderMemberConfig_WithKey_CarriesTheKeyNameTheCliReads()
    {
        using JsonDocument doc = JsonDocument.Parse(CliMemberInstall.RenderMemberConfig(FakeAdminKey));

        Assert.Equal(FakeAdminKey, doc.RootElement.GetProperty("admin_api_key").GetString());
    }

    [Fact]
    public void ConfigSteps_TargetTheMemberHomeAndNeverCarryTheKey()
    {
        IReadOnlyList<IReadOnlyList<string>> steps = CliMemberInstall.ConfigSteps(Container, "C:\\state\\cli-config-x.json");

        Assert.Equal(4, steps.Count);
        Assert.Equal(["exec", "-u", "root", Container, "mkdir", "-p", "/home/lab/.dhadgar"], steps[0]);
        Assert.Equal(["cp", "C:\\state\\cli-config-x.json", Container + ":/home/lab/.dhadgar/config.json"], steps[1]);
        Assert.Equal("600", steps[3][5]);
        foreach (IReadOnlyList<string> step in steps)
        {
            Assert.DoesNotContain(step, item => item.Contains(FakeAdminKey, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task InstallConfigAsync_StagesCopiesAndDeletesTheFile()
    {
        string staging = NewStagingDirectory();
        try
        {
            var docker = new FakeDocker();

            Result result = await CliMemberInstall.InstallConfigAsync(docker, Container, staging, FakeAdminKey, Validator, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(4, docker.Calls.Count);
            Assert.NotNull(docker.CopiedContent);
            Assert.Contains(FakeAdminKey, docker.CopiedContent!, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(staging));
            Assert.DoesNotContain(docker.Calls.SelectMany(c => c), item => item.Contains(FakeAdminKey, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public async Task InstallConfigAsync_NonZeroExit_FailsWithoutLeavingTheStagedFile()
    {
        string staging = NewStagingDirectory();
        try
        {
            var docker = new FakeDocker { ExitCode = 1 };

            Result result = await CliMemberInstall.InstallConfigAsync(docker, Container, staging, FakeAdminKey, Validator, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Contains("[Lab.Docker]", result.Error, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(staging));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public async Task InstallConfigAsync_UnwritableStagingDirectory_ReturnsIoFailure()
    {
        // A file where the staging folder should be: the create fails with an IO error, which becomes a Result.
        string blocker = Path.Combine(Path.GetTempPath(), "l37-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "not a directory");
        try
        {
            var docker = new FakeDocker();

            Result result = await CliMemberInstall.InstallConfigAsync(docker, Container, Path.Combine(blocker, "sub"), null, Validator, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Contains("[Lab.Io]", result.Error, StringComparison.Ordinal);
            Assert.Empty(docker.Calls);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    private static string NewStagingDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "l37-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Records each call; reads the staged file at the <c>cp</c> step; answers with a fixed exit code.</summary>
    private sealed class FakeDocker : IDockerCli
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public int ExitCode { get; init; }

        public string? CopiedContent { get; private set; }

        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls.Add(args.ToArray());
            if (args[0] == "cp" && File.Exists(args[1]))
            {
                CopiedContent = File.ReadAllText(args[1]);
            }

            return Task.FromResult(Result<DockerOutput>.Success(new DockerOutput(ExitCode, string.Empty, string.Empty)));
        }
    }
}

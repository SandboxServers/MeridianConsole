using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Stack;
using Dhadgar.Lab.Tests.Pool;
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
    public async Task PublishCli_WithoutTheCliProject_FailsWithALabError()
    {
        string root = Path.Combine(Path.GetTempPath(), "lab-n3-" + Guid.NewGuid().ToString("N"));
        LabPaths paths = LabPaths.FromRoot(root);
        string checkout = Path.Combine(root, "repo");
        string outputRoot = Path.Combine(paths.Builds, "0123abcd");

        Result result = await LabBuilder.PublishCliAsync(outputRoot, checkout, paths, Validator, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(outputRoot, "cli", LabBuilder.CliLauncherName)));
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
    public void RenderMemberConfig_WithoutKey_HoldsTheServiceUrlsOnly()
    {
        using JsonDocument doc = JsonDocument.Parse(CliMemberInstall.RenderMemberConfig(null));

        Assert.Equal(CliMemberInstall.MemberGatewayUrl, doc.RootElement.GetProperty("gateway_url").GetString());
        Assert.Equal(CliMemberInstall.MemberIdentityUrl, doc.RootElement.GetProperty("identity_url").GetString());
        Assert.Equal(CliMemberInstall.MemberNodesUrl, doc.RootElement.GetProperty("nodes_url").GetString());
        Assert.Equal(CliMemberInstall.MemberNotificationsUrl, doc.RootElement.GetProperty("notifications_url").GetString());
        Assert.Equal(CliMemberInstall.MemberDiscordUrl, doc.RootElement.GetProperty("discord_url").GetString());
        Assert.False(doc.RootElement.TryGetProperty("admin_api_key", out _));
    }

    [Fact]
    public void AdminCredentialFor_MapsOnlyTheAdminVerbs()
    {
        Assert.Equal(LabCredentialRefs.NotificationsAdmin, CliMemberInstall.AdminCredentialFor("notifications"));
        Assert.Equal(LabCredentialRefs.DiscordAdmin, CliMemberInstall.AdminCredentialFor("discord"));
        Assert.Null(CliMemberInstall.AdminCredentialFor("nodes"));
    }

    [Fact]
    public void CliPublishArgs_SkipsGlobalToolInstall_AndHasNoRid()
    {
        IReadOnlyList<string> args = LabBuilder.CliPublishArgs("C:\\repo\\cli.csproj", "C:\\out");

        Assert.Equal(["publish", "C:\\repo\\cli.csproj", "-c", "Release", "--no-restore", "-p:SkipGlobalToolInstall=true", "-o", "C:\\out"], args);
        Assert.DoesNotContain(args, item => item.StartsWith("-r", StringComparison.Ordinal) || item.Contains("RuntimeIdentifier", StringComparison.Ordinal));
    }

    [Fact]
    public void LatestCliDirectory_ReturnsTheCliFolder()
    {
        string root = NewStagingDirectory();
        try
        {
            LabPaths paths = LabPaths.FromRoot(root, Path.Combine(root, "repo"));
            WriteLatestBuild(paths, withCli: true);

            string? dir = CliMemberInstall.LatestCliDirectory(paths, Validator);

            Assert.NotNull(dir);
            Assert.Equal(Path.GetFullPath(Path.Combine(paths.Builds, BuildId, "cli")), Path.GetFullPath(dir!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LatestCliDirectory_WithoutCliFolder_IsNull()
    {
        string root = NewStagingDirectory();
        try
        {
            LabPaths paths = LabPaths.FromRoot(root, Path.Combine(root, "repo"));
            WriteLatestBuild(paths, withCli: false);

            Assert.Null(CliMemberInstall.LatestCliDirectory(paths, Validator));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SweepStale_RemovesOnlyOldConfigFiles()
    {
        string dir = NewStagingDirectory();
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string old = Path.Combine(dir, "cli-config-a.json");
            string fresh = Path.Combine(dir, "cli-config-b.json");
            string other = Path.Combine(dir, "other.json");
            File.WriteAllText(old, "{}");
            File.SetLastWriteTimeUtc(old, now.AddMinutes(-10).UtcDateTime);
            File.WriteAllText(fresh, "{}");
            File.WriteAllText(other, "{}");
            File.SetLastWriteTimeUtc(other, now.AddMinutes(-10).UtcDateTime);

            int removed = CliMemberInstall.SweepStale(dir, now, TimeSpan.FromMinutes(5));

            Assert.Equal(1, removed);
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(fresh));
            Assert.True(File.Exists(other));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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

    [Fact]
    public async Task InstallConfigAsync_WithKey_StagesUnderTheStagingFolder_AndNoArgCarriesTheKey()
    {
        string root = NewStagingDirectory();
        try
        {
            LabPaths paths = LabPaths.FromRoot(root, Path.Combine(root, "repo"));
            string staging = CliMemberInstall.StagingDirectory(paths);
            var staged = new List<(string Path, string Text)>();
            var docker = new FakeDockerCli
            {
                OnCall = call =>
                {
                    if (call[0] == "cp" && File.Exists(call[1]))
                    {
                        staged.Add((call[1], File.ReadAllText(call[1])));
                    }
                },
            };

            Result result = await CliMemberInstall.InstallConfigAsync(docker, Container, staging, FakeAdminKey, Validator, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Single(staged);
            Assert.Contains(FakeAdminKey, staged[0].Text, StringComparison.Ordinal);
            Assert.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, Path.GetFullPath(staged[0].Path), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(docker.Calls.SelectMany(c => c), item => item.Contains(FakeAdminKey, StringComparison.Ordinal));
            Assert.Empty(Directory.GetFiles(staging));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const string BuildId = "0123abcd";

    /// <summary>Writes a latest-build stamp (as <c>LastBuildReaderTests</c> does), with or without a <c>cli</c> folder.</summary>
    private static void WriteLatestBuild(LabPaths paths, bool withCli)
    {
        string dir = Path.Combine(paths.Builds, BuildId);
        _ = Directory.CreateDirectory(dir);
        if (withCli)
        {
            _ = Directory.CreateDirectory(Path.Combine(dir, "cli"));
        }

        var json = new JsonObject
        {
            ["build_id"] = BuildId,
            ["commit"] = "0123abcd4567ef89",
            ["dirty"] = true,
            ["checkout"] = Path.Combine(paths.Builds, "..", "repo", "src", "lab"),
        };
        File.WriteAllText(Path.Combine(dir, "build.json"), json.ToJsonString());
        File.WriteAllText(Path.Combine(paths.Builds, "latest.txt"), BuildId);
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

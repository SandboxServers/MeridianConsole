using System.Text.Json;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Contracts.Agents;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Identity;

public sealed class FileAgentStateStoreTests : IDisposable
{
    private static readonly DateTimeOffset EnrolledAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dhadgar-state-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsTheIdentity()
    {
        var sut = CreateStore(_directory);
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), EnrolledAt);

        var save = await sut.SaveAsync(identity);
        var load = await sut.LoadAsync();

        Assert.True(save.IsSuccess, save.Error);
        Assert.True(load.IsSuccess, load.Error);
        Assert.Equal(identity, load.Value!.Identity);
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsNone()
    {
        Directory.CreateDirectory(_directory);
        var sut = CreateStore(_directory);

        var load = await sut.LoadAsync();

        Assert.True(load.IsSuccess, load.Error);
        Assert.Null(load.Value!.Identity);
    }

    [Fact]
    public async Task Load_MissingDirectory_ReturnsNone()
    {
        var sut = CreateStore(_directory);

        var load = await sut.LoadAsync();

        Assert.True(load.IsSuccess, load.Error);
        Assert.Null(load.Value!.Identity);
    }

    [Fact]
    public async Task Load_StatePathIsDirectory_ReturnsIoFailure()
    {
        Directory.CreateDirectory(StatePath());
        var sut = CreateStore(_directory);

        var load = await sut.LoadAsync();

        Assert.True(load.IsFailure);
        Assert.Contains("[AgentState.Io]", load.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_StaleTempFile_IsReplacedNotAdopted()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(StatePath() + ".tmp", "planted");
        var sut = CreateStore(_directory);
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), EnrolledAt);

        var save = await sut.SaveAsync(identity);
        var load = await sut.LoadAsync();

        Assert.True(save.IsSuccess, save.Error);
        Assert.False(File.Exists(StatePath() + ".tmp"));
        Assert.Equal(identity, load.Value!.Identity);
    }

    [Fact]
    public async Task Save_TempPathIsDirectory_FailsWithResult()
    {
        Directory.CreateDirectory(StatePath() + ".tmp");
        var sut = CreateStore(_directory);

        var save = await sut.SaveAsync(new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), EnrolledAt));

        Assert.True(save.IsFailure);
        Assert.Contains("[AgentState.Io]", save.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(StatePath()));
    }

    [Theory]
    [InlineData(@"\foo")]
    [InlineData("C:foo")]
    [InlineData(@"\\server\share\x")]
    [InlineData(@"\\?\C:\x")]
    [InlineData("//server/share/x")]
    [InlineData("relative/dir")]
    public async Task Load_StateDirectoryNotFullyQualifiedLocal_ReturnsConfigFailure(string directory)
    {
        var load = await CreateStore(directory).LoadAsync();

        Assert.True(load.IsFailure);
        Assert.Contains("[AgentState.Config]", load.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_OnUnix_FileIs0600AndDirectory0700()
    {
        if (OperatingSystem.IsWindows()) return; // Unix-only: Windows file protection is WindowsStateFileSecurity (P27f)

        var sut = CreateStore(_directory);

        var save = await sut.SaveAsync(new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), EnrolledAt));

        Assert.True(save.IsSuccess, save.Error);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(StatePath()));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(_directory));
    }

    [Fact]
    public async Task Load_OnUnix_GroupWritableFile_ReturnsUntrusted()
    {
        if (OperatingSystem.IsWindows()) return; // Unix-only: Windows file protection is WindowsStateFileSecurity (P27f)

        var sut = CreateStore(_directory);
        await sut.SaveAsync(new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), EnrolledAt));
        File.SetUnixFileMode(StatePath(), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite);

        var load = await sut.LoadAsync();

        Assert.True(load.IsFailure);
        Assert.Contains("[AgentState.Untrusted]", load.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_CorruptJson_ReturnsCorruptFailure()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(StatePath(), "{ not json");
        var sut = CreateStore(_directory);

        var load = await sut.LoadAsync();

        Assert.True(load.IsFailure);
        Assert.Contains("[AgentState.Corrupt]", load.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_EmptyNodeId_ReturnsCorruptFailure()
    {
        Directory.CreateDirectory(_directory);
        var raw = JsonSerializer.Serialize(
            new AgentIdentity(Guid.Empty, Guid.NewGuid(), EnrolledAt),
            JsonSerializerOptions.Web);
        await File.WriteAllTextAsync(StatePath(), raw);
        var sut = CreateStore(_directory);

        var load = await sut.LoadAsync();

        Assert.True(load.IsFailure);
        Assert.Contains("[AgentState.Corrupt]", load.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsTheSigningKeyFields()
    {
        var sut = CreateStore(_directory);
        var identity = new AgentIdentity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EnrolledAt,
            CommandSigningPublicKey: "c2lnbmluZy1rZXk=",
            CommandSigningKeyId: new string('a', AgentCommandSigning.KeyIdLength));

        var save = await sut.SaveAsync(identity);
        var load = await sut.LoadAsync();

        Assert.True(save.IsSuccess, save.Error);
        Assert.True(load.IsSuccess, load.Error);
        Assert.Equal(identity, load.Value!.Identity);
    }

    [Fact]
    public async Task Load_Sprint2FileWithoutSigningKey_LoadsWithNullKeyFields()
    {
        Directory.CreateDirectory(_directory);
        var nodeId = Guid.NewGuid();
        await File.WriteAllTextAsync(
            StatePath(),
            $"{{\"nodeId\":\"{nodeId}\",\"organizationId\":\"{Guid.NewGuid()}\",\"enrolledAt\":\"{EnrolledAt:O}\"}}");
        var sut = CreateStore(_directory);

        var load = await sut.LoadAsync();

        Assert.True(load.IsSuccess, load.Error);
        var identity = load.Value!.Identity!;
        Assert.Equal(nodeId, identity.NodeId);
        Assert.Null(identity.CommandSigningPublicKey);
        Assert.Null(identity.CommandSigningKeyId);
    }

    [Fact]
    public async Task Load_OnlyOneSigningKeyField_ReturnsCorruptFailure()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            StatePath(),
            $"{{\"nodeId\":\"{Guid.NewGuid()}\",\"organizationId\":\"{Guid.NewGuid()}\",\"enrolledAt\":\"{EnrolledAt:O}\",\"commandSigningPublicKey\":\"c2lnbmluZy1rZXk=\"}}");
        var sut = CreateStore(_directory);

        var load = await sut.LoadAsync();

        Assert.True(load.IsFailure);
        Assert.Contains("[AgentState.Corrupt]", load.Error, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Save_LeavesNoTempFile()
    {
        var sut = CreateStore(_directory);

        var save = await sut.SaveAsync(new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), EnrolledAt));

        Assert.True(save.IsSuccess, save.Error);
        Assert.True(File.Exists(StatePath()));
        Assert.False(File.Exists(StatePath() + ".tmp"));
    }

    [Fact]
    public async Task Save_CreatesMissingDirectory()
    {
        var sut = CreateStore(_directory);
        Assert.False(Directory.Exists(_directory));

        var save = await sut.SaveAsync(new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), EnrolledAt));

        Assert.True(save.IsSuccess, save.Error);
        Assert.True(Directory.Exists(_directory));
    }

    [Fact]
    public async Task Load_StateDirectoryWithParentSegments_ReturnsFailure()
    {
        var sut = CreateStore(Path.Combine(_directory, "..", "escape"));

        var load = await sut.LoadAsync();

        Assert.True(load.IsFailure);
        Assert.Contains("'..'", load.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_ValidatorRejection_ReturnsFailure()
    {
        var sut = new FileAgentStateStore(
            Options.Create(new AgentOptions { StateDirectory = _directory }),
            new RejectingPathValidator(),
            new PortableStateFileSecurity(),
            NullLogger<FileAgentStateStore>.Instance);

        var load = await sut.LoadAsync();

        Assert.True(load.IsFailure);
        Assert.Equal("path rejected by test validator", load.Error);
    }

    private string StatePath() => Path.Combine(_directory, FileAgentStateStore.FileName);

    private static FileAgentStateStore CreateStore(string stateDirectory) => new(
        Options.Create(new AgentOptions { StateDirectory = stateDirectory }),
        new PathValidator(NullLogger<PathValidator>.Instance),
        new PortableStateFileSecurity(),
        NullLogger<FileAgentStateStore>.Instance);

    private sealed class RejectingPathValidator : IPathValidator
    {
        public Result<string> ValidatePath(string path, IEnumerable<string> allowedBasePaths) =>
            Result<string>.Failure("path rejected by test validator");

        public bool IsSafePath(string path) => false;

        public Result<string> NormalizePath(string path) =>
            Result<string>.Failure("path rejected by test validator");
    }
}

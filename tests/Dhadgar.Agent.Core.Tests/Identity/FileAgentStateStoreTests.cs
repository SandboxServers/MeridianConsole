using System.Text.Json;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Agent.Core.Identity;
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
        Assert.Equal(identity, load.Value);
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsMissingFailure()
    {
        var sut = CreateStore(_directory);

        var load = await sut.LoadAsync();

        Assert.True(load.IsFailure);
        Assert.Contains("[AgentState.Missing]", load.Error, StringComparison.Ordinal);
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
            NullLogger<FileAgentStateStore>.Instance);

        var load = await sut.LoadAsync();

        Assert.True(load.IsFailure);
        Assert.Equal("path rejected by test validator", load.Error);
    }

    private string StatePath() => Path.Combine(_directory, FileAgentStateStore.FileName);

    private static FileAgentStateStore CreateStore(string stateDirectory) => new(
        Options.Create(new AgentOptions { StateDirectory = stateDirectory }),
        new PathValidator(NullLogger<PathValidator>.Instance),
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

using Dhadgar.Agent.Core.Identity;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Identity;

public sealed class PortableStateFileSecurityTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dhadgar-sfs-" + Guid.NewGuid().ToString("N"));

    public PortableStateFileSecurityTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateNew_ExistingFile_Fails()
    {
        var path = Path.Combine(_directory, "existing.json");
        await File.WriteAllTextAsync(path, "planted");

        var result = new PortableStateFileSecurity().CreateNew(path);

        Assert.True(result.IsFailure);
        Assert.Contains("[AgentState.Io]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateNew_NewFile_ReturnsWritableStream()
    {
        var path = Path.Combine(_directory, "new.json");

        var result = new PortableStateFileSecurity().CreateNew(path);

        Assert.True(result.IsSuccess, result.Error);
        await using (var stream = result.Value!)
        {
            await stream.WriteAsync(new byte[] { 1 });
        }

        Assert.True(File.Exists(path));
        Assert.Equal(1, new FileInfo(path).Length);
    }
}

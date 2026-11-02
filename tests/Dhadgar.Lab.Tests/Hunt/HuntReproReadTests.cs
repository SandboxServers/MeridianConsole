using Dhadgar.Lab.Tools.Hunt;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Lab.Tests.Hunt;

/// <summary>The capped, caught repro read of the replay path (H2).</summary>
public sealed class HuntReproReadTests
{
    [Fact]
    public async Task ReadReproAsync_LargerThanCap_IsInvalid()
    {
        string folder = NewFolder();
        try
        {
            string path = Path.Combine(folder, "big.toml");
            await File.WriteAllBytesAsync(path, new byte[HuntModelTool.MaxReproBytes + 1], CancellationToken.None);

            Result<string> read = await HuntModelTool.ReadReproAsync(path, CancellationToken.None);

            Assert.True(read.IsFailure);
            Assert.StartsWith("[Lab.InvalidArgument]", read.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ReadReproAsync_Missing_IsNotFound()
    {
        string folder = NewFolder();
        try
        {
            Result<string> read = await HuntModelTool.ReadReproAsync(Path.Combine(folder, "absent.toml"), CancellationToken.None);

            Assert.True(read.IsFailure);
            Assert.StartsWith("[Lab.NotFound]", read.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ReadReproAsync_SmallFile_ReturnsText()
    {
        string folder = NewFolder();
        try
        {
            string path = Path.Combine(folder, "small.toml");
            await File.WriteAllTextAsync(path, "hello repro", CancellationToken.None);

            Result<string> read = await HuntModelTool.ReadReproAsync(path, CancellationToken.None);

            Assert.True(read.IsSuccess, read.Error);
            Assert.Equal("hello repro", read.Value);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "lab-repro-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}

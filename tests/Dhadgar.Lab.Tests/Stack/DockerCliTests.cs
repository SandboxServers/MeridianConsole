using Dhadgar.Lab.Docker;

namespace Dhadgar.Lab.Tests.Stack;

public sealed class DockerCliTests
{
    [Theory]
    [InlineData("desktop linux")]
    [InlineData("-H tcp://evil")]
    [InlineData("ctx;rm")]
    public async Task RunAsync_InvalidContext_ReturnsFailureWithoutStartingDocker(string context)
    {
        var cli = new DockerCli(new DockerOptions(context, null));

        var result = await cli.RunAsync(DockerEngine.Linux, ["ps"], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Docker]", result.Error, StringComparison.Ordinal);
    }
}

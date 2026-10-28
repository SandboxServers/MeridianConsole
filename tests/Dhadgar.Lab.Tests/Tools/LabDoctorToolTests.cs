using System.Text.Json;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Tools;

/// <summary>
/// lab_doctor: the Docker check runs on the Linux engine with explicit version arguments, through <see cref="IDockerCli"/>.
/// </summary>
public sealed class LabDoctorToolTests
{
    private static readonly LabCallContext Context = new("call-test", null, "test");

    [Fact]
    public async Task Docker_UsesLinuxEngineAndVersionArgs()
    {
        using var vault = new SecretVault();
        var docker = new RecordingDocker();
        var tool = new LabDoctorTool(new Redactor(vault), docker);

        Result<JsonElement> result = await tool.InvokeAsync(default, Context, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEmpty(docker.Calls);
        Assert.Equal(DockerEngine.Linux, docker.Calls[0].Engine);
        Assert.Equal(new[] { "version", "--format", "{{.Server.Os}}" }, docker.Calls[0].Args);
    }

    private sealed class RecordingDocker : IDockerCli
    {
        public List<(DockerEngine Engine, string[] Args)> Calls { get; } = [];

        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls.Add((engine, args.ToArray()));
            return Task.FromResult(Result<DockerOutput>.Success(new DockerOutput(0, "linux\n", string.Empty)));
        }
    }
}

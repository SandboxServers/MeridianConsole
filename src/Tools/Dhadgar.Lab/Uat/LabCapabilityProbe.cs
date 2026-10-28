using Dhadgar.Lab.Docker;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;

namespace Dhadgar.Lab.Uat;

/// <summary>
/// The host's capabilities for a section's <c>requires</c> list. <c>docker</c> asks the Linux engine for its info;
/// <c>windows</c> reads the OS; <c>windows_containers</c> answers false until L31 wires the Windows engine;
/// <c>network</c> is true unless <c>Lab:Offline</c> is set. A failed check answers false and never throws.
/// </summary>
public sealed class LabCapabilityProbe : ICapabilityProbe
{
    private readonly IDockerCli _docker;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Creates the probe.
    /// </summary>
    public LabCapabilityProbe(IDockerCli docker, IConfiguration configuration)
    {
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(string capability, CancellationToken ct)
    {
        switch (capability)
        {
            case "docker":
                return await DockerAnswersAsync(ct).ConfigureAwait(false);
            case "windows":
                return OperatingSystem.IsWindows();
            case "windows_containers":
                return false;
            case "network":
                return !_configuration.GetValue<bool>("Lab:Offline", false);
            default:
                return false;
        }
    }

    private async Task<bool> DockerAnswersAsync(CancellationToken ct)
    {
        Result<DockerOutput> output = await _docker
            .RunAsync(DockerEngine.Linux, new[] { "info", "--format", "{{.ServerVersion}}" }, ct)
            .ConfigureAwait(false);
        return output.IsSuccess && output.Value!.ExitCode == 0;
    }
}

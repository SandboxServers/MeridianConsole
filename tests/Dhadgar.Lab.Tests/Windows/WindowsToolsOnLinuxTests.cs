using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Windows;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Windows;

/// <summary>
/// The Windows host reads refuse members in L12 and report <c>[Lab.Unavailable]</c> off Windows.
/// </summary>
public sealed class WindowsToolsOnLinuxTests
{
    private static readonly LabCallContext Context = new("call-1", null, "mcp");

    [Theory]
    [InlineData("certstore_list", """{"store":"machine_my","member":"vm-1"}""")]
    [InlineData("registry_read", """{"member":"vm-1"}""")]
    [InlineData("state_file_read", """{"member":"vm-1"}""")]
    public async Task Member_IsRefused_OnAnyPlatform(string tool, string json)
    {
        using var vault = new SecretVault();
        using var document = JsonDocument.Parse(json);

        var result = await InvokeAsync(tool, document.RootElement.Clone(), vault);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.Unavailable] members arrive in L32", result.Error);
    }

    [SkippableTheory]
    [InlineData("certstore_list", """{"store":"machine_my"}""")]
    [InlineData("registry_read", """{}""")]
    [InlineData("state_file_read", """{}""")]
    public async Task HostRead_OffWindows_IsUnavailable(string tool, string json)
    {
        Skip.If(OperatingSystem.IsWindows(), "runs on non-Windows hosts only");

        using var vault = new SecretVault();
        using var document = JsonDocument.Parse(json);

        var result = await InvokeAsync(tool, document.RootElement.Clone(), vault);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Unavailable]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CertStoreList_UnknownStore_IsInvalidArgument()
    {
        using var vault = new SecretVault();
        using var document = JsonDocument.Parse("""{"store":"not_a_store"}""");

        var result = await InvokeAsync("certstore_list", document.RootElement.Clone(), vault);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.", result.Error, StringComparison.Ordinal);
    }

    private static Task<Result<JsonElement>> InvokeAsync(string tool, JsonElement args, SecretVault vault)
    {
        var redactor = new Redactor(vault);
        ILabTool target = tool switch
        {
            "certstore_list" => new CertStoreListTool(),
            "registry_read" => new RegistryReadTool(),
            _ => new StateFileReadTool(new AgentStateReader(new PathValidator(NullLogger<PathValidator>.Instance), redactor, Path.GetTempPath())),
        };

        return target.InvokeAsync(args, Context, CancellationToken.None);
    }
}

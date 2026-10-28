using System.IO;
using System.Text;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Contracts.Agents;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Windows;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Windows;

/// <summary>
/// The host state-file read: blanking of sensitive properties, redaction of vault values, refusal of oversized
/// and invalid files. Runs against a temporary root, so it is OS-neutral.
/// </summary>
public sealed class AgentStateReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-state-" + Guid.NewGuid().ToString("N"));
    private readonly SecretVault _vault = new();

    public void Dispose()
    {
        _vault.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string StateFile => Path.Combine(_root, AgentWindowsFootprint.StateDirectoryRelative, AgentWindowsFootprint.StateFileName);

    private AgentStateReader CreateReader(IPathValidator? validator = null) =>
        new(validator ?? new PathValidator(NullLogger<PathValidator>.Instance), new Redactor(_vault), _root);

    private void WriteState(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
        File.WriteAllText(StateFile, content, Encoding.UTF8);
    }

    [Fact]
    public void Read_MissingFile_ReturnsNotExists()
    {
        var result = CreateReader().Read();

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Exists);
        Assert.Null(result.Value.Content);
    }

    [Fact]
    public void Read_BlanksSensitiveProperties_AndKeepsOthers()
    {
        WriteState("""{"agentId":"agent-1","enrollmentToken":"tok-value-123456","apiKey":"key-value-123456","clientSecret":"sec-value-123456","nested":{"accessToken":"nested-token-value"}}""");

        var result = CreateReader().Read();

        Assert.True(result.IsSuccess);
        var content = result.Value!.Content!.Value;
        Assert.Equal("agent-1", content.GetProperty("agentId").GetString());
        Assert.Equal(JsonValueKind.Null, content.GetProperty("enrollmentToken").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.GetProperty("apiKey").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.GetProperty("clientSecret").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.GetProperty("nested").GetProperty("accessToken").ValueKind);
        Assert.DoesNotContain("tok-value-123456", content.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("nested-token-value", content.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RedactsVaultSecretsInOtherValues()
    {
        const string secret = "vaulted-password-value";
        _vault.Add(SecretKind.Password, secret.AsSpan());
        WriteState("""{"note":"connected with vaulted-password-value"}""");

        var result = CreateReader().Read();

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(secret, result.Value!.Content!.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Read_InvalidJson_FailsWithoutEchoingContent()
    {
        WriteState("not-json secret-looking-text-xyz");

        var result = CreateReader().Read();

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Unavailable]", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-looking-text-xyz", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_OversizedFile_IsRefused()
    {
        WriteState(new string('a', (int)AgentStateReader.MaxStateBytes + 1));

        var result = CreateReader().Read();

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Unavailable]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ValidatorRefusal_IsInvalidArgument()
    {
        WriteState("""{"agentId":"agent-1"}""");

        var result = CreateReader(new RefusingValidator()).Read();

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    private sealed class RefusingValidator : IPathValidator
    {
        public Result<string> ValidatePath(string path, IEnumerable<string> allowedBasePaths) =>
            Result<string>.Failure("outside the allowed base");

        public bool IsSafePath(string path) => false;

        public Result<string> NormalizePath(string path) => Result<string>.Failure("refused");
    }
}

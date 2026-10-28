using Dhadgar.Lab.Nodes;
using Dhadgar.Lab.Secrets;

namespace Dhadgar.Lab.Tests.Nodes;

/// <summary>
/// The hostname and key_ref arguments of <c>nodes_enroll_probe</c> (L51).
/// </summary>
public sealed class NodesEnrollProbeTests
{
    [Theory]
    [InlineData("Upper")]
    [InlineData("has space")]
    [InlineData("under_score")]
    [InlineData("")]
    public void Hostname_Invalid_Refused(string hostname)
    {
        using var vault = new SecretVault();
        string token = vault.Add(SecretKind.BearerToken, "token-value".AsSpan());

        if (hostname.Length == 0)
        {
            Assert.Throws<ArgumentException>(() => EnrollProbe.BuildRequest(vault, token, "linux", hostname, null, out _));
            return;
        }

        var result = EnrollProbe.BuildRequest(vault, token, "linux", hostname, null, out _);
        Assert.True(result.IsFailure);
        Assert.Contains("hostname", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Hostname_TooLong_Refused()
    {
        using var vault = new SecretVault();
        string token = vault.Add(SecretKind.BearerToken, "token-value".AsSpan());
        var result = EnrollProbe.BuildRequest(vault, token, "linux", new string('a', 49), null, out _);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void KeyRef_Reused_GivesTheSamePublicKey()
    {
        using var vault = new SecretVault();
        string token = vault.Add(SecretKind.BearerToken, "token-value".AsSpan());
        var first = EnrollProbe.BuildRequest(vault, token, "linux", "lab-one", null, out string keyRef);
        Assert.True(first.IsSuccess, first.Error);
        var second = EnrollProbe.BuildRequest(vault, token, "linux", "lab-one", keyRef, out _);
        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal(first.Value.PublicKey, second.Value.PublicKey);
    }
}

using System.Text.Json;
using Dhadgar.Lab.Secrets;

namespace Dhadgar.Lab.Tests.Secrets;

/// <summary>
/// The redactor masks vault values and the secret patterns in text and JSON.
/// </summary>
public sealed class RedactorTests
{
    [Fact]
    public void Redact_VaultValueInText_ReplacedWithReference()
    {
        using var vault = new SecretVault();
        vault.Add(SecretKind.EnrollmentToken, "s3cret-value-123");
        var redactor = new Redactor(vault);

        var result = redactor.Redact("token is s3cret-value-123 ok");

        Assert.Equal("token is ***tok-1 ok", result);
    }

    [Fact]
    public void Redact_VaultValueShorterThanEightCharacters_IsKept()
    {
        using var vault = new SecretVault();
        vault.Add(SecretKind.Password, "abc1234");
        var redactor = new Redactor(vault);

        Assert.Equal("value abc1234 end", redactor.Redact("value abc1234 end"));
    }

    [Fact]
    public void Redact_OverlappingVaultValues_LongestFirst()
    {
        using var vault = new SecretVault();
        vault.Add(SecretKind.Password, "abcdefgh");
        vault.Add(SecretKind.Password, "abcdefghijkl");
        var redactor = new Redactor(vault);

        var result = redactor.Redact("xx abcdefghijkl yy");

        Assert.Equal("xx ***pwd-2 yy", result);
    }

    [Fact]
    public void Redact_VaultValueInJsonStrings_KeepsShapeAndNonStringValues()
    {
        using var vault = new SecretVault();
        vault.Add(SecretKind.EnrollmentToken, "s3cret-value-123");
        var redactor = new Redactor(vault);
        using var document = JsonDocument.Parse("{\"a\":{\"b\":[\"x s3cret-value-123\"]},\"n\":5,\"t\":true,\"z\":null}");

        var redacted = redactor.Redact(document.RootElement);

        Assert.Equal("{\"a\":{\"b\":[\"x ***tok-1\"]},\"n\":5,\"t\":true,\"z\":null}", redacted.GetRawText());
    }

    [Fact]
    public void Redact_PemPrivateKeyBlock_Masked()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        var input = "before\n-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA\n-----END RSA PRIVATE KEY-----\nafter";

        var result = redactor.Redact(input);

        Assert.Equal("before\n***pem\nafter", result);
    }

    [Fact]
    public void Redact_BearerToken_Masked()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        var result = redactor.Redact("Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123456789");

        Assert.Equal("Authorization: Bearer ***", result);
    }

    [Fact]
    public void Redact_ConnectionStringPassword_Masked()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        var result = redactor.Redact("Server=db01;Database=lab;User Id=lab;Password=hunter2-long;Trusted=no");

        Assert.Equal("Server=db01;Database=lab;User Id=lab;Password=***;Trusted=no", result);
    }

    [Fact]
    public void Redact_PwdSegment_Masked()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        Assert.Equal("Server=x;Pwd=***;", redactor.Redact("Server=x;Pwd=abc123;"));
    }

    [Fact]
    public void Redact_TextWithoutSecrets_ReturnsTextUnchanged()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        Assert.Equal("plain status text", redactor.Redact("plain status text"));
    }
}

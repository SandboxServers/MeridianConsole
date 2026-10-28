using System.Text.Json;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat.Bundle;

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

    [Fact]
    public void Redact_BearerTokenLowerCaseScheme_Masked()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        var result = redactor.Redact("authorization: bearer abcdefghijklmnopqrstuvwxyz0123456789");

        Assert.Equal("authorization: Bearer ***", result);
    }

    [Fact]
    public void Redact_PasswordLowerCaseKey_Masked()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        Assert.Equal("Server=x;password=***;", redactor.Redact("Server=x;password=abc12345;"));
    }

    [Fact]
    public void Redact_PwdUpperCaseKey_Masked()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        Assert.Equal("PWD=***;", redactor.Redact("PWD=x;"));
    }

    [Fact]
    public void Redact_UnterminatedPemBlock_Masked()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        var result = redactor.Redact("x\n-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASC");

        Assert.DoesNotContain("MIIE", result, StringComparison.Ordinal);
        Assert.Equal("x\n***pem", result);
    }

    [Fact]
    public void Redact_TwoPemBlocks_EachStopsAtItsOwnEnd()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        var input = "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----\nmid\n-----BEGIN PRIVATE KEY-----\nBBBB\n-----END PRIVATE KEY-----";

        Assert.Equal("***pem\nmid\n***pem", redactor.Redact(input));
    }

    [Fact]
    public void Redact_VaultValueAsJsonPropertyName_Masked()
    {
        using var vault = new SecretVault();
        vault.Add(SecretKind.EnrollmentToken, "s3cret-value-123");
        var redactor = new Redactor(vault);
        using var document = JsonDocument.Parse("{\"s3cret-value-123\":1,\"Password=hunter2hunter2\":2}");

        var redacted = redactor.Redact(document.RootElement).GetRawText();

        Assert.DoesNotContain("s3cret-value-123", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2hunter2", redacted, StringComparison.Ordinal);
        Assert.Equal("{\"***tok-1\":1,\"Password=***\":2}", redacted);
    }

    [Fact]
    public void Redact_JsonEscapedFormAlone_Masked()
    {
        using var vault = new SecretVault();
        vault.Add(SecretKind.EnrollmentToken, "ab\"cd\\ef");
        var redactor = new Redactor(vault);
        var escaped = "ab\\\"cd\\\\ef";

        var redacted = redactor.Redact("{\"v\":\"" + escaped + "\"}");

        Assert.Equal("{\"v\":\"***tok-1\"}", redacted);
        Assert.DoesNotContain(escaped, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void HasSecret_JsonEscapedFormAlone_Found()
    {
        using var vault = new SecretVault();
        vault.Add(SecretKind.EnrollmentToken, "ab\"cd\\ef");
        var scanner = new SecretScanner(vault);

        Assert.True(scanner.HasSecret("{\"v\":\"ab\\\"cd\\\\ef\"}"));
        Assert.False(scanner.HasSecret("{\"v\":\"abcdef\"}"));
    }

    [Fact]
    public void LowercaseHexEscape_OfAVaultValue_IsRedactedAndScanned()
    {
        using var vault = new SecretVault();
        vault.Add(SecretKind.Password, "sécret-v4lue");
        var redactor = new Redactor(vault);
        var scanner = new SecretScanner(vault);
        string text = "{\"v\":\"s\\u00e9cret-v4lue\"}";

        Assert.True(scanner.HasSecret(text));
        string redacted = redactor.Redact(text);

        Assert.DoesNotContain("\\u00e9cret", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.False(scanner.HasSecret(redacted));
    }

    [Fact]
    public void Redact_WithCount_CountsEachVaultMatchOnce()
    {
        using var vault = new SecretVault();
        vault.Add(SecretKind.EnrollmentToken, "s3cret-value-123");
        var redactor = new Redactor(vault);

        var result = redactor.Redact("a s3cret-value-123 b s3cret-value-123", out var count);

        Assert.Equal("a ***tok-1 b ***tok-1", result);
        Assert.Equal(2, count);
    }

    [Fact]
    public void Redact_WithoutSecrets_CountIsZero()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        var result = redactor.Redact("nothing here", out var count);

        Assert.Equal("nothing here", result);
        Assert.Equal(0, count);
    }
}

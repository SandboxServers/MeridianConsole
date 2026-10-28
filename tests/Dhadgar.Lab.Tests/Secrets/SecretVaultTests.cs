using Dhadgar.Lab.Secrets;

namespace Dhadgar.Lab.Tests.Secrets;

/// <summary>
/// The vault hands out references, reads values without copying them out, and zeroes storage on remove and dispose.
/// </summary>
public sealed class SecretVaultTests
{
    [Fact]
    public void Add_FirstOfEachKind_GetsKindPrefix()
    {
        using var vault = new SecretVault();

        Assert.Equal("tok-1", vault.Add(SecretKind.EnrollmentToken, "enrollment-value"));
        Assert.Equal("key-1", vault.Add(SecretKind.PrivateKey, "private-key-value"));
        Assert.Equal("pwd-1", vault.Add(SecretKind.Password, "password-value"));
        Assert.Equal("brr-1", vault.Add(SecretKind.BearerToken, "bearer-value-1"));
    }

    [Fact]
    public void Add_SecondOfSameKind_CountsPerKind()
    {
        using var vault = new SecretVault();

        vault.Add(SecretKind.EnrollmentToken, "first-token-value");

        Assert.Equal("tok-2", vault.Add(SecretKind.EnrollmentToken, "second-token-value"));
        Assert.Equal("pwd-1", vault.Add(SecretKind.Password, "a-password-value"));
    }

    [Fact]
    public void Use_PassesStoredValueWithoutCopyingOut()
    {
        using var vault = new SecretVault();
        var reference = vault.Add(SecretKind.Password, "hunter22");

        var result = vault.Use(reference, value => value.SequenceEqual("hunter22".AsSpan()));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Use_UnknownReference_ReturnsNotFound()
    {
        using var vault = new SecretVault();

        var result = vault.Use("tok-99", value => value.Length);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.NotFound]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Contains_StoredReference_ReturnsTrue()
    {
        using var vault = new SecretVault();
        var reference = vault.Add(SecretKind.BearerToken, "bearer-stored-value");

        Assert.True(vault.Contains(reference));
        Assert.False(vault.Contains("brr-2"));
    }

    [Fact]
    public void Remove_ZeroesStorage()
    {
        using var vault = new SecretVault();
        var reference = vault.Add(SecretKind.PrivateKey, "private-key-material");
        var storage = vault.PeekForTests(reference);
        Assert.NotNull(storage);

        vault.Remove(reference);

        Assert.All(storage, c => Assert.Equal('\0', c));
        Assert.False(vault.Contains(reference));
    }

    [Fact]
    public void Remove_UnknownReference_IsNoOp()
    {
        using var vault = new SecretVault();
        var reference = vault.Add(SecretKind.Password, "password-kept-value");

        vault.Remove("pwd-404");

        Assert.True(vault.Contains(reference));
    }

    [Fact]
    public void Use_AfterRemove_ReturnsNotFound()
    {
        using var vault = new SecretVault();
        var reference = vault.Add(SecretKind.EnrollmentToken, "enrollment-removed");
        vault.Remove(reference);

        var result = vault.Use(reference, value => value.Length);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Dispose_ZeroesEveryStoredValue()
    {
        var vault = new SecretVault();
        var first = vault.Add(SecretKind.Password, "first-password-value");
        var second = vault.Add(SecretKind.EnrollmentToken, "second-token-value");
        var firstStorage = vault.PeekForTests(first);
        var secondStorage = vault.PeekForTests(second);
        Assert.NotNull(firstStorage);
        Assert.NotNull(secondStorage);

        vault.Dispose();

        Assert.All(firstStorage, c => Assert.Equal('\0', c));
        Assert.All(secondStorage, c => Assert.Equal('\0', c));
    }
}

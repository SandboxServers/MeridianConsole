using System.Buffers.Text;
using System.Security.Cryptography;
using Dhadgar.Lab.Daemon;

namespace Dhadgar.Lab.Tests.Daemon;

public sealed class TokenValidatorTests
{
    [Fact]
    public void ParseToken_MinimumLengthToken_Succeeds()
    {
        var raw = Base64Url.EncodeToString(new byte[LabDefaults.MinTokenBytes]);

        var result = TokenValidator.ParseToken(raw);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(LabDefaults.MinTokenBytes, result.Value.Length);
    }

    [Fact]
    public void ParseToken_LongerThanMinimum_Succeeds()
    {
        var raw = Base64Url.EncodeToString(new byte[LabDefaults.MinTokenBytes + 1]);

        var result = TokenValidator.ParseToken(raw);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public void ParseToken_OneByteShort_RefusedAsInvalidArgument()
    {
        var raw = Base64Url.EncodeToString(new byte[LabDefaults.MinTokenBytes - 1]);

        var result = TokenValidator.ParseToken(raw);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseToken_Null_RefusedAsInvalidArgument()
    {
        var result = TokenValidator.ParseToken(null);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseToken_Whitespace_RefusedAsInvalidArgument()
    {
        var result = TokenValidator.ParseToken("   ");

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseToken_NotBase64Url_RefusedWithoutEchoingInput()
    {
        const string raw = "not*base64url*value";

        var result = TokenValidator.ParseToken(raw);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(raw, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseToken_ThirtyTwoBytesStandardBase64_RefusedAsNotBase64Url()
    {
        // Standard base64 uses '+' and '/', which base64url does not allow.
        var raw = Convert.ToBase64String(Enumerable.Repeat((byte)0xFB, LabDefaults.MinTokenBytes).ToArray());

        var result = TokenValidator.ParseToken(raw);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ParseToken_FreshRandomToken_Succeeds()
    {
        var raw = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes));

        var result = TokenValidator.ParseToken(raw);

        Assert.True(result.IsSuccess, result.Error);
    }
}

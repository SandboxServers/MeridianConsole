using System.Text.Json;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat.Eval;

public sealed class JsonPointerTests
{
    private const string Doc = """
        {"rows":[{"used_at":null,"id":"a"},{"used_at":"2026"}],"a/b":1,"m~n":2,"row_count":2}
        """;

    [Fact]
    public void EmptyPointer_IsWholeDocument()
    {
        Result<PointerLookup> found = JsonPointer.Resolve(Root(), "");

        Assert.True(found.IsSuccess);
        Assert.True(found.Value.Found);
        Assert.Equal(JsonValueKind.Object, found.Value.Value.ValueKind);
    }

    [Fact]
    public void ArrayIndex_ResolvesElement()
    {
        Result<PointerLookup> found = JsonPointer.Resolve(Root(), "/rows/1/used_at");

        Assert.True(found.IsSuccess);
        Assert.True(found.Value.Found);
        Assert.Equal("2026", found.Value.Value.GetString());
    }

    [Fact]
    public void NullValue_IsFoundNotMissing()
    {
        Result<PointerLookup> found = JsonPointer.Resolve(Root(), "/rows/0/used_at");

        Assert.True(found.IsSuccess);
        Assert.True(found.Value.Found);
        Assert.Equal(JsonValueKind.Null, found.Value.Value.ValueKind);
    }

    [Fact]
    public void Escapes_ResolveTildeAndSlash()
    {
        Assert.Equal(1, JsonPointer.Resolve(Root(), "/a~1b").Value.Value.GetInt32());
        Assert.Equal(2, JsonPointer.Resolve(Root(), "/m~0n").Value.Value.GetInt32());
    }

    [Theory]
    [InlineData("/missing")]
    [InlineData("/rows/9")]
    [InlineData("/rows/-")]
    [InlineData("/rows/01")]
    [InlineData("/row_count/deeper")]
    public void MissingTarget_IsSuccessNotFound(string pointer)
    {
        Result<PointerLookup> found = JsonPointer.Resolve(Root(), pointer);

        Assert.True(found.IsSuccess);
        Assert.False(found.Value.Found);
    }

    [Theory]
    [InlineData("rows")]
    [InlineData("/bad~2")]
    [InlineData("/trailing~")]
    public void Malformed_IsFailure(string pointer)
    {
        Result<PointerLookup> found = JsonPointer.Resolve(Root(), pointer);

        Assert.True(found.IsFailure);
    }

    private static JsonElement Root() => JsonSerializer.Deserialize<JsonElement>(Doc);
}

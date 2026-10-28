using System.Text.Json;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat.Eval;

public sealed class UatVarsTests
{
    private const string UnknownVar = "[Lab.InvalidArgument] unknown var";

    [Fact]
    public void Expand_SubstitutesBuiltIns()
    {
        var vars = new UatVars("ab12cd", "token-mint", "lab-selftest", 42, null);

        Result<string> text = vars.Expand("selftest-${run_id}-${row_id}-${seed}");

        Assert.True(text.IsSuccess);
        Assert.Equal("selftest-ab12cd-token-mint-42", text.Value);
    }

    [Fact]
    public void Expand_UnknownVar_IsFailure()
    {
        var vars = new UatVars("ab12cd", "r", "s", 1, null);

        Result<string> text = vars.Expand("${member}");

        Assert.True(text.IsFailure);
        Assert.Equal(UnknownVar, text.Error);
    }

    [Fact]
    public void Expand_MemberOnlyWhenDefined()
    {
        var withMember = new UatVars("ab12cd", "r", "s", 1, "member-1");

        Assert.Equal("member-1", withMember.Expand("${member}").Value);
    }

    [Fact]
    public void Expand_SecretReference_IsLeftForTheVault()
    {
        var vars = new UatVars("ab12cd", "r", "s", 1, null);

        Result<string> text = vars.Expand("{\"key\":\"${secret:agent-token}\"}");

        Assert.True(text.IsSuccess);
        Assert.Equal("{\"key\":\"${secret:agent-token}\"}", text.Value);
    }

    [Fact]
    public void Expand_UnclosedPlaceholder_IsLiteral()
    {
        var vars = new UatVars("ab12cd", "r", "s", 1, null);

        Assert.Equal("cost ${ 5", vars.Expand("cost ${ 5").Value);
    }

    [Fact]
    public void Expand_Json_SubstitutesStringsOnly()
    {
        var vars = new UatVars("ab12cd", "r", "s", 7, null);
        JsonElement args = JsonSerializer.Deserialize<JsonElement>("{\"label\":\"x-${seed}\",\"n\":3,\"list\":[\"${run_id}\",true]}");

        Result<JsonElement> expanded = vars.Expand(args);

        Assert.True(expanded.IsSuccess);
        Assert.Equal("x-7", expanded.Value.GetProperty("label").GetString());
        Assert.Equal(3, expanded.Value.GetProperty("n").GetInt32());
        Assert.Equal("ab12cd", expanded.Value.GetProperty("list")[0].GetString());
        Assert.True(expanded.Value.GetProperty("list")[1].GetBoolean());
    }

    [Fact]
    public void Capture_StoresStringAndRawValues()
    {
        var vars = new UatVars("ab12cd", "r", "s", 1, null);
        JsonElement result = JsonSerializer.Deserialize<JsonElement>("{\"token_ref\":\"tok-9\",\"token_id\":12,\"a/b\":\"slash\"}");

        Assert.True(vars.Capture("token_ref", result, "/token_ref").IsSuccess);
        Assert.True(vars.Capture("token_id", result, "/token_id").IsSuccess);
        Assert.True(vars.Capture("slash", result, "/a~1b").IsSuccess);

        Assert.Equal("tok-9", vars.Expand("${token_ref}").Value);
        Assert.Equal("12", vars.Expand("${token_id}").Value);
        Assert.Equal("slash", vars.Expand("${slash}").Value);
    }

    [Fact]
    public void Capture_MissingPointer_IsFailure()
    {
        var vars = new UatVars("ab12cd", "r", "s", 1, null);
        JsonElement result = JsonSerializer.Deserialize<JsonElement>("{}");

        Result captured = vars.Capture("token_ref", result, "/token_ref");

        Assert.True(captured.IsFailure);
        Assert.True(vars.Expand("${token_ref}").IsFailure);
    }

    [Fact]
    public void Capture_BadName_IsFailure()
    {
        var vars = new UatVars("ab12cd", "r", "s", 1, null);
        JsonElement result = JsonSerializer.Deserialize<JsonElement>("{\"a\":1}");

        Assert.True(vars.Capture("bad-name", result, "/a").IsFailure);
    }
}

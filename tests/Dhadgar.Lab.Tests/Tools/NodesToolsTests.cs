using System.Text.Json;
using Dhadgar.Lab.Nodes;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Tools.Nodes;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Tools;

/// <summary>
/// The Nodes tools: the mint result carries a vault reference and never the plain token; a positive shift is refused.
/// </summary>
public sealed class NodesToolsTests
{
    private static readonly LabCallContext Context = new("call-test", null, "test");

    [Fact]
    public async Task Mint_ReturnsTokenRef_AndNoPlainToken()
    {
        var tool = new NodesTokenMintTool(new FakeNodesAdapter());

        Result<JsonElement> result = await tool.InvokeAsync(default, Context, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value.TryGetProperty("token_ref", out JsonElement tokenRef));
        Assert.Equal("tok-1", tokenRef.GetString());
        Assert.True(result.Value.TryGetProperty("organization_id", out _));
        Assert.False(result.Value.TryGetProperty("token", out _));
        Assert.DoesNotContain("plain", result.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mint_RefusesAnOrganizationOtherThanTheLabDefault()
    {
        var tool = new NodesTokenMintTool(new FakeNodesAdapter());
        using JsonDocument args = JsonDocument.Parse($"{{\"organization_id\":\"{Guid.NewGuid()}\"}}");

        Result<JsonElement> result = await tool.InvokeAsync(args.RootElement, Context, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Unavailable]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shift_RefusesAPositiveShift()
    {
        var adapter = new FakeNodesAdapter();
        var tool = new NodesCertTimeShiftTool(adapter);
        using JsonDocument args = JsonDocument.Parse($"{{\"node_id\":\"{Guid.NewGuid()}\",\"shift_minutes\":5}}");

        Result<JsonElement> result = await tool.InvokeAsync(args.RootElement, Context, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, adapter.ShiftCalls);
    }

    [Fact]
    public async Task Shift_DefaultsToAllFourFields()
    {
        var adapter = new FakeNodesAdapter();
        var tool = new NodesCertTimeShiftTool(adapter);
        using JsonDocument args = JsonDocument.Parse($"{{\"node_id\":\"{Guid.NewGuid()}\",\"shift_minutes\":-60}}");

        Result<JsonElement> result = await tool.InvokeAsync(args.RootElement, Context, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, adapter.ShiftCalls);
        Assert.Equal(4, adapter.LastFields.Count);
        Assert.Equal(1, result.Value.GetProperty("rows_updated").GetInt32());
    }

    private sealed class FakeNodesAdapter : INodesLabAdapter
    {
        public int ShiftCalls { get; private set; }

        public IReadOnlyCollection<CertTimeField> LastFields { get; private set; } = Array.Empty<CertTimeField>();

        public Task<Result<MintedToken>> MintTokenAsync(Guid organizationId, string label, TimeSpan validity, CancellationToken ct) =>
            Task.FromResult(Result<MintedToken>.Success(new MintedToken(Guid.NewGuid(), "tok-1", DateTimeOffset.UtcNow.Add(validity))));

        public Task<Result<int>> ShiftCertificateTimesAsync(Guid nodeId, TimeSpan shift, IReadOnlyCollection<CertTimeField> fields, CancellationToken ct)
        {
            ShiftCalls++;
            LastFields = fields;
            return Task.FromResult(Result<int>.Success(1));
        }
    }
}

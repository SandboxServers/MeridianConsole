using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat;

public sealed class UatPlanToolTests : IDisposable
{
    private const string ValidSpec = """
        schema = 1

        [section]
        id = "plan-check"
        title = "Plan check"

        [[row]]
        id = "read-key"
        title = "Key readable"
        expected = "present"

          [[row.step]]
          id = "read"
          tool = "registry_read"
          args = {}

          [[row.expect]]
          id = "key-row"
          text = "key read"
          source = "step"
          step = "read"
          op = "truthy"
          required = true
        """;

    private readonly string _root;
    private readonly string _specs;
    private readonly LabPaths _paths;
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);
    private readonly ServiceProvider _services;

    public UatPlanToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meridian-lab-l13-" + Guid.NewGuid().ToString("N"));
        _specs = Path.Combine(_root, "docs", "guides", "uat-specs");
        _ = Directory.CreateDirectory(Path.Combine(_specs, "catalog"));
        File.WriteAllText(Path.Combine(_specs, "a.toml"), ValidSpec);
        File.WriteAllText(Path.Combine(_specs, "catalog", "c.toml"), ValidSpec);
        File.WriteAllText(Path.Combine(_specs, "bad.toml"), ValidSpec.Replace("tool = \"registry_read\"", "tool = \"fault_kill\"", StringComparison.Ordinal));

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Lab:RepoRoot"] = _root })
            .Build();
        _paths = LabPaths.Create(config);

        var registry = new LabToolRegistry(new ILabTool[] { new RegistryReadTool() });
        _services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task ListSpecs_ListsRootAndCatalogSpecs_Sorted()
    {
        Result<JsonElement> result = await new UatListSpecsTool(_paths, _validator)
            .InvokeAsync(Args("{}"), Context(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        string json = result.Value!.GetRawText();
        Assert.Contains("a.toml", json, StringComparison.Ordinal);
        Assert.Contains("catalog/c.toml", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListSpecs_SpecsDirOutsideRepo_IsRefused()
    {
        string outside = Path.GetTempPath();

        Result<JsonElement> result = await new UatListSpecsTool(_paths, _validator)
            .InvokeAsync(Args(JsonSerializer.Serialize(new { specs_dir = outside })), Context(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Plan_ValidRootSpec_IsValid_AndListsRowIds()
    {
        Result<JsonElement> result = await PlanTool().InvokeAsync(Args("{\"spec\":\"a.toml\"}"), Context(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(BoolProperty(result.Value!, "valid"));
        Assert.Contains("read-key", result.Value!.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plan_CatalogSpec_IsReadable()
    {
        Result<JsonElement> result = await PlanTool().InvokeAsync(Args("{\"spec\":\"catalog/c.toml\"}"), Context(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(BoolProperty(result.Value!, "valid"));
    }

    [Fact]
    public async Task Plan_SpecWithFaultTool_ReturnsProblem_AsData()
    {
        Result<JsonElement> result = await PlanTool().InvokeAsync(Args("{\"spec\":\"bad.toml\"}"), Context(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(BoolProperty(result.Value!, "valid"));
        Assert.Contains("cannot be a step", result.Value!.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"spec\":\"../a.toml\"}")]
    [InlineData("{\"spec\":\"catalog/../a.toml\"}")]
    [InlineData("{\"spec\":\"a.txt\"}")]
    [InlineData("{\"spec\":\"missing.toml\"}")]
    public async Task Plan_BadName_OrMissingFile_IsFailure(string args)
    {
        Result<JsonElement> result = await PlanTool().InvokeAsync(Args(args), Context(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    private UatPlanTool PlanTool() => new(_paths, _validator, _services);

    private static JsonElement Args(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static LabCallContext Context() => new("call-test", null, "test");

    private static bool BoolProperty(JsonElement element, string name)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name.Replace("_", string.Empty, StringComparison.Ordinal), name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.True;
            }
        }

        return false;
    }
}

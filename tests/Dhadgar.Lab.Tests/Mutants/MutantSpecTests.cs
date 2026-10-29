using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Mutants;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Mutants;

public sealed class MutantSpecTests
{
    private const string BaseSha = "eda187205b54622a4450ea011faeab46941d1dc1";
    private const string LabBaseSha = "a961539a4476cfe02b11da6cce2149e5e0be46af";
    private const string ProductFile = "src/Dhadgar.Nodes/Services/EnrollmentService.cs";

    [Fact]
    public void RealMutants_LoadCleanly()
    {
        string root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        IPathValidator validator = new PathValidator(NullLogger<PathValidator>.Instance);

        Result<IReadOnlyList<MutantSpec>> specs = MutantSpecLoader.LoadAll(Path.Combine(root, "tools", "lab", "mutants"), root, validator);

        Assert.True(specs.IsSuccess, specs.Error);
        string[] ids = specs.Value!.Select(spec => spec.Id).ToArray();
        Assert.Contains("M1-token-no-concurrency", ids);
        Assert.Contains("M4-no-concurrency-catch", ids);
        Assert.Contains("M5-token-logged", ids);
        Assert.All(specs.Value!, spec => Assert.Contains(spec.Kind, new[] { MutantKind.Product, MutantKind.Lab }));
        Assert.Contains(specs.Value!, spec => spec.Id == "LM1-journal-after-inject" && spec.Kind == MutantKind.Lab);
        Assert.All(specs.Value!.Where(spec => spec.Kind == MutantKind.Product), spec => Assert.Equal(BaseSha, spec.Base));
        Assert.All(specs.Value!.Where(spec => spec.Kind == MutantKind.Lab), spec => Assert.Equal(LabBaseSha, spec.Base));
        Assert.Contains(specs.Value!, spec => spec.Id == "LM1-journal-after-inject" && spec.Kind == MutantKind.Lab);
    }

    [Fact]
    public void Valid_ProductSpec_LoadsWithTypedArgs()
    {
        Result<MutantSpec> spec = Load(Spec());

        Assert.True(spec.IsSuccess, spec.Error);
        Assert.Equal(MutantKind.Product, spec.Value!.Kind);
        Assert.Equal("history_run", spec.Value.Detector.Tool);
        Assert.Equal(4L, spec.Value.Detector.Args!["clients"]!.GetValue<long>());
        Assert.Equal("enroll-race", spec.Value.Detector.Args["scenario"]!.GetValue<string>());
    }

    [Fact]
    public void IdMustMatchFolderName()
    {
        Result<MutantSpec> spec = Load(Spec(id: "P9-other"));

        Assert.True(spec.IsFailure);
        Assert.Contains("folder name", spec.Error);
    }

    [Fact]
    public void WrongSchema_IsRefused()
    {
        Result<MutantSpec> spec = Load(Spec().Replace("schema = 1", "schema = 2", StringComparison.Ordinal));

        Assert.True(spec.IsFailure);
        Assert.Contains("schema", spec.Error);
    }

    [Theory]
    [InlineData("tools/lab/x.cs")]
    [InlineData("src/Tools/Dhadgar.Lab/Mutants/MutantSpec.cs")]
    [InlineData("src/Dhadgar.Nodes/../../secrets.txt")]
    [InlineData("/etc/passwd")]
    public void ProductFiles_OutsideProductSrc_AreRefused(string file)
    {
        Result<MutantSpec> spec = Load(Spec(files: $"[\"{file}\"]"));

        Assert.True(spec.IsFailure, file);
    }

    [Fact]
    public void LabFile_OutsideSrcTools_IsRefused()
    {
        Result<MutantSpec> spec = Load(Spec(kind: "lab", files: $"[\"{ProductFile}\"]", detector: "{ test = \"Dhadgar.Lab.Tests.X.Y\" }"));

        Assert.True(spec.IsFailure);
    }

    [Fact]
    public void LabMutant_WithTestDetector_Loads()
    {
        Result<MutantSpec> spec = Load(Spec(kind: "lab", files: "[\"src/Tools/Dhadgar.Lab/Mutants/MutantSpec.cs\"]", detector: "{ test = \"Dhadgar.Lab.Tests.X.Y\" }"));

        Assert.True(spec.IsSuccess, spec.Error);
        Assert.Equal("Dhadgar.Lab.Tests.X.Y", spec.Value!.Detector.Test);
        Assert.Null(spec.Value.Detector.Tool);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("EDA187205B54622A4450EA011FAEAB46941D1DC1")]
    public void Base_MustBeFullLowercaseSha(string sha)
    {
        Result<MutantSpec> spec = Load(Spec(sha: sha));

        Assert.True(spec.IsFailure);
    }

    [Theory]
    [InlineData(".*")]
    [InlineData("^hist:.*")]
    [InlineData("(unclosed")]
    public void ExpectPattern_MustBeBounded(string pattern)
    {
        Result<MutantSpec> spec = Load(Spec(detector: "{ tool = \"history_run\", expect_pattern = '" + pattern + "' }"));

        Assert.True(spec.IsFailure, pattern);
    }

    [Fact]
    public void ProductDetector_WithoutExpectPattern_IsRefused()
    {
        Result<MutantSpec> spec = Load(Spec(detector: "{ tool = \"history_run\" }"));

        Assert.True(spec.IsFailure);
    }

    [Fact]
    public void FileMissingAtBase_IsRefused()
    {
        string text = Spec();
        Result<MutantSpec> spec = MutantSpecLoader.LoadText("P1-test", text, "folder", RepoRoot(), Validator(), existsAtBase: _ => false);

        Assert.True(spec.IsFailure);
        Assert.Contains("does not exist", spec.Error);
    }

    private static string RepoRoot() => LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);

    private static PathValidator Validator() => new(NullLogger<PathValidator>.Instance);

    private static Result<MutantSpec> Load(string text) =>
        MutantSpecLoader.LoadText("P1-test", text, "folder", RepoRoot(), Validator(), existsAtBase: _ => true);

    private static string Spec(
        string id = "P1-test",
        string kind = "product",
        string files = "[\"" + ProductFile + "\"]",
        string sha = BaseSha,
        string detector = "{ tool = \"history_run\", args = { scenario = \"enroll-race\", clients = 4 }, expect_pattern = '^hist:enrollment\\|(E1|E6)\\|enroll-race$' }") =>
        string.Join(
            "\n",
            "schema = 1",
            "id = \"" + id + "\"",
            "kind = \"" + kind + "\"",
            "base = \"" + sha + "\"",
            "title = \"Test mutant\"",
            "files = " + files,
            "services = [\"nodes\"]",
            "detector = " + detector,
            string.Empty);
}

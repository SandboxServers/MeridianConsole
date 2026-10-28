using System.Text.RegularExpressions;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// Arguments of <c>uat_list_specs</c>. <c>specs_dir</c> must validate under the repo root; the default is the repo's UAT spec folder.
/// </summary>
public sealed record UatListSpecsArgs(string? SpecsDir = null);

/// <summary>
/// Arguments of <c>uat_plan</c>: <c>spec</c> is a file name or <c>catalog/&lt;file&gt;</c>.
/// </summary>
public sealed record UatPlanArgs(string Spec, string? SpecsDir = null);

/// <summary>
/// The result of <c>uat_list_specs</c>: the folder listed and its spec names (<c>catalog/</c> names included).
/// </summary>
public sealed record UatListSpecsResult(string Dir, IReadOnlyList<string> Specs);

/// <summary>
/// The result of <c>uat_plan</c>: whether the spec loads and validates, its problems, and its row ids in order.
/// </summary>
public sealed record UatPlanResult(string Spec, bool Valid, IReadOnlyList<UatProblem> Problems, IReadOnlyList<string> Rows);

/// <summary>
/// Shared path rules for the UAT plan tools: the specs folder lives under the repo root, is not a link, and spec names are
/// file names or <c>catalog/&lt;file&gt;</c> only (R-PATH).
/// </summary>
internal static class UatSpecPaths
{
    internal const string DefaultSubdir = "docs/guides/uat-specs";
    internal const long MaxSpecBytes = 1024 * 1024;
    private static readonly Regex SpecName = new(@"^(catalog/)?[A-Za-z0-9][A-Za-z0-9._-]{0,127}\.toml\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static Result<string> ResolveDir(string? requested, LabPaths paths, IPathValidator validator)
    {
        if (string.IsNullOrWhiteSpace(paths.RepoRoot))
        {
            return Result<string>.Failure("[Lab.Unavailable] Lab:RepoRoot is not set");
        }

        string candidate;
        string root;
        try
        {
            root = Path.GetFullPath(paths.RepoRoot);
            candidate = requested is null
                ? Path.Combine(root, DefaultSubdir)
                : Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(root, requested));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] specs_dir is not a valid path");
        }

        Result<string> validated = validator.ValidatePath(candidate, [root]);
        if (validated.IsFailure)
        {
            return Result<string>.Failure(validated.Error);
        }

        if (IsLink(validated.Value!) || !Directory.Exists(validated.Value!))
        {
            return Result<string>.Failure("[Lab.Path] the specs folder is missing or is a symbolic link or junction");
        }

        return Result<string>.Success(validated.Value!);
    }

    internal static Result<string> ResolveSpec(string dir, string name, IPathValidator validator)
    {
        if (!SpecName.IsMatch(name))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] spec must be a file name or catalog/<file>, ending in .toml");
        }

        string candidate = Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar));
        Result<string> validated = validator.ValidatePath(candidate, [dir]);
        if (validated.IsFailure)
        {
            return Result<string>.Failure(validated.Error);
        }

        if (IsLink(validated.Value!))
        {
            return Result<string>.Failure("[Lab.Path] the spec is a symbolic link or junction");
        }

        return Result<string>.Success(validated.Value!);
    }

    private static bool IsLink(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            var dir = new DirectoryInfo(path);
            return dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}

/// <summary>
/// <c>uat_list_specs</c> (Open): the spec files in the specs folder and its <c>catalog/</c> subfolder. Touches nothing.
/// </summary>
public sealed class UatListSpecsTool : LabTool<UatListSpecsArgs>
{
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;

    /// <summary>
    /// Creates the tool over the lab paths and the shared path validator.
    /// </summary>
    public UatListSpecsTool(LabPaths paths, IPathValidator validator)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <inheritdoc />
    public override string Name => "uat_list_specs";

    /// <inheritdoc />
    public override string Description => "Lists the UAT spec files (docs/guides/uat-specs and its catalog/ folder). Reads names only; runs nothing.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(UatListSpecsArgs args, LabCallContext context, CancellationToken ct)
    {
        Result<string> dir = UatSpecPaths.ResolveDir(args.SpecsDir, _paths, _validator);
        if (dir.IsFailure)
        {
            return Task.FromResult(Result<object>.Failure(dir.Error));
        }

        var names = new List<string>();
        foreach (string file in Directory.EnumerateFiles(dir.Value!, "*.toml", SearchOption.TopDirectoryOnly))
        {
            names.Add(Path.GetFileName(file));
        }

        string catalog = Path.Combine(dir.Value!, "catalog");
        if (Directory.Exists(catalog))
        {
            foreach (string file in Directory.EnumerateFiles(catalog, "*.toml", SearchOption.TopDirectoryOnly))
            {
                names.Add("catalog/" + Path.GetFileName(file));
            }
        }

        names.Sort(StringComparer.Ordinal);
        return Task.FromResult(Result<object>.Success(new UatListSpecsResult(dir.Value!, names)));
    }
}

/// <summary>
/// <c>uat_plan</c> (Open): loads and validates one spec with no lease, runs no step and touches nothing. Problems are
/// returned as data (R-RESULT).
/// </summary>
public sealed class UatPlanTool : LabTool<UatPlanArgs>
{
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Creates the tool. The tool registry is resolved at call time because the registry itself contains this tool.
    /// </summary>
    public UatPlanTool(LabPaths paths, IPathValidator validator, IServiceProvider services)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public override string Name => "uat_plan";

    /// <inheritdoc />
    public override string Description => "Loads and validates one UAT spec (no lease, runs nothing). Returns the problems with file and line, and the row ids.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(UatPlanArgs args, LabCallContext context, CancellationToken ct)
    {
        Result<string> dir = UatSpecPaths.ResolveDir(args.SpecsDir, _paths, _validator);
        if (dir.IsFailure)
        {
            return Result<object>.Failure(dir.Error);
        }

        Result<string> path = UatSpecPaths.ResolveSpec(dir.Value!, args.Spec, _validator);
        if (path.IsFailure)
        {
            return Result<object>.Failure(path.Error);
        }

        if (!File.Exists(path.Value!))
        {
            return Result<object>.Failure("[Lab.NotFound] no such spec");
        }

        if (new FileInfo(path.Value!).Length > UatSpecPaths.MaxSpecBytes)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] the spec is larger than 1 MiB");
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(path.Value!, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<object>.Failure("[Lab.Unavailable] the spec could not be read");
        }

        string name = args.Spec;
        SpecLoadOutcome loaded = SpecLoader.LoadText(name, text);
        if (loaded.Spec is null)
        {
            return Result<object>.Success(new UatPlanResult(name, false, loaded.Problems, Array.Empty<string>()));
        }

        LabToolRegistry registry = _services.GetRequiredService<LabToolRegistry>();
        IReadOnlyList<UatProblem> problems = SpecValidator.Validate(loaded.Spec, text, registry);
        var rows = loaded.Spec.Rows.Select(r => r.Id).ToArray();
        return Result<object>.Success(new UatPlanResult(name, problems.Count == 0, problems, rows));
    }
}

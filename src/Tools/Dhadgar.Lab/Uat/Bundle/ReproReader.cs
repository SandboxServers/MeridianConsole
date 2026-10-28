using System.Globalization;
using System.Text.RegularExpressions;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Bundle;

/// <summary>
/// The settings a replay takes from a bundle's <c>repro\repro.toml</c>: the seed, the section and rows of the run, the label,
/// and the spec file with the sha256 it had when the run was written.
/// </summary>
/// <param name="Seed">The run seed.</param>
/// <param name="Section">The section id.</param>
/// <param name="Rows">The row ids that ran; empty means every row of the section.</param>
/// <param name="Label">The run label, or an empty string.</param>
/// <param name="SpecFile">The spec file name as the run was started with.</param>
/// <param name="SpecSha256">The sha256 of the spec file bytes at the time of the run.</param>
public sealed record ReproSettings(ulong Seed, string Section, IReadOnlyList<string> Rows, string Label, string SpecFile, string SpecSha256);

/// <summary>
/// Reads the small TOML subset that <c>BundleWriter</c> writes into <c>repro.toml</c>. Unknown keys are ignored; a missing
/// required key is refused with <c>[Lab.InvalidArgument]</c>.
/// </summary>
public static class ReproReader
{
    private static readonly Regex QuotedPattern = new("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses the text of a <c>repro.toml</c> file.
    /// </summary>
    /// <param name="text">The file text.</param>
    public static Result<ReproSettings> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('[') && line.EndsWith(']'))
            {
                continue;
            }

            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                return Result<ReproSettings>.Failure("[Lab.InvalidArgument] repro.toml has a line that is not key = value");
            }

            values.TryAdd(line[..eq].Trim(), line[(eq + 1)..].Trim());
        }

        if (!values.TryGetValue("seed", out string? seedText) ||
            !ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            return Result<ReproSettings>.Failure("[Lab.InvalidArgument] repro.toml has no valid seed");
        }

        if (!values.TryGetValue("section", out string? sectionText) || ParseString(sectionText) is not { Length: > 0 } section)
        {
            return Result<ReproSettings>.Failure("[Lab.InvalidArgument] repro.toml has no section");
        }

        if (!values.TryGetValue("file", out string? fileText) || ParseString(fileText) is not { Length: > 0 } file)
        {
            return Result<ReproSettings>.Failure("[Lab.InvalidArgument] repro.toml has no spec file");
        }

        if (!values.TryGetValue("sha256", out string? shaText) || ParseString(shaText) is not { Length: > 0 } sha)
        {
            return Result<ReproSettings>.Failure("[Lab.InvalidArgument] repro.toml has no spec sha256");
        }

        string label = values.TryGetValue("label", out string? labelText) ? ParseString(labelText) ?? string.Empty : string.Empty;
        List<string> rows = values.TryGetValue("rows", out string? rowsText) ? ParseArray(rowsText) : [];

        return Result<ReproSettings>.Success(new ReproSettings(seed, section, rows, label, file, sha));
    }

    private static string? ParseString(string value)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        {
            return null;
        }

        Match match = QuotedPattern.Match(value);
        return match.Success && match.Index == 0 && match.Length == value.Length ? Unescape(match.Groups[1].Value) : null;
    }

    private static List<string> ParseArray(string value)
    {
        var rows = new List<string>();
        if (value.Length < 2 || value[0] != '[' || value[^1] != ']')
        {
            return rows;
        }

        foreach (Match match in QuotedPattern.Matches(value))
        {
            rows.Add(Unescape(match.Groups[1].Value));
        }

        return rows;
    }

    private static string Unescape(string value)
    {
        var text = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
            }

            text.Append(value[i]);
        }

        return text.ToString();
    }
}

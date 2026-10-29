using System.Text.Json.Nodes;

namespace Dhadgar.Lab.Mutants;

/// <summary>
/// Where a mutant's patch may reach: a product change under <c>src/</c> outside <c>src/Tools/</c>, or a lab change under
/// <c>src/Tools/</c> (D-ML51).
/// </summary>
public enum MutantKind
{
    /// <summary>A product change, built only in a <c>git archive</c> copy by <c>lab_build {mutant}</c>.</summary>
    Product,

    /// <summary>A lab change, applied only by <c>Dhadgar.Lab.SelfCheck</c> (L55).</summary>
    Lab,
}

/// <summary>
/// The detector that must catch a mutant. A product mutant names a lab tool and its arguments; a lab mutant names a test.
/// </summary>
/// <param name="Tool">The lab tool to dispatch (product mutants only).</param>
/// <param name="Args">The tool arguments as JSON (product mutants only).</param>
/// <param name="Test">The fully qualified test name (lab mutants only).</param>
/// <param name="ExpectPattern">The signature pattern that proves the mutant was detected, or null when none is named.</param>
public sealed record MutantDetector(string? Tool, JsonObject? Args, string? Test, string? ExpectPattern);

/// <summary>
/// One mutant as <c>tools/lab/mutants/&lt;id&gt;/mutant.toml</c> declares it (C19, schema 1).
/// </summary>
/// <param name="Id">The mutant id, equal to its folder name.</param>
/// <param name="Kind">Product or lab.</param>
/// <param name="Base">The 40-character commit sha the patch applies to.</param>
/// <param name="Title">A short human title.</param>
/// <param name="Files">Every repo-relative file the patch may touch.</param>
/// <param name="Services">Images rebuilt from the patched tree (product mutants).</param>
/// <param name="Detector">The detector run and its expected signature.</param>
/// <param name="Folder">The absolute folder that holds <c>mutant.toml</c> and <c>change.patch</c>.</param>
public sealed record MutantSpec(
    string Id,
    MutantKind Kind,
    string Base,
    string Title,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Services,
    MutantDetector Detector,
    string Folder);

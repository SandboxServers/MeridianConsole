using System.Text.Json.Nodes;

namespace Dhadgar.Lab.Results;

/// <summary>
/// The outcome of loading the shipped known-issue file for one ingest (D-ML92). A checked ingest matched every row against
/// <see cref="Issues"/> and records the file's SHA-256; an unchecked ingest matched against <see cref="KnownIssues.Empty"/>
/// and records the fixed <see cref="Reason"/>.
/// </summary>
public sealed record KnownIssueCheck(KnownIssues Issues, bool Checked, string? Reason, string? FileSha256)
{
    /// <summary>The reason a bundle gives for not matching known issues (D-ML92); the writer and the attestor both use it.</summary>
    public const string BundleUncheckedReason = "the bundle does not match known issues until L26; the results DB ingest does";

    /// <summary>A checked outcome over a loaded file.</summary>
    /// <param name="issues">The loaded issues.</param>
    /// <param name="fileSha256">Lowercase hex SHA-256 of the file's bytes.</param>
    public static KnownIssueCheck Of(KnownIssues issues, string fileSha256) => new(issues, true, null, fileSha256);

    /// <summary>An unchecked outcome; <paramref name="reason"/> is fixed text, never file content.</summary>
    public static KnownIssueCheck Unchecked(string reason) => new(KnownIssues.Empty, false, reason, null);

    /// <summary>
    /// The JSON recorded in <c>runs.known_issues_json</c> and the <c>ingest</c> payload:
    /// <c>{"checked":true,"issues":n,"file_sha256":"hex"}</c> or <c>{"checked":false,"reason":"..."}</c>.
    /// </summary>
    public JsonObject ToJson()
    {
        if (Checked)
        {
            return new JsonObject
            {
                ["checked"] = true,
                ["issues"] = Issues.Issues.Count,
                ["file_sha256"] = FileSha256,
            };
        }

        return new JsonObject
        {
            ["checked"] = false,
            ["reason"] = Reason,
        };
    }
}

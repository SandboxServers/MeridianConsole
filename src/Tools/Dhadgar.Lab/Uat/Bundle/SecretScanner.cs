using System.Text.RegularExpressions;
using Dhadgar.Lab.Secrets;

namespace Dhadgar.Lab.Uat.Bundle;

/// <summary>
/// The result of one scan: whether every file passed, and the files that were removed because they held a secret.
/// </summary>
/// <param name="Passed">True when no file held a secret.</param>
/// <param name="RemovedFiles">The bundle-relative names of the removed files, in scan order.</param>
public sealed record SecretScanResult(bool Passed, IReadOnlyList<string> RemovedFiles);

/// <summary>
/// The last step before a bundle is final (C9). Every file is checked for every vault value (which includes the daemon
/// token) in its raw and JSON-escaped forms, PEM private key blocks, <c>Bearer &lt;20+ chars&gt;</c> and <c>Password=</c>/<c>Pwd=</c>. A hit deletes that file.
/// </summary>
public sealed class SecretScanner
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex PemKey = new(
        "-----BEGIN [A-Z ]*PRIVATE KEY-----",
        RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex BearerToken = new(
        @"Bearer\s+[A-Za-z0-9._~+/=-]{20,}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout);

    private static readonly Regex PasswordSegment = new(
        "(Password|Pwd)=",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout);

    private readonly SecretVault _vault;

    /// <summary>
    /// Creates the scanner over <paramref name="vault"/>. The vault is not owned by the scanner.
    /// </summary>
    public SecretScanner(SecretVault vault)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    /// <summary>
    /// Returns true when <paramref name="text"/> holds a vault value or one of the literal patterns.
    /// </summary>
    /// <param name="text">The text to check.</param>
    public bool HasSecret(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (PemKey.IsMatch(text) || BearerToken.IsMatch(text) || PasswordSegment.IsMatch(text))
        {
            return true;
        }

        foreach (var (reference, length) in _vault.Snapshot())
        {
            if (length == 0)
            {
                continue;
            }

            // A hit is any form of the secret: the raw value or one of its JSON-escaped forms.
            Dhadgar.Shared.Results.Result<bool> found = _vault.Use(reference, secret =>
                !SecretForms.ForEachForm(secret, form => text.AsSpan().IndexOf(form, StringComparison.Ordinal) < 0));
            if (found.IsSuccess && found.Value)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Scans every file under <paramref name="folder"/>. A file with a hit is deleted and recorded. Files that cannot be read
    /// count as hits, because an unreadable file cannot be shown to be clean.
    /// </summary>
    /// <param name="folder">The bundle folder. Must exist.</param>
    public SecretScanResult ScanFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var removed = new List<string>();
        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string name = Path.GetRelativePath(folder, file);
            bool hit;
            try
            {
                hit = HasSecret(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                hit = true;
            }

            if (!hit)
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file stays on disk; the run is still marked failed below.
            }

            removed.Add(name);
        }

        return new SecretScanResult(removed.Count == 0, removed);
    }
}

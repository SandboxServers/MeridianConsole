using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Probe;

/// <summary>The outcome of comparing two <c>LocalMachine\Root</c> snapshots.</summary>
internal sealed record RootDiff(
    IReadOnlyList<string> RootAdded,
    IReadOnlyList<string> RootRemoved,
    IReadOnlyList<string> Warnings);

/// <summary>
/// SHA-1 thumbprints of <c>LocalMachine\Root</c>, taken before the first change (D-S222) and compared
/// after cleanup. A removed root is a footprint; an added agent or UAT root is a footprint; other additions
/// (Windows updates roots itself) are warnings.
/// </summary>
internal sealed record RootSnapshot(IReadOnlyList<string> Thumbprints)
{
    /// <summary>Reads the current <c>LocalMachine\Root</c> thumbprints.</summary>
    public static Result<RootSnapshot> Take()
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var certs = store.Certificates;
            try
            {
                var thumbprints = certs.Select(c => c.Thumbprint).OrderBy(t => t, StringComparer.Ordinal).ToList();
                return Result<RootSnapshot>.Success(new RootSnapshot(thumbprints));
            }
            finally
            {
                foreach (var cert in certs)
                {
                    cert.Dispose();
                }
            }
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            return Result<RootSnapshot>.Failure($"LocalMachine\\Root could not be read: {ex.Message}");
        }
    }

    /// <summary>Writes one thumbprint per line.</summary>
    public Result Save(string path)
    {
        try
        {
            File.WriteAllLines(path, Thumbprints);
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failure($"root snapshot could not be saved: {ex.Message}");
        }
    }

    /// <summary>Reads a snapshot written by <see cref="Save"/>.</summary>
    public static Result<RootSnapshot> Load(string path)
    {
        try
        {
            var lines = File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
            return Result<RootSnapshot>.Success(new RootSnapshot(lines));
        }
        catch (IOException ex)
        {
            return Result<RootSnapshot>.Failure($"root snapshot could not be read: {ex.Message}");
        }
    }

    /// <summary>
    /// Compares two snapshots. <paramref name="lookup"/> returns the certificate for an added thumbprint
    /// (the caller disposes nothing; this method disposes what it receives).
    /// </summary>
    public static RootDiff Diff(RootSnapshot before, RootSnapshot after, Func<string, X509Certificate2?> lookup)
    {
        var removed = before.Thumbprints.Except(after.Thumbprints, StringComparer.OrdinalIgnoreCase).ToList();
        var added = after.Thumbprints.Except(before.Thumbprints, StringComparer.OrdinalIgnoreCase).ToList();
        var rootAdded = new List<string>();
        var warnings = new List<string>();

        foreach (var thumbprint in added)
        {
            using var cert = lookup(thumbprint);
            if (cert is not null
                && (AgentCertificateMatcher.IsAgentCa(cert)
                    || cert.Subject.Contains("O=Meridian Console UAT", StringComparison.OrdinalIgnoreCase)))
            {
                rootAdded.Add(thumbprint);
            }
            else
            {
                warnings.Add($"root certificate {thumbprint} was added during the run");
            }
        }

        return new RootDiff(rootAdded, removed, warnings);
    }
}

using System.Text.RegularExpressions;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Http;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Observe;

/// <summary>
/// The live fingerprint parts that need a running engine or Toxiproxy (C18). A part that cannot be read is
/// <c>unknown</c> and named in <see cref="Warnings"/>; collecting never fails a run (R-RESULT).
/// </summary>
/// <param name="ImageDigests">Image tag to image id, or <c>unknown</c>.</param>
/// <param name="EngineVersions">Engine name to version, or <c>unknown</c>.</param>
/// <param name="ToxiproxyVersion">The Toxiproxy version, or null when it could not be read.</param>
/// <param name="Warnings">One text per part that could not be collected.</param>
public sealed record LiveFingerprintParts(
    IReadOnlyDictionary<string, string> ImageDigests,
    IReadOnlyDictionary<string, string> EngineVersions,
    string? ToxiproxyVersion,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Collects the live fingerprint parts: image digests and the engine version through <see cref="IDockerCli"/> (R-DOCKER),
/// and the Toxiproxy version from <c>GET http://127.0.0.1:18474/version</c> only (R-HOST). The caller owns the handler,
/// which must not follow redirects and must not use a proxy (see <see cref="LabHttp.CreateHandler"/>).
/// </summary>
public static class LiveFingerprint
{
    /// <summary>The only Toxiproxy URL the fingerprint reads.</summary>
    public static readonly Uri ToxiproxyVersionUri = new("http://127.0.0.1:18474/version");

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly Regex SafeText = new("^[A-Za-z0-9._:+-]{1,128}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the live parts. Each read that fails becomes <c>unknown</c> plus one warning.
    /// </summary>
    /// <param name="docker">The docker CLI wrapper; every call goes through it.</param>
    /// <param name="engine">The engine to ask.</param>
    /// <param name="imageTags">The image tags to inspect; empty when the run has no images.</param>
    /// <param name="handler">The HTTP handler for the Toxiproxy probe.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<LiveFingerprintParts> CollectAsync(IDockerCli docker, DockerEngine engine, IReadOnlyList<string> imageTags,
        HttpMessageHandler handler, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(imageTags);
        ArgumentNullException.ThrowIfNull(handler);

        var warnings = new List<string>();
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        var engines = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string tag in imageTags)
        {
            string? digest = await ReadDockerAsync(docker, engine, ["image", "inspect", "--format", "{{.Id}}", tag], ct).ConfigureAwait(false);
            digests[tag] = digest ?? FingerprintBuilder.UnknownPart;
            if (digest is null)
            {
                warnings.Add($"image digest not collected for {tag}: unknown");
            }
        }

        string? server = await ReadDockerAsync(docker, engine, ["version", "--format", "{{.Server.Version}}"], ct).ConfigureAwait(false);
        engines["docker_server"] = server ?? FingerprintBuilder.UnknownPart;
        if (server is null)
        {
            warnings.Add("docker engine version not collected: unknown");
        }

        string? toxiproxy = await ReadToxiproxyVersionAsync(handler, ct).ConfigureAwait(false);
        if (toxiproxy is null)
        {
            warnings.Add("toxiproxy version not collected: unknown");
        }

        return new LiveFingerprintParts(digests, engines, toxiproxy, warnings);
    }

    private static async Task<string?> ReadDockerAsync(IDockerCli docker, DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)
    {
        Result<DockerOutput> run = await docker.RunAsync(engine, args, ct).ConfigureAwait(false);
        if (run.IsFailure || run.Value is null || run.Value.ExitCode != 0)
        {
            return null;
        }

        return Clean(run.Value.StdOut);
    }

    private static async Task<string?> ReadToxiproxyVersionAsync(HttpMessageHandler handler, CancellationToken ct)
    {
        if (!LabHosts.IsLabEndpoint(ToxiproxyVersionUri))
        {
            return null;
        }

        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = ProbeTimeout };
        try
        {
            using HttpResponseMessage response = await client
                .GetAsync(ToxiproxyVersionUri, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return Clean(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return null;
        }
    }

    private static string? Clean(string? text)
    {
        string value = (text ?? string.Empty).Trim();
        return SafeText.IsMatch(value) ? value : null;
    }
}

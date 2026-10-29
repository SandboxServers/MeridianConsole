using System.Text.Json;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Fuzz;

/// <summary>
/// One fuzz target (C10 "Fuzz targets"): one route, with the <c>Dhadgar.Contracts</c> type its body is built from (R-CONTRACT).
/// </summary>
/// <param name="Name">The target name used by <c>fuzz_run</c> and in row ids (<c>fuzz/&lt;target&gt;/&lt;n&gt;</c>).</param>
/// <param name="Via">The lab target for <c>LabHttp</c> (<c>gateway</c> sets the probe Host header).</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="PathTemplate">The concrete path; the route has no parameters.</param>
/// <param name="BodyType">The name of the <c>Dhadgar.Contracts</c> body type.</param>
/// <param name="Auth">The auth the route needs (none for enroll).</param>
public sealed record FuzzTarget(string Name, string Via, string Method, string PathTemplate, string BodyType, string Auth);

/// <summary>
/// The fuzz target table (C10). L28 ships <c>enroll</c> only; L38 and L44 add their own entries here.
/// </summary>
public static class FuzzTargets
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The <c>enroll</c> target: <c>POST /api/v1/agents/enroll</c>, <c>EnrollNodeRequest</c>, through the Gateway.</summary>
    public static readonly FuzzTarget Enroll = new(
        "enroll", "gateway", "POST", "/api/v1/agents/enroll", nameof(EnrollNodeRequest), "none");

    /// <summary>Every target, in table order.</summary>
    public static IReadOnlyList<FuzzTarget> All { get; } = [Enroll];

    /// <summary>Finds a target by name. An unknown name is <c>[Lab.NotFound]</c>, never a throw.</summary>
    public static Result<FuzzTarget> Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (FuzzTarget target in All)
        {
            if (string.Equals(target.Name, name, StringComparison.Ordinal))
            {
                return Result<FuzzTarget>.Success(target);
            }
        }

        return Result<FuzzTarget>.Failure("[Lab.NotFound] no fuzz target has that name");
    }

    /// <summary>
    /// The valid body for a target, built from its contract type and serialised with the product's JSON options
    /// (<see cref="JsonSerializerDefaults.Web"/>, camelCase, as the Nodes endpoint reads it). The mutators start from these bytes.
    /// </summary>
    public static byte[] ValidBody(FuzzTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!string.Equals(target.BodyType, nameof(EnrollNodeRequest), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("no body builder for " + target.BodyType);
        }

        var body = new EnrollNodeRequest(
            Token: "fuzz-placeholder-token",
            Platform: "linux",
            Hardware: new HardwareInventoryRequest(
                Hostname: "fuzz-host",
                OsVersion: "fuzz-os",
                CpuCores: 2,
                MemoryBytes: 2L * 1024 * 1024 * 1024,
                DiskBytes: 20L * 1024 * 1024 * 1024,
                NetworkInterfaces: [new NetworkInterfaceInfo("eth0", "02-00-00-00-00-01", ["10.0.0.2"])]),
            PublicKey: "ZnV6ei1wdWJsaWMta2V5",
            AgentVersion: "0.0.0-fuzz");

        return JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
    }
}

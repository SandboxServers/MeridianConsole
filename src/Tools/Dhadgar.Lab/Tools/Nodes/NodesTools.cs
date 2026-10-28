using System.Text.Json;
using Dhadgar.Lab.Http;
using Dhadgar.Lab.Nodes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Nodes;

/// <summary>
/// Arguments of <c>nodes_token_mint</c>. The organization defaults to the lab organization; other organizations arrive in L56.
/// </summary>
public sealed record NodesTokenMintArgs(string? Label = null, int? ValidityMinutes = null, Guid? OrganizationId = null);

/// <summary>
/// Arguments of <c>nodes_enroll_probe</c>. <c>key_ref</c> reuses a vaulted key for a retry; otherwise a new key is generated.
/// </summary>
public sealed record NodesEnrollProbeArgs(string TokenRef, string Platform, string Via, string? Hostname = null, string? KeyRef = null);

/// <summary>
/// Arguments of <c>nodes_cert_time_shift</c>. <c>fields</c> defaults to all four timestamps.
/// </summary>
public sealed record NodesCertTimeShiftArgs(Guid NodeId, int ShiftMinutes, IReadOnlyList<string>? Fields = null);

/// <summary>
/// Result of <c>nodes_token_mint</c>. The plain token is never returned; <c>token_ref</c> names it in the vault.
/// </summary>
public sealed record NodesTokenMintResult(string TokenRef, Guid TokenId, DateTimeOffset ExpiresAt, Guid OrganizationId);

/// <summary>
/// Result of <c>nodes_enroll_probe</c>. <c>status</c> is the HTTP status (0 when no response arrived).
/// </summary>
public sealed record NodesEnrollProbeResult(int Status, Guid? NodeId, string? CertificateThumbprint, string KeyRef, string? Error);

/// <summary>
/// Result of <c>nodes_cert_time_shift</c>.
/// </summary>
public sealed record NodesCertTimeShiftResult(int RowsUpdated);

/// <summary>
/// <c>nodes_token_mint</c> (Leased): mints an enrollment token through the Nodes product and returns its vault reference.
/// </summary>
public sealed class NodesTokenMintTool : LabTool<NodesTokenMintArgs>
{
    private const int MinValidityMinutes = 1;
    private const int MaxValidityMinutes = 1440;
    private const int DefaultValidityMinutes = 60;

    private readonly INodesLabAdapter _nodes;

    /// <summary>
    /// Creates the tool over the Nodes adapter.
    /// </summary>
    public NodesTokenMintTool(INodesLabAdapter nodes)
    {
        _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
    }

    /// <inheritdoc />
    public override string Name => "nodes_token_mint";

    /// <inheritdoc />
    public override string Description => "Mint an enrollment token through the Nodes product; returns token_ref (the plain token stays in the lab vault).";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(NodesTokenMintArgs args, LabCallContext context, CancellationToken ct)
    {
        int validity = args.ValidityMinutes ?? DefaultValidityMinutes;
        if (validity is < MinValidityMinutes or > MaxValidityMinutes)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] validity_minutes must be 1 to 1440");
        }

        Guid organizationId = args.OrganizationId ?? LabIds.OrganizationId;
        if (organizationId != LabIds.OrganizationId)
        {
            return Result<object>.Failure("[Lab.Unavailable] organizations other than the lab default arrive in L56");
        }

        string label = string.IsNullOrWhiteSpace(args.Label) ? "lab-mint" : args.Label.Trim();
        Result<MintedToken> minted = await _nodes
            .MintTokenAsync(organizationId, label, TimeSpan.FromMinutes(validity), ct)
            .ConfigureAwait(false);
        if (minted.IsFailure)
        {
            return Result<object>.Failure(minted.Error);
        }

        return Result<object>.Success(new NodesTokenMintResult(
            minted.Value.TokenRef,
            minted.Value.TokenId,
            minted.Value.ExpiresAt,
            organizationId));
    }
}

/// <summary>
/// <c>nodes_enroll_probe</c> (Leased): posts an agent enrollment through the chosen route and reports the outcome.
/// </summary>
public sealed class NodesEnrollProbeTool : LabTool<NodesEnrollProbeArgs>
{
    private const string GatewayPath = "/api/v1/agents/enroll";
    private const string NodesPath = "/agents/enroll";

    // The product's enrollment response and request use the ASP.NET default (camelCase) JSON names.
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    private readonly LabHttp _http;
    private readonly SecretVault _vault;

    /// <summary>
    /// Creates the tool over the lab HTTP client and the vault.
    /// </summary>
    public NodesEnrollProbeTool(LabHttp http, SecretVault vault)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    /// <inheritdoc />
    public override string Name => "nodes_enroll_probe";

    /// <inheritdoc />
    public override string Description => "Post an agent enrollment via gateway, agent_ingress or nodes; returns the HTTP status, node_id and key_ref.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(NodesEnrollProbeArgs args, LabCallContext context, CancellationToken ct)
    {
        string path;
        switch (args.Via)
        {
            case "gateway":
            case "agent_ingress":
                path = GatewayPath;
                break;
            case "nodes":
                path = NodesPath;
                break;
            default:
                return Result<object>.Failure("[Lab.InvalidArgument] via must be gateway, agent_ingress or nodes");
        }

        Result<Dhadgar.Contracts.Nodes.EnrollNodeRequest> request = EnrollProbe.BuildRequest(
            _vault,
            args.TokenRef,
            args.Platform,
            args.Hostname ?? EnrollProbe.DefaultHostname,
            args.KeyRef,
            out string keyRef);
        if (request.IsFailure)
        {
            return Result<object>.Failure(request.Error);
        }

        JsonElement body = JsonSerializer.SerializeToElement(request.Value, WireOptions);
        Result<HttpProbeResult> sent = await _http
            .SendAsync(args.Via, "POST", path, null, body, ct)
            .ConfigureAwait(false);
        if (sent.IsFailure)
        {
            return Result<object>.Failure(sent.Error);
        }

        HttpProbeResult response = sent.Value;
        Guid? nodeId = null;
        string? thumbprint = null;
        if (response.Status == 201 && response.Body is { ValueKind: JsonValueKind.Object } json)
        {
            if (json.TryGetProperty("nodeId", out JsonElement id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out Guid parsed))
            {
                nodeId = parsed;
            }

            if (json.TryGetProperty("certificateThumbprint", out JsonElement print) && print.ValueKind == JsonValueKind.String)
            {
                thumbprint = print.GetString();
            }
        }

        return Result<object>.Success(new NodesEnrollProbeResult(response.Status, nodeId, thumbprint, keyRef, response.Error));
    }
}

/// <summary>
/// <c>nodes_cert_time_shift</c> (Leased): moves a node's certificate timestamps back in time (never forward).
/// </summary>
public sealed class NodesCertTimeShiftTool : LabTool<NodesCertTimeShiftArgs>
{
    private const int MinShiftMinutes = -525600;
    private const int MaxShiftMinutes = 0;

    private readonly INodesLabAdapter _nodes;

    /// <summary>
    /// Creates the tool over the Nodes adapter.
    /// </summary>
    public NodesCertTimeShiftTool(INodesLabAdapter nodes)
    {
        _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
    }

    /// <inheritdoc />
    public override string Name => "nodes_cert_time_shift";

    /// <inheritdoc />
    public override string Description => "Shift a node's certificate times back by shift_minutes (0 to -525600) to simulate expiry and skew.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(NodesCertTimeShiftArgs args, LabCallContext context, CancellationToken ct)
    {
        if (args.ShiftMinutes > MaxShiftMinutes)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] shift_minutes must not be positive");
        }

        if (args.ShiftMinutes < MinShiftMinutes)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] shift_minutes must be -525600 to 0");
        }

        var fields = new List<CertTimeField>();
        foreach (string name in args.Fields ?? AllFields)
        {
            Result<CertTimeField> field = ParseField(name);
            if (field.IsFailure)
            {
                return Result<object>.Failure(field.Error);
            }

            fields.Add(field.Value);
        }

        if (fields.Count == 0)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] at least one certificate field is required");
        }

        Result<int> shifted = await _nodes
            .ShiftCertificateTimesAsync(args.NodeId, TimeSpan.FromMinutes(args.ShiftMinutes), fields, ct)
            .ConfigureAwait(false);
        return shifted.IsFailure
            ? Result<object>.Failure(shifted.Error)
            : Result<object>.Success(new NodesCertTimeShiftResult(shifted.Value));
    }

    private static readonly string[] AllFields = ["issued_at", "not_before", "not_after", "grace_until"];

    private static Result<CertTimeField> ParseField(string name) => name switch
    {
        "issued_at" => Result<CertTimeField>.Success(CertTimeField.IssuedAt),
        "not_before" => Result<CertTimeField>.Success(CertTimeField.NotBefore),
        "not_after" => Result<CertTimeField>.Success(CertTimeField.NotAfter),
        "grace_until" => Result<CertTimeField>.Success(CertTimeField.GraceUntil),
        _ => Result<CertTimeField>.Failure("[Lab.InvalidArgument] fields must be issued_at, not_before, not_after or grace_until"),
    };
}

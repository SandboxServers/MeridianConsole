using System.Text.Json;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Lab.Http;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// The <c>http</c> source (C7): one probe through <see cref="LabHttp"/>, which composes the URL on the loopback host, checks it
/// with <c>LabHosts.IsLabEndpoint</c>, never follows redirects and bypasses proxies. <c>${secret:...}</c>
/// stays in the body for the vault to resolve. Result shape: <c>{status, headers, body}</c>.
/// </summary>
public sealed class HttpSource : IClauseSource
{
    private readonly LabHttp _http;

    /// <summary>
    /// Creates the source over the lab probe client.
    /// </summary>
    public HttpSource(LabHttp http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public string Name => "http";

    /// <inheritdoc />
    public async Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)
    {
        Result<string> via = ClauseFields.Required(clause.SourceFields, "via");
        if (via.IsFailure)
        {
            return Result<JsonElement>.Failure(via.Error);
        }

        Result<string> method = ClauseFields.Required(clause.SourceFields, "method");
        if (method.IsFailure)
        {
            return Result<JsonElement>.Failure(method.Error);
        }

        Result<string> path = ClauseFields.Required(clause.SourceFields, "path");
        if (path.IsFailure)
        {
            return Result<JsonElement>.Failure(path.Error);
        }

        Result<string> expandedPath = vars.Expand(path.Value);
        if (expandedPath.IsFailure)
        {
            return Result<JsonElement>.Failure(expandedPath.Error);
        }

        Result<Dictionary<string, string>> headers = ClauseFields.StringMap(clause.SourceFields, "headers");
        if (headers.IsFailure)
        {
            return Result<JsonElement>.Failure(headers.Error);
        }

        var expandedHeaders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> header in headers.Value)
        {
            Result<string> value = vars.Expand(header.Value);
            if (value.IsFailure)
            {
                return Result<JsonElement>.Failure(value.Error);
            }

            expandedHeaders[header.Key] = value.Value;
        }

        JsonElement? body = null;
        if (clause.SourceFields.TryGetValue("body", out JsonElement rawBody))
        {
            Result<JsonElement> expandedBody = vars.Expand(rawBody);
            if (expandedBody.IsFailure)
            {
                return Result<JsonElement>.Failure(expandedBody.Error);
            }

            body = expandedBody.Value;
        }

        Result<HttpProbeResult> probe = await _http.SendAsync(via.Value, method.Value, expandedPath.Value, expandedHeaders, body, ct);
        if (probe.IsFailure)
        {
            return Result<JsonElement>.Failure(probe.Error);
        }

        return Result<JsonElement>.Success(JsonSerializer.SerializeToElement(new
        {
            status = probe.Value.Status,
            headers = probe.Value.Headers,
            body = probe.Value.Body,
        }));
    }
}

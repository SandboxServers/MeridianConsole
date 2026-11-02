using System.Text.Json;
using Dhadgar.Lab.Http;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Http;

/// <summary>
/// <c>http_request</c> arguments (C4). <c>body</c> is JSON; <c>${secret:&lt;ref&gt;}</c> placeholders are resolved in the vault.
/// </summary>
public sealed record HttpRequestArgs(string Via, string Method, string Path, Dictionary<string, string>? Headers = null, JsonElement? Body = null);

/// <summary>
/// <c>http_request</c> (Leased): one HTTP probe to the gateway, agent ingress (through Toxiproxy) or a lab service. Lab endpoints only.
/// </summary>
public sealed class HttpRequestTool : LabTool<HttpRequestArgs>
{
    private readonly LabHttp _http;

    /// <summary>
    /// Creates the tool over the probe client.
    /// </summary>
    public HttpRequestTool(LabHttp http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public override string Name => "http_request";

    /// <inheritdoc />
    public override string Description => "Send one lab-only HTTP request via gateway, agent_ingress, sink or a lab service. Redirects are not followed; ${secret:<ref>} placeholders in the JSON body are resolved and redacted in the reply.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(HttpRequestArgs args, LabCallContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (string.IsNullOrWhiteSpace(args.Via) || string.IsNullOrWhiteSpace(args.Method) || string.IsNullOrWhiteSpace(args.Path))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] via, method and path are required");
        }

        Result<HttpProbeResult> result = await _http.SendAsync(args.Via, args.Method, args.Path, args.Headers, args.Body, ct).ConfigureAwait(false);
        return result.IsFailure ? Result<object>.Failure(result.Error) : Result<object>.Success(result.Value);
    }
}

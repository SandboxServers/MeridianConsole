using System.Text.Json;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// One lab tool. Every tool is registered in DI and found by name; the dispatcher is the only caller.
/// </summary>
public interface ILabTool
{
    /// <summary>
    /// The tool name, for example <c>lab_status</c>. Unique across the registry.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// A short description for MCP clients.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// The JSON Schema of the arguments.
    /// </summary>
    JsonElement InputSchema { get; }

    /// <summary>
    /// Runs the tool. Failures are returned as <c>[Lab.Code] message</c>; nothing is thrown for them.
    /// </summary>
    Task<Result<JsonElement>> InvokeAsync(JsonElement arguments, LabCallContext context, CancellationToken ct);
}

/// <summary>
/// Base for every tool. Parses <typeparamref name="TArgs"/> with <see cref="LabJson.Options"/>, builds the input schema
/// from the same type, and maps a parse failure to <c>[Lab.InvalidArgument]</c>.
/// </summary>
public abstract class LabTool<TArgs> : ILabTool where TArgs : class
{
    /// <summary>
    /// Creates the tool and its input schema.
    /// </summary>
    protected LabTool()
    {
        InputSchema = LabJson.SchemaFor<TArgs>();
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public JsonElement InputSchema { get; }

    /// <summary>
    /// The tool's work once the arguments parsed. Returns a failure for every expected error.
    /// </summary>
    protected abstract Task<Result<object>> ExecuteAsync(TArgs args, LabCallContext context, CancellationToken ct);

    /// <inheritdoc />
    public async Task<Result<JsonElement>> InvokeAsync(JsonElement arguments, LabCallContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A call with no arguments is an empty object; anything else must match TArgs exactly.
        string raw = arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "{}" : arguments.GetRawText();

        TArgs? args;
        try
        {
            args = JsonSerializer.Deserialize<TArgs>(raw, LabJson.Options);
        }
        catch (JsonException)
        {
            // The message is not returned: it can echo an argument value.
            return Result<JsonElement>.Failure("[Lab.InvalidArgument] arguments do not match the tool's schema");
        }

        if (args is null)
        {
            return Result<JsonElement>.Failure("[Lab.InvalidArgument] arguments do not match the tool's schema");
        }

        Result<object> result = await ExecuteAsync(args, context, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result<JsonElement>.Failure(result.Error);
        }

        return Result<JsonElement>.Success(JsonSerializer.SerializeToElement(result.Value, LabJson.Options));
    }
}

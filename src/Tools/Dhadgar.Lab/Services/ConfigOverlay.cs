using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Services;

/// <summary>
/// Per-service config overlay kept in <c>config/&lt;service&gt;.json</c> under <see cref="LabPaths.Config"/> (L09). A key is
/// accepted only when the adapter's <see cref="ILabServiceAdapter.OverlayKeys"/> (the C4 allowlist) names it. The overlay
/// is applied at the next container start, never to a running container.
/// </summary>
public sealed class ConfigOverlay
{
    /// <summary>Longest overlay value the lab accepts.</summary>
    public const int MaxValueLength = 256;

    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;

    /// <summary>
    /// Creates the overlay store over the lab layout and the product path validator.
    /// </summary>
    public ConfigOverlay(LabPaths paths, IPathValidator validator)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <summary>
    /// Reads the overlay for a service. A missing file is an empty overlay.
    /// </summary>
    public Result<IReadOnlyDictionary<string, string>> Read(ILabServiceAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        Result<string> path = OverlayPath(adapter);
        if (path.IsFailure)
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure(path.Error);
        }

        if (!File.Exists(path.Value))
        {
            return Result<IReadOnlyDictionary<string, string>>.Success(new Dictionary<string, string>(StringComparer.Ordinal));
        }

        try
        {
            string text = File.ReadAllText(path.Value);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.Io] the config overlay is not a JSON object");
            }

            var overlay = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } value)
                {
                    overlay[property.Name] = value;
                }
            }

            return Result<IReadOnlyDictionary<string, string>>.Success(overlay);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.Io] the config overlay could not be read");
        }
    }

    /// <summary>
    /// Sets or clears (<paramref name="value"/> null) one overlay key and returns the resulting overlay.
    /// Fails with <c>[Lab.InvalidArgument] key not allowed</c> for a key outside the adapter's allowlist.
    /// </summary>
    public Result<IReadOnlyDictionary<string, string>> Set(ILabServiceAdapter adapter, string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (string.IsNullOrWhiteSpace(key) || !adapter.OverlayKeys.Contains(key))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] key not allowed");
        }

        if (value is not null && (value.Length > MaxValueLength || value.Any(char.IsControl)))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] value is not allowed");
        }

        Result<IReadOnlyDictionary<string, string>> current = Read(adapter);
        if (current.IsFailure)
        {
            return current;
        }

        var overlay = new Dictionary<string, string>(current.Value!, StringComparer.Ordinal);
        if (value is null)
        {
            overlay.Remove(key);
        }
        else
        {
            overlay[key] = value;
        }

        Result written = Write(adapter, overlay);
        return written.IsFailure
            ? Result<IReadOnlyDictionary<string, string>>.Failure(written.Error)
            : Result<IReadOnlyDictionary<string, string>>.Success(overlay);
    }

    /// <summary>
    /// Maps overlay keys (<c>Logging:LogLevel:Default</c>) to container environment names (<c>Logging__LogLevel__Default</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ToEnvironment(IReadOnlyDictionary<string, string> overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in overlay)
        {
            env[entry.Key.Replace(":", "__", StringComparison.Ordinal)] = entry.Value;
        }

        return env;
    }

    private Result<string> OverlayPath(ILabServiceAdapter adapter) =>
        _validator.ValidatePath(Path.Combine(_paths.Config, $"{adapter.Name}.json"), [_paths.Config]);

    private Result Write(ILabServiceAdapter adapter, IReadOnlyDictionary<string, string> overlay)
    {
        Result<string> path = OverlayPath(adapter);
        if (path.IsFailure)
        {
            return Result.Failure(path.Error);
        }

        try
        {
            Directory.CreateDirectory(_paths.Config);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (KeyValuePair<string, string> entry in overlay.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(entry.Key, entry.Value);
                }

                writer.WriteEndObject();
            }

            File.WriteAllBytes(path.Value, stream.ToArray());
            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure("[Lab.Io] the config overlay could not be written");
        }
    }
}

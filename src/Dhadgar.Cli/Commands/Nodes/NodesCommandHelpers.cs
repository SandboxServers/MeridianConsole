using Refit;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;

namespace Dhadgar.Cli.Commands.Nodes;

internal static class NodesCommandHelpers
{
    private static readonly bool IncludeErrorDetails =
        string.Equals(Environment.GetEnvironmentVariable("DHADGAR_CLI_DEBUG"), "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable("DHADGAR_CLI_DEBUG"), "true", StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions InputOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryEnsureAuthenticated(CliConfig config, out int exitCode)
    {
        if (config.IsAuthenticated())
        {
            exitCode = 0;
            return true;
        }

        exitCode = WriteError("not_authenticated", "Run 'dhadgar auth login' first.");
        return false;
    }

    public static int WriteError(string code, string message, object? details = null)
    {
        WriteJson(new { error = code, message, details = SanitizeDetails(details) });
        return 1;
    }

    public static int WriteApiError(ApiException ex)
    {
        object? details = null;

        if (!string.IsNullOrWhiteSpace(ex.Content))
        {
            try
            {
                details = JsonSerializer.Deserialize<JsonElement>(ex.Content, InputOptions);
            }
            catch (JsonException)
            {
                details = ex.Content;
            }
        }

        var statusCode = (int)ex.StatusCode;
        var reason = ex.StatusCode.ToString();

        return WriteError("http_error", $"{statusCode} {reason}", details);
    }

    public static void WriteJson(object value)
    {
        var json = JsonSerializer.Serialize(value, OutputOptions);
        Console.Out.WriteLine(json);
    }

    public static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public static string FormatStatus(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "online" => "Online",
            "offline" => "Offline",
            "degraded" => "Degraded",
            "maintenance" => "Maintenance",
            "enrolling" => "Enrolling",
            "decommissioned" => "Decommissioned",
            _ => status
        };
    }

    private static object? SanitizeDetails(object? details)
    {
        if (details is null)
        {
            return null;
        }

        if (IncludeErrorDetails)
        {
            return details;
        }

        if (details is JsonElement element)
        {
            return ExtractSafeDetails(element);
        }

        if (details is string)
        {
            return null;
        }

        return null;
    }

    private static Dictionary<string, string>? ExtractSafeDetails(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var safe = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!IsSafeErrorField(property.Name))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            safe[property.Name] = property.Value.ToString();
        }

        return safe.Count == 0 ? null : safe;
    }

    private static readonly HashSet<string> SafeErrorFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "error",
        "error_description",
        "error_uri",
        "message",
        "code",
        "correlationId",
        "correlation_id",
        "traceId",
        "trace_id",
        "requestId",
        "request_id"
    };

    private static bool IsSafeErrorField(string name) => SafeErrorFields.Contains(name);
}

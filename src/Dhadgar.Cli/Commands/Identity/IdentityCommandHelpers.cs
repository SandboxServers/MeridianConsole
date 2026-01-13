using Refit;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;

namespace Dhadgar.Cli.Commands.Identity;

internal static class IdentityCommandHelpers
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

    public static T? Deserialize<T>(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(body, InputOptions);
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

    private static bool IsSafeErrorField(string name)
    {
        return name.Equals("error", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("error_description", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("error_uri", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("message", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("code", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("correlation_id", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("trace_id", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("request_id", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("requestId", StringComparison.OrdinalIgnoreCase);
    }
}

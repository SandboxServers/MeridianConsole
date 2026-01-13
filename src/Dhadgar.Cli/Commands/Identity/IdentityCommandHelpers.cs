using Refit;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dhadgar.Cli.Configuration;

namespace Dhadgar.Cli.Commands.Identity;

internal static class IdentityCommandHelpers
{
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
        WriteJson(new { error = code, message, details });
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
}

using System.Net.Http.Headers;
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

    public static HttpClient CreateClient(CliConfig config)
    {
        var client = new HttpClient();

        if (!string.IsNullOrWhiteSpace(config.AccessToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.AccessToken);
        }

        return client;
    }

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

    public static async Task<int> WriteJsonResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                WriteJson(new { ok = true });
                return 0;
            }

            var element = JsonSerializer.Deserialize<JsonElement>(body, InputOptions);
            WriteJson(element);
            return 0;
        }

        return WriteHttpError(response, body);
    }

    public static int WriteError(string code, string message, object? details = null)
    {
        WriteJson(new { error = code, message, details });
        return 1;
    }

    public static int WriteHttpError(HttpResponseMessage response, string body)
    {
        object? details = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                details = JsonSerializer.Deserialize<JsonElement>(body, InputOptions);
            }
            catch (JsonException)
            {
                details = body;
            }
        }

        return WriteError("http_error", $"{(int)response.StatusCode} {response.ReasonPhrase}", details);
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

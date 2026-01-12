using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dhadgar.Cli.Configuration;
using Spectre.Console;

namespace Dhadgar.Cli.Infrastructure;

public sealed class AuthenticatedHttpClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly CliConfig _config;

    public AuthenticatedHttpClient(CliConfig config)
    {
        _config = config;
        _client = new HttpClient();

        if (!string.IsNullOrWhiteSpace(config.AccessToken))
        {
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.AccessToken);
        }
    }

    public async Task<TResponse?> GetAsync<TResponse>(string url, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetAsync(url, ct);
            await EnsureSuccessWithDetails(response);
            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]HTTP Error:[/] {ex.Message}");
            return default;
        }
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string url,
        TRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _client.PostAsJsonAsync(url, request, ct);
            await EnsureSuccessWithDetails(response);
            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]HTTP Error:[/] {ex.Message}");
            return default;
        }
    }

    public async Task<bool> PostAsync<TRequest>(string url, TRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.PostAsJsonAsync(url, request, ct);
            await EnsureSuccessWithDetails(response);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]HTTP Error:[/] {ex.Message}");
            return false;
        }
    }

    public async Task<TResponse?> PutAsync<TRequest, TResponse>(
        string url,
        TRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _client.PutAsJsonAsync(url, request, ct);
            await EnsureSuccessWithDetails(response);
            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]HTTP Error:[/] {ex.Message}");
            return default;
        }
    }

    public async Task<TResponse?> PatchAsync<TRequest, TResponse>(
        string url,
        TRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _client.PatchAsJsonAsync(url, request, ct);
            await EnsureSuccessWithDetails(response);
            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]HTTP Error:[/] {ex.Message}");
            return default;
        }
    }

    public async Task<bool> DeleteAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.DeleteAsync(url, ct);
            await EnsureSuccessWithDetails(response);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]HTTP Error:[/] {ex.Message}");
            return false;
        }
    }

    private static async Task EnsureSuccessWithDetails(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var content = await response.Content.ReadAsStringAsync();
        var statusCode = (int)response.StatusCode;

        // Try to parse error as JSON
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("error", out var errorProp))
            {
                throw new HttpRequestException(
                    $"[{statusCode}] {response.ReasonPhrase}: {errorProp.GetString()}");
            }
        }
        catch (JsonException)
        {
            // Not JSON, use raw content
        }

        throw new HttpRequestException($"[{statusCode}] {response.ReasonPhrase}: {content}");
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}

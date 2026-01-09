using System.Net.Http.Headers;
using System.Text.Json;

namespace Dhadgar.Gateway.Endpoints;

/// <summary>
/// Diagnostic endpoints for verifying end-to-end service connectivity.
/// Only available in Development environment.
/// </summary>
public static class DiagnosticsEndpoints
{
    public static void MapDiagnosticsEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        var group = app.MapGroup("/diagnostics")
            .WithTags("Diagnostics")
            .AllowAnonymous();

        group.MapGet("/integration", RunIntegrationCheck)
            .WithName("IntegrationCheck")
            .WithDescription("Verifies end-to-end connectivity: Gateway -> Identity -> Secrets");

        group.MapGet("/services", CheckServiceHealth)
            .WithName("ServiceHealth")
            .WithDescription("Checks health of all configured backend services");
    }

    private static async Task<IResult> RunIntegrationCheck(
        IConfiguration configuration,
        IHttpClientFactory? httpClientFactory,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var result = new IntegrationCheckResult
        {
            Timestamp = DateTime.UtcNow,
            Steps = new List<IntegrationStep>()
        };

        // HttpClient from IHttpClientFactory should NOT be disposed - factory manages lifetime.
        // Fallback HttpClient for dev diagnostics only; suppressing CA2000 is intentional.
#pragma warning disable CA2000
        var httpClient = httpClientFactory?.CreateClient() ?? new HttpClient();
#pragma warning restore CA2000

        try
        {
            // Step 1: Check Gateway health (self)
            result.Steps.Add(new IntegrationStep
            {
                Name = "Gateway",
                Status = "OK",
                Message = "Gateway is responding",
                DurationMs = 0
            });

            // Step 2: Get Identity service URL from YARP config
            var identityUrl = configuration["ReverseProxy:Clusters:identity:Destinations:d1:Address"]
                ?? "http://localhost:5010/";

            var secretsUrl = configuration["ReverseProxy:Clusters:secrets:Destinations:d1:Address"]
                ?? "http://localhost:5110/";

            // Step 2: Check Identity health
            var identityStep = await CheckServiceAsync(
                httpClient,
                "Identity",
                $"{identityUrl.TrimEnd('/')}/healthz",
                logger,
                ct);
            result.Steps.Add(identityStep);

            if (identityStep.Status != "OK")
            {
                result.OverallStatus = "FAILED";
                result.FailureReason = "Identity service is not healthy";
                return Results.Ok(result);
            }

            // Step 3: Get token from Identity using client credentials flow
            var clientId = configuration["OpenIddict:DevClient:ClientId"] ?? "dev-client";
            var clientSecret = configuration["OpenIddict:DevClient:ClientSecret"] ?? "dev-secret";

            var tokenStep = await GetTokenAsync(
                httpClient,
                $"{identityUrl.TrimEnd('/')}/connect/token",
                clientId,
                clientSecret,
                logger,
                ct);
            result.Steps.Add(tokenStep);

            if (tokenStep.Status != "OK" || string.IsNullOrEmpty(tokenStep.Token))
            {
                result.OverallStatus = "FAILED";
                result.FailureReason = "Failed to obtain token from Identity service";
                return Results.Ok(result);
            }

            // Step 4: Check Secrets service health
            var secretsHealthStep = await CheckServiceAsync(
                httpClient,
                "Secrets",
                $"{secretsUrl.TrimEnd('/')}/healthz",
                logger,
                ct);
            result.Steps.Add(secretsHealthStep);

            if (secretsHealthStep.Status != "OK")
            {
                result.OverallStatus = "FAILED";
                result.FailureReason = "Secrets service is not healthy";
                return Results.Ok(result);
            }

            // Step 5: Call Secrets service with token (note: this will likely return 403
            // because dev-client doesn't have permission claims, but it proves the auth flow)
            var secretsStep = await CallSecretsServiceAsync(
                httpClient,
                $"{secretsUrl.TrimEnd('/')}/api/v1/secrets/oauth",
                tokenStep.Token!,
                logger,
                ct);
            result.Steps.Add(secretsStep);

            // Determine overall status
            if (result.Steps.All(s => s.Status == "OK" || s.Status == "EXPECTED_FORBIDDEN"))
            {
                result.OverallStatus = "OK";
                result.Message = "End-to-end integration verified: Gateway -> Identity -> Secrets";
            }
            else
            {
                result.OverallStatus = "PARTIAL";
                result.Message = "Some integration steps failed - check individual step results";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Integration check failed");
            result.OverallStatus = "ERROR";
            result.FailureReason = ex.Message;
        }

        return Results.Ok(result);
    }

    private static async Task<IntegrationStep> CheckServiceAsync(
        HttpClient httpClient,
        string serviceName,
        string healthUrl,
        ILogger logger,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var response = await httpClient.GetAsync(healthUrl, ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new IntegrationStep
                {
                    Name = $"{serviceName} Health",
                    Status = "OK",
                    Message = $"{serviceName} service is healthy",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            return new IntegrationStep
            {
                Name = $"{serviceName} Health",
                Status = "FAILED",
                Message = $"{serviceName} returned {response.StatusCode}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Failed to check {ServiceName} health at {Url}", serviceName, healthUrl);
            return new IntegrationStep
            {
                Name = $"{serviceName} Health",
                Status = "FAILED",
                Message = $"Connection failed: {ex.Message}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    private static async Task<IntegrationStep> GetTokenAsync(
        HttpClient httpClient,
        string tokenUrl,
        string clientId,
        string clientSecret,
        ILogger logger,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = "openid"
            });

            var response = await httpClient.PostAsync(tokenUrl, content, ct);
            sw.Stop();

            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
                var accessToken = tokenResponse.GetProperty("access_token").GetString();

                return new IntegrationStep
                {
                    Name = "Token Acquisition",
                    Status = "OK",
                    Message = "Successfully obtained access token from Identity service",
                    DurationMs = sw.ElapsedMilliseconds,
                    Token = accessToken
                };
            }

            logger.LogWarning("Token request failed: {StatusCode} - {Body}", response.StatusCode, responseBody);
            return new IntegrationStep
            {
                Name = "Token Acquisition",
                Status = "FAILED",
                Message = $"Token request failed: {response.StatusCode}",
                DurationMs = sw.ElapsedMilliseconds,
                Details = responseBody
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Failed to get token from {Url}", tokenUrl);
            return new IntegrationStep
            {
                Name = "Token Acquisition",
                Status = "FAILED",
                Message = $"Token request error: {ex.Message}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    private static async Task<IntegrationStep> CallSecretsServiceAsync(
        HttpClient httpClient,
        string secretsUrl,
        string accessToken,
        ILogger logger,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, secretsUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await httpClient.SendAsync(request, ct);
            sw.Stop();

            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                return new IntegrationStep
                {
                    Name = "Secrets Service Call",
                    Status = "OK",
                    Message = "Successfully called Secrets service with token",
                    DurationMs = sw.ElapsedMilliseconds,
                    Details = "Secrets service returned data (content hidden)"
                };
            }

            // 403 is expected because dev-client doesn't have permission claims
            // This still proves the auth flow works
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return new IntegrationStep
                {
                    Name = "Secrets Service Call",
                    Status = "EXPECTED_FORBIDDEN",
                    Message = "Token accepted but permission denied (expected - dev-client lacks permission claims)",
                    DurationMs = sw.ElapsedMilliseconds,
                    Details = "This confirms: Gateway -> Identity (token) -> Secrets (auth validated) flow works"
                };
            }

            // 401 means token was rejected
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new IntegrationStep
                {
                    Name = "Secrets Service Call",
                    Status = "FAILED",
                    Message = "Token was rejected by Secrets service - check JWT validation config",
                    DurationMs = sw.ElapsedMilliseconds,
                    Details = responseBody
                };
            }

            return new IntegrationStep
            {
                Name = "Secrets Service Call",
                Status = "FAILED",
                Message = $"Unexpected response: {response.StatusCode}",
                DurationMs = sw.ElapsedMilliseconds,
                Details = responseBody
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Failed to call Secrets service at {Url}", secretsUrl);
            return new IntegrationStep
            {
                Name = "Secrets Service Call",
                Status = "FAILED",
                Message = $"Secrets call error: {ex.Message}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    private static async Task<IResult> CheckServiceHealth(
        IConfiguration configuration,
        IHttpClientFactory? httpClientFactory,
        ILogger<Program> logger,
        CancellationToken ct)
    {
#pragma warning disable CA2000
        var httpClient = httpClientFactory?.CreateClient() ?? new HttpClient();
#pragma warning restore CA2000
        var results = new Dictionary<string, ServiceHealthResult>();

        // Get all clusters from YARP config
        var clusters = configuration.GetSection("ReverseProxy:Clusters").GetChildren();

        var tasks = new List<Task<(string Name, ServiceHealthResult Result)>>();

        foreach (var cluster in clusters)
        {
            var clusterName = cluster.Key;
            var destinationAddress = cluster.GetSection("Destinations:d1:Address").Value;

            if (string.IsNullOrEmpty(destinationAddress))
            {
                continue;
            }

            tasks.Add(CheckServiceHealthAsync(httpClient, clusterName, destinationAddress, ct));
        }

        var completedTasks = await Task.WhenAll(tasks);

        foreach (var (name, result) in completedTasks)
        {
            results[name] = result;
        }

        var healthy = results.Values.Count(r => r.IsHealthy);
        var total = results.Count;

        return Results.Ok(new
        {
            timestamp = DateTime.UtcNow,
            summary = $"{healthy}/{total} services healthy",
            services = results
        });
    }

    private static async Task<(string Name, ServiceHealthResult Result)> CheckServiceHealthAsync(
        HttpClient httpClient,
        string serviceName,
        string baseAddress,
        CancellationToken ct)
    {
        var healthUrl = $"{baseAddress.TrimEnd('/')}/healthz";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = await httpClient.GetAsync(healthUrl, ct);
            sw.Stop();

            return (serviceName, new ServiceHealthResult
            {
                IsHealthy = response.IsSuccessStatusCode,
                StatusCode = (int)response.StatusCode,
                ResponseTimeMs = sw.ElapsedMilliseconds,
                Url = baseAddress
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (serviceName, new ServiceHealthResult
            {
                IsHealthy = false,
                Error = ex.Message,
                ResponseTimeMs = sw.ElapsedMilliseconds,
                Url = baseAddress
            });
        }
    }
}

public class IntegrationCheckResult
{
    public DateTime Timestamp { get; set; }
    public string OverallStatus { get; set; } = "PENDING";
    public string? Message { get; set; }
    public string? FailureReason { get; set; }
    public IList<IntegrationStep> Steps { get; set; } = new List<IntegrationStep>();
}

public class IntegrationStep
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Message { get; set; }
    public long DurationMs { get; set; }
    public string? Details { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? Token { get; set; }
}

public class ServiceHealthResult
{
    public bool IsHealthy { get; set; }
    public int? StatusCode { get; set; }
    public string? Error { get; set; }
    public long ResponseTimeMs { get; set; }
    public string? Url { get; set; }
}

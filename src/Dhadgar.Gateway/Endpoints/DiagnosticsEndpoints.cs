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

        group.MapGet("/wif", TestWifToken)
            .WithName("TestWif")
            .WithDescription("Tests Workload Identity Federation token issuance and validation");
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
                if (!tokenResponse.TryGetProperty("access_token", out var tokenProperty) ||
                    string.IsNullOrWhiteSpace(tokenProperty.GetString()))
                {
                    logger.LogWarning("Token response missing access_token: {Body}", responseBody);
                    return new IntegrationStep
                    {
                        Name = "Token Acquisition",
                        Status = "FAILED",
                        Message = "Token response did not contain access_token",
                        DurationMs = sw.ElapsedMilliseconds,
                        Details = responseBody
                    };
                }

                var accessToken = tokenProperty.GetString();

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

    private static async Task<IResult> TestWifToken(
        IConfiguration configuration,
        IHttpClientFactory? httpClientFactory,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var result = new WifTestResult
        {
            Timestamp = DateTime.UtcNow,
            Steps = new List<WifTestStep>()
        };

#pragma warning disable CA2000
        var httpClient = httpClientFactory?.CreateClient() ?? new HttpClient();
#pragma warning restore CA2000

        try
        {
            // Step 1: Get Identity service URL
            var identityUrl = configuration["ReverseProxy:Clusters:identity:Destinations:d1:Address"]
                ?? "http://localhost:5010/";
            var issuer = configuration["Auth:Issuer"]
                ?? "https://dev.meridianconsole.com/api/v1/identity";

            result.Issuer = issuer;
            result.Steps.Add(new WifTestStep
            {
                Name = "Configuration",
                Status = "OK",
                Message = $"Using issuer: {issuer}",
                DurationMs = 0
            });

            // Step 2: Get WIF token from Identity
            var clientId = configuration["OpenIddict:DevClient:ClientId"] ?? "dev-client";
            var clientSecret = configuration["OpenIddict:DevClient:ClientSecret"] ?? "dev-secret";

            var tokenStep = await GetWifTokenAsync(
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
                result.Message = "Failed to obtain WIF token from Identity service";
                return Results.Ok(result);
            }

            result.Token = tokenStep.Token;

            // Step 3: Decode and inspect token claims
            var claimsStep = InspectWifToken(tokenStep.Token!, logger);
            result.Steps.Add(claimsStep);
            result.Claims = claimsStep.Claims;

            // Step 4: Verify JWKS endpoint is accessible
            var jwksStep = await VerifyJwksEndpointAsync(
                httpClient,
                $"{identityUrl.TrimEnd('/')}/.well-known/jwks.json",
                logger,
                ct);
            result.Steps.Add(jwksStep);

            // Step 5: Verify OpenID configuration
            var oidcStep = await VerifyOidcConfigurationAsync(
                httpClient,
                $"{identityUrl.TrimEnd('/')}/.well-known/openid-configuration",
                logger,
                ct);
            result.Steps.Add(oidcStep);

            result.OverallStatus = result.Steps.All(s => s.Status == "OK") ? "OK" : "PARTIAL";
            result.Message = result.OverallStatus == "OK"
                ? "WIF token issued successfully and all endpoints verified"
                : "WIF token issued but some verification steps failed";

            result.AzureFederatedCredentialSetup = new AzureFederatedCredentialInstructions
            {
                Issuer = issuer,
                Subject = $"repo:YOUR-ORG/YOUR-REPO:ref:refs/heads/main",
                Audience = "api://AzureADTokenExchange"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WIF test failed");
            result.OverallStatus = "ERROR";
            result.Message = ex.Message;
        }

        return Results.Ok(result);
    }

    private static async Task<WifTestStep> GetWifTokenAsync(
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
                ["scope"] = "wif" // Request WIF scope
            });

            var response = await httpClient.PostAsync(tokenUrl, content, ct);
            sw.Stop();

            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
                if (!tokenResponse.TryGetProperty("access_token", out var tokenProperty) ||
                    string.IsNullOrWhiteSpace(tokenProperty.GetString()))
                {
                    return new WifTestStep
                    {
                        Name = "WIF Token Acquisition",
                        Status = "FAILED",
                        Message = "Token response missing access_token",
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }

                var accessToken = tokenProperty.GetString();

                return new WifTestStep
                {
                    Name = "WIF Token Acquisition",
                    Status = "OK",
                    Message = "Successfully obtained WIF token with 'wif' scope",
                    DurationMs = sw.ElapsedMilliseconds,
                    Token = accessToken
                };
            }

            logger.LogWarning("WIF token request failed: {StatusCode} - {Body}", response.StatusCode, responseBody);
            return new WifTestStep
            {
                Name = "WIF Token Acquisition",
                Status = "FAILED",
                Message = $"Token request failed: {response.StatusCode}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Failed to get WIF token from {Url}", tokenUrl);
            return new WifTestStep
            {
                Name = "WIF Token Acquisition",
                Status = "FAILED",
                Message = $"Token request error: {ex.Message}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    private static WifTestStep InspectWifToken(string token, ILogger logger)
    {
        try
        {
            // JWT format: header.payload.signature
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return new WifTestStep
                {
                    Name = "Token Inspection",
                    Status = "FAILED",
                    Message = "Token is not a valid JWT format",
                    DurationMs = 0
                };
            }

            // Decode payload (base64url)
            var payload = parts[1];
            // Add padding if needed
            payload += new string('=', (4 - payload.Length % 4) % 4);
            var payloadBytes = Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/'));
            var payloadJson = System.Text.Encoding.UTF8.GetString(payloadBytes);

            var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson);

            var extractedClaims = new Dictionary<string, string>();
            if (claims != null)
            {
                foreach (var claim in claims)
                {
                    extractedClaims[claim.Key] = claim.Value.ToString();
                }
            }

            var audienceClaim = claims?.GetValueOrDefault("aud").ToString() ?? "not found";
            var issuerClaim = claims?.GetValueOrDefault("iss").ToString() ?? "not found";
            var subjectClaim = claims?.GetValueOrDefault("sub").ToString() ?? "not found";

            return new WifTestStep
            {
                Name = "Token Inspection",
                Status = "OK",
                Message = $"Token decoded - aud: {audienceClaim}, iss: {issuerClaim}, sub: {subjectClaim}",
                DurationMs = 0,
                Claims = extractedClaims
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to inspect WIF token");
            return new WifTestStep
            {
                Name = "Token Inspection",
                Status = "FAILED",
                Message = $"Failed to decode token: {ex.Message}",
                DurationMs = 0
            };
        }
    }

    private static async Task<WifTestStep> VerifyJwksEndpointAsync(
        HttpClient httpClient,
        string jwksUrl,
        ILogger logger,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var response = await httpClient.GetAsync(jwksUrl, ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var jwks = await response.Content.ReadAsStringAsync(ct);
                var jwksJson = JsonSerializer.Deserialize<JsonElement>(jwks);
                var keysCount = jwksJson.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array
                    ? keys.GetArrayLength()
                    : 0;

                return new WifTestStep
                {
                    Name = "JWKS Endpoint",
                    Status = "OK",
                    Message = $"JWKS endpoint accessible with {keysCount} key(s)",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            return new WifTestStep
            {
                Name = "JWKS Endpoint",
                Status = "FAILED",
                Message = $"JWKS endpoint returned {response.StatusCode}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Failed to verify JWKS endpoint at {Url}", jwksUrl);
            return new WifTestStep
            {
                Name = "JWKS Endpoint",
                Status = "FAILED",
                Message = $"JWKS endpoint error: {ex.Message}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    private static async Task<WifTestStep> VerifyOidcConfigurationAsync(
        HttpClient httpClient,
        string oidcUrl,
        ILogger logger,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var response = await httpClient.GetAsync(oidcUrl, ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var config = await response.Content.ReadAsStringAsync(ct);
                var configJson = JsonSerializer.Deserialize<JsonElement>(config);

                var issuer = configJson.TryGetProperty("issuer", out var iss) ? iss.GetString() : "not found";
                var jwksUri = configJson.TryGetProperty("jwks_uri", out var jwks) ? jwks.GetString() : "not found";

                return new WifTestStep
                {
                    Name = "OpenID Configuration",
                    Status = "OK",
                    Message = $"OpenID configuration accessible - issuer: {issuer}",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            return new WifTestStep
            {
                Name = "OpenID Configuration",
                Status = "FAILED",
                Message = $"OpenID configuration returned {response.StatusCode}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Failed to verify OpenID configuration at {Url}", oidcUrl);
            return new WifTestStep
            {
                Name = "OpenID Configuration",
                Status = "FAILED",
                Message = $"OpenID configuration error: {ex.Message}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }
}

public class WifTestResult
{
    public DateTime Timestamp { get; set; }
    public string OverallStatus { get; set; } = "PENDING";
    public string? Message { get; set; }
    public string? Issuer { get; set; }
    public string? Token { get; set; }
    public Dictionary<string, string>? Claims { get; set; }
    public IList<WifTestStep> Steps { get; set; } = new List<WifTestStep>();
    public AzureFederatedCredentialInstructions? AzureFederatedCredentialSetup { get; set; }
}

public class WifTestStep
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Message { get; set; }
    public long DurationMs { get; set; }
    public string? Token { get; set; }
    public Dictionary<string, string>? Claims { get; set; }
}

public class AzureFederatedCredentialInstructions
{
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Instructions { get; set; } = @"To configure Azure Workload Identity Federation:

1. Create/update Federated Credential in Azure:
   az ad app federated-credential create \
     --id <APP_OBJECT_ID> \
     --parameters '{
       ""name"": ""github-actions-wif"",
       ""issuer"": ""<ISSUER_FROM_ABOVE>"",
       ""subject"": ""<SUBJECT_FROM_ABOVE>"",
       ""audiences"": [""api://AzureADTokenExchange""]
     }'

2. In GitHub Actions, request the WIF token:
   - Use 'wif' scope when requesting token from Identity service
   - Exchange it with Azure: POST https://login.microsoftonline.com/<TENANT>/oauth2/v2.0/token

For GitHub-hosted runners, use subject: repo:ORG/REPO:ref:refs/heads/BRANCH
For self-hosted agents, customize subject claim in your Identity service.";
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

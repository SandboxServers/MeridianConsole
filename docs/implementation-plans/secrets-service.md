# Secrets Service Implementation Plan

> **Status**: Ready for implementation after Gateway and Identity services are complete
> **Last Updated**: 2026-01-15
> **Current State**: ~85-90% production-ready, needs test coverage, contracts extraction, and polish

## Executive Summary

The Secrets service backend is **functionally complete** with all CRUD operations for secrets, certificates, and Key Vault management implemented. The remaining work focuses on:

1. **Test coverage** (critical gap)
2. **Shared contracts extraction** (code quality)
3. **MassTransit event publishing** (platform integration)
4. **Documentation alignment** (stale docs)
5. **Production hardening** (vault purge, rate limiting)

---

## Table of Contents

1. [Current Implementation Status](#current-implementation-status)
2. [Architecture Overview](#architecture-overview)
3. [Phase 1: Documentation Alignment](#phase-1-documentation-alignment)
4. [Phase 2: Shared Contracts Extraction](#phase-2-shared-contracts-extraction)
5. [Phase 3: Comprehensive Test Coverage](#phase-3-comprehensive-test-coverage)
6. [Phase 4: MassTransit Event Integration](#phase-4-masstransit-event-integration)
7. [Phase 5: Production Hardening](#phase-5-production-hardening)
8. [Phase 6: Gateway Integration](#phase-6-gateway-integration)
9. [Dependencies and Prerequisites](#dependencies-and-prerequisites)
10. [Success Criteria](#success-criteria)

---

## Current Implementation Status

### Fully Implemented

| Component | Location | Notes |
|-----------|----------|-------|
| Secret Read Endpoints | `Endpoints/SecretsEndpoints.cs` | GET single, batch, category (oauth/betterauth/infrastructure) |
| Secret Write Endpoints | `Endpoints/SecretWriteEndpoints.cs` | PUT set, POST rotate, DELETE |
| Certificate Endpoints | `Endpoints/CertificateEndpoints.cs` | List, import, delete across vaults |
| Key Vault Endpoints | `Endpoints/KeyVaultEndpoints.cs` | Full CRUD via ARM SDK |
| KeyVault Secret Provider | `Services/KeyVaultSecretProvider.cs` | Azure Key Vault integration with 5-min cache |
| Development Secret Provider | `Services/DevelopmentSecretProvider.cs` | In-memory provider for local dev |
| Certificate Provider | `Services/KeyVaultCertificateProvider.cs` | Certificate management |
| Key Vault Manager | `Services/AzureKeyVaultManager.cs` | ARM-based vault lifecycle |
| Permission System | Endpoint-level claims checking | Category + direct permissions |
| Health Checks | `Readiness/SecretsReadinessCheck.cs` | Secret + certificate probing |
| OpenTelemetry | `Program.cs` | Traces, metrics, logs with correlation |
| JWT Authentication | `Program.cs` | Bearer token validation |

### Gaps Requiring Work

| Gap | Priority | Phase |
|-----|----------|-------|
| Stale documentation | High | 1 |
| DTOs inline (not in Contracts) | Medium | 2 |
| Minimal test coverage | Critical | 3 |
| No MassTransit events | Medium | 4 |
| Vault purge incomplete | Medium | 5 |
| No rate limiting | Low | 6 |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         Gateway (YARP)                          │
│                    Rate Limiting, Auth Forwarding               │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                       Secrets Service                           │
├─────────────────────────────────────────────────────────────────┤
│  Endpoints                                                      │
│  ├── SecretsEndpoints (read)                                    │
│  ├── SecretWriteEndpoints (write/rotate/delete)                 │
│  ├── CertificateEndpoints (list/import/delete)                  │
│  └── KeyVaultEndpoints (vault CRUD)                             │
├─────────────────────────────────────────────────────────────────┤
│  Services                                                       │
│  ├── ISecretProvider → KeyVaultSecretProvider (prod)            │
│  │                   → DevelopmentSecretProvider (dev)          │
│  ├── ICertificateProvider → KeyVaultCertificateProvider         │
│  └── IKeyVaultManager → AzureKeyVaultManager                    │
├─────────────────────────────────────────────────────────────────┤
│  Infrastructure                                                 │
│  ├── JWT Bearer Authentication (from Identity)                  │
│  ├── Claims-based Authorization                                 │
│  ├── IMemoryCache (5-min TTL)                                   │
│  ├── OpenTelemetry (traces/metrics/logs)                        │
│  └── MassTransit (registered, events TBD)                       │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Azure Key Vault                            │
│            Secrets │ Certificates │ Keys (future)               │
└─────────────────────────────────────────────────────────────────┘
```

---

## Phase 1: Documentation Alignment

**Goal**: Update documentation to reflect actual implementation state.

### Tasks

#### 1.1 Archive Old Implementation Plan
```bash
# Rename old plan to indicate it's historical
git mv docs/SECRETS-SERVICE-IMPLEMENTATION-PLAN.md docs/archive/SECRETS-SERVICE-IMPLEMENTATION-PLAN-2025.md
```

#### 1.2 Create Architecture Document

Create `docs/architecture/secrets-service.md`:

```markdown
# Secrets Service Architecture

## Overview
The Secrets service provides centralized secret, certificate, and Key Vault
management for the Meridian Console platform.

## Endpoints
[Document all 13+ endpoints with request/response schemas]

## Permission Model
[Document all permission claims]

## Configuration
[Document SecretsOptions, allowed secrets, etc.]

## Azure Integration
[Document DefaultAzureCredential chain, Key Vault setup]
```

#### 1.3 Update CLAUDE.md

Add Secrets service to the "What's Implemented" section:
- Secret read/write/rotate operations
- Certificate management
- Key Vault lifecycle management

#### 1.4 Create API Documentation

Ensure Swagger/OpenAPI docs are complete:
- Add XML comments to all endpoint methods
- Add request/response examples
- Document error responses (400, 403, 404, 409)

### Deliverables
- [ ] `docs/archive/SECRETS-SERVICE-IMPLEMENTATION-PLAN-2025.md`
- [ ] `docs/architecture/secrets-service.md`
- [ ] Updated `CLAUDE.md`
- [ ] XML comments on all endpoints

### Estimated Effort
~2-3 hours

---

## Phase 2: Shared Contracts Extraction

**Goal**: Extract DTOs from inline definitions to shared contracts for cross-service reuse.

### Tasks

#### 2.1 Create Contracts Directory Structure

```
src/Shared/Dhadgar.Contracts/
└── Secrets/
    ├── SecretContracts.cs       # Secret DTOs
    ├── CertificateContracts.cs  # Certificate DTOs
    └── KeyVaultContracts.cs     # Key Vault DTOs
```

#### 2.2 Secret Contracts

Create `src/Shared/Dhadgar.Contracts/Secrets/SecretContracts.cs`:

```csharp
namespace Dhadgar.Contracts.Secrets;

// Read responses
public sealed record SecretResponse(string Name, string? Value);
public sealed record SecretsResponse(Dictionary<string, string> Secrets);
public sealed record SecretMetadata(string Name, string? Version, DateTime? UpdatedAt);

// Requests
public sealed record BatchSecretsRequest(IReadOnlyList<string> SecretNames);
public sealed record SetSecretRequest(string Value);

// Write responses
public sealed record SetSecretResponse(string Name, bool Updated);
public sealed record RotateSecretResponse(
    string Name,
    string Version,
    DateTime RotatedAt,
    DateTime? ExpiresAt);
public sealed record DeleteSecretResponse(string Name, bool Deleted);
```

#### 2.3 Certificate Contracts

Create `src/Shared/Dhadgar.Contracts/Secrets/CertificateContracts.cs`:

```csharp
namespace Dhadgar.Contracts.Secrets;

public sealed record CertificateInfo(
    string Name,
    string? Subject,
    string? Issuer,
    DateTime? ExpiresAt,
    string? Thumbprint,
    bool Enabled);

public sealed record CertificateListResponse(IReadOnlyList<CertificateInfo> Certificates);

public sealed record ImportCertificateRequest(
    string Name,
    string CertificateData,  // Base64 encoded
    string? Password);

public sealed record ImportCertificateResponse(
    string Name,
    string? Subject,
    string? Issuer,
    string? Thumbprint,
    DateTime? ExpiresAt);
```

#### 2.4 Key Vault Contracts

Create `src/Shared/Dhadgar.Contracts/Secrets/KeyVaultContracts.cs`:

```csharp
namespace Dhadgar.Contracts.Secrets;

public sealed record VaultSummary(
    string Name,
    string VaultUri,
    string Location,
    int SecretCount,
    bool Enabled);

public sealed record VaultDetail(
    string Name,
    string VaultUri,
    string Location,
    string ResourceGroup,
    string Sku,
    int SecretCount,
    int KeyCount,
    int CertificateCount,
    bool EnableSoftDelete,
    int? SoftDeleteRetentionDays,
    bool EnablePurgeProtection,
    bool EnableRbacAuthorization,
    string? PublicNetworkAccess,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

public sealed record VaultListResponse(IReadOnlyList<VaultSummary> Vaults);

public sealed record CreateVaultRequest(string Name, string Location);

public sealed record UpdateVaultRequest(
    bool? EnableSoftDelete,
    bool? EnablePurgeProtection,
    int? SoftDeleteRetentionDays,
    string? Sku);
```

#### 2.5 Update Secrets Service to Use Contracts

Replace inline record definitions in endpoint files with imports from `Dhadgar.Contracts.Secrets`.

#### 2.6 Update CLI to Use Shared Contracts

Update `src/Dhadgar.Cli/Infrastructure/Clients/ISecretsApi.cs` and `IKeyVaultApi.cs` to reference shared contracts instead of duplicating DTOs.

### Deliverables
- [ ] `src/Shared/Dhadgar.Contracts/Secrets/SecretContracts.cs`
- [ ] `src/Shared/Dhadgar.Contracts/Secrets/CertificateContracts.cs`
- [ ] `src/Shared/Dhadgar.Contracts/Secrets/KeyVaultContracts.cs`
- [ ] Updated `Dhadgar.Secrets` endpoints
- [ ] Updated `Dhadgar.Cli` clients

### Estimated Effort
~3-4 hours

---

## Phase 3: Comprehensive Test Coverage

**Goal**: Achieve ~80% code coverage with unit and integration tests.

### Current Test State

```
tests/Dhadgar.Secrets.Tests/
├── HelloWorldTests.cs           # 1 test (smoke)
├── ReadinessTests.cs            # 3 unit tests
└── ReadinessIntegrationTests.cs # 3 integration tests
```

**Total: 7 tests** - Covers only health checks.

### Target Test Structure

```
tests/Dhadgar.Secrets.Tests/
├── HelloWorldTests.cs                    # Keep existing
├── Readiness/
│   ├── ReadinessTests.cs                 # Keep existing
│   └── ReadinessIntegrationTests.cs      # Keep existing
├── Unit/
│   ├── KeyVaultSecretProviderTests.cs    # NEW
│   ├── DevelopmentSecretProviderTests.cs # NEW
│   ├── KeyVaultCertificateProviderTests.cs # NEW
│   ├── AzureKeyVaultManagerTests.cs      # NEW
│   └── PermissionHelperTests.cs          # NEW
├── Integration/
│   ├── SecretsEndpointTests.cs           # NEW
│   ├── SecretWriteEndpointTests.cs       # NEW
│   ├── CertificateEndpointTests.cs       # NEW
│   ├── KeyVaultEndpointTests.cs          # NEW
│   └── AuthorizationTests.cs             # NEW
└── Fixtures/
    ├── SecretsWebApplicationFactory.cs   # Enhance existing
    ├── FakeSecretProvider.cs             # NEW
    ├── FakeCertificateProvider.cs        # NEW
    └── FakeKeyVaultManager.cs            # NEW
```

### Tasks

#### 3.1 Create Test Fixtures

**FakeSecretProvider.cs**:
```csharp
public class FakeSecretProvider : ISecretProvider
{
    private readonly Dictionary<string, string> _secrets = new();
    private readonly HashSet<string> _allowedSecrets;

    public FakeSecretProvider(IEnumerable<string>? allowedSecrets = null)
    {
        _allowedSecrets = allowedSecrets?.ToHashSet() ?? new();
    }

    public void SetSecret(string name, string value) => _secrets[name] = value;

    public Task<string?> GetSecretAsync(string secretName, CancellationToken ct = default)
        => Task.FromResult(_secrets.GetValueOrDefault(secretName));

    // ... implement all interface methods
}
```

**FakeCertificateProvider.cs** and **FakeKeyVaultManager.cs**: Similar pattern.

#### 3.2 Unit Tests - Secret Providers

**KeyVaultSecretProviderTests.cs**:
```csharp
public class KeyVaultSecretProviderTests
{
    [Fact]
    public async Task GetSecretAsync_ReturnsValue_WhenSecretExists()

    [Fact]
    public async Task GetSecretAsync_ReturnsNull_WhenSecretNotFound()

    [Fact]
    public async Task GetSecretAsync_ReturnsNull_WhenSecretNotAllowed()

    [Fact]
    public async Task GetSecretAsync_SkipsPlaceholderValues()

    [Fact]
    public async Task GetSecretAsync_UsesCacheOnSecondCall()

    [Fact]
    public async Task SetSecretAsync_InvalidatesCache()

    [Fact]
    public async Task SetSecretAsync_RejectsOversizedValues()

    [Fact]
    public async Task RotateSecretAsync_GeneratesCryptographicallySecureValue()

    [Fact]
    public async Task RotateSecretAsync_ReturnsVersionInfo()
}
```

**DevelopmentSecretProviderTests.cs**: Similar tests for in-memory provider.

#### 3.3 Unit Tests - Certificate Provider

**KeyVaultCertificateProviderTests.cs**:
```csharp
public class KeyVaultCertificateProviderTests
{
    [Fact]
    public async Task ListCertificatesAsync_ReturnsMetadata()

    [Fact]
    public async Task ImportCertificateAsync_AcceptsPfxFormat()

    [Fact]
    public async Task ImportCertificateAsync_AcceptsPemFormat()

    [Fact]
    public async Task ImportCertificateAsync_RejectsInvalidFormat()

    [Fact]
    public async Task ImportCertificateAsync_RejectsWrongPassword()

    [Fact]
    public async Task DeleteCertificateAsync_ReturnsTrue_WhenExists()

    [Fact]
    public async Task DeleteCertificateAsync_ReturnsFalse_WhenNotFound()
}
```

#### 3.4 Unit Tests - Key Vault Manager

**AzureKeyVaultManagerTests.cs**:
```csharp
public class AzureKeyVaultManagerTests
{
    [Theory]
    [InlineData("ab", false)]           // Too short
    [InlineData("abc", true)]           // Min length
    [InlineData("valid-vault-name", true)]
    [InlineData("valid123", true)]
    [InlineData("UPPERCASE", true)]     // Azure normalizes
    [InlineData("has_underscore", false)]
    [InlineData("has.dot", false)]
    [InlineData("has space", false)]
    [InlineData("abcdefghijklmnopqrstuvwxyz", false)] // Too long (>24)
    public void ValidateVaultName_ReturnsExpected(string name, bool expected)

    [Fact]
    public async Task CreateVaultAsync_SetsDefaultProperties()

    [Fact]
    public async Task UpdateVaultAsync_CannotDisablePurgeProtection()

    [Theory]
    [InlineData(6, false)]   // Below min
    [InlineData(7, true)]    // Min
    [InlineData(90, true)]   // Max
    [InlineData(91, false)]  // Above max
    public void ValidateRetentionDays_ReturnsExpected(int days, bool expected)
}
```

#### 3.5 Integration Tests - Endpoints

**SecretsEndpointTests.cs**:
```csharp
public class SecretsEndpointTests : IClassFixture<SecretsWebApplicationFactory>
{
    [Fact]
    public async Task GetSecret_Returns200_WhenAuthorizedAndExists()

    [Fact]
    public async Task GetSecret_Returns403_WhenUnauthorized()

    [Fact]
    public async Task GetSecret_Returns404_WhenNotFound()

    [Fact]
    public async Task GetOAuthSecrets_ReturnsOnlyAllowedSecrets()

    [Fact]
    public async Task BatchSecrets_ReturnsOnlyPermittedSecrets()

    [Fact]
    public async Task BatchSecrets_Returns403_WhenNoPermissions()
}
```

**SecretWriteEndpointTests.cs**:
```csharp
public class SecretWriteEndpointTests : IClassFixture<SecretsWebApplicationFactory>
{
    [Fact]
    public async Task SetSecret_Returns200_WhenAuthorized()

    [Fact]
    public async Task SetSecret_Returns400_WhenValueTooLarge()

    [Fact]
    public async Task SetSecret_Returns400_WhenValueEmpty()

    [Fact]
    public async Task SetSecret_Returns403_WhenUnauthorized()

    [Fact]
    public async Task RotateSecret_Returns200_WithNewVersion()

    [Fact]
    public async Task RotateSecret_Returns403_WithoutRotatePermission()

    [Fact]
    public async Task DeleteSecret_Returns204_WhenExists()

    [Fact]
    public async Task DeleteSecret_Returns404_WhenNotFound()
}
```

**CertificateEndpointTests.cs** and **KeyVaultEndpointTests.cs**: Similar patterns.

#### 3.6 Authorization Tests

**AuthorizationTests.cs**:
```csharp
public class AuthorizationTests : IClassFixture<SecretsWebApplicationFactory>
{
    [Fact]
    public async Task AllEndpoints_Return401_WithoutToken()

    [Fact]
    public async Task ReadEndpoints_Return403_WithoutReadPermission()

    [Fact]
    public async Task WriteEndpoints_Return403_WithoutWritePermission()

    [Fact]
    public async Task RotateEndpoint_Requires_RotatePermission_NotJustWrite()

    [Fact]
    public async Task CategoryPermission_GrantsAccess_ToAllSecretsInCategory()

    [Fact]
    public async Task DirectPermission_GrantsAccess_ToSpecificSecretOnly()
}
```

#### 3.7 Enhance WebApplicationFactory

Update `SecretsWebApplicationFactory.cs`:
```csharp
public class SecretsWebApplicationFactory : WebApplicationFactory<Program>
{
    public FakeSecretProvider FakeSecretProvider { get; } = new();
    public FakeCertificateProvider FakeCertificateProvider { get; } = new();
    public FakeKeyVaultManager FakeKeyVaultManager { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace real providers with fakes
            services.RemoveAll<ISecretProvider>();
            services.AddSingleton<ISecretProvider>(FakeSecretProvider);

            services.RemoveAll<ICertificateProvider>();
            services.AddSingleton<ICertificateProvider>(FakeCertificateProvider);

            services.RemoveAll<IKeyVaultManager>();
            services.AddSingleton<IKeyVaultManager>(FakeKeyVaultManager);

            // Configure test authentication
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        });
    }

    public HttpClient CreateAuthenticatedClient(params string[] permissions)
    {
        var client = CreateClient();
        // Add JWT with specified permissions
        return client;
    }
}
```

### Test Coverage Targets

| Component | Target Coverage |
|-----------|----------------|
| `SecretsEndpoints.cs` | 90% |
| `SecretWriteEndpoints.cs` | 90% |
| `CertificateEndpoints.cs` | 85% |
| `KeyVaultEndpoints.cs` | 85% |
| `KeyVaultSecretProvider.cs` | 80% |
| `DevelopmentSecretProvider.cs` | 90% |
| `KeyVaultCertificateProvider.cs` | 75% |
| `AzureKeyVaultManager.cs` | 70% |
| **Overall** | **~80%** |

### Deliverables
- [ ] Test fixtures (`FakeSecretProvider`, `FakeCertificateProvider`, `FakeKeyVaultManager`)
- [ ] Enhanced `SecretsWebApplicationFactory`
- [ ] Unit tests for all providers/managers
- [ ] Integration tests for all endpoints
- [ ] Authorization test suite
- [ ] ~50-60 new tests total

### Estimated Effort
~8-12 hours

---

## Phase 4: MassTransit Event Integration

**Goal**: Publish domain events for secret operations to enable platform-wide reactions.

### Event Definitions

Create `src/Shared/Dhadgar.Contracts/Secrets/SecretsEvents.cs`:

```csharp
namespace Dhadgar.Contracts.Secrets;

/// <summary>
/// Published when a secret is created or updated.
/// </summary>
public sealed record SecretUpdatedEvent(
    Guid EventId,
    DateTime Timestamp,
    string SecretName,
    string? Category,       // "oauth" | "betterauth" | "infrastructure" | null
    string Version,
    Guid? UserId,
    Guid? OrganizationId,
    string CorrelationId);

/// <summary>
/// Published when a secret is rotated (new cryptographic value generated).
/// </summary>
public sealed record SecretRotatedEvent(
    Guid EventId,
    DateTime Timestamp,
    string SecretName,
    string? Category,
    string OldVersion,
    string NewVersion,
    DateTime? ExpiresAt,
    Guid? UserId,
    Guid? OrganizationId,
    string CorrelationId);

/// <summary>
/// Published when a secret is deleted.
/// </summary>
public sealed record SecretDeletedEvent(
    Guid EventId,
    DateTime Timestamp,
    string SecretName,
    string? Category,
    bool SoftDelete,        // true if recoverable
    Guid? UserId,
    Guid? OrganizationId,
    string CorrelationId);

/// <summary>
/// Published when a certificate is imported.
/// </summary>
public sealed record CertificateImportedEvent(
    Guid EventId,
    DateTime Timestamp,
    string CertificateName,
    string? VaultName,
    string Subject,
    string Thumbprint,
    DateTime ExpiresAt,
    Guid? UserId,
    Guid? OrganizationId,
    string CorrelationId);

/// <summary>
/// Published when a certificate is approaching expiration.
/// Triggered by background monitoring (not user action).
/// </summary>
public sealed record CertificateExpiringEvent(
    Guid EventId,
    DateTime Timestamp,
    string CertificateName,
    string? VaultName,
    string Subject,
    string Thumbprint,
    DateTime ExpiresAt,
    int DaysUntilExpiration,
    string CorrelationId);

/// <summary>
/// Published when a Key Vault is created.
/// </summary>
public sealed record KeyVaultCreatedEvent(
    Guid EventId,
    DateTime Timestamp,
    string VaultName,
    string VaultUri,
    string Location,
    Guid? UserId,
    Guid? OrganizationId,
    string CorrelationId);
```

### Tasks

#### 4.1 Add Event Publishing to Endpoints

Update `SecretWriteEndpoints.cs`:
```csharp
app.MapPut("/api/v1/secrets/{secretName}", async (
    string secretName,
    SetSecretRequest request,
    ISecretProvider provider,
    IPublishEndpoint publishEndpoint,
    HttpContext context,
    CancellationToken ct) =>
{
    // ... existing logic ...

    var result = await provider.SetSecretAsync(secretName, request.Value, ct);

    if (result)
    {
        await publishEndpoint.Publish(new SecretUpdatedEvent(
            EventId: Guid.NewGuid(),
            Timestamp: DateTime.UtcNow,
            SecretName: secretName,
            Category: DetermineCategory(secretName),
            Version: "latest",
            UserId: context.User.GetUserId(),
            OrganizationId: context.User.GetOrganizationId(),
            CorrelationId: context.GetCorrelationId()
        ), ct);
    }

    return Results.Ok(new SetSecretResponse(secretName, result));
});
```

#### 4.2 Add Certificate Expiration Monitor

Create `Services/CertificateExpirationMonitor.cs`:
```csharp
public class CertificateExpirationMonitor : BackgroundService
{
    private readonly ICertificateProvider _certificateProvider;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<CertificateExpirationMonitor> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);
    private readonly int[] _warningDays = [30, 14, 7, 3, 1];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckCertificateExpirations(stoppingToken);
            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CheckCertificateExpirations(CancellationToken ct)
    {
        var certificates = await _certificateProvider.ListCertificatesAsync(ct: ct);

        foreach (var cert in certificates)
        {
            if (cert.ExpiresAt is null) continue;

            var daysUntilExpiration = (cert.ExpiresAt.Value - DateTime.UtcNow).Days;

            if (_warningDays.Contains(daysUntilExpiration))
            {
                await _publishEndpoint.Publish(new CertificateExpiringEvent(
                    EventId: Guid.NewGuid(),
                    Timestamp: DateTime.UtcNow,
                    CertificateName: cert.Name,
                    VaultName: null,
                    Subject: cert.Subject ?? "",
                    Thumbprint: cert.Thumbprint ?? "",
                    ExpiresAt: cert.ExpiresAt.Value,
                    DaysUntilExpiration: daysUntilExpiration,
                    CorrelationId: Guid.NewGuid().ToString()
                ), ct);
            }
        }
    }
}
```

#### 4.3 Register Background Service

In `Program.cs`:
```csharp
builder.Services.AddHostedService<CertificateExpirationMonitor>();
```

### Event Consumers (Other Services)

These events can be consumed by:

| Event | Potential Consumer | Action |
|-------|-------------------|--------|
| `SecretRotatedEvent` | Notifications | Alert admins of rotation |
| `SecretRotatedEvent` | BetterAuth | Reload rotated OAuth secrets |
| `CertificateExpiringEvent` | Notifications | Send expiration warnings |
| `CertificateImportedEvent` | Notifications | Alert of new cert |
| `KeyVaultCreatedEvent` | Billing | Track vault usage |

### Deliverables
- [ ] `src/Shared/Dhadgar.Contracts/Secrets/SecretsEvents.cs`
- [ ] Event publishing in `SecretWriteEndpoints.cs`
- [ ] Event publishing in `CertificateEndpoints.cs`
- [ ] Event publishing in `KeyVaultEndpoints.cs`
- [ ] `CertificateExpirationMonitor` background service
- [ ] Unit tests for event publishing

### Estimated Effort
~4-6 hours

---

## Phase 5: Production Hardening

**Goal**: Address remaining production concerns.

### Tasks

#### 5.1 Implement Vault Purge via ARM REST API

The Azure SDK doesn't expose vault purge. Implement via raw HTTP:

```csharp
// In AzureKeyVaultManager.cs
public async Task<bool> PurgeVaultAsync(string vaultName, string location, CancellationToken ct = default)
{
    // ARM REST API: POST https://management.azure.com/subscriptions/{sub}/providers/
    //               Microsoft.KeyVault/locations/{location}/deletedVaults/{vaultName}/purge?api-version=2023-07-01

    var credential = new DefaultAzureCredential();
    var token = await credential.GetTokenAsync(
        new TokenRequestContext(["https://management.azure.com/.default"]), ct);

    using var client = new HttpClient();
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", token.Token);

    var uri = $"https://management.azure.com/subscriptions/{_subscriptionId}" +
              $"/providers/Microsoft.KeyVault/locations/{location}" +
              $"/deletedVaults/{vaultName}/purge?api-version=2023-07-01";

    var response = await client.PostAsync(uri, null, ct);
    return response.IsSuccessStatusCode;
}
```

#### 5.2 Add Input Validation

Create `Validation/SecretsValidation.cs`:
```csharp
public static class SecretsValidation
{
    public const int MaxSecretSizeBytes = 25_600; // 25KB
    public const int MaxCertificateSizeBytes = 1_048_576; // 1MB

    public static bool IsValidSecretName(string name)
        => !string.IsNullOrWhiteSpace(name)
           && name.Length <= 127
           && Regex.IsMatch(name, @"^[a-zA-Z0-9-]+$");

    public static bool IsValidVaultName(string name)
        => name.Length >= 3
           && name.Length <= 24
           && Regex.IsMatch(name, @"^[a-zA-Z0-9-]+$")
           && !name.StartsWith('-')
           && !name.EndsWith('-');

    public static bool IsValidSecretValue(string value)
        => !string.IsNullOrEmpty(value)
           && Encoding.UTF8.GetByteCount(value) <= MaxSecretSizeBytes;
}
```

#### 5.3 Add Structured Error Responses

Ensure all error responses follow RFC 7807:
```csharp
public static class SecretsErrors
{
    public static IResult SecretNotFound(string secretName) =>
        Results.Problem(
            statusCode: 404,
            title: "Secret Not Found",
            detail: $"The secret '{secretName}' does not exist or is not accessible.",
            type: "https://meridian.console/errors/secret-not-found");

    public static IResult SecretTooLarge(int actualSize) =>
        Results.Problem(
            statusCode: 400,
            title: "Secret Value Too Large",
            detail: $"Secret value is {actualSize} bytes, maximum allowed is 25,600 bytes.",
            type: "https://meridian.console/errors/secret-too-large");

    public static IResult InsufficientPermissions(string required) =>
        Results.Problem(
            statusCode: 403,
            title: "Insufficient Permissions",
            detail: $"This operation requires the '{required}' permission.",
            type: "https://meridian.console/errors/insufficient-permissions");
}
```

#### 5.4 Add Metrics

Create custom metrics for observability:
```csharp
public static class SecretsMetrics
{
    private static readonly Counter<long> SecretsRead =
        Meter.CreateCounter<long>("secrets.read.count");
    private static readonly Counter<long> SecretsWritten =
        Meter.CreateCounter<long>("secrets.written.count");
    private static readonly Counter<long> SecretsRotated =
        Meter.CreateCounter<long>("secrets.rotated.count");
    private static readonly Histogram<double> SecretReadLatency =
        Meter.CreateHistogram<double>("secrets.read.latency.ms");

    public static void RecordSecretRead(string category) =>
        SecretsRead.Add(1, new KeyValuePair<string, object?>("category", category));

    public static void RecordSecretWrite(string category) =>
        SecretsWritten.Add(1, new KeyValuePair<string, object?>("category", category));
}
```

### Deliverables
- [ ] Vault purge implementation via ARM REST API
- [ ] Input validation helpers
- [ ] Standardized error responses
- [ ] Custom metrics instrumentation
- [ ] Validation unit tests

### Estimated Effort
~4-5 hours

---

## Phase 6: Gateway Integration

**Goal**: Configure Gateway routes and policies for Secrets service.

### Tasks

#### 6.1 Add YARP Route Configuration

In `src/Dhadgar.Gateway/appsettings.json`:
```json
{
  "ReverseProxy": {
    "Routes": {
      "secrets-route": {
        "ClusterId": "secrets-cluster",
        "Match": {
          "Path": "/api/v1/secrets/{**catch-all}"
        },
        "Transforms": [
          { "PathRemovePrefix": "" }
        ]
      },
      "certificates-route": {
        "ClusterId": "secrets-cluster",
        "Match": {
          "Path": "/api/v1/certificates/{**catch-all}"
        }
      },
      "keyvaults-route": {
        "ClusterId": "secrets-cluster",
        "Match": {
          "Path": "/api/v1/keyvaults/{**catch-all}"
        }
      }
    },
    "Clusters": {
      "secrets-cluster": {
        "Destinations": {
          "secrets-service": {
            "Address": "http://secrets:8080"
          }
        }
      }
    }
  }
}
```

#### 6.2 Add Rate Limiting Policy

```csharp
// In Gateway Program.cs
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("secrets-read", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
    });

    options.AddFixedWindowLimiter("secrets-write", opt =>
    {
        opt.PermitLimit = 20;
        opt.Window = TimeSpan.FromMinutes(1);
    });

    options.AddFixedWindowLimiter("secrets-rotate", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
    });
});
```

Apply to routes:
```json
{
  "Routes": {
    "secrets-read-route": {
      "RateLimiterPolicy": "secrets-read",
      "Match": { "Path": "/api/v1/secrets/{**any}", "Methods": ["GET"] }
    },
    "secrets-write-route": {
      "RateLimiterPolicy": "secrets-write",
      "Match": { "Path": "/api/v1/secrets/{**any}", "Methods": ["PUT", "DELETE"] }
    },
    "secrets-rotate-route": {
      "RateLimiterPolicy": "secrets-rotate",
      "Match": { "Path": "/api/v1/secrets/{secretName}/rotate", "Methods": ["POST"] }
    }
  }
}
```

### Deliverables
- [ ] YARP route configuration for all Secrets endpoints
- [ ] Rate limiting policies (read/write/rotate)
- [ ] Gateway integration tests

### Estimated Effort
~2-3 hours

---

## Dependencies and Prerequisites

### Must Be Complete Before Starting

1. **Identity Service**
   - JWT token issuance working
   - Permission claims in tokens
   - `/.well-known/openid-configuration` endpoint

2. **Gateway Service**
   - Basic YARP routing functional
   - Authentication forwarding configured
   - Rate limiter infrastructure ready

### Azure Requirements

- Azure subscription with Key Vault access
- Service principal or managed identity with:
  - `Key Vault Secrets Officer` role on target vaults
  - `Key Vault Certificates Officer` role on target vaults
  - `Key Vault Contributor` role for vault management
  - `Reader` role on subscription (for listing vaults)

### Development Environment

- Azure CLI authenticated (`az login`)
- Test Key Vault provisioned (`mc-oauth` or dev vault)
- User secrets configured:
  ```bash
  dotnet user-secrets set "Secrets:KeyVaultUri" "https://your-vault.vault.azure.net/" \
    --project src/Dhadgar.Secrets
  ```

---

## Success Criteria

### Phase 1 Complete When
- [ ] Old implementation plan archived
- [ ] New architecture doc created
- [ ] CLAUDE.md updated
- [ ] All endpoints have XML documentation

### Phase 2 Complete When
- [ ] All DTOs in `Dhadgar.Contracts.Secrets`
- [ ] Secrets service uses shared contracts
- [ ] CLI uses shared contracts
- [ ] No duplicate DTO definitions

### Phase 3 Complete When
- [ ] 50+ tests added
- [ ] ~80% code coverage achieved
- [ ] All endpoint happy paths tested
- [ ] All endpoint error paths tested
- [ ] Authorization enforcement tested
- [ ] CI passes with new tests

### Phase 4 Complete When
- [ ] Event contracts defined
- [ ] Events published on write operations
- [ ] Certificate expiration monitor running
- [ ] Events visible in RabbitMQ management UI

### Phase 5 Complete When
- [ ] Vault purge implemented and tested
- [ ] Input validation on all endpoints
- [ ] Consistent error responses
- [ ] Custom metrics emitting

### Phase 6 Complete When
- [ ] Gateway routes secrets traffic correctly
- [ ] Rate limiting enforced
- [ ] End-to-end flow works: Gateway → Secrets → Key Vault

---

## Estimated Total Effort

| Phase | Effort | Dependencies |
|-------|--------|--------------|
| Phase 1: Documentation | ~2-3 hours | None |
| Phase 2: Contracts | ~3-4 hours | None |
| Phase 3: Tests | ~8-12 hours | None |
| Phase 4: MassTransit | ~4-6 hours | Phase 2 |
| Phase 5: Hardening | ~4-5 hours | Phase 3 |
| Phase 6: Gateway | ~2-3 hours | Gateway service ready |
| **Total** | **~24-33 hours** | |

---

## Appendix: Permission Matrix

| Endpoint | Method | Required Permission |
|----------|--------|---------------------|
| `/api/v1/secrets/{name}` | GET | `secrets:read:{name}` OR `secrets:read:{category}` |
| `/api/v1/secrets/batch` | POST | Per-secret permissions checked |
| `/api/v1/secrets/oauth` | GET | `secrets:read:oauth` |
| `/api/v1/secrets/betterauth` | GET | `secrets:read:betterauth` |
| `/api/v1/secrets/infrastructure` | GET | `secrets:read:infrastructure` |
| `/api/v1/secrets/{name}` | PUT | `secrets:write:{name}` OR `secrets:write:{category}` |
| `/api/v1/secrets/{name}/rotate` | POST | `secrets:rotate:{name}` |
| `/api/v1/secrets/{name}` | DELETE | `secrets:write:{name}` OR `secrets:write:{category}` |
| `/api/v1/certificates` | GET | `secrets:read:certificates` |
| `/api/v1/certificates` | POST | `secrets:write:certificates` |
| `/api/v1/certificates/{name}` | DELETE | `secrets:write:certificates` |
| `/api/v1/keyvaults` | GET | `keyvault:read` |
| `/api/v1/keyvaults/{name}` | GET | `keyvault:read` |
| `/api/v1/keyvaults` | POST | `keyvault:write` |
| `/api/v1/keyvaults/{name}` | PATCH | `keyvault:write` |
| `/api/v1/keyvaults/{name}` | DELETE | `keyvault:write` |

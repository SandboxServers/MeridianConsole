# Configuration Management Guide

## Overview

Dhadgar uses ASP.NET Core's built-in configuration system with environment-specific overrides. Configuration is managed through:

1. **Base configuration** (`appsettings.json`)
2. **Environment-specific overrides** (`appsettings.{Environment}.json`)
3. **User secrets** (local development)
4. **Environment variables** (Docker/Kubernetes)
5. **Azure Key Vault** (production secrets)

---

## Configuration Hierarchy

ASP.NET Core loads configuration in this order (later sources override earlier):

1. `appsettings.json` (base configuration, committed to repo)
2. `appsettings.{Environment}.json` (environment overrides, committed to repo)
3. User Secrets (local dev only, NOT committed)
4. Environment Variables (runtime configuration)
5. Command-line arguments (rare, typically for debugging)

**Example:**
```json
// appsettings.json (base)
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=dhadgar"
  },
  "RabbitMq": {
    "Host": "localhost"
  }
}

// appsettings.Production.json (production overrides)
{
  "ConnectionStrings": {
    "Postgres": "Host=prod-db.internal;Port=5432;Database=dhadgar_prod"
  },
  "RabbitMq": {
    "Host": "prod-rabbitmq.internal"
  }
}
```

---

## Environment-Specific Configuration Files

### Current Strategy: Checked-In Environment Files ✅

**Recommended for Dhadgar** because:
- ✅ Explicit, version-controlled environment configs
- ✅ Easy to review changes in PRs
- ✅ No build-time transforms needed
- ✅ Works seamlessly with Docker/Kubernetes

### Structure

```
src/Dhadgar.Gateway/
├── appsettings.json                    # Base config (all environments)
├── appsettings.Development.json        # Local dev overrides
├── appsettings.Localdev.json           # Docker Compose dev environment
├── appsettings.Staging.json            # Staging environment
├── appsettings.Production.json         # Production environment
└── Program.cs
```

### What Goes Where?

| Configuration Type | File | Example |
|-------------------|------|---------|
| **Default values** | `appsettings.json` | Connection strings to localhost, logging defaults |
| **Development overrides** | `appsettings.Development.json` | Debug logging, dev database |
| **Localdev** | `appsettings.Localdev.json` | Docker Compose service names (postgres, rabbitmq) |
| **Staging** | `appsettings.Staging.json` | Staging database, staging endpoints |
| **Production** | `appsettings.Production.json` | Production database, production endpoints |
| **Secrets** | Environment Variables or Key Vault | Passwords, API keys, connection strings with credentials |

---

## Secrets Management

### ❌ DO NOT put secrets in appsettings files

**Never commit:**
- Database passwords
- API keys
- OAuth client secrets
- Signing keys
- Connection strings with credentials

### ✅ Use these instead:

#### 1. User Secrets (Local Development)

```bash
# Initialize user secrets for a project
dotnet user-secrets init --project src/Dhadgar.Gateway

# Set a secret
dotnet user-secrets set "ConnectionStrings:Postgres" \
  "Host=localhost;Port=5432;Database=dhadgar;Username=dhadgar;Password=secret" \
  --project src/Dhadgar.Gateway

# List secrets
dotnet user-secrets list --project src/Dhadgar.Gateway
```

**In `appsettings.json`** (no password):
```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=dhadgar"
  }
}
```

**User overrides locally** (has password, not committed):
```
ConnectionStrings:Postgres = Host=localhost;Port=5432;Database=dhadgar;Username=dhadgar;Password=secret
```

#### 2. Environment Variables (Docker/Kubernetes)

**In `appsettings.json`** (placeholder):
```json
{
  "ConnectionStrings": {
    "Postgres": ""
  }
}
```

**In Docker Compose** (`deploy/compose/docker-compose.yml`):
```yaml
services:
  gateway:
    image: dhadgar/gateway:latest
    environment:
      ConnectionStrings__Postgres: "Host=postgres;Port=5432;Database=dhadgar;Username=${PG_USER};Password=${PG_PASSWORD}"
      ASPNETCORE_ENVIRONMENT: Localdev
    env_file:
      - .env  # Contains PG_USER and PG_PASSWORD
```

**In Kubernetes** (Secret):
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: gateway-secrets
type: Opaque
stringData:
  postgres-connection: "Host=postgres;Port=5432;Database=dhadgar;Username=dhadgar;Password=secret"
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: gateway
spec:
  template:
    spec:
      containers:
      - name: gateway
        env:
        - name: ConnectionStrings__Postgres
          valueFrom:
            secretKeyRef:
              name: gateway-secrets
              key: postgres-connection
        - name: ASPNETCORE_ENVIRONMENT
          value: Production
```

#### 3. Azure Key Vault (Production)

**In `Program.cs`** (Dhadgar.Identity example):
```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/"),
    new DefaultAzureCredential());
```

**In `appsettings.Production.json`**:
```json
{
  "KeyVaultName": "dhadgar-prod-kv"
}
```

**In Key Vault**:
- Secret name: `ConnectionStrings--Postgres`
- Secret value: `Host=prod-db;Port=5432;Database=dhadgar;Username=dhadgar;Password=actualProductionPassword`

---

## Environment Variables in ASP.NET Core

ASP.NET Core uses double underscores (`__`) to represent nested configuration:

```json
// appsettings.json
{
  "ConnectionStrings": {
    "Postgres": "..."
  },
  "RabbitMq": {
    "Host": "localhost",
    "Port": 5672
  }
}
```

**Equivalent environment variables:**
```bash
export ConnectionStrings__Postgres="Host=postgres;Port=5432;Database=dhadgar"
export RabbitMq__Host="rabbitmq"
export RabbitMq__Port="5672"
```

---

## Per-Environment Configuration Strategy

### Localdev (Docker Compose)

**File**: `appsettings.Localdev.json`

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=postgres;Port=5432;Database=dhadgar;Username=dhadgar;Password=dhadgar"
  },
  "RabbitMq": {
    "Host": "rabbitmq",
    "Port": 5672,
    "Username": "dhadgar",
    "Password": "dhadgar"
  },
  "Redis": {
    "ConnectionString": "redis:6379,password=dhadgar"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

**How it's used:**
```yaml
# docker-compose.yml
services:
  gateway:
    environment:
      ASPNETCORE_ENVIRONMENT: Localdev  # Loads appsettings.Localdev.json
```

### Staging

**File**: `appsettings.Staging.json`

```json
{
  "ConnectionStrings": {
    "Postgres": ""  // Provided via environment variable
  },
  "RabbitMq": {
    "Host": "staging-rabbitmq.internal"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "OpenTelemetry": {
    "OtlpEndpoint": "http://otel-collector:4317"
  }
}
```

**Kubernetes ConfigMap**:
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: gateway-config
data:
  ASPNETCORE_ENVIRONMENT: "Staging"
  RabbitMq__Host: "staging-rabbitmq.internal"
```

### Production

**File**: `appsettings.Production.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "AllowedHosts": "*",
  "OpenTelemetry": {
    "OtlpEndpoint": "http://otel-collector.observability.svc.cluster.local:4317"
  },
  "KeyVaultName": "dhadgar-prod-kv"
}
```

**All sensitive config comes from:**
- Azure Key Vault (connection strings, API keys)
- Kubernetes Secrets (injected as environment variables)

---

## Configuration Validation

### Strongly-Typed Configuration (Recommended)

**Option classes** (`Options/` directory):
```csharp
// src/Dhadgar.Gateway/Options/RabbitMqOptions.cs
public class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
}
```

**Registration in `Program.cs`**:
```csharp
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

// With validation
builder.Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**Usage in services**:
```csharp
public class MyService
{
    private readonly RabbitMqOptions _rabbitMqOptions;

    public MyService(IOptions<RabbitMqOptions> rabbitMqOptions)
    {
        _rabbitMqOptions = rabbitMqOptions.Value;
    }
}
```

---

## Docker Configuration

### Multi-Stage Build with Environment Files

**Dockerfile** (already using this pattern):
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Copy published artifacts
COPY --from=build /app/publish .

# appsettings.*.json files are included in publish output
# Environment is set at runtime via ASPNETCORE_ENVIRONMENT

ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "Dhadgar.Gateway.dll"]
```

**At runtime** (Docker Compose or Kubernetes sets environment):
```yaml
# docker-compose.yml
services:
  gateway:
    image: dhadgar/gateway:latest
    environment:
      ASPNETCORE_ENVIRONMENT: Localdev
      ConnectionStrings__Postgres: "Host=postgres;..."
```

---

## Configuration Transform Alternatives (NOT Recommended)

### ❌ XML Document Transforms (XDT)

**Why NOT:**
- ❌ Requires build-time transforms
- ❌ Complex XML syntax
- ❌ Harder to review in PRs
- ❌ Doesn't work well with Docker (build once, run anywhere)

### ❌ Config Builders (SlowCheetah, etc.)

**Why NOT:**
- ❌ Adds complexity to build process
- ❌ Transforms happen at build time (violates "build once" principle)
- ❌ Harder to debug which config is actually running

---

## Best Practices

### ✅ DO

1. **Use environment-specific JSON files** (`appsettings.{Environment}.json`)
2. **Commit non-sensitive config** to source control
3. **Use environment variables** for secrets
4. **Use Azure Key Vault** for production secrets
5. **Use user secrets** for local development
6. **Validate configuration** with `IOptions<T>` and data annotations
7. **Document required configuration** in README files

### ❌ DON'T

1. **Don't commit secrets** to appsettings files
2. **Don't use build-time transforms** (XDT, SlowCheetah)
3. **Don't hardcode environment URLs** - use configuration
4. **Don't duplicate config** across files (use base + overrides)
5. **Don't expose Key Vault names** in appsettings files (use environment variables for vault name)

---

## Troubleshooting

### Configuration not loading?

**Check:**
1. `ASPNETCORE_ENVIRONMENT` is set correctly
2. `appsettings.{Environment}.json` file exists in publish output
3. Environment variables use double underscores (`__`) not colons (`:`)
4. Configuration section names match exactly (case-sensitive)

**Debug configuration loading:**
```csharp
// Program.cs
var config = builder.Configuration.AsEnumerable();
foreach (var kvp in config)
{
    Console.WriteLine($"{kvp.Key} = {kvp.Value}");
}
```

### Environment variable not overriding JSON?

Environment variables have **higher priority** than JSON files. If JSON is winning:
- Check environment variable name uses `__` (double underscore)
- Ensure environment is set when container starts
- Restart the service after changing environment variables

### Secrets not loading from Key Vault?

**Check:**
1. Managed Identity has `Get` and `List` permissions on Key Vault
2. Key Vault name is correct in configuration
3. Secret names use `--` not `:` (Azure Key Vault limitation)
4. Application logs show Key Vault connection attempts

---

## Migration Guide: Existing Services

If a service currently uses a different configuration approach:

1. **Create environment-specific JSON files**:
   ```bash
   cd src/Dhadgar.Gateway
   cp appsettings.json appsettings.Localdev.json
   cp appsettings.json appsettings.Staging.json
   cp appsettings.json appsettings.Production.json
   ```

2. **Remove secrets from all files**:
   - Move database passwords to environment variables
   - Move API keys to user secrets (dev) or Key Vault (prod)

3. **Update Docker Compose** to set `ASPNETCORE_ENVIRONMENT`:
   ```yaml
   environment:
     ASPNETCORE_ENVIRONMENT: Localdev
   ```

4. **Update Kubernetes manifests** to inject environment:
   ```yaml
   env:
   - name: ASPNETCORE_ENVIRONMENT
     value: Production
   ```

5. **Test each environment** to ensure config loads correctly.

---

## Example: Complete Configuration for a Service

**appsettings.json** (base):
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "AllowedHosts": "*",
  "ServiceName": "Gateway"
}
```

**appsettings.Development.json** (local dev):
```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=dhadgar"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

**appsettings.Localdev.json** (Docker Compose):
```json
{
  "ConnectionStrings": {
    "Postgres": "Host=postgres;Port=5432;Database=dhadgar;Username=dhadgar;Password=dhadgar"
  },
  "RabbitMq": {
    "Host": "rabbitmq"
  }
}
```

**appsettings.Production.json** (Kubernetes):
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  },
  "KeyVaultName": "dhadgar-prod-kv"
}
```

**Kubernetes Secret** (injected as env vars):
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: gateway-secrets
stringData:
  postgres-connection: "Host=prod-db;Port=5432;Database=dhadgar;Username=app;Password=actualPassword"
```

**Kubernetes Deployment** (uses secret):
```yaml
apiVersion: apps/v1
kind: Deployment
spec:
  template:
    spec:
      containers:
      - name: gateway
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: Production
        - name: ConnectionStrings__Postgres
          valueFrom:
            secretKeyRef:
              name: gateway-secrets
              key: postgres-connection
```

---

## Summary

**Current Approach:** Environment-specific JSON files + Environment Variables + Azure Key Vault

**This is the RIGHT approach** for modern .NET microservices because:
- ✅ Version-controlled, reviewable configuration
- ✅ No build-time transforms (build once, run anywhere)
- ✅ Works seamlessly with Docker and Kubernetes
- ✅ Clear separation of non-sensitive and sensitive config
- ✅ Easy to debug and troubleshoot

**No additional tooling or transforms needed!** 🎉

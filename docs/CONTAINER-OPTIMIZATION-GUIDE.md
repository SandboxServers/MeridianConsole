# Container Image Optimization Guide

## Current State Analysis

**Current Images** (Debian-based):
- Base: `mcr.microsoft.com/dotnet/aspnet:10.0` (~220 MB)
- Your services: 102-137 MB per container
- Total for 13 services: ~1.5 GB

**Dependencies**: curl (for healthchecks via `apt-get install`)

---

## Optimization Options

### Option 1: Alpine Linux ⭐ **RECOMMENDED**

**Image**: `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`

**Pros**:
- ✅ 50% smaller base image (~110 MB vs ~220 MB)
- ✅ Smaller attack surface (fewer packages = fewer CVEs)
- ✅ .NET 10 fully supported ([official support confirmed](https://github.com/dotnet/dotnet-docker/issues/6860))
- ✅ Still has `curl` available via `apk add curl`
- ✅ Can install debugging tools if needed

**Cons**:
- ⚠️ Uses musl libc instead of glibc (rarely an issue for .NET apps)
- ⚠️ Native dependencies must be Alpine-compatible (not a problem for your stack)

**Expected Savings**: 50-60 MB per service → **650-780 MB total savings**

**Dockerfile Change**:
```dockerfile
# Before
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# After
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
```

**Healthcheck Change**:
```dockerfile
# Before (Debian)
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# After (Alpine)
RUN apk add --no-cache curl
```

---

### Option 2: Ubuntu Chiseled (Distroless) 🔒 **MOST SECURE**

**Image**: `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`

**Pros**:
- ✅ Smallest image (~80-90 MB base)
- ✅ **NO shell** (not even sh/bash)
- ✅ **NO package manager** (apk/apt unavailable)
- ✅ Maximum security - minimal attack surface
- ✅ Forces declarative container design

**Cons**:
- ❌ Cannot install curl for healthchecks (must use TCP healthchecks or HTTP from orchestrator)
- ❌ Cannot `docker exec` into container for debugging
- ❌ Must add all tools at build time
- ❌ Harder to troubleshoot production issues

**Expected Savings**: 80-100 MB per service → **1+ GB total savings**

**Dockerfile Changes**:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app

# NO apk/apt commands allowed (no package manager)
# NO shell commands (no bash/sh)

COPY --from=build /app/publish .
# Cannot use useradd/groupadd (no shell)
# User 'app' already exists in chiseled image
USER app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Healthcheck must use TCP or be removed (no curl)
# HEALTHCHECK --interval=30s --timeout=3s CMD ["true"]  # Dummy healthcheck
# Better: Remove HEALTHCHECK, let Kubernetes handle it with httpGet probe

ENTRYPOINT ["dotnet", "Dhadgar.Gateway.dll"]
```

**Kubernetes Healthcheck** (replaces Dockerfile HEALTHCHECK):
```yaml
livenessProbe:
  httpGet:
    path: /healthz
    port: 8080
  initialDelaySeconds: 5
  periodSeconds: 30
```

---

### Option 3: Alpine AOT (Ahead-of-Time Compilation) 🚀 **PERFORMANCE**

**Image**: `mcr.microsoft.com/dotnet/aspnet:10.0-alpine-aot`

**Pros**:
- ✅ Fastest startup time (pre-compiled native code)
- ✅ Lower memory usage
- ✅ Alpine security benefits

**Cons**:
- ⚠️ Requires Native AOT-compatible code (no reflection, limited dynamic code)
- ⚠️ Your codebase uses EF Core, MassTransit, OpenIddict → **NOT AOT-compatible**

**Verdict**: ❌ Not viable for Dhadgar (too many dynamic frameworks)

---

## Recommendation Matrix

| Scenario | Recommendation | Rationale |
|----------|----------------|-----------|
| **Development** | Alpine | Smaller images, still debuggable |
| **Staging** | Alpine | Test Alpine compatibility before production |
| **Production (K8s)** | Chiseled | Maximum security, orchestrator handles healthchecks |
| **Production (Docker Compose)** | Alpine | Need healthchecks, no orchestrator |

---

## Implementation Plan

### Phase 1: Alpine Migration (Low Risk, High Reward)

**Action Items**:
1. Update all 13 Dockerfiles to use Alpine base image
2. Replace `apt-get install curl` with `apk add --no-cache curl`
3. Test locally with Docker Compose
4. Deploy to staging
5. Monitor for compatibility issues

**Risk**: Very low - .NET 10 Alpine is production-grade

**Savings**: ~650-780 MB across all containers

**Time**: 1-2 hours

---

### Phase 2: Chiseled Migration (Optional, High Security)

**Prerequisites**:
- [ ] Kubernetes cluster deployed
- [ ] httpGet liveness/readiness probes configured
- [ ] Monitoring/logging working (no shell access for debugging)

**Action Items**:
1. Create Chiseled Dockerfiles (remove all shell commands)
2. Update Kubernetes manifests with HTTP healthchecks
3. Test in staging cluster
4. Deploy to production

**Risk**: Medium - harder to debug issues without shell access

**Savings**: ~1+ GB across all containers

**Time**: 4-6 hours

---

## Dockerfile Templates

### Template: Alpine (Recommended)

```dockerfile
# Build stage (unchanged)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Dhadgar.sln", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["global.json", "./"]

COPY ["src/Dhadgar.Gateway/Dhadgar.Gateway.csproj", "src/Dhadgar.Gateway/"]
COPY ["src/Shared/Dhadgar.Contracts/Dhadgar.Contracts.csproj", "src/Shared/Dhadgar.Contracts/"]
COPY ["src/Shared/Dhadgar.Shared/Dhadgar.Shared.csproj", "src/Shared/Dhadgar.Shared/"]
COPY ["src/Shared/Dhadgar.Messaging/Dhadgar.Messaging.csproj", "src/Shared/Dhadgar.Messaging/"]
COPY ["src/Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj", "src/Shared/Dhadgar.ServiceDefaults/"]

RUN dotnet restore "src/Dhadgar.Gateway/Dhadgar.Gateway.csproj"

COPY ["src/Dhadgar.Gateway/", "src/Dhadgar.Gateway/"]
COPY ["src/Shared/", "src/Shared/"]

WORKDIR /src/src/Dhadgar.Gateway
RUN dotnet publish "Dhadgar.Gateway.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Runtime stage (Alpine)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Install curl for healthchecks (Alpine uses apk)
RUN apk add --no-cache curl

# Create non-root user (Alpine uses addgroup/adduser)
RUN addgroup -S appuser && adduser -S appuser -G appuser
COPY --from=build /app/publish .
RUN chown -R appuser:appuser /app
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Dhadgar.Gateway.dll"]
```

### Template: Chiseled (Maximum Security)

```dockerfile
# Build stage (unchanged)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Dhadgar.sln", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["global.json", "./"]

COPY ["src/Dhadgar.Gateway/Dhadgar.Gateway.csproj", "src/Dhadgar.Gateway/"]
COPY ["src/Shared/Dhadgar.Contracts/Dhadgar.Contracts.csproj", "src/Shared/Dhadgar.Contracts/"]
COPY ["src/Shared/Dhadgar.Shared/Dhadgar.Shared.csproj", "src/Shared/Dhadgar.Shared/"]
COPY ["src/Shared/Dhadgar.Messaging/Dhadgar.Messaging.csproj", "src/Shared/Dhadgar.Messaging/"]
COPY ["src/Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj", "src/Shared/Dhadgar.ServiceDefaults/"]

RUN dotnet restore "src/Dhadgar.Gateway/Dhadgar.Gateway.csproj"

COPY ["src/Dhadgar.Gateway/", "src/Dhadgar.Gateway/"]
COPY ["src/Shared/", "src/Shared/"]

WORKDIR /src/src/Dhadgar.Gateway
RUN dotnet publish "Dhadgar.Gateway.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Runtime stage (Chiseled - NO SHELL)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app

# NO apk/apt (no package manager)
# NO adduser (no shell)
# User 'app' already exists in chiseled image

COPY --from=build --chown=app:app /app/publish .
USER app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# NO HEALTHCHECK (no curl, no shell)
# Orchestrator (Kubernetes) will handle healthchecks via httpGet

ENTRYPOINT ["dotnet", "Dhadgar.Gateway.dll"]
```

---

## Cost Analysis

**ACR Storage Costs**: $0.10/GB/month

### Current (Debian):
- 13 services × 115 MB avg = 1.5 GB
- 2 tags per service (BuildId + latest) = 3 GB total
- **Cost**: $0.30/month

### After Alpine Migration:
- 13 services × 60 MB avg = 780 MB
- 2 tags per service = 1.56 GB total
- **Cost**: $0.16/month
- **Savings**: $0.14/month (47% reduction)

### After Chiseled Migration:
- 13 services × 45 MB avg = 585 MB
- 2 tags per service = 1.17 GB total
- **Cost**: $0.12/month
- **Savings**: $0.18/month (60% reduction)

**Verdict**: Not a huge dollar savings, but meaningful for security and deployment speed.

---

## Security Benefits

### Attack Surface Reduction

**Debian** (current):
- ~220 packages installed
- Typical CVE count: 20-50 per scan
- Shell available (bash)
- Package manager available (apt)

**Alpine**:
- ~40 packages installed (80% reduction)
- Typical CVE count: 5-15 per scan (70% reduction)
- Shell available (sh/ash)
- Package manager available (apk)

**Chiseled**:
- ~10 packages installed (95% reduction)
- Typical CVE count: 0-5 per scan (90% reduction)
- **NO shell**
- **NO package manager**

### Why Fewer CVEs Matter

Even if your app code is secure, CVEs in base image packages can:
- Trigger compliance failures
- Require emergency patching
- Create alert fatigue for security teams

**Example**: A CVE in `libssl` (present in Debian, not in Chiseled) could force you to rebuild all containers, even though your app doesn't directly use libssl.

---

## Next Steps

1. **Commit current changes** (Security.yml template)
2. **Choose migration path**:
   - ✅ Recommended: Alpine (Phase 1)
   - ⏳ Optional: Chiseled (Phase 2 after K8s deployment)
3. **Update Dockerfiles** (13 files)
4. **Test locally**
5. **Deploy to staging**
6. **Monitor Trivy scans** (should see CVE count drop)

---

## References

- [.NET 10 Alpine Support (GitHub)](https://github.com/dotnet/dotnet-docker/issues/6860)
- [Microsoft Learn: .NET Container OS Targets](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/net-core-net-framework-containers/net-container-os-targets)
- [.NET 10 Alpine Install Guide](https://learn.microsoft.com/en-us/dotnet/core/install/linux-alpine)
- [Understanding Microsoft's Docker Images for .NET](https://blog.sixeyed.com/understanding-microsofts-docker-images-for-net-apps/)

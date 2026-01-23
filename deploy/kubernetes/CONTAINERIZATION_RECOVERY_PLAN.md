# Containerization Recovery Plan

## What Happened

### Original Work Completed
During the previous session, the following containerization infrastructure was created:

1. **13 Dockerfiles** - Multi-stage builds for all microservices
   - Gateway, Identity, Billing, Servers, Nodes, Tasks, Files, Mods, Console, Notifications, Secrets, Discord
   - Pattern: SDK build stage → ASP.NET runtime stage
   - Security: Non-root user (appuser:appuser)
   - Health checks on port 8080

2. **Azure Pipeline Configuration**
   - `azure-pipelines-containers.yml` - Main pipeline config
   - Parallel builds for all 13 services
   - Dual tagging: Build.BuildId + latest
   - ACR cleanup (keep latest 2 images per service)

3. **Helm Chart Updates**
   - Updated `deploy/kubernetes/helm/meridian-console/values.yaml`
   - Changed imageRegistry to: `meridianconsoleacr-etdvg4cthscffqdf.azurecr.io`
   - Changed image repositories from `meridian/*` to `dhadgar/*`

4. **Documentation**
   - `deploy/kubernetes/ACR-DETAILS.md` - ACR reference guide
   - `deploy/kubernetes/CONTAINER-BUILD-SETUP.md` - End-to-end setup walkthrough
   - `deploy/kubernetes/CONTAINER-BUILD-SUMMARY.md` - Implementation summary
   - Updated `CLAUDE.md` with Azure Infrastructure section

### The Problem
The containerization work was created in the working directory but was **NEVER committed** to git. During branch switching confusion (trying to separate from the `sast-linter-discovery` branch work), a `git reset --hard HEAD` command was executed, which **permanently deleted all uncommitted work**.

### Root Cause
- Two Claude instances working in the same repository without using git worktrees
- Branch collision: Both `aura-infra-discovery` (containerization) and `sast-linter-discovery` (code quality) were being worked on
- Attempted to cherry-pick/separate work led to git state confusion
- Hard reset wiped all uncommitted containerization files

### Current State
- Branch: `aura-infra-discovery` exists but is clean (no containerization work)
- All Dockerfiles: **DELETED**
- All pipeline configs: **DELETED**
- All documentation: **DELETED**
- ACR infrastructure: **Still exists** (meridianconsoleacr in Azure)
- Session tokens: **Low** (~138k remaining)

## Recovery Plan

### Prerequisites
- Fresh Claude session with full token budget
- Stay on `aura-infra-discovery` branch throughout recovery
- **DO NOT** touch `sast-linter-discovery` branch (other Claude's work)

### Phase 1: Dockerfile Recreation

Create all 13 Dockerfiles with standardized multi-stage pattern:

**Template Pattern:**
```dockerfile
# Dockerfile for Dhadgar.{Service}
# Build context: Solution root (MeridianConsole/)
# Build command: docker build -f src/Dhadgar.{Service}/Dockerfile -t dhadgar/{service}:latest .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution files
COPY ["Dhadgar.sln", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["global.json", "./"]

# Copy project files
COPY ["src/Dhadgar.{Service}/Dhadgar.{Service}.csproj", "src/Dhadgar.{Service}/"]
COPY ["src/Shared/Dhadgar.Contracts/Dhadgar.Contracts.csproj", "src/Shared/Dhadgar.Contracts/"]
COPY ["src/Shared/Dhadgar.Shared/Dhadgar.Shared.csproj", "src/Shared/Dhadgar.Shared/"]
COPY ["src/Shared/Dhadgar.Messaging/Dhadgar.Messaging.csproj", "src/Shared/Dhadgar.Messaging/"]
COPY ["src/Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj", "src/Shared/Dhadgar.ServiceDefaults/"]

# Restore
RUN dotnet restore "src/Dhadgar.{Service}/Dhadgar.{Service}.csproj"

# Copy source
COPY ["src/Dhadgar.{Service}/", "src/Dhadgar.{Service}/"]
COPY ["src/Shared/", "src/Shared/"]

# Build and publish
WORKDIR /src/src/Dhadgar.{Service}
RUN dotnet publish "Dhadgar.{Service}.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd -r appuser && useradd -r -g appuser appuser
COPY --from=build /app/publish .
RUN chown -R appuser:appuser /app
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Dhadgar.{Service}.dll"]
```

**Services to create:**
- [ ] src/Dhadgar.Gateway/Dockerfile
- [ ] src/Dhadgar.Identity/Dockerfile
- [ ] src/Dhadgar.Billing/Dockerfile
- [ ] src/Dhadgar.Servers/Dockerfile
- [ ] src/Dhadgar.Nodes/Dockerfile
- [ ] src/Dhadgar.Tasks/Dockerfile
- [ ] src/Dhadgar.Files/Dockerfile
- [ ] src/Dhadgar.Mods/Dockerfile
- [ ] src/Dhadgar.Console/Dockerfile
- [ ] src/Dhadgar.Notifications/Dockerfile
- [ ] src/Dhadgar.Secrets/Dockerfile
- [ ] src/Dhadgar.Discord/Dockerfile

### Phase 2: Azure Pipeline Configuration

**File: `azure-pipelines-containers.yml`** (in MeridianConsole root)
```yaml
trigger:
  branches:
    include:
      - main
      - develop
      - feature/*
  paths:
    include:
      - src/Dhadgar.Gateway/**
      - src/Dhadgar.Identity/**
      - src/Dhadgar.Billing/**
      - src/Dhadgar.Servers/**
      - src/Dhadgar.Nodes/**
      - src/Dhadgar.Tasks/**
      - src/Dhadgar.Files/**
      - src/Dhadgar.Mods/**
      - src/Dhadgar.Console/**
      - src/Dhadgar.Notifications/**
      - src/Dhadgar.Secrets/**
      - src/Dhadgar.Discord/**
      - src/Shared/**
      - Dhadgar.sln

resources:
  repositories:
    - repository: YAML
      type: github
      endpoint: SandboxServers
      name: SandboxServers/Azure-Pipeline-YAML

variables:
  azureSubscription: 'Azure-Sub'  # Service connection name

extends:
  template: Templates/Container-Build/Pipeline/Pipeline.yml@YAML
  parameters:
    acrName: 'meridianconsoleacr'
    acrServiceConnection: 'meridianconsoleacr'
    pushLatest: true
    cleanup: true
    keepCount: 2
    services:
      - name: Gateway
        imageName: dhadgar/gateway
        dockerfilePath: src/Dhadgar.Gateway/Dockerfile
      - name: Identity
        imageName: dhadgar/identity
        dockerfilePath: src/Dhadgar.Identity/Dockerfile
      - name: Billing
        imageName: dhadgar/billing
        dockerfilePath: src/Dhadgar.Billing/Dockerfile
      - name: Servers
        imageName: dhadgar/servers
        dockerfilePath: src/Dhadgar.Servers/Dockerfile
      - name: Nodes
        imageName: dhadgar/nodes
        dockerfilePath: src/Dhadgar.Nodes/Dockerfile
      - name: Tasks
        imageName: dhadgar/tasks
        dockerfilePath: src/Dhadgar.Tasks/Dockerfile
      - name: Files
        imageName: dhadgar/files
        dockerfilePath: src/Dhadgar.Files/Dockerfile
      - name: Mods
        imageName: dhadgar/mods
        dockerfilePath: src/Dhadgar.Mods/Dockerfile
      - name: Console
        imageName: dhadgar/console
        dockerfilePath: src/Dhadgar.Console/Dockerfile
      - name: Notifications
        imageName: dhadgar/notifications
        dockerfilePath: src/Dhadgar.Notifications/Dockerfile

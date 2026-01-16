# Containerization Recovery Plan

## What Happened

### Original Work Completed
During the previous session, the following containerization infrastructure was created:

1. **13 Dockerfiles** - Multi-stage builds for all microservices
   - Gateway, Identity, Billing, Servers, Nodes, Tasks, Files, Mods, Console, Notifications, Firewall, Secrets, Discord
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
- [ ] src/Dhadgar.Firewall/Dockerfile
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
      - src/Dhadgar.Firewall/**
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
      - name: Firewall
        imageName: dhadgar/firewall
        dockerfilePath: src/Dhadgar.Firewall/Dockerfile
      - name: Secrets
        imageName: dhadgar/secrets
        dockerfilePath: src/Dhadgar.Secrets/Dockerfile
      - name: Discord
        imageName: dhadgar/discord
        dockerfilePath: src/Dhadgar.Discord/Dockerfile
```

**Note:** Azure Pipeline templates already exist in `/mnt/c/Users/xxL0L/code_projects/Azure-Pipeline-YAML/Templates/Container-Build/` from previous session. These were successfully created and do NOT need to be recreated.

### Phase 3: Helm Chart Updates

**File: `deploy/kubernetes/helm/meridian-console/values.yaml`**

Update the following sections:

```yaml
# Image registry configuration
imageRegistry: "meridianconsoleacr-etdvg4cthscffqdf.azurecr.io"

# Update each service image repository
gateway:
  enabled: true
  replicaCount: 2
  image:
    repository: dhadgar/gateway  # Changed from meridian/gateway
    tag: latest
    pullPolicy: IfNotPresent

identity:
  enabled: true
  replicaCount: 2
  image:
    repository: dhadgar/identity  # Changed from meridian/identity
    tag: latest
    pullPolicy: IfNotPresent

# ... repeat for all 13 services
```

### Phase 4: Documentation

**File: `deploy/kubernetes/ACR-DETAILS.md`**
- Complete ACR reference with authentication commands
- Usage examples (login, list repositories, show tags, check storage)
- Integration with Kubernetes (image pull secrets)

**File: `deploy/kubernetes/CONTAINER-BUILD-SETUP.md`**
- 4-phase setup walkthrough
- ACR configuration
- Azure DevOps setup
- Kubernetes deployment steps

**File: `deploy/kubernetes/CONTAINER-BUILD-SUMMARY.md`**
- High-level architecture overview
- Build flow diagram
- Storage calculations
- Troubleshooting guide

**File: `CLAUDE.md`** - Add Azure Infrastructure section after "Configuration Management" section:
```markdown
## Azure Infrastructure

### Azure Container Registry (ACR)

The platform uses Azure Container Registry to store Docker images for all microservices.

**ACR Details**:
- **Name**: `meridianconsoleacr`
- **Login Server**: `meridianconsoleacr-etdvg4cthscffqdf.azurecr.io`
- **Resource Group**: `meridian-rg`
- **Location**: `centralus`
- **SKU**: Basic (10 GB storage limit)
- **Subscription**: `c87357b8-2149-476d-b91c-eb79095634ac`
- **Created**: 2025-12-27
- **Admin User**: Disabled (use service principal authentication)

**Image Naming Convention**:
- All microservice images use the `dhadgar/*` namespace
- Example: `meridianconsoleacr-etdvg4cthscffqdf.azurecr.io/dhadgar/gateway:latest`

**Authentication**:
```bash
# Login to ACR (requires Azure CLI authentication first)
az acr login --name meridianconsoleacr

# Or use the full login server
az acr login --name meridianconsoleacr-etdvg4cthscffqdf.azurecr.io
```

**Verify ACR Contents**:
```bash
# List all repositories
az acr repository list --name meridianconsoleacr --output table

# List tags for a specific repository
az acr repository show-tags \
  --name meridianconsoleacr \
  --repository dhadgar/gateway \
  --output table

# Check storage usage (Basic plan: 10 GB limit)
az acr show-usage --name meridianconsoleacr --output table
```

**Container Build Pipeline**:
- Pipeline: `azure-pipelines-containers.yml`
- Builds all 13 microservices in parallel
- Pushes images to ACR with dual tagging (Build ID + `latest`)
- Automatic cleanup keeps latest 2 images per service
- See: `deploy/kubernetes/CONTAINER-BUILD-SETUP.md` for details
```

### Phase 5: Testing & Validation

**Test 1: Docker Build**
```bash
cd /mnt/c/Users/xxL0L/code_projects/MeridianConsole
docker build -f src/Dhadgar.Gateway/Dockerfile -t dhadgar/gateway:test .
```

**Test 2: Verify All Dockerfiles**
```bash
ls -1 src/Dhadgar.*/Dockerfile | wc -l  # Should output: 13
```

**Test 3: Helm Chart Validation**
```bash
cd deploy/kubernetes/helm/meridian-console
helm lint .
```

**Test 4: Pipeline YAML Validation**
```bash
python3 -c "import yaml; yaml.safe_load(open('azure-pipelines-containers.yml'))"
```

### Phase 6: Git Commit & PR

**Stay on `aura-infra-discovery` branch throughout!**

```bash
# Verify branch
git branch --show-current  # Should output: aura-infra-discovery

# Stage all containerization files
git add \
  src/Dhadgar.*/Dockerfile \
  azure-pipelines-containers.yml \
  deploy/kubernetes/*.md \
  deploy/kubernetes/helm/meridian-console/values.yaml \
  CLAUDE.md

# Commit
git commit -m "feat: add comprehensive containerization infrastructure for all microservices

Implemented complete Docker and Azure Container Registry integration:

**Dockerfiles** (13 services):
- Multi-stage builds (SDK → ASP.NET runtime)
- Non-root user security (appuser:appuser)
- Health checks for all services
- Optimized layer caching

**Azure Pipelines**:
- azure-pipelines-containers.yml for parallel builds
- Extends templates from Azure-Pipeline-YAML repo
- Dual tagging: Build.BuildId + latest
- Automated ACR cleanup (keep latest 2 images)

**Helm Chart Updates**:
- Updated values.yaml with ACR login server
- Changed image repositories from meridian/* to dhadgar/*

**Documentation**:
- CLAUDE.md: Added Azure Infrastructure section
- ACR-DETAILS.md: Complete ACR reference guide
- CONTAINER-BUILD-SETUP.md: End-to-end setup walkthrough
- CONTAINER-BUILD-SUMMARY.md: Implementation summary

All microservices are now containerized and ready for ACR push.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"

# Push to remote
git push -u origin aura-infra-discovery

# Create PR
gh pr create \
  --base main \
  --head aura-infra-discovery \
  --title "feat: Add containerization infrastructure for all microservices" \
  --body "See CONTAINERIZATION_RECOVERY_PLAN.md for full details"
```

## Critical Reminders

1. **DO NOT touch `sast-linter-discovery` branch** - That's the other Claude's work
2. **Stay on `aura-infra-discovery` throughout** - No branch switching
3. **Commit early and often** - Don't let work exist only in working directory
4. **Test before committing** - At least one Docker build test
5. **Fresh session required** - Current session has limited tokens (~138k)

## Future Prevention

To prevent this issue in the future:
- Use **git worktrees** for parallel Claude sessions (documented in CLAUDE.md)
- Each Claude instance gets its own worktree directory
- Changes isolated, no branch collision possible
- Example:
  ```bash
  git worktree add ../MeridianConsole-aura feature/aura-infra
  git worktree add ../MeridianConsole-sast feature/sast-linter
  ```

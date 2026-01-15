# Azure DevOps Self-Hosted Agents

This directory contains a Docker setup for running self-hosted Azure DevOps agents with full glibc compatibility.

## Overview

**Image Base**: Ubuntu 24.04 LTS (Noble Numbat)

**Key Features**:
- ✅ Automatic agent deregistration on shutdown (no orphaned agents!)
- ✅ Proper signal handling with tini init system
- ✅ Graceful shutdown with 45-second timeout
- ✅ Container-aware agent naming
- ✅ Scalable via Docker Compose

**Included Tools**:
- .NET SDK 10 (pinned via `global.json`)
- Node.js 20 + npm
- Azure CLI
- PowerShell 7
- Docker CLI + Buildx + Compose plugins
- Git, curl, jq, make, g++, Python3

**Why Ubuntu 24.04 instead of Alpine?**
- Full glibc compatibility (no musl issues with native binaries)
- Official Microsoft package repos for PowerShell and Azure CLI
- Proven compatibility with Azure DevOps agent
- Better Docker CLI support

## Quick Start

### 1. Set Environment Variables

Create a `.env` file in this directory:

```bash
# Required
AZP_URL=https://dev.azure.com/your-org
AZP_TOKEN=your-pat-token
AZP_POOL=Default

# Optional
AZP_AGENT_VERSION=4.266.2
DOTNET_SDK_VERSION=  # Leave empty to use global.json
```

**PAT Token Permissions**: Agent Pools (Read & Manage)

### 2. Build Image

```bash
docker compose -f agent-swarm.yml build
```

### 3. Run Agent(s)

**Single agent**:
```bash
docker compose -f agent-swarm.yml up -d
```

**Scale to multiple agents**:
```bash
docker compose -f agent-swarm.yml up -d --scale azdo-agent=3
```

### 4. Verify Registration

Check Azure DevOps → Project Settings → Agent pools → [Your Pool]

Agents should appear with names like:
- `dhadgar-dev-agents-azdo-agent-1`
- `dhadgar-dev-agents-azdo-agent-2`
- `dhadgar-dev-agents-azdo-agent-3`

### 5. Shutdown

```bash
# Graceful shutdown (agents will deregister)
docker compose -f agent-swarm.yml down

# Or scale down
docker compose -f agent-swarm.yml up -d --scale azdo-agent=1
```

**Important**: The agent WILL deregister itself automatically. No manual cleanup needed!

## Architecture

### Signal Handling

The agent uses a layered signal handling approach:

1. **tini** (PID 1) - Proper init system, forwards signals correctly
2. **start.sh** - Traps SIGTERM/SIGINT, runs cleanup function
3. **Cleanup function** - Stops agent process, runs `config.sh remove`

When Docker sends SIGTERM (e.g., during `docker stop` or `docker compose down`):
- tini receives SIGTERM and forwards to start.sh
- start.sh traps signal, sets `shutdown_requested=1`
- Cleanup function sends SIGTERM to agent process
- Waits up to 30s for agent to exit gracefully
- Runs `./config.sh remove` to deregister from Azure DevOps
- Docker waits up to 45s total before force killing (configured via `stop_grace_period`)

### Agent Naming

Agents automatically name themselves based on the container name:

- Queries Docker socket at `/var/run/docker.sock`
- Uses container name (e.g., `dhadgar-dev-agents-azdo-agent-1`)
- Falls back to `$HOSTNAME` if socket unavailable

This ensures unique, identifiable agent names when scaling.

### Work Directory Isolation

Each agent gets its own work directory under `/azp/_work/{agent-name}` to prevent conflicts when scaling.

## Troubleshooting

### Agent Not Deregistering

**Symptoms**: Old agents stay "Offline" in Azure DevOps after shutdown.

**Check**:
1. Container logs: `docker compose -f agent-swarm.yml logs azdo-agent`
2. Look for: "Deregistering agent from Azure DevOps..."
3. Look for: "Agent {name} successfully deregistered."

**Common Issues**:
- PAT token expired or lacks permissions
- Container killed before cleanup (check `stop_grace_period`)
- Network issue preventing API call

**Fix**: Ensure PAT token has "Agent Pools (Read & Manage)" permission and `stop_grace_period` is at least 45s.

### Build Failures

**Issue**: .NET SDK version mismatch

**Fix**: Ensure `global.json` exists in repo root with correct SDK version:
```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestPatch"
  }
}
```

**Issue**: Missing Alpine packages

**Fix**: Add to Dockerfile RUN command:
```dockerfile
RUN apk add --no-cache \
    your-package-here
```

Search packages: https://pkgs.alpinelinux.org/packages

### Agent Won't Start

**Check environment variables**:
```bash
docker compose -f agent-swarm.yml config
```

**Check agent logs**:
```bash
docker compose -f agent-swarm.yml logs -f azdo-agent
```

**Common Issues**:
- Missing `AZP_URL` or `AZP_TOKEN`
- Invalid PAT token
- Agent pool doesn't exist
- Network connectivity to Azure DevOps

## Image Size

**Ubuntu 24.04 LTS base**: ~77 MB compressed, ~196 MB uncompressed
**Final agent image** (with all tools): ~650-750 MB

The image is larger than Alpine-based alternatives but provides:
- Full glibc compatibility (no musl workarounds)
- Native Microsoft tooling support
- Stable, enterprise-ready LTS base

## Advanced Configuration

### Custom Agent Name

Set `AZP_AGENT_NAME` environment variable:

```yaml
environment:
  AZP_AGENT_NAME: my-custom-agent
```

### Custom Work Directory

Set `AZP_WORK` environment variable:

```yaml
environment:
  AZP_WORK: /custom/_work
```

### Run Specific Agent Version

Override via build arg:

```bash
docker compose -f agent-swarm.yml build \
  --build-arg AGENT_VERSION=4.250.0
```

### Add Custom Tools

Edit `agent.Dockerfile`:

```dockerfile
# Add your tools here
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        terraform \
        kubectl \
        helm \
    && rm -rf /var/lib/apt/lists/*
```

Or install from external sources:

```dockerfile
# Example: Install Helm
RUN curl https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
```

Then rebuild:
```bash
docker compose -f agent-swarm.yml build --no-cache
```

## Maintenance

### Update Agent Version

1. Edit `.env`:
   ```bash
   AZP_AGENT_VERSION=4.270.0
   ```

2. Rebuild:
   ```bash
   docker compose -f agent-swarm.yml build --no-cache
   ```

3. Rolling restart:
   ```bash
   docker compose -f agent-swarm.yml up -d
   ```

   Docker Compose will recreate containers with new image, deregistering old agents automatically.

### Update .NET SDK

1. Update `global.json` in repo root
2. Rebuild image (no `.env` change needed)

### Clean Up Old Images

```bash
docker image prune -f
```

## Security Considerations

### PAT Token Storage

**Never commit PAT tokens to Git!**

Use `.env` file (add to `.gitignore`) or pass via environment:

```bash
export AZP_TOKEN="your-token"
docker compose -f agent-swarm.yml up -d
```

### Docker Socket Access

The agent mounts `/var/run/docker.sock` for:
- Container name detection (agent naming)
- Pipeline Docker tasks (docker build, docker push, etc.)

**Warning**: This grants the agent full Docker daemon access. Only run trusted pipelines.

### Ubuntu LTS vs Alpine

This image uses Ubuntu 24.04 LTS for maximum compatibility:

**Pros**:
- Full glibc support (all native binaries work)
- Official Microsoft repos for PowerShell, Azure CLI
- Docker CE packages available
- Enterprise support available

**Cons**:
- Larger image size (~650-750 MB vs Alpine's ~350 MB)
- Slower to download and extract

If image size is critical and you don't need Microsoft tooling, consider Alpine. Otherwise, Ubuntu provides the most reliable experience for Azure DevOps agents.

## References

- [Azure DevOps Agent Documentation](https://learn.microsoft.com/en-us/azure/devops/pipelines/agents/docker)
- [Ubuntu 24.04 Packages](https://packages.ubuntu.com/noble/)
- [Docker Compose Scale](https://docs.docker.com/compose/compose-file/deploy/#replicas)
- [tini Init System](https://github.com/krallin/tini)
- [Microsoft Linux Software Repository](https://learn.microsoft.com/en-us/linux/packages)

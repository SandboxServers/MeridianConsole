# Azure DevOps Self-Hosted Agents

This directory contains a minimal, Alpine-based Docker setup for running self-hosted Azure DevOps agents.

## Overview

**Image Base**: Alpine Linux 3.21 (~5MB base vs Ubuntu's ~77MB)

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
- Git, curl, jq, make, g++, Python3

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

## Image Size Comparison

| Base Image | Compressed Size | Uncompressed Size |
|------------|-----------------|-------------------|
| Ubuntu 22.04 | ~77 MB | ~196 MB |
| Alpine 3.21 | ~5 MB | ~12 MB |
| **Savings** | **~93%** | **~94%** |

Final agent image (with all tools): ~350 MB vs ~800+ MB with Ubuntu

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
RUN apk add --no-cache \
    terraform \
    kubectl \
    helm
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

### Alpine vs Ubuntu

Alpine uses musl libc instead of glibc. Most .NET and Node.js tooling works fine, but edge cases exist:

- Some native npm packages may fail (rare)
- Pre-compiled binaries expecting glibc won't work

If you encounter compatibility issues, switch back to Ubuntu:

```dockerfile
FROM ubuntu:22.04
# Change apk commands to apt-get
# Change su-exec to gosu
```

## References

- [Azure DevOps Agent Documentation](https://learn.microsoft.com/en-us/azure/devops/pipelines/agents/docker)
- [Alpine Linux Package Search](https://pkgs.alpinelinux.org/packages)
- [Docker Compose Scale](https://docs.docker.com/compose/compose-file/deploy/#replicas)
- [tini Init System](https://github.com/krallin/tini)

# Azure DevOps Agent with Pre-installed Security Tools

This Docker-based Azure DevOps agent includes all security scanning tools pre-installed to avoid downloading them on every pipeline run.

## What's Included

All security tools are baked into the agent image at build time:

| Tool | Purpose | Version | Location |
|------|---------|---------|----------|
| **Semgrep** | SAST (Static Application Security Testing) | Latest | System Python packages |
| **Checkov** | IaC (Infrastructure as Code) scanning | Latest | System Python packages |
| **OWASP Dependency-Check** | SCA (Software Composition Analysis) | 11.1.0 | `/usr/local/bin/dependency-check` |
| **Trivy** | Container image vulnerability scanning | 0.58.1 | `/usr/local/bin/trivy` |
| **GitLeaks** | Secret scanning | 8.21.2 | `/usr/local/bin/gitleaks` |
| **Syft** | SBOM (Software Bill of Materials) generation | 1.18.1 | `/usr/local/bin/syft` |
| **Java 17 (OpenJDK)** | Required for OWASP Dependency-Check | 17 | `/usr/lib/jvm/java-17-openjdk-amd64` |

## Building the Agent

From the repository root:

```bash
# Build the agent image
docker build \
  -f deploy/azure-pipelines/agent.Dockerfile \
  -t azp-agent-security:latest \
  --build-arg AGENT_VERSION=4.266.2 \
  .
```

**Note**: The Dockerfile requires `global.json` from the repository root to determine the .NET SDK version.

## Running the Agent

### Prerequisites

1. **Azure DevOps PAT Token**: Create a Personal Access Token with Agent Pools (read, manage) scope
2. **Agent Pool**: Create an agent pool in Azure DevOps (e.g., "Sandbox Servers Agents")

### Environment Variables

```bash
AZP_URL=https://dev.azure.com/YourOrganization
AZP_TOKEN=your-pat-token-here
AZP_POOL=Sandbox Servers Agents
AZP_AGENT_NAME=docker-agent-01  # Optional, auto-generated if not set
```

### Docker Compose (Recommended)

Create a `docker-compose.yml`:

```yaml
services:
  azp-agent:
    image: azp-agent-security:latest
    restart: unless-stopped
    environment:
      AZP_URL: https://dev.azure.com/YourOrganization
      AZP_TOKEN: ${AZP_TOKEN}
      AZP_POOL: Sandbox Servers Agents
    volumes:
      # Mount Docker socket for container builds
      - /var/run/docker.sock:/var/run/docker.sock
      # Optional: Persistent work directory
      - ./azp-agent-work:/azp/_work
```

Run:

```bash
export AZP_TOKEN="your-pat-token"
docker compose up -d
```

### Docker Run (Alternative)

```bash
docker run -d \
  --name azp-agent \
  --restart unless-stopped \
  -e AZP_URL=https://dev.azure.com/YourOrganization \
  -e AZP_TOKEN=your-pat-token \
  -e AZP_POOL="Sandbox Servers Agents" \
  -v /var/run/docker.sock:/var/run/docker.sock \
  azp-agent-security:latest
```

## Security Pipeline Integration

The Security stage template ([Templates/Standalone/Stages/Security.yml](../../../c:/Users/Steve/source/projects/Azure-Pipeline-YAML/Templates/Standalone/Stages/Security.yml)) has been optimized to use pre-installed tools.

**Before** (downloads tools every run):
```yaml
- pwsh: |
    Write-Host "Installing Semgrep..."
    pip install semgrep
    # ... PATH manipulation ...
  displayName: 'Install Semgrep'

- pwsh: |
    Write-Host "Running Semgrep..."
    semgrep scan ...
  displayName: 'Run Semgrep'
```

**After** (uses pre-installed tools):
```yaml
- pwsh: |
    Write-Host "Running Semgrep..."
    semgrep scan ...
  displayName: 'Run Semgrep'
```

**Time savings per pipeline run**: ~3-5 minutes (depending on network speed and scanner downloads)

## Tool Verification

SSH into the running agent container:

```bash
docker exec -it azp-agent bash
```

Verify installations:

```bash
# Python tools
semgrep --version
checkov --version

# Java
java -version
echo $JAVA_HOME

# OWASP Dependency-Check
dependency-check --version

# Container tools
trivy --version
syft version
gitleaks version

# Other prerequisites
docker --version
dotnet --version
pwsh -Version
az version
```

## Updating Security Tools

To update tool versions:

1. Edit [agent.Dockerfile](agent.Dockerfile)
2. Update version numbers in the relevant `RUN` commands
3. Rebuild the agent image
4. Redeploy the agent container

**Example** - Update Trivy from 0.58.1 to 0.59.0:

```dockerfile
# Before
RUN curl -fsSL https://github.com/aquasecurity/trivy/releases/download/v0.58.1/trivy_0.58.1_Linux-64bit.tar.gz -o /tmp/trivy.tar.gz \

# After
RUN curl -fsSL https://github.com/aquasecurity/trivy/releases/download/v0.59.0/trivy_0.59.0_Linux-64bit.tar.gz -o /tmp/trivy.tar.gz \
```

## Troubleshooting

### Agent Not Registering

**Symptom**: Container starts but agent doesn't appear in Azure DevOps pool.

**Fixes**:
- Check AZP_TOKEN is valid and has Agent Pools permissions
- Verify AZP_URL is correct (no trailing slash)
- Check container logs: `docker logs azp-agent`

### Docker Socket Permission Denied

**Symptom**: Pipeline fails with "permission denied while trying to connect to Docker daemon socket"

**Fix**: The agent automatically adjusts Docker group permissions in `start.sh`. If issues persist:

```bash
# On host, add your user to docker group
sudo usermod -aG docker $USER

# Verify socket permissions
ls -l /var/run/docker.sock
```

### Security Scanner Not Found

**Symptom**: "semgrep: command not found" or similar errors.

**Fixes**:
1. Verify the tool is installed in the agent image:
   ```bash
   docker exec azp-agent which semgrep
   ```

2. If missing, rebuild the agent image with the latest Dockerfile

3. Ensure you're using the correct agent pool in your pipeline YAML:
   ```yaml
   pool:
     name: 'Sandbox Servers Agents'  # Must match your pool name
   ```

### NVD API Rate Limiting (OWASP Dependency-Check)

**Symptom**: "429 Too Many Requests" or slow SCA scans.

**Fix**: Set the NVD API key in your Azure Pipelines variable group:

1. Get a free API key: https://nvd.nist.gov/developers/request-an-api-key
2. Add to `security-scanning` variable group: `NVD_API_KEY=your-key-here`
3. Mark as secret

The pipeline automatically passes this to OWASP Dependency-Check via the `nvdApiKey` parameter.

### OWASP Dependency-Check Database Storage

**Symptom**: First scan is slow (5-15 minutes) on every pipeline run.

**Cause**: The NVD database (~200MB) is stored in `$(Agent.TempDirectory)` which is ephemeral and cleaned up between runs.

**Fix** (optional, for faster subsequent scans):

1. Create a persistent volume in [agent-swarm.yml](agent-swarm.yml):
   ```yaml
   volumes:
     - azp-agent-work:/azp/_work
     - dependency-check-data:/azp/_work/_tool/dependency-check-data
   ```

2. Update Security.yml to use the persistent path (instead of `$(Agent.TempDirectory)/dependency-check-data`)

**Trade-off**: Persistence saves 5-10 minutes per scan but uses ~500MB disk space per agent.

## Cost Optimization

**Estimated savings per pipeline run**:
- Network bandwidth: ~200-500 MB (scanner downloads)
- Pipeline time: 3-5 minutes
- Agent compute: ~$0.02-0.05 per run (assuming $0.01/min agent cost)

**For 100 pipeline runs/month**:
- Time saved: 5-8 hours
- Cost saved: $2-5 (agent compute only)
- Network bandwidth saved: 20-50 GB

## Architecture Notes

### Why Pre-install vs Dynamic Install?

**Dynamic install** (downloading on each run):
- ✅ Always gets latest versions
- ❌ Slower pipeline runs (3-5 min overhead)
- ❌ Network bandwidth waste
- ❌ External dependency (GitHub releases must be available)
- ❌ Rate limiting issues

**Pre-installed** (baked into agent):
- ✅ Fast pipeline runs (no download overhead)
- ✅ No network dependencies
- ✅ Predictable tool versions
- ✅ Works in air-gapped environments
- ❌ Requires agent rebuild to update tools

**Decision**: Pre-install is better for production pipelines. Update tools monthly or when CVEs are discovered.

### Docker Socket Access

The agent mounts the host Docker socket to enable container builds within pipelines. This is **required** for:
- Building service container images (Package stage)
- Loading container tarballs for Trivy/Syft scanning

**Security consideration**: The agent runs as a non-root user (`azp`) but is added to the Docker group. This grants Docker API access, which is equivalent to root on the host. Only run this agent on trusted infrastructure.

## Maintenance Schedule

Recommended update cadence:

| Component | Update Frequency | Reason |
|-----------|------------------|--------|
| Security tools | Monthly | Stay current with vulnerability databases |
| Azure DevOps agent | Quarterly | New features, bug fixes |
| Base OS (Ubuntu) | When LTS updates | Security patches |
| .NET SDK | When project updates | Match `global.json` |

## Related Documentation

- [Security Stage Template](../../../c:/Users/Steve/source/projects/Azure-Pipeline-YAML/Templates/Standalone/Stages/Security.yml)
- [Security Scanner Setup](../../../c:/Users/Steve/source/projects/Azure-Pipeline-YAML/Templates/Standalone/Stages/SECURITY-SCANNER-SETUP.md)
- [PR Comment Setup](../../../c:/Users/Steve/source/projects/Azure-Pipeline-YAML/Templates/Standalone/Stages/SECURITY-PR-COMMENTS.md)
- [Azure Pipelines Agent Docs](https://learn.microsoft.com/en-us/azure/devops/pipelines/agents/docker)

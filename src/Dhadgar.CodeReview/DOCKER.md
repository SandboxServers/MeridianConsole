# Docker Deployment Guide

## Overview

The CodeReview service can be deployed as a Docker Compose container group with:
- **CodeReview Service**: ASP.NET Core application handling webhooks and orchestrating reviews
- **Ollama**: GPU-accelerated LLM inference engine running DeepSeek Coder

## Prerequisites

1. **Docker Desktop** with WSL2 backend (Windows)
2. **NVIDIA GPU Driver** (version 527.41+)
3. **NVIDIA Container Toolkit** (installed in WSL2)
4. **GitHub App** created and configured (see main README.md)

## Quick Start

### 1. Prepare Secrets

```powershell
# Navigate to service directory
cd src/Dhadgar.CodeReview

# Create secrets directory
mkdir secrets

# Copy your GitHub App private key
cp ~/Downloads/your-app.private-key.pem secrets/github-app.pem

# Copy environment template and fill in values
cp .env.example .env
# Edit .env with your GitHub App credentials
```

### 2. Deploy with PowerShell Script

```powershell
# Full deployment (recommended for first time)
.\Deploy-CodeReview.ps1

# Skip building if image already exists
.\Deploy-CodeReview.ps1 -SkipBuild

# Clean deployment (removes existing containers/volumes)
.\Deploy-CodeReview.ps1 -Clean

# Use a different model
.\Deploy-CodeReview.ps1 -Model "deepseek-coder:7b"
```

The script will:
- ✅ Check all prerequisites (Docker, GPU, secrets)
- ✅ Build the CodeReview Docker image
- ✅ Pull the Ollama image
- ✅ Start both services
- ✅ Download the LLM model (~20GB)
- ✅ Run health checks

### 3. Verify Deployment

```powershell
# Check service status
docker compose ps

# View logs
docker compose logs -f

# Test Ollama
docker exec codereview-ollama ollama list

# Test CodeReview API
curl http://localhost:8080/healthz
```

### 4. Expose Webhook

```powershell
# In a new terminal
ngrok http 8080

# Update GitHub App webhook URL to: https://YOUR-URL.ngrok.io/webhook
```

## Manual Deployment

If you prefer not to use the PowerShell script:

```bash
# Build the image
docker build -f src/Dhadgar.CodeReview/Dockerfile -t dhadgar/codereview:latest .

# Start services
cd src/Dhadgar.CodeReview
docker compose up -d

# Pull the model
docker exec codereview-ollama ollama pull deepseek-coder:33b

# View logs
docker compose logs -f
```

## Configuration

### Environment Variables

Edit the `.env` file:

```bash
GITHUB_APP_ID=123456
GITHUB_INSTALLATION_ID=789012
GITHUB_WEBHOOK_SECRET=your-webhook-secret
```

### Model Configuration

Edit `docker-compose.yml` to change the model:

```yaml
environment:
  - Ollama__Model=deepseek-coder:7b  # Faster, less accurate
  - Ollama__Model=deepseek-coder:33b # Balanced (default)
```

### Review Settings

```yaml
environment:
  - Review__EnableAutoReview=true    # Auto-review on new commits
  - Review__MaxDiffSize=100000       # Max changes allowed
  - Review__MaxFilesPerReview=50     # Max files allowed
```

## Container Management

### Start/Stop

```bash
# Start services
docker compose up -d

# Stop services
docker compose down

# Stop and remove volumes
docker compose down -v

# Restart a service
docker compose restart codereview
docker compose restart ollama
```

### View Logs

```bash
# All services
docker compose logs -f

# Specific service
docker compose logs -f codereview
docker compose logs -f ollama

# Last 100 lines
docker compose logs --tail=100
```

### Access Containers

```bash
# Shell into CodeReview service
docker exec -it codereview-service bash

# Shell into Ollama
docker exec -it codereview-ollama bash

# Run Ollama commands
docker exec codereview-ollama ollama list
docker exec codereview-ollama ollama run deepseek-coder:33b "test"
```

## GPU Monitoring

```bash
# Check GPU usage
docker exec codereview-ollama nvidia-smi

# Watch GPU usage in real-time
watch -n 1 'docker exec codereview-ollama nvidia-smi'
```

## Data Persistence

Data is stored in Docker volumes:

- **ollama_models**: LLM models (~20GB)
- **codereview_data**: SQLite database (review history)
- **codereview_logs**: Service logs

To backup:

```bash
# Backup database
docker cp codereview-service:/app/data/codereview.db ./backup-$(date +%Y%m%d).db

# Backup logs
docker cp codereview-service:/app/logs ./logs-backup
```

## Troubleshooting

### GPU Not Detected

```bash
# Check NVIDIA drivers in WSL2
wsl -d Ubuntu -- nvidia-smi

# Verify Docker GPU access
docker run --rm --gpus all nvidia/cuda:12.9.0-base-ubuntu22.04 nvidia-smi
```

### Model Download Failed

```bash
# Check available disk space
df -h

# Manually pull model
docker exec -it codereview-ollama ollama pull deepseek-coder:33b
```

### Service Won't Start

```bash
# Check logs for errors
docker compose logs codereview

# Verify .env file exists
cat .env

# Verify secrets directory
ls -la secrets/
```

### Webhook Not Receiving Events

```bash
# Check if service is listening
curl http://localhost:8080/healthz

# Check ngrok is running
curl http://localhost:4040/api/tunnels

# View webhook logs
docker compose logs -f codereview | grep -i webhook
```

## Performance Tuning

### Use Smaller Model (Faster Reviews)

```yaml
environment:
  - Ollama__Model=deepseek-coder:7b
```

**Performance**: ~5-15 seconds per review
**VRAM**: ~8-10GB

### Use Larger Model (Better Quality)

```yaml
environment:
  - Ollama__Model=deepseek-coder:33b
```

**Performance**: ~10-30 seconds per review
**VRAM**: ~20-22GB

### Adjust Timeout

```yaml
environment:
  - Ollama__TimeoutSeconds=600  # Increase for large PRs
```

## Security Considerations

### Secrets Management

- ✅ GitHub App private key is mounted read-only
- ✅ Environment variables loaded from `.env` file
- ✅ `.env` and `secrets/` are in `.gitignore`

### Network Isolation

The services run in an isolated Docker network (`codereview-network`) with no external access except:
- CodeReview API: Port 8080
- Ollama API: Port 11434 (for debugging)

### Production Hardening

For production deployment:

1. Remove Ollama port exposure
2. Use Docker secrets instead of `.env` file
3. Enable TLS on CodeReview API
4. Use a reverse proxy (Traefik, Caddy, nginx)
5. Implement rate limiting

## Resource Requirements

### Minimum

- **CPU**: 4 cores
- **RAM**: 16GB
- **GPU**: RTX 4090 (24GB VRAM)
- **Disk**: 50GB (for model + data)

### Recommended

- **CPU**: 8+ cores
- **RAM**: 32GB
- **GPU**: RTX 4090
- **Disk**: 100GB SSD

## Updates

### Update CodeReview Service

```bash
# Rebuild and restart
docker compose build codereview
docker compose up -d codereview
```

### Update Ollama

```bash
docker compose pull ollama
docker compose up -d ollama
```

### Update Model

```bash
# Pull newer version
docker exec codereview-ollama ollama pull deepseek-coder:33b

# Remove old model (optional)
docker exec codereview-ollama ollama rm deepseek-coder:old-version
```

## Complete Removal

```bash
# Stop and remove all containers, volumes, and images
docker compose down -v
docker rmi dhadgar/codereview:latest
docker volume rm codereview_ollama_models codereview_data codereview_logs
```

## Additional Resources

- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/)
- [Ollama Docker Documentation](https://hub.docker.com/r/ollama/ollama)

# Dhadgar.CodeReview

GPU-Accelerated AI Code Review Service using DeepSeek Coder and your RTX 4090.

## Overview

This service uses a GitHub App to receive webhook events from pull requests and performs automated code reviews using a locally-running LLM (Ollama with DeepSeek Coder 33B). It runs on your desktop with GPU acceleration via Docker Compose and is completely independent from the main Dhadgar platform - it just lives in this repo for convenience.

## Features

- **🚀 GPU-Accelerated**: Leverages your RTX 4090 via Ollama for fast inference
- **🤖 Automated Reviews**: Triggers on PR updates or via `/review` comment command
- **💻 Code-Specific**: Optimized for C#/.NET code review with security and best practices focus
- **📦 Containerized**: Docker Compose deployment with all dependencies
- **🔒 Standalone**: No dependencies on other Dhadgar services (uses its own SQLite database)
- **📊 Review History**: Stores all reviews in persistent volumes

## Deployment Options

### 🐳 Docker Compose (Recommended)

**One-command deployment with PowerShell script:**

```powershell
cd src/Dhadgar.CodeReview
.\Deploy-CodeReview.ps1
```

See [DOCKER.md](DOCKER.md) for complete Docker deployment guide.

### 💻 Direct Installation

For development or testing without Docker:

```bash
dotnet run --project src/Dhadgar.CodeReview
```

See [SETUP.md](SETUP.md) for manual setup instructions.

## Quick Start (Docker - Recommended)

### Prerequisites

1. **Docker Desktop** with WSL2 backend
2. **NVIDIA Driver** for RTX 4090 (version 527.41+)
3. **NVIDIA Container Toolkit** in WSL2
4. **GitHub App** (we'll create this)

### 1. Create GitHub App

1. Go to https://github.com/settings/apps/new
2. Configure basic settings (see [SETUP.md](SETUP.md) for detailed instructions)
3. Download the private key `.pem` file
4. Note your **App ID** and **Installation ID**

### 2. Configure Secrets

```powershell
cd src/Dhadgar.CodeReview

# Create secrets directory
mkdir secrets

# Copy your GitHub App private key
cp ~/Downloads/your-app.private-key.pem secrets/github-app.pem

# Create .env file from template
cp .env.example .env

# Edit .env and add your GitHub App credentials:
# - GITHUB_APP_ID
# - GITHUB_INSTALLATION_ID
# - GITHUB_WEBHOOK_SECRET
```

### 3. Deploy

```powershell
.\Deploy-CodeReview.ps1
```

This automated script will:
- ✅ Check all prerequisites
- ✅ Build the Docker images
- ✅ Start Ollama with GPU support
- ✅ Download the LLM model (~20GB)
- ✅ Start the CodeReview service
- ✅ Run health checks

### 4. Expose Webhook

```powershell
# In a new terminal
ngrok http 8080

# Update GitHub App webhook URL to: https://YOUR-URL.ngrok.io/webhook
```

### 5. Test It!

Comment `/review` on any pull request where the app is installed.

---

## Manual Setup (Non-Docker)

If you prefer to run without Docker, see [SETUP.md](SETUP.md) for complete manual installation instructions.

## Prerequisites (Manual Setup)

1. **Windows 11** (23H2+) or **Windows 10** (21H2+)
2. **NVIDIA Driver** for RTX 4090 (version 527.41+)
3. **WSL2** enabled
4. **.NET 10 SDK**
5. **Ollama** installed directly (not in Docker)

## Setup

### 1. Install Ollama

```bash
# Option A: Direct installation (recommended)
curl https://ollama.ai/install.sh | sh

# Option B: Windows installer
# Download from https://ollama.com/download

# Pull the model
ollama pull deepseek-coder:33b

# Verify GPU access
nvidia-smi
```

### 2. Create GitHub App

1. Go to https://github.com/settings/apps/new
2. Configure:
   - **Name**: `MeridianConsole-AI-Reviewer` (or your choice)
   - **Webhook URL**: `https://your-ngrok-url.com/webhook` (use ngrok for testing)
   - **Webhook Secret**: Generate a strong secret
   - **Permissions**:
     - Pull requests: Read & Write
     - Contents: Read-only
     - Metadata: Read-only
   - **Events**: Pull request, Pull request review comment
3. Generate Private Key and download the `.pem` file
4. Install the app on your repository
5. Note the App ID and Installation ID

### 3. Configure Service

```bash
# Navigate to service directory
cd src/Dhadgar.CodeReview

# Create secrets directory
mkdir secrets

# Copy GitHub App private key
cp /path/to/github-app.pem secrets/github-app.pem

# Configure user secrets
dotnet user-secrets init
dotnet user-secrets set "GitHub:AppId" "YOUR_APP_ID"
dotnet user-secrets set "GitHub:InstallationId" "YOUR_INSTALLATION_ID"
dotnet user-secrets set "GitHub:WebhookSecret" "YOUR_WEBHOOK_SECRET"
```

### 4. Run Service

```bash
# Restore packages
dotnet restore

# Apply database migrations
dotnet ef database update

# Run service
dotnet run
```

The service will start on `http://localhost:5000` (or configured port).

### 5. Expose Webhook with ngrok

```bash
# In another terminal
ngrok http 5000

# Update GitHub App webhook URL to the ngrok URL
```

## Usage

### Manual Trigger

Comment `/review` on any pull request to trigger a code review.

### Automatic Trigger

Set `Review:EnableAutoReview` to `true` in `appsettings.json` to automatically review PRs when new commits are pushed.

## Configuration

Edit `appsettings.json`:

```json
{
  "GitHub": {
    "AppId": "",  // Set via user secrets
    "InstallationId": "",  // Set via user secrets
    "PrivateKeyPath": "./secrets/github-app.pem",
    "WebhookSecret": ""  // Set via user secrets
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "deepseek-coder:33b",
    "TimeoutSeconds": 300
  },
  "Review": {
    "MaxDiffSize": 50000,
    "MaxFilesPerReview": 20,
    "EnableAutoReview": false  // Set to true for automatic reviews
  }
}
```

## Running as Windows Service (Optional)

For 24/7 operation:

```bash
# Publish as self-contained
dotnet publish -c Release -r win-x64 --self-contained

# Install using NSSM or sc.exe
sc create DhadgarCodeReview binPath="C:\path\to\Dhadgar.CodeReview.exe"
```

## Troubleshooting

### GPU Not Detected

```bash
# Check NVIDIA driver
nvidia-smi

# Check Ollama GPU access
ollama run deepseek-coder:33b "test"
```

### Model Not Found

```bash
# List installed models
ollama list

# Pull model if missing
ollama pull deepseek-coder:33b
```

### Webhook Not Receiving Events

- Check ngrok is running and URL is correct
- Verify webhook secret matches in GitHub App settings
- Check service logs for errors

## Architecture

This service is **completely independent** from the main Dhadgar platform:

- **No shared database** - Uses its own SQLite database
- **No shared messaging** - No RabbitMQ/Redis dependencies
- **No Kubernetes** - Runs directly on your desktop
- **Repo convenience** - Lives in this repo for organization only

## Performance

With RTX 4090 and DeepSeek Coder 33B:
- **First review**: ~30-60 seconds (model loading)
- **Subsequent reviews**: ~10-30 seconds (model cached in VRAM)
- **VRAM usage**: ~20-22GB

## License

Part of the Meridian Console / Dhadgar project.

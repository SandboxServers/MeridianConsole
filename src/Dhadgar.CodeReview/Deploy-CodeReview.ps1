#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deploy Dhadgar.CodeReview service to local Docker engine with GPU support.

.DESCRIPTION
    This script builds and deploys the CodeReview service along with Ollama (GPU-enabled)
    to your local Docker engine. It handles the full deployment pipeline:
    - Pre-flight checks (Docker, NVIDIA, secrets)
    - Building the CodeReview container image
    - Pulling the Ollama image
    - Starting the container group
    - Loading the LLM model into Ollama
    - Health checks

.PARAMETER SkipBuild
    Skip building the Docker image (use existing image)

.PARAMETER SkipModelPull
    Skip pulling the LLM model (assume it's already downloaded)

.PARAMETER Clean
    Remove existing containers and volumes before deploying

.PARAMETER Model
    LLM model to use (default: deepseek-coder:33b)

.EXAMPLE
    .\Deploy-CodeReview.ps1
    Full deployment with all steps

.EXAMPLE
    .\Deploy-CodeReview.ps1 -SkipBuild
    Deploy without rebuilding the image

.EXAMPLE
    .\Deploy-CodeReview.ps1 -Clean
    Clean deployment (removes existing containers/volumes)
#>

[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$SkipModelPull,
    [switch]$Clean,
    [string]$Model = "deepseek-coder:33b"
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot

# Colors for output
function Write-Step {
    param([string]$Message)
    Write-Host "`n🔹 $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✅ $Message" -ForegroundColor Green
}

function Write-Failure {
    param([string]$Message)
    Write-Host "❌ $Message" -ForegroundColor Red
}

function Write-Warning {
    param([string]$Message)
    Write-Host "⚠️  $Message" -ForegroundColor Yellow
}

# Banner
Write-Host @"

╔═══════════════════════════════════════════════════════════╗
║                                                           ║
║     Dhadgar.CodeReview - Docker Deployment Script        ║
║     GPU-Accelerated AI Code Reviews                      ║
║                                                           ║
╚═══════════════════════════════════════════════════════════╝

"@ -ForegroundColor Magenta

# ============================================================================
# Pre-flight Checks
# ============================================================================

Write-Step "Running pre-flight checks..."

# Check Docker
try {
    $dockerVersion = docker version --format '{{.Server.Version}}' 2>$null
    if (-not $dockerVersion) {
        throw "Docker is not running"
    }
    Write-Success "Docker is running (version: $dockerVersion)"
} catch {
    Write-Failure "Docker is not installed or not running"
    Write-Host "Please install Docker Desktop and ensure it's running."
    exit 1
}

# Check Docker Compose
try {
    $composeVersion = docker compose version --short 2>$null
    if (-not $composeVersion) {
        throw "Docker Compose not found"
    }
    Write-Success "Docker Compose is available (version: $composeVersion)"
} catch {
    Write-Failure "Docker Compose is not available"
    Write-Host "Please install Docker Desktop which includes Docker Compose."
    exit 1
}

# Check NVIDIA GPU (optional but recommended)
try {
    $gpuCheck = docker run --rm --gpus all nvidia/cuda:12.9.0-base-ubuntu22.04 nvidia-smi 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Success "NVIDIA GPU access is working"
    } else {
        Write-Warning "Could not verify GPU access - reviews may run on CPU"
    }
} catch {
    Write-Warning "GPU test failed - make sure NVIDIA Container Toolkit is installed in WSL2"
}

# Check for secrets
$secretsDir = Join-Path $ScriptDir "secrets"
$pemFile = Join-Path $secretsDir "github-app.pem"

if (-not (Test-Path $secretsDir)) {
    Write-Warning "Secrets directory not found - creating it"
    New-Item -ItemType Directory -Path $secretsDir -Force | Out-Null
}

if (-not (Test-Path $pemFile)) {
    Write-Failure "GitHub App private key not found at: $pemFile"
    Write-Host @"

Please create the secrets directory and add your GitHub App private key:

    1. Create directory: mkdir secrets
    2. Copy your .pem file: cp path/to/your-app.private-key.pem secrets/github-app.pem

"@
    exit 1
} else {
    Write-Success "GitHub App private key found"
}

# Check for .env file
$envFile = Join-Path $ScriptDir ".env"
$envExample = Join-Path $ScriptDir ".env.example"

if (-not (Test-Path $envFile)) {
    Write-Warning ".env file not found"
    Write-Host @"

Please create a .env file with your GitHub App credentials:

    1. Copy the example: cp .env.example .env
    2. Edit .env and fill in your values:
       - GITHUB_APP_ID
       - GITHUB_INSTALLATION_ID
       - GITHUB_WEBHOOK_SECRET

"@

    if (Test-Path $envExample) {
        Copy-Item $envExample $envFile
        Write-Host "Created .env from .env.example - please edit it with your values."
    }
    exit 1
} else {
    Write-Success ".env file found"
}

# ============================================================================
# Clean up (if requested)
# ============================================================================

if ($Clean) {
    Write-Step "Cleaning up existing deployment..."

    try {
        docker compose -f (Join-Path $ScriptDir "docker-compose.yml") down -v
        Write-Success "Removed existing containers and volumes"
    } catch {
        Write-Warning "No existing deployment to clean up"
    }
}

# ============================================================================
# Build Docker Image
# ============================================================================

if (-not $SkipBuild) {
    Write-Step "Building CodeReview Docker image..."

    $buildContext = Resolve-Path (Join-Path $ScriptDir ".." "..")
    $dockerfile = Join-Path $ScriptDir "Dockerfile"

    Write-Host "Build context: $buildContext" -ForegroundColor Gray
    Write-Host "Dockerfile: $dockerfile" -ForegroundColor Gray

    docker build `
        -f $dockerfile `
        -t dhadgar/codereview:latest `
        $buildContext

    if ($LASTEXITCODE -ne 0) {
        Write-Failure "Docker build failed"
        exit 1
    }

    Write-Success "Docker image built successfully"
} else {
    Write-Step "Skipping Docker build (using existing image)"
}

# ============================================================================
# Start Services
# ============================================================================

Write-Step "Starting services with Docker Compose..."

$composeFile = Join-Path $ScriptDir "docker-compose.yml"

docker compose -f $composeFile up -d

if ($LASTEXITCODE -ne 0) {
    Write-Failure "Failed to start services"
    exit 1
}

Write-Success "Services started"

# Wait for Ollama to be healthy
Write-Step "Waiting for Ollama service to be ready..."

$maxWait = 60
$waited = 0

while ($waited -lt $maxWait) {
    try {
        $health = docker inspect codereview-ollama --format='{{.State.Health.Status}}' 2>$null
        if ($health -eq "healthy") {
            Write-Success "Ollama service is healthy"
            break
        }
    } catch {
        # Ignore errors
    }

    Start-Sleep -Seconds 2
    $waited += 2
    Write-Host "." -NoNewline -ForegroundColor Gray
}

if ($waited -ge $maxWait) {
    Write-Warning "Ollama service did not become healthy within $maxWait seconds"
}

# ============================================================================
# Pull LLM Model
# ============================================================================

if (-not $SkipModelPull) {
    Write-Step "Pulling LLM model: $Model (this may take a while for first-time download)..."

    # Check if model already exists
    $modelList = docker exec codereview-ollama ollama list 2>$null
    if ($modelList -match $Model) {
        Write-Success "Model $Model already downloaded"
    } else {
        Write-Host "Downloading model (this is a large file, ~20GB)..." -ForegroundColor Yellow
        docker exec codereview-ollama ollama pull $Model

        if ($LASTEXITCODE -ne 0) {
            Write-Failure "Failed to pull model"
            Write-Host "You can pull it manually later with: docker exec codereview-ollama ollama pull $Model"
        } else {
            Write-Success "Model downloaded successfully"
        }
    }
} else {
    Write-Step "Skipping model pull (assuming model already exists)"
}

# ============================================================================
# Health Checks
# ============================================================================

Write-Step "Running health checks..."

# Check Ollama
try {
    $ollamaHealth = docker exec codereview-ollama curl -s http://localhost:11434/api/tags
    if ($ollamaHealth) {
        Write-Success "Ollama API is responding"
    }
} catch {
    Write-Warning "Could not verify Ollama health"
}

# Check CodeReview service (give it time to start)
Write-Host "Waiting for CodeReview service to start..." -ForegroundColor Gray
Start-Sleep -Seconds 5

try {
    $serviceHealth = Invoke-RestMethod -Uri "http://localhost:8080/healthz" -ErrorAction SilentlyContinue
    if ($serviceHealth.status -eq "ok") {
        Write-Success "CodeReview service is responding"
    }
} catch {
    Write-Warning "CodeReview service is starting up (may take a moment)"
}

# ============================================================================
# Summary
# ============================================================================

Write-Host @"

╔═══════════════════════════════════════════════════════════╗
║                                                           ║
║              Deployment Complete!                         ║
║                                                           ║
╚═══════════════════════════════════════════════════════════╝

"@ -ForegroundColor Green

Write-Host @"
Services Running:
  • Ollama (GPU):      http://localhost:11434
  • CodeReview API:    http://localhost:8080
  • Swagger UI:        http://localhost:8080/swagger

Next Steps:
  1. Expose webhook with ngrok:
     ngrok http 8080

  2. Update GitHub App webhook URL to:
     https://YOUR-NGROK-URL.ngrok.io/webhook

  3. Test with a pull request:
     Comment "/review" on any PR

Useful Commands:
  • View logs:           docker compose -f src/Dhadgar.CodeReview/docker-compose.yml logs -f
  • Stop services:       docker compose -f src/Dhadgar.CodeReview/docker-compose.yml down
  • Restart:             docker compose -f src/Dhadgar.CodeReview/docker-compose.yml restart
  • Check GPU usage:     docker exec codereview-ollama nvidia-smi

"@ -ForegroundColor Cyan

Write-Host "🚀 Ready for AI-powered code reviews!" -ForegroundColor Magenta

#Requires -Version 7.0
<#
.SYNOPSIS
    Deploys 10 Azure DevOps agents to a dedicated Talos worker node.
    No container registry required - builds locally and imports directly to node.

.PARAMETER WorkerNode
    The name of the Talos worker node to deploy agents to.

.PARAMETER WorkerNodeIP
    The IP address of the Talos worker node (for talosctl commands).

.PARAMETER AzpToken
    The Azure DevOps PAT token. Can also be set via AZP_TOKEN environment variable.

.PARAMETER ImageTag
    The Docker image tag to use. Defaults to 'latest'.

.PARAMETER SkipBuild
    Skip building the Docker image (use existing image on node).

.EXAMPLE
    .\deploy.ps1 -WorkerNodeIP 192.168.1.251 -AzpToken "your-pat-token"

.EXAMPLE
    $env:AZP_TOKEN = "your-pat-token"
    .\deploy.ps1 -WorkerNodeIP 192.168.1.251

.EXAMPLE
    # Skip build if image already imported
    .\deploy.ps1 -WorkerNodeIP 192.168.1.251 -SkipBuild
#>
param(
    [string]$WorkerNode = "talos-xmo-fex",
    [Parameter(Mandatory = $true)]
    [string]$WorkerNodeIP,
    [string]$AzpToken = $env:AZP_TOKEN,
    [string]$ImageTag = "latest",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Get-Item "$ScriptDir/../../..").FullName

# Configuration
$ImageName = "dhadgar-azdo-agent"
$FullImageName = "localhost/${ImageName}:${ImageTag}"
$Namespace = "ado-agents"
$TarFile = Join-Path $env:TEMP "dhadgar-azdo-agent.tar"

Write-Host "=== ADO Agent Kubernetes Deployment (No Registry) ===" -ForegroundColor Cyan
Write-Host "Image: $FullImageName"
Write-Host "Worker Node: $WorkerNode ($WorkerNodeIP)"
Write-Host "Tar File: $TarFile"
Write-Host ""

# Verify prerequisites
Write-Host "=== Verifying prerequisites ===" -ForegroundColor Cyan

# Check kubectl
$kubectlExists = Get-Command kubectl -ErrorAction SilentlyContinue
if (-not $kubectlExists) {
    Write-Host "ERROR: kubectl not found. Install it first." -ForegroundColor Red
    exit 1
}
Write-Host "[OK] kubectl found"

# Check talosctl
$talosctlExists = Get-Command talosctl -ErrorAction SilentlyContinue
if (-not $talosctlExists) {
    Write-Host "ERROR: talosctl not found. Install it first." -ForegroundColor Red
    Write-Host "  Download from: https://github.com/siderolabs/talos/releases"
    exit 1
}
Write-Host "[OK] talosctl found"

if (-not $SkipBuild) {
    # Check docker
    $dockerExists = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $dockerExists) {
        Write-Host "ERROR: docker not found. Install it first." -ForegroundColor Red
        Write-Host "  On Windows Server 2022: .\scripts\Install-DockerServer2022.ps1"
        exit 1
    }
    Write-Host "[OK] docker found"
}
Write-Host ""

# Step 1: Build image locally (no registry needed)
if (-not $SkipBuild) {
    Write-Host "=== Step 1: Building Docker image locally ===" -ForegroundColor Cyan

    docker build `
        -t $FullImageName `
        -f "$RepoRoot/deploy/azure-pipelines/agent.Dockerfile" `
        $RepoRoot

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Docker build failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "Image built: $FullImageName"
    Write-Host ""

    # Step 2: Export image to tar
    Write-Host "=== Step 2: Exporting image to tar file ===" -ForegroundColor Cyan

    if (Test-Path $TarFile) {
        Remove-Item $TarFile -Force
    }

    docker save -o $TarFile $FullImageName

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Docker save failed" -ForegroundColor Red
        exit 1
    }

    $tarSize = [math]::Round((Get-Item $TarFile).Length / 1MB, 2)
    Write-Host "Image exported: $TarFile ($tarSize MB)"
    Write-Host ""

    # Step 3: Import image to Talos node
    Write-Host "=== Step 3: Importing image to Talos node ===" -ForegroundColor Cyan
    Write-Host "Copying image to node $WorkerNodeIP (this may take a while)..."

    # Use talosctl to copy and import the image
    # First, we need to copy the tar to the node, then import it
    # talosctl can pipe directly: cat file | talosctl -n node image import -

    Get-Content $TarFile -Raw -AsByteStream | talosctl -n $WorkerNodeIP image import -

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Failed to import image to Talos node" -ForegroundColor Red
        Write-Host ""
        Write-Host "Alternative: Copy manually and import" -ForegroundColor Yellow
        Write-Host "  scp $TarFile root@${WorkerNodeIP}:/tmp/"
        Write-Host "  talosctl -n $WorkerNodeIP image import /tmp/dhadgar-azdo-agent.tar"
        exit 1
    }

    Write-Host "Image imported to node successfully"
    Write-Host ""

    # Cleanup tar file
    Remove-Item $TarFile -Force
    Write-Host "Cleaned up temporary tar file"
    Write-Host ""
}
else {
    Write-Host "=== Skipping build (using existing image on node) ===" -ForegroundColor Yellow
    Write-Host ""
}

# Step 4: Configure worker node
Write-Host "=== Step 4: Configuring worker node $WorkerNode ===" -ForegroundColor Cyan

# Check if node exists
$nodeExists = kubectl get node $WorkerNode 2>$null
if (-not $nodeExists) {
    Write-Host "WARNING: Node '$WorkerNode' not found in cluster." -ForegroundColor Yellow
    Write-Host "Make sure you've applied the worker.yaml config to the Talos VM." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To add the worker node:"
    Write-Host "  talosctl apply-config --insecure --nodes $WorkerNodeIP --file worker.yaml"
    Write-Host ""
    $continue = Read-Host "Continue anyway? (y/N)"
    if ($continue -ne "y") { exit 1 }
}
else {
    # Label and taint the node
    kubectl label node $WorkerNode workload=ado-agents --overwrite 2>$null
    kubectl taint nodes $WorkerNode dedicated=ado-agents:NoSchedule --overwrite 2>$null
    Write-Host "Node labeled and tainted for dedicated ADO agent workloads"
}
Write-Host ""

# Step 5: Create namespace and secret
Write-Host "=== Step 5: Creating namespace and secret ===" -ForegroundColor Cyan
kubectl apply -f "$ScriptDir/namespace.yaml"

# Check if secret exists
$secretExists = kubectl get secret azdo-agent-credentials -n $Namespace 2>$null
if (-not $secretExists) {
    if ([string]::IsNullOrEmpty($AzpToken)) {
        Write-Host "ERROR: AZP_TOKEN is required for initial deployment." -ForegroundColor Red
        Write-Host "Set it via: `$env:AZP_TOKEN = 'your-pat-token'"
        Write-Host "Or pass it as parameter: -AzpToken 'your-pat-token'"
        exit 1
    }
    kubectl create secret generic azdo-agent-credentials `
        --namespace $Namespace `
        --from-literal=AZP_TOKEN=$AzpToken
    Write-Host "Secret created"
}
else {
    Write-Host "Secret already exists, skipping creation"
}
Write-Host ""

# Step 6: Deploy agents
Write-Host "=== Step 6: Deploying ADO agents ===" -ForegroundColor Cyan
kubectl apply -f "$ScriptDir/deployment.yaml"
Write-Host ""

# Step 7: Wait for rollout
Write-Host "=== Step 7: Waiting for deployment rollout ===" -ForegroundColor Cyan
kubectl rollout status deployment/azdo-agent -n $Namespace --timeout=300s
Write-Host ""

# Step 8: Show status
Write-Host "=== Deployment Complete ===" -ForegroundColor Green
kubectl get pods -n $Namespace -o wide
Write-Host ""
Write-Host "All 10 ADO agents should now be registering with Azure DevOps."
Write-Host "Check agent pool at: https://dev.azure.com/SandboxServers/_settings/agentpools"

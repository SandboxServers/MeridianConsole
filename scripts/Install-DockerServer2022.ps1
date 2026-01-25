#Requires -RunAsAdministrator
#Requires -Version 5.1
<#
.SYNOPSIS
    Installs Docker Engine on Windows Server 2022.

.DESCRIPTION
    This script installs Docker Engine directly from Microsoft's official packages,
    bypassing the unreliable DockerMsftProvider PowerShell module.

.PARAMETER SkipReboot
    Skip automatic reboot after installation (you'll need to reboot manually).

.EXAMPLE
    .\Install-DockerServer2022.ps1

.EXAMPLE
    .\Install-DockerServer2022.ps1 -SkipReboot

.NOTES
    Requires administrator privileges.
    A reboot is required after installation.
#>
param(
    [switch]$SkipReboot
)

$ErrorActionPreference = "Stop"

Write-Host "=== Docker Engine Installation for Windows Server 2022 ===" -ForegroundColor Cyan
Write-Host ""

# Check if already installed
$dockerInstalled = Get-Command docker -ErrorAction SilentlyContinue
if ($dockerInstalled) {
    Write-Host "Docker is already installed:" -ForegroundColor Green
    docker version
    exit 0
}

# Step 1: Enable Containers feature
Write-Host "=== Step 1: Enabling Containers Windows feature ===" -ForegroundColor Cyan
$containersFeature = Get-WindowsFeature -Name Containers
if ($containersFeature.Installed) {
    Write-Host "Containers feature is already enabled"
}
else {
    Write-Host "Installing Containers feature..."
    Install-WindowsFeature -Name Containers
    Write-Host "Containers feature installed"
}
Write-Host ""

# Step 2: Download Docker directly
Write-Host "=== Step 2: Downloading Docker Engine ===" -ForegroundColor Cyan

$dockerVersion = "27.5.1"
$dockerZip = "$env:TEMP\docker-$dockerVersion.zip"
$dockerUrl = "https://download.docker.com/win/static/stable/x86_64/docker-$dockerVersion.zip"

Write-Host "Downloading Docker $dockerVersion..."
Write-Host "URL: $dockerUrl"

# Use TLS 1.2
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

try {
    Invoke-WebRequest -Uri $dockerUrl -OutFile $dockerZip -UseBasicParsing
}
catch {
    Write-Host "ERROR: Failed to download Docker" -ForegroundColor Red
    Write-Host $_.Exception.Message
    exit 1
}

Write-Host "Download complete: $dockerZip"
Write-Host ""

# Step 3: Extract Docker
Write-Host "=== Step 3: Extracting Docker ===" -ForegroundColor Cyan

$dockerPath = "$env:ProgramFiles\Docker"
if (Test-Path $dockerPath) {
    Write-Host "Removing existing Docker installation..."
    Remove-Item -Recurse -Force $dockerPath
}

Write-Host "Extracting to $dockerPath..."
Expand-Archive -Path $dockerZip -DestinationPath $env:ProgramFiles -Force

# Cleanup zip
Remove-Item $dockerZip -Force
Write-Host "Extraction complete"
Write-Host ""

# Step 4: Add to PATH
Write-Host "=== Step 4: Adding Docker to PATH ===" -ForegroundColor Cyan

$machinePath = [Environment]::GetEnvironmentVariable("Path", [EnvironmentVariableTarget]::Machine)
if ($machinePath -notlike "*$dockerPath*") {
    [Environment]::SetEnvironmentVariable("Path", "$machinePath;$dockerPath", [EnvironmentVariableTarget]::Machine)
    Write-Host "Added $dockerPath to system PATH"
}
else {
    Write-Host "Docker already in PATH"
}

# Update current session PATH
$env:Path = "$env:Path;$dockerPath"
Write-Host ""

# Step 5: Register Docker service
Write-Host "=== Step 5: Registering Docker service ===" -ForegroundColor Cyan

& "$dockerPath\dockerd.exe" --register-service

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to register Docker service" -ForegroundColor Red
    exit 1
}

Write-Host "Docker service registered"
Write-Host ""

# Step 6: Configure Docker service
Write-Host "=== Step 6: Configuring Docker service ===" -ForegroundColor Cyan
Set-Service -Name docker -StartupType Automatic
Write-Host "Docker service set to start automatically"
Write-Host ""

# Step 7: Start Docker service (may fail before reboot, that's OK)
Write-Host "=== Step 7: Starting Docker service ===" -ForegroundColor Cyan
try {
    Start-Service docker
    Write-Host "Docker service started"

    # Verify
    Write-Host ""
    Write-Host "=== Verifying Installation ===" -ForegroundColor Cyan
    & "$dockerPath\docker.exe" version
}
catch {
    Write-Host "Docker service could not start (this is normal before reboot)" -ForegroundColor Yellow
    Write-Host "The service will start automatically after reboot."
}
Write-Host ""

# Step 8: Reboot
Write-Host "=== Installation Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Docker $dockerVersion has been installed."

if ($SkipReboot) {
    Write-Host ""
    Write-Host "Reboot skipped. After reboot, verify with:" -ForegroundColor Yellow
    Write-Host "  docker version"
    Write-Host "  docker run hello-world:nanoserver"
}
else {
    Write-Host ""
    $response = Read-Host "Reboot now? (Y/n)"
    if ($response -ne "n") {
        Write-Host "Rebooting in 10 seconds... Press Ctrl+C to cancel."
        Start-Sleep -Seconds 10
        Restart-Computer -Force
    }
    else {
        Write-Host ""
        Write-Host "Remember to reboot before using Docker." -ForegroundColor Yellow
        Write-Host "After reboot, verify with:"
        Write-Host "  docker version"
        Write-Host "  docker run hello-world:nanoserver"
    }
}

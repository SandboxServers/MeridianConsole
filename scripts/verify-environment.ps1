#Requires -Version 5.1
<#
.SYNOPSIS
    Verifies the MeridianConsole development environment is properly configured.

.DESCRIPTION
    Checks all required tools, services, and configurations for local development.
    Outputs a color-coded status report with fix suggestions for any issues.

.EXAMPLE
    .\verify-environment.ps1

.EXAMPLE
    .\verify-environment.ps1 -Detailed
#>

[CmdletBinding()]
param(
    [switch]$Detailed
)

$ErrorActionPreference = "Continue"

# ============================================================================
# Helper Functions
# ============================================================================

function Write-Status {
    param(
        [string]$Component,
        [string]$Status,
        [string]$Message,
        [string]$Fix = ""
    )

    $symbol = switch ($Status) {
        "OK"      { "[OK]"; $color = "Green" }
        "WARN"    { "[!!]"; $color = "Yellow" }
        "FAIL"    { "[X]"; $color = "Red" }
        "INFO"    { "[i]"; $color = "Cyan" }
        default   { "[?]"; $color = "Gray" }
    }

    Write-Host "$symbol " -ForegroundColor $color -NoNewline
    Write-Host "$Component" -NoNewline
    if ($Message) {
        Write-Host " - $Message" -ForegroundColor Gray
    } else {
        Write-Host ""
    }

    if ($Fix -and $Status -eq "FAIL") {
        Write-Host "    Fix: $Fix" -ForegroundColor Yellow
    }
}

function Test-Command {
    param([string]$Command)
    $null = Get-Command $Command -ErrorAction SilentlyContinue
    return $?
}

function Get-VersionOrNull {
    param([string]$Command, [string]$VersionArg = "--version")
    try {
        $output = & $Command $VersionArg 2>&1 | Select-Object -First 1
        return $output
    } catch {
        return $null
    }
}

# ============================================================================
# Main Verification
# ============================================================================

$results = @{
    Passed = 0
    Warnings = 0
    Failed = 0
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  MeridianConsole Development Environment Verification" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# ----------------------------------------------------------------------------
# .NET SDK
# ----------------------------------------------------------------------------
Write-Host "--- .NET SDK ---" -ForegroundColor White

if (Test-Command "dotnet") {
    $dotnetVersion = (dotnet --version 2>$null)
    $requiredVersion = "10.0"

    if ($dotnetVersion -like "$requiredVersion*") {
        Write-Status ".NET SDK" "OK" "Version $dotnetVersion"
        $results.Passed++
    } else {
        Write-Status ".NET SDK" "WARN" "Version $dotnetVersion (expected $requiredVersion.x)" `
            -Fix "Install .NET 10 SDK: winget install Microsoft.DotNet.SDK.10"
        $results.Warnings++
    }

    # Check EF Core tools
    $efVersion = dotnet ef --version 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Status "EF Core Tools" "OK" "Available"
        $results.Passed++
    } else {
        Write-Status "EF Core Tools" "WARN" "Not installed globally (project-local is fine)" `
            -Fix "dotnet tool install --global dotnet-ef"
        $results.Warnings++
    }
} else {
    Write-Status ".NET SDK" "FAIL" "Not installed" `
        -Fix "winget install Microsoft.DotNet.SDK.10"
    $results.Failed++
}

Write-Host ""

# ----------------------------------------------------------------------------
# Docker
# ----------------------------------------------------------------------------
Write-Host "--- Docker ---" -ForegroundColor White

if (Test-Command "docker") {
    $dockerVersion = Get-VersionOrNull "docker" "--version"
    Write-Status "Docker CLI" "OK" $dockerVersion
    $results.Passed++

    # Check if Docker daemon is running
    $dockerInfo = docker info 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Status "Docker Daemon" "OK" "Running"
        $results.Passed++
    } else {
        Write-Status "Docker Daemon" "FAIL" "Not running" `
            -Fix "Start Docker Desktop from the Start menu"
        $results.Failed++
    }
} else {
    Write-Status "Docker" "FAIL" "Not installed" `
        -Fix "winget install Docker.DockerDesktop"
    $results.Failed++
}

# Docker Compose
if (Test-Command "docker") {
    $composeVersion = docker compose version 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Status "Docker Compose" "OK" ($composeVersion -replace "Docker Compose version ", "")
        $results.Passed++
    } else {
        Write-Status "Docker Compose" "FAIL" "Not available" `
            -Fix "Included with Docker Desktop - try reinstalling"
        $results.Failed++
    }
}

Write-Host ""

# ----------------------------------------------------------------------------
# Kubernetes Tools
# ----------------------------------------------------------------------------
Write-Host "--- Kubernetes ---" -ForegroundColor White

if (Test-Command "kubectl") {
    $kubectlVersion = kubectl version --client --short 2>$null
    if (-not $kubectlVersion) {
        $kubectlVersion = (kubectl version --client -o json 2>$null | ConvertFrom-Json).clientVersion.gitVersion
    }
    Write-Status "kubectl" "OK" $kubectlVersion
    $results.Passed++
} else {
    Write-Status "kubectl" "FAIL" "Not installed" `
        -Fix "winget install Kubernetes.kubectl"
    $results.Failed++
}

if (Test-Command "minikube") {
    $minikubeVersion = (minikube version --short 2>$null)
    Write-Status "minikube" "OK" $minikubeVersion
    $results.Passed++

    # Check minikube status
    $minikubeStatus = minikube status --format='{{.Host}}' 2>$null
    if ($minikubeStatus -eq "Running") {
        Write-Status "minikube Cluster" "OK" "Running"
        $results.Passed++
    } elseif ($minikubeStatus -eq "Stopped") {
        Write-Status "minikube Cluster" "WARN" "Stopped" `
            -Fix "minikube start"
        $results.Warnings++
    } else {
        Write-Status "minikube Cluster" "INFO" "Not initialized (optional)"
        # Not counting as failure - minikube is optional for basic dev
    }
} else {
    Write-Status "minikube" "WARN" "Not installed (optional for basic dev)" `
        -Fix "winget install Kubernetes.minikube"
    $results.Warnings++
}

Write-Host ""

# ----------------------------------------------------------------------------
# Git
# ----------------------------------------------------------------------------
Write-Host "--- Version Control ---" -ForegroundColor White

if (Test-Command "git") {
    $gitVersion = (git --version) -replace "git version ", ""
    Write-Status "Git" "OK" $gitVersion
    $results.Passed++
} else {
    Write-Status "Git" "FAIL" "Not installed" `
        -Fix "winget install Git.Git"
    $results.Failed++
}

Write-Host ""

# ----------------------------------------------------------------------------
# Local Infrastructure (Docker Compose services)
# ----------------------------------------------------------------------------
Write-Host "--- Local Infrastructure ---" -ForegroundColor White

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$composeFile = Join-Path $repoRoot "deploy\compose\docker-compose.dev.yml"

if (Test-Path $composeFile) {
    Write-Status "docker-compose.dev.yml" "OK" "Found"
    $results.Passed++

    # Check if containers are running
    if (Test-Command "docker") {
        $dockerInfo = docker info 2>&1
        if ($LASTEXITCODE -eq 0) {
            $containers = docker ps --format "{{.Names}}" 2>$null

            # Check PostgreSQL
            if ($containers -match "postgres") {
                Write-Status "PostgreSQL Container" "OK" "Running"
                $results.Passed++
            } else {
                Write-Status "PostgreSQL Container" "WARN" "Not running" `
                    -Fix "docker compose -f deploy/compose/docker-compose.dev.yml up -d"
                $results.Warnings++
            }

            # Check RabbitMQ
            if ($containers -match "rabbitmq") {
                Write-Status "RabbitMQ Container" "OK" "Running"
                $results.Passed++
            } else {
                Write-Status "RabbitMQ Container" "WARN" "Not running" `
                    -Fix "docker compose -f deploy/compose/docker-compose.dev.yml up -d"
                $results.Warnings++
            }

            # Check Redis
            if ($containers -match "redis") {
                Write-Status "Redis Container" "OK" "Running"
                $results.Passed++
            } else {
                Write-Status "Redis Container" "WARN" "Not running" `
                    -Fix "docker compose -f deploy/compose/docker-compose.dev.yml up -d"
                $results.Warnings++
            }
        }
    }
} else {
    Write-Status "docker-compose.dev.yml" "WARN" "Not found (not in repo directory?)"
    $results.Warnings++
}

Write-Host ""

# ----------------------------------------------------------------------------
# Project Build
# ----------------------------------------------------------------------------
Write-Host "--- Project ---" -ForegroundColor White

$solutionFile = Join-Path $repoRoot "Dhadgar.sln"
if (Test-Path $solutionFile) {
    Write-Status "Solution File" "OK" "Dhadgar.sln found"
    $results.Passed++

    # Check if restored
    $objFolder = Join-Path $repoRoot "src\Dhadgar.Gateway\obj"
    if (Test-Path $objFolder) {
        Write-Status "NuGet Restore" "OK" "Packages restored"
        $results.Passed++
    } else {
        Write-Status "NuGet Restore" "WARN" "Not restored" `
            -Fix "dotnet restore"
        $results.Warnings++
    }
} else {
    Write-Status "Solution File" "INFO" "Not in repository root"
}

Write-Host ""

# ----------------------------------------------------------------------------
# Network Connectivity (Service Ports)
# ----------------------------------------------------------------------------
if ($Detailed) {
    Write-Host "--- Network Connectivity ---" -ForegroundColor White

    $ports = @(
        @{Name="PostgreSQL"; Port=5432},
        @{Name="RabbitMQ AMQP"; Port=5672},
        @{Name="RabbitMQ Management"; Port=15672},
        @{Name="Redis"; Port=6379}
    )

    foreach ($service in $ports) {
        $connection = Test-NetConnection -ComputerName localhost -Port $service.Port -WarningAction SilentlyContinue
        if ($connection.TcpTestSucceeded) {
            Write-Status $service.Name "OK" "Port $($service.Port) accessible"
        } else {
            Write-Status $service.Name "WARN" "Port $($service.Port) not accessible"
        }
    }

    Write-Host ""
}

# ============================================================================
# Summary
# ============================================================================

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Summary" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

$totalChecks = $results.Passed + $results.Warnings + $results.Failed

Write-Host "  Passed:   " -NoNewline
Write-Host "$($results.Passed)" -ForegroundColor Green -NoNewline
Write-Host " / $totalChecks"

Write-Host "  Warnings: " -NoNewline
Write-Host "$($results.Warnings)" -ForegroundColor Yellow -NoNewline
Write-Host " / $totalChecks"

Write-Host "  Failed:   " -NoNewline
Write-Host "$($results.Failed)" -ForegroundColor Red -NoNewline
Write-Host " / $totalChecks"

Write-Host ""

if ($results.Failed -eq 0 -and $results.Warnings -eq 0) {
    Write-Host "Your development environment is fully configured!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Quick start commands:" -ForegroundColor Cyan
    Write-Host "  dotnet build                    # Build the solution"
    Write-Host "  dotnet test                     # Run tests"
    Write-Host "  dotnet run --project src/Dhadgar.Gateway  # Run Gateway"
    Write-Host ""
} elseif ($results.Failed -eq 0) {
    Write-Host "Your environment is mostly ready. Review warnings above." -ForegroundColor Yellow
    Write-Host ""
} else {
    Write-Host "Some required components are missing. Please fix the issues above." -ForegroundColor Red
    Write-Host "Run '.\scripts\bootstrap-dev.ps1' to install missing components." -ForegroundColor Yellow
    Write-Host ""
}

# Return exit code based on failures
exit $results.Failed

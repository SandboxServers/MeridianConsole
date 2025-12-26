# Development Environment Setup

This guide walks you through setting up a local development environment for MeridianConsole (Dhadgar).

## Overview

MeridianConsole is a .NET 10 microservices solution that requires:
- **.NET SDK 10.0** for building and running services
- **Docker** for local infrastructure (PostgreSQL, RabbitMQ, Redis)
- **minikube + kubectl** (optional) for Kubernetes development
- **VS Code** with recommended extensions for the best development experience

---

## Prerequisites

Before starting, ensure you have:
- A 64-bit operating system (Windows 10/11, Ubuntu 20.04+, or macOS 12+)
- At least 8GB RAM (16GB recommended)
- 20GB free disk space
- Administrator/sudo access for installing tools

---

## Quick Start

### Linux (Ubuntu/Debian)

```bash
# Install .NET SDK 10
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --version 10.0.100
echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
echo 'export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools' >> ~/.bashrc
source ~/.bashrc

# Install Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
newgrp docker

# Clone and start
git clone https://github.com/SandboxServers/MeridianConsole.git
cd MeridianConsole
docker compose -f deploy/compose/docker-compose.dev.yml up -d
dotnet restore && dotnet build
```

### Windows (PowerShell as Administrator)

```powershell
# Install .NET SDK 10 via winget
winget install Microsoft.DotNet.SDK.10

# Install Docker Desktop
winget install Docker.DockerDesktop

# Restart your terminal, then:
git clone https://github.com/SandboxServers/MeridianConsole.git
cd MeridianConsole
docker compose -f deploy/compose/docker-compose.dev.yml up -d
dotnet restore; dotnet build
```

---

## Manual Installation

### 1. .NET SDK 10

#### Windows
Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0) or use winget:
```powershell
winget install Microsoft.DotNet.SDK.10
```

#### Linux (Ubuntu/Debian)
```bash
# Using the official install script
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --version 10.0.100

# Add to PATH (add these to ~/.bashrc or ~/.zshrc)
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools
```

#### Verify Installation
```bash
dotnet --version
# Should output: 10.0.100
```

### 2. Docker

#### Windows
1. Download and install [Docker Desktop](https://www.docker.com/products/docker-desktop/)
2. During installation, enable WSL 2 backend (recommended)
3. Start Docker Desktop after installation
4. Verify with: `docker --version`

#### Linux (Ubuntu/Debian)
```bash
# Install Docker Engine
curl -fsSL https://get.docker.com | sh

# Add your user to the docker group (avoid sudo for docker commands)
sudo usermod -aG docker $USER

# Apply group changes (or log out and back in)
newgrp docker

# Enable Docker to start on boot
sudo systemctl enable docker

# Verify
docker --version
docker compose version
```

### 3. minikube + kubectl (Optional)

Only needed if you plan to work on Kubernetes deployments.

#### Windows
```powershell
winget install Kubernetes.minikube
winget install Kubernetes.kubectl
```

#### Linux
```bash
# kubectl
curl -LO "https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl"
chmod +x kubectl
sudo mv kubectl /usr/local/bin/

# minikube
curl -LO https://storage.googleapis.com/minikube/releases/latest/minikube-linux-amd64
chmod +x minikube-linux-amd64
sudo mv minikube-linux-amd64 /usr/local/bin/minikube

# Start minikube (uses Docker driver by default)
minikube start
```

### 4. VS Code Setup

1. Install [VS Code](https://code.visualstudio.com/)
2. Open the MeridianConsole folder in VS Code
3. When prompted, install the recommended extensions (or press `Ctrl+Shift+P` > "Extensions: Show Recommended Extensions")

#### Recommended Extensions

The repository includes `.vscode/extensions.json` with these recommendations:

| Extension | Purpose |
|-----------|---------|
| C# Dev Kit (`ms-dotnettools.csdevkit`) | Full C# IDE experience |
| C# (`ms-dotnettools.csharp`) | C# language support, IntelliSense |
| Docker (`ms-azuretools.vscode-docker`) | Docker file editing, container management |
| YAML (`redhat.vscode-yaml`) | YAML syntax and validation |
| REST Client (`humao.rest-client`) | Test APIs directly from VS Code |
| Kubernetes (`ms-kubernetes-tools.vscode-kubernetes-tools`) | K8s manifest editing and cluster management |
| GitLens (`eamodio.gitlens`) | Enhanced Git integration |

---

## Running the Development Environment

### 1. Start Local Infrastructure

The development stack includes PostgreSQL 16, RabbitMQ 3 with management UI, and Redis 7.

```bash
# From the repository root
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

Default credentials for all services: `dhadgar` / `dhadgar`

#### Check Infrastructure Status
```bash
docker compose -f deploy/compose/docker-compose.dev.yml ps
```

All services should show "running" status.

### 2. Build the Solution

```bash
dotnet restore
dotnet build
```

### 3. Run Services

#### Run a Single Service
```bash
# Run the Gateway
dotnet run --project src/Dhadgar.Gateway

# Run with hot reload (auto-restart on file changes)
dotnet watch --project src/Dhadgar.Gateway
```

#### Run Multiple Services

Open multiple terminals and run different services, or use VS Code's task runner:
- Press `Ctrl+Shift+B` to run the "build" task
- Press `Ctrl+Shift+P` > "Tasks: Run Task" > select a task

### 4. Access Development UIs

| Service | URL | Credentials |
|---------|-----|-------------|
| Gateway Swagger | http://localhost:5000/swagger | - |
| RabbitMQ Management | http://localhost:15672 | dhadgar / dhadgar |
| PostgreSQL | localhost:5432 | dhadgar / dhadgar |
| Redis | localhost:6379 | password: dhadgar |

---

## Verification

Run these commands to verify your setup is working:

### 1. Check Docker Services
```bash
docker compose -f deploy/compose/docker-compose.dev.yml ps
# All services should show "running"
```

### 2. Test PostgreSQL Connection
```bash
docker exec -it dhadgar-dev-postgres-1 psql -U dhadgar -d dhadgar_platform -c "SELECT 1;"
# Should return: 1
```

### 3. Test RabbitMQ
```bash
curl -u dhadgar:dhadgar http://localhost:15672/api/overview
# Should return JSON with RabbitMQ status
```

### 4. Build and Test Solution
```bash
dotnet build
dotnet test
# All tests should pass
```

### 5. Run a Service
```bash
dotnet run --project src/Dhadgar.Gateway &
curl http://localhost:5000/hello
# Should return: Hello from Dhadgar.Gateway!
```

---

## Troubleshooting

### Docker Not Starting

**Windows:**
- Ensure Docker Desktop is running (check system tray)
- If using WSL 2 backend, ensure WSL is installed: `wsl --install`
- Try restarting Docker Desktop

**Linux:**
```bash
# Check Docker service status
sudo systemctl status docker

# Start Docker if not running
sudo systemctl start docker

# Check for permission issues
docker ps
# If "permission denied", ensure your user is in docker group:
sudo usermod -aG docker $USER
# Then log out and back in, or run: newgrp docker
```

### minikube Issues

**"minikube start" fails:**
```bash
# Delete existing cluster and start fresh
minikube delete
minikube start --driver=docker

# If still failing, check Docker is running
docker ps
```

**kubectl can't connect to cluster:**
```bash
# Ensure minikube is running
minikube status

# If stopped, start it
minikube start

# Update kubeconfig
minikube update-context
```

### Port Conflicts

If you see "port already in use" errors:

```bash
# Find what's using the port (Linux/macOS)
lsof -i :5432
# or on Windows PowerShell:
netstat -ano | findstr :5432

# Kill the process or change the port in docker-compose.dev.yml
```

Common port conflicts:
- **5432** - Another PostgreSQL instance
- **5672/15672** - Another RabbitMQ instance
- **6379** - Another Redis instance
- **5000** - ASP.NET Core default port (change in service's `launchSettings.json`)

### WSL Issues (Windows)

**WSL 2 not installed:**
```powershell
# Install WSL 2 with Ubuntu
wsl --install

# Restart your computer after installation
```

**Docker Desktop not using WSL 2:**
1. Open Docker Desktop Settings
2. Go to General
3. Enable "Use the WSL 2 based engine"
4. Apply & Restart

**File permission issues in WSL:**
```bash
# If you cloned the repo in Windows and accessing from WSL,
# re-clone inside the WSL filesystem for better performance:
cd ~
git clone https://github.com/SandboxServers/MeridianConsole.git
```

### .NET SDK Not Found

**"dotnet: command not found":**
```bash
# Check if .NET is installed but not in PATH
ls ~/.dotnet/

# Add to PATH (add to ~/.bashrc or ~/.zshrc)
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools

# Reload shell
source ~/.bashrc
```

**Wrong SDK version:**
```bash
dotnet --list-sdks
# If 10.0.100 is not listed, install it using the instructions above
```

### Build Failures

**"The SDK 'Microsoft.NET.Sdk' could not be found":**
```bash
# Ensure you're in the repository root with global.json
cat global.json
# Should show sdk version 10.0.100

# Verify SDK is installed
dotnet --list-sdks | grep 10.0
```

**Package restore failures:**
```bash
# Clear NuGet cache and restore again
dotnet nuget locals all --clear
dotnet restore
```

---

## Platform-Specific Notes

### Windows

- **Recommended**: Use Docker Desktop with WSL 2 backend for best performance
- **Alternative**: Hyper-V backend works but is slower
- **Path lengths**: Enable long paths if you encounter issues:
  ```powershell
  # Run as Administrator
  New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name "LongPathsEnabled" -Value 1 -PropertyType DWORD -Force
  ```

### Linux (Ubuntu/Debian)

- Docker runs natively with best performance
- Use `newgrp docker` after adding yourself to the docker group (avoids logout/login)
- For production-like testing, consider using minikube with the Docker driver

### WSL 2 (Windows Subsystem for Linux)

- **Best of both worlds**: Native Linux development on Windows
- **Performance tip**: Clone repos inside WSL filesystem (`~/projects/`) not on Windows mount (`/mnt/c/`)
- **Docker integration**: Docker Desktop automatically integrates with WSL 2 distros
- **VS Code integration**: Use "Remote - WSL" extension to edit files in WSL

### macOS

- Install Docker Desktop for Mac
- Use Homebrew for .NET SDK: `brew install dotnet-sdk`
- Apple Silicon (M1/M2): All tools support ARM64 natively

---

## Next Steps

After completing setup:

1. Read the [CLAUDE.md](../CLAUDE.md) file for development conventions
2. Explore the `src/` directory to understand service structure
3. Run tests: `dotnet test`
4. Start a service and explore its Swagger UI
5. Check the `docs/architecture/` directory for system design documentation

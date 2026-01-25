#!/usr/bin/env bash
set -euo pipefail

# WSL2 Environment Setup for ADO Agent Deployment
# Installs: Docker, PowerShell, talosctl, kubectl

echo "=== WSL2 Environment Setup for ADO Agent Deployment ==="
echo ""

# Check we're in WSL
if [ ! -f /proc/sys/fs/binfmt_misc/WSLInterop ]; then
    echo "ERROR: This script must be run inside WSL2"
    exit 1
fi

echo "=== Step 1: Updating packages ==="
sudo apt-get update
sudo apt-get upgrade -y
echo ""

echo "=== Step 2: Installing Docker ==="
if command -v docker &>/dev/null; then
    echo "Docker already installed: $(docker --version)"
else
    curl -fsSL https://get.docker.com | sh
    sudo usermod -aG docker $USER
    echo "Docker installed. You'll need to log out and back in for group membership."
fi
echo ""

echo "=== Step 3: Installing PowerShell ==="
if command -v pwsh &>/dev/null; then
    echo "PowerShell already installed: $(pwsh --version)"
else
    # Install PowerShell via Microsoft repository
    sudo apt-get install -y wget apt-transport-https software-properties-common

    # Get Ubuntu version
    source /etc/os-release

    # Download and register Microsoft repository GPG keys
    wget -q "https://packages.microsoft.com/config/ubuntu/$VERSION_ID/packages-microsoft-prod.deb"
    sudo dpkg -i packages-microsoft-prod.deb
    rm packages-microsoft-prod.deb

    # Install PowerShell
    sudo apt-get update
    sudo apt-get install -y powershell

    echo "PowerShell installed: $(pwsh --version)"
fi
echo ""

echo "=== Step 4: Installing kubectl ==="
if command -v kubectl &>/dev/null; then
    echo "kubectl already installed: $(kubectl version --client --short 2>/dev/null || kubectl version --client)"
else
    curl -LO "https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl"
    sudo install -o root -g root -m 0755 kubectl /usr/local/bin/kubectl
    rm kubectl
    echo "kubectl installed: $(kubectl version --client --short 2>/dev/null || kubectl version --client)"
fi
echo ""

echo "=== Step 5: Installing talosctl ==="
if command -v talosctl &>/dev/null; then
    echo "talosctl already installed: $(talosctl version --client 2>/dev/null | head -2)"
else
    # Get latest talosctl
    TALOS_VERSION=$(curl -s https://api.github.com/repos/siderolabs/talos/releases/latest | grep '"tag_name"' | cut -d'"' -f4)
    curl -Lo /tmp/talosctl "https://github.com/siderolabs/talos/releases/download/${TALOS_VERSION}/talosctl-linux-amd64"
    sudo install -o root -g root -m 0755 /tmp/talosctl /usr/local/bin/talosctl
    rm /tmp/talosctl
    echo "talosctl installed: ${TALOS_VERSION}"
fi
echo ""

echo "=== Step 6: Setting up talosctl config ==="
WINDOWS_USER=$(cmd.exe /c "echo %USERNAME%" 2>/dev/null | tr -d '\r')
WINDOWS_TALOSCONFIG="/mnt/c/Users/${WINDOWS_USER}/.talos/config"

if [ -f "$WINDOWS_TALOSCONFIG" ]; then
    mkdir -p ~/.talos
    if [ ! -f ~/.talos/config ]; then
        cp "$WINDOWS_TALOSCONFIG" ~/.talos/config
        echo "Copied talosconfig from Windows: $WINDOWS_TALOSCONFIG"
    else
        echo "talosconfig already exists at ~/.talos/config"
    fi
else
    echo "WARNING: Windows talosconfig not found at $WINDOWS_TALOSCONFIG"
    echo "You'll need to copy it manually:"
    echo "  cp /mnt/c/Users/YourUser/.talos/config ~/.talos/config"
fi
echo ""

echo "=== Step 7: Setting up kubectl config ==="
WINDOWS_KUBECONFIG="/mnt/c/Users/${WINDOWS_USER}/.kube/config"

if [ -f "$WINDOWS_KUBECONFIG" ]; then
    mkdir -p ~/.kube
    if [ ! -f ~/.kube/config ]; then
        cp "$WINDOWS_KUBECONFIG" ~/.kube/config
        echo "Copied kubeconfig from Windows: $WINDOWS_KUBECONFIG"
    else
        echo "kubeconfig already exists at ~/.kube/config"
    fi
else
    echo "WARNING: Windows kubeconfig not found at $WINDOWS_KUBECONFIG"
    echo "You can generate it from talosctl:"
    echo "  talosctl kubeconfig --nodes 192.168.1.5"
fi
echo ""

echo "=== Setup Complete ==="
echo ""
echo "IMPORTANT: Log out and back in (or run 'newgrp docker') for Docker permissions."
echo ""
echo "After that, deploy ADO agents with:"
echo "  cd /mnt/c/Users/Administrator/source/projects/meridianconsole/deploy/kubernetes/ado-agents"
echo '  export AZP_TOKEN="your-pat-token"'
echo "  pwsh ./deploy.ps1 -WorkerNodeIP 192.168.1.251"
echo ""
echo "Or use the bash script:"
echo '  export AZP_TOKEN="your-pat-token"'
echo "  ./deploy.sh --worker-ip 192.168.1.251"

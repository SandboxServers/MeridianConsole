#!/usr/bin/env bash
#
# MeridianConsole Development Environment Bootstrap Script
# Supports: Ubuntu/Debian (primary), Fedora, Arch Linux
#
# Usage: ./bootstrap-dev.sh [OPTIONS]
#
# Options:
#   --skip-docker         Skip Docker installation and configuration
#   --skip-minikube       Skip minikube installation and configuration
#   --skip-infrastructure Skip starting local dev infrastructure (Docker Compose)
#   --help                Show this help message
#
# This script is idempotent - safe to run multiple times.
#

set -euo pipefail

# =============================================================================
# Configuration
# =============================================================================

readonly DOTNET_VERSION="10.0"
readonly DOTNET_SDK_VERSION="10.0.100"
readonly MINIKUBE_VERSION="latest"
readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Flags
SKIP_DOCKER=false
SKIP_MINIKUBE=false
SKIP_INFRASTRUCTURE=false

# =============================================================================
# Colors and Output Functions
# =============================================================================

readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly CYAN='\033[0;36m'
readonly BOLD='\033[1m'
readonly NC='\033[0m' # No Color

print_banner() {
    echo -e "${CYAN}"
    echo "============================================================================="
    echo "  __  __           _     _ _             ____                      _        "
    echo " |  \/  | ___ _ __(_) __| (_) __ _ _ __ / ___|___  _ __  ___  ___ | | ___   "
    echo " | |\/| |/ _ \ '__| |/ _\` | |/ _\` | '_ \| |   / _ \| '_ \/ __|/ _ \| |/ _ \  "
    echo " | |  | |  __/ |  | | (_| | | (_| | | | | |__| (_) | | | \__ \ (_) | |  __/  "
    echo " |_|  |_|\___|_|  |_|\__,_|_|\__,_|_| |_|\____\___/|_| |_|___/\___/|_|\___|  "
    echo "                                                                             "
    echo "  Development Environment Bootstrap Script                                   "
    echo "============================================================================="
    echo -e "${NC}"
}

info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

warn() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

error() {
    echo -e "${RED}[ERROR]${NC} $1" >&2
}

step() {
    echo -e "\n${BOLD}${CYAN}>>> $1${NC}\n"
}

# =============================================================================
# Error Handling
# =============================================================================

cleanup() {
    local exit_code=$?
    if [[ $exit_code -ne 0 ]]; then
        error "Script failed with exit code $exit_code"
        error "Check the output above for details"
    fi
}

trap cleanup EXIT

# =============================================================================
# Utility Functions
# =============================================================================

command_exists() {
    command -v "$1" &>/dev/null
}

is_installed() {
    local cmd="$1"
    local version_check="${2:-}"

    if command_exists "$cmd"; then
        if [[ -n "$version_check" ]]; then
            "$cmd" --version 2>/dev/null | grep -q "$version_check" && return 0
        else
            return 0
        fi
    fi
    return 1
}

get_distro() {
    if [[ -f /etc/os-release ]]; then
        # shellcheck source=/dev/null
        source /etc/os-release
        case "${ID:-}" in
            ubuntu|debian|linuxmint|pop)
                echo "debian"
                ;;
            fedora|rhel|centos|rocky|almalinux)
                echo "fedora"
                ;;
            arch|manjaro|endeavouros)
                echo "arch"
                ;;
            *)
                # Check ID_LIKE for derivatives
                case "${ID_LIKE:-}" in
                    *debian*|*ubuntu*)
                        echo "debian"
                        ;;
                    *fedora*|*rhel*)
                        echo "fedora"
                        ;;
                    *arch*)
                        echo "arch"
                        ;;
                    *)
                        echo "unknown"
                        ;;
                esac
                ;;
        esac
    else
        echo "unknown"
    fi
}

check_internet() {
    if ! ping -c 1 -W 5 8.8.8.8 &>/dev/null && ! ping -c 1 -W 5 1.1.1.1 &>/dev/null; then
        return 1
    fi
    return 0
}

wait_for_apt_lock() {
    local max_wait=60
    local waited=0

    while fuser /var/lib/dpkg/lock-frontend &>/dev/null 2>&1 || \
          fuser /var/lib/apt/lists/lock &>/dev/null 2>&1 || \
          fuser /var/cache/apt/archives/lock &>/dev/null 2>&1; do
        if [[ $waited -ge $max_wait ]]; then
            error "Timed out waiting for apt lock"
            return 1
        fi
        info "Waiting for apt lock to be released..."
        sleep 2
        ((waited+=2))
    done
}

# =============================================================================
# Pre-flight Checks
# =============================================================================

preflight_checks() {
    step "Running pre-flight checks"

    # Check if running as root
    if [[ $EUID -eq 0 ]]; then
        warn "Running as root is not recommended"
        warn "This script will use sudo for commands that require elevated privileges"
        warn "Press Ctrl+C to cancel, or wait 5 seconds to continue..."
        sleep 5
    fi

    # Detect distro
    DISTRO=$(get_distro)
    if [[ "$DISTRO" == "unknown" ]]; then
        error "Unsupported Linux distribution"
        error "This script supports Ubuntu/Debian, Fedora, and Arch Linux"
        exit 1
    fi
    success "Detected distribution family: $DISTRO"

    # Check internet connectivity
    info "Checking internet connectivity..."
    if ! check_internet; then
        error "No internet connection detected"
        error "Please ensure you have internet access and try again"
        exit 1
    fi
    success "Internet connectivity verified"

    # Check for required tools
    if ! command_exists curl; then
        info "Installing curl..."
        case "$DISTRO" in
            debian)
                wait_for_apt_lock
                sudo apt-get update -qq
                sudo apt-get install -y -qq curl
                ;;
            fedora)
                sudo dnf install -y -q curl
                ;;
            arch)
                sudo pacman -Sy --noconfirm --quiet curl
                ;;
        esac
    fi

    if ! command_exists wget; then
        info "Installing wget..."
        case "$DISTRO" in
            debian)
                wait_for_apt_lock
                sudo apt-get install -y -qq wget
                ;;
            fedora)
                sudo dnf install -y -q wget
                ;;
            arch)
                sudo pacman -S --noconfirm --quiet wget
                ;;
        esac
    fi

    success "Pre-flight checks completed"
}

# =============================================================================
# .NET SDK Installation
# =============================================================================

install_dotnet() {
    step "Installing .NET SDK $DOTNET_SDK_VERSION"

    # Check if already installed with correct version
    if command_exists dotnet; then
        local installed_version
        installed_version=$(dotnet --version 2>/dev/null || echo "")
        if [[ "$installed_version" == "$DOTNET_SDK_VERSION" ]]; then
            success ".NET SDK $DOTNET_SDK_VERSION is already installed"
            return 0
        elif [[ "$installed_version" == "$DOTNET_VERSION"* ]]; then
            success ".NET SDK $installed_version is installed (compatible with $DOTNET_VERSION)"
            return 0
        fi
        warn "Different .NET version installed: $installed_version"
    fi

    case "$DISTRO" in
        debian)
            install_dotnet_debian
            ;;
        fedora)
            install_dotnet_fedora
            ;;
        arch)
            install_dotnet_arch
            ;;
    esac

    # Verify installation
    if command_exists dotnet; then
        success ".NET SDK installed: $(dotnet --version)"
    else
        error "Failed to install .NET SDK"
        exit 1
    fi
}

install_dotnet_debian() {
    info "Adding Microsoft package repository for Debian/Ubuntu..."

    # Get Ubuntu/Debian version
    local version_id
    # shellcheck source=/dev/null
    source /etc/os-release
    version_id="${VERSION_ID:-}"
    local os_id="${ID:-ubuntu}"

    # Install prerequisites
    wait_for_apt_lock
    sudo apt-get update -qq
    sudo apt-get install -y -qq apt-transport-https ca-certificates gnupg

    # Add Microsoft GPG key
    curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | sudo gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg 2>/dev/null || true

    # Add repository
    # For .NET 10 preview, we use the PMC feed
    if [[ "$os_id" == "ubuntu" ]]; then
        echo "deb [arch=amd64,arm64,armhf signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/ubuntu/${version_id}/prod ${VERSION_CODENAME:-$(lsb_release -cs)} main" | sudo tee /etc/apt/sources.list.d/microsoft-prod.list > /dev/null
    else
        # For Debian
        echo "deb [arch=amd64,arm64,armhf signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/debian/${version_id}/prod ${VERSION_CODENAME:-$(lsb_release -cs)} main" | sudo tee /etc/apt/sources.list.d/microsoft-prod.list > /dev/null
    fi

    wait_for_apt_lock
    sudo apt-get update -qq

    # Try to install from packages, fall back to install script for preview versions
    if sudo apt-get install -y -qq dotnet-sdk-10.0 2>/dev/null; then
        info "Installed .NET SDK from Microsoft packages"
    else
        warn ".NET 10 not available in packages, using install script..."
        install_dotnet_script
    fi
}

install_dotnet_fedora() {
    info "Installing .NET SDK on Fedora..."

    # Fedora has .NET in official repos, but for preview we use Microsoft's
    if sudo dnf install -y dotnet-sdk-10.0 2>/dev/null; then
        info "Installed .NET SDK from Fedora packages"
    else
        warn ".NET 10 not available in packages, using install script..."
        install_dotnet_script
    fi
}

install_dotnet_arch() {
    info "Installing .NET SDK on Arch Linux..."

    # Arch has dotnet-sdk in community repo
    if sudo pacman -S --noconfirm dotnet-sdk 2>/dev/null; then
        local installed_version
        installed_version=$(dotnet --version 2>/dev/null || echo "")
        if [[ "$installed_version" == "$DOTNET_VERSION"* ]]; then
            info "Installed .NET SDK from Arch packages"
            return 0
        fi
    fi

    warn ".NET 10 not available in Arch packages, using install script..."
    install_dotnet_script
}

install_dotnet_script() {
    info "Installing .NET SDK using official install script..."

    local install_dir="$HOME/.dotnet"

    # Download and run install script
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh

    # Install specific SDK version
    /tmp/dotnet-install.sh --version "$DOTNET_SDK_VERSION" --install-dir "$install_dir" || \
    /tmp/dotnet-install.sh --channel "$DOTNET_VERSION" --install-dir "$install_dir"

    rm -f /tmp/dotnet-install.sh

    # Add to PATH if not already there
    if [[ ":$PATH:" != *":$install_dir:"* ]]; then
        export PATH="$install_dir:$PATH"
        export DOTNET_ROOT="$install_dir"

        # Add to shell profile
        local profile_file="$HOME/.bashrc"
        if [[ -f "$HOME/.zshrc" ]]; then
            profile_file="$HOME/.zshrc"
        fi

        if ! grep -q "DOTNET_ROOT" "$profile_file" 2>/dev/null; then
            cat >> "$profile_file" << 'EOF'

# .NET SDK
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
EOF
            info "Added .NET to PATH in $profile_file"
        fi
    fi
}

# =============================================================================
# Docker Installation
# =============================================================================

install_docker() {
    if [[ "$SKIP_DOCKER" == "true" ]]; then
        info "Skipping Docker installation (--skip-docker flag)"
        return 0
    fi

    step "Installing Docker Engine"

    # Check if already installed
    if command_exists docker; then
        success "Docker is already installed: $(docker --version)"
        configure_docker
        return 0
    fi

    case "$DISTRO" in
        debian)
            install_docker_debian
            ;;
        fedora)
            install_docker_fedora
            ;;
        arch)
            install_docker_arch
            ;;
    esac

    # Verify installation
    if command_exists docker; then
        success "Docker installed: $(docker --version)"
        configure_docker
    else
        error "Failed to install Docker"
        exit 1
    fi
}

install_docker_debian() {
    info "Installing Docker Engine on Debian/Ubuntu..."

    # Remove old versions
    sudo apt-get remove -y docker docker-engine docker.io containerd runc 2>/dev/null || true

    # Install prerequisites
    wait_for_apt_lock
    sudo apt-get update -qq
    sudo apt-get install -y -qq ca-certificates curl gnupg lsb-release

    # Add Docker GPG key
    sudo install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/$(. /etc/os-release && echo "$ID")/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg 2>/dev/null || true
    sudo chmod a+r /etc/apt/keyrings/docker.gpg

    # Add repository
    # shellcheck source=/dev/null
    source /etc/os-release
    echo \
      "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/${ID} \
      ${VERSION_CODENAME:-$(lsb_release -cs)} stable" | \
      sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

    # Install Docker
    wait_for_apt_lock
    sudo apt-get update -qq
    sudo apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
}

install_docker_fedora() {
    info "Installing Docker Engine on Fedora..."

    # Remove old versions
    sudo dnf remove -y docker docker-client docker-client-latest docker-common \
        docker-latest docker-latest-logrotate docker-logrotate docker-engine 2>/dev/null || true

    # Add Docker repository
    sudo dnf -y install dnf-plugins-core
    sudo dnf config-manager --add-repo https://download.docker.com/linux/fedora/docker-ce.repo

    # Install Docker
    sudo dnf install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
}

install_docker_arch() {
    info "Installing Docker on Arch Linux..."

    sudo pacman -S --noconfirm docker docker-compose
}

configure_docker() {
    step "Configuring Docker"

    # Add user to docker group
    if ! groups "$USER" | grep -q docker; then
        info "Adding $USER to docker group..."
        sudo usermod -aG docker "$USER"
        warn "You may need to log out and back in for docker group membership to take effect"
        warn "Alternatively, run: newgrp docker"
    else
        success "User $USER is already in docker group"
    fi

    # Enable and start Docker service
    info "Enabling Docker service..."
    sudo systemctl enable docker
    sudo systemctl start docker

    # Test Docker (may fail if user hasn't re-logged in)
    info "Testing Docker installation..."
    if docker run --rm hello-world &>/dev/null; then
        success "Docker is working correctly"
    elif sudo docker run --rm hello-world &>/dev/null; then
        success "Docker is working (requires re-login for non-sudo access)"
    else
        warn "Docker test failed - service may need a moment to start"
    fi
}

# =============================================================================
# kubectl Installation
# =============================================================================

install_kubectl() {
    if [[ "$SKIP_MINIKUBE" == "true" ]]; then
        info "Skipping kubectl installation (--skip-minikube flag)"
        return 0
    fi

    step "Installing kubectl"

    if command_exists kubectl; then
        success "kubectl is already installed: $(kubectl version --client --short 2>/dev/null || kubectl version --client 2>/dev/null | head -1)"
        return 0
    fi

    case "$DISTRO" in
        debian)
            install_kubectl_debian
            ;;
        fedora)
            install_kubectl_fedora
            ;;
        arch)
            install_kubectl_arch
            ;;
    esac

    if command_exists kubectl; then
        success "kubectl installed successfully"
    else
        error "Failed to install kubectl"
        exit 1
    fi
}

install_kubectl_debian() {
    info "Installing kubectl on Debian/Ubuntu..."

    # Add Kubernetes GPG key
    sudo mkdir -p /etc/apt/keyrings
    curl -fsSL https://pkgs.k8s.io/core:/stable:/v1.31/deb/Release.key | sudo gpg --dearmor -o /etc/apt/keyrings/kubernetes-apt-keyring.gpg 2>/dev/null || true
    sudo chmod 644 /etc/apt/keyrings/kubernetes-apt-keyring.gpg

    # Add repository
    echo 'deb [signed-by=/etc/apt/keyrings/kubernetes-apt-keyring.gpg] https://pkgs.k8s.io/core:/stable:/v1.31/deb/ /' | \
        sudo tee /etc/apt/sources.list.d/kubernetes.list > /dev/null

    wait_for_apt_lock
    sudo apt-get update -qq
    sudo apt-get install -y -qq kubectl
}

install_kubectl_fedora() {
    info "Installing kubectl on Fedora..."

    cat <<EOF | sudo tee /etc/yum.repos.d/kubernetes.repo > /dev/null
[kubernetes]
name=Kubernetes
baseurl=https://pkgs.k8s.io/core:/stable:/v1.31/rpm/
enabled=1
gpgcheck=1
gpgkey=https://pkgs.k8s.io/core:/stable:/v1.31/rpm/repodata/repomd.xml.key
EOF

    sudo dnf install -y kubectl
}

install_kubectl_arch() {
    info "Installing kubectl on Arch Linux..."
    sudo pacman -S --noconfirm kubectl
}

# =============================================================================
# minikube Installation
# =============================================================================

install_minikube() {
    if [[ "$SKIP_MINIKUBE" == "true" ]]; then
        info "Skipping minikube installation (--skip-minikube flag)"
        return 0
    fi

    step "Installing minikube"

    if command_exists minikube; then
        success "minikube is already installed: $(minikube version --short 2>/dev/null || minikube version | head -1)"
        configure_minikube
        return 0
    fi

    info "Downloading minikube binary..."

    local arch
    arch=$(uname -m)
    case "$arch" in
        x86_64)
            arch="amd64"
            ;;
        aarch64|arm64)
            arch="arm64"
            ;;
        *)
            error "Unsupported architecture: $arch"
            exit 1
            ;;
    esac

    curl -fsSLo /tmp/minikube "https://storage.googleapis.com/minikube/releases/latest/minikube-linux-$arch"
    sudo install /tmp/minikube /usr/local/bin/minikube
    rm -f /tmp/minikube

    if command_exists minikube; then
        success "minikube installed: $(minikube version --short 2>/dev/null || minikube version | head -1)"
        configure_minikube
    else
        error "Failed to install minikube"
        exit 1
    fi
}

configure_minikube() {
    step "Configuring minikube"

    # Set Docker as default driver
    info "Setting Docker as minikube driver..."
    minikube config set driver docker

    # Check if minikube is already running
    if minikube status &>/dev/null; then
        success "minikube cluster is already running"
        minikube_enable_addons
        return 0
    fi

    # Check if Docker is accessible
    if ! docker info &>/dev/null && ! sudo docker info &>/dev/null; then
        warn "Docker is not accessible - minikube start will be skipped"
        warn "After logging out and back in, run: minikube start --cpus=4 --memory=8192"
        return 0
    fi

    # Start minikube
    info "Starting minikube cluster (this may take a few minutes)..."
    if minikube start --cpus=4 --memory=8192; then
        success "minikube cluster started"
        minikube_enable_addons
    elif sudo -E minikube start --cpus=4 --memory=8192; then
        success "minikube cluster started (with sudo)"
        minikube_enable_addons
    else
        warn "Failed to start minikube - you may need to re-login for Docker group access"
        warn "After re-login, run: minikube start --cpus=4 --memory=8192"
    fi
}

minikube_enable_addons() {
    info "Enabling minikube addons..."

    minikube addons enable ingress || warn "Failed to enable ingress addon"
    minikube addons enable metrics-server || warn "Failed to enable metrics-server addon"

    success "minikube addons enabled: ingress, metrics-server"
}

# =============================================================================
# Git Installation
# =============================================================================

install_git() {
    step "Installing Git"

    if command_exists git; then
        success "Git is already installed: $(git --version)"
        return 0
    fi

    case "$DISTRO" in
        debian)
            wait_for_apt_lock
            sudo apt-get install -y -qq git
            ;;
        fedora)
            sudo dnf install -y git
            ;;
        arch)
            sudo pacman -S --noconfirm git
            ;;
    esac

    if command_exists git; then
        success "Git installed: $(git --version)"
    else
        error "Failed to install Git"
        exit 1
    fi
}

# =============================================================================
# PostgreSQL Client Tools
# =============================================================================

install_postgresql_client() {
    step "Installing PostgreSQL client tools"

    if command_exists psql; then
        success "PostgreSQL client is already installed: $(psql --version)"
        return 0
    fi

    case "$DISTRO" in
        debian)
            wait_for_apt_lock
            sudo apt-get install -y -qq postgresql-client
            ;;
        fedora)
            sudo dnf install -y postgresql
            ;;
        arch)
            sudo pacman -S --noconfirm postgresql-libs
            ;;
    esac

    if command_exists psql; then
        success "PostgreSQL client installed: $(psql --version)"
    else
        warn "PostgreSQL client installation may have failed"
    fi
}

# =============================================================================
# Project Setup
# =============================================================================

setup_project() {
    step "Setting up MeridianConsole project"

    # Check if we're in or near the project directory
    local project_dir=""

    if [[ -f "$PROJECT_ROOT/Dhadgar.sln" ]]; then
        project_dir="$PROJECT_ROOT"
    elif [[ -f "$PWD/Dhadgar.sln" ]]; then
        project_dir="$PWD"
    else
        info "Not in MeridianConsole project directory - skipping project setup"
        info "To set up the project later, navigate to the project root and run:"
        info "  dotnet restore"
        info "  docker compose -f deploy/compose/docker-compose.dev.yml up -d"
        return 0
    fi

    cd "$project_dir"
    info "Project directory: $project_dir"

    # Restore .NET packages
    if command_exists dotnet; then
        info "Restoring .NET packages..."
        if dotnet restore; then
            success ".NET packages restored"
        else
            warn "dotnet restore failed - may need to check SDK version"
        fi
    fi

    # Start infrastructure
    if [[ "$SKIP_INFRASTRUCTURE" == "true" ]]; then
        info "Skipping infrastructure startup (--skip-infrastructure flag)"
        return 0
    fi

    if [[ -f "$project_dir/deploy/compose/docker-compose.dev.yml" ]]; then
        if docker info &>/dev/null; then
            info "Starting local development infrastructure..."
            if docker compose -f "$project_dir/deploy/compose/docker-compose.dev.yml" up -d; then
                success "Development infrastructure started"
                info "Services running:"
                docker compose -f "$project_dir/deploy/compose/docker-compose.dev.yml" ps --format "table {{.Name}}\t{{.Status}}\t{{.Ports}}" 2>/dev/null || \
                docker compose -f "$project_dir/deploy/compose/docker-compose.dev.yml" ps
            else
                warn "Failed to start infrastructure - Docker may not be accessible"
                warn "After re-login, run: docker compose -f deploy/compose/docker-compose.dev.yml up -d"
            fi
        else
            warn "Docker not accessible - skipping infrastructure startup"
            warn "After re-login, run: docker compose -f deploy/compose/docker-compose.dev.yml up -d"
        fi
    fi
}

# =============================================================================
# Final Summary
# =============================================================================

print_summary() {
    echo ""
    echo -e "${CYAN}=============================================================================${NC}"
    echo -e "${BOLD}  Installation Summary${NC}"
    echo -e "${CYAN}=============================================================================${NC}"
    echo ""

    # Check installed tools
    local all_ok=true

    echo -e "${BOLD}Installed Tools:${NC}"

    if command_exists dotnet; then
        echo -e "  ${GREEN}[OK]${NC} .NET SDK: $(dotnet --version 2>/dev/null || echo 'installed')"
    else
        echo -e "  ${RED}[MISSING]${NC} .NET SDK"
        all_ok=false
    fi

    if command_exists git; then
        echo -e "  ${GREEN}[OK]${NC} Git: $(git --version 2>/dev/null | sed 's/git version //')"
    else
        echo -e "  ${RED}[MISSING]${NC} Git"
        all_ok=false
    fi

    if [[ "$SKIP_DOCKER" != "true" ]]; then
        if command_exists docker; then
            echo -e "  ${GREEN}[OK]${NC} Docker: $(docker --version 2>/dev/null | sed 's/Docker version //' | cut -d',' -f1)"
        else
            echo -e "  ${RED}[MISSING]${NC} Docker"
            all_ok=false
        fi

        if docker compose version &>/dev/null; then
            echo -e "  ${GREEN}[OK]${NC} Docker Compose: $(docker compose version 2>/dev/null | sed 's/Docker Compose version //')"
        else
            echo -e "  ${YELLOW}[CHECK]${NC} Docker Compose (may need re-login)"
        fi
    else
        echo -e "  ${YELLOW}[SKIPPED]${NC} Docker"
        echo -e "  ${YELLOW}[SKIPPED]${NC} Docker Compose"
    fi

    if [[ "$SKIP_MINIKUBE" != "true" ]]; then
        if command_exists kubectl; then
            echo -e "  ${GREEN}[OK]${NC} kubectl: $(kubectl version --client --short 2>/dev/null || kubectl version --client 2>/dev/null | head -1 | sed 's/Client Version: //')"
        else
            echo -e "  ${RED}[MISSING]${NC} kubectl"
            all_ok=false
        fi

        if command_exists minikube; then
            echo -e "  ${GREEN}[OK]${NC} minikube: $(minikube version --short 2>/dev/null || minikube version 2>/dev/null | head -1 | sed 's/minikube version: //')"
        else
            echo -e "  ${RED}[MISSING]${NC} minikube"
            all_ok=false
        fi
    else
        echo -e "  ${YELLOW}[SKIPPED]${NC} kubectl"
        echo -e "  ${YELLOW}[SKIPPED]${NC} minikube"
    fi

    if command_exists psql; then
        echo -e "  ${GREEN}[OK]${NC} PostgreSQL client: $(psql --version 2>/dev/null | sed 's/psql (PostgreSQL) //')"
    else
        echo -e "  ${YELLOW}[OPTIONAL]${NC} PostgreSQL client not installed"
    fi

    echo ""

    # Check if user needs to re-login
    if ! groups "$USER" | grep -q docker 2>/dev/null || ! docker info &>/dev/null 2>&1; then
        echo -e "${YELLOW}${BOLD}Action Required:${NC}"
        echo -e "  You need to log out and back in for Docker group membership to take effect."
        echo -e "  Alternatively, run: ${CYAN}newgrp docker${NC}"
        echo ""
    fi

    echo -e "${BOLD}Next Steps:${NC}"
    echo -e "  1. ${CYAN}cd $PROJECT_ROOT${NC}"
    echo -e "  2. ${CYAN}dotnet build${NC}"
    echo -e "  3. ${CYAN}dotnet test${NC}"
    if [[ "$SKIP_MINIKUBE" != "true" ]]; then
        echo -e "  4. ${CYAN}minikube status${NC} (verify cluster is running)"
    fi
    echo ""

    if [[ "$all_ok" == "true" ]]; then
        echo -e "${GREEN}${BOLD}Bootstrap completed successfully!${NC}"
    else
        echo -e "${YELLOW}${BOLD}Bootstrap completed with warnings. Please review the output above.${NC}"
    fi

    echo ""
}

# =============================================================================
# Argument Parsing
# =============================================================================

show_help() {
    cat << EOF
MeridianConsole Development Environment Bootstrap Script

Usage: $0 [OPTIONS]

Options:
  --skip-docker         Skip Docker installation and configuration
  --skip-minikube       Skip minikube and kubectl installation and configuration
  --skip-infrastructure Skip starting local dev infrastructure (Docker Compose)
  --help                Show this help message

Supported Distributions:
  - Ubuntu/Debian (and derivatives like Linux Mint, Pop!_OS)
  - Fedora (and RHEL-based like CentOS, Rocky, Alma)
  - Arch Linux (and derivatives like Manjaro, EndeavourOS)

This script installs:
  - .NET SDK $DOTNET_SDK_VERSION
  - Docker Engine with Docker Compose plugin
  - minikube (local Kubernetes)
  - kubectl (Kubernetes CLI)
  - Git
  - PostgreSQL client tools

The script is idempotent and safe to run multiple times.
EOF
}

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --skip-docker)
                SKIP_DOCKER=true
                shift
                ;;
            --skip-minikube)
                SKIP_MINIKUBE=true
                shift
                ;;
            --skip-infrastructure)
                SKIP_INFRASTRUCTURE=true
                shift
                ;;
            --help|-h)
                show_help
                exit 0
                ;;
            *)
                error "Unknown option: $1"
                echo "Use --help for usage information"
                exit 1
                ;;
        esac
    done
}

# =============================================================================
# Main
# =============================================================================

main() {
    parse_args "$@"

    print_banner

    preflight_checks
    install_git
    install_dotnet
    install_docker
    install_kubectl
    install_minikube
    install_postgresql_client
    setup_project

    print_summary
}

main "$@"

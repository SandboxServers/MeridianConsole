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
#   --status              Show current checkpoint status and exit
#   --reset               Clear checkpoint and start fresh
#   --help                Show this help message
#
# This script is idempotent, supports checkpointing, and will stop when a
# restart is required, allowing you to resume after restart.
#

set -uo pipefail

# =============================================================================
# Configuration
# =============================================================================

readonly DOTNET_VERSION="10.0"
readonly DOTNET_SDK_VERSION="10.0.100"
readonly MINIKUBE_VERSION="latest"
readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
readonly LOG_FILE="$HOME/.meridian-bootstrap.log"
readonly CHECKPOINT_FILE="$HOME/.meridian-bootstrap-checkpoint.json"

# Flags
SKIP_DOCKER=false
SKIP_MINIKUBE=false
SKIP_INFRASTRUCTURE=false
SHOW_STATUS=false
RESET_CHECKPOINT=false

# Phases (in order)
readonly PHASES=(
    "PreFlight"
    "CoreTools"      # Git, curl, wget
    "DotNet"         # .NET SDK - MAY REQUIRE NEW TERMINAL
    "Docker"         # Docker Engine - MAY REQUIRE LOGOUT
    "Kubernetes"     # kubectl, minikube
    "ProjectSetup"   # dotnet restore, docker-compose
    "Complete"
)

# =============================================================================
# Colors and Output Functions
# =============================================================================

readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly CYAN='\033[0;36m'
readonly MAGENTA='\033[0;35m'
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
    echo -e "    ${BOLD}Log file:${NC}   $LOG_FILE"
    echo -e "    ${BOLD}Checkpoint:${NC} $CHECKPOINT_FILE"
    echo ""
}

info() {
    echo -e "${BLUE}[INFO]${NC} $1"
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [INFO] $1" >> "$LOG_FILE"
}

success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [SUCCESS] $1" >> "$LOG_FILE"
}

warn() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [WARNING] $1" >> "$LOG_FILE"
}

error() {
    echo -e "${RED}[ERROR]${NC} $1" >&2
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [ERROR] $1" >> "$LOG_FILE"
}

step() {
    echo -e "\n${BOLD}${CYAN}>>> $1${NC}\n"
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [PHASE] $1" >> "$LOG_FILE"
}

# =============================================================================
# Checkpoint Management
# =============================================================================

get_checkpoint() {
    if [[ -f "$CHECKPOINT_FILE" ]]; then
        cat "$CHECKPOINT_FILE"
    else
        echo "{}"
    fi
}

get_checkpoint_phase() {
    if [[ -f "$CHECKPOINT_FILE" ]]; then
        grep -o '"phase"[[:space:]]*:[[:space:]]*"[^"]*"' "$CHECKPOINT_FILE" 2>/dev/null | \
            sed 's/.*"phase"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/' || echo ""
    else
        echo ""
    fi
}

get_checkpoint_restart_required() {
    if [[ -f "$CHECKPOINT_FILE" ]]; then
        grep -o '"restart_required"[[:space:]]*:[[:space:]]*true' "$CHECKPOINT_FILE" &>/dev/null && echo "true" || echo "false"
    else
        echo "false"
    fi
}

get_checkpoint_restart_reason() {
    if [[ -f "$CHECKPOINT_FILE" ]]; then
        grep -o '"restart_reason"[[:space:]]*:[[:space:]]*"[^"]*"' "$CHECKPOINT_FILE" 2>/dev/null | \
            sed 's/.*"restart_reason"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/' || echo ""
    else
        echo ""
    fi
}

save_checkpoint() {
    local phase="$1"
    local restart_required="${2:-false}"
    local restart_reason="${3:-}"
    local restart_type="${4:-terminal}"

    cat > "$CHECKPOINT_FILE" << EOF
{
    "phase": "$phase",
    "timestamp": "$(date -Iseconds)",
    "restart_required": $restart_required,
    "restart_reason": "$restart_reason",
    "restart_type": "$restart_type",
    "parameters": {
        "skip_docker": $SKIP_DOCKER,
        "skip_minikube": $SKIP_MINIKUBE,
        "skip_infrastructure": $SKIP_INFRASTRUCTURE
    }
}
EOF
    info "Checkpoint saved: Phase=$phase"
}

clear_checkpoint() {
    if [[ -f "$CHECKPOINT_FILE" ]]; then
        rm -f "$CHECKPOINT_FILE"
        info "Checkpoint cleared"
    fi
}

get_phase_index() {
    local phase="$1"
    for i in "${!PHASES[@]}"; do
        if [[ "${PHASES[$i]}" == "$phase" ]]; then
            echo "$i"
            return
        fi
    done
    echo "-1"
}

should_run_phase() {
    local phase="$1"
    local start_phase="$2"

    local phase_index=$(get_phase_index "$phase")
    local start_index=$(get_phase_index "$start_phase")

    [[ $phase_index -ge $start_index ]]
}

request_restart_and_exit() {
    local reason="$1"
    local next_phase="$2"
    local restart_type="${3:-terminal}"  # terminal, logout, or reboot

    save_checkpoint "$next_phase" "true" "$reason" "$restart_type"

    echo ""
    echo -e "${RED}======================================================================${NC}"
    echo -e "${RED}  RESTART REQUIRED${NC}"
    echo -e "${RED}======================================================================${NC}"
    echo ""
    echo -e "  ${YELLOW}Reason:${NC} $reason"
    echo ""

    case "$restart_type" in
        reboot)
            echo -e "  ${CYAN}ACTION REQUIRED:${NC}"
            echo -e "    1. Save all your work"
            echo -e "    2. Restart your computer: ${BOLD}sudo reboot${NC}"
            echo -e "    3. Re-run this script after restart:"
            echo ""
            echo -e "       ${GREEN}./scripts/bootstrap-dev.sh${NC}"
            echo ""
            echo -e "  ${BOLD}The script will automatically resume from where it left off.${NC}"
            ;;
        logout)
            echo -e "  ${CYAN}ACTION REQUIRED:${NC}"
            echo -e "    1. Log out of your session (or restart)"
            echo -e "    2. Log back in"
            echo -e "    3. Re-run this script:"
            echo ""
            echo -e "       ${GREEN}./scripts/bootstrap-dev.sh${NC}"
            echo ""
            echo -e "  ${BOLD}Or apply group changes without logout:${NC}"
            echo -e "       ${YELLOW}newgrp docker${NC}"
            echo -e "       ${GREEN}./scripts/bootstrap-dev.sh${NC}"
            echo ""
            echo -e "  ${BOLD}The script will automatically resume from where it left off.${NC}"
            ;;
        terminal)
            echo -e "  ${CYAN}ACTION REQUIRED:${NC}"
            echo -e "    1. Close this terminal"
            echo -e "    2. Open a NEW terminal"
            echo -e "    3. Re-run this script:"
            echo ""
            echo -e "       ${GREEN}./scripts/bootstrap-dev.sh${NC}"
            echo ""
            echo -e "  ${BOLD}Or reload your shell profile:${NC}"
            echo -e "       ${YELLOW}source ~/.bashrc${NC}  (or ~/.zshrc)"
            echo -e "       ${GREEN}./scripts/bootstrap-dev.sh${NC}"
            echo ""
            echo -e "  ${BOLD}The script will automatically resume from where it left off.${NC}"
            ;;
    esac

    echo ""
    echo -e "${RED}======================================================================${NC}"
    echo ""

    exit 0
}

show_checkpoint_status() {
    echo ""
    echo -e "${CYAN}============================================================${NC}"
    echo -e "${CYAN}  CHECKPOINT STATUS${NC}"
    echo -e "${CYAN}============================================================${NC}"
    echo ""

    if [[ ! -f "$CHECKPOINT_FILE" ]]; then
        echo -e "  ${BOLD}No checkpoint found.${NC} Script will start from beginning."
    else
        local phase=$(get_checkpoint_phase)
        local restart_required=$(get_checkpoint_restart_required)
        local restart_reason=$(get_checkpoint_restart_reason)
        local timestamp=$(grep -o '"timestamp"[[:space:]]*:[[:space:]]*"[^"]*"' "$CHECKPOINT_FILE" 2>/dev/null | \
            sed 's/.*"timestamp"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/' || echo "unknown")

        echo -e "  ${BOLD}Current Phase:${NC} $phase"
        echo -e "  ${BOLD}Saved At:${NC} $timestamp"

        if [[ "$restart_required" == "true" ]]; then
            echo -e "  ${BOLD}Restart Required:${NC} ${YELLOW}Yes${NC}"
            echo -e "  ${BOLD}Reason:${NC} ${YELLOW}$restart_reason${NC}"
        else
            echo -e "  ${BOLD}Restart Required:${NC} ${GREEN}No${NC}"
        fi

        echo ""
        echo -e "  ${BOLD}Saved Parameters:${NC}"

        local skip_docker=$(grep -o '"skip_docker"[[:space:]]*:[[:space:]]*[^,}]*' "$CHECKPOINT_FILE" 2>/dev/null | \
            sed 's/.*:[[:space:]]*//' || echo "false")
        local skip_minikube=$(grep -o '"skip_minikube"[[:space:]]*:[[:space:]]*[^,}]*' "$CHECKPOINT_FILE" 2>/dev/null | \
            sed 's/.*:[[:space:]]*//' || echo "false")
        local skip_infra=$(grep -o '"skip_infrastructure"[[:space:]]*:[[:space:]]*[^,}]*' "$CHECKPOINT_FILE" 2>/dev/null | \
            sed 's/.*:[[:space:]]*//' || echo "false")

        echo "    skip_docker: $skip_docker"
        echo "    skip_minikube: $skip_minikube"
        echo "    skip_infrastructure: $skip_infra"
    fi

    echo ""
    echo -e "${CYAN}============================================================${NC}"
    echo ""
    echo -e "  To clear checkpoint and start fresh: ${GREEN}./bootstrap-dev.sh --reset${NC}"
    echo ""
}

restore_parameters_from_checkpoint() {
    if [[ -f "$CHECKPOINT_FILE" ]]; then
        local skip_docker=$(grep -o '"skip_docker"[[:space:]]*:[[:space:]]*true' "$CHECKPOINT_FILE" &>/dev/null && echo "true" || echo "false")
        local skip_minikube=$(grep -o '"skip_minikube"[[:space:]]*:[[:space:]]*true' "$CHECKPOINT_FILE" &>/dev/null && echo "true" || echo "false")
        local skip_infra=$(grep -o '"skip_infrastructure"[[:space:]]*:[[:space:]]*true' "$CHECKPOINT_FILE" &>/dev/null && echo "true" || echo "false")

        # Only restore if not overridden by command line
        if [[ "$SKIP_DOCKER" == "false" && "$skip_docker" == "true" ]]; then
            SKIP_DOCKER=true
        fi
        if [[ "$SKIP_MINIKUBE" == "false" && "$skip_minikube" == "true" ]]; then
            SKIP_MINIKUBE=true
        fi
        if [[ "$SKIP_INFRASTRUCTURE" == "false" && "$skip_infra" == "true" ]]; then
            SKIP_INFRASTRUCTURE=true
        fi
    fi
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

is_wsl2() {
    if [[ -f /proc/version ]] && grep -qi microsoft /proc/version; then
        return 0
    fi
    return 1
}

has_docker_desktop() {
    # Docker Desktop for Windows is accessible in WSL2
    if is_wsl2 && command_exists docker; then
        # Check if docker is using Docker Desktop (looks for docker-desktop context)
        if docker context ls 2>/dev/null | grep -q "docker-desktop"; then
            return 0
        fi
        # Alternative check: Docker Desktop typically has a specific socket path
        if [[ -S /var/run/docker.sock ]] && docker info 2>/dev/null | grep -qi "docker desktop"; then
            return 0
        fi
    fi
    return 1
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
# Phase: PreFlight
# =============================================================================

phase_preflight() {
    step "Phase: Pre-flight Checks"

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

    save_checkpoint "CoreTools"
}

# =============================================================================
# Phase: CoreTools
# =============================================================================

phase_core_tools() {
    step "Phase: Core Tools (curl, wget, git)"

    # Install curl if needed
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
    success "curl is available"

    # Install wget if needed
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
    success "wget is available"

    # Install git if needed
    if ! command_exists git; then
        info "Installing git..."
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
    fi
    success "Git is available: $(git --version)"

    save_checkpoint "DotNet"
}

# =============================================================================
# Phase: .NET SDK
# =============================================================================

phase_dotnet() {
    step "Phase: .NET SDK $DOTNET_SDK_VERSION"

    # Check if already installed with correct version
    if command_exists dotnet; then
        local installed_version
        installed_version=$(dotnet --version 2>/dev/null || echo "")
        if [[ "$installed_version" == "$DOTNET_SDK_VERSION" ]]; then
            success ".NET SDK $DOTNET_SDK_VERSION is already installed"
            save_checkpoint "Docker"
            return 0
        elif [[ "$installed_version" == "$DOTNET_VERSION"* ]]; then
            success ".NET SDK $installed_version is installed (compatible with $DOTNET_VERSION)"
            save_checkpoint "Docker"
            return 0
        fi
        warn "Different .NET version installed: $installed_version"
    fi

    # Install .NET SDK
    local dotnet_installed=false

    case "$DISTRO" in
        debian)
            install_dotnet_debian && dotnet_installed=true
            ;;
        fedora)
            install_dotnet_fedora && dotnet_installed=true
            ;;
        arch)
            install_dotnet_arch && dotnet_installed=true
            ;;
    esac

    # Verify installation
    if command_exists dotnet; then
        success ".NET SDK installed: $(dotnet --version)"
        save_checkpoint "Docker"
    elif [[ "$dotnet_installed" == "true" ]]; then
        # .NET was installed but not in PATH yet - needs new terminal
        request_restart_and_exit \
            ".NET SDK was installed to ~/.dotnet. A new terminal session is required for the 'dotnet' command to be available in PATH." \
            "Docker" \
            "terminal"
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
    if [[ "$os_id" == "ubuntu" ]]; then
        echo "deb [arch=amd64,arm64,armhf signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/ubuntu/${version_id}/prod ${VERSION_CODENAME:-$(lsb_release -cs)} main" | sudo tee /etc/apt/sources.list.d/microsoft-prod.list > /dev/null
    else
        echo "deb [arch=amd64,arm64,armhf signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/debian/${version_id}/prod ${VERSION_CODENAME:-$(lsb_release -cs)} main" | sudo tee /etc/apt/sources.list.d/microsoft-prod.list > /dev/null
    fi

    wait_for_apt_lock
    sudo apt-get update -qq

    # Try to install from packages, fall back to install script for preview versions
    if sudo apt-get install -y -qq dotnet-sdk-10.0 2>/dev/null; then
        info "Installed .NET SDK from Microsoft packages"
        return 0
    else
        warn ".NET 10 not available in packages, using install script..."
        install_dotnet_script
        return $?
    fi
}

install_dotnet_fedora() {
    info "Installing .NET SDK on Fedora..."

    if sudo dnf install -y dotnet-sdk-10.0 2>/dev/null; then
        info "Installed .NET SDK from Fedora packages"
        return 0
    else
        warn ".NET 10 not available in packages, using install script..."
        install_dotnet_script
        return $?
    fi
}

install_dotnet_arch() {
    info "Installing .NET SDK on Arch Linux..."

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
    return $?
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
            return 0  # Signal that restart is needed
        fi
    fi

    return 0
}

# =============================================================================
# Phase: Docker
# =============================================================================

phase_docker() {
    if [[ "$SKIP_DOCKER" == "true" ]]; then
        info "Skipping Docker installation (--skip-docker flag)"
        save_checkpoint "Kubernetes"
        return 0
    fi

    step "Phase: Docker Engine"

    # Check for Docker Desktop in WSL2
    if has_docker_desktop; then
        success "Docker Desktop detected (running in WSL2)"
        success "Docker is already installed: $(docker --version)"
        if docker info &>/dev/null; then
            success "Docker is accessible and working"
            info "Skipping Docker Engine installation (using Docker Desktop)"
            save_checkpoint "Kubernetes"
            return 0
        else
            warn "Docker Desktop detected but not accessible - it may not be running"
            warn "Please start Docker Desktop on Windows and try again"
            save_checkpoint "Kubernetes"
            return 0
        fi
    fi

    # Check if already installed (non-Docker Desktop)
    if command_exists docker; then
        success "Docker is already installed: $(docker --version)"

        # Check if user is in docker group
        if groups "$USER" | grep -q docker; then
            # Test if Docker is accessible
            if docker info &>/dev/null; then
                success "Docker is accessible without sudo"
                configure_docker_service
                save_checkpoint "Kubernetes"
                return 0
            else
                warn "Docker installed but not accessible - may need logout/login"
            fi
        else
            # User not in docker group - add them (only if not WSL2 with Docker Desktop)
            if ! is_wsl2; then
                info "Adding $USER to docker group..."
                if sudo -n usermod -aG docker "$USER" 2>/dev/null; then
                    request_restart_and_exit \
                        "You were added to the 'docker' group. A logout/login is required for this change to take effect." \
                        "Kubernetes" \
                        "logout"
                else
                    warn "Could not add user to docker group (sudo required)"
                fi
            fi
        fi
    fi

    # Install Docker
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

    # Verify installation and add to group
    if command_exists docker; then
        success "Docker installed: $(docker --version)"

        # Add user to docker group
        if ! groups "$USER" | grep -q docker; then
            info "Adding $USER to docker group..."
            sudo usermod -aG docker "$USER"

            request_restart_and_exit \
                "Docker was installed and you were added to the 'docker' group. A logout/login is required for Docker to be accessible without sudo." \
                "Kubernetes" \
                "logout"
        fi

        configure_docker_service
        save_checkpoint "Kubernetes"
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

configure_docker_service() {
    # Skip systemctl in WSL2 or if Docker Desktop is detected
    if is_wsl2 || has_docker_desktop; then
        info "Skipping Docker service configuration (WSL2/Docker Desktop)"
        # Test Docker
        info "Testing Docker installation..."
        if docker run --rm hello-world &>/dev/null; then
            success "Docker is working correctly"
        else
            warn "Docker test failed - ensure Docker Desktop is running on Windows"
        fi
        return 0
    fi

    info "Configuring Docker service..."

    # Enable and start Docker service (only if sudo is available)
    if sudo -n true 2>/dev/null; then
        sudo systemctl enable docker 2>/dev/null || warn "Could not enable Docker service"
        sudo systemctl start docker 2>/dev/null || warn "Could not start Docker service"
    else
        warn "Sudo not available - skipping Docker service configuration"
        warn "You may need to manually start the Docker service"
    fi

    # Test Docker
    info "Testing Docker installation..."
    if docker run --rm hello-world &>/dev/null; then
        success "Docker is working correctly"
    elif sudo -n docker run --rm hello-world 2>/dev/null; then
        success "Docker is working (requires re-login for non-sudo access)"
    else
        warn "Docker test failed - service may need a moment to start"
    fi
}

# =============================================================================
# Phase: Kubernetes
# =============================================================================

phase_kubernetes() {
    if [[ "$SKIP_MINIKUBE" == "true" ]]; then
        info "Skipping Kubernetes tools installation (--skip-minikube flag)"
        save_checkpoint "ProjectSetup"
        return 0
    fi

    step "Phase: Kubernetes Tools (kubectl, minikube)"

    # Install kubectl
    install_kubectl

    # Install minikube
    install_minikube

    # Configure minikube
    configure_minikube

    save_checkpoint "ProjectSetup"
}

install_kubectl() {
    if command_exists kubectl; then
        success "kubectl is already installed: $(kubectl version --client --short 2>/dev/null || kubectl version --client 2>/dev/null | head -1)"
        return 0
    fi

    info "Installing kubectl..."

    # Try package manager installation first
    local install_success=false

    case "$DISTRO" in
        debian)
            # Try with sudo if available
            if sudo -n true 2>/dev/null; then
                sudo mkdir -p /etc/apt/keyrings 2>/dev/null || true
                curl -fsSL https://pkgs.k8s.io/core:/stable:/v1.31/deb/Release.key | sudo gpg --dearmor -o /etc/apt/keyrings/kubernetes-apt-keyring.gpg 2>/dev/null || true
                sudo chmod 644 /etc/apt/keyrings/kubernetes-apt-keyring.gpg 2>/dev/null || true

                echo 'deb [signed-by=/etc/apt/keyrings/kubernetes-apt-keyring.gpg] https://pkgs.k8s.io/core:/stable:/v1.31/deb/ /' | \
                    sudo tee /etc/apt/sources.list.d/kubernetes.list > /dev/null 2>&1 || true

                wait_for_apt_lock
                sudo apt-get update -qq 2>/dev/null || true
                sudo apt-get install -y -qq kubectl 2>/dev/null && install_success=true
            fi
            ;;
        fedora)
            if sudo -n true 2>/dev/null; then
                cat <<EOF | sudo tee /etc/yum.repos.d/kubernetes.repo > /dev/null 2>&1 || true
[kubernetes]
name=Kubernetes
baseurl=https://pkgs.k8s.io/core:/stable:/v1.31/rpm/
enabled=1
gpgcheck=1
gpgkey=https://pkgs.k8s.io/core:/stable:/v1.31/rpm/repodata/repomd.xml.key
EOF
                sudo dnf install -y kubectl 2>/dev/null && install_success=true
            fi
            ;;
        arch)
            if sudo -n true 2>/dev/null; then
                sudo pacman -S --noconfirm kubectl 2>/dev/null && install_success=true
            fi
            ;;
    esac

    # If package manager install failed, download binary to user directory
    if ! command_exists kubectl; then
        warn "Package manager installation failed or sudo not available, installing to ~/.local/bin"
        install_kubectl_binary
    else
        success "kubectl installed successfully"
    fi
}

install_kubectl_binary() {
    mkdir -p "$HOME/.local/bin"

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
            return 1
            ;;
    esac

    info "Downloading kubectl binary..."
    local kubectl_version
    kubectl_version=$(curl -fsSL https://dl.k8s.io/release/stable.txt)

    curl -fsSLo "$HOME/.local/bin/kubectl" "https://dl.k8s.io/release/${kubectl_version}/bin/linux/${arch}/kubectl"
    chmod +x "$HOME/.local/bin/kubectl"

    # Add to PATH if not already there
    if [[ ":$PATH:" != *":$HOME/.local/bin:"* ]]; then
        export PATH="$HOME/.local/bin:$PATH"

        local profile_file="$HOME/.bashrc"
        if [[ -f "$HOME/.zshrc" ]]; then
            profile_file="$HOME/.zshrc"
        fi

        if ! grep -q ".local/bin" "$profile_file" 2>/dev/null; then
            echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$profile_file"
            info "Added ~/.local/bin to PATH in $profile_file"
        fi
    fi

    if command_exists kubectl; then
        success "kubectl binary installed to ~/.local/bin"
    else
        error "Failed to install kubectl"
        return 1
    fi
}

install_minikube() {
    if command_exists minikube; then
        success "minikube is already installed: $(minikube version --short 2>/dev/null || minikube version | head -1)"
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

    # Create local bin directory if it doesn't exist
    mkdir -p "$HOME/.local/bin"

    curl -fsSLo "$HOME/.local/bin/minikube" "https://storage.googleapis.com/minikube/releases/latest/minikube-linux-$arch"
    chmod +x "$HOME/.local/bin/minikube"

    # Add to PATH if not already there
    if [[ ":$PATH:" != *":$HOME/.local/bin:"* ]]; then
        export PATH="$HOME/.local/bin:$PATH"

        # Add to shell profile
        local profile_file="$HOME/.bashrc"
        if [[ -f "$HOME/.zshrc" ]]; then
            profile_file="$HOME/.zshrc"
        fi

        if ! grep -q ".local/bin" "$profile_file" 2>/dev/null; then
            echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$profile_file"
            info "Added ~/.local/bin to PATH in $profile_file"
        fi
    fi

    if command_exists minikube; then
        success "minikube installed: $(minikube version --short 2>/dev/null || minikube version | head -1)"
    else
        error "Failed to install minikube"
        exit 1
    fi
}

configure_minikube() {
    info "Configuring minikube..."

    # WSL2-specific: Check for Windows minikube conflict
    if is_wsl2; then
        # Check if Windows minikube exists
        if command -v minikube.exe &>/dev/null || [[ -f "/mnt/c/Program Files/minikube/minikube.exe" ]]; then
            warn "Detected Windows minikube installation"

            # Check if there's an existing cluster (from Windows)
            if minikube status &>/dev/null || minikube.exe status &>/dev/null 2>&1; then
                echo ""
                warn "╔════════════════════════════════════════════════════════════════════╗"
                warn "║  WSL2 + Windows minikube conflict detected!                       ║"
                warn "╚════════════════════════════════════════════════════════════════════╝"
                echo ""
                warn "You have minikube running from Windows which may conflict with WSL2 minikube."
                warn ""
                warn "RECOMMENDED APPROACH for WSL2 + Docker Desktop:"
                warn "  → Use Windows minikube (already installed)"
                warn "  → Skip WSL2 minikube to avoid conflicts"
                warn ""
                warn "To use Windows minikube from WSL2, add to ~/.bashrc:"
                warn "  export PATH=\"\$PATH:/mnt/c/Program Files/minikube\""
                warn ""
                warn "OR, to use WSL2 minikube only:"
                warn "  1. Delete the Windows minikube cluster: minikube.exe delete"
                warn "  2. Delete the WSL2 minikube cluster: minikube delete"
                warn "  3. Re-run this script or: minikube start --cpus=4 --memory=8192"
                echo ""
                warn "Skipping minikube cluster start to avoid conflicts..."
                return 0
            fi
        fi
    fi

    # Set Docker as default driver
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
    if minikube start --cpus=4 --memory=8192 2>&1; then
        success "minikube cluster started"
        minikube_enable_addons
    else
        local exit_code=$?
        warn "Failed to start minikube (exit code: $exit_code)"

        if is_wsl2; then
            warn "This may be due to:"
            warn "  - Conflicting Windows minikube installation"
            warn "  - Corrupted certificates (run: minikube delete && minikube start)"
            warn "  - Docker group membership (re-login required)"
        else
            warn "You may need to re-login for Docker group access"
        fi

        warn "To start manually later: minikube start --cpus=4 --memory=8192"
    fi
}

minikube_enable_addons() {
    info "Enabling minikube addons..."

    minikube addons enable ingress || warn "Failed to enable ingress addon"
    minikube addons enable metrics-server || warn "Failed to enable metrics-server addon"

    success "minikube addons enabled: ingress, metrics-server"
}

# =============================================================================
# Phase: Project Setup
# =============================================================================

phase_project_setup() {
    step "Phase: Project Setup"

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
        save_checkpoint "Complete"
        return 0
    fi

    cd "$project_dir" || exit 1
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

    # Install PostgreSQL client
    install_postgresql_client

    # Start infrastructure
    if [[ "$SKIP_INFRASTRUCTURE" == "true" ]]; then
        info "Skipping infrastructure startup (--skip-infrastructure flag)"
    elif [[ -f "$project_dir/deploy/compose/docker-compose.dev.yml" ]]; then
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

    save_checkpoint "Complete"
}

install_postgresql_client() {
    if command_exists psql; then
        success "PostgreSQL client is already installed: $(psql --version)"
        return 0
    fi

    info "Installing PostgreSQL client tools..."

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
        if [[ "$SKIP_DOCKER" != "true" ]]; then
            echo -e "${YELLOW}${BOLD}Action Required:${NC}"
            echo -e "  You may need to log out and back in for Docker group membership to take effect."
            echo -e "  Alternatively, run: ${CYAN}newgrp docker${NC}"
            echo ""
        fi
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
  --status              Show current checkpoint status and exit
  --reset               Clear checkpoint and start fresh
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

The script is idempotent, supports checkpointing, and will stop when a
restart is required. Re-run the script after restart to continue.

Checkpoint file: ~/.meridian-bootstrap-checkpoint.json
Log file: ~/.meridian-bootstrap.log
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
            --status)
                SHOW_STATUS=true
                shift
                ;;
            --reset)
                RESET_CHECKPOINT=true
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

    # Handle --status flag
    if [[ "$SHOW_STATUS" == "true" ]]; then
        show_checkpoint_status
        exit 0
    fi

    # Handle --reset flag
    if [[ "$RESET_CHECKPOINT" == "true" ]]; then
        clear_checkpoint
        success "Checkpoint cleared. Starting fresh."
    fi

    # Initialize log file
    echo "============================================================" >> "$LOG_FILE"
    echo "MeridianConsole Bootstrap - $(date)" >> "$LOG_FILE"
    echo "============================================================" >> "$LOG_FILE"

    print_banner

    # Check for existing checkpoint
    local start_phase="PreFlight"
    local checkpoint_phase=$(get_checkpoint_phase)

    if [[ -n "$checkpoint_phase" && "$checkpoint_phase" != "" ]]; then
        echo ""
        info "Found existing checkpoint at phase: $checkpoint_phase"

        if [[ "$(get_checkpoint_restart_required)" == "true" ]]; then
            info "Resuming after restart (Reason: $(get_checkpoint_restart_reason))"
        fi

        start_phase="$checkpoint_phase"

        # Restore parameters from checkpoint
        restore_parameters_from_checkpoint

        echo ""
    fi

    # Detect distro early (needed by all phases)
    DISTRO=$(get_distro)

    # Run phases
    if should_run_phase "PreFlight" "$start_phase"; then
        phase_preflight
    fi

    if should_run_phase "CoreTools" "$start_phase"; then
        phase_core_tools
    fi

    if should_run_phase "DotNet" "$start_phase"; then
        phase_dotnet
    fi

    if should_run_phase "Docker" "$start_phase"; then
        phase_docker
    fi

    if should_run_phase "Kubernetes" "$start_phase"; then
        phase_kubernetes
    fi

    if should_run_phase "ProjectSetup" "$start_phase"; then
        phase_project_setup
    fi

    # Clear checkpoint on successful completion
    clear_checkpoint

    print_summary
}

main "$@"

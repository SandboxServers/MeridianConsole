#!/usr/bin/env bash
#
# MeridianConsole Development Environment Verification Script
# Checks all required tools, services, and configurations for local development.
#

set -o pipefail

# ============================================================================
# Colors and Formatting
# ============================================================================

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
WHITE='\033[1;37m'
GRAY='\033[0;90m'
NC='\033[0m' # No Color

# ============================================================================
# Counters
# ============================================================================

PASSED=0
WARNINGS=0
FAILED=0

# ============================================================================
# Helper Functions
# ============================================================================

print_status() {
    local component="$1"
    local status="$2"
    local message="$3"
    local fix="$4"

    case "$status" in
        OK)   printf "${GREEN}[OK]${NC} " ;;
        WARN) printf "${YELLOW}[!!]${NC} " ;;
        FAIL) printf "${RED}[X]${NC} " ;;
        INFO) printf "${CYAN}[i]${NC} " ;;
        *)    printf "[?] " ;;
    esac

    printf "%s" "$component"
    if [ -n "$message" ]; then
        printf " ${GRAY}- %s${NC}" "$message"
    fi
    printf "\n"

    if [ "$status" = "FAIL" ] && [ -n "$fix" ]; then
        printf "    ${YELLOW}Fix: %s${NC}\n" "$fix"
    fi
}

command_exists() {
    command -v "$1" &> /dev/null
}

get_version() {
    local cmd="$1"
    local arg="${2:---version}"
    $cmd $arg 2>&1 | head -n1
}

# ============================================================================
# Main Verification
# ============================================================================

echo ""
echo -e "${CYAN}============================================================${NC}"
echo -e "${CYAN}  MeridianConsole Development Environment Verification${NC}"
echo -e "${CYAN}============================================================${NC}"
echo ""

# Detect OS
if [ -f /etc/os-release ]; then
    . /etc/os-release
    OS_NAME="$NAME"
    OS_VERSION="$VERSION_ID"
else
    OS_NAME="Unknown"
    OS_VERSION="Unknown"
fi

print_status "Operating System" "INFO" "$OS_NAME $OS_VERSION"
echo ""

# ----------------------------------------------------------------------------
# .NET SDK
# ----------------------------------------------------------------------------

echo -e "${WHITE}--- .NET SDK ---${NC}"

if command_exists dotnet; then
    dotnet_version=$(dotnet --version 2>/dev/null)
    required_version="10.0"

    if [[ "$dotnet_version" == "$required_version"* ]]; then
        print_status ".NET SDK" "OK" "Version $dotnet_version"
        ((PASSED++))
    else
        print_status ".NET SDK" "WARN" "Version $dotnet_version (expected $required_version.x)" \
            "Install .NET 10 SDK from https://dot.net/download"
        ((WARNINGS++))
    fi

    # Check EF Core tools
    if dotnet ef --version &>/dev/null; then
        print_status "EF Core Tools" "OK" "Available"
        ((PASSED++))
    else
        print_status "EF Core Tools" "WARN" "Not installed globally (project-local is fine)" \
            "dotnet tool install --global dotnet-ef"
        ((WARNINGS++))
    fi
else
    print_status ".NET SDK" "FAIL" "Not installed" \
        "Install from https://dot.net/download or use the bootstrap script"
    ((FAILED++))
fi

echo ""

# ----------------------------------------------------------------------------
# Docker
# ----------------------------------------------------------------------------

echo -e "${WHITE}--- Docker ---${NC}"

if command_exists docker; then
    docker_version=$(docker --version 2>/dev/null | sed 's/Docker version //' | cut -d',' -f1)
    print_status "Docker CLI" "OK" "Version $docker_version"
    ((PASSED++))

    # Check if Docker daemon is running
    if docker info &>/dev/null; then
        print_status "Docker Daemon" "OK" "Running"
        ((PASSED++))
    else
        print_status "Docker Daemon" "FAIL" "Not running or no permission" \
            "sudo systemctl start docker && sudo usermod -aG docker \$USER"
        ((FAILED++))
    fi
else
    print_status "Docker" "FAIL" "Not installed" \
        "Install Docker Engine: https://docs.docker.com/engine/install/"
    ((FAILED++))
fi

# Docker Compose
if command_exists docker && docker compose version &>/dev/null; then
    compose_version=$(docker compose version 2>/dev/null | sed 's/Docker Compose version //')
    print_status "Docker Compose" "OK" "$compose_version"
    ((PASSED++))
elif command_exists docker-compose; then
    compose_version=$(docker-compose --version 2>/dev/null | sed 's/.*version //' | cut -d',' -f1)
    print_status "Docker Compose" "OK" "$compose_version (standalone)"
    ((PASSED++))
else
    print_status "Docker Compose" "FAIL" "Not available" \
        "Install Docker Compose plugin: apt install docker-compose-plugin"
    ((FAILED++))
fi

echo ""

# ----------------------------------------------------------------------------
# Kubernetes Tools
# ----------------------------------------------------------------------------

echo -e "${WHITE}--- Kubernetes ---${NC}"

if command_exists kubectl; then
    kubectl_version=$(kubectl version --client -o json 2>/dev/null | grep -o '"gitVersion": "[^"]*"' | head -1 | cut -d'"' -f4)
    if [ -z "$kubectl_version" ]; then
        kubectl_version=$(kubectl version --client --short 2>/dev/null | head -1)
    fi
    print_status "kubectl" "OK" "$kubectl_version"
    ((PASSED++))
else
    print_status "kubectl" "FAIL" "Not installed" \
        "Install kubectl: https://kubernetes.io/docs/tasks/tools/"
    ((FAILED++))
fi

if command_exists minikube; then
    minikube_version=$(minikube version --short 2>/dev/null)
    print_status "minikube" "OK" "$minikube_version"
    ((PASSED++))

    # Check minikube status
    minikube_status=$(minikube status --format='{{.Host}}' 2>/dev/null)
    if [ "$minikube_status" = "Running" ]; then
        print_status "minikube Cluster" "OK" "Running"
        ((PASSED++))
    elif [ "$minikube_status" = "Stopped" ]; then
        print_status "minikube Cluster" "WARN" "Stopped" \
            "minikube start"
        ((WARNINGS++))
    else
        print_status "minikube Cluster" "INFO" "Not initialized (optional)"
    fi
else
    print_status "minikube" "WARN" "Not installed (optional for basic dev)" \
        "Install minikube: https://minikube.sigs.k8s.io/docs/start/"
    ((WARNINGS++))
fi

echo ""

# ----------------------------------------------------------------------------
# Git
# ----------------------------------------------------------------------------

echo -e "${WHITE}--- Version Control ---${NC}"

if command_exists git; then
    git_version=$(git --version | sed 's/git version //')
    print_status "Git" "OK" "$git_version"
    ((PASSED++))
else
    print_status "Git" "FAIL" "Not installed" \
        "apt install git / dnf install git / pacman -S git"
    ((FAILED++))
fi

echo ""

# ----------------------------------------------------------------------------
# Local Infrastructure (Docker Compose services)
# ----------------------------------------------------------------------------

echo -e "${WHITE}--- Local Infrastructure ---${NC}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
COMPOSE_FILE="$REPO_ROOT/deploy/compose/docker-compose.dev.yml"

if [ -f "$COMPOSE_FILE" ]; then
    print_status "docker-compose.dev.yml" "OK" "Found"
    ((PASSED++))

    # Check if containers are running
    if command_exists docker && docker info &>/dev/null; then
        containers=$(docker ps --format "{{.Names}}" 2>/dev/null)

        # Check PostgreSQL
        if echo "$containers" | grep -q "postgres"; then
            print_status "PostgreSQL Container" "OK" "Running"
            ((PASSED++))
        else
            print_status "PostgreSQL Container" "WARN" "Not running" \
                "docker compose -f deploy/compose/docker-compose.dev.yml up -d"
            ((WARNINGS++))
        fi

        # Check RabbitMQ
        if echo "$containers" | grep -q "rabbitmq"; then
            print_status "RabbitMQ Container" "OK" "Running"
            ((PASSED++))
        else
            print_status "RabbitMQ Container" "WARN" "Not running" \
                "docker compose -f deploy/compose/docker-compose.dev.yml up -d"
            ((WARNINGS++))
        fi

        # Check Redis
        if echo "$containers" | grep -q "redis"; then
            print_status "Redis Container" "OK" "Running"
            ((PASSED++))
        else
            print_status "Redis Container" "WARN" "Not running" \
                "docker compose -f deploy/compose/docker-compose.dev.yml up -d"
            ((WARNINGS++))
        fi
    fi
else
    print_status "docker-compose.dev.yml" "WARN" "Not found (not in repo directory?)"
    ((WARNINGS++))
fi

echo ""

# ----------------------------------------------------------------------------
# Project Build
# ----------------------------------------------------------------------------

echo -e "${WHITE}--- Project ---${NC}"

SOLUTION_FILE="$REPO_ROOT/Dhadgar.sln"
if [ -f "$SOLUTION_FILE" ]; then
    print_status "Solution File" "OK" "Dhadgar.sln found"
    ((PASSED++))

    # Check if restored
    OBJ_FOLDER="$REPO_ROOT/src/Dhadgar.Gateway/obj"
    if [ -d "$OBJ_FOLDER" ]; then
        print_status "NuGet Restore" "OK" "Packages restored"
        ((PASSED++))
    else
        print_status "NuGet Restore" "WARN" "Not restored" \
            "dotnet restore"
        ((WARNINGS++))
    fi
else
    print_status "Solution File" "INFO" "Not in repository root"
fi

echo ""

# ----------------------------------------------------------------------------
# Network Connectivity (Service Ports) - Optional detailed check
# ----------------------------------------------------------------------------

if [ "$1" = "--detailed" ] || [ "$1" = "-d" ]; then
    echo -e "${WHITE}--- Network Connectivity ---${NC}"

    check_port() {
        local name="$1"
        local port="$2"
        if timeout 1 bash -c "echo >/dev/tcp/localhost/$port" 2>/dev/null; then
            print_status "$name" "OK" "Port $port accessible"
        else
            print_status "$name" "WARN" "Port $port not accessible"
        fi
    }

    check_port "PostgreSQL" 5432
    check_port "RabbitMQ AMQP" 5672
    check_port "RabbitMQ Management" 15672
    check_port "Redis" 6379

    echo ""
fi

# ============================================================================
# Summary
# ============================================================================

echo -e "${CYAN}============================================================${NC}"
echo -e "${CYAN}  Summary${NC}"
echo -e "${CYAN}============================================================${NC}"
echo ""

TOTAL=$((PASSED + WARNINGS + FAILED))

printf "  Passed:   ${GREEN}%d${NC} / %d\n" "$PASSED" "$TOTAL"
printf "  Warnings: ${YELLOW}%d${NC} / %d\n" "$WARNINGS" "$TOTAL"
printf "  Failed:   ${RED}%d${NC} / %d\n" "$FAILED" "$TOTAL"
echo ""

if [ $FAILED -eq 0 ] && [ $WARNINGS -eq 0 ]; then
    echo -e "${GREEN}Your development environment is fully configured!${NC}"
    echo ""
    echo -e "${CYAN}Quick start commands:${NC}"
    echo "  dotnet build                    # Build the solution"
    echo "  dotnet test                     # Run tests"
    echo "  dotnet run --project src/Dhadgar.Gateway  # Run Gateway"
    echo ""
elif [ $FAILED -eq 0 ]; then
    echo -e "${YELLOW}Your environment is mostly ready. Review warnings above.${NC}"
    echo ""
else
    echo -e "${RED}Some required components are missing. Please fix the issues above.${NC}"
    echo -e "${YELLOW}Run './scripts/bootstrap-dev.sh' to install missing components.${NC}"
    echo ""
fi

exit $FAILED

#!/bin/bash

# Meridian Console Helm Chart Installation Script
# This script helps you install the Meridian Console control plane to Kubernetes

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Default values
RELEASE_NAME="meridian"
NAMESPACE="meridian-system"
CHART_PATH="./meridian-console"
VALUES_FILE=""
DRY_RUN=false
UPGRADE=false

# Function to print colored output
print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to show usage
usage() {
    cat << EOF
Usage: $0 [OPTIONS]

Install Meridian Console Helm chart to Kubernetes

OPTIONS:
    -n, --namespace NAMESPACE    Kubernetes namespace (default: meridian-system)
    -r, --release NAME          Helm release name (default: meridian)
    -f, --values FILE           Custom values file
    -u, --upgrade               Upgrade existing installation
    -d, --dry-run               Perform a dry-run installation
    -h, --help                  Show this help message

EXAMPLES:
    # Install with defaults
    $0

    # Install with custom namespace
    $0 -n production

    # Install with custom values
    $0 -f custom-values.yaml

    # Upgrade existing installation
    $0 -u

    # Dry-run to see what would be installed
    $0 -d

EOF
    exit 1
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -n|--namespace)
            NAMESPACE="$2"
            shift 2
            ;;
        -r|--release)
            RELEASE_NAME="$2"
            shift 2
            ;;
        -f|--values)
            VALUES_FILE="$2"
            shift 2
            ;;
        -u|--upgrade)
            UPGRADE=true
            shift
            ;;
        -d|--dry-run)
            DRY_RUN=true
            shift
            ;;
        -h|--help)
            usage
            ;;
        *)
            print_error "Unknown option: $1"
            usage
            ;;
    esac
done

# Check if helm is installed
if ! command -v helm &> /dev/null; then
    print_error "Helm is not installed. Please install Helm 3.8+ first."
    exit 1
fi

# Check if kubectl is installed
if ! command -v kubectl &> /dev/null; then
    print_error "kubectl is not installed. Please install kubectl first."
    exit 1
fi

# Check if chart directory exists
if [ ! -d "$CHART_PATH" ]; then
    print_error "Chart directory not found: $CHART_PATH"
    exit 1
fi

print_info "========================================"
print_info "Meridian Console Helm Installation"
print_info "========================================"
print_info "Release name: $RELEASE_NAME"
print_info "Namespace: $NAMESPACE"
print_info "Chart path: $CHART_PATH"
if [ -n "$VALUES_FILE" ]; then
    print_info "Values file: $VALUES_FILE"
fi
if [ "$DRY_RUN" = true ]; then
    print_warn "DRY-RUN MODE ENABLED"
fi
echo ""

# Add Bitnami repository for dependencies
print_info "Adding Bitnami Helm repository..."
helm repo add bitnami https://charts.bitnami.com/bitnami --force-update
helm repo update

# Update chart dependencies
print_info "Updating chart dependencies..."
helm dependency update "$CHART_PATH"

# Build Helm command
HELM_CMD="helm"
if [ "$UPGRADE" = true ]; then
    HELM_CMD="$HELM_CMD upgrade --install"
else
    HELM_CMD="$HELM_CMD install"
fi

HELM_CMD="$HELM_CMD $RELEASE_NAME $CHART_PATH"
HELM_CMD="$HELM_CMD --namespace $NAMESPACE"
HELM_CMD="$HELM_CMD --create-namespace"

if [ -n "$VALUES_FILE" ]; then
    HELM_CMD="$HELM_CMD --values $VALUES_FILE"
fi

if [ "$DRY_RUN" = true ]; then
    HELM_CMD="$HELM_CMD --dry-run --debug"
fi

# Security warning
if [ "$DRY_RUN" = false ]; then
    echo ""
    print_warn "========================================"
    print_warn "SECURITY WARNING"
    print_warn "========================================"
    print_warn "This installation uses DEFAULT PASSWORDS!"
    print_warn ""
    print_warn "For production deployments, you MUST:"
    print_warn "  1. Change PostgreSQL password"
    print_warn "  2. Change RabbitMQ password"
    print_warn "  3. Change Redis password"
    print_warn "  4. Generate secure JWT signing key"
    print_warn ""
    print_warn "Use a custom values file or --set flags:"
    print_warn "  --set secrets.postgresPassword=<secure-password>"
    print_warn "  --set secrets.rabbitmqPassword=<secure-password>"
    print_warn "  --set secrets.redisPassword=<secure-password>"
    print_warn "  --set secrets.jwtSigningKey=<secure-key>"
    print_warn "========================================"
    echo ""

    read -p "Continue with installation? (y/N) " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        print_info "Installation cancelled."
        exit 0
    fi
fi

# Execute Helm command
print_info "Executing Helm installation..."
echo ""
eval "$HELM_CMD"

if [ "$DRY_RUN" = false ]; then
    echo ""
    print_info "========================================"
    print_info "Installation completed!"
    print_info "========================================"
    print_info ""
    print_info "Check deployment status:"
    print_info "  kubectl get pods -n $NAMESPACE"
    print_info ""
    print_info "View Helm release:"
    print_info "  helm list -n $NAMESPACE"
    print_info ""
    print_info "Get release notes:"
    print_info "  helm get notes $RELEASE_NAME -n $NAMESPACE"
    print_info ""
fi

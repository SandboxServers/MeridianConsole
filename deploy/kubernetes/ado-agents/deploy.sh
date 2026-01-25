#!/usr/bin/env bash
set -euo pipefail

# ADO Agent Kubernetes Deployment Script
# Deploys 10 Azure DevOps agents to a dedicated Talos worker node
# No container registry required - builds locally and imports directly to node

usage() {
  echo "Usage: $0 --worker-ip <IP> [--worker-node <name>] [--image-tag <tag>] [--skip-build]"
  echo ""
  echo "Options:"
  echo "  --worker-ip     IP address of the Talos worker node (required)"
  echo "  --worker-node   Name of the worker node (default: talos-xmo-fex)"
  echo "  --image-tag     Docker image tag (default: latest)"
  echo "  --skip-build    Skip building the image (use existing on node)"
  echo ""
  echo "Environment variables:"
  echo "  AZP_TOKEN       Azure DevOps PAT token (required for initial deployment)"
  exit 1
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"

# Defaults
IMAGE_NAME="dhadgar-azdo-agent"
IMAGE_TAG="latest"
WORKER_NODE="talos-xmo-fex"
WORKER_IP=""
NAMESPACE="ado-agents"
SKIP_BUILD=false
TAR_FILE="/tmp/dhadgar-azdo-agent.tar"

# Parse arguments
while [[ $# -gt 0 ]]; do
  case $1 in
    --worker-ip)
      WORKER_IP="$2"
      shift 2
      ;;
    --worker-node)
      WORKER_NODE="$2"
      shift 2
      ;;
    --image-tag)
      IMAGE_TAG="$2"
      shift 2
      ;;
    --skip-build)
      SKIP_BUILD=true
      shift
      ;;
    -h|--help)
      usage
      ;;
    *)
      echo "Unknown option: $1"
      usage
      ;;
  esac
done

if [ -z "${WORKER_IP}" ]; then
  echo "ERROR: --worker-ip is required"
  usage
fi

FULL_IMAGE_NAME="localhost/${IMAGE_NAME}:${IMAGE_TAG}"

echo "=== ADO Agent Kubernetes Deployment (No Registry) ==="
echo "Image: ${FULL_IMAGE_NAME}"
echo "Worker Node: ${WORKER_NODE} (${WORKER_IP})"
echo "Tar File: ${TAR_FILE}"
echo ""

# Verify prerequisites
echo "=== Verifying prerequisites ==="

if ! command -v kubectl &>/dev/null; then
  echo "ERROR: kubectl not found. Install it first."
  exit 1
fi
echo "[OK] kubectl found"

if ! command -v talosctl &>/dev/null; then
  echo "ERROR: talosctl not found. Install it first."
  echo "  Download from: https://github.com/siderolabs/talos/releases"
  exit 1
fi
echo "[OK] talosctl found"

if [ "${SKIP_BUILD}" = false ]; then
  if ! command -v docker &>/dev/null; then
    echo "ERROR: docker not found. Install it first."
    exit 1
  fi
  echo "[OK] docker found"
fi
echo ""

# Step 1: Build image locally
if [ "${SKIP_BUILD}" = false ]; then
  echo "=== Step 1: Building Docker image locally ==="
  docker build \
    -t "${FULL_IMAGE_NAME}" \
    -f "${REPO_ROOT}/deploy/azure-pipelines/agent.Dockerfile" \
    "${REPO_ROOT}"
  echo "Image built: ${FULL_IMAGE_NAME}"
  echo ""

  # Step 2: Export image to tar
  echo "=== Step 2: Exporting image to tar file ==="
  rm -f "${TAR_FILE}"
  docker save -o "${TAR_FILE}" "${FULL_IMAGE_NAME}"
  TAR_SIZE=$(du -h "${TAR_FILE}" | cut -f1)
  echo "Image exported: ${TAR_FILE} (${TAR_SIZE})"
  echo ""

  # Step 3: Import image to Talos node
  echo "=== Step 3: Importing image to Talos node ==="
  echo "Copying image to node ${WORKER_IP} (this may take a while)..."

  cat "${TAR_FILE}" | talosctl -n "${WORKER_IP}" image import -

  echo "Image imported to node successfully"
  echo ""

  # Cleanup
  rm -f "${TAR_FILE}"
  echo "Cleaned up temporary tar file"
  echo ""
else
  echo "=== Skipping build (using existing image on node) ==="
  echo ""
fi

# Step 4: Configure worker node
echo "=== Step 4: Configuring worker node ${WORKER_NODE} ==="

if ! kubectl get node "${WORKER_NODE}" &>/dev/null; then
  echo "WARNING: Node '${WORKER_NODE}' not found in cluster."
  echo "Make sure you've applied the worker.yaml config to the Talos VM."
  echo ""
  echo "To add the worker node:"
  echo "  talosctl apply-config --insecure --nodes ${WORKER_IP} --file worker.yaml"
  echo ""
  read -p "Continue anyway? (y/N) " -n 1 -r
  echo
  if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    exit 1
  fi
else
  kubectl label node "${WORKER_NODE}" workload=ado-agents --overwrite || true
  kubectl taint nodes "${WORKER_NODE}" dedicated=ado-agents:NoSchedule --overwrite || true
  echo "Node labeled and tainted for dedicated ADO agent workloads"
fi
echo ""

# Step 5: Create namespace and secret
echo "=== Step 5: Creating namespace and secret ==="
kubectl apply -f "${SCRIPT_DIR}/namespace.yaml"

if ! kubectl get secret azdo-agent-credentials -n "${NAMESPACE}" &>/dev/null; then
  if [ -z "${AZP_TOKEN:-}" ]; then
    echo "ERROR: AZP_TOKEN environment variable is required for initial deployment."
    echo "Set it via: export AZP_TOKEN='your-pat-token'"
    exit 1
  fi
  kubectl create secret generic azdo-agent-credentials \
    --namespace "${NAMESPACE}" \
    --from-literal=AZP_TOKEN="${AZP_TOKEN}"
  echo "Secret created"
else
  echo "Secret already exists, skipping creation"
fi
echo ""

# Step 6: Deploy agents
echo "=== Step 6: Deploying ADO agents ==="
kubectl apply -f "${SCRIPT_DIR}/deployment.yaml"
echo ""

# Step 7: Wait for rollout
echo "=== Step 7: Waiting for deployment rollout ==="
kubectl rollout status deployment/azdo-agent -n "${NAMESPACE}" --timeout=300s
echo ""

# Step 8: Show status
echo "=== Deployment Complete ==="
kubectl get pods -n "${NAMESPACE}" -o wide
echo ""
echo "All 10 ADO agents should now be registering with Azure DevOps."
echo "Check agent pool at: https://dev.azure.com/SandboxServers/_settings/agentpools"

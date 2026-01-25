# ADO Agents on Kubernetes (Talos)

Deploys Azure DevOps self-hosted agents to a dedicated Talos worker node.

**No container registry required** - images are built locally and imported directly to the Talos node.

## Prerequisites

1. **Talos worker node** added to the cluster (see below)
2. **Docker** for building images
3. **kubectl** configured for your Talos cluster
4. **talosctl** configured for your Talos cluster
5. **ADO PAT token** with Agent Pools (read, manage) scope

## Quick Start

### 1. Add the Worker Node to Your Talos Cluster

```bash
# If you have the original secrets.yaml from cluster creation:
talosctl gen config talos-cluster https://192.168.1.5:6443 \
  --with-secrets secrets.yaml \
  --output-dir _out

# Apply worker config to the new VM
talosctl apply-config --insecure \
  --nodes 192.168.1.251 \
  --file _out/worker.yaml

# Verify the node joined
kubectl get nodes
```

### 2. Deploy the Agents

**PowerShell (Windows):**

```powershell
$env:AZP_TOKEN = "your-pat-token"
.\deploy.ps1 -WorkerNodeIP 192.168.1.251
```

**Bash (Linux/macOS/WSL):**

```bash
export AZP_TOKEN="your-pat-token"
./deploy.sh --worker-ip 192.168.1.251
```

The script will:
1. Build the Docker image locally
2. Export it to a tar file
3. Import it directly into the Talos node's containerd
4. Label and taint the worker node
5. Create the namespace and secret
6. Deploy 10 agent pods

### Skip Build (Use Existing Image)

If you've already imported the image to the node:

```powershell
# PowerShell
.\deploy.ps1 -WorkerNodeIP 192.168.1.251 -SkipBuild

# Bash
./deploy.sh --worker-ip 192.168.1.251 --skip-build
```

## What Gets Deployed

- **Namespace**: `ado-agents`
- **Secret**: `azdo-agent-credentials` (stores PAT token)
- **Deployment**: 10 replicas of the ADO agent

## Node Configuration

The deployment script automatically labels and taints the worker node:

```bash
# Label (for nodeSelector)
kubectl label node talos-xmo-fex workload=ado-agents

# Taint (prevents other workloads from scheduling)
kubectl taint nodes talos-xmo-fex dedicated=ado-agents:NoSchedule
```

## Customization

### Change Replica Count

Edit `deployment.yaml`:

```yaml
spec:
  replicas: 10  # Change this
```

Or use kubectl:

```bash
kubectl scale deployment/azdo-agent -n ado-agents --replicas=5
```

### Change Resource Limits

Edit `deployment.yaml` resources section:

```yaml
resources:
  requests:
    cpu: "500m"
    memory: "2Gi"
  limits:
    cpu: "2"
    memory: "4Gi"
```

### Use Different Agent Pool

Edit `deployment.yaml` environment variable:

```yaml
- name: AZP_POOL
  value: "Your Pool Name"
```

### Change Worker Node Name

Both scripts accept a parameter:

```powershell
# PowerShell
.\deploy.ps1 -WorkerNodeIP 192.168.1.251 -WorkerNode my-worker-node

# Bash
./deploy.sh --worker-ip 192.168.1.251 --worker-node my-worker-node
```

## Monitoring

```bash
# Check pod status
kubectl get pods -n ado-agents -o wide

# View logs
kubectl logs -n ado-agents -l app.kubernetes.io/name=azdo-agent --tail=50

# Follow logs from all agents
kubectl logs -n ado-agents -l app.kubernetes.io/name=azdo-agent -f --max-log-requests=10
```

## Troubleshooting

### Pods stuck in Pending

Check if the node has the correct label/taint:

```bash
kubectl describe node talos-xmo-fex | grep -A5 "Labels:"
kubectl describe node talos-xmo-fex | grep -A5 "Taints:"
```

### Agents not appearing in Azure DevOps

Check pod logs for registration errors:

```bash
kubectl logs -n ado-agents <pod-name>
```

Common issues:
- Invalid PAT token
- PAT token doesn't have Agent Pools scope
- Network connectivity to dev.azure.com

### Image not found (ErrImageNeverPull)

The image wasn't imported to the node. Re-run the deploy script without `-SkipBuild`:

```powershell
.\deploy.ps1 -WorkerNodeIP 192.168.1.251
```

Or manually import:

```bash
# Build and save locally
docker build -t localhost/dhadgar-azdo-agent:latest -f deploy/azure-pipelines/agent.Dockerfile .
docker save -o /tmp/agent.tar localhost/dhadgar-azdo-agent:latest

# Import to node
cat /tmp/agent.tar | talosctl -n 192.168.1.251 image import -
```

### Check images on Talos node

```bash
talosctl -n 192.168.1.251 image ls | grep dhadgar
```

## Cleanup

```bash
# Remove deployment (agents will deregister automatically)
kubectl delete -f deployment.yaml

# Remove everything
kubectl delete namespace ado-agents

# Remove node label/taint
kubectl label node talos-xmo-fex workload-
kubectl taint nodes talos-xmo-fex dedicated-
```

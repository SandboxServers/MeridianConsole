# Meridian Console Helm Chart & ACR Publishing - Complete Summary

**Date**: 2025-12-27
**Project**: Meridian Console (Dhadgar)
**Purpose**: Kubernetes deployment via Helm chart published to Azure Container Registry

---

## Overview

This document summarizes the complete Helm chart creation and Azure Container Registry (ACR) publishing setup for the Meridian Console application.

### What Was Created

1. **Complete Helm chart** for deploying 13 microservices to Kubernetes
2. **Azure Container Registry publishing** via Azure DevOps Pipelines
3. **Azure setup automation** scripts and documentation
4. **Installation and usage documentation**

---

## Azure Container Registry Details

### Registry Information

```
Name: meridianconsoleacr
Login Server: meridianconsoleacr-etdvg4cthscffqdf.azurecr.io
Location: Central US
SKU: Basic tier
Resource Group: meridian-rg
Created: 2025-12-27

Configuration:
  - Admin user: Disabled
  - Public network access: Enabled
  - Anonymous pull: Disabled
  - Provisioning state: Succeeded
```

### Chart Location in ACR

```
oci://meridianconsoleacr-etdvg4cthscffqdf.azurecr.io/helm/meridian-console
```

---

## Helm Chart Structure

### Location

```
/deploy/kubernetes/helm/meridian-console/
```

### What's Included

**13 Microservices** deployed as separate Deployments + Services:
- Gateway (YARP reverse proxy - entry point)
- Identity (AuthN/AuthZ, JWT, RBAC)
- Billing (SaaS subscriptions)
- Servers (Game server lifecycle)
- Nodes (Node inventory, health, capacity)
- Tasks (Orchestration, background jobs)
- Files (File metadata, transfer orchestration)
- Mods (Mod registry, versioning)
- Console (Real-time console via SignalR)
- Notifications (Email/Discord/webhooks)
- Secrets (Secret storage, rotation)
- Discord (Discord integration)

**Infrastructure Dependencies** (Bitnami subcharts):
- PostgreSQL 15.2.5 (with database-per-service pattern - 12 databases)
- RabbitMQ 14.3.3 (message bus)
- Redis 19.5.2 (caching, session storage)

**Key Features**:
- Database-per-service pattern (12 separate PostgreSQL databases)
- SignalR sticky sessions for Console service
- ConfigMap and Secret templates for environment configuration
- Ingress for Gateway (single public entry point)
- Reusable template helpers in `_helpers.tpl`

### File Inventory

```
deploy/kubernetes/helm/meridian-console/
├── Chart.yaml                          # Chart metadata + dependencies
├── values.yaml                         # 500+ lines of configuration
├── README.md                          # Usage documentation
├── .helmignore                        # Files to exclude from packaging
├── templates/
│   ├── _helpers.tpl                   # Reusable template functions
│   ├── NOTES.txt                      # Post-install instructions
│   ├── configmap.yaml                 # Common configuration
│   ├── secret.yaml                    # Sensitive values
│   ├── ingress.yaml                   # Gateway ingress
│   ├── gateway/                       # deployment.yaml, service.yaml
│   ├── identity/                      # deployment.yaml, service.yaml
│   ├── billing/                       # deployment.yaml, service.yaml
│   ├── servers/                       # deployment.yaml, service.yaml
│   ├── nodes/                         # deployment.yaml, service.yaml
│   ├── tasks/                         # deployment.yaml, service.yaml
│   ├── files/                         # deployment.yaml, service.yaml
│   ├── mods/                          # deployment.yaml, service.yaml
│   ├── console/                       # deployment.yaml, service.yaml
│   ├── notifications/                 # deployment.yaml, service.yaml
│   ├── firewall/                      # deployment.yaml, service.yaml
│   ├── secrets/                       # deployment.yaml, service.yaml
│   └── discord/                       # deployment.yaml, service.yaml
└── charts/                            # Dependency subcharts (auto-downloaded)
```

---

## Publishing Setup

### Azure DevOps Pipeline

**File**: `deploy/kubernetes/helm/helm-publish-pipeline.yml`

**Triggers**:
- Push to `main` branch (when chart files change)
- Can be configured for tag/release triggers
- Manual trigger supported

**Prerequisites**:
1. Azure service connection in Azure DevOps named `meridian-acr-connection`
2. Service connection must have `AcrPush` role on ACR

**What it does**:
1. Checks out code
2. Installs Helm 3.13.0
3. Authenticates to Azure and ACR
4. Adds Bitnami repository
5. Updates chart dependencies (downloads PostgreSQL, RabbitMQ, Redis charts)
6. Lints the chart
7. Packages the chart
8. Pushes to ACR as OCI artifact

### Creating Azure Service Connection

**Option 1: Azure DevOps UI**
1. Go to Project Settings → Service connections
2. Create new Azure Resource Manager connection
3. Choose "Service principal (automatic)"
4. Scope to resource group `meridian-rg`
5. Name it `meridian-acr-connection`
6. Grant `AcrPush` role on ACR

**Option 2: Azure CLI (using existing setup script)**

Run the setup script to create service principal:
```bash
cd /mnt/c/Users/xxL0L/code_projects/MeridianConsole/deploy/kubernetes/helm
./setup-acr.sh
```

This creates:
- Azure AD app `meridian-helm-publisher`
- Service principal with `AcrPush` role
- Outputs: AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID

Then manually create service connection in Azure DevOps using these credentials.

---

## Azure Setup Automation

### Files Created

1. **`setup-acr.sh`** - Automated Azure configuration script
   - Creates Azure AD app and service principal
   - Sets up federated credentials (for GitHub Actions, if needed)
   - Grants ACR permissions
   - Validates existing resources before applying changes

2. **`ACR-SETUP.md`** - Comprehensive setup guide
   - Detailed instructions for ACR creation
   - Service principal setup
   - RBAC configuration
   - Troubleshooting

3. **`ACR-QUICKSTART.md`** - Quick start guide
   - 5-minute setup for common scenarios
   - Copy-paste script examples

4. **`PUBLISHING.md`** - Publishing workflow documentation
   - How automatic publishing works
   - Versioning strategy
   - Usage examples

---

## Installation & Usage

### Prerequisites

**For Publishing** (CI/CD):
- Azure DevOps with service connection to Azure
- `AcrPush` role on ACR

**For Installing** (users):
- Helm 3.8+ (for OCI support)
- Azure CLI
- Access to ACR (via Azure RBAC)
- Kubernetes cluster (AKS, Talos, or other)

### Installing the Chart

**Step 1: Authenticate to ACR**
```bash
az login
az acr login --name meridianconsoleacr
```

**Step 2: Install with Helm**
```bash
# Install with defaults
helm install meridian oci://meridianconsoleacr-etdvg4cthscffqdf.azurecr.io/helm/meridian-console \
  --version 0.1.0 \
  --namespace meridian-system \
  --create-namespace

# Install with custom values
helm install meridian oci://meridianconsoleacr-etdvg4cthscffqdf.azurecr.io/helm/meridian-console \
  --version 0.1.0 \
  --namespace meridian-system \
  --create-namespace \
  --values custom-values.yaml

# Install latest version (omit --version)
helm install meridian oci://meridianconsoleacr-etdvg4cthscffqdf.azurecr.io/helm/meridian-console \
  --namespace meridian-system \
  --create-namespace
```

**Step 3: Verify Installation**
```bash
# Check deployments
kubectl get deployments -n meridian-system

# Check services
kubectl get services -n meridian-system

# Check pods
kubectl get pods -n meridian-system

# View release
helm list -n meridian-system
```

### Upgrading

```bash
# Re-authenticate if needed
az acr login --name meridianconsoleacr

# Upgrade to specific version
helm upgrade meridian oci://meridianconsoleacr-etdvg4cthscffqdf.azurecr.io/helm/meridian-console \
  --version 0.2.0 \
  --namespace meridian-system

# Upgrade to latest
helm upgrade meridian oci://meridianconsoleacr-etdvg4cthscffqdf.azurecr.io/helm/meridian-console \
  --namespace meridian-system
```

### Uninstalling

```bash
helm uninstall meridian --namespace meridian-system
```

### Listing Available Versions

```bash
# Using Azure CLI
az acr repository show-tags \
  --name meridianconsoleacr \
  --repository helm/meridian-console \
  --output table

# List all repositories
az acr repository list --name meridianconsoleacr --output table
```

---

## Versioning Strategy

### Chart Version

Defined in `Chart.yaml`:
```yaml
version: 0.1.0
appVersion: "0.1.0"
```

**Semantic Versioning (SemVer)**:
- **MAJOR** (1.0.0) - Breaking changes
- **MINOR** (0.1.0) - New features, backwards compatible
- **PATCH** (0.0.1) - Bug fixes, backwards compatible

### Releasing New Versions

**Step 1: Update Chart.yaml**
```bash
cd deploy/kubernetes/helm/meridian-console
# Edit Chart.yaml, increment version
```

**Step 2: Commit and Push**
```bash
git add deploy/kubernetes/helm/meridian-console/Chart.yaml
git commit -m "chore(helm): bump chart version to 0.2.0"
git push origin main
```

**Step 3: Pipeline Automatically Publishes**
- Azure DevOps pipeline triggers on push
- Chart is packaged and pushed to ACR
- New version available immediately

---

## Configuration Customization

### Common Customizations

**Custom values file example** (`custom-values.yaml`):

```yaml
# Use external PostgreSQL instead of subchart
postgresql:
  enabled: false

# Override database connection
secrets:
  postgresHost: "prod-postgres.mydomain.com"
  postgresPort: "5432"
  postgresUser: "meridian_prod"
  postgresPassword: "strong-password-here"

# Use external RabbitMQ
rabbitmq:
  enabled: false

rabbitmqConfig:
  host: "prod-rabbitmq.mydomain.com"
  username: "meridian"
  password: "strong-password-here"

# Scale Gateway
gateway:
  replicaCount: 3
  resources:
    requests:
      cpu: "500m"
      memory: "512Mi"
    limits:
      cpu: "1000m"
      memory: "1Gi"

# Configure Ingress
ingress:
  enabled: true
  className: "nginx"
  annotations:
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
  hosts:
    - host: meridian.yourdomain.com
      paths:
        - path: /
          pathType: Prefix
  tls:
    - secretName: meridian-tls
      hosts:
        - meridian.yourdomain.com
```

### Database Configuration

**Using Subchart (Default)**:
```yaml
postgresql:
  enabled: true
  auth:
    username: dhadgar
    password: change-me-in-production
    database: dhadgar
```

**Using External Database**:
```yaml
postgresql:
  enabled: false

secrets:
  postgresHost: "my-postgres-server.postgres.database.azure.com"
  postgresPort: "5432"
  postgresUser: "meridian@my-postgres-server"
  postgresPassword: "strong-password"
```

### Resource Limits

Edit per-service in `values.yaml`:
```yaml
gateway:
  resources:
    requests:
      cpu: "100m"
      memory: "128Mi"
    limits:
      cpu: "500m"
      memory: "512Mi"
```

---

## Key Technical Decisions

1. **Single Monolithic Chart** rather than umbrella chart
   - Ensures version consistency across all services
   - Simpler dependency management

2. **Database-per-Service Pattern**
   - 12 separate PostgreSQL databases
   - PostgreSQL init script creates all databases
   - Each service has isolated schema

3. **OCI Artifacts** instead of traditional Helm repository
   - Simpler installation (no `helm repo add` needed)
   - Native ACR support
   - Better integration with Azure ecosystem

4. **Infrastructure as Subcharts**
   - PostgreSQL, RabbitMQ, Redis via Bitnami
   - Can be disabled to use external services
   - Production-ready defaults

5. **Azure DevOps Pipelines** for publishing
   - Integrates with existing ADO setup
   - Uses Azure service connections
   - Supports automated and manual triggers

6. **Frontends Excluded**
   - Blazor WASM apps deployed to Azure Static Web Apps
   - Not part of Kubernetes deployment

7. **Agents Excluded**
   - Customer-hosted components
   - Not deployed via Helm chart

---

## Azure DevOps Setup Checklist

- [ ] Create Azure service connection in ADO
  - Name: `meridian-acr-connection`
  - Type: Azure Resource Manager
  - Scope: Resource group `meridian-rg`
  - Role: Must have `AcrPush` on ACR

- [ ] Add pipeline to Azure DevOps
  - Path: `deploy/kubernetes/helm/helm-publish-pipeline.yml`
  - Update `azureSubscription` parameter if using different name

- [ ] Set pipeline variables (optional)
  - `ACR_NAME` - Override default if needed

- [ ] Test pipeline
  - Run manually first
  - Verify chart appears in ACR
  - Test installation on dev cluster

---

## Troubleshooting

### Pipeline Fails: "ACR not found"

Check the `ACR_NAME` variable in pipeline:
```yaml
variables:
  ACR_NAME: 'meridianconsoleacr'
```

### Pipeline Fails: "Access denied"

Verify service connection has `AcrPush` role:
```bash
# Get service principal ID from service connection
# Then verify role assignment
az role assignment list \
  --assignee <service-principal-id> \
  --scope /subscriptions/<subscription-id>/resourceGroups/meridian-rg/providers/Microsoft.ContainerRegistry/registries/meridianconsoleacr
```

### Can't Pull Chart: "Not authenticated"

Re-authenticate to ACR:
```bash
az login
az acr login --name meridianconsoleacr
```

### Helm Dependency Update Fails

Download dependencies manually:
```bash
cd deploy/kubernetes/helm/meridian-console
helm repo add bitnami https://charts.bitnami.com/bitnami
helm repo update
helm dependency update
```

### Pods Failing: Database Connection Issues

Check PostgreSQL credentials:
```bash
kubectl get secret meridian-console-secret -n meridian-system -o jsonpath='{.data.postgresPassword}' | base64 -d
```

Verify connection from pod:
```bash
kubectl exec -it <gateway-pod> -n meridian-system -- /bin/bash
env | grep POSTGRES
```

### SignalR Console Not Working

Check session affinity:
```bash
kubectl get service console -n meridian-system -o yaml | grep sessionAffinity
# Should show: sessionAffinity: ClientIP
```

---

## Production Readiness Checklist

### Security

- [ ] Change all default passwords in `values.yaml`
- [ ] Use Kubernetes secrets for sensitive values
- [ ] Enable TLS/SSL on Ingress
- [ ] Configure network policies
- [ ] Restrict ACR access (consider private endpoint)
- [ ] Review RBAC permissions

### Performance

- [ ] Set appropriate resource requests/limits
- [ ] Configure HPA (Horizontal Pod Autoscaler) if needed
- [ ] Use external managed databases (Azure Database for PostgreSQL)
- [ ] Use external managed RabbitMQ (CloudAMQP, Azure Service Bus)
- [ ] Use external Redis (Azure Cache for Redis)

### Monitoring

- [ ] Configure Prometheus metrics scraping
- [ ] Set up alerts for critical services
- [ ] Configure log aggregation (Azure Monitor, ELK, etc.)
- [ ] Enable distributed tracing

### Backup

- [ ] Configure database backups
- [ ] Document disaster recovery procedures
- [ ] Test restore procedures

### Documentation

- [ ] Document custom values for your environment
- [ ] Create runbooks for common operations
- [ ] Document troubleshooting procedures

---

## Cost Optimization

### ACR Tier Comparison

| Tier | Cost/Month | Features |
|------|------------|----------|
| Basic | $5 | Good for dev/test |
| Standard | $20 | **Recommended for production** |
| Premium | $50 | Geo-replication, content trust |

**Current**: Basic tier ($5/month)
**Recommendation**: Upgrade to Standard for production

### Infrastructure Costs

**Using Subcharts (Default)**:
- PostgreSQL, RabbitMQ, Redis run in Kubernetes
- Cost = Kubernetes node resources
- Good for: Small deployments, dev/test

**Using Managed Services**:
- Azure Database for PostgreSQL: ~$50-200/month
- Azure Cache for Redis: ~$15-100/month
- RabbitMQ (CloudAMQP or self-hosted): Variable
- Good for: Production, high availability needs

---

## Next Steps

### Immediate (Setup)

1. **Create Azure service connection in ADO**
   - Project Settings → Service connections
   - Name: `meridian-acr-connection`
   - Grant `AcrPush` role

2. **Add pipeline to Azure DevOps**
   - Pipelines → New pipeline
   - Select repo → Existing YAML
   - Path: `deploy/kubernetes/helm/helm-publish-pipeline.yml`

3. **Run pipeline**
   - Trigger manually first time
   - Verify chart published to ACR

4. **Test installation**
   - Install on dev Kubernetes cluster
   - Verify all services start
   - Test connectivity

### Short-term (Pre-Production)

1. **Customize values** for your environment
2. **Set up monitoring and logging**
3. **Configure TLS/SSL** on Ingress
4. **Review security settings**
5. **Document runbooks**

### Long-term (Production)

1. **Migrate to managed databases** (PostgreSQL, Redis)
2. **Upgrade ACR to Standard tier**
3. **Implement GitOps** (ArgoCD, Flux)
4. **Set up disaster recovery**
5. **Configure autoscaling**

---

## Reference Documentation

### Created Files

- `deploy/kubernetes/helm/meridian-console/` - Helm chart
- `deploy/kubernetes/helm/helm-publish-pipeline.yml` - Azure DevOps pipeline
- `deploy/kubernetes/helm/setup-acr.sh` - Azure setup script
- `deploy/kubernetes/helm/ACR-SETUP.md` - Setup guide
- `deploy/kubernetes/helm/ACR-QUICKSTART.md` - Quick start
- `deploy/kubernetes/helm/PUBLISHING.md` - Publishing docs
- `deploy/kubernetes/helm/HELM-SETUP-SUMMARY.md` - This file

### External Resources

- [Helm Documentation](https://helm.sh/docs/)
- [Azure Container Registry](https://learn.microsoft.com/en-us/azure/container-registry/)
- [Azure DevOps Pipelines](https://learn.microsoft.com/en-us/azure/devops/pipelines/)
- [Bitnami Charts](https://github.com/bitnami/charts)

---

## Contact & Support

**Repository**: https://github.com/SandboxServers/MeridianConsole
**Azure Resource Group**: meridian-rg
**ACR**: meridianconsoleacr-etdvg4cthscffqdf.azurecr.io

---

**Document Version**: 1.0
**Last Updated**: 2025-12-27
**Created By**: Claude Code session

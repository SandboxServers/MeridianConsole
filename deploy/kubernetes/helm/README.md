# Meridian Console Kubernetes Deployment

This directory contains Helm charts and deployment resources for Meridian Console on Kubernetes.

## Directory Structure

```
helm/
├── meridian-console/          # Main Helm chart
│   ├── Chart.yaml            # Chart metadata and dependencies
│   ├── values.yaml           # Default configuration values
│   ├── README.md             # Comprehensive chart documentation
│   ├── .helmignore           # Files to ignore when packaging
│   └── templates/            # Kubernetes resource templates
│       ├── _helpers.tpl      # Template helpers
│       ├── NOTES.txt         # Post-install instructions
│       ├── configmap.yaml    # Shared configuration
│       ├── secret.yaml       # Shared secrets
│       ├── ingress.yaml      # Gateway ingress
│       ├── gateway/          # Gateway service templates
│       ├── identity/         # Identity service templates
│       ├── billing/          # Billing service templates
│       ├── servers/          # Servers service templates
│       ├── nodes/            # Nodes service templates
│       ├── tasks/            # Tasks service templates
│       ├── files/            # Files service templates
│       ├── mods/             # Mods service templates
│       ├── console/          # Console service templates
│       ├── notifications/    # Notifications service templates
│       ├── secrets/          # Secrets service templates
│       └── discord/          # Discord service templates
├── install.sh                # Installation helper script
├── values-production.yaml    # Production configuration template
├── QUICKSTART.md             # Quick start guide
└── README.md                 # This file
```

## Quick Start

See [QUICKSTART.md](QUICKSTART.md) for a step-by-step guide to get running quickly.

### One-Line Install (Development)

```bash
./install.sh
```

### Production Install

```bash
./install.sh -f values-production.yaml
```

## What's Included

### Microservices (13 total)

- **Gateway** - YARP reverse proxy (entry point)
- **Identity** - Authentication and authorization
- **Billing** - SaaS subscription management
- **Servers** - Game server lifecycle
- **Nodes** - Node inventory and health
- **Tasks** - Background job orchestration
- **Files** - File metadata and transfers
- **Mods** - Mod registry and versioning
- **Console** - Real-time console streaming (SignalR)
- **Notifications** - Email/Discord/webhook notifications
- **Secrets** - Secret storage and rotation
- **Discord** - Discord bot integration

### Infrastructure (Optional)

- **PostgreSQL 15** - Database (database-per-service pattern)
- **RabbitMQ** - Message broker
- **Redis** - Caching and session storage

By default, all infrastructure is deployed via Bitnami charts. For production, use external managed services.

## Not Included

The following components are deployed separately:

- **Frontends**: Panel, ShoppingCart, Scope → Azure Static Web Apps
- **Agents**: Customer-hosted agents → Run on customer hardware

## Installation Methods

### Method 1: Helper Script (Recommended)

```bash
# Development
./install.sh

# Production with custom values
./install.sh -f values-production.yaml

# Different namespace
./install.sh -n production

# Upgrade existing
./install.sh -u

# Dry-run
./install.sh -d
```

### Method 2: Direct Helm Commands

```bash
# Add Bitnami repo
helm repo add bitnami https://charts.bitnami.com/bitnami
helm repo update

# Update dependencies
helm dependency update meridian-console

# Install
helm install meridian meridian-console \
  --namespace meridian-system \
  --create-namespace

# Or upgrade if already installed
helm upgrade meridian meridian-console \
  --namespace meridian-system
```

## Configuration

### Key Configuration Files

- **values.yaml** - Default values (development-friendly)
- **values-production.yaml** - Production template (copy and customize)

### Essential Configuration

Before production deployment, customize:

1. **Secrets** (CRITICAL)
   - PostgreSQL password
   - RabbitMQ password
   - Redis password
   - JWT signing key

2. **Ingress**
   - Domain name
   - TLS certificates

3. **Resources**
   - CPU/memory limits
   - Replica counts

4. **Infrastructure**
   - External vs included
   - Connection strings

See [meridian-console/README.md](meridian-console/README.md) for all configuration options.

## Production Considerations

### Use External Infrastructure

For production, use managed services instead of in-cluster infrastructure:

```yaml
postgresql:
  enabled: false

rabbitmq:
  enabled: false

redis:
  enabled: false

secrets:
  postgresHost: "your-postgres.database.azure.com"
  rabbitmqHost: "your-rabbitmq.mq.example.com"
  redisHost: "your-redis.cache.windows.net"
```

### Security

- Change ALL default passwords
- Use external secret management (Vault, External Secrets Operator)
- Enable TLS everywhere
- Configure Network Policies
- Enable Pod Security Standards
- Use private container registry

### Observability

- Enable Prometheus ServiceMonitor
- Configure log aggregation (Fluentd, Loki)
- Set up distributed tracing (Jaeger, Tempo)
- Create Grafana dashboards

### High Availability

- Multiple replicas for critical services
- Pod Disruption Budgets
- Topology spread constraints
- Anti-affinity rules

### Backups

- PostgreSQL automated backups
- Disaster recovery plan
- Test recovery procedures

## Scaling

### Horizontal Pod Autoscaling

Example HPA for the Servers service:

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: meridian-servers
  namespace: meridian-system
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: meridian-servers
  minReplicas: 2
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
```

### Manual Scaling

```bash
kubectl scale deployment meridian-servers \
  --namespace meridian-system \
  --replicas=5
```

## Monitoring

### Check Deployment Status

```bash
kubectl get pods -n meridian-system
kubectl get deployments -n meridian-system
kubectl get services -n meridian-system
```

### View Logs

```bash
# Specific service
kubectl logs -n meridian-system -l app.kubernetes.io/component=gateway -f

# All services
kubectl logs -n meridian-system -l app.kubernetes.io/instance=meridian --all-containers -f
```

### Health Checks

All services expose `/healthz` endpoints for liveness/readiness probes.

## Upgrading

```bash
# Update dependencies
helm dependency update meridian-console

# Upgrade release
helm upgrade meridian meridian-console \
  --namespace meridian-system \
  --values custom-values.yaml

# Or use the script
./install.sh -u -f custom-values.yaml
```

## Uninstalling

```bash
helm uninstall meridian --namespace meridian-system

# Optional: delete namespace (removes all resources)
kubectl delete namespace meridian-system
```

## Troubleshooting

### Common Issues

1. **Pods stuck in Pending**
   - Check PVC provisioner
   - Check resource quotas
   - Check node capacity

2. **ImagePullBackOff**
   - Verify image names
   - Check registry credentials
   - Verify network connectivity

3. **CrashLoopBackOff**
   - Check application logs
   - Verify configuration
   - Check database connectivity

### Debug Commands

```bash
# Pod details
kubectl describe pod <pod-name> -n meridian-system

# Events
kubectl get events -n meridian-system --sort-by='.lastTimestamp'

# Resource usage
kubectl top pods -n meridian-system
kubectl top nodes

# Network debugging
kubectl run -it --rm debug --image=nicolaka/netshoot -n meridian-system -- bash
```

## Documentation

- [Chart README](meridian-console/README.md) - Comprehensive chart documentation
- [Quick Start](QUICKSTART.md) - Get running in minutes
- [Main Project](../../README.md) - Meridian Console overview

## Support

- Issues: https://github.com/SandboxServers/MeridianConsole/issues
- Discussions: https://github.com/SandboxServers/MeridianConsole/discussions

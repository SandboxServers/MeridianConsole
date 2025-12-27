# Meridian Console Helm Chart

A Helm chart for deploying Meridian Console - a modern, security-first game server control plane.

## Overview

Meridian Console is a multi-tenant SaaS platform that orchestrates game servers on customer-owned hardware via customer-hosted agents. This Helm chart deploys the control plane microservices to a Kubernetes cluster.

**Note**: This chart does NOT include customer-hosted agents or frontend applications (Panel, ShoppingCart, Scope), which are deployed separately.

## Prerequisites

- Kubernetes 1.24+
- Helm 3.8+
- PV provisioner support in the underlying infrastructure (for PostgreSQL, RabbitMQ, Redis persistence)
- Ingress controller (e.g., NGINX Ingress Controller, Traefik) - optional but recommended
- cert-manager (for TLS certificates) - optional but recommended

## Architecture

This chart deploys the following microservices:

| Service | Purpose | Database | Replicas |
|---------|---------|----------|----------|
| Gateway | YARP reverse proxy, single entry point | No | 2 |
| Identity | Authentication, authorization, JWT | Yes | 2 |
| Billing | SaaS subscriptions, metering | Yes | 1 |
| Servers | Game server lifecycle management | Yes | 2 |
| Nodes | Node inventory, health, capacity | Yes | 2 |
| Tasks | Background job orchestration | Yes | 2 |
| Files | File metadata and transfer orchestration | Yes | 1 |
| Mods | Mod registry and versioning | Yes | 1 |
| Console | Real-time console streaming (SignalR) | Yes | 2 |
| Notifications | Email, Discord, webhook notifications | Yes | 1 |
| Firewall | Port and policy management | Yes | 1 |
| Secrets | Secret storage and rotation | Yes | 2 |
| Discord | Discord bot integration | Yes | 1 |

### Infrastructure Dependencies

The chart includes optional infrastructure dependencies via Bitnami charts:

- **PostgreSQL 15** - Database for 12 services (database-per-service pattern)
- **RabbitMQ** - Message broker for asynchronous communication
- **Redis** - Caching and session storage

By default, all infrastructure dependencies are deployed. For production, you may want to use managed services (e.g., Azure Database for PostgreSQL, Amazon MQ, Azure Cache for Redis).

## Installation

### Quick Start

Install with default values (suitable for development/testing):

```bash
# Add the Bitnami repository (for infrastructure dependencies)
helm repo add bitnami https://charts.bitnami.com/bitnami
helm repo update

# Install the chart
helm install meridian ./meridian-console \
  --namespace meridian-system \
  --create-namespace
```

### Production Installation

For production, you MUST override the default passwords and configure external dependencies:

```bash
helm install meridian ./meridian-console \
  --namespace meridian-system \
  --create-namespace \
  --set secrets.postgresPassword="<secure-password>" \
  --set secrets.rabbitmqPassword="<secure-password>" \
  --set secrets.redisPassword="<secure-password>" \
  --set secrets.jwtSigningKey="<secure-256-bit-key>" \
  --set gateway.ingress.hosts[0].host="api.yourdomain.com" \
  --set gateway.ingress.tls[0].hosts[0]="api.yourdomain.com" \
  --set gateway.ingress.tls[0].secretName="meridian-tls"
```

### Using External Infrastructure

To use external PostgreSQL, RabbitMQ, and Redis:

```bash
helm install meridian ./meridian-console \
  --namespace meridian-system \
  --create-namespace \
  --set postgresql.enabled=false \
  --set rabbitmq.enabled=false \
  --set redis.enabled=false \
  --set secrets.postgresHost="your-postgres-host.database.azure.com" \
  --set secrets.postgresPassword="<password>" \
  --set secrets.rabbitmqHost="your-rabbitmq-host.mq.azure.com" \
  --set secrets.rabbitmqPassword="<password>" \
  --set secrets.redisHost="your-redis-host.redis.cache.windows.net" \
  --set secrets.redisPassword="<password>"
```

## Configuration

### Global Configuration

| Parameter | Description | Default |
|-----------|-------------|---------|
| `global.imageRegistry` | Global container image registry | `""` |
| `global.imagePullSecrets` | Global image pull secrets | `[]` |
| `global.storageClass` | Global storage class for PVCs | `""` |

### Common Service Configuration

All services support the following common parameters:

| Parameter | Description | Default |
|-----------|-------------|---------|
| `<service>.enabled` | Enable/disable the service | `true` |
| `<service>.replicaCount` | Number of replicas | Varies (1-2) |
| `<service>.image.repository` | Container image repository | `meridian/<service>` |
| `<service>.image.tag` | Container image tag | `latest` |
| `<service>.image.pullPolicy` | Image pull policy | `IfNotPresent` |
| `<service>.service.type` | Kubernetes service type | `ClusterIP` |
| `<service>.service.port` | Service port | `80` |
| `<service>.service.targetPort` | Container target port | `8080` |
| `<service>.resources` | CPU/memory resource requests and limits | See `values.yaml` |

### Gateway Configuration

| Parameter | Description | Default |
|-----------|-------------|---------|
| `gateway.ingress.enabled` | Enable Ingress for Gateway | `true` |
| `gateway.ingress.className` | Ingress class name | `nginx` |
| `gateway.ingress.annotations` | Ingress annotations | See `values.yaml` |
| `gateway.ingress.hosts` | Ingress hosts configuration | `[{host: api.meridian.example.com}]` |
| `gateway.ingress.tls` | Ingress TLS configuration | See `values.yaml` |

### PostgreSQL Configuration

| Parameter | Description | Default |
|-----------|-------------|---------|
| `postgresql.enabled` | Deploy PostgreSQL | `true` |
| `postgresql.auth.username` | PostgreSQL username | `dhadgar` |
| `postgresql.auth.password` | PostgreSQL password | `dhadgar-change-me` |
| `postgresql.auth.database` | Default database name | `dhadgar_platform` |
| `postgresql.primary.persistence.size` | PostgreSQL PVC size | `20Gi` |

See [Bitnami PostgreSQL chart](https://github.com/bitnami/charts/tree/main/bitnami/postgresql) for all options.

### RabbitMQ Configuration

| Parameter | Description | Default |
|-----------|-------------|---------|
| `rabbitmq.enabled` | Deploy RabbitMQ | `true` |
| `rabbitmq.auth.username` | RabbitMQ username | `dhadgar` |
| `rabbitmq.auth.password` | RabbitMQ password | `dhadgar-change-me` |
| `rabbitmq.persistence.size` | RabbitMQ PVC size | `10Gi` |

See [Bitnami RabbitMQ chart](https://github.com/bitnami/charts/tree/main/bitnami/rabbitmq) for all options.

### Redis Configuration

| Parameter | Description | Default |
|-----------|-------------|---------|
| `redis.enabled` | Deploy Redis | `true` |
| `redis.auth.password` | Redis password | `redis-change-me` |
| `redis.master.persistence.size` | Redis master PVC size | `8Gi` |
| `redis.replica.replicaCount` | Number of Redis replicas | `2` |

See [Bitnami Redis chart](https://github.com/bitnami/charts/tree/main/bitnami/redis) for all options.

### Secrets Configuration

| Parameter | Description | Default |
|-----------|-------------|---------|
| `secrets.postgresHost` | PostgreSQL host | `{{ .Release.Name }}-postgresql` |
| `secrets.postgresPassword` | PostgreSQL password | `dhadgar-change-me` |
| `secrets.rabbitmqHost` | RabbitMQ host | `{{ .Release.Name }}-rabbitmq` |
| `secrets.rabbitmqPassword` | RabbitMQ password | `dhadgar-change-me` |
| `secrets.redisHost` | Redis host | `{{ .Release.Name }}-redis-master` |
| `secrets.redisPassword` | Redis password | `redis-change-me` |
| `secrets.jwtSigningKey` | JWT signing key (CRITICAL) | `dev-only-jwt-key-change-me-in-production` |
| `secrets.jwtIssuer` | JWT issuer URL | `https://meridian.example.com` |
| `secrets.jwtAudience` | JWT audience | `meridian-api` |

**⚠️ SECURITY WARNING**: The default secrets are for development only. You MUST change these for production!

## Upgrading

### Update Dependencies

After modifying infrastructure dependencies in `Chart.yaml`:

```bash
helm dependency update ./meridian-console
```

### Upgrade Release

```bash
helm upgrade meridian ./meridian-console \
  --namespace meridian-system \
  --values custom-values.yaml
```

### Rolling Back

```bash
helm rollback meridian [REVISION] --namespace meridian-system
```

## Uninstalling

```bash
helm uninstall meridian --namespace meridian-system
```

**Note**: This will delete all resources including PersistentVolumeClaims (data will be lost unless you have backups).

To preserve PVCs, manually delete the Helm release resources but keep PVCs:

```bash
kubectl delete deployment,service,ingress -n meridian-system -l app.kubernetes.io/instance=meridian
```

## Troubleshooting

### Check Pod Status

```bash
kubectl get pods -n meridian-system
kubectl describe pod <pod-name> -n meridian-system
kubectl logs <pod-name> -n meridian-system
```

### Check Service Endpoints

```bash
kubectl get svc -n meridian-system
kubectl get endpoints -n meridian-system
```

### Database Connection Issues

Verify PostgreSQL is running and accessible:

```bash
kubectl run postgresql-client --rm --tty -i --restart='Never' \
  --namespace meridian-system \
  --image docker.io/bitnami/postgresql:15 \
  --env="PGPASSWORD=<password>" \
  --command -- psql --host meridian-postgresql \
  -U dhadgar -d dhadgar_platform -p 5432
```

### RabbitMQ Connection Issues

Check RabbitMQ management UI:

```bash
kubectl port-forward -n meridian-system svc/meridian-rabbitmq 15672:15672
# Visit http://localhost:15672
```

### Common Issues

1. **Pods stuck in `Pending` state**: Check PVC provisioning
   ```bash
   kubectl get pvc -n meridian-system
   ```

2. **ImagePullBackOff errors**: Verify image names and registry credentials
   ```bash
   kubectl describe pod <pod-name> -n meridian-system
   ```

3. **CrashLoopBackOff**: Check application logs
   ```bash
   kubectl logs <pod-name> -n meridian-system --previous
   ```

## Security Considerations

### Production Checklist

- [ ] Change all default passwords (PostgreSQL, RabbitMQ, Redis)
- [ ] Generate secure JWT signing key (256-bit)
- [ ] Configure TLS/HTTPS for Ingress (use cert-manager)
- [ ] Use Kubernetes Secrets or external secret management (Vault, External Secrets Operator)
- [ ] Enable Pod Security Standards (PSS)
- [ ] Configure Network Policies to isolate services
- [ ] Enable audit logging
- [ ] Use dedicated service accounts with RBAC
- [ ] Regularly update container images
- [ ] Enable resource quotas and limits
- [ ] Configure backup strategy for databases

### External Secret Management

For production, use an external secret management solution:

**Example with Kubernetes External Secrets Operator:**

```yaml
apiVersion: external-secrets.io/v1beta1
kind: ExternalSecret
metadata:
  name: meridian-secrets
  namespace: meridian-system
spec:
  secretStoreRef:
    name: azure-keyvault
    kind: SecretStore
  target:
    name: meridian-console-secrets
  data:
    - secretKey: postgres-password
      remoteRef:
        key: postgres-password
    - secretKey: jwt-signing-key
      remoteRef:
        key: jwt-signing-key
```

## Architecture Decisions

### Database-per-Service Pattern

Each microservice has its own PostgreSQL database to ensure:
- Service independence
- Schema isolation
- Independent scaling
- Blast radius containment

### SignalR Sticky Sessions

The Console service uses SignalR for real-time WebSocket connections, requiring session affinity (`sessionAffinity: ClientIP`).

### Gateway as Single Entry Point

All external traffic flows through the Gateway (YARP reverse proxy) for:
- Centralized authentication
- Rate limiting
- Request routing
- TLS termination

## Observability

### Metrics (Prometheus)

Enable ServiceMonitor for Prometheus Operator:

```yaml
serviceMonitor:
  enabled: true
  interval: 30s
```

### Logs

Services log to stdout in JSON format, compatible with:
- Fluentd
- Fluent Bit
- Promtail (Loki)
- Azure Monitor

### Distributed Tracing

(Future) OpenTelemetry instrumentation for distributed tracing with Jaeger or Tempo.

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](../../CONTRIBUTING.md).

## License

See [LICENSE](../../LICENSE).

## Support

- Issues: https://github.com/SandboxServers/MeridianConsole/issues
- Documentation: https://scope.meridian.local (when deployed)

# Meridian Console Helm Chart - Quick Start

This guide will get you up and running with Meridian Console on Kubernetes in minutes.

## Prerequisites

- Kubernetes cluster (1.24+) - Minikube, kind, or cloud provider
- Helm 3.8+
- kubectl configured to access your cluster

## TL;DR - Development Install

```bash
# Navigate to the helm directory
cd deploy/kubernetes/helm

# Add Bitnami repository
helm repo add bitnami https://charts.bitnami.com/bitnami
helm repo update

# Update dependencies
helm dependency update meridian-console

# Install the chart
helm install meridian meridian-console \
  --namespace meridian-system \
  --create-namespace

# Watch the deployment
kubectl get pods -n meridian-system -w
```

That's it! The control plane is now running with all infrastructure included.

## Accessing the Gateway

### Via Port Forward (Development)

```bash
kubectl port-forward -n meridian-system svc/meridian-gateway 8080:80
```

Then visit: http://localhost:8080

### Via Ingress (Production)

If you configured Ingress, access via the configured domain:

```bash
# Get the Ingress status
kubectl get ingress -n meridian-system

# Access your domain
curl https://api.yourdomain.com/healthz
```

## Checking Service Health

### All Services

```bash
kubectl get pods -n meridian-system
```

### Specific Service Logs

```bash
# Gateway logs
kubectl logs -n meridian-system -l app.kubernetes.io/component=gateway -f

# Identity service logs
kubectl logs -n meridian-system -l app.kubernetes.io/component=identity -f

# All logs
kubectl logs -n meridian-system -l app.kubernetes.io/instance=meridian --all-containers -f
```

## Accessing Infrastructure

### PostgreSQL

```bash
kubectl run postgresql-client --rm --tty -i --restart='Never' \
  --namespace meridian-system \
  --image docker.io/bitnami/postgresql:15 \
  --env="PGPASSWORD=dhadgar-change-me" \
  --command -- psql --host meridian-postgresql \
  -U dhadgar -d dhadgar_platform -p 5432
```

### RabbitMQ Management UI

```bash
kubectl port-forward -n meridian-system svc/meridian-rabbitmq 15672:15672
```

Visit: http://localhost:15672 (username: `dhadgar`, password: `dhadgar-change-me`)

### Redis

```bash
kubectl run redis-client --rm --tty -i --restart='Never' \
  --namespace meridian-system \
  --image docker.io/bitnami/redis:7.2 \
  --env REDIS_PASSWORD=redis-change-me \
  --command -- redis-cli -h meridian-redis-master -a $REDIS_PASSWORD
```

## Configuration

### Using Custom Values

Create a `custom-values.yaml`:

```yaml
gateway:
  ingress:
    hosts:
      - host: api.mydomain.com
        paths:
          - path: /
            pathType: Prefix

secrets:
  postgresPassword: "my-secure-password"
  rabbitmqPassword: "my-secure-password"
  redisPassword: "my-secure-password"
```

Install with custom values:

```bash
helm install meridian meridian-console \
  --namespace meridian-system \
  --create-namespace \
  --values custom-values.yaml
```

### Using --set Flags

```bash
helm install meridian meridian-console \
  --namespace meridian-system \
  --create-namespace \
  --set gateway.ingress.hosts[0].host="api.mydomain.com" \
  --set secrets.postgresPassword="secure-password"
```

## Scaling Services

### Scale a Specific Service

```bash
# Scale the Servers service to 5 replicas
kubectl scale deployment meridian-servers \
  --namespace meridian-system \
  --replicas=5

# Or via Helm upgrade
helm upgrade meridian meridian-console \
  --namespace meridian-system \
  --set servers.replicaCount=5
```

## Upgrading

```bash
# Update dependencies
helm dependency update meridian-console

# Upgrade the release
helm upgrade meridian meridian-console \
  --namespace meridian-system \
  --values custom-values.yaml
```

## Uninstalling

```bash
# Delete the release (this will delete all resources including PVCs!)
helm uninstall meridian --namespace meridian-system

# Delete the namespace (optional)
kubectl delete namespace meridian-system
```

## Common Tasks

### View Release Notes

```bash
helm get notes meridian -n meridian-system
```

### View Current Values

```bash
helm get values meridian -n meridian-system
```

### View All Resources

```bash
kubectl get all -n meridian-system -l app.kubernetes.io/instance=meridian
```

### Check Service Endpoints

```bash
kubectl get endpoints -n meridian-system
```

### Describe a Pod

```bash
kubectl describe pod <pod-name> -n meridian-system
```

## Troubleshooting

### Pods Not Starting

```bash
# Check pod status
kubectl get pods -n meridian-system

# Describe problematic pod
kubectl describe pod <pod-name> -n meridian-system

# Check logs
kubectl logs <pod-name> -n meridian-system

# Check previous logs (if pod restarted)
kubectl logs <pod-name> -n meridian-system --previous
```

### Database Connection Issues

```bash
# Check if PostgreSQL is running
kubectl get pods -n meridian-system -l app.kubernetes.io/name=postgresql

# Check PostgreSQL logs
kubectl logs -n meridian-system -l app.kubernetes.io/name=postgresql

# Test connection from a pod
kubectl exec -it <service-pod> -n meridian-system -- env | grep POSTGRES
```

### Image Pull Errors

```bash
# Check if images exist and are accessible
kubectl describe pod <pod-name> -n meridian-system | grep -A5 "Events:"

# For private registries, create image pull secret
kubectl create secret docker-registry acr-credentials \
  --namespace meridian-system \
  --docker-server=your-registry.azurecr.io \
  --docker-username=<username> \
  --docker-password=<password>
```

## Production Checklist

Before going to production:

- [ ] Change all default passwords in `values-production.yaml`
- [ ] Configure proper Ingress with TLS
- [ ] Set up external PostgreSQL, RabbitMQ, Redis (recommended)
- [ ] Configure resource limits based on load testing
- [ ] Set up monitoring (Prometheus/Grafana)
- [ ] Configure log aggregation
- [ ] Set up backups for PostgreSQL
- [ ] Review security settings (NetworkPolicies, PodSecurityStandards)
- [ ] Configure autoscaling (HPA) for high-traffic services
- [ ] Set up disaster recovery plan

## Next Steps

1. Configure frontends (Panel, ShoppingCart, Scope) to point to your Gateway
2. Enroll customer-hosted agents
3. Set up monitoring and alerting
4. Configure backups
5. Load test and tune resource limits

## Support

- Full Documentation: [README.md](meridian-console/README.md)
- Issues: https://github.com/SandboxServers/MeridianConsole/issues

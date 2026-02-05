# Docker Compose Development Environment

This directory contains Docker Compose configurations for local development infrastructure.

## Aspire vs Docker Compose

The recommended approach for local development is **.NET Aspire** (see below). Docker Compose is maintained as an alternative for specific scenarios:

| Use Case | Recommendation |
|----------|----------------|
| **Local development** | Aspire (`dotnet run --project src/Dhadgar.AppHost`) - better DX, Aspire Dashboard for traces/metrics/logs |
| **CI/CD pipelines** | Docker Compose - portable, no .NET required on runner |
| **Production simulation** | Docker Compose - matches production config more closely |
| **Aspire unavailable** | Docker Compose - fallback when Aspire workload can't be installed |

**Running with Aspire:**
```bash
# Start all services with infrastructure auto-wired
dotnet run --project src/Dhadgar.AppHost

# Opens Aspire Dashboard at https://localhost:17178
# - All services start automatically with proper dependencies
# - Connection strings injected automatically
# - Integrated tracing, metrics, and logs
```

## Quick Start (Docker Compose)

```bash
# Start all infrastructure services
docker compose -f docker-compose.dev.yml up -d

# Check status
docker ps --filter "name=dhadgar-dev"

# View logs
docker compose -f docker-compose.dev.yml logs -f

# Stop all services
docker compose -f docker-compose.dev.yml down
```

## Services Included

### Core Infrastructure
- **PostgreSQL 16** - Port 5432 (credentials: dhadgar/dhadgar, DB: dhadgar_platform)
- **RabbitMQ 3 Management** - Ports 5672 (AMQP), 15672 (Management UI)
- **Redis 7** - Port 6379 (password: dhadgar)

### Observability Stack
- **Grafana** - Port 3000 (credentials: admin/admin)
- **Prometheus** - Port 9090
- **Loki** - Port 3100 (log aggregation)
- **OpenTelemetry Collector** - Ports 4317 (gRPC), 4318 (HTTP), 8889 (Prometheus metrics)

## Configuration Files

- `docker-compose.dev.yml` - Main development stack with observability
- `docker-compose.minimal.yml` - Core infrastructure only (faster startup)
- `otel-collector-config.yml` - OpenTelemetry Collector configuration
- `loki-config.yml` - Loki log aggregation configuration
- `prometheus.yml` - Prometheus scrape configuration

## Troubleshooting

### Slow Docker Pulls

If image downloads are slow or hang:

```bash
# Clean up Docker cache
docker system prune -f

# Pull images individually
docker pull postgres:16
docker pull rabbitmq:3-management
docker pull redis:7
docker pull grafana/grafana:latest
```

### Port Conflicts

Check if ports are already in use:

```bash
# Windows
netstat -ano | findstr "5432 5672 6379 3000"

# Linux/Mac
lsof -i :5432,5672,6379,3000
```

### Reset Everything

```bash
# Stop and remove all containers, networks, volumes
docker compose -f docker-compose.dev.yml down -v

# Start fresh
docker compose -f docker-compose.dev.yml up -d
```

## Grafana Note

The compose file uses `grafana/grafana:latest` instead of a pinned version. This is intentional to avoid Docker Hub download issues with specific layer versions while maintaining recent stable features. Grafana maintains backward compatibility well, so using `:latest` is safe for local development.

## Environment Variables

Override defaults by creating a `.env` file in this directory:

```env
POSTGRES_USER=myuser
POSTGRES_PASSWORD=mypassword
POSTGRES_DB=mydb
RABBITMQ_DEFAULT_USER=myuser
RABBITMQ_DEFAULT_PASS=mypassword
REDIS_PASSWORD=mypassword
GRAFANA_ADMIN_USER=myadmin
GRAFANA_ADMIN_PASSWORD=myadminpass
```

## Integration with Gateway Service

To enable full observability in the Gateway service, add to `src/Dhadgar.Gateway/appsettings.Development.json`:

```json
{
  "OpenTelemetry": {
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

This will send traces, metrics, and logs to the OTLP Collector, which forwards to Prometheus and Loki for visualization in Grafana.

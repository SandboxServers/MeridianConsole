# Gateway Operations Runbook

This runbook covers operational procedures for the Meridian Console Gateway service.

## Health Check Endpoints

### Available Endpoints

| Endpoint | Purpose | Expected Response |
|----------|---------|-------------------|
| `/healthz` | Basic health | 200 OK with service info |
| `/livez` | Kubernetes liveness | 200 OK |
| `/readyz` | Kubernetes readiness | 200 OK with dependency status |

### Health Check Details

**`/healthz`** - Returns basic health information:
```json
{
  "status": "Healthy",
  "service": "Dhadgar.Gateway",
  "version": "1.0.0",
  "timestamp": "2026-01-16T12:00:00Z"
}
```

**`/readyz`** - Checks critical dependencies:
- JWT Authority reachability
- YARP backend availability
- Configuration validity

## Readiness Requirements

The Gateway reports as ready when:
1. All configured clusters have at least one healthy destination
2. JWT validation configuration is valid
3. CORS configuration is valid (origins required in production)

## Diagnostic Endpoints

### Service Health (`/diagnostics/services`)

Check health of all backend services:
```bash
curl http://gateway:5000/diagnostics/services | jq
```

Response includes:
- Health status per service
- Response time (ms)
- Service URL
- Summary of healthy/unhealthy services

### Route Configuration (`/diagnostics/routes`)

List all configured routes:
```bash
curl http://gateway:5000/diagnostics/routes | jq
```

Response includes:
- Route ID
- Path pattern
- Target cluster
- Authorization policy
- Rate limiter policy
- Order priority

### Cluster Status (`/diagnostics/clusters`)

Check YARP cluster health:
```bash
curl http://gateway:5000/diagnostics/clusters | jq
```

Response includes:
- Cluster ID
- Available destinations
- Total destinations
- Health status

## CLI Commands

Use the `dhadgar` CLI for Gateway operations:

```bash
# Check Gateway and all services health
dhadgar gateway health

# List all backend services with health status
dhadgar gateway services

# List all configured routes
dhadgar gateway routes

# List all YARP clusters
dhadgar gateway clusters
```

## Cloudflare IP Management

### Current Configuration Location

Cloudflare IP ranges are configured in `appsettings.json`:

```json
"Cloudflare": {
  "IPv4Ranges": [
    "173.245.48.0/20",
    "103.21.244.0/22",
    "..."
  ],
  "IPv6Ranges": [
    "2400:cb00::/32",
    "..."
  ]
}
```

### Updating Cloudflare IPs

1. **Get current IPs from Cloudflare:**
   ```bash
   curl https://www.cloudflare.com/ips-v4
   curl https://www.cloudflare.com/ips-v6
   ```

2. **Update `appsettings.json`** with new ranges

3. **Deploy new configuration:**
   - Rolling restart for config update
   - No downtime required

4. **Verify after update:**
   ```bash
   dhadgar gateway health
   ```

### Scheduled Updates

Cloudflare IP ranges change infrequently. Recommended: Review quarterly or subscribe to Cloudflare changelog.

## Session Affinity

### Configuration

The Console cluster uses cookie-based session affinity for SignalR connections:

```json
"console": {
  "SessionAffinity": {
    "Enabled": true,
    "Policy": "Cookie",
    "AffinityCookieName": ".Dhadgar.Console.Affinity"
  }
}
```

### Considerations

- SignalR/WebSocket connections require sticky sessions
- Cookie is set on first request
- Client must support cookies for proper session handling

### Troubleshooting Session Issues

1. **Check cookie is being set:**
   ```bash
   curl -c cookies.txt -v http://gateway:5000/hubs/console/negotiate
   ```

2. **Verify cookie presence on subsequent requests:**
   ```bash
   curl -b cookies.txt http://gateway:5000/hubs/console/...
   ```

## Circuit Breaker

### How It Works

The circuit breaker protects against cascading failures:

| State | Description |
|-------|-------------|
| **Closed** | Normal operation, requests pass through |
| **Open** | Backend failing, requests blocked (503) |
| **Half-Open** | Testing recovery, limited requests allowed |

### Configuration

```json
"CircuitBreaker": {
  "FailureThreshold": 5,
  "SuccessThreshold": 2,
  "OpenDurationSeconds": 30,
  "FailureStatusCodes": [500, 502, 503, 504]
}
```

### Circuit Open Response

When circuit is open, clients receive:
```json
{
  "type": "https://meridian.console/errors/circuit-open",
  "title": "Service Temporarily Unavailable",
  "status": 503,
  "detail": "The {cluster} service is temporarily unavailable. Please retry later."
}
```

### Monitoring Circuit State

Monitor application logs for circuit state changes:
```
Circuit opened for cluster {ClusterId} after {FailureCount} failures
Circuit for cluster {ClusterId} transitioning to half-open
Circuit closed for cluster {ClusterId} after {SuccessCount} successful requests
```

### Manual Recovery

If a circuit is stuck open:
1. Fix the underlying backend issue
2. Wait for `OpenDurationSeconds` to elapse
3. Circuit will transition to half-open automatically
4. Successful requests will close the circuit

## Rate Limiting

### Rate Limit Headers

Responses include rate limit information:
- `X-RateLimit-Limit`: Maximum requests per window
- `X-RateLimit-Remaining`: Requests remaining
- `X-RateLimit-Reset`: Window reset time

### 429 Response

When rate limited:
```json
{
  "type": "https://tools.ietf.org/html/rfc6585#section-4",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Try again in {seconds} seconds."
}
```

### Rate Limit Bypass (Emergency)

In emergencies, rate limits can be temporarily disabled:
1. Set `RateLimiting:Enabled: false` in config
2. Deploy configuration update
3. **Re-enable immediately after emergency**

## WebSocket/SignalR Considerations

### Connection Requirements

1. Session affinity enabled for Console cluster
2. WebSocket upgrade allowed through YARP
3. Timeouts configured appropriately

### Configuration

```json
"console": {
  "HttpRequest": {
    "Timeout": "00:05:00"
  }
}
```

### Troubleshooting WebSocket Issues

1. **Check WebSocket negotiation:**
   ```bash
   curl -v "http://gateway:5000/hubs/console/negotiate?negotiateVersion=1"
   ```

2. **Verify WebSocket upgrade:**
   - Response should include `Upgrade: websocket` header
   - Status should be 101 Switching Protocols

3. **Check for timeout issues:**
   - Long-running connections may hit proxy timeouts
   - Adjust `HttpRequest.Timeout` as needed

## Performance Monitoring

### Key Metrics

| Metric | Description | Alert Threshold |
|--------|-------------|-----------------|
| `http_request_duration_seconds` | Request latency | P99 > 1s |
| `http_requests_total` | Total requests | Anomaly detection |
| `yarp_proxy_requests_started_total` | Proxied requests | Per cluster |
| `rate_limiter_rejected_total` | Rate limit hits | Spike detection |

### Prometheus Queries

**Request latency by route:**
```promql
histogram_quantile(0.99, sum(rate(http_request_duration_seconds_bucket{service="gateway"}[5m])) by (le, route))
```

**Rate limit rejections:**
```promql
sum(rate(rate_limiter_rejected_total{service="gateway"}[5m])) by (policy)
```

## Incident Response

### Service Degradation

1. Check `/diagnostics/services` for unhealthy backends
2. Review circuit breaker state in logs
3. Check rate limiter metrics for spikes
4. Verify Cloudflare is passing requests

### Complete Outage

1. Verify Gateway pod is running: `kubectl get pods -l app=gateway`
2. Check pod logs: `kubectl logs -l app=gateway`
3. Verify network policies allow traffic
4. Check YARP configuration validity

### Security Incident

1. Enable verbose logging
2. Review request logs for anomalies
3. Check rate limiter rejections
4. Consider temporary IP blocking via Cloudflare

## Backup and Recovery

### Configuration Backup

Gateway configuration is stored in:
- `appsettings.json` (git-controlled)
- Environment variables (Kubernetes secrets)
- Cloudflare IP ranges (in config)

### Recovery Steps

1. Redeploy from known-good commit
2. Verify configuration with `dhadgar gateway routes`
3. Test health with `dhadgar gateway health`
4. Monitor for errors in logs

## Maintenance Procedures

### Rolling Restart

```bash
kubectl rollout restart deployment/gateway
kubectl rollout status deployment/gateway
```

### Configuration Update

1. Update `appsettings.json` or environment variables
2. Deploy new configuration
3. Verify with `dhadgar gateway health`
4. Monitor for errors

### Scaling

```bash
# Scale up
kubectl scale deployment/gateway --replicas=3

# Verify
kubectl get pods -l app=gateway
```

Gateway is stateless and scales horizontally. Circuit breaker state is per-instance but recovers automatically.

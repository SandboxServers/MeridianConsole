# ADR-0003: Use YARP for API Gateway

## Status

Accepted

## Context

The platform needs an API gateway to:
- Provide a single entry point for all client requests
- Route requests to appropriate backend services
- Handle cross-cutting concerns (authentication, rate limiting, CORS)
- Support WebSocket connections for real-time features (Console service)

Options considered:
1. **YARP (Yet Another Reverse Proxy)** - Microsoft's .NET-native reverse proxy
2. **Ocelot** - Popular .NET API gateway, older architecture
3. **Kong/Traefik** - Standalone gateway products, separate deployment
4. **Envoy** - High-performance proxy, requires sidecar pattern

## Decision

Use YARP as the API gateway, hosted in `Dhadgar.Gateway`.

Key configuration:
- Routes defined in `appsettings.json` under `ReverseProxy`
- Active health checks for backend services
- Session affinity for SignalR connections
- Rate limiting via ASP.NET Core rate limiting middleware
- CORS policies for web clients

## Consequences

### Positive

- Native .NET integration, familiar programming model
- Configuration-driven routing with runtime updates
- Built-in load balancing and health checks
- Same observability (OpenTelemetry) as other services
- No additional infrastructure to deploy

### Negative

- Less feature-rich than dedicated gateway products
- No built-in API management features (documentation, versioning)
- Must implement some patterns manually (circuit breaker done via Polly)

### Neutral

- Gateway is a .NET service like any other, deployed the same way
- Scalar UI provides API documentation at `/scalar/v1`

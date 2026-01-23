# Gateway Service

API Gateway and single public entry point for all traffic.

## Tech Stack
- ASP.NET Core with YARP reverse proxy
- Rate limiting (fixed window + sliding window)
- OpenTelemetry instrumentation

## Port
5000

## Key Files
- `appsettings.json` - Route configuration under `ReverseProxy` section
- `Middleware/` - Security headers, request enrichment, CORS

## Dependencies
- Dhadgar.ServiceDefaults (middleware, observability)

## Notes
- Stateless - no database
- All backend services are routed through here
- Rate limit policies: Global, Auth, Tenant, Agent

# Gateway Authentication

This document describes the authentication and authorization architecture for the Meridian Console Gateway service.

## Overview

The Gateway acts as the single public entry point for all API requests, handling:
- JWT validation against the Identity service
- Authorization policy enforcement
- Rate limiting by authentication tier
- Request enrichment with trusted claims

## JWT Validation

### Authority-Based Validation

The Gateway uses Authority-based JWT validation, meaning it fetches JWKS (JSON Web Key Set) from the Identity service to validate tokens:

```json
"Authentication": {
  "Jwt": {
    "Authority": "http://identity:5010",
    "Audience": "meridian-api",
    "RequireHttpsMetadata": true
  }
}
```

**Key Benefits:**
- No shared signing keys between services
- Automatic key rotation via JWKS endpoint
- Standard OIDC/OAuth 2.0 compliance

### Token Flow

1. Client obtains JWT from Identity service (`/api/v1/identity/token`)
2. Client includes JWT in `Authorization: Bearer <token>` header
3. Gateway validates signature via JWKS from Identity service
4. Gateway extracts claims and injects trusted headers
5. Backend services receive enriched request with validated claims

## Authorization Policies

### Policy Definitions

| Policy | Description | Usage |
|--------|-------------|-------|
| `Anonymous` | No authentication required | Health checks, public endpoints |
| `TenantScoped` | Requires valid JWT with `org_id` claim | Most API routes |
| `Agent` | Requires valid JWT with `client_type: agent` | Agent-specific endpoints |
| `DenyAll` | Always denies access | Internal endpoint blocking |

### Route Authorization

Routes are configured with authorization policies in `appsettings.json`:

```json
"identity-route": {
  "AuthorizationPolicy": "TenantScoped",
  "RateLimiterPolicy": "Auth",
  "Order": 10
}
```

### Internal Endpoint Blocking

Internal endpoints (e.g., `/api/v1/identity/internal/*`) are blocked from external access using:
1. A high-priority route (Order: 1) matching internal paths
2. `DenyAll` policy that always returns 403 Forbidden

## Request Enrichment

The `RequestEnrichmentMiddleware` extracts claims from validated JWTs and injects trusted headers:

| Claim | Header | Description |
|-------|--------|-------------|
| `org_id` | `X-Tenant-Id` | Organization/tenant identifier |
| `sub` | `X-User-Id` | User identifier |
| `client_type` | `X-Client-Type` | Client type (user, agent) |
| `agent_id` | `X-Agent-Id` | Agent identifier (agents only) |
| `role` | `X-Roles` | Comma-separated roles |

### Security Headers

The following headers are **stripped from incoming requests** to prevent spoofing:
- `X-Tenant-Id`
- `X-User-Id`
- `X-Client-Type`
- `X-Agent-Id`
- `X-Roles`
- `X-Real-IP`

Backend services should **only** trust these headers when set by the Gateway, never from direct requests.

## Rate Limiting

### Rate Limiting Tiers

| Policy | Target | Limits | Partitioning |
|--------|--------|--------|--------------|
| `Global` | All requests | 1000/min | Per IP |
| `Auth` | Authentication routes | 30/min | Per IP (/64 for IPv6) |
| `PerTenant` | Tenant API routes | 600/min | Per Tenant ID |
| `PerAgent` | Agent routes | 300/min | Per Agent ID |

### IPv6 Rate Limiting

For IPv6 addresses, rate limiting uses the /64 prefix (network portion) instead of the full address. This prevents attackers from bypassing rate limits by rotating through addresses within their allocation.

```
2001:db8:85a3:1234:5678:9abc:def0:1234 → 2001:db8:85a3:1234::
2001:db8:85a3:1234:ffff:ffff:ffff:ffff → 2001:db8:85a3:1234::
```

Both addresses above would count toward the same rate limit bucket.

## Configuration for Production

### Required Configuration

```json
{
  "Authentication": {
    "Jwt": {
      "Authority": "https://identity.yourdomain.com",
      "Audience": "meridian-api",
      "RequireHttpsMetadata": true
    }
  },
  "Cors": {
    "AllowedOrigins": [
      "https://panel.yourdomain.com",
      "https://yourdomain.com"
    ]
  }
}
```

### Environment Variables

| Variable | Description |
|----------|-------------|
| `Authentication__Jwt__Authority` | Identity service URL |
| `Authentication__Jwt__Audience` | Expected audience claim |
| `Cors__AllowedOrigins__0` | First allowed origin |
| `Cors__AllowedOrigins__1` | Second allowed origin |

### CORS Validation

In non-Development environments, CORS origins must be explicitly configured. The Gateway will throw an exception on startup if `Cors:AllowedOrigins` is empty in production.

## Security Considerations

### Trusted Proxy Chain

The Gateway is designed to run behind Cloudflare:
1. Cloudflare terminates TLS and sets `CF-Connecting-IP`
2. ForwardedHeaders middleware validates X-Forwarded-For against known Cloudflare IP ranges
3. Only validated IPs are trusted for `RemoteIpAddress`

### Defense in Depth

1. **JWT Validation**: Cryptographic verification of token signatures
2. **Policy Enforcement**: Route-level authorization checks
3. **Header Stripping**: Prevention of claim spoofing
4. **Rate Limiting**: Protection against brute force and DoS
5. **IP Validation**: Prevention of IP spoofing via trusted proxy validation

### Internal Endpoint Protection

Internal endpoints (`/api/v1/*/internal/*`) should be:
1. Blocked at the Gateway (via DenyAll policy)
2. Not exposed via external routes
3. Only accessible via internal Kubernetes networking

## Troubleshooting

### Common Issues

**401 Unauthorized**
- Token expired or invalid
- Wrong audience claim
- Identity service unreachable

**403 Forbidden**
- Missing required claims
- Policy not satisfied
- DenyAll route matched

**429 Too Many Requests**
- Rate limit exceeded
- Wait for window reset or use different credentials

### Diagnostic Endpoints

- `GET /healthz` - Gateway health
- `GET /readyz` - Readiness (includes auth config check)
- `GET /diagnostics/routes` - Route configuration
- `GET /diagnostics/clusters` - Backend cluster status

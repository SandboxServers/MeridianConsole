# BetterAuth Service - Implementation Plan

## Document Status
**Version:** 1.0
**Date:** 2026-01-22
**Status:** Implemented
**Service Port:** 5130

---

## Table of Contents

1. [Service Overview](#service-overview)
2. [Architecture](#architecture)
3. [Technology Stack](#technology-stack)
4. [Better Auth SDK Integration](#better-auth-sdk-integration)
5. [OAuth Providers](#oauth-providers)
6. [Authentication Flows](#authentication-flows)
7. [Session Management](#session-management)
8. [Token Exchange System](#token-exchange-system)
9. [Secrets Management](#secrets-management)
10. [Database Schema](#database-schema)
11. [Security Considerations](#security-considerations)
12. [Deployment](#deployment)
13. [Testing Strategy](#testing-strategy)

---

## Service Overview

### Purpose

Dhadgar.BetterAuth is the user-facing authentication service for Meridian Console. It handles:

- **Social OAuth Authentication**: Discord, Google, GitHub, Twitch, Facebook, Apple, Microsoft (100% passwordless)
- **Session Management**: Secure cookie-based sessions with cross-subdomain support
- **Account Linking**: Users can link multiple OAuth providers to a single account
- **Token Exchange**: Issues short-lived exchange tokens for the Identity service

### Why a Separate Service?

The Meridian Console uses a **hybrid identity architecture**:

| Service | Responsibility | Technology |
|---------|---------------|------------|
| **BetterAuth** | User-facing authentication, social OAuth | Node.js + Better Auth SDK |
| **Identity** | Authorization, JWT issuance, gaming OAuth, RBAC | .NET 10 + OpenIddict |

This separation exists because:

1. **Better Auth** has superior social OAuth support with minimal configuration
2. **Identity service** provides native .NET integration with the microservices
3. Each service uses the best tool for its specific job
4. Clean separation between authentication (who you are) and authorization (what you can do)

### Service Boundaries

**BetterAuth handles:**
- OAuth redirects and callbacks
- Session cookie management
- User profile from OAuth providers
- Exchange token generation

**BetterAuth does NOT handle:**
- JWT token issuance (delegated to Identity)
- Permission/role management (delegated to Identity)
- Organization membership (delegated to Identity)
- Gaming OAuth providers (Steam, Epic, etc.) (delegated to Identity)

---

## Architecture

### System Context

```
                                ┌─────────────────────────────────────────────────────────┐
                                │                   Frontend Apps                          │
                                │    (Panel, Shopping Cart, Marketing Site)               │
                                └──────────────────────┬──────────────────────────────────┘
                                                       │
                                                       ▼
                                ┌─────────────────────────────────────────────────────────┐
                                │                     Gateway (YARP)                       │
                                │              /api/v1/betterauth/* → BetterAuth          │
                                │              /api/v1/identity/*   → Identity            │
                                └──────────────────────┬──────────────────────────────────┘
                                                       │
                      ┌────────────────────────────────┼────────────────────────────────┐
                      │                                │                                │
                      ▼                                ▼                                ▼
      ┌───────────────────────────┐    ┌───────────────────────────┐    ┌─────────────────────┐
      │   Dhadgar.BetterAuth      │    │    Dhadgar.Identity       │    │  Dhadgar.Secrets    │
      │   (Node.js / Port 5130)   │    │   (.NET / Port 5010)      │    │  (.NET / Port 5110) │
      │                           │    │                           │    │                     │
      │  • OAuth Flows            │    │  • JWT Issuance           │    │  • Key Vault        │
      │  • Passwordless Auth      │───▶│  • Token Exchange         │    │  • OAuth Secrets    │
      │  • Session Management     │    │  • RBAC/Permissions       │    │  • Certificates     │
      │  • Account Linking        │    │  • Organization Mgmt      │    │                     │
      │  • Exchange Token Issue   │    │  • Gaming OAuth           │    │                     │
      └───────────────────────────┘    └───────────────────────────┘    └─────────────────────┘
                      │                                │                                │
                      │                                │                                │
                      └────────────────────────────────┼────────────────────────────────┘
                                                       │
                                                       ▼
                                ┌─────────────────────────────────────────────────────────┐
                                │                   PostgreSQL                             │
                                │                 dhadgar_platform                         │
                                │                                                         │
                                │   BetterAuth Tables:              Identity Tables:      │
                                │   • user                          • users               │
                                │   • session                       • organizations       │
                                │   • account                       • memberships         │
                                │   • verification                  • roles               │
                                └─────────────────────────────────────────────────────────┘
```

### Request Flow

1. **User initiates login** via frontend (e.g., "Sign in with Discord")
2. **Gateway routes** `/api/v1/betterauth/*` to BetterAuth service
3. **BetterAuth** redirects user to OAuth provider, handles callback, creates session
4. **Frontend calls** `/api/v1/betterauth/exchange` to get an exchange token
5. **Frontend calls** `/api/v1/identity/exchange` with the exchange token
6. **Identity service** validates the token, creates/updates user, returns JWT
7. **Frontend uses JWT** for subsequent API calls to all services

---

## Technology Stack

### Runtime Environment

| Component | Version | Notes |
|-----------|---------|-------|
| Node.js | 22 (Alpine in Docker) | ES Modules enabled |
| Package Manager | npm | Production dependencies only in container |
| Port | 5130 (dev) / 8080 (container) | Behind Gateway |

### Core Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `better-auth` | ^1.3.1 (actual: 1.4.10) | Core authentication library |
| `express` | ^4.19.2 | HTTP server framework |
| `pg` | ^8.12.0 | PostgreSQL client for direct queries |
| `jose` | ^5.6.3 | ES256 JWT signing for exchange tokens |
| `cors` | ^2.8.5 | Cross-origin request handling |
| `dotenv` | ^16.4.5 | Environment variable loading |

### .NET Solution Integration

The project includes a `.csproj` file that integrates with the .NET build system:

```xml
<Target Name="NpmInstall" BeforeTargets="Build">
  <Exec Command="npm install" WorkingDirectory="$(ProjectDir)" />
</Target>
```

Running `dotnet build` from the solution root will also install npm dependencies for BetterAuth.

---

## Better Auth SDK Integration

### Configuration

The Better Auth SDK is configured in `src/auth.js`:

```javascript
export const authConfig = {
  appName: "Meridian Console",
  baseURL: "http://localhost:5130/api/v1/betterauth",
  basePath: "/api/v1/betterauth",
  secret: process.env.BETTER_AUTH_SECRET,
  trustedOrigins: ["https://panel.meridianconsole.com", ...],
  database: new Pool({ connectionString: process.env.DATABASE_URL }),
  session: {
    expiresIn: 60 * 60 * 24 * 7, // 7 days
    updateAge: 60 * 60 * 24,     // Refresh after 24 hours
    cookieCache: { enabled: true, maxAge: 60 * 5 } // 5 minute client cache
  },
  emailAndPassword: { enabled: false },  // 100% passwordless
  socialProviders: { discord: {...}, google: {...}, ... },
  plugins: [genericOAuth(...)], // Microsoft federated credentials
  account: {
    accountLinking: {
      enabled: true,
      trustedProviders: ["discord", "google", "github", ...]
    }
  },
  advanced: {
    crossSubDomainCookies: { enabled: true, domain: "meridianconsole.com" },
    defaultCookieAttributes: {
      sameSite: "none", secure: true, httpOnly: true, partitioned: true
    }
  }
};
```

### Express Integration

```javascript
// Better Auth handles all routes under /api/v1/betterauth/*
app.all("/api/v1/betterauth/*", toNodeHandler(auth));
```

### Auto-Migration

Better Auth automatically creates and updates database tables on startup:

```javascript
const { toBeCreated, toBeAdded, runMigrations } = await getMigrations(authConfig);
if (toBeCreated.length > 0 || toBeAdded.length > 0) {
  await runMigrations();
}
```

---

## OAuth Providers

### Supported Providers

| Provider | Scopes | Notes |
|----------|--------|-------|
| **Facebook** | email, public_profile | Standard OAuth |
| **Google** | openid, profile, email | Standard OAuth |
| **Discord** | identify, email | Popular for gaming |
| **Twitch** | user:read:email | Gaming streaming |
| **GitHub** | read:user, user:email | Developer accounts |
| **Apple** | name, email | Sign in with Apple |
| **Microsoft** | openid, profile, email, User.Read | Uses federated credentials |

### Configuration Sources

All OAuth credentials are stored in Azure Key Vault and loaded via the Secrets service:

| Key Vault Secret | Environment Variable |
|-----------------|---------------------|
| `oauth-discord-client-id` | `OAUTH_DISCORD_CLIENT_ID` |
| `oauth-discord-client-secret` | `OAUTH_DISCORD_CLIENT_SECRET` |
| `oauth-google-client-id` | `OAUTH_GOOGLE_CLIENT_ID` |
| `oauth-google-client-secret` | `OAUTH_GOOGLE_CLIENT_SECRET` |
| `oauth-github-client-id` | `OAUTH_GITHUB_CLIENT_ID` |
| `oauth-github-client-secret` | `OAUTH_GITHUB_CLIENT_SECRET` |
| ... | ... |

### Microsoft Federated Credentials

Microsoft OAuth uses **federated credentials** instead of a client secret, providing enhanced security:

```javascript
// Custom token exchange using client_assertion
getToken: async ({ code, redirectURI }) => {
  const clientAssertion = await getMicrosoftClientAssertion();

  const params = new URLSearchParams({
    client_id: microsoftClientId,
    code: code,
    redirect_uri: redirectURI,
    grant_type: "authorization_code",
    client_assertion_type: "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
    client_assertion: clientAssertion
  });
  // ...
}
```

The flow:
1. BetterAuth requests a WIF token from Identity service
2. Identity issues a JWT with `sub=betterauth-client`
3. BetterAuth uses the JWT as `client_assertion` to Microsoft
4. Microsoft validates the JWT against the federated credential
5. Microsoft returns an access token

---

## Authentication Flows

### OAuth Flow (e.g., Discord)

```
┌──────────┐     ┌─────────┐     ┌──────────────┐     ┌─────────┐     ┌──────────┐
│  Browser │     │ Gateway │     │  BetterAuth  │     │ Discord │     │ Identity │
└────┬─────┘     └────┬────┘     └──────┬───────┘     └────┬────┘     └────┬─────┘
     │                │                  │                  │               │
     │ Click "Discord Login"             │                  │               │
     │────────────────────────────────▶  │                  │               │
     │                │                  │                  │               │
     │                │  /api/v1/betterauth/sign-in/social  │               │
     │                │  ?provider=discord                  │               │
     │                │─────────────────▶│                  │               │
     │                │                  │                  │               │
     │                │     302 Redirect to Discord OAuth   │               │
     │◀───────────────────────────────────────────────────────────────────  │
     │                │                  │                  │               │
     │  User authorizes at Discord       │                  │               │
     │──────────────────────────────────────────────────▶  │               │
     │                │                  │                  │               │
     │  Callback with code               │                  │               │
     │──────────────────────────────────▶│                  │               │
     │                │                  │                  │               │
     │                │                  │ Exchange code    │               │
     │                │                  │─────────────────▶│               │
     │                │                  │                  │               │
     │                │                  │ User info        │               │
     │                │                  │◀─────────────────│               │
     │                │                  │                  │               │
     │                │  Set session cookie, redirect       │               │
     │◀──────────────────────────────────│                  │               │
     │                │                  │                  │               │
     │  POST /api/v1/betterauth/exchange │                  │               │
     │────────────────────────────────▶  │                  │               │
     │                │                  │                  │               │
     │                │     { exchangeToken: "..." }        │               │
     │◀──────────────────────────────────│                  │               │
     │                │                  │                  │               │
     │  POST /api/v1/identity/exchange   │                  │               │
     │  { exchangeToken: "..." }         │                  │               │
     │────────────────────────────────────────────────────────────────────▶│
     │                │                  │                  │               │
     │  { accessToken, refreshToken, expiresIn, userId }    │               │
     │◀───────────────────────────────────────────────────────────────────  │
```

---

## Session Management

### Session Configuration

```javascript
session: {
  expiresIn: 60 * 60 * 24 * 7,  // 7 days (matches refresh token lifetime)
  updateAge: 60 * 60 * 24,      // Refresh if older than 24 hours
  cookieCache: {
    enabled: true,
    maxAge: 60 * 5              // 5 minute client-side cache
  }
}
```

### Cookie Configuration

```javascript
advanced: {
  crossSubDomainCookies: {
    enabled: true,
    domain: "meridianconsole.com"  // Share across subdomains
  },
  defaultCookieAttributes: {
    sameSite: "none",    // Required for cross-origin
    secure: true,        // HTTPS only
    httpOnly: true,      // Not accessible via JavaScript
    partitioned: true    // Third-party cookie support
  }
}
```

### Session Cookies

| Cookie | Purpose |
|--------|---------|
| `better-auth.session_token` | Session identifier |
| `better-auth.state` | OAuth state for CSRF protection |

---

## Token Exchange System

### Purpose

The exchange token is a short-lived, single-use JWT that BetterAuth issues to allow the Identity service to verify authentication without sharing session cookies.

### Exchange Token Structure

**Header:**
```json
{
  "alg": "ES256",
  "kid": "betterauth-exchange-v1"
}
```

**Payload:**
```json
{
  "sub": "user-uuid",
  "email": "user@example.com",
  "name": "John Doe",
  "picture": "https://...",
  "purpose": "token_exchange",
  "client_app": "panel",
  "provider": "discord",
  "providers": [
    { "providerId": "discord", "accountId": "123456789" },
    { "providerId": "google", "accountId": "987654321" }
  ],
  "iss": "https://meridianconsole.com/api/v1/betterauth",
  "aud": "https://meridianconsole.com/api/v1/identity/exchange",
  "iat": 1704067200,
  "exp": 1704067260,
  "jti": "unique-token-id"
}
```

### Properties

| Property | Value |
|----------|-------|
| **Algorithm** | ES256 (ECDSA with P-256 curve) |
| **Lifetime** | 60 seconds (hard-coded) |
| **Single-use** | Identity service tracks JTI to prevent replay |
| **Private Key** | Stored in Key Vault as `betterauth-exchange-private-key` |

### Client App Resolution

The `client_app` claim is resolved using this priority:

1. **Origin header hostname matching:**
   - `panel.meridianconsole.com` -> `"panel"`
   - `cart.meridianconsole.com` -> `"shop"`
   - `meridianconsole.com` or `www.meridianconsole.com` -> `"shop"`

2. **Request body `clientApp` field** (if in allowed list)

3. **Default:** `"unknown"`

---

## Secrets Management

### Secret Loading Flow

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   BetterAuth    │     │    Identity     │     │    Secrets      │
│    Startup      │     │    Service      │     │    Service      │
└────────┬────────┘     └────────┬────────┘     └────────┬────────┘
         │                       │                       │
         │ Request access token  │                       │
         │──────────────────────▶│                       │
         │                       │                       │
         │ Access token          │                       │
         │◀──────────────────────│                       │
         │                       │                       │
         │ GET /api/v1/secrets/betterauth                │
         │───────────────────────────────────────────────▶│
         │                       │                       │
         │ { "betterauth-secret": "...", ... }           │
         │◀──────────────────────────────────────────────│
         │                       │                       │
         │ GET /api/v1/secrets/oauth                     │
         │───────────────────────────────────────────────▶│
         │                       │                       │
         │ { "oauth-discord-client-id": "...", ... }     │
         │◀──────────────────────────────────────────────│
```

### Required Secrets

| Key Vault Name | Purpose |
|----------------|---------|
| `betterauth-secret` | Session encryption key (32+ characters) |
| `betterauth-exchange-private-key` | ES256 private key (PEM format) |

### Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `SECRETS_SERVICE_URL` | URL of the Secrets service | Yes |
| `SERVICE_CLIENT_ID` | OAuth client ID for Secrets auth | Yes |
| `SERVICE_CLIENT_SECRET` | OAuth client secret | Yes |
| `PORT` | HTTP server port | No (default: 5130) |
| `BETTER_AUTH_URL` | Public URL of BetterAuth | No |
| `BETTER_AUTH_TRUSTED_ORIGINS` | Comma-separated allowed origins | No |

---

## Database Schema

BetterAuth creates and manages its own tables in the shared `dhadgar_platform` database.

### Tables

#### `user`

| Column | Type | Description |
|--------|------|-------------|
| `id` | VARCHAR | Primary key (UUID) |
| `name` | VARCHAR | Display name |
| `email` | VARCHAR | Email address (unique) |
| `emailVerified` | BOOLEAN | Email verification status |
| `image` | VARCHAR | Profile picture URL |
| `createdAt` | TIMESTAMP | Creation time |
| `updatedAt` | TIMESTAMP | Last update time |

#### `session`

| Column | Type | Description |
|--------|------|-------------|
| `id` | VARCHAR | Primary key |
| `userId` | VARCHAR | Foreign key to user |
| `token` | VARCHAR | Session token (hashed) |
| `expiresAt` | TIMESTAMP | Session expiration |
| `ipAddress` | VARCHAR | Client IP |
| `userAgent` | TEXT | Client user agent |
| `createdAt` | TIMESTAMP | Creation time |
| `updatedAt` | TIMESTAMP | Last update time |

#### `account`

| Column | Type | Description |
|--------|------|-------------|
| `id` | VARCHAR | Primary key |
| `userId` | VARCHAR | Foreign key to user |
| `providerId` | VARCHAR | OAuth provider name |
| `accountId` | VARCHAR | Provider's user ID |
| `accessToken` | TEXT | OAuth access token |
| `refreshToken` | TEXT | OAuth refresh token |
| `accessTokenExpiresAt` | TIMESTAMP | Token expiration |
| `scope` | TEXT | Granted scopes |
| `idToken` | TEXT | OIDC ID token |
| `createdAt` | TIMESTAMP | Creation time |
| `updatedAt` | TIMESTAMP | Last update time |

#### `verification`

| Column | Type | Description |
|--------|------|-------------|
| `id` | VARCHAR | Primary key |
| `identifier` | VARCHAR | Email or phone |
| `token` | VARCHAR | Verification token |
| `expiresAt` | TIMESTAMP | Token expiration |
| `createdAt` | TIMESTAMP | Creation time |
| `updatedAt` | TIMESTAMP | Last update time |

---

## Security Considerations

### Session Security

| Control | Implementation |
|---------|---------------|
| HTTP-only cookies | Session tokens not accessible via JavaScript |
| Secure flag | Cookies only sent over HTTPS |
| SameSite=None | Required for cross-origin, with Partitioned flag |
| 7-day expiration | Sessions expire after inactivity |

### Exchange Token Security

| Control | Implementation |
|---------|---------------|
| ES256 signing | ECDSA with P-256 curve (FIPS 186-4 compliant) |
| 60-second lifetime | Minimizes window for token theft |
| Single-use JTI | Identity service tracks used tokens |
| Audience restriction | Token only valid for Identity service |

### OAuth Security

| Control | Implementation |
|---------|---------------|
| State parameter | CSRF protection for OAuth flows |
| Account linking | Only for verified email addresses |
| Federated credentials | Microsoft OAuth uses JWT assertions instead of secrets |
| Trusted providers | Explicit list of providers for account linking |

### Secrets Management

| Control | Implementation |
|---------|---------------|
| No hardcoded secrets | All sensitive values from Key Vault |
| Service account auth | Dedicated client credentials |
| Minimal permissions | Only `secrets:read:betterauth-*` and `secrets:read:oauth-*` |
| In-memory only | Secrets never written to disk |

### Rate Limiting

Via Gateway:
- **30 requests/minute** per IP for authentication endpoints
- Protects against brute force and credential stuffing

---

## Deployment

### Dockerfile

```dockerfile
FROM node:22-alpine AS base
WORKDIR /app

# Install dependencies
FROM base AS deps
COPY src/Dhadgar.BetterAuth/package*.json ./
RUN npm ci --omit=dev

# Production image
FROM base AS runner
ENV NODE_ENV=production
ENV PORT=8080

COPY --from=deps /app/node_modules ./node_modules
COPY src/Dhadgar.BetterAuth/src ./src
COPY src/Dhadgar.BetterAuth/package.json ./

RUN addgroup --system --gid 1001 nodejs && \
    adduser --system --uid 1001 betterauth && \
    chown -R betterauth:nodejs /app

USER betterauth
EXPOSE 8080
CMD ["node", "src/server.js"]
```

### Gateway Configuration

```json
{
  "ReverseProxy": {
    "Routes": {
      "betterauth-route": {
        "ClusterId": "betterauth",
        "Order": 10,
        "Match": { "Path": "/api/v1/betterauth/{**catch-all}" },
        "AuthorizationPolicy": "Anonymous",
        "RateLimiterPolicy": "Auth"
      }
    },
    "Clusters": {
      "betterauth": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Path": "/healthz"
          }
        },
        "Destinations": {
          "d1": { "Address": "http://localhost:5130/" }
        }
      }
    }
  }
}
```

### Kubernetes (Planned)

- Deployment with 2+ replicas for HA
- Horizontal Pod Autoscaler based on CPU
- Service mesh integration for mTLS
- Config from ConfigMaps and Secrets

---

## Testing Strategy

### Health Check

```bash
curl http://localhost:5130/healthz
# Response: {"service":"Dhadgar.BetterAuth","status":"ok"}
```

### Integration Testing

```bash
# 1. Start infrastructure
docker compose -f deploy/compose/docker-compose.dev.yml up -d

# 2. Open browser to initiate OAuth
open "http://localhost:5000/api/v1/betterauth/sign-in/social?provider=discord&callbackURL=http://localhost:4321"

# 3. After OAuth callback, check session
curl -v http://localhost:5000/api/v1/betterauth/session \
  --cookie "better-auth.session_token=..."

# 4. Get exchange token
curl -X POST http://localhost:5000/api/v1/betterauth/exchange \
  --cookie "better-auth.session_token=..." \
  -H "Content-Type: application/json" \
  -d '{}'

# 5. Exchange for JWT
curl -X POST http://localhost:5000/api/v1/identity/exchange \
  -H "Content-Type: application/json" \
  -d '{"exchangeToken": "eyJ..."}'
```

### Unit Testing (Recommended)

```bash
# Install test dependencies
npm install --save-dev vitest @types/node

# Run tests
npm test
```

---

## Related Documentation

- [Identity Service Implementation Plan](./identity-service.md)
- [Gateway Service Implementation Plan](./gateway-service.md)
- [Secrets Service Implementation Plan](./secrets-service.md)
- [Authentication Analysis](../architecture/authentication-analysis.md)
- [BetterAuth API Reference](../betterauth-api-reference.md)

---

## Changelog

### v1.0.0 (2026-01-22)

- Initial documentation
- Better Auth integration with Express.js
- OAuth providers: Discord, Google, GitHub, Twitch, Facebook, Apple, Microsoft
- Passwordless authentication (OAuth only)
- Exchange token system for Identity service integration
- Secrets service integration for credential management
- Microsoft federated credentials support
- Account linking across providers
- Cross-subdomain cookie support

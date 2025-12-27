# Authentication Solutions Analysis for Meridian Console

**Date:** December 26, 2025
**Status:** Decision Pending
**Author:** Claude (AI-assisted analysis)

---

## Executive Summary

This document analyzes five authentication solutions (Auth.js, Clerk, Better Auth, OpenAuth, Stack Auth) for Meridian Console and provides a recommendation based on the platform's .NET 10 microservices architecture and Blazor WebAssembly frontends.

**Key Finding:** All five solutions are JavaScript/TypeScript libraries designed for Node.js ecosystems. They are not ideal for a .NET-native architecture.

**Recommendation:** Use **OpenIddict** (open-source .NET library) integrated into the existing `Dhadgar.Identity` service.

---

## Table of Contents

1. [Architecture Context](#architecture-context)
2. [Solution Analysis](#solution-analysis)
3. [Azure Static Web Apps Constraints](#azure-static-web-apps-constraints)
4. [Recommended Solutions for .NET](#recommended-solutions-for-net)
5. [Token Strategy](#token-strategy)
6. [Final Recommendation](#final-recommendation)
7. [Comparison Matrix](#comparison-matrix)

---

## Architecture Context

Meridian Console's authentication requirements:

| Component | Technology | Auth Needs |
|-----------|------------|------------|
| Backend Services | .NET 10 microservices | JWT validation, service-to-service auth |
| Frontend (Panel) | Blazor WebAssembly | OIDC login flow, token management |
| Frontend (ShoppingCart) | Blazor WebAssembly | Same as above |
| Hosting | Azure Static Web Apps | Custom OIDC provider support |
| Agents | .NET (Linux/Windows) | mTLS + JWT for control plane auth |
| Gateway | YARP | JWT validation, route protection |

### Existing Infrastructure

- `Dhadgar.Identity` service already scaffolded with EF Core
- PostgreSQL database for user storage
- Multi-tenant architecture (organizations/tenants)

---

## Solution Analysis

### 1. Auth.js (formerly NextAuth.js)

| Aspect | Details |
|--------|---------|
| **Status** | Maintenance mode - absorbed by Better Auth (Jan 2025) |
| **Architecture** | Embedded Node.js library |
| **License** | ISC |
| **.NET Compatibility** | None |
| **Blazor Compatibility** | None |
| **SWA Compatibility** | Only via Node.js Azure Functions |

**Analysis:**

Auth.js was the de-facto authentication library for Next.js applications. However, the main contributor (Balázs Orbán) quit in January 2025, and version 5 never achieved a stable release despite years in beta.

The project has been absorbed by Better Auth, which now maintains security patches but recommends new projects use Better Auth instead.

**Verdict:** ❌ Not viable. Dead project, wrong ecosystem.

---

### 2. Clerk

| Aspect | Details |
|--------|---------|
| **Type** | Hosted SaaS (no self-host option) |
| **Pricing** | Free: 10K MAU, Pro: $0.02/MAU after that |
| **Add-ons** | MFA: $100/mo, SAML: $100/mo + $50/connection |
| **Architecture** | Drop-in React components + REST API |
| **.NET Compatibility** | REST API only (no SDK) |
| **Blazor Compatibility** | No components available |
| **SWA Compatibility** | Requires custom OIDC setup |

**Pricing Breakdown:**

```
Free Tier:
- 10,000 MAU
- 100 monthly active organizations
- Core features included

Pro Tier ($0.02/MAU after 10K):
- 50,000 MAU = ~$800/month
- 100,000 MAU = ~$1,800/month

Required Add-ons for Enterprise:
- MFA: +$100/month
- SAML SSO: +$100/month + $50/connection
- User Impersonation: +$100/month
```

**Pros:**
- Excellent developer experience for React/Next.js
- Full user management, organizations, RBAC
- SOC 2 Type II compliant
- Bot detection, brute-force protection

**Cons:**
- No .NET SDK (custom REST integration required)
- React components useless for Blazor
- Vendor lock-in (no self-host)
- Pricing adds up quickly with add-ons
- No data portability

**Verdict:** ❌ Not recommended. Wrong ecosystem, expensive for features we can't use.

---

### 3. Better Auth

| Aspect | Details |
|--------|---------|
| **Status** | Active development, YC X25 backed |
| **Type** | Self-hosted TypeScript library |
| **License** | MIT (free) |
| **Architecture** | Runs embedded in Node.js app |
| **.NET Compatibility** | None |
| **Blazor Compatibility** | None |
| **SWA Compatibility** | Only via Node.js Functions |

**Key Features:**
- Plugin architecture for modular features
- 2FA, multi-tenancy, multi-session, rate limiting
- Database-agnostic (PostgreSQL, MySQL, SQLite)
- Framework-agnostic (Next.js, Remix, Astro, vanilla Node)
- Automatic schema generation and migrations

**Pros:**
- Free and open source
- Self-hosted (data ownership)
- TypeScript-native with excellent DX
- Active development and YC backing
- No per-user pricing

**Cons:**
- TypeScript only - cannot run in .NET
- Would require separate Node.js service
- No Blazor components
- Adds operational complexity

**Verdict:** ❌ Wrong ecosystem. Great library, but for TypeScript projects only.

---

### 4. OpenAuth (by SST)

| Aspect | Details |
|--------|---------|
| **Status** | Beta |
| **Type** | Standalone OAuth 2.0 server |
| **License** | MIT (free) |
| **Architecture** | Centralized auth server (like self-hosted Auth0) |
| **.NET Compatibility** | ✅ Standards-based OIDC |
| **Blazor Compatibility** | ✅ Standard OIDC flow |
| **SWA Compatibility** | ✅ Custom OIDC provider |
| **Go SDK** | ✅ Available for agents |

**Architecture:**

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Blazor WASM    │────▶│    OpenAuth     │────▶│  Your User DB   │
│  (OIDC Client)  │     │  (Auth Server)  │     │  (PostgreSQL)   │
└─────────────────┘     └─────────────────┘     └─────────────────┘
                               │
                               ▼
                        ┌─────────────────┐
                        │   Your APIs     │
                        │ (JWT Validation)│
                        └─────────────────┘
```

**Key Differentiator:**

Unlike other solutions that are embedded libraries, OpenAuth is a **centralized auth server** designed for multi-app/microservices architectures. It implements the full OAuth 2.0 spec.

**Deployment Options:**
- Node.js
- Bun
- AWS Lambda
- Cloudflare Workers

**Pros:**
- Standards-based (any OIDC client works)
- Designed for microservices (centralized auth)
- Self-hosted on your infrastructure
- Serverless-friendly
- Go SDK available for agents
- Free and open source

**Cons:**
- Beta status (not production-proven)
- No user management (bring your own)
- TypeScript server (not .NET native)
- Another service to maintain

**Verdict:** ⚠️ Best of the JS options. Viable because it's standards-based, but still adds operational complexity.

---

### 5. Stack Auth

| Aspect | Details |
|--------|---------|
| **Status** | Launched March 2025, YC backed |
| **Type** | Self-hosted or managed |
| **License** | MIT + AGPL |
| **Architecture** | Next.js focused with REST API |
| **.NET Compatibility** | REST API only |
| **Blazor Compatibility** | No components |
| **SWA Compatibility** | Would need custom integration |

**Key Features:**
- OAuth, passwords, magic links, passkeys
- Organizations and teams with RBAC
- User impersonation
- Email invitations
- Permission trees

**Pros:**
- Open source (can self-host free)
- Full user management included
- B2B features (orgs, teams, RBAC)
- 5-minute setup claim
- No vendor lock-in

**Cons:**
- React/Next.js focused
- No .NET SDK
- Components useless for Blazor
- Would require custom REST integration

**Verdict:** ❌ Similar to Clerk but open source. Still wrong ecosystem for .NET.

---

## Azure Static Web Apps Constraints

### Authentication Tiers

| Tier | Price | Pre-configured Providers | Custom OIDC |
|------|-------|-------------------------|-------------|
| Free | $0 | GitHub, Entra ID | ❌ |
| Standard | $9/app/month | Same | ✅ |

**Critical Constraints:**

1. **Custom auth requires Standard tier** ($9/app/month per SWA)
2. Enabling custom providers **disables all pre-configured providers**
3. Custom providers must support **OpenID Connect (OIDC)**
4. Configuration is done via `staticwebapp.config.json`

### Custom OIDC Configuration Example

```json
{
  "auth": {
    "identityProviders": {
      "customOpenIdConnectProviders": {
        "meridian": {
          "registration": {
            "clientIdSettingName": "MERIDIAN_CLIENT_ID",
            "clientCredential": {
              "clientSecretSettingName": "MERIDIAN_CLIENT_SECRET"
            },
            "openIdConnectConfiguration": {
              "wellKnownOpenIdConfiguration": "https://identity.meridian.io/.well-known/openid-configuration"
            }
          },
          "login": {
            "nameClaimType": "name",
            "scopes": ["openid", "profile", "email"]
          }
        }
      }
    }
  }
}
```

---

## Recommended Solutions for .NET

Given the architecture mismatch with JavaScript solutions, here are the **actual options** for Meridian Console:

### Option A: OpenIddict (Primary Recommendation)

| Aspect | Details |
|--------|---------|
| **Type** | Open-source .NET library |
| **License** | Apache 2.0 (free forever) |
| **Integration** | Embeds in `Dhadgar.Identity` |
| **Standards** | OAuth 2.0 + OpenID Connect |
| **Maintenance** | Active, by Kévin Chalet |

**Architecture:**

```
┌─────────────────────────────────────────────────────────────┐
│                    Blazor WASM (SWA)                        │
│              Standard OIDC Authentication                    │
│     Microsoft.AspNetCore.Components.WebAssembly.Authentication│
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│              Dhadgar.Identity + OpenIddict                  │
│                                                             │
│   • /connect/authorize    (OIDC authorization endpoint)    │
│   • /connect/token        (Token endpoint)                 │
│   • /connect/userinfo     (User info endpoint)             │
│   • /.well-known/openid-configuration (Discovery)          │
│                                                             │
│   Features:                                                 │
│   • ASP.NET Core Identity (user storage)                   │
│   • Social logins (Google, Discord, GitHub)                │
│   • Multi-tenant support                                    │
│   • Refresh token rotation                                  │
│   • Device authorization flow (for agents)                  │
└─────────────────────────────────────────────────────────────┘
                            │
              ┌─────────────┼─────────────┐
              ▼             ▼             ▼
         Gateway        Services       Agents
      (JWT validation) (JWT validation) (mTLS + JWT)
```

**Why OpenIddict:**

1. **Native .NET** - First-class ASP.NET Core integration
2. **Already have Identity service** - Just add OpenIddict packages
3. **Standards-compliant** - Works with SWA custom OIDC
4. **Free forever** - Apache 2.0 license
5. **Flexible** - You control every aspect
6. **Active** - Regular updates and security patches

**Implementation Effort:** Medium (2-3 days for basic setup)

---

### Option B: Keycloak

| Aspect | Details |
|--------|---------|
| **Type** | Standalone identity server |
| **License** | Apache 2.0 (free) |
| **Language** | Java (runs as Docker container) |
| **Standards** | OAuth 2.0 + OIDC + SAML |
| **Admin UI** | Full-featured web console |

**Architecture:**

```
┌─────────────────┐     ┌─────────────────┐
│  Blazor WASM    │────▶│    Keycloak     │
│  (OIDC Client)  │     │   (Container)   │
└─────────────────┘     └─────────────────┘
                               │
        ┌──────────────────────┼──────────────────────┐
        ▼                      ▼                      ▼
┌───────────────┐    ┌─────────────────┐    ┌───────────────┐
│ Identity Svc  │    │    Gateway      │    │    Agents     │
│ (thin wrapper)│    │ (JWT validation)│    │ (JWT + mTLS)  │
└───────────────┘    └─────────────────┘    └───────────────┘
```

**Pros:**
- Battle-tested (Red Hat backed)
- Full admin UI out of the box
- Everything included (users, orgs, RBAC, MFA, SAML)
- Just run a container
- Extensive documentation

**Cons:**
- Java (memory: 512MB-1GB minimum)
- Another service to manage
- Your Identity service becomes a thin proxy
- Steeper learning curve

**Implementation Effort:** Low (1 day for basic setup, container-based)

---

### Option C: Microsoft Entra ID (Azure AD)

| Aspect | Details |
|--------|---------|
| **Type** | Managed cloud IdP |
| **Pricing** | Free tier available |
| **Integration** | Native SWA support |
| **Enterprise** | SSO, conditional access, compliance |

**Best For:**
- Enterprise customers requiring SSO
- Teams already using Azure/M365
- Minimal maintenance preference

**Cons:**
- Vendor lock-in
- Complex pricing at scale
- Less control over auth flows
- Not ideal for consumer-facing B2C

---

### Option D: OpenAuth (SST) + Identity Service

Use OpenAuth as OIDC server, keep Identity service for user management:

```
Blazor WASM ──▶ OpenAuth (OIDC) ──▶ Dhadgar.Identity (Users DB)
```

**When to Consider:**
- Want serverless auth (Lambda/Workers)
- Prefer TypeScript for auth logic
- Comfortable with beta software

---

## Token Strategy

### JWT vs Access/Refresh Tokens

This is a false dichotomy - **you use both**:

```
┌──────────────────────────────────────────────────────────────┐
│                     Token Types                               │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  Access Token (JWT)                                  │    │
│  │  ──────────────────                                  │    │
│  │  • Format: JSON Web Token (signed)                   │    │
│  │  • Lifetime: Short (15-60 minutes)                   │    │
│  │  • Contains: user_id, tenant_id, roles, permissions  │    │
│  │  • Validation: Stateless (signature + claims check)  │    │
│  │  • Sent: Authorization header on every API request   │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                              │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  Refresh Token                                       │    │
│  │  ─────────────                                       │    │
│  │  • Format: Opaque string (not JWT)                   │    │
│  │  • Lifetime: Long (7-30 days)                        │    │
│  │  • Storage: Server-side (database)                   │    │
│  │  • Revocable: Yes (delete from DB)                   │    │
│  │  • Purpose: Obtain new access tokens silently        │    │
│  │  • Rotation: Issue new refresh token on each use     │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                              │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  ID Token (JWT) - OIDC Only                          │    │
│  │  ─────────────────────────                           │    │
│  │  • Format: JSON Web Token                            │    │
│  │  • Contains: User profile (name, email, picture)     │    │
│  │  • Purpose: Frontend user display                    │    │
│  │  • Never sent to: Backend APIs                       │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

### Token Flow for Blazor WebAssembly

```
┌──────────────┐                    ┌──────────────┐                    ┌──────────────┐
│ Blazor WASM  │                    │   Identity   │                    │   Gateway    │
│   (Client)   │                    │   Service    │                    │   + APIs     │
└──────┬───────┘                    └──────┬───────┘                    └──────┬───────┘
       │                                   │                                   │
       │  1. OIDC Authorization Request    │                                   │
       │──────────────────────────────────▶│                                   │
       │                                   │                                   │
       │  2. User authenticates            │                                   │
       │◀─────────────────────────────────▶│                                   │
       │                                   │                                   │
       │  3. Authorization code            │                                   │
       │◀──────────────────────────────────│                                   │
       │                                   │                                   │
       │  4. Exchange code for tokens      │                                   │
       │──────────────────────────────────▶│                                   │
       │                                   │                                   │
       │  5. Access token + Refresh token  │                                   │
       │◀──────────────────────────────────│                                   │
       │                                   │                                   │
       │  6. API request + Access token    │                                   │
       │─────────────────────────────────────────────────────────────────────▶│
       │                                   │                                   │
       │  7. Validate JWT, return data     │                                   │
       │◀─────────────────────────────────────────────────────────────────────│
       │                                   │                                   │
       │  ... Access token expires ...     │                                   │
       │                                   │                                   │
       │  8. Refresh token request         │                                   │
       │──────────────────────────────────▶│                                   │
       │                                   │                                   │
       │  9. New access + refresh tokens   │                                   │
       │◀──────────────────────────────────│                                   │
       │                                   │                                   │
```

### Blazor WASM Token Storage

```csharp
// Microsoft's recommendation for Blazor WASM:
// - Access token: In-memory only (AuthenticationStateProvider)
// - Refresh token: Secure HTTP-only cookie OR in-memory
// - NEVER use localStorage (XSS vulnerable)
// - sessionStorage acceptable but not ideal

// The built-in authentication library handles this correctly
// when configured with OIDC
```

### Recommended Token Configuration

```json
{
  "AccessToken": {
    "Lifetime": "00:15:00",
    "SigningAlgorithm": "RS256",
    "Claims": ["sub", "tenant_id", "roles", "permissions"]
  },
  "RefreshToken": {
    "Lifetime": "7.00:00:00",
    "RotateOnUse": true,
    "RevokeOnSecurityStamp": true
  },
  "IdToken": {
    "Lifetime": "01:00:00",
    "Claims": ["sub", "name", "email", "picture"]
  }
}
```

---

## Final Recommendation

### Primary: OpenIddict in Dhadgar.Identity

```
Cost:           $0 (Apache 2.0 license)
Maintenance:    Medium (you own the code)
Flexibility:    Maximum
.NET Native:    Yes
SWA Compatible: Yes (Standard tier, $9/app/month)
Time to MVP:    2-3 days
```

**Implementation Steps:**

1. Add OpenIddict NuGet packages to `Dhadgar.Identity`
2. Configure ASP.NET Core Identity for user storage
3. Set up OIDC endpoints (authorize, token, userinfo)
4. Add social login providers (Google, Discord, GitHub)
5. Configure Blazor WASM with `Microsoft.AspNetCore.Components.WebAssembly.Authentication`
6. Set up SWA custom OIDC provider in `staticwebapp.config.json`
7. Configure Gateway for JWT validation
8. Implement agent authentication (client credentials or device flow)

### Alternative: Keycloak (If Faster Setup Preferred)

```
Cost:           $0 (Apache 2.0)
Maintenance:    Low (container updates only)
Flexibility:    Medium (configuration-based)
.NET Native:    No (but standards-based)
SWA Compatible: Yes
Time to MVP:    1 day
```

---

## Comparison Matrix

| Criteria | Auth.js | Clerk | Better Auth | OpenAuth | Stack Auth | **OpenIddict** | Keycloak |
|----------|---------|-------|-------------|----------|------------|----------------|----------|
| .NET Compatible | ❌ | ⚠️ | ❌ | ✅ | ⚠️ | ✅ | ✅ |
| Blazor Support | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ | ✅ |
| SWA Compatible | ⚠️ | ⚠️ | ⚠️ | ✅ | ⚠️ | ✅ | ✅ |
| Self-Hosted | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Cost | Free | $$ | Free | Free | Free | **Free** | Free |
| User Management | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ |
| Multi-tenant | ⚠️ | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ |
| Social Login | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| MFA | ⚠️ | $$ | ✅ | ❌ | ✅ | ✅ | ✅ |
| SAML/Enterprise SSO | ❌ | $$ | ⚠️ | ❌ | ⚠️ | ⚠️ | ✅ |
| Production Ready | ❌ | ✅ | ✅ | ⚠️ | ⚠️ | ✅ | ✅ |
| **Recommendation** | ❌ | ❌ | ❌ | ⚠️ | ❌ | **✅** | ✅ |

---

## References

- [OpenIddict Documentation](https://documentation.openiddict.com/)
- [Keycloak Documentation](https://www.keycloak.org/documentation)
- [Azure SWA Custom Auth](https://learn.microsoft.com/en-us/azure/static-web-apps/authentication-custom)
- [Blazor WASM Security](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/webassembly/)
- [Clerk Pricing](https://clerk.com/pricing)
- [Better Auth](https://www.better-auth.com/)
- [Stack Auth](https://stack-auth.com/)
- [OpenAuth (SST)](https://github.com/sst/openauth)
- [.NET Auth in 2025](https://medium.com/@vikpoca/authentication-and-authorization-in-net-in-2025-6f70b601028f)

---

## Appendix: Package References

### OpenIddict Packages for Dhadgar.Identity

```xml
<PackageReference Include="OpenIddict.AspNetCore" Version="5.8.0" />
<PackageReference Include="OpenIddict.EntityFrameworkCore" Version="5.8.0" />
<PackageReference Include="OpenIddict.Validation.AspNetCore" Version="5.8.0" />
```

### Blazor WASM Authentication Package

```xml
<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.Authentication" Version="10.0.0" />
```

### Gateway JWT Validation

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
```

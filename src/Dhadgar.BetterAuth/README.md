# Dhadgar.BetterAuth

**Passwordless authentication service using Better Auth SDK for Meridian Console.**

This service provides user-facing authentication via social OAuth providers (Discord, Google, GitHub, Twitch, Facebook, Apple, Microsoft) and email/password login. It operates as part of a hybrid identity architecture where Better Auth handles the user-facing authentication flows while the Identity service (Dhadgar.Identity) manages authorization, permissions, and JWT token issuance.

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Tech Stack](#tech-stack)
4. [Quick Start](#quick-start)
5. [Configuration](#configuration)
6. [OAuth Providers](#oauth-providers)
7. [Authentication Flow](#authentication-flow)
8. [Session Management](#session-management)
9. [Token Exchange](#token-exchange)
10. [Integration with Identity Service](#integration-with-identity-service)
11. [API Endpoints](#api-endpoints)
12. [Database Schema](#database-schema)
13. [Secrets Management](#secrets-management)
14. [Gateway Configuration](#gateway-configuration)
15. [Docker Deployment](#docker-deployment)
16. [Testing](#testing)
17. [Troubleshooting](#troubleshooting)
18. [Security Considerations](#security-considerations)
19. [Related Documentation](#related-documentation)

---

## Overview

Dhadgar.BetterAuth is a Node.js service that wraps the [Better Auth](https://better-auth.com) library (v1.4.x) to provide:

- **Social OAuth Authentication**: Discord, Google, GitHub, Twitch, Facebook, Apple, Microsoft
- **Email/Password Authentication**: Traditional credential-based login
- **Session Management**: Secure cookie-based sessions with configurable lifetimes
- **Account Linking**: Users can link multiple OAuth providers to a single account
- **Token Exchange**: Issues short-lived exchange tokens that the Identity service converts to JWTs

### Why a Separate Service?

The Meridian Console uses a hybrid identity architecture:

1. **Better Auth (Node.js)** handles user-facing authentication because:
   - Superior OAuth provider support with minimal configuration
   - Built-in account linking and session management
   - Active development with YC backing
   - TypeScript-native with excellent developer experience

2. **Identity Service (.NET)** handles authorization because:
   - Native integration with the rest of the .NET microservices
   - OpenIddict for JWT issuance and validation
   - Complex RBAC, organization management, and permission systems
   - Gaming OAuth providers (Steam, Epic Games, Battle.net, Xbox Live)

This separation follows the principle of using the best tool for each job while maintaining clean boundaries between authentication and authorization concerns.

---

## Architecture

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
          │  • Email/Password         │───▶│  • Token Exchange         │    │  • OAuth Secrets    │
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

### Data Flow

1. **User initiates login** via frontend (e.g., "Sign in with Discord")
2. **Gateway routes** `/api/v1/betterauth/*` to BetterAuth service
3. **BetterAuth** redirects user to OAuth provider, handles callback, creates session
4. **Frontend calls** `/api/v1/betterauth/exchange` to get an exchange token
5. **Frontend calls** `/api/v1/identity/exchange` with the exchange token
6. **Identity service** validates the token, creates/updates user, returns JWT
7. **Frontend uses JWT** for subsequent API calls to all services

---

## Tech Stack

### Core Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| **better-auth** | ^1.3.1 (actual: 1.4.10) | Core authentication library |
| **express** | ^4.19.2 | HTTP server framework |
| **pg** | ^8.12.0 | PostgreSQL client for direct queries |
| **jose** | ^5.6.3 | ES256 JWT signing for exchange tokens |
| **cors** | ^2.8.5 | Cross-origin request handling |
| **dotenv** | ^16.4.5 | Environment variable loading |

### Runtime Environment

| Component | Version |
|-----------|---------|
| Node.js | 22 (Alpine in Docker) |
| ES Modules | `"type": "module"` in package.json |
| Package Manager | npm |

### .NET Integration

The project includes a `.csproj` file that acts as a shim, allowing it to participate in `dotnet build`:

```xml
<!-- Dhadgar.BetterAuth.csproj -->
<Target Name="NpmInstall" BeforeTargets="Build">
  <Exec Command="npm install" WorkingDirectory="$(ProjectDir)" />
</Target>
```

This means running `dotnet build` from the solution root will also install npm dependencies for BetterAuth.

---

## Quick Start

### Prerequisites

- Node.js 22+ (or use Docker)
- PostgreSQL running on localhost:5432
- The Secrets service running (or direct database access in development)
- Identity service running (for token exchange)

### Local Development

1. **Start infrastructure** (PostgreSQL, Redis, RabbitMQ):
   ```bash
   docker compose -f deploy/compose/docker-compose.dev.yml up -d
   ```

2. **Create a `.env` file** from the example:
   ```bash
   cp src/Dhadgar.BetterAuth/.env.example src/Dhadgar.BetterAuth/.env
   ```

3. **Configure secrets** - For local development without Key Vault:
   ```bash
   # In .env file, set these directly (dev only):
   BETTER_AUTH_SECRET=your-32-char-random-string-here
   POSTGRES_PASSWORD=dhadgar
   ```

4. **Install dependencies**:
   ```bash
   cd src/Dhadgar.BetterAuth
   npm install
   ```

5. **Run the service**:
   ```bash
   npm run dev
   # Or: node src/server.js
   ```

6. **Verify it's running**:
   ```bash
   curl http://localhost:5130/healthz
   # Response: {"service":"Dhadgar.BetterAuth","status":"ok"}
   ```

### Via .NET Build

From the solution root:

```bash
dotnet build src/Dhadgar.BetterAuth
# This runs npm install automatically
```

---

## Configuration

### Environment Variables

Configuration is loaded from environment variables, with secrets fetched from the Secrets service at startup.

#### Server Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `PORT` | HTTP server port | `5130` |
| `NODE_ENV` | Environment mode | `development` |

#### Secrets Service Integration

| Variable | Description | Default |
|----------|-------------|---------|
| `SECRETS_SERVICE_URL` | URL of the Secrets service | Required |
| `SERVICE_CLIENT_ID` | OAuth client ID for Secrets auth | `dev-client` |
| `SERVICE_CLIENT_SECRET` | OAuth client secret for Secrets auth | `dev-secret` |

#### Better Auth Core

| Variable | Description | Default |
|----------|-------------|---------|
| `BETTER_AUTH_URL` | Public URL of this service | `http://localhost:5130/api/v1/betterauth` |
| `BETTER_AUTH_TRUSTED_ORIGINS` | Comma-separated allowed origins | (empty = all) |
| `BETTER_AUTH_SECRET` | Secret key for session encryption | Loaded from Secrets service |
| `BETTER_AUTH_CLIENT_APPS` | Allowed client app identifiers | `panel,shop` |

#### Database

| Variable | Description | Default |
|----------|-------------|---------|
| `POSTGRES_HOST` | PostgreSQL host | `localhost` |
| `POSTGRES_PORT` | PostgreSQL port | `5432` |
| `POSTGRES_DATABASE` | Database name | `dhadgar_platform` |
| `POSTGRES_USERNAME` | Database user | `dhadgar` |
| `POSTGRES_PASSWORD` | Database password | Loaded from Secrets service |
| `DATABASE_URL` | Full connection string (auto-built if not set) | Built from above |

#### Exchange Token Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `EXCHANGE_TOKEN_PRIVATE_KEY` | ES256 private key (PEM) | Loaded from Secrets service |
| `EXCHANGE_TOKEN_ISSUER` | JWT issuer claim | `BETTER_AUTH_URL` |
| `EXCHANGE_TOKEN_AUDIENCE` | JWT audience claim | Identity service exchange endpoint |
| `EXCHANGE_TOKEN_KID` | Key ID in JWT header | `betterauth-exchange-v1` |

#### Microsoft OAuth (Federated Credentials)

| Variable | Description | Default |
|----------|-------------|---------|
| `IDENTITY_SERVICE_URL` | URL of Identity service for WIF tokens | `http://localhost:5010` |
| `WIF_CLIENT_ID` | Client ID for federated credentials | `betterauth-client` |
| `WIF_CLIENT_SECRET` | Client secret for WIF token request | (set in production) |

### Example .env File

```bash
# Server
PORT=5130

# Secrets Service (REQUIRED)
SECRETS_SERVICE_URL=http://localhost:5000

# Better Auth
BETTER_AUTH_URL=https://meridianconsole.com/api/v1/betterauth
BETTER_AUTH_TRUSTED_ORIGINS=https://meridianconsole.com,https://panel.meridianconsole.com,http://localhost:4321

# Database
POSTGRES_HOST=localhost
POSTGRES_PORT=5432
POSTGRES_DATABASE=dhadgar_platform
POSTGRES_USERNAME=dhadgar

# Exchange Token
EXCHANGE_TOKEN_AUDIENCE=https://meridianconsole.com/api/v1/identity/exchange
EXCHANGE_TOKEN_ISSUER=https://meridianconsole.com/api/v1/betterauth
EXCHANGE_TOKEN_KID=betterauth-exchange-v1

# Client apps
BETTER_AUTH_CLIENT_APPS=panel,shop
```

---

## OAuth Providers

BetterAuth supports 7 OAuth providers. Configuration is loaded from Azure Key Vault via the Secrets service.

### Supported Providers

| Provider | Config Keys | Scopes |
|----------|-------------|--------|
| **Facebook** | `OAUTH_FACEBOOK_APP_ID`, `OAUTH_FACEBOOK_APP_SECRET` | email, public_profile |
| **Google** | `OAUTH_GOOGLE_CLIENT_ID`, `OAUTH_GOOGLE_CLIENT_SECRET` | openid, profile, email |
| **Discord** | `OAUTH_DISCORD_CLIENT_ID`, `OAUTH_DISCORD_CLIENT_SECRET` | identify, email |
| **Twitch** | `OAUTH_TWITCH_CLIENT_ID`, `OAUTH_TWITCH_CLIENT_SECRET` | user:read:email |
| **GitHub** | `OAUTH_GITHUB_CLIENT_ID`, `OAUTH_GITHUB_CLIENT_SECRET` | read:user, user:email |
| **Apple** | `OAUTH_APPLE_CLIENT_ID`, `OAUTH_APPLE_CLIENT_SECRET` | name, email |
| **Microsoft** | `OAUTH_MICROSOFT_CLIENT_ID` | openid, profile, email, User.Read |

### Setting Up OAuth Providers

#### 1. Discord

1. Go to [Discord Developer Portal](https://discord.com/developers/applications)
2. Create a new application
3. Go to OAuth2 > General
4. Add redirect URI: `https://meridianconsole.com/api/v1/betterauth/callback/discord`
5. Copy Client ID and Client Secret to Key Vault

#### 2. Google

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create OAuth 2.0 credentials
3. Add authorized redirect URI: `https://meridianconsole.com/api/v1/betterauth/callback/google`
4. Copy Client ID and Client Secret to Key Vault

#### 3. GitHub

1. Go to [GitHub Developer Settings](https://github.com/settings/developers)
2. Create a new OAuth App
3. Set callback URL: `https://meridianconsole.com/api/v1/betterauth/callback/github`
4. Copy Client ID and Client Secret to Key Vault

#### 4. Microsoft (Federated Credentials)

Microsoft OAuth uses **federated credentials** instead of a client secret. This provides enhanced security by eliminating secret rotation requirements.

**Setup:**

1. Create an App Registration in [Azure Portal](https://portal.azure.com)
2. Add a Federated Credential:
   - Federated credential scenario: "Other issuer"
   - Issuer: Identity service issuer URL
   - Subject identifier: `betterauth-client`
   - Audience: `api://AzureADTokenExchange`
3. Add redirect URI: `https://meridianconsole.com/api/v1/betterauth/callback/microsoft`
4. Store only `oauth-microsoft-client-id` in Key Vault (no secret needed)

**How it works:**

```
1. User clicks "Sign in with Microsoft"
2. BetterAuth requests WIF token from Identity service
3. Identity issues JWT with sub=betterauth-client
4. BetterAuth uses JWT as client_assertion to Microsoft
5. Microsoft validates JWT against federated credential
6. Microsoft returns access token
```

#### 5. Twitch

1. Go to [Twitch Developer Console](https://dev.twitch.tv/console)
2. Register a new application
3. Add OAuth redirect URL: `https://meridianconsole.com/api/v1/betterauth/callback/twitch`
4. Copy Client ID and Client Secret to Key Vault

#### 6. Facebook

1. Go to [Facebook for Developers](https://developers.facebook.com/)
2. Create a new app
3. Add Facebook Login product
4. Set Valid OAuth Redirect URIs: `https://meridianconsole.com/api/v1/betterauth/callback/facebook`
5. Copy App ID and App Secret to Key Vault

#### 7. Apple

1. Go to [Apple Developer Portal](https://developer.apple.com/)
2. Create an App ID with Sign in with Apple capability
3. Create a Services ID for web authentication
4. Add return URL: `https://meridianconsole.com/api/v1/betterauth/callback/apple`
5. Generate a private key for Sign in with Apple
6. Store Client ID and encoded private key in Key Vault

### Key Vault Secret Names

| Secret Name | Environment Variable |
|-------------|---------------------|
| `oauth-facebook-app-id` | `OAUTH_FACEBOOK_APP_ID` |
| `oauth-facebook-app-secret` | `OAUTH_FACEBOOK_APP_SECRET` |
| `oauth-google-client-id` | `OAUTH_GOOGLE_CLIENT_ID` |
| `oauth-google-client-secret` | `OAUTH_GOOGLE_CLIENT_SECRET` |
| `oauth-discord-client-id` | `OAUTH_DISCORD_CLIENT_ID` |
| `oauth-discord-client-secret` | `OAUTH_DISCORD_CLIENT_SECRET` |
| `oauth-twitch-client-id` | `OAUTH_TWITCH_CLIENT_ID` |
| `oauth-twitch-client-secret` | `OAUTH_TWITCH_CLIENT_SECRET` |
| `oauth-github-client-id` | `OAUTH_GITHUB_CLIENT_ID` |
| `oauth-github-client-secret` | `OAUTH_GITHUB_CLIENT_SECRET` |
| `oauth-apple-client-id` | `OAUTH_APPLE_CLIENT_ID` |
| `oauth-apple-client-secret` | `OAUTH_APPLE_CLIENT_SECRET` |
| `oauth-microsoft-client-id` | `OAUTH_MICROSOFT_CLIENT_ID` |

---

## Authentication Flow

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
     │                │                  │                  │               │
```

### Email/Password Flow

1. **Sign Up**: `POST /api/v1/betterauth/sign-up/email`
   ```json
   { "email": "user@example.com", "password": "secure123", "name": "John Doe" }
   ```

2. **Sign In**: `POST /api/v1/betterauth/sign-in/email`
   ```json
   { "email": "user@example.com", "password": "secure123" }
   ```

3. **Get Exchange Token**: `POST /api/v1/betterauth/exchange`
   - Requires valid session cookie
   - Returns `{ exchangeToken: "..." }`

4. **Exchange for JWT**: `POST /api/v1/identity/exchange`
   - Returns `{ accessToken, refreshToken, expiresIn, userId }`

---

## Session Management

BetterAuth manages sessions using secure HTTP-only cookies.

### Session Configuration

From `auth.js`:

```javascript
session: {
  // Session expires after 7 days of inactivity
  expiresIn: 60 * 60 * 24 * 7, // 604800 seconds

  // Session is refreshed if older than 24 hours
  updateAge: 60 * 60 * 24, // 86400 seconds

  // Client-side session caching
  cookieCache: {
    enabled: true,
    maxAge: 60 * 5 // 5 minutes
  }
}
```

### Cookie Configuration

```javascript
advanced: {
  // Share cookies across subdomains
  crossSubDomainCookies: {
    enabled: true,
    domain: "meridianconsole.com"
  },
  defaultCookieAttributes: {
    sameSite: "none",    // Required for cross-origin
    secure: true,        // HTTPS only
    httpOnly: true,      // Not accessible via JavaScript
    partitioned: true    // Third-party cookie support
  }
}
```

### Session Cookie Names

| Cookie | Purpose |
|--------|---------|
| `better-auth.session_token` | Session identifier |
| `better-auth.state` | OAuth state for CSRF protection |

### Checking Session Status

```javascript
// Frontend: Check if user has active session
const session = await authClient.getSession();
if (session?.user) {
  // User is authenticated
}
```

---

## Token Exchange

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

### Exchange Token Properties

| Property | Description |
|----------|-------------|
| **Algorithm** | ES256 (ECDSA with P-256 curve) |
| **Lifetime** | 60 seconds (hard-coded) |
| **Single-use** | Identity service tracks JTI to prevent replay |
| **Client App** | Resolved from origin or request body |
| **Providers** | All linked OAuth accounts for the user |

### Generating Exchange Tokens

The `/api/v1/betterauth/exchange` endpoint:

1. Validates the session cookie
2. Fetches all linked accounts for the user from the database
3. Determines the current provider (most recently used)
4. Signs a JWT with the ES256 private key
5. Returns the token

### Client App Resolution

The `client_app` claim is resolved using this priority:

1. **Origin header hostname matching:**
   - `panel.meridianconsole.com` → `"panel"`
   - `cart.meridianconsole.com` → `"shop"`
   - `meridianconsole.com` or `www.meridianconsole.com` → `"shop"`

2. **Request body `clientApp` field** (if in allowed list)

3. **Default:** `"unknown"`

---

## Integration with Identity Service

### Token Exchange Endpoint

The Identity service provides `/exchange` to convert BetterAuth exchange tokens to JWTs:

```http
POST /api/v1/identity/exchange
Content-Type: application/json

{
  "exchangeToken": "eyJhbGciOiJFUzI1NiIs..."
}
```

**Response:**
```json
{
  "accessToken": "eyJhbGciOiJSUzI1NiIs...",
  "refreshToken": "opaque-refresh-token",
  "expiresIn": 900,
  "userId": "guid",
  "organizationId": "guid"
}
```

### Identity Service Validation

The Identity service validates exchange tokens by:

1. **Signature verification** using the ES256 public key (from Key Vault or config)
2. **Issuer validation** (`iss` must match BetterAuth URL)
3. **Audience validation** (`aud` must match Identity exchange endpoint)
4. **Lifetime validation** (token must not be expired)
5. **Replay prevention** (JTI must not have been used before)

### User Synchronization

When a token is exchanged, Identity service:

1. Looks up or creates a user based on email
2. Updates user profile (name, picture) if changed
3. Links OAuth provider accounts
4. Assigns default organization if new user
5. Issues JWT with user permissions

---

## API Endpoints

### Better Auth Standard Endpoints

All Better Auth endpoints are mounted at `/api/v1/betterauth/`:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/sign-up/email` | POST | Register with email/password |
| `/sign-in/email` | POST | Sign in with email/password |
| `/sign-in/social` | GET | Initiate OAuth flow |
| `/callback/:provider` | GET | OAuth callback handler |
| `/sign-out` | POST | Sign out (clear session) |
| `/session` | GET | Get current session |
| `/user` | GET | Get current user info |
| `/update-user` | POST | Update user profile |
| `/change-password` | POST | Change password |
| `/forgot-password` | POST | Initiate password reset |
| `/reset-password` | POST | Complete password reset |

### Custom Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/healthz` | GET | Health check |
| `/api/v1/betterauth/exchange` | POST | Issue exchange token |

### Health Check

```bash
curl http://localhost:5130/healthz
```

Response:
```json
{
  "service": "Dhadgar.BetterAuth",
  "status": "ok"
}
```

### Exchange Token Endpoint

```bash
curl -X POST http://localhost:5130/api/v1/betterauth/exchange \
  -H "Cookie: better-auth.session_token=..." \
  -H "Content-Type: application/json" \
  -d '{"clientApp": "panel"}'
```

Response:
```json
{
  "exchangeToken": "eyJhbGciOiJFUzI1NiIsImtpZCI6ImJldHRlcmF1dGgtZXhjaGFuZ2UtdjEifQ..."
}
```

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

### Auto-Migration

BetterAuth automatically runs migrations on startup:

```javascript
const { toBeCreated, toBeAdded, runMigrations } = await getMigrations(authConfig);

if (toBeCreated.length > 0 || toBeAdded.length > 0) {
  await runMigrations();
}
```

---

## Secrets Management

BetterAuth integrates with the Dhadgar.Secrets service to retrieve sensitive configuration.

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
         │                       │                       │
```

### Required Secrets

These secrets **must** exist in Key Vault for BetterAuth to start:

| Key Vault Name | Purpose |
|----------------|---------|
| `betterauth-secret` | Session encryption key |
| `betterauth-exchange-private-key` | ES256 private key for exchange tokens |

### Secret Categories

The secrets client fetches three categories:

1. **BetterAuth secrets** (`/api/v1/secrets/betterauth`)
   - `betterauth-secret`
   - `betterauth-exchange-private-key`

2. **OAuth secrets** (`/api/v1/secrets/oauth`)
   - All `oauth-*` secrets

3. **Infrastructure secrets** (`/api/v1/secrets/infrastructure`)
   - `postgres-password` (optional, for production)

---

## Gateway Configuration

The Gateway (YARP) routes BetterAuth traffic:

### Route Configuration

From `appsettings.json`:

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

### Key Points

- **AuthorizationPolicy: Anonymous** - No JWT required (handles its own auth)
- **RateLimiterPolicy: Auth** - 30 requests per minute per IP
- **Health checks** - Every 30 seconds via `/healthz`
- **No path transform** - Requests pass through as-is

---

## Docker Deployment

### Dockerfile

```dockerfile
FROM node:22-alpine AS base
WORKDIR /app

# Install dependencies
FROM base AS deps
COPY package*.json ./
RUN npm ci --omit=dev

# Production image
FROM base AS runner
ENV NODE_ENV=production
ENV PORT=8080

COPY --from=deps /app/node_modules ./node_modules
COPY src ./src
COPY package.json ./

RUN addgroup --system --gid 1001 nodejs && \
    adduser --system --uid 1001 betterauth && \
    chown -R betterauth:nodejs /app

USER betterauth

EXPOSE 8080

CMD ["node", "src/server.js"]
```

### Docker Compose Service

```yaml
betterauth:
  build:
    context: ../..
    dockerfile: src/Dhadgar.BetterAuth/Dockerfile
  environment:
    NODE_ENV: production
    PORT: 8080
    SECRETS_SERVICE_URL: http://secrets:8080
    IDENTITY_SERVICE_URL: http://identity:8080
    SERVICE_CLIENT_ID: betterauth-service
    SERVICE_CLIENT_SECRET: ${BETTERAUTH_SERVICE_CLIENT_SECRET}
    BETTER_AUTH_URL: https://dev.meridianconsole.com/api/v1/betterauth
    BETTER_AUTH_TRUSTED_ORIGINS: https://dev.meridianconsole.com,https://panel.meridianconsole.com
    POSTGRES_HOST: postgres
    POSTGRES_PORT: 5432
    POSTGRES_DATABASE: dhadgar_platform
    POSTGRES_USERNAME: dhadgar
    POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
  ports:
    - "5130:8080"
  depends_on:
    postgres:
      condition: service_healthy
    secrets:
      condition: service_started
```

### Building the Image

```bash
# From repository root
docker build -f src/Dhadgar.BetterAuth/Dockerfile -t dhadgar/betterauth .

# Or via docker-compose
docker compose -f deploy/compose/docker-compose.services.yml build betterauth
```

---

## Testing

### Unit Testing

Currently, there is no dedicated test project for BetterAuth. Testing recommendations:

1. **Install test dependencies:**
   ```bash
   npm install --save-dev vitest @types/node
   ```

2. **Create test files** in `src/__tests__/`:
   ```javascript
   // src/__tests__/exchange.test.js
   import { describe, it, expect } from 'vitest';
   import { createExchangeToken } from '../exchange.js';

   describe('createExchangeToken', () => {
     it('should create a valid JWT', async () => {
       // Test implementation
     });
   });
   ```

3. **Add test script** to `package.json`:
   ```json
   {
     "scripts": {
       "test": "vitest"
     }
   }
   ```

### Integration Testing

Test the full authentication flow:

```bash
# 1. Start all services
docker compose -f deploy/compose/docker-compose.services.yml up -d

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

### Health Check Testing

```bash
# Local
curl http://localhost:5130/healthz

# Via Gateway
curl http://localhost:5000/api/v1/betterauth/healthz
```

---

## Troubleshooting

### Common Issues

#### "SECRETS_SERVICE_URL is required"

**Cause:** The `SECRETS_SERVICE_URL` environment variable is not set.

**Solution:**
```bash
export SECRETS_SERVICE_URL=http://localhost:5000
# Or in .env file
SECRETS_SERVICE_URL=http://localhost:5000
```

#### "BetterAuth cannot start: missing required secrets"

**Cause:** Required secrets (`betterauth-secret`, `betterauth-exchange-private-key`) are not in Key Vault.

**Solution:**
1. Check Key Vault has the secrets:
   ```bash
   az keyvault secret show --vault-name mc-secrets-gbl --name betterauth-secret
   ```
2. Ensure the Secrets service has access to Key Vault
3. Verify service account has `secrets:read:betterauth-*` permission

#### "DATABASE_URL is required for Better Auth"

**Cause:** Database connection string not set and couldn't be built.

**Solution:**
```bash
# Set individual components
export POSTGRES_HOST=localhost
export POSTGRES_PORT=5432
export POSTGRES_DATABASE=dhadgar_platform
export POSTGRES_USERNAME=dhadgar
export POSTGRES_PASSWORD=dhadgar

# Or set DATABASE_URL directly
export DATABASE_URL=postgresql://dhadgar:dhadgar@localhost:5432/dhadgar_platform
```

#### OAuth Provider Not Working

**Symptoms:** "Provider not configured" error or empty provider list.

**Diagnosis:**
1. Check startup logs for provider configuration:
   ```
   OAuth providers: discord, google, github
   ```

2. Verify secrets are loaded:
   ```bash
   curl http://localhost:5110/api/v1/secrets/oauth \
     -H "Authorization: Bearer <token>"
   ```

3. Ensure redirect URIs match exactly in provider console

#### "Origin not allowed" CORS Error

**Cause:** Frontend origin not in `BETTER_AUTH_TRUSTED_ORIGINS`.

**Solution:**
```bash
export BETTER_AUTH_TRUSTED_ORIGINS=https://panel.meridianconsole.com,http://localhost:4321
```

#### Exchange Token Invalid

**Symptoms:** Identity service returns 401 when exchanging token.

**Diagnosis:**
1. Check token isn't expired (60s lifetime)
2. Verify issuer/audience match between services
3. Confirm public key is correctly loaded in Identity service
4. Check for clock skew between services (max 30s allowed)

#### Session Cookie Not Set

**Symptoms:** After OAuth callback, no session cookie is present.

**Possible causes:**
1. **Cross-origin issues:** Ensure `SameSite=None` and `Secure=true` are set
2. **Domain mismatch:** Cookie domain must match request domain
3. **Browser privacy:** Some browsers block third-party cookies

### Debugging

#### Enable Debug Logging

```bash
export DEBUG=better-auth:*
node src/server.js
```

#### Check Service Health

```bash
# BetterAuth
curl http://localhost:5130/healthz

# Secrets Service
curl http://localhost:5110/healthz

# Identity Service
curl http://localhost:5010/healthz
```

#### View Database State

```sql
-- Check users
SELECT * FROM "user" ORDER BY "createdAt" DESC LIMIT 10;

-- Check sessions
SELECT * FROM "session" WHERE "expiresAt" > NOW();

-- Check linked accounts
SELECT u.email, a."providerId", a."accountId"
FROM "user" u
JOIN "account" a ON a."userId" = u.id;
```

---

## Security Considerations

### Session Security

- **HTTP-only cookies:** Session tokens are not accessible via JavaScript
- **Secure flag:** Cookies only sent over HTTPS
- **SameSite=None:** Required for cross-origin, but with `Partitioned` flag
- **7-day expiration:** Sessions expire after inactivity

### Exchange Token Security

- **ES256 signing:** ECDSA with P-256 curve (FIPS 186-4 compliant)
- **60-second lifetime:** Minimizes window for token theft
- **Single-use JTI:** Identity service tracks used tokens
- **Audience restriction:** Token only valid for Identity service

### OAuth Security

- **State parameter:** CSRF protection for OAuth flows
- **PKCE (planned):** Code verifier for public clients
- **Account linking:** Only for verified email addresses
- **Federated credentials:** Microsoft OAuth uses JWT assertions instead of secrets

### Secrets Management

- **No hardcoded secrets:** All sensitive values from Key Vault
- **Service account auth:** BetterAuth uses dedicated client credentials
- **Minimal permissions:** Service account only has `secrets:read:betterauth-*` and `secrets:read:oauth-*`

### Rate Limiting

Via Gateway:
- **30 requests/minute** per IP for authentication endpoints
- Protects against brute force and credential stuffing

---

## Related Documentation

### Internal Documentation

- [Identity Service README](../Dhadgar.Identity/README.md) - JWT issuance, RBAC
- [Identity API Reference](../../docs/identity-api-reference.md) - Complete API docs
- [Identity Implementation Plan](../../docs/implementation-plans/identity-service.md) - Architecture details
- [Identity Webhooks](../../docs/identity-webhooks.md) - Event notifications
- [Secrets Service Analysis](../../docs/SECRETS_SERVICE_ANALYSIS.md) - Secrets management
- [Authentication Analysis](../../docs/architecture/authentication-analysis.md) - Technology comparison

### External Documentation

- [Better Auth Documentation](https://better-auth.com/docs) - Official docs
- [Better Auth GitHub](https://github.com/better-auth/better-auth) - Source code
- [jose Library](https://github.com/panva/jose) - JWT signing library
- [Express.js](https://expressjs.com/) - HTTP server framework

### Configuration Files

- `.env.example` - Environment variable template
- `package.json` - NPM dependencies
- `Dockerfile` - Container build configuration
- `docker-compose.services.yml` - Docker Compose service definition

---

## Changelog

### v0.1.0 (Initial)

- Better Auth integration with Express.js
- OAuth providers: Discord, Google, GitHub, Twitch, Facebook, Apple, Microsoft
- Email/password authentication
- Exchange token system for Identity service integration
- Secrets service integration for credential management
- Microsoft federated credentials support
- Account linking across providers
- Cross-subdomain cookie support

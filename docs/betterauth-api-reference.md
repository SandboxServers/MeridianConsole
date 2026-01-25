# BetterAuth API Reference

**Service:** Dhadgar.BetterAuth
**Port:** 5130 (development) / 8080 (container)
**Base Path:** `/api/v1/betterauth`
**Gateway Route:** `https://meridianconsole.com/api/v1/betterauth/*`

---

## Table of Contents

1. [Overview](#overview)
2. [Authentication Flows](#authentication-flows)
3. [Better Auth Standard Endpoints](#better-auth-standard-endpoints)
4. [Custom Endpoints](#custom-endpoints)
5. [Session Endpoints](#session-endpoints)
6. [OAuth Endpoints](#oauth-endpoints)
7. [Error Responses](#error-responses)
8. [Integration Examples](#integration-examples)
9. [Client Libraries](#client-libraries)

---

## Overview

BetterAuth provides user-facing authentication for Meridian Console. All endpoints are proxied through the Gateway at `/api/v1/betterauth/*`.

### Important Notes

- **Session-based authentication**: BetterAuth uses HTTP-only cookies for session management
- **Exchange tokens**: To get a JWT for API calls, use the `/exchange` endpoint to get a short-lived token, then exchange it with the Identity service
- **Cross-origin support**: Cookies are configured with `SameSite=None` and `Secure=true` for cross-origin flows

---

## Authentication Flows

### OAuth Login Flow (Passwordless)

Meridian Console is **100% passwordless**. All user authentication is via OAuth providers.

```text
1. Redirect user to:    GET /api/v1/betterauth/sign-in/social?provider=discord&callbackURL=...
2. User authenticates with provider
3. Provider redirects to: GET /api/v1/betterauth/callback/discord?code=...
4. BetterAuth sets session cookie, redirects to callbackURL
5. Frontend calls:      POST /api/v1/betterauth/exchange
6. Returns:             { exchangeToken: "..." }
7. Frontend calls:      POST /api/v1/identity/exchange { exchangeToken: "..." }
8. Returns:             { accessToken, refreshToken, expiresIn, userId }
```

---

## Better Auth Standard Endpoints

These endpoints are provided by the Better Auth SDK. Full documentation: [better-auth.com/docs](https://www.better-auth.com/docs)

> **Note:** Meridian Console is **100% passwordless**. Email/password endpoints are disabled. All authentication is via OAuth providers.

### User Management

#### Get Current User

Returns the currently authenticated user.

```http
GET /api/v1/betterauth/user
Cookie: better-auth.session_token=...
```

**Response (200 OK):**

```json
{
  "user": {
    "id": "uuid",
    "email": "user@example.com",
    "name": "John Doe",
    "image": "https://cdn.example.com/avatar.jpg",
    "emailVerified": true,
    "createdAt": "2024-01-01T00:00:00.000Z",
    "updatedAt": "2024-01-01T00:00:00.000Z"
  }
}
```

---

#### Update User

Updates the current user's profile.

```http
POST /api/v1/betterauth/update-user
Content-Type: application/json
Cookie: better-auth.session_token=...
```

**Request Body:**

```json
{
  "name": "Jane Doe",
  "image": "https://cdn.example.com/new-avatar.jpg"
}
```

**Response (200 OK):**

```json
{
  "user": {
    "id": "uuid",
    "email": "user@example.com",
    "name": "Jane Doe",
    "image": "https://cdn.example.com/new-avatar.jpg",
    "emailVerified": true,
    "createdAt": "2024-01-01T00:00:00.000Z",
    "updatedAt": "2024-01-02T00:00:00.000Z"
  }
}
```

---

## Session Endpoints

### Get Current Session

Returns the current session and user info.

```http
GET /api/v1/betterauth/session
Cookie: better-auth.session_token=...
```

**Response (200 OK):**

```json
{
  "session": {
    "id": "session-id",
    "userId": "uuid",
    "expiresAt": "2024-01-08T00:00:00.000Z",
    "createdAt": "2024-01-01T00:00:00.000Z",
    "updatedAt": "2024-01-01T00:00:00.000Z"
  },
  "user": {
    "id": "uuid",
    "email": "user@example.com",
    "name": "John Doe",
    "image": null,
    "emailVerified": false,
    "createdAt": "2024-01-01T00:00:00.000Z",
    "updatedAt": "2024-01-01T00:00:00.000Z"
  }
}
```

**Response (401 Unauthorized):**

```json
{
  "session": null,
  "user": null
}
```

---

### Sign Out

Terminates the current session.

```http
POST /api/v1/betterauth/sign-out
Cookie: better-auth.session_token=...
```

**Response (200 OK):**

```json
{
  "status": true
}
```

**Effect:** Clears the `better-auth.session_token` cookie.

---

## OAuth Endpoints

### Initiate OAuth Flow

Redirects the user to the OAuth provider for authentication.

```http
GET /api/v1/betterauth/sign-in/social?provider={provider}&callbackURL={callbackURL}
```

**Query Parameters:**

| Parameter     | Required | Description                                                                               |
| ------------- | -------- | ----------------------------------------------------------------------------------------- |
| `provider`    | Yes      | OAuth provider: `discord`, `google`, `github`, `twitch`, `facebook`, `apple`, `microsoft` |
| `callbackURL` | No       | URL to redirect after authentication (default: origin)                                    |

**Example:**

```http
GET /api/v1/betterauth/sign-in/social?provider=discord&callbackURL=https://panel.meridianconsole.com/dashboard
```

**Response (302 Found):**

```http
Location: https://discord.com/oauth2/authorize?client_id=...&redirect_uri=...&scope=identify%20email&state=...
```

---

### OAuth Callback

Handles the OAuth provider callback. This endpoint is called by the OAuth provider, not by the frontend directly.

```http
GET /api/v1/betterauth/callback/{provider}?code={code}&state={state}
```

**Path Parameters:**

| Parameter  | Description         |
| ---------- | ------------------- |
| `provider` | OAuth provider name |

**Query Parameters:**

| Parameter | Description                            |
| --------- | -------------------------------------- |
| `code`    | Authorization code from OAuth provider |
| `state`   | State parameter for CSRF protection    |

**Response (302 Found):**

```
Location: {callbackURL}
Set-Cookie: better-auth.session_token=...; HttpOnly; Secure; SameSite=None; Path=/
```

---

### Supported OAuth Providers

| Provider  | Provider ID | Scopes                            |
| --------- | ----------- | --------------------------------- |
| Discord   | `discord`   | identify, email                   |
| Google    | `google`    | openid, profile, email            |
| GitHub    | `github`    | read:user, user:email             |
| Twitch    | `twitch`    | user:read:email                   |
| Facebook  | `facebook`  | email, public_profile             |
| Apple     | `apple`     | name, email                       |
| Microsoft | `microsoft` | openid, profile, email, User.Read |

---

## Custom Endpoints

### Health Check

Returns the service health status.

```http
GET /healthz
```

**Response (200 OK):**

```json
{
  "service": "Dhadgar.BetterAuth",
  "status": "ok"
}
```

---

### Exchange Token

Issues a short-lived exchange token for the Identity service. This token can be exchanged for a JWT.

```http
POST /api/v1/betterauth/exchange
Content-Type: application/json
Cookie: better-auth.session_token=...
```

**Request Body (optional):**

```json
{
  "clientApp": "panel"
}
```

**Response (200 OK):**

```json
{
  "exchangeToken": "eyJhbGciOiJFUzI1NiIsImtpZCI6ImJldHRlcmF1dGgtZXhjaGFuZ2UtdjEifQ.eyJzdWIiOiJ1c2VyLXV1aWQiLCJlbWFpbCI6InVzZXJAZXhhbXBsZS5jb20iLCJuYW1lIjoiSm9obiBEb2UiLCJwaWN0dXJlIjpudWxsLCJwdXJwb3NlIjoidG9rZW5fZXhjaGFuZ2UiLCJjbGllbnRfYXBwIjoicGFuZWwiLCJwcm92aWRlciI6ImRpc2NvcmQiLCJwcm92aWRlcnMiOlt7InByb3ZpZGVySWQiOiJkaXNjb3JkIiwiYWNjb3VudElkIjoiMTIzNDU2Nzg5In1dLCJpc3MiOiJodHRwczovL21lcmlkaWFuY29uc29sZS5jb20vYXBpL3YxL2JldHRlcmF1dGgiLCJhdWQiOiJodHRwczovL21lcmlkaWFuY29uc29sZS5jb20vYXBpL3YxL2lkZW50aXR5L2V4Y2hhbmdlIiwiaWF0IjoxNzA0MDY3MjAwLCJleHAiOjE3MDQwNjcyNjAsImp0aSI6InVuaXF1ZS10b2tlbi1pZCJ9.signature"
}
```

**Exchange Token Claims:**

| Claim        | Description                                         |
| ------------ | --------------------------------------------------- |
| `sub`        | User ID                                             |
| `email`      | User email                                          |
| `name`       | User display name                                   |
| `picture`    | User profile image URL                              |
| `purpose`    | Always `"token_exchange"`                           |
| `client_app` | Resolved client app (`panel`, `shop`, or `unknown`) |
| `provider`   | Most recently used OAuth provider                   |
| `providers`  | Array of all linked OAuth providers                 |
| `iss`        | Issuer (BetterAuth URL)                             |
| `aud`        | Audience (Identity exchange endpoint)               |
| `exp`        | Expiration (60 seconds from issue)                  |
| `jti`        | Unique token ID (prevents replay)                   |

**Response (401 Unauthorized):**

```json
{
  "error": "unauthorized"
}
```

**Response (500 Internal Server Error):**

```json
{
  "error": "exchange_failed"
}
```

---

## Error Responses

### Standard Error Format

All errors follow a consistent format:

```json
{
  "error": "error_code",
  "message": "Human-readable description"
}
```

### Common Error Codes

| Status | Error Code        | Description                           |
| ------ | ----------------- | ------------------------------------- |
| 400    | `invalid_request` | Missing or invalid request parameters |
| 401    | `unauthorized`    | No valid session cookie               |
| 403    | `forbidden`       | User lacks permission                 |
| 404    | `not_found`       | Resource not found                    |
| 429    | `rate_limited`    | Too many requests                     |
| 500    | `internal_error`  | Server error                          |

### OAuth Error Responses

OAuth errors include additional context:

```json
{
  "error": "oauth_error",
  "provider": "discord",
  "message": "Failed to exchange authorization code"
}
```

---

## Integration Examples

### React/TypeScript with Better Auth Client

```typescript
import { createAuthClient } from "better-auth/client";

const authClient = createAuthClient({
  baseURL: "https://meridianconsole.com/api/v1/betterauth",
});

// Check session
const session = await authClient.getSession();
if (session?.user) {
  console.log("Logged in as:", session.user.email);
}

// Sign in with OAuth (passwordless)
await authClient.signIn.social({
  provider: "discord",
  callbackURL: window.location.origin + "/dashboard",
});

// Sign out
await authClient.signOut();
```

### Vanilla JavaScript

```javascript
// Check session status
async function checkSession() {
  const response = await fetch(
    "https://meridianconsole.com/api/v1/betterauth/session",
    {
      credentials: "include",
    },
  );
  const data = await response.json();
  return data.session ? data.user : null;
}

// Get exchange token for JWT (after OAuth login)
async function getExchangeToken() {
  const response = await fetch(
    "https://meridianconsole.com/api/v1/betterauth/exchange",
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ clientApp: "panel" }),
      credentials: "include",
    },
  );
  const data = await response.json();
  return data.exchangeToken;
}

// Exchange for JWT
async function getJWT() {
  const exchangeToken = await getExchangeToken();
  const response = await fetch(
    "https://meridianconsole.com/api/v1/identity/exchange",
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ exchangeToken }),
    },
  );
  return response.json(); // { accessToken, refreshToken, expiresIn, userId }
}

// Start OAuth flow
function signInWithDiscord() {
  const callbackURL = encodeURIComponent(window.location.origin + "/dashboard");
  window.location.href = `https://meridianconsole.com/api/v1/betterauth/sign-in/social?provider=discord&callbackURL=${callbackURL}`;
}
```

### Astro/React Component

```tsx
// components/AuthProvider.tsx
import { createAuthClient } from "better-auth/client";
import { createContext, useContext, useEffect, useState } from "react";

const authClient = createAuthClient({
  baseURL: import.meta.env.PUBLIC_BETTERAUTH_URL,
});

const AuthContext = createContext<{
  user: any | null;
  loading: boolean;
  signIn: (provider: string) => void;
  signOut: () => Promise<void>;
}>({
  user: null,
  loading: true,
  signIn: () => {},
  signOut: async () => {},
});

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<any | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    authClient.getSession().then((session) => {
      setUser(session?.user || null);
      setLoading(false);
    });
  }, []);

  const signIn = (provider: string) => {
    authClient.signIn.social({
      provider,
      callbackURL: window.location.href,
    });
  };

  const signOut = async () => {
    await authClient.signOut();
    setUser(null);
  };

  return (
    <AuthContext.Provider value={{ user, loading, signIn, signOut }}>
      {children}
    </AuthContext.Provider>
  );
}

export const useAuth = () => useContext(AuthContext);
```

### cURL Examples

```bash
# Note: Meridian Console is 100% passwordless
# Users authenticate via OAuth providers (Discord, Google, GitHub, etc.)
# After OAuth callback, use these endpoints:

# Check session
curl https://meridianconsole.com/api/v1/betterauth/session \
  -b cookies.txt

# Get exchange token
curl -X POST https://meridianconsole.com/api/v1/betterauth/exchange \
  -H "Content-Type: application/json" \
  -d '{"clientApp":"panel"}' \
  -b cookies.txt

# Exchange for JWT (call Identity service)
EXCHANGE_TOKEN="eyJ..."
curl -X POST https://meridianconsole.com/api/v1/identity/exchange \
  -H "Content-Type: application/json" \
  -d "{\"exchangeToken\":\"$EXCHANGE_TOKEN\"}"

# Sign out
curl -X POST https://meridianconsole.com/api/v1/betterauth/sign-out \
  -b cookies.txt
```

---

## Client Libraries

### Official Better Auth Client

```bash
npm install better-auth
```

```typescript
import { createAuthClient } from "better-auth/client";

const authClient = createAuthClient({
  baseURL: "https://meridianconsole.com/api/v1/betterauth",
});
```

### React Hooks

```bash
npm install better-auth
```

```typescript
import { createAuthClient } from "better-auth/react";

const { useSession, signIn, signOut } = createAuthClient({
  baseURL: "https://meridianconsole.com/api/v1/betterauth"
});

function Component() {
  const { data: session, isPending } = useSession();

  if (isPending) return <div>Loading...</div>;
  if (!session) return <button onClick={() => signIn.social({ provider: "discord" })}>Login</button>;

  return <div>Hello, {session.user.name}</div>;
}
```

---

## Rate Limits

Authentication endpoints are rate-limited via the Gateway:

| Endpoint Pattern       | Limit       | Window   |
| ---------------------- | ----------- | -------- |
| `/api/v1/betterauth/*` | 30 requests | 1 minute |

Rate limit headers:

```
X-RateLimit-Limit: 30
X-RateLimit-Remaining: 29
X-RateLimit-Reset: 1704067260
```

---

## Related Documentation

- [BetterAuth Service Implementation Plan](./implementation-plans/betterauth-service.md)
- [Identity Service Implementation Plan](./implementation-plans/identity-service.md)
- [Better Auth Official Docs](https://www.better-auth.com/docs)
- [Gateway Service Implementation Plan](./implementation-plans/gateway-service.md)

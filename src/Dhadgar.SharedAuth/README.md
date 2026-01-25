# Dhadgar.SharedAuth

**Shared Authentication Client Library for Meridian Console Frontend Applications**

This TypeScript library provides a unified authentication client, token management utilities, and OAuth provider configuration for all Meridian Console frontend applications. It is designed to work with the BetterAuth authentication backend and the Identity service token exchange system.

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Tech Stack](#tech-stack)
4. [Installation](#installation)
5. [Package Contents](#package-contents)
6. [Auth Client API](#auth-client-api)
7. [Token Storage](#token-storage)
8. [OAuth Providers](#oauth-providers)
9. [Sign-In Categories](#sign-in-categories)
10. [TypeScript Types](#typescript-types)
11. [Usage Examples](#usage-examples)
12. [Configuration](#configuration)
13. [Security Considerations](#security-considerations)
14. [Integration with Backend Services](#integration-with-backend-services)
15. [Related Documentation](#related-documentation)

---

## Overview

Dhadgar.SharedAuth is the centralized authentication library used by Meridian Console's frontend applications (ShoppingCart, Panel). It provides:

- **Authentication Client Factory**: Creates configured auth clients for different frontend apps
- **Token Management**: Secure storage and lifecycle management of JWT tokens
- **OAuth Provider Metadata**: Brand colors, icons, and configuration for 17+ OAuth providers
- **Sign-In Categories**: UI-agnostic grouping of providers for sign-in page layouts
- **Type Definitions**: Full TypeScript support with exported interfaces

The library is designed as a **source-only package** - it is not compiled or published to npm. Instead, consuming applications reference it directly via file path and their bundler (Vite/Astro) compiles the TypeScript source at build time. This eliminates version synchronization issues and simplifies the monorepo development workflow.

---

## Architecture

### Authentication Flow

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   Frontend App  │     │   BetterAuth    │     │    Identity     │
│  (Panel/Cart)   │     │    Service      │     │    Service      │
└────────┬────────┘     └────────┬────────┘     └────────┬────────┘
         │                       │                       │
         │  1. signIn()          │                       │
         │──────────────────────>│                       │
         │                       │                       │
         │  2. Redirect URL      │                       │
         │<──────────────────────│                       │
         │                       │                       │
         │  3. User authenticates with OAuth provider    │
         │  ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ >│
         │                       │                       │
         │  4. OAuth callback    │                       │
         │──────────────────────>│                       │
         │                       │                       │
         │  5. Session created   │                       │
         │<──────────────────────│                       │
         │                       │                       │
         │  6. exchangeTokens()  │                       │
         │──────────────────────>│                       │
         │                       │  7. Exchange token    │
         │                       │──────────────────────>│
         │                       │                       │
         │                       │  8. JWT tokens        │
         │                       │<──────────────────────│
         │                       │                       │
         │  9. Access + Refresh tokens stored            │
         │<──────────────────────────────────────────────│
         │                       │                       │
```

### Dual-Token Architecture

Meridian Console uses a **hybrid identity system**:

1. **BetterAuth Session** (Cookie-based)
   - Handles OAuth flows with social/gaming providers
   - Manages browser session via HTTP-only cookies
   - Session stored server-side in PostgreSQL

2. **Identity JWT Tokens** (sessionStorage)
   - Access token for API authorization (short-lived)
   - Refresh token for token renewal (longer-lived)
   - Stored client-side in `sessionStorage`
   - Used for `Authorization: Bearer` headers

The `exchangeTokens()` method bridges these two systems by converting a valid BetterAuth session into Identity JWT tokens.

---

## Tech Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| TypeScript | ^5.7.2 | Type-safe JavaScript |
| ES2022 | - | Target compilation level |
| ESNext Modules | - | Native ES module format |
| DOM APIs | - | Browser `sessionStorage`, `fetch`, `window.location` |

### Build Configuration

The package uses a **source-only** distribution model:

```json
{
  "main": "./src/index.ts",
  "types": "./src/index.ts",
  "exports": {
    ".": {
      "import": "./src/index.ts",
      "types": "./src/index.ts"
    }
  }
}
```

**Why source-only?**
- No build step required for the library itself
- Consuming bundlers (Vite, esbuild) handle TypeScript compilation
- Changes to SharedAuth are immediately available without rebuilding
- Tree-shaking works optimally since bundlers see the original source

---

## Installation

### For Consuming Applications (Panel, ShoppingCart)

Add the package as a file dependency in your `package.json`:

```json
{
  "dependencies": {
    "@dhadgar/shared-auth": "file:../Dhadgar.SharedAuth"
  }
}
```

Then install dependencies:

```bash
npm install
```

### TypeScript Configuration

Consuming applications should have compatible TypeScript settings:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "lib": ["ES2022", "DOM", "DOM.Iterable"],
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true
  }
}
```

### Type Checking the Library

To validate the library's TypeScript:

```bash
cd src/Dhadgar.SharedAuth
npm install
npm run typecheck
```

---

## Package Contents

### Exports Summary

```typescript
// Main exports from '@dhadgar/shared-auth'

// Functions
export { createAuthClient } from './client';
export { tokenStorage } from './storage';
export { getProvider, getProvidersByCategory, getSignInCategory } from './types';

// Constants
export { OAUTH_PROVIDERS, OAUTH_CATEGORIES, SIGN_IN_CATEGORIES } from './types';

// Types
export type {
  User,
  Session,
  AuthTokens,
  AuthState,
  OAuthProvider,
  OAuthCategory,
  SignInCategory,
  AuthClientConfig,
  SignInOptions,
  AuthSession,
} from './types';
```

### File Structure

```
src/
├── index.ts      # Main entry point - re-exports all public API
├── client.ts     # createAuthClient factory and auth methods
├── storage.ts    # tokenStorage utility object
└── types.ts      # Type definitions and provider/category data
```

---

## Auth Client API

### `createAuthClient(config: AuthClientConfig)`

Factory function that creates a configured authentication client instance.

#### Configuration Options

```typescript
interface AuthClientConfig {
  /** Base URL of the API Gateway (e.g., 'https://api.meridian.console') */
  gatewayUrl: string;

  /** Path to BetterAuth endpoints (default: '/api/v1/betterauth') */
  betterAuthPath?: string;

  /** Path to Identity service endpoints (default: '/api/v1/identity') */
  identityPath?: string;
}
```

#### Example

```typescript
import { createAuthClient } from '@dhadgar/shared-auth';

const authClient = createAuthClient({
  gatewayUrl: 'http://localhost:5000',
  // Uses defaults: betterAuthPath='/api/v1/betterauth', identityPath='/api/v1/identity'
});
```

### Auth Client Methods

The returned auth client object provides the following methods:

---

#### `signIn(options: SignInOptions): Promise<void>`

Initiates an OAuth sign-in flow by redirecting to the OAuth provider.

```typescript
interface SignInOptions {
  /** OAuth provider ID (e.g., 'google', 'discord', 'steam') */
  provider: string;

  /** URL to redirect to after successful authentication (default: '/callback') */
  callbackURL?: string;
}
```

**Behavior:**
- Makes a POST request to BetterAuth to initiate OAuth
- Validates the returned redirect URL
- Redirects the browser to the OAuth provider
- Does not return (browser navigates away)

**Provider Routing:**
- Built-in social providers (Google, Discord, etc.): Uses `/sign-in/social` endpoint
- Generic OAuth providers (Microsoft): Uses `/sign-in/oauth2` endpoint with `providerId`

**Security Validations:**
- Redirect URL must use HTTPS
- Hostname is validated against trusted OAuth provider list
- Untrusted hostnames log a warning but allow redirect (defense in depth)

**Example:**

```typescript
// Simple sign-in
await authClient.signIn({ provider: 'discord' });

// With custom callback URL
await authClient.signIn({
  provider: 'google',
  callbackURL: '/auth/callback'
});
```

---

#### `signOut(): Promise<void>`

Signs the user out and clears all authentication state.

**Behavior:**
- Makes a POST request to BetterAuth's `/sign-out` endpoint
- Clears all tokens from `sessionStorage` (always, even if request fails)
- Does not redirect (caller handles navigation)

**Example:**

```typescript
await authClient.signOut();
// Navigate to home page
window.location.href = '/';
```

---

#### `getSession(): Promise<AuthSession | null>`

Retrieves the current BetterAuth session (cookie-based).

**Returns:** `AuthSession` if authenticated, `null` otherwise.

```typescript
interface AuthSession {
  user: User;
  session: {
    id: string;
    expiresAt: Date;
  };
}
```

**Use Case:** Check if user has an active BetterAuth session before exchanging tokens.

**Example:**

```typescript
const session = await authClient.getSession();
if (session) {
  console.log('Logged in as:', session.user.email);
}
```

---

#### `exchangeTokens(): Promise<AuthTokens | null>`

Exchanges a valid BetterAuth session for Identity service JWT tokens.

**Flow:**
1. POST to BetterAuth `/exchange` to get a one-time exchange token
2. POST exchange token to Identity `/exchange` for JWT access/refresh tokens
3. Store tokens in `sessionStorage`
4. Return the token object

**Returns:** `AuthTokens` if successful, `null` if exchange fails.

```typescript
interface AuthTokens {
  accessToken: string;   // Short-lived JWT for API calls
  refreshToken: string;  // Long-lived token for renewal
  expiresAt: number;     // Unix timestamp (seconds) when accessToken expires
}
```

**Example:**

```typescript
// After OAuth callback, exchange session for tokens
const tokens = await authClient.exchangeTokens();
if (tokens) {
  console.log('Token expires at:', new Date(tokens.expiresAt * 1000));
}
```

---

#### `refreshTokens(): Promise<AuthTokens | null>`

Refreshes the access token using the stored refresh token.

**Behavior:**
- Retrieves refresh token from `sessionStorage`
- POST to Identity `/refresh` endpoint
- Updates stored tokens on success
- Clears all tokens on failure (forces re-authentication)

**Returns:** New `AuthTokens` if successful, `null` if refresh fails.

**Example:**

```typescript
const refreshed = await authClient.refreshTokens();
if (!refreshed) {
  // Redirect to login - session expired
  window.location.href = '/login';
}
```

---

#### `getTokens(): Promise<AuthTokens | null>`

Gets current tokens with automatic refresh handling.

**Behavior:**
- Returns `null` if no tokens exist
- Calls `refreshTokens()` if access token is expired
- Triggers background refresh if token is expiring soon (<5 minutes)
- Returns current tokens immediately (even if background refresh is pending)

**Example:**

```typescript
const tokens = await authClient.getTokens();
if (tokens) {
  // Use tokens.accessToken for API calls
}
```

---

#### `isAuthenticated(): boolean`

Synchronously checks if the user has valid (non-expired) tokens.

**Returns:** `true` if tokens exist and are not expired, `false` otherwise.

**Note:** This is a synchronous check that does not verify with the server.

**Example:**

```typescript
if (authClient.isAuthenticated()) {
  // Show authenticated UI
} else {
  // Show login button
}
```

---

#### `getAccessToken(): Promise<string | null>`

Convenience method to get just the access token string.

**Behavior:**
- Calls `getTokens()` internally (handles refresh)
- Returns only the `accessToken` string

**Example:**

```typescript
const token = await authClient.getAccessToken();
if (token) {
  const response = await fetch('/api/resource', {
    headers: { 'Authorization': `Bearer ${token}` }
  });
}
```

---

## Token Storage

### `tokenStorage` Object

A utility object for managing authentication tokens in browser `sessionStorage`.

#### Why sessionStorage?

| Storage Type | Persistence | XSS Risk | Tab Isolation |
|--------------|-------------|----------|---------------|
| localStorage | Persistent | Higher | No |
| sessionStorage | Tab-only | Lower | Yes |
| Memory | Page-only | Lowest | Yes |

`sessionStorage` provides a balance:
- Survives page refreshes within a tab
- Automatically cleared when tab is closed
- Isolated per browser tab (prevents token leakage between tabs)
- More resistant to XSS than localStorage

### Storage Keys

```typescript
const ACCESS_TOKEN_KEY = 'meridian_access_token';
const REFRESH_TOKEN_KEY = 'meridian_refresh_token';
const EXPIRES_AT_KEY = 'meridian_expires_at';
```

### Methods

#### `getAccessToken(): string | null`

Returns the stored access token or `null`.

```typescript
const token = tokenStorage.getAccessToken();
```

#### `getRefreshToken(): string | null`

Returns the stored refresh token or `null`.

```typescript
const refreshToken = tokenStorage.getRefreshToken();
```

#### `getExpiresAt(): number | null`

Returns the token expiration timestamp (Unix seconds) or `null`.

```typescript
const expiresAt = tokenStorage.getExpiresAt();
if (expiresAt) {
  console.log('Expires:', new Date(expiresAt * 1000));
}
```

#### `getTokens(): AuthTokens | null`

Returns all tokens as an object, or `null` if any token is missing.

```typescript
const tokens = tokenStorage.getTokens();
if (tokens) {
  console.log('Access token:', tokens.accessToken);
  console.log('Refresh token:', tokens.refreshToken);
  console.log('Expires at:', tokens.expiresAt);
}
```

#### `setTokens(tokens: AuthTokens): void`

Stores all tokens in sessionStorage.

```typescript
tokenStorage.setTokens({
  accessToken: 'eyJ...',
  refreshToken: 'eyJ...',
  expiresAt: 1700000000,
});
```

#### `clear(): void`

Removes all tokens from sessionStorage.

```typescript
tokenStorage.clear();
```

#### `isExpired(): boolean`

Checks if the access token is expired (with 60-second buffer).

```typescript
if (tokenStorage.isExpired()) {
  // Need to refresh or re-authenticate
}
```

**Note:** Returns `true` if no expiration is stored.

#### `isExpiringSoon(): boolean`

Checks if the access token will expire within 5 minutes.

```typescript
if (tokenStorage.isExpiringSoon()) {
  // Good time to refresh in background
}
```

### Server-Side Rendering (SSR) Safety

All `tokenStorage` methods check for browser environment:

```typescript
function isBrowser(): boolean {
  return typeof window !== 'undefined' && typeof sessionStorage !== 'undefined';
}
```

On the server (SSR), all methods return `null` or no-op, preventing hydration mismatches.

---

## OAuth Providers

### `OAUTH_PROVIDERS` Array

A comprehensive list of 17 OAuth providers with UI metadata.

```typescript
interface OAuthProvider {
  /** Unique provider ID (matches backend configuration) */
  id: string;

  /** Display name for UI */
  name: string;

  /** Icon identifier for rendering */
  icon: string;

  /** Brand primary color (hex) */
  color: string;

  /** Glow/shadow color (rgba) for hover effects */
  glowColor: string;
}
```

### Available Providers

| ID | Name | Color | Category |
|----|------|-------|----------|
| `google` | Google | #4285F4 | Social |
| `microsoft` | Microsoft | #00A4EF | Social |
| `facebook` | Facebook | #1877F2 | Social |
| `amazon` | Amazon | #FF9900 | Social |
| `yahoo` | Yahoo | #6001D2 | Social |
| `discord` | Discord | #5865F2 | Gaming |
| `steam` | Steam | #1B2838 | Gaming |
| `xbox` | Xbox | #107C10 | Gaming |
| `twitch` | Twitch | #9146FF | Gaming |
| `battlenet` | Battle.net | #00AEFF | Gaming |
| `roblox` | Roblox | #E2231A | Gaming |
| `github` | GitHub | #24292F | Other |
| `slack` | Slack | #4A154B | Other |
| `reddit` | Reddit | #FF4500 | Other |
| `spotify` | Spotify | #1DB954 | Other |
| `paypal` | PayPal | #003087 | Other |
| `lego` | LEGO | #FFED00 | Other |

### `getProvider(id: string): OAuthProvider | undefined`

Lookup a provider by ID.

```typescript
import { getProvider } from '@dhadgar/shared-auth';

const discord = getProvider('discord');
if (discord) {
  console.log(discord.name);  // 'Discord'
  console.log(discord.color); // '#5865F2'
}
```

### Special Provider: Xbox

Xbox authentication uses Microsoft OAuth under the hood. The `xbox` provider entry exists for UI purposes (different branding), but the actual OAuth flow uses the `microsoft` provider ID with Xbox-specific scopes configured on the backend.

---

## Sign-In Categories

### Decoupled UI Configuration

The sign-in page layout is configured separately from provider data. This allows:
- Reordering providers without changing their metadata
- Different layouts for different pages
- A/B testing of provider arrangements
- Per-category UI variations (compact vs. full)

### `SIGN_IN_CATEGORIES` Array

```typescript
interface SignInCategory {
  /** Category identifier */
  id: OAuthCategory;

  /** Display name */
  name: string;

  /** Subtitle/description */
  description: string;

  /** Ordered list of provider IDs in this category */
  providers: string[];

  /** If true, render as compact icon grid (default: false) */
  compact?: boolean;
}

type OAuthCategory = 'social' | 'gaming' | 'other';
```

### Default Configuration

```typescript
export const SIGN_IN_CATEGORIES: SignInCategory[] = [
  {
    id: 'social',
    name: 'Social & Identity',
    description: 'Sign in with your social account',
    providers: ['google', 'microsoft', 'facebook', 'amazon', 'yahoo'],
  },
  {
    id: 'gaming',
    name: 'Gaming',
    description: 'Connect your gaming identity',
    providers: ['discord', 'steam', 'xbox', 'twitch', 'battlenet', 'roblox'],
    compact: true,  // Renders as icon grid
  },
  {
    id: 'other',
    name: 'More Options',
    description: 'Other sign-in methods',
    providers: ['github', 'slack', 'reddit', 'spotify', 'paypal', 'lego'],
  },
];
```

### `getProvidersByCategory(categoryId: OAuthCategory): OAuthProvider[]`

Returns full provider objects for a category, in configured order.

```typescript
import { getProvidersByCategory } from '@dhadgar/shared-auth';

const gamingProviders = getProvidersByCategory('gaming');
// Returns: [discord, steam, xbox, twitch, battlenet, roblox] provider objects
```

**Warning Behavior:** Logs a console warning if a provider ID in the category doesn't exist in `OAUTH_PROVIDERS`. This catches typos in configuration.

### `getSignInCategory(id: OAuthCategory): SignInCategory | undefined`

Lookup a category by ID.

```typescript
import { getSignInCategory } from '@dhadgar/shared-auth';

const gaming = getSignInCategory('gaming');
if (gaming?.compact) {
  // Render as icon grid
}
```

### `OAUTH_CATEGORIES` (Legacy)

A simplified lookup object for backward compatibility:

```typescript
export const OAUTH_CATEGORIES: Record<OAuthCategory, { name: string; description: string }> = {
  social: { name: 'Social & Identity', description: 'Sign in with your social account' },
  gaming: { name: 'Gaming', description: 'Connect your gaming identity' },
  other: { name: 'More Options', description: 'Other sign-in methods' },
};
```

---

## TypeScript Types

### `User`

Represents an authenticated user.

```typescript
interface User {
  id: string;
  email: string;
  name?: string;
  image?: string;
  emailVerified: boolean;
  createdAt: Date;
  updatedAt: Date;
}
```

### `Session`

Represents a BetterAuth session.

```typescript
interface Session {
  id: string;
  userId: string;
  expiresAt: Date;
  token: string;
  ipAddress?: string;
  userAgent?: string;
}
```

### `AuthTokens`

JWT token pair from Identity service.

```typescript
interface AuthTokens {
  accessToken: string;
  refreshToken: string;
  expiresAt: number;  // Unix timestamp in seconds
}
```

### `AuthState`

Full authentication state (useful for React context/stores).

```typescript
interface AuthState {
  user: User | null;
  tokens: AuthTokens | null;
  isLoading: boolean;
  isAuthenticated: boolean;
}
```

### `AuthSession`

Combined user and session from `getSession()`.

```typescript
interface AuthSession {
  user: User;
  session: {
    id: string;
    expiresAt: Date;
  };
}
```

### `SignInOptions`

Options for `signIn()` method.

```typescript
interface SignInOptions {
  provider: string;
  callbackURL?: string;
}
```

### `AuthClientConfig`

Configuration for `createAuthClient()`.

```typescript
interface AuthClientConfig {
  gatewayUrl: string;
  betterAuthPath?: string;
  identityPath?: string;
}
```

---

## Usage Examples

### Basic Setup (ShoppingCart/Panel Pattern)

**`src/lib/auth/client.ts`**

```typescript
import { createAuthClient } from '@dhadgar/shared-auth';

const GATEWAY_URL = import.meta.env.PUBLIC_GATEWAY_URL || 'http://localhost:5000';

export const authClient = createAuthClient({
  gatewayUrl: GATEWAY_URL,
});
```

**`src/lib/auth/index.ts`**

```typescript
export { authClient } from './client';

// Re-export from shared auth package
export {
  tokenStorage,
  OAUTH_PROVIDERS,
  OAUTH_CATEGORIES,
  SIGN_IN_CATEGORIES,
  getProvider,
  getProvidersByCategory,
  getSignInCategory,
} from '@dhadgar/shared-auth';

export type {
  User,
  Session,
  AuthTokens,
  AuthState,
  OAuthProvider,
  OAuthCategory,
  SignInCategory,
  SignInOptions,
  AuthSession,
} from '@dhadgar/shared-auth';
```

### OAuth Button Component

```typescript
import { useState, useCallback } from 'react';
import {
  authClient,
  SIGN_IN_CATEGORIES,
  getProvidersByCategory,
  type OAuthProvider,
  type SignInCategory,
} from '../../lib/auth';

interface OAuthButtonGroupProps {
  callbackURL?: string;
}

export default function OAuthButtonGroup({ callbackURL }: OAuthButtonGroupProps) {
  const [loadingProvider, setLoadingProvider] = useState<string | null>(null);

  const handleOAuth = useCallback(async (providerId: string) => {
    setLoadingProvider(providerId);
    try {
      const runtimeCallbackURL = callbackURL || `${window.location.origin}/callback`;
      await authClient.signIn({ provider: providerId, callbackURL: runtimeCallbackURL });
    } catch (error) {
      console.error('OAuth error:', error);
      setLoadingProvider(null);
    }
  }, [callbackURL]);

  return (
    <div>
      {SIGN_IN_CATEGORIES.map((category) => {
        const providers = getProvidersByCategory(category.id);
        return (
          <div key={category.id}>
            <h3>{category.name}</h3>
            <div className={category.compact ? 'grid grid-cols-3' : 'flex flex-col'}>
              {providers.map((provider) => (
                <button
                  key={provider.id}
                  onClick={() => handleOAuth(provider.id)}
                  disabled={loadingProvider !== null}
                  style={{ backgroundColor: provider.color }}
                >
                  {loadingProvider === provider.id ? 'Loading...' : provider.name}
                </button>
              ))}
            </div>
          </div>
        );
      })}
    </div>
  );
}
```

### OAuth Callback Handler

```typescript
// src/pages/callback.astro or callback.tsx

import { authClient } from '../lib/auth';

// In a React component or Astro client script:
async function handleCallback() {
  // Exchange BetterAuth session for Identity tokens
  const tokens = await authClient.exchangeTokens();

  if (tokens) {
    // Successfully authenticated
    window.location.href = '/dashboard';
  } else {
    // Exchange failed
    window.location.href = '/login?error=exchange_failed';
  }
}
```

### API Client with Auto-Refresh

```typescript
import { authClient, tokenStorage } from '../lib/auth';

const GATEWAY_URL = import.meta.env.PUBLIC_GATEWAY_URL || 'http://localhost:5000';

export async function apiClient<T>(
  path: string,
  options: RequestInit = {}
): Promise<T> {
  const headers = new Headers(options.headers);
  headers.set('Content-Type', 'application/json');

  // Get access token (auto-refreshes if needed)
  const accessToken = await authClient.getAccessToken();
  if (!accessToken) {
    throw new Error('Not authenticated');
  }
  headers.set('Authorization', `Bearer ${accessToken}`);

  const response = await fetch(`${GATEWAY_URL}${path}`, {
    ...options,
    headers,
  });

  // Handle 401 - try to refresh and retry once
  if (response.status === 401) {
    const refreshed = await authClient.refreshTokens();
    if (refreshed) {
      headers.set('Authorization', `Bearer ${refreshed.accessToken}`);
      const retryResponse = await fetch(`${GATEWAY_URL}${path}`, {
        ...options,
        headers,
      });
      if (retryResponse.ok) {
        return retryResponse.json();
      }
    }
    // Refresh failed - clear tokens and redirect to login
    tokenStorage.clear();
    window.location.href = '/login';
    throw new Error('Session expired');
  }

  if (!response.ok) {
    throw new Error(`API error: ${response.status}`);
  }

  return response.json();
}
```

### Auth State Hook (React)

```typescript
import { useState, useEffect } from 'react';
import { authClient, tokenStorage, type User, type AuthTokens } from '../lib/auth';

interface AuthState {
  user: User | null;
  tokens: AuthTokens | null;
  isLoading: boolean;
  isAuthenticated: boolean;
}

export function useAuth(): AuthState {
  const [state, setState] = useState<AuthState>({
    user: null,
    tokens: null,
    isLoading: true,
    isAuthenticated: false,
  });

  useEffect(() => {
    async function checkAuth() {
      // Check for existing tokens
      const tokens = tokenStorage.getTokens();

      if (tokens && !tokenStorage.isExpired()) {
        // Have valid tokens
        const session = await authClient.getSession();
        setState({
          user: session?.user || null,
          tokens,
          isLoading: false,
          isAuthenticated: true,
        });
      } else if (tokens) {
        // Tokens expired, try to refresh
        const refreshed = await authClient.refreshTokens();
        if (refreshed) {
          const session = await authClient.getSession();
          setState({
            user: session?.user || null,
            tokens: refreshed,
            isLoading: false,
            isAuthenticated: true,
          });
        } else {
          setState({
            user: null,
            tokens: null,
            isLoading: false,
            isAuthenticated: false,
          });
        }
      } else {
        // No tokens
        setState({
          user: null,
          tokens: null,
          isLoading: false,
          isAuthenticated: false,
        });
      }
    }

    checkAuth();
  }, []);

  return state;
}
```

---

## Configuration

### Environment Variables

Frontend applications should configure these environment variables:

| Variable | Description | Default |
|----------|-------------|---------|
| `PUBLIC_GATEWAY_URL` | Base URL of the API Gateway | `http://localhost:5000` |

**Astro Usage:**

```typescript
// In client code
const GATEWAY_URL = import.meta.env.PUBLIC_GATEWAY_URL || 'http://localhost:5000';
```

**Note:** Astro requires `PUBLIC_` prefix for client-exposed environment variables.

### Customizing Paths

If your backend uses non-standard paths:

```typescript
const authClient = createAuthClient({
  gatewayUrl: 'https://api.example.com',
  betterAuthPath: '/auth/v2',      // Custom BetterAuth path
  identityPath: '/identity/v2',    // Custom Identity path
});
```

### Adding New OAuth Providers

1. **Add provider to `OAUTH_PROVIDERS`** in `src/types.ts`:

```typescript
export const OAUTH_PROVIDERS: OAuthProvider[] = [
  // ... existing providers
  {
    id: 'newprovider',
    name: 'New Provider',
    icon: 'newprovider',
    color: '#123456',
    glowColor: 'rgba(18, 52, 86, 0.4)'
  },
];
```

2. **Add provider to appropriate category**:

```typescript
export const SIGN_IN_CATEGORIES: SignInCategory[] = [
  {
    id: 'social',
    name: 'Social & Identity',
    description: 'Sign in with your social account',
    providers: ['google', 'microsoft', 'newprovider', /* ... */],
  },
  // ...
];
```

3. **Configure backend** BetterAuth to support the new provider.

---

## Security Considerations

### Token Storage

- **sessionStorage** is used instead of localStorage to reduce XSS exposure
- Tokens are automatically cleared when browser tab is closed
- Tokens are isolated per-tab (no cross-tab token sharing)

### Redirect Validation

The `signIn()` method validates redirect URLs before navigating:

```typescript
const TRUSTED_OAUTH_HOSTNAMES = [
  'login.microsoftonline.com',
  'accounts.google.com',
  'discord.com',
  'github.com',
  'appleid.apple.com',
  'www.facebook.com',
  'id.twitch.tv',
];

function validateAndRedirect(url: string): void {
  const parsedUrl = new URL(url);

  // Require HTTPS
  if (parsedUrl.protocol !== 'https:') {
    throw new Error('Redirect URL must use HTTPS');
  }

  // Warn (but allow) untrusted origins - defense in depth
  const isTrusted = TRUSTED_OAUTH_HOSTNAMES.some(h => parsedUrl.hostname === h);
  if (!isTrusted) {
    console.warn('Redirect to untrusted origin:', parsedUrl.origin);
  }

  window.location.href = parsedUrl.toString();
}
```

### Token Validation

Token responses are validated before storage:

```typescript
function validateTokenResponse(data: unknown): asserts data is TokenResponse {
  if (typeof response.accessToken !== 'string' || response.accessToken.length === 0) {
    throw new Error('Invalid token response: missing accessToken');
  }
  if (typeof response.refreshToken !== 'string' || response.refreshToken.length === 0) {
    throw new Error('Invalid token response: missing refreshToken');
  }
  if (typeof response.expiresIn !== 'number' || response.expiresIn <= 0) {
    throw new Error('Invalid token response: invalid expiresIn');
  }
}
```

### Credentials Mode

All BetterAuth requests include credentials for cookie handling:

```typescript
credentials: 'include'  // Sends HTTP-only session cookies
```

### Token Expiration Buffer

Tokens are considered expired 60 seconds before actual expiry to prevent race conditions:

```typescript
isExpired(): boolean {
  const expiresAt = this.getExpiresAt();
  if (!expiresAt) return true;
  // 60-second buffer
  return Date.now() >= (expiresAt - 60) * 1000;
}
```

---

## Integration with Backend Services

### BetterAuth Service

The auth client communicates with BetterAuth via these endpoints:

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/sign-in/social` | POST | Initiate social OAuth (Google, Discord, etc.) |
| `/sign-in/oauth2` | POST | Initiate generic OAuth (Microsoft) |
| `/sign-out` | POST | End session |
| `/get-session` | GET | Get current session/user |
| `/exchange` | POST | Get one-time exchange token |

### Identity Service

Token operations use Identity service endpoints:

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/exchange` | POST | Exchange BetterAuth token for JWT |
| `/refresh` | POST | Refresh access token |

### Gateway Routing

All requests go through the API Gateway (YARP), which routes:

- `/api/v1/betterauth/*` -> BetterAuth service
- `/api/v1/identity/*` -> Identity service

---

## Related Documentation

### Internal Documentation

- **CLAUDE.md** (this project): Quick reference for Claude Code agents
- **Identity Service Implementation Plan**: `docs/implementation-plans/identity-service.md`
- **Identity Webhooks**: `docs/identity-webhooks.md`
- **BetterAuth Service**: `src/Dhadgar.BetterAuth/CLAUDE.md`

### Consuming Applications

- **ShoppingCart**: `src/Dhadgar.ShoppingCart/CLAUDE.md`
- **Panel**: `src/Dhadgar.Panel/CLAUDE.md`

### External Resources

- [Better Auth Documentation](https://www.better-auth.com/)
- [Astro Environment Variables](https://docs.astro.build/en/guides/environment-variables/)
- [Web Authentication (WebAuthn)](https://webauthn.guide/)

---

## Troubleshooting

### "Not authenticated" after sign-in

1. Check that OAuth callback page calls `exchangeTokens()`
2. Verify BetterAuth session cookie is being sent (check browser DevTools)
3. Check for CORS errors in browser console

### Tokens not persisting

1. Verify `sessionStorage` is available (not in SSR context)
2. Check that tokens are being returned from `exchangeTokens()`
3. Ensure you're not in a private/incognito window with storage disabled

### Provider not appearing in UI

1. Check provider ID exists in `OAUTH_PROVIDERS`
2. Check provider ID is listed in appropriate `SIGN_IN_CATEGORIES` entry
3. Look for console warnings about unknown provider IDs

### Microsoft sign-in failing

Microsoft uses `genericOAuth` routing, which requires:
- The provider ID must be in `GENERIC_OAUTH_PROVIDERS` array
- Backend must have Microsoft configured as genericOAuth (not social)

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2025-01 | Initial release with 17 OAuth providers |

---

**Maintainer:** Meridian Console Team
**Package:** `@dhadgar/shared-auth`
**Location:** `src/Dhadgar.SharedAuth/`

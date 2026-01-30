# Dhadgar.ShoppingCart

![Status: Stub](https://img.shields.io/badge/Status-Stub-red)

**Marketing Site and Checkout Flow for Meridian Console**

Dhadgar.ShoppingCart is the public-facing marketing website and subscription checkout application for the Meridian Console platform. Built with Astro, React, and Tailwind CSS, it serves as the primary landing page for prospective customers, showcases product features and pricing, and handles user authentication for the SaaS platform.

## Table of Contents

- [Overview](#overview)
- [Current Status](#current-status)
- [Technology Stack](#technology-stack)
- [Quick Start](#quick-start)
- [Project Structure](#project-structure)
- [Pages](#pages)
- [Components](#components)
- [Layouts](#layouts)
- [Authentication](#authentication)
- [API Integration](#api-integration)
- [Styling](#styling)
- [Planned Features](#planned-features)
- [Configuration](#configuration)
- [Building for Production](#building-for-production)
- [Deployment](#deployment)
- [Testing](#testing)
- [Related Documentation](#related-documentation)

---

## Overview

The ShoppingCart application fulfills several key responsibilities within the Meridian Console ecosystem:

1. **Marketing and Landing Pages**: Presents the product value proposition, features, and benefits to prospective customers through a visually striking, sci-fi themed interface.

2. **Pricing Display**: Shows subscription tiers (Starter, Professional, Enterprise) with clear feature comparisons to help customers choose the right plan.

3. **Authentication Gateway**: Provides OAuth-based sign-in supporting 17+ identity providers (Google, Microsoft, Discord, Steam, Xbox, etc.), enabling users to create accounts and authenticate.

4. **Profile Management**: Displays user profile information, linked accounts, and organization memberships after authentication.

5. **Future Checkout Flow**: Will eventually handle the complete subscription purchase and management workflow (currently planned, not implemented).

### Design Philosophy

The ShoppingCart uses a distinctive cyberpunk/sci-fi visual design with:
- **Magenta (#FF00AA)** as the primary accent color (differentiating it from Panel's cyan theme)
- Dark space-themed backgrounds with subtle grid patterns
- Glowing borders and hover effects
- Futuristic typography using the Orbitron display font
- Consistent design language with other Meridian Console frontends

---

## Current Status

**Development Phase: Wireframe with OAuth Login Flow Working**

The ShoppingCart application is currently in a functional wireframe state with the following capabilities:

### Working Features

| Feature | Status | Description |
|---------|--------|-------------|
| Landing Page | Working | Full marketing page with hero, features, pricing sections |
| OAuth Login | Working | Complete OAuth flow with 17+ providers |
| Callback Handling | Working | Proper OAuth callback processing and token exchange |
| Profile Page | Working | Displays user info, organizations, linked accounts |
| Auth State Management | Working | Header shows authenticated state, sign out works |
| Static Site Generation | Working | Builds to static HTML for Azure Static Web Apps |

### Not Yet Implemented

- Actual checkout/payment processing (Stripe integration planned)
- Subscription management UI
- Plan upgrade/downgrade flows
- Invoice history and billing details
- Marketing email capture
- A/B testing infrastructure
- Analytics integration

---

## Technology Stack

### Core Framework

| Technology | Version | Purpose |
|------------|---------|---------|
| [Astro](https://astro.build) | 5.1.1 | Static site generation, hybrid rendering |
| [React](https://react.dev) | 18.3.1 | Interactive UI components |
| [TypeScript](https://typescriptlang.org) | 5.7.2 | Type-safe JavaScript |
| [Tailwind CSS](https://tailwindcss.com) | 3.4.16 | Utility-first CSS framework |

### Key Dependencies

| Package | Purpose |
|---------|---------|
| `@dhadgar/shared-auth` | Shared OAuth client and provider configuration |
| `@astrojs/react` | React integration for Astro |
| `@astrojs/tailwind` | Tailwind CSS integration for Astro |
| `clsx` | Conditional CSS class composition |

### .NET Integration

The project includes a `.csproj` file that acts as a "shim" to integrate the Node.js/Astro build into the .NET solution. This allows `dotnet build` to build the entire Meridian Console solution including this frontend project. The shim uses `Microsoft.Build.NoTargets` SDK to invoke npm commands without producing .NET output.

---

## Quick Start

### Prerequisites

- **Node.js 20+** (required for Astro)
- **npm** (comes with Node.js)
- **Local infrastructure** (optional, for full auth flow):
  ```bash
  # From repository root
  docker compose -f deploy/compose/docker-compose.dev.yml up -d
  ```

### Installation

```bash
# Navigate to the project directory
cd src/Dhadgar.ShoppingCart

# Install dependencies
npm install
```

### Development Server

```bash
# Start the development server (hot reload enabled)
npm run dev

# The site will be available at http://localhost:4322
```

The development server runs on **port 4322** (configured in `package.json`) to avoid conflicts with:
- Dhadgar.Scope (port 4321)
- Gateway service (port 5000)

### Alternative: Build via .NET

```bash
# From repository root - builds all projects including ShoppingCart
dotnet build

# Or build just ShoppingCart
dotnet build src/Dhadgar.ShoppingCart
```

This approach invokes `npm install` and `npm run build` automatically.

---

## Project Structure

```
Dhadgar.ShoppingCart/
├── api/                           # Azure Functions API (placeholder)
│   ├── host.json                  # Azure Functions host configuration
│   └── Hello/                     # Example function endpoint
│       └── function.json          # Function binding configuration
│
├── src/                           # Source code
│   ├── components/                # React components
│   │   ├── auth/                  # Authentication components
│   │   │   ├── AuthProvider.tsx   # React context for auth state
│   │   │   ├── CallbackHandler.tsx# OAuth callback processor
│   │   │   ├── LoginPage.tsx      # Generic login page (Panel-style)
│   │   │   ├── OAuthButtonGroup.tsx# OAuth provider button grid
│   │   │   ├── ProtectedContent.tsx# Route protection wrapper
│   │   │   ├── ProviderIcon.tsx   # SVG icons for OAuth providers
│   │   │   ├── ShopLoginPage.tsx  # Shop-specific login (magenta theme)
│   │   │   └── index.ts           # Barrel export
│   │   │
│   │   ├── layout/                # Layout-level components
│   │   │   ├── AuthHeader.tsx     # Auth-aware header with user menu
│   │   │   └── index.ts           # Barrel export
│   │   │
│   │   ├── profile/               # Profile-related components
│   │   │   ├── ProfilePage.tsx    # Full user profile page
│   │   │   └── index.ts           # Barrel export
│   │   │
│   │   └── ui/                    # Reusable UI primitives
│   │       ├── GlowButton.tsx     # Themed button with glow effects
│   │       ├── LoadingSpinner.tsx # Animated loading indicator
│   │       ├── Panel.tsx          # Card/container component
│   │       └── index.ts           # Barrel export
│   │
│   ├── layouts/                   # Astro layout components
│   │   ├── AuthLayout.astro       # Layout for auth pages (minimal)
│   │   ├── BaseLayout.astro       # Base HTML structure, fonts
│   │   └── MarketingLayout.astro  # Full marketing layout with nav/footer
│   │
│   ├── lib/                       # Utility libraries
│   │   └── auth/                  # Authentication utilities
│   │       ├── api.ts             # API client with JWT injection
│   │       ├── client.ts          # Auth client instance
│   │       └── index.ts           # Re-exports from shared-auth
│   │
│   ├── pages/                     # Astro pages (file-based routing)
│   │   ├── callback.astro         # OAuth callback handler
│   │   ├── index.astro            # Landing/marketing page
│   │   ├── login.astro            # Sign-in page
│   │   └── profile.astro          # User profile page
│   │
│   └── styles/                    # Global styles
│       └── global.css             # Tailwind imports + custom CSS
│
├── _swa_publish/                  # Build output directory
│   └── wwwroot/                   # Static files for deployment
│
├── astro.config.mjs               # Astro configuration
├── Dhadgar.ShoppingCart.csproj    # .NET build shim
├── env.d.ts                       # TypeScript environment types
├── package.json                   # npm dependencies and scripts
├── tailwind.config.mjs            # Tailwind CSS configuration
└── tsconfig.json                  # TypeScript configuration
```

---

## Pages

The ShoppingCart uses Astro's file-based routing. Each `.astro` file in `src/pages/` becomes a URL route.

### `/` - Landing Page (index.astro)

**Purpose**: Main marketing page for prospective customers.

**Layout**: `MarketingLayout.astro`

**Sections**:
1. **Hero Section**: Large title "COMMAND YOUR GAME SERVERS" with call-to-action buttons
2. **Features Section**: Three feature cards highlighting:
   - Node Management (deploy agents, manage servers)
   - Instant Deployment (one-click game server deployment)
   - Real-time Monitoring (console access, metrics, alerting)
3. **Pricing Section**: Three pricing tiers:
   - **Starter** ($0/month): 3 nodes, 5 game servers, community support
   - **Professional** ($29/month): 20 nodes, unlimited servers, priority support, analytics
   - **Enterprise** (Custom): Unlimited nodes, dedicated support, custom SLA, on-premise option
4. **CTA Section**: Final call-to-action to start free

**Technical Notes**:
- Pure Astro page with no React components (server-rendered HTML)
- Links to `/login` for all sign-up actions
- Uses anchor links (`#features`, `#pricing`) for in-page navigation

### `/login` - Sign In Page (login.astro)

**Purpose**: OAuth authentication entry point.

**Layout**: `AuthLayout.astro`

**Component**: `ShopLoginPage.tsx` (React, client-side hydrated)

**Features**:
- Displays all 17 OAuth providers grouped by category
- Magenta-themed header (distinct from Panel's cyan)
- "Back to home" link
- Links to Terms of Service and Privacy Policy

### `/callback` - OAuth Callback (callback.astro)

**Purpose**: Handles OAuth provider redirects after authentication.

**Layout**: `AuthLayout.astro`

**Component**: `CallbackHandler.tsx` (React, client-side hydrated)

**Flow**:
1. OAuth provider redirects here with authorization code
2. `CallbackHandler` checks for errors in URL params
3. If successful, exchanges BetterAuth session for Identity tokens
4. Stores tokens in sessionStorage
5. Redirects to `/profile` on success

**States**:
- **Loading**: "ESTABLISHING SECURE CONNECTION..."
- **Success**: "ACCESS GRANTED" with redirect
- **Error**: "AUTHENTICATION FAILED" with error message and retry link

### `/profile` - User Profile (profile.astro)

**Purpose**: Display authenticated user's profile information.

**Layout**: `BaseLayout.astro`

**Component**: `ProfilePage.tsx` (React, client-side hydrated)

**Sections**:
1. **Header**: Logo, navigation, sign-out button
2. **Profile Card**: Avatar, display name, email, verification status, passkey status, member since, last login
3. **Organizations**: List of organizations user belongs to, with roles and join dates
4. **Sign-In Methods**: OAuth providers linked to the account
5. **Linked Gaming Accounts**: Gaming platform connections (Steam, Xbox, etc.)
6. **User ID**: Debug info for support purposes

**Authorization**: Redirects to `/login` if not authenticated.

---

## Components

### Authentication Components (`src/components/auth/`)

#### AuthProvider.tsx

React context provider that manages authentication state across the application.

**Exports**:
- `AuthProvider`: Context provider component
- `useAuth()`: Hook to access auth state and methods

**State**:
```typescript
interface AuthState {
  user: User | null;
  tokens: AuthTokens | null;
  isLoading: boolean;
  isAuthenticated: boolean;
}
```

**Methods**:
- `signIn(provider, callbackURL)`: Initiate OAuth flow
- `signOut()`: Clear auth state and call backend sign-out
- `refreshAuth()`: Re-check authentication status

**Features**:
- Automatic token refresh every 60 seconds
- Proactive refresh when tokens are expiring soon (5-minute threshold)

#### OAuthButtonGroup.tsx

Displays OAuth provider buttons organized by category.

**Props**:
```typescript
interface OAuthButtonGroupProps {
  callbackURL?: string;  // Override callback URL
  compact?: boolean;     // Use compact grid layout
}
```

**Categories** (from SharedAuth):
1. **Social & Identity**: Google, Microsoft, Facebook, Amazon, Yahoo
2. **Gaming**: Discord, Steam, Xbox, Twitch, Battle.net, Roblox (compact grid)
3. **More Options**: GitHub, Slack, Reddit, Spotify, PayPal, LEGO

**Features**:
- Loading state with spinner
- Disabled state during auth flow
- Category headers and dividers
- Responsive grid layout

#### CallbackHandler.tsx

Processes OAuth callback and exchanges tokens.

**States**: `loading` | `success` | `error`

**Error Handling**:
- URL error parameters (`?error=access_denied`)
- Token exchange failures
- Network errors

#### LoginPage.tsx vs ShopLoginPage.tsx

Two variations of the login page:
- **LoginPage.tsx**: Generic version with cyan theme (Panel-style)
- **ShopLoginPage.tsx**: Shop-specific with magenta theme

Both use `OAuthButtonGroup` but with different styling and copy.

#### ProtectedContent.tsx

Wrapper component for protected routes.

```tsx
<ProtectedContent>
  <SensitiveContent />
</ProtectedContent>
```

**Behavior**:
- Checks token storage for valid auth
- Attempts token exchange if session exists
- Redirects to `/login` if not authenticated
- Shows loading spinner during check

#### ProviderIcon.tsx

SVG icons for all supported OAuth providers.

**Supported Providers** (17 total):
- Amazon, Battle.net, Discord, Facebook, GitHub, Google, LEGO
- Microsoft, PayPal, Reddit, Roblox, Slack, Spotify, Steam
- Twitch, Xbox, Yahoo

**Features**:
- Proper brand colors for each provider
- Accessible with ARIA labels and titles
- Consistent sizing via className prop

### Layout Components (`src/components/layout/`)

#### AuthHeader.tsx

Auth-aware header component for the marketing layout.

**States**:
1. **Loading**: Animated placeholder skeleton
2. **Authenticated**: User avatar, name, profile link, sign-out button
3. **Not Authenticated**: "Sign In" link, "Get Started" CTA button

**Features**:
- Fetches user profile on mount
- Shows first letter of name/email as avatar
- Responsive (hides name on mobile)

### Profile Components (`src/components/profile/`)

#### ProfilePage.tsx

Full-page profile component with comprehensive user information.

**Data Fetched**:
- User profile (from `/api/v1/identity/me`)
- Organizations (from `/api/v1/identity/me/organizations`)
- Linked accounts (from `/api/v1/identity/me/linked-accounts`)

**Features**:
- Provider-specific color styling for linked accounts
- Relative time formatting ("2h ago", "3d ago")
- Loading and error states
- Sign-out functionality

### UI Components (`src/components/ui/`)

#### GlowButton.tsx

Themed button component with cyberpunk styling.

**Variants**:
- `primary`: Cyan glow (default)
- `secondary`: Neutral/gray
- `danger`: Magenta glow
- `ghost`: Transparent

**Sizes**: `sm`, `md`, `lg`

**Props**:
- `isLoading`: Shows spinner
- `icon`: Leading icon
- `fullWidth`: 100% width

#### Panel.tsx

Card/container component with consistent styling.

**Variants**:
- `default`: Standard dark panel
- `elevated`: With inner glow shadow
- `bordered`: Cyan border with glow

**Props**:
- `header`: Optional header content
- `footer`: Optional footer content
- `noPadding`: Remove default padding

#### LoadingSpinner.tsx

Animated circular loading indicator.

**Sizes**: `sm` (16px), `md` (32px), `lg` (48px)

**Style**: Cyan color with spinning animation.

---

## Layouts

### BaseLayout.astro

Foundation layout for all pages.

**Features**:
- HTML5 document structure
- Dark mode class on `<html>`
- Google Fonts preconnect and loading (Orbitron, Inter, JetBrains Mono)
- Meta tags (description, viewport)
- Global CSS import
- Favicon

### AuthLayout.astro

Extends BaseLayout for authentication pages.

**Features**:
- Full-screen centered layout
- Background grid pattern
- Gradient overlays (magenta/cyan)
- Ambient glow effects (blurred circles)

### MarketingLayout.astro

Extends BaseLayout for marketing pages.

**Features**:
- Fixed header with navigation
- Auth-aware header component (shows user or sign-in)
- Main content area
- Footer with links (Privacy, Terms, Support)

**Navigation Items**:
- Features (anchor link)
- Pricing (anchor link)
- Docs (external link)

---

## Authentication

### Architecture Overview

ShoppingCart uses a three-tier authentication architecture:

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   OAuth         │ --> │   BetterAuth    │ --> │   Identity      │
│   Provider      │     │   (Session)     │     │   Service       │
│   (Google,etc)  │     │   Gateway       │     │   (JWT Tokens)  │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

1. **OAuth Provider**: External identity provider (Google, Microsoft, etc.)
2. **BetterAuth**: Session-based authentication layer at the Gateway
3. **Identity Service**: Issues JWT access/refresh tokens for API access

### OAuth Flow

```
User clicks "Sign in with Google"
        │
        ▼
┌─────────────────────────────────────────────────────────────┐
│ 1. OAuthButtonGroup calls authClient.signIn({ provider })   │
│    - POST to /api/v1/betterauth/sign-in/social              │
│    - Server returns OAuth provider redirect URL              │
└─────────────────────────────────────────────────────────────┘
        │
        ▼
┌─────────────────────────────────────────────────────────────┐
│ 2. Browser redirects to OAuth provider (e.g., Google)       │
│    - User authenticates with provider                        │
│    - Provider redirects back to /callback                    │
└─────────────────────────────────────────────────────────────┘
        │
        ▼
┌─────────────────────────────────────────────────────────────┐
│ 3. CallbackHandler exchanges tokens                          │
│    a. POST to /api/v1/betterauth/exchange                   │
│       - Gets one-time exchange token                         │
│    b. POST to /api/v1/identity/exchange                     │
│       - Exchanges for access/refresh tokens                  │
│    c. Stores tokens in sessionStorage                       │
└─────────────────────────────────────────────────────────────┘
        │
        ▼
┌─────────────────────────────────────────────────────────────┐
│ 4. Redirect to /profile (or original destination)           │
│    - Tokens used for subsequent API calls                    │
└─────────────────────────────────────────────────────────────┘
```

### Supported OAuth Providers

The following providers are configured in SharedAuth and displayed on the login page:

| Category | Providers |
|----------|-----------|
| Social & Identity | Google, Microsoft, Facebook, Amazon, Yahoo |
| Gaming | Discord, Steam, Xbox, Twitch, Battle.net, Roblox |
| Other | GitHub, Slack, Reddit, Spotify, PayPal, LEGO |

**Note**: Xbox uses Microsoft OAuth under the hood (same OAuth configuration with different UI presentation).

### SharedAuth Package

The authentication logic is shared between ShoppingCart and other frontends via the `@dhadgar/shared-auth` local npm package.

**Location**: `/src/Dhadgar.SharedAuth`

**Exports**:
- `createAuthClient(config)`: Factory for auth client
- `tokenStorage`: Session storage utilities
- `OAUTH_PROVIDERS`: Provider metadata (colors, icons)
- `SIGN_IN_CATEGORIES`: UI category configuration

**Key Functions**:
- `authClient.signIn({ provider, callbackURL })`: Start OAuth flow
- `authClient.signOut()`: Clear session and tokens
- `authClient.getSession()`: Get BetterAuth session
- `authClient.exchangeTokens()`: Trade session for JWT tokens
- `authClient.refreshTokens()`: Refresh expired tokens
- `authClient.isAuthenticated()`: Check auth status

### Token Storage

Tokens are stored in **sessionStorage** (not localStorage) for security:
- `meridian_access_token`: JWT access token
- `meridian_refresh_token`: Refresh token
- `meridian_expires_at`: Token expiry timestamp

**Security Notes**:
- Session storage clears on tab close
- Tokens are not persisted across sessions
- Refresh tokens enable extended sessions within a tab

### Token Refresh

The auth system implements proactive token refresh:

1. **Automatic Check**: Every 60 seconds, check if tokens are expiring soon
2. **Expiring Soon Threshold**: 5 minutes before expiry
3. **Expired Threshold**: 60 seconds before expiry (considered expired)
4. **401 Retry**: API client automatically refreshes and retries on 401

---

## API Integration

### API Client (`src/lib/auth/api.ts`)

The `apiClient` function provides authenticated API calls with automatic token handling.

**Features**:
- Automatic JWT Bearer token injection
- Automatic token refresh on 401 responses
- Retry logic after successful refresh
- Typed responses with error handling

**Usage**:
```typescript
import { api } from '../lib/auth';

// GET request
const result = await api.get<UserProfile>('/api/v1/identity/me');

// POST request
const result = await api.post<Response>('/api/v1/endpoint', { data });

// Non-authenticated request
const result = await api.get<Data>('/public/endpoint', { requireAuth: false });
```

### Identity API Helpers

Pre-built functions for common Identity service calls:

```typescript
import { identityApi } from '../lib/auth/api';

// Get current user profile
const profile = await identityApi.getProfile();

// Get user's organizations
const organizations = await identityApi.getOrganizations();

// Get linked accounts
const linkedAccounts = await identityApi.getLinkedAccounts();

// Get user permissions
const permissions = await identityApi.getPermissions();
```

### Response Types

```typescript
interface ApiResponse<T> {
  data?: T;
  error?: string;
  status: number;
}

interface UserProfile {
  id: string;
  email: string;
  displayName: string | null;
  emailVerified: boolean;
  preferredOrganizationId: string | null;
  hasPasskeysRegistered: boolean;
  createdAt: string;
  lastAuthenticatedAt: string;
  authProviders: AuthProvider[];
}

interface Organization {
  id: string;
  name: string;
  slug: string;
  role: string;
  joinedAt: string;
  isPreferred: boolean;
}

interface LinkedAccount {
  id: string;
  provider: string;
  providerDisplayName: string | null;
  linkedAt: string;
  lastUsedAt: string | null;
}
```

---

## Styling

### Tailwind Configuration

The project uses a customized Tailwind CSS configuration (`tailwind.config.mjs`) with:

#### Custom Colors

```javascript
colors: {
  // Cyberpunk accent colors
  'cyber-cyan': '#00D4FF',
  'cyber-magenta': '#FF00AA',  // Primary for ShoppingCart
  'cyber-amber': '#FFB000',
  'cyber-green': '#00FF88',

  // Background colors
  'space-dark': '#0A0E17',
  'panel-dark': '#111827',
  'panel-darker': '#0D1117',
  'glow-line': '#1E3A5F',

  // Text colors
  'text-primary': '#E8F0FF',
  'text-secondary': '#6B7B8F',
  'text-muted': '#4A5568',

  // Brand accent (magenta for shop)
  'brand-primary': '#FF00AA',
  'brand-glow': 'rgba(255, 0, 170, 0.3)',
}
```

#### Custom Fonts

```javascript
fontFamily: {
  display: ['Orbitron', 'system-ui', 'sans-serif'],  // Headings
  body: ['Inter', 'system-ui', 'sans-serif'],         // Body text
  mono: ['JetBrains Mono', 'Consolas', 'monospace'], // Code
}
```

#### Custom Shadows (Glow Effects)

```javascript
boxShadow: {
  'glow-cyan': '0 0 20px rgba(0, 212, 255, 0.3)',
  'glow-magenta': '0 0 20px rgba(255, 0, 170, 0.3)',
  'glow-magenta-lg': '0 0 40px rgba(255, 0, 170, 0.4)',
  'glow-amber': '0 0 20px rgba(255, 176, 0, 0.3)',
  'glow-green': '0 0 20px rgba(0, 255, 136, 0.3)',
  'inner-glow': 'inset 0 0 20px rgba(255, 0, 170, 0.1)',
}
```

#### Custom Animations

```javascript
animation: {
  'pulse-glow': 'pulse-glow 2s ease-in-out infinite',
  'fade-in': 'fade-in 0.3s ease-out',
  'slide-up': 'slide-up 0.3s ease-out',
}
```

### Global CSS (`src/styles/global.css`)

Custom CSS beyond Tailwind utilities:

**Scrollbar Styling**:
- Dark track background
- Magenta hover color on thumb

**Selection Colors**:
- Magenta background
- Light text

**Focus Styles**:
- Magenta outline for keyboard navigation

**Component Classes**:
- `.text-glow`: Text shadow effect
- `.glass`: Frosted glass effect
- `.status-online`/`.status-offline`: Status indicators
- `.data-label`/`.data-value`: Data display styling
- `.gradient-text`: Magenta-to-cyan gradient text

---

## Planned Features

The following features are planned but not yet implemented:

### Marketing Pages

| Feature | Description | Priority |
|---------|-------------|----------|
| Blog/News | Company updates, tutorials, announcements | Medium |
| About Page | Company information, team, mission | Low |
| Contact Page | Support contact form | Medium |
| FAQ Page | Frequently asked questions | Medium |
| Comparison Page | Compare with competitors | Low |

### Checkout Flow

| Feature | Description | Priority |
|---------|-------------|----------|
| Stripe Integration | Payment processing | High |
| Plan Selection | Choose subscription tier | High |
| Payment Form | Credit card entry | High |
| Order Summary | Review before purchase | High |
| Success Page | Post-purchase confirmation | High |
| Coupon/Promo Codes | Discount application | Medium |

### Subscription Management

| Feature | Description | Priority |
|---------|-------------|----------|
| Current Plan Display | Show active subscription | High |
| Upgrade/Downgrade | Change subscription tier | High |
| Cancel Subscription | Self-service cancellation | High |
| Invoice History | Past invoices and receipts | Medium |
| Payment Method Update | Change credit card | Medium |
| Usage Dashboard | Show resource consumption | Medium |

### User Account

| Feature | Description | Priority |
|---------|-------------|----------|
| Profile Editing | Update display name, avatar | Medium |
| Link Additional Accounts | Connect more OAuth providers | Medium |
| Unlink Accounts | Remove OAuth connections | Medium |
| Email Preferences | Marketing communication settings | Low |
| Delete Account | GDPR compliance | Medium |

### Technical Improvements

| Feature | Description | Priority |
|---------|-------------|----------|
| Analytics Integration | Track user behavior | Medium |
| A/B Testing | Landing page optimization | Low |
| SEO Optimization | Meta tags, structured data | Medium |
| Performance Monitoring | Core Web Vitals tracking | Medium |
| Error Tracking | Sentry or similar integration | High |

---

## Configuration

### Environment Variables

Environment variables are defined in `env.d.ts`:

```typescript
interface ImportMetaEnv {
  readonly PUBLIC_GATEWAY_URL: string;
  readonly PUBLIC_APP_NAME: string;
}
```

**Setting Environment Variables**:

For local development, create a `.env` file in the project root:

```env
PUBLIC_GATEWAY_URL=http://localhost:5000
PUBLIC_APP_NAME=Meridian Console
```

For production, set these in Azure Static Web Apps configuration.

### Astro Configuration (`astro.config.mjs`)

```javascript
export default defineConfig({
  integrations: [react(), tailwind()],
  output: 'static',        // Static site generation
  build: {
    assets: '_assets'      // Asset directory name
  },
  outDir: '_swa_publish/wwwroot'  // Output for Azure SWA
});
```

### TypeScript Configuration (`tsconfig.json`)

```json
{
  "extends": "astro/tsconfigs/strict",
  "compilerOptions": {
    "jsx": "react-jsx",
    "jsxImportSource": "react",
    "baseUrl": ".",
    "paths": {
      "@/*": ["src/*"],
      "@components/*": ["src/components/*"],
      "@lib/*": ["src/lib/*"],
      "@layouts/*": ["src/layouts/*"]
    }
  }
}
```

**Path Aliases**:
- `@/` - `src/`
- `@components/` - `src/components/`
- `@lib/` - `src/lib/`
- `@layouts/` - `src/layouts/`

---

## Building for Production

### Using npm

```bash
# Build static site
npm run build

# Preview production build locally
npm run preview
```

### Using .NET (via shim)

```bash
# From repository root
dotnet build src/Dhadgar.ShoppingCart

# Or publish
dotnet publish src/Dhadgar.ShoppingCart
```

### Build Output

The build outputs to `_swa_publish/wwwroot/`:

```
_swa_publish/
└── wwwroot/
    ├── index.html
    ├── login/index.html
    ├── callback/index.html
    ├── profile/index.html
    ├── _assets/
    │   ├── *.css
    │   └── *.js
    └── favicon.svg
```

All pages are pre-rendered as static HTML with client-side hydration for interactive components.

---

## Deployment

### Azure Static Web Apps

ShoppingCart deploys to Azure Static Web Apps (SWA).

**Deployment Directory**: `_swa_publish/wwwroot/`

**Configuration**:
- App location: `src/Dhadgar.ShoppingCart`
- Output location: `_swa_publish/wwwroot`
- API location: `api` (Azure Functions placeholder)

### CI/CD Pipeline

The deployment is handled via Azure Pipelines (`azure-pipelines.yml`).

**Steps**:
1. Install Node.js
2. Run `npm install`
3. Run `npm run build`
4. Deploy `_swa_publish/wwwroot/` to Azure SWA

### Azure Functions API

The `api/` directory contains Azure Functions configuration for serverless API endpoints. Currently includes only a placeholder `Hello` function.

**host.json**:
```json
{
  "version": "2.0"
}
```

**Future Use**: Server-side API endpoints that don't fit in the static site model (e.g., webhook handlers, form submissions).

---

## Testing

### Current Test Coverage

A placeholder test project exists at `/tests/Dhadgar.ShoppingCart.Tests/` but contains minimal tests since the ShoppingCart is primarily a static marketing site with client-side interactivity.

### Running Tests

```bash
# From repository root
dotnet test tests/Dhadgar.ShoppingCart.Tests
```

### Testing Strategy

For this frontend project, consider:

1. **Component Tests** (Vitest + Testing Library):
   - Test React components in isolation
   - Test auth state management
   - Test API client behavior

2. **E2E Tests** (Playwright):
   - Test OAuth login flow
   - Test navigation
   - Test responsive design

3. **Visual Regression Tests**:
   - Capture screenshots of key pages
   - Compare against baselines

---

## Related Documentation

### Project Documentation

| Document | Location | Description |
|----------|----------|-------------|
| Main README | `/README.md` | Repository overview |
| CLAUDE.md | `/CLAUDE.md` | AI assistant instructions |
| Scope README | `/src/Dhadgar.Scope/README.md` | Documentation site details |
| SharedAuth CLAUDE | `/src/Dhadgar.SharedAuth/CLAUDE.md` | Shared auth library |
| Docker Compose | `/deploy/compose/README.md` | Local infrastructure |
| Container Build | `/deploy/kubernetes/CONTAINER-BUILD-SETUP.md` | Docker/ACR setup |

### External Documentation

| Technology | URL |
|------------|-----|
| Astro Docs | https://docs.astro.build |
| React Docs | https://react.dev |
| Tailwind CSS | https://tailwindcss.com/docs |
| Azure Static Web Apps | https://docs.microsoft.com/azure/static-web-apps |

### Related Services

| Service | Description | Relationship |
|---------|-------------|--------------|
| Dhadgar.Gateway | API Gateway | Routes auth requests |
| Dhadgar.Identity | User management | Issues JWT tokens |
| Dhadgar.Panel | Main application | Post-login destination |
| Dhadgar.Scope | Documentation | Linked from marketing site |

---

## Troubleshooting

### Common Issues

**"Cannot find module '@dhadgar/shared-auth'"**

The SharedAuth package is a local npm package. Ensure it's built:
```bash
cd src/Dhadgar.SharedAuth
npm install
npm run build
```

Then reinstall ShoppingCart dependencies:
```bash
cd src/Dhadgar.ShoppingCart
rm -rf node_modules
npm install
```

**OAuth Callback Errors**

Check that:
1. Gateway is running (`dotnet run --project src/Dhadgar.Gateway`)
2. Identity service is running
3. `PUBLIC_GATEWAY_URL` is set correctly
4. OAuth provider is configured in BetterAuth

**Styles Not Loading**

Clear the Astro cache:
```bash
rm -rf .astro
npm run dev
```

**Port Conflicts**

ShoppingCart runs on port 4322 by default. Change in `package.json`:
```json
"scripts": {
  "dev": "astro dev --port 4323"
}
```

---

## License

This project is part of the Meridian Console platform. See the repository root for license information.

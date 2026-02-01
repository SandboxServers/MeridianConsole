# Dhadgar.Panel

![Status: Stub](https://img.shields.io/badge/Status-Stub-red)

The main control plane user interface for Meridian Console - a modern, security-first game server control plane.

## Table of Contents

1. [Overview](#overview)
2. [Current Status](#current-status)
3. [Tech Stack](#tech-stack)
4. [Quick Start](#quick-start)
5. [Project Structure](#project-structure)
6. [Architecture](#architecture)
7. [Components](#components)
8. [Authentication](#authentication)
9. [API Integration](#api-integration)
10. [Styling and Theming](#styling-and-theming)
11. [Configuration](#configuration)
12. [Building](#building)
13. [Deployment](#deployment)
14. [Testing](#testing)
15. [Planned Features](#planned-features)
16. [Troubleshooting](#troubleshooting)
17. [Related Documentation](#related-documentation)

---

## Overview

**Dhadgar.Panel** is the primary administrative dashboard for Meridian Console. It serves as the main interface through which users manage their game servers, monitor nodes, handle organization settings, and access real-time console output. Built with Astro, React, and Tailwind CSS, the Panel provides a modern, responsive, and performant user experience with a distinctive cyberpunk-inspired visual design.

### What Panel Does

The Panel is the "mission control" for game server operators. It provides:

- **Server Management**: Create, configure, start, stop, and delete game server instances
- **Node Monitoring**: View connected agent nodes, their health status, and resource utilization
- **Organization Management**: Manage teams, invite members, assign roles and permissions
- **Real-time Console**: Stream live console output from running game servers via SignalR
- **Billing Dashboard**: View subscription status, usage, and manage payments (SaaS edition)
- **User Profile**: Manage linked OAuth accounts, view sessions, and configure preferences

### What Panel Is NOT

- **Not a game server**: Panel orchestrates servers; it doesn't run them
- **Not a hosting provider interface**: Servers run on customer-owned nodes, not Meridian infrastructure
- **Not a static documentation site**: That's `Dhadgar.Scope`
- **Not a marketing/checkout site**: That's `Dhadgar.ShoppingCart`

---

## Current Status

**Development Phase**: Early-stage scaffolding with foundational structure in place.

### What Exists Today

| Component | Status | Description |
|-----------|--------|-------------|
| Project Setup | Complete | Astro 5.1.1 with React and Tailwind CSS integration |
| SSR Configuration | Complete | Node.js adapter for server-side rendering |
| Authentication Flow | Complete | OAuth login, callback handling, token management |
| Layout System | Complete | Base, Auth, and Dashboard layouts |
| UI Component Library | Partial | Panel, GlowButton, LoadingSpinner components |
| Login Page | Complete | OAuth provider buttons with cyberpunk styling |
| Dashboard Page | Scaffolding | Basic user info display with placeholder stats |
| Shared Auth Library | Complete | `@dhadgar/shared-auth` integration |
| Dockerfile | Complete | Multi-stage build for Kubernetes deployment |
| .NET Build Integration | Complete | `dotnet build` triggers `npm install && npm build` |

### What's Planned (Not Yet Implemented)

| Feature | Priority | Notes |
|---------|----------|-------|
| Server List/Detail Views | High | CRUD operations, status display |
| Node Monitoring Dashboard | High | Health, capacity, real-time metrics |
| Organization Settings | High | Members, roles, invitations |
| Real-time Console | Medium | SignalR integration for log streaming |
| Billing Portal | Medium | Stripe integration (SaaS edition only) |
| User Profile Page | Medium | Linked accounts, sessions, preferences |
| Mod Management | Low | Mod repository browser and installer |
| File Manager | Low | Server file browsing and editing |
| Task History | Low | Provisioning/operation audit log |

### Migration Note

The Panel was originally built with Blazor WebAssembly (see legacy `Pages/` and `Shared/` directories). The project has been migrated to Astro/React/Tailwind to align with modern frontend best practices. The legacy `.razor` files remain as reference but are not used by the current build.

---

## Tech Stack

### Core Framework

| Technology | Version | Purpose |
|------------|---------|---------|
| **Astro** | 5.1.1 | Static site generation with islands architecture |
| **React** | 18.3.1 | Interactive UI components (islands) |
| **Tailwind CSS** | 3.4.16 | Utility-first CSS framework |
| **TypeScript** | 5.7.2 | Type-safe JavaScript |
| **Node.js** | 22+ | Runtime for SSR and build tools |

### Key Dependencies

| Package | Purpose |
|---------|---------|
| `@astrojs/react` | React integration for Astro |
| `@astrojs/tailwind` | Tailwind CSS integration |
| `@astrojs/node` | Node.js adapter for SSR deployment |
| `@dhadgar/shared-auth` | Shared authentication client library |
| `clsx` | Conditional CSS class utility |

### Why Astro?

Astro was chosen for several reasons:

1. **Islands Architecture**: Only ship JavaScript for interactive components (React islands). Static content requires zero JS.
2. **SSR Support**: Server-side rendering for initial page loads improves performance and SEO.
3. **Framework Agnostic**: Can use React, Vue, Svelte, or vanilla JS components as needed.
4. **Performance**: Consistently scores high on Core Web Vitals benchmarks.
5. **Developer Experience**: Fast builds, hot module replacement, and excellent TypeScript support.

### Why Not Blazor?

The original Blazor WebAssembly implementation had several drawbacks:

- Large initial payload (~2-5MB for .NET runtime)
- Limited ecosystem for modern UI components
- Complexity in integrating with JavaScript-based tooling
- Slower development iteration compared to modern JS frameworks

---

## Quick Start

### Prerequisites

- Node.js 22 or later
- npm (comes with Node.js)
- The backend services running (Gateway, Identity, BetterAuth)

### Development Server

```bash
# Navigate to the Panel directory
cd src/Dhadgar.Panel

# Install dependencies
npm install

# Start development server with hot reload
npm run dev
```

The dev server starts at `http://localhost:4321` by default.

### Using .NET Build

The Panel integrates with the .NET solution build system:

```bash
# Build Panel via dotnet (runs npm install && npm build)
dotnet build src/Dhadgar.Panel

# Build entire solution including Panel
dotnet build
```

### Environment Variables

Create a `.env` file in the Panel directory for local development:

```env
# Gateway URL for API calls
PUBLIC_GATEWAY_URL=http://localhost:5000

# Application name (used in UI)
PUBLIC_APP_NAME=Meridian Console
```

**Note**: Environment variables prefixed with `PUBLIC_` are exposed to the client bundle. Never prefix sensitive values with `PUBLIC_`.

---

## Project Structure

```
src/Dhadgar.Panel/
├── src/
│   ├── components/           # React components (Astro islands)
│   │   ├── auth/            # Authentication components
│   │   │   ├── AuthProvider.tsx      # React context for auth state
│   │   │   ├── CallbackHandler.tsx   # OAuth callback processing
│   │   │   ├── LoginPage.tsx         # Login page content
│   │   │   ├── OAuthButtonGroup.tsx  # OAuth provider buttons
│   │   │   ├── ProtectedContent.tsx  # Auth-required wrapper
│   │   │   ├── ProviderIcon.tsx      # OAuth provider icons
│   │   │   └── index.ts              # Barrel export
│   │   ├── dashboard/       # Dashboard page components
│   │   │   └── DashboardContent.tsx  # Main dashboard content
│   │   └── ui/              # Reusable UI components
│   │       ├── GlowButton.tsx        # Styled button component
│   │       ├── LoadingSpinner.tsx    # Loading indicator
│   │       ├── Panel.tsx             # Card/panel component
│   │       └── index.ts              # Barrel export
│   ├── layouts/             # Astro layout components
│   │   ├── AuthLayout.astro          # Layout for auth pages
│   │   ├── BaseLayout.astro          # Root HTML layout
│   │   └── DashboardLayout.astro     # Layout with nav/footer
│   ├── lib/                 # Utility libraries
│   │   └── auth/            # Auth utilities
│   │       ├── api.ts       # API client with JWT injection
│   │       ├── client.ts    # Auth client initialization
│   │       └── index.ts     # Barrel export
│   ├── pages/               # Astro pages (file-based routing)
│   │   ├── index.astro      # Root redirect
│   │   ├── login.astro      # Login page
│   │   ├── logout.astro     # Logout handler
│   │   ├── callback.astro   # OAuth callback
│   │   └── dashboard/
│   │       └── index.astro  # Dashboard page
│   └── styles/
│       └── global.css       # Global styles and Tailwind imports
├── public/                  # Static assets
│   ├── favicon.svg          # Site favicon
│   └── fonts/               # Custom fonts
├── Pages/                   # (Legacy) Blazor pages - not used
├── Shared/                  # (Legacy) Blazor layouts - not used
├── dist/                    # Build output
│   ├── client/              # Static assets for CDN
│   └── server/              # Node.js server bundle
├── astro.config.mjs         # Astro configuration
├── tailwind.config.mjs      # Tailwind CSS configuration
├── tsconfig.json            # TypeScript configuration
├── package.json             # Node.js dependencies
├── Dhadgar.Panel.csproj     # .NET shim for solution integration
├── Dockerfile               # Container build instructions
├── env.d.ts                 # TypeScript environment types
└── CLAUDE.md                # AI assistant context
```

### File-Based Routing

Astro uses file-based routing. Each `.astro` file in `src/pages/` becomes a route:

| File | Route |
|------|-------|
| `src/pages/index.astro` | `/` |
| `src/pages/login.astro` | `/login` |
| `src/pages/dashboard/index.astro` | `/dashboard` |
| `src/pages/servers/[id].astro` | `/servers/:id` (dynamic) |

---

## Architecture

### Islands Architecture

The Panel uses Astro's "islands" architecture where static HTML is rendered on the server, and only interactive components (React islands) receive JavaScript hydration.

```
┌─────────────────────────────────────────────────────────────────┐
│                        Astro Page                                │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Static HTML (no JavaScript required)                     │   │
│  │  - Navigation                                             │   │
│  │  - Page title                                             │   │
│  │  - Static content                                         │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─────────────────┐  ┌─────────────────┐  ┌────────────────┐  │
│  │  React Island    │  │  React Island    │  │  React Island   │  │
│  │  (Interactive)   │  │  (Interactive)   │  │  (Interactive)  │  │
│  │                  │  │                  │  │                 │  │
│  │  DashboardContent│  │  OAuthButtons    │  │  ServerList     │  │
│  │  client:load     │  │  client:load     │  │  client:visible │  │
│  └─────────────────┘  └─────────────────┘  └────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Hydration Strategies

Astro provides several hydration strategies for islands:

| Directive | When Hydrated | Use Case |
|-----------|---------------|----------|
| `client:load` | Immediately on page load | Auth state, critical interactivity |
| `client:idle` | After page becomes idle | Lower-priority components |
| `client:visible` | When scrolled into view | Below-the-fold content |
| `client:media` | At specific breakpoint | Mobile-only components |
| `client:only` | Never SSR, client-only | Components that can't SSR |

### Server-Side Rendering (SSR)

The Panel runs as an SSR application using the `@astrojs/node` adapter. This means:

1. Initial HTML is rendered on the server (fast first paint)
2. React components hydrate on the client for interactivity
3. Server handles routing and can access server-side resources
4. Deployed as a Node.js application (not static files)

### Data Flow

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   Panel (SSR)   │────▶│    Gateway      │────▶│  Backend APIs   │
│   Node.js       │     │    (YARP)       │     │                 │
│   Port 8080     │     │    Port 5000    │     │  Identity       │
└─────────────────┘     └─────────────────┘     │  Servers        │
                                                │  Nodes          │
                                                │  Tasks          │
                                                │  ...            │
                                                └─────────────────┘
```

---

## Components

### UI Components (`src/components/ui/`)

#### Panel

A versatile card/container component with multiple variants.

```tsx
import Panel from '../components/ui/Panel';

// Basic usage
<Panel>Content here</Panel>

// With header
<Panel header="Section Title">Content</Panel>

// Variants
<Panel variant="default">Default styling</Panel>
<Panel variant="elevated">Elevated with inner glow</Panel>
<Panel variant="bordered">Bordered with cyan glow</Panel>

// With footer
<Panel
  header="Settings"
  footer={<button>Save</button>}
>
  Form content
</Panel>
```

**Props:**

| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `variant` | `'default' \| 'elevated' \| 'bordered'` | `'default'` | Visual style |
| `header` | `ReactNode` | - | Header content or string |
| `footer` | `ReactNode` | - | Footer content |
| `noPadding` | `boolean` | `false` | Remove default padding |

#### GlowButton

A styled button with glow effects and multiple variants.

```tsx
import { GlowButton } from '../components/ui';

// Variants
<GlowButton variant="primary">Primary Action</GlowButton>
<GlowButton variant="secondary">Secondary</GlowButton>
<GlowButton variant="danger">Delete</GlowButton>
<GlowButton variant="ghost">Ghost</GlowButton>

// Sizes
<GlowButton size="sm">Small</GlowButton>
<GlowButton size="md">Medium</GlowButton>
<GlowButton size="lg">Large</GlowButton>

// States
<GlowButton isLoading>Saving...</GlowButton>
<GlowButton disabled>Disabled</GlowButton>
<GlowButton fullWidth>Full Width</GlowButton>

// With icon
<GlowButton icon={<PlusIcon />}>Add Server</GlowButton>
```

**Props:**

| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `variant` | `'primary' \| 'secondary' \| 'danger' \| 'ghost'` | `'primary'` | Color scheme |
| `size` | `'sm' \| 'md' \| 'lg'` | `'md'` | Button size |
| `isLoading` | `boolean` | `false` | Show loading spinner |
| `icon` | `ReactNode` | - | Icon element |
| `fullWidth` | `boolean` | `false` | Take full container width |

#### LoadingSpinner

An animated loading indicator.

```tsx
import { LoadingSpinner } from '../components/ui';

<LoadingSpinner />
<LoadingSpinner size="sm" />
<LoadingSpinner size="lg" />
```

### Auth Components (`src/components/auth/`)

#### LoginPage

The main login page content with OAuth provider buttons.

```tsx
import LoginPage from '../components/auth/LoginPage';

<LoginPage callbackURL="/callback" />
```

#### AuthProvider

React context provider for authentication state. Use with `useAuth` hook.

```tsx
import { AuthProvider, useAuth } from '../components/auth';

// Wrap your app
<AuthProvider>
  <App />
</AuthProvider>

// In a component
function MyComponent() {
  const { user, isAuthenticated, isLoading, signIn, signOut } = useAuth();

  if (isLoading) return <LoadingSpinner />;
  if (!isAuthenticated) return <LoginPage />;

  return <div>Welcome, {user.name}!</div>;
}
```

#### ProtectedContent

Wrapper component that redirects to login if not authenticated.

```tsx
import ProtectedContent from '../components/auth/ProtectedContent';

<ProtectedContent>
  <SecretContent />
</ProtectedContent>

// With custom fallback
<ProtectedContent fallback={<div>Please wait...</div>}>
  <SecretContent />
</ProtectedContent>
```

### Layouts (`src/layouts/`)

#### BaseLayout

The root HTML layout with fonts, meta tags, and global styles.

```astro
---
import BaseLayout from '../layouts/BaseLayout.astro';
---

<BaseLayout title="Page Title" description="Optional description">
  <main>Page content</main>
</BaseLayout>
```

#### AuthLayout

Layout for authentication pages with decorative background effects.

```astro
---
import AuthLayout from '../layouts/AuthLayout.astro';
---

<AuthLayout title="Login | Meridian Console">
  <LoginForm />
</AuthLayout>
```

#### DashboardLayout

Main application layout with navigation, header, and footer.

```astro
---
import DashboardLayout from '../layouts/DashboardLayout.astro';
---

<DashboardLayout title="Dashboard | Meridian Console">
  <DashboardContent client:load />
</DashboardLayout>
```

---

## Authentication

### Overview

The Panel uses a hybrid authentication system:

1. **BetterAuth**: Handles OAuth flows (Google, Discord, GitHub, etc.) and session management
2. **Identity Service**: Issues JWT access/refresh tokens for API authorization
3. **Shared Auth Library**: `@dhadgar/shared-auth` provides the client-side authentication logic

### Authentication Flow

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│    Panel     │     │  BetterAuth  │     │   Identity   │     │    APIs      │
│   (Client)   │     │  (Gateway)   │     │   Service    │     │              │
└──────┬───────┘     └──────┬───────┘     └──────┬───────┘     └──────┬───────┘
       │                    │                    │                    │
       │ 1. Click OAuth     │                    │                    │
       │─────────────────▶  │                    │                    │
       │                    │                    │                    │
       │ 2. Redirect to     │                    │                    │
       │    Provider        │                    │                    │
       │◀─────────────────  │                    │                    │
       │                    │                    │                    │
       │ 3. User authenticates with OAuth provider                   │
       │                    │                    │                    │
       │ 4. Callback with   │                    │                    │
       │    session cookie  │                    │                    │
       │◀─────────────────  │                    │                    │
       │                    │                    │                    │
       │ 5. Request exchange│                    │                    │
       │    token           │                    │                    │
       │─────────────────▶  │                    │                    │
       │                    │                    │                    │
       │ 6. Exchange token  │                    │                    │
       │◀─────────────────  │                    │                    │
       │                    │                    │                    │
       │ 7. Exchange for JWTs                    │                    │
       │─────────────────────────────────────▶   │                    │
       │                    │                    │                    │
       │ 8. Access + Refresh tokens              │                    │
       │◀─────────────────────────────────────   │                    │
       │                    │                    │                    │
       │ 9. API request with Bearer token                            │
       │────────────────────────────────────────────────────────────▶│
       │                    │                    │                    │
       │ 10. API response   │                    │                    │
       │◀────────────────────────────────────────────────────────────│
```

### Token Storage

Tokens are stored in `sessionStorage` for security:

| Key | Content |
|-----|---------|
| `meridian_access_token` | JWT access token (15-minute expiry) |
| `meridian_refresh_token` | Opaque refresh token (7-day expiry) |
| `meridian_expires_at` | Unix timestamp when access token expires |

**Why sessionStorage?**

- Cleared when tab/window closes (session-scoped)
- Not accessible to other tabs (isolation)
- Better than localStorage for sensitive tokens
- JavaScript-accessible for SPA token refresh

### Supported OAuth Providers

The Panel supports multiple OAuth providers (configured via BetterAuth):

| Provider | Category | Status |
|----------|----------|--------|
| Google | Social | Configured |
| Microsoft | Social | Configured |
| Facebook | Social | Planned |
| Discord | Gaming | Configured |
| Steam | Gaming | Planned |
| Xbox | Gaming | Planned |
| Twitch | Gaming | Planned |
| GitHub | Developer | Configured |

### Using the Auth API Client

The `api` utility automatically injects JWT tokens and handles refresh:

```typescript
import { api } from '../lib/auth';

// GET request
const { data, error, status } = await api.get<User[]>('/api/v1/identity/me/organizations');

// POST request
const { data, error } = await api.post<Server>('/api/v1/servers', {
  name: 'My Server',
  game: 'minecraft',
  nodeId: '...'
});

// Request without auth
const { data } = await api.get('/public/health', { requireAuth: false });
```

### Adding Protected Routes

To protect a page, use the `ProtectedContent` component:

```astro
---
import DashboardLayout from '../layouts/DashboardLayout.astro';
import ProtectedContent from '../components/auth/ProtectedContent';
import MyPageContent from '../components/MyPageContent';
---

<DashboardLayout title="Protected Page">
  <ProtectedContent client:load>
    <MyPageContent client:load />
  </ProtectedContent>
</DashboardLayout>
```

---

## API Integration

### Gateway Routing

All API requests go through the YARP Gateway at `PUBLIC_GATEWAY_URL`:

| Route | Target Service |
|-------|----------------|
| `/api/v1/identity/*` | Identity service |
| `/api/v1/servers/*` | Servers service |
| `/api/v1/nodes/*` | Nodes service |
| `/api/v1/tasks/*` | Tasks service |
| `/api/v1/files/*` | Files service |
| `/api/v1/mods/*` | Mods service |
| `/api/v1/betterauth/*` | BetterAuth (embedded in Gateway) |

### API Client Usage

The `apiClient` function handles:

1. Automatic JWT injection via `Authorization: Bearer <token>` header
2. Token refresh when receiving 401 responses
3. JSON serialization/deserialization
4. Error handling with Problem Details support

```typescript
import { api, apiClient } from '../lib/auth';

// Using convenience methods
const servers = await api.get<Server[]>('/api/v1/servers');
const server = await api.post<Server>('/api/v1/servers', { name: 'New Server' });
await api.delete(`/api/v1/servers/${serverId}`);

// Using raw apiClient for more control
const response = await apiClient<CustomResponse>('/api/v1/custom', {
  method: 'PUT',
  body: JSON.stringify(data),
  requireAuth: true,
  headers: {
    'X-Custom-Header': 'value'
  }
});
```

### Response Types

```typescript
interface ApiResponse<T> {
  data?: T;        // Response data (on success)
  error?: string;  // Error message (on failure)
  status: number;  // HTTP status code
}
```

### Error Handling

```typescript
const { data, error, status } = await api.get<Server[]>('/api/v1/servers');

if (error) {
  switch (status) {
    case 401:
      // Redirect to login
      window.location.href = '/login';
      break;
    case 403:
      // Show permission denied
      showError('You do not have permission to view servers');
      break;
    case 404:
      // Resource not found
      showError('Server not found');
      break;
    default:
      // Generic error
      showError(error);
  }
  return;
}

// Success - use data
renderServers(data);
```

---

## Styling and Theming

### Design System

The Panel uses a cyberpunk-inspired design system with:

- **Dark background**: Deep space blacks and dark grays
- **Cyan accents**: Primary interactive color
- **Glow effects**: Neon-style glows on interactive elements
- **Monospace fonts**: For technical data display
- **Geometric patterns**: Grid backgrounds and angular elements

### Color Palette

Defined in `tailwind.config.mjs`:

| Token | Value | Usage |
|-------|-------|-------|
| `cyber-cyan` | `#00D4FF` | Primary accent, links, buttons |
| `cyber-magenta` | `#FF00AA` | Errors, danger actions |
| `cyber-amber` | `#FFB000` | Warnings, pending states |
| `cyber-green` | `#00FF88` | Success, online status |
| `space-dark` | `#0A0E17` | Page background |
| `panel-dark` | `#111827` | Card backgrounds |
| `panel-darker` | `#0D1117` | Secondary surfaces |
| `glow-line` | `#1E3A5F` | Borders, dividers |
| `text-primary` | `#E8F0FF` | Main text |
| `text-secondary` | `#6B7B8F` | Secondary text |
| `text-muted` | `#4A5568` | Disabled/muted text |

### Typography

| Class | Font | Usage |
|-------|------|-------|
| `font-display` | Orbitron | Headings, branding |
| `font-body` | Inter | Body text, UI labels |
| `font-mono` | JetBrains Mono | Code, data values |

### Custom Utilities

From `global.css`:

```css
/* Text glow effect */
.text-glow { text-shadow: 0 0 10px currentColor; }

/* Glass morphism */
.glass {
  background: rgba(17, 24, 39, 0.8);
  backdrop-filter: blur(10px);
  border: 1px solid rgba(30, 58, 95, 0.5);
}

/* Status indicators */
.status-online { @apply w-2 h-2 rounded-full bg-cyber-green shadow-glow-green; }
.status-offline { @apply w-2 h-2 rounded-full bg-cyber-magenta shadow-glow-magenta; }
.status-warning { @apply w-2 h-2 rounded-full bg-cyber-amber shadow-glow-amber; }

/* Data display */
.data-label { @apply text-text-muted text-xs font-mono uppercase tracking-wider; }
.data-value { @apply text-text-primary font-mono; }

/* Gradient text */
.gradient-text { @apply bg-gradient-to-r from-cyber-cyan to-cyber-magenta bg-clip-text text-transparent; }
```

### Animations

| Class | Effect |
|-------|--------|
| `animate-pulse-glow` | Pulsing glow on buttons |
| `animate-scan-line` | Sci-fi scan line effect |
| `animate-fade-in` | Fade in on mount |
| `animate-slide-up` | Slide up with fade |

---

## Configuration

### Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `PUBLIC_GATEWAY_URL` | Yes | `http://localhost:5000` | Gateway base URL |
| `PUBLIC_APP_NAME` | No | `Meridian Console` | Application name |
| `HOST` | No | `0.0.0.0` | Server bind address |
| `PORT` | No | `8080` | Server port |
| `NODE_ENV` | No | `development` | Environment mode |

### Astro Configuration

`astro.config.mjs`:

```javascript
import { defineConfig } from 'astro/config';
import react from '@astrojs/react';
import tailwind from '@astrojs/tailwind';
import node from '@astrojs/node';

export default defineConfig({
  // Enable React and Tailwind integrations
  integrations: [react(), tailwind()],

  // SSR mode with Node.js adapter
  output: 'server',
  adapter: node({
    mode: 'standalone'  // Single entry point for deployment
  }),

  // Development server settings
  server: {
    port: 4321,
    host: true  // Listen on all interfaces
  }
});
```

### TypeScript Configuration

`tsconfig.json`:

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

### Tailwind Configuration

See `tailwind.config.mjs` for the complete theme configuration including:

- Custom color palette
- Custom fonts
- Glow shadow effects
- Custom animations
- Grid pattern backgrounds

---

## Building

### Development Build

```bash
npm run dev
```

Starts the Astro dev server with hot module replacement at `http://localhost:4321`.

### Production Build

```bash
npm run build
```

Creates an optimized production build in `dist/`:

- `dist/server/` - Node.js server bundle
- `dist/client/` - Static assets (CSS, JS, images)

### Preview Production Build

```bash
npm run preview
```

Runs the production build locally for testing.

### Build via .NET

The `.csproj` file is configured to run npm commands during `dotnet build`:

```xml
<Target Name="NpmInstall" ...>
  <Exec Command="npm install" WorkingDirectory="$(PanelProjectDir)" />
</Target>

<Target Name="NpmBuild" AfterTargets="NpmInstall">
  <Exec Command="npm run build" WorkingDirectory="$(PanelProjectDir)" />
</Target>
```

This allows building the entire solution with `dotnet build`.

### Build Output

After building, the output is in `dist/`:

```
dist/
├── client/                    # Static assets
│   ├── _astro/               # Hashed JS/CSS bundles
│   ├── favicon.svg
│   └── fonts/
└── server/                   # Node.js server
    ├── entry.mjs             # Server entry point
    ├── chunks/               # Code-split chunks
    ├── pages/                # SSR page handlers
    ├── manifest_*.mjs        # Asset manifest
    └── renderers.mjs         # React renderer
```

---

## Deployment

### Running Locally

```bash
# After building
npm run start

# Or directly
node dist/server/entry.mjs
```

The server starts on port 8080 (configurable via `PORT` environment variable).

### Docker Deployment

Build the container:

```bash
# From repository root
docker build -f src/Dhadgar.Panel/Dockerfile -t dhadgar-panel .
```

Run the container:

```bash
docker run -p 8080:8080 \
  -e PUBLIC_GATEWAY_URL=https://api.meridianconsole.com \
  dhadgar-panel
```

### Dockerfile Overview

The Dockerfile uses a multi-stage build:

1. **deps stage**: Install npm dependencies
2. **builder stage**: Build the Astro application
3. **runner stage**: Minimal production image

Key features:
- Based on `node:22-alpine` for small image size
- Non-root user for security
- Build arguments for compile-time environment variables
- Copies only production dependencies

### Kubernetes Deployment

The Panel is designed to run as a Kubernetes deployment:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: panel
spec:
  replicas: 2
  template:
    spec:
      containers:
      - name: panel
        image: meridianconsoleacr.azurecr.io/dhadgar/panel:latest
        ports:
        - containerPort: 8080
        env:
        - name: PUBLIC_GATEWAY_URL
          value: "https://api.meridianconsole.com"
        - name: NODE_ENV
          value: "production"
        resources:
          requests:
            memory: "128Mi"
            cpu: "100m"
          limits:
            memory: "256Mi"
            cpu: "500m"
        readinessProbe:
          httpGet:
            path: /
            port: 8080
          initialDelaySeconds: 5
        livenessProbe:
          httpGet:
            path: /
            port: 8080
          initialDelaySeconds: 10
```

### Azure Container Registry

Images are pushed to Azure Container Registry:

```bash
# Login to ACR
az acr login --name meridianconsoleacr

# Tag and push
docker tag dhadgar-panel meridianconsoleacr.azurecr.io/dhadgar/panel:latest
docker push meridianconsoleacr.azurecr.io/dhadgar/panel:latest
```

---

## Testing

### Test Project

Tests are located in `tests/Dhadgar.Panel.Tests/`.

```bash
# Run Panel tests
dotnet test tests/Dhadgar.Panel.Tests
```

### Unit Testing (Planned)

React component testing with Vitest:

```typescript
// Example test (not yet implemented)
import { render, screen } from '@testing-library/react';
import { GlowButton } from './GlowButton';

test('renders button text', () => {
  render(<GlowButton>Click me</GlowButton>);
  expect(screen.getByText('Click me')).toBeInTheDocument();
});
```

### E2E Testing (Planned)

End-to-end testing with Playwright:

```typescript
// Example test (not yet implemented)
import { test, expect } from '@playwright/test';

test('login flow', async ({ page }) => {
  await page.goto('/login');
  await page.click('[data-testid="oauth-google"]');
  // ... OAuth flow mocking
  await expect(page).toHaveURL('/dashboard');
});
```

---

## Planned Features

### Server Management

The server management interface will include:

- **Server List**: Filterable/sortable table of all servers
- **Server Details**: Status, resource usage, configuration
- **Server Actions**: Start, stop, restart, delete
- **Server Creation**: Wizard for provisioning new servers
- **Console Access**: Real-time log streaming via SignalR
- **File Manager**: Browse and edit server files
- **Mod Manager**: Install and configure mods

### Node Monitoring

Node monitoring will display:

- **Node List**: All connected agent nodes
- **Node Health**: CPU, memory, disk, network metrics
- **Node Details**: Specifications, location, capabilities
- **Capacity Planning**: Available vs used resources
- **Agent Status**: Version, last heartbeat, connection quality

### Organization Management

Organization features will include:

- **Member List**: View and manage team members
- **Invite Flow**: Invite users by email
- **Role Management**: Assign roles (owner, admin, operator, viewer)
- **Custom Roles**: Create roles with specific permissions
- **Activity Log**: Audit trail of organization actions
- **Settings**: Organization name, preferences, limits

### Billing Dashboard

The billing interface (SaaS edition) will show:

- **Subscription Status**: Current plan, renewal date
- **Usage Metrics**: Server hours, bandwidth, storage
- **Payment History**: Past invoices and receipts
- **Payment Methods**: Manage cards (Stripe integration)
- **Plan Upgrade**: Change subscription tier

### User Profile

User profile features:

- **Profile Settings**: Display name, avatar
- **Linked Accounts**: View/unlink OAuth providers
- **Active Sessions**: View and revoke sessions
- **Security**: Enable 2FA, passkeys (future)
- **Preferences**: Theme, notifications, timezone

---

## Troubleshooting

### Common Issues

#### "CORS error when calling API"

The Gateway must allow requests from the Panel origin. Check Gateway CORS configuration.

#### "Token refresh loop"

If you see continuous refresh attempts:
1. Check that Identity service is running
2. Verify `PUBLIC_GATEWAY_URL` is correct
3. Clear sessionStorage and re-login

#### "OAuth callback error"

Ensure the callback URL is registered with the OAuth provider and matches `/callback`.

#### "Build fails with shared-auth error"

The `@dhadgar/shared-auth` package is a local file reference. Ensure `src/Dhadgar.SharedAuth/` exists and has been built:

```bash
cd src/Dhadgar.SharedAuth
npm install
```

#### "Port already in use"

Change the port via environment variable or Astro config:

```bash
PORT=4322 npm run dev
```

### Debugging

Enable verbose logging by checking browser DevTools:

- **Network tab**: View API requests and responses
- **Console tab**: Auth state changes are logged
- **Application tab**: View sessionStorage tokens

---

## Related Documentation

### In This Repository

- [CLAUDE.md (root)](../../CLAUDE.md) - Main project documentation
- [Architecture Analysis](../../docs/architecture/README.md) - System architecture
- [Authentication Analysis](../../docs/architecture/authentication-analysis.md) - Auth decisions
- [Identity API Reference](../../docs/identity-api-reference.md) - Identity service API
- [Identity Claims Reference](../../docs/identity-claims-reference.md) - Permission system
- [Gateway Authentication](../../docs/gateway-authentication.md) - Gateway auth config

### Related Projects

- [Dhadgar.SharedAuth](../Dhadgar.SharedAuth/) - Shared auth client library
- [Dhadgar.Scope](../Dhadgar.Scope/) - Documentation site (same tech stack)
- [Dhadgar.ShoppingCart](../Dhadgar.ShoppingCart/) - Marketing/checkout site
- [Dhadgar.Gateway](../Dhadgar.Gateway/) - API gateway
- [Dhadgar.Identity](../Dhadgar.Identity/) - Identity service

### External Documentation

- [Astro Documentation](https://docs.astro.build/)
- [React Documentation](https://react.dev/)
- [Tailwind CSS Documentation](https://tailwindcss.com/docs)
- [BetterAuth Documentation](https://www.better-auth.com/docs)

---

## Contributing

When contributing to the Panel:

1. Follow the existing component patterns
2. Use TypeScript for type safety
3. Use Tailwind CSS for styling (no custom CSS unless necessary)
4. Add JSDoc comments for exported functions and components
5. Test components in isolation before integrating
6. Ensure accessibility (keyboard navigation, ARIA labels)

### Code Style

- Components: PascalCase (e.g., `GlowButton.tsx`)
- Utilities: camelCase (e.g., `apiClient.ts`)
- Files: kebab-case for pages (e.g., `server-details.astro`)
- CSS: Use Tailwind utilities; avoid `@apply` except in global.css

### Pull Request Checklist

- [ ] TypeScript compiles without errors
- [ ] `npm run build` succeeds
- [ ] Components render correctly
- [ ] Authentication flow works
- [ ] Responsive on mobile and desktop
- [ ] Dark theme looks correct

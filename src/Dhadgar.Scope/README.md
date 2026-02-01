# Dhadgar.Scope - Meridian Console Documentation Site

![Status: In Development](https://img.shields.io/badge/Status-In%20Development-yellow)

## Table of Contents

1. [Overview](#overview)
2. [Tech Stack](#tech-stack)
3. [Quick Start](#quick-start)
4. [Project Structure](#project-structure)
5. [Pages](#pages)
6. [Components](#components)
7. [Layouts](#layouts)
8. [Styling](#styling)
9. [Configuration](#configuration)
10. [Building](#building)
11. [Deployment](#deployment)
12. [Adding Content](#adding-content)
13. [Related Documentation](#related-documentation)

---

## Overview

**Dhadgar.Scope** is the public-facing documentation and architecture reference site for the Meridian Console platform. It serves as a "living, searchable architecture + delivery map" that consolidates project scope, system architecture, security posture, data models, and network flows into a single, navigable reference.

### Purpose

The Scope site addresses several critical needs:

1. **Single Source of Truth**: Eliminates scattered documentation by providing one authoritative reference for architecture decisions, service relationships, and project scope.

2. **Deep Linking**: Every section has a stable, shareable URL (e.g., `/s/architecture`, `/s/mvp`) enabling precise references in discussions, pull requests, and meetings.

3. **Interactive Visualizations**: Includes a Cytoscape.js-powered dependency graph, architecture tree views, database schema browsers, and communication matrices that let developers explore relationships visually.

4. **Fast Search**: The sidebar includes instant search functionality to filter sections in real-time, designed for quick lookups during meetings.

5. **Mobile-First Design**: Fully responsive with a mobile drawer navigation, proper touch targets, and optimized layouts for phones and tablets.

### What It Contains

The site is organized into 19 numbered sections covering:

- Project structure and team information
- Product vision and scope
- Technology stack and build strategy
- Deployment topology and data retention
- Security architecture and certificate management
- System architecture (interactive tree view)
- Database schemas (per-service browser)
- Network flows and service communication matrix
- RabbitMQ topology
- Services catalogue and agent architecture
- KiP Edition (self-hosted variant)
- MVP scope and delivery phases
- Product governance and repository structure

---

## Tech Stack

### Core Framework

| Technology       | Version | Purpose                                         |
| ---------------- | ------- | ----------------------------------------------- |
| **Astro**        | 5.1.1   | Static site generator with islands architecture |
| **React**        | 18.3.1  | UI components (hydrated as client-side islands) |
| **Tailwind CSS** | 3.4.16  | Utility-first CSS framework                     |
| **TypeScript**   | 5.7.2   | Type-safe JavaScript                            |

### Key Dependencies

| Package             | Version | Purpose                                        |
| ------------------- | ------- | ---------------------------------------------- |
| `@astrojs/react`    | 4.1.1   | Astro integration for React components         |
| `@astrojs/tailwind` | 6.0.1   | Astro integration for Tailwind CSS             |
| `clsx`              | 2.1.1   | Utility for conditional CSS class names        |
| `cytoscape`         | 3.30.4  | Graph visualization library for dependency map |

### Development Dependencies

| Package                     | Version | Purpose                             |
| --------------------------- | ------- | ----------------------------------- |
| `eslint`                    | 9.39.2  | JavaScript/TypeScript linting       |
| `eslint-plugin-astro`       | 1.5.0   | ESLint rules for Astro files        |
| `eslint-plugin-react`       | 7.37.5  | ESLint rules for React              |
| `eslint-plugin-react-hooks` | 7.0.1   | ESLint rules for React hooks        |
| `eslint-plugin-jsx-a11y`    | 6.10.2  | Accessibility linting               |
| `eslint-plugin-security`    | 3.0.1   | Security-focused linting            |
| `prettier`                  | 3.7.4   | Code formatting                     |
| `prettier-plugin-astro`     | 0.14.1  | Prettier support for `.astro` files |

### Why These Technologies?

**Astro** was chosen for its "islands architecture" which allows shipping zero JavaScript by default while selectively hydrating interactive components. This results in:

- Extremely fast initial page loads (mostly static HTML)
- SEO-friendly pre-rendered content
- Interactive React components only where needed (`client:load` directive)

**React** provides the component model for complex interactive features like:

- The Cytoscape-powered dependency graph
- Tree view components for architecture and database schemas
- Mobile drawer navigation with scroll locking

**Tailwind CSS** enables rapid UI development with:

- Dark mode support (`darkMode: 'class'`)
- Consistent design tokens
- Responsive utilities for mobile-first design
- Custom color extensions (e.g., `slate-950`)

---

## Quick Start

### Prerequisites

- **Node.js 20+**: Required for Astro and npm commands
- **npm**: Package manager (comes with Node.js)

### Installation

```bash
# Navigate to the Scope project
cd src/Dhadgar.Scope

# Install dependencies
npm install
```

### Development Server

```bash
# Start the dev server with hot reload
npm run dev
```

The development server runs on **<http://localhost:4321>** by default. Changes to source files trigger instant hot module replacement (HMR).

### Available npm Scripts

| Script         | Command                                              | Description                       |
| -------------- | ---------------------------------------------------- | --------------------------------- |
| `dev`          | `astro dev`                                          | Start development server with HMR |
| `build`        | `astro build`                                        | Build production static site      |
| `preview`      | `astro preview`                                      | Preview production build locally  |
| `lint`         | `eslint . --ext .ts,.tsx,.astro`                     | Run ESLint on all source files    |
| `lint:fix`     | `eslint . --ext .ts,.tsx,.astro --fix`               | Auto-fix linting issues           |
| `format`       | `prettier --write "**/*.{ts,tsx,astro,css,md,json}"` | Format all files with Prettier    |
| `format:check` | `prettier --check "**/*.{ts,tsx,astro,css,md,json}"` | Check formatting without changes  |

### Integration with .NET Solution

The project includes a `Dhadgar.Scope.csproj` file that integrates with the main .NET solution. This is a "shim" project using `Microsoft.Build.NoTargets` SDK that:

1. Runs `npm install` when `package.json` changes
2. Runs `npm run build` during the .NET build process
3. Allows `dotnet build` to build the entire solution including this Node.js project

```bash
# Build via .NET (from solution root)
dotnet build src/Dhadgar.Scope

# Build via npm directly
cd src/Dhadgar.Scope && npm run build
```

---

## Project Structure

```
src/Dhadgar.Scope/
├── .astro/                      # Astro build cache (gitignored)
├── _swa_publish/                # Azure Static Web Apps output
│   └── wwwroot/                 # Production build output
├── node_modules/                # npm dependencies (gitignored)
├── public/                      # Static assets (copied as-is)
│   ├── content/                 # JSON data files for visualizations
│   │   ├── architecture-park.v1.json
│   │   ├── comm-matrix.v1.json
│   │   ├── db-schemas.v1.json
│   │   ├── dependencies.json
│   │   └── sections.json
│   ├── images/
│   │   └── meridian-console.png
│   └── staticwebapp.config.json # Azure SWA configuration
├── src/
│   ├── components/              # React components
│   │   ├── layout/              # Layout components
│   │   │   ├── MainLayout.tsx
│   │   │   ├── MobileDrawer.tsx
│   │   │   ├── SectionNav.tsx
│   │   │   └── Sidebar.tsx
│   │   ├── ui/                  # Reusable UI components
│   │   │   ├── Alert.tsx
│   │   │   ├── Button.tsx
│   │   │   ├── Chip.tsx
│   │   │   └── TextField.tsx
│   │   └── visualizations/      # Data visualization components
│   │       ├── ArchitectureTreeView.tsx
│   │       ├── CommMatrixMobile.tsx
│   │       ├── DbSchemaTreeView.tsx
│   │       ├── DependencyDetailsPanel.tsx
│   │       └── DependencyGraph.tsx
│   ├── layouts/
│   │   └── BaseLayout.astro     # Root HTML layout
│   ├── lib/                     # TypeScript utilities
│   │   ├── data.ts              # Data fetching functions
│   │   ├── sections-registry.ts # Section metadata registry
│   │   └── types.ts             # TypeScript type definitions
│   ├── pages/                   # Astro pages (file-based routing)
│   │   ├── 404.astro            # Custom 404 page
│   │   ├── dependencies.astro   # Interactive dependency map
│   │   ├── index.astro          # Homepage
│   │   └── s/
│   │       └── [slug].astro     # Dynamic section pages
│   ├── sections/                # Section content components
│   │   ├── Agents.astro
│   │   ├── Architecture.astro
│   │   ├── BuildStrategy.astro
│   │   ├── Certificates.astro
│   │   ├── DatabaseSchemas.astro
│   │   ├── DataRetention.astro
│   │   ├── Deployment.astro
│   │   ├── Flows.astro
│   │   ├── Governance.astro
│   │   ├── Kip.astro
│   │   ├── Matrix.astro
│   │   ├── Mvp.astro
│   │   ├── ProjectStructure.astro
│   │   ├── Rabbitmq.astro
│   │   ├── Repos.astro
│   │   ├── Security.astro
│   │   ├── Services.astro
│   │   ├── TechStack.astro
│   │   └── Vision.astro
│   └── styles/
│       └── global.css           # Global styles and Tailwind imports
├── .env.production              # Production environment variables
├── .gitignore
├── .prettierrc                  # Prettier configuration
├── astro.config.mjs             # Astro configuration
├── CLAUDE.md                    # Claude Code instructions
├── Dhadgar.Scope.csproj         # .NET shim project
├── eslint.config.js             # ESLint flat config
├── package.json
├── package-lock.json
├── tailwind.config.mjs          # Tailwind CSS configuration
└── tsconfig.json                # TypeScript configuration
```

### Key Directories Explained

**`public/content/`**: Contains JSON files that power the interactive visualizations. These files are fetched at runtime by React components:

- `dependencies.json` - Node and edge data for the dependency graph
- `architecture-park.v1.json` - District and node data for architecture tree view
- `db-schemas.v1.json` - Per-service database schema information
- `comm-matrix.v1.json` - Service-to-service communication protocols

**`src/sections/`**: Contains the actual documentation content as Astro components. Each section is a standalone `.astro` file that can include static HTML, Tailwind classes, and embedded React components.

**`src/components/visualizations/`**: Complex React components that provide interactive data exploration. These are hydrated client-side using Astro's `client:load` directive.

---

## Pages

### Homepage (`/`)

**File**: `src/pages/index.astro`

The landing page that introduces users to the Scope Navigator. Contains:

- Hero section with branding and quick description
- "Start where you are" section with navigation shortcuts
- Product overview explaining what Meridian Console is
- Service architecture overview (11 microservices listed)
- Key capabilities cards
- Quick search explanation

### Dependency Map (`/dependencies`)

**File**: `src/pages/dependencies.astro`

A full-page interactive dependency graph powered by Cytoscape.js. Features:

- Draggable nodes organized by architectural layer
- Click-to-select with detailed information panel
- Search filtering to highlight specific services
- Zoom, pan, fit, and reset controls
- Responsive design with mobile tabbed interface

### Section Pages (`/s/[slug]`)

**File**: `src/pages/s/[slug].astro`

Dynamic route that renders individual documentation sections. Uses Astro's `getStaticPaths()` to generate static pages for all 19 sections at build time.

Features:

- Breadcrumb navigation
- Progress bar showing current position
- Previous/Next navigation
- Section content rendered from `src/sections/` components

### 404 Page (`/404`)

**File**: `src/pages/404.astro`

Custom not-found page with:

- Friendly error message
- Navigation back to home
- Link to dependency map

### All Sections

| #   | Section                      | Slug                | Description                                     |
| --- | ---------------------------- | ------------------- | ----------------------------------------------- |
| 1   | Project Structure            | `project-structure` | Product name, repository, team, asset ownership |
| 2   | Vision & Scope               | `vision`            | Product overview, target users, differentiators |
| 3   | Build Strategy               | `build-strategy`    | Development approach and tooling                |
| 4   | Tech Stack                   | `tech-stack`        | Technologies used across the platform           |
| 5   | Deployment Topology          | `deployment`        | Infrastructure and deployment architecture      |
| 6   | Data Retention               | `data-retention`    | Data lifecycle and retention policies           |
| 7   | Security Architecture        | `security`          | Security design and principles                  |
| 8   | Certificate Management       | `certificates`      | TLS/mTLS certificate handling                   |
| 9   | System Architecture          | `architecture`      | Interactive architecture tree view              |
| 10  | Database Schemas             | `database-schemas`  | Interactive per-service schema browser          |
| 11  | Network Flows                | `flows`             | Data flow patterns                              |
| 12  | Service Communication Matrix | `matrix`            | Interactive service-to-service protocols        |
| 13  | RabbitMQ Topology            | `rabbitmq`          | Message broker exchange/queue design            |
| 14  | Services Catalogue           | `services`          | Detailed service descriptions                   |
| 15  | Agent Architecture           | `agents`            | Distributed agent design                        |
| 16  | KiP Edition                  | `kip`               | Self-hosted variant details                     |
| 17  | MVP Scope & Phases           | `mvp`               | Delivery phases and milestones                  |
| 18  | Product Governance           | `governance`        | Decision-making and processes                   |
| 19  | Repository Structure         | `repos`             | Codebase organization                           |

---

## Components

### Layout Components

#### `MainLayout.tsx`

**Location**: `src/components/layout/MainLayout.tsx`

The primary layout wrapper for all pages. Provides:

- Responsive flex layout (sidebar + content)
- Mobile top bar with hamburger menu button
- Desktop sidebar (hidden on mobile)
- Mobile drawer integration
- Scroll-locked body when drawer is open

**Usage**:

```astro
<MainLayout client:load>
  <!-- Page content here -->
</MainLayout>
```

The `client:load` directive hydrates this component immediately on page load, enabling the mobile menu state management.

#### `Sidebar.tsx`

**Location**: `src/components/layout/Sidebar.tsx`

The navigation sidebar containing:

- Brand header ("Meridian Console / Scope")
- Dependency Map link
- Search input with real-time filtering
- Numbered section list (filterable)
- Hosting note footer

**Props**:

```typescript
interface SidebarProps {
  onNavigate?: () => void; // Called when a link is clicked (for mobile drawer)
}
```

The search filter uses `useMemo` for efficient filtering and matches against both section titles and slugs.

#### `SectionNav.tsx`

**Location**: `src/components/layout/SectionNav.tsx`

Navigation header shown on section pages. Features:

- Breadcrumb trail (Home / Section Name)
- Visual progress bar showing position (e.g., "Section 5 of 19, 26% complete")
- Previous/Next navigation buttons
- Responsive title display (full title on desktop, "Previous/Next" on mobile)

**Props**:

```typescript
interface SectionNavProps {
  section: ScopeSectionInfo; // Current section
  prevSection: ScopeSectionInfo | null;
  nextSection: ScopeSectionInfo | null;
  total: number; // Total section count
}
```

#### `MobileDrawer.tsx`

**Location**: `src/components/layout/MobileDrawer.tsx`

Full-screen mobile navigation drawer. Features:

- Backdrop with blur effect
- Slide-in drawer panel
- Scroll lock on body when open
- Escape key to close
- Click-outside to close
- Close button in header
- Contains `Sidebar` component

**Props**:

```typescript
interface MobileDrawerProps {
  isOpen: boolean;
  onClose: () => void;
}
```

Uses CSS class `scroll-locked` defined in `global.css` to prevent background scrolling.

---

### UI Components

#### `Alert.tsx`

**Location**: `src/components/ui/Alert.tsx`

Contextual alert/callout component with icon and colored border.

**Props**:

```typescript
interface AlertProps {
  severity?: "info" | "warning" | "error" | "success";
  dense?: boolean; // Smaller padding
  className?: string;
  children: ReactNode;
}
```

**Color mapping**:

- `info`: Blue border/background
- `warning`: Amber border/background
- `error`: Red border/background
- `success`: Green border/background

Each severity has a corresponding SVG icon.

#### `Button.tsx`

**Location**: `src/components/ui/Button.tsx`

Versatile button component supporting links and buttons.

**Props**:

```typescript
interface ButtonProps {
  variant?: "filled" | "outlined" | "text";
  size?: "small" | "medium";
  color?: "primary" | "default";
  disabled?: boolean;
  href?: string; // Renders as <a> if provided
  onClick?: MouseEventHandler<HTMLButtonElement>;
  className?: string;
  children: ReactNode;
}
```

Automatically renders as `<a>` when `href` is provided, otherwise `<button>`.

#### `Chip.tsx`

**Location**: `src/components/ui/Chip.tsx`

Small label/tag component for displaying metadata.

**Props**:

```typescript
interface ChipProps {
  size?: "small" | "medium";
  className?: string;
  children: ReactNode;
}
```

Used in the dependency graph for hints like "Scroll / pinch to zoom".

#### `TextField.tsx`

**Location**: `src/components/ui/TextField.tsx`

Styled text input with optional icon and clear button.

**Props**:

```typescript
interface TextFieldProps {
  value: string;
  onChange: ChangeEventHandler<HTMLInputElement>;
  placeholder?: string;
  label?: string;
  clearable?: boolean; // Shows X button when has value
  onClear?: () => void;
  className?: string;
  icon?: ReactNode; // Leading icon (e.g., search icon)
}
```

Used in the sidebar search and dependency graph search.

---

### Visualization Components

#### `DependencyGraph.tsx`

**Location**: `src/components/visualizations/DependencyGraph.tsx`

The most complex component - an interactive dependency graph using Cytoscape.js.

**Features**:

- Loads data from `/content/dependencies.json`
- Layer-based node positioning (external, presentation, core, business, foundation)
- Color-coded nodes by architectural layer
- Click to select nodes and view details
- Search to filter/highlight nodes
- Zoom, pan, fit controls
- Mobile tabbed interface (Graph/Details tabs)
- Tablet bottom drawer for details
- Desktop side panel for details

**Architecture**:

- Uses `useRef` for Cytoscape instance management
- Stores initial positions for reset functionality
- Event handlers for node tap, background tap, resize
- CSS classes for selection (`dh-selected`) and dimming (`dh-dim`)

**Layer colors**:

```typescript
const LAYER_COLORS: Record<string, string> = {
  external: "#0F766E", // Teal
  presentation: "#4338CA", // Indigo
  core: "#6D28D9", // Violet
  business: "#1D4ED8", // Blue
  foundation: "#B45309", // Amber
};
```

#### `DependencyDetailsPanel.tsx`

**Location**: `src/components/visualizations/DependencyDetailsPanel.tsx`

Details panel shown when a node is selected in the dependency graph.

**Displays**:

- Node name with emoji
- Layer indicator with color dot
- Port number (if applicable)
- Description
- Responsibilities list
- Endpoints list
- Dependencies (clickable to navigate)
- Dependents (clickable to navigate)

**Props**:

```typescript
interface DependencyDetailsPanelProps {
  selected: DependencyNode | null;
  onSelectByName: (name: string) => void;
  onClear: () => void;
  className?: string;
}
```

#### `ArchitectureTreeView.tsx`

**Location**: `src/components/visualizations/ArchitectureTreeView.tsx`

Collapsible tree view for exploring system architecture by district.

**Features**:

- Loads data from `/content/architecture-park.v1.json`
- Groups nodes by district (collapsible sections)
- Filter input for searching nodes
- Click to select and view details panel
- Details include: emoji, name, kind, description, responsibilities, ports

**Node kinds** (with colors):

- service (indigo)
- agent (purple)
- db (amber)
- foundation (teal)
- external (gray)
- client (blue)

#### `DbSchemaTreeView.tsx`

**Location**: `src/components/visualizations/DbSchemaTreeView.tsx`

Per-service database schema browser.

**Features**:

- Loads data from `/content/db-schemas.v1.json`
- Service selector dropdown
- Groups items by kind: Tables, Views, Functions, Enums, Types
- Collapsible kind sections
- Click to view item details (columns for tables, etc.)
- Notes section at bottom

#### `CommMatrixMobile.tsx`

**Location**: `src/components/visualizations/CommMatrixMobile.tsx`

Mobile-friendly service communication matrix.

**Features**:

- Loads data from `/content/comm-matrix.v1.json`
- Source service selector dropdown
- Protocol filter buttons (All, HTTP, WSS, AMQP, DB, DNS, OTHER)
- Lists outbound connections with protocol badges
- Connection count summary

**Protocol colors**:

- HTTP: Blue
- WSS: Purple
- AMQP: Amber
- DB: Teal
- DNS: Gray
- OTHER: Pink

---

## Layouts

### BaseLayout.astro

**Location**: `src/layouts/BaseLayout.astro`

The root HTML layout that wraps all pages. Provides:

**Head section**:

- Character encoding (UTF-8)
- Responsive viewport meta tag
- SEO description meta tag
- Favicon link (`/images/favicon.png`)
- Dynamic page title (`{title} | Meridian Console Scope`)
- Google Fonts preconnect and Roboto font loading
- Cytoscape.js CDN script (for graph visualizations)

**Body**:

- Dark gradient background (`from-slate-950 via-slate-950 to-black`)
- Base text color (white)
- Roboto font family
- Antialiased text rendering
- `<slot />` for page content

**Props**:

```typescript
interface Props {
  title: string;
  description?: string; // Defaults to "Meridian Console Scope Navigator..."
}
```

**Global styles import**:

```astro
<style is:global>
  @import "../styles/global.css";
</style>
```

---

## Styling

### Tailwind CSS Configuration

**File**: `tailwind.config.mjs`

```javascript
export default {
  content: ["./src/**/*.{astro,html,js,jsx,md,mdx,svelte,ts,tsx,vue}"],
  darkMode: "class",
  theme: {
    extend: {
      colors: {
        slate: {
          950: "#020617", // Custom dark shade
        },
      },
      fontFamily: {
        sans: ["Roboto", "system-ui", "sans-serif"],
      },
    },
  },
  plugins: [],
};
```

**Key customizations**:

- `darkMode: 'class'`: Dark mode activated by `.dark` class on `<html>`
- Custom `slate-950` color for very dark backgrounds
- Roboto as the primary sans-serif font

### Global CSS

**File**: `src/styles/global.css`

The global stylesheet includes:

1. **Tailwind directives**:

   ```css
   @tailwind base;
   @tailwind components;
   @tailwind utilities;
   ```

2. **Dark mode color scheme**:

   ```css
   :root {
     color-scheme: dark;
   }
   ```

3. **Native form styling** (prevents bright white dropdowns):

   ```css
   select,
   option {
     background-color: rgb(15 23 42); /* slate-900 */
     color: rgb(226 232 240); /* slate-200 */
   }
   ```

4. **Scope prose classes** (for section content):
   - `.scope-prose` - Base typography for documentation content
   - `.scope-code` - Code block styling
   - `.scope-table` - Table styling with borders
   - `.scope-panel` - Card/panel styling
   - `.scope-panel-alt` - Alternate panel background
   - `.scope-callout` - Highlighted callout boxes (indigo tint)

5. **Mobile ergonomics**:
   - Long URL/code word breaking
   - Horizontal scrolling tables on mobile
   - iOS safe area padding
   - Touch highlight removal

6. **Scroll lock utility**:
   ```css
   body.scroll-locked {
     position: fixed;
     overflow: hidden;
     width: 100%;
   }
   ```

### Design Patterns

**Color palette** (dark theme):

- Background: `slate-950` gradient to black
- Cards/panels: `white/5` (5% white opacity)
- Borders: `white/10` (10% white opacity)
- Text primary: `white` or `white/85`
- Text secondary: `white/70` or `white/60`
- Accent: `indigo-500` (buttons, links, callouts)

**Border radius**: Consistently uses `rounded-xl` (0.75rem) or `rounded-2xl` (1rem) for modern, soft appearance.

**Spacing**: Uses Tailwind's spacing scale, commonly `p-4` to `p-6` for padding.

---

## Configuration

### Astro Configuration

**File**: `astro.config.mjs`

```javascript
import { defineConfig } from "astro/config";
import react from "@astrojs/react";
import tailwind from "@astrojs/tailwind";

export default defineConfig({
  integrations: [react(), tailwind()],
  output: "static",
  build: {
    assets: "_assets",
  },
  outDir: "_swa_publish/wwwroot",
});
```

**Configuration details**:

- `integrations`: Enables React components and Tailwind CSS
- `output: 'static'`: Generates static HTML (no SSR server needed)
- `build.assets`: Places bundled JS/CSS in `/_assets/` directory
- `outDir`: Build output goes to `_swa_publish/wwwroot` for Azure SWA

### TypeScript Configuration

**File**: `tsconfig.json`

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
      "@layouts/*": ["src/layouts/*"],
      "@lib/*": ["src/lib/*"]
    }
  }
}
```

**Path aliases** allow cleaner imports:

```typescript
import { Button } from "@components/ui/Button";
import { sections } from "@lib/sections-registry";
```

### ESLint Configuration

**File**: `eslint.config.js`

Uses ESLint flat config format with:

- JavaScript recommended rules
- Astro plugin (for `.astro` files)
- TypeScript plugin
- React and React Hooks plugins
- JSX-A11Y accessibility rules
- Security plugin

**Key rules**:

- `react/react-in-jsx-scope: off` (not needed with new JSX transform)
- `@typescript-eslint/no-unused-vars` ignores variables prefixed with `_`

**Ignored paths**: `node_modules/**`, `dist/**`, `_swa_publish/**`, `.astro/**`

### Prettier Configuration

**File**: `.prettierrc`

```json
{
  "semi": true,
  "trailingComma": "es5",
  "singleQuote": false,
  "printWidth": 100,
  "tabWidth": 2,
  "useTabs": false,
  "endOfLine": "lf",
  "plugins": ["prettier-plugin-astro"],
  "overrides": [
    {
      "files": "*.astro",
      "options": {
        "parser": "astro"
      }
    }
  ]
}
```

### Environment Variables

**File**: `.env.production`

```
PUBLIC_GATEWAY_URL=https://dev.meridianconsole.com
```

Environment variables prefixed with `PUBLIC_` are available in client-side code. This URL points to the Gateway API endpoint.

**Local development**: Create `.env.local` (gitignored) to override values locally.

---

## Building

### Production Build

```bash
npm run build
```

This command:

1. Runs Astro's build process
2. Pre-renders all static pages
3. Bundles React components
4. Processes Tailwind CSS
5. Outputs to `_swa_publish/wwwroot/`

### Build Output Structure

```
_swa_publish/wwwroot/
├── _assets/                # Bundled JS and CSS
│   ├── [hash].js
│   └── [hash].css
├── content/                # Copied from public/content
│   ├── architecture-park.v1.json
│   ├── comm-matrix.v1.json
│   ├── db-schemas.v1.json
│   ├── dependencies.json
│   └── sections.json
├── images/                 # Copied from public/images
│   └── meridian-console.png
├── s/                      # Section pages
│   ├── project-structure/
│   │   └── index.html
│   ├── vision/
│   │   └── index.html
│   └── ... (all 19 sections)
├── 404.html
├── dependencies/
│   └── index.html
├── index.html
└── staticwebapp.config.json
```

### Preview Production Build

```bash
npm run preview
```

Starts a local server to preview the production build before deployment.

### Build via .NET

The project can also be built through the .NET solution:

```bash
# From solution root
dotnet build src/Dhadgar.Scope

# Or build entire solution
dotnet build
```

The `Dhadgar.Scope.csproj` file contains MSBuild targets that:

1. Run `npm install` when package files change
2. Run `npm run build` after install
3. Output build completion message

---

## Deployment

### Azure Static Web Apps

Dhadgar.Scope deploys to **Azure Static Web Apps** (Free tier). The deployment is automated via Azure Pipelines.

### Pipeline Configuration

In `azure-pipelines.yml`, the Scope service is configured as:

```yaml
- id: Dhadgar.Scope
  projectPath: src/Dhadgar.Scope/Dhadgar.Scope.csproj
  testProjectPath: tests/Dhadgar.Scope.Tests/Dhadgar.Scope.Tests.csproj
  deploy: swa
  swa:
    apiTokenVar: Dhadgar_Swa_ApiToken
    appLocation: src/Dhadgar.Scope
    outputLocation: _swa_publish/wwwroot
    appBuildCommand: npm install && npm run build
```

The pipeline:

1. Runs `npm install && npm run build` in `src/Dhadgar.Scope`
2. Deploys contents of `_swa_publish/wwwroot` to Azure SWA
3. Uses the `Dhadgar_Swa_ApiToken` pipeline variable for authentication

### Static Web App Configuration

**File**: `public/staticwebapp.config.json`

```json
{
  "navigationFallback": {
    "rewrite": "/index.html",
    "exclude": [
      "/images/*",
      "/content/*",
      "/_assets/*",
      "/*.png",
      "/*.ico",
      "/*.json",
      "/*.js",
      "/*.css"
    ]
  },
  "mimeTypes": {
    ".json": "application/json"
  },
  "globalHeaders": {
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
    "Referrer-Policy": "strict-origin-when-cross-origin"
  }
}
```

**Configuration explained**:

- **navigationFallback**: Enables client-side routing by redirecting unknown paths to `index.html`, except for static assets
- **mimeTypes**: Ensures JSON files are served with correct content type
- **globalHeaders**: Security headers applied to all responses:
  - `X-Content-Type-Options: nosniff` - Prevents MIME type sniffing
  - `X-Frame-Options: DENY` - Prevents embedding in iframes
  - `Referrer-Policy: strict-origin-when-cross-origin` - Controls referrer information

### Manual Deployment

To deploy manually using the Azure SWA CLI:

```bash
# Install SWA CLI
npm install -g @azure/static-web-apps-cli

# Build the project
npm run build

# Deploy (requires authentication)
swa deploy _swa_publish/wwwroot --deployment-token <token>
```

---

## Adding Content

### Adding a New Section

1. **Create the section component** in `src/sections/`:

```astro
---
// src/sections/NewSection.astro
// Section X: New Section Title
---

<div class="rounded-2xl border border-white/10 bg-white/5 p-6">
  <h2 class="mb-4 text-2xl font-bold">Section X: New Section Title</h2>

  <p class="mb-6 text-white/70">Section description here.</p>

  <!-- Add content using scope-* CSS classes -->
  <div class="scope-panel">
    <p>Panel content</p>
  </div>

  <table class="scope-table">
    <thead>
      <tr>
        <th>Column 1</th>
        <th>Column 2</th>
      </tr>
    </thead>
    <tbody>
      <tr>
        <td>Data 1</td>
        <td>Data 2</td>
      </tr>
    </tbody>
  </table>

  <div class="scope-callout">
    <strong>Note:</strong> Important callout text.
  </div>
</div>
```

2. **Register the section** in `src/lib/sections-registry.ts`:

```typescript
export const sections: ScopeSectionInfo[] = [
  // ... existing sections
  { number: 20, title: "New Section Title", slug: "new-section" },
];
```

3. **Import and map the component** in `src/pages/s/[slug].astro`:

```astro
---
// Add import
import NewSection from "../../sections/NewSection.astro";

// Add to sectionComponents map
const sectionComponents: Record<string, any> = {
  // ... existing mappings
  "new-section": NewSection,
};
---
```

4. **Update JSON data** if the section includes visualizations (update relevant files in `public/content/`).

### Adding Interactive Visualizations to a Section

To embed a React visualization component:

```astro
---
// Import the React component
import { ArchitectureTreeView } from "../components/visualizations/ArchitectureTreeView";
---

<div class="rounded-2xl border border-white/10 bg-white/5 p-6">
  <h2 class="mb-4 text-2xl font-bold">Section Title</h2>

  <p class="mb-6 text-white/70">Description text.</p>

  <!-- Hydrate the React component client-side -->
  <ArchitectureTreeView client:load />
</div>
```

The `client:load` directive tells Astro to hydrate this component immediately when the page loads.

### Adding New Data Files

1. Create the JSON file in `public/content/`:

```json
// public/content/new-data.v1.json
{
  "version": "1.0",
  "items": [...]
}
```

2. Add TypeScript types in `src/lib/types.ts`:

```typescript
export interface NewDataItem {
  id: string;
  name: string;
  // ... other fields
}

export interface NewData {
  version: string;
  items: NewDataItem[];
}
```

3. Add a data fetching function in `src/lib/data.ts`:

```typescript
export async function getNewData(): Promise<NewData> {
  const res = await fetch("/content/new-data.v1.json");
  return res.json();
}
```

4. Create or update a visualization component to use the data.

### CSS Classes for Content

Use these pre-defined classes for consistent styling:

| Class              | Purpose                                |
| ------------------ | -------------------------------------- |
| `.scope-prose`     | Base typography for section content    |
| `.scope-code`      | Code blocks with dark background       |
| `.scope-table`     | Tables with borders and header styling |
| `.scope-panel`     | Card/panel with border and background  |
| `.scope-panel-alt` | Panel with slightly lighter background |
| `.scope-callout`   | Highlighted callout (indigo accent)    |

---

## Related Documentation

### Project Documentation

- **Repository Root CLAUDE.md**: `/CLAUDE.md` - Main project documentation and conventions
- **Deploy Compose README**: `/deploy/compose/README.md` - Local Docker infrastructure
- **Container Build Setup**: `/deploy/kubernetes/CONTAINER-BUILD-SETUP.md` - CI/CD container builds

### Technology References

- [Astro Documentation](https://docs.astro.build/)
- [React Documentation](https://react.dev/)
- [Tailwind CSS Documentation](https://tailwindcss.com/docs)
- [Cytoscape.js Documentation](https://js.cytoscape.org/)
- [Azure Static Web Apps Documentation](https://learn.microsoft.com/en-us/azure/static-web-apps/)

### Related Projects in the Solution

| Project                | Description                                                   |
| ---------------------- | ------------------------------------------------------------- |
| `Dhadgar.Panel`        | Main control plane UI (Blazor - pending migration to Astro)   |
| `Dhadgar.ShoppingCart` | Marketing/checkout site (Blazor - pending migration to Astro) |
| `Dhadgar.Gateway`      | API Gateway (YARP reverse proxy)                              |

### Test Project

- **Location**: `tests/Dhadgar.Scope.Tests/`
- **Framework**: Jest or Vitest (Node.js testing)
- **Purpose**: Unit and integration tests for the Scope frontend

---

## Troubleshooting

### Common Issues

**Cytoscape graph not rendering**:

- Check browser DevTools console for errors
- The Cytoscape library is loaded via CDN - may be blocked by corporate networks
- Verify `dependencies.json` is accessible at `/content/dependencies.json`

**Styles not applying**:

- Run `npm run build` to regenerate Tailwind CSS
- Check that the file is included in `tailwind.config.mjs` content array
- Verify the `global.css` import in `BaseLayout.astro`

**404 errors on direct URL access (production)**:

- Verify `staticwebapp.config.json` has correct `navigationFallback` settings
- Ensure the file is copied to build output (should be in `public/`)

**Build fails with TypeScript errors**:

- Run `npm run lint` to identify issues
- Check that imports use correct path aliases
- Verify type definitions in `src/lib/types.ts`

### Development Tips

1. **Use the search filter** in the sidebar to quickly test section rendering
2. **Open DevTools Network tab** to debug JSON data fetching issues
3. **Check Astro build output** for page generation statistics
4. **Use `npm run preview`** to test production build locally before deployment

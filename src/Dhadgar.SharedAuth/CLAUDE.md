# SharedAuth Library

Shared authentication client and OAuth provider configuration.

## Tech Stack
- TypeScript
- npm package (local)

## Contents
- `createAuthClient()` - Auth client factory
- `tokenStorage` - Token management
- `OAUTH_PROVIDERS` - Provider metadata (colors, icons)
- `SIGN_IN_CATEGORIES` - UI category configuration

## Usage
```typescript
import { createAuthClient, OAUTH_PROVIDERS } from '@dhadgar/shared-auth';
```

## Notes
- Consumed by ShoppingCart and other frontend apps
- Provider list must match backend BetterAuth configuration

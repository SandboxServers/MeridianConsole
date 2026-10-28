export { authClient } from './client';
export { apiClient, api } from './api';

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

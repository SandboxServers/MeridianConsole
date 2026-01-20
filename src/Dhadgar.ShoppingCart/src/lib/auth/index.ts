export { authClient } from './client';
export { apiClient, api } from './api';

// Re-export from shared auth package
export { tokenStorage, OAUTH_PROVIDERS } from '@dhadgar/shared-auth';
export type {
  User,
  Session,
  AuthTokens,
  AuthState,
  OAuthProvider,
  SignInOptions,
  AuthSession,
} from '@dhadgar/shared-auth';

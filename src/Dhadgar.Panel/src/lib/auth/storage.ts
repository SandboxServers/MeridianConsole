import type { AuthTokens } from './types';

const ACCESS_TOKEN_KEY = 'meridian_access_token';
const REFRESH_TOKEN_KEY = 'meridian_refresh_token';
const EXPIRES_AT_KEY = 'meridian_expires_at';

function isBrowser(): boolean {
  return typeof window !== 'undefined' && typeof sessionStorage !== 'undefined';
}

export const tokenStorage = {
  getAccessToken(): string | null {
    if (!isBrowser()) return null;
    return sessionStorage.getItem(ACCESS_TOKEN_KEY);
  },

  getRefreshToken(): string | null {
    if (!isBrowser()) return null;
    return sessionStorage.getItem(REFRESH_TOKEN_KEY);
  },

  getExpiresAt(): number | null {
    if (!isBrowser()) return null;
    const value = sessionStorage.getItem(EXPIRES_AT_KEY);
    return value ? parseInt(value, 10) : null;
  },

  getTokens(): AuthTokens | null {
    const accessToken = this.getAccessToken();
    const refreshToken = this.getRefreshToken();
    const expiresAt = this.getExpiresAt();

    if (!accessToken || !refreshToken || !expiresAt) {
      return null;
    }

    return { accessToken, refreshToken, expiresAt };
  },

  setTokens(tokens: AuthTokens): void {
    if (!isBrowser()) return;
    sessionStorage.setItem(ACCESS_TOKEN_KEY, tokens.accessToken);
    sessionStorage.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken);
    sessionStorage.setItem(EXPIRES_AT_KEY, tokens.expiresAt.toString());
  },

  clear(): void {
    if (!isBrowser()) return;
    sessionStorage.removeItem(ACCESS_TOKEN_KEY);
    sessionStorage.removeItem(REFRESH_TOKEN_KEY);
    sessionStorage.removeItem(EXPIRES_AT_KEY);
  },

  isExpired(): boolean {
    const expiresAt = this.getExpiresAt();
    if (!expiresAt) return true;
    // Consider expired if less than 60 seconds remaining
    return Date.now() >= (expiresAt - 60) * 1000;
  },

  isExpiringSoon(): boolean {
    const expiresAt = this.getExpiresAt();
    if (!expiresAt) return true;
    // Consider expiring soon if less than 5 minutes remaining
    return Date.now() >= (expiresAt - 300) * 1000;
  },
};

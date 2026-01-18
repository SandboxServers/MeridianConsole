import { tokenStorage } from './storage';
import type { AuthTokens, User } from './types';

const GATEWAY_URL = import.meta.env.PUBLIC_GATEWAY_URL || 'http://localhost:5000';
const BETTERAUTH_PATH = '/api/v1/betterauth';
const IDENTITY_PATH = '/api/v1/identity';

export interface SignInOptions {
  provider: string;
  callbackURL?: string;
}

export interface AuthSession {
  user: User;
  session: {
    id: string;
    expiresAt: Date;
  };
}

async function fetchWithCredentials(url: string, options: RequestInit = {}): Promise<Response> {
  return fetch(url, {
    ...options,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...options.headers,
    },
  });
}

export const authClient = {
  /**
   * Initiate OAuth sign-in flow
   * Better Auth requires POST to /sign-in/social which returns a redirect URL
   */
  async signIn(options: SignInOptions): Promise<void> {
    const { provider, callbackURL = '/callback' } = options;

    try {
      const response = await fetchWithCredentials(
        `${GATEWAY_URL}${BETTERAUTH_PATH}/sign-in/social`,
        {
          method: 'POST',
          body: JSON.stringify({ provider, callbackURL }),
        }
      );

      if (!response.ok) {
        const error = await response.text();
        console.error('Sign in error:', error);
        throw new Error(`Sign in failed: ${response.status}`);
      }

      const data = await response.json();
      if (data.url) {
        window.location.href = data.url;
      } else {
        throw new Error('No redirect URL received from auth server');
      }
    } catch (error) {
      console.error('Sign in error:', error);
      throw error;
    }
  },

  /**
   * Sign out and clear all auth state
   */
  async signOut(): Promise<void> {
    try {
      await fetchWithCredentials(`${GATEWAY_URL}${BETTERAUTH_PATH}/sign-out`, {
        method: 'POST',
      });
    } catch (error) {
      console.error('Sign out error:', error);
    } finally {
      tokenStorage.clear();
    }
  },

  /**
   * Get current BetterAuth session (cookie-based)
   */
  async getSession(): Promise<AuthSession | null> {
    try {
      const response = await fetchWithCredentials(`${GATEWAY_URL}${BETTERAUTH_PATH}/get-session`);
      if (!response.ok) return null;
      const data = await response.json();
      return data?.session ? data : null;
    } catch (error) {
      console.error('Get session error:', error);
      return null;
    }
  },

  /**
   * Exchange BetterAuth session for Identity access/refresh tokens
   */
  async exchangeTokens(): Promise<AuthTokens | null> {
    try {
      // First get the exchange token from BetterAuth
      const exchangeResponse = await fetchWithCredentials(
        `${GATEWAY_URL}${BETTERAUTH_PATH}/exchange`,
        { method: 'POST' }
      );

      if (!exchangeResponse.ok) {
        console.error('Failed to get exchange token');
        return null;
      }

      const { exchangeToken } = await exchangeResponse.json();

      // Exchange for Identity tokens
      const identityResponse = await fetch(`${GATEWAY_URL}${IDENTITY_PATH}/exchange`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ exchangeToken }),
      });

      if (!identityResponse.ok) {
        console.error('Failed to exchange for Identity tokens');
        return null;
      }

      const data = await identityResponse.json();

      // Identity returns expiresIn (seconds until expiry), convert to expiresAt (Unix timestamp in seconds)
      const tokens: AuthTokens = {
        accessToken: data.accessToken,
        refreshToken: data.refreshToken,
        expiresAt: Math.floor(Date.now() / 1000) + data.expiresIn,
      };

      tokenStorage.setTokens(tokens);
      return tokens;
    } catch (error) {
      console.error('Token exchange error:', error);
      return null;
    }
  },

  /**
   * Refresh access token using refresh token
   */
  async refreshTokens(): Promise<AuthTokens | null> {
    const refreshToken = tokenStorage.getRefreshToken();
    if (!refreshToken) return null;

    try {
      const response = await fetch(`${GATEWAY_URL}${IDENTITY_PATH}/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      });

      if (!response.ok) {
        tokenStorage.clear();
        return null;
      }

      const data = await response.json();

      // Identity returns expiresIn (seconds until expiry), convert to expiresAt (Unix timestamp in seconds)
      const tokens: AuthTokens = {
        accessToken: data.accessToken,
        refreshToken: data.refreshToken,
        expiresAt: Math.floor(Date.now() / 1000) + data.expiresIn,
      };

      tokenStorage.setTokens(tokens);
      return tokens;
    } catch (error) {
      console.error('Token refresh error:', error);
      tokenStorage.clear();
      return null;
    }
  },

  /**
   * Get current tokens, refreshing if needed
   */
  async getTokens(): Promise<AuthTokens | null> {
    const tokens = tokenStorage.getTokens();

    if (!tokens) return null;

    if (tokenStorage.isExpired()) {
      return this.refreshTokens();
    }

    if (tokenStorage.isExpiringSoon()) {
      // Refresh in background, return current tokens
      this.refreshTokens().catch(console.error);
    }

    return tokens;
  },

  /**
   * Check if user is authenticated
   */
  isAuthenticated(): boolean {
    return !tokenStorage.isExpired();
  },

  /**
   * Get access token for API calls
   */
  async getAccessToken(): Promise<string | null> {
    const tokens = await this.getTokens();
    return tokens?.accessToken ?? null;
  },
};

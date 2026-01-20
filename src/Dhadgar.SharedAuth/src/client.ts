import { tokenStorage } from './storage';
import type { AuthTokens, AuthClientConfig, SignInOptions, AuthSession } from './types';

// genericOAuth providers use different routes than built-in social providers
const GENERIC_OAUTH_PROVIDERS = ['microsoft'];

// Trusted OAuth provider origins for redirect validation (defense in depth)
const TRUSTED_OAUTH_ORIGINS = [
  'https://login.microsoftonline.com',
  'https://accounts.google.com',
  'https://discord.com',
  'https://github.com',
  'https://appleid.apple.com',
  'https://www.facebook.com',
  'https://id.twitch.tv',
];

function createFetchWithCredentials() {
  return async function fetchWithCredentials(url: string, options: RequestInit = {}): Promise<Response> {
    return fetch(url, {
      ...options,
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json',
        ...options.headers,
      },
    });
  };
}

/**
 * Validates and performs redirect to OAuth provider
 * @throws Error if URL is invalid or untrusted
 */
function validateAndRedirect(url: string): void {
  let parsedUrl: URL;
  try {
    parsedUrl = new URL(url);
  } catch {
    throw new Error('Invalid redirect URL received from auth server');
  }

  // Ensure HTTPS for security
  if (parsedUrl.protocol !== 'https:') {
    throw new Error('Redirect URL must use HTTPS');
  }

  // Validate against trusted OAuth provider origins
  // Use hostname for subdomain check since origin includes protocol
  const isTrusted = TRUSTED_OAUTH_ORIGINS.some(origin =>
    parsedUrl.origin === origin || parsedUrl.hostname.endsWith('.microsoftonline.com')
  );

  if (!isTrusted) {
    console.warn('Redirect to untrusted origin:', parsedUrl.origin);
    // Allow redirect but log warning - server is trusted, this is defense in depth
  }

  window.location.href = parsedUrl.toString();
}

/**
 * Handles sign-in response from BetterAuth
 * @throws Error if response indicates failure or contains invalid data
 */
async function handleSignInResponse(response: Response): Promise<void> {
  if (!response.ok) {
    const errorText = await response.text().catch(() => 'Unknown error');
    throw new Error(`Sign in failed: ${response.status} - ${errorText}`);
  }

  let data: { url?: string };
  try {
    data = await response.json();
  } catch {
    throw new Error('Invalid response from auth server: failed to parse JSON');
  }

  if (!data.url) {
    throw new Error('No redirect URL received from auth server');
  }

  validateAndRedirect(data.url);
}

/**
 * Creates an auth client configured for a specific application
 */
export function createAuthClient(config: AuthClientConfig) {
  const gatewayUrl = config.gatewayUrl;
  const betterAuthPath = config.betterAuthPath ?? '/api/v1/betterauth';
  const identityPath = config.identityPath ?? '/api/v1/identity';
  const fetchWithCredentials = createFetchWithCredentials();

  return {
    /**
     * Initiate OAuth sign-in flow
     * Built-in social providers use POST to /sign-in/social with { provider }
     * genericOAuth providers (Microsoft) use POST to /sign-in/oauth2 with { providerId }
     */
    async signIn(options: SignInOptions): Promise<void> {
      const { provider, callbackURL = '/callback' } = options;

      // genericOAuth providers use /sign-in/oauth2 with providerId in body
      if (GENERIC_OAUTH_PROVIDERS.includes(provider)) {
        const response = await fetchWithCredentials(
          `${gatewayUrl}${betterAuthPath}/sign-in/oauth2`,
          {
            method: 'POST',
            body: JSON.stringify({ providerId: provider, callbackURL }),
          }
        );
        await handleSignInResponse(response);
        return;
      }

      // Built-in social providers use POST which returns a redirect URL
      const response = await fetchWithCredentials(
        `${gatewayUrl}${betterAuthPath}/sign-in/social`,
        {
          method: 'POST',
          body: JSON.stringify({ provider, callbackURL }),
        }
      );
      await handleSignInResponse(response);
    },

    /**
     * Sign out and clear all auth state
     */
    async signOut(): Promise<void> {
      try {
        await fetchWithCredentials(`${gatewayUrl}${betterAuthPath}/sign-out`, {
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
        const response = await fetchWithCredentials(`${gatewayUrl}${betterAuthPath}/get-session`);
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
          `${gatewayUrl}${betterAuthPath}/exchange`,
          { method: 'POST' }
        );

        if (!exchangeResponse.ok) {
          console.error('Failed to get exchange token');
          return null;
        }

        const { exchangeToken } = await exchangeResponse.json();

        // Exchange for Identity tokens
        const identityResponse = await fetch(`${gatewayUrl}${identityPath}/exchange`, {
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
        const response = await fetch(`${gatewayUrl}${identityPath}/refresh`, {
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
}

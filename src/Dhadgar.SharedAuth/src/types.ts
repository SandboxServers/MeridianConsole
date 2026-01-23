export interface User {
  id: string;
  email: string;
  name?: string;
  image?: string;
  emailVerified: boolean;
  createdAt: Date;
  updatedAt: Date;
}

export interface Session {
  id: string;
  userId: string;
  expiresAt: Date;
  token: string;
  ipAddress?: string;
  userAgent?: string;
}

export interface AuthTokens {
  accessToken: string;
  refreshToken: string;
  expiresAt: number;
}

export interface AuthState {
  user: User | null;
  tokens: AuthTokens | null;
  isLoading: boolean;
  isAuthenticated: boolean;
}

export interface OAuthProvider {
  id: string;
  name: string;
  icon: string;
  color: string;
  glowColor: string;
}

// Sign-in category configuration (decoupled from provider data)
export type OAuthCategory = 'social' | 'gaming' | 'other';

export interface SignInCategory {
  id: OAuthCategory;
  name: string;
  description: string;
  providers: string[];
  /** Use compact grid layout (icons only) */
  compact?: boolean;
}

export interface AuthClientConfig {
  gatewayUrl: string;
  betterAuthPath?: string;
  identityPath?: string;
}

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

// OAuth provider data (UI-agnostic)
// Note: Xbox uses Microsoft OAuth under the hood (same providerId)
export const OAUTH_PROVIDERS: OAuthProvider[] = [
  { id: 'google', name: 'Google', icon: 'google', color: '#4285F4', glowColor: 'rgba(66, 133, 244, 0.4)' },
  { id: 'microsoft', name: 'Microsoft', icon: 'microsoft', color: '#00A4EF', glowColor: 'rgba(0, 164, 239, 0.4)' },
  { id: 'facebook', name: 'Facebook', icon: 'facebook', color: '#1877F2', glowColor: 'rgba(24, 119, 242, 0.4)' },
  { id: 'linkedin', name: 'LinkedIn', icon: 'linkedin', color: '#0A66C2', glowColor: 'rgba(10, 102, 194, 0.4)' },
  { id: 'amazon', name: 'Amazon', icon: 'amazon', color: '#FF9900', glowColor: 'rgba(255, 153, 0, 0.4)' },
  { id: 'yahoo', name: 'Yahoo', icon: 'yahoo', color: '#6001D2', glowColor: 'rgba(96, 1, 210, 0.4)' },
  { id: 'discord', name: 'Discord', icon: 'discord', color: '#5865F2', glowColor: 'rgba(88, 101, 242, 0.4)' },
  { id: 'steam', name: 'Steam', icon: 'steam', color: '#1B2838', glowColor: 'rgba(102, 192, 244, 0.4)' },
  { id: 'xbox', name: 'Xbox', icon: 'xbox', color: '#107C10', glowColor: 'rgba(16, 124, 16, 0.4)' },
  { id: 'twitch', name: 'Twitch', icon: 'twitch', color: '#9146FF', glowColor: 'rgba(145, 70, 255, 0.4)' },
  { id: 'battlenet', name: 'Battle.net', icon: 'battlenet', color: '#00AEFF', glowColor: 'rgba(0, 174, 255, 0.4)' },
  { id: 'roblox', name: 'Roblox', icon: 'roblox', color: '#E2231A', glowColor: 'rgba(226, 35, 26, 0.4)' },
  { id: 'github', name: 'GitHub', icon: 'github', color: '#24292F', glowColor: 'rgba(36, 41, 47, 0.4)' },
  { id: 'slack', name: 'Slack', icon: 'slack', color: '#4A154B', glowColor: 'rgba(74, 21, 75, 0.4)' },
  { id: 'reddit', name: 'Reddit', icon: 'reddit', color: '#FF4500', glowColor: 'rgba(255, 69, 0, 0.4)' },
  { id: 'spotify', name: 'Spotify', icon: 'spotify', color: '#1DB954', glowColor: 'rgba(29, 185, 84, 0.4)' },
  { id: 'paypal', name: 'PayPal', icon: 'paypal', color: '#003087', glowColor: 'rgba(0, 48, 135, 0.4)' },
  { id: 'lego', name: 'LEGO', icon: 'lego', color: '#FFED00', glowColor: 'rgba(255, 237, 0, 0.4)' },
];

// Provider lookup map for efficient access
const providerMap = new Map(OAUTH_PROVIDERS.map(p => [p.id, p]));

/** Get a provider by ID */
export const getProvider = (id: string): OAuthProvider | undefined => providerMap.get(id);

// Sign-in page category configuration (decoupled from provider data)
// This defines the UI structure - which providers appear in which sections
export const SIGN_IN_CATEGORIES: SignInCategory[] = [
  {
    id: 'social',
    name: 'Social & Identity',
    description: 'Sign in with your social account',
    providers: ['google', 'microsoft', 'facebook', 'linkedin', 'amazon', 'yahoo'],
  },
  {
    id: 'gaming',
    name: 'Gaming',
    description: 'Connect your gaming identity',
    providers: ['discord', 'steam', 'xbox', 'twitch', 'battlenet', 'roblox'],
  },
  {
    id: 'other',
    name: 'More Options',
    description: 'Other sign-in methods',
    providers: ['github', 'slack', 'reddit', 'spotify', 'paypal', 'lego'],
  },
];

/** Get providers for a sign-in category */
export const getProvidersByCategory = (categoryId: OAuthCategory): OAuthProvider[] => {
  const category = SIGN_IN_CATEGORIES.find(c => c.id === categoryId);
  if (!category) return [];

  const providers: OAuthProvider[] = [];
  for (const id of category.providers) {
    const provider = providerMap.get(id);
    if (provider) {
      providers.push(provider);
    } else {
      console.warn(
        `[SharedAuth] Unknown provider "${id}" in category "${categoryId}". ` +
        `Check SIGN_IN_CATEGORIES configuration for typos.`
      );
    }
  }
  return providers;
};

/** Get sign-in category by ID */
export const getSignInCategory = (id: OAuthCategory): SignInCategory | undefined =>
  SIGN_IN_CATEGORIES.find(c => c.id === id);

// Legacy export for backward compatibility
export const OAUTH_CATEGORIES: Record<OAuthCategory, { name: string; description: string }> = {
  social: { name: 'Social & Identity', description: 'Sign in with your social account' },
  gaming: { name: 'Gaming', description: 'Connect your gaming identity' },
  other: { name: 'More Options', description: 'Other sign-in methods' },
};

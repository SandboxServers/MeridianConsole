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

export const OAUTH_PROVIDERS: OAuthProvider[] = [
  {
    id: 'discord',
    name: 'Discord',
    icon: 'discord',
    color: '#5865F2',
    glowColor: 'rgba(88, 101, 242, 0.4)',
  },
  {
    id: 'google',
    name: 'Google',
    icon: 'google',
    color: '#4285F4',
    glowColor: 'rgba(66, 133, 244, 0.4)',
  },
  {
    id: 'github',
    name: 'GitHub',
    icon: 'github',
    color: '#24292F',
    glowColor: 'rgba(36, 41, 47, 0.4)',
  },
  {
    id: 'microsoft',
    name: 'Microsoft',
    icon: 'microsoft',
    color: '#00A4EF',
    glowColor: 'rgba(0, 164, 239, 0.4)',
  },
  {
    id: 'twitch',
    name: 'Twitch',
    icon: 'twitch',
    color: '#9146FF',
    glowColor: 'rgba(145, 70, 255, 0.4)',
  },
  {
    id: 'facebook',
    name: 'Facebook',
    icon: 'facebook',
    color: '#1877F2',
    glowColor: 'rgba(24, 119, 242, 0.4)',
  },
  {
    id: 'apple',
    name: 'Apple',
    icon: 'apple',
    color: '#000000',
    glowColor: 'rgba(255, 255, 255, 0.2)',
  },
];

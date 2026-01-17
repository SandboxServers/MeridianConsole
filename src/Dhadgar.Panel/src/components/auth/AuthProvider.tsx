import {
  createContext,
  useContext,
  useEffect,
  useState,
  useCallback,
  type ReactNode,
} from 'react';
import { authClient, tokenStorage } from '../../lib/auth';
import type { AuthState, User, AuthTokens } from '../../lib/auth';

interface AuthContextValue extends AuthState {
  signIn: (provider: string, callbackURL?: string) => void;
  signOut: () => Promise<void>;
  refreshAuth: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

interface AuthProviderProps {
  children: ReactNode;
}

export function AuthProvider({ children }: AuthProviderProps) {
  const [state, setState] = useState<AuthState>({
    user: null,
    tokens: null,
    isLoading: true,
    isAuthenticated: false,
  });

  const refreshAuth = useCallback(async () => {
    setState((prev) => ({ ...prev, isLoading: true }));

    try {
      // First check if we have stored tokens
      let tokens = tokenStorage.getTokens();

      if (tokens && tokenStorage.isExpired()) {
        // Try to refresh
        tokens = await authClient.refreshTokens();
      }

      if (tokens) {
        // Get user session from BetterAuth
        const session = await authClient.getSession();
        setState({
          user: session?.user ?? null,
          tokens,
          isLoading: false,
          isAuthenticated: true,
        });
      } else {
        // Check if there's a BetterAuth session we can exchange
        const session = await authClient.getSession();
        if (session?.user) {
          const newTokens = await authClient.exchangeTokens();
          if (newTokens) {
            setState({
              user: session.user,
              tokens: newTokens,
              isLoading: false,
              isAuthenticated: true,
            });
            return;
          }
        }

        setState({
          user: null,
          tokens: null,
          isLoading: false,
          isAuthenticated: false,
        });
      }
    } catch (error) {
      console.error('Auth refresh error:', error);
      setState({
        user: null,
        tokens: null,
        isLoading: false,
        isAuthenticated: false,
      });
    }
  }, []);

  useEffect(() => {
    refreshAuth();

    // Set up token refresh interval
    const intervalId = setInterval(() => {
      if (tokenStorage.isExpiringSoon() && !tokenStorage.isExpired()) {
        authClient.refreshTokens().catch(console.error);
      }
    }, 60000); // Check every minute

    return () => clearInterval(intervalId);
  }, [refreshAuth]);

  const signIn = useCallback((provider: string, callbackURL = '/callback') => {
    authClient.signIn({ provider, callbackURL });
  }, []);

  const signOut = useCallback(async () => {
    await authClient.signOut();
    setState({
      user: null,
      tokens: null,
      isLoading: false,
      isAuthenticated: false,
    });
  }, []);

  const value: AuthContextValue = {
    ...state,
    signIn,
    signOut,
    refreshAuth,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}

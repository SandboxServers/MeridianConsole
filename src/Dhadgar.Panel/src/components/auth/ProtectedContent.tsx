import { useEffect, type ReactNode } from 'react';
import { authClient, tokenStorage } from '../../lib/auth';
import { LoadingSpinner } from '../ui';

interface ProtectedContentProps {
  children: ReactNode;
  fallback?: ReactNode;
}

export default function ProtectedContent({ children, fallback }: ProtectedContentProps) {
  useEffect(() => {
    async function checkAuth() {
      // If no tokens stored, redirect to login
      if (tokenStorage.isExpired()) {
        // Try to exchange if there's a BetterAuth session
        const session = await authClient.getSession();
        if (session?.user) {
          const tokens = await authClient.exchangeTokens();
          if (tokens) return; // Auth successful
        }
        // No valid auth, redirect
        window.location.href = '/login';
      }
    }

    checkAuth();
  }, []);

  // Check auth state
  if (typeof window !== 'undefined' && tokenStorage.isExpired()) {
    return (
      fallback || (
        <div className="flex items-center justify-center min-h-[50vh]">
          <LoadingSpinner size="lg" />
        </div>
      )
    );
  }

  return <>{children}</>;
}

import { useEffect, useState } from 'react';
import { authClient } from '../../lib/auth';
import { LoadingSpinner } from '../ui';

type CallbackState = 'loading' | 'success' | 'error';

export default function CallbackHandler() {
  const [state, setState] = useState<CallbackState>('loading');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    async function handleCallback() {
      try {
        // Check for error in URL params
        const params = new URLSearchParams(window.location.search);
        const errorParam = params.get('error');
        if (errorParam) {
          setError(errorParam === 'access_denied'
            ? 'Access was denied by the provider'
            : `Authentication error: ${errorParam}`);
          setState('error');
          return;
        }

        // Try to exchange BetterAuth session for Identity tokens
        const tokens = await authClient.exchangeTokens();

        if (tokens) {
          setState('success');
          // Redirect to profile page after successful login
          window.location.href = '/profile';
        } else {
          // Check if there's a session without tokens (provider might not be fully configured)
          const session = await authClient.getSession();
          if (session?.user) {
            setError('Token exchange failed. The authentication provider may not be fully configured.');
            setState('error');
          } else {
            setError('No authentication session found. Please try again.');
            setState('error');
          }
        }
      } catch (err) {
        console.error('Callback error:', err);
        setError(err instanceof Error ? err.message : 'An unexpected error occurred');
        setState('error');
      }
    }

    handleCallback();
  }, []);

  if (state === 'loading') {
    return (
      <div className="min-h-screen flex flex-col items-center justify-center p-4">
        <LoadingSpinner size="lg" />
        <p className="mt-4 text-text-secondary font-mono text-sm animate-pulse">
          ESTABLISHING SECURE CONNECTION...
        </p>
      </div>
    );
  }

  if (state === 'error') {
    return (
      <div className="min-h-screen flex flex-col items-center justify-center p-4">
        <div className="text-center max-w-md">
          <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-cyber-magenta/20 border border-cyber-magenta/50 flex items-center justify-center">
            <svg
              className="w-8 h-8 text-cyber-magenta"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <circle cx="12" cy="12" r="10" />
              <line x1="15" y1="9" x2="9" y2="15" />
              <line x1="9" y1="9" x2="15" y2="15" />
            </svg>
          </div>
          <h1 className="font-display text-xl text-cyber-magenta mb-2">
            AUTHENTICATION FAILED
          </h1>
          <p className="text-text-secondary mb-6">{error}</p>
          <a
            href="/login"
            className="inline-flex items-center justify-center px-6 py-2 rounded-full border border-cyber-cyan/50 bg-cyber-cyan/20 text-cyber-cyan font-medium transition-all hover:bg-cyber-cyan/30 hover:border-cyber-cyan hover:shadow-glow-cyan"
          >
            Return to Login
          </a>
        </div>
      </div>
    );
  }

  // Success state - brief moment before redirect
  return (
    <div className="min-h-screen flex flex-col items-center justify-center p-4">
      <div className="text-center">
        <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-cyber-green/20 border border-cyber-green/50 flex items-center justify-center">
          <svg
            className="w-8 h-8 text-cyber-green"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
          >
            <polyline points="20 6 9 17 4 12" />
          </svg>
        </div>
        <h1 className="font-display text-xl text-cyber-green mb-2">
          ACCESS GRANTED
        </h1>
        <p className="text-text-secondary font-mono text-sm">
          Redirecting...
        </p>
      </div>
    </div>
  );
}

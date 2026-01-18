import { useState } from 'react';
import { clsx } from 'clsx';
import { authClient, OAUTH_PROVIDERS } from '../../lib/auth';
import ProviderIcon from './ProviderIcon';

interface OAuthButtonGroupProps {
  callbackURL?: string;
  compact?: boolean;
}

export default function OAuthButtonGroup({
  callbackURL,
  compact = false,
}: OAuthButtonGroupProps) {
  const [loadingProvider, setLoadingProvider] = useState<string | null>(null);

  const handleOAuth = async (providerId: string) => {
    setLoadingProvider(providerId);
    try {
      // Compute callback URL at runtime using current origin (not build-time URL)
      const runtimeCallbackURL = callbackURL || `${window.location.origin}/callback`;
      await authClient.signIn({ provider: providerId, callbackURL: runtimeCallbackURL });
    } catch (error) {
      console.error('OAuth error:', error);
      setLoadingProvider(null);
    }
  };

  return (
    <div className={clsx('grid gap-3', compact ? 'grid-cols-4' : 'grid-cols-1')}>
      {OAUTH_PROVIDERS.map((provider) => (
        <button
          key={provider.id}
          onClick={() => handleOAuth(provider.id)}
          disabled={loadingProvider !== null}
          className={clsx(
            'relative flex items-center justify-center gap-3 px-4 py-3',
            'rounded-lg border border-glow-line/50 bg-panel-dark',
            'font-body text-text-primary',
            'transition-all duration-200',
            'hover:border-text-secondary hover:bg-glow-line/20',
            'focus:outline-none focus:ring-2 focus:ring-cyber-cyan/50 focus:ring-offset-2 focus:ring-offset-space-dark',
            'disabled:opacity-50 disabled:cursor-not-allowed',
            loadingProvider === provider.id && 'animate-pulse'
          )}
          style={{
            '--provider-color': provider.color,
            '--provider-glow': provider.glowColor,
          } as React.CSSProperties}
        >
          {loadingProvider === provider.id ? (
            <svg
              className="animate-spin h-5 w-5"
              xmlns="http://www.w3.org/2000/svg"
              fill="none"
              viewBox="0 0 24 24"
            >
              <circle
                className="opacity-25"
                cx="12"
                cy="12"
                r="10"
                stroke="currentColor"
                strokeWidth="4"
              />
              <path
                className="opacity-75"
                fill="currentColor"
                d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
              />
            </svg>
          ) : (
            <ProviderIcon provider={provider.id} className="w-5 h-5 flex-shrink-0" />
          )}
          {!compact && (
            <span className="flex-1 text-left">
              Continue with {provider.name}
            </span>
          )}
        </button>
      ))}
    </div>
  );
}

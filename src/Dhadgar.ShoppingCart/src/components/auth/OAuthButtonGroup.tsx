import { useState, useCallback } from 'react';
import { clsx } from 'clsx';
import {
  authClient,
  OAUTH_PROVIDERS,
  SIGN_IN_CATEGORIES,
  getProvidersByCategory,
  type OAuthProvider,
  type SignInCategory,
} from '../../lib/auth';
import ProviderIcon from './ProviderIcon';

interface OAuthButtonGroupProps {
  callbackURL?: string;
  compact?: boolean;
}

function CategorySection({
  category,
  providers,
  loadingProvider,
  onOAuth,
  isFirst,
}: {
  category: SignInCategory;
  providers: OAuthProvider[];
  loadingProvider: string | null;
  onOAuth: (providerId: string) => void;
  isFirst: boolean;
}) {
  const isCompact = category.compact === true;

  return (
    <div className={clsx(!isFirst && 'mt-6 pt-6 border-t border-glow-line/20')}>
      {/* Category Header */}
      <div className="mb-3">
        <h3 className="font-display text-xs tracking-wider text-text-secondary uppercase">
          {category.name}
        </h3>
      </div>

      {/* Provider Buttons */}
      <div className={clsx(
        'grid gap-2 md:gap-3',
        isCompact
          ? 'grid-cols-3 md:grid-cols-6' // Gaming: 3 cols mobile, 6 cols desktop (all visible)
          : 'grid-cols-1 md:grid-cols-2' // Social/Other: 1 col mobile, 2 cols desktop
      )}>
        {providers.map((provider) => (
          <button
            type="button"
            key={provider.id}
            onClick={() => onOAuth(provider.id)}
            disabled={loadingProvider !== null}
            className={clsx(
              'relative flex items-center gap-3 md:gap-4',
              isCompact
                ? 'justify-center px-4 py-4 md:px-5 md:py-5'
                : 'justify-start px-4 py-3 md:px-6 md:py-5',
              'rounded-lg border border-glow-line/50 bg-panel-dark',
              'font-body text-text-primary text-sm md:text-base',
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
            title={isCompact ? provider.name : undefined}
            aria-label={isCompact ? `Sign in with ${provider.name}` : undefined}
          >
            {loadingProvider === provider.id ? (
              <svg
                className="animate-spin h-6 w-6 md:h-8 md:w-8"
                xmlns="http://www.w3.org/2000/svg"
                fill="none"
                viewBox="0 0 24 24"
                aria-hidden="true"
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
              <ProviderIcon provider={provider.id} className="w-6 h-6 md:w-8 md:h-8 flex-shrink-0" />
            )}
            {!isCompact && (
              <span className="flex-1 text-left">
                {provider.name}
              </span>
            )}
          </button>
        ))}
      </div>
    </div>
  );
}

export default function OAuthButtonGroup({
  callbackURL,
  compact = false,
}: OAuthButtonGroupProps) {
  const [loadingProvider, setLoadingProvider] = useState<string | null>(null);

  const handleOAuth = useCallback(async (providerId: string) => {
    setLoadingProvider(providerId);
    try {
      const runtimeCallbackURL = callbackURL || `${window.location.origin}/callback`;
      await authClient.signIn({ provider: providerId, callbackURL: runtimeCallbackURL });
    } catch (error) {
      console.error('OAuth error:', error);
      setLoadingProvider(null);
    }
  }, [callbackURL]);

  // Compact mode: simple grid of all providers
  if (compact) {
    return (
      <div className="grid grid-cols-4 md:grid-cols-6 gap-3">
        {OAUTH_PROVIDERS.map((provider) => (
          <button
            type="button"
            key={provider.id}
            onClick={() => handleOAuth(provider.id)}
            disabled={loadingProvider !== null}
            className={clsx(
              'relative flex items-center justify-center px-4 py-4 md:px-5 md:py-5',
              'rounded-lg border border-glow-line/50 bg-panel-dark',
              'transition-all duration-200',
              'hover:border-text-secondary hover:bg-glow-line/20',
              'focus:outline-none focus:ring-2 focus:ring-cyber-cyan/50',
              'disabled:opacity-50 disabled:cursor-not-allowed',
              loadingProvider === provider.id && 'animate-pulse'
            )}
            title={provider.name}
            aria-label={`Sign in with ${provider.name}`}
          >
            {loadingProvider === provider.id ? (
              <svg
                className="animate-spin h-6 w-6 md:h-8 md:w-8"
                fill="none"
                viewBox="0 0 24 24"
                aria-hidden="true"
              >
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
              </svg>
            ) : (
              <ProviderIcon provider={provider.id} className="w-6 h-6 md:w-8 md:h-8" />
            )}
          </button>
        ))}
      </div>
    );
  }

  // Full mode: categorized sections using decoupled configuration
  return (
    <div>
      {SIGN_IN_CATEGORIES.map((category, index) => {
        const providers = getProvidersByCategory(category.id);
        if (providers.length === 0) return null;

        return (
          <CategorySection
            key={category.id}
            category={category}
            providers={providers}
            loadingProvider={loadingProvider}
            onOAuth={handleOAuth}
            isFirst={index === 0}
          />
        );
      })}
    </div>
  );
}

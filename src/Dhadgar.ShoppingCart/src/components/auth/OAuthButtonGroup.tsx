import { useState } from 'react';
import { clsx } from 'clsx';
import { authClient, OAUTH_PROVIDERS, OAUTH_CATEGORIES, getProvidersByCategory, type OAuthCategory, type OAuthProvider } from '../../lib/auth';
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
  category: OAuthCategory;
  providers: OAuthProvider[];
  loadingProvider: string | null;
  onOAuth: (providerId: string) => void;
  isFirst: boolean;
}) {
  const categoryInfo = OAUTH_CATEGORIES[category];
  const isGaming = category === 'gaming';

  return (
    <div className={clsx(!isFirst && 'mt-6 pt-6 border-t border-glow-line/20')}>
      {/* Category Header */}
      <div className="mb-3">
        <h3 className="font-display text-xs tracking-wider text-text-secondary uppercase">
          {categoryInfo.name}
        </h3>
      </div>

      {/* Provider Buttons */}
      <div className={clsx(
        'grid gap-2',
        isGaming ? 'grid-cols-3' : 'grid-cols-1'
      )}>
        {providers.map((provider) => (
          <button
            key={provider.id}
            onClick={() => onOAuth(provider.id)}
            disabled={loadingProvider !== null}
            className={clsx(
              'relative flex items-center gap-3',
              isGaming ? 'justify-center px-3 py-3' : 'justify-start px-4 py-3',
              'rounded-lg border border-glow-line/50 bg-panel-dark',
              'font-body text-text-primary text-sm',
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
            title={isGaming ? provider.name : undefined}
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
            {!isGaming && (
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

  const handleOAuth = async (providerId: string) => {
    setLoadingProvider(providerId);
    try {
      const runtimeCallbackURL = callbackURL || `${window.location.origin}/callback`;
      await authClient.signIn({ provider: providerId, callbackURL: runtimeCallbackURL });
    } catch (error) {
      console.error('OAuth error:', error);
      setLoadingProvider(null);
    }
  };

  // Compact mode: simple grid of all providers
  if (compact) {
    return (
      <div className="grid grid-cols-4 gap-3">
        {OAUTH_PROVIDERS.map((provider) => (
          <button
            key={provider.id}
            onClick={() => handleOAuth(provider.id)}
            disabled={loadingProvider !== null}
            className={clsx(
              'relative flex items-center justify-center px-3 py-3',
              'rounded-lg border border-glow-line/50 bg-panel-dark',
              'transition-all duration-200',
              'hover:border-text-secondary hover:bg-glow-line/20',
              'focus:outline-none focus:ring-2 focus:ring-cyber-cyan/50',
              'disabled:opacity-50 disabled:cursor-not-allowed',
              loadingProvider === provider.id && 'animate-pulse'
            )}
            title={provider.name}
          >
            {loadingProvider === provider.id ? (
              <svg className="animate-spin h-5 w-5" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
              </svg>
            ) : (
              <ProviderIcon provider={provider.id} className="w-5 h-5" />
            )}
          </button>
        ))}
      </div>
    );
  }

  // Full mode: categorized sections
  const categories: OAuthCategory[] = ['social', 'gaming', 'other'];

  return (
    <div>
      {categories.map((category, index) => {
        const providers = getProvidersByCategory(category);
        if (providers.length === 0) return null;

        return (
          <CategorySection
            key={category}
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

import { Panel } from '../ui';
import OAuthButtonGroup from './OAuthButtonGroup';

interface ShopLoginPageProps {
  callbackURL?: string;
}

export default function ShopLoginPage({ callbackURL }: ShopLoginPageProps) {
  return (
    <div className="min-h-screen flex items-center justify-center p-4">
      <div className="w-full max-w-md md:max-w-2xl lg:max-w-3xl animate-fade-in">
        <Panel variant="bordered" className="relative overflow-hidden border-cyber-magenta/30 shadow-glow-magenta">
          {/* Header */}
          <div className="text-center mb-8">
            <div className="inline-flex items-center justify-center w-16 h-16 mb-4 rounded-full bg-cyber-magenta/10 border border-cyber-magenta/30">
              <svg
                className="w-8 h-8 text-cyber-magenta"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
              >
                <path d="M12 2L2 7l10 5 10-5-10-5z" />
                <path d="M2 17l10 5 10-5" />
                <path d="M2 12l10 5 10-5" />
              </svg>
            </div>
            <h1 className="font-display text-2xl text-cyber-magenta tracking-wider mb-2">
              WELCOME BACK
            </h1>
            <p className="text-text-secondary text-sm">
              Sign in to manage your Meridian Console subscription
            </p>
          </div>

          {/* OAuth Buttons */}
          <OAuthButtonGroup callbackURL={callbackURL} />

          {/* Footer */}
          <div className="mt-8 pt-6 border-t border-glow-line/30 text-center">
            <p className="text-text-muted text-xs">
              Don't have an account?{' '}
              <a href="/" className="text-cyber-magenta hover:underline">
                Learn more about Meridian
              </a>
            </p>
          </div>
        </Panel>

        {/* Back to home */}
        <p className="mt-4 text-center">
          <a href="/" className="text-text-muted text-sm hover:text-text-secondary transition-colors">
            &larr; Back to home
          </a>
        </p>
      </div>
    </div>
  );
}

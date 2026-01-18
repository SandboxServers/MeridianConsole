import Panel from '../ui/Panel';
import OAuthButtonGroup from './OAuthButtonGroup';

interface LoginPageProps {
  callbackURL?: string;
}

export default function LoginPage({ callbackURL = '/callback' }: LoginPageProps) {
  return (
    <div className="min-h-screen flex items-center justify-center p-4">
      <div className="w-full max-w-md animate-fade-in">
        <Panel variant="bordered" className="relative overflow-hidden">
          {/* Header */}
          <div className="text-center mb-8">
            <div className="inline-flex items-center justify-center w-16 h-16 mb-4 rounded-full bg-cyber-cyan/10 border border-cyber-cyan/30">
              <svg
                className="w-8 h-8 text-cyber-cyan"
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
            <h1 className="font-display text-2xl text-cyber-cyan tracking-wider mb-2">
              SYSTEM ACCESS
            </h1>
            <p className="text-text-secondary text-sm">
              Authenticate to access Meridian Console
            </p>
          </div>

          {/* OAuth Buttons */}
          <OAuthButtonGroup callbackURL={callbackURL} />

          {/* Footer */}
          <div className="mt-8 pt-6 border-t border-glow-line/30 text-center">
            <p className="text-text-muted text-xs">
              By continuing, you agree to our{' '}
              <a href="/terms" className="text-cyber-cyan hover:underline">
                Terms of Service
              </a>{' '}
              and{' '}
              <a href="/privacy" className="text-cyber-cyan hover:underline">
                Privacy Policy
              </a>
            </p>
          </div>

          {/* Decorative scan line */}
          <div className="absolute inset-0 pointer-events-none overflow-hidden">
            <div className="absolute inset-x-0 h-px bg-gradient-to-r from-transparent via-cyber-cyan/20 to-transparent animate-scan-line" />
          </div>
        </Panel>

        {/* Version info */}
        <p className="mt-4 text-center text-text-muted text-xs font-mono">
          MERIDIAN CONSOLE v1.0.0
        </p>
      </div>
    </div>
  );
}

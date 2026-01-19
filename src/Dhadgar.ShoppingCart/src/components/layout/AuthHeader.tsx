import { useEffect, useState } from 'react';
import { authClient } from '../../lib/auth';
import { identityApi, type UserProfile } from '../../lib/auth/api';

export default function AuthHeader() {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    async function checkAuth() {
      const authenticated = authClient.isAuthenticated();
      setIsAuthenticated(authenticated);

      if (authenticated) {
        const profileData = await identityApi.getProfile();
        setProfile(profileData);
      }

      setIsLoading(false);
    }

    checkAuth();
  }, []);

  const handleSignOut = async () => {
    await authClient.signOut();
    window.location.href = '/';
  };

  // Show nothing while loading to prevent flash
  if (isLoading) {
    return (
      <div className="flex items-center gap-4">
        <span className="w-16 h-8 bg-panel-dark/50 rounded animate-pulse" />
        <span className="w-24 h-8 bg-panel-dark/50 rounded-full animate-pulse" />
      </div>
    );
  }

  // Authenticated state
  if (isAuthenticated && profile) {
    return (
      <div className="flex items-center gap-4">
        <a
          href="/profile"
          className="flex items-center gap-2 text-text-secondary hover:text-text-primary transition-colors"
        >
          <div className="w-8 h-8 rounded-full bg-cyber-cyan/20 border border-cyber-cyan/50 flex items-center justify-center">
            <span className="font-display text-xs text-cyber-cyan">
              {(profile.displayName || profile.email)[0].toUpperCase()}
            </span>
          </div>
          <span className="hidden sm:inline text-sm truncate max-w-[120px]">
            {profile.displayName || profile.email.split('@')[0]}
          </span>
        </a>
        <button
          onClick={handleSignOut}
          className="px-4 py-2 rounded-full border border-glow-line text-text-secondary font-medium transition-all hover:border-cyber-magenta/50 hover:text-cyber-magenta text-sm"
        >
          Sign Out
        </button>
      </div>
    );
  }

  // Not authenticated
  return (
    <div className="flex items-center gap-4">
      <a
        href="/login"
        className="text-text-secondary hover:text-text-primary transition-colors"
      >
        Sign In
      </a>
      <a
        href="/login"
        className="px-4 py-2 rounded-full bg-cyber-magenta/20 border border-cyber-magenta/50 text-cyber-magenta font-medium transition-all hover:bg-cyber-magenta/30 hover:shadow-glow-magenta"
      >
        Get Started
      </a>
    </div>
  );
}

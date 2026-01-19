import { useEffect, useState } from 'react';
import { identityApi, type UserProfile, type Organization, type LinkedAccount } from '../../lib/auth/api';
import { authClient } from '../../lib/auth';
import { LoadingSpinner } from '../ui';

const PROVIDER_COLORS: Record<string, string> = {
  discord: 'bg-[#5865F2]/20 border-[#5865F2]/50 text-[#5865F2]',
  google: 'bg-[#4285F4]/20 border-[#4285F4]/50 text-[#4285F4]',
  github: 'bg-[#24292F]/20 border-[#24292F]/50 text-white',
  microsoft: 'bg-[#00A4EF]/20 border-[#00A4EF]/50 text-[#00A4EF]',
  twitch: 'bg-[#9146FF]/20 border-[#9146FF]/50 text-[#9146FF]',
  steam: 'bg-[#1B2838]/20 border-[#66c0f4]/50 text-[#66c0f4]',
  battlenet: 'bg-[#00AEFF]/20 border-[#00AEFF]/50 text-[#00AEFF]',
};

function formatDate(dateString: string): string {
  return new Date(dateString).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function formatRelativeTime(dateString: string): string {
  const date = new Date(dateString);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffMins = Math.floor(diffMs / 60000);
  const diffHours = Math.floor(diffMs / 3600000);
  const diffDays = Math.floor(diffMs / 86400000);

  if (diffMins < 1) return 'Just now';
  if (diffMins < 60) return `${diffMins}m ago`;
  if (diffHours < 24) return `${diffHours}h ago`;
  if (diffDays < 7) return `${diffDays}d ago`;
  return formatDate(dateString);
}

export default function ProfilePage() {
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [linkedAccounts, setLinkedAccounts] = useState<LinkedAccount[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    async function loadProfile() {
      if (!authClient.isAuthenticated()) {
        window.location.href = '/login';
        return;
      }

      try {
        const [profileData, orgsData, accountsData] = await Promise.all([
          identityApi.getProfile(),
          identityApi.getOrganizations(),
          identityApi.getLinkedAccounts(),
        ]);

        if (!profileData) {
          setError('Failed to load profile');
          return;
        }

        setProfile(profileData);
        setOrganizations(orgsData);
        setLinkedAccounts(accountsData);
      } catch (err) {
        console.error('Failed to load profile:', err);
        setError('Failed to load profile');
      } finally {
        setIsLoading(false);
      }
    }

    loadProfile();
  }, []);

  const handleSignOut = async () => {
    await authClient.signOut();
    window.location.href = '/';
  };

  if (isLoading) {
    return (
      <div className="min-h-screen flex flex-col items-center justify-center p-4">
        <LoadingSpinner size="lg" />
        <p className="mt-4 text-text-secondary font-mono text-sm animate-pulse">
          LOADING PROFILE...
        </p>
      </div>
    );
  }

  if (error || !profile) {
    return (
      <div className="min-h-screen flex flex-col items-center justify-center p-4">
        <div className="text-center max-w-md">
          <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-cyber-magenta/20 border border-cyber-magenta/50 flex items-center justify-center">
            <svg className="w-8 h-8 text-cyber-magenta" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <circle cx="12" cy="12" r="10" />
              <line x1="15" y1="9" x2="9" y2="15" />
              <line x1="9" y1="9" x2="15" y2="15" />
            </svg>
          </div>
          <h1 className="font-display text-xl text-cyber-magenta mb-2">PROFILE ERROR</h1>
          <p className="text-text-secondary mb-6">{error || 'Unable to load profile'}</p>
          <a href="/" className="inline-flex items-center justify-center px-6 py-2 rounded-full border border-cyber-cyan/50 bg-cyber-cyan/20 text-cyber-cyan font-medium transition-all hover:bg-cyber-cyan/30">
            Return Home
          </a>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-space-dark">
      {/* Header */}
      <header className="border-b border-glow-line/30 bg-panel-darker/50">
        <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 py-6">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-4">
              <a href="/" className="flex items-center gap-3">
                <div className="w-8 h-8 rounded bg-cyber-magenta/20 border border-cyber-magenta/50 flex items-center justify-center">
                  <svg className="w-5 h-5 text-cyber-magenta" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <path d="M12 2L2 7l10 5 10-5-10-5z"></path>
                    <path d="M2 17l10 5 10-5"></path>
                    <path d="M2 12l10 5 10-5"></path>
                  </svg>
                </div>
                <span className="font-display text-lg text-text-primary tracking-wider">MERIDIAN</span>
              </a>
            </div>
            <button
              onClick={handleSignOut}
              className="px-4 py-2 rounded-full border border-glow-line text-text-secondary font-medium transition-all hover:border-cyber-magenta/50 hover:text-cyber-magenta"
            >
              Sign Out
            </button>
          </div>
        </div>
      </header>

      {/* Main Content */}
      <main className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
        <h1 className="font-display text-2xl text-text-primary tracking-wider mb-8">USER PROFILE</h1>

        {/* Profile Card */}
        <section className="mb-8 p-6 rounded-lg bg-panel-dark border border-glow-line/50">
          <div className="flex items-start gap-6">
            {/* Avatar */}
            <div className="w-20 h-20 rounded-full bg-cyber-magenta/20 border-2 border-cyber-magenta/50 flex items-center justify-center flex-shrink-0">
              <span className="font-display text-2xl text-cyber-magenta">
                {(profile.displayName || profile.email)[0].toUpperCase()}
              </span>
            </div>

            {/* Info */}
            <div className="flex-1 min-w-0">
              <h2 className="font-display text-xl text-text-primary mb-1">
                {profile.displayName || 'No display name set'}
              </h2>
              <p className="text-text-secondary mb-4">{profile.email}</p>

              <div className="grid grid-cols-2 gap-4 text-sm">
                <div>
                  <span className="text-text-muted">Email Verified</span>
                  <p className={profile.emailVerified ? 'text-cyber-green' : 'text-cyber-magenta'}>
                    {profile.emailVerified ? 'Yes' : 'No'}
                  </p>
                </div>
                <div>
                  <span className="text-text-muted">Passkeys</span>
                  <p className={profile.hasPasskeysRegistered ? 'text-cyber-green' : 'text-text-secondary'}>
                    {profile.hasPasskeysRegistered ? 'Registered' : 'Not set up'}
                  </p>
                </div>
                <div>
                  <span className="text-text-muted">Member Since</span>
                  <p className="text-text-secondary">{formatDate(profile.createdAt)}</p>
                </div>
                <div>
                  <span className="text-text-muted">Last Login</span>
                  <p className="text-text-secondary">{formatRelativeTime(profile.lastAuthenticatedAt)}</p>
                </div>
              </div>
            </div>
          </div>
        </section>

        {/* Organizations */}
        <section className="mb-8">
          <h2 className="font-display text-lg text-text-primary tracking-wider mb-4">ORGANIZATIONS</h2>
          {organizations.length === 0 ? (
            <p className="text-text-muted text-sm">No organizations found.</p>
          ) : (
            <div className="space-y-3">
              {organizations.map((org) => (
                <div
                  key={org.id}
                  className={`p-4 rounded-lg bg-panel-dark border ${
                    org.isPreferred ? 'border-cyber-cyan/50' : 'border-glow-line/50'
                  }`}
                >
                  <div className="flex items-center justify-between">
                    <div>
                      <div className="flex items-center gap-2">
                        <h3 className="font-medium text-text-primary">{org.name}</h3>
                        {org.isPreferred && (
                          <span className="px-2 py-0.5 text-xs rounded bg-cyber-cyan/20 text-cyber-cyan border border-cyber-cyan/50">
                            Default
                          </span>
                        )}
                      </div>
                      <p className="text-sm text-text-muted">{org.slug}</p>
                    </div>
                    <div className="text-right">
                      <span className="px-3 py-1 text-xs rounded-full bg-panel-darker border border-glow-line text-text-secondary capitalize">
                        {org.role}
                      </span>
                      <p className="text-xs text-text-muted mt-1">
                        Joined {formatRelativeTime(org.joinedAt)}
                      </p>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </section>

        {/* Linked Accounts */}
        <section className="mb-8">
          <h2 className="font-display text-lg text-text-primary tracking-wider mb-4">LINKED ACCOUNTS</h2>
          {linkedAccounts.length === 0 ? (
            <p className="text-text-muted text-sm">No linked accounts found.</p>
          ) : (
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              {linkedAccounts.map((account) => {
                const colorClass = PROVIDER_COLORS[account.provider.toLowerCase()] || 'bg-panel-darker border-glow-line text-text-secondary';
                return (
                  <div
                    key={account.id}
                    className={`p-4 rounded-lg border ${colorClass}`}
                  >
                    <div className="flex items-center gap-3">
                      <div className="w-10 h-10 rounded-full bg-black/20 flex items-center justify-center">
                        <span className="font-display text-sm uppercase">
                          {account.provider.slice(0, 2)}
                        </span>
                      </div>
                      <div className="flex-1 min-w-0">
                        <h3 className="font-medium capitalize">{account.provider}</h3>
                        <p className="text-xs opacity-70 truncate">
                          {account.providerDisplayName || 'Connected'}
                        </p>
                      </div>
                    </div>
                    <div className="mt-3 pt-3 border-t border-current/20 text-xs opacity-70">
                      <p>Linked {formatRelativeTime(account.linkedAt)}</p>
                      {account.lastUsedAt && (
                        <p>Last used {formatRelativeTime(account.lastUsedAt)}</p>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </section>

        {/* User ID (for debugging/support) */}
        <section className="p-4 rounded-lg bg-panel-darker border border-glow-line/30">
          <p className="text-xs text-text-muted font-mono">
            User ID: {profile.id}
          </p>
        </section>
      </main>
    </div>
  );
}

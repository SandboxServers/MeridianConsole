import { useEffect, useState } from 'react';
import { authClient, tokenStorage, api } from '../../lib/auth';
import { GlowButton, Panel, LoadingSpinner } from '../ui';

interface UserInfo {
  id: string;
  email: string;
  name?: string;
}

export default function DashboardContent() {
  const [user, setUser] = useState<UserInfo | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    async function loadUser() {
      try {
        // Check auth and get user info
        const session = await authClient.getSession();
        if (session?.user) {
          setUser({
            id: session.user.id,
            email: session.user.email,
            name: session.user.name,
          });
        } else {
          // No session, redirect to login
          window.location.href = '/login';
        }
      } catch (error) {
        console.error('Failed to load user:', error);
      } finally {
        setIsLoading(false);
      }
    }

    loadUser();
  }, []);

  const handleLogout = async () => {
    await authClient.signOut();
    window.location.href = '/login';
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-[50vh]">
        <LoadingSpinner size="lg" />
      </div>
    );
  }

  return (
    <div className="space-y-6 animate-fade-in">
      {/* Welcome header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-display text-2xl text-text-primary tracking-wide">
            COMMAND CENTER
          </h1>
          <p className="text-text-secondary mt-1">
            Welcome back{user?.name ? `, ${user.name}` : ''}
          </p>
        </div>
        <GlowButton variant="danger" onClick={handleLogout}>
          Sign Out
        </GlowButton>
      </div>

      {/* User info panel */}
      <Panel header="User Information" variant="bordered">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div>
            <label className="block text-text-muted text-xs font-mono uppercase tracking-wider mb-1">
              User ID
            </label>
            <p className="text-text-primary font-mono text-sm bg-panel-darker rounded px-3 py-2 border border-glow-line/30">
              {user?.id ?? 'N/A'}
            </p>
          </div>
          <div>
            <label className="block text-text-muted text-xs font-mono uppercase tracking-wider mb-1">
              Email
            </label>
            <p className="text-text-primary font-mono text-sm bg-panel-darker rounded px-3 py-2 border border-glow-line/30">
              {user?.email ?? 'N/A'}
            </p>
          </div>
        </div>
      </Panel>

      {/* Quick stats */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <Panel variant="default">
          <div className="text-center">
            <p className="text-text-muted text-xs font-mono uppercase tracking-wider mb-2">
              Active Servers
            </p>
            <p className="font-display text-3xl text-cyber-cyan">0</p>
          </div>
        </Panel>
        <Panel variant="default">
          <div className="text-center">
            <p className="text-text-muted text-xs font-mono uppercase tracking-wider mb-2">
              Connected Nodes
            </p>
            <p className="font-display text-3xl text-cyber-green">0</p>
          </div>
        </Panel>
        <Panel variant="default">
          <div className="text-center">
            <p className="text-text-muted text-xs font-mono uppercase tracking-wider mb-2">
              Pending Tasks
            </p>
            <p className="font-display text-3xl text-cyber-amber">0</p>
          </div>
        </Panel>
      </div>

      {/* Session info */}
      <Panel header="Session Status" variant="elevated">
        <div className="flex items-center gap-3">
          <div className="w-3 h-3 rounded-full bg-cyber-green animate-pulse" />
          <span className="text-text-secondary font-mono text-sm">
            Secure session active
          </span>
        </div>
      </Panel>
    </div>
  );
}

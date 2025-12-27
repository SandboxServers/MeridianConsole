import { useState } from 'react';
import { Sidebar } from './Sidebar';
import { MobileDrawer } from './MobileDrawer';
import type { ReactNode } from 'react';

interface MainLayoutProps {
  children: ReactNode;
}

export function MainLayout({ children }: MainLayoutProps) {
  const [sidebarOpen, setSidebarOpen] = useState(false);

  return (
    <div className="mx-auto w-full px-4 py-6 2xl:px-8">
      {/* Mobile top bar */}
      <div className="mb-4 flex items-center justify-between rounded-2xl border border-white/10 bg-white/5 p-4 lg:hidden">
        <a href="/" className="block">
          <div className="text-sm text-white/60">Meridian Console</div>
          <div className="text-xl font-bold tracking-tight">Scope</div>
        </a>

        <button
          type="button"
          onClick={() => setSidebarOpen(true)}
          className="rounded-lg border border-white/10 bg-white/5 p-2 transition-colors hover:bg-white/10"
        >
          <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h16" />
          </svg>
        </button>
      </div>

      <div className="flex flex-col gap-4 lg:flex-row">
        {/* Desktop sidebar */}
        <aside className="hidden lg:block lg:w-80">
          <div className="sticky top-6">
            <Sidebar />
          </div>
        </aside>

        {/* Main content */}
        <main className="min-w-0 flex-1 scope-content">
          {children}
        </main>
      </div>

      {/* Mobile drawer */}
      <MobileDrawer isOpen={sidebarOpen} onClose={() => setSidebarOpen(false)} />
    </div>
  );
}

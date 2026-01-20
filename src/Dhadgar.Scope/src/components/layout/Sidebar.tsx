import { useState, useMemo } from "react";
import { sections } from "../../lib/sections-registry";
import { TextField } from "../ui/TextField";

interface SidebarProps {
  onNavigate?: () => void;
}

export function Sidebar({ onNavigate }: SidebarProps) {
  const [filter, setFilter] = useState("");

  const filteredSections = useMemo(() => {
    const f = filter.trim().toLowerCase();
    if (!f) return sections;
    return sections.filter(
      (s) => s.title.toLowerCase().includes(f) || s.slug.toLowerCase().includes(f)
    );
  }, [filter]);

  const handleLinkClick = () => {
    onNavigate?.();
  };

  return (
    <div className="space-y-4">
      {/* Branding */}
      <div className="rounded-2xl border border-white/10 bg-white/5 p-4">
        <a href="/" onClick={handleLinkClick} className="block">
          <div className="text-sm text-white/60">Meridian Console</div>
          <div className="text-xl font-bold tracking-tight">Scope</div>
        </a>

        <div className="mt-4 space-y-2">
          <a
            href="/dependencies"
            onClick={handleLinkClick}
            className="flex w-full items-center gap-2 rounded-xl border border-white/10 bg-white/5 px-3 py-2 text-sm transition-colors hover:bg-white/10"
          >
            <svg
              className="h-5 w-5 text-white/70"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              aria-hidden="true"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M4 5a1 1 0 011-1h14a1 1 0 011 1v2a1 1 0 01-1 1H5a1 1 0 01-1-1V5zM4 13a1 1 0 011-1h6a1 1 0 011 1v6a1 1 0 01-1 1H5a1 1 0 01-1-1v-6zM16 13a1 1 0 011-1h2a1 1 0 011 1v6a1 1 0 01-1 1h-2a1 1 0 01-1-1v-6z"
              />
            </svg>
            Dependency Map
          </a>

          <TextField
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="Search sections..."
            clearable
            onClear={() => setFilter("")}
            icon={
              <svg
                className="h-4 w-4"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
                aria-hidden="true"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"
                />
              </svg>
            }
          />
        </div>
      </div>

      {/* Section navigation */}
      <div className="rounded-2xl border border-white/10 bg-white/5 p-2">
        <nav className="max-h-[70vh] overflow-auto">
          {filteredSections.map((section) => (
            <a
              key={section.slug}
              href={`/s/${section.slug}`}
              onClick={handleLinkClick}
              className="flex items-center rounded-xl px-3 py-2 text-sm transition-colors hover:bg-white/5"
            >
              <span className="text-white/60">{section.number}.</span>
              <span className="ml-2">{section.title}</span>
            </a>
          ))}
          {filteredSections.length === 0 && (
            <div className="px-3 py-2 text-sm text-white/50">No sections found</div>
          )}
        </nav>
      </div>

      {/* Hosting note */}
      <div className="rounded-2xl border border-white/10 bg-white/5 p-4">
        <div className="text-xs font-semibold text-white/70">Hosting note</div>
        <div className="mt-2 text-xs text-white/60">
          This site is built with Astro and designed to deploy cleanly to Azure Static Web Apps
          (Free tier).
        </div>
      </div>
    </div>
  );
}

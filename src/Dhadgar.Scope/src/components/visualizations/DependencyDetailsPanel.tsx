import type { DependencyNode } from "../../lib/types";

interface DependencyDetailsPanelProps {
  selected: DependencyNode | null;
  onSelectByName: (name: string) => void;
  onClear: () => void;
  className?: string;
}

export function DependencyDetailsPanel({
  selected,
  onSelectByName,
  onClear,
  className = "",
}: DependencyDetailsPanelProps) {
  if (!selected) {
    return (
      <div className={`${className} flex flex-col items-center justify-center py-12 text-center`}>
        <svg
          className="h-12 w-12 text-white/20"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={1.5}
            d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
          />
        </svg>
        <p className="mt-4 text-sm text-white/60">Click a node in the graph to see details</p>
      </div>
    );
  }

  const layerColors: Record<string, string> = {
    external: "bg-teal-700",
    presentation: "bg-indigo-700",
    core: "bg-violet-700",
    business: "bg-blue-700",
    foundation: "bg-amber-700",
  };

  return (
    <div className={className}>
      {/* Header */}
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            {selected.emoji && <span className="text-lg">{selected.emoji}</span>}
            <h3 className="text-lg font-bold">{selected.name}</h3>
          </div>
          <div className="mt-1 flex items-center gap-2">
            <span
              className={`inline-block h-2 w-2 rounded-full ${layerColors[selected.layer] || "bg-gray-600"}`}
            />
            <span className="text-xs text-white/60 capitalize">{selected.layer} layer</span>
            {selected.port && <span className="text-xs text-white/60">• Port {selected.port}</span>}
          </div>
        </div>
        <button
          type="button"
          onClick={onClear}
          className="rounded-lg border border-white/10 bg-white/5 p-1.5 transition-colors hover:bg-white/10"
          title="Clear selection"
        >
          <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={2}
              d="M6 18L18 6M6 6l12 12"
            />
          </svg>
        </button>
      </div>

      {/* Description */}
      {selected.description && <p className="mt-3 text-sm text-white/70">{selected.description}</p>}

      {/* Responsibilities */}
      {selected.responsibilities.length > 0 && (
        <div className="mt-4">
          <h4 className="text-xs font-semibold uppercase tracking-wide text-white/60">
            Responsibilities
          </h4>
          <ul className="mt-2 space-y-1">
            {selected.responsibilities.map((r, i) => (
              <li key={i} className="flex items-start gap-2 text-sm text-white/80">
                <span className="mt-1.5 h-1.5 w-1.5 flex-shrink-0 rounded-full bg-indigo-400" />
                {r}
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* Endpoints */}
      {selected.endpoints.length > 0 && (
        <div className="mt-4">
          <h4 className="text-xs font-semibold uppercase tracking-wide text-white/60">Endpoints</h4>
          <ul className="mt-2 space-y-1">
            {selected.endpoints.map((e, i) => (
              <li key={i} className="text-sm font-mono text-white/70">
                {e}
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* Dependencies */}
      {selected.dependencies.length > 0 && (
        <div className="mt-4">
          <h4 className="text-xs font-semibold uppercase tracking-wide text-white/60">
            Dependencies ({selected.dependencies.length})
          </h4>
          <div className="mt-2 flex flex-wrap gap-2">
            {selected.dependencies.map((d) => (
              <button
                key={d}
                type="button"
                onClick={() => onSelectByName(d)}
                className="rounded-lg bg-white/5 px-2 py-1 text-xs text-white/80 transition-colors hover:bg-white/10"
              >
                {d}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Dependents */}
      {selected.dependents.length > 0 && (
        <div className="mt-4">
          <h4 className="text-xs font-semibold uppercase tracking-wide text-white/60">
            Dependents ({selected.dependents.length})
          </h4>
          <div className="mt-2 flex flex-wrap gap-2">
            {selected.dependents.map((d) => (
              <button
                key={d}
                type="button"
                onClick={() => onSelectByName(d)}
                className="rounded-lg bg-white/5 px-2 py-1 text-xs text-white/80 transition-colors hover:bg-white/10"
              >
                {d}
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

import { useState, useEffect } from "react";
import type { ArchitectureGraphData, ArchitectureNode } from "../../lib/types";

export function ArchitectureTreeView() {
  const [data, setData] = useState<ArchitectureGraphData | null>(null);
  const [expandedDistricts, setExpandedDistricts] = useState<Set<string>>(new Set());
  const [selectedNode, setSelectedNode] = useState<ArchitectureNode | null>(null);
  const [filter, setFilter] = useState("");

  useEffect(() => {
    fetch("/content/architecture-park.v1.json")
      .then((res) => res.json())
      .then(setData);
  }, []);

  if (!data) {
    return <div className="p-4 text-white/60">Loading architecture data...</div>;
  }

  const toggleDistrict = (id: string) => {
    const next = new Set(expandedDistricts);
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    setExpandedDistricts(next);
  };

  const filteredNodes = filter
    ? data.nodes.filter((n) => n.name.toLowerCase().includes(filter.toLowerCase()))
    : data.nodes;

  const nodesByDistrict = data.districts.map((d) => ({
    district: d,
    nodes: filteredNodes.filter((n) => n.district === d.id),
  }));

  const kindColors: Record<string, string> = {
    service: "bg-indigo-600",
    agent: "bg-purple-600",
    db: "bg-amber-600",
    foundation: "bg-teal-600",
    external: "bg-gray-600",
    client: "bg-blue-600",
  };

  return (
    <div className="space-y-4">
      {/* Search */}
      <input
        type="text"
        placeholder="Filter nodes..."
        value={filter}
        onChange={(e) => setFilter(e.target.value)}
        className="w-full rounded-xl border border-white/15 bg-black/30 px-4 py-2 text-sm text-white placeholder-white/50 focus:border-indigo-500/50 focus:outline-none focus:ring-2 focus:ring-indigo-500/50"
      />

      <div className="grid gap-4 lg:grid-cols-2">
        {/* Tree View */}
        <div className="space-y-2">
          {nodesByDistrict.map(({ district, nodes }) => (
            <div key={district.id} className="rounded-xl border border-white/10 bg-white/5">
              <button
                type="button"
                onClick={() => toggleDistrict(district.id)}
                className="flex w-full items-center justify-between p-3 text-left hover:bg-white/5"
              >
                <span className="font-semibold">{district.name}</span>
                <span className="text-xs text-white/60">{nodes.length} nodes</span>
              </button>
              {expandedDistricts.has(district.id) && (
                <div className="border-t border-white/10 p-2">
                  {nodes.map((node) => (
                    <button
                      key={node.id}
                      type="button"
                      onClick={() => setSelectedNode(node)}
                      className={`flex w-full items-center gap-2 rounded-lg p-2 text-left text-sm transition-colors hover:bg-white/5 ${
                        selectedNode?.id === node.id ? "bg-white/10" : ""
                      }`}
                    >
                      <span
                        className={`h-2 w-2 rounded-full ${kindColors[node.kind] || "bg-gray-500"}`}
                      />
                      <span>{node.emoji}</span>
                      <span>{node.name}</span>
                    </button>
                  ))}
                </div>
              )}
            </div>
          ))}
        </div>

        {/* Details Panel */}
        <div className="rounded-xl border border-white/10 bg-white/5 p-4">
          {selectedNode ? (
            <div className="space-y-4">
              <div>
                <div className="flex items-center gap-2">
                  <span className="text-xl">{selectedNode.emoji}</span>
                  <h3 className="text-lg font-bold">{selectedNode.name}</h3>
                </div>
                <div className="mt-1 flex items-center gap-2">
                  <span
                    className={`h-2 w-2 rounded-full ${kindColors[selectedNode.kind] || "bg-gray-500"}`}
                  />
                  <span className="text-xs text-white/60 capitalize">{selectedNode.kind}</span>
                </div>
              </div>

              {selectedNode.description && (
                <p className="text-sm text-white/70">{selectedNode.description}</p>
              )}

              {selectedNode.responsibilities.length > 0 && (
                <div>
                  <h4 className="text-xs font-semibold uppercase tracking-wide text-white/60">
                    Responsibilities
                  </h4>
                  <ul className="mt-2 space-y-1">
                    {selectedNode.responsibilities.map((r, i) => (
                      <li key={i} className="flex items-start gap-2 text-sm text-white/80">
                        <span className="mt-1.5 h-1.5 w-1.5 flex-shrink-0 rounded-full bg-indigo-400" />
                        {r}
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {selectedNode.ports.length > 0 && (
                <div>
                  <h4 className="text-xs font-semibold uppercase tracking-wide text-white/60">
                    Ports
                  </h4>
                  <div className="mt-2 flex flex-wrap gap-2">
                    {selectedNode.ports.map((p, i) => (
                      <span key={i} className="rounded-lg bg-white/5 px-2 py-1 text-xs font-mono">
                        {p}
                      </span>
                    ))}
                  </div>
                </div>
              )}
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center py-12 text-center">
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
                  d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10"
                />
              </svg>
              <p className="mt-4 text-sm text-white/60">Select a node to see details</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

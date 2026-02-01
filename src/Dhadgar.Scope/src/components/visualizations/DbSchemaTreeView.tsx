import { useState, useEffect } from "react";
import type { DbSchemaCatalog, DbSchemaItem } from "../../lib/types";

export function DbSchemaTreeView() {
  const [data, setData] = useState<DbSchemaCatalog | null>(null);
  const [selectedService, setSelectedService] = useState<string>("");
  const [expandedKinds, setExpandedKinds] = useState<Set<string>>(new Set(["table"]));
  const [selectedItem, setSelectedItem] = useState<DbSchemaItem | null>(null);
  const [filter, setFilter] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetch("/content/db-schemas.v1.json")
      .then((res) => {
        if (!res.ok) {
          throw new Error(`HTTP error ${res.status}`);
        }
        return res.json();
      })
      .then((d: DbSchemaCatalog) => {
        setData(d);
        if (d.services.length > 0) {
          setSelectedService(d.services[0].key);
        }
      })
      .catch((err) => {
        console.error("Failed to load schema data:", err);
        setError("Failed to load schema data. Please try again later.");
      });
  }, []);

  if (error) {
    return <div className="p-4 text-red-400">{error}</div>;
  }

  if (!data) {
    return <div className="p-4 text-white/60">Loading schema data...</div>;
  }

  const currentService = data.services.find((s) => s.key === selectedService);

  const toggleKind = (kind: string) => {
    const next = new Set(expandedKinds);
    if (next.has(kind)) {
      next.delete(kind);
    } else {
      next.add(kind);
    }
    setExpandedKinds(next);
  };

  const kindLabels: Record<string, string> = {
    table: "Tables",
    view: "Views",
    function: "Functions",
    enum: "Enums",
    type: "Types",
  };

  const filteredItems =
    currentService?.items.filter((item) => item.id.toLowerCase().includes(filter.toLowerCase())) ||
    [];

  const itemsByKind = ["table", "view", "function", "enum", "type"]
    .map((kind) => ({
      kind,
      items: filteredItems.filter((i) => i.kind === kind),
    }))
    .filter((g) => g.items.length > 0);

  return (
    <div className="space-y-4">
      {/* Service selector and filter */}
      <div className="flex flex-col gap-3 sm:flex-row">
        <select
          value={selectedService}
          onChange={(e) => {
            setSelectedService(e.target.value);
            setSelectedItem(null);
          }}
          className="flex-1 rounded-xl border border-white/15 bg-black/30 px-4 py-2 text-sm text-white focus:border-indigo-500/50 focus:outline-none focus:ring-2 focus:ring-indigo-500/50"
        >
          {data.services.map((s) => (
            <option key={s.key} value={s.key}>
              {s.name}
            </option>
          ))}
        </select>

        <input
          type="text"
          placeholder="Filter items..."
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          className="flex-1 rounded-xl border border-white/15 bg-black/30 px-4 py-2 text-sm text-white placeholder-white/50 focus:border-indigo-500/50 focus:outline-none focus:ring-2 focus:ring-indigo-500/50"
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        {/* Tree View */}
        <div className="space-y-2">
          {itemsByKind.map(({ kind, items }) => (
            <div key={kind} className="rounded-xl border border-white/10 bg-white/5">
              <button
                type="button"
                onClick={() => toggleKind(kind)}
                className="flex w-full items-center justify-between p-3 text-left hover:bg-white/5"
              >
                <span className="font-semibold">{kindLabels[kind] || kind}</span>
                <span className="text-xs text-white/60">{items.length}</span>
              </button>
              {expandedKinds.has(kind) && (
                <div className="border-t border-white/10 p-2">
                  {items.map((item) => (
                    <button
                      key={item.id}
                      type="button"
                      onClick={() => setSelectedItem(item)}
                      className={`flex w-full items-center gap-2 rounded-lg p-2 text-left text-sm transition-colors hover:bg-white/5 ${
                        selectedItem?.id === item.id ? "bg-white/10" : ""
                      }`}
                    >
                      <span className="font-mono text-white/80">{item.id.split(".").pop()}</span>
                    </button>
                  ))}
                </div>
              )}
            </div>
          ))}
        </div>

        {/* Details Panel */}
        <div className="rounded-xl border border-white/10 bg-white/5 p-4">
          {selectedItem ? (
            <div className="space-y-4">
              <div>
                <h3 className="font-mono text-lg font-bold">{selectedItem.id}</h3>
                <span className="text-xs text-white/60 capitalize">{selectedItem.kind}</span>
              </div>

              {selectedItem.details.length > 0 && (
                <div>
                  <h4 className="text-xs font-semibold uppercase tracking-wide text-white/60">
                    {selectedItem.kind === "table" ? "Columns" : "Details"}
                  </h4>
                  <ul className="mt-2 space-y-1">
                    {selectedItem.details.map((d, i) => (
                      <li key={i} className="font-mono text-sm text-white/80">
                        {d}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <svg
                aria-hidden="true"
                className="h-12 w-12 text-white/20"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={1.5}
                  d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4"
                />
              </svg>
              <p className="mt-4 text-sm text-white/60">Select an item to see details</p>
            </div>
          )}
        </div>
      </div>

      {/* Notes */}
      {data.notes.length > 0 && (
        <div className="rounded-xl border border-white/10 bg-white/5 p-4">
          <h4 className="text-xs font-semibold uppercase tracking-wide text-white/60">Notes</h4>
          <ul className="mt-2 space-y-1">
            {data.notes.map((note, i) => (
              <li key={i} className="text-sm text-white/70">
                {note}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

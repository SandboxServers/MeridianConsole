import { useState, useEffect } from "react";
import type { CommMatrixData } from "../../lib/types";

export function CommMatrixMobile() {
  const [data, setData] = useState<CommMatrixData | null>(null);
  const [selectedSource, setSelectedSource] = useState<string>("");
  const [protocolFilter, setProtocolFilter] = useState<string>("");

  useEffect(() => {
    fetch("/content/comm-matrix.v1.json")
      .then((res) => res.json())
      .then((d: CommMatrixData) => {
        setData(d);
        if (d.rows.length > 0) {
          setSelectedSource(d.rows[0].from);
        }
      });
  }, []);

  if (!data) {
    return <div className="p-4 text-white/60">Loading communication matrix...</div>;
  }

  const currentRow = data.rows.find((r) => r.from === selectedSource);
  const protocols = ["HTTP", "WSS", "AMQP", "DB", "DNS", "OTHER"];

  const filteredCells =
    currentRow?.cells.filter((c) => {
      if (c.value === "-") return false;
      if (protocolFilter && c.value !== protocolFilter) return false;
      return true;
    }) || [];

  const protocolColors: Record<string, string> = {
    HTTP: "bg-blue-600",
    WSS: "bg-purple-600",
    AMQP: "bg-amber-600",
    DB: "bg-teal-600",
    DNS: "bg-gray-600",
    OTHER: "bg-pink-600",
  };

  return (
    <div className="space-y-4">
      {/* Source selector */}
      <select
        value={selectedSource}
        onChange={(e) => setSelectedSource(e.target.value)}
        className="w-full rounded-xl border border-white/15 bg-black/30 px-4 py-2 text-sm text-white focus:border-indigo-500/50 focus:outline-none focus:ring-2 focus:ring-indigo-500/50"
      >
        {data.rows.map((r) => (
          <option key={r.from} value={r.from}>
            {r.from}
          </option>
        ))}
      </select>

      {/* Protocol filter */}
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          onClick={() => setProtocolFilter("")}
          className={`rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors ${
            protocolFilter === ""
              ? "bg-white/20 text-white"
              : "bg-white/5 text-white/70 hover:bg-white/10"
          }`}
        >
          All
        </button>
        {protocols.map((p) => (
          <button
            key={p}
            type="button"
            onClick={() => setProtocolFilter(p)}
            className={`rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors ${
              protocolFilter === p
                ? "bg-white/20 text-white"
                : "bg-white/5 text-white/70 hover:bg-white/10"
            }`}
          >
            {p}
          </button>
        ))}
      </div>

      {/* Connections */}
      <div className="space-y-2">
        {filteredCells.length > 0 ? (
          filteredCells.map((cell) => (
            <div
              key={cell.to}
              className="flex items-center justify-between rounded-xl border border-white/10 bg-white/5 p-3"
            >
              <span className="text-sm">{cell.to}</span>
              <span
                className={`rounded-lg px-2 py-1 text-xs font-semibold ${protocolColors[cell.value] || "bg-gray-600"}`}
              >
                {cell.value}
              </span>
            </div>
          ))
        ) : (
          <div className="rounded-xl border border-white/10 bg-white/5 p-4 text-center text-sm text-white/60">
            No outbound connections{protocolFilter ? ` via ${protocolFilter}` : ""}
          </div>
        )}
      </div>

      {/* Summary */}
      {currentRow && (
        <div className="rounded-xl border border-white/10 bg-white/5 p-3 text-xs text-white/60">
          {selectedSource} has {currentRow.cells.filter((c) => c.value !== "-").length} outbound
          connections
        </div>
      )}
    </div>
  );
}

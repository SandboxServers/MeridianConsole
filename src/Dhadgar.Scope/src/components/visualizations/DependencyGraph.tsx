import { useState, useEffect, useRef, useCallback } from "react";
import type { Core, EventObject } from "cytoscape";
import type { DependencyGraphData, DependencyNode } from "../../lib/types";
import { DependencyDetailsPanel } from "./DependencyDetailsPanel";
import { TextField } from "../ui/TextField";
import { Chip } from "../ui/Chip";
import { Button } from "../ui/Button";
import { Alert } from "../ui/Alert";

declare const cytoscape: (options: Record<string, unknown>) => Core;

const LAYER_ORDER = ["external", "presentation", "core", "business", "foundation"];

const LAYER_COLORS: Record<string, string> = {
  external: "#0F766E",
  presentation: "#4338CA",
  core: "#6D28D9",
  business: "#1D4ED8",
  foundation: "#B45309",
};

export function DependencyGraph() {
  const [data, setData] = useState<DependencyGraphData | null>(null);
  const [selected, setSelected] = useState<DependencyNode | null>(null);
  const [query, setQuery] = useState("");
  const [mobileView, setMobileView] = useState<"graph" | "details">("graph");
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [initError, setInitError] = useState(false);

  const containerRef = useRef<HTMLDivElement>(null);
  const cyRef = useRef<Core | null>(null);
  const initialPositionsRef = useRef<Record<string, { x: number; y: number }>>({});

  // Load data
  useEffect(() => {
    fetch("/content/dependencies.json")
      .then((res) => res.json())
      .then((d: DependencyGraphData) => setData(d))
      .catch(() => setInitError(true));
  }, []);

  // Build elements for Cytoscape
  const buildElements = useCallback((graphData: DependencyGraphData) => {
    const elements: Array<{ data: Record<string, string | undefined> }> = [];

    graphData.nodes.forEach((n) => {
      const label = `${n.emoji ? n.emoji + " " : ""}${n.name}`;
      elements.push({
        data: {
          id: n.id,
          name: n.name,
          label,
          layer: n.layer,
        },
      });
    });

    graphData.edges.forEach((e) => {
      elements.push({
        data: {
          id: e.id,
          source: e.source,
          target: e.target,
          relationship: e.relationship || "depends_on",
        },
      });
    });

    return elements;
  }, []);

  // Apply preset positions based on layers
  const applyPresetPositions = useCallback((nodes: DependencyNode[], cy: Core) => {
    const grouped: Record<string, DependencyNode[]> = {};
    nodes.forEach((n) => {
      const key = n.layer || "business";
      grouped[key] = grouped[key] || [];
      grouped[key].push(n);
    });

    const container = cy.container();
    const w = container?.clientWidth || 1100;
    const bandHeight = 170;
    const minGap = 160;

    LAYER_ORDER.forEach((layer, li) => {
      const arr = (grouped[layer] || []).slice().sort((a, b) => a.name.localeCompare(b.name));
      const count = Math.max(arr.length, 1);
      const gap = Math.max(minGap, Math.floor(w / (count + 1)));

      arr.forEach((n, idx) => {
        const x = (idx + 1) * gap;
        const y = (li + 1) * bandHeight;
        const node = cy.getElementById(n.id);
        if (node.length) node.position({ x, y });
      });
    });
  }, []);

  // Initialize Cytoscape
  useEffect(() => {
    if (!data || !containerRef.current || typeof cytoscape === "undefined") return;

    try {
      const elements = buildElements(data);

      const cy = cytoscape({
        container: containerRef.current,
        elements,
        layout: { name: "preset" },
        wheelSensitivity: 0.2,
        minZoom: 0.2,
        maxZoom: 2.0,
        selectionType: "single",
        style: [
          {
            selector: "node",
            style: {
              shape: "round-rectangle",
              width: 170,
              height: 44,
              "background-color": "#111827",
              "border-width": 1,
              "border-color": "#334155",
              label: "data(label)",
              "text-wrap": "ellipsis",
              "text-max-width": 160,
              "font-size": 12,
              color: "#E5E7EB",
              "text-outline-width": 0,
            },
          },
          ...LAYER_ORDER.map((layer) => ({
            selector: `node[layer = "${layer}"]`,
            style: { "background-color": LAYER_COLORS[layer] },
          })),
          {
            selector: "edge",
            style: {
              "curve-style": "bezier",
              width: 2,
              "line-color": "#94A3B8",
              "target-arrow-color": "#94A3B8",
              "target-arrow-shape": "triangle",
              "arrow-scale": 0.9,
              opacity: 0.85,
            },
          },
          {
            selector: ".dh-selected",
            style: {
              "border-width": 3,
              "border-color": "#F59E0B",
            },
          },
          {
            selector: ".dh-dim",
            style: {
              opacity: 0.15,
            },
          },
        ],
      });

      cyRef.current = cy;
      applyPresetPositions(data.nodes, cy);

      // Store initial positions
      data.nodes.forEach((n) => {
        const el = cy.getElementById(n.id);
        if (el.length) {
          initialPositionsRef.current[n.id] = el.position();
        }
      });

      cy.fit(undefined, 30);

      // Node click handler
      cy.on("tap", "node", (evt: EventObject) => {
        const nodeId = evt.target.id();
        const node = data.nodes.find((n) => n.id === nodeId);
        if (node) {
          setSelected(node);
          setMobileView("details");
          setDrawerOpen(true);

          cy.elements().removeClass("dh-selected");
          evt.target.addClass("dh-selected");
          cy.animate({ center: { eles: evt.target } }, { duration: 200 });
        }
      });

      // Background click handler
      cy.on("tap", (evt: EventObject) => {
        if (evt.target === cy) {
          setSelected(null);
          setMobileView("graph");
          setDrawerOpen(false);
          cy.elements().removeClass("dh-selected");
          cy.elements().removeClass("dh-dim");
        }
      });

      // Resize handler
      const handleResize = () => {
        if (cyRef.current) {
          cyRef.current.resize();
          applyPresetPositions(data.nodes, cyRef.current);
          cyRef.current.fit(undefined, 30);
        }
      };
      window.addEventListener("resize", handleResize);

      return () => {
        window.removeEventListener("resize", handleResize);
        cy.destroy();
        cyRef.current = null;
      };
    } catch {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- error handler for initialization failure
      setInitError(true);
    }
  }, [data, buildElements, applyPresetPositions]);

  // Handle search
  useEffect(() => {
    const cy = cyRef.current;
    if (!cy) return;

    const q = query.toLowerCase().trim();
    if (!q) {
      cy.elements().removeClass("dh-dim");
      return;
    }

    const matches = cy.nodes().filter((n) => (n.data("name") || "").toLowerCase().includes(q));
    cy.elements().addClass("dh-dim");
    matches.removeClass("dh-dim");
    matches.connectedEdges().removeClass("dh-dim");
    matches.connectedEdges().connectedNodes().removeClass("dh-dim");
  }, [query]);

  const handleFit = () => {
    cyRef.current?.fit(undefined, 30);
  };

  const handleReset = () => {
    const cy = cyRef.current;
    if (!cy) return;

    cy.elements().removeClass("dh-dim");
    Object.entries(initialPositionsRef.current).forEach(([id, pos]) => {
      const el = cy.getElementById(id);
      if (el.length) el.position(pos);
    });
    cy.fit(undefined, 30);
  };

  const handleSelectByName = (name: string) => {
    if (!data) return;
    const node = data.nodes.find((n) => n.name.toLowerCase() === name.toLowerCase());
    if (!node || !cyRef.current) return;

    setSelected(node);
    const el = cyRef.current.getElementById(node.id);
    if (el.length) {
      cyRef.current.elements().removeClass("dh-selected");
      el.addClass("dh-selected");
      cyRef.current.animate({ center: { eles: el } }, { duration: 200 });
    }
  };

  const handleClear = () => {
    setSelected(null);
    setMobileView("graph");
    setDrawerOpen(false);
    cyRef.current?.elements().removeClass("dh-selected");
    cyRef.current?.elements().removeClass("dh-dim");
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="rounded-2xl border border-white/10 bg-white/5 p-6">
        <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Interactive Dependency Map</h1>
            <p className="mt-2 text-white/75">
              Click a node to inspect its responsibilities, endpoints, and relationships. Use search
              to jump.
            </p>
          </div>

          <div className="w-full md:w-96">
            <TextField
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Try: Gateway, RabbitMQ, Nodes..."
              clearable
              onClear={() => setQuery("")}
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

        {/* Mobile tabs */}
        <div className="mt-4 flex items-center gap-2 md:hidden">
          <div className="flex w-full rounded-2xl border border-white/10 bg-white/5 p-1">
            <button
              type="button"
              onClick={() => setMobileView("graph")}
              className={`flex-1 rounded-xl px-4 py-2 text-sm font-semibold transition-colors ${
                mobileView === "graph" ? "bg-white/10 text-white" : "text-white/70"
              }`}
            >
              Graph
            </button>
            <button
              type="button"
              onClick={() => setMobileView("details")}
              className={`flex-1 rounded-xl px-4 py-2 text-sm font-semibold transition-colors ${
                mobileView === "details" ? "bg-white/10 text-white" : "text-white/70"
              }`}
            >
              Details
            </button>
          </div>
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        {/* Graph panel */}
        <div
          className={`lg:col-span-2 rounded-2xl border border-white/10 bg-white/5 p-3 ${mobileView === "details" ? "hidden md:block" : ""}`}
        >
          <div className="flex flex-wrap items-center justify-between gap-2 px-2 pb-3">
            <div className="flex flex-wrap gap-2">
              <Chip>Scroll / pinch to zoom</Chip>
              <Chip>Drag nodes</Chip>
              <Chip>Tap background to clear</Chip>
            </div>

            <div className="flex flex-wrap gap-2">
              <Button onClick={handleFit} size="small">
                Fit
              </Button>
              <Button onClick={handleReset} size="small">
                Reset
              </Button>
              <Button
                onClick={() => setDrawerOpen(!drawerOpen)}
                size="small"
                color="primary"
                className="hidden md:inline-flex lg:hidden"
              >
                Details
              </Button>
            </div>
          </div>

          <div
            ref={containerRef}
            id="dhadgar-dependency-graph"
            className="h-[70vh] w-full rounded-xl bg-black/30"
          />

          {initError && (
            <Alert severity="warning" className="mt-3" dense>
              Graph renderer failed to initialize. If you&apos;re behind a strict network, the
              Cytoscape CDN may be blocked. Check DevTools console for details.
            </Alert>
          )}
        </div>

        {/* Desktop details panel */}
        <div className="hidden lg:block">
          <DependencyDetailsPanel
            selected={selected}
            onSelectByName={handleSelectByName}
            onClear={handleClear}
            className="rounded-2xl border border-white/10 bg-white/5 p-6"
          />
        </div>
      </div>

      {/* Phone details panel (tabbed) */}
      <div className={`md:hidden ${mobileView === "details" ? "" : "hidden"}`}>
        <DependencyDetailsPanel
          selected={selected}
          onSelectByName={handleSelectByName}
          onClear={handleClear}
          className="rounded-2xl border border-white/10 bg-white/5 p-6"
        />
      </div>

      {/* Tablet drawer */}
      {drawerOpen && (
        <>
          <button
            type="button"
            aria-label="Close details panel"
            className="fixed inset-0 z-50 hidden cursor-default bg-black/60 md:block lg:hidden"
            onClick={() => setDrawerOpen(false)}
            onKeyDown={(e) => e.key === "Escape" && setDrawerOpen(false)}
          />
          <div className="fixed bottom-0 left-0 right-0 z-50 hidden max-h-[70vh] overflow-auto rounded-t-2xl border-t border-white/10 bg-slate-950/90 p-4 shadow-2xl backdrop-blur md:block lg:hidden">
            <div className="flex items-center justify-between">
              <span className="text-sm font-semibold text-white/80">Details</span>
              <button
                type="button"
                onClick={() => setDrawerOpen(false)}
                aria-label="Close details drawer"
                className="rounded-lg border border-white/10 bg-white/5 p-1.5 transition-colors hover:bg-white/10"
              >
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
                    d="M6 18L18 6M6 6l12 12"
                  />
                </svg>
              </button>
            </div>
            <div className="mt-4">
              <DependencyDetailsPanel
                selected={selected}
                onSelectByName={handleSelectByName}
                onClear={handleClear}
                className="rounded-2xl border border-white/10 bg-white/5 p-4"
              />
            </div>
          </div>
        </>
      )}
    </div>
  );
}

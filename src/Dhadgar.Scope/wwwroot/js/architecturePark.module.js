// Diagnostics: Scope visuals (safe to leave in prod; minimal output)
(function () {
  if (window.__scopeVisualDiag) return;
  window.__scopeVisualDiag = {
    logs: [],
    log(level, area, message, extra) {
      const entry = { t: new Date().toISOString(), level, area, message, extra };
      this.logs.push(entry);
      try {
        const fn = level === 'error' ? console.error : (level === 'warn' ? console.warn : console.log);
        fn.call(console, `[ScopeVisual:${area}] ${message}`, extra ?? '');
      } catch { }
    }
  };
})();
const __diag = window.__scopeVisualDiag;

function __renderFatal(hostEl, title, detail) {
  try {
    if (!hostEl) return;
    hostEl.innerHTML = '';
    const wrap = document.createElement('div');
    wrap.style.cssText = 'position:relative;width:100%;height:100%;display:flex;align-items:center;justify-content:center;padding:16px;';
    const card = document.createElement('div');
    card.style.cssText = 'max-width:680px;width:100%;border:1px solid rgba(255,255,255,.12);border-radius:16px;background:rgba(15,23,42,.55);backdrop-filter:blur(12px);padding:16px;';
    const h = document.createElement('div');
    h.style.cssText = 'font-weight:700;font-size:18px;margin-bottom:8px;color:#fff;';
    h.textContent = title || 'Visual failed to load';
    const p = document.createElement('div');
    p.style.cssText = 'font-size:14px;line-height:1.4;color:rgba(255,255,255,.85);white-space:pre-wrap;';
    p.textContent = detail || '';
    const hint = document.createElement('div');
    hint.style.cssText = 'margin-top:12px;font-size:12px;color:rgba(255,255,255,.7);';
    hint.textContent = 'Open DevTools → Console and look for [ScopeVisual:*] logs. Also check Network for blocked scripts (cytoscape).';
    card.appendChild(h); card.appendChild(p); card.appendChild(hint);
    wrap.appendChild(card);
    hostEl.appendChild(wrap);
  } catch { }
}
// Diagnostics: Scope visuals
window.__scopeVisual = window.__scopeVisual || { logs: [] };
function __svLog(level, msg, obj){ try { window.__scopeVisual.logs.push({ t: new Date().toISOString(), level, msg, obj }); } catch(e){}; (level==="error"?console.error:console.log)("[ScopeVisual]", msg, obj||""); }

// architecturePark.module.js
// Cytoscape-based "miniature build-out" explorer for the System Architecture diagram.
// Loaded via Blazor JS module import.

const _instances = new Map();

function _key(host) {
  // ElementReference is proxied; use the actual DOM element as key
  return host;
}

function _ensureCytoscape() {
  return (typeof window !== "undefined") ? window.cytoscape : undefined;
}
function _safeLower(s) { return (s ?? "").toString().toLowerCase(); }

function _renderFallback(host, message) {
  try {
    host.innerHTML = "";
    const wrap = document.createElement("div");
    wrap.style.height = "100%";
    wrap.style.width = "100%";
    wrap.style.display = "flex";
    wrap.style.alignItems = "center";
    wrap.style.justifyContent = "center";
    wrap.style.padding = "16px";
    wrap.style.borderRadius = "16px";
    wrap.style.background = "rgba(15, 23, 42, 0.35)";
    wrap.style.border = "1px solid rgba(148,163,184,0.18)";
    wrap.style.color = "rgba(226,232,240,0.9)";
    wrap.style.fontFamily = "ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, Helvetica, Arial";
    wrap.innerText = message;
    host.appendChild(wrap);
  } catch {}
}

function _buildElements(data) {
  const districts = data.districts ?? [];
  const nodes = data.nodes ?? [];
  const edges = data.edges ?? [];

  const elements = [];

  // District "pads" as locked nodes (background blocks)
  for (const d of districts) {
    elements.push({
      data: {
        id: `district:${d.id}`,
        label: d.name,
        districtId: d.id,
        isDistrict: true
      },
      position: { x: d.center?.x ?? 0, y: d.center?.y ?? 0 }
    });
  }

  // Service nodes
  for (const n of nodes) {
    const districtId = n.districtId ?? n.district ?? "";
    elements.push({
      data: {
        id: n.id,
        label: n.label ?? n.name ?? n.id,
        districtId,
        kind: n.kind ?? "service",
        endpoints: n.endpoints ?? [],
        responsibilities: n.responsibilities ?? []
      },
      position: { x: n.position?.x ?? 0, y: n.position?.y ?? 0 }
    });
  }

  // Relationship edges
  for (const e of edges) {
    elements.push({
      data: {
        id: e.id,
        source: e.from,
        target: e.to,
        kind: e.kind ?? "OTHER",
        label: e.label ?? ""
      }
    });
  }

  return elements;
}

function _styles() {
  return [
    // Default
    {
      selector: "node",
      style: {
        "font-family": "ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, Helvetica, Arial",
        "font-size": 10,
        "color": "#E5E7EB",
        "text-wrap": "wrap",
        "text-max-width": 140,
        "text-valign": "center",
        "text-halign": "center",
        "background-color": "#111827",
        "border-color": "rgba(148,163,184,0.30)",
        "border-width": 1,
        "shape": "round-rectangle",
        "padding": 10,
        "width": "label",
        "height": "label"
      }
    },

    // District pads
    {
      selector: 'node[isDistrict]',
      style: {
        "background-color": "rgba(59,130,246,0.06)",
        "border-color": "rgba(99,102,241,0.35)",
        "border-width": 1,
        "width": 520,
        "height": 320,
        "shape": "round-rectangle",
        "label": "data(label)",
        "text-valign": "top",
        "text-halign": "left",
        "text-margin-x": 14,
        "text-margin-y": 12,
        "font-size": 12,
        "color": "rgba(226,232,240,0.75)",
        "z-index": 0
      }
    },

    // Service nodes above districts
    {
      selector: "node[!isDistrict]",
      style: {
        "label": "data(label)",
        "background-color": "rgba(17,24,39,0.92)",
        "border-color": "rgba(99,102,241,0.45)",
        "border-width": 1,
        "z-index": 10
      }
    },

    // Selected nodes
    {
      selector: "node:selected",
      style: {
        "border-width": 2,
        "border-color": "rgba(167,139,250,0.95)",
        "background-color": "rgba(30,41,59,0.95)"
      }
    },

    // Edges base
    {
      selector: "edge",
      style: {
        "curve-style": "bezier",
        "line-color": "rgba(148,163,184,0.40)",
        "target-arrow-color": "rgba(148,163,184,0.55)",
        "target-arrow-shape": "triangle",
        "arrow-scale": 0.9,
        "width": 1,
        "opacity": 0.95
      }
    },

    // Edge kind colors (kept subtle on dark theme)
    { selector: 'edge[kind = "HTTP"]', style: { "line-color": "rgba(99,102,241,0.85)", "target-arrow-color": "rgba(99,102,241,0.85)" } },
    { selector: 'edge[kind = "WSS"]',  style: { "line-color": "rgba(34,197,94,0.80)",  "target-arrow-color": "rgba(34,197,94,0.80)" } },
    { selector: 'edge[kind = "AMQP"]', style: { "line-color": "rgba(168,85,247,0.80)", "target-arrow-color": "rgba(168,85,247,0.80)" } },
    { selector: 'edge[kind = "DB"]',   style: { "line-color": "rgba(245,158,11,0.80)", "target-arrow-color": "rgba(245,158,11,0.80)" } },
    { selector: 'edge[kind = "DNS"]',  style: { "line-color": "rgba(56,189,248,0.80)", "target-arrow-color": "rgba(56,189,248,0.80)" } },
    { selector: 'edge[kind = "OTHER"]',style: { "line-color": "rgba(148,163,184,0.55)", "target-arrow-color": "rgba(148,163,184,0.55)" } },

    // Hidden elements
    { selector: ".hidden", style: { "display": "none" } },

    // Tour highlights
    {
      selector: ".tourEdge",
      style: {
        "width": 3,
        "opacity": 1.0,
        "line-color": "rgba(167,139,250,0.95)",
        "target-arrow-color": "rgba(167,139,250,0.95)"
      }
    },
    {
      selector: ".tourNode",
      style: {
        "border-width": 3,
        "border-color": "rgba(167,139,250,0.95)"
      }
    },
    {
      selector: ".tourDistrict",
      style: {
        "background-color": "rgba(167,139,250,0.08)",
        "border-color": "rgba(167,139,250,0.45)"
      }
    }
  ];
}

function _applyExplode(state, explodePct) {
  const explode = Math.max(0, Math.min(1, explodePct ?? 0));
  state.explode = explode;

  const cy = state.cy;

  cy.batch(() => {
    cy.nodes("node[!isDistrict]").forEach(n => {
      const base = state.basePositions.get(n.id());
      if (!base) return;

      const districtId = n.data("districtId");
      const center = state.districtCenters.get(districtId) ?? { x: 0, y: 0 };

      const vx = base.x - center.x;
      const vy = base.y - center.y;

      // Scale factor 1..2.75 depending on explode
      const scale = 1 + (explode * 1.75);

      n.position({
        x: center.x + (vx * scale),
        y: center.y + (vy * scale)
      });
    });
  });
}

function _applyKinds(state, kinds) {
  const set = new Set((kinds ?? []).map(k => (k ?? "").toString().toUpperCase()));
  const cy = state.cy;

  cy.batch(() => {
    cy.edges().forEach(e => {
      const k = (e.data("kind") ?? "OTHER").toString().toUpperCase();
      if (set.size === 0 || set.has(k)) e.removeClass("hidden");
      else e.addClass("hidden");
    });
  });
}

function _clearTourVisuals(state) {
  const cy = state.cy;
  cy.batch(() => {
    cy.edges().removeClass("tourEdge");
    cy.nodes().removeClass("tourNode").removeClass("tourDistrict");
  });
}

function _applyTour(state, tourId, stepIndex) {
  _clearTourVisuals(state);

  if (!tourId) return;
  const tours = state.data?.tours ?? [];
  const tour = tours.find(t => t.id === tourId);
  if (!tour) return;

  const steps = tour.steps ?? [];
  const idx = Math.max(0, Math.min(steps.length - 1, stepIndex ?? 0));
  const step = steps[idx];
  if (!step) return;

  const cy = state.cy;

  cy.batch(() => {
    const focusNodeId = step.focusNodeId;
    if (focusNodeId) {
      const n = cy.getElementById(focusNodeId);
      if (n && n.length) n.addClass("tourNode");
    }

    for (const eid of (step.highlightEdges ?? [])) {
      const e = cy.getElementById(eid);
      if (e && e.length) e.addClass("tourEdge");
    }

    for (const did of (step.highlightDistricts ?? [])) {
      const d = cy.getElementById(`district:${did}`);
      if (d && d.length) d.addClass("tourDistrict");
    }
  });

  // bring into view
  const focusNodeId = step.focusNodeId;
  if (focusNodeId) {
    const n = cy.getElementById(focusNodeId);
    if (n && n.length) {
      cy.animate({ center: { eles: n }, duration: 300 });
    }
  }
}

export function init(hostElement, data, options) {
  __diag.log('info','arch','init called', { hasHost: !!hostElement, cytoscapeType: typeof window.cytoscape });
  if (!hostElement) { __diag.log('error','arch','host element missing'); return; }
  if (typeof window.cytoscape !== 'function') {
    __diag.log('error','arch','Cytoscape not loaded', { cytoscapeType: typeof window.cytoscape });
    __renderFatal(hostElement, 'Cytoscape not loaded', `The cytoscape script did not load.\nCheck DevTools → Network for cytoscape.min.js and any CSP/blocked requests.\nIf you are offline or unpkg is blocked, vendor cytoscape into wwwroot and reference it locally.`);
    return;
  }

  const cytoscape = _ensureCytoscape();
  const host = hostElement;

  if (!cytoscape) { _renderFallback(host, "Interactive graph failed to load (Cytoscape missing). If you're offline or blocked from unpkg, the explorer won't render."); return; }

  if (!host) throw new Error("init(hostElement, ...) requires a DOM element container.");

  // Dispose if already attached
  dispose(host);

  const elements = _buildElements(data);

  const cy = cytoscape({
    container: host,
    elements,
    style: _styles(),
    layout: { name: "preset" },
    wheelSensitivity: 0.14,
    minZoom: 0.08,
    maxZoom: 3.2,
    boxSelectionEnabled: false,
    selectionType: "single",
    userPanningEnabled: true,
    userZoomingEnabled: true,
    autounselectify: false
  });

  // District pads should not be draggable/selectable
  cy.nodes('node[isDistrict]').forEach(n => {
    n.lock();
    n.unselectify();
    n.grabify(false);
  });

  const state = {
    cy,
    data,
    options: options ?? {},
    explode: 0,
    basePositions: new Map(),
    districtCenters: new Map()
  };

  // Capture base positions & district centers
  for (const n of (data.nodes ?? [])) {
    if (!n?.id) continue;
    const pos = n.position ?? { x: 0, y: 0 };
    state.basePositions.set(n.id, { x: pos.x ?? 0, y: pos.y ?? 0 });
  }
  for (const d of (data.districts ?? [])) {
    state.districtCenters.set(d.id, { x: d.center?.x ?? 0, y: d.center?.y ?? 0 });
  }

  const dotNetRef = state.options?.dotNetRef;
  const onSelect = state.options?.onNodeSelectedMethod ?? "OnNodeSelected";

  // Selection -> notify .NET
  cy.on("tap", "node[!isDistrict]", async (evt) => {
    const id = evt?.target?.id?.() ?? null;
    if (!id) return;

    try { evt.target.select(); } catch {}
    if (dotNetRef && typeof dotNetRef.invokeMethodAsync === "function") {
      try { await dotNetRef.invokeMethodAsync(onSelect, id); } catch {}
    }
  });

  // Tap background clears selection
  cy.on("tap", (evt) => {
    if (evt.target !== cy) return;
    cy.elements().unselect();
  });

  _instances.set(_key(host), state);

  // initial fit with padding
  try { cy.fit(cy.elements("node[!isDistrict]"), 40); } catch {}
}

export function setExplode(hostElement, explodePct) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  _applyExplode(state, explodePct);
}

export function setKinds(hostElement, kinds) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  _applyKinds(state, kinds);
}

export function search(hostElement, query) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;

  const q = _safeLower(query).trim();
  if (!q) return;

  const cy = state.cy;

  // find node by label/id/endpoint
  const match = cy.nodes("node[!isDistrict]").filter(n => {
    const id = _safeLower(n.id());
    const label = _safeLower(n.data("label"));
    const districtId = _safeLower(n.data("districtId"));
    const endpoints = (n.data("endpoints") ?? []).map(e => _safeLower(e)).join(" ");
    return id.includes(q) || label.includes(q) || districtId.includes(q) || endpoints.includes(q);
  }).first();

  if (match && match.nonempty && match.length) {
    cy.elements().unselect();
    match.select();
    cy.animate({ center: { eles: match }, duration: 250 });
  }
}

export function setTour(hostElement, tourId, stepIndex) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  _applyTour(state, tourId, stepIndex);
}

export function clearTour(hostElement) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  _clearTourVisuals(state);
}

export function fit(hostElement) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  try { state.cy.fit(state.cy.elements("node[!isDistrict]"), 40); } catch {}
}

export function reset(hostElement) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;

  _clearTourVisuals(state);

  const cy = state.cy;
  cy.batch(() => {
    cy.elements().unselect();
    cy.edges().removeClass("hidden");
    // restore base positions before re-applying explode
    cy.nodes("node[!isDistrict]").forEach(n => {
      const base = state.basePositions.get(n.id());
      if (base) n.position({ x: base.x, y: base.y });
    });
  });

  _applyExplode(state, state.explode ?? 0);
  try { cy.fit(cy.elements("node[!isDistrict]"), 40); } catch {}
}

export function clearSelection(hostElement) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  try { state.cy.elements().unselect(); } catch {}
}

export function dispose(hostElement) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  try { state.cy.destroy(); } catch {}
  _instances.delete(_key(hostElement));
}

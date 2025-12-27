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

// dbSchemaGraph.module.js
// Cytoscape-based explorer for the Database Schemas "dot model" graph.
// Loaded via Blazor JS module import.

const _instances = new Map();

function _key(host) { return host; }

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

function _styles() {
  return [
    {
      selector: "node",
      style: {
        "font-family": "ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, Helvetica, Arial",
        "font-size": 10,
        "color": "#E5E7EB",
        "text-wrap": "wrap",
        "text-max-width": 160,
        "text-valign": "center",
        "text-halign": "center",
        "background-color": "rgba(17,24,39,0.92)",
        "border-color": "rgba(148,163,184,0.30)",
        "border-width": 1,
        "shape": "round-rectangle",
        "padding": 10,
        "width": "label",
        "height": "label"
      }
    },

    // Schema pads
    {
      selector: "node[isSchema]",
      style: {
        "background-color": "rgba(34,197,94,0.06)",
        "border-color": "rgba(34,197,94,0.28)",
        "border-width": 1,
        "width": 560,
        "height": 360,
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

    // Item kinds
    { selector: 'node[kind = "table"]', style: { "border-color": "rgba(99,102,241,0.55)" } },
    { selector: 'node[kind = "view"]', style: { "border-color": "rgba(56,189,248,0.55)" } },
    { selector: 'node[kind = "function"]', style: { "border-color": "rgba(168,85,247,0.55)" } },
    { selector: 'node[kind = "enum"]', style: { "border-color": "rgba(245,158,11,0.55)" } },
    { selector: 'node[kind = "type"]', style: { "border-color": "rgba(34,197,94,0.55)" } },

    {
      selector: "node:selected",
      style: {
        "border-width": 2,
        "border-color": "rgba(167,139,250,0.95)",
        "background-color": "rgba(30,41,59,0.95)"
      }
    },

    // edges
    {
      selector: "edge",
      style: {
        "curve-style": "bezier",
        "line-color": "rgba(148,163,184,0.45)",
        "target-arrow-color": "rgba(148,163,184,0.60)",
        "target-arrow-shape": "triangle",
        "arrow-scale": 0.85,
        "width": 1,
        "opacity": 0.95
      }
    },
    { selector: 'edge[rel = "fk"]', style: { "line-color": "rgba(99,102,241,0.75)", "target-arrow-color": "rgba(99,102,241,0.75)" } },
    { selector: 'edge[rel = "reads"]', style: { "line-color": "rgba(56,189,248,0.75)", "target-arrow-color": "rgba(56,189,248,0.75)" } },
    { selector: 'edge[rel = "writes"]', style: { "line-color": "rgba(34,197,94,0.75)", "target-arrow-color": "rgba(34,197,94,0.75)" } },
    { selector: 'edge[rel = "calls"]', style: { "line-color": "rgba(168,85,247,0.75)", "target-arrow-color": "rgba(168,85,247,0.75)" } },
    { selector: 'edge[rel = "uses"]', style: { "line-color": "rgba(245,158,11,0.75)", "target-arrow-color": "rgba(245,158,11,0.75)" } },

    { selector: ".hidden", style: { "display": "none" } },

    // tour
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
      style: { "border-width": 3, "border-color": "rgba(167,139,250,0.95)" }
    }
  ];
}

function _applyResponsiveSchemaStyle(cy) {
  const host = cy?.container?.();
  if (!host) return;
  const hostW = Math.max(1, host.clientWidth ?? 1);
  const scale = Math.max(0.5, Math.min(1, hostW / 1200));
  const padW = Math.round(560 * scale);
  const padH = Math.round(360 * scale);
  const fontSize = Math.round(12 * scale);

  cy.style()
    .selector("node[isSchema]")
    .style({
      "width": padW,
      "height": padH,
      "font-size": fontSize,
      "text-margin-x": Math.round(14 * scale),
      "text-margin-y": Math.round(12 * scale)
    })
    .update();
}

function _buildElements(data) {
  const elements = [];

  // Flatten items across services
  const services = data.services ?? [];
  const allItems = [];
  const rels = [];

  for (const s of services) {
    for (const it of (s.items ?? [])) {
      allItems.push({ ...it, _serviceId: s.id, _serviceName: s.name, _schema: s.schema });
    }
    for (const r of (s.relationships ?? [])) {
      rels.push(r);
    }
  }

  // Schema pads
  const schemas = Array.from(new Set(services.map(s => s.schema).filter(Boolean)));
  for (const schema of schemas) {
    elements.push({
      data: { id: `schema:${schema}`, label: schema, isSchema: true, schema },
      position: { x: 0, y: 0 }
    });
  }

  // Items
  for (const it of allItems) {
    elements.push({
      data: {
        id: it.id,
        label: it.label ?? it.id,
        kind: it.kind ?? "table",
        schema: it._schema ?? "",
        serviceId: it._serviceId ?? "",
        serviceName: it._serviceName ?? "",
        details: it.details ?? ""
      },
      position: { x: 0, y: 0 }
    });
  }

  // Relationships
  for (const r of rels) {
    elements.push({
      data: {
        id: r.id,
        source: r.from,
        target: r.to,
        rel: (r.rel ?? "uses").toString().toLowerCase(),
        label: r.label ?? ""
      }
    });
  }

  return { elements, schemas, allItems };
}

function _computeLayout(state, explodePct) {
  const explode = Math.max(0, Math.min(1, explodePct ?? 0));
  state.explode = explode;

  const cy = state.cy;
  const schemas = state.schemas;

  const host = cy.container?.();
  const hostW = Math.max(1, host?.clientWidth ?? 1);
  const hostH = Math.max(1, host?.clientHeight ?? 1);
  const baseScale = Math.max(0.5, Math.min(1, hostW / 1200));

  // Grid of schema pads across the canvas
  const columns = Math.max(1, Math.round(Math.sqrt(schemas.length)));
  const padW = Math.round(780 * baseScale);
  const padH = Math.round(520 * baseScale);

  const centers = new Map();
  for (let i = 0; i < schemas.length; i++) {
    const schema = schemas[i];
    const col = i % columns;
    const row = Math.floor(i / columns);
    const cx = col * padW;
    const cyy = row * padH;

    centers.set(schema, { x: cx, y: cyy });

    const pad = cy.getElementById(`schema:${schema}`);
    if (pad && pad.length) {
      pad.position({ x: cx, y: cyy });
      pad.lock(); pad.unselectify(); pad.grabify(false);
    }
  }

  // Place items within their schema pad in a small grid; explode scales distances outward
  const perSchema = new Map();
  for (const it of state.allItems) {
    const schema = it._schema ?? "";
    if (!perSchema.has(schema)) perSchema.set(schema, []);
    perSchema.get(schema).push(it);
  }

  const scale = (1 + (explode * 1.6)) * baseScale;

  cy.batch(() => {
    for (const [schema, items] of perSchema.entries()) {
      const center = centers.get(schema) ?? { x: 0, y: 0 };
      const innerCols = Math.max(2, Math.ceil(Math.sqrt(items.length)));
      const cellX = 170 * baseScale;
      const cellY = 95 * baseScale;

      for (let i = 0; i < items.length; i++) {
        const it = items[i];
        const n = cy.getElementById(it.id);
        if (!n || !n.length) continue;

        const col = i % innerCols;
        const row = Math.floor(i / innerCols);

        const ox = (col - (innerCols - 1) / 2) * cellX * scale;
        const oy = (row - (Math.ceil(items.length / innerCols) - 1) / 2) * cellY * scale;

        n.position({ x: center.x + ox, y: center.y + oy });
      }
    }
  });
}

function _applyServiceFilter(state, schemaOrAll) {
  const cy = state.cy;
  const schema = (schemaOrAll ?? "").toString();

  cy.batch(() => {
    if (!schema || schema === "*" || schema.toLowerCase() === "all") {
      cy.nodes("node[!isSchema]").removeClass("hidden");
      cy.nodes("node[isSchema]").removeClass("hidden");
      cy.edges().removeClass("hidden");
      return;
    }

    // Hide nodes not in schema
    cy.nodes("node[!isSchema]").forEach(n => {
      const ns = (n.data("schema") ?? "").toString();
      if (ns === schema) n.removeClass("hidden");
      else n.addClass("hidden");
    });

    // Show only the schema pad and hide others
    cy.nodes("node[isSchema]").forEach(n => {
      const ns = (n.data("schema") ?? "").toString();
      if (ns === schema) n.removeClass("hidden");
      else n.addClass("hidden");
    });

    // Hide edges where either endpoint hidden
    cy.edges().forEach(e => {
      const s = cy.getElementById(e.data("source"));
      const t = cy.getElementById(e.data("target"));
      if (!s || !t || s.hasClass("hidden") || t.hasClass("hidden")) e.addClass("hidden");
      else e.removeClass("hidden");
    });
  });
}

function _clearTour(state) {
  const cy = state.cy;
  cy.batch(() => {
    cy.edges().removeClass("tourEdge");
    cy.nodes().removeClass("tourNode");
  });
}

function _applyTour(state, tourId, stepIndex) {
  _clearTour(state);
  if (!tourId) return;

  const tour = (state.data.tour ?? []).find(t => t.id === tourId);
  if (!tour) return;

  const steps = tour.steps ?? [];
  const idx = Math.max(0, Math.min(steps.length - 1, stepIndex ?? 0));
  const step = steps[idx];
  if (!step) return;

  const cy = state.cy;

  cy.batch(() => {
    if (step.focusItemId) {
      const n = cy.getElementById(step.focusItemId);
      if (n && n.length) n.addClass("tourNode");
    }

    for (const eid of (step.highlightEdges ?? [])) {
      const e = cy.getElementById(eid);
      if (e && e.length) e.addClass("tourEdge");
    }
  });

  if (step.focusItemId) {
    const n = cy.getElementById(step.focusItemId);
    if (n && n.length) cy.animate({ center: { eles: n }, duration: 250 });
  }
}

export function init(hostElement, data, options) {
  __diag.log('info','db','init called', { hasHost: !!hostElement, cytoscapeType: typeof window.cytoscape });
  if (!hostElement) { __diag.log('error','db','host element missing'); return; }
  if (typeof window.cytoscape !== 'function') {
    __diag.log('error','db','Cytoscape not loaded', { cytoscapeType: typeof window.cytoscape });
    __renderFatal(hostElement, 'Cytoscape not loaded', `The cytoscape script did not load.\nCheck DevTools → Network for cytoscape.min.js and any CSP/blocked requests.\nIf you are offline or unpkg is blocked, vendor cytoscape into wwwroot and reference it locally.`);
    return;
  }

  const cytoscape = _ensureCytoscape();
  const host = hostElement;

  if (!cytoscape) { _renderFallback(host, "Interactive graph failed to load (Cytoscape missing). If you're offline or blocked from unpkg, the explorer won't render."); return; }
  if (!host) throw new Error("init(hostElement, ...) requires a DOM element container.");

  dispose(host);

  const { elements, schemas, allItems } = _buildElements(data);

  const cy = cytoscape({
    container: host,
    elements,
    style: _styles(),
    layout: { name: "preset" },
    wheelSensitivity: 0.14,
    minZoom: 0.06,
    maxZoom: 3.2,
    boxSelectionEnabled: false,
    selectionType: "single"
  });

  _applyResponsiveSchemaStyle(cy);

  const state = {
    cy,
    data,
    options: options ?? {},
    schemas,
    allItems,
    explode: 0,
    serviceFilter: ""
  };

  const dotNetRef = state.options?.dotNetRef;
  const onSelect = state.options?.onItemSelectedMethod ?? "OnItemSelected";

  cy.on("tap", "node[!isSchema]", async (evt) => {
    const id = evt?.target?.id?.() ?? null;
    if (!id) return;
    try { evt.target.select(); } catch {}
    if (dotNetRef && typeof dotNetRef.invokeMethodAsync === "function") {
      try { await dotNetRef.invokeMethodAsync(onSelect, id); } catch {}
    }
  });

  cy.on("tap", (evt) => {
    if (evt.target !== cy) return;
    cy.elements().unselect();
  });

  _instances.set(_key(host), state);

  // initial layout and fit
  _computeLayout(state, 0);
  try { cy.fit(cy.elements("node[!isSchema]"), 50); } catch {}
}

export function setExplode(hostElement, explodePct) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  _computeLayout(state, explodePct);
}

export function setServiceFilter(hostElement, schemaOrAll) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  state.serviceFilter = schemaOrAll ?? "";
  _applyServiceFilter(state, state.serviceFilter);
}

export function search(hostElement, query) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;

  const q = _safeLower(query).trim();
  if (!q) return;

  const cy = state.cy;

  const match = cy.nodes("node[!isSchema]").filter(n => {
    const id = _safeLower(n.id());
    const label = _safeLower(n.data("label"));
    const schema = _safeLower(n.data("schema"));
    const kind = _safeLower(n.data("kind"));
    return id.includes(q) || label.includes(q) || schema.includes(q) || kind.includes(q);
  }).first();

  if (match && match.length) {
    cy.elements().unselect();
    match.select();
    cy.animate({ center: { eles: match }, duration: 250 });
  }
}

export function fit(hostElement) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  try { state.cy.fit(state.cy.elements("node[!isSchema]"), 50); } catch {}
}

export function reset(hostElement) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;

  _clearTour(state);

  // show all and reset filter
  state.serviceFilter = "";
  _applyServiceFilter(state, "*");
  _computeLayout(state, state.explode ?? 0);

  try { state.cy.fit(state.cy.elements("node[!isSchema]"), 50); } catch {}
}

export function clearSelection(hostElement) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  try { state.cy.elements().unselect(); } catch {}
}

export function setTour(hostElement, tourId, stepIndex) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  _applyTour(state, tourId, stepIndex);
}

export function clearTour(hostElement) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  _clearTour(state);
}

export function dispose(hostElement) {
  const state = _instances.get(_key(hostElement));
  if (!state) return;
  try { state.cy.destroy(); } catch {}
  _instances.delete(_key(hostElement));
}

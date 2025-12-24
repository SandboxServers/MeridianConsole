// Interactive dependency graph for the Scope site (Blazor WASM + Azure Static Web Apps).
// Uses Cytoscape.js for rendering and JS interop for node selection callbacks.

(function () {
  const layerOrder = ["external", "presentation", "core", "business", "foundation"];

  let cy = null;
  let dotnet = null;
  let initialPositions = null;

  function norm(s) {
    return (s || "").toString().toLowerCase();
  }

  function buildElements(data) {
    const elements = [];

    (data.nodes || []).forEach(n => {
      const label = `${n.emoji ? (n.emoji + " ") : ""}${n.name}`;
      elements.push({
        data: {
          id: n.id,
          name: n.name,
          label,
          layer: n.layer
        }
      });
    });

    (data.edges || []).forEach(e => {
      elements.push({
        data: {
          id: e.id,
          source: e.source,
          target: e.target,
          relationship: e.relationship || "depends_on"
        }
      });
    });

    return elements;
  }

  function applyPresetPositions(nodes) {
    if (!cy) return;

    const grouped = {};
    (nodes || []).forEach(n => {
      const key = n.layer || "business";
      grouped[key] = grouped[key] || [];
      grouped[key].push(n);
    });

    const w = (cy.container() && cy.container().clientWidth) ? cy.container().clientWidth : 1100;
    const bandHeight = 170;
    const minGap = 160;

    layerOrder.forEach((layer, li) => {
      const arr = (grouped[layer] || []).slice().sort((a, b) => (a.name || "").localeCompare(b.name || ""));
      const count = Math.max(arr.length, 1);
      const gap = Math.max(minGap, Math.floor(w / (count + 1)));

      arr.forEach((n, idx) => {
        const x = (idx + 1) * gap;
        const y = (li + 1) * bandHeight;
        const node = cy.getElementById(n.id);
        if (node && node.length) node.position({ x, y });
      });
    });
  }

  function init(containerId, data, dotnetRef) {
    dotnet = dotnetRef || null;

    const container = document.getElementById(containerId);
    if (!container) return;

    const elements = buildElements(data);

    cy = cytoscape({
      container,
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
            "shape": "round-rectangle",
            "width": 170,
            "height": 44,
            "background-color": "#111827",
            "border-width": 1,
            "border-color": "#334155",
            "label": "data(label)",
            "text-wrap": "ellipsis",
            "text-max-width": 160,
            "font-size": 12,
            "color": "#E5E7EB",
            "text-outline-width": 0
          }
        },
        { selector: 'node[layer = "external"]', style: { "background-color": "#0F766E" } },
        { selector: 'node[layer = "presentation"]', style: { "background-color": "#4338CA" } },
        { selector: 'node[layer = "core"]', style: { "background-color": "#6D28D9" } },
        { selector: 'node[layer = "business"]', style: { "background-color": "#1D4ED8" } },
        { selector: 'node[layer = "foundation"]', style: { "background-color": "#B45309" } },

        {
          selector: "edge",
          style: {
            "curve-style": "bezier",
            "width": 2,
            "line-color": "#94A3B8",
            "target-arrow-color": "#94A3B8",
            "target-arrow-shape": "triangle",
            "arrow-scale": 0.9,
            "opacity": 0.85
          }
        },
        {
          selector: ".dh-selected",
          style: {
            "border-width": 3,
            "border-color": "#F59E0B",
            "text-outline-width": 0
          }
        },
        {
          selector: ".dh-dim",
          style: {
            "opacity": 0.15
          }
        }
      ]
    });

    applyPresetPositions(data.nodes);

    // Capture initial positions for a reliable reset.
    initialPositions = {};
    data.nodes.forEach(n => {
      const el = cy.getElementById(n.id);
      if (el && el.length) initialPositions[n.id] = el.position();
    });

    cy.fit(undefined, 30);

    cy.on("tap", "node", (evt) => {
      const id = evt.target.id();
      select(id, true);
    });

    window.addEventListener('resize', () => { if (cy) { cy.resize(); } });

    cy.on("tap", (evt) => {
      if (evt.target === cy) {
        clear(true);
      }
    });

    // keep layout stable on resize
    window.addEventListener("resize", () => {
      try {
        applyPresetPositions(data.nodes);
        cy.fit(undefined, 30);
      } catch { /* ignore */ }
    });
  }

  function select(nodeId, notifyDotNet) {
    if (!cy) return;
    const node = cy.getElementById(nodeId);
    if (!node || !node.length) return;

    cy.elements().removeClass("dh-selected");
    node.addClass("dh-selected");

    cy.animate(
      { center: { eles: node } },
      { duration: 200 }
    );

    if (notifyDotNet && dotnet) {
      dotnet.invokeMethodAsync("OnNodeSelected", nodeId);
    }
  }

  function clear(notifyDotNet) {
    if (!cy) return;
    cy.elements().removeClass("dh-selected");
    cy.elements().removeClass("dh-dim");

    if (notifyDotNet && dotnet) {
      dotnet.invokeMethodAsync("OnBackgroundClicked");
    }
  }

    function fit() {
    if (!cy) return;
    cy.fit(undefined, 30);
  }

  function reset() {
    if (!cy) return;
    cy.elements().removeClass("dh-dim");
    if (initialPositions) {
      Object.keys(initialPositions).forEach((id) => {
        const el = cy.getElementById(id);
        if (el && el.length) el.position(initialPositions[id]);
      });
    }
    cy.fit(undefined, 30);
  }

function search(query) {
    if (!cy) return;

    const q = norm(query).trim();
    if (!q) {
      cy.elements().removeClass("dh-dim");
      return;
    }

    const matches = cy.nodes().filter(n => norm(n.data("name")).includes(q));
    cy.elements().addClass("dh-dim");

    matches.removeClass("dh-dim");
    matches.connectedEdges().removeClass("dh-dim");
    matches.connectedEdges().connectedNodes().removeClass("dh-dim");
  }

  window.dhadgarDeps = { init, select, clear, search, fit, reset };
})();

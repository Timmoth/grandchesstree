// Interactive run inspector for leaderboard.html.
//
// Fetches assets/data/leaderboard.json at runtime and wires up three
// dropdowns (engine, mode, position). The JSON schema only carries per-depth
// elapsed_sec; reference node counts for the six TGCT-canonical positions
// are baked in here so NPS can be computed without a Leaderboard schema bump.
(function () {
  "use strict";

  const REF_NODES = {
    startpos: { 1:20, 2:400, 3:8902, 4:197281, 5:4865609, 6:119060324, 7:3195901860, 8:84998978956, 9:2439530234167, 10:69352859712417, 11:2097651003696806, 12:62854969236701747 },
    kiwipete: { 1:48, 2:2039, 3:97862, 4:4085603, 5:193690690, 6:8031647685, 7:374190009323, 8:15493944087984, 9:708027759953502 },
    sje:      { 1:46, 2:2079, 3:89890, 4:3894594, 5:164075551, 6:6923051137, 7:287188994746, 8:11923589843526, 9:490154852788714 },
    pos3:     { 1:14, 2:191, 3:2812, 4:43238, 5:674624, 6:11030083, 7:178633661 },
    pos4:     { 1:6, 2:264, 3:9467, 4:422333, 5:15833292, 6:706045033 },
    pos5:     { 1:44, 2:1486, 3:62379, 4:2103487, 5:89941194, 6:3048196529 },
  };

  const MODE_LABELS = {
    "single-no-cache":   "Single, no cache",
    "single-with-cache": "Single, with cache",
    "multi-no-cache":    "Multi, no cache",
    "multi-with-cache":  "Multi, with cache",
  };
  const MODE_ORDER = ["single-no-cache", "single-with-cache", "multi-no-cache", "multi-with-cache"];

  const $ = (id) => document.getElementById(id);
  const engineSel   = $("lb-engine");
  const modeSel     = $("lb-mode");
  const positionSel = $("lb-position");
  const emptyEl     = $("lb-empty");
  const detailEl    = $("lb-detail");
  const statsEl     = $("lb-stats");
  const chartEl     = $("lb-chart");
  const rowsEl      = $("lb-depth-rows");

  if (!engineSel) return; // not on leaderboard page

  const DATA_URL = "assets/data/leaderboard.json";
  let data = null;

  function fmtInt(n) { return n != null ? n.toLocaleString("en-US") : "—"; }
  function fmtNps(n) {
    if (n == null) return "—";
    if (n >= 1e12) { const v = n / 1e12; return (v >= 10 ? v.toFixed(1) : v.toFixed(2)) + "T"; }
    if (n >= 1e9)  { const v = n / 1e9;  return (v >= 10 ? v.toFixed(1) : v.toFixed(2)) + "B"; }
    if (n >= 1e6)  { const v = n / 1e6;  return (v >= 10 ? v.toFixed(1) : v.toFixed(2)) + "M"; }
    if (n >= 1e3)  return (n / 1e3).toFixed(1) + "K";
    return Math.round(n).toString();
  }
  function fmtElapsed(s) {
    if (s == null) return "—";
    if (s < 1e-3) return (s * 1e6).toFixed(0) + " µs";
    if (s < 1)    return (s * 1000).toFixed(1) + " ms";
    if (s < 60)   return s.toFixed(2) + " s";
    const m = Math.floor(s / 60);
    return `${m}m ${(s - m * 60).toFixed(1)}s`;
  }

  function populate() {
    const engines = [...new Set(data.rows.map(r => `${r.engine}@${r.version}`))];
    engineSel.innerHTML = engines
      .map(e => `<option value="${e}">${e.replace("@", " ")}</option>`).join("");

    const modes = MODE_ORDER.filter(m => data.rows.some(r => r.mode === m));
    modeSel.innerHTML = modes
      .map(m => `<option value="${m}">${MODE_LABELS[m] || m}</option>`).join("");

    const positions = [...new Set(data.rows.flatMap(r => (r.positions || []).map(p => p.name)))];
    positionSel.innerHTML = positions
      .map(p => `<option value="${p}">${p}</option>`).join("");
  }

  function getRun() {
    const [engine, version] = engineSel.value.split("@");
    const mode = modeSel.value;
    const posName = positionSel.value;
    const row = data.rows.find(r => r.engine === engine && r.version === version && r.mode === mode);
    if (!row) return null;
    const pos = (row.positions || []).find(p => p.name === posName);
    if (!pos || !pos.depths || pos.depths.length === 0) return null;
    return { row, pos };
  }

  function enrichDepths(pos) {
    const refMap = REF_NODES[pos.name] || {};
    return pos.depths.map(d => {
      const nodes = d.nodes != null ? d.nodes : (refMap[d.depth] != null ? refMap[d.depth] : null);
      const elapsed = d.elapsed_sec;
      const nps = (nodes != null && elapsed > 0) ? (nodes / elapsed) : (d.nps != null ? d.nps : null);
      return {
        depth: d.depth,
        nodes,
        elapsed_sec: elapsed,
        nps,
        av_cpu_pct: d.av_cpu_pct != null ? d.av_cpu_pct : null,
        av_rss_mb: d.av_rss_mb != null ? d.av_rss_mb : null,
        peak_rss_mb: d.peak_rss_mb != null ? d.peak_rss_mb : null,
      };
    });
  }

  function fmtMB(mb) {
    if (mb == null) return "—";
    if (mb < 1024) return Math.round(mb) + " MB";
    return (mb / 1024).toFixed(2) + " GB";
  }
  function fmtPct(p) {
    if (p == null) return "—";
    return Math.round(p) + "%";
  }
  function fmtBytes(b) {
    if (b == null) return "—";
    const gib = b / (1024 ** 3);
    if (gib >= 1024) return (gib / 1024).toFixed(2) + " TiB";
    if (gib >= 1) {
      // 0.1 GiB resolution feels right for RAM sizes.
      return (Math.round(gib * 10) / 10) + " GiB";
    }
    const mib = b / (1024 ** 2);
    return Math.round(mib) + " MiB";
  }

  function renderHost(engineName) {
    const hostEl = document.getElementById("lb-host");
    if (!hostEl) return;
    const entry = data && data.hosts ? data.hosts[engineName] : null;
    if (!entry || !entry.host) {
      hostEl.classList.add("hidden");
      hostEl.innerHTML = "";
      return;
    }
    hostEl.classList.remove("hidden");

    const h = entry.host;
    const cpuSub = (h.cpu_physical_cores != null || h.cpu_logical_cores != null)
      ? `${h.cpu_physical_cores != null ? h.cpu_physical_cores + " cores" : ""}${
          h.cpu_physical_cores != null && h.cpu_logical_cores != null ? " · " : ""
        }${h.cpu_logical_cores != null ? h.cpu_logical_cores + " threads" : ""}`
      : "";
    const memSub = h.mem_speed_mts != null ? `${h.mem_speed_mts} MT/s` : "";
    const platform = h.platform || h.system || "—";
    const pyLine = h.python_version ? `Python ${h.python_version}` : "";
    const versionTag = entry.version ? ` <span class="text-slate-400">(${escapeXml(entry.version)})</span>` : "";

    hostEl.innerHTML = `
      <div class="rounded-xl border border-slate-200 bg-white p-6">
        <div class="flex flex-wrap items-baseline justify-between gap-2">
          <h3 class="text-sm font-semibold uppercase tracking-wider text-slate-700">Ran on</h3>
          <p class="text-xs text-slate-500">${escapeXml(engineName)}${versionTag}</p>
        </div>
        <div class="mt-4 grid gap-6 sm:grid-cols-3">
          <div>
            <p class="text-xs font-medium uppercase tracking-wider text-slate-500">CPU</p>
            <p class="mt-1 text-sm font-semibold text-slate-900">${escapeXml(h.cpu_model || "—")}</p>
            ${cpuSub ? `<p class="text-xs text-slate-500 tabular">${escapeXml(cpuSub)}</p>` : ""}
          </div>
          <div>
            <p class="text-xs font-medium uppercase tracking-wider text-slate-500">Memory</p>
            <p class="mt-1 text-sm font-semibold text-slate-900 tabular">${escapeXml(fmtBytes(h.ram_total_bytes))}</p>
            ${memSub ? `<p class="text-xs text-slate-500 tabular">${escapeXml(memSub)}</p>` : ""}
          </div>
          <div>
            <p class="text-xs font-medium uppercase tracking-wider text-slate-500">Platform</p>
            <p class="mt-1 break-words font-mono text-xs text-slate-800">${escapeXml(platform)}</p>
            ${pyLine ? `<p class="mt-1 text-xs text-slate-500">${escapeXml(pyLine)}</p>` : ""}
          </div>
        </div>
      </div>`;
  }

  function renderStats(pos, depths) {
    const totalElapsed = depths.reduce((s, d) => s + (d.elapsed_sec || 0), 0);
    const npsValues = depths.map(d => d.nps).filter(v => v != null);
    const peakNps = npsValues.length ? Math.max(...npsValues) : null;
    const deepest = depths[depths.length - 1];

    const cpuAtDeepest = pos.best_av_cpu_pct != null ? pos.best_av_cpu_pct : deepest.av_cpu_pct;
    const peakRss = pos.best_peak_rss_mb != null
      ? pos.best_peak_rss_mb
      : depths.map(d => d.peak_rss_mb).filter(v => v != null).reduce((a, b) => Math.max(a, b), 0) || null;

    const cards = [
      ["Deepest depth",     `d${deepest.depth}`],
      ["NPS @ deepest",     fmtNps(deepest.nps)],
      ["Peak NPS",          fmtNps(peakNps)],
      ["Total wall-clock",  fmtElapsed(totalElapsed)],
    ];
    if (cpuAtDeepest != null) cards.push(["CPU @ deepest", fmtPct(cpuAtDeepest)]);
    if (peakRss != null)      cards.push(["Peak RSS",      fmtMB(peakRss)]);

    statsEl.innerHTML = cards.map(([k, v]) =>
      `<div class="rounded-lg border border-slate-200 bg-white p-4">
        <dt class="text-xs font-medium uppercase tracking-wider text-slate-500">${k}</dt>
        <dd class="mt-2 text-xl font-semibold tabular text-slate-900">${v}</dd>
      </div>`
    ).join("");
  }

  function renderTable(depths) {
    const deepest = depths[depths.length - 1];
    const hasCpu = depths.some(d => d.av_cpu_pct != null);
    const hasMem = depths.some(d => d.peak_rss_mb != null);

    // Toggle the optional column headers based on whether their data is present.
    const cpuTh = document.getElementById("lb-th-cpu");
    const memTh = document.getElementById("lb-th-mem");
    if (cpuTh) cpuTh.classList.toggle("hidden", !hasCpu);
    if (memTh) memTh.classList.toggle("hidden", !hasMem);

    rowsEl.innerHTML = depths.map(d => {
      const isDeepest = d.depth === deepest.depth;
      const cpuCell = hasCpu
        ? `<td class="px-4 py-2 text-right tabular">${fmtPct(d.av_cpu_pct)}</td>`
        : "";
      const memCell = hasMem
        ? `<td class="px-4 py-2 text-right tabular">${fmtMB(d.peak_rss_mb)}</td>`
        : "";
      return `<tr class="border-t border-slate-200 ${isDeepest ? "bg-emerald-50/40" : ""}">
        <td class="px-4 py-2 tabular"><span class="font-semibold ${isDeepest ? "text-emerald-700" : "text-slate-900"}">d${d.depth}</span></td>
        <td class="px-4 py-2 text-right tabular">${fmtInt(d.nodes)}</td>
        <td class="px-4 py-2 text-right tabular">${fmtElapsed(d.elapsed_sec)}</td>
        <td class="px-4 py-2 text-right tabular ${isDeepest ? "font-semibold text-emerald-700" : ""}">${fmtNps(d.nps)}</td>
        ${cpuCell}
        ${memCell}
      </tr>`;
    }).join("");
  }

  function renderChart(depths) {
    // Single plot area with three independent Y scales:
    //   - NPS on the left, log-scaled
    //   - CPU % on a right axis (linear)
    //   - Peak RSS on a second right axis offset further out (linear)
    // Hover bands across each depth column show all three values at once.
    const xPoints = depths.filter(d => d.depth != null);
    if (xPoints.length === 0) {
      chartEl.innerHTML = '<p class="text-sm text-slate-500">No data to chart.</p>';
      return;
    }

    const seriesDefs = [
      {
        key: "nps", label: "NPS",
        color: "#0f172a", strokeWidth: 2.25, dash: null,
        side: "left", scale: "log",
        filter: p => p.nps != null && p.nps > 0,
        y: p => p.nps,
        formatTick: v => fmtNps(v),
        formatVal: p => fmtNps(p.nps),
        highlightDeepest: true,
      },
      {
        key: "cpu", label: "CPU %",
        color: "#d97706", strokeWidth: 1.75, dash: "5,3",
        side: "right", scale: "linear",
        filter: p => p.av_cpu_pct != null,
        y: p => p.av_cpu_pct,
        formatTick: v => Math.round(v) + "%",
        formatVal: p => fmtPct(p.av_cpu_pct),
      },
      {
        key: "rss", label: "Peak RSS",
        color: "#0891b2", strokeWidth: 1.75, dash: "2,3",
        side: "right", scale: "linear",
        filter: p => p.peak_rss_mb != null,
        y: p => p.peak_rss_mb,
        formatTick: v => fmtMB(v),
        formatVal: p => fmtMB(p.peak_rss_mb),
      },
    ];

    const series = seriesDefs
      .map(def => ({ ...def, points: xPoints.filter(def.filter) }))
      .filter(s => s.points.length > 0);
    if (series.length === 0) {
      chartEl.innerHTML = '<p class="text-sm text-slate-500">No data to chart.</p>';
      return;
    }

    const rightSeries = series.filter(s => s.side === "right");
    const W = 720;
    const PAD_TOP = 56, PAD_BOTTOM = 38, PAD_LEFT = 80;
    const RIGHT_AXIS_W = 70;
    const PAD_RIGHT = 14 + rightSeries.length * RIGHT_AXIS_W;
    const innerH = 290;
    const H = PAD_TOP + innerH + PAD_BOTTOM;
    const innerW = W - PAD_LEFT - PAD_RIGHT;

    const minDepth = Math.min(...xPoints.map(p => p.depth));
    const maxDepth = Math.max(...xPoints.map(p => p.depth));
    const depthSpan = Math.max(1, maxDepth - minDepth);
    const xFor = (d) => PAD_LEFT + (maxDepth === minDepth ? innerW / 2 : (d - minDepth) / depthSpan * innerW);

    // Build per-series Y scale.
    for (const s of series) {
      const vals = s.points.map(s.y).filter(v => v != null);
      if (s.scale === "log") {
        let logMin = Math.floor(Math.log10(Math.max(Math.min(...vals), 1)));
        let logMax = Math.ceil(Math.log10(Math.max(Math.max(...vals), 10)));
        if (logMax <= logMin) logMax = logMin + 1;
        s.logMin = logMin; s.logMax = logMax;
        s.yFor = (v) => PAD_TOP + innerH - (Math.log10(Math.max(v, 1)) - logMin) / (logMax - logMin) * innerH;
      } else {
        const niceMax = niceCeiling(Math.max(...vals) * 1.1);
        s.niceMax = niceMax;
        s.yFor = (v) => PAD_TOP + innerH - (v / niceMax) * innerH;
      }
    }

    let svg = "";

    // Background grid (log decades from the NPS series — the dominant signal).
    const grid = series.find(s => s.scale === "log") || series[0];
    if (grid.scale === "log") {
      for (let lg = grid.logMin; lg <= grid.logMax; lg++) {
        const y = grid.yFor(Math.pow(10, lg));
        svg += `<line x1="${PAD_LEFT}" x2="${PAD_LEFT + innerW}" y1="${y}" y2="${y}" stroke="#e2e8f0" stroke-dasharray="3,3"/>`;
      }
    }

    // Left axis (NPS) line + tick labels.
    svg += `<line x1="${PAD_LEFT}" y1="${PAD_TOP}" x2="${PAD_LEFT}" y2="${PAD_TOP + innerH}" stroke="${grid.color}" stroke-width="1.25"/>`;
    if (grid.scale === "log") {
      for (let lg = grid.logMin; lg <= grid.logMax; lg++) {
        const y = grid.yFor(Math.pow(10, lg));
        svg += `<text x="${PAD_LEFT - 8}" y="${y + 4}" text-anchor="end" fill="${grid.color}" font-size="11" class="tabular">${grid.formatTick(Math.pow(10, lg))}</text>`;
      }
    }

    // X axis line, ticks, and depth labels.
    svg += `<line x1="${PAD_LEFT}" y1="${PAD_TOP + innerH}" x2="${PAD_LEFT + innerW}" y2="${PAD_TOP + innerH}" stroke="#cbd5e1"/>`;
    for (let d = minDepth; d <= maxDepth; d++) {
      const x = xFor(d);
      svg += `<line x1="${x}" x2="${x}" y1="${PAD_TOP + innerH}" y2="${PAD_TOP + innerH + 4}" stroke="#94a3b8"/>`;
      svg += `<text x="${x}" y="${PAD_TOP + innerH + 18}" text-anchor="middle" fill="#64748b" font-size="11" class="tabular">d${d}</text>`;
    }
    svg += `<text x="${PAD_LEFT + innerW / 2}" y="${H - 6}" text-anchor="middle" fill="#475569" font-size="12">Depth</text>`;

    // Right-side axes (one per right series), offset outward.
    rightSeries.forEach((s, i) => {
      const ax = PAD_LEFT + innerW + 8 + i * RIGHT_AXIS_W;
      svg += `<line x1="${ax}" y1="${PAD_TOP}" x2="${ax}" y2="${PAD_TOP + innerH}" stroke="${s.color}" stroke-width="1.25" opacity="0.6"/>`;
      const ticks = [0, s.niceMax / 2, s.niceMax];
      for (const t of ticks) {
        const y = s.yFor(t);
        svg += `<line x1="${ax}" x2="${ax + 4}" y1="${y}" y2="${y}" stroke="${s.color}" stroke-width="1.25" opacity="0.6"/>`;
        svg += `<text x="${ax + 7}" y="${y + 4}" text-anchor="start" fill="${s.color}" font-size="11" class="tabular">${s.formatTick(t)}</text>`;
      }
    });

    // Series lines (back to front, drawn in series order).
    for (const s of series) {
      if (s.points.length > 1) {
        const path = "M " + s.points.map(p => `${xFor(p.depth)},${s.yFor(s.y(p))}`).join(" L ");
        const dash = s.dash ? ` stroke-dasharray="${s.dash}"` : "";
        svg += `<path d="${path}" fill="none" stroke="${s.color}" stroke-width="${s.strokeWidth}"${dash}/>`;
      }
    }
    // Series dots (so they sit above lines).
    for (const s of series) {
      const deepestDepth = s.points[s.points.length - 1].depth;
      for (const p of s.points) {
        const isDeepest = s.highlightDeepest && p.depth === deepestDepth;
        const fill = isDeepest ? "#059669" : s.color;
        const r = isDeepest ? 5 : 3.5;
        svg += `<circle cx="${xFor(p.depth)}" cy="${s.yFor(s.y(p))}" r="${r}" fill="${fill}"/>`;
      }
    }

    // Hover bands — one transparent rect per depth column, on top, with a
    // combined tooltip listing all three metrics at that depth.
    for (let i = 0; i < xPoints.length; i++) {
      const p = xPoints[i];
      const x = xFor(p.depth);
      const leftEdge = i === 0 ? PAD_LEFT : (xFor(xPoints[i - 1].depth) + x) / 2;
      const rightEdge = i === xPoints.length - 1 ? PAD_LEFT + innerW : (xFor(xPoints[i + 1].depth) + x) / 2;
      const lines = [`d${p.depth}`];
      for (const s of series) {
        const match = s.points.find(pp => pp.depth === p.depth);
        if (match) lines.push(`${s.label}: ${s.formatVal(match)}`);
      }
      if (p.elapsed_sec != null) lines.push(`elapsed: ${fmtElapsed(p.elapsed_sec)}`);
      svg += `<rect x="${leftEdge}" y="${PAD_TOP}" width="${rightEdge - leftEdge}" height="${innerH}" fill="transparent"><title>${escapeXml(lines.join("\n"))}</title></rect>`;
    }

    // Legend at the top of the chart.
    let lx = PAD_LEFT;
    for (const s of series) {
      const dashAttr = s.dash ? ` stroke-dasharray="${s.dash}"` : "";
      svg += `<g transform="translate(${lx}, ${PAD_TOP - 32})">
        <line x1="0" x2="22" y1="6" y2="6" stroke="${s.color}" stroke-width="${s.strokeWidth}"${dashAttr}/>
        <circle cx="11" cy="6" r="3.5" fill="${s.color}"/>
        <text x="30" y="10" font-size="11" fill="#334155" font-weight="500">${s.label}</text>
      </g>`;
      lx += 100;
    }

    chartEl.innerHTML = `<svg viewBox="0 0 ${W} ${H}" class="w-full h-auto" role="img" aria-label="Run metrics overlay">${svg}</svg>`;
  }

  function escapeXml(s) {
    return String(s)
      .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;").replace(/'/g, "&apos;");
  }

  // Click-to-sort on the build-rendered summary table. Operates on the DOM
  // directly — no re-render, no data round-trip. Numeric columns sort by
  // each cell's `data-nps` attribute (so we never parse formatted strings);
  // missing cells (no `data-nps`) always sink to the bottom regardless of
  // direction. Engine column sorts by `data-engine` on the row.
  function wireSummaryTableSort() {
    const table = document.getElementById("lb-summary");
    if (!table || !table.tHead || !table.tBodies[0]) return;
    const tbody = table.tBodies[0];

    let activeKey = null;
    let activeAsc = false;

    // String-typed columns read their value from a data-* attribute on the
    // <tr>; numeric (mode) columns read data-nps off the matching <td>.
    const STRING_KEYS = { engine: "data-engine", language: "data-language" };

    function readValue(row, key) {
      const attr = STRING_KEYS[key];
      if (attr) {
        return { kind: "str", v: row.getAttribute(attr) || "" };
      }
      const cell = row.querySelector(`td[data-mode="${key}"]`);
      if (!cell) return { kind: "num", v: null };
      const raw = cell.getAttribute("data-nps");
      if (raw == null || raw === "") return { kind: "num", v: null };
      const n = Number(raw);
      return { kind: "num", v: Number.isFinite(n) ? n : null };
    }

    function sortBy(key) {
      if (key === activeKey) {
        activeAsc = !activeAsc;
      } else {
        activeKey = key;
        // Sensible default: string columns A→Z, NPS columns biggest first.
        activeAsc = key in STRING_KEYS;
      }
      const rows = Array.from(tbody.querySelectorAll("tr"));
      rows.sort((a, b) => {
        const va = readValue(a, key);
        const vb = readValue(b, key);
        if (va.kind === "str") {
          // Empty strings (e.g. missing language) sink to the bottom
          // regardless of direction, mirroring how nulls behave for numbers.
          if (!va.v && !vb.v) return 0;
          if (!va.v) return 1;
          if (!vb.v) return -1;
          const cmp = va.v.localeCompare(vb.v, undefined, { sensitivity: "base" });
          return activeAsc ? cmp : -cmp;
        }
        // Numeric: nulls always last, irrespective of direction.
        if (va.v == null && vb.v == null) return 0;
        if (va.v == null) return 1;
        if (vb.v == null) return -1;
        return activeAsc ? va.v - vb.v : vb.v - va.v;
      });
      const frag = document.createDocumentFragment();
      rows.forEach(r => frag.appendChild(r));
      tbody.appendChild(frag);
      updateIndicators(key, activeAsc);
    }

    function updateIndicators(key, asc) {
      table.tHead.querySelectorAll("th[data-sort]").forEach(th => {
        const icon = th.querySelector(".sort-icon");
        if (!icon) return;
        if (th.getAttribute("data-sort") === key) {
          icon.textContent = asc ? "▲" : "▼";
          icon.classList.remove("hidden");
        } else {
          icon.textContent = "";
          icon.classList.add("hidden");
        }
      });
    }

    table.tHead.querySelectorAll("th[data-sort]").forEach(th => {
      th.addEventListener("click", () => sortBy(th.getAttribute("data-sort")));
    });

    // Build-time row order matches a single-no-cache descending sort, so
    // mirror that into the JS state and show the matching ▼ indicator.
    // This is a state sync only — no DOM reorder happens here, because
    // the rows are already in the right order from build.js.
    activeKey = "single-no-cache";
    activeAsc = false;
    updateIndicators(activeKey, activeAsc);
  }

  // Round a number up to a friendly axis ceiling. Finer-grained than the
  // classic 1/2/5/10 series so panels (esp. memory) don't waste vertical
  // headroom when the data clusters around a single magnitude.
  function niceCeiling(v) {
    if (!isFinite(v) || v <= 0) return 1;
    const exp = Math.floor(Math.log10(v));
    const base = Math.pow(10, exp);
    const m = v / base;
    let nice;
    if (m <= 1)        nice = 1;
    else if (m <= 1.5) nice = 1.5;
    else if (m <= 2)   nice = 2;
    else if (m <= 2.5) nice = 2.5;
    else if (m <= 3)   nice = 3;
    else if (m <= 5)   nice = 5;
    else if (m <= 7.5) nice = 7.5;
    else               nice = 10;
    return nice * base;
  }

  function render() {
    const sel = getRun();
    if (!sel) {
      detailEl.classList.add("hidden");
      emptyEl.classList.remove("hidden");
      return;
    }
    const depths = enrichDepths(sel.pos);
    if (depths.length === 0) {
      detailEl.classList.add("hidden");
      emptyEl.classList.remove("hidden");
      return;
    }
    emptyEl.classList.add("hidden");
    detailEl.classList.remove("hidden");
    renderStats(sel.pos, depths);
    renderHost(sel.row.engine);
    renderTable(depths);
    renderChart(depths);
  }

  [engineSel, modeSel, positionSel].forEach(el => el.addEventListener("change", render));

  wireSummaryTableSort();

  fetch(DATA_URL, { cache: "no-cache" })
    .then(r => {
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      return r.json();
    })
    .then(json => {
      data = json;
      if (!data.rows || data.rows.length === 0) {
        emptyEl.textContent = "No runs yet.";
        return;
      }
      populate();
      render();
    })
    .catch(err => {
      console.error("[leaderboard] fetch failed", err);
      emptyEl.textContent = "Failed to load leaderboard data.";
    });
})();

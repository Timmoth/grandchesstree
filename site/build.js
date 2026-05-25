#!/usr/bin/env node
/*
 * Build the static site at site/dist/ from site/src/.
 *
 * What it does
 *   1. Cleans dist/ — but preserves dist/wasm/movegen/ (managed by
 *      ../MoveGen/MoveGen.Wasm/publish-to-webapp.sh, which is too slow to
 *      run on every site rebuild).
 *   2. For each top-level src/*.html: replaces the existing <header>...
 *      </header> and <footer>...</footer> blocks with the shared templates
 *      in src/templates/, with the active nav link picked up from the
 *      current page's `class="text-slate-900"` marker.
 *   3. Copies assets/, robots.txt, sitemap.xml.
 *   4. Runs articles/build-articles.js so the published article HTML lands
 *      under dist/move-generator/.
 *
 * Usage
 *   node site/build.js
 *   node site/build.js --skip-articles    # faster iteration when working on shell pages
 *
 * Idempotent. Safe to re-run.
 */
"use strict";

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

const HERE = __dirname;
const SRC = path.join(HERE, "src");
const DIST = path.join(HERE, "dist");
const TPL_DIR = path.join(SRC, "templates");

const args = process.argv.slice(2);
const SKIP_ARTICLES = args.includes("--skip-articles");

// ---------- I/O helpers ----------
const read = (p) => fs.readFileSync(p, "utf8");
const write = (p, c) => { fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, c); };
const exists = (p) => fs.existsSync(p);

function copyDir(srcDir, destDir) {
  fs.mkdirSync(destDir, { recursive: true });
  for (const entry of fs.readdirSync(srcDir, { withFileTypes: true })) {
    const s = path.join(srcDir, entry.name);
    const d = path.join(destDir, entry.name);
    if (entry.isDirectory()) copyDir(s, d);
    else fs.copyFileSync(s, d);
  }
}
function removeDir(p) { if (exists(p)) fs.rmSync(p, { recursive: true, force: true }); }
function removeIfFile(p) { if (exists(p) && fs.statSync(p).isFile()) fs.unlinkSync(p); }

// ---------- Templates ----------
const headerTpl = read(path.join(TPL_DIR, "header.html"));
const footerTpl = read(path.join(TPL_DIR, "footer.html"));

// Sections the nav highlights. Add here when a new top-level section is added.
const NAV_SECTIONS = ["distributed-perft", "tools", "move-generator", "cpu-leaderboard", "gpu-leaderboard"];

// Composite buckets: the "Leaderboards" dropdown button gets the active style
// when *either* the CPU or GPU child page is current.
const NAV_BUCKETS = {
  "leaderboards": ["cpu-leaderboard", "gpu-leaderboard"],
};

function navClass(active, section) {
  return active === section ? "text-slate-900" : "hover:text-slate-900";
}

function navBucketClass(active, members) {
  return members.includes(active) ? "text-slate-900" : "hover:text-slate-900";
}

function tplVars({ active = null, base = "" }) {
  const vars = { BASE: base };
  for (const s of NAV_SECTIONS) {
    vars[`NAV_${s.toUpperCase().replace(/-/g, "_")}`] = navClass(active, s);
  }
  for (const [name, members] of Object.entries(NAV_BUCKETS)) {
    vars[`NAV_${name.toUpperCase().replace(/-/g, "_")}`] = navBucketClass(active, members);
  }
  return vars;
}

function interpolate(tpl, vars) {
  return tpl.replace(/\{\{(\w+)\}\}/g, (m, key) => (key in vars ? vars[key] : m));
}

function renderHeader(opts) { return interpolate(headerTpl, tplVars(opts)); }
function renderFooter(opts) { return interpolate(footerTpl, tplVars(opts)); }

// ---------- Page transform ----------
// Detect which nav link is currently active in a page by looking for
// `class="text-slate-900">` on an <a> inside the <header>.
function detectActiveSection(html) {
  const headerMatch = html.match(/<header [\s\S]*?<\/header>/);
  if (!headerMatch) return null;
  const header = headerMatch[0];
  // Look for hrefs of known sections with the active class.
  if (/href="(?:\.\.\/)?distributed-perft\.html"[^>]*class="text-slate-900"/.test(header)) return "distributed-perft";
  if (/href="(?:\.\.\/)?tools\.html"[^>]*class="text-slate-900"/.test(header)) return "tools";
  if (/href="(?:\.\.\/)?move-generator\/?"[^>]*class="text-slate-900"/.test(header)) return "move-generator";
  if (/href="(?:\.\.\/)?leaderboard\.html"[^>]*class="text-slate-900"/.test(header)) return "cpu-leaderboard";
  if (/href="(?:\.\.\/)?gpu-leaderboard\.html"[^>]*class="text-slate-900"/.test(header)) return "gpu-leaderboard";
  return null;
}

function transformPage(html, { base = "" } = {}) {
  const active = detectActiveSection(html);
  const newHeader = renderHeader({ active, base });
  const newFooter = renderFooter({ base });
  return html
    .replace(/    <header [\s\S]*?<\/header>\n/, newHeader)
    .replace(/    <footer [\s\S]*?<\/footer>\n/, newFooter);
}

// ---------- Leaderboard data ----------
// site/src/assets/data/leaderboard.json is the served data file. Drop a new
// run's leaderboard.json there (the file format is the one emitted by
// PerftWar/perft_war.py aggregate) and rebuild — the cards, table, badge,
// and timestamp on leaderboard.html re-render from the JSON.
const LB_JSON = path.join(SRC, "assets/data/leaderboard.json");

const LB_MODES = [
  { key: "single-no-cache",   label: "Single, no cache" },
  { key: "single-with-cache", label: "Single, with cache" },
  { key: "multi-no-cache",    label: "Multi, no cache" },
  { key: "multi-with-cache",  label: "Multi, with cache" },
];

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}
function fmtNpsFull(n) { return n.toLocaleString("en-US"); }
function fmtNpsShort(n) {
  if (n >= 1e9) { const v = n / 1e9; return (v >= 10 ? v.toFixed(1) : v.toFixed(2)) + "B"; }
  if (n >= 1e6) { const v = n / 1e6; return (v >= 10 ? v.toFixed(1) : v.toFixed(2)) + "M"; }
  if (n >= 1e3) return (n / 1e3).toFixed(1) + "K";
  return String(n);
}
function fmtTimestamp(iso) {
  const d = new Date(iso);
  const pad = (x) => String(x).padStart(2, "0");
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())} ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())} UTC`;
}

function emptyLeaderboardVars() {
  const cards = LB_MODES.map(({ label }) =>
`<article class="rounded-xl border border-dashed border-slate-200 bg-white p-5">
              <p class="text-xs font-medium uppercase tracking-wider text-slate-500">${escapeHtml(label)}</p>
              <p class="mt-3 text-2xl font-semibold text-slate-300">&mdash;</p>
              <p class="mt-2 text-sm text-slate-400">No entries yet</p>
            </article>`
  ).join("\n            ");
  return {
    LB_TIMESTAMP_ISO: "",
    LB_TIMESTAMP: "—",
    LB_ENGINES_LABEL: "No engines yet",
    LB_CARDS: cards,
    LB_TABLE_ROWS:
`<tr class="border-t border-slate-200">
                    <td colspan="7" class="px-4 py-6 text-center text-sm text-slate-500">
                      No results yet. Drop a leaderboard.json into
                      <span class="font-mono">site/src/assets/data/</span> and rebuild.
                    </td>
                  </tr>`,
  };
}

function leaderboardVars() {
  if (!exists(LB_JSON)) return emptyLeaderboardVars();
  const data = JSON.parse(read(LB_JSON));
  if (!data || !Array.isArray(data.rows) || data.rows.length === 0) {
    return emptyLeaderboardVars();
  }

  // Group rows by engine+version. Skip rows with no number — null means
  // "run in progress, no value yet" (PerftWar writes nulls as placeholders).
  const enginesMap = new Map();
  for (const row of data.rows) {
    if (row.mean_nps == null) continue;
    const key = `${row.engine}@${row.version}`;
    if (!enginesMap.has(key)) {
      enginesMap.set(key, {
        engine: row.engine,
        version: row.version,
        language: row.language || null,
        repo: row.repo || null,
        modes: {},
      });
    }
    enginesMap.get(key).modes[row.mode] = row.mean_nps;
    // Tolerate older JSONs where some rows lack metadata — fill in from any
    // row that does carry it.
    if (row.language && !enginesMap.get(key).language) {
      enginesMap.get(key).language = row.language;
    }
    if (row.repo && !enginesMap.get(key).repo) {
      enginesMap.get(key).repo = row.repo;
    }
  }
  const engines = [...enginesMap.values()];
  if (engines.length === 0) return emptyLeaderboardVars();
  // Default sort matches the JS sort handler's default — single-no-cache,
  // largest NPS first. Engines without that mode sink to the bottom; among
  // those, fall back to their best-mode NPS so they're not arbitrarily
  // ordered.
  engines.sort((a, b) => {
    const av = a.modes["single-no-cache"];
    const bv = b.modes["single-no-cache"];
    if (av != null && bv != null) return bv - av;
    if (av != null) return -1;
    if (bv != null) return 1;
    return Math.max(...Object.values(b.modes)) - Math.max(...Object.values(a.modes));
  });

  // Per mode, sorted list of (engine, version, nps) — first is the leader.
  const perMode = {};
  for (const m of LB_MODES) {
    perMode[m.key] = engines
      .map(e => ({ engine: e.engine, version: e.version, nps: e.modes[m.key] }))
      .filter(e => e.nps != null)
      .sort((a, b) => b.nps - a.nps);
  }

  const cards = LB_MODES.map(({ key, label }) => {
    const entries = perMode[key];
    if (!entries.length) {
      return `<article class="rounded-xl border border-dashed border-slate-200 bg-white p-5">
              <p class="text-xs font-medium uppercase tracking-wider text-slate-500">${escapeHtml(label)}</p>
              <p class="mt-3 text-2xl font-semibold text-slate-300">&mdash;</p>
              <p class="mt-2 text-sm text-slate-400">No entries yet</p>
            </article>`;
    }
    const best = entries[0];
    const contested = entries.length > 1;
    const borderCls = contested ? "border-emerald-200 bg-emerald-50/40" : "border-slate-200 bg-white";
    const labelCls = contested ? "text-emerald-700" : "text-slate-500";
    return `<article class="rounded-xl border ${borderCls} p-5">
              <p class="text-xs font-medium uppercase tracking-wider ${labelCls}">${escapeHtml(label)}</p>
              <p class="mt-3 text-2xl font-semibold tabular text-slate-900">${fmtNpsShort(best.nps)} <span class="text-sm font-normal text-slate-500">NPS</span></p>
              <p class="mt-2 text-sm text-slate-600">
                <span class="font-semibold text-slate-900">${escapeHtml(best.engine)}</span>
                <span class="text-slate-500">${escapeHtml(best.version)}</span>
              </p>
            </article>`;
  }).join("\n            ");

  const tableRows = engines.map((e) => {
    // Engine name → repo link comes straight from the leaderboard JSON's
    // per-row `repo` field (which the aggregator now copies from each
    // engine descriptor). Rows without a repo render as plain text — the
    // only case that should hit is leaderboard.json predating that schema.
    const engineCell = e.repo
      ? `<a href="${escapeHtml(e.repo)}" target="_blank" rel="noopener noreferrer" class="font-semibold text-slate-900 underline decoration-slate-300 underline-offset-2 hover:decoration-slate-700">${escapeHtml(e.engine)}</a>`
      : `<span class="font-semibold text-slate-900">${escapeHtml(e.engine)}</span>`;

    const cells = LB_MODES.map((m) => {
      const v = e.modes[m.key];
      if (v == null) {
        return `<td data-mode="${m.key}" class="whitespace-nowrap px-4 py-3 text-right tabular text-slate-400">&mdash;</td>`;
      }
      const entries = perMode[m.key];
      const leader = entries[0];
      const isLeader = entries.length > 1 && leader.engine === e.engine && leader.version === e.version;
      const cls = isLeader
        ? "whitespace-nowrap px-4 py-3 text-right tabular font-semibold text-emerald-700"
        : "whitespace-nowrap px-4 py-3 text-right tabular";
      return `<td data-mode="${m.key}" data-nps="${v}" class="${cls}">${fmtNpsFull(v)} <span class="ml-1 text-xs font-normal text-slate-500">(${fmtNpsShort(v)})</span></td>`;
    }).join("");

    const lang = e.language || "";
    const langCell = lang
      ? `<span class="inline-flex items-center rounded-full border border-slate-200 bg-slate-50 px-2 py-0.5 text-xs font-medium text-slate-700">${escapeHtml(lang)}</span>`
      : `<span class="text-slate-400">&mdash;</span>`;
    return `<tr data-engine="${escapeHtml(e.engine)}" data-language="${escapeHtml(lang)}" class="border-t border-slate-200 hover:bg-slate-50/60">
                    <td class="whitespace-nowrap px-4 py-3">${engineCell}</td>
                    <td class="whitespace-nowrap px-4 py-3">${langCell}</td>
                    <td class="whitespace-nowrap px-4 py-3 font-mono text-xs text-slate-500">${escapeHtml(e.version)}</td>
                    ${cells}
                  </tr>`;
  }).join("\n                  ");

  const distinctEngines = new Set(engines.map(e => e.engine)).size;
  const enginesLabel = distinctEngines === 1 ? "1 engine" : `${distinctEngines} engines`;

  return {
    LB_TIMESTAMP_ISO: data.generated_at || "",
    LB_TIMESTAMP: data.generated_at ? fmtTimestamp(data.generated_at) : "—",
    LB_ENGINES_LABEL: enginesLabel,
    LB_CARDS: cards,
    LB_TABLE_ROWS: tableRows,
  };
}

function buildPages() {
  const lbVars = leaderboardVars();
  for (const entry of fs.readdirSync(SRC)) {
    if (!entry.endsWith(".html")) continue;
    const srcPath = path.join(SRC, entry);
    let html = transformPage(read(srcPath), { base: "" });
    html = interpolate(html, lbVars);
    const destPath = path.join(DIST, entry);
    write(destPath, html);
    console.log(`  page    ${entry}`);
  }
}

// ---------- Static asset copy ----------
const ASSET_DIRS = ["assets"];
const ASSET_FILES = ["robots.txt", "sitemap.xml"];

function copyAssets() {
  for (const d of ASSET_DIRS) {
    const s = path.join(SRC, d);
    if (!exists(s)) continue;
    const dst = path.join(DIST, d);
    removeDir(dst);
    copyDir(s, dst);
    console.log(`  copy    ${d}/`);
  }
  for (const f of ASSET_FILES) {
    const s = path.join(SRC, f);
    if (exists(s)) {
      fs.mkdirSync(DIST, { recursive: true });
      fs.copyFileSync(s, path.join(DIST, f));
      console.log(`  copy    ${f}`);
    }
  }
}

// ---------- Articles ----------
function buildArticles() {
  const script = path.join(SRC, "articles", "build-articles.js");
  if (!exists(script)) {
    console.warn("  [skip] src/articles/build-articles.js not found");
    return;
  }
  const r = spawnSync("node", [script], {
    stdio: "inherit",
    env: {
      ...process.env,
      // The articles publisher picks up these paths and writes accordingly.
      MG_OUT_DIR: path.join(DIST, "move-generator"),
      MG_TEMPLATES_DIR: TPL_DIR,
    },
  });
  if (r.status !== 0) throw new Error(`build-articles.js exited ${r.status}`);
}

// ---------- Clean ----------
// Always preserves dist/wasm/ (managed externally by
// MoveGen.Wasm/publish-to-webapp.sh). When --skip-articles is set, also
// preserves dist/move-generator/ so iterating on shell pages doesn't blow
// away the already-rendered article series.
function cleanDist() {
  if (!exists(DIST)) return;
  const preserve = new Set(["wasm"]);
  if (SKIP_ARTICLES) preserve.add("move-generator");
  for (const entry of fs.readdirSync(DIST)) {
    if (preserve.has(entry)) continue;
    const p = path.join(DIST, entry);
    if (fs.statSync(p).isDirectory()) removeDir(p);
    else removeIfFile(p);
  }
}

// ---------- Main ----------
function main() {
  console.log(`[site/build] ${SRC} → ${DIST}`);
  fs.mkdirSync(DIST, { recursive: true });
  cleanDist();
  buildPages();
  copyAssets();
  if (SKIP_ARTICLES) {
    console.log("  [skip] --skip-articles");
  } else {
    buildArticles();
  }
  console.log("\n[site/build] done. dist/:");
  for (const f of fs.readdirSync(DIST).sort()) {
    const p = path.join(DIST, f);
    console.log("  " + f + (fs.statSync(p).isDirectory() ? "/" : ""));
  }
}

main();

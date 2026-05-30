// Homepage perft table: switchable across startpos / kiwipete / sje, with
// per-cell verifier badges that open a popover listing the people who
// confirmed that data point.
//
// Data shape (assets/data/homepage_perft.json):
//   positions:
//     <slug>:
//       name, fen, description
//       rows: [{ ply, cells: { <col>: { value, verifiers: [{name, url?}, ...] } | null } }]
//
// A null cell value means the column doesn't apply at that depth (e.g.
// unique counts past d=11 for startpos).

(() => {
  "use strict";

  const DATA_URL = "assets/data/homepage_perft.json";

  const COLS = [
    { key: "nodes",      label: "Nodes" },
    { key: "unique",     label: "Unique" },
    { key: "captures",   label: "Captures" },
    { key: "enpassants", label: "E.p." },
    { key: "castles",    label: "Castles" },
    { key: "promotions", label: "Promos" },
    { key: "checks",     label: "Checks" },
    { key: "mates",      label: "Mates" },
  ];

  // Values come through as numeric strings to preserve precision for
  // large counts (perft(15) ~ 2 × 10^21 overflows JS Number). Format with
  // thousands separators without re-parsing. Plain numbers and missing
  // values still pass through cleanly.
  function fmt(n) {
    if (n === null || n === undefined) return "—";
    if (typeof n === "number") return n.toLocaleString("en-US");
    if (typeof n === "string" && /^-?\d+$/.test(n)) {
      return n.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
    }
    return String(n);
  }

  // --- popover --------------------------------------------------------------
  let popoverEl = null;

  function closePopover() {
    if (popoverEl) {
      popoverEl.remove();
      popoverEl = null;
      document.removeEventListener("click", onDocClick, true);
      document.removeEventListener("keydown", onKey);
      window.removeEventListener("scroll", onScroll, true);
      window.removeEventListener("resize", onScroll, true);
    }
  }

  function onDocClick(e) {
    if (popoverEl && !popoverEl.contains(e.target) && !e.target.closest(".verifier-badge")) {
      closePopover();
    }
  }
  function onKey(e) { if (e.key === "Escape") closePopover(); }
  function onScroll() { closePopover(); }

  function appendSectionHeader(parent, text) {
    const h = document.createElement("div");
    h.className = "mb-1 mt-3 text-[10px] font-semibold uppercase tracking-wider text-slate-500 first:mt-0";
    h.textContent = text;
    parent.appendChild(h);
  }

  function appendLinkItem(list, item) {
    const li = document.createElement("li");
    if (item.url) {
      const a = document.createElement("a");
      a.href = item.url;
      a.target = "_blank";
      a.rel = "noopener noreferrer";
      a.className = "text-slate-700 underline decoration-slate-300 underline-offset-2 hover:decoration-slate-700";
      a.textContent = item.name;
      li.appendChild(a);
    } else {
      li.className = "text-slate-700";
      li.textContent = item.name;
    }
    list.appendChild(li);
  }

  function openPopover(badge, verifiers, sources) {
    closePopover();
    const el = document.createElement("div");
    // position:fixed + body-anchored so the popover escapes any
    // overflow:auto/hidden ancestor (e.g. the table's horizontal-scroll
    // wrapper) and never gets clipped.
    el.className =
      "verifier-popover fixed z-50 w-72 rounded-lg border border-slate-200 bg-white p-3 text-sm shadow-lg";
    el.style.visibility = "hidden"; // measure first, then position
    el.setAttribute("role", "dialog");

    const title = document.createElement("div");
    title.className = "mb-1 text-xs font-semibold uppercase tracking-wider text-slate-500";
    title.textContent = `Verified by ${verifiers.length}`;
    el.appendChild(title);

    if (verifiers.length) {
      appendSectionHeader(el, "Verifiers");
      const ul = document.createElement("ul");
      ul.className = "space-y-1";
      for (const v of verifiers) appendLinkItem(ul, v);
      el.appendChild(ul);
    }

    if (sources && sources.length) {
      appendSectionHeader(el, "Sources");
      const ul = document.createElement("ul");
      ul.className = "space-y-1 text-xs";
      for (const s of sources) appendLinkItem(ul, s);
      el.appendChild(ul);
    }

    document.body.appendChild(el);
    popoverEl = el;

    // Measure now that it's in the DOM and choose vertical placement.
    const rect = badge.getBoundingClientRect();
    const popW = el.offsetWidth;
    const popH = el.offsetHeight;
    const vw = window.innerWidth;
    const vh = window.innerHeight;
    const gap = 8;

    // Prefer below the badge; flip above if it would overflow.
    let top = rect.bottom + gap;
    if (top + popH > vh - gap && rect.top - gap - popH >= gap) {
      top = rect.top - gap - popH;
    } else if (top + popH > vh - gap) {
      top = Math.max(gap, vh - popH - gap);
    }

    // Align left edge with badge; nudge inward if it would overflow right
    // (or off the left edge on a very narrow viewport).
    let left = rect.left;
    if (left + popW > vw - gap) left = vw - popW - gap;
    if (left < gap) left = gap;

    el.style.top = top + "px";
    el.style.left = left + "px";
    el.style.visibility = "";

    setTimeout(() => {
      document.addEventListener("click", onDocClick, true);
      document.addEventListener("keydown", onKey);
      // capture=true so we catch scrolls from any scrollable ancestor
      // (notably the table's overflow-x-auto wrapper).
      window.addEventListener("scroll", onScroll, true);
      window.addEventListener("resize", onScroll, true);
    }, 0);
  }

  // --- table rendering ------------------------------------------------------
  function makeBadge(cell) {
    const verifiers = cell.verifiers || [];
    if (verifiers.length === 0) return null;
    const sources = cell.sources || [];
    const wrap = document.createElement("span");
    wrap.className = "relative inline-block ml-1";

    const btn = document.createElement("button");
    btn.type = "button";
    btn.className =
      "verifier-badge align-super inline-flex h-4 min-w-[1rem] items-center justify-center rounded-full bg-slate-200 px-1 text-[10px] font-semibold leading-none text-slate-700 hover:bg-slate-900 hover:text-white";
    btn.setAttribute("aria-label", `${verifiers.length} verifier${verifiers.length === 1 ? "" : "s"}`);
    btn.setAttribute("aria-haspopup", "dialog");
    btn.textContent = String(verifiers.length);
    btn.addEventListener("click", (e) => {
      e.stopPropagation();
      const existing = wrap.querySelector(".verifier-popover");
      if (existing) { closePopover(); return; }
      openPopover(btn, verifiers, sources);
    });

    wrap.appendChild(btn);
    return wrap;
  }

  function renderTable(position) {
    const tbody = document.getElementById("perft-tbody");
    if (!tbody) return;
    tbody.innerHTML = "";

    for (const row of position.rows) {
      const tr = document.createElement("tr");
      tr.className = "border-t border-slate-200 hover:bg-slate-50/60";

      const plyTd = document.createElement("td");
      plyTd.className = "px-3 py-3 tabular sticky left-0 bg-white";
      const plyStrong = document.createElement("span");
      plyStrong.className = "font-semibold text-slate-900";
      plyStrong.textContent = row.ply;
      plyTd.appendChild(plyStrong);
      tr.appendChild(plyTd);

      for (const { key } of COLS) {
        const td = document.createElement("td");
        td.className = "px-3 py-3 tabular whitespace-nowrap";
        const cell = row.cells[key];
        if (!cell) {
          td.classList.add("text-slate-400");
          td.textContent = "—";
        } else {
          const span = document.createElement("span");
          span.textContent = fmt(cell.value);
          td.appendChild(span);
          const badge = makeBadge(cell);
          if (badge) td.appendChild(badge);
        }
        tr.appendChild(td);
      }
      tbody.appendChild(tr);
    }
  }

  function renderHeader(position) {
    const fen = document.getElementById("perft-fen");
    const desc = document.getElementById("perft-desc");
    if (fen) fen.textContent = position.fen;
    if (desc) desc.textContent = position.description;
  }

  function renderButtons(data, current, setActive) {
    const bar = document.getElementById("perft-switcher");
    if (!bar) return;
    bar.innerHTML = "";
    for (const slug of Object.keys(data.positions)) {
      const p = data.positions[slug];
      const btn = document.createElement("button");
      btn.type = "button";
      btn.dataset.slug = slug;
      const base =
        "rounded-full border px-3 py-1.5 text-sm font-medium transition-colors";
      const active = "border-slate-900 bg-slate-900 text-white";
      const inactive = "border-slate-300 bg-white text-slate-700 hover:bg-slate-50";
      btn.className = `${base} ${slug === current ? active : inactive}`;
      btn.textContent = p.name;
      btn.addEventListener("click", () => setActive(slug));
      bar.appendChild(btn);
    }
  }

  // --- bootstrap ------------------------------------------------------------
  fetch(DATA_URL, { cache: "no-cache" })
    .then((r) => r.json())
    .then((data) => {
      let current = "startpos";
      const setActive = (slug) => {
        if (!data.positions[slug]) return;
        current = slug;
        closePopover();
        renderButtons(data, current, setActive);
        renderHeader(data.positions[current]);
        renderTable(data.positions[current]);
      };
      setActive(current);
    })
    .catch((err) => {
      console.error("[homepage-perft] fetch failed", err);
      const tbody = document.getElementById("perft-tbody");
      if (tbody) {
        tbody.innerHTML =
          '<tr><td colspan="9" class="px-3 py-6 text-center text-slate-500">Failed to load perft data.</td></tr>';
      }
    });
})();

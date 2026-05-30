#!/usr/bin/env node
/*
 * Build the move-generation series into static HTML pages under
 * WebApp/move-generator/.  Minimal Markdown → HTML converter targeting the
 * features the articles actually use:
 *   - ATX headings (# .. ####)
 *   - paragraphs
 *   - **bold**, *italic*
 *   - `inline code`
 *   - fenced code blocks (```lang ... ```)
 *   - unordered (-) and ordered (1.) lists
 *   - blockquotes
 *   - horizontal rules
 *   - pipe tables (header + alignment row + rows)
 *   - links [text](url)
 *   - images ![alt](src)
 *
 * Outputs flat per-article HTML files plus a hub index.html.
 * Run with: node build-articles.js
 */
"use strict";

const fs = require("fs");
const path = require("path");

// New layout: this script lives in Site/src/articles/.
//   __dirname  =  <repo>/Site/src/articles
//   site root  =  <repo>/site            (two up)
//   default output → Site/dist/move-generator
// Both paths are overridable via env vars so Site/build.js can point them
// somewhere else without modifying this file.
const ARTICLES_DIR = __dirname;
const IMG_SRC = path.join(ARTICLES_DIR, "img");
const SITE_ROOT = path.resolve(__dirname, "..", "..");
const DEFAULT_OUT = path.join(SITE_ROOT, "dist", "move-generator");
const DEFAULT_TPL = path.join(SITE_ROOT, "src", "templates");
const OUT_DIR = process.env.MG_OUT_DIR || DEFAULT_OUT;
const TEMPLATES_DIR = process.env.MG_TEMPLATES_DIR || DEFAULT_TPL;
const IMG_DEST = path.join(OUT_DIR, "img");

// Shared site chrome — same templates the rest of the site uses.
const HEADER_TPL = fs.readFileSync(path.join(TEMPLATES_DIR, "header.html"), "utf8");
const FOOTER_TPL = fs.readFileSync(path.join(TEMPLATES_DIR, "footer.html"), "utf8");

function renderChrome(tpl, { active, base }) {
  const isActive = (s) => active === s ? "text-slate-900" : "hover:text-slate-900";
  // Composite bucket: "Leaderboards" parent highlights when *either* CPU or
  // GPU child is current. Mirror Site/build.js NAV_BUCKETS.
  const isInBucket = (members) => members.includes(active) ? "text-slate-900" : "hover:text-slate-900";
  return tpl
    .replace(/\{\{BASE\}\}/g, base)
    .replace(/\{\{NAV_DISTRIBUTED_PERFT\}\}/g, isActive("distributed-perft"))
    .replace(/\{\{NAV_TOOLS\}\}/g, isActive("tools"))
    .replace(/\{\{NAV_MOVE_GENERATOR\}\}/g, isActive("move-generator"))
    .replace(/\{\{NAV_CPU_LEADERBOARD\}\}/g, isActive("cpu-leaderboard"))
    .replace(/\{\{NAV_GPU_LEADERBOARD\}\}/g, isActive("gpu-leaderboard"))
    .replace(/\{\{NAV_LEADERBOARDS\}\}/g, isInBucket(["cpu-leaderboard", "gpu-leaderboard"]));
}

const SERIES_TITLE = "Let's build a move generator";
const SERIES_DESC =
  "A seven-part series on building a correct, cross-platform chess move " +
  "generator in C# — from FEN parsing through magic bitboards to fully " +
  "legal move generation.";

const ARTICLES = [
  { md: "01-introduction.md",        slug: "01-introduction" },
  { md: "02-representing-the-board.md", slug: "02-board" },
  { md: "03-encoding-a-move.md",     slug: "03-move-encoding" },
  { md: "04-pseudo-legal-moves.md",  slug: "04-pseudo-legal-moves" },
  { md: "05-making-a-move.md",       slug: "05-make-move" },
  { md: "06-magic-bitboards.md",     slug: "06-magic-bitboards" },
  { md: "07-legal-move-generation.md", slug: "07-legal-moves" },
];

// -----------------------------------------------------------------------------
// HTML escaping
// -----------------------------------------------------------------------------
function esc(s) {
  return String(s)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function slugify(s) {
  return String(s).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

// -----------------------------------------------------------------------------
// Inline parser — stash code/images/links → escape text → bold/italic → restore
// -----------------------------------------------------------------------------
function inline(text) {
  const stashes = [];
  const stash = (html) => {
    stashes.push(html);
    return `\x00${stashes.length - 1}\x00`;
  };

  // Inline code first so its contents aren't processed further.
  text = text.replace(/`([^`]+)`/g, (_, code) =>
    stash(`<code class="rounded bg-slate-100 px-1.5 py-0.5 font-mono text-[0.92em] text-slate-800">${esc(code)}</code>`)
  );

  // Images.
  text = text.replace(/!\[([^\]]*)\]\(([^)]+)\)/g, (_, alt, src) =>
    stash(`<img src="${esc(src)}" alt="${esc(alt)}" loading="lazy" class="mx-auto block rounded-md border border-slate-200 shadow-sm" />`)
  );

  // Links.
  text = text.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_, label, url) => {
    const external = /^https?:\/\//.test(url);
    const attrs = external ? ` target="_blank" rel="noopener noreferrer"` : "";
    return stash(
      `<a href="${esc(url)}"${attrs} class="font-medium text-slate-900 underline decoration-slate-300 underline-offset-2 hover:decoration-slate-700">${esc(label)}</a>`
    );
  });

  // Now escape remaining text.
  text = esc(text);

  // Bold first (longer match), then italic. Italic uses word-boundary lookarounds
  // so things like `a*b*c` and `**foo**` don't get partially matched.
  text = text.replace(/\*\*([^*]+?)\*\*/g, '<strong>$1</strong>');
  text = text.replace(/(?<![\w*])\*([^*\n]+?)\*(?![\w*])/g, '<em>$1</em>');

  // Restore stashes.
  text = text.replace(/\x00(\d+)\x00/g, (_, i) => stashes[parseInt(i, 10)]);

  return text;
}

// -----------------------------------------------------------------------------
// Block parser
// -----------------------------------------------------------------------------
function isBlockStart(line) {
  return (
    /^```/.test(line) ||
    /^#{1,6}\s/.test(line) ||
    /^[-*+]\s/.test(line) ||
    /^\d+\.\s/.test(line) ||
    /^>/.test(line) ||
    /^(-{3,}|\*{3,}|_{3,})\s*$/.test(line)
  );
}

function parseTableRow(line) {
  return line.replace(/^\s*\|/, "").replace(/\|\s*$/, "").split("|").map((s) => s.trim());
}
function parseTableAlign(spec) {
  const s = spec.trim();
  if (s.startsWith(":") && s.endsWith(":")) return "center";
  if (s.endsWith(":")) return "right";
  if (s.startsWith(":")) return "left";
  return "left";
}
function looksLikeTableSep(line) {
  return /^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)+\|?\s*$/.test(line);
}

function parseBlocks(md) {
  const lines = md.split(/\r?\n/);
  const blocks = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];

    // Fenced code block.
    const fence = line.match(/^```(\S*)\s*$/);
    if (fence) {
      const lang = fence[1] || "";
      const buf = [];
      i++;
      while (i < lines.length && !/^```\s*$/.test(lines[i])) {
        buf.push(lines[i]);
        i++;
      }
      i++; // skip closing fence
      blocks.push({ type: "code", lang, content: buf.join("\n") });
      continue;
    }

    if (!line.trim()) { i++; continue; }

    // Heading.
    const h = line.match(/^(#{1,6})\s+(.+?)\s*#*\s*$/);
    if (h) {
      blocks.push({ type: "heading", level: h[1].length, text: h[2] });
      i++;
      continue;
    }

    // Horizontal rule.
    if (/^(-{3,}|\*{3,}|_{3,})\s*$/.test(line)) {
      blocks.push({ type: "hr" });
      i++;
      continue;
    }

    // Blockquote.
    if (/^>\s?/.test(line)) {
      const buf = [];
      while (i < lines.length && /^>/.test(lines[i])) {
        buf.push(lines[i].replace(/^>\s?/, ""));
        i++;
      }
      blocks.push({ type: "quote", content: buf.join("\n") });
      continue;
    }

    // Table (current line has |, next is the alignment row).
    if (line.includes("|") && i + 1 < lines.length && looksLikeTableSep(lines[i + 1])) {
      const header = parseTableRow(line);
      const aligns = parseTableRow(lines[i + 1]).map(parseTableAlign);
      const rows = [];
      i += 2;
      while (i < lines.length && lines[i].includes("|") && lines[i].trim()) {
        rows.push(parseTableRow(lines[i]));
        i++;
      }
      blocks.push({ type: "table", header, rows, aligns });
      continue;
    }

    // List.
    if (/^[-*+]\s+/.test(line) || /^\d+\.\s+/.test(line)) {
      const ordered = /^\d+\.\s+/.test(line);
      const itemRe = ordered ? /^\d+\.\s+(.+)$/ : /^[-*+]\s+(.+)$/;
      const items = [];
      while (i < lines.length) {
        const m = lines[i].match(itemRe);
        if (m) {
          const buf = [m[1]];
          i++;
          // Continuation lines indented or just plain (until a block-starting line or blank).
          while (i < lines.length && lines[i].trim() && !lines[i].match(itemRe) && !isBlockStart(lines[i])) {
            buf.push(lines[i].replace(/^\s+/, ""));
            i++;
          }
          items.push(buf.join(" "));
        } else if (!lines[i].trim()) {
          break;
        } else {
          break;
        }
      }
      blocks.push({ type: "list", ordered, items });
      continue;
    }

    // Paragraph.
    const buf = [line];
    i++;
    while (i < lines.length && lines[i].trim() && !isBlockStart(lines[i]) && !(lines[i].includes("|") && i + 1 < lines.length && looksLikeTableSep(lines[i + 1]))) {
      buf.push(lines[i]);
      i++;
    }
    blocks.push({ type: "paragraph", text: buf.join(" ").replace(/\s+/g, " ").trim() });
  }
  return blocks;
}

// -----------------------------------------------------------------------------
// Renderer
// -----------------------------------------------------------------------------
function renderBlock(b, opts = {}) {
  switch (b.type) {
    case "heading": {
      const cls = {
        1: "mt-12 text-3xl font-bold tracking-tight text-slate-900 md:text-4xl",
        2: "mt-12 border-b border-slate-200 pb-2 text-2xl font-semibold tracking-tight text-slate-900 md:text-3xl",
        3: "mt-10 text-xl font-semibold tracking-tight text-slate-900 md:text-2xl",
        4: "mt-8 text-lg font-semibold tracking-tight text-slate-900",
        5: "mt-6 text-base font-semibold text-slate-900",
        6: "mt-6 text-sm font-semibold uppercase tracking-wider text-slate-700",
      };
      const id = slugify(b.text);
      const inner = inline(b.text);
      return `<h${b.level} id="${id}" class="${cls[b.level] || cls[6]}"><a href="#${id}" class="no-underline">${inner}</a></h${b.level}>`;
    }
    case "paragraph":
      return `<p class="mt-4 leading-relaxed text-slate-700">${inline(b.text)}</p>`;
    case "code": {
      // Prism picks up the language from the `language-<lang>` class on <code>.
      // We use a light slate-50 container to match the rest of the page and
      // override Prism's own background via the inline style on <code> below.
      const lang = b.lang || "none";
      const langPill = b.lang
        ? `<div class="absolute right-2 top-2 z-10 rounded bg-slate-200 px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-slate-600">${esc(b.lang)}</div>`
        : "";
      return (
        `<div class="relative mt-4">${langPill}` +
        `<pre class="overflow-x-auto rounded-md border border-slate-200 bg-slate-50 px-4 py-3 text-xs leading-relaxed text-slate-800 !bg-slate-50"><code class="language-${esc(lang)} !bg-transparent">${esc(b.content)}</code></pre>` +
        `</div>`
      );
    }
    case "hr":
      return `<hr class="my-10 border-slate-200" />`;
    case "quote":
      return `<blockquote class="mt-4 border-l-4 border-slate-300 bg-slate-50 px-4 py-2 italic text-slate-600">` +
        renderBlocks(parseBlocks(b.content)) +
        `</blockquote>`;
    case "list": {
      const tag = b.ordered ? "ol" : "ul";
      const listCls = b.ordered ? "list-decimal" : "list-disc";
      const items = b.items.map((it) => `<li class="mt-1.5">${inline(it)}</li>`).join("");
      return `<${tag} class="mt-4 ${listCls} space-y-1 pl-6 text-slate-700">${items}</${tag}>`;
    }
    case "table": {
      const a = (i) => {
        const al = b.aligns[i] || "left";
        return al === "center" ? "text-center" : al === "right" ? "text-right" : "text-left";
      };
      const head = b.header.map((c, i) =>
        `<th class="border-b border-slate-300 px-3 py-2 ${a(i)} text-sm font-semibold text-slate-700">${inline(c)}</th>`
      ).join("");
      const rows = b.rows.map((r) =>
        `<tr>${r.map((c, i) => `<td class="border-b border-slate-100 px-3 py-2 ${a(i)} align-middle text-sm text-slate-700">${inline(c)}</td>`).join("")}</tr>`
      ).join("");
      return `<div class="mt-4 overflow-x-auto"><table class="w-full border-collapse"><thead><tr>${head}</tr></thead><tbody>${rows}</tbody></table></div>`;
    }
    default: return "";
  }
}
function renderBlocks(bs) { return bs.map(renderBlock).join("\n"); }

// -----------------------------------------------------------------------------
// Page templates
// -----------------------------------------------------------------------------
function pageHtml({ slug, title, description, body, prev, next, partNumber, partTitle }) {
  const cleanTitle = partTitle || title;
  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>${esc(cleanTitle)} &mdash; ${esc(SERIES_TITLE)} &middot; The Grand Chess Tree</title>
    <meta name="description" content="${esc(description)}" />
    <meta name="theme-color" content="#0f172a" />
    <link rel="canonical" href="https://grandchesstree.com/move-generator/${esc(slug)}.html" />
    <link rel="author" href="https://timmoth.com/" />
    <meta name="author" content="Tim Jones" />
    <meta name="robots" content="index, follow, max-image-preview:large" />
    <meta name="keywords" content="chess engine, move generation, bitboards, perft, C#, Grand Chess Tree" />
    <link rel="preconnect" href="https://cdn.tailwindcss.com" crossorigin />
    <link
      rel="icon"
      href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'%3E%3Ctext y='.9em' font-size='90'%3E%E2%99%9E%3C/text%3E%3C/svg%3E"
    />

    <meta property="og:type" content="article" />
    <meta property="og:locale" content="en_US" />
    <meta property="og:url" content="https://grandchesstree.com/move-generator/${esc(slug)}.html" />
    <meta property="og:title" content="${esc(cleanTitle)} — ${esc(SERIES_TITLE)}" />
    <meta property="og:description" content="${esc(description)}" />
    <meta property="og:site_name" content="The Grand Chess Tree" />
    <meta property="og:image" content="https://grandchesstree.com/assets/img/og-card.png" />
    <meta property="og:image:width" content="1200" />
    <meta property="og:image:height" content="630" />
    <meta property="og:image:alt" content="The Grand Chess Tree" />
    <meta name="twitter:card" content="summary_large_image" />
    <meta name="twitter:title" content="${esc(cleanTitle)} — ${esc(SERIES_TITLE)}" />
    <meta name="twitter:description" content="${esc(description)}" />
    <meta name="twitter:image" content="https://grandchesstree.com/assets/img/og-card.png" />

    <script type="application/ld+json">
    {
      "@context": "https://schema.org",
      "@type": "TechArticle",
      "headline": "${esc(cleanTitle)}",
      "description": "${esc(description)}",
      "url": "https://grandchesstree.com/move-generator/${esc(slug)}.html",
      "datePublished": "2026-05-23",
      "dateModified": "2026-05-23",
      "author": {
        "@type": "Person",
        "name": "Tim Jones",
        "url": "https://timmoth.com/"
      },
      "publisher": {
        "@type": "Organization",
        "name": "The Grand Chess Tree",
        "url": "https://grandchesstree.com/"
      },
      "isPartOf": {
        "@type": "CreativeWorkSeries",
        "name": "${esc(SERIES_TITLE)}",
        "url": "https://grandchesstree.com/move-generator/"
      },
      "articleSection": "Move generation",
      "inLanguage": "en"
    }
    </script>

    <script src="https://cdn.tailwindcss.com"></script>
    <!-- Prism.js syntax highlighting for code blocks (CDN, no install). -->
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/prismjs@1.29.0/themes/prism.min.css" />
    <style>
      html { font-feature-settings: "cv11", "ss01"; }
      .tabular { font-variant-numeric: tabular-nums; }
      /* Keep our slate-50 container; just let Prism colour the tokens. */
      pre[class*="language-"], code[class*="language-"] { background: transparent !important; text-shadow: none; }
      pre[class*="language-"] { padding: 0; margin: 0; }
    </style>
  </head>
  <body class="bg-white text-slate-700 antialiased selection:bg-slate-200 selection:text-slate-900">
    <a href="#main" class="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-50 focus:rounded-md focus:bg-slate-900 focus:px-4 focus:py-2 focus:text-white">
      Skip to content
    </a>

${renderChrome(HEADER_TPL, { active: "move-generator", base: "../" })}
    <main id="main">
      <section class="border-b border-slate-200 bg-gradient-to-b from-slate-50 to-white">
        <div class="mx-auto max-w-3xl px-6 py-10 md:py-14">
          <nav aria-label="Breadcrumb" class="mb-6 text-sm text-slate-500">
            <ol class="flex flex-wrap items-center gap-2">
              <li><a href="../index.html" class="hover:text-slate-900">Home</a></li>
              <li aria-hidden="true">/</li>
              <li><a href="index.html" class="hover:text-slate-900">Move generator</a></li>
              <li aria-hidden="true">/</li>
              <li class="text-slate-700">Part ${partNumber}</li>
            </ol>
          </nav>
          <p class="text-xs font-medium uppercase tracking-wider text-slate-500">Part ${partNumber} of ${ARTICLES.length}</p>
          <h1 class="mt-2 text-3xl font-bold tracking-tight text-slate-900 md:text-4xl">${esc(cleanTitle)}</h1>
          <p class="mt-3 max-w-2xl text-slate-600">${esc(description)}</p>
        </div>
      </section>

      <article class="mx-auto max-w-3xl px-6 py-10 md:py-14">
        ${body}
      </article>

      <nav class="mx-auto max-w-3xl px-6 pb-16">
        <div class="grid gap-4 sm:grid-cols-2">
          ${prev ? `<a href="${esc(prev.slug)}.html" class="group rounded-2xl border border-slate-200 bg-white p-5 transition hover:-translate-y-0.5 hover:shadow-md">
            <p class="text-xs font-medium uppercase tracking-wider text-slate-500">← Previous</p>
            <p class="mt-1 font-semibold text-slate-900">Part ${prev.partNumber}: ${esc(prev.partTitle || prev.title)}</p>
          </a>` : `<span></span>`}
          ${next ? `<a href="${esc(next.slug)}.html" class="group rounded-2xl border border-slate-200 bg-white p-5 text-right transition hover:-translate-y-0.5 hover:shadow-md">
            <p class="text-xs font-medium uppercase tracking-wider text-slate-500">Next →</p>
            <p class="mt-1 font-semibold text-slate-900">Part ${next.partNumber}: ${esc(next.partTitle || next.title)}</p>
          </a>` : `<span></span>`}
        </div>
      </nav>
    </main>

${renderChrome(FOOTER_TPL, { base: "../" })}
    <!-- Prism core + autoloader: language defs are fetched lazily from the same CDN. -->
    <script src="https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-core.min.js"></script>
    <script src="https://cdn.jsdelivr.net/npm/prismjs@1.29.0/plugins/autoloader/prism-autoloader.min.js"></script>
    <script>Prism.plugins.autoloader.languages_path = "https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/";</script>
  </body>
</html>
`;
}

function hubHtml(articles) {
  const cards = articles.map((a) => `
            <article class="group flex flex-col rounded-2xl border border-slate-200 bg-white p-6 transition hover:-translate-y-0.5 hover:shadow-md">
              <p class="text-xs font-medium uppercase tracking-wider text-slate-500">Part ${a.partNumber}</p>
              <h2 class="mt-2 text-lg font-semibold tracking-tight text-slate-900">${esc(a.partTitle || a.title)}</h2>
              <p class="mt-3 flex-1 text-sm leading-relaxed text-slate-600">${esc(a.description)}</p>
              <a href="${esc(a.slug)}.html" class="mt-4 inline-flex items-center gap-1 font-medium text-slate-900 underline decoration-slate-300 underline-offset-2 group-hover:decoration-slate-700">
                Read part ${a.partNumber}
                <svg aria-hidden="true" class="h-4 w-4 transition group-hover:translate-x-0.5" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 4l6 6-6 6"/></svg>
              </a>
            </article>`).join("\n");

  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>${esc(SERIES_TITLE)} &mdash; The Grand Chess Tree</title>
    <meta name="description" content="${esc(SERIES_DESC)}" />
    <meta name="theme-color" content="#0f172a" />
    <link rel="canonical" href="https://grandchesstree.com/move-generator/" />
    <link rel="author" href="https://timmoth.com/" />
    <meta name="author" content="Tim Jones" />
    <meta name="robots" content="index, follow, max-image-preview:large" />
    <meta name="keywords" content="chess engine, move generation, perft, bitboards, magic bitboards, C#, Grand Chess Tree" />
    <link rel="preconnect" href="https://cdn.tailwindcss.com" crossorigin />
    <link
      rel="icon"
      href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'%3E%3Ctext y='.9em' font-size='90'%3E%E2%99%9E%3C/text%3E%3C/svg%3E"
    />

    <meta property="og:type" content="website" />
    <meta property="og:locale" content="en_US" />
    <meta property="og:url" content="https://grandchesstree.com/move-generator/" />
    <meta property="og:title" content="${esc(SERIES_TITLE)} — The Grand Chess Tree" />
    <meta property="og:description" content="${esc(SERIES_DESC)}" />
    <meta property="og:site_name" content="The Grand Chess Tree" />
    <meta property="og:image" content="https://grandchesstree.com/assets/img/og-card.png" />
    <meta property="og:image:width" content="1200" />
    <meta property="og:image:height" content="630" />
    <meta property="og:image:alt" content="The Grand Chess Tree" />
    <meta name="twitter:card" content="summary_large_image" />
    <meta name="twitter:title" content="${esc(SERIES_TITLE)} — The Grand Chess Tree" />
    <meta name="twitter:description" content="${esc(SERIES_DESC)}" />
    <meta name="twitter:image" content="https://grandchesstree.com/assets/img/og-card.png" />

    <script type="application/ld+json">
    {
      "@context": "https://schema.org",
      "@type": "CreativeWorkSeries",
      "name": "${esc(SERIES_TITLE)}",
      "description": "${esc(SERIES_DESC)}",
      "url": "https://grandchesstree.com/move-generator/",
      "author": {
        "@type": "Person",
        "name": "Tim Jones",
        "url": "https://timmoth.com/"
      },
      "publisher": {
        "@type": "Organization",
        "name": "The Grand Chess Tree",
        "url": "https://grandchesstree.com/"
      },
      "numberOfItems": ${ARTICLES.length},
      "inLanguage": "en"
    }
    </script>

    <script src="https://cdn.tailwindcss.com"></script>
    <!-- Prism.js syntax highlighting for code blocks (CDN, no install). -->
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/prismjs@1.29.0/themes/prism.min.css" />
    <style>
      html { font-feature-settings: "cv11", "ss01"; }
      .tabular { font-variant-numeric: tabular-nums; }
      /* Keep our slate-50 container; just let Prism colour the tokens. */
      pre[class*="language-"], code[class*="language-"] { background: transparent !important; text-shadow: none; }
      pre[class*="language-"] { padding: 0; margin: 0; }
    </style>
  </head>
  <body class="bg-white text-slate-700 antialiased selection:bg-slate-200 selection:text-slate-900">
    <a href="#main" class="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-50 focus:rounded-md focus:bg-slate-900 focus:px-4 focus:py-2 focus:text-white">
      Skip to content
    </a>

${renderChrome(HEADER_TPL, { active: "move-generator", base: "../" })}
    <main id="main">
      <section class="border-b border-slate-200 bg-gradient-to-b from-slate-50 to-white">
        <div class="mx-auto max-w-6xl px-4 sm:px-6 py-14 md:py-20">
          <nav aria-label="Breadcrumb" class="mb-6 text-sm text-slate-500">
            <ol class="flex flex-wrap items-center gap-2">
              <li><a href="../index.html" class="hover:text-slate-900">Home</a></li>
              <li aria-hidden="true">/</li>
              <li class="text-slate-700">Move generator</li>
            </ol>
          </nav>
          <h1 class="text-3xl font-bold tracking-tight text-slate-900 md:text-5xl">${esc(SERIES_TITLE)}</h1>
          <p class="mt-4 max-w-2xl text-slate-600 md:text-lg">${esc(SERIES_DESC)}</p>
        </div>
      </section>

      <section class="py-16 md:py-20">
        <div class="mx-auto max-w-6xl px-4 sm:px-6">
          <div class="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
${cards}
          </div>
        </div>
      </section>
    </main>

${renderChrome(FOOTER_TPL, { base: "../" })}
    <!-- Prism core + autoloader: language defs are fetched lazily from the same CDN. -->
    <script src="https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-core.min.js"></script>
    <script src="https://cdn.jsdelivr.net/npm/prismjs@1.29.0/plugins/autoloader/prism-autoloader.min.js"></script>
    <script>Prism.plugins.autoloader.languages_path = "https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/";</script>
  </body>
</html>
`;
}

// -----------------------------------------------------------------------------
// Main
// -----------------------------------------------------------------------------
function copyImages() {
  fs.mkdirSync(IMG_DEST, { recursive: true });
  for (const f of fs.readdirSync(IMG_SRC)) {
    fs.copyFileSync(path.join(IMG_SRC, f), path.join(IMG_DEST, f));
  }
}

function buildArticleData() {
  const built = [];
  for (let idx = 0; idx < ARTICLES.length; idx++) {
    const { md, slug } = ARTICLES[idx];
    const raw = fs.readFileSync(path.join(ARTICLES_DIR, md), "utf8");
    const blocks = parseBlocks(raw);

    // Pull the first H1 as the title; capture the first paragraph as description.
    let title = `Part ${idx + 1}`;
    let partTitle = "";
    let description = "";
    let h1Idx = blocks.findIndex((b) => b.type === "heading" && b.level === 1);
    if (h1Idx >= 0) {
      title = blocks[h1Idx].text;
      const m = title.match(/Part\s+(\d+)\s*[—\-]\s*(.+)/);
      partTitle = m ? m[2].trim() : title;
      // Strip the H1; description is first paragraph after it.
      const tail = blocks.slice(h1Idx + 1);
      const firstP = tail.find((b) => b.type === "paragraph");
      if (firstP) description = firstP.text;
      blocks.splice(h1Idx, 1);
    }
    const body = renderBlocks(blocks);
    built.push({
      slug, title, partTitle, partNumber: idx + 1, description, body, file: md,
    });
  }
  // Link prev/next.
  for (let i = 0; i < built.length; i++) {
    built[i].prev = i > 0 ? built[i - 1] : null;
    built[i].next = i < built.length - 1 ? built[i + 1] : null;
  }
  return built;
}

function main() {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  copyImages();
  const articles = buildArticleData();

  for (const a of articles) {
    const html = pageHtml(a);
    const out = path.join(OUT_DIR, `${a.slug}.html`);
    fs.writeFileSync(out, html);
    console.log(`wrote ${out}`);
  }
  const hub = path.join(OUT_DIR, "index.html");
  fs.writeFileSync(hub, hubHtml(articles));
  console.log(`wrote ${hub}`);

  console.log(`\nbuilt ${articles.length} articles → ${OUT_DIR}`);
}

if (require.main === module) main();
module.exports = { parseBlocks, renderBlocks, inline };

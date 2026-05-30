/*
 * Save an SVG chess board as a PNG.
 *
 * Inlines any external <image href="..."> nodes as data URIs first so the
 * canvas rasterization is portable across browsers and works regardless of
 * CORS posture. Renders at 2× the SVG viewBox for a crisp result.
 */
(function () {
  "use strict";

  const inlineCache = new Map(); // href → data URI

  async function fetchAsDataUri(href) {
    if (inlineCache.has(href)) return inlineCache.get(href);
    const resp = await fetch(href);
    if (!resp.ok) throw new Error(`fetch ${href}: ${resp.status}`);
    const blob = await resp.blob();
    const dataUri = await new Promise((res, rej) => {
      const r = new FileReader();
      r.onload = () => res(r.result);
      r.onerror = rej;
      r.readAsDataURL(blob);
    });
    inlineCache.set(href, dataUri);
    return dataUri;
  }

  async function inlineImages(svgClone) {
    const XLINK = "http://www.w3.org/1999/xlink";
    const XMLNS = "http://www.w3.org/2000/xmlns/";
    // Declare xmlns:xlink on the root so xlink:href round-trips through XMLSerializer.
    if (!svgClone.getAttribute("xmlns:xlink")) {
      svgClone.setAttributeNS(XMLNS, "xmlns:xlink", XLINK);
    }
    const imgs = Array.from(svgClone.querySelectorAll("image"));
    await Promise.all(imgs.map(async (img) => {
      let href = img.getAttribute("href") || img.getAttributeNS(XLINK, "href");
      if (!href || href.startsWith("data:")) return;
      try {
        const dataUri = await fetchAsDataUri(href);
        // Many SVG-to-canvas rasterizers (Safari, older Chromium) only honour
        // xlink:href, not the SVG2 href attribute. Set both for portability.
        img.setAttribute("href", dataUri);
        img.setAttributeNS(XLINK, "href", dataUri);
      } catch (e) {
        console.warn("[board-export] failed to inline", href, e);
      }
    }));
  }

  function sanitizeFilename(s) {
    return String(s || "board")
      .trim()
      .replace(/[\s/\\:*?"<>|]+/g, "_")
      .replace(/_+/g, "_")
      .slice(0, 100) || "board";
  }

  function triggerDownload(blob, filename) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  /**
   * @param {SVGSVGElement} svg
   * @param {string} filename  Filename (without extension). ".png" is added.
   * @param {{ scale?: number, pixelated?: boolean }} [opts]
   */
  async function saveSvgAsPng(svg, filename, opts) {
    const scale = (opts && opts.scale) || 2;
    const pixelated = !opts || opts.pixelated !== false;

    const clone = svg.cloneNode(true);
    const SVG_NS = "http://www.w3.org/2000/svg";
    // Make sure xmlns is present on the cloned node so it's a valid standalone document.
    if (!clone.getAttribute("xmlns")) clone.setAttribute("xmlns", SVG_NS);
    await inlineImages(clone);

    const vb = svg.viewBox && svg.viewBox.baseVal;
    const w = (vb && vb.width)  || svg.clientWidth  || 400;
    const h = (vb && vb.height) || svg.clientHeight || 400;

    // Set explicit width/height on the clone — needed by Safari to rasterize the SVG document.
    clone.setAttribute("width",  String(w));
    clone.setAttribute("height", String(h));

    // Inject a stylesheet so the rasterizer keeps pixel art crisp instead of bilinear-smoothing.
    // Multiple property values cover every browser: 'pixelated' (Chromium), 'crisp-edges' (Firefox/Safari).
    if (pixelated) {
      const styleEl = document.createElementNS(SVG_NS, "style");
      styleEl.textContent =
        "image{" +
          "image-rendering:pixelated;" +
          "image-rendering:-webkit-optimize-contrast;" +
          "image-rendering:-moz-crisp-edges;" +
          "image-rendering:crisp-edges;" +
        "}";
      clone.insertBefore(styleEl, clone.firstChild);
    }

    const xml = new XMLSerializer().serializeToString(clone);
    const svgBlob = new Blob([xml], { type: "image/svg+xml;charset=utf-8" });
    const svgUrl  = URL.createObjectURL(svgBlob);

    try {
      const img = new Image();
      img.decoding = "sync";
      await new Promise((res, rej) => {
        img.onload = res;
        img.onerror = (e) => rej(new Error("SVG image load failed"));
        img.src = svgUrl;
      });

      const canvas = document.createElement("canvas");
      canvas.width  = Math.round(w * scale);
      canvas.height = Math.round(h * scale);
      const ctx = canvas.getContext("2d");
      ctx.imageSmoothingEnabled = !pixelated;
      ctx.drawImage(img, 0, 0, canvas.width, canvas.height);

      const blob = await new Promise((res) => canvas.toBlob(res, "image/png"));
      if (!blob) throw new Error("canvas.toBlob returned null");
      triggerDownload(blob, sanitizeFilename(filename) + ".png");
    } finally {
      URL.revokeObjectURL(svgUrl);
    }
  }

  if (typeof window !== "undefined") {
    window.GCT = window.GCT || {};
    window.GCT.saveSvgAsPng = saveSvgAsPng;
  }
})();

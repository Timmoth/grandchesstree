/*
 * Loads the .NET-compiled WASM move generator (MoveGen.Wasm) on demand and
 * exposes it via window.GCT.MoveGenWasm.
 *
 * Call .load() to kick off the runtime download (returns a Promise that
 * resolves when ready). After load resolves, .perft() / .perftDivide() are
 * synchronous calls into native AOT code. Falls back gracefully — if load()
 * rejects, callers should use the pure-JS GCT.MoveGen as a backup.
 */
(function () {
  "use strict";

  const DEFAULT_BASE = "wasm/movegen";

  let exports = null;
  let loadPromise = null;
  let lastError = null;

  async function load(basePath) {
    if (loadPromise) return loadPromise;
    const base = basePath || DEFAULT_BASE;
    // dotnet.js is an ES module; dynamic import lets us load it from a non-module page.
    // We resolve relative to the document so it works regardless of where the page lives.
    const dotnetUrl = new URL(`${base}/_framework/dotnet.js`, document.baseURI).href;
    loadPromise = (async () => {
      try {
        const mod = await import(/* @vite-ignore */ dotnetUrl);
        const { dotnet } = mod;
        const runtime = await dotnet
          .withDiagnosticTracing(false)
          .create();
        await runtime.runMain();
        const allExports = await runtime.getAssemblyExports("MoveGen.Wasm");
        exports = allExports.MoveGen.Wasm.Exports;
        return exports;
      } catch (e) {
        lastError = e;
        loadPromise = null; // allow retry
        throw e;
      }
    })();
    return loadPromise;
  }

  function perft(fen, depth) {
    if (!exports) throw new Error("MoveGen.Wasm not loaded — call load() first");
    return exports.Perft(fen, depth);
  }

  function perftDivide(fen, depth) {
    if (!exports) throw new Error("MoveGen.Wasm not loaded — call load() first");
    const text = exports.PerftDivide(fen, depth);
    if (!text) return [];
    return text.split("\n").map((line) => {
      const sp = line.indexOf(" ");
      return { uci: line.slice(0, sp), nodes: BigInt(line.slice(sp + 1)) };
    });
  }

  function engineInfo() { return exports ? exports.EngineInfo() : null; }
  function isReady() { return exports !== null; }
  function getLastError() { return lastError; }

  function legalMovesUci(fen) {
    if (!exports) throw new Error("MoveGen.Wasm not loaded — call load() first");
    const text = exports.LegalMovesUci(fen);
    return text ? text.split(" ") : [];
  }

  function perftRootMove(fen, uci, depth) {
    if (!exports) throw new Error("MoveGen.Wasm not loaded — call load() first");
    return exports.PerftRootMove(fen, uci, depth);
  }

  if (typeof window !== "undefined") {
    window.GCT = window.GCT || {};
    window.GCT.MoveGenWasm = {
      load, perft, perftDivide, perftRootMove, legalMovesUci,
      engineInfo, isReady, getLastError,
    };
  }
})();

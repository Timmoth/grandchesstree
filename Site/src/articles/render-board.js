#!/usr/bin/env node
/*
 * Render a FEN to a PNG using the exact same geometry, colours, and pixel-art
 * rendering rules as the FEN visualizer's Save-PNG button (assets/js/board-export.js).
 *
 * Pipeline:
 *   FEN  →  movegen.parseFen  →  hand-built SVG (piece PNGs inlined as data URIs)
 *        →  rsvg-convert -w <width>  →  PNG
 *
 * Usage:
 *   render-board.js <FEN> <output.png> [--width N] [--flip]
 *
 * Width defaults to 856 (= the FEN visualizer's 428-px viewBox × 2, matching
 * the in-browser Save PNG scale).
 */
"use strict";

const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

// __dirname  =  <repo>/Site/src/articles
// site root  =  <repo>/site
const SITE_ROOT = path.resolve(__dirname, "..", "..");
const MG = require(path.join(SITE_ROOT, "src", "assets", "js", "movegen.js"));
const PIECES_DIR = path.join(SITE_ROOT, "src", "assets", "img", "pieces");

// Match the FEN visualizer constants verbatim.
const SQUARE = 48;
const MARGIN = 22;
const PIECE_SCALE = 0.8;
const BOARD_PX = SQUARE * 8;
const TOTAL = BOARD_PX + MARGIN * 2;

const PIECE_FILE = {
   0: "white_pawn.png",  1: "white_knight.png",  2: "white_bishop.png",
   3: "white_rook.png",  4: "white_queen.png",   5: "white_king.png",
   6: "black_pawn.png",  7: "black_knight.png",  8: "black_bishop.png",
   9: "black_rook.png", 10: "black_queen.png",  11: "black_king.png",
};

const pieceDataUris = {};
for (const [idx, file] of Object.entries(PIECE_FILE)) {
  const buf = fs.readFileSync(path.join(PIECES_DIR, file));
  pieceDataUris[idx] = "data:image/png;base64," + buf.toString("base64");
}

/**
 * Render a position SVG. If `bitboard` is non-null (a BigInt), no pieces are drawn —
 * instead set bits get a translucent green overlay + bit-index label, matching the
 * bitboard inspector.
 */
function renderSvg({ fen = null, bitboard = null, flipped = false } = {}) {
  if (fen == null && bitboard == null) throw new Error("renderSvg needs fen or bitboard");
  const pos = fen ? MG.parseFen(fen) : null;
  if (fen && !pos) throw new Error(`Invalid FEN: ${fen}`);

  const out = [];
  out.push(
    `<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" ` +
    `width="${TOTAL}" height="${TOTAL}" viewBox="0 0 ${TOTAL} ${TOTAL}">`
  );
  out.push(
    `<style>image{` +
      `image-rendering:pixelated;` +
      `image-rendering:-webkit-optimize-contrast;` +
      `image-rendering:-moz-crisp-edges;` +
      `image-rendering:crisp-edges;` +
    `}</style>`
  );
  out.push(`<rect x="0" y="0" width="${TOTAL}" height="${TOTAL}" rx="6" fill="#1f2937"/>`);

  const pieceSize = SQUARE * PIECE_SCALE;
  const pieceInset = (SQUARE - pieceSize) / 2;

  for (let dr = 0; dr < 8; dr++) {
    for (let df = 0; df < 8; df++) {
      const rank = flipped ? dr : 7 - dr;
      const file = flipped ? 7 - df : df;
      const sq = rank * 8 + file;
      const x = MARGIN + df * SQUARE;
      const y = MARGIN + dr * SQUARE;
      const isLight = (rank + file) % 2 === 1;
      out.push(
        `<rect x="${x}" y="${y}" width="${SQUARE}" height="${SQUARE}" ` +
        `fill="${isLight ? "#f0d9b5" : "#b58863"}"/>`
      );

      if (pos) {
        const piece = pos.squares[sq];
        if (piece >= 0) {
          const uri = pieceDataUris[piece];
          out.push(
            `<image x="${x + pieceInset}" y="${y + pieceInset}" ` +
            `width="${pieceSize}" height="${pieceSize}" ` +
            `preserveAspectRatio="xMidYMid meet" ` +
            `xlink:href="${uri}" href="${uri}"/>`
          );
        }
      }

      if (bitboard != null && ((bitboard >> BigInt(sq)) & 1n) === 1n) {
        out.push(
          `<rect x="${x}" y="${y}" width="${SQUARE}" height="${SQUARE}" ` +
          `fill="#22c55e" fill-opacity="0.55"/>`
        );
        out.push(
          `<text x="${x + SQUARE / 2}" y="${y + SQUARE / 2}" ` +
          `text-anchor="middle" dominant-baseline="middle" ` +
          `font-size="13" font-weight="600" font-family="ui-monospace, monospace" ` +
          `fill="#064e3b">${sq}</text>`
        );
      }
    }
  }

  // File labels (a–h) in the bottom margin.
  for (let df = 0; df < 8; df++) {
    const file = flipped ? 7 - df : df;
    const cx = MARGIN + df * SQUARE + SQUARE / 2;
    const cy = MARGIN + BOARD_PX + MARGIN / 2;
    out.push(
      `<text x="${cx}" y="${cy}" text-anchor="middle" dominant-baseline="middle" ` +
      `font-size="12" font-weight="600" font-family="ui-monospace, monospace" ` +
      `fill="#e2e8f0">${"abcdefgh"[file]}</text>`
    );
  }
  // Rank labels (1–8) in the left margin.
  for (let dr = 0; dr < 8; dr++) {
    const rank = flipped ? dr : 7 - dr;
    const cx = MARGIN / 2;
    const cy = MARGIN + dr * SQUARE + SQUARE / 2;
    out.push(
      `<text x="${cx}" y="${cy}" text-anchor="middle" dominant-baseline="middle" ` +
      `font-size="12" font-weight="600" font-family="ui-monospace, monospace" ` +
      `fill="#e2e8f0">${rank + 1}</text>`
    );
  }

  out.push(`</svg>`);
  return out.join("\n");
}

function which(cmd) {
  try { return execSync(`command -v ${cmd}`, { encoding: "utf8" }).trim(); }
  catch (_) { return null; }
}

function svgToPng(svg, outPath, width) {
  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  const tmpSvg = outPath + ".tmp.svg";
  fs.writeFileSync(tmpSvg, svg);
  try {
    if (which("rsvg-convert")) {
      execSync(`rsvg-convert -w ${width} -o "${outPath}" "${tmpSvg}"`, { stdio: "inherit" });
    } else if (which("magick")) {
      execSync(`magick -background none -density 300 "${tmpSvg}" -resize ${width}x "${outPath}"`, { stdio: "inherit" });
    } else if (which("convert")) {
      execSync(`convert -background none -density 300 "${tmpSvg}" -resize ${width}x "${outPath}"`, { stdio: "inherit" });
    } else {
      throw new Error("Need rsvg-convert or ImageMagick (magick/convert) on PATH");
    }
  } finally {
    fs.unlinkSync(tmpSvg);
  }
}

function main() {
  const args = process.argv.slice(2);
  let mode = "fen";       // "fen" | "bitboard"
  let positional = [];
  let width = TOTAL * 2;  // = 856
  let flipped = false;
  for (let i = 0; i < args.length; i++) {
    if (args[i] === "--bitboard") mode = "bitboard";
    else if (args[i] === "--width") width = parseInt(args[++i], 10);
    else if (args[i] === "--flip") flipped = true;
    else positional.push(args[i]);
  }
  if (positional.length < 2) {
    console.error("usage:");
    console.error("  render-board.js <fen>         <output.png> [--width N] [--flip]");
    console.error("  render-board.js --bitboard <hex64> <output.png> [--width N] [--flip]");
    process.exit(2);
  }
  const arg = positional[0];
  const outPath = path.resolve(positional[1]);

  let svg;
  if (mode === "bitboard") {
    let s = arg.replace(/[_\s,]/g, "");
    if (s.startsWith("0x") || s.startsWith("0X")) s = s.slice(2);
    const bb = BigInt("0x" + s);
    if (bb < 0n || bb > (1n << 64n) - 1n) throw new Error("bitboard value out of 64-bit range");
    svg = renderSvg({ bitboard: bb, flipped });
  } else {
    svg = renderSvg({ fen: arg, flipped });
  }
  svgToPng(svg, outPath, width);
  console.log(`wrote ${outPath} (${width}px wide)`);
}

if (require.main === module) main();
module.exports = { renderSvg, svgToPng };

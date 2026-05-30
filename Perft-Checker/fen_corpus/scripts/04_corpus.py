#!/usr/bin/env python3
"""Stage 4: validate, dedupe, and merge extracted FENs into a single CSV.

Reads every extracted/<hash>.jsonl produced by 03_extract.py. Drops malformed
FENs. Deduplicates on the first four FEN fields (board, side, castling, EP).

For each FEN we also compute a `context_quality` tag based on the smallest
source page contributing it — a FEN that's one of 5 in a talkchess thread is
HIGH context (the page is about that FEN); a FEN that's one of 5,000 in
`book.epd` is LOW context (the page is a bulk dump with no per-FEN
commentary). Used downstream to surface useful source URLs when an engine
fails on a corpus FEN.

Output CSV columns:
  fen, source_urls, sources_count, min_page_fens, context_quality
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import config  # noqa: E402


_RANK_RE   = re.compile(r"^[1-8pnbrqkPNBRQK]+$")
_EP_RE     = re.compile(r"^(-|[a-h][36])$")
_CAST_RE   = re.compile(r"^(-|[KQkqA-Ha-h]+)$")  # tolerant: also accept X-FEN files


def validate_fen(fen: str) -> bool:
    parts = fen.split()
    if len(parts) != 6:
        return False
    board, side, cast, ep, hm, fm = parts
    ranks = board.split("/")
    if len(ranks) != 8:
        return False
    for rk in ranks:
        if not _RANK_RE.match(rk):
            return False
        n = 0
        for c in rk:
            n += int(c) if c.isdigit() else 1
        if n != 8:
            return False
    if side not in ("w", "b"):
        return False
    if not _CAST_RE.match(cast):
        return False
    if not _EP_RE.match(ep):
        return False
    if not (hm.isdigit() and fm.isdigit()):
        return False
    return True


def dedup_key(fen: str) -> str:
    return " ".join(fen.split()[:4])


def context_quality(min_page_fens: int,
                    high_max: int, medium_max: int) -> str:
    if min_page_fens <= high_max:
        return "high"
    if min_page_fens <= medium_max:
        return "medium"
    return "low"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--high-max", type=int, default=30,
                    help="max FEN count per source page for HIGH context")
    ap.add_argument("--medium-max", type=int, default=1000,
                    help="max FEN count per source page for MEDIUM context "
                         "(above this is LOW — bulk dump)")
    args = ap.parse_args()

    config.ensure_dirs()

    # Pass 1: count how many *valid* FENs come from each source URL.
    # This is what context_quality keys off — a discussion page typically has
    # one or a handful of FENs, a bulk EPD file has thousands.
    fens_per_source: dict[str, int] = defaultdict(int)
    for p in sorted(config.EXTRACTED_DIR.glob("*.jsonl")):
        for line in p.read_text().splitlines():
            if not line.strip():
                continue
            try:
                rec = json.loads(line)
            except json.JSONDecodeError:
                continue
            fen = (rec.get("fen") or "").strip()
            if not validate_fen(fen):
                continue
            src = rec.get("source_url") or ""
            if src:
                fens_per_source[src] += 1

    # Pass 2: dedupe per FEN and merge sources.
    by_key: dict[str, dict] = defaultdict(lambda: {
        "fen": None,
        "source_urls": set(),
    })
    raw = invalid = 0
    for p in sorted(config.EXTRACTED_DIR.glob("*.jsonl")):
        for line in p.read_text().splitlines():
            if not line.strip():
                continue
            try:
                rec = json.loads(line)
            except json.JSONDecodeError:
                continue
            raw += 1
            fen = (rec.get("fen") or "").strip()
            if not validate_fen(fen):
                invalid += 1
                continue
            k = dedup_key(fen)
            slot = by_key[k]
            if slot["fen"] is None:
                slot["fen"] = fen
            if rec.get("source_url"):
                slot["source_urls"].add(rec["source_url"])

    quality_counts = {"high": 0, "medium": 0, "low": 0}
    with config.CORPUS_CSV.open("w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["fen", "source_urls", "sources_count",
                    "min_page_fens", "context_quality"])
        for slot in sorted(by_key.values(), key=lambda s: s["fen"] or ""):
            if slot["fen"] is None:
                continue
            sources = sorted(slot["source_urls"])
            counts = [fens_per_source.get(s, 0) for s in sources] or [0]
            # min_page_fens = how few FENs the *best* (most-focused) source
            # contributing this FEN had. Lower = more per-FEN discussion.
            min_pf = min(counts)
            quality = context_quality(min_pf, args.high_max, args.medium_max)
            quality_counts[quality] += 1
            # Sort sources by FEN count ascending so the highest-context URL
            # (smallest page) appears first — that's what we want to surface.
            sorted_sources = sorted(
                sources, key=lambda s: fens_per_source.get(s, 0))
            w.writerow([
                slot["fen"],
                " | ".join(sorted_sources),
                len(sources),
                min_pf,
                quality,
            ])

    print(f"Read {raw} extractions: {invalid} invalid")
    print(f"Wrote {len(by_key)} unique positions → {config.CORPUS_CSV}")
    print(f"Context quality: "
          f"high={quality_counts['high']}, "
          f"medium={quality_counts['medium']}, "
          f"low={quality_counts['low']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

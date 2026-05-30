#!/usr/bin/env python3
"""One-shot: fill remaining d7 cells using mperft instead of TGCT.

TGCT's MT divide crashes with AccessViolationException on a small handful
of pathological positions (multi-queen, mass-promotion). mperft handles
them without complaint, so for the d7 long-tail we shell out to mperft
once per missing FEN and write the results into perft_results.jsonl in
the same schema 08_perft.py uses (so the existing merge picks them up).

Usage:
    python3 08b_mperft_fill.py --depth 7
    # then re-run 08_perft.py merge or python3 -c "<inline merge>" to
    # propagate into static_analysis.jsonl.

Per-position command:
    mperft --fen "<FEN>" --depth N --div --nullmove --threads <T>
           --hash <MB> --quiet

mperft output:
     d3d6       8,736 positions in 0.000 642.832 Mpos/s
    d7d8B    351,079 positions in 0.000 749.380 Mpos/s
    ...
    total   :  24,190,576 positions in 0.031 769.286 Mpos/s

Promotion chars come out uppercase (B/N/Q/R); we lowercase for parity
with TGCT's divide output.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import config  # noqa: E402


STATIC_ANALYSIS = config.ROOT / "static_analysis.jsonl"
RESULTS_FILE    = config.ROOT / "perft_results.jsonl"
DEFAULT_MPERFT  = (config.ROOT.parent / "bin" / "mperft" / "mperft")


# mperft output regexes
_MOVE_RE  = re.compile(r"^\s*([a-h][1-8][a-h][1-8][BNRQbnrq]?)\s+([\d,]+)\s+positions\b")
_TOTAL_RE = re.compile(r"^total\s*:\s*([\d,]+)\s+positions\b", re.IGNORECASE)


def find_missing_fens(max_depth: int) -> list[tuple[str, str]]:
    """Yield (fen_key, fen) where d_{max_depth-1} > 0 but d_{max_depth} == 0."""
    missing = []
    with STATIC_ANALYSIS.open() as f:
        for line in f:
            if not line.strip(): continue
            try: row = json.loads(line)
            except json.JSONDecodeError: continue
            if max_depth > 1 and row.get(f"d{max_depth-1}", 0) == 0:
                continue
            if row.get(f"d{max_depth}", 0) > 0:
                continue
            missing.append((row["fen_key"], row["fen"]))
    return missing


def already_computed(fen_key: str, depth: int) -> bool:
    """Skip positions that were filled in a prior mperft run."""
    if not RESULTS_FILE.exists(): return False
    with RESULTS_FILE.open() as f:
        for line in f:
            try: r = json.loads(line)
            except Exception: continue
            if (r.get("fen_key") == fen_key
                and int(r.get("d", 0)) == depth
                and r.get("nodes") is not None
                and int(r["nodes"]) > 0):
                return True
    return False


def run_mperft(binary: Path, fen: str, depth: int,
               threads: int, hash_mb: int, timeout: float
               ) -> tuple[int | None, dict[str, int], float, str | None]:
    """Returns (nodes, divide, elapsed, error)."""
    cmd = [
        str(binary),
        "--fen",    fen,
        "--depth",  str(depth),
        "--div",
        "--nullmove",
        "--threads", str(threads),
        "--hash",   str(hash_mb),
        "--quiet",
    ]
    start = time.time()
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True,
                              timeout=timeout)
    except subprocess.TimeoutExpired:
        return None, {}, time.time() - start, "timeout"

    elapsed = time.time() - start
    if proc.returncode != 0:
        return None, {}, elapsed, f"exit={proc.returncode}: {proc.stderr.strip()[:200]}"

    divide: dict[str, int] = {}
    total: int | None = None
    for line in proc.stdout.splitlines():
        mm = _MOVE_RE.match(line)
        if mm:
            divide[mm.group(1).lower()] = int(mm.group(2).replace(",", ""))
            continue
        tm = _TOTAL_RE.match(line)
        if tm:
            total = int(tm.group(1).replace(",", ""))

    if total is None:
        return None, divide, elapsed, "no-total-line"
    if sum(divide.values()) != total:
        return total, divide, elapsed, (
            f"divide-sum {sum(divide.values())} != total {total}")
    return total, divide, elapsed, None


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--depth", type=int, default=7,
                    help="target depth to fill (default 7)")
    ap.add_argument("--mperft", default=str(DEFAULT_MPERFT),
                    help="path to mperft binary")
    ap.add_argument("--threads", type=int, default=os.cpu_count() or 8,
                    help="threads passed to mperft --threads")
    ap.add_argument("--hash-mb", type=int, default=4096,
                    help="hash table MB passed to mperft --hash")
    ap.add_argument("--timeout", type=float, default=14400.0,
                    help="per-position timeout in seconds (default 4h)")
    args = ap.parse_args()

    binary = Path(args.mperft)
    if not binary.exists():
        print(f"mperft not found at {binary}", file=sys.stderr)
        return 1
    if not STATIC_ANALYSIS.exists():
        print(f"no static_analysis at {STATIC_ANALYSIS}", file=sys.stderr)
        return 1

    missing = find_missing_fens(args.depth)
    print(f"Missing d{args.depth} cells: {len(missing)}")
    todo = [(k, f) for k, f in missing
            if not already_computed(k, args.depth)]
    print(f"To compute: {len(todo)} "
          f"(skipping {len(missing) - len(todo)} already filled)")
    if not todo:
        return 0

    with RESULTS_FILE.open("a") as out:
        for i, (key, fen) in enumerate(todo, 1):
            print(f"\n[{i}/{len(todo)}] {fen}")
            nodes, divide, elapsed, err = run_mperft(
                binary, fen, args.depth,
                args.threads, args.hash_mb, args.timeout)
            rec = {
                "fen_key":     key,
                "d":           args.depth,
                "nodes":       nodes,
                "divide":      divide,
                "elapsed_sec": round(elapsed, 4),
                "engine":      "mperft",
                "worker":      0,
                "error":       err,
            }
            out.write(json.dumps(rec, ensure_ascii=False) + "\n")
            out.flush()
            if nodes is None:
                print(f"   FAILED in {elapsed:.1f}s: {err}")
            else:
                print(f"   nodes={nodes:,d}  moves={len(divide)}  in {elapsed:.1f}s")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

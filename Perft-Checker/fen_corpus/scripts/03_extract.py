#!/usr/bin/env python3
"""Stage 3: extract every FEN from each cached page with a regex.

No LLM. Just a pattern match against the canonical six-field FEN form,
followed by validate_fen() to drop false positives. One jsonl line per FEN
found, one file per page (matching the page-cache hash).

Re-running is safe — by default each page is processed once. Pass --force to
re-extract pages that already have an output file (e.g. after broadening the
regex).
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import config  # noqa: E402

# Match a six-field FEN. The board portion is matched permissively (allow the
# usual algebraic chars + slashes); validate_fen() does the strict checks.
_FEN_RE = re.compile(
    r"(?<![A-Za-z0-9/])"
    r"([1-8pnbrqkPNBRQK/]{15,})"             # board
    r"\s+([wb])"                              # side to move
    r"\s+(-|[KQkqA-Ha-h]{1,4})"               # castling (incl. X-FEN files)
    r"\s+(-|[a-h][36])"                       # en passant
    r"\s+(\d{1,4})"                            # halfmove
    r"\s+(\d{1,4})"                            # fullmove
    r"(?![A-Za-z0-9])"
)
_RANK_RE = re.compile(r"^[1-8pnbrqkPNBRQK]+$")


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
        n = sum(int(c) if c.isdigit() else 1 for c in rk)
        if n != 8:
            return False
    if side not in ("w", "b"):
        return False
    if not re.match(r"^(-|[KQkqA-Ha-h]+)$", cast):
        return False
    if not re.match(r"^(-|[a-h][36])$", ep):
        return False
    return hm.isdigit() and fm.isdigit()


def extract(text: str) -> list[str]:
    seen: set[str] = set()
    out: list[str] = []
    for m in _FEN_RE.finditer(text):
        fen = " ".join(m.groups())
        if not validate_fen(fen):
            continue
        key = " ".join(fen.split()[:4])
        if key in seen:
            continue
        seen.add(key)
        out.append(fen)
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--force", action="store_true",
                    help="re-extract even if an output file already exists")
    args = ap.parse_args()

    config.ensure_dirs()

    processed = empty = 0
    total_fens = 0
    for p in sorted(config.PAGES_DIR.glob("*.json")):
        out_path = config.EXTRACTED_DIR / f"{p.stem}.jsonl"
        if out_path.exists() and not args.force:
            continue
        rec = json.loads(p.read_text())
        if rec.get("status") != 200 or not rec.get("text", "").strip():
            out_path.write_text("")
            empty += 1
            continue

        fens = extract(rec["text"])
        with out_path.open("w") as f:
            for fen in fens:
                f.write(json.dumps({"fen": fen, "source_url": rec["url"]}) + "\n")
        if not fens:
            empty += 1
        else:
            print(f"  {len(fens):>4}  {rec['url']}")
        processed += 1
        total_fens += len(fens)

    print(f"\nProcessed {processed} pages, {empty} produced no FENs, "
          f"{total_fens} FENs total")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

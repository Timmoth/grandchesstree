#!/usr/bin/env python3
"""Stage 9: pack static_analysis.jsonl into a compact gzipped binary.

The JSONL is great for authoring + greppability but a poor distribution
format — at ~400 MB it's far too large to bundle with the perftcheck
binary. This stage emits `corpus.gctc.gz`, a custom binary container
designed for size:

  * URLs deduplicated into a global table (most FENs share the same
    source URLs — collapsing them is the biggest win).
  * Tag vocabulary deduplicated into a small table; each row carries a
    list of tag-ids instead of strings.
  * UCI moves packed into 16 bits (6b from | 6b to | 4b promo) instead
    of 4–5-byte ASCII strings.
  * Variable-length integers (LEB128) for everything sized.
  * Outer gzip — already part of stdlib in both Python and .NET, so the
    consumer never gains a third-party dependency.

Layout (after un-gzipping):

  [4 bytes]  magic         = "GCTC"
  [2 bytes]  version       = 1                       (u16 LE)
  [4 bytes]  position_count                          (u32 LE)

  [varint]   url_count
  for each url:
    [varint]   len
    [bytes]    utf8

  [u8]       tag_count
  for each tag:
    [u8]       len
    [bytes]    utf8

  for each position (position_count times):
    [varint]   fen_len   [bytes] fen_utf8
    [u8]       context_quality (0=high, 1=medium, 2=low, 3=unknown)
    [varint]   tag_count_here  [u8 × N] tag_ids       (ids ≤ 255 always)
    [varint]   url_count_here  [varint × N] url_ids   (ids into url_table)
    [varint × 7]                                       d1…d7 totals
    for d in 1..7:
      [varint] divide_count
      for each move:
        [u16 LE] move_id   (6b from | 6b to | 4b promo)
        [varint] node_count

The consumer reads the gzipped stream in one pass and yields one record
per position, exactly matching the EpdCase shape downstream code expects.

Stdlib only.
"""

from __future__ import annotations

import argparse
import gzip
import io
import json
import struct
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import config  # noqa: E402


SRC_FILE  = config.ROOT / "static_analysis.jsonl"
DST_FILE  = config.ROOT / "corpus.gctc.gz"

MAGIC      = b"GCTC"
VERSION    = 1

QUALITY_TO_ID = {"high": 0, "medium": 1, "low": 2}
ID_TO_QUALITY = {v: k for k, v in QUALITY_TO_ID.items()}
QUALITY_UNKNOWN = 3


# ---------- varint / move encoding ----------

def write_varint(buf: io.BytesIO, value: int) -> None:
    """LEB128 unsigned."""
    if value < 0:
        raise ValueError(f"varint expects unsigned, got {value}")
    while True:
        byte = value & 0x7F
        value >>= 7
        if value:
            buf.write(bytes((byte | 0x80,)))
        else:
            buf.write(bytes((byte,)))
            return


def encode_move(uci: str) -> int:
    """UCI move string → 16-bit packed move id.
       bits 0-5: from square (0-63)
       bits 6-11: to square (0-63)
       bits 12-15: promotion (0=none, 1=q, 2=r, 3=b, 4=n)
    """
    if len(uci) < 4 or len(uci) > 5:
        raise ValueError(f"bad UCI move: {uci!r}")
    f_file = ord(uci[0]) - ord('a');  f_rank = ord(uci[1]) - ord('1')
    t_file = ord(uci[2]) - ord('a');  t_rank = ord(uci[3]) - ord('1')
    if not (0 <= f_file < 8 and 0 <= f_rank < 8 and 0 <= t_file < 8 and 0 <= t_rank < 8):
        raise ValueError(f"out-of-range squares in {uci!r}")
    promo = 0
    if len(uci) == 5:
        promo = {"q": 1, "r": 2, "b": 3, "n": 4}.get(uci[4].lower(), 0)
        if promo == 0:
            raise ValueError(f"bad promo char in {uci!r}")
    frm = f_rank * 8 + f_file
    to  = t_rank * 8 + t_file
    return (frm & 0x3F) | ((to & 0x3F) << 6) | ((promo & 0xF) << 12)


def decode_move(packed: int) -> str:
    """Inverse of encode_move; for roundtrip checks."""
    frm   = packed & 0x3F
    to    = (packed >> 6) & 0x3F
    promo = (packed >> 12) & 0xF
    f_file = frm % 8;  f_rank = frm // 8
    t_file = to % 8;   t_rank = to // 8
    s = (f"{chr(ord('a') + f_file)}{f_rank + 1}"
         f"{chr(ord('a') + t_file)}{t_rank + 1}")
    if promo:
        s += "qrbn"[promo - 1]
    return s


# ---------- pack ----------

def is_chess960_row(row: dict) -> bool:
    """Chess960 positions use X-FEN castling (file-letter notation like
    `Gge`). Most engines either don't support Chess960 at all or require
    `setoption UCI_Chess960 value true` first — neither of which
    perftcheck-driven checks can rely on out of the box. Skip them in
    the packed corpus by default to keep the test surface engine-portable."""
    if row.get("is_chess960"):
        return True
    return "chess960" in (row.get("tags") or ())


# ---------- illegal-FEN filter ----------
#
# A FEN that survives 07_describe's basic structural checks can still be
# unreachable by any sequence of legal moves. The most common case in the
# wild is "side-not-to-move has their king in check" — that means the
# previous mover either ignored an existing check or moved themselves
# into one, both of which are illegal. Engines accept these FENs and
# happily produce *some* perft count from them, but the counts vary
# wildly between engines (Stockfish refuses; mperft and TGCT compute
# different answers) so the corpus's reference numbers are meaningless.
# Skip them.

def _parse_board(board_str: str) -> list[list[str]] | None:
    """Return 8×8 grid (row 0 = rank 8, row 7 = rank 1) or None if malformed.
       Empty squares are '.'."""
    rows = board_str.split("/")
    if len(rows) != 8: return None
    grid: list[list[str]] = []
    for raw in rows:
        row: list[str] = []
        for ch in raw:
            if ch.isdigit():
                row.extend(["."] * int(ch))
            elif ch in "PNBRQKpnbrqk":
                row.append(ch)
            else:
                return None
        if len(row) != 8: return None
        grid.append(row)
    return grid


def _square_attacked(grid: list[list[str]], target: tuple[int, int],
                     attacker_is_white: bool) -> bool:
    """Is grid[target] attacked by any piece of the attacker side?"""
    tr, tc = target
    if attacker_is_white:
        pawn, knight, bishop, rook, queen, king = "PNBRQK"
    else:
        pawn, knight, bishop, rook, queen, king = "pnbrqk"

    # Pawn attacks. White pawns attack diagonally toward rank 8 (row → 0);
    # black pawns toward rank 1 (row → 7).
    pr = tr + 1 if attacker_is_white else tr - 1
    if 0 <= pr < 8:
        for dc in (-1, 1):
            pc = tc + dc
            if 0 <= pc < 8 and grid[pr][pc] == pawn:
                return True

    # Knight.
    for dr, dc in ((-2,-1),(-2,1),(-1,-2),(-1,2),(1,-2),(1,2),(2,-1),(2,1)):
        r, c = tr + dr, tc + dc
        if 0 <= r < 8 and 0 <= c < 8 and grid[r][c] == knight:
            return True

    # King (so the two kings can't legally sit adjacent).
    for dr in (-1, 0, 1):
        for dc in (-1, 0, 1):
            if dr == 0 and dc == 0: continue
            r, c = tr + dr, tc + dc
            if 0 <= r < 8 and 0 <= c < 8 and grid[r][c] == king:
                return True

    # Diagonals — bishop or queen, stopping at the first non-empty square.
    for dr, dc in ((-1,-1),(-1,1),(1,-1),(1,1)):
        r, c = tr + dr, tc + dc
        while 0 <= r < 8 and 0 <= c < 8:
            piece = grid[r][c]
            if piece != ".":
                if piece == bishop or piece == queen: return True
                break
            r += dr; c += dc

    # Orthogonals — rook or queen.
    for dr, dc in ((-1,0),(1,0),(0,-1),(0,1)):
        r, c = tr + dr, tc + dc
        while 0 <= r < 8 and 0 <= c < 8:
            piece = grid[r][c]
            if piece != ".":
                if piece == rook or piece == queen: return True
                break
            r += dr; c += dc

    return False


def _castling_inconsistent(grid: list[list[str]], castling: str) -> bool:
    """True if the castling field claims rights that aren't physically
    backed up by the board (e.g. `K` without a white king on e1 + white
    rook on h1). Different engines silently sanitize these differently
    (Stockfish strips invalid rights; mperft and TGCT may not), so the
    corpus's reference values can't be reconciled across engines — skip
    them rather than ship unreliable totals.

    Note: standard KQkq letters only. X-FEN file-letter castling (chess
    960) is excluded by the chess960 filter before this runs.
    """
    if castling == "-": return False
    # grid coordinates: row 0 = rank 8, row 7 = rank 1, col 0 = a-file.
    white_king_e1 = grid[7][4] == "K"
    black_king_e8 = grid[0][4] == "k"
    white_rook_a1 = grid[7][0] == "R"
    white_rook_h1 = grid[7][7] == "R"
    black_rook_a8 = grid[0][0] == "r"
    black_rook_h8 = grid[0][7] == "r"
    for ch in castling:
        if ch == "K" and not (white_king_e1 and white_rook_h1): return True
        if ch == "Q" and not (white_king_e1 and white_rook_a1): return True
        if ch == "k" and not (black_king_e8 and black_rook_h8): return True
        if ch == "q" and not (black_king_e8 and black_rook_a8): return True
    return False


def _count_pieces(grid: list[list[str]]) -> dict[str, int]:
    counts: dict[str, int] = {}
    for row in grid:
        for sq in row:
            if sq != ".":
                counts[sq] = counts.get(sq, 0) + 1
    return counts


def _ep_square_invalid(grid: list[list[str]], side: str, ep: str) -> bool:
    """`ep` is the FEN's en-passant target. If set, it must reflect a
    double-pawn-push the *previous* player just made:
      - white to move → ep is on rank 6 (e.g. f6), the pushed black pawn
        sits on rank 5 directly below the ep square, and rank 6 itself
        is empty (the square the pawn passed through).
      - black to move → ep is on rank 3, the pushed white pawn sits on
        rank 4, rank 3 itself is empty.
    Anything else (square not on rank 3/6, no matching pawn, occupied
    target square) is a phantom EP — different engines interpret it
    inconsistently, so skip the position.
    """
    if ep == "-" or not ep: return False
    if len(ep) != 2: return True
    file_ch, rank_ch = ep[0], ep[1]
    if file_ch < "a" or file_ch > "h": return True
    file_idx = ord(file_ch) - ord("a")
    if side == "w" and rank_ch == "6":
        ep_row = 2          # rank 6 = grid row 2
        pawn_row = 3        # the black pawn that double-pushed sits on rank 5
        pawn_char = "p"
    elif side == "b" and rank_ch == "3":
        ep_row = 5          # rank 3 = grid row 5
        pawn_row = 4        # the white pawn that double-pushed sits on rank 4
        pawn_char = "P"
    else:
        return True
    if grid[ep_row][file_idx] != ".":              return True
    if grid[pawn_row][file_idx] != pawn_char:      return True
    # The square the pawn would have come from (rank 7 / rank 2) must be
    # empty, otherwise the double-push couldn't have happened.
    origin_row = 1 if side == "w" else 6
    if grid[origin_row][file_idx] != ".":          return True
    return False


def is_illegal_row(row: dict) -> bool:
    """True iff the FEN can't have arisen from legal play.

    Checks:
      1. Wrong king count (each side must have exactly one).
      2. Side-not-to-move's king is in check (their last move would have
         either left them in check or moved them into one).
      3. KQkq castling rights inconsistent with piece placement.
      4. En-passant target inconsistent with board (phantom ep).

    Stricter rules (double-check arrangements that can't arise from a
    single move, etc.) aren't checked — needs more state and matters
    less in practice.
    """
    fen = row.get("fen")
    if not isinstance(fen, str): return False
    parts = fen.split()
    if len(parts) < 4: return False
    grid = _parse_board(parts[0])
    if grid is None: return False
    side = parts[1]
    if side not in ("w", "b"): return False

    # (1) Exactly one king of each colour.
    counts = _count_pieces(grid)
    if counts.get("K", 0) != 1: return True
    if counts.get("k", 0) != 1: return True

    # (3) Castling rights ↔ piece placement.
    if _castling_inconsistent(grid, parts[2]): return True

    # (4) En-passant target ↔ board state.
    if _ep_square_invalid(grid, side, parts[3]): return True

    # (2) Opposite king in check.
    opponent_king = "k" if side == "w" else "K"
    pos = None
    for r in range(8):
        for c in range(8):
            if grid[r][c] == opponent_king:
                pos = (r, c); break
        if pos is not None: break
    # (Won't be None here because of the king-count check above, but
    # keep the guard for robustness against future filter reordering.)
    if pos is None: return True
    return _square_attacked(grid, pos, attacker_is_white=(side == "w"))


def collect_vocabularies(src: Path, include_chess960: bool,
                         include_illegal: bool
                         ) -> tuple[list[str], list[str]]:
    """One pass over the JSONL to build URL + tag tables, ordered by
       descending frequency so the hottest entries get the smallest ids
       (cheaper varints). Each table is a list[str] indexed by id."""
    url_counts: Counter[str] = Counter()
    tag_counts: Counter[str] = Counter()
    with src.open() as f:
        for line in f:
            if not line.strip(): continue
            try: row = json.loads(line)
            except json.JSONDecodeError: continue
            if not include_chess960 and is_chess960_row(row):
                continue
            if not include_illegal and is_illegal_row(row):
                continue
            for u in row.get("source_urls") or ():
                url_counts[u] += 1
            for t in row.get("tags") or ():
                tag_counts[t] += 1
    urls = [u for u, _ in url_counts.most_common()]
    tags = [t for t, _ in tag_counts.most_common()]
    if len(tags) > 255:
        # Tag id is u8; would need a wider field if vocab grows.
        raise SystemExit(f"tag vocabulary has {len(tags)} entries (>255); "
                         "widen the format to support this")
    return urls, tags


def pack(src: Path, dst: Path,
         include_chess960: bool = False,
         include_illegal:  bool = False) -> dict:
    urls, tags = collect_vocabularies(src, include_chess960, include_illegal)
    url_index = {u: i for i, u in enumerate(urls)}
    tag_index = {t: i for i, t in enumerate(tags)}

    buf = io.BytesIO()
    buf.write(MAGIC)
    buf.write(struct.pack("<H", VERSION))
    # position_count is filled in after we know it; reserve 4 bytes.
    pos_count_offset = buf.tell()
    buf.write(b"\x00\x00\x00\x00")

    # url table
    write_varint(buf, len(urls))
    for u in urls:
        encoded = u.encode("utf-8")
        write_varint(buf, len(encoded))
        buf.write(encoded)

    # tag table
    buf.write(bytes((len(tags),)))
    for t in tags:
        encoded = t.encode("utf-8")
        if len(encoded) > 255:
            raise SystemExit(f"tag '{t}' is > 255 bytes; widen len field")
        buf.write(bytes((len(encoded),)))
        buf.write(encoded)

    n_positions = 0
    n_skipped_chess960 = 0
    n_skipped_illegal = 0
    n_div_entries = 0
    with src.open() as f:
        for line in f:
            if not line.strip(): continue
            try: row = json.loads(line)
            except json.JSONDecodeError: continue
            fen = row.get("fen")
            if not fen: continue
            if not include_chess960 and is_chess960_row(row):
                n_skipped_chess960 += 1
                continue
            if not include_illegal and is_illegal_row(row):
                n_skipped_illegal += 1
                continue

            fen_b = fen.encode("utf-8")
            write_varint(buf, len(fen_b))
            buf.write(fen_b)

            q = row.get("context_quality")
            buf.write(bytes((QUALITY_TO_ID.get(q, QUALITY_UNKNOWN),)))

            row_tags = row.get("tags") or ()
            write_varint(buf, len(row_tags))
            for t in row_tags:
                buf.write(bytes((tag_index[t],)))

            row_urls = row.get("source_urls") or ()
            write_varint(buf, len(row_urls))
            for u in row_urls:
                write_varint(buf, url_index[u])

            for d in range(1, 8):
                write_varint(buf, int(row.get(f"d{d}", 0)))

            for d in range(1, 8):
                divide = row.get(f"divide_d{d}") or {}
                write_varint(buf, len(divide))
                for mv, count in divide.items():
                    buf.write(struct.pack("<H", encode_move(mv)))
                    write_varint(buf, int(count))
                    n_div_entries += 1

            n_positions += 1

    # patch position_count
    inner = buf.getvalue()
    inner = inner[:pos_count_offset] + struct.pack("<I", n_positions) + inner[pos_count_offset + 4:]

    # outer gzip (mtime=0 → deterministic output)
    with gzip.GzipFile(filename="", mode="wb", fileobj=open(dst, "wb"),
                       mtime=0, compresslevel=9) as gz:
        gz.write(inner)

    return {
        "positions":        n_positions,
        "skipped_chess960": n_skipped_chess960,
        "skipped_illegal":  n_skipped_illegal,
        "urls":             len(urls),
        "tags":             len(tags),
        "div_entries":      n_div_entries,
        "raw_bytes":        len(inner),
        "gz_bytes":         dst.stat().st_size,
    }


# ---------- unpack (for roundtrip verification) ----------

def read_varint(rd) -> int:
    shift = 0; result = 0
    while True:
        b = rd.read(1)
        if not b:
            raise EOFError("varint truncated")
        v = b[0]
        result |= (v & 0x7F) << shift
        if not (v & 0x80):
            return result
        shift += 7
        if shift > 63:
            raise OverflowError("varint > 64 bits")


def unpack_first_n(src: Path, n: int) -> list[dict]:
    rows: list[dict] = []
    with gzip.GzipFile(filename=str(src), mode="rb") as gz:
        magic = gz.read(4)
        if magic != MAGIC:
            raise ValueError(f"bad magic: {magic!r}")
        (ver,) = struct.unpack("<H", gz.read(2))
        if ver != VERSION:
            raise ValueError(f"unknown version {ver}")
        (count,) = struct.unpack("<I", gz.read(4))

        n_urls = read_varint(gz)
        urls = []
        for _ in range(n_urls):
            ln = read_varint(gz); urls.append(gz.read(ln).decode("utf-8"))
        n_tags = gz.read(1)[0]
        tags = []
        for _ in range(n_tags):
            ln = gz.read(1)[0]; tags.append(gz.read(ln).decode("utf-8"))

        take = min(n, count)
        for _ in range(take):
            fen_len = read_varint(gz); fen = gz.read(fen_len).decode("utf-8")
            q_id = gz.read(1)[0]
            quality = ID_TO_QUALITY.get(q_id)
            n_t = read_varint(gz); row_tags = [tags[gz.read(1)[0]] for _ in range(n_t)]
            n_u = read_varint(gz); row_urls = [urls[read_varint(gz)] for _ in range(n_u)]
            totals = [read_varint(gz) for _ in range(7)]
            divides = []
            for _ in range(7):
                nm = read_varint(gz)
                d: dict[str, int] = {}
                for _ in range(nm):
                    mv_packed = struct.unpack("<H", gz.read(2))[0]
                    d[decode_move(mv_packed)] = read_varint(gz)
                divides.append(d)
            rows.append({
                "fen": fen, "context_quality": quality,
                "tags": row_tags, "source_urls": row_urls,
                "totals": totals, "divides": divides,
            })
    return rows


# ---------- main ----------

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--src", default=str(SRC_FILE))
    ap.add_argument("--dst", default=str(DST_FILE))
    ap.add_argument("--include-chess960", action="store_true",
                    help="include Chess960 positions in the packed binary. "
                         "By default they're skipped because most engines "
                         "either don't support Chess960 or require "
                         "`setoption UCI_Chess960 value true` first, which "
                         "perftcheck doesn't currently send.")
    ap.add_argument("--include-illegal", action="store_true",
                    help="include positions where the side-not-to-move is "
                         "in check (illegal arrival). Skipped by default "
                         "because different engines compute different "
                         "perft counts on illegal FENs, so the reference "
                         "values are unreliable.")
    ap.add_argument("--verify", action="store_true",
                    help="re-read the first N rows and diff against the JSONL")
    ap.add_argument("--verify-n", type=int, default=200)
    args = ap.parse_args()

    src = Path(args.src); dst = Path(args.dst)
    if not src.exists():
        print(f"source not found: {src}", file=sys.stderr); return 1
    dst.parent.mkdir(parents=True, exist_ok=True)

    print(f"Packing {src.name} → {dst.name}")
    stats = pack(src, dst,
                 include_chess960=args.include_chess960,
                 include_illegal=args.include_illegal)
    raw_mb = stats["raw_bytes"] / 1024**2
    gz_mb  = stats["gz_bytes"]  / 1024**2
    src_mb = src.stat().st_size / 1024**2
    print(f"  positions      {stats['positions']:,d}")
    if stats['skipped_chess960']:
        print(f"  skipped (960)  {stats['skipped_chess960']:,d}  "
              f"(re-include with --include-chess960)")
    if stats['skipped_illegal']:
        print(f"  skipped (ill)  {stats['skipped_illegal']:,d}  "
              f"(side-not-to-move in check; --include-illegal to keep)")
    print(f"  url vocab      {stats['urls']:,d}")
    print(f"  tag vocab      {stats['tags']:,d}")
    print(f"  divide entries {stats['div_entries']:,d}")
    print(f"  raw binary     {raw_mb:.1f} MB")
    print(f"  gzipped (lvl9) {gz_mb:.1f} MB"
          f"   ({100 * gz_mb / src_mb:.1f}% of {src_mb:.0f} MB JSONL"
          f", {src_mb/gz_mb:.1f}× compression)")

    if args.verify:
        print(f"\nVerifying first {args.verify_n} rows…")
        rows_bin = unpack_first_n(dst, args.verify_n)
        diffs = 0
        # Walk the JSONL and the binary in lock-step, skipping rows on the
        # JSONL side that the pack also skipped (chess960 by default).
        with src.open() as f:
            i = 0
            for line in f:
                if i >= args.verify_n: break
                if not line.strip(): continue
                jrow = json.loads(line)
                if not args.include_chess960 and is_chess960_row(jrow):
                    continue
                if not args.include_illegal and is_illegal_row(jrow):
                    continue
                brow = rows_bin[i]
                if jrow["fen"] != brow["fen"]:
                    print(f"  row {i}: fen mismatch"); diffs += 1; continue
                for d in range(1, 8):
                    if int(jrow.get(f"d{d}", 0)) != brow["totals"][d-1]:
                        print(f"  row {i} d{d}: total mismatch "
                              f"{jrow.get(f'd{d}')} vs {brow['totals'][d-1]}")
                        diffs += 1
                for d in range(1, 8):
                    jdiv = jrow.get(f"divide_d{d}") or {}
                    bdiv = brow["divides"][d-1]
                    if jdiv != bdiv:
                        print(f"  row {i} divide_d{d}: mismatch "
                              f"(json={len(jdiv)} bin={len(bdiv)})")
                        diffs += 1
                jtags = sorted(jrow.get("tags") or ())
                btags = sorted(brow["tags"])
                if jtags != btags:
                    print(f"  row {i} tags differ"); diffs += 1
                jurls = jrow.get("source_urls") or ()
                if list(jurls) != brow["source_urls"]:
                    print(f"  row {i} source_urls differ"); diffs += 1
                i += 1
        if diffs == 0:
            print(f"  ok — {args.verify_n} rows roundtrip cleanly.")
        else:
            print(f"  {diffs} diff(s) found.")
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

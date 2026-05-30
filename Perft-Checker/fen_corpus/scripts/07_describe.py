#!/usr/bin/env python3
"""Stage 7: deterministic per-FEN static analysis.

For every FEN in corpus.csv, compute every position feature we can extract
from the FEN string alone — no LLM, no scraping — and emit one JSON line
per position to `static_analysis.jsonl`.

The output is designed for downstream aggregation: when an engine fails on
N positions, the user can intersect / cross-tabulate the `tags` arrays
across those rows to identify common features ("all 17 failures had
en-passant capture available", "all 42 had no castling rights", etc.).

Output row schema:
{
  "fen": "...",
  "fen_key": "...",                       // dedup key (first 4 fields)
  "source_urls": [...],                   // links sorted best-context-first
  "context_quality": "high",
  "sources_count": N,
  "min_page_fens": M,

  "side_to_move": "w" | "b",
  "is_chess960": bool,

  "castling": {raw, white_kingside, white_queenside,
               black_kingside, black_queenside},
  "en_passant": {square, set, capture_possible},
  "halfmove_clock": int,
  "fullmove_number": int,

  "piece_counts": {white: {K,Q,R,B,N,P}, black: {k,q,r,b,n,p}},
  "material": {white_excl_king, black_excl_king, diff_white_minus_black},
  "totals": {white_pieces, black_pieces, total_pieces, pawns_total},
  "king_positions": {white, black},

  "phase": "opening" | "middlegame" | "endgame",
  "tags": [...]                            // boolean aggregation tags
}

Stdlib only, fast (whole corpus in seconds).
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import config  # noqa: E402


OUTPUT_FILE = config.ROOT / "static_analysis.jsonl"

# Well-known reference positions, by first-4-field dedup key.
STARTPOS_KEY  = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -"
KIWIPETE_KEY  = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -"
# CPW classic test positions (Position 3 / 4 / 5 / 6 from chessprogramming wiki).
CPW_POS3_KEY  = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - -"
CPW_POS4_KEY  = "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq -"
CPW_POS5_KEY  = "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ -"
CPW_POS6_KEY  = (
    "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - -"
)

PIECE_VALUE = {"q": 9, "r": 5, "b": 3, "n": 3, "p": 1}

WHITE_PIECES = "KQRBNP"
BLACK_PIECES = "kqrbnp"


# ---------- FEN parsing ----------

def parse_board(board_field: str) -> list[list[str]]:
    """Return 8x8 list of pieces. board[0] is rank 8, board[7] is rank 1.
    Empty squares are represented as '.'."""
    rows: list[list[str]] = []
    for row_str in board_field.split("/"):
        row: list[str] = []
        for c in row_str:
            if c.isdigit():
                row.extend(["."] * int(c))
            elif c in WHITE_PIECES + BLACK_PIECES:
                row.append(c)
            # silently skip unknown chars
        # pad/trim to 8 just in case
        while len(row) < 8:
            row.append(".")
        rows.append(row[:8])
    while len(rows) < 8:
        rows.append(["."] * 8)
    return rows[:8]


def square_name(file_idx: int, board_row: int) -> str:
    """board_row=0 means rank 8, board_row=7 means rank 1."""
    rank = 8 - board_row
    return f"{chr(ord('a') + file_idx)}{rank}"


def find_piece(board: list[list[str]], piece: str) -> str | None:
    for r, row in enumerate(board):
        for f, sq in enumerate(row):
            if sq == piece:
                return square_name(f, r)
    return None


def en_passant_capture_possible(board: list[list[str]],
                                ep_square: str | None,
                                side: str) -> bool:
    """Is there an actual pawn of the side-to-move adjacent to the EP square,
    able to capture? FEN EP rank 3 means side-to-move=black (white just
    pushed); FEN EP rank 6 means side-to-move=white."""
    if not ep_square or ep_square == "-":
        return False
    if len(ep_square) != 2:
        return False
    file_ch, rank_ch = ep_square[0], ep_square[1]
    if file_ch < "a" or file_ch > "h" or rank_ch not in ("3", "6"):
        return False
    file_idx = ord(file_ch) - ord("a")

    if rank_ch == "3" and side == "b":
        # Black captures with a pawn on rank 4 (board row 4).
        row = 4
        own_pawn = "p"
    elif rank_ch == "6" and side == "w":
        # White captures with a pawn on rank 5 (board row 3).
        row = 3
        own_pawn = "P"
    else:
        # Inconsistent EP square / side-to-move pairing — no capture.
        return False

    for df in (-1, 1):
        f = file_idx + df
        if 0 <= f < 8 and board[row][f] == own_pawn:
            return True
    return False


# ---------- main analysis ----------

def analyze(fen: str) -> dict | None:
    parts = fen.split()
    if len(parts) != 6:
        return None
    board_field, side, castling, ep, halfmove, fullmove = parts
    board = parse_board(board_field)

    # Piece counts
    white = {p: 0 for p in WHITE_PIECES}
    black = {p: 0 for p in BLACK_PIECES}
    for row in board:
        for sq in row:
            if sq in white:
                white[sq] += 1
            elif sq in black:
                black[sq] += 1

    # Material (kings excluded)
    white_material = sum(PIECE_VALUE[p.lower()] * white[p]
                         for p in "QRBNP")
    black_material = sum(PIECE_VALUE[p] * black[p] for p in "qrbnp")

    # Castling
    cast = castling if castling != "-" else ""
    is_chess960 = any(c in cast for c in "ABCDEFGHabcdefgh")
    castling_info = {
        "raw":             castling,
        "white_kingside":  ("K" in cast)
                           or any(c.isupper() and c in "EFGH" for c in cast),
        "white_queenside": ("Q" in cast)
                           or any(c.isupper() and c in "ABCD" for c in cast),
        "black_kingside":  ("k" in cast)
                           or any(c.islower() and c in "efgh" for c in cast),
        "black_queenside": ("q" in cast)
                           or any(c.islower() and c in "abcd" for c in cast),
    }
    any_castling = any(castling_info[k] for k in
                       ("white_kingside", "white_queenside",
                        "black_kingside", "black_queenside"))

    # EP
    ep_set = ep != "-" and ep != ""
    ep_possible = en_passant_capture_possible(board, ep, side)

    try: halfmove_i = int(halfmove)
    except ValueError: halfmove_i = 0
    try: fullmove_i = int(fullmove)
    except ValueError: fullmove_i = 1

    total_white = sum(white.values())
    total_black = sum(black.values())
    total      = total_white + total_black
    pawns_total = white["P"] + black["p"]
    non_king_pieces = (total_white - white["K"]) + (total_black - black["k"])

    # King positions
    king_w = find_piece(board, "K")
    king_b = find_piece(board, "k")

    # Pawn rank features (7th-rank promotion imminent)
    # rank 7 = board row 1; rank 2 = board row 6
    white_pawn_on_7th = any(sq == "P" for sq in board[1])
    black_pawn_on_2nd = any(sq == "p" for sq in board[6])

    # Phase heuristic
    has_white_queen = white["Q"] > 0
    has_black_queen = black["q"] > 0
    queens_on = has_white_queen or has_black_queen
    if total >= 28 and queens_on and fullmove_i <= 12:
        phase = "opening"
    elif non_king_pieces <= 10 or not queens_on:
        phase = "endgame"
    else:
        phase = "middlegame"

    # Material classification
    diff = white_material - black_material
    if abs(diff) <= 1:
        material_balance = "equal"
    elif diff > 0:
        material_balance = "white_advantage" if diff <= 4 else "white_decisive"
    else:
        material_balance = "black_advantage" if diff >= -4 else "black_decisive"

    # Special positions
    key = " ".join(parts[:4])
    is_startpos = key == STARTPOS_KEY
    is_kiwipete = key == KIWIPETE_KEY
    is_cpw_test = key in (CPW_POS3_KEY, CPW_POS4_KEY,
                          CPW_POS5_KEY, CPW_POS6_KEY)
    is_empty_board = total == 0
    is_only_kings = (total == 2 and white["K"] == 1 and black["k"] == 1)
    lone_king_white = (total_white == 1 and white["K"] == 1 and total_black > 1)
    lone_king_black = (total_black == 1 and black["k"] == 1 and total_white > 1)
    missing_white_king = white["K"] == 0
    missing_black_king = black["k"] == 0
    illegal_position = (missing_white_king or missing_black_king
                       or white["K"] > 1 or black["k"] > 1
                       or white["P"] > 8 or black["p"] > 8)

    # Tags for aggregation
    tags: list[str] = []
    tags.append("side_to_move_white" if side == "w" else "side_to_move_black")

    if castling_info["white_kingside"]:  tags.append("castling_white_kingside")
    if castling_info["white_queenside"]: tags.append("castling_white_queenside")
    if castling_info["black_kingside"]:  tags.append("castling_black_kingside")
    if castling_info["black_queenside"]: tags.append("castling_black_queenside")
    if not any_castling:                  tags.append("no_castling_rights")
    elif all(castling_info[k] for k in ("white_kingside", "white_queenside",
                                        "black_kingside", "black_queenside")):
        tags.append("all_castling_rights")
    else:
        tags.append("partial_castling_rights")

    if ep_set:        tags.append("en_passant_set")
    if ep_possible:   tags.append("en_passant_capture_possible")
    if ep_set and not ep_possible:
                      tags.append("en_passant_phantom")

    tags.append(f"material_{material_balance}")

    if has_white_queen and has_black_queen: tags.append("both_queens_on")
    if queens_on:    tags.append("queens_on")
    else:            tags.append("queens_off")
    if white["Q"] == 0 and has_black_queen: tags.append("only_black_has_queen")
    if black["q"] == 0 and has_white_queen: tags.append("only_white_has_queen")
    if white["R"] + black["r"] == 0: tags.append("no_rooks")
    if white["B"] >= 2: tags.append("bishop_pair_white")
    if black["b"] >= 2: tags.append("bishop_pair_black")
    if white["P"] == 0: tags.append("white_no_pawns")
    if black["p"] == 0: tags.append("black_no_pawns")

    if lone_king_white: tags.append("lone_king_white")
    if lone_king_black: tags.append("lone_king_black")
    if is_only_kings:   tags.append("only_kings")
    if is_empty_board:  tags.append("empty_board")
    if illegal_position: tags.append("illegal_position")

    if white_pawn_on_7th: tags.append("white_pawn_on_7th")
    if black_pawn_on_2nd: tags.append("black_pawn_on_2nd")
    if white_pawn_on_7th or black_pawn_on_2nd:
        tags.append("promotion_imminent")

    # Endgame-type tags
    if non_king_pieces <= 14 and not queens_on:
        if white["R"] + black["r"] >= 1 and white["B"] + white["N"] + black["b"] + black["n"] == 0:
            tags.append("rook_endgame")
        if (white["R"] + black["r"] == 0
                and (white["B"] + white["N"] + black["b"] + black["n"]) >= 1):
            tags.append("minor_piece_endgame")
        if (white["R"] + black["r"] == 0
                and white["B"] + white["N"] + black["b"] + black["n"] == 0
                and pawns_total > 0):
            tags.append("pawn_endgame")

    if halfmove_i >= 80: tags.append("near_fifty_move_rule")
    if fullmove_i <= 5:  tags.append("very_early_game")

    tags.append(f"{phase}_phase")

    if is_chess960:  tags.append("chess960")
    if is_startpos:  tags.append("startpos")
    if is_kiwipete:  tags.append("kiwipete")
    if is_cpw_test:  tags.append("cpw_perft_position")

    return {
        "side_to_move":      side,
        "is_chess960":       is_chess960,
        "castling":          castling_info,
        "en_passant":        {"square": ep if ep_set else None,
                              "set": ep_set,
                              "capture_possible": ep_possible},
        "halfmove_clock":    halfmove_i,
        "fullmove_number":   fullmove_i,
        "piece_counts":      {"white": white, "black": black},
        "material":          {"white_excl_king": white_material,
                              "black_excl_king": black_material,
                              "diff_white_minus_black": diff,
                              "balance": material_balance},
        "totals":            {"white_pieces": total_white,
                              "black_pieces": total_black,
                              "total_pieces": total,
                              "pawns_total": pawns_total,
                              "non_king_pieces": non_king_pieces},
        "king_positions":    {"white": king_w, "black": king_b},
        "phase":             phase,
        "tags":              sorted(set(tags)),
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--quality", default="all",
                    choices=("high", "medium", "both", "all"),
                    help="which context-quality tier of FENs to analyse "
                         "(default: all)")
    ap.add_argument("--out", default=str(OUTPUT_FILE),
                    help="output JSONL path")
    args = ap.parse_args()

    config.ensure_dirs()

    if not config.CORPUS_CSV.exists():
        print(f"No corpus at {config.CORPUS_CSV} — run 04_corpus.py first.",
              file=sys.stderr)
        return 1

    if args.quality == "all":
        quality_filter = {"high", "medium", "low"}
    elif args.quality == "both":
        quality_filter = {"high", "medium"}
    else:
        quality_filter = {args.quality}

    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    n_in = n_out = n_invalid = 0
    by_tier: dict[str, int] = {}
    with config.CORPUS_CSV.open() as f, out_path.open("w") as out:
        for row in csv.DictReader(f):
            n_in += 1
            if row["context_quality"] not in quality_filter:
                continue

            a = analyze(row["fen"])
            if a is None:
                n_invalid += 1
                continue

            sources = [u.strip() for u in row["source_urls"].split(" | ")
                       if u.strip()]
            obj = {
                "fen":             row["fen"],
                "fen_key":         " ".join(row["fen"].split()[:4]),
                "source_urls":     sources,
                "context_quality": row["context_quality"],
                "sources_count":   int(row["sources_count"]),
                "min_page_fens":   int(row["min_page_fens"]),
                **a,
                # Perft node counts — populated by a separate pipeline.
                # Always present so downstream consumers can rely on the
                # schema; defaults to 0 when not yet computed.
                "d1": 0, "d2": 0, "d3": 0, "d4": 0,
                "d5": 0, "d6": 0, "d7": 0,
                # Per-depth root-move divide (uci_move → child_node_count).
                # Populated by stage 8 alongside the d_N totals via TGCT's
                # `divide:` command; defaults to {} when not yet computed.
                "divide_d1": {}, "divide_d2": {}, "divide_d3": {},
                "divide_d4": {}, "divide_d5": {}, "divide_d6": {},
                "divide_d7": {},
            }
            out.write(json.dumps(obj, ensure_ascii=False) + "\n")
            n_out += 1
            by_tier[row["context_quality"]] = by_tier.get(
                row["context_quality"], 0) + 1

    print(f"Read {n_in} corpus rows, wrote {n_out} analyses → {out_path}")
    print(f"  by tier: {by_tier}")
    if n_invalid:
        print(f"  skipped {n_invalid} unparseable FENs")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

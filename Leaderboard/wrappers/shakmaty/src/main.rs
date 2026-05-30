//! Minimal UCI wrapper around the shakmaty crate.
//!
//! See PerftWar/wrappers/README.md for the design rationale and the exact
//! UCI subset implemented. This file does nothing of substance other than:
//!   - Parse a tiny set of UCI commands off stdin.
//!   - Call `shakmaty::Chess` to set up the position and generate moves.
//!   - Recurse, bulk-counting at depth 1 to match Stockfish's own perft.

use std::io::{self, BufRead, Write};

use shakmaty::fen::Fen;
use shakmaty::{CastlingMode, Chess, Position, PositionError};

/// Recursive perft. Uses `legal_moves().len()` at depth 1 for bulk
/// counting (matches Stockfish's own perft optimisation).
fn perft(pos: &Chess, depth: u32) -> u64 {
    if depth == 0 {
        return 1;
    }
    let moves = pos.legal_moves();
    if depth == 1 {
        return moves.len() as u64;
    }
    let mut nodes = 0u64;
    for mv in moves {
        let mut child = pos.clone();
        child.play_unchecked(&mv);
        nodes += perft(&child, depth - 1);
    }
    nodes
}

fn parse_fen(s: &str) -> Option<Chess> {
    // The perft corpus includes positions that fail shakmaty's strict legality
    // checks — e.g. `B6b/8/8/8/2K5/5k2/8/b6B b - - 0 1` trips IMPOSSIBLE_CHECK
    // (two sliding checkers aligned). For movegen benchmarking we want to load
    // these anyway, so ignore the soft checks that don't affect move generation.
    let fen: Fen = s.parse().ok()?;
    fen.into_position::<Chess>(CastlingMode::Standard)
        .or_else(PositionError::ignore_impossible_check)
        .or_else(PositionError::ignore_too_much_material)
        .or_else(PositionError::ignore_invalid_castling_rights)
        .or_else(PositionError::ignore_invalid_ep_square)
        .ok()
}

// Sentinel emitted when perft is asked of a position whose FEN was rejected.
// Matches `^Nodes searched: \d+$` so the verifier doesn't hang, but is large
// enough (and obviously bogus) that it can't be mistaken for a real count.
const PARSE_FAIL_SENTINEL: u64 = u64::MAX;

fn main() {
    let stdin = io::stdin();
    let mut out = io::stdout().lock();
    // `None` = the last `position fen` command was rejected by parse_fen, so
    // perft must refuse to run (otherwise stale state would mascarade as a
    // movegen bug — this is the exact failure mode that produced our false
    // shakmaty "bug report" on B6b/8/8/8/2K5/5k2/8/b6B b - - 0 1).
    let mut pos: Option<Chess> = Some(Chess::default());

    for line in stdin.lock().lines() {
        let Ok(line) = line else { break };
        let cmd = line.trim();

        if cmd == "uci" {
            let _ = writeln!(out, "id name shakmaty-perft");
            let _ = writeln!(out, "id author shakmaty UCI wrapper");
            let _ = writeln!(out, "uciok");
        } else if cmd == "isready" {
            let _ = writeln!(out, "readyok");
        } else if cmd == "ucinewgame" || cmd.starts_with("position startpos") {
            pos = Some(Chess::default());
        } else if let Some(rest) = cmd.strip_prefix("position fen ") {
            // Drop any trailing " moves …" — PerftWar never plays moves.
            let fen = rest.split(" moves").next().unwrap_or(rest).trim();
            match parse_fen(fen) {
                Some(p) => pos = Some(p),
                None => {
                    eprintln!("[shakmaty-perft] FEN rejected: {fen}");
                    pos = None;
                }
            }
        } else if cmd.starts_with("go perft ") || cmd.starts_with("perft ") {
            let depth_str = cmd
                .strip_prefix("go perft ")
                .or_else(|| cmd.strip_prefix("perft "))
                .unwrap_or("0")
                .trim();
            if let Ok(d) = depth_str.parse::<u32>() {
                match &pos {
                    Some(p) => {
                        let _ = writeln!(out, "Nodes searched: {}", perft(p, d));
                    }
                    None => {
                        let _ = writeln!(out, "Nodes searched: {PARSE_FAIL_SENTINEL}");
                    }
                }
            }
        } else if cmd == "quit" {
            break;
        }
        let _ = out.flush();
    }
}

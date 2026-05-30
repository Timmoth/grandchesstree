//! Minimal UCI wrapper around the cozy-chess crate.
//!
//! See PerftWar/wrappers/README.md for the design rationale and the exact
//! UCI subset implemented. This file does nothing of substance other than:
//!   - Parse a tiny set of UCI commands off stdin.
//!   - Call `cozy_chess::Board` to set up the position and generate moves.
//!   - Recurse, bulk-counting at depth 1 to match Stockfish's own perft.

use std::io::{self, BufRead, Write};

use cozy_chess::Board;

/// Recursive perft. Bulk-counts at depth 1 (sums move counts across each
/// piece's `PieceMoves` batch) so the comparison against full engines that
/// do the same optimisation stays apples-to-apples.
fn perft(board: &Board, depth: u32) -> u64 {
    if depth == 0 {
        return 1;
    }
    if depth == 1 {
        let mut n = 0u64;
        board.generate_moves(|moves| {
            n += moves.into_iter().count() as u64;
            false
        });
        return n;
    }
    let mut nodes = 0u64;
    board.generate_moves(|moves| {
        for mv in moves {
            let mut child = board.clone();
            child.play_unchecked(mv);
            nodes += perft(&child, depth - 1);
        }
        false
    });
    nodes
}

fn main() {
    let stdin = io::stdin();
    let mut out = io::stdout().lock();
    let mut board = Board::default();

    for line in stdin.lock().lines() {
        let Ok(line) = line else { break };
        let cmd = line.trim();

        if cmd == "uci" {
            let _ = writeln!(out, "id name cozy-perft");
            let _ = writeln!(out, "id author cozy-chess UCI wrapper");
            let _ = writeln!(out, "uciok");
        } else if cmd == "isready" {
            let _ = writeln!(out, "readyok");
        } else if cmd == "ucinewgame" || cmd.starts_with("position startpos") {
            board = Board::default();
        } else if let Some(rest) = cmd.strip_prefix("position fen ") {
            // Any " moves …" tail is dropped — PerftWar never sends moves.
            let fen = rest.split(" moves").next().unwrap_or(rest).trim();
            if let Ok(b) = Board::from_fen(fen, false) {
                board = b;
            }
        } else if cmd.starts_with("go perft ") || cmd.starts_with("perft ") {
            let depth_str = cmd
                .strip_prefix("go perft ")
                .or_else(|| cmd.strip_prefix("perft "))
                .unwrap_or("0")
                .trim();
            if let Ok(d) = depth_str.parse::<u32>() {
                let _ = writeln!(out, "Nodes searched: {}", perft(&board, d));
            }
        } else if cmd == "quit" {
            break;
        }
        // Anything else is silently ignored (PerftWar may send
        // `setoption name Threads value 1` etc; harmless to no-op).
        let _ = out.flush();
    }
}

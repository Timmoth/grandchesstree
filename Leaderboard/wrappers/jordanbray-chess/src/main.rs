//! Minimal UCI wrapper around the jordanbray/chess crate (crates.io: `chess`).
//!
//! See PerftWar/wrappers/README.md for the design rationale and the exact
//! UCI subset implemented. This file does nothing of substance other than:
//!   - Parse a tiny set of UCI commands off stdin.
//!   - Call `chess::Board` to set up the position and `MoveGen::new_legal`
//!     to enumerate legal moves.
//!   - Recurse, bulk-counting at depth 1 to match Stockfish's own perft.

use std::io::{self, BufRead, Write};
use std::str::FromStr;

use chess::{Board, MoveGen};

/// Recursive perft. Uses `MoveGen::new_legal(&board).count()` at depth 1
/// to bulk count, matching Stockfish's optimisation.
fn perft(board: &Board, depth: u32) -> u64 {
    if depth == 0 {
        return 1;
    }
    if depth == 1 {
        return MoveGen::new_legal(board).count() as u64;
    }
    let mut nodes = 0u64;
    // MoveGen is a consuming iterator; recreate it for the recursion path.
    for mv in MoveGen::new_legal(board) {
        let child = board.make_move_new(mv);
        nodes += perft(&child, depth - 1);
    }
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
            let _ = writeln!(out, "id name jordan-perft");
            let _ = writeln!(out, "id author jordanbray/chess UCI wrapper");
            let _ = writeln!(out, "uciok");
        } else if cmd == "isready" {
            let _ = writeln!(out, "readyok");
        } else if cmd == "ucinewgame" || cmd.starts_with("position startpos") {
            board = Board::default();
        } else if let Some(rest) = cmd.strip_prefix("position fen ") {
            // Drop any " moves …" tail.
            let fen = rest.split(" moves").next().unwrap_or(rest).trim();
            if let Ok(b) = Board::from_str(fen) {
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
        let _ = out.flush();
    }
}

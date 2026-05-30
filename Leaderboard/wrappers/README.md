# Engine wrappers

Some entries on the Leaderboard leaderboard aren't full chess engines — they're
**move-generator libraries** that don't ship a UCI-speaking executable on
their own. Leaderboard's harness drives every entry through the same
subprocess + stdin/stdout protocol, so each library needs a thin shim
binary that:

1. Loops on stdin reading UCI-style commands.
2. Handles `position startpos | position fen <fen>` and `go perft <n>` / `perft <n>`.
3. Calls the library's perft and writes `Nodes searched: <count>` to stdout.

Those shims live here. Each is intentionally small (~60 lines) and does
**no work of its own** — it just plumbs FEN → library function → node count.

## Why a wrapper, not in-process FFI?

Leaderboard measures every entry the same way: spawn a process, send a
position, time the perft response. Calling libraries in-process (PyO3,
ctypes, etc.) would shave ~50–200 ms of subprocess startup off the
library entries only — biasing the leaderboard against every full
engine that *does* pay that cost. Wrappers keep every measurement on
the same footing.

## Methodology

All wrappers bulk-count at depth 1 (`movelist.len()`), matching the
optimisation Stockfish and similar engines use in their own perft. The
wrapper therefore measures the library's pure move-generation
throughput, **not** the cost of recursing one extra level just to count
leaves.

## Wrappers

### Movegen libraries (Rust)

| Wrapper | Language | Library | Wrapper binary path | Source |
|---|---|---|---|---|
| `cozy-chess/` | Rust | cozy-chess | `target/release/cozy-perft` | crates.io: `cozy-chess` |
| `shakmaty/` | Rust | shakmaty | `target/release/shakmaty-perft` | crates.io: `shakmaty` |
| `jordanbray-chess/` | Rust | jordanbray's chess | `target/release/jordan-perft` | crates.io: `chess` |

The Rust wrappers pull their library from crates.io as a normal
Cargo dependency — no clone needed. The wrapper is a tiny Rust binary
that links the library and exposes a UCI loop.

### Pure-perft CLI tools (bash shim)

Some entries on the board are specialist perft tools that *already*
have their own CLI but don't speak UCI — they take a FEN and depth as
command-line arguments (or via a one-shot stdin script). For these we
ship a tiny bash wrapper instead of a compiled wrapper binary; it just
re-spawns the underlying tool on each `go perft <n>`, passes through
its output verbatim, and prints `perft-done` as the line that Leaderboard's
`end_re` matches.

| Wrapper | Tool | Wrapper script | Cloned source |
|---|---|---|---|
| `mperft/` | abulmo/MPerft | `run.sh` | `bin/mperft/` |

**Mode flags** are passed in via the wrapper's argv from the descriptor's
`launch` field — e.g. `wrappers/mperft/run.sh --threads 1 --hash 0 --bulk`
for MPerft's single-no-cache mode. The wrapper forwards them verbatim to
the underlying tool.

## UCI subset implemented

Each wrapper handles `uci`, `isready`, `ucinewgame`, `position startpos`,
`position fen <fen>`, `go perft <n>` (also accepts bare `perft <n>`), and
`quit`. Anything else is silently ignored. Trailing `moves …` after a
position is ignored — Leaderboard never sends moves.

Descriptors for these wrappers only define `single-no-cache` — bare movegen
libraries have no TT or built-in threading.

## Known caveats

- **Library API drift.** Rust wrappers pin minor versions in `Cargo.toml`.
  A breaking minor release upstream may require a small wrapper edit.
- **No UCI banner version.** Install scripts read the resolved version
  from `Cargo.lock` and print it so you can update
  `engines/<lib>.json`'s `"version"` field manually.

## Adding a new wrapper

Pattern from any of the existing wrappers:

1. `wrappers/<lib>/Cargo.toml` (or Makefile) declaring the library
   dependency and the binary name.
2. `wrappers/<lib>/src/main.rs` (or `wrapper.cpp`) implementing the
   UCI subset above plus a perft function that bulk-counts at depth 1.
3. `scripts/install-<lib>.sh` that builds the wrapper and runs
   `verify_perft` from `scripts/_common.sh`.
4. `engines/<lib>.json` pointing `launch` at the resulting binary,
   declaring only `single-no-cache` mode.

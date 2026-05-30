# Leaderboard

Benchmark harness for the TGCT engine leaderboard. Walks every shipped engine
through three reference perft positions under **iterative deepening**, going
as deep as a wall-clock budget allows. Strictly verifies node counts at every
depth — any mismatch disqualifies the engine. Aggregates the per-engine
output into a flat leaderboard JSON the
[grandchesstree.com](https://grandchesstree.com/leaderboard.html) page reads.

`perft_war.py` is the harness; 33 engines have descriptors under
[`engines/`](engines/) and matching install scripts under
[`scripts/`](scripts/). Stdlib-only Python — runs on a fresh baremetal
Ubuntu host with no extra dependencies.

**Correctness first.** Any wrong node count → the engine run is disqualified,
no NPS values written. The leaderboard ranks only engines that pass; speed
is the tiebreaker.

## The four modes

| Mode                | Threads        | Cache (TT, typical) |
|---------------------|----------------|---------------------|
| `single-no-cache`   | 1              | 1 MB (≈ none)       |
| `single-with-cache` | 1              | 4 GB                |
| `multi-no-cache`    | all host cores | 1 MB (≈ none)       |
| `multi-with-cache`  | all host cores | 4 GB                |

Mode names are just identifiers — the actual threads/cache settings live in
the descriptor's `setup` lines, with `{threads}` substituted to the host's
core count. Engines opt into only the modes they support.
`engines/example-stockfish.json` shows the convention for all four.

## Position set

Three TGCT-canonical positions, each with a depth → expected-node-count map:

| Name      | FEN                                                                        | Deepest known |
|-----------|----------------------------------------------------------------------------|---------------|
| startpos  | `rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1`                 | d12           |
| kiwipete  | `r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1`     | d9            |
| sje       | `r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10` | d9            |

Authoritative node counts are baked into `perft_war.py`'s `POSITIONS` table
(derived from `Site/src/results/perft_p{0,1,2}_results.json`).

## Quick start

```sh
# 1. Install one or more engines (clones, builds, verifies).
./scripts/install-stockfish.sh
./scripts/install-cozy-chess.sh

# 2. Benchmark one engine. Default budget = 60s per (mode, position).
python3 perft_war.py run engines/stockfish.json

# 3. Or run the full suite + aggregate in one go.
./scripts/run-all.sh
./scripts/run-all.sh --budget-sec 30                     # tighter budget
./scripts/run-all.sh --engines stockfish,cozy-chess      # subset
./scripts/run-all.sh --positions startpos                # one position only

# 4. Roll up per-engine results into the combined leaderboard.json.
python3 perft_war.py aggregate
```

Output lands in `results/<engine>.json` (per-engine) and
`results/leaderboard.json` (aggregate, what the website reads).

## CLI

```
perft_war.py run <descriptor> [options]
  --budget-sec FLOAT   Per-(mode,position) wall-clock budget (default 60.0)
  --modes LIST         Comma-separated subset of modes; default = all declared
  --positions LIST     Comma-separated subset of positions; default = all
  --results-dir DIR    Where to write the per-engine JSON (default "results")

perft_war.py aggregate
  --results-dir DIR    Read from here (default "results")
  --out PATH           Write combined leaderboard (default "results/leaderboard.json")
```

A second tool, `perft_verify.py`, runs the same engines against the
~28k EPD corpus shipped with Perft-Checker — useful for catching
correctness regressions outside the three benchmark positions:

```
perft_verify.py verify <descriptor> [--depth-cap N] [--per-case-timeout SEC]
perft_verify.py verify-all          [--depth-cap N] [--workers N] [--engines a,b,c]
```

## Execution model

For each `(engine, mode, position)`:

1. Spawn the engine subprocess once via the descriptor's `launch` command,
   send `setup` lines, then walk depths `d1, d2, d3, …` over a single
   stdin/stdout session.
2. After each depth completes, record `nodes`, `elapsed_sec`, derived `nps`,
   and process stats (`av_cpu_pct`, `peak_rss_mb`, …).
3. Stop when either:
   - The wall-clock budget × `BUDGET_OVERRUN_FACTOR` (1.5) is exhausted, **or**
   - The predictor estimates the next depth would exceed the remaining
     window (using last-depth NPS at 1.0× as a non-optimistic projection).
4. The **deepest completed depth** is the position's headline result
   (`best_depth`, `best_nps`, etc.).

Per-mode `mean_nps` is the arithmetic mean of `best_nps` across the three
positions.

Per-engine `min_depth` (default 1) lets descriptors skip shallow depths that
crash specific engines (e.g. Horsie segfaults on `perft 1`).

## Engine descriptor

One JSON per engine, e.g. `engines/stockfish.json`:

```json
{
  "name":     "stockfish",
  "version":  "18",
  "owner":    "official-stockfish",
  "repo":     "https://github.com/official-stockfish/Stockfish",
  "language": "C++",
  "modes": {
    "single-no-cache": {
      "launch": "bin/stockfish/stockfish",
      "setup":  ["setoption name Threads value 1",
                 "setoption name Hash value 1"],
      "case":   "position fen {fen}\ngo perft {depth}",
      "end_re": "^Nodes searched: \\d+$",
      "quit":   "quit"
    }
  }
}
```

Per-mode keys:

| Key       | Required | Meaning |
|-----------|----------|---------|
| `launch`  | yes      | Shell command that starts the engine. Run via `exec` so PID = engine. |
| `setup`   | no       | Lines sent on stdin right after launch (UCI `setoption`, mode flags). |
| `case`    | yes      | Template for the per-depth command. `{fen}`, `{depth}`, `{threads}` are substituted. Newlines = separate stdin lines. |
| `end_re`  | yes      | Regex that matches the line containing the perft result. |
| `quit`    | no       | Command to cleanly shut the engine down (default `"quit"`). |

Top-level optional keys: `min_depth` (skip d < N), `language` (display tag),
`owner`, `repo`.

The harness scans the `end_re`-matched line for `\b<expected_nodes>\b`
(word-boundary regex) to verify correctness. Mismatch → fail-fast disqualify.

`example-*.json` descriptors are skipped by `run-all.sh`.

## Install scripts

Each engine has a one-shot script under [`scripts/`](scripts/) that clones,
builds with the right per-host arch flags, and verifies `go perft 4` from
startpos. Hosts currently covered: `darwin-arm64`, `darwin-x86_64`,
`linux-x86_64`, `linux-aarch64` — anything else falls through to the
engine's own auto-detection.

Shared helpers in [`scripts/_common.sh`](scripts/_common.sh): `detect_host`,
`clone_or_keep`, `verify_perft`, banner-version extraction. Adding a new
engine is a thin script setting `ENGINE`, `REPO`, `ENGINE_DIR`, `BINARY`,
then sourcing `_common.sh`.

Move-generator **libraries** (Rust crates, C++ source trees with no UCI
binary of their own) are wrapped under [`wrappers/`](wrappers/) — small
shim binaries that expose the same `position fen / perft N` subset. See
[`wrappers/README.md`](wrappers/README.md) for the shim methodology.

## Output

**Per-engine** (`results/<engine>.json`): multiple versions accumulate
under a top-level `versions` map.

```json
{
  "name": "stockfish",
  "owner": "official-stockfish",
  "repo": "https://github.com/official-stockfish/Stockfish",
  "language": "C++",
  "versions": {
    "18": {
      "ran_at": "2026-05-30T17:14:00Z",
      "disqualified": false,
      "budget_sec": 60.0,
      "host": {
        "cpu_model": "Apple M2", "cpu_physical_cores": 8,
        "ram_total_bytes": 17179869184, "platform": "macOS-15.7-arm64", "...": "..."
      },
      "modes": {
        "single-no-cache": {
          "mean_nps": 167000000,
          "positions": [
            {
              "name": "startpos", "fen": "...",
              "depths": [
                { "depth": 1, "nodes": 20, "elapsed_sec": 0.012, "nps": 1666,
                  "av_cpu_pct": 95, "av_rss_mb": 8, "peak_rss_mb": 12 },
                { "depth": 6, "nodes": 119060324, "elapsed_sec": 0.71,
                  "nps": 167690000, "av_cpu_pct": 99, "peak_rss_mb": 64 }
              ],
              "best_depth": 6, "best_nodes": 119060324,
              "best_elapsed_sec": 0.71, "best_nps": 167690000,
              "best_av_cpu_pct": 99, "best_peak_rss_mb": 64
            }
          ]
        }
      }
    }
  }
}
```

Disqualified versions instead carry `disqualified: true`, `reason`, and a
`failed_case` block with the FEN, depth, expected nodes, and last 20 lines
of captured stdout.

**Aggregated** (`results/leaderboard.json`): one row per
`(engine, version, mode)`, plus a top-level `hosts` map keyed by engine.
Picks the most recent non-disqualified version per engine. This is the
file the site loads.

## Layout

```
Leaderboard/
├── perft_war.py            harness — run / aggregate
├── perft_verify.py         cross-engine perft correctness against PerftSuite EPDs
├── engines/                33 JSON descriptors
├── scripts/
│   ├── _common.sh          host detection, perft probe, banner extraction
│   ├── install-<engine>.sh per-engine clone+build+verify (33 of these)
│   └── run-all.sh          orchestrate every engine + aggregate
├── wrappers/               shims for move-gen libs without a UCI binary
│                           (cozy-chess, shakmaty, jordanbray-chess, mperft,
│                            gigantua, chessbit, perft_cpu_2026, quanticade)
├── bin/                    per-engine built binaries land here (gitignored)
└── results/                per-engine result JSON + leaderboard.json
```

# perftcheck

A cross-platform CLI that validates a UCI chess engine's `go perft N` output
against a corpus of known-correct positions and writes a JSON report.

Documented for users at [grandchesstree.com/perftcheck.html](https://grandchesstree.com/perftcheck.html).
The release workflow at `.github/workflows/publish-perftcheck.yml` builds and uploads a
self-contained binary per platform to GitHub Releases on every tagged
release — `perftcheck-linux-x64`, `perftcheck-osx-arm64`,
`perftcheck-win-x64.exe`, etc.

## Quick start

```sh
perftcheck --engine /path/to/engine
```

That's it. perftcheck ships with the corpus embedded in the binary (~84
MB total, single self-contained file), so no separate download or flag
is needed. Runs every position up to depth 4 (the default depth cap),
prints a live progress bar + summary, dumps `perft-report.json` to the
current directory. Exit code `0` if every case passed, `1` otherwise.

## Input format

By default the corpus comes from inside the perftcheck binary — a
gzipped `corpus.gctc.gz` (~46 MB compressed, 142,953 positions, d1–d7
totals and divides) embedded as a managed resource. You'll almost never
need to think about it.

For custom corpora — fewer positions, your own perft results, a draft
build of the pipeline — pass `--static-analysis <FILE>` to override.
Two formats are accepted, transparently distinguished by extension +
magic bytes:

1. **`corpus.gctc.gz`** — distribution format, a custom gzipped binary
   produced by `fen_corpus/scripts/09_pack.py` (URL dedup, packed UCI
   moves, varint counts). ~46 MB for the bundled corpus.
2. **`static_analysis.jsonl`** — authoring/debug format, one JSON object
   per line, produced by `fen_corpus/scripts/07_describe.py` and then
   filled with totals + divides by `08_perft.py`. ~400 MB at the current
   corpus size — grep-friendly, used as the input to `09_pack.py`.

Each row carries:

- The FEN
- The source URLs where this position was discussed (sorted best-context-first)
- Per-position deterministic feature tags (`castling`, `en_passant_capture_possible`, `material_white_advantage`, `endgame_phase`, etc.)
- The reference perft node counts `d1`…`d7`
- The reference root-move divides `divide_d1`…`divide_d7` (uci_move → child_node_count). Used by `--drill-down` as the top-level oracle.

Cases are derived by walking each row's d1…d7 fields; depths with a zero
value are treated as "not yet computed" by the perft-population pipeline
and skipped, not tested as 0. Divide maps default to `{}` and are
populated alongside totals by `08_perft.py`. The binary-format spec is
at the top of `09_pack.py`; `--quality high|medium|low|both|all`
works identically against either file.

## Options

| Flag                          | Default               | Meaning                                                                       |
|-------------------------------|-----------------------|-------------------------------------------------------------------------------|
| `-e, --engine <PATH>`         | *required*            | UCI engine executable                                                         |
| `-s, --static-analysis <FILE>`| embedded corpus       | Override the embedded corpus. Accepts `corpus.gctc.gz` (binary) or `static_analysis.jsonl`. |
| `--depth-cap <N>`             | `4`                   | Skip cases deeper than this                                                   |
| `--depth-min <N>`             | `1`                   | Skip cases shallower than this                                                |
| `--timeout <SECS>`            | `30`                  | Per-case timeout. Engine is killed and restarted on overrun.                  |
| `--filter <SUBSTR>`           | —                     | Only run cases whose FEN contains this substring                              |
| `--quality <TIER>`            | `all`                 | Restrict by context tier: `high`, `medium`, `low`, `both` (high+med), `all`   |
| `--tag <NAME>`                | —                     | Only run cases whose tags contain this tag. Repeatable; AND across all flags. |
| `--limit <N>`                 | —                     | Take only the first N matching cases                                          |
| `--report <PATH>`             | `perft-report.json`   | Path to write the JSON report                                                 |
| `--fail-fast`                 | off                   | Stop on the first non-pass                                                    |
| `--quiet`                     | off                   | Suppress live console output (CI mode)                                        |
| `--perft-command <CMD>`       | `go perft`            | UCI verb sent before the depth number. Set to `perft` for engines like Potential that accept the bare form. |
| `--bare-number-total`         | off                   | Accept a bare integer on its own line as the perft total. Needed by Stormphrax (and a few others) that print just the number with no `Nodes:` prefix. Off by default so chatty engines' numeric debug output doesn't get mis-parsed as the total. |
| `--drill-down`                | off                   | On each mismatch, run divides depth-by-depth until the exact leaf position where move-gen first diverges is found. Requires `--ref-engine`. |
| `--ref-engine <PATH>`         | —                     | TGCT/GrandChessTree.Engine binary used as the divide+apply oracle for `--drill-down`. Must support the extended `divide:<d>:<mb>:<fen>:<moves>` form. |
| `--include-edge-cases`        | off                   | Include 13 positions tagged `edge_case_engine_disagreement` — FENs where production engines (StockDory, Stormphrax) historically diverge from the Stockfish/TGCT oracle. Categories: en-passant-blocks-check, near-50-move-rule, pathological piece counts (multi-queen, bishop-on-every-square). Off by default to keep clean runs clean; opt in when stress-testing exotic move-gen paths. |

## Failure-tag aggregation

After a run, the summary table shows each tag (en-passant, castling, endgame
phase, etc.) alongside how many of the failures had that tag and what
fraction that is. Tags present in ≥50% of failures are highlighted — those
are strong signals that point at a likely root cause.

```text
Tags across the 17 failure(s) (top 20 of 23, ≥50% highlighted):
╭────────────────────────────────┬──────────┬──────────╮
│ tag                            │ failures │ fraction │
├────────────────────────────────┼──────────┼──────────┤
│ en_passant_capture_possible    │       16 │     94 % │   ← strong signal
│ side_to_move_black             │       14 │     82 % │   ← strong signal
│ castling_white_kingside        │        9 │     53 % │   ← strong signal
│ material_equal                 │        6 │     35 % │
│ middlegame_phase               │        5 │     29 % │
…
```

The same data is in the JSON report under `failureTags.countByTag` /
`fractionByTag` for programmatic analysis.

## Drill-down (find the exact buggy move)

When the engine's perft total disagrees with truth at depth N, the bug is
usually one move in one sub-tree. Manually narrowing it is the classic
"perft divide → pick wrong root → apply → divide again" loop, repeated
until depth 1, where the divide is the legal-move list and the diff is
the bug. `--drill-down` automates that loop.

Enable it by pointing at a trusted reference engine — TGCT, the
`GrandChessTree.Engine` in this repo, which accepts the extended
`divide:<d>:<mb>:<fen>:<moves>` command (per-move divide *and* the
resulting FEN after applying an optional move list, in one round-trip):

```sh
perftcheck \
  --engine /path/to/buggy \
  --static-analysis static_analysis.jsonl \
  --drill-down --ref-engine /path/to/GrandChessTree.Engine
```

For every mismatch the runner:

1. Compares the test engine's divide at the failing position to the
   corpus's `divide_d<N>` for that FEN.
2. Picks the first move whose count differs (missing → extra →
   wrong-count, in that order — missing moves are almost always the
   actionable signal).
3. Applies that move via TGCT, descends to depth N-1, asks TGCT for the
   new expected divide, and compares again.
4. Stops at depth 1, where the diff between the test engine's and TGCT's
   move lists *is* the bug: which moves the engine illegally generates
   and which it failed to generate, at a single specific position.

Each failure in the JSON report gains a `drillDown` block:

```json
"drillDown": {
  "bugDepth":      1,
  "moveSequence":  ["e2e4", "g8f6", "f1c4"],
  "leafFen":       "rnbqkb1r/pppp1ppp/5n2/4p3/2B1P3/8/PPPP1PPP/RNBQK1NR b KQkq - 2 2",
  "missingMoves":  ["d8e7"],
  "extraMoves":    [],
  "wrongCount":    {}
}
```

A bug-depth histogram is printed at the end of the run — a tall `d=1`
column means a pure move-generator issue; deeper bottom-outs point at
make-move or undo bugs.

Cost: each mismatch adds roughly N TGCT round-trips. For ~400 failures
at depth 4, drill-down typically adds 5–15 minutes.

## Engine protocol

Standard UCI:

```
→ uci
←   id name <engine>
…
←   uciok
→ isready
←   readyok
→ position fen <fen>
→ go perft <depth>
←   …
←   Nodes searched: <total>
→ isready
←   readyok
…
→ quit
```

Several other spellings of the total line are accepted to support
non-Stockfish engines:

- `Total nodes: N` / `Total: N` / `Nodes: N` (Jet emits only the last form).
- `Nodes searched: N,NNN` with comma thousands separators (StockDory).
- `Nodes searched: N in Tms (M nps)` with trailing timing info (Pawnocchio).
- `info depth N nodes K time T nps M` — embedded inside a UCI info line (Viridithas).
- A bare integer on its own line — opt-in via `--bare-number-total` (Stormphrax).

## JSON report shape

```json
{
  "tool":      "perftcheck",
  "version":   "0.3.0",
  "engine":    "/abs/path/to/engine",
  "engineId":  "Stockfish 18",
  "startedUtc": "2026-05-29T12:00:00Z",
  "durationSeconds": 21.94,
  "options": {
    "depthMin": 1, "depthCap": 4, "timeoutSeconds": 30,
    "staticAnalysisFile": "static_analysis.jsonl"
  },
  "totals":   { "cases": 17234, "passed": 17217, "failed": 17 },
  "failures": [
    {
      "kind":     "mismatch",
      "fen":      "…",
      "depth":    3,
      "expected": 12345,
      "actual":   12344,
      "diff":     -1,
      "elapsedSeconds": 0.014,
      "source":   "static_analysis.jsonl:8721",
      "tags":     ["en_passant_capture_possible", "side_to_move_black", "…"],
      "sourceUrls": [
        "https://github.com/X/Y/issues/123",
        "https://www.talkchess.com/forum/viewtopic.php?t=NNNNN"
      ]
    }
  ],
  "failureTags": {
    "failuresWithTags": 17,
    "countByTag":    { "en_passant_capture_possible": 16, "side_to_move_black": 14, … },
    "fractionByTag": { "en_passant_capture_possible": 0.94, "side_to_move_black": 0.82, … }
  }
}
```

When `--drill-down` is used, every `mismatch` entry additionally carries
a `drillDown` block — see the *Drill-down* section above for the shape.

Failure kinds:
- `"mismatch"` — node count differs from expected (`expected`/`actual`/`diff` populated).
- `"timeout"` — engine didn't respond within `--timeout` seconds.
- `"error"`   — engine pipe closed or output unparseable (raw output captured in `engineOutput`, truncated to ~2 KB).

`source` is `static_analysis.jsonl:<line>` so a failure maps back to a
specific line in the input.

## Common recipes

```sh
# CI gate — quiet output, JSON report only
perftcheck -e ./engine -s static_analysis.jsonl --quiet --report perft.json

# Just the curated subset
perftcheck -e ./engine -s static_analysis.jsonl --quality high

# Only positions tagged with en-passant capture available
perftcheck -e ./engine -s static_analysis.jsonl --tag en_passant_capture_possible

# Halt at the first divergence — for bisection
perftcheck -e ./engine -s static_analysis.jsonl --fail-fast

# Engine that uses bare `perft N` (Potential, some others)
perftcheck -e ./potential -s static_analysis.jsonl --perft-command perft

# Stormphrax: bare `perft N` *and* bare-integer total
perftcheck -e ./stormphrax --perft-command perft --bare-number-total

# Stress-test the engine on exotic positions (EP-blocks-check etc.)
perftcheck -e ./engine --include-edge-cases

# Run *only* the 13 edge-case positions (cross-engine disagreement set)
perftcheck -e ./engine --include-edge-cases --tag edge_case_engine_disagreement

# Diagnose each mismatch down to the leaf position
perftcheck -e ./buggy -s static_analysis.jsonl \
           --drill-down --ref-engine ./GrandChessTree.Engine
```

## Building from source

```sh
dotnet build                                        # debug builds
dotnet run -- \
  --engine /path/to/engine \
  --static-analysis static_analysis.jsonl           # dev iteration

# Self-contained binaries for all platforms (~38 MB each, compressed)
./build/publish-all.sh                              # macOS / Linux
build\publish-all.cmd                               # Windows
```

Targets: `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`.
Each produces a single self-contained executable in `dist/<rid>/perftcheck[.exe]`
— no .NET runtime required on the target machine.

## Engine compatibility notes

- **Stockfish** — works out of the box. Uses `Nodes searched: N`; divide
  lines are `e2e4: 600`.
- **Komodo, Ethereal, Berserk, Lc0** — UCI standard, work out of the box.
- **Stormphrax** — needs `--perft-command perft --bare-number-total`. Total
  is a bare integer on its own line; `go perft` isn't recognised. Known
  divergence on EP-blocks-check, near-50-move-rule, and pathological
  piece counts (filtered by default — see `--include-edge-cases`).
- **StockDory** — works out of the box; total uses comma thousands
  separators (`Nodes searched: 1,234,567`), parsed correctly. Known
  divergence on 5 pathological positions (filtered by default).
- **Viridithas** — works out of the box; total embedded in a UCI info
  line (`info depth N nodes K time T nps M`), parsed correctly. Strict
  FEN parsing — rejects `fullmove=0` and `halfmove≥100` (not filtered
  here; those are FEN-format quirks, not move-gen bugs).
- **Pawnocchio** — works for most positions; total has trailing timing
  info (`Nodes searched: N in Tms (M nps)`), parsed correctly. Hangs on
  some positions — raise `--timeout` if you see timeouts.
- **Jet** — works out of the box; total is just `Nodes: N`.
- **Potential** — accepts only the bare `perft N` form (no `go` prefix).
  Pass `--perft-command perft`. Divide lines are `e2e4 600` (space, no
  colon) — parsed correctly.
- **GrandChessTree.Engine** (this repo) — has a minimal `uci` /
  `go perft N` shim so it works as a test engine, *and* serves as the
  drill-down oracle via its colon-delimited extension command
  `divide:<depth>:<mb>:<fen>[:<moves>]` (apply moves + divide + return
  resulting FEN in one round-trip).
- **MoveGen** (this repo, `Site/src/articles/MoveGen/`) — adds UCI mode to the demo Program.cs.

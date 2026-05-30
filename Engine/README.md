# Engine

The Grand Chess Tree perft engine — a multi-threaded move generator and
perft kernel built on PEXT-based magic bitboards, plus an
external-memory BFS pipeline for counting **unique** positions reachable
at a given ply.

| Project                  | Role                                                  |
|--------------------------|-------------------------------------------------------|
| `GrandChessTree.Engine`  | Console entry point. Driven over stdin (see below).   |
| `GrandChessTree.Shared`  | Move generator, perft kernels, BFS pipeline.          |

It's used directly by [Perft-Checker](../Perft-Checker) as the
drill-down oracle, by the
[Distributed Perft](../Leaderboard) workers to compute task fragments,
and standalone for local perft research.

## Requirements

- **.NET 10 SDK** for builds. The published binary is self-contained and
  needs no runtime on the target host.
- **x86_64 CPU with BMI2** (Intel Haswell+ / AMD Zen3+) by default. The
  move generator uses PEXT for magic-bitboard attack lookup, and the
  default build path has no software fallback — the engine throws
  `PlatformNotSupportedException` at the first move generation on
  hardware without BMI2.
- **ARM hosts (linux-arm64, osx-arm64)** are supported via a separate
  build that swaps PEXT for the classical magic-number multiplication.
  Enable it by passing `-p:DefineConstants="ARM"` to `dotnet publish`
  (see *Build* below). Functionally identical to the x86_64 build; NPS
  will differ from PEXT-on-x86, both because the lookup path is slower
  per square *and* because the hardware is different.

## Build

```sh
dotnet build GrandChessTree.sln -c Release
```

Self-contained single-file publish (default, for x86_64 hosts with BMI2):

```sh
dotnet publish GrandChessTree.Engine -c Release /p:Release=true
```

For ARM hosts, add `-p:DefineConstants="ARM"` so the source compiles
the magic-bitboard fallback path instead of the BMI2 PEXT path:

```sh
# osx-arm64
dotnet publish GrandChessTree.Engine -c Release -r osx-arm64 \
    /p:Release=true -p:DefineConstants="ARM"

# linux-arm64
dotnet publish GrandChessTree.Engine -c Release -r linux-arm64 \
    /p:Release=true -p:DefineConstants="ARM"
```

`-p:DefineConstants="ARM"` flips the `#if ARM` block in
`GrandChessTree.Shared/Precomputed/AttackTables.cs` to use
`MagicBitBoard.GetMoves(...)` for the sliding-piece attack lookup.
The release workflow at `.github/workflows/publish-engine.yml`
applies this automatically for the two arm64 runtime IDs; you only
need the flag when building locally.

The `Release=true` MSBuild property flips the project into
self-contained / single-file / R2R mode (see the conditional
`PropertyGroup` block in `GrandChessTree.Engine.csproj`). Output lands
in `GrandChessTree.Engine/bin/Release/net10.0/publish/` (or `-o <dir>`
if you pass an explicit output directory).

## Run

The engine reads colon-delimited commands from stdin, one per line, and
prints results to stdout. A consistent **end marker** —
`-----------------` on its own line — terminates every command's output
block, so harnesses can drive the engine reliably without knowing the
command's specific verbosity. Parse pattern:

```
        ←  -----results-----
        ←  <metric>: <value>      (one or more lines)
        ←  ...
        ←  -----------------      ← stop here, ready for next command
```

### Command reference

Most commands share the `<depth>:<mb_hash>:[<threads>:]<fen>` shape
(the BFS `wave_*` commands are the exception). `<mb_hash>` is sized
either **per-thread** or as a **shared TT** depending on the variant —
see *Threading model* below before picking a number.

| Command                                                  | Output line(s)                              | Notes                                                          |
|----------------------------------------------------------|---------------------------------------------|----------------------------------------------------------------|
| `stats:<d>:<mb>:<fen>`                                   | `nps:`, `time:`, then `nodes:`, `captures:`, `enpassants:`, `castles:`, `promotions:`, plus direct/discovered/double check & mate breakdowns | Full perft statistics, single-threaded |
| `stats_mt:<d>:<mb>:<t>:<fen>`                            | as above                                    | Same, fanned across `<t>` threads with a **shared** TT          |
| `nodes:<d>:<mb>:<fen>`                                   | `nodes: <N>`                                | Bare node count, single-threaded                                |
| `nodes_mt:<d>:<mb>:<t>:<fen>`                            | `nodes: <N>`                                | Multi-threaded with shared TT — fastest deep-perft mode         |
| `divide:<d>:<mb>:<fen>[:<moves>]`                        | `<uci_move> <N>` lines, then `nodes: <total>` | Per-root-move node counts. Optional space-separated UCI `<moves>` are applied first; the trailing `fen:` line reports the resulting FEN — used by perftcheck drill-down |
| `divide_mt:<d>:<mb>:<t>:<fen>`                           | as above                                    | Threaded; **`<mb>` is per-thread** (see warning below)          |
| `unique:<d>:<mb>:<fen>`                                  | `unique positions: <N>`                     | Hash-table dedup of all reachable positions                     |
| `unique_mt:<d>:<mb>:<t>:<fen>`                           | as above                                    | Multi-threaded unique with shared TT                            |
| `unique_dump:<d>:<mb>:<path>:<fen>`                      | as above + `dump bytes:` / `dump path:`     | Same but streams every inserted Zobrist key as raw u64 LE       |
| `unique_mt_dump:<d>:<mb>:<t>:<path>:<fen>`               | as above                                    | Threaded variant                                                |
| `unique_spill_mt:<d>:<mb>:<t>:<buckets>:<out_dir>:<fen>` | `unique positions:` + per-bucket sizes      | Scalable mode: 128-bit Zobrist keys streamed to `<buckets>` files. Post-process with the Rust merger — see [`GrandChessTree.Shared/UniquePerft/README.md`](GrandChessTree.Shared/UniquePerft/README.md) |
| `wave_init:<buckets>:<out_dir>:<fen>`                    | BFS bookkeeping                             | BFS wave[0]: writes 1 starting position to `<buckets>` bucket files (26-byte records) |
| `wave_expand:<in_dir>:<out_dir>:<buckets>:<threads>`     | BFS bookkeeping                             | BFS step: reads positions from `<in_dir>`, expands each by 1 ply, spills children to `<out_dir>` buckets |
| `decode_fen:<b64>`                                       | the decoded FEN                             | Inverse of the BFS pipeline's compact board encoding            |
| `help` / `h`                                             | full help text                              | —                                                               |
| `exit` / `quit`                                          | —                                           | Closes the process                                              |
| `clear`                                                  | —                                           | Clears the console                                              |

Special-position aliases accepted in any `<fen>` slot:

- `start` → `rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1`
- `kiwipete` → the Kiwipete position
- `sje` → the SJE test position

### UCI subset

A minimal UCI shim is recognised so the engine can be used as a test
target by perft-validation harnesses like
[perftcheck](../Perft-Checker):

```
→ uci
←   id name GrandChessTree
←   id author Tim Jones
←   uciok
→ isready
←   readyok
→ ucinewgame                  (no-op)
→ setoption name X value Y    (no-op)
→ position fen <FEN>
→ position startpos
→ go perft <N>
←   Nodes searched: <N>
→ quit
```

The colon-protocol and UCI shim share state — you can mix them in one
session but it's rarely useful in practice.

## Threading model

The `_mt` commands fan work out across `<threads>` threads, but the
hash-table allocation strategy **differs between variants** and is the
single most common footgun:

| Command           | TT strategy                     | `<mb_hash>` is…                |
|-------------------|---------------------------------|--------------------------------|
| `nodes_mt`        | one shared TT, allocated once   | total MB                       |
| `unique_mt`       | one shared TT                   | total MB                       |
| `unique_mt_dump`  | one shared TT                   | total MB                       |
| `unique_spill_mt` | no TT (streams to bucket files) | ignored / unused               |
| `stats_mt`        | per-thread, one TT per thread   | MB per thread                  |
| `divide_mt`       | per-thread, one TT per thread   | MB per thread                  |

For the **shared** variants, the sweet spot is 128–512 MB regardless of
thread count — over-allocating hurts cache locality. Aim for "just big
enough to cover the hot set".

For the **per-thread** variants total memory is `<mb_hash> × <threads>`
MB. A single allocation that would exceed `int.MaxValue` table entries
(~32 GB at 16 bytes per entry) throws `Hash table too large`. The
practical limit hits well before the cap on most boxes: 32 threads ×
2 GB = 64 GB, already saturating a typical 64 GB box. Recommended
starting point for `divide_mt` / `stats_mt` at depth 6–7 is
`<mb_hash>` around `1024` with `<threads>` matching your physical
core count.

The "launch depth" — how many ply of work are expanded into the work
queue before threads take over — is auto-tuned by depth (`launchDepth =
5` at d≥10, `4` at d≥8, `3` at d≥6, …). The goal is at least
`threadCount × 256` work items so the slowest thread doesn't dominate.

## Implementation notes

- **Magic bitboards via PEXT.** Bishop / rook attack lookups deposit
  the occupancy mask into a pre-computed offset table using
  `Bmi2.X64.ParallelBitExtract`. Faster than fancy-magic multiplication
  on any CPU that has the instruction. The classical magic-multiplication
  fallback compiles in when `ARM` is defined (see *Build*) — on M1 / M2
  / Apple Silicon and arm64 Linux that lookup path runs at full speed
  without BMI2.
- **Move encoding is a packed `uint`**: 4 bits piece, 6 bits from-square,
  6 bits to-square, 4 bits move-type (quiet / capture / castle /
  en-passant / 4 × promotion / 4 × capture-promotion). The
  16-bit packed format `(from | to<<6 | promo<<12)` used by Perft-Checker's
  binary corpus is the lossy distribution form of this.
- **Bulk perft**: at the penultimate ply, generation is short-circuited
  to a counting kernel that doesn't materialise the move list — gives
  ~3× the raw nps over divide at the same depth.
- **Server GC + dynamic PGO** are explicitly enabled in
  `GrandChessTree.Engine.csproj`. Important: dotnet's defaults flip
  these off in some publish modes.
- **`SkipLocalsInit`** is applied across the move-gen so the JIT
  doesn't zero stack frames that we know we're about to overwrite.

## Unique-position BFS pipeline

The external-memory BFS pipeline (the `wave_*` and `unique_spill_mt`
commands) lets you enumerate unique positions reachable at a target
depth without ever holding the full set in RAM. Position keys (128-bit
Zobrist) are bucketed to disk by a 7-bit prefix and merged with a Rust
post-processor.

See [`GrandChessTree.Shared/UniquePerft/README.md`](GrandChessTree.Shared/UniquePerft/README.md)
for the on-disk record format, bucket sizing, and the merger workflow.

## Wrapper script

`gct-engine.sh` is a thin POSIX wrapper that execs the engine DLL under
`dotnet`. Used by harnesses (e.g. perftcheck via `--ref-engine`) that
want a stable single binary path regardless of how the engine was
built:

```sh
./gct-engine.sh   # equivalent to: dotnet GrandChessTree.Engine/bin/.../GrandChessTree.Engine.dll
```

For published self-contained binaries the wrapper isn't needed — point
your harness at the binary directly.

## Examples

```sh
# Startpos perft to depth 6, 4 threads, 256 MB shared TT
echo "nodes_mt:6:256:4:start" | ./GrandChessTree.Engine
# → nodes: 119060324

# Divide from kiwipete at depth 5
echo "divide:5:128:kiwipete" | ./GrandChessTree.Engine
# → a2a3 4627439
# → ... (one line per legal move)
# → nodes: 193690690

# Drill-down style: apply two moves, then divide on the resulting position
echo "divide:4:128:start:e2e4 e7e5" | ./GrandChessTree.Engine
# → a2a3 8457
# → ...
# → fen: rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2

# Perft + full stats, single-threaded
echo "stats:5:256:start" | ./GrandChessTree.Engine
# → nps: 280.5M
# → time: 17ms
# → nodes:4865609
# → captures:82719
# → enpassants:258
# → castles:0
# → promotions:0
# → ...
```

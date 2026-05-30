# Unique-Positions

External-memory BFS pipeline for counting distinct chess positions reachable
in exactly *n* plies. Implements OEIS A083276 — castling rights and en
passant captures are part of the position.

The pipeline is a BFS over chess plies. Each completed wave_N (the unique
position set after N plies) lives on disk. The cycle is:

```
wave_N (K bucket files, sorted, deduped)
   │  read sequentially
   ▼
engine: per position, emit children (hash-routed to K output buckets)
   │
   ▼
spill_(N+1) (K bucket files, raw, with duplicates)
   │  per-bucket sort + dedup
   ▼
wave_(N+1) (K bucket files, sorted, deduped)
```

Hash routing makes per-bucket dedup independent of every other bucket, which
gives the merger trivial parallelism. The merger plays the role a TT would
in a recursive algorithm — a fresh external hash set per wave.

## Layout

```
Unique-Positions/
├── Cargo.toml                         package "unique-positions"
├── scripts/
│   ├── run_wave_bfs.sh                main orchestration: d1 → d11 BFS, d12 count
│   ├── watch_wave_bfs.sh              live progress JSON tailer
│   └── wave_manifest.sh               sha256 manifest of a wave dir
└── src/
    ├── main.rs                        legacy `compress-bench` benchmarking CLI
    ├── merge.rs                       count-mode merger (16-byte hash records → unique count)
    ├── wave_merge.rs                  BFS merger (34-byte records → sorted+deduped wave)
    └── bin/
        ├── wave_global_merge.rs       k-way merge of bucketed wave → single global xz
        └── global_to_buckets.rs       inverse: re-derive bucketed from global xz
```

The C# engine (`Engine/`) is the move-gen and lives separately.
This crate is the merger / sort / orchestration around it.

## Build

```bash
cd Unique-Positions
cargo build --release --bin wave_merge --bin wave_global_merge --bin global_to_buckets --bin merge
```

Plus the engine (one-time):
```bash
cd ../Engine
dotnet build -c Release GrandChessTree.Engine/GrandChessTree.Engine.csproj
```

## Run d1 → d11

```bash
K=64 K_PLY_10=256 K_PLY_11=4096 \
PASSES=1 PASSES_PLY_10=2 PASSES_PLY_11=10 \
THREADS=32 LOG2_MEMTABLE=30 \
LOG2_MEMTABLE_PLY_10=0 LOG2_MEMTABLE_PLY_11=0 \
WORK=/mnt/scratch/wave_bfs \
WAVE_DIR=/mnt/bulk/wave_bfs \
SPILL_OVERFLOW_DIR=/mnt/bulk/spill_overflow SPILL_OVERFLOW_PCT=50 \
COMPRESS=1 DFS_GLOBAL_MERGE=1 \
./scripts/run_wave_bfs.sh 11 start
```

Each ply checkpoints against the published reference values (Labelle /
Wismuth, in `WISMUTH=()` near the top of the script). A mismatch aborts.

## Run d12 (count only)

After d11 finishes:

```bash
COUNT_PLY=12 K_PLY_12=4096 PASSES_PLY_12=40 \
THREADS=32 LOG2_MEMTABLE=30 LOG2_MEMTABLE_PLY_12=0 \
WORK=/mnt/scratch/wave_bfs \
WAVE_DIR=/mnt/bulk/wave_bfs \
SPILL_OVERFLOW_DIR=/mnt/bulk/spill_overflow SPILL_OVERFLOW_PCT=50 \
RESUME=1 \
./scripts/run_wave_bfs.sh 11 start
```

(Yes, depth arg stays as `11` — COUNT_PLY=12 takes precedence and the BFS
loop is skipped.) Output is a single integer count.

## Env-var summary

| Var | Default | Meaning |
|---|---|---|
| `K`, `K_PLY_<N>` | 64 | output buckets per ply; higher K → more merger parallelism |
| `PASSES`, `PASSES_PLY_<N>` | 1 | output-bucket-range chunking; trades CPU for disk |
| `THREADS` | 32 | engine threads |
| `LOG2_MEMTABLE`, `LOG2_MEMTABLE_PLY_<N>` | 30 | log2 slots in the in-RAM dedup memtable (16 GB at 30); set `=0` from ply 10+ to disable the memtable once it saturates (unique count > capacity) |
| `WORK` | required | working dir (scratch tier, NVMe preferred) |
| `WAVE_DIR` | optional | bulk tier for wave_N storage (symlinked from `$WORK/wave_$N`) |
| `SPILL_OVERFLOW_DIR` | optional | second tier for spill, routed via symlinks |
| `SPILL_OVERFLOW_PCT` | 50 | % of buckets routed to overflow per pass |
| `COMPRESS` | 0 | xz-compress bucketed wave output (`--delta=dist=34`) |
| `DFS_GLOBAL_MERGE` | 0 | additionally produce `wave_${ply}_global.xz` per ply |
| `DFS_GLOBAL_PRIMARY` | 0 | delete bucketed after global merge (requires DFS_GLOBAL_MERGE) |
| `RESUME` | 0 | skip plies whose `.done` markers or global xz already exist |
| `COUNT_PLY` | 0 | if >0, count-only expand of that ply from existing wave_(N-1) |

## Verification

Wismuth/Labelle reference values are embedded in the script's `WISMUTH=()`
array (d1-d11). Every ply boundary checkpoints against this; a mismatch
aborts the run.

For d12 (no published reference), planned consistency checks:
- Cross-K: re-run a bucket subset at a different K — hash routes differ, same answer.
- Cross-hardware: re-run a bucket subset on a different machine.
- Mass-balance: engine emit counts × dedup ratio should match per-bucket uniques.
- sha256 manifest of bucket contents (`scripts/wave_manifest.sh`).

## Article

See [`Site/src/articles/depth-12-counting-draft.md`](../Site/src/articles/depth-12-counting-draft.md)
for a writeup of the architecture and the depth-by-depth design pivots.

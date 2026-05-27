# Unique-Position Counting

External-memory BFS for counting distinct chess positions reachable at exactly
ply N from the start position. Validates against
[Wismuth/Labelle](https://wismuth.com/chess/statistics-positions.html) at the
plies they published:

| Ply | Unique positions      | Source         | Status         |
|-----|----------------------:|----------------|----------------|
| 7   |           96,400,068  | Wismuth        | ✓ reproduced   |
| 8   |          988,187,354  | Wismuth        | ✓ reproduced   |
| 9   |        9,183,421,888  | Wismuth        | ✓ reproduced   |
| 10  |       85,375,278,064  | Wismuth        | not yet run    |
| 11  |      726,155,461,002  | Wismuth        | not yet run    |
| 12  |    ~5.6 T (estimate)  | unknown        | target         |

## Why external memory

The deduplicated position set at ply 12 is ~5–6 trillion positions ≈ 145 TB at
the engine's 26-byte fixed packed encoding. Won't fit in RAM, won't fit on a
single drive easily, and naive in-memory hash sets blow up much earlier
(ply 10 needs ~2 TB just for the set itself).

External-memory BFS sidesteps this by **streaming positions to disk, sorting
them by hash bucket, and deduplicating one bucket at a time**. Each phase is
either disk-sequential (fast on any medium) or in-memory (bounded by per-bucket
size, not total size).

## Pipeline

For each ply N the pipeline does two passes over disk:

### Phase 1 — Expand (`wave_expand`)

Read the deduplicated wave at ply N-1, generate every legal child, write each
child to a sharded *spill* directory.

- Input: `wave_(N-1)/bucket_*.bin` (sorted unique 26-byte position records)
- Output: `spill_N/bucket_*.bin` (unsorted, duplicates allowed)
- 32-thread parallel; each thread holds a tiny per-bucket write buffer (256 KB)
- Buckets routed by `hash >> shift` — same hash always lands in the same bucket
- Memtable suppression filters local duplicates before they spill (controlled by RAM)

Bucket count K is chosen so each merged bucket fits in RAM. Ply 9 uses K = 64;
ply 11+ uses K ≥ 256.

### Phase 2 — Merge (`wave_merge`, Rust)

For each spill bucket independently:

1. mmap the bucket file
2. Sort records in memory (CPU + RAM)
3. Run-length deduplicate (sorted → unique in one pass)
4. Write sorted unique output to `wave_N/bucket_NNNN.bin.tmp`, fsync, rename
5. Touch `wave_N/bucket_NNNN.done` marker (records `<seen>\n<unique>\n`)
6. **Delete the spill bucket** (when `WAVE_MERGE_DELETE_AFTER=1`) — frees disk
   progressively, keeps peak usage bounded

Independent per-bucket parallelism via rayon by default. With
`WAVE_MERGE_DELETE_AFTER=1` (or `WAVE_MERGE_SEQUENTIAL=1`) the merger processes
sequentially with delete-after-each — necessary when spill size approaches
free disk.

## Record encoding

Records are 26 bytes (engine `BoardStateSerialization`):

- bytes 0–15: piece data, 4-bit nibbles in popcount order over the occupancy
- bytes 16–23: occupancy bitboard (necessary to map nibble position → square)
- byte 24: en-passant file (high nibble, 0–8) + castle rights (low nibble)
- byte 25: side-to-move flag

A tighter fixed encoding isn't worth pursuing: the occupancy bitmap is required
to decode piece nibbles, and the trailer bytes already pack tightly. The
information-theoretic floor for a fully-decodable fixed record is ~20 bytes,
which would require variable-length entropy coding and break sort/dedup.

## Resume / checkpoint

Both phases are crash-tolerant. Restart the same command and they pick up.

| Phase   | Marker                                | What's safe on crash                    |
|---------|---------------------------------------|------------------------------------------|
| Expand  | `wave_N/_progress/<bucket>.bin.done`  | In-flight input buckets reprocess; their already-spilled records become duplicates that merge dedups out |
| Merge   | `wave_N/bucket_NNNN.bin.done`         | `.tmp` partials are cleaned up; bare `.bin` without `.done` is overwritten |

Force a full re-run:

- Merge: `WAVE_MERGE_NO_RESUME=1`
- Expand: `WAVE_EXPAND_NO_RESUME=1`

## Live progress

Both phases write `<out_dir>/progress.json` every ~3 seconds. Safe to
`cat`/`jq`/`watch -n5 cat` from another shell:

```json
{
  "phase": "wave_expand",
  "elapsed_seconds": 1234.5,
  "input_files_total": 256,
  "input_files_completed": 142,
  "input_files_in_progress": 32,
  "input_records_total": 9183421888,
  "input_records_processed": 5099001234,
  "input_records_per_sec": 4123000,
  "spill_records": 16500000000,
  "spill_bytes": 429000000000,
  "spill_bytes_per_sec": 348000000,
  "avg_children_per_position": 3.236,
  "eta_seconds": 1100
}
```

## Storage I/O profile

| Operation                  | Pattern                              | Drive requirement       |
|----------------------------|--------------------------------------|-------------------------|
| Read input wave            | Pure sequential                      | Any                     |
| Write spill                | Sharded append, K parallel streams   | NVMe, SSD, or many HDDs |
| Read spill for merge       | Sequential per bucket                | Any                     |
| Write deduped output       | Sequential per bucket                | Any                     |

**Spill writes are the only random-ish pattern.** A single HDD collapses to
30–80 MB/s under K concurrent bucket streams; 4+ HDDs in parallel recover to
250–600 MB/s because each drive only handles a subset of buckets.

## Volumes per ply (26-byte records)

| Ply | Wave on disk | Spill (raw children) | Dedup ratio |
|-----|-------------:|---------------------:|------------:|
| 9   |      220 GB  |              745 GB  |       3.12× |
| 10  |       2.2 TB |               7.0 TB |       3.20× |
| 11  |        19 TB |                65 TB |       3.40× |
| 12  |     ~145 TB  |             ~510 TB  |      ~3.40× |

For **ply 12 count only** (don't store wave_12), peak working set is
`wave_11 (~19 TB) + spill scratch (varies)`. Spill scratch determines pass
count: total spill ÷ scratch per pass = number of passes. Each pass requires
a full re-expand of wave_11, so more scratch = fewer passes = much less wall
time.

## Files in this directory

| File                              | Purpose                                                        |
|-----------------------------------|----------------------------------------------------------------|
| `PerftUnique.cs`                  | Entry points, memtable lifecycle, shard/spill plumbing         |
| `WhitePerftUnique.cs`             | White-to-move expand recursion + leaf serialization            |
| `BlackPerftUnique.cs`             | Black-to-move expand recursion + leaf serialization            |
| `BucketPositionSpillSink.cs`      | Thread-buffered, hash-routed disk writer (26 B records — full position) |
| `BucketSpillSink.cs`              | Same but for 16 B `(h1, h2)` records (count-only mode)         |
| `LockFreeHashSet.cs`              | In-memory 64-bit-key memtable, native-allocated, lock-free CAS |
| `LockFreeHashSet128.cs`           | Same but 128-bit composite keys — eliminates Zobrist collisions |
| `SecondaryHash.cs`                | Independent 64-bit hash via xorshift-seeded Zobrist (for 128-bit memtable) |
| `KeyDumpSink.cs`                  | Simple flat dump for debugging                                 |

The Rust merger lives under `research/compress-bench/src/wave_merge.rs` (uses
`memmap2` + `rayon`).

## Running it

```text
# UCI shim (Distributed-Perft/GrandChessTree.Engine)
wave_expand:<input_wave_dir>:<output_spill_dir>:<K>:<threads>
quit
```

Then run the merger:

```bash
WAVE_MERGE_DELETE_AFTER=1 \
  research/compress-bench/target/release/wave_merge \
  <spill_dir> <output_wave_dir>
```

See `Distributed-Perft/GrandChessTree.Engine/Program.cs` for the full command
surface (wave_init, wave_expand, unique_spill_mt, unique_mt_dump, etc.).

## Bench procedure on 7950X

Before launching a multi-day run on remote hardware, validate locally:

```bash
# 1. Re-run wave_8 → wave_9 end-to-end with progress + resume enabled.
#    Expected unique count: 9,183,421,888 (matches Wismuth).
dotnet run -c Release --project Distributed-Perft/GrandChessTree.Engine -- <<EOF
wave_expand:/path/to/wave_8:/path/to/spill_9:64:32
quit
EOF

WAVE_MERGE_DELETE_AFTER=1 \
  research/compress-bench/target/release/wave_merge \
  /path/to/spill_9 /path/to/wave_9

# 2. Resume test: Ctrl+C the merge after ~30 buckets, restart, confirm it
#    skips completed buckets and reaches the same final count.

# 3. Watch progress in another shell:
watch -n 5 'cat /path/to/wave_9/progress.json | jq .'
```

If the unique total matches 9,183,421,888 on the 7950X, you're clear to push
to the Hetzner box.

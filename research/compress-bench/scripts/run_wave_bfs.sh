#!/usr/bin/env bash
# Run the BFS unique-position pipeline from ply 0 to a target depth.
# Usage:  run_wave_bfs.sh [depth] [fen]    (defaults: depth=9, fen=start)
#
# Env overrides:
#   WORK=/path           work dir (default /tmp/wave_bfs)
#   K=64                 bucket count, power of 2. Use 256+ for ply 11+ so
#                        per-bucket merge fits in RAM
#   THREADS=32           worker threads in wave_expand
#   LOG2_MEMTABLE=30     memtable size in log2(slots) — 0 disables.
#                        Each slot is 16 B (128-bit hash). E.g. 30 = 16 GB,
#                        31 = 32 GB, 32 = 64 GB. Suppresses local duplicates
#                        before they hit disk; for ply 11+ this can cut spill
#                        volume 40-50%. Default 30 (16 GB) is safe on any
#                        32 GB+ machine; set lower (or 0) on small RAM.
#   COUNT_PLY=0          if >0, run only that depth in count-mode (16-byte
#                        spill records, no wave output). Used for ply 12 count
#                        on top of an existing wave_(N-1). Implies that wave
#                        already exists in WORK/wave_$((COUNT_PLY-1))/.
#
# Live system stats: run scripts/watch_wave_bfs.sh in another terminal.

set -euo pipefail

DEPTH=${1:-9}
FEN=${2:-start}
WORK=${WORK:-/tmp/wave_bfs}
K=${K:-64}
THREADS=${THREADS:-32}
LOG2_MEMTABLE=${LOG2_MEMTABLE:-30}
COUNT_PLY=${COUNT_PLY:-0}

REPO=$(cd "$(dirname "$0")/../../.." && pwd)
ENGINE="dotnet $REPO/Distributed-Perft/GrandChessTree.Engine/bin/Release/net10.0/GrandChessTree.Engine.dll"
WAVE_MERGER=$REPO/research/compress-bench/target/release/wave_merge
COUNT_MERGER=$REPO/research/compress-bench/target/release/merge

# Sanity checks
[[ -x "$WAVE_MERGER" ]] || { echo "missing $WAVE_MERGER — build with: (cd $REPO/research/compress-bench && cargo build --release --bin wave_merge)"; exit 1; }
[[ -f "$REPO/Distributed-Perft/GrandChessTree.Engine/bin/Release/net10.0/GrandChessTree.Engine.dll" ]] || { echo "missing engine dll — build with: dotnet build -c Release $REPO/Distributed-Perft/GrandChessTree.Engine/GrandChessTree.Engine.csproj"; exit 1; }

# Wismuth/Labelle reference for the start position.
declare -A WISMUTH=( [1]=20 [2]=400 [3]=5362 [4]=72078 [5]=822518 [6]=9417681 [7]=96400068 [8]=988187354 [9]=9183421888 [10]=85375278064 [11]=726155461002 )

# ---- COUNT_PLY mode: single-step count-only expansion ----
# For ply 12 counting: feed wave_11 -> spill_12 (16-byte) -> count via `merge`.
if [[ "$COUNT_PLY" -gt 0 ]]; then
    if [[ ! -x "$COUNT_MERGER" ]]; then
        echo "missing $COUNT_MERGER — build with: (cd $REPO/research/compress-bench && cargo build --release --bin merge)"
        exit 1
    fi
    prev=$((COUNT_PLY - 1))
    [[ -d "$WORK/wave_$prev" ]] || { echo "need WORK/wave_$prev to exist; got nothing at $WORK/wave_$prev"; exit 1; }
    echo "Count-mode: ply $COUNT_PLY from $WORK/wave_$prev (K=$K, threads=$THREADS, memtable=2^$LOG2_MEMTABLE)"
    rm -rf "$WORK/spill_$COUNT_PLY"
    echo "wave_expand:$WORK/wave_$prev:$WORK/spill_$COUNT_PLY:$K:$THREADS:$LOG2_MEMTABLE:count
quit" | $ENGINE 2>&1 | grep -E "^(input positions|child records|mode|memtable|elapsed)" | sed 's/^/    /'
    echo "Counting spill_$COUNT_PLY (16-byte records)..."
    $COUNT_MERGER "$WORK/spill_$COUNT_PLY" 2>&1 | tail -10
    exit 0
fi

# ---- Full BFS pipeline ----
echo "Running BFS pipeline: depth=$DEPTH fen=$FEN K=$K threads=$THREADS memtable=2^$LOG2_MEMTABLE"
echo "Work dir: $WORK"
echo "Live stats: run scripts/watch_wave_bfs.sh $WORK in another terminal"
echo ""

rm -rf "$WORK"
mkdir -p "$WORK"

echo "===== wave_init ====="
echo "wave_init:$K:$WORK/wave_0:$FEN
quit" | $ENGINE | tail -1

total_start=$(date +%s)
for ply in $(seq 1 "$DEPTH"); do
    prev=$((ply - 1))
    echo ""
    echo "===== ply $ply ====="
    t0=$(date +%s)

    echo "  expand: wave_$prev -> spill_$ply"
    echo "wave_expand:$WORK/wave_$prev:$WORK/spill_$ply:$K:$THREADS:$LOG2_MEMTABLE:full
quit" | $ENGINE 2>&1 | grep -E "^(input positions|child records|mode|memtable|elapsed)" | sed 's/^/    /'

    echo "  merge:  spill_$ply -> wave_$ply"
    WAVE_MERGE_DELETE_AFTER=1 $WAVE_MERGER "$WORK/spill_$ply" "$WORK/wave_$ply" 2>&1 | grep -E "^(total |dedup|elapsed)" | sed 's/^/    /'

    t1=$(date +%s)
    unique=$(ls "$WORK/wave_$ply"/bucket_*.bin 2>/dev/null | xargs -I{} stat -c %s {} | awk '{sum+=$1} END {print sum/26}')
    expected=${WISMUTH[$ply]:-unknown}
    if [[ "$FEN" == "start" && "$expected" != "unknown" ]]; then
        if [[ "$unique" == "$expected" ]]; then
            verdict="\033[32m✓ matches Wismuth ($expected)\033[0m"
        else
            verdict="\033[31m✗ MISMATCH (got $unique, expected $expected)\033[0m"
        fi
    else
        verdict="(no reference for this fen)"
    fi
    echo -e "  wave_$ply: $unique unique  $verdict  [ply $((t1-t0))s]"
done
total_end=$(date +%s)
echo ""
echo "===== DONE in $((total_end - total_start))s ====="
echo "Final wave_$DEPTH on disk:"
du -sh "$WORK/wave_$DEPTH"

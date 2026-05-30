#!/usr/bin/env bash
# bench_engine.sh — measures engine throughput for wave_expand wave_7 → spill_8.
#
# Use during the engine-optimization plan: median of 3 runs is the data point.
# Output one line per run, plus a final tagged "RESULT" line for grep-ability.
#
# Usage:
#   bench_engine.sh [N_RUNS]   (default N_RUNS=3)
#
# Env knobs:
#   WORK       (default /tmp/wave_d8bench)  — must already contain wave_7/
#   K          (default 64)                 — output bucket count
#   THREADS    (default 32)
#   L2M        (default 30)                 — log2 memtable size
#   ENGINE     (default constructed)
#   WAVE_MERGER (default constructed)
#
# Pre-conditions:
#   - $WORK/wave_7 has 64 bucket_*.bin done-markers and contains 96,400,068 records
#   - Engine and Rust binaries are built

set -euo pipefail

WORK=${WORK:-/tmp/wave_d8bench}
K=${K:-64}
THREADS=${THREADS:-32}
L2M=${L2M:-30}
N_RUNS=${1:-3}

# Repo-root-relative defaults; override any of the variables below if your
# layout differs. REPO_ROOT is computed from this script's own location
# (Unique-Positions/scripts/<this>).
REPO_ROOT="${REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"

ENGINE_DLL=${ENGINE_DLL:-$REPO_ROOT/Engine/GrandChessTree.Engine/bin/Release/net10.0/GrandChessTree.Engine.dll}
WAVE_MERGER=${WAVE_MERGER:-$REPO_ROOT/Unique-Positions/target/release/wave_merge}
DOTNET=${DOTNET:-dotnet}

ENGINE="$DOTNET $ENGINE_DLL"

# Sanity
[[ -f "$ENGINE_DLL" ]] || { echo "missing engine DLL: $ENGINE_DLL"; exit 2; }
[[ -x "$WAVE_MERGER" ]] || { echo "missing wave_merge: $WAVE_MERGER"; exit 2; }
[[ -d "$WORK/wave_7" ]] || { echo "missing wave_7 input: $WORK/wave_7"; exit 2; }

# Expected output values
EXPECTED_PARENTS=96400068
EXPECTED_D8_UNIQUE=988187354

best_rate=0
best_wall=""
walls=()

echo "bench_engine: $N_RUNS runs of wave_expand wave_7 → spill_8 (K=$K, THREADS=$THREADS, L2M=$L2M)"
echo "engine: $ENGINE_DLL"
echo ""

for run in $(seq 1 "$N_RUNS"); do
    rm -rf "$WORK/spill_8" "$WORK/wave_8" 2>/dev/null
    log=$(mktemp)

    t0=$(date +%s.%N)
    echo "wave_expand:$WORK/wave_7:$WORK/spill_8:$K:$THREADS:$L2M:full
quit" | $ENGINE 2>&1 > "$log"
    t1=$(date +%s.%N)
    wall=$(awk -v a="$t0" -v b="$t1" 'BEGIN{printf "%.3f", b-a}')

    parents=$(grep -oP 'input positions:\s+\K[0-9]+' "$log" | head -1)
    children=$(grep -oP 'child records:\s+\K[0-9]+' "$log" | head -1)
    engine_elapsed_ms=$(grep -oP 'elapsed:\s+\K[0-9]+(?=ms)' "$log" | head -1)

    # children/sec at this run
    rate=$(awk -v c="${children:-0}" -v w="$wall" 'BEGIN{ if (w > 0) printf "%.3f", c/w/1000000; else print "0"; }')

    # Quick correctness gate on parent count
    parents_ok="✓"
    [[ "$parents" != "$EXPECTED_PARENTS" ]] && parents_ok="✗ got=$parents expect=$EXPECTED_PARENTS"

    printf '  run %d: wall=%6.3fs   children=%s   %s M/s   parents=%s\n' \
        "$run" "$wall" "$children" "$rate" "$parents_ok"

    walls+=("$wall")
    rm -f "$log"
done

# Median wall (sort + middle)
sorted=$(printf '%s\n' "${walls[@]}" | sort -n)
mid_index=$(( N_RUNS / 2 + 1 ))
median_wall=$(echo "$sorted" | sed -n "${mid_index}p")
median_rate=$(awk -v w="$median_wall" -v p="$EXPECTED_PARENTS" 'BEGIN{ if (w > 0) printf "%.3f", p*27/w/1000000; }')
# rate uses assumed 27 children/parent — close to actual 26.35 measured

echo ""
echo "median engine wall (s): $median_wall"

# Final validation: run merger once on the LAST run's spill_8 to verify Wismuth
echo ""
echo "validating d8 unique count via wave_merge..."
WAVE_MERGE_DELETE_AFTER=1 "$WAVE_MERGER" "$WORK/spill_8" "$WORK/wave_8" 2>&1 | grep -E "total unique positions"
unique=$(find "$WORK/wave_8" -name 'bucket_*.bin.done' -print0 2>/dev/null | xargs -0 -r awk 'FNR==2 {s+=$1} END {print s+0}')
unique=${unique:-0}
if [[ "$unique" == "$EXPECTED_D8_UNIQUE" ]]; then
    verdict="✓ matches Wismuth (988,187,354)"
else
    verdict="✗ MISMATCH got=$unique expected=$EXPECTED_D8_UNIQUE"
fi
echo "  d8 unique: $unique  $verdict"

rm -rf "$WORK/spill_8" "$WORK/wave_8"

# Tagged final line for easy grep / parse in plan tracking
echo ""
echo "RESULT median_wall=${median_wall}s parents=$EXPECTED_PARENTS d8_correct=$( [[ "$unique" == "$EXPECTED_D8_UNIQUE" ]] && echo yes || echo no )"

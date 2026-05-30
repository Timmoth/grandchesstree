#!/usr/bin/env bash
# profile_engine.sh — capture a CPU profile of one wave_expand wave_7 → spill_8 run.
#
# Output: speedscope JSON in $WORK/profile-speedscope.json plus a raw .nettrace.
# Open the speedscope file at https://www.speedscope.app/ (drag-drop) for a
# flame-graph view, or load the .nettrace in PerfView.
#
# Usage:
#   profile_engine.sh
#
# Env knobs: WORK (default /tmp/wave_d8bench), THREADS (default 32), K (64), L2M (30).

set -euo pipefail

WORK=${WORK:-/tmp/wave_d8bench}
K=${K:-64}
THREADS=${THREADS:-32}
L2M=${L2M:-30}
DURATION_SEC=${DURATION_SEC:-60}

# Repo-root-relative defaults; override any of the variables below if your
# layout differs. REPO_ROOT is computed from this script's own location
# (Unique-Positions/scripts/<this>).
REPO_ROOT="${REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"

ENGINE_DLL=${ENGINE_DLL:-$REPO_ROOT/Engine/GrandChessTree.Engine/bin/Release/net10.0/GrandChessTree.Engine.dll}
DOTNET=${DOTNET:-dotnet}
DOTNET_TRACE=${DOTNET_TRACE:-dotnet-trace}
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

[[ -f "$ENGINE_DLL" ]] || { echo "missing engine DLL: $ENGINE_DLL"; exit 2; }
[[ -d "$WORK/wave_7" ]] || { echo "missing wave_7: $WORK/wave_7"; exit 2; }
[[ -x "$DOTNET_TRACE" ]] || { echo "missing dotnet-trace: $DOTNET_TRACE"; exit 2; }

rm -rf "$WORK/spill_8" "$WORK/wave_8"

TRACE_FILE="$WORK/profile.nettrace"
SPEEDSCOPE_FILE="$WORK/profile-speedscope.json"
rm -f "$TRACE_FILE" "$SPEEDSCOPE_FILE"

# Run the engine in background.
cmd="wave_expand:$WORK/wave_7:$WORK/spill_8:$K:$THREADS:$L2M:full
quit"

echo "starting engine in background..."
echo "$cmd" | $DOTNET "$ENGINE_DLL" > "$WORK/engine.log" 2>&1 &
ENGINE_PID=$!

# Wait for the process to be ready (very short)
sleep 1

echo "attaching dotnet-trace to PID $ENGINE_PID for ${DURATION_SEC}s..."
$DOTNET_TRACE collect \
    -p "$ENGINE_PID" \
    --duration "00:00:$(printf '%02d' "$DURATION_SEC")" \
    --providers Microsoft-DotNETCore-SampleProfiler \
    -o "$TRACE_FILE" \
    --format NetTrace 2>&1 | tail -20

# Wait for engine to finish
wait "$ENGINE_PID" || true
echo "engine wall time: $(grep -oP 'elapsed:\s+\K[0-9]+(?=ms)' "$WORK/engine.log" | head -1) ms"
grep -E "child records|input positions" "$WORK/engine.log" | head -2

echo ""
echo "converting to speedscope..."
$DOTNET_TRACE convert --format Speedscope "$TRACE_FILE" -o "$SPEEDSCOPE_FILE"
ls -la "$TRACE_FILE" "$SPEEDSCOPE_FILE"

echo ""
echo "drop $SPEEDSCOPE_FILE on https://www.speedscope.app/ to view"

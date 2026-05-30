#!/usr/bin/env bash
# profile_perf2.sh — Linux perf record (system-wide) of one wave_expand run.
# Filters report output to the engine process. More robust than -p PID on
# some kernels where threaded apps don't get followed properly.

set -euo pipefail

WORK=${WORK:-/tmp/wave_d8bench}
K=${K:-64}
THREADS=${THREADS:-32}
L2M=${L2M:-30}
DURATION_SEC=${DURATION_SEC:-40}

# Repo-root-relative defaults; override any of the variables below if your
# layout differs. REPO_ROOT is computed from this script's own location
# (Unique-Positions/scripts/<this>).
REPO_ROOT="${REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"

ENGINE_DLL=${ENGINE_DLL:-$REPO_ROOT/Engine/GrandChessTree.Engine/bin/Release/net10.0/GrandChessTree.Engine.dll}
DOTNET=${DOTNET:-dotnet}
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

[[ -f "$ENGINE_DLL" ]] || { echo "missing engine DLL"; exit 2; }
[[ -d "$WORK/wave_7" ]] || { echo "missing wave_7"; exit 2; }

rm -rf "$WORK/spill_8" "$WORK/wave_8"

PERF_DATA="$WORK/perf.data"
rm -f "$PERF_DATA" /tmp/perf-*.map

cmd="wave_expand:$WORK/wave_7:$WORK/spill_8:$K:$THREADS:$L2M:full
quit"

echo "starting engine with PerfMap..."
DOTNET_PerfMapEnabled=1 \
DOTNET_EnableWriteXorExecute=0 \
  bash -c 'echo "'"$cmd"'" | exec '"$DOTNET"' "'"$ENGINE_DLL"'" > "'"$WORK"'/engine.log" 2>&1' &
ENGINE_PID=$!

sleep 2
echo "engine pid=$ENGINE_PID; capturing system-wide for ${DURATION_SEC}s..."

# System-wide capture. Higher-rate frequency. No call-graph (avoids huge data).
echo "${SUDO_PASSWORD:-}" | sudo -S perf record -a -F 999 \
    -o "$PERF_DATA" -- sleep "$DURATION_SEC" 2>&1 | tail -3 || true

echo "${SUDO_PASSWORD:-}" | sudo -S chown $(id -u):$(id -g) "$PERF_DATA"

wait "$ENGINE_PID" || true
grep -E "input positions|child records|elapsed" "$WORK/engine.log" | head -3

ls -la "$PERF_DATA"
ls -la /tmp/perf-${ENGINE_PID}.map 2>/dev/null | head -3 || echo "no perfmap at /tmp/perf-${ENGINE_PID}.map"
ls -la /tmp/perf-*.map 2>/dev/null | head -3

echo ""
echo "--- Top 30 user symbols by self-CPU (filtered to dotnet process) ---"
perf report -i "$PERF_DATA" --no-children -F overhead,sample,symbol \
    --dso=dotnet --stdio 2>/dev/null \
    | awk '/^#/ {next} NF > 0 {print}' | head -35 || true

echo ""
echo "--- Top 30 symbols overall (all DSOs, comm=dotnet) ---"
perf report -i "$PERF_DATA" --no-children -F overhead,sample,comm,dso,symbol \
    --comms=dotnet --stdio 2>/dev/null \
    | awk '/^#/ {next} NF > 0 {print}' | head -35 || true

echo ""
echo "raw report at: perf report -i $PERF_DATA --stdio | less"

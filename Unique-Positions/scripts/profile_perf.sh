#!/usr/bin/env bash
# profile_perf.sh — Linux perf record + .NET PerfMap profile of one wave_expand
# wave_7 -> spill_8 run. Resolves JIT'd managed method names (the dotnet-trace
# sampler attributes inlined code only at managed call boundaries; perf with
# DOTNET_PerfMapEnabled samples down to JIT'd instructions).
#
# Outputs a flat self-time summary and a Top-30 collapsed-by-symbol report.
#
# Usage:
#   profile_perf.sh
#
# Env: WORK, K, THREADS, L2M, DURATION_SEC.

set -euo pipefail

WORK=${WORK:-/tmp/wave_d8bench}
K=${K:-64}
THREADS=${THREADS:-32}
L2M=${L2M:-30}
DURATION_SEC=${DURATION_SEC:-45}

# Repo-root-relative defaults; override any of the variables below if your
# layout differs. REPO_ROOT is computed from this script's own location
# (Unique-Positions/scripts/<this>).
REPO_ROOT="${REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"

ENGINE_DLL=${ENGINE_DLL:-$REPO_ROOT/Engine/GrandChessTree.Engine/bin/Release/net10.0/GrandChessTree.Engine.dll}
DOTNET=${DOTNET:-dotnet}
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

[[ -f "$ENGINE_DLL" ]] || { echo "missing engine DLL: $ENGINE_DLL"; exit 2; }
[[ -d "$WORK/wave_7" ]] || { echo "missing wave_7: $WORK/wave_7"; exit 2; }

rm -rf "$WORK/spill_8" "$WORK/wave_8"

PERF_DATA="$WORK/perf.data"
REPORT="$WORK/perf-report.txt"
TOP="$WORK/perf-top.txt"
rm -f "$PERF_DATA" "$REPORT" "$TOP" /tmp/perf-*.map

cmd="wave_expand:$WORK/wave_7:$WORK/spill_8:$K:$THREADS:$L2M:full
quit"

echo "starting engine with PerfMap enabled..."
# DOTNET_PerfMapEnabled=1: write /tmp/perf-<PID>.map (function name + addr range).
# DOTNET_EnableWriteXorExecute=0: PerfMap requires JIT pages to stay readable.
DOTNET_PerfMapEnabled=1 \
DOTNET_EnableWriteXorExecute=0 \
  bash -c 'echo "'"$cmd"'" | exec '"$DOTNET"' "'"$ENGINE_DLL"'" > "'"$WORK"'/engine.log" 2>&1' &
ENGINE_PID=$!

sleep 2  # let JIT warm up + emit perfmap

echo "engine pid=$ENGINE_PID; recording perf for ${DURATION_SEC}s..."
echo "${SUDO_PASSWORD:-}" | sudo -S perf record -F 999 -p "$ENGINE_PID" -g --call-graph=fp \
    -o "$PERF_DATA" -- sleep "$DURATION_SEC" 2>&1 | tail -3 || true
echo "${SUDO_PASSWORD:-}" | sudo -S chown $(id -u):$(id -g) "$PERF_DATA" 2>/dev/null || true

wait "$ENGINE_PID" || true

if [[ -s "$WORK/engine.log" ]]; then
    grep -E "child records|input positions|elapsed:" "$WORK/engine.log" | head -3
fi

echo ""
echo "--- top symbols by self-CPU (perf report --no-children -F overhead,sample,symbol --stdio) ---"
perf report -i "$PERF_DATA" --no-children -F overhead,sample,symbol --stdio 2>/dev/null \
    | awk '/^#/ {next} NF > 0 {print}' \
    | head -40 | tee "$TOP"

echo ""
echo "Full perf data at: $PERF_DATA  (use 'perf report -i $PERF_DATA' for interactive)"

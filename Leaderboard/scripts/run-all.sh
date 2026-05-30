#!/usr/bin/env bash
# Run the full Leaderboard benchmark suite. Iterates every engine descriptor
# under engines/*.json, runs perft_war.py for each, then aggregates.
#
# Prints a header before each engine showing:
#   - which engine (X of N)
#   - how many (mode, position) slots that engine contributes
#   - cumulative elapsed
#   - rolling ETA computed from average time per slot so far
#
# ETA refines as more engines complete; early estimates (after 1–2 engines)
# can be off if those engines were unusually fast or slow.
#
# Usage:
#   scripts/run-all.sh                                # 60s budget, all engines
#   scripts/run-all.sh --budget-sec 30
#   scripts/run-all.sh --engines tgct_engine,mperft   # subset
#   scripts/run-all.sh --positions startpos           # one position only

set -euo pipefail

BUDGET_SEC=60
RESULTS_DIR=results
POSITIONS=""
ENGINES=""
SKIP_AGGREGATE=false

usage() {
  cat <<'EOF'
Run the full Leaderboard benchmark suite.

Usage:
  scripts/run-all.sh [options]

Options:
  --budget-sec N    Per-(mode,position) wall-clock budget [default 60]
  --positions LIST  Comma-separated subset (e.g. startpos,kiwipete)
  --engines LIST    Comma-separated subset of engine names
  --results-dir DIR Where to write per-engine results [default results]
  --no-aggregate    Skip the final aggregate step
  -h, --help        Show this help

Examples:
  scripts/run-all.sh                              # 60s budget, all engines
  scripts/run-all.sh --budget-sec 30
  scripts/run-all.sh --engines tgct_engine,mperft
  scripts/run-all.sh --positions startpos
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --budget-sec)   BUDGET_SEC="$2"; shift 2 ;;
    --positions)    POSITIONS="$2"; shift 2 ;;
    --engines)      ENGINES="$2"; shift 2 ;;
    --results-dir)  RESULTS_DIR="$2"; shift 2 ;;
    --no-aggregate) SKIP_AGGREGATE=true; shift ;;
    -h|--help)      usage; exit 0 ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done

# Resolve Leaderboard/ regardless of where the user invoked the script from.
HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE/.."

# Build the engine list (skip example-* descriptors).
ENGINE_FILES=()
if [ -n "$ENGINES" ]; then
  IFS=',' read -ra names <<<"$ENGINES"
  for n in "${names[@]}"; do
    f="engines/${n}.json"
    [ -f "$f" ] || { echo "no such engine: $n ($f)" >&2; exit 2; }
    ENGINE_FILES+=("$f")
  done
else
  for f in engines/*.json; do
    case "$(basename "$f")" in
      example-*.json) continue ;;
    esac
    ENGINE_FILES+=("$f")
  done
fi
N_ENGINES="${#ENGINE_FILES[@]}"
[ "$N_ENGINES" -gt 0 ] || { echo "no engines selected" >&2; exit 2; }

# Position count (used for slot accounting + the ETA).
if [ -n "$POSITIONS" ]; then
  IFS=',' read -ra _pos <<<"$POSITIONS"
  POS_COUNT="${#_pos[@]}"
else
  # Fall back to the count declared in perft_war.py's POSITIONS table.
  POS_COUNT=$(python3 -c "import perft_war; print(len(perft_war.POSITIONS))")
fi

# Count modes per engine + total slots up front.
MODES_PER_ENGINE=()
SLOTS_PER_ENGINE=()
total_slots=0
for f in "${ENGINE_FILES[@]}"; do
  m=$(python3 -c "import json; print(len(json.load(open('$f'))['modes']))")
  s=$((m * POS_COUNT))
  MODES_PER_ENGINE+=("$m")
  SLOTS_PER_ENGINE+=("$s")
  total_slots=$((total_slots + s))
done

worst_case_min=$(( (total_slots * BUDGET_SEC + 59) / 60 ))

fmt_hms() {
  local s=$1
  printf "%d:%02d:%02d" $((s/3600)) $((s%3600/60)) $((s%60))
}

echo "==============================================================="
echo "Suite: ${N_ENGINES} engines, ${total_slots} (mode,position) slots"
echo "Budget: ${BUDGET_SEC}s per slot — worst-case ~${worst_case_min} min"
[ -n "$POSITIONS" ] && echo "Positions filter: ${POSITIONS}"
echo "==============================================================="

START_TS=$(date +%s)
COMPLETED_SLOTS=0

for i in "${!ENGINE_FILES[@]}"; do
  f="${ENGINE_FILES[$i]}"
  m="${MODES_PER_ENGINE[$i]}"
  s="${SLOTS_PER_ENGINE[$i]}"
  name=$(basename "$f" .json)
  idx=$((i+1))

  elapsed=$(($(date +%s) - START_TS))
  if [ "$COMPLETED_SLOTS" -gt 0 ]; then
    # Average per-slot time so far → project remaining.
    avg_x100=$(( elapsed * 100 / COMPLETED_SLOTS ))
    eta=$(( avg_x100 * (total_slots - COMPLETED_SLOTS) / 100 ))
    eta_str=$(fmt_hms "$eta")
  else
    eta_str="?"
  fi

  echo
  echo "▶ [$idx/$N_ENGINES] $name — ${m} modes × ${POS_COUNT} positions = ${s} slots"
  echo "  elapsed $(fmt_hms "$elapsed")  |  slots ${COMPLETED_SLOTS}/${total_slots}  |  eta ${eta_str}"
  echo

  args=(run "$f" --budget-sec "$BUDGET_SEC" --results-dir "$RESULTS_DIR")
  [ -n "$POSITIONS" ] && args+=(--positions "$POSITIONS")
  python3 -u perft_war.py "${args[@]}"

  COMPLETED_SLOTS=$((COMPLETED_SLOTS + s))
done

if [ "$SKIP_AGGREGATE" = false ]; then
  echo
  echo "▶ Aggregating into ${RESULTS_DIR}/leaderboard.json"
  python3 -u perft_war.py aggregate --results-dir "$RESULTS_DIR"
fi

TOTAL=$(($(date +%s) - START_TS))
echo
echo "==============================================================="
echo "Done in $(fmt_hms "$TOTAL")."
echo "==============================================================="

#!/usr/bin/env bash
# UCI wrapper around ankan-ban/perft_cpu_2026.
#
# perft_cpu_2026 is a perft-only CLI tool: `perft_cpu <fen> <depth>
# [-nott] [-htt <MB>] [-mt <N>]`. Leaderboard drives UCI over stdin, so this
# wrapper:
#   1. Reads UCI commands off stdin.
#   2. On each `go perft <n>`, spawns perft_cpu fresh with this wrapper's
#      mode flags + the current FEN and depth, forwards stdout, prints
#      `perft-done` as the end marker.
#
# Mode flags (-mt, -htt, -nott) come in via this wrapper's argv — see
# engines/perft_cpu_2026.json's `launch` lines.

set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
PERFT_BIN="$HERE/../../bin/perft_cpu_2026/build/perft_cpu"

# Mode args forwarded as-is to perft_cpu.
EXTRA_ARGS=("$@")

FEN="rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"

while IFS= read -r line; do
  case "$line" in
    uci)
      echo "id name perft_cpu_2026-wrapper"
      echo "id author perft_cpu_2026 UCI wrapper"
      echo "uciok"
      ;;
    isready)
      echo "readyok"
      ;;
    ucinewgame|"position startpos"*)
      FEN="rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"
      ;;
    "position fen "*)
      rest="${line#position fen }"
      FEN="${rest%% moves*}"
      ;;
    "go perft "*|"perft "*)
      depth="${line##* }"
      # perft_cpu takes positional <fen> <depth>, then mode flags.
      "$PERFT_BIN" "$FEN" "$depth" "${EXTRA_ARGS[@]}" </dev/null 2>&1
      echo
      echo "perft-done"
      ;;
    quit)
      break
      ;;
  esac
done

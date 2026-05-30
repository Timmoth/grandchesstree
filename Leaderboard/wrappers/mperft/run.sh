#!/usr/bin/env bash
# UCI wrapper around abulmo/MPerft.
#
# MPerft is a perft-only CLI tool, not a UCI engine. Leaderboard drives every
# entry over stdin in a UCI subset, so this wrapper:
#   1. Reads UCI commands off stdin (uci, isready, position fen <fen>,
#      go perft <n>, quit).
#   2. Holds the current FEN in a shell variable.
#   3. On each `go perft <n>`, spawns mperft fresh with --fen / --depth
#      and the mode flags this wrapper was invoked with.
#   4. Passes mperft's stdout through verbatim (so Leaderboard's verify_nodes
#      sees the node count) and prints a fixed `perft-done` end marker.
#
# Mode-specific flags (--threads N, --hash N, --bulk) come in via this
# wrapper's argv — see engines/mperft.json's `launch` lines.

set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
MPERFT_BIN="$HERE/../../bin/mperft/mperft"

# Mode args (e.g. --threads 1 --hash 0 --bulk) are forwarded verbatim.
EXTRA_ARGS=("$@")

FEN="rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"

while IFS= read -r line; do
  case "$line" in
    uci)
      echo "id name mperft-wrapper"
      echo "id author MPerft UCI wrapper"
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
      "$MPERFT_BIN" --fen "$FEN" --depth "$depth" "${EXTRA_ARGS[@]}" 2>&1
      echo "perft-done"
      ;;
    quit)
      break
      ;;
  esac
done

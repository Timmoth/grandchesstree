#!/usr/bin/env bash
# UCI wrapper around Gigantua/Gigantua.
#
# Gigantua takes positional args:   gigantua "<fen>" <depth>
# It is purely single-threaded, bulk-counting, no transposition table — so
# this wrapper accepts no mode flags (the descriptor doesn't define any).
# Leaderboard drives a UCI subset over stdin; we translate per `go perft <n>`
# into a fresh Gigantua invocation, then print `perft-done` as the end
# marker that engines/gigantua.json's `end_re` matches.

set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
GIGANTUA_BIN="$HERE/../../bin/gigantua/gigantua"

FEN="rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"

while IFS= read -r line; do
  case "$line" in
    uci)
      echo "id name gigantua-wrapper"
      echo "id author Gigantua UCI wrapper"
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
      # Critical: redirect stdin to /dev/null. Gigantua ends with a
      # std::cin.get() "press any key" pause and would otherwise (a) hang
      # forever in our pipeline, and (b) compete with this wrapper for
      # bytes from perft_war's command pipe.
      "$GIGANTUA_BIN" "$FEN" "$depth" </dev/null 2>&1
      # Gigantua's last line ("Press any key to exit...") has no trailing
      # newline, so our `perft-done` marker would otherwise collide with
      # it on the same physical line and the runner's `^perft-done$`
      # end_re would never match. Emit an explicit newline first.
      echo
      echo "perft-done"
      ;;
    quit)
      break
      ;;
  esac
done

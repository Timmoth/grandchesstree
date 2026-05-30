#!/usr/bin/env bash
# UCI wrapper around thuijbregts/chessbit.
#
# Chessbit is an interactive CLI tool with its own command set
# (`setfen <fen>`, `perft <depth>`, `exit`). Leaderboard drives a UCI
# subset over stdin, so this wrapper translates per `go perft <n>` into
# a fresh chessbit invocation, feeding it the FEN + depth via stdin,
# then prints `perft-done` as the end marker.
#
# Per-call re-spawn means any internal state inside chessbit (if any)
# is fresh each call. Chessbit appears to be stateless besides the
# board position, so this is fine.

set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
# chessbit's source lives in repo/chessbit/, and the install script
# leaves the built binary there as chessbit/chessbit. We can't lift it
# one level up because `bin/chessbit/chessbit` is the source dir name.
CHESSBIT_BIN="$HERE/../../bin/chessbit/chessbit/chessbit"

FEN="rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"

while IFS= read -r line; do
  case "$line" in
    uci)
      echo "id name chessbit-wrapper"
      echo "id author chessbit UCI wrapper"
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
      printf 'setfen %s\nperft %s\nexit\n' "$FEN" "$depth" \
        | "$CHESSBIT_BIN" 2>&1
      echo
      echo "perft-done"
      ;;
    quit)
      break
      ;;
  esac
done

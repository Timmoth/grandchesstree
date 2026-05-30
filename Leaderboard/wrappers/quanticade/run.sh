#!/usr/bin/env bash
# UCI wrapper around Quanticade/Quanticade.
#
# Quanticade has an upstream bug in `perft_test`: the searchinfo->nodes
# counter is not reset between perft calls, so a second `go perft N` in
# the same engine session reports cumulative nodes (e.g. d1=20 then
# d2=420 instead of 400). The wrapper sidesteps this by spawning a
# fresh Quanticade per `go perft N` call.

set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
QUANTICADE_BIN="$HERE/../../bin/quanticade/Quanticade"

FEN="rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"

while IFS= read -r line; do
  case "$line" in
    uci)
      echo "id name quanticade-wrapper"
      echo "id author Quanticade UCI wrapper"
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
      # Drive Quanticade for a single perft, then quit. Fresh process means
      # the perft nodes counter starts at zero. The wrapper marker
      # `perft-done` follows on its own line for the runner's end_re.
      printf 'position fen %s\ngo perft %s\nquit\n' "$FEN" "$depth" \
        | "$QUANTICADE_BIN" 2>&1
      echo
      echo "perft-done"
      ;;
    quit)
      break
      ;;
  esac
done

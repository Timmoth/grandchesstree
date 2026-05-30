#!/usr/bin/env bash
# Clone, build, and verify MPerft → bin/mperft/mperft.
#
# MPerft is a perft-only CLI tool by Richard Delorme (abulmo). Pure C,
# no external deps, simple Makefile. It does NOT speak UCI — Leaderboard
# drives it via wrappers/mperft/run.sh.

ENGINE="mperft"
REPO="https://github.com/abulmo/MPerft"
ENGINE_DIR="bin/mperft"
BINARY="$ENGINE_DIR/mperft"
WRAPPER="wrappers/mperft/run.sh"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

clone_or_keep "$ENGINE_DIR" "$REPO"

[ -f "$WRAPPER" ] || die "wrapper missing at $WRAPPER"
chmod +x "$WRAPPER" 2>/dev/null || true

log "building (CC=clang make)"
(
  cd "$ENGINE_DIR"
  make clean >/dev/null 2>&1 || true
  CC=clang make
)

# MPerft's Makefile lands the binary at the repo root or in src/ depending
# on version. Probe both and symlink to the descriptor path if needed.
if [ ! -x "$BINARY" ]; then
  alt=$(find "$ENGINE_DIR" -maxdepth 2 -type f -perm -111 -name 'mperft*' 2>/dev/null | head -1 || true)
  if [ -n "$alt" ] && [ "$alt" != "$BINARY" ]; then
    log "found binary at $alt — symlinking to $BINARY"
    ln -sf "$(basename "$alt")" "$BINARY" || ln -sf "$alt" "$BINARY"
  fi
fi
[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

# Verify via the WRAPPER (not the raw binary — the descriptor talks UCI).
# verify_perft invokes the path as a single executable, so we pass just the
# wrapper without mode flags; mperft runs with its defaults, which is all
# we need for "is the chain alive and does perft 4 == 197281".
out=$(verify_perft "$WRAPPER") || die "perft test failed (via wrapper)"

commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "mperft commit: $commit → consider updating engines/$ENGINE.json's \"version\""

log "done. launch: $WRAPPER (binary: $BINARY)"

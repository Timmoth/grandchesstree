#!/usr/bin/env bash
# Clone, build, and verify RubiChess → bin/rubichess/src/RubiChess.
# RubiChess's Makefile auto-detects x86 features but doesn't have first-
# class arm64; on arm64 hosts we force ARCH=native and let the compiler
# do the right thing.

ENGINE="rubichess"
REPO="https://github.com/Matthies/RubiChess"
ENGINE_DIR="bin/rubichess"
BINARY="$ENGINE_DIR/src/RubiChess"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
case "$HOST" in
  darwin-arm64|linux-aarch64) MAKE_ARGS="ARCH=native" ;;
  darwin-x86_64|linux-x86_64) MAKE_ARGS="ARCH=avx2" ;;
  *)                          MAKE_ARGS="" ;;
esac

log "building (host=$HOST $MAKE_ARGS)"
(
  cd "$ENGINE_DIR/src"
  make clean >/dev/null 2>&1 || true
  # shellcheck disable=SC2086
  make $MAKE_ARGS -j
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi

log "done. launch: $BINARY"

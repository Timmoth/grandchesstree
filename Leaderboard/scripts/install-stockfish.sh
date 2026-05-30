#!/usr/bin/env bash
# Clone, build, and verify Stockfish → bin/stockfish/stockfish.
# Idempotent: existing checkouts are reused; the build is always re-run.

ENGINE="stockfish"
REPO="https://github.com/official-stockfish/Stockfish"
ENGINE_DIR="bin/stockfish"
BINARY="$ENGINE_DIR/stockfish"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
case "$HOST" in
  darwin-arm64)  ARCH_FLAG="ARCH=apple-silicon" ;;
  darwin-x86_64) ARCH_FLAG="ARCH=x86-64-avx2" ;;
  linux-x86_64)  ARCH_FLAG="ARCH=x86-64-avx2" ;;
  linux-aarch64) ARCH_FLAG="ARCH=armv8" ;;
  *)             ARCH_FLAG=""; log "unknown host $HOST — letting Makefile auto-detect" ;;
esac

log "building (host=$HOST $ARCH_FLAG)"
(
  cd "$ENGINE_DIR/src"
  make clean >/dev/null 2>&1 || true
  # shellcheck disable=SC2086
  make build $ARCH_FLAG -j
  # Move the produced binary up one level so the descriptor's
  # "bin/stockfish/stockfish" path resolves.
  cp -f stockfish ../stockfish
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi

log "done. launch: $BINARY"

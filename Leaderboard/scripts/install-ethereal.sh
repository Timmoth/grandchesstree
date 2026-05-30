#!/usr/bin/env bash
# Clone, build, and verify Ethereal → bin/ethereal/src/Ethereal.
# Idempotent: existing checkouts are reused; the build is always re-run.

ENGINE="ethereal"
REPO="https://github.com/AndyGrant/Ethereal"
ENGINE_DIR="bin/ethereal"
BINARY="$ENGINE_DIR/src/Ethereal"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
# Ethereal's Makefile auto-detects most archs; only override when needed.
# On Mac arm64 the default build may try PEXT — POPCNT=0 and PEXT=0 force the
# portable path. On Linux x86_64 the default native build is fine.
case "$HOST" in
  darwin-arm64)  EXTRA="POPCNT=0 PEXT=0" ;;
  *)             EXTRA="" ;;
esac

# Ethereal's default `make` target is `pgo`, which calls llvm-profdata to
# merge profile data into the second build pass. If llvm isn't installed
# (`apt install llvm` on Debian/Ubuntu), the build fails halfway through
# with a confusing "command not found" error. Detect that case up front
# and fall back to the no-PGO `basic` target with a clear warning.
TARGET=""
if ! command -v llvm-profdata >/dev/null 2>&1; then
  log "WARNING: llvm-profdata not found — falling back to non-PGO 'basic' build"
  log "         install 'llvm' (apt) / 'llvm-tools' for the faster PGO build"
  TARGET="basic"
fi

log "building (host=$HOST $EXTRA${TARGET:+ target=$TARGET})"
(
  cd "$ENGINE_DIR/src"
  make clean >/dev/null 2>&1 || true
  # shellcheck disable=SC2086
  make $EXTRA $TARGET -j
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi

log "done. launch: $BINARY"

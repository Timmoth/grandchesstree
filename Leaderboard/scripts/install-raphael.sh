#!/usr/bin/env bash
# Clone, build, and verify Raphael → bin/raphael/raphael.
#
# Raphael (Orbital-Web) is a UCI engine. Its `make uci` target lands a binary
# named `uci` in the repo root, so we symlink it to `bin/raphael/raphael` for
# a consistent launch path.
#
# Build needs:
#   - g++ + make
#   - Internet at build time: Makefile downloads the NNUE network listed in
#     network.txt before linking.
#
# PEXT: the Makefile sets `-DCHESS_USE_PEXT` only when ARCH is one of
# `avx512`, `avx512_vnni`, or `avx2_bmi2`. We default to `avx2_bmi2` (works
# on Haswell+ Intel and Zen2+ AMD); override with `RAPHAEL_ARCH=avx512_vnni`
# on Icelake+ / Zen4 for a small extra bump.

ENGINE="raphael"
REPO="https://github.com/Orbital-Web/Raphael"
ENGINE_DIR="bin/raphael"
BINARY="$ENGINE_DIR/raphael"
ARCH="${RAPHAEL_ARCH:-avx2_bmi2}"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

# --- Preflight -------------------------------------------------------------
command -v g++ >/dev/null 2>&1 || die "g++ not found (Raphael's Makefile defaults to g++)"
command -v make >/dev/null 2>&1 || die "make not found"
command -v curl >/dev/null 2>&1 || die "curl not found (Makefile downloads NNUE network)"

# --- Clone -----------------------------------------------------------------
clone_or_keep "$ENGINE_DIR" "$REPO"

# --- Build -----------------------------------------------------------------
HOST=$(detect_host)
log "building (host=$HOST, ARCH=$ARCH, target=uci)"
(
  cd "$ENGINE_DIR"
  make -j uci ARCH="$ARCH"
)

# The Makefile lands the binary as `./uci` in the repo root; symlink for
# a stable launch path that matches engines/raphael.json.
if [ -x "$ENGINE_DIR/uci" ]; then
  ln -sf uci "$BINARY"
fi
[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi
commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "raphael commit: $commit"

log "done. launch: $BINARY"

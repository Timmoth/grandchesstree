#!/usr/bin/env bash
# Clone, build, and verify Quanticade → bin/quanticade/Quanticade.
#
# Quanticade is a UCI engine in C with NNUE eval. Build needs:
#   - gcc (default) or clang
#   - Internet at build time: Makefile curls the matching .nnue from
#     github.com/Quanticade/Networks (raw URL).
#   - x86_64 (default `native` target uses BMI/BMI2/AVX2 when available).

ENGINE="quanticade"
REPO="https://github.com/Quanticade/Quanticade"
ENGINE_DIR="bin/quanticade"
BINARY="$ENGINE_DIR/Quanticade"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

# --- Preflight -------------------------------------------------------------
if ! command -v gcc >/dev/null 2>&1 && ! command -v clang >/dev/null 2>&1; then
  die "neither gcc nor clang found"
fi
command -v make >/dev/null 2>&1 || die "make not found"
# curl is needed by the Makefile to fetch the NNUE network.
command -v curl >/dev/null 2>&1 || command -v wget >/dev/null 2>&1 \
  || die "neither curl nor wget found (Makefile needs one to download the NNUE network)"

# --- Clone + build ---------------------------------------------------------
clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
# Quanticade's Makefile default target is `all`, which builds with
# `-march=native` baked into CFLAGS. There is no `native` target.
log "building (host=$HOST, make all)"
(
  cd "$ENGINE_DIR"
  make
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi
commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "quanticade commit: $commit"

log "done. launch: $BINARY"

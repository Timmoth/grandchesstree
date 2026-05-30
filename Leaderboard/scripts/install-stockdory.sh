#!/usr/bin/env bash
# Clone, build, and verify StockDory → bin/stockdory/Build/StockDory.
# StockDory's README mandates a fairly recent toolchain:
#   - CMake >= 3.21
#   - Clang (LLVM) >= 20.0.0
#   - Ninja >= 1.10.2
# This script does a hard preflight on those — failing fast with a clear
# message is far less painful than a 200-line cmake spew.

ENGINE="stockdory"
REPO="https://github.com/TheBlackPlague/StockDory"
ENGINE_DIR="bin/stockdory"
BINARY="$ENGINE_DIR/Build/StockDory"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

# --- Toolchain preflight ----------------------------------------------------
command -v cmake >/dev/null 2>&1 || die "cmake not found (need >= 3.21)"
command -v ninja >/dev/null 2>&1 || die "ninja not found (install via apt/brew)"

# Find a clang >= 20. Ubuntu 24.04's apt clang is 18; clang-20 from
# apt.llvm.org installs as `clang-20`/`clang++-20` (versioned names) and
# not as bare `clang` unless update-alternatives was set up. Accept both.
_clang_major() {
  command -v "$1" >/dev/null 2>&1 || { echo ""; return; }
  "$1" --version 2>/dev/null | head -1 | sed -nE 's/.*version ([0-9]+).*/\1/p'
}
CLANG=""
v=$(_clang_major clang)
if [ -n "$v" ] && [ "$v" -ge 20 ]; then
  CLANG=clang
fi
if [ -z "$CLANG" ]; then
  for cand in clang-25 clang-24 clang-23 clang-22 clang-21 clang-20; do
    if command -v "$cand" >/dev/null 2>&1; then
      CLANG="$cand"
      break
    fi
  done
fi
[ -n "$CLANG" ] || die "clang >= 20 not found; install via apt.llvm.org (see https://apt.llvm.org)"
CXX_BIN="${CLANG/clang/clang++}"
log "using compiler: $CLANG (and $CXX_BIN)"

# --- Clone + build ----------------------------------------------------------
clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
log "configuring (host=$HOST, cmake + ninja, $CLANG)"
(
  cd "$ENGINE_DIR"
  rm -rf Build
  cmake -B Build -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_C_COMPILER="$CLANG" \
        -DCMAKE_CXX_COMPILER="$CXX_BIN" \
        -G Ninja
  cmake --build Build --config Release
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi

# StockDory doesn't tag releases the way most engines do — pin to git commit
# for reproducibility.
commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "stockdory commit: $commit"

log "done. launch: $BINARY"

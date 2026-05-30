#!/usr/bin/env bash
# Clone, build, and verify chessbit → bin/chessbit/chessbit.
#
# chessbit (Thomas Huijbregts) ships only a Visual Studio `.sln` and a
# README documenting an Intel-compiler invocation — no Makefile or
# CMakeLists. We compile all .cpp files in the chessbit/chessbit/
# subdirectory directly with g++/clang++ + the same flags the README
# notes (C++20, BMI2, AVX2, -O3).

ENGINE="chessbit"
REPO="https://github.com/thuijbregts/chessbit"
ENGINE_DIR="bin/chessbit"
SRC_DIR="$ENGINE_DIR/chessbit"   # source files live in repo/chessbit/
# Note: we leave the binary inside SRC_DIR rather than lifting one level
# up — `bin/chessbit/chessbit` is already the source dir, so the binary
# would collide. The wrapper resolves this exact nested path.
BINARY="$SRC_DIR/chessbit"
WRAPPER="wrappers/chessbit/run.sh"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

# --- Preflight -------------------------------------------------------------
HOST=$(detect_host)
case "$HOST" in
  *-x86_64) ;;
  *) die "chessbit uses BMI/BMI2/AVX2 — x86 only. Detected: $HOST" ;;
esac

if ! command -v g++ >/dev/null 2>&1; then
  die "g++ not found (need >= 11 for C++20)"
fi
gcc_major=$(g++ -dumpversion 2>/dev/null | cut -d. -f1)
if [ -n "$gcc_major" ] && [ "$gcc_major" -lt 11 ]; then
  log "WARNING: g++ $gcc_major may be too old for chessbit's C++20 usage (recommend >= 11)"
fi

# --- Clone + build ---------------------------------------------------------
clone_or_keep "$ENGINE_DIR" "$REPO"

[ -f "$WRAPPER" ] || die "wrapper missing at $WRAPPER"
chmod +x "$WRAPPER" 2>/dev/null || true

[ -d "$SRC_DIR" ] || die "expected source dir $SRC_DIR — repo layout may have changed"

# chessbit uses MSVC names (__forceinline, __popcnt64) unconditionally.
# Generate a compat shim and force-include it.
mkdir -p "$ENGINE_DIR/shim"
cat > "$ENGINE_DIR/shim/chessbit_compat.h" << 'EOF'
#pragma once
#ifndef _MSC_VER
// chessbit uses MSVC names; map to gcc/clang-friendly equivalents.
// Plain `inline` (not always_inline) because chessbit marks the
// recursive `enumMoves` as __forceinline and gcc rejects
// always_inline on recursive calls; O3+LTO still inlines aggressively.
#  define __forceinline inline
#  define __popcnt64(x) __builtin_popcountll((unsigned long long)(x))
#endif
EOF

SHIM_PATH="$(cd "$ENGINE_DIR" && pwd)/shim/chessbit_compat.h"
log "building (g++ -std=c++20 -march=native -O3 -mbmi2 -mbmi -flto + shim)"
(
  cd "$SRC_DIR"
  rm -f chessbit
  # shellcheck disable=SC2046
  g++ -std=c++20 -march=native -O3 -mbmi2 -mbmi -flto \
      -fomit-frame-pointer -DNDEBUG \
      -include "$SHIM_PATH" \
      $(ls *.cpp) -o chessbit
)
[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

# Verify via the WRAPPER.
out=$(verify_perft "$WRAPPER") || die "perft test failed (via wrapper)"

commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "chessbit commit: $commit → consider updating engines/$ENGINE.json's \"version\""

log "done. launch: $WRAPPER (binary: $BINARY)"

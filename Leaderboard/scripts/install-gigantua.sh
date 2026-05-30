#!/usr/bin/env bash
# Clone, build, and verify Gigantua → bin/gigantua/gigantua.
#
# Gigantua (Daniel Infuehr) is a perft-only CLI tool. The repo doesn't ship
# a Makefile or CMakeLists — only a Visual Studio sln plus a
# `Compile_linux.bat` documenting the GCC and Clang invocations. We follow
# the GCC pattern, minus the `wsl` prefix that script uses.
#
# Build needs C++20: gcc >= 11 or clang >= 12. No external deps.

ENGINE="gigantua"
REPO="https://github.com/Gigantua/Gigantua"
ENGINE_DIR="bin/gigantua"
SRC_DIR="$ENGINE_DIR/Gigantua"
BINARY="$ENGINE_DIR/gigantua"
WRAPPER="wrappers/gigantua/run.sh"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

# --- Preflight -------------------------------------------------------------
if ! command -v g++ >/dev/null 2>&1; then
  die "g++ not found (need >= 11 for C++20)"
fi
gcc_major=$(g++ -dumpversion 2>/dev/null | cut -d. -f1)
if [ -n "$gcc_major" ] && [ "$gcc_major" -lt 11 ]; then
  log "WARNING: g++ $gcc_major may be too old for Gigantua's C++20 usage (recommend >= 11)"
fi

# --- Clone + build ---------------------------------------------------------
clone_or_keep "$ENGINE_DIR" "$REPO"

[ -f "$WRAPPER" ] || die "wrapper missing at $WRAPPER"
chmod +x "$WRAPPER" 2>/dev/null || true

[ -f "$SRC_DIR/Gigantua.cpp" ] \
  || die "expected $SRC_DIR/Gigantua.cpp — repo layout may have changed"

log "building (g++ -std=c++20 -march=native -O3 -flto)"
(
  cd "$SRC_DIR"
  rm -f gigantua
  g++ -std=c++20 -march=native -O3 -flto \
      -fomit-frame-pointer -foptimize-sibling-calls \
      Gigantua.cpp -o gigantua
)

# Move the binary up out of the inner Gigantua/ subdirectory so the
# descriptor's launch path resolves cleanly.
if [ -x "$SRC_DIR/gigantua" ]; then
  mv -f "$SRC_DIR/gigantua" "$BINARY"
fi
[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

# Verify via the WRAPPER (single-threaded, no flags).
out=$(verify_perft "$WRAPPER") || die "perft test failed (via wrapper)"

commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "gigantua commit: $commit → consider updating engines/$ENGINE.json's \"version\""

log "done. launch: $WRAPPER (binary: $BINARY)"

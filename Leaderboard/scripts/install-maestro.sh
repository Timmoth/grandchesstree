#!/usr/bin/env bash
# Clone, build, and verify Maestro → bin/maestro/maestro.
#
# Maestro (Hin-Yu-Evan-Fung) is a UCI engine in C++. The build is driven
# by CMakeLists.txt at the repo root. Upstream's USER_FLAGS string is
# x86-only (-mavx2 -mssse3 -msse4.1 etc), so on arm64 hosts we patch the
# CMakeLists.txt before configuring to swap in -DUSE_NEON -march=native.
#
# EXECUTABLE_OUTPUT_PATH is set to `../bin` in the CMakeLists. When we
# configure into ./build, that resolves to <repo>/bin/Maestro, which the
# build script then symlinks to bin/maestro/maestro for a stable launch
# path.
#
# The NNUE network (bin/nn-eba324f53044.nnue) is committed to the repo
# and embedded into the binary at build time via incbin — no external
# download needed.

ENGINE="maestro"
REPO="https://github.com/Hin-Yu-Evan-Fung/Maestro-Chess-Engine"
ENGINE_DIR="bin/maestro"
BINARY="$ENGINE_DIR/maestro"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

command -v cmake >/dev/null 2>&1 || die "cmake not found (needed for Maestro's build)"
command -v make >/dev/null 2>&1 || die "make not found"

clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
# Upstream's USER_FLAGS is x86-only; on arm64 we rewrite it to NEON+native
# so the build doesn't error on -mavx2 / -mssse3 / -msse4.1 etc.
case "$HOST" in
  darwin-arm64|linux-aarch64)
    log "patching CMakeLists.txt USER_FLAGS for arm64 (USE_NEON + -march=native)"
    # GNU sed uses `-i` without arg; BSD/macOS sed needs `-i ''`. Using a
    # `.bak` suffix works on both — leaves a backup but doesn't break.
    sed -i.bak \
      's|set(USER_FLAGS .*)|set(USER_FLAGS "-O3 -pthread -DUSE_NEON -march=native")|' \
      "$ENGINE_DIR/CMakeLists.txt"
    ;;
esac

log "building (host=$HOST, cmake -B build && cmake --build build -j)"
(
  cd "$ENGINE_DIR"
  rm -rf build
  cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
  cmake --build build -j
)

# EXECUTABLE_OUTPUT_PATH=../bin (from build/) lands the binary at <ENGINE_DIR>/bin/Maestro.
PRODUCED="$ENGINE_DIR/bin/Maestro"
[ -x "$PRODUCED" ] || die "expected binary at $PRODUCED but it's missing"
ln -sf "bin/Maestro" "$BINARY"
[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi
commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "maestro commit: $commit"

log "done. launch: $BINARY"

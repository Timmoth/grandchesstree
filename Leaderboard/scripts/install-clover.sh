#!/usr/bin/env bash
# Clone, build, and verify Clover → bin/clover/clover.
#
# Clover (lucametehau/CloverEngine) is a UCI engine in C++. The Makefile
# lives in src/, the only `make` target is literally called `make` (yes,
# `make make` works, and so does plain `make` since it's the first concrete
# target). The binary lands in src/ as `Clover.<VERSION>[-<arch-suffix>]`.
#
# build_flag values supported by upstream: old | avx2 | avx512 | native | tune | generate.
# Default is `native` (`-mno-avx512f -march=native`). On Apple Silicon, the
# `-mno-avx512f` flag is a no-op warning but still compiles; native arch
# detection picks up the arm features correctly.
#
# The NNUE file `quantised.nnue` is committed to the repo (in src/) and
# embedded into the binary at build time via -DEVALFILE.

ENGINE="clover"
REPO="https://github.com/lucametehau/CloverEngine"
ENGINE_DIR="bin/clover"
BINARY="$ENGINE_DIR/clover"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

command -v make >/dev/null 2>&1 || die "make not found"

clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
# build_flag=native works for both x86 (mno-avx512f + march=native) and arm
# (Apple Clang ignores -mno-avx512f with a warning, march=native picks up
# the correct arm features).
BUILD_FLAG="native"

log "building (host=$HOST, build_flag=$BUILD_FLAG, in src/)"
(
  cd "$ENGINE_DIR/src"
  make clean >/dev/null 2>&1 || rm -f *.o 3rdparty/Fathom/src/*.o
  # The first concrete target in the Makefile is literally named `make`;
  # invoking `make` with no args runs it (because it's the first non-pattern
  # target). Pass build_flag through.
  make build_flag="$BUILD_FLAG"
)

# Resolve the produced binary — name depends on build_flag:
#   native  → Clover.<VERSION>
#   avx2    → Clover.<VERSION>-avx2
#   avx512  → Clover.<VERSION>-avx512
#   old     → Clover.<VERSION>-old
produced=$(find "$ENGINE_DIR/src" -maxdepth 1 -type f -perm -111 -name 'Clover.*' \
            ! -name '*.o' 2>/dev/null | head -1 || true)
if [ -z "$produced" ]; then
  die "couldn't find produced Clover binary under $ENGINE_DIR/src"
fi
log "produced: $produced — symlinking to $BINARY"
ln -sf "$(basename "$produced")" "$ENGINE_DIR/src/clover"
ln -sf "src/clover" "$BINARY"
[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

# Clover prints its banner as "Clover 9.1 by Luca Metehau" (not a UCI id-name
# line), so detect_version sed pattern won't catch it. Pluck the version from
# the banner line directly.
banner=$(printf '%s\n' "$out" | grep -m1 '^Clover ' || true)
if [ -n "$banner" ]; then
  log "banner: $banner"
fi
commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "clover commit: $commit"

log "done. launch: $BINARY"

#!/usr/bin/env bash
# Clone, build, and verify Pawnocchio → bin/pawnocchio/zig-out/bin/pawnocchio.
#
# Pawnocchio (JonathanHallstrom) is a UCI engine in Zig. Build:
#   1. clone with submodules (the NNUE network lives in a submodule)
#   2. zig build --release=fast --prefix zig-out
#
# Zig 0.15.2 is the version called out in the README. Older/newer zigs may
# fail; we surface that as a preflight check.

ENGINE="pawnocchio"
REPO="https://github.com/JonathanHallstrom/pawnocchio"
ENGINE_DIR="bin/pawnocchio"
BINARY="$ENGINE_DIR/zig-out/bin/pawnocchio"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

# --- Preflight -------------------------------------------------------------
command -v zig >/dev/null 2>&1 || die "zig not found (Pawnocchio's README pins Zig 0.15.2)"
zig_ver=$(zig version 2>/dev/null || true)
log "using zig: $zig_ver"

# --- Clone (with submodules) -----------------------------------------------
if [ -d "$ENGINE_DIR/.git" ]; then
  log "$ENGINE_DIR already cloned"
  git -C "$ENGINE_DIR" submodule update --init --depth 1 || true
else
  [ -d "$ENGINE_DIR" ] && rm -rf "$ENGINE_DIR"
  log "cloning $REPO into $ENGINE_DIR (with submodules)"
  git clone --depth 1 --recurse-submodules --shallow-submodules "$REPO" "$ENGINE_DIR"
fi

# --- Build -----------------------------------------------------------------
HOST=$(detect_host)
log "building (host=$HOST, zig build --release=fast)"
(
  cd "$ENGINE_DIR"
  zig build --release=fast --prefix zig-out
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi
commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "pawnocchio commit: $commit"

log "done. launch: $BINARY"

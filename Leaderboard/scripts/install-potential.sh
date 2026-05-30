#!/usr/bin/env bash
# Clone, build, and verify Potential → bin/potential/src/Potential.
#
# Potential (ProgramciDusunur) is a UCI engine in C. Build per the README:
#   cd src && make
# Output lands at src/Potential.
#
# Note: Potential's perft command is the bare `perft N` form (not `go perft`),
# so engines/potential.json's case template reflects that. `verify_perft` in
# _common.sh tries both forms so the install-time smoke test still works.

ENGINE="potential"
REPO="https://github.com/ProgramciDusunur/Potential"
ENGINE_DIR="bin/potential"
BINARY="$ENGINE_DIR/src/Potential"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

# --- Preflight -------------------------------------------------------------
command -v gcc >/dev/null 2>&1 || command -v cc >/dev/null 2>&1 \
  || die "no C compiler (gcc/cc) found"
command -v make >/dev/null 2>&1 || die "make not found"

# --- Clone -----------------------------------------------------------------
clone_or_keep "$ENGINE_DIR" "$REPO"

# --- Build -----------------------------------------------------------------
HOST=$(detect_host)
log "building (host=$HOST, cd src && make)"
(
  cd "$ENGINE_DIR/src"
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
[ -n "$commit" ] && log "potential commit: $commit"

log "done. launch: $BINARY"

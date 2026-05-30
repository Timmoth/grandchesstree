#!/usr/bin/env bash
# Clone, build, and verify Jet → bin/jet/Jet.
#
# Jet (rafid-dev) is a UCI engine in C++. The Makefile uses clang++ by
# default and builds at the repo root as `./Jet`. The NNUE network
# (src/hexadecane_512_v2.net) is committed to the repo and embedded into
# the binary at build time — no separate download step.
#
# Per the perft handler in src/main.cpp, only `perft depth N` is honoured:
# `perft <N>` falls through to a hard-coded depth=6. The default verify_perft
# in _common.sh sends `go perft 4` / `perft 4`, neither of which exercises
# the right depth here, so we use a Jet-specific verify-via-stdin invocation
# below that sends `perft depth 4` explicitly.

ENGINE="jet"
REPO="https://github.com/rafid-dev/jet"
ENGINE_DIR="bin/jet"
BINARY="$ENGINE_DIR/Jet"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

# --- Preflight -------------------------------------------------------------
command -v clang++ >/dev/null 2>&1 || die "clang++ not found (Jet's makefile defaults to CXX=clang++)"
command -v make >/dev/null 2>&1 || die "make not found"

# --- Clone -----------------------------------------------------------------
clone_or_keep "$ENGINE_DIR" "$REPO"

# --- Build -----------------------------------------------------------------
HOST=$(detect_host)
log "building (host=$HOST, make all)"
(
  cd "$ENGINE_DIR"
  make clean >/dev/null 2>&1 || true
  make
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

# --- Verify (Jet-specific) -------------------------------------------------
# Use `perft depth 4`, not the generic `go perft 4` / `perft 4` that the shared
# verify_perft tries — see the Jet-specific note above.
log "verifying via 'perft depth 4'"
out=$( (printf 'uci\nucinewgame\nposition startpos\nperft depth 4\n'; sleep 1; printf 'quit\n') \
       | timeout 15 "$BINARY" 2>&1 || true )
if ! printf '%s\n' "$out" | tr -d ',_' | grep -q '\b197281\b'; then
  log "perft verification FAILED — last 15 lines below"
  printf '%s\n' "$out" | tail -15 >&2
  die "perft test failed"
fi
log "perft verified"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi
commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "jet commit: $commit"

log "done. launch: $BINARY"

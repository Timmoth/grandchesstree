#!/usr/bin/env bash
# Clone, build, and verify stash-bot → bin/stash-bot/src/stash.
#
# Stash (Morgan Houppin) is a UCI engine in C. The repo's README says:
#   - Source is under src/
#   - Plain `make` autodetects arch flags, BUT
#   - `make NATIVE=yes` enables ALL available instruction sets on the host,
#     which matters for movegen speed (the README and upstream maintainer
#     explicitly call this out).
# We always pass NATIVE=yes here since Leaderboard runs the binary on the
# same host that built it.

ENGINE="stash-bot"
REPO="https://github.com/mhouppin/stash-bot"
ENGINE_DIR="bin/stash-bot"
BINARY="$ENGINE_DIR/src/stash"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

# --- Preflight -------------------------------------------------------------
command -v gcc >/dev/null 2>&1 || command -v clang >/dev/null 2>&1 \
  || die "neither gcc nor clang found"
command -v make >/dev/null 2>&1 || die "make not found"

# --- Clone + build ---------------------------------------------------------
clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
log "building (host=$HOST, cd src && make NATIVE=yes)"
(
  cd "$ENGINE_DIR/src"
  make NATIVE=yes
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi
commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "stash-bot commit: $commit"

log "done. launch: $BINARY"

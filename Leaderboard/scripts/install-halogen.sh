#!/usr/bin/env bash
# Clone, build, and verify Halogen → bin/halogen/build/Halogen (symlink to
# the actual binary at bin/halogen/bin/Halogen-pgo).
#
# Halogen uses a Makefile under src/ — the build produces Halogen-pgo (PGO
# build, the default `make` target) under bin/halogen/bin/. We symlink it
# to bin/halogen/build/Halogen so the descriptor's launch path resolves.

ENGINE="halogen"
REPO="https://github.com/KierenP/Halogen"
ENGINE_DIR="bin/halogen"
BINARY="$ENGINE_DIR/build/Halogen"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
log "building (host=$HOST, make PGO)"
(
  cd "$ENGINE_DIR/src"
  make clean >/dev/null 2>&1 || true
  make -j
)

# Halogen's Makefile drops the binary under ../bin/Halogen-pgo relative to
# src/, i.e. $ENGINE_DIR/bin/Halogen-pgo. Symlink it to the descriptor path.
ACTUAL="$ENGINE_DIR/bin/Halogen-pgo"
[ -x "$ACTUAL" ] || die "expected built binary at $ACTUAL but it's missing"

mkdir -p "$ENGINE_DIR/build"
ln -sf "../bin/Halogen-pgo" "$BINARY"
[ -x "$BINARY" ] || die "symlink at $BINARY isn't executable"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi

log "done. launch: $BINARY"

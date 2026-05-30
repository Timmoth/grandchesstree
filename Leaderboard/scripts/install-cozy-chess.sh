#!/usr/bin/env bash
# Build the cozy-chess UCI wrapper → wrappers/cozy-chess/target/release/cozy-perft.
# No clone needed: the wrapper pulls cozy-chess from crates.io as a dependency.

ENGINE="cozy-chess"
WRAPPER_DIR="wrappers/cozy-chess"
BINARY="$WRAPPER_DIR/target/release/cozy-perft"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

[ -d "$WRAPPER_DIR" ] || die "wrapper source missing at $WRAPPER_DIR"
[ -f "$WRAPPER_DIR/Cargo.toml" ] || die "wrapper not initialised — Cargo.toml absent in $WRAPPER_DIR"

log "building wrapper (cargo build --release)"
(cd "$WRAPPER_DIR" && cargo build --release)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

# Resolve the actual cozy-chess crate version that cargo pulled in.
resolved=$(awk '/^name = "cozy-chess"$/{found=1; next} found && /^version = /{print $3; exit}' "$WRAPPER_DIR/Cargo.lock" | tr -d '"')
if [ -n "$resolved" ]; then
  log "cozy-chess crate version: $resolved"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $resolved"
fi

log "done. launch: $BINARY"

#!/usr/bin/env bash
# Build the jordanbray/chess UCI wrapper → wrappers/jordanbray-chess/target/release/jordan-perft.
# No clone needed: the wrapper pulls the `chess` crate from crates.io as a dependency.

ENGINE="jordanbray-chess"
WRAPPER_DIR="wrappers/jordanbray-chess"
BINARY="$WRAPPER_DIR/target/release/jordan-perft"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

[ -d "$WRAPPER_DIR" ] || die "wrapper source missing at $WRAPPER_DIR"
[ -f "$WRAPPER_DIR/Cargo.toml" ] || die "wrapper not initialised — Cargo.toml absent in $WRAPPER_DIR"

log "building wrapper (cargo build --release)"
(cd "$WRAPPER_DIR" && cargo build --release)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

# The crate is just called "chess" on crates.io.
resolved=$(awk '/^name = "chess"$/{found=1; next} found && /^version = /{print $3; exit}' "$WRAPPER_DIR/Cargo.lock" | tr -d '"')
if [ -n "$resolved" ]; then
  log "chess crate (jordanbray) version: $resolved"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $resolved"
fi

log "done. launch: $BINARY"

#!/usr/bin/env bash
# Build the shakmaty UCI wrapper → wrappers/shakmaty/target/release/shakmaty-perft.
# No clone needed: the wrapper pulls shakmaty from crates.io as a dependency.

ENGINE="shakmaty"
WRAPPER_DIR="wrappers/shakmaty"
BINARY="$WRAPPER_DIR/target/release/shakmaty-perft"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

[ -d "$WRAPPER_DIR" ] || die "wrapper source missing at $WRAPPER_DIR"
[ -f "$WRAPPER_DIR/Cargo.toml" ] || die "wrapper not initialised — Cargo.toml absent in $WRAPPER_DIR"

log "building wrapper (cargo build --release)"
(cd "$WRAPPER_DIR" && cargo build --release)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

resolved=$(awk '/^name = "shakmaty"$/{found=1; next} found && /^version = /{print $3; exit}' "$WRAPPER_DIR/Cargo.lock" | tr -d '"')
if [ -n "$resolved" ]; then
  log "shakmaty crate version: $resolved"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $resolved"
fi

log "done. launch: $BINARY"

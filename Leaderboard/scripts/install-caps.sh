#!/usr/bin/env bash
# Clone, build, and verify CAPS (motors) → bin/caps/caps.
#
# CAPS (Chess Alpha-beta Pruning Search) lives in the `motors` Cargo
# workspace by ToTheAnd. The repo's Makefile invokes:
#   cargo rustc --release --package motors --bin motors \
#     --no-default-features --features='caps,unsafe' \
#     -- --emit link=caps
# with RUSTFLAGS='-C target-cpu=native', producing a binary literally
# called `caps` in the working directory. We run `make` inside
# bin/caps/ so the binary lands at bin/caps/caps.
#
# CAPS speaks UCI and UGI. By default it starts in interactive mode;
# the engine descriptor's `setup` sends `uci` as the first line to
# switch into non-interactive mode before perft cases run, so this
# install script's verify_perft step (which also sends `uci`) works
# out of the box.

ENGINE="caps"
REPO="https://github.com/toanth/motors"
ENGINE_DIR="bin/caps"
BINARY="$ENGINE_DIR/caps"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

# --- Preflight -------------------------------------------------------------
command -v cargo >/dev/null 2>&1 || die "cargo not found (need rustup-installed Rust toolchain)"
command -v make >/dev/null 2>&1 || die "make not found"

# --- Clone -----------------------------------------------------------------
clone_or_keep "$ENGINE_DIR" "$REPO"

# --- Build -----------------------------------------------------------------
HOST=$(detect_host)
log "building (host=$HOST, make → cargo rustc with target-cpu=native)"
(
  cd "$ENGINE_DIR"
  make
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

# --- Verify ----------------------------------------------------------------
out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi
commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "motors commit: $commit"

log "done. launch: $BINARY"

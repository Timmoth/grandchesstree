#!/usr/bin/env bash
# Clone, build, and verify Viridithas → bin/viridithas/target/release/viridithas.
#
# Viridithas (cosmobobak) is a UCI engine in Rust. The build requires the
# matching NNUE file (`viridithas.nnue.zst`) to be placed in the source
# root *before* `cargo build`, because the network is embedded into the
# binary via include_bytes!.
#
# Build:
#   1. clone the repo
#   2. download viridithas.nnue.zst from the latest viridithas-networks release
#   3. RUSTFLAGS="-C target-cpu=native" cargo build --release
#
# We skip the `syzygy,bindgen` features that the README mentions — those are
# only needed for Syzygy tablebase support, which Leaderboard's perft corpus
# doesn't exercise, and bindgen drags in a libclang build-dep.

ENGINE="viridithas"
REPO="https://github.com/cosmobobak/viridithas"
ENGINE_DIR="bin/viridithas"
BINARY="$ENGINE_DIR/target/release/viridithas"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

# --- Preflight -------------------------------------------------------------
command -v cargo >/dev/null 2>&1 || die "cargo not found (need rustup-installed Rust toolchain)"
command -v curl >/dev/null 2>&1 || die "curl not found (needed to fetch the NNUE)"

# --- Clone -----------------------------------------------------------------
clone_or_keep "$ENGINE_DIR" "$REPO"

# --- NNUE download ---------------------------------------------------------
# The build embeds viridithas.nnue.zst into the binary; pull the latest from
# the viridithas-networks releases. We use the GitHub API to discover the
# correct download URL (per the README's recommended approach).
NNUE_PATH="$ENGINE_DIR/viridithas.nnue.zst"
if [ ! -f "$NNUE_PATH" ]; then
  log "downloading latest viridithas.nnue.zst"
  nnue_url=$(curl -fsSL "https://api.github.com/repos/cosmobobak/viridithas-networks/releases/latest" \
             | grep -o '"browser_download_url": "[^"]*\.nnue\.zst"' \
             | head -1 \
             | sed 's/^"browser_download_url": "//;s/"$//')
  [ -n "$nnue_url" ] || die "couldn't discover NNUE download URL from viridithas-networks releases"
  curl -fSL -o "$NNUE_PATH" "$nnue_url"
  [ -s "$NNUE_PATH" ] || die "NNUE download produced an empty file"
fi

# --- Build -----------------------------------------------------------------
HOST=$(detect_host)
log "building (host=$HOST, cargo build --release, target-cpu=native)"
(
  cd "$ENGINE_DIR"
  RUSTFLAGS="-C target-cpu=native" cargo build --release
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

# Viridithas processes stdin-EOF as quit before `go perft` finishes draining
# its output, so the default verify_perft (which closes stdin immediately)
# never sees the result. Keep stdin open for ~2s so the perft can complete.
log "verifying via go perft 4 (keep-stdin-open variant)"
out=$( (printf 'uci\nucinewgame\nposition startpos\ngo perft 4\n'; sleep 2; printf 'quit\n') \
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
[ -n "$commit" ] && log "viridithas commit: $commit"

log "done. launch: $BINARY"

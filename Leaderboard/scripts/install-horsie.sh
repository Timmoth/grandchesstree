#!/usr/bin/env bash
# Clone, build, and verify Horsie → bin/horsie/horsie.
# Horsie is a C++ port of Lizard. Its Makefile auto-detects host arch
# (incl. ARM via -DARM) so a plain `make` works on Apple Silicon and Linux.
#
# Caveats:
# - Horsie is NNUE-based; the Makefile has a `download-net` target that
#   fetches the eval file (the URL is in the repo's network.txt). The
#   default `make` target should chain through download-net automatically
#   when no eval is present — but on hosts without curl / network access,
#   the user may need to drop the .nnue file in manually.
# - The default `make` (== `make native`) emits plain `horsie` — not
#   `horsie-native` as the Makefile's RELEASE recipes use a different
#   $(SUFFIX). See `EXE := horsie` near the top of the Makefile.

ENGINE="horsie"
REPO="https://github.com/liamt19/Horsie"
ENGINE_DIR="bin/horsie"
BINARY="$ENGINE_DIR/horsie"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
log "building (host=$HOST, default 'native' target — Makefile autodetects arch)"
(
  cd "$ENGINE_DIR"
  make clean >/dev/null 2>&1 || true
  make
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi

log "done. launch: $BINARY"

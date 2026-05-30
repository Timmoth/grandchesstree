#!/usr/bin/env bash
# Clone, build, and verify Chessnut → bin/chessnut/chessnut.
#
# Chessnut (FireFather) is a UCI engine in C++. The repo's Makefile lives in
# src/ and emits a binary called `chessnut_mingw`, regardless of host. We
# build there and symlink it up to bin/chessnut/chessnut so the descriptor's
# launch path is stable.
#
# Caveats:
# - The Makefile's LFLAGS default to `-static`, which won't link on macOS
#   (Apple doesn't ship a static libSystem). We override LFLAGS to empty on
#   darwin so the link can find the system libs dynamically.
# - The default CC is g++; on macOS we still pass CXX=clang++ explicitly
#   because that's what `g++` resolves to via the Xcode shim anyway.
# - No external network/NNUE — chessnut uses a hand-crafted eval.

ENGINE="chessnut"
REPO="https://github.com/FireFather/chessnut"
ENGINE_DIR="bin/chessnut"
BINARY="$ENGINE_DIR/chessnut"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

command -v make >/dev/null 2>&1 || die "make not found"

clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
case "$HOST" in
  darwin-arm64|darwin-x86_64)
    MAKE_ARGS=(CC=clang++ LFLAGS=)  # drop -static, Apple doesn't allow it
    ;;
  *)
    MAKE_ARGS=()
    ;;
esac

log "building (host=$HOST, in src/, MAKE_ARGS=${MAKE_ARGS[*]:-<none>})"
(
  cd "$ENGINE_DIR/src"
  make clean >/dev/null 2>&1 || true
  make "${MAKE_ARGS[@]}"
)

# The Makefile emits `chessnut_mingw` regardless of host. Symlink to a
# stable name so the engine descriptor doesn't have to know.
if [ -x "$ENGINE_DIR/src/chessnut_mingw" ]; then
  ln -sf src/chessnut_mingw "$BINARY"
fi
[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi
commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "chessnut commit: $commit"

log "done. launch: $BINARY"

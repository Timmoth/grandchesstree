#!/usr/bin/env bash
# Clone, build, and verify Stormphrax → bin/stormphrax/stormphrax.
#
# Stormphrax (Ciekce) is a true UCI engine, no wrapper needed. Build needs:
#   - clang++ (the README explicitly says GCC is not supported and MSVC
#     doesn't work either).
#   - Internet at build time: Makefile curls the matching .nnue from
#     github.com/Ciekce/stormphrax-nets releases.
#   - x86_64 (BMI/AVX assumed in `native` build).

ENGINE="stormphrax"
REPO="https://github.com/Ciekce/Stormphrax"
ENGINE_DIR="bin/stormphrax"
BINARY="$ENGINE_DIR/stormphrax"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

# --- Toolchain preflight ---------------------------------------------------
_clang_major() {
  command -v "$1" >/dev/null 2>&1 || { echo ""; return; }
  "$1" --version 2>/dev/null | head -1 | sed -nE 's/.*version ([0-9]+).*/\1/p'
}
CLANGXX=""
v=$(_clang_major clang++)
if [ -n "$v" ] && [ "$v" -ge 15 ]; then
  CLANGXX=clang++
fi
if [ -z "$CLANGXX" ]; then
  for cand in clang++-25 clang++-24 clang++-23 clang++-22 clang++-21 clang++-20 clang++-19 clang++-18 clang++-17 clang++-16 clang++-15; do
    if command -v "$cand" >/dev/null 2>&1; then
      CLANGXX="$cand"
      break
    fi
  done
fi
[ -n "$CLANGXX" ] || die "clang++ >= 15 not found (Stormphrax requires clang; GCC is explicitly unsupported)"
log "using compiler: $CLANGXX"

command -v make >/dev/null 2>&1 || die "make not found"
command -v curl >/dev/null 2>&1 || die "curl not found (Makefile downloads the NNUE network)"

# --- Clone + build ---------------------------------------------------------
clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
# EXE pins the output name so we don't have to chase a version-suffixed
# binary like `stormphrax-7.0.108`.
log "building (host=$HOST, $CLANGXX, EXE=stormphrax, native)"
(
  cd "$ENGINE_DIR"
  make CXX="$CLANGXX" EXE=stormphrax native
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi
commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "stormphrax commit: $commit"

log "done. launch: $BINARY"

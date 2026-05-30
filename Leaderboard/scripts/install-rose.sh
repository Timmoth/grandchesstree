#!/usr/bin/env bash
# Clone, build, and verify Rose → bin/rose/rose.
#
# Rose (87flowers) is a true UCI engine, no wrapper needed. Build needs:
#   - clang++ >= 19 (for the C++26 `#embed` directive used to inline the
#     NNUE network into the binary).
#   - Internet at build time: the Makefile curls the matching .rosenet
#     from github.com/87flowers/rose-nets releases.
#   - x86_64 with native arch (default ARCH=native).

ENGINE="rose"
REPO="https://github.com/87flowers/Rose"
ENGINE_DIR="bin/rose"
BINARY="$ENGINE_DIR/rose"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

# --- Toolchain preflight ---------------------------------------------------
_clang_major() {
  command -v "$1" >/dev/null 2>&1 || { echo ""; return; }
  "$1" --version 2>/dev/null | head -1 | sed -nE 's/.*version ([0-9]+).*/\1/p'
}
CLANGXX=""
v=$(_clang_major clang++)
if [ -n "$v" ] && [ "$v" -ge 19 ]; then
  CLANGXX=clang++
fi
if [ -z "$CLANGXX" ]; then
  for cand in clang++-25 clang++-24 clang++-23 clang++-22 clang++-21 clang++-20 clang++-19; do
    if command -v "$cand" >/dev/null 2>&1; then
      CLANGXX="$cand"
      break
    fi
  done
fi
[ -n "$CLANGXX" ] || die "clang++ >= 19 not found (Rose needs C++26 \`#embed\`; install via apt.llvm.org)"
log "using compiler: $CLANGXX"

command -v make >/dev/null 2>&1 || die "make not found"
command -v curl >/dev/null 2>&1 || die "curl not found (Makefile downloads the NNUE network)"

# --- Clone + build ---------------------------------------------------------
clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
log "building (host=$HOST, $CLANGXX, ARCH=native)"
(
  cd "$ENGINE_DIR"
  # `make rose` builds the release binary at ./rose. We override CXX so the
  # versioned clang++ (e.g. clang++-20) is used regardless of system default.
  make CXX="$CLANGXX" rose
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

# Rose embeds its version into the UCI banner — pluck it.
version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi

# Also log the cloned commit for reproducibility.
commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "rose commit: $commit"

log "done. launch: $BINARY"

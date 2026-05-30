#!/usr/bin/env bash
# Clone, build, and verify Berserk → bin/berserk/src/berserk.
# Idempotent: existing checkouts are reused; the build is always re-run.
#
# Berserk's Makefile downloads the NNUE network from a private S3 bucket
# that currently returns 403 Forbidden, which makes a fresh build fail.
# We work around it by pre-fetching the network ourselves: S3 first (in
# case it comes back), then a GitHub Releases search as fallback.

ENGINE="berserk"
REPO="https://github.com/jhonnold/berserk"
ENGINE_DIR="bin/berserk"
BINARY="$ENGINE_DIR/src/berserk"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

clone_or_keep "$ENGINE_DIR" "$REPO"

# Pre-fetch the NNUE network if not already present. The Makefile checks
# `test -f $(EVALFILE)` before downloading, so we just need to plant the
# file at the right path.
EVAL_NAME=$(awk -F' *= *' '/^MAIN_NETWORK/ {print $2; exit}' "$ENGINE_DIR/src/makefile" 2>/dev/null || true)
if [ -n "$EVAL_NAME" ]; then
  EVAL_PATH="$ENGINE_DIR/src/$EVAL_NAME"
  if [ ! -s "$EVAL_PATH" ]; then
    log "pre-fetching NNUE network: $EVAL_NAME"
    curl -sfL --max-time 30 -o "$EVAL_PATH" \
      "https://berserk-networks.s3.amazonaws.com/$EVAL_NAME" 2>/dev/null \
      || rm -f "$EVAL_PATH"
    if [ ! -s "$EVAL_PATH" ]; then
      log "S3 unavailable — searching GitHub releases for $EVAL_NAME"
      URL=$(curl -s "https://api.github.com/repos/jhonnold/berserk/releases?per_page=30" \
            | grep -oE "\"browser_download_url\": *\"[^\"]+/$EVAL_NAME\"" \
            | head -1 | sed -E 's/.*"(https[^"]+)"/\1/')
      if [ -n "$URL" ]; then
        log "fetching $URL"
        curl -sfL --max-time 120 -o "$EVAL_PATH" "$URL"
      fi
    fi
    [ -s "$EVAL_PATH" ] || die "failed to fetch NNUE network $EVAL_NAME — try downloading manually into $EVAL_PATH"
  else
    log "NNUE network already present: $EVAL_PATH"
  fi
fi

HOST=$(detect_host)
case "$HOST" in
  darwin-arm64)  ARCH_FLAG="ARCH=apple-silicon" ;;
  darwin-x86_64) ARCH_FLAG="ARCH=x86-64-avx2" ;;
  linux-x86_64)  ARCH_FLAG="ARCH=x86-64-avx2" ;;
  linux-aarch64) ARCH_FLAG="ARCH=armv8" ;;
  *)             ARCH_FLAG=""; log "unknown host $HOST — letting Makefile auto-detect" ;;
esac

log "building (host=$HOST $ARCH_FLAG)"
(
  cd "$ENGINE_DIR/src"
  make clean >/dev/null 2>&1 || true
  # shellcheck disable=SC2086
  make $ARCH_FLAG -j
)

[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

version=$(detect_version "$out")
if [ -n "$version" ]; then
  log "UCI banner: $version"
  log "→ consider setting engines/$ENGINE.json's \"version\" to: $version"
fi

log "done. launch: $BINARY"

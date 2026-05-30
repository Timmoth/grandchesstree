#!/usr/bin/env bash
# Clone, build, and verify Xiphos → bin/xiphos/xiphos.
#
# Xiphos (milostatarevic) is a UCI engine in C. Its Makefile exposes
# three targets, each producing a differently-named binary:
#   make sse        → xiphos-sse        (generic x86, baseline)
#   make bmi2       → xiphos-bmi2       (Haswell+/Zen2+, uses PEXT for slider attacks)
#   make nopopcnt   → xiphos-nopopcnt   (no popcnt asm — works on arm64 too)
#
# Importantly, src/bitboard.h is the only file with x86 inline asm
# (popcntq / bsfq / pextq) and they're all gated by `#ifndef _NOPOPCNT`
# / `#ifdef _BMI2`. So the nopopcnt target compiles cleanly on arm64
# because the CFLAGS don't include any -m flag — only `-O3 -flto -Wall`.
#
# Selection policy:
#   linux-x86_64 / darwin-x86_64  → bmi2     (the fastest portable choice)
#   linux-aarch64 / darwin-arm64  → nopopcnt (only one that builds on arm)

ENGINE="xiphos"
REPO="https://github.com/milostatarevic/xiphos"
ENGINE_DIR="bin/xiphos"
BINARY="$ENGINE_DIR/xiphos"

# shellcheck source=_common.sh
source "$(cd "$(dirname "$0")" && pwd)/_common.sh"

command -v make >/dev/null 2>&1 || die "make not found"
command -v gcc >/dev/null 2>&1 || command -v clang >/dev/null 2>&1 \
  || die "neither gcc nor clang found"

clone_or_keep "$ENGINE_DIR" "$REPO"

HOST=$(detect_host)
case "$HOST" in
  linux-x86_64|darwin-x86_64)  TARGET=bmi2     ;;
  linux-aarch64|darwin-arm64)  TARGET=nopopcnt ;;
  *)                           TARGET=sse      ;;
esac

log "building (host=$HOST, make $TARGET)"
(
  cd "$ENGINE_DIR"
  make clean >/dev/null 2>&1 || true
  make "$TARGET"
)

PRODUCED="$ENGINE_DIR/xiphos-$TARGET"
[ -x "$PRODUCED" ] || die "expected $PRODUCED but it's missing"
ln -sf "xiphos-$TARGET" "$BINARY"
[ -x "$BINARY" ] || die "expected binary at $BINARY but it's missing"

out=$(verify_perft "$BINARY") || die "perft test failed"

# Xiphos prints its banner as a plain header line (`<VERSION> <ARCH> by <AUTHOR>`)
# rather than the UCI id-name form, so detect_version's sed pattern misses it.
# Pluck a banner-looking line directly.
banner=$(printf '%s\n' "$out" | grep -m1 'by Milos' || true)
[ -n "$banner" ] && log "banner: $banner"
commit=$(git -C "$ENGINE_DIR" rev-parse --short HEAD 2>/dev/null || echo "")
[ -n "$commit" ] && log "xiphos commit: $commit (target=$TARGET)"

log "done. launch: $BINARY"

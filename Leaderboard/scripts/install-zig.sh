#!/usr/bin/env bash
# Install Zig 0.15.2 into $HOME/.local/zig/ (no sudo, no system changes).
# Symlinks the zig binary into $HOME/.local/bin/zig so it's on PATH for
# scripts that already source ~/.profile or ~/.bashrc.
#
# Pawnocchio's README pins exactly Zig 0.15.2; older/newer versions break
# the build. We install side-by-side rather than touching any system zig.
#
# Idempotent: re-running is a no-op if the same version is already there.

set -euo pipefail

ZIG_VERSION="0.15.2"
ZIG_SHA256="02aa270f183da276e5b5920b1dac44a63f1a49e55050ebde3aecc9eb82f93239"
ZIG_URL="https://ziglang.org/download/${ZIG_VERSION}/zig-x86_64-linux-${ZIG_VERSION}.tar.xz"

INSTALL_ROOT="$HOME/.local/zig"
INSTALL_DIR="$INSTALL_ROOT/${ZIG_VERSION}"
BIN_DIR="$HOME/.local/bin"
SYMLINK="$BIN_DIR/zig"

log() { printf '[install-zig] %s\n' "$*"; }
die() { log "ERROR: $*"; exit 1; }

# Architecture check — the URL above is x86_64-only.
case "$(uname -s)/$(uname -m)" in
  Linux/x86_64) : ;;
  *)            die "this bootstrap is x86_64-linux only (host is $(uname -s)/$(uname -m))" ;;
esac

# Quick exit if already installed at the expected version.
if [ -x "$INSTALL_DIR/zig" ]; then
  ver=$("$INSTALL_DIR/zig" version 2>/dev/null || true)
  if [ "$ver" = "$ZIG_VERSION" ]; then
    log "$INSTALL_DIR/zig already installed (version $ver)"
  else
    die "$INSTALL_DIR exists but reports version '$ver' (expected $ZIG_VERSION); remove the dir and re-run"
  fi
else
  mkdir -p "$INSTALL_ROOT"
  TMP_TAR="$(mktemp -t zig-XXXXXX.tar.xz)"
  trap 'rm -f "$TMP_TAR"' EXIT

  log "downloading $ZIG_URL"
  curl -fSL --retry 3 -o "$TMP_TAR" "$ZIG_URL"

  log "verifying sha256"
  echo "${ZIG_SHA256}  ${TMP_TAR}" | sha256sum -c -

  log "extracting to $INSTALL_DIR"
  mkdir -p "$INSTALL_DIR"
  # The tarball unpacks into zig-x86_64-linux-0.15.2/; strip that wrapper dir
  # so the zig binary lands directly under $INSTALL_DIR.
  tar --strip-components=1 -xJf "$TMP_TAR" -C "$INSTALL_DIR"

  ver=$("$INSTALL_DIR/zig" version 2>/dev/null || true)
  [ "$ver" = "$ZIG_VERSION" ] || die "post-extract zig reports version '$ver' (expected $ZIG_VERSION)"
  log "extracted; zig version: $ver"
fi

# Symlink into ~/.local/bin so PATH-aware shells pick it up.
mkdir -p "$BIN_DIR"
ln -sfn "$INSTALL_DIR/zig" "$SYMLINK"
log "symlinked $SYMLINK → $INSTALL_DIR/zig"

# Tell the user how to PATH if it isn't already.
case ":$PATH:" in
  *":$BIN_DIR:"*) : ;;
  *) log "NOTE: $BIN_DIR is not on PATH for this shell. Add to your ~/.bashrc or ~/.profile:"
     log "      export PATH=\"\$HOME/.local/bin:\$PATH\"" ;;
esac

log "done. invoke via: $SYMLINK  (or just 'zig' once PATH is set)"

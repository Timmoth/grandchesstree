# Shared helpers for engine install scripts. Source me from
# install-<engine>.sh after setting at least ENGINE, REPO, ENGINE_DIR.
#
# Re-exec safety: I cd into Leaderboard/ as a side effect so per-engine scripts
# can use repo-root-relative paths verbatim.

set -euo pipefail

# Resolve Leaderboard/ regardless of where the user invoked the script from.
_SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$_SCRIPT_DIR/.."

# rustup installs leave cargo on $HOME/.cargo/bin and patch .bashrc/.profile,
# but non-interactive SSH sessions skip those — so cargo-based installs would
# fail with "cargo: not found" out of the box. Source the env explicitly if
# it's there; harmless if not.
[ -f "$HOME/.cargo/env" ] && . "$HOME/.cargo/env"

# Same story for $HOME/.local/bin: pip-installed user tools and our own
# install-zig.sh land here, but non-interactive SSH doesn't run the .profile
# stanza that puts it on PATH. Prepend if the dir exists.
if [ -d "$HOME/.local/bin" ] && [[ ":$PATH:" != *":$HOME/.local/bin:"* ]]; then
  PATH="$HOME/.local/bin:$PATH"
fi

log() { printf '[%s] %s\n' "${ENGINE:-?}" "$*"; }
die() { log "ERROR: $*"; exit 1; }

detect_host() {
  case "$(uname -s)/$(uname -m)" in
    Darwin/arm64)   echo "darwin-arm64" ;;
    Darwin/x86_64)  echo "darwin-x86_64" ;;
    Linux/x86_64)   echo "linux-x86_64" ;;
    Linux/aarch64)  echo "linux-aarch64" ;;
    *)              echo "unknown" ;;
  esac
}

# Clone if absent; keep otherwise. Forcing a re-clone is `rm -rf <dir>` first.
clone_or_keep() {
  local dir="$1" repo="$2"
  if [ -d "$dir/.git" ]; then
    log "$dir already cloned (rm -rf to force a fresh checkout)"
  else
    [ -d "$dir" ] && rm -rf "$dir"
    log "cloning $repo into $dir"
    git clone --depth 1 "$repo" "$dir"
  fi
}

# Run a single perft 4 from startpos and confirm 197281 lands in stdout.
# Echoes the captured stdout (so callers can introspect for the UCI banner).
# Tries `go perft` first, falls back to bare `perft` (Ethereal-style).
# Strips thousands separators before matching so engines like StockDory
# that print "Nodes searched: 197,281" still validate.
verify_perft() {
  local binary="$1"
  for cmd in "go perft 4" "perft 4"; do
    local out
    out=$(printf 'uci\nucinewgame\nposition startpos\n%s\nquit\n' "$cmd" \
          | timeout 15 "$binary" 2>&1 || true)
    if printf '%s\n' "$out" | tr -d ',_' | grep -q '\b197281\b'; then
      log "perft verified via '$cmd'"
      printf '%s\n' "$out"
      return 0
    fi
  done
  log "perft verification FAILED — last 15 lines below"
  printf '%s\n' "$out" | tail -15 >&2
  return 1
}

# Pluck `id name <Engine> <version>` from a UCI banner; print just the version.
detect_version() {
  printf '%s\n' "$1" | sed -n 's/^id name [^ ][^ ]* \(.*\)$/\1/p' | head -1
}

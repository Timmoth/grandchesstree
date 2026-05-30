#!/usr/bin/env bash
#
# Publishes MoveGen.Wasm (AOT + trimmed) and copies the AppBundle into
# Site/dist/wasm/movegen/ for the perft tool to load.
#
# Usage:  ./publish-to-webapp.sh

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
SITE="$(cd "$HERE/../../../.." && pwd)"
DEST="$SITE/dist/wasm/movegen"

echo "Publishing MoveGen.Wasm..."
# Clean previous publish output so stale content-hashed files don't accumulate.
rm -rf "$HERE/bin/Release/publish"
dotnet publish "$HERE" -c Release -o "$HERE/bin/Release/publish"

BUNDLE="$HERE/bin/Release/publish/wwwroot"
if [[ ! -d "$BUNDLE" ]]; then
  # Older SDK layout falls back to AppBundle/
  BUNDLE="$HERE/bin/Release/net10.0/browser-wasm/AppBundle"
fi
if [[ ! -d "$BUNDLE" ]]; then
  echo "Could not locate the published bundle; check the publish output above." >&2
  exit 1
fi

echo "Copying $BUNDLE -> $DEST"
rm -rf "$DEST"
mkdir -p "$DEST"
cp -R "$BUNDLE/." "$DEST/"

echo
echo "Done. Bundle size:"
du -sh "$DEST"
echo "_framework breakdown:"
du -sh "$DEST/_framework"/*.wasm 2>/dev/null | sort -h | tail -10 || true

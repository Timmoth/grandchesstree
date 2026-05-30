#!/bin/sh
# Build self-contained single-file binaries for every supported platform.
# Output: dist/<rid>/perftcheck[.exe]

set -e
cd "$(dirname "$0")/.."

mkdir -p dist
for RID in linux-x64 linux-arm64 osx-x64 osx-arm64 win-x64; do
    echo "=== publishing $RID ==="
    dotnet publish Perft-Checker.csproj \
        -c Release \
        -r "$RID" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:EnableCompressionInSingleFile=true \
        -p:DebugType=embedded \
        -o "dist/$RID"
done

echo
echo "Binaries:"
for RID in linux-x64 linux-arm64 osx-x64 osx-arm64 win-x64; do
    BIN="dist/$RID/perftcheck"
    [ "$RID" = "win-x64" ] && BIN="$BIN.exe"
    if [ -f "$BIN" ]; then
        SIZE=$(du -h "$BIN" | cut -f1)
        echo "  $RID: $BIN  ($SIZE)"
    fi
done

#!/bin/sh
# Wrapper so perftcheck can launch the GCT engine like a regular UCI binary.
# Builds the ARM/x86 DLL if missing, then execs it under `dotnet`.
DIR="$(cd "$(dirname "$0")" && pwd)"
DLL="$DIR/GrandChessTree.Engine/bin/Release/net10.0/GrandChessTree.Engine.dll"
exec dotnet "$DLL" "$@"

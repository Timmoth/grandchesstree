#!/bin/sh
# Wrapper that invokes the MoveGen UCI engine via the dotnet host.
# Used by Perft-Checker during dev so we don't need to codesign the
# self-contained single-file binary on Apple Silicon.
exec dotnet "$(dirname "$0")/MoveGen.App/bin/Release/net10.0/MoveGen.App.dll" "$@"

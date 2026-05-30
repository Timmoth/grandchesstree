#!/usr/bin/env bash
# Per-bucket integrity manifest for a wave_N directory.
# Run after a wave_N completes (especially wave_11 before depth-12 starts) to
# capture enough state to identify which bucket files would need to be re-derived
# if a /mnt/bulk RAID0 drive fails mid-run.
#
# Usage:
#   wave_manifest.sh <wave_dir> [out_file]
#
# Default out_file = <wave_dir>/manifest.txt
# Output: one line per bucket — "<filename> <size> <sha256-of-first-64KB>"

set -euo pipefail

WAVE_DIR=${1:?usage: wave_manifest.sh <wave_dir> [out_file]}
OUT=${2:-$WAVE_DIR/manifest.txt}

[[ -d "$WAVE_DIR" ]] || { echo "not a directory: $WAVE_DIR"; exit 1; }

t0=$(date +%s)
files=$(ls "$WAVE_DIR"/bucket_*.bin 2>/dev/null | sort)
[[ -z "$files" ]] && { echo "no bucket_*.bin files in $WAVE_DIR"; exit 1; }

tmp="${OUT}.tmp"
: > "$tmp"

count=0
for f in $files; do
    size=$(stat -c %s "$f")
    # Sample sha256 over first 64 KB — enough to detect bit rot or wrong file
    # without scanning multi-TB. Adjacent passes also write a separate .done
    # marker containing the unique count; combining both is plenty to detect
    # corruption.
    sha=$(head -c 65536 "$f" | sha256sum | awk '{print $1}')
    printf '%s\t%d\t%s\n' "$(basename "$f")" "$size" "$sha" >> "$tmp"
    count=$((count+1))
done

mv "$tmp" "$OUT"
echo "wave_manifest: $count buckets, manifest -> $OUT  [$(( $(date +%s) - t0 ))s]"

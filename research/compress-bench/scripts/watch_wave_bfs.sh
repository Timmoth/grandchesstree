#!/usr/bin/env bash
# Live system monitor for a run_wave_bfs.sh in-flight.
# Run in a separate terminal alongside the main run.
#
# Usage:  watch_wave_bfs.sh [work_dir] [interval_sec]
#         work_dir defaults to /tmp/wave_bfs, interval defaults to 5 seconds.
#
# Env overrides:
#   DISK_DEV=auto   block device in /proc/diskstats to read IO from. Auto-
#                   detected from the device backing $WORK if unset.
#
# Output (one line per interval):
#   [t=Ns  phase]  cpu=N%  ram=USED/TOTAL GB  disk=USED/AVAIL GB  io=W:N R:N MB/s  data=N GB
#
# Stops cleanly on Ctrl-C.

set -uo pipefail

WORK=${1:-/tmp/wave_bfs}
INTERVAL=${2:-5}

# Auto-detect block device backing $WORK. Falls back to dm-0 then nvme0n1.
auto_disk_dev() {
    local src
    src=$(df --output=source "$WORK" 2>/dev/null | tail -1 | awk '{n=split($1,a,"/"); print a[n]}')
    if [[ -n "$src" && ! -e "/sys/block/$src" ]]; then
        for d in /sys/block/dm-*; do
            [[ -r "$d/dm/name" ]] || continue
            if [[ "$(cat "$d/dm/name")" == "$src" ]]; then
                src="$(basename "$d")"
                break
            fi
        done
    fi
    [[ -n "$src" && -e "/sys/block/$src" ]] && echo "$src" && return
    [[ -e /sys/block/dm-0 ]] && echo "dm-0" && return
    [[ -e /sys/block/nvme0n1 ]] && echo "nvme0n1" && return
    echo ""
}
DISK_DEV=${DISK_DEV:-$(auto_disk_dev)}

if [[ -z "$DISK_DEV" ]]; then
    echo "warn: no block device detected; io columns will show 0" >&2
fi

echo "Watching $WORK every ${INTERVAL}s (disk_dev=${DISK_DEV:-<none>}) — Ctrl-C to stop"
echo

prev_t=0 prev_wr=0 prev_rd=0
start_t=$(date +%s)

while true; do
    now_t=$(date +%s)

    # /proc/diskstats: field 3=name, field 6=sectors read, field 10=sectors written
    if [[ -n "$DISK_DEV" ]]; then
        read -r now_rd now_wr < <(awk -v dev="$DISK_DEV" '$3==dev {print $6, $10; exit}' /proc/diskstats)
        now_rd=${now_rd:-0}; now_wr=${now_wr:-0}
    else
        now_rd=0; now_wr=0
    fi

    wkbps=0; rkbps=0
    if [[ $prev_t -gt 0 && $((now_t - prev_t)) -gt 0 ]]; then
        dt=$((now_t - prev_t))
        wkbps=$(( (now_wr - prev_wr) * 512 / 1048576 / dt ))
        rkbps=$(( (now_rd - prev_rd) * 512 / 1048576 / dt ))
    fi
    prev_t=$now_t; prev_wr=$now_wr; prev_rd=$now_rd

    cpu=$(top -bn1 2>/dev/null | awk '/^%Cpu/ {printf "%.0f", 100-$8; exit}')
    mem_avail_gb=$(awk '/MemAvailable/ {printf "%.0f", $2/1048576; exit}' /proc/meminfo 2>/dev/null)
    mem_total_gb=$(awk '/MemTotal/ {printf "%.0f", $2/1048576; exit}' /proc/meminfo 2>/dev/null)
    mem_used_gb=$(( ${mem_total_gb:-0} - ${mem_avail_gb:-0} ))

    du_info=$(df -BG "$WORK" 2>/dev/null | awk 'NR==2 {gsub("G",""); printf "%s/%s", $3, $4}')

    # Pick newest progress.json under $WORK. Read .phase and the bytes counter.
    pj=$(ls -t "$WORK"/spill_*/progress.json "$WORK"/wave_*/progress.json 2>/dev/null | head -1)
    phase="-"
    data_gb="-"
    if [[ -f "$pj" ]]; then
        phase=$(grep -oE '"phase":"[^"]+"' "$pj" 2>/dev/null | head -1 | cut -d: -f2 | tr -d '"')
        bytes=$(grep -oE '"(spill_bytes|input_bytes_processed)":[0-9]+' "$pj" 2>/dev/null | head -1 | cut -d: -f2)
        [[ -n "$bytes" ]] && data_gb=$(awk -v b="$bytes" 'BEGIN {printf "%.1f", b/1073741824}')
    fi

    elapsed=$((now_t - start_t))
    printf "[t=%5ds  %-12s]  cpu=%3s%%  ram=%2s/%-2s GB  disk=%-9s GB  io=W:%4s R:%4s MB/s  data=%6s GB\n" \
        "$elapsed" "${phase:--}" "${cpu:-?}" "$mem_used_gb" "$mem_total_gb" \
        "${du_info:-?}" "$wkbps" "$rkbps" "$data_gb"

    sleep "$INTERVAL"
done
